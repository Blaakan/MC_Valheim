using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.CreaturesMoraleMod;

// Me = leader down, pack run (design 2.7).
//   leader owner -> everybody  "<guid>.Rout"  (Vector3 death position, int leader prefab hash), multiplayer only
// Trigger: BaseAI.OnDeath postfix (dying creature's owner only): prefab is a leader, not exempt, a player or a tame
// took part (ZDO Attackers > 0, or last hit by a player or a tame), not handled already (ZDOID set, 10 s).
// Delivery: single player = Apply here. Multiplayer = routed rpc to Everybody: ZRoutedRpc handle it on the sender at
// once and route it by the server to every other ready peer; each game Apply to the followers IT owns. Vanilla peer
// ignore the unknown name. Every game that Apply keep the rout a while (recent routs): the owner check of a follower
// whose ownership moved while the broadcast flew apply it at its first check.
// Register throw when same name twice on one ZRoutedRpc, and there is no unregister: me remember the instance. New
// ZNet = new ZRoutedRpc = register again (ZNet.Awake postfix, or Start when turned on inside a world). Handler stay
// on while off and return at once then. Vanilla call the handler with no try/catch (network receive path): me catch.
// Rout frames: FleeFrames.Run from the death point (shared with the fear frames), from the MonsterAI.UpdateAI prefix.
internal static class Rout
{
    internal const string RoutRpc = ModInfo.Guid + ".Rout";
    private const float HandledSeconds = 10f;

    // Same rout seen by two games: deadlines differ by network delay and clock sync (client clock = server clock
    // late by the trip). Me no apply same rout twice after an ownership change: second apply would wipe anger of a
    // hit during the rout again and pick a new flee point.
    private const long SameRoutTicks = 2L * TimeSpan.TicksPerSecond;

    private struct Recent
    {
        internal Vector3 Position;
        internal int Leader;
        internal long Until;
        internal List<ZDOID> Sleepers;   // followers asleep at the rout (did not see it): never routed by it; null = none
    }

    private static readonly List<Recent> RecentRouts = new List<Recent>();
    private static readonly Dictionary<ZDOID, float> Handled = new Dictionary<ZDOID, float>();
    private static readonly List<ZDOID> HandledDone = new List<ZDOID>();
    private static ZRoutedRpc _registeredOn;
    private static bool _active;

#if DEBUG
    // Self test: use the rpc even with no peer (ZRoutedRpc then handle it here at once), so register + handler run.
    internal static bool ForceBroadcast { get; set; }
#endif

    // Creatures this game applied the last rout to (sender log, self tests).
    internal static int LastAppliedHere { get; private set; }

    internal static void Start()
    {
        _active = true;
        EnsureRegistered();
    }

    internal static void Stop()
    {
        _active = false;
        Clear();
    }

    // World end or off: forget recent routs and handled leader deaths.
    internal static void Clear()
    {
        RecentRouts.Clear();
        Handled.Clear();
    }

    // Once per ZRoutedRpc instance. Instance set before Register: a throw never make me try again on same one.
    internal static void EnsureRegistered()
    {
        var routed = ZRoutedRpc.instance;
        if (routed == null || ReferenceEquals(routed, _registeredOn))
        {
            return;
        }
        _registeredOn = routed;
        routed.Register<Vector3, int>(RoutRpc, OnRout);
    }

    // ---------- trigger ----------

    // BaseAI.OnDeath postfix: owner only (Character.OnDeath return before m_onDeath on other games); ZDO still valid.
    internal static void OnDeath(BaseAI baseAi)
    {
        if (!_active || !Plugin.Live || ServerRules.Pending)
        {
            return;
        }
        var ai = baseAi as MonsterAI;
        if (ai == null)
        {
            return;
        }
        var nview = ai.m_nview;
        if (nview == null || !nview.IsValid() || !nview.IsOwner())
        {
            return;
        }
        var zdo = nview.GetZDO();
        var rules = ServerRules.Current;
        var leaderHash = zdo.GetPrefab();
        if (!rules.IsLeader(leaderHash))
        {
            return;
        }
        var state = CreatureState.Get(ai);
        state.RefreshFacts(rules);
        if (state.Exempt || !PlayerOrTameTookPart(ai.m_character, zdo))
        {
            return;
        }
        var t = Time.time;
        PruneHandled(t);
        if (Handled.ContainsKey(zdo.m_uid))
        {
            return;
        }
        Handled[zdo.m_uid] = t + HandledSeconds;
        var position = ai.transform.position;
        Send(position, leaderHash);
        Log.Debug($"Rout: {ai.m_character.m_name} died, applied to {LastAppliedHere} creature(s) here.");
    }

    // Decision 10: a leader that drown, fall or die to other creatures alone does not rout its pack.
    private static bool PlayerOrTameTookPart(Character character, ZDO zdo)
    {
        if (zdo.GetInt(ZDOVars.s_attackers) > 0)
        {
            return true;
        }
        var last = character != null ? character.m_lastHit : null;
        var attacker = last != null ? last.GetAttacker() : null;
        return attacker != null && (attacker.IsPlayer() || attacker.IsTamed());
    }

    private static void PruneHandled(float now)
    {
        if (Handled.Count == 0)
        {
            return;
        }
        HandledDone.Clear();
        foreach (var pair in Handled)
        {
            if (pair.Value <= now)
            {
                HandledDone.Add(pair.Key);
            }
        }
        foreach (var id in HandledDone)
        {
            Handled.Remove(id);
        }
        HandledDone.Clear();
    }

