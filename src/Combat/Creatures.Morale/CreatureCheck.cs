using UnityEngine;

namespace MC.Combat.CreaturesMoraleMod;

// Me = the owner's side of every creature (design 7.3), from the MonsterAI.UpdateAI prefix (20 Hz, every creature):
//   gained? (not owned in the last 0.25 s) -> memory reset;  once per second -> Check;  routed -> rout frame;
//   running from a player -> fear frame (cornered count + flee frame).
// Check, in order: cached facts + exemption; rout end from the ZDO; a recent rout it missed; night hunter stand-down
// (2.5 piece 5); fear (piece 2; none once its flee frames broke); when not running: take back the alert of a run that
// just ended (rout over, fear calmed: FleeFrames.Calm; kept when a player it would fight is close and sensed), drop a
// player it is now afraid of (piece 3), calm down when startled with only afraid-of players near (piece 6).
// Alert = vanilla's own state: every player see its icon.
internal static class CreatureCheck
{
    private const float GainGap = 0.25f;
    private const float Interval = 1f;
    private const float FirstSpread = 0.3f;
    internal const float HuntRange = 200f;       // vanilla hunt fallback range (BaseAI.FindEnemy)
    internal const float CalmAfterHurt = 5f;     // no un-alert this soon after a hurt (no alert flicker while burning)

    // Prefix body for an owned creature. Return = prefix return.
    internal static bool Update(MonsterAI ai, float dt, ref bool result)
    {
        var state = CreatureState.Get(ai);
        var t = Time.time;
        if (t - state.LastOwnedTime > GainGap)
        {
            state.OnGained(t);
        }
        state.LastOwnedTime = t;
        if (t >= state.NextCheck)
        {
            Check(ai, state, t);
        }
        if (state.Exempt || state.FramesBroken)
        {
            return true;
        }
        if (state.RoutEnd > t)
        {
            return FleeFrames.Run(ai, state, dt, state.RoutFrom, ref result);
        }
        if (!ReferenceEquals(state.FearPlayer, null))
        {
            return Fear.Frame(ai, state, dt, ref result);
        }
        return true;
    }

    private static void Check(MonsterAI ai, CreatureState state, float t)
    {
        // Timer first: an exception below never make me run every frame.
        state.NextCheck = t + Interval + (state.JustGained ? Random.Range(0f, FirstSpread) : 0f);
        state.JustGained = false;

        var rules = ServerRules.Current;
        state.RefreshFacts(rules);
        var nview = ai.m_nview;
        var zdo = nview.GetZDO();
        var now = Attitudes.Now();

        // Rout end on the local clock (rout frames), read from the ZDO: it follow the creature across owners.
        var until = CreatureKeys.GetRoutUntil(zdo);
        if (until > now)
        {
            var wasRouted = state.RoutEnd > t;
            state.RoutEnd = t + (float)((until - now) / (double)System.TimeSpan.TicksPerSecond);
            state.RoutFrom = CreatureKeys.GetRoutFrom(zdo, ai.transform.position);
            if (!wasRouted)
            {
                state.FirstFleeFrame = true;
            }
        }
        else
        {
            state.RoutEnd = 0f;
        }

        if (state.Exempt || ServerRules.Pending || !Plugin.Live)
        {
            // Rules pending (or me off) = vanilla, also for a creature another game routed: no rout frames here. Its
            // alert is vanilla's now (target search or 30 s give-up).
            state.RoutEnd = 0f;
            state.HuntStandDown = false;
            Fear.Stop(ai, state, false);
            state.FleeAlert = false;
            return;
        }
        Rout.CheckRecent(ai, state, zdo, rules, now);
        if (state.RoutEnd > t)
        {
            state.HuntStandDown = false;
            Fear.Stop(ai, state, false);
            return;
        }
        UpdateHuntStandDown(ai, state, rules);
        if (state.FramesBroken)
        {
            // Flee frames broke (vanilla every tick now): no fear, but still never keep a player it fear as target.
            Fear.Stop(ai, state, false);
        }
        else
        {
            Fear.Update(ai, state, rules, t);
            if (!ReferenceEquals(state.FearPlayer, null))
            {
                return; // running: its frames clear targets and keep it alerted
            }
        }
        // Not running, not routed: a run that just ended (rout over, fear player gone) give back the unalerted look,
        // unless a player it would fight is close and sensed (alert kept for that fight, as at a fear's end). Here, not
        // at the rout's last frame: a shaken follower that runs on from a close player keep its alert (no second alert
        // sound).
        FleeFrames.Calm(ai, state);
        DropAfraidTarget(ai);
        CalmDown(ai);
    }

