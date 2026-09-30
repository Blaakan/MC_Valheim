using UnityEngine;

namespace MC.Combat.CreaturesMoraleMod;

// Me = the fear (design 2.5 piece 2), on the creature's owner. An afraid creature never attack the player it fear, and
// it is not passive either: player within FearRange that it SENSE right now (vanilla CanSeeTarget or CanHearTarget,
// not CanSenseTarget which say false for afraid) = it run from them (fear frames: FleeFrames from the player's place)
// until they are Margin m beyond the range or unsensed for Memory s, then it calm (no target, un-alerted). Running =
// vanilla alerted state (red icon on every player's plate, from ZDO "alert"); calm = vanilla unalerted look again.
// Sneaking unseen and silent player can close in. Fighting beat fear (decision 29): its target is, or it sense within
// FearRange, a player it is hostile to or provoked by = no fear (it fight that one; each player judged alone).
// Check once per second (CreatureCheck); frames every tick while FearPlayer set (MonsterAI.UpdateAI prefix).
internal static class Fear
{
    // Keep running until the player is this much beyond FearRange (no flicker at the edge).
    internal const float Margin = 4f;

    // Keep running this long after it last sensed the player (running = alerted = sees all around).
    internal const float Memory = 5f;

    // Pure. Does this player start (not current) or keep (current) the fear? sinceSensed = seconds since the creature
    // last sensed the player it already run from.
    internal static bool Qualifies(float distance, float fearRange, bool current, bool sensedNow, float sinceSensed)
    {
        if (fearRange <= 0f)
        {
            return false;
        }
        if (current)
        {
            return distance <= fearRange + Margin && (sensedNow || sinceSensed <= Memory);
        }
        return sensedNow && distance <= fearRange;
    }

    // Vanilla senses, without our CanSenseTarget gate (which say false for an afraid creature).
    internal static bool Senses(MonsterAI ai, Player player) => ai.CanSeeTarget(player) || ai.CanHearTarget(player);

    // Once-per-second check part. Caller: owner, mod live, rules in force, not exempt, not routed.
    // First the closest afraid-of player that qualify; only then (rare) the sight check of players it would fight, so a
    // creature that can never be afraid (in no home list) never pay for a raycast here.
    internal static void Update(MonsterAI ai, CreatureState state, MoraleRules rules, float t)
    {
        var range = rules.FearRange;
        if (range <= 0f || ai.IsSleeping())
        {
            Stop(ai, state, true);
            return;
        }
        // Its target is a player it would fight: it fight (vanilla), no fear.
        var target = ai.m_targetCreature as Player;
        if (CreatureCheck.Counts(target))
        {
            var toward = Attitudes.Judge(ai, target);
            if (toward == Attitude.Hostile || toward == Attitude.Provoked)
            {
                Stop(ai, state, false);
                return;
            }
        }
        var pos = ai.transform.position;
        var rangeSqr = range * range;
        var keep = range + Margin;
        var keepSqr = keep * keep;
        Player best = null;
        var bestSqr = float.MaxValue;
        var bestSensed = false;
        var anyFought = false;
        foreach (var player in Player.GetAllPlayers())
        {
            if (!CreatureCheck.Counts(player))
            {
                continue;
            }
            var sqr = (player.transform.position - pos).sqrMagnitude;
            if (sqr > keepSqr)
            {
                continue;
            }
            var current = ReferenceEquals(player, state.FearPlayer);
            if (!current && sqr > rangeSqr)
            {
                continue;
            }
            var attitude = Attitudes.Judge(ai, player);
            if (attitude == Attitude.Hostile || attitude == Attitude.Provoked)
            {
                anyFought |= sqr <= rangeSqr;
                continue;
            }
            if (attitude != Attitude.Afraid)
            {
                continue;
            }
            var sensed = Senses(ai, player);
            if (current && sensed)
            {
                state.FearSensedAt = t;
            }
            var since = current ? t - state.FearSensedAt : float.MaxValue;
            if (!Qualifies(Mathf.Sqrt(sqr), range, current, sensed, since) || sqr >= bestSqr)
            {
                continue;
            }
            best = player;
            bestSqr = sqr;
            bestSensed = sensed;
        }
        if (best == null)
        {
            // Run over: it calm. A player it would now fight close and sensed (standing or stars changed mid-run): Calm
            // keep the alert for that fight (same rule as a rout's end). Sight check only when a run end.
            Stop(ai, state, true);
            return;
        }
        if (anyFought && SensesFought(ai, pos, rangeSqr))
        {
            // A player it would fight is close and sensed: it fight, not run (vanilla pick them at its next search).
            Stop(ai, state, false);
            return;
        }
        if (!ReferenceEquals(best, state.FearPlayer))
        {
            // New fear, or a closer player: fresh flee point away from them at once, cornered count from zero.
            state.FearPlayer = best;
            state.FearSensedAt = t;
            state.CorneredTime = 0f;
            state.FirstFleeFrame = true;
            return;
        }
        if (bestSensed)
        {
            state.FearSensedAt = t;
        }
    }

