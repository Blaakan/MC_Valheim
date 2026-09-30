using System;
using System.Collections.Generic;
using System.Diagnostics;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Combat.WeaponsMovesetMod;

// Me = controller probe (design 2.9). Unity no list a controller's states at runtime, and player's attack states not
// named after their trigger or clip (1.0.16 run: none found by name). So me ask a copy of the controller: hidden
// Animator on own GameObject (no avatar, no renderer, no animation events), stance parameters of the Animator me serve,
// every other parameter calm (on ground, standing). Me step it by hand to the stance's idle, then per trigger: back to
// that idle, fire trigger, step until layer 0 go to a state tagged attack = trigger's state (same rule as learning in
// MoveTracker, without a swing first). One batch = one GameObject made and destroyed in same call: no cost per frame
// after, nothing left in scene, the real Animator never touched. Controller's state machine behaviours run on the copy
// (Unity make one set per Animator): StateController ones me mute first (their enter effects would spawn at the copy),
// never one the real Animator share.
internal static class StateProbe
{
    internal const int LiveStance = -1;

    private const int Layer = 0;
    private const float Step = 0.02f;       // one physics tick, like the live Animator (update mode Fixed)
    private const float SettleStep = 0.1f;  // coarse steps to the idle: only where it ends matter
    private const int SettleSteps = 80;     // 8 s at most to reach the stance's idle
    private const int RestSteps = 20;       // 2 s in one state without a transition = at rest (long idle clip)
    private const int TriggerSteps = 50;    // 1 s at most per trigger (live: in the attack state 0.02-0.2 s after)

    // Far under the world: an effect of a state behaviour me did not mute would spawn where nobody is.
    private static readonly Vector3 Spot = new Vector3(0f, -5000f, 0f);

    private static readonly int StateI = Animator.StringToHash("statei");
    private static readonly int StateF = Animator.StringToHash("statef");
    private static readonly int OnGround = Animator.StringToHash("onGround");

#if DEBUG
    // Self tests read these (last batch).
    internal static int LastUpdates;
    internal static double LastMilliseconds;
    internal static int LastStance;
    internal static int LastMuted;
    internal static int LastShared;
#endif

    // One trigger (RollFlow cache miss, once per trigger and controller). 0 = no attack state found or probe failed.
    internal static int Find(Animator source, string trigger)
    {
        var hashes = new int[1];
        var watch = Stopwatch.StartNew();
        Run(source, new[] { trigger }, hashes, null, LiveStance);
        watch.Stop();
        Log.Debug(hashes[0] != 0
            ? $"Found the animation state of {trigger} in the animation controller ({watch.Elapsed.TotalMilliseconds:0.0} ms)."
            : $"The animation controller has no attack state for {trigger}: a roll attack with it plays after the roll.");
        return hashes[0];
    }

    // triggers[i] -> hashes[i] (layer 0 fullPathHash, 0 = none). clips (Debug, may be null): clips[i] = first layer 0
    // clip of that state. stance: statei/statef value, LiveStance = the source Animator's own (the weapon in hand).
    // Never throw: failure = zeros, one error line.
    internal static void Run(Animator source, IList<string> triggers, int[] hashes, string[] clips, int stance)
    {
        Array.Clear(hashes, 0, hashes.Length);
        if (source == null || triggers == null || triggers.Count == 0)
        {
            return;
        }
        GameObject go = null;
        var updates = 0;
        var watch = Stopwatch.StartNew();
        try
        {
            var controller = source.runtimeAnimatorController;
            if (controller == null)
            {
                return;
            }
            go = new GameObject("MC_MovesetStateProbe") { hideFlags = HideFlags.HideAndDontSave };
            go.transform.position = Spot;
            var probe = go.AddComponent<Animator>();
            probe.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            probe.updateMode = AnimatorUpdateMode.Normal;
            probe.applyRootMotion = false;
            probe.fireEvents = false;
            probe.logWarnings = false;
            probe.runtimeAnimatorController = controller;
            probe.Rebind();
            var theirs = source.GetBehaviours<StateController>();
            Mute(probe, theirs, out _);
            var triggerHashes = SetUp(probe, source, ref stance);
            probe.Update(0f);
            updates++;
            // Again: in case Unity made the behaviours only at the first update.
            var muted = Mute(probe, theirs, out var shared);
            var from = Settle(probe, ref updates);
            for (var i = 0; i < triggers.Count && i < hashes.Length; i++)
            {
                string clip = null;
                hashes[i] = One(probe, from, triggers[i], triggerHashes, clips != null, ref clip, ref updates);
                if (clips != null && i < clips.Length)
                {
                    clips[i] = clip;
                }
            }
#if DEBUG
            LastStance = stance;
            LastMuted = muted;
            LastShared = shared;
#endif
        }
        catch (Exception e)
        {
            Array.Clear(hashes, 0, hashes.Length);
            PatchGuard.Report("StateProbe.Run", e);
        }
        finally
        {
            if (go != null)
            {
                go.SetActive(false); // no automatic update before Destroy at frame end
                Object.Destroy(go);
            }
            watch.Stop();
#if DEBUG
            LastUpdates = updates;
            LastMilliseconds = watch.Elapsed.TotalMilliseconds;
#endif
        }
    }

