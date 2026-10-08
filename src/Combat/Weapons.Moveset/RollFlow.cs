using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.WeaponsMovesetMod;

// How roll attack leave the roll (Debug lines, self tests).
internal enum FlowResult : byte
{
    None,        // Animator not in the roll (any more): game's normal transition
    CrossFade,   // me cross-faded Animator out of the roll into the attack state
    TriggerOnly, // no attack state known: game's own trigger decide. Own roll attack: only after the roll (never cut
                 // in without a state), so swing play once roll's blend is over. Other player: trigger wait likewise.
}

// Where attack state hash come from.
internal enum StateSource : byte
{
    None,
    Learned, // state Animator itself entered for this trigger earlier this game session (exact)
    Probed,  // state a copy of the controller went to for this trigger (StateProbe), once per trigger and controller
}

// Me = roll flow (design 2.9; user feedback 2026-09-30: "roll, full reset to idle, then attack"). Vanilla Animator
// finish its 0.2 s blend from dodge to idle before it take the attack trigger. Me no wait: me cross-fade layer 0
// straight from dodge state into the attack state (CrossFadeInFixedTime with its hash) and reset the trigger on that
// Animator (else it play the swing again after). Attack state play from its start during blend: hit time same, only
// look change.
//   Local player: MoveTracker call Cut right after StartAttack (trigger already set). Cut into the roll only with a
//   state known (HasStateFor, before the roll gate): trigger alone mid-roll would snap facing and bend the roll.
//   Other players: vanilla SetTrigger RPC (OnRemoteTrigger), no own RPC. Peers without me see vanilla order.
//   Attack state: learned (exact) > probed (copy of the controller, StateProbe: so the FIRST roll attack of a session
//   flow too) > none (trigger alone). State names no help: 1.0.16 run found none named after its trigger or clip.
// Nothing saved: dictionaries for the game session.
internal static class RollFlow
{
    internal static readonly int DodgeTag = ZSyncAnimation.GetHash("dodge");
    internal static readonly int AttackTag = ZSyncAnimation.GetHash("attack");

    // Every melee move play on layer 0 (runtime NOTE 1.0.16: "layer 1 in none").
    private const int Layer = 0;

    // Probe caches kept for this many controllers at most (player's own, a controller-swapping mod's few).
    private const int MaxControllers = 8;

    // Trigger -> layer 0 fullPathHash Animator went into for it (MoveTracker learn it).
    private static readonly Dictionary<string, int> Learned = new Dictionary<string, int>(StringComparer.Ordinal);

    // Controller instance id -> (trigger -> probed hash, 0 = probe found none). Last table looked up kept by reference:
    // hot path (CanFlow in the cut window) = one reference compare and one lookup.
    private static readonly Dictionary<int, Dictionary<string, int>> ProbedBy =
        new Dictionary<int, Dictionary<string, int>>();
    private static RuntimeAnimatorController _probedController;
    private static Dictionary<string, int> _probed;

#if DEBUG
    // Clip each trigger play on layer 0 (moveset.jump / moveset.roll NOTEs, Valheim 1.0.16). Self tests only: probed
    // state must play this clip (proves probe = live Animator).
    private static readonly Dictionary<string, string> Clips = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        { "swing_longsword1", "Attack2" },
        { "swing_longsword2", "Attack3" },
        { "swing_axe1", "Axe combo 2" },
        { "swing_axe2", "Axe combo 3" },
        { "battleaxe_attack2", "BattleAxe_Combo3" },
        { "dualaxes1", "DualAxes Attack 2 2" },
        { "dualaxes3", "DualAxes Attack 4" },
        { "greatsword1", "Greatsword BaseAttack (2)" },
        { "greatsword2", "Greatsword BaseAttack (3)" },
        { "atgeir_attack1", "2Hand-Spear-Attack9" },
        { "atgeir_attack2", "2Hand-Spear-Attack3" },
        { "knife_stab1", "knife_slash1" },
        { "knife_stab2", "knife_slash2" },
        { "dual_knives1", "Knife Attack Combo (2)" },
        { "dual_knives2", "Knife Attack Combo (3)" },
        { "unarmed_attack1", "Punchstep 2" },
    };

    // Self tests: force trigger alone (watchdog), or learned states only (learning test).
    internal static bool TestNoState;
    internal static bool TestNoProbe;
    internal static int CrossFades;
    internal static int RemoteCuts;
    internal static StateSource LastSource;
    internal static int LastHash;
    internal static float LastBlend = -1f; // seconds given to the last cross-fade

    internal static string ClipOf(string trigger) =>
        trigger != null && Clips.TryGetValue(trigger, out var clip) ? clip : null;

    internal static IEnumerable<string> TableTriggers => Clips.Keys;

    internal static int LearnedHash(string trigger) =>
        trigger != null && Learned.TryGetValue(trigger, out var hash) ? hash : 0;

    internal static void ForgetLearned() => Learned.Clear();

    // Next roll attacks like the first of a game session: nothing learned, nothing probed.
    internal static void ForgetAll()
    {
        Learned.Clear();
        ForgetProbed();
    }

    // Fresh probe, no cache (self tests compare it with the live Animator). stance: StateProbe.LiveStance = animator's.
    internal static void ProbeNow(Animator animator, IList<string> triggers, int stance, int[] hashes, string[] clips) =>
        StateProbe.Run(animator, triggers, hashes, clips, stance);