    // Leader owner. Single player (no peer): apply here, no network. Else tell everybody (this game too, at once).
    internal static void Send(Vector3 position, int leaderHash)
    {
        LastAppliedHere = 0;
        var routed = ZRoutedRpc.instance;
        var net = ZNet.instance;
        var broadcast = routed != null && net != null && net.GetPeerConnections() > 0;
#if DEBUG
        broadcast |= routed != null && ForceBroadcast;
#endif
        if (!broadcast)
        {
            LastAppliedHere = Apply(position, leaderHash);
            return;
        }
        EnsureRegistered();
        routed.InvokeRoutedRPC(ZRoutedRpc.Everybody, RoutRpc, position, leaderHash);
    }

    // Every game with me (sender too): apply to the followers this game own.
    private static void OnRout(long sender, Vector3 position, int leaderHash)
    {
        try
        {
            if (!_active || !Plugin.Live)
            {
                return;
            }
            var applied = Apply(position, leaderHash);
            var routed = ZRoutedRpc.instance;
            if (routed != null && sender == routed.m_id)
            {
                LastAppliedHere = applied;
            }
            else if (applied > 0)
            {
                Log.Debug($"Rout from another game applied to {applied} creature(s).");
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Rout.OnRout", e);
        }
    }

    // ---------- apply ----------

    // Followers of that leader, owned here, alive, near the death point, not exempt, not sleeping (it did not see it)
    // start to run. Also kept as a recent rout for followers gained later, with the loaded followers that were asleep
    // (any owner: sleep state reach every game by rpc), so one that wake while it last is not routed then. Return how
    // many.
    internal static int Apply(Vector3 position, int leaderHash)
    {
        try
        {
            if (!_active || !Plugin.Live || ServerRules.Pending)
            {
                return 0;
            }
            var rules = ServerRules.Current;
            if (!rules.IsLeader(leaderHash))
            {
                return 0;
            }
            var now = Attitudes.Now();
            var until = now + Attitudes.SecondsToTicks(rules.RoutSeconds);
            PruneRecent(now);
            var radiusSqr = rules.RoutRadius * rules.RoutRadius;
            var count = 0;
            List<ZDOID> sleepers = null;
            var all = BaseAI.BaseAIInstances;
            for (var i = 0; i < all.Count; i++)
            {
                var ai = all[i] as MonsterAI;
                if (ai == null || (ai.transform.position - position).sqrMagnitude > radiusSqr)
                {
                    continue;
                }
                var nview = ai.m_nview;
                var character = ai.m_character;
                if (nview == null || !nview.IsValid() || character == null || character.IsDead())
                {
                    continue;
                }
                var zdo = nview.GetZDO();
                if (!rules.IsFollower(leaderHash, zdo.GetPrefab()))
                {
                    continue;
                }
                if (ai.IsSleeping())
                {
                    if (sleepers == null)
                    {
                        sleepers = new List<ZDOID>();
                    }
                    sleepers.Add(zdo.m_uid);
                    continue;
                }
                if (!nview.IsOwner())
                {
                    continue;
                }
                var state = CreatureState.Get(ai);
                state.RefreshFacts(rules);
                if (state.Exempt)
                {
                    continue;
                }
                RoutOne(ai, state, zdo, position, until, now);
                count++;
            }
            RecentRouts.Add(new Recent { Position = position, Leader = leaderHash, Until = until, Sleepers = sleepers });
            return count;
        }
        catch (Exception e)
        {
            PatchGuard.Report("Rout.Apply", e);
            return 0;
        }
    }

    // Owner check (once per second): a recent rout this follower missed (ownership moved while the broadcast flew,
    // or it came into the radius) and whose RoutUntil is older than the rout's (by more than SameRoutTicks: else it
    // is the same rout, applied by the game that owned it then). Asleep now, or asleep at the rout (woke since): no.
    internal static void CheckRecent(MonsterAI ai, CreatureState state, ZDO zdo, MoraleRules rules, long now)
    {
        if (RecentRouts.Count == 0)
        {
            return;
        }
        PruneRecent(now);
        var radiusSqr = rules.RoutRadius * rules.RoutRadius;
        var pos = ai.transform.position;
        for (var i = 0; i < RecentRouts.Count; i++)
        {
            var r = RecentRouts[i];
            if (!rules.IsFollower(r.Leader, state.PrefabHash) || (pos - r.Position).sqrMagnitude > radiusSqr
                || ai.IsSleeping() || CreatureKeys.GetRoutUntil(zdo) >= r.Until - SameRoutTicks
                || (r.Sleepers != null && r.Sleepers.Contains(zdo.m_uid)))
            {
                continue;
            }
            RoutOne(ai, state, zdo, r.Position, r.Until, now);
        }
    }

    private static void PruneRecent(long now)
    {
        for (var i = RecentRouts.Count - 1; i >= 0; i--)
        {
            if (RecentRouts[i].Until <= now)
            {
                RecentRouts.RemoveAt(i);
            }
        }
    }

    // ZDO: RoutUntil (later rout extend), RoutFrom, provokers wiped (the rout wipe the anger). Memory: rout end at once,
    // flee and path timers reset so the first Flee pick a fresh point and path (else it walk its chase path 1 s more).
    private static void RoutOne(MonsterAI ai, CreatureState state, ZDO zdo, Vector3 from, long until, long now)
    {
        var end = CreatureKeys.SetRout(zdo, until, from);
        CreatureKeys.ClearProvokers(zdo);
        state.RoutEnd = Time.time + (float)((end - now) / (double)TimeSpan.TicksPerSecond);
        state.RoutFrom = from;
        state.HuntStandDown = false;
        Fear.Stop(ai, state, false); // the rout come first: no fear while it run from its leader
        FleeFrames.ResetFleeTimers(ai);
    }
}
