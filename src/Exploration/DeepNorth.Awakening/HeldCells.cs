using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.DeepNorthAwakeningMod;

// Me = the areas after Kall (design 2.6, user rules rounds 3 and 5). Before Kall nothing here matter: every awake cell
// spawn and storm. After Kall an area that is not cleared spawn its Jotun each time a player enter it again, then is
// engaged ("to be defeated"): no more Jotun while players are near it. Jotun army killed inside it count; at its target
// (seeded, ClearKillsMin..Max) it is cleared for good: no spawn, no storm, off the map. Creatures left alive stay.
//   Tag:      area Jotun carry their cell in ZDO int TagKey (burst caps count only the area's own, AreaSpawns).
//   Burst:    AreaSpawns, for the cell under a player in the zone. Engaged when it got Jotun or has some loaded there;
//             nothing spawned = try again RetrySeconds later.
//   Engaged:  server-authoritative: a game that burst send routed "<guid>.Engage"(cells); the server keep the cell until
//             no player (character or reference position) is within ReleaseDistance of its seed point: every point of
//             the cell (CellReach) plus the loaded zones around a player, so the cell left everybody's loaded range.
//             The game itself hold the cell LocalHoldSeconds meanwhile (network delay). Kall killed during the session:
//             the server engage at once the cell under every player and the cells of the area Jotun loaded around them
//             (no refill in the area of the fight; a cell nobody entered and with no Jotun still burst when entered).
//   Kills:    server: each Jotun-army ZDO destroyed (a death) at a Deep North position after Kall count for the awake,
//             not cleared cell there, whichever area it came from. Kept in the world in KillSlots (long: cell << 32 |
//             count) of a zone control nobody owns (or the server owns); at load the highest count of a cell win (a
//             slot whose zone control a player took is never written again: me take a new one, counts only grow).
//             Half the target = news "weakening"; target = cleared.
//   Cleared:  ClearedSlots (cell id) of such a zone control, read back at load; the cell's kill slot freed.
//   Lists:    cleared, engaged and kills per cell to everybody on change ("<guid>.Cleared") and to a player after each
//             rules request.
//   News:     routed "<guid>.News" (kind, cell, x, z) to everybody: half way = top-left "The Jotun army is weakening",
//             cleared = vanilla centre "The Jotun Retreat", shown by a game whose player stand in the cell or within
//             NewsRange of the kill.
// Routed rpcs have no unregister: handlers check _active. Vanilla peers ignore unknown routed rpcs.
internal static class HeldCells
{
    internal const string ClearedRpc = ModInfo.Guid + ".Cleared";
    internal const string EngageRpc = ModInfo.Guid + ".Engage";
    internal const string NewsRpc = ModInfo.Guid + ".News";
    internal const int Layout = 2;
    internal const int MaxCells = 100000;
    internal const float SendDelay = 1f;
    internal const float ReleaseCheckSeconds = 2f;
    internal const float LocalHoldSeconds = 10f;
    internal const float RetrySeconds = 5f;
    internal const float NewsRange = 100f;
    internal const int NewsWeakening = 1;
    internal const int NewsCleared = 2;
    internal const string ClearedText = "$fimbulvinterorb_destroyed";
    internal const string WeakeningText = "The Jotun army is weakening";

    // From the cell's seed point every point of the cell is within sqrt(2) x 340 m = 481 m (400 m squares, seed moved
    // up to 35 %, nearest seed wins). A player inside a cell never release it.
    internal const float CellReach = 482f;

    internal static readonly int TagKey = (ModInfo.Guid + ".Cell").GetStableHashCode();
    internal const int NoTag = int.MinValue;

    // Zone-control slots: cleared cells (int cell id) and kill progress (long cell << 32 | count; 0 = free).
    internal const int SlotCount = 8;
    internal static readonly int[] ClearedSlots = MakeSlots(".Cleared");
    internal static readonly int[] KillSlots = MakeSlots(".Kills");

    private static bool _active;
    private static ZRoutedRpc _registeredOn;

    // What this game was told / did.
    private static HashSet<int> _cleared = new HashSet<int>();
    private static HashSet<int> _engaged = new HashSet<int>();
    private static Dictionary<int, int> _kills = new Dictionary<int, int>();
    private static bool _known;
    private static ZNet _session;
    private static readonly Dictionary<int, float> LocalHold = new Dictionary<int, float>();

