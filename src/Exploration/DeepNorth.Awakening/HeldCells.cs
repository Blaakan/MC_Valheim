using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.DeepNorthAwakeningMod;

// Me = the areas after Kall (design 2.6, user rule). Before Kall nothing here matter: every awake cell spawn and storm.
// After Kall an area that is not cleared spawn its Jotun each time a player enter it again, then is engaged ("to be
// defeated"): no more spawns while players are near it. Kill every Jotun it spawned = cleared for good (no spawn, no
// storm). Leave without clearing = next time a player enter it, its Jotun spawn again.
//   Tag:      area Jotun carry their cell in ZDO int TagKey (Character.Awake postfix while a Jotun entry spawn).
//   Burst:    AreaSpawns, for the cell under a player in the zone (never a cell the spawn ring cannot reach), each
//             Jotun kind topped up to its cap counting the cell's own living Jotun (LivingOf, wherever they roam).
//             Engaged when it got Jotun or has living ones; nothing spawned and none living = try again RetrySeconds
//             later.
//   Engaged:  server-authoritative: a game that burst send routed "<guid>.Engage"(cells); the server keep the cell until
//             no player (character or reference position) is within ReleaseDistance of its seed point: every point of
//             the cell (CellReach) plus the loaded zones around a player, so the cell left everybody's loaded range.
//             The game itself hold the cell LocalHoldSeconds meanwhile (network delay). Kall killed during the session:
//             the server engage at once the cell under every player and the cells around them that still have Jotun
//             (no refill in the area of the fight; a cell nobody entered and with no Jotun still burst when entered).
//   Living:   server track tagged army ZDOs (cell and kind): full scan at Kall or at load with Kall, rescans RescanAt s
//             after (a client that spawned in the moment before the Kall key reached it); then every death of a tagged
//             one and every engage recount the cell around its seed point (RecountZones), so Jotun of later bursts
//             count too. A cell whose death recount find no living Jotun died out; once settled (last rescan) it is
//             cleared.
//   Cleared:  the server write it into ClearedSlots of a zone control nobody owns (or the server owns): persistent
//             vanilla ZDOs, read back from every _ZoneCtrl at load. Sent with the engaged cells and living counts per
//             kind to everybody on change ("<guid>.Cleared") and to a player after each rules request. A cell that
//             become cleared while the local player stand in it = vanilla "The Jotun Retreat".
// Routed rpcs have no unregister: handlers check _active. Vanilla peers ignore unknown routed rpcs.
internal static class HeldCells
{
    internal const string ClearedRpc = ModInfo.Guid + ".Cleared";
    internal const string EngageRpc = ModInfo.Guid + ".Engage";
    internal const int Layout = 1;
    internal const int MaxCells = 100000;
    internal const float SendDelay = 1f;
    internal const float RecountDelay = 1f;
    internal const float EngageRecountDelay = 4f;
    internal const float ReleaseCheckSeconds = 2f;
    internal const float LocalHoldSeconds = 10f;
    internal const float RetrySeconds = 5f;
    internal const int RecountZones = 9;
    internal const string ClearedText = "$fimbulvinterorb_destroyed";

    // From the cell's seed point every point of the cell is within sqrt(2) x 340 m = 481 m (400 m squares, seed moved
    // up to 35 %, nearest seed wins). A player inside a cell never release it.
    internal const float CellReach = 482f;

    internal static readonly int TagKey = (ModInfo.Guid + ".Cell").GetStableHashCode();
    internal const int NoTag = int.MinValue;

    // Zone-control slots holding cleared cells (any zone control: the cell id is the value).
    internal const int ClearedSlotCount = 8;
    internal static readonly int[] ClearedSlots = MakeSlots();

    private static bool _active;
    private static ZRoutedRpc _registeredOn;

    // What this game was told / did.
    private static HashSet<int> _cleared = new HashSet<int>();
    private static HashSet<int> _engaged = new HashSet<int>();
    private static Dictionary<int, int[]> _living = new Dictionary<int, int[]>();
    private static bool _known;
    private static ZNet _session;
    private static readonly Dictionary<int, float> LocalHold = new Dictionary<int, float>();

