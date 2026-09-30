using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.CreaturesMoraleMod;

// Me = one frame of running away (design 2.7 rout frames, 2.5 piece 2 fear frames), from the MonsterAI.UpdateAI prefix
// on the owner. Base update through a NON-virtual call (regeneration, timers, other mods' BaseAI.UpdateAI patches still
// run), no target, alerted (it runs), vanilla Flee from the point, vanilla MonsterAI update skipped for that frame only
// (its movement would fight ours for the one path cache, design 1.4). Rout and fear share this frame.
// Alert = the game's own alerted state: owner write ZDO "alert", every game read it, vanilla plate show red icon. No
// label of mine. Raised once per run (SetAlerted only when not alerted: alert sound once), taken back by Calm (or kept
// for a fight that start right at the run's end).
internal static class FleeFrames
{
    private static Func<BaseAI, float, bool> _baseUpdate;

    // Prefix body. Return = prefix return (false = vanilla skipped this frame).
    // Exception before the base call: true (vanilla run). After it: false with the base result (base never run twice
    // in one frame). Either way creature marked FramesBroken (vanilla from next tick until unloaded, no retry per frame).
    internal static bool Run(MonsterAI ai, CreatureState state, float dt, Vector3 from, ref bool result)
    {
        var baseCalled = false;
        try
        {
            if (state.FirstFleeFrame)
            {
                state.FirstFleeFrame = false;
                ResetFleeTimers(ai);
            }
            var baseUpdate = BaseUpdate();
            baseCalled = true;
            result = baseUpdate(ai, dt);
            if (!result)
            {
                return false; // not owner or invalid: vanilla would return here too
            }
            ai.m_targetCreature = null;
            ai.m_targetStatic = null;
            ai.SetTargetInfo(ZDOID.None);
            ai.ChargeStop();
            if (!ai.IsAlerted())
            {
                ai.SetAlerted(true); // it runs (alert sound once, red icon for everyone)
            }
            state.FleeAlert = true;
            ai.Flee(dt, from);
            return false;
        }
        catch (Exception e)
        {
            PatchGuard.Report("FleeFrames.Run", e);
            state.FramesBroken = true;
            return !baseCalled;
        }
    }

    // Run over (fear calmed, rout ended), owner, once-per-second check: vanilla unalerted look again (silent). Only an
    // alert my frames held (FleeAlert). A creature target, a live provocation (anybody's; it fight at next search) or a
    // hunt now hold the alert = theirs, me let go. A building target no hold it: vanilla never alert for a building and
    // hit buildings unalerted (else my run's "!" stay up to 30 s). A player it would fight close and sensed (FearRange,
    // same rule as fear, decision 29): it fight that one, alert kept (no second alert sound, no "!" -> aware -> "!"),
    // vanilla pick them soon. Hurt within 5 s (burn tick) = keep, try at next check (each raise replay the sound).
    // Cheap: runs once per check, work (and the sight check) only after a run.
    internal static void Calm(MonsterAI ai, CreatureState state)
    {
        if (!state.FleeAlert)
        {
            return;
        }
        var nview = ai.m_nview;
        if (!ai.IsAlerted() || nview == null || !nview.IsValid())
        {
            state.FleeAlert = false; // vanilla took it back already (sleep, give-up)
            return;
        }
        if (ai.m_targetCreature != null || ai.HuntPlayer() || CreatureKeys.IsProvoked(nview.GetZDO(), Attitudes.Now()))
        {
            state.FleeAlert = false;
            return;
        }
        var range = ServerRules.Current.FearRange;
        if (Fear.SensesFought(ai, ai.transform.position, range * range))
        {
            // Rout over (target timer froze in rout frames) or fear over: vanilla search now, alert stay for the fight.
            state.FleeAlert = false;
            ai.m_updateTargetTimer = Mathf.Min(ai.m_updateTargetTimer, 0.5f);
            return;
        }
        if (ai.m_timeSinceHurt < CreatureCheck.CalmAfterHurt)
        {
            return;
        }
        state.FleeAlert = false;
        ai.SetAlerted(false);
    }

    // First Flee pick a fresh flee point and path at once (else it walk its old chase path 1 s more, design 1.4).
    internal static void ResetFleeTimers(MonsterAI ai)
    {
        ai.m_fleeTargetUpdateTime = -999f;
        ai.m_lastFindPathTime = -999f;
    }

    // BaseAI.UpdateAI without the virtual dispatch (MonsterAI override skipped). Made once. Harmony patches on the base
    // method still run: they live in its body.
    private static Func<BaseAI, float, bool> BaseUpdate()
    {
        if (_baseUpdate == null)
        {
            _baseUpdate = AccessTools.MethodDelegate<Func<BaseAI, float, bool>>(
                AccessTools.Method(typeof(BaseAI), nameof(BaseAI.UpdateAI)), null, false);
        }
        return _baseUpdate;
    }
}