    // Server.
    private static readonly HashSet<int> ServerCleared = new HashSet<int>();
    private static readonly Dictionary<int, Vector2> ServerEngaged = new Dictionary<int, Vector2>();
    private static readonly Dictionary<int, int> Kills = new Dictionary<int, int>();
    private static readonly Dictionary<int, KillSlot> KillSlotOf = new Dictionary<int, KillSlot>();
    private static readonly HashSet<ZDOID> CountedKills = new HashSet<ZDOID>();
    private static readonly List<Vector3> TempPlaces = new List<Vector3>();
    private static readonly List<ZDO> TempZdos = new List<ZDO>();
    private static bool _scanned;
    private static bool _sawNoKall;
    private static bool _dirty;
    private static bool _slotWarned;
    private static float _sendAt;
    private static float _nextRelease;

    // Where a cell's kill count was written last. Id kept: the ZDO object may be pooled and reused by another ZDO.
    private struct KillSlot
    {
        internal ZDO Zdo;
        internal ZDOID Id;
        internal int Slot;
    }

    internal static bool Scanned => _scanned;

    internal static int ClearedCount => ServerCleared.Count;

    internal static int EngagedCount => ServerEngaged.Count;

    internal static int CountingCount => Kills.Count;

    // Bump each time the cleared list this game knows change (map overlay repaint).
    internal static int ClearedVersion { get; private set; }

    private static int[] MakeSlots(string name)
    {
        var slots = new int[SlotCount];
        for (var i = 0; i < slots.Length; i++)
        {
            slots[i] = (ModInfo.Guid + name + i).GetStableHashCode();
        }
        return slots;
    }

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

    // Once per ZRoutedRpc (new ZNet = new one). Instance set before Register: a throw never make me try again.
    internal static void EnsureRegistered()
    {
        var routed = ZRoutedRpc.instance;
        if (routed == null || ReferenceEquals(routed, _registeredOn))
        {
            return;
        }
        _registeredOn = routed;
        routed.Register<ZPackage>(ClearedRpc, OnCleared);
        routed.Register<ZPackage>(EngageRpc, OnEngage);
        routed.Register<ZPackage>(NewsRpc, OnNews);
    }

    private static bool KnownHere => _known && ReferenceEquals(_session, ZNet.instance);

    // Storms, meteors, spawns, map (hot path). Before Kall never; after Kall the cells the server said.
    internal static bool IsCleared(int cell)
    {
        return WorldState.KallDefeated && KnownHere && _cleared.Contains(cell);
    }

    internal static bool IsEngaged(int cell)
    {
        return KnownHere && _engaged.Contains(cell) || Holding(cell);
    }

    // Jotun killed in a cell toward its clearing, as the server said (0 when unknown).
    internal static int KillsIn(int cell)
    {
        return KnownHere && _kills.TryGetValue(cell, out var n) ? n : 0;
    }

    private static bool Holding(int cell)
    {
        return LocalHold.TryGetValue(cell, out var until) && Time.unscaledTime < until;
    }

    // After Kall: may this game burst the cell now (entered again, not cleared, not engaged)? The server's lists must
    // be here (else a cleared or engaged cell could spawn).
    internal static bool MayBurst(int cell)
    {
        return KnownHere && !_cleared.Contains(cell) && !_engaged.Contains(cell) && !Holding(cell);
    }

    // AreaSpawns after a burst: engage the cells that got Jotun or have some loaded (full: nothing to top up); others
    // try again RetrySeconds later.
    internal static void AfterBurst(List<int> cells, HashSet<int> spawnedInto)
    {
        var engage = new List<int>();
        var now = Time.unscaledTime;
        foreach (var cell in cells)
        {
            if (spawnedInto.Contains(cell) || TaggedNear(cell))
            {
                engage.Add(cell);
                LocalHold[cell] = now + LocalHoldSeconds;
            }
            else
            {
                LocalHold[cell] = now + RetrySeconds;
            }
        }
        SendEngage(engage);
    }

