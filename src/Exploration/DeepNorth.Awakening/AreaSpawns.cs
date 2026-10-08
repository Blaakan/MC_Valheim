using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.DeepNorthAwakeningMod;

internal enum EntryKind : byte
{
    Jotun,
    Nature,
    Meteor,
}

// Me = the area spawns (design 2.3). Own SpawnData entries, in no SpawnSystemList (spawn-list mods never see them).
// Runner = SpawnSystem.UpdateSpawning postfix on the zone-control owner: when the north is awake and the zone touch an
// awake cell, each due entry run alone through vanilla UpdateSpawnList([entry], now, false, own salt): vanilla does
// counts, spawn points, groups, levels; own salt = own ZDO timer key, alone = no shared spawn counter. Due test read the
// same timer vanilla read, first: the 5 x 5 zone object search only run when an entry can spawn.
// Gate = SpawnSystem.IsSpawnPointGood prefix (Gate): our entries only spawn in awake cells (meteors: storming, not
// cleared).
// After Kall (design 2.6): no timed spawns. The cell under a player in the zone, not cleared and not engaged = entered
// again: one burst per cell (Jotun entries with their timers reset, so vanilla tries up to each cap; the gate only allow
// that cell; the cap count = the cell's own Jotun of that kind in the zones around, BurstCount), then the cells that got
// Jotun or have some are engaged (to be defeated) until no player is near (HeldCells).
// Tag = while Jotun entries run, Character.Awake postfix write the cell on each new Jotun (HeldCells.TagKey) and note
// it (SpawnedInto).
// Nature band (design 2.5): every BandInterval s per zone, BandChance: 10-20 frost Greydwarfs (2-4 of them shamans) and
// 1-3 big ones (Gammeltroll or Barka) together at one spot, vanilla spawn point rules for each; none while BandCap
// band members already live near; never in a cleared area (after Kall they keep coming until the area is cleared).
// Entries rebuilt when rules, stage or the prefab table change.
internal static class AreaSpawns
{
    private sealed class Entry
    {
        internal EntryKind Kind;
        internal SpawnSystem.SpawnData Data;
        internal List<SpawnSystem.SpawnData> One;
        internal string Salt;
        internal int TimerKey;
    }

    // Base values (design 2.3 table). Jotun copy the vanilla invasion entries.
    private struct Spec
    {
        internal string Prefab;
        internal int Max;
        internal float Interval;
        internal int GroupMin;
        internal int GroupMax;
        internal int MaxLevel;
    }

    private static readonly Spec[] JotunSpecs =
    {
        new Spec { Prefab = Hostility.Krigen, Max = 3, Interval = 500f, GroupMin = 1, GroupMax = 2, MaxLevel = 3 },
        new Spec { Prefab = Hostility.KrigenDual, Max = 2, Interval = 500f, GroupMin = 1, GroupMax = 1, MaxLevel = 3 },
        new Spec { Prefab = Hostility.Hexen, Max = 2, Interval = 500f, GroupMin = 1, GroupMax = 1, MaxLevel = 2 },
        new Spec { Prefab = Hostility.Elaking, Max = 6, Interval = 1000f, GroupMin = 1, GroupMax = 3, MaxLevel = 2 },
    };

    // Band members: prefab, levels (vanilla Deep North list values; Gammeltroll and Barka stay 0-star there).
    private static readonly (string Prefab, int MaxLevel)[] BandSpecs =
    {
        (Hostility.FrostGreydwarf, 3),
        (Hostility.FrostShaman, 3),
        (Hostility.Gammeltroll, 1),
        (Hostility.Barka, 1),
    };

    internal const string MeteorPrefab = "projectile_FimbulvinterMeteor";
    private const string SaltPrefix = "mcdn";

    // How far from the zone centre me look for awake cells (zone half 32 m + vanilla spawn ring 80 m).
    internal const float ZoneReach = 112f;

    internal const float BandInterval = 1200f;
    internal const int BandGreydwarfMin = 10;
    internal const int BandGreydwarfMax = 20;
    internal const int BandShamanMin = 2;
    internal const int BandShamanMax = 4;
    internal const int BandBigMin = 1;
    internal const int BandBigMax = 3;
    internal const int BandCap = 3;
    internal const float BandCapRadius = 160f;
    private const float BandRadius = 10f;
    private const float BandBigRadius = 13f;
    private static readonly int BandTimerKey = (SaltPrefix + "Band_NatureBand1").GetStableHashCode();
    internal static readonly int BandKey = (ModInfo.Guid + ".Band").GetStableHashCode();