    // Some player within FearRange it is hostile to or provoked by, and it sense them (decision 29). Also the run's end
    // (FleeFrames.Calm): such a player keep the alert for the fight. Raycast only for such players.
    internal static bool SensesFought(MonsterAI ai, Vector3 pos, float rangeSqr)
    {
        foreach (var player in Player.GetAllPlayers())
        {
            if (!CreatureCheck.Counts(player) || (player.transform.position - pos).sqrMagnitude > rangeSqr)
            {
                continue;
            }
            var attitude = Attitudes.Judge(ai, player);
            if ((attitude == Attitude.Hostile || attitude == Attitude.Provoked) && Senses(ai, player))
            {
                return true;
            }
        }
        return false;
    }

    // Fear over. calm = the player is out of range or unsensed: it calms (FleeFrames.Calm: silent un-alert of the alert
    // my frames held; hurt within 5 s = at a later check; a player it would fight close and sensed = alert kept for
    // them), and look for other enemies at once. Not calm = something else take over (a provocation, a player it fight
    // while one it fear still near, a rout, exempt, mod off): alert left as it is, and theirs now.
    internal static void Stop(MonsterAI ai, CreatureState state, bool calm)
    {
        var was = !ReferenceEquals(state.FearPlayer, null);
        state.FearPlayer = null;
        state.CorneredTime = 0f;
        if (!was || ai == null)
        {
            return;
        }
        if (calm)
        {
            FleeFrames.Calm(ai, state);
        }
        else
        {
            state.FleeAlert = false;
        }
        ai.m_updateTargetTimer = Mathf.Min(ai.m_updateTargetTimer, 0.5f);
    }

    // MonsterAI.UpdateAI prefix body while FearPlayer set (owner). Cornered count first (2.6): the player stayed within
    // CorneredRange long enough = it strike, vanilla run this frame with its new target. Else one flee frame.
    internal static bool Frame(MonsterAI ai, CreatureState state, float dt, ref bool result)
    {
        var player = state.FearPlayer;
        if (!CreatureCheck.Counts(player))
        {
            // Gone, dead or ghost since the check: run over, next check decide (new fear, or calm: FleeAlert kept for
            // it, so no stray alert stay up).
            state.FearPlayer = null;
            state.CorneredTime = 0f;
            ai.m_updateTargetTimer = Mathf.Min(ai.m_updateTargetTimer, 0.5f);
            return true;
        }
        var rules = ServerRules.Current;
        var from = player.transform.position;
        var sqr = (from - ai.transform.position).sqrMagnitude;
        // Passive Mobs world (only shaken followers run there): no cornering, as before the fear (design 2.8).
        if (Cornered.Advance(ref state.CorneredTime, sqr, dt, rules.CorneredRange, rules.CorneredSeconds)
            && !Attitudes.PassiveMobs())
        {
            Cornered.Strike(ai, state, player, rules);
            return true;
        }
        return FleeFrames.Run(ai, state, dt, from, ref result);
    }
}