    // Server tracking.
    private static readonly Dictionary<int, HashSet<ZDOID>> ByCell = new Dictionary<int, HashSet<ZDOID>>();
    private static readonly Dictionary<ZDOID, int> CellOf = new Dictionary<ZDOID, int>();
    private static readonly Dictionary<ZDOID, int> KindOf = new Dictionary<ZDOID, int>();
    private static readonly List<Vector3> TempPlaces = new List<Vector3>();
    private static readonly HashSet<ZDOID> Dead = new HashSet<ZDOID>();
    private static readonly HashSet<int> DiedOut = new HashSet<int>();
    private static readonly HashSet<int> ServerCleared = new HashSet<int>();
    private static readonly Dictionary<int, Vector2> ServerEngaged = new Dictionary<int, Vector2>();
    private static readonly HashSet<int> DeathRecount = new HashSet<int>();
    private static readonly HashSet<int> TrackRecount = new HashSet<int>();
    private static readonly List<ZDO> TempZdos = new List<ZDO>();
    private static bool _scanned;
    private static bool _sawNoKall;
    private static bool _dirty;
    private static float _sendAt;
    private static float _scannedAt;
    private static float _recountAt;
    private static float _nextRelease;
    private static int _rescans;

    // Late-spawn rescans, seconds after the first scan. Clears wait for the last one.
    private static readonly float[] RescanAt = { 5f, 15f, 30f };

    internal static bool Scanned => _scanned;

    internal static int TrackedCreatures => CellOf.Count;

    internal static int HeldCount => ByCell.Count;

    internal static int ClearedCount => ServerCleared.Count;

    internal static int EngagedCount => ServerEngaged.Count;

    // Bump each time the cleared list this game knows change (map overlay repaint).
    internal static int ClearedVersion { get; private set; }

