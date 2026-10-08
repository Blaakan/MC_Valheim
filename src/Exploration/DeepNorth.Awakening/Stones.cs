using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.DeepNorthAwakeningMod;

// Me = the server side of the Malicious Ice (design 2.1). Server only (single player, host, dedicated).
//   Count:   ZDOMan destroy delegate (ServerWorld) give every destroyed ZDO; prefab BlackIce_Start = one stone, once per
//            ZDOID (stale ZDO update can make server destroy same ZDOID twice). Works whoever broke it, mod or not.
//   Apply:   every stone = vanilla "The Jotun Advance" (token, each game localise it); stone 1, 2 = no invasion;
//            stone 3 = InvasionsAtThirdStone vanilla invasions; stone 4+ = 1 invasion. Invasion = vanilla server
//            RPC_RequestStartEvent called here with Bypass (vanilla cap of 3 and placement rules apply).
//   Hold:    any other start request for jotun_invasion (player without me breaking a stone, admin pevents, other mod)
//            wait HoldSeconds; a stone break that come within HoldSeconds AFTER it eat it (stone already handled), else
//            it run. Only after: a vanilla owner send its request before its stone's ZDO destroy (m_onDestroyed run
//            before ZNetScene.Destroy, destroy go out on the next ZDOMan.Update, same connection), so a break before
//            the request is another stone and must not eat an admin's request.
//   Detect:  world without the key (mod new on this world, or admin removed it): placed Morkhalla with no stone ZDO
//            near = broken; write the count, no message, no invasion. Again when the key go from there to missing.
internal static class Stones
{
    internal const string StonePrefab = "BlackIce_Start";
    internal const string MorkhallaPrefab = "MorkBorg";
    internal const string InvasionEvent = "jotun_invasion";
    internal const string StoneText = "$fimbulvinterorb_start";
    internal const float HoldSeconds = 5f;
    internal const float StoneSearchRadius = 160f;

    internal static readonly int StoneHash = StonePrefab.GetStableHashCode();

    private struct HeldRequest
    {
        internal long Sender;
        internal int Index;
        internal float Arrived;
    }

    private static readonly HashSet<ZDOID> Counted = new HashSet<ZDOID>();
    private static readonly List<Vector3> Pending = new List<Vector3>();
    private static readonly List<float> RecentBreaks = new List<float>();
    private static readonly List<HeldRequest> Held = new List<HeldRequest>();
    private static bool _detectDone;
    private static bool _lastHadKey;
    private static bool _blockedLogged;

#if DEBUG
    // Self test force the invasions of the third stone (server-only setting, never the config). Null = setting.
    internal static int? TestInvasionsAtThirdStone { get; set; }
#endif

    private static int InvasionsAtThirdStone
    {
        get
        {
#if DEBUG
            if (TestInvasionsAtThirdStone.HasValue)
            {
                return TestInvasionsAtThirdStone.Value;
            }
#endif
            return Plugin.InvasionsAtThirdStone != null ? Plugin.InvasionsAtThirdStone.Value : 3;
        }
    }

    // True while me start an invasion myself: the request prefix let it through.
    internal static bool Bypass { get; private set; }

    internal static bool HasWork => Pending.Count > 0 || Held.Count > 0 || RecentBreaks.Count > 0 || !_detectDone;

    internal static bool IsInvasionName(string name) =>
        string.Equals(name, InvasionEvent, StringComparison.InvariantCultureIgnoreCase);

    internal static bool IsInvasionIndex(PersistentEventSystem pes, int index)
    {
        var list = pes != null ? pes.m_possibleEvents : null;
        return list != null && index >= 0 && index < list.Count && list[index] != null
               && IsInvasionName(list[index].internalName);
    }