    private static bool TaggedNear(int cell)
    {
        foreach (var ch in Character.GetAllCharacters())
        {
            var nview = ch != null ? ch.m_nview : null;
            var zdo = nview != null ? nview.GetZDO() : null;
            if (zdo != null && !ch.IsDead() && zdo.GetInt(TagKey, NoTag) == cell)
            {
                return true;
            }
        }
        return false;
    }

    private static void SendEngage(List<int> cells)
    {
        var routed = ZRoutedRpc.instance;
        if (cells.Count == 0 || routed == null)
        {
            return;
        }
        EnsureRegistered();
        // No target = the server (or this game when it is the server).
        routed.InvokeRoutedRPC(EngageRpc, CellList(cells));
    }

    // ServerWorld tick (server).
    internal static void ServerUpdate()
    {
        if (!WorldState.KallDefeated)
        {
            // Players in the world without Kall (not the load moments before the keys and players are there).
            if (ZoneSystem.instance != null && (Player.m_localPlayer != null || ZNet.instance.GetPeerConnections() > 0))
            {
                _sawNoKall = true;
            }
            if (_scanned)
            {
                // Admin took the key away: back to before Kall.
                ResetServer();
            }
            return;
        }
        var now = Time.unscaledTime;
        if (!_scanned)
        {
            ScanMarks();
            _scanned = true;
            _dirty = true;
            _sendAt = 0f;
            var around = 0;
            if (_sawNoKall)
            {
                // Kall fell during this session: the areas around the players are in the fight, not entered again.
                around = EngageAroundPlayers();
            }
            Log.Info($"Kall is defeated: {ServerCleared.Count} Deep North area(s) are cleared, {Kills.Count} have Jotun "
                     + "defeated toward their clearing"
                     + (around > 0 ? $", {around} around the players wait to be defeated" : "")
                     + ". The others spawn their Jotun again each time a player enters them, until cleared.");
        }
        if (now >= _nextRelease)
        {
            _nextRelease = now + ReleaseCheckSeconds;
            ReleaseFar();
        }
        if (_dirty && now >= _sendAt)
        {
            _dirty = false;
            Broadcast();
        }
    }

    // Kall fell now: engage the cell under every player in the Deep North (the burst rule) and the cells of the area
    // Jotun loaded around them (5 x 5 zones): the area of the fight. A cell nearby that nobody entered and that has no
    // Jotun is left alone: it burst when entered.
    private static int EngageAroundPlayers()
    {
        var rules = ServerRules.Current;
        var zdos = ZDOMan.instance;
        if (rules == null || rules.IsPending || zdos == null)
        {
            return 0;
        }
        var seed = WorldState.Seed;
        var coverage = rules.Coverage(WorldState.Stage);
        var area = new SimulationDistance(SimulationDistance.OriginalNear, 0, true);
        var n = 0;
        foreach (var player in ZNet.instance.GetAllCharacterZDOS())
        {
            var p = player.GetPosition();
            if (WorldGenerator.IsDeepnorth(p.x, p.z) && Engage(Cells.At(seed, p.x, p.z), seed, coverage))
            {
                n++;
            }
            TempZdos.Clear();
            zdos.FindSectorObjects(ZoneSystem.GetZone(p), area, TempZdos);
            foreach (var zdo in TempZdos)
            {
                var tag = Hostility.IsArmy(zdo.GetPrefab()) ? zdo.GetInt(TagKey, NoTag) : NoTag;
                if (tag != NoTag && Engage(tag, seed, coverage))
                {
                    n++;
                }
            }
        }
        TempZdos.Clear();
        return n;
    }

    private static bool Engage(int cell, int seed, float coverage)
    {
        if (ServerCleared.Contains(cell) || ServerEngaged.ContainsKey(cell) || !Cells.IsAwake(seed, cell, coverage))
        {
            return false;
        }
        ServerEngaged[cell] = SeedOf(cell);
        return true;
    }

    // Seed distance at which a cell left every player's loaded range: the cell's reach plus the loaded zones around a
    // player (near simulation distance, from the player's zone corner).
    internal static float ReleaseDistance()
    {
        var near = SimulationDistance.OriginalNear;
        var net = ZNet.instance;
        if (net != null)
        {
            near = Mathf.Max(near, net.m_simulationDistance.NearSimulationDistance);
        }
        var zone = ZoneSystem.instance != null ? ZoneSystem.instance.m_zoneSize : 64f;
        return CellReach + (near + 1) * zone * 1.4143f;
    }