    private static readonly List<Entry> Entries = new List<Entry>();
    private static readonly Dictionary<SpawnSystem.SpawnData, EntryKind> Kinds =
        new Dictionary<SpawnSystem.SpawnData, EntryKind>();
    private static readonly Dictionary<string, SpawnSystem.SpawnData> Band =
        new Dictionary<string, SpawnSystem.SpawnData>();
    private static readonly HashSet<string> MissingLogged = new HashSet<string>();
    private static readonly List<int> BurstCells = new List<int>();
    private const int NoCell = int.MinValue;
    private static int _burstCell = NoCell;
    private static readonly HashSet<int> SpawnedInto = new HashSet<int>();
    private static int _builtRules = int.MinValue;
    private static int _builtStage = -1;
    private static ZNetScene _builtScene;

    // True while Jotun entries spawn (Character.Awake postfix tag the new creature).
    internal static bool Tagging { get; private set; }

    // True while an after-Kall burst run (the gate allow only the burst cell, caps count only its Jotun).
    internal static bool Bursting { get; private set; }

    // True while a nature band spawn (Character.Awake postfix mark the new creature as a band member).
    internal static bool BandTagging { get; private set; }

    internal static int EntryCount => Entries.Count;

    internal static bool IsOurs(SpawnSystem.SpawnData data, out EntryKind kind)
    {
        if (data == null)
        {
            kind = EntryKind.Jotun;
            return false;
        }
        return Kinds.TryGetValue(data, out kind);
    }

    // UpdateSpawning postfix body.
    internal static void Run(SpawnSystem ss)
    {
        var rules = ServerRules.Current;
        if (!WorldState.Awake(rules))
        {
            return;
        }
        var nview = ss.m_nview;
        if (nview == null || !nview.IsValid() || !nview.IsOwner() || Player.m_localPlayer == null)
        {
            return;
        }
        var zdo = nview.GetZDO();
        var hm = ss.m_heightmap;
        if (hm == null || !hm.HaveBiome(Heightmap.Biome.DeepNorth))
        {
            return;
        }
        var kall = WorldState.KallDefeated;
        Look(ss.transform.position, rules, out var anyAwake, out var anyStorm, out var anyOpen);
        if (!anyAwake)
        {
            return;
        }
        EnsureBuilt(rules);
        if (Entries.Count == 0)
        {
            return;
        }
        // Refill: another mod's prefix may have skipped the original, the list may hold another zone's players.
        var players = SpawnSystem.m_tempNearPlayers;
        players.Clear();
        ss.GetPlayersInZone(players);
        if (players.Count == 0)
        {
            return;
        }
        BurstCells.Clear();
        if (kall)
        {
            PickBurstCells(players, rules);
            if (BurstCells.Count == 0 && !anyStorm && !anyOpen)
            {
                return;
            }
        }
        var now = ZNet.instance.GetTime();
        SpawnedInto.Clear();
        foreach (var e in Entries)
        {
            if (e.Kind == EntryKind.Meteor)
            {
                if (anyStorm && Due(zdo, e.TimerKey, e.Data.m_spawnInterval, now))
                {
                    RunEntry(ss, e, now, false);
                }
            }
            else if (!kall && Due(zdo, e.TimerKey, e.Data.m_spawnInterval, now))
            {
                RunEntry(ss, e, now, false);
            }
        }
        if (BurstCells.Count > 0)
        {
            // Entered again: one pass per cell, so two cells never share one set of caps.
            try
            {
                foreach (var cell in BurstCells)
                {
                    _burstCell = cell;
                    foreach (var e in Entries)
                    {
                        if (e.Kind != EntryKind.Jotun)
                        {
                            continue;
                        }
                        // Vanilla try up to the cap at once.
                        zdo.Set(e.TimerKey, 0L);
                        RunEntry(ss, e, now, true);
                    }
                }
            }
            finally
            {
                _burstCell = NoCell;
            }
            // To be defeated until no player is near: the cells that got Jotun or have some.
            HeldCells.AfterBurst(BurstCells, SpawnedInto);
            Log.Debug($"Kall is defeated: {SpawnedInto.Count} of {BurstCells.Count} Deep North area(s) entered again "
                      + "spawned their Jotun.");
            BurstCells.Clear();
            SpawnedInto.Clear();
        }
        if (anyOpen && rules.NatureFightsBack && rules.NatureBandChance > 0 && Due(zdo, BandTimerKey, BandInterval, now))
        {
            zdo.Set(BandTimerKey, now.Ticks);
            if (UnityEngine.Random.Range(0f, 100f) < rules.NatureBandChance)
            {
                TrySpawnBand(ss, players);
            }
        }
    }