    private static int[] MakeSlots()
    {
        var slots = new int[ClearedSlotCount];
        for (var i = 0; i < slots.Length; i++)
        {
            slots[i] = (ModInfo.Guid + ".Cleared" + i).GetStableHashCode();
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
    }

    private static bool KnownHere => _known && ReferenceEquals(_session, ZNet.instance);

    // Storms, meteors, spawns (hot path). Before Kall never; after Kall the cells the server said.
    internal static bool IsCleared(int cell)
    {
        return WorldState.KallDefeated && KnownHere && _cleared.Contains(cell);
    }

    internal static bool IsEngaged(int cell)
    {
        return KnownHere && _engaged.Contains(cell) || Holding(cell);
    }

    // Living area Jotun of a cell the server counted (0 when unknown).
    internal static int Living(int cell)
    {
        if (!KnownHere || !_living.TryGetValue(cell, out var kinds))
        {
            return 0;
        }
        var n = 0;
        foreach (var k in kinds)
        {
            n += k;
        }
        return n;
    }

    // Living area Jotun of one kind (Hostility.Army index) in a cell (0 when unknown).
    internal static int LivingOf(int cell, int kind)
    {
        return KnownHere && kind >= 0 && kind < Hostility.Army.Length && _living.TryGetValue(cell, out var kinds)
            ? kinds[kind]
            : 0;
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

    // AreaSpawns after a burst: engage the cells that got Jotun or already have some (full: nothing to top up); others
    // try again RetrySeconds later.
    internal static void AfterBurst(List<int> cells, HashSet<int> spawnedInto)
    {
        var engage = new List<int>();
        var now = Time.unscaledTime;
        foreach (var cell in cells)
        {
            if (spawnedInto.Contains(cell) || Living(cell) > 0 || TaggedNear(cell))
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
            ScanLiving();
            ScanMarks();
            _scanned = true;
            _scannedAt = now;
            _rescans = 0;
            _dirty = true;
            _sendAt = 0f;
            var around = 0;
            if (_sawNoKall)
            {
                // Kall fell during this session: the areas around the players are in the fight, not loaded again.
                around = EngageAroundPlayers();
            }
            Log.Info($"Kall is defeated: {CellOf.Count} Jotun spawned by the Deep North areas still hold {ByCell.Count} "
                     + $"area(s); {ServerCleared.Count} area(s) are cleared"
                     + (around > 0 ? $"; {around} area(s) around the players wait to be defeated" : "")
                     + ". The others spawn their Jotun again each time a player enters them, until cleared.");
        }
        else if (_rescans < RescanAt.Length && now - _scannedAt >= RescanAt[_rescans])
        {
            _rescans++;
            ScanLiving();
            _dirty = true;
        }
        if ((DeathRecount.Count > 0 || TrackRecount.Count > 0) && now >= _recountAt)
        {
            Recount();
        }
        if (Settled && DiedOut.Count > 0)
        {
            foreach (var cell in DiedOut)
            {
                if (!ByCell.ContainsKey(cell) && ServerCleared.Add(cell))
                {
                    WriteMark(cell);
                    ServerEngaged.Remove(cell);
                    Cells.FromId(cell, out var i, out var j);
                    Log.Info($"The last Jotun of Deep North area {i},{j} died: the area is cleared for good.");
                    _dirty = true;
                }
            }
            DiedOut.Clear();
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

    private static bool Settled => _rescans >= RescanAt.Length;

    // Kall fell now: engage the cell under every player in the Deep North (the burst rule) and the cells around them
    // (zone samples like the spawn runner's) that still have Jotun: the area of the fight. A cell nearby that nobody
    // entered and that has no Jotun is left alone: it burst when entered.
    private static int EngageAroundPlayers()
    {
        var rules = ServerRules.Current;
        if (rules == null || rules.IsPending)
        {
            return 0;
        }
        var seed = WorldState.Seed;
        var coverage = rules.Coverage(WorldState.Stage);
        var n = 0;
        foreach (var zdo in ZNet.instance.GetAllCharacterZDOS())
        {
            var p = zdo.GetPosition();
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dz = -1; dz <= 1; dz++)
                {
                    var under = dx == 0 && dz == 0;
                    if (under && !WorldGenerator.IsDeepnorth(p.x, p.z))
                    {
                        continue;
                    }
                    var cell = Cells.At(seed, p.x + dx * AreaSpawns.ZoneReach, p.z + dz * AreaSpawns.ZoneReach);
                    if ((under || ByCell.ContainsKey(cell)) && !ServerCleared.Contains(cell)
                                                              && !ServerEngaged.ContainsKey(cell)
                                                              && Cells.IsAwake(seed, cell, coverage))
                    {
                        ServerEngaged[cell] = SeedOf(cell);
                        n++;
                    }
                }
            }
        }
        return n;
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

    // Track every tagged army ZDO not tracked yet (union: never forget one already seen die). Returns how many new.
    private static int ScanLiving()
    {
        var added = 0;
        foreach (var prefab in Hostility.Army)
        {
            foreach (var zdo in Stones.AllZdos(prefab))
            {
                if (TrackIfNew(zdo))
                {
                    added++;
                }
            }
        }
        return added;
    }

    private static bool TrackIfNew(ZDO zdo)
    {
        var tag = zdo.GetInt(TagKey, NoTag);
        if (tag == NoTag || CellOf.ContainsKey(zdo.m_uid) || Dead.Contains(zdo.m_uid))
        {
            return false;
        }
        Track(zdo.m_uid, tag, Hostility.ArmyIndex(zdo.GetPrefab()));
        DiedOut.Remove(tag);
        _dirty = true;
        return true;
    }

    // Cells whose Jotun died or were just spawned: count their living tagged Jotun around their seed point (19 x 19
    // zones: the cell and where its Jotun roam). A death cell with none left died out.
    private static void Recount()
    {
        var zdos = ZDOMan.instance;
        if (zdos == null)
        {
            return;
        }
        var area = new SimulationDistance(RecountZones, 0, true);
        var cells = new HashSet<int>(DeathRecount);
        cells.UnionWith(TrackRecount);
        foreach (var cell in cells)
        {
            var seed = SeedOf(cell);
            TempZdos.Clear();
            zdos.FindSectorObjects(ZoneSystem.GetZone(new Vector3(seed.x, 0f, seed.y)), area, TempZdos);
            foreach (var zdo in TempZdos)
            {
                if (Hostility.IsArmy(zdo.GetPrefab()) && zdo.GetInt(TagKey, NoTag) == cell)
                {
                    TrackIfNew(zdo);
                }
            }
        }
        TempZdos.Clear();
        foreach (var cell in DeathRecount)
        {
            if (!ByCell.ContainsKey(cell))
            {
                DiedOut.Add(cell);
            }
        }
        DeathRecount.Clear();
        TrackRecount.Clear();
        _dirty = true;
    }

    // Cleared cells remembered on every zone control the server hold.
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

    // Server: keep a cleared cell in the world, on a zone control nobody owns (or the server owns) with a free slot.
    // Nobody else write that ZDO meanwhile; a game that load its zone later get it from the server.
    private static void WriteMark(int cell)
    {
        foreach (var zdo in Stones.AllZdos(ZoneCtrlName))
        {
            if (zdo.HasOwner() && !zdo.IsOwner())
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

    private static void Track(ZDOID id, int cell, int kind)
    {
        CellOf[id] = cell;
        KindOf[id] = kind;
        if (!ByCell.TryGetValue(cell, out var set))
        {
            set = new HashSet<ZDOID>();
            ByCell[cell] = set;
        }
        set.Add(id);
    }

    // ServerWorld destroy delegate (server). Cheap: prefab compare first; a tagged army ZDO queue its cell's recount.
    internal static void OnDestroyed(ZDO zdo)
    {
        if (!_scanned || !Hostility.IsArmy(zdo.GetPrefab()))
        {
            return;
        }
        var tag = zdo.GetInt(TagKey, NoTag);
        if (tag == NoTag)
        {
            return;
        }
        // A stale ZDO update can bring a dead ZDOID back for a moment: a scan must not count it again.
        Dead.Add(zdo.m_uid);
        if (CellOf.TryGetValue(zdo.m_uid, out var cell))
        {
            CellOf.Remove(zdo.m_uid);
            KindOf.Remove(zdo.m_uid);
            if (ByCell.TryGetValue(cell, out var set))
            {
                set.Remove(zdo.m_uid);
                if (set.Count == 0)
                {
                    ByCell.Remove(cell);
                }
            }
        }
        DeathRecount.Add(tag);
        // Never sooner than an engage recount already waiting (its new ZDOs may still be on the way).
        _recountAt = Mathf.Max(_recountAt, Time.unscaledTime + RecountDelay);
        // A send already waiting (an engage: at once) is never pushed back.
        if (!_dirty)
        {
            _sendAt = Time.unscaledTime + SendDelay;
        }
        _dirty = true;
    }

    // Server: a game burst these cells (or found them full). Engaged until no player is near; count their Jotun soon.
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
                    TrackRecount.Add(cell);
                }
            }
            _recountAt = Mathf.Max(_recountAt, Time.unscaledTime + EngageRecountDelay);
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
        // Living per cell, one count per army kind (Hostility.Army order).
        var kinds = new int[Hostility.Army.Length];
        pkg.Write(ByCell.Count);
        foreach (var pair in ByCell)
        {
            Array.Clear(kinds, 0, kinds.Length);
            foreach (var id in pair.Value)
            {
                if (KindOf.TryGetValue(id, out var k) && k >= 0 && k < kinds.Length)
                {
                    kinds[k]++;
                }
            }
            pkg.Write(pair.Key);
            foreach (var n in kinds)
            {
                pkg.Write(n);
            }
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
            if (!TryRead(pkg, out var cleared, out var engaged, out var living))
            {
                Log.Warning("The server sent Deep North area lists this version cannot read; they are ignored.");
                return;
            }
            var first = !KnownHere;
            var old = _cleared;
            if (first || !old.SetEquals(cleared))
            {
                ClearedVersion++;
            }
            _cleared = cleared;
            _engaged = engaged;
            _living = living;
            _known = true;
            _session = ZNet.instance;
            if (!first)
            {
                AnnounceCleared(old, cleared);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("HeldCells.OnCleared", e);
        }
    }

    // Pure-ish (self test): read the lists. Unknown layout or junk = false.
    internal static bool TryRead(ZPackage pkg, out HashSet<int> cleared, out HashSet<int> engaged,
        out Dictionary<int, int[]> living)
    {
        cleared = null;
        engaged = null;
        living = null;
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
            var l = new Dictionary<int, int[]>();
            for (var i = 0; i < count; i++)
            {
                var cell = pkg.ReadInt();
                var kinds = new int[Hostility.Army.Length];
                for (var k = 0; k < kinds.Length; k++)
                {
                    kinds[k] = Math.Max(0, pkg.ReadInt());
                }
                l[cell] = kinds;
            }
            cleared = c;
            engaged = e;
            living = l;
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

    // Local player stand in a cell that just became cleared = vanilla "The Jotun Retreat".
    private static void AnnounceCleared(HashSet<int> old, HashSet<int> now)
    {
        var player = Player.m_localPlayer;
        var hud = MessageHud.instance;
        if (player == null || hud == null || player.InInterior())
        {
            return;
        }
        var rules = ServerRules.Current;
        var pos = player.transform.position;
        if (!WorldState.Awake(rules) || !WorldGenerator.IsDeepnorth(pos.x, pos.z))
        {
            return;
        }
        var cell = WorldState.CellAt(pos);
        if (now.Contains(cell) && !old.Contains(cell) && WorldState.IsAwakeCell(cell, rules))
        {
            hud.ShowMessage(MessageHud.MessageType.Center, ClearedText);
        }
    }

    private static void ResetServer()
    {
        ByCell.Clear();
        CellOf.Clear();
        KindOf.Clear();
        Dead.Clear();
        DiedOut.Clear();
        ServerCleared.Clear();
        ServerEngaged.Clear();
        DeathRecount.Clear();
        TrackRecount.Clear();
        _scanned = false;
        _dirty = false;
        _rescans = 0;
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
        var kinds = new List<string>();
        for (var k = 0; k < Hostility.Army.Length; k++)
        {
            kinds.Add($"{LivingOf(cell, k)} {Hostility.Army[k]}");
        }
        return $"; area {i},{j} here: {Living(cell)} of its Jotun alive ({string.Join(", ", kinds.ToArray())}), "
               + (IsEngaged(cell) ? "waits to be defeated" : "spawns its Jotun when entered");
    }

    // WorldState.Refresh (every game): no Kall (or the key taken away) = lists of an earlier Kall mean nothing. Forget
    // them: after a new Kall no burst until the server's new lists come.
    internal static void ForgetIfNoKall()
    {
        if (_known && !WorldState.KallDefeated)
        {
            ClearedVersion++;
            _cleared = new HashSet<int>();
            _engaged = new HashSet<int>();
            _living = new Dictionary<int, int[]>();
            _known = false;
            _session = null;
            LocalHold.Clear();
        }
    }

    // World end, feature off.
    internal static void Clear()
    {
        ResetServer();
        _sawNoKall = false;
        ClearedVersion++;
        _cleared = new HashSet<int>();
        _engaged = new HashSet<int>();
        _living = new Dictionary<int, int[]>();
        _known = false;
        _session = null;
        LocalHold.Clear();
    }

#if DEBUG
    internal static void TestRescan() => ResetServer();

    // Self test: skip the late-spawn rescans so a clear is decided at once.
    internal static void TestSettle()
    {
        _rescans = RescanAt.Length;
        _dirty = true;
    }

    internal static bool TestTracks(ZDOID id) => CellOf.ContainsKey(id);

    internal static bool TestServerEngaged(int cell) => ServerEngaged.ContainsKey(cell);

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

    internal static bool TestMarked(ZDO zdo, int cell)
    {
        foreach (var slot in ClearedSlots)
        {
            if (zdo.GetInt(slot, NoTag) == cell)
            {
                return true;
            }
        }
        return false;
    }

    internal static bool TestMarkedAnywhere(int cell)
    {
        foreach (var zdo in Stones.AllZdos(ZoneCtrlName))
        {
            if (TestMarked(zdo, cell))
            {
                return true;
            }
        }
        return false;
    }

    // Self test: remove a cell from every zone control (world as before the test).
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
        }
    }

    internal static ZPackage TestPackage(IEnumerable<int> cleared, IEnumerable<int> engaged,
        IDictionary<int, int[]> living)
    {
        var c = new List<int>(cleared);
        var e = new List<int>(engaged);
        var pkg = new ZPackage();
        pkg.Write(Layout);
        pkg.Write(c.Count);
        foreach (var x in c)
        {
            pkg.Write(x);
        }
        pkg.Write(e.Count);
        foreach (var x in e)
        {
            pkg.Write(x);
        }
        pkg.Write(living.Count);
        foreach (var pair in living)
        {
            pkg.Write(pair.Key);
            foreach (var n in pair.Value)
            {
                pkg.Write(n);
            }
        }
        return pkg;
    }
#endif
}