    internal static int InvasionIndex(PersistentEventSystem pes)
    {
        var list = pes != null ? pes.m_possibleEvents : null;
        if (list == null)
        {
            return -1;
        }
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] != null && IsInvasionName(list[i].internalName))
            {
                return i;
            }
        }
        return -1;
    }

    internal static int ActiveInvasions(PersistentEventSystem pes, int index)
    {
        var n = 0;
        var active = pes != null && pes.m_activePersistentEvents != null ? pes.m_activePersistentEvents.list : null;
        if (active == null)
        {
            return 0;
        }
        foreach (var e in active)
        {
            if (e != null && e.sourceEventId == index)
            {
                n++;
            }
        }
        return n;
    }

    // Pure (self test): vanilla invasions a stone starts.
    internal static int InvasionsFor(int stoneNumber, int atThirdStone)
    {
        if (stoneNumber < 3)
        {
            return 0;
        }
        return stoneNumber == 3 ? Math.Max(0, atThirdStone) : 1;
    }

    // ServerWorld destroy delegate, server only. ZDO still readable here (before vanilla remove it). Me only note the
    // stone: key write, messages and invasions (which make new objects) wait for the next server tick, outside
    // vanilla's destroy handling.
    internal static void OnDestroyed(ZDO zdo)
    {
        if (zdo.GetPrefab() != StoneHash || !Counted.Add(zdo.m_uid))
        {
            return;
        }
        RecentBreaks.Add(Time.unscaledTime);
        // Key missing (detection not run yet, or admin just removed it): count the old stones now, while this stone
        // ZDO is still in its sector (detection see it whole; Apply add it after).
        WorldState.Refresh();
        RearmIfKeyGone();
        DetectIfNeeded(force: true);
        Pending.Add(zdo.GetPosition());
    }

    private static void Apply(Vector3 pos)
    {
        WorldState.Refresh();
        var n = WorldState.Stones + 1;
        WriteChecked(n);
        Log.Info($"A Malicious Ice was broken at ({pos.x:0}, {pos.z:0}): {n} broken in this world, Deep North stage "
                 + $"{WorldState.StageOf(n)}.");
        Message(StoneText);
        var invasions = InvasionsFor(n, InvasionsAtThirdStone);
        if (invasions > 0)
        {
            StartInvasions(invasions);
        }
    }

    // Write the count; another mod blocking global keys (World Advancement Progression) = warn once.
    private static void WriteChecked(int count)
    {
        WorldState.WriteStones(count);
        _lastHadKey = WorldState.HasStonesKey;
        if (!WorldState.HasStonesKey && !_blockedLogged)
        {
            _blockedLogged = true;
            Log.Warning($"The world did not keep the global key {WorldState.StonesKey}: another mod blocks it (World "
                        + $"Advancement Progression: add {WorldState.StonesKey} to its allowed keys). The Deep North "
                        + "cannot awaken until it is kept.");
        }
    }

    // Key was there and is gone (admin removekey / resetkeys): count the explored Morkhalla again. Only on that change,
    // so a mod that block the key never cause a full scan every tick.
    private static void RearmIfKeyGone()
    {
        if (_detectDone && _lastHadKey && !WorldState.HasStonesKey)
        {
            _detectDone = false;
            Log.Info($"The global key {WorldState.StonesKey} was removed: counting the broken Malicious Ice again.");
        }
        _lastHadKey = WorldState.HasStonesKey;
    }

    internal static void Message(string text)
    {
        var routed = ZRoutedRpc.instance;
        if (routed == null || string.IsNullOrEmpty(text))
        {
            return;
        }
        // Vanilla MessageHud rpc; each game localise the text (vanilla token work too). Dedicated server has no
        // MessageHud: its own copy find no handler and do nothing.
        routed.InvokeRoutedRPC(ZRoutedRpc.Everybody, "ShowMessage", (int)MessageHud.MessageType.Center, text);
    }

    // Server: start count vanilla invasions now. Returns how many really started.
    internal static int StartInvasions(int count)
    {
        var pes = PersistentEventSystem.instance;
        var index = InvasionIndex(pes);
        if (index < 0)
        {
            Log.Warning($"No '{InvasionEvent}' event exists in this game, so no Jotun invasion can start.");
            return 0;
        }
        var before = ActiveInvasions(pes, index);
        for (var i = 0; i < count; i++)
        {
            RunVanilla(pes, ZNet.GetUID(), index);
        }
        var started = ActiveInvasions(pes, index) - before;
#if DEBUG
        TestLastRequested = count;
        TestLastStarted = started;
#endif
        if (started < count)
        {
            var max = pes.m_possibleEvents[index].maxConcurrent;
            Log.Info($"Started {started} of {count} Jotun invasion(s): the game allows {max} at once and needs a free "
                     + "spot far from every player.");
        }
        else
        {
            Log.Info($"Started {started} Jotun invasion(s) in the world.");
        }
        return started;
    }

    private static void RunVanilla(PersistentEventSystem pes, long sender, int index)
    {
        Bypass = true;
        try
        {
            pes.RPC_RequestStartEvent(sender, index);
        }
        finally
        {
            Bypass = false;
        }
    }

    // RPC_RequestStartEvent prefix (server): a jotun_invasion request me did not make.
    internal static void Hold(long sender, int index)
    {
        Held.Add(new HeldRequest { Sender = sender, Index = index, Arrived = Time.unscaledTime });
        Log.Debug($"Held a Jotun invasion request for {HoldSeconds:0} s: a broken Malicious Ice is handled by "
                  + $"{ModInfo.Name}, any other request starts after the wait.");
    }

    // ServerWorld tick (server).
    internal static void Update()
    {
        RearmIfKeyGone();
        DetectIfNeeded(force: false);
        if (Pending.Count > 0)
        {
            var stones = Pending.ToArray();
            Pending.Clear();
            foreach (var pos in stones)
            {
                Apply(pos);
            }
        }
        var now = Time.unscaledTime;
        for (var i = 0; i < Held.Count; i++)
        {
            var h = Held[i];
            if (now - h.Arrived < HoldSeconds)
            {
                continue;
            }
            Held.RemoveAt(i);
            i--;
            Settle(PersistentEventSystem.instance, h);
        }
        for (var i = RecentBreaks.Count - 1; i >= 0; i--)
        {
            if (now - RecentBreaks[i] > 4f * HoldSeconds)
            {
                RecentBreaks.RemoveAt(i);
            }
        }
    }

    // A held request whose wait is over (or the feature turning off): a stone break claimed it = drop, else run.
    private static void Settle(PersistentEventSystem pes, HeldRequest h)
    {
        if (EatBreakAfter(h.Arrived))
        {
            Log.Debug("Dropped a Jotun invasion request that came with a broken Malicious Ice (already handled).");
            return;
        }
        if (pes != null)
        {
            RunVanilla(pes, h.Sender, h.Index);
        }
    }

    // Pure-ish (self test): first break from just before the request (clock tolerance) to HoldSeconds after it.
    internal static bool ClaimsRequest(float breakTime, float arrived)
    {
        var d = breakTime - arrived;
        return d >= -0.05f && d <= HoldSeconds;
    }

    private static bool EatBreakAfter(float arrived)
    {
        for (var i = 0; i < RecentBreaks.Count; i++)
        {
            if (ClaimsRequest(RecentBreaks[i], arrived))
            {
                RecentBreaks.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    // Feature off (patches still on, RecentBreaks still known): held requests settle now, like at the end of the wait.
    internal static void ReleaseHeld()
    {
        var pes = PersistentEventSystem.instance;
        var held = new List<HeldRequest>(Held);
        Held.Clear();
        if (pes == null || ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }
        foreach (var h in held)
        {
            Settle(pes, h);
        }
    }

    // Old world: key missing = count broken Morkhalla once (no message, no invasion).
    internal static void DetectIfNeeded(bool force)
    {
        if (_detectDone)
        {
            return;
        }
        var zs = ZoneSystem.instance;
        var net = ZNet.instance;
        if (zs == null || ZDOMan.instance == null || net == null || !net.IsServer())
        {
            return;
        }
        // Location list not ready yet (new world still placing): wait, unless a stone break need the count now.
        if (!force && !zs.LocationsGenerated)
        {
            return;
        }
        WorldState.Refresh();
        _detectDone = true;
        if (WorldState.HasStonesKey)
        {
            _lastHadKey = true;
            return;
        }
        var morkhallas = new List<Vector3>();
        foreach (var li in zs.m_locationInstances.Values)
        {
            var loc = li.m_location;
            if (li.m_placed && loc != null && IsMorkhalla(loc))
            {
                morkhallas.Add(li.m_position);
            }
        }
        var stones = new List<Vector3>();
        foreach (var zdo in AllZdos(StonePrefab))
        {
            stones.Add(zdo.GetPosition());
        }
        var broken = CountBroken(morkhallas, stones, StoneSearchRadius);
        WriteChecked(broken);
        Log.Info($"Counted the broken Malicious Ice of this world: {morkhallas.Count} explored Morkhalla, {broken} with "
                 + $"its Malicious Ice already broken. Deep North stage {WorldState.StageOf(broken)}.");
    }

    // Runtime name = prefab name (ZoneSystem set m_prefabName from the prefab); list entry name "morkborg" as fallback.
    private static bool IsMorkhalla(ZoneSystem.ZoneLocation loc)
    {
        return string.Equals(loc.m_prefabName, MorkhallaPrefab, StringComparison.OrdinalIgnoreCase)
               || string.Equals(loc.m_name, MorkhallaPrefab, StringComparison.OrdinalIgnoreCase);
    }

    // Pure (self test): Morkhalla with no stone within radius (horizontal) = broken.
    internal static int CountBroken(List<Vector3> morkhallas, List<Vector3> stones, float radius)
    {
        var r2 = radius * radius;
        var broken = 0;
        foreach (var m in morkhallas)
        {
            var found = false;
            foreach (var s in stones)
            {
                var dx = s.x - m.x;
                var dz = s.z - m.z;
                if (dx * dx + dz * dz <= r2)
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                broken++;
            }
        }
        return broken;
    }

    // Every ZDO of a prefab the server hold (vanilla iterative scan, run to the end at once).
    internal static List<ZDO> AllZdos(string prefab)
    {
        var list = new List<ZDO>();
        var zdos = ZDOMan.instance;
        if (zdos == null)
        {
            return list;
        }
        var index = 0;
        var guard = 0;
        while (!zdos.GetAllZDOsWithPrefabIterative(prefab, list, ref index) && guard++ < 1000000)
        {
        }
        return list;
    }

    // World end, feature off.
    internal static void Clear()
    {
        Counted.Clear();
        Pending.Clear();
        RecentBreaks.Clear();
        Held.Clear();
        _detectDone = false;
        _lastHadKey = false;
        _blockedLogged = false;
        Bypass = false;
    }

#if DEBUG
    // Self test: forget detection so it runs again.
    internal static void TestResetDetection() => _detectDone = false;

    internal static int HeldCount => Held.Count;

    // Self test: what the last StartInvasions was asked for and what really started (-1 = none since the reset).
    internal static int TestLastRequested { get; set; } = -1;

    internal static int TestLastStarted { get; set; } = -1;

    // Self test: this stone's destroy is never counted (a stone that goes away like in a game without me: no count,
    // no message, no invasion).
    internal static void TestIgnoreBreak(ZDOID id) => Counted.Add(id);
#endif
}