    private static Vector2 SeedOf(int cell)
    {
        Cells.FromId(cell, out var i, out var j);
        Cells.SeedPoint(WorldState.Seed, i, j, out var x, out var z);
        return new Vector2(x, z);
    }

    // Server: release engaged cells no player is near any more. Player = character, or the reference position (a dead
    // player waiting to respawn has no character; the game put the reference at the bed or the spawn point).
    private static void ReleaseFar()
    {
        if (ServerEngaged.Count == 0)
        {
            return;
        }
        var net = ZNet.instance;
        TempPlaces.Clear();
        foreach (var zdo in net.GetAllCharacterZDOS())
        {
            TempPlaces.Add(zdo.GetPosition());
        }
        foreach (var peer in net.GetPeers())
        {
            if (peer != null && peer.IsReady())
            {
                TempPlaces.Add(peer.GetRefPos());
            }
        }
        if (!net.IsDedicated())
        {
            TempPlaces.Add(net.GetReferencePosition());
        }
        var release = ReleaseDistance();
        var r2 = release * release;
        List<int> gone = null;
        foreach (var pair in ServerEngaged)
        {
            var near = false;
            foreach (var pos in TempPlaces)
            {
                var dx = pos.x - pair.Value.x;
                var dz = pos.z - pair.Value.y;
                if (dx * dx + dz * dz <= r2)
                {
                    near = true;
                    break;
                }
            }
            if (!near)
            {
                (gone ??= new List<int>()).Add(pair.Key);
            }
        }
        if (gone == null)
        {
            return;
        }
        foreach (var cell in gone)
        {
            ServerEngaged.Remove(cell);
        }
        _dirty = true;
    }

    // Cleared cells and kill progress remembered on every zone control the server hold.
    private static void ScanMarks()
    {
        foreach (var zdo in Stones.AllZdos(ZoneCtrlName))
        {
            foreach (var slot in ClearedSlots)
            {
                var cell = zdo.GetInt(slot, NoTag);
                if (cell != NoTag)
                {
                    ServerCleared.Add(cell);
                }
            }
            for (var i = 0; i < KillSlots.Length; i++)
            {
                var v = zdo.GetLong(KillSlots[i], 0L);
                if (v == 0L)
                {
                    continue;
                }
                Unpack(v, out var cell, out var count);
                if (!Kills.TryGetValue(cell, out var have) || count > have)
                {
                    Kills[cell] = count;
                    KillSlotOf[cell] = new KillSlot { Zdo = zdo, Id = zdo.m_uid, Slot = i };
                }
            }
        }
        foreach (var cell in ServerCleared)
        {
            Kills.Remove(cell);
            KillSlotOf.Remove(cell);
        }
    }

    private static string ZoneCtrlName
    {
        get
        {
            var zs = ZoneSystem.instance;
            return zs != null && zs.m_zoneCtrlPrefab != null ? zs.m_zoneCtrlPrefab.name : "_ZoneCtrl";
        }
    }

    // Nobody else write a zone control nobody owns (or the server owns); a game that load its zone later get it from
    // the server. One a player own: its next write would replace mine.
    private static bool Writable(ZDO zdo) => !zdo.HasOwner() || zdo.IsOwner();

    // Pure (self test): kill slot value.
    internal static long Pack(int cell, int count) => ((long)cell << 32) | (uint)count;

    internal static void Unpack(long value, out int cell, out int count)
    {
        cell = (int)(value >> 32);
        count = (int)(value & 0xFFFFFFFFL);
    }

    // Server: keep a cleared cell in the world, on a writable zone control with a free slot.
    private static void WriteMark(int cell)
    {
        foreach (var zdo in Stones.AllZdos(ZoneCtrlName))
        {
            if (!Writable(zdo))
            {
                continue;
            }
            var free = -1;
            for (var i = 0; i < ClearedSlots.Length; i++)
            {
                var v = zdo.GetInt(ClearedSlots[i], NoTag);
                if (v == cell)
                {
                    return;
                }
                if (v == NoTag && free < 0)
                {
                    free = i;
                }
            }
            if (free >= 0)
            {
                zdo.Set(ClearedSlots[free], cell);
                return;
            }
        }
        Log.Warning("Found no free zone control to remember a cleared Deep North area; it is cleared until the server restarts.");
    }