#endif

    // Activation / deactivation: forget states (Animator facts, found again cheap).
    internal static void Reset()
    {
        Learned.Clear();
        ForgetProbed();
    }

    private static void ForgetProbed()
    {
        ProbedBy.Clear();
        _probedController = null;
        _probed = null;
    }

    // Learned state still in this Animator's controller. Other mod swapped controller = not known: learn again (Learn
    // overwrite the old hash).
    internal static bool Knows(Animator animator, string trigger) =>
        trigger != null && animator != null && Learned.TryGetValue(trigger, out var hash) && hash != 0
        && animator.HasState(Layer, hash);

    // Attack state known for this trigger on this Animator (learned or probed)? No Debug counter touched; first ask
    // for a trigger run the controller probe and fill its cache like Cut would.
    internal static bool HasStateFor(Animator animator, string trigger) =>
        animator != null && !string.IsNullOrEmpty(trigger) && StateFor(animator, trigger, out _) != 0;

    internal static void Learn(string trigger, int hash)
    {
        if (string.IsNullOrEmpty(trigger) || hash == 0)
        {
            return;
        }
        Learned[trigger] = hash;
        Log.Debug($"Learned the animation state of {trigger}.");
    }

    // Layer 0 in the roll: current state tag dodge (also while it blend out), or it blend into a dodge state.
    internal static bool InRoll(Animator animator)
    {
        if (animator.GetCurrentAnimatorStateInfo(Layer).tagHash == DodgeTag)
        {
            return true;
        }
        return animator.IsInTransition(Layer) && animator.GetNextAnimatorStateInfo(Layer).tagHash == DodgeTag;
    }

    // Layer 0 state Animator is in, or go to during a transition.
    internal static void NextOrCurrent(Animator animator, out int hash, out int tag)
    {
        if (animator.IsInTransition(Layer))
        {
            var next = animator.GetNextAnimatorStateInfo(Layer);
            hash = next.fullPathHash;
            tag = next.tagHash;
            return;
        }
        var current = animator.GetCurrentAnimatorStateInfo(Layer);
        hash = current.fullPathHash;
        tag = current.tagHash;
    }

    // Trigger just set on this Animator. In the roll + state known = cross-fade into it, trigger reset here (only this
    // Animator; other peers do their own). State unknown = trigger alone. Not in roll = normal transition.
    internal static FlowResult Cut(Animator animator, string trigger, float blend, out StateSource source)
    {
        source = StateSource.None;
        if (animator == null || string.IsNullOrEmpty(trigger) || !InRoll(animator))
        {
            return FlowResult.None;
        }
        var hash = StateFor(animator, trigger, out source);
        if (hash == 0)
        {
            return FlowResult.TriggerOnly;
        }
        animator.CrossFadeInFixedTime(hash, blend > 0f ? blend : 0f, Layer, 0f);
        animator.ResetTrigger(trigger);
#if DEBUG
        CrossFades++;
        LastSource = source;
        LastHash = hash;
        LastBlend = blend > 0f ? blend : 0f;
#endif
        return FlowResult.CrossFade;
    }

    // ZSyncAnimation.RPC_SetTrigger postfix: every trigger of every character in range. Other player's roll attack
    // trigger while their Animator still in the roll = same cut there. Cheap filters first (rules flag, 12 compares).
    internal static void OnRemoteTrigger(ZSyncAnimation zanim, string name)
    {
        if (zanim == null || string.IsNullOrEmpty(name))
        {
            return;
        }
        var rules = ServerRules.Current;
        if (rules == null || !rules.RollAttack || !IsRollTrigger(rules, name))
        {
            return;
        }
        var local = Player.m_localPlayer;
        if (local != null && ReferenceEquals(zanim, local.m_zanim))
        {
            return; // own roll attack: MoveTracker cut it (it know whether it is a move)
        }
        var animator = zanim.m_animator;
        if (animator == null || !InRoll(animator))
        {
            return;
        }
        var player = zanim.GetComponent<Player>();
        if (player == null)
        {
            return; // monsters no roll
        }
        if (ForOther(animator, name, rules, out var source) == FlowResult.CrossFade)
        {
#if DEBUG
            RemoteCuts++;
#endif
            Log.Debug($"Cross-faded {player.GetPlayerName()}'s roll attack {name} out of the roll ({SourceText(source)}).");
        }
    }

    // Other player's part after its player filters (self test call it on the local Animator).
    internal static FlowResult ForOther(Animator animator, string name, MoveRules rules, out StateSource source)
    {
        source = StateSource.None;
        if (animator == null || rules == null || !rules.RollAttack || !IsRollTrigger(rules, name))
        {
            return FlowResult.None;
        }
        return Cut(animator, name, rules.FlowBlend, out source);
    }

    // Debug line part: how roll attack leave the roll.
    internal static string HowText(FlowResult flow, StateSource source)
    {
        switch (flow)
        {
            case FlowResult.CrossFade:
                return "cross-fade from the roll, " + SourceText(source);
            case FlowResult.TriggerOnly:
                return "no attack state known yet: the game's own transition";
            default:
                return "normal transition";
        }
    }

    private static string SourceText(StateSource source) =>
        source == StateSource.Learned ? "learned state"
        : source == StateSource.Probed ? "state from the animation controller" : "no state";

    private static bool IsRollTrigger(MoveRules rules, string name)
    {
        var triggers = rules.RollTriggers;
        for (var i = 0; i < triggers.Length; i++)
        {
            if (string.Equals(triggers[i], name, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    // Learned (checked with HasState: other mod may swap controller) > probed (once per trigger and controller) > 0.
    private static int StateFor(Animator animator, string trigger, out StateSource source)
    {
        source = StateSource.None;
#if DEBUG
        if (TestNoState)
        {
            return 0;
        }
#endif
        if (Learned.TryGetValue(trigger, out var hash) && hash != 0 && animator.HasState(Layer, hash))
        {
            source = StateSource.Learned;
            return hash;
        }
#if DEBUG
        if (TestNoProbe)
        {
            return 0;
        }
#endif
        hash = Probed(animator, trigger);
        if (hash != 0)
        {
            source = StateSource.Probed;
        }
        return hash;
    }

    // Probe cache of this Animator's controller; a miss run the probe (a few ms, once per trigger and controller).
    private static int Probed(Animator animator, string trigger)
    {
        var controller = animator.runtimeAnimatorController;
        if (controller == null)
        {
            return 0;
        }
        if (!ReferenceEquals(controller, _probedController) || _probed == null)
        {
            var id = controller.GetInstanceID();
            if (!ProbedBy.TryGetValue(id, out var table))
            {
                if (ProbedBy.Count >= MaxControllers)
                {
                    ProbedBy.Clear();
                }
                table = new Dictionary<string, int>(StringComparer.Ordinal);
                ProbedBy[id] = table;
            }
            _probedController = controller;
            _probed = table;
        }
        if (!_probed.TryGetValue(trigger, out var hash))
        {
            hash = StateProbe.Find(animator, trigger);
            _probed[trigger] = hash;
        }
        return hash;
    }
}