    // Night hunter (world spawn with the hunt flag) stand down while no player within the hunt range is hostile or
    // provoked toward it: not forced alert every tick, no fallback to players, a normal night creature (afraid of the
    // players who outclass it). A player it already chase and is hostile or provoked toward stay its prey at any
    // distance (vanilla chase a kept target without sensing it, and a hunter never give up after 60 s without attacking).
    private static void UpdateHuntStandDown(MonsterAI ai, CreatureState state, MoraleRules rules)
    {
        if (!state.NightHunter || !rules.NightHuntersCanBeAfraid)
        {
            state.HuntStandDown = false;
            return;
        }
        var prey = ai.m_targetCreature as Player;
        if (Counts(prey))
        {
            var toward = Attitudes.Judge(ai, prey);
            if (toward == Attitude.Hostile || toward == Attitude.Provoked)
            {
                state.HuntStandDown = false;
                return;
            }
        }
        var pos = ai.transform.position;
        foreach (var player in Player.GetAllPlayers())
        {
            if (!Counts(player) || (player.transform.position - pos).sqrMagnitude > HuntRange * HuntRange)
            {
                continue;
            }
            var attitude = Attitudes.Judge(ai, player);
            if (attitude == Attitude.Hostile || attitude == Attitude.Provoked)
            {
                state.HuntStandDown = false;
                return;
            }
        }
        state.HuntStandDown = true;
    }

    // HuntPlayer postfix, for a creature whose last check set the stand-down: still good with rules in force now?
    // Rules changed since (NightHuntersCanBeAfraid off = exempt, client wait for server rules, mod going off) end it at
    // once, like Judge that read facts again on rules change. Cheap: few compares while facts fresh.
    internal static bool StandDownHolds(CreatureState state)
    {
        if (!Plugin.Live || ServerRules.Pending)
        {
            return false;
        }
        var rules = ServerRules.Current;
        if (!rules.NightHuntersCanBeAfraid)
        {
            return false;
        }
        state.EnsureFacts(rules);
        return !state.Exempt;
    }

    // Piece 3 (not running): target is a player it is now afraid of (boss kill or kill step mid-fight, provocation ran
    // out, alert by a projectile that was not ours, taken over mid-fight) and that is not close and sensed (else the fear
    // took it): what vanilla's give-up does (MonsterAI.UpdateTarget), but search again soon (other enemies).
    private static void DropAfraidTarget(MonsterAI ai)
    {
        var target = ai.m_targetCreature as Player;
        if (target == null || Attitudes.Judge(ai, target) != Attitude.Afraid)
        {
            return;
        }
        ai.SetAlerted(false);
        ai.m_targetCreature = null;
        ai.m_targetStatic = null;
        ai.m_timeSinceAttacking = 0f;
        ai.m_updateTargetTimer = 1f;
    }

    // Piece 6 (not running): alerted with no target (burn or poison tick, pheromones, spawn ability, ownerless
    // projectile, a run this game took over from another owner mid-way), not hunting, not hurt for 5 s, and every player
    // near is one it is afraid of -> silent un-alert. No player near: me leave vanilla's own 30 s give-up alone. (Alert of
    // my own runs: FleeFrames.Calm, player near or not.)
    private static void CalmDown(MonsterAI ai)
    {
        if (!ai.IsAlerted() || ai.m_targetCreature != null || ai.m_targetStatic != null || ai.m_timeSinceHurt < CalmAfterHurt
            || ai.HuntPlayer())
        {
            return;
        }
        var range = Mathf.Max(ai.m_viewRange, 10f);
        var pos = ai.transform.position;
        var any = false;
        foreach (var player in Player.GetAllPlayers())
        {
            if (!Counts(player) || (player.transform.position - pos).sqrMagnitude > range * range)
            {
                continue;
            }
            if (Attitudes.Judge(ai, player) != Attitude.Afraid)
            {
                return;
            }
            any = true;
        }
        if (any)
        {
            ai.SetAlerted(false);
        }
    }

    // Ghost and debug-fly players are not there for creatures (vanilla: never sensed, never hunted).
    internal static bool Counts(Player player) =>
        player != null && !player.IsDead() && !player.InGhostMode() && !player.InDebugFlyMode();
}