    // Server: keep a cell's kill count in the world: its slot again when still writable, else a free one.
    private static void WriteKills(int cell, int count)
    {
        var value = Pack(cell, count);
        if (KillSlotOf.TryGetValue(cell, out var at) && SlotUsable(at))
        {
            at.Zdo.Set(KillSlots[at.Slot], value);
            return;
        }
        foreach (var zdo in Stones.AllZdos(ZoneCtrlName))
        {
            if (!Writable(zdo))
            {
                continue;
            }
            for (var i = 0; i < KillSlots.Length; i++)
            {
                if (zdo.GetLong(KillSlots[i], 0L) == 0L)
                {
                    zdo.Set(KillSlots[i], value);
                    KillSlotOf[cell] = new KillSlot { Zdo = zdo, Id = zdo.m_uid, Slot = i };
                    return;
                }
            }
        }
        if (!_slotWarned)
        {
            _slotWarned = true;
            Log.Warning("Found no free zone control to remember Jotun kills in a Deep North area; they count until the server restarts.");
        }
    }

    // The zone control me wrote may be gone (admin forcedelete, world tools) and its object reused by the pool for
    // another ZDO: same id, same live object, still a zone control, still writable.
    private static bool SlotUsable(KillSlot at)
    {
        var zdos = ZDOMan.instance;
        var zdo = at.Zdo;
        return zdo != null && zdos != null && zdo.m_uid == at.Id && ReferenceEquals(zdos.GetZDO(at.Id), zdo)
               && zdo.GetPrefab() == ZoneCtrlName.GetStableHashCode() && Writable(zdo);
    }

    // Cleared: its kill slot is free again (when still writable; a stale one lose to the cleared mark at load).
    private static void DropKills(int cell)
    {
        if (KillSlotOf.TryGetValue(cell, out var at) && SlotUsable(at) && at.Zdo.GetLong(KillSlots[at.Slot], 0L) != 0L)
        {
            Unpack(at.Zdo.GetLong(KillSlots[at.Slot], 0L), out var c, out _);
            if (c == cell)
            {
                at.Zdo.Set(KillSlots[at.Slot], 0L);
            }
        }
        KillSlotOf.Remove(cell);
    }

    // ServerWorld destroy delegate (server). Cheap: prefab compare first. A Jotun-army ZDO destroyed = a death (they
    // are saved creatures: unloading never destroy them), except event and day creatures (raid Jotun walk away and
    // vanish when their raid ends: BaseAI.MoveAwayAndDespawn). Each ZDOID once: a stale update can bring a dead ZDO back
    // on the server and destroy it again (ZDOMan.RPC_ZDOData).
    internal static void OnDestroyed(ZDO zdo)
    {
        if (!_scanned || !Hostility.IsArmy(zdo.GetPrefab()))
        {
            return;
        }
        if (zdo.GetBool(ZDOVars.s_eventCreature) || zdo.GetBool(ZDOVars.s_despawnInDay) || !CountedKills.Add(zdo.m_uid))
        {
            return;
        }
        var rules = ServerRules.Current;
        if (!WorldState.Awake(rules))
        {
            return;
        }
        var p = zdo.GetPosition();
        if (!WorldGenerator.IsDeepnorth(p.x, p.z))
        {
            return;
        }
        var cell = WorldState.CellAt(p);
        if (ServerCleared.Contains(cell) || !WorldState.IsAwakeCell(cell, rules))
        {
            return;
        }
        Kills.TryGetValue(cell, out var before);
        var now = before + 1;
        var target = rules.KillTarget(WorldState.Seed, cell);
        var half = (target + 1) / 2;
        Cells.FromId(cell, out var i, out var j);
        if (now >= target)
        {
            ServerCleared.Add(cell);
            ServerEngaged.Remove(cell);
            Kills.Remove(cell);
            WriteMark(cell);
            DropKills(cell);
            Log.Info($"{now} Jotun defeated in Deep North area {i},{j}: the area is cleared for good.");
            News(NewsCleared, cell, p);
        }
        else
        {
            Kills[cell] = now;
            WriteKills(cell, now);
            if (before < half && now >= half)
            {
                Log.Info($"{now} of {target} Jotun defeated in Deep North area {i},{j}: the Jotun army there is weakening.");
                News(NewsWeakening, cell, p);
            }
        }
        // A send already waiting (an engage: at once) is never pushed back.
        if (!_dirty)
        {
            _sendAt = Time.unscaledTime + SendDelay;
        }
        _dirty = true;
    }