    // StateController enter effects and child toggles off on the copy's own behaviours. One that is also the served
    // Animator's (a behaviour shared between Animators, not Unity's default) is left alone: muting it would mute the
    // player's too; its effect then spawns far under the world. Count = muted ones; shared = left alone.
    private static int Mute(Animator probe, StateController[] theirs, out int shared)
    {
        shared = 0;
        var behaviours = probe.GetBehaviours<StateController>();
        if (behaviours == null)
        {
            return 0;
        }
        var n = 0;
        foreach (var b in behaviours)
        {
            if (b == null)
            {
                continue;
            }
            if (theirs != null && Array.IndexOf(theirs, b) >= 0)
            {
                shared++;
                continue;
            }
            b.m_enterEffect = new EffectList();
            b.m_enterDisableChildren = false;
            b.m_enterEnableChildren = false;
            n++;
        }
        return n;
    }

    // Calm parameters: every bool false but onGround, numbers 0, triggers reset; stance = asked one or source's.
    // Returns the trigger hashes (reset before each probe: state behaviours may set some).
    private static int[] SetUp(Animator probe, Animator source, ref int stance)
    {
        var parameters = probe.parameters;
        var triggers = new List<int>(parameters.Length);
        var hasStateI = false;
        var hasStateF = false;
        foreach (var p in parameters)
        {
            if (p == null)
            {
                continue;
            }
            var hash = p.nameHash;
            switch (p.type)
            {
                case AnimatorControllerParameterType.Trigger:
                    triggers.Add(hash);
                    probe.ResetTrigger(hash);
                    break;
                case AnimatorControllerParameterType.Bool:
                    probe.SetBool(hash, hash == OnGround);
                    break;
                case AnimatorControllerParameterType.Float:
                    probe.SetFloat(hash, 0f);
                    hasStateF |= hash == StateF;
                    break;
                case AnimatorControllerParameterType.Int:
                    probe.SetInteger(hash, 0);
                    hasStateI |= hash == StateI;
                    break;
            }
        }
        // Same controller = same parameters: source has statei too (no warning from a missing name).
        if (stance < 0)
        {
            stance = hasStateI ? source.GetInteger(StateI) : 0;
        }
        if (hasStateI)
        {
            probe.SetInteger(StateI, stance);
        }
        if (hasStateF)
        {
            probe.SetFloat(StateF, stance);
        }
        return triggers.ToArray();
    }

    // Step until layer 0 rest: one state, no transition, and it played its whole clip once without leaving (a looping
    // idle; a start state with an exit time, like the wake-up, would have left by then) or stayed RestSteps. Its hash
    // (last state seen when it never rest).
    private static int Settle(Animator probe, ref int updates)
    {
        var hash = 0;
        var same = 0;
        for (var i = 0; i < SettleSteps; i++)
        {
            probe.Update(SettleStep);
            updates++;
            if (probe.IsInTransition(Layer))
            {
                same = 0;
                continue;
            }
            var state = probe.GetCurrentAnimatorStateInfo(Layer);
            same = state.fullPathHash == hash ? same + 1 : 1;
            hash = state.fullPathHash;
            if (state.normalizedTime >= 1f || same >= RestSteps)
            {
                break;
            }
        }
        return hash;
    }

    // Back to the idle, triggers clear, fire this one, step: first layer 0 state (next or current) tagged attack that
    // is not the idle. 0 = none within TriggerSteps.
    private static int One(Animator probe, int from, string trigger, int[] triggerHashes, bool wantClip,
        ref string clip, ref int updates)
    {
        if (string.IsNullOrEmpty(trigger))
        {
            return 0;
        }
        if (from != 0)
        {
            probe.Play(from, Layer, 0f);
            probe.Update(0f);
            updates++;
        }
        foreach (var t in triggerHashes)
        {
            probe.ResetTrigger(t);
        }
        probe.SetTrigger(trigger);
        var found = 0;
        for (var i = 0; i < TriggerSteps && found == 0; i++)
        {
            probe.Update(Step);
            updates++;
            RollFlow.NextOrCurrent(probe, out var hash, out var tag);
            if (tag == RollFlow.AttackTag && hash != from)
            {
                found = hash;
                if (wantClip)
                {
                    clip = ClipName(probe);
                }
            }
        }
        probe.ResetTrigger(trigger);
        return found;
    }

    private static string ClipName(Animator probe)
    {
        var infos = probe.IsInTransition(Layer)
            ? probe.GetNextAnimatorClipInfo(Layer)
            : probe.GetCurrentAnimatorClipInfo(Layer);
        return infos.Length > 0 && infos[0].clip != null ? infos[0].clip.name : null;
    }
}