    private static void RunEntry(SpawnSystem ss, Entry e, DateTime now, bool burst)
    {
        Tagging = e.Kind == EntryKind.Jotun;
        Bursting = burst && e.Kind == EntryKind.Jotun;
        try
        {
            ss.UpdateSpawnList(e.One, now, false, e.Salt);
        }
        finally
        {
            Tagging = false;
            Bursting = false;
        }
    }

    // After Kall: the cells under the zone's players that are entered again (awake, not cleared, not engaged). Never a
    // cell only a zone sample reach: the vanilla ring (40-80 m from a player) could not put its Jotun in it.
    private static void PickBurstCells(List<Player> players, AwakeningRules rules)
    {
        foreach (var p in players)
        {
            if (p == null)
            {
                continue;
            }
            var pos = p.transform.position;
            if (!WorldGenerator.IsDeepnorth(pos.x, pos.z))
            {
                continue;
            }
            var cell = WorldState.CellAt(pos);
            if (BurstCells.Contains(cell) || !WorldState.IsAwakeCell(cell, rules) || !HeldCells.MayBurst(cell))
            {
                continue;
            }
            BurstCells.Add(cell);
        }
    }

    // GetNrOfZDOInstances prefix while Bursting: the burst cell's own Jotun of that kind in the 5 x 5 zones, not every
    // one of that prefab (other areas' Jotun and vanilla ones must not fill its caps). -1 = not one of ours: vanilla
    // count.
    internal static int BurstCount(GameObject prefab, List<ZDO> zdos)
    {
        if (prefab == null || _burstCell == NoCell)
        {
            return -1;
        }
        var hash = prefab.name.GetStableHashCode();
        var kind = Hostility.ArmyIndex(hash);
        if (kind < 0)
        {
            return -1;
        }
        var local = 0;
        foreach (var zdo in zdos)
        {
            if (zdo != null && zdo.GetPrefab() == hash && zdo.GetInt(HeldCells.TagKey, HeldCells.NoTag) == _burstCell)
            {
                local++;
            }
        }
        return local;
    }

    // Character.Awake postfix while Tagging: cells that got Jotun in this pass.
    internal static void NoteTagged(int cell) => SpawnedInto.Add(cell);