    // Server: a game burst these cells (or found them full). Engaged until no player is near.
    private static void OnEngage(long sender, ZPackage pkg)
    {
        try
        {
            var net = ZNet.instance;
            if (!_active || net == null || !net.IsServer() || !WorldState.KallDefeated || !TryReadCells(pkg, out var cells))
            {
                return;
            }
            foreach (var cell in cells)
            {
                if (!ServerCleared.Contains(cell))
                {
                    ServerEngaged[cell] = SeedOf(cell);
                }
            }
            _dirty = true;
            _sendAt = 0f;
        }
        catch (Exception e)
        {
            PatchGuard.Report("HeldCells.OnEngage", e);
        }
    }

    private static ZPackage CellList(ICollection<int> cells)
    {
        var pkg = new ZPackage();
        pkg.Write(Layout);
        pkg.Write(cells.Count);
        foreach (var c in cells)
        {
            pkg.Write(c);
        }
        return pkg;
    }

    private static ZPackage Package()
    {
        var cleared = new List<int>(ServerCleared);
        cleared.Sort();
        var engaged = new List<int>(ServerEngaged.Keys);
        engaged.Sort();
        return Package(cleared, engaged, Kills);
    }

    private static ZPackage Package(ICollection<int> cleared, ICollection<int> engaged, IDictionary<int, int> kills)
    {
        var pkg = new ZPackage();
        pkg.Write(Layout);
        pkg.Write(cleared.Count);
        foreach (var c in cleared)
        {
            pkg.Write(c);
        }
        pkg.Write(engaged.Count);
        foreach (var c in engaged)
        {
            pkg.Write(c);
        }
        pkg.Write(kills.Count);
        foreach (var pair in kills)
        {
            pkg.Write(pair.Key);
            pkg.Write(pair.Value);
        }
        return pkg;
    }

    private static void Broadcast()
    {
        var routed = ZRoutedRpc.instance;
        if (routed == null)
        {
            return;
        }
        EnsureRegistered();
        // Everybody: this game run the handler too (single player, host see the same lists).
        routed.InvokeRoutedRPC(ZRoutedRpc.Everybody, ClearedRpc, Package());
    }

    // ServerRules.OnRequest (server): a player who joins after Kall, or turns me back on, get the lists at once.
    internal static void SendTo(ZNetPeer peer)
    {
        var routed = ZRoutedRpc.instance;
        if (!_active || !_scanned || routed == null || peer == null || !WorldState.KallDefeated)
        {
            return;
        }
        routed.InvokeRoutedRPC(peer.m_uid, ClearedRpc, Package());
    }

    private static void OnCleared(long sender, ZPackage pkg)
    {
        try
        {
            if (!_active)
            {
                return;
            }
            if (!TryRead(pkg, out var cleared, out var engaged, out var kills))
            {
                Log.Warning("The server sent Deep North area lists this version cannot read; they are ignored.");
                return;
            }
            if (!KnownHere || !_cleared.SetEquals(cleared))
            {
                ClearedVersion++;
            }
            _cleared = cleared;
            _engaged = engaged;
            _kills = kills;
            _known = true;
            _session = ZNet.instance;
        }
        catch (Exception e)
        {
            PatchGuard.Report("HeldCells.OnCleared", e);
        }
    }

    // Pure-ish (self test): read the lists. Unknown layout or junk = false.
    internal static bool TryRead(ZPackage pkg, out HashSet<int> cleared, out HashSet<int> engaged,
        out Dictionary<int, int> kills)
    {
        cleared = null;
        engaged = null;
        kills = null;
        try
        {
            if (pkg == null || pkg.ReadInt() != Layout || !ReadSet(pkg, out var c) || !ReadSet(pkg, out var e))
            {
                return false;
            }
            var count = pkg.ReadInt();
            if (count < 0 || count > MaxCells)
            {
                return false;
            }
            var k = new Dictionary<int, int>();
            for (var i = 0; i < count; i++)
            {
                var cell = pkg.ReadInt();
                k[cell] = Math.Max(0, pkg.ReadInt());
            }
            cleared = c;
            engaged = e;
            kills = k;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryReadCells(ZPackage pkg, out HashSet<int> cells)
    {
        cells = null;
        try
        {
            return pkg != null && pkg.ReadInt() == Layout && ReadSet(pkg, out cells);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool ReadSet(ZPackage pkg, out HashSet<int> set)
    {
        set = null;
        var count = pkg.ReadInt();
        if (count < 0 || count > MaxCells)
        {
            return false;
        }
        var s = new HashSet<int>();
        for (var i = 0; i < count; i++)
        {
            s.Add(pkg.ReadInt());
        }
        set = s;
        return true;
    }

    // Server: tell everybody (each game decide if its player is there).
    private static void News(int kind, int cell, Vector3 at)
    {
        var routed = ZRoutedRpc.instance;
        if (routed == null)
        {
            return;
        }
        EnsureRegistered();
        routed.InvokeRoutedRPC(ZRoutedRpc.Everybody, NewsRpc, NewsPackage(kind, cell, at));
    }

    internal static ZPackage NewsPackage(int kind, int cell, Vector3 at)
    {
        var pkg = new ZPackage();
        pkg.Write(Layout);
        pkg.Write(kind);
        pkg.Write(cell);
        pkg.Write(at.x);
        pkg.Write(at.z);
        return pkg;
    }

    // Pure-ish (self test).
    internal static bool TryReadNews(ZPackage pkg, out int kind, out int cell, out Vector3 at)
    {
        kind = 0;
        cell = NoTag;
        at = Vector3.zero;
        try
        {
            if (pkg == null || pkg.ReadInt() != Layout)
            {
                return false;
            }
            kind = pkg.ReadInt();
            cell = pkg.ReadInt();
            var x = pkg.ReadSingle();
            var z = pkg.ReadSingle();
            at = new Vector3(x, 0f, z);
            return kind == NewsWeakening || kind == NewsCleared;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // The local player stand in the cell, or near the kill: weakening top left, cleared = vanilla "The Jotun Retreat".
    private static void OnNews(long sender, ZPackage pkg)
    {
        try
        {
            if (!_active || !TryReadNews(pkg, out var kind, out var cell, out var at))
            {
                return;
            }
            var shown = false;
            var player = Player.m_localPlayer;
            var hud = MessageHud.instance;
            if (player != null && hud != null)
            {
                var pos = player.transform.position;
                var dx = pos.x - at.x;
                var dz = pos.z - at.z;
                if (WorldState.CellAt(pos) == cell || dx * dx + dz * dz <= NewsRange * NewsRange)
                {
                    shown = true;
                    if (kind == NewsCleared)
                    {
                        hud.ShowMessage(MessageHud.MessageType.Center, ClearedText);
                    }
                    else
                    {
                        hud.ShowMessage(MessageHud.MessageType.TopLeft, WeakeningText);
                    }
                }
            }
#if DEBUG
            _lastNewsKind = kind;
            _lastNewsCell = cell;
            _lastNewsShown = shown;
#endif
        }
        catch (Exception e)
        {
            PatchGuard.Report("HeldCells.OnNews", e);
        }
    }

    private static void ResetServer()
    {
        ServerCleared.Clear();
        ServerEngaged.Clear();
        Kills.Clear();
        KillSlotOf.Clear();
        CountedKills.Clear();
        _scanned = false;
        _dirty = false;
    }

    // Admin command: what this game knows of the area at a position (after Kall).
    internal static string DescribeArea(Vector3 position, AwakeningRules rules)
    {
        if (!WorldState.KallDefeated || !WorldState.Awake(rules) || !WorldGenerator.IsDeepnorth(position.x, position.z))
        {
            return "";
        }
        var cell = WorldState.CellAt(position);
        Cells.FromId(cell, out var i, out var j);
        if (!WorldState.IsAwakeCell(cell, rules))
        {
            return $"; area {i},{j} here is not invaded";
        }
        if (!KnownHere)
        {
            return $"; area {i},{j} here: waiting for the server's lists";
        }
        if (_cleared.Contains(cell))
        {
            return $"; area {i},{j} here is cleared";
        }
        return $"; area {i},{j} here: {KillsIn(cell)} of {rules.KillTarget(WorldState.Seed, cell)} Jotun defeated, "
               + (IsEngaged(cell) ? "waits to be defeated" : "spawns its Jotun when entered");
    }

    // WorldState.Refresh (every game): no Kall (or the key taken away) = lists of an earlier Kall mean nothing. Forget
    // them: after a new Kall no burst until the server's new lists come.
    internal static void ForgetIfNoKall()
    {
        if (_known && !WorldState.KallDefeated)
        {
            ForgetLists();
        }
    }

    private static void ForgetLists()
    {
        ClearedVersion++;
        _cleared = new HashSet<int>();
        _engaged = new HashSet<int>();
        _kills = new Dictionary<int, int>();
        _known = false;
        _session = null;
        LocalHold.Clear();
    }

    // World end, feature off.
    internal static void Clear()
    {
        ResetServer();
        _sawNoKall = false;
        _slotWarned = false;
        ForgetLists();
    }

#if DEBUG
    private static int _lastNewsKind;
    private static int _lastNewsCell;
    private static bool _lastNewsShown;

    internal static void TestRescan() => ResetServer();

    internal static bool TestServerEngaged(int cell) => ServerEngaged.ContainsKey(cell);

    internal static int TestKills(int cell) => Kills.TryGetValue(cell, out var n) ? n : 0;

    // Self test: last news this game got (kind 0 = none since the reset), and whether its player saw it.
    internal static void TestLastNews(out int kind, out int cell, out bool shown)
    {
        kind = _lastNewsKind;
        cell = _lastNewsCell;
        shown = _lastNewsShown;
    }

    internal static void TestResetNews()
    {
        _lastNewsKind = 0;
        _lastNewsCell = NoTag;
        _lastNewsShown = false;
    }

    // Self test: forget every engagement (as if every player had left), server and here.
    internal static void TestClearEngaged()
    {
        ServerEngaged.Clear();
        LocalHold.Clear();
        _engaged = new HashSet<int>();
        _dirty = true;
        _sendAt = 0f;
    }

    // Self test: Kall seen at load (no engage around the players), or seen fall during the session.
    internal static void TestSawNoKall(bool value) => _sawNoKall = value;

    internal static bool TestMarkedAnywhere(int cell)
    {
        foreach (var zdo in Stones.AllZdos(ZoneCtrlName))
        {
            foreach (var slot in ClearedSlots)
            {
                if (zdo.GetInt(slot, NoTag) == cell)
                {
                    return true;
                }
            }
        }
        return false;
    }

    // Self test: kill slots of a cell anywhere in the world.
    internal static int TestKillSlots(int cell)
    {
        var n = 0;
        foreach (var zdo in Stones.AllZdos(ZoneCtrlName))
        {
            foreach (var slot in KillSlots)
            {
                var v = zdo.GetLong(slot, 0L);
                if (v != 0L)
                {
                    Unpack(v, out var c, out _);
                    if (c == cell)
                    {
                        n++;
                    }
                }
            }
        }
        return n;
    }

    // Self test: remove a cell from every zone control, cleared and kill slots (world as before the test).
    internal static void TestUnmarkEverywhere(int cell)
    {
        foreach (var zdo in Stones.AllZdos(ZoneCtrlName))
        {
            foreach (var slot in ClearedSlots)
            {
                if (zdo.GetInt(slot, NoTag) == cell)
                {
                    zdo.Set(slot, NoTag);
                }
            }
            foreach (var slot in KillSlots)
            {
                var v = zdo.GetLong(slot, 0L);
                if (v != 0L)
                {
                    Unpack(v, out var c, out _);
                    if (c == cell)
                    {
                        zdo.Set(slot, 0L);
                    }
                }
            }
        }
    }

    internal static ZPackage TestPackage(ICollection<int> cleared, ICollection<int> engaged, IDictionary<int, int> kills)
        => Package(cleared, engaged, kills);
#endif
}