    // Any awake cell within ZoneReach of the zone centre (3 x 3 samples); any of them not cleared; any storming one that
    // is not cleared.
    private static void Look(Vector3 center, AwakeningRules rules, out bool anyAwake, out bool anyStorm, out bool anyOpen)
    {
        anyAwake = false;
        anyStorm = false;
        anyOpen = false;
        var seed = WorldState.Seed;
        var coverage = rules.Coverage(WorldState.Stage);
        var last = int.MinValue;
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dz = -1; dz <= 1; dz++)
            {
                var cell = Cells.At(seed, center.x + dx * ZoneReach, center.z + dz * ZoneReach);
                if (cell == last)
                {
                    continue;
                }
                last = cell;
                if (!Cells.IsAwake(seed, cell, coverage))
                {
                    continue;
                }
                anyAwake = true;
                if (HeldCells.IsCleared(cell))
                {
                    continue;
                }
                anyOpen = true;
                if (!anyStorm && rules.Meteors && WorldState.IsStormingCell(cell, rules))
                {
                    anyStorm = true;
                }
            }
        }
    }

    // Same test as vanilla UpdateSpawnList: at least one interval since the stamp on the zone-control ZDO.
    private static bool Due(ZDO zdo, int key, float interval, DateTime now)
    {
        var stamp = zdo.GetLong(key, 0L);
        if (stamp < 0L || stamp > DateTime.MaxValue.Ticks)
        {
            return true;
        }
        return (now - new DateTime(stamp)).TotalSeconds >= interval;
    }

    // IsSpawnPointGood prefix body: false = refuse the point (our entries only).
    internal static bool Gate(EntryKind kind, Vector3 point)
    {
        var rules = ServerRules.Current;
        if (!WorldState.Awake(rules))
        {
            return false;
        }
        var cell = WorldState.CellAt(point);
        if (!WorldState.IsAwakeCell(cell, rules))
        {
            return false;
        }
        switch (kind)
        {
            case EntryKind.Jotun:
                // After Kall only during a burst, only into its cells.
                return !WorldState.KallDefeated || (Bursting && cell == _burstCell);
            case EntryKind.Nature:
                return !HeldCells.IsCleared(cell);
            default:
                return rules.Meteors && !HeldCells.IsCleared(cell) && WorldState.IsStormingCell(cell, rules);
        }
    }

    // Pure (self test): a band with range(min, maxExclusive) rolls.
    internal struct BandRoll
    {
        internal int Greydwarfs;
        internal int Shamans;
        internal int Gammeltrolls;
        internal int Barkas;
    }

    internal static BandRoll RollBand(Func<int, int, int> range)
    {
        var total = range(BandGreydwarfMin, BandGreydwarfMax + 1);
        // About one shaman in five: 10 = 2, 20 = 4.
        var shamans = Mathf.Clamp(Mathf.RoundToInt(total / 5f), BandShamanMin, BandShamanMax);
        var roll = new BandRoll { Greydwarfs = total - shamans, Shamans = shamans };
        var big = range(BandBigMin, BandBigMax + 1);
        for (var i = 0; i < big; i++)
        {
            if (range(0, 2) == 0)
            {
                roll.Gammeltrolls++;
            }
            else
            {
                roll.Barkas++;
            }
        }
        return roll;
    }

    // Band members alive near a point (loaded creatures: the player's area covers the zone's 5 x 5 zones).
    internal static int BandMembersNear(Vector3 center, float radius)
    {
        var n = 0;
        var r2 = radius * radius;
        foreach (var ch in Character.GetAllCharacters())
        {
            if (ch == null || ch.IsDead())
            {
                continue;
            }
            var d = ch.transform.position - center;
            if (d.x * d.x + d.z * d.z > r2)
            {
                continue;
            }
            var nview = ch.m_nview;
            var zdo = nview != null ? nview.GetZDO() : null;
            if (zdo != null && zdo.GetInt(BandKey, 0) != 0)
            {
                n++;
            }
        }
        return n;
    }

    // One band at a spot vanilla would spawn a frost Greydwarf (40-80 m from a player, awake cell). Returns members.
    internal static int TrySpawnBand(SpawnSystem ss, List<Player> players)
    {
        if (!Band.TryGetValue(Hostility.FrostGreydwarf, out var anchor)
            || BandMembersNear(ss.transform.position, BandCapRadius) >= BandCap
            || !ss.FindBaseSpawnPoint(anchor, players, out var center, out _))
        {
            return 0;
        }
        var roll = RollBand(UnityEngine.Random.Range);
        var spawned = 0;
        BandTagging = true;
        try
        {
            spawned += SpawnMembers(ss, Hostility.FrostGreydwarf, roll.Greydwarfs, center, BandRadius);
            spawned += SpawnMembers(ss, Hostility.FrostShaman, roll.Shamans, center, BandRadius);
            spawned += SpawnMembers(ss, Hostility.Gammeltroll, roll.Gammeltrolls, center, BandBigRadius);
            spawned += SpawnMembers(ss, Hostility.Barka, roll.Barkas, center, BandBigRadius);
        }
        finally
        {
            BandTagging = false;
        }
        Log.Debug($"Nature fights back: a band of {roll.Greydwarfs} frost Greydwarfs, {roll.Shamans} shaman(s), "
                  + $"{roll.Gammeltrolls} Gammeltroll and {roll.Barkas} Barka ({spawned} spawned).");
#if DEBUG
        TestLastBand = roll;
        TestLastBandSpawned = spawned;
        TestLastBandCenter = center;
        TestBands++;
#endif
        return spawned;
    }

    private static int SpawnMembers(SpawnSystem ss, string prefab, int count, Vector3 center, float radius)
    {
        if (count <= 0 || !Band.TryGetValue(prefab, out var data))
        {
            return 0;
        }
        var done = 0;
        for (var i = 0; i < count; i++)
        {
            for (var t = 0; t < 10; t++)
            {
                var r = UnityEngine.Random.insideUnitCircle * radius;
                var p = center + new Vector3(r.x, 0f, r.y);
                if (ss.IsSpawnPointGood(data, ref p))
                {
                    ss.Spawn(data, p + Vector3.up * data.m_groundOffset, false);
                    done++;
                    break;
                }
            }
        }
        return done;
    }

    private static void EnsureBuilt(AwakeningRules rules)
    {
        var scene = ZNetScene.instance;
        var stage = WorldState.Stage;
        if (_builtRules == ServerRules.Version && _builtStage == stage && ReferenceEquals(scene, _builtScene)
            && scene != null)
        {
            return;
        }
        Build(rules, stage, scene);
        _builtRules = ServerRules.Version;
        _builtStage = stage;
        _builtScene = scene;
    }

    private static void Build(AwakeningRules rules, int stage, ZNetScene scene)
    {
        Entries.Clear();
        Kinds.Clear();
        Band.Clear();
        if (scene == null)
        {
            return;
        }
        var stars = rules.StarChance(stage);
        for (var i = 0; i < JotunSpecs.Length; i++)
        {
            var s = JotunSpecs[i];
            var max = Math.Max(1, (int)Math.Round(s.Max * rules.JotunDensity / 100f));
            var data = Make(scene, s.Prefab, "MC Deep North - " + s.Prefab, max, s.Interval, 100f, s.GroupMin,
                s.GroupMax, 3f, 10f);
            if (data == null)
            {
                continue;
            }
            data.m_minAltitude = 0f;
            // Vanilla read override > 0 only (0 = its own 10 %): no stars at all = max level 1.
            if (stars <= 0f)
            {
                data.m_maxLevel = 1;
            }
            else
            {
                data.m_maxLevel = s.MaxLevel;
                data.m_overrideLevelupChance = stars;
            }
            Add(EntryKind.Jotun, data, i);
        }
        if (rules.Meteors)
        {
            // Copy of vanilla "Fimbulvinter - Meteors": no cap, every 5 s, 1-3 at 30 m, 150 m up, any altitude.
            var meteor = Make(scene, MeteorPrefab, "MC Deep North - meteors", 0, 5f, 100f, 1, 3, 30f, 0f);
            if (meteor != null)
            {
                meteor.m_groundOffset = 150f;
                Add(EntryKind.Meteor, meteor, JotunSpecs.Length);
            }
        }
        if (rules.NatureFightsBack)
        {
            // Band members: not run by vanilla UpdateSpawnList (the band spawn them together), only gated.
            foreach (var (prefab, maxLevel) in BandSpecs)
            {
                var data = Make(scene, prefab, "MC Deep North - band " + prefab, 1, BandInterval, 100f, 1, 1, 3f, 0f);
                if (data == null)
                {
                    continue;
                }
                data.m_minAltitude = 0f;
                data.m_maxLevel = maxLevel;
                Band[prefab] = data;
                Kinds[data] = EntryKind.Nature;
            }
        }
        Log.Debug($"Deep North area spawns ready for stage {stage}: {Entries.Count} entries, {Band.Count} band "
                  + $"creatures, Jotun star chance {stars:0.#} %.");
    }

    private static SpawnSystem.SpawnData Make(ZNetScene scene, string prefabName, string name, int max, float interval,
        float chance, int groupMin, int groupMax, float groupRadius, float spawnDistance)
    {
        var prefab = scene.GetPrefab(prefabName);
        if (prefab == null)
        {
            if (MissingLogged.Add(prefabName))
            {
                Log.Warning($"The game has no prefab '{prefabName}'; the Deep North areas spawn without it.");
            }
            return null;
        }
        return new SpawnSystem.SpawnData
        {
            m_name = name,
            m_enabled = true,
            m_prefab = prefab,
            m_biome = Heightmap.Biome.DeepNorth,
            m_biomeArea = Heightmap.BiomeArea.Everything,
            m_maxSpawned = max,
            m_spawnInterval = interval,
            m_spawnChance = chance,
            m_spawnDistance = spawnDistance,
            m_groupSizeMin = groupMin,
            m_groupSizeMax = groupMax,
            m_groupRadius = groupRadius,
            m_spawnAtDay = true,
            m_spawnAtNight = true,
            m_minAltitude = -1000f,
            m_maxAltitude = 1000f,
            m_maxTilt = 35f,
            m_inForest = true,
            m_outsideForest = true,
            m_outsideLava = true,
            m_huntPlayer = false,
            m_minLevel = 1,
            m_maxLevel = 1,
        };
    }

    private static void Add(EntryKind kind, SpawnSystem.SpawnData data, int index)
    {
        var salt = SaltPrefix + index + "_";
        Entries.Add(new Entry
        {
            Kind = kind,
            Data = data,
            One = new List<SpawnSystem.SpawnData> { data },
            Salt = salt,
            // Vanilla key: salt + prefab name + 1-based index in the list (1: list of one).
            TimerKey = (salt + data.m_prefab.name + 1).GetStableHashCode(),
        });
        Kinds[data] = kind;
    }

    // World end, feature off.
    internal static void Clear()
    {
        Entries.Clear();
        Kinds.Clear();
        Band.Clear();
        BurstCells.Clear();
        SpawnedInto.Clear();
        _burstCell = NoCell;
        _builtRules = int.MinValue;
        _builtStage = -1;
        _builtScene = null;
        Tagging = false;
        Bursting = false;
        BandTagging = false;
    }

#if DEBUG
    // Self test: the built entry of a prefab (null = none).
    internal static SpawnSystem.SpawnData TestEntry(string prefab)
    {
        foreach (var e in Entries)
        {
            if (e.Data.m_prefab != null && e.Data.m_prefab.name == prefab)
            {
                return e.Data;
            }
        }
        return null;
    }

    internal static void TestBuild() => EnsureBuilt(ServerRules.Current);

    // Self test: forced rules changed (no new rules version): entries are built again at the next use.
    internal static void TestInvalidate() => _builtRules = int.MinValue;

    // Self test: the last nature band (what was rolled, how many really spawned, where) and how many bands so far.
    internal static BandRoll TestLastBand { get; private set; }

    internal static int TestLastBandSpawned { get; private set; }

    internal static Vector3 TestLastBandCenter { get; private set; }

    internal static int TestBands { get; private set; }

    // Self test: area timers of this zone back to "never ran" (stamp 0 on its zone control, like a zone nobody visited):
    // Jotun and meteor entries, the band check. Keys made like Add and the band key, from the specs (works with no
    // entries built too).
    internal static void TestResetTimers(SpawnSystem ss, bool spawns, bool band)
    {
        var nview = ss != null ? ss.m_nview : null;
        var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
        if (zdo == null)
        {
            return;
        }
        if (spawns)
        {
            for (var i = 0; i < JotunSpecs.Length; i++)
            {
                zdo.Set((SaltPrefix + i + "_" + JotunSpecs[i].Prefab + 1).GetStableHashCode(), 0L);
            }
            zdo.Set((SaltPrefix + JotunSpecs.Length + "_" + MeteorPrefab + 1).GetStableHashCode(), 0L);
        }
        if (band)
        {
            zdo.Set(BandTimerKey, 0L);
        }
    }

    // Self test: caps of the Jotun entries by prefab at density 100 (design 2.3 table).
    internal static int TestBaseCap(string prefab)
    {
        foreach (var s in JotunSpecs)
        {
            if (s.Prefab == prefab)
            {
                return s.Max;
            }
        }
        return 0;
    }

    // Self test: biggest group of a Jotun entry by prefab (design 2.3 table). The game size the last group of a pass
    // against the count from before the pass: one pass can end at cap + group - 1.
    internal static int TestGroupMax(string prefab)
    {
        foreach (var s in JotunSpecs)
        {
            if (s.Prefab == prefab)
            {
                return s.GroupMax;
            }
        }
        return 1;
    }
#endif
}
