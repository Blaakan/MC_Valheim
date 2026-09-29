using System;
using System.Collections.Generic;
using System.Diagnostics;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.UX.ContainerSortMod;

// Progression order of "By biome". Unknown (modded, test items) last.
internal enum BiomeRank : byte
{
    Meadows,
    BlackForest,
    Swamp,
    Ocean,
    Mountain,
    Plains,
    Mistlands,
    Ashlands,
    DeepNorth,
    Unknown,
}

// Me = item prefab -> biome where players normally get it first. Game give items no biome, so me build it once per
// world (first "By biome" sort), from three sources, earliest biome win:
//   1. hand table (BiomeTable, final);
//   2. world scan: vegetation drops and creature drops, with the biomes they spawn in;
//   3. derivation: recipe / smelter / cooking / fermenter output = latest of its inputs and of its station's build
//      materials (bronze sword need bronze need copper + tin, forge need copper...).
// Debug log show every item and where its biome come from.
internal static class BiomeIndex
{
    private enum Source : byte
    {
        Table,
        Scan,
        Derived,       // exact: every input and station material known
        DerivedApprox, // fallback: some input unknown, ignored (log "Derived?")
    }

    private struct Entry
    {
        internal BiomeRank Rank;
        internal Source Src;
    }

    // One way to get an output item: inputs (items) + maybe a station piece (its build materials count too).
    private sealed class Candidate
    {
        internal string Output;
        internal string[] Inputs;
        internal string[] Station; // null = no station (hand craft); empty = station without known materials
        internal bool OneOf;       // recipe need only one of the inputs
    }

    private const int MaxDropDepth = 3;
    private const int PassCap = 20;

    private static readonly Dictionary<string, Entry> Map = new Dictionary<string, Entry>(StringComparer.Ordinal);
    private static readonly HashSet<GameObject> Visited = new HashSet<GameObject>();
    private static ObjectDB _builtFor;
    private static int _scanErrors;

    internal static BiomeRank Get(ItemDrop.ItemData item)
    {
        EnsureBuilt();
        var name = ItemPrefab.Name(item);
        return name != null && Map.TryGetValue(name, out var e) ? e.Rank : BiomeRank.Unknown;
    }

    // New world = new ObjectDB = build again. No world (no ObjectDB) = nothing, all Unknown.
    internal static void EnsureBuilt()
    {
        var db = ObjectDB.instance;
        if (db == null || ReferenceEquals(db, _builtFor))
        {
            return;
        }
        _builtFor = db;
        Build(db);
    }

    internal static void Clear()
    {
        Map.Clear();
        Visited.Clear();
        _builtFor = null;
    }

    private static void Build(ObjectDB db)
    {
        var watch = Stopwatch.StartNew();
        Map.Clear();
        _scanErrors = 0;

        foreach (var kv in BiomeTable.Entries)
        {
            Map[kv.Key] = new Entry { Rank = kv.Value, Src = Source.Table };
        }

        Stage("vegetation scan", ScanVegetation);
        Stage("creature scan", ScanSpawns);
        Stage("recipe derivation", () => Derive(db));

        watch.Stop();
        var ms = watch.ElapsedMilliseconds;
        Stage("log", () => LogResult(db, ms));
    }

    // One stage blow up = log it, other stages still run.
    private static void Stage(string what, Action step)
    {
        try
        {
            step();
        }
        catch (Exception e)
        {
            Log.Warning($"Biome index: {what} failed, items it would place stay unknown. {e}");
        }
    }

    // ---------------------------------------------------------------- world scan

    private static void ScanVegetation()
    {
        var zs = ZoneSystem.instance;
        if (zs == null || zs.m_vegetation == null)
        {
            return;
        }
        foreach (var veg in zs.m_vegetation)
        {
            if (veg == null || !veg.m_enable || veg.m_prefab == null)
            {
                continue;
            }
            var rank = FromMask(veg.m_biome);
            if (rank == BiomeRank.Unknown)
            {
                continue;
            }
            // Fresh visited set per entry: same prefab under other biome must count again.
            Visited.Clear();
            CollectDrops(veg.m_prefab, rank, 0);
        }
        Visited.Clear();
    }

    // Item itself, or what it drop when picked, mined, chopped, destroyed (logs and sub-logs too).
    private static void CollectDrops(GameObject go, BiomeRank rank, int depth)
    {
        if (go == null || depth > MaxDropDepth || !Visited.Add(go))
        {
            return;
        }
        try
        {
            if (go.GetComponent<ItemDrop>() != null)
            {
                SetMin(go.name, (int)rank, Source.Scan);
                return;
            }
            var pickable = go.GetComponentInChildren<Pickable>(true);
            if (pickable != null)
            {
                AddItem(pickable.m_itemPrefab, rank);
                AddTable(pickable.m_extraDrops, rank);
            }
            var dropOnDestroyed = go.GetComponentInChildren<DropOnDestroyed>(true);
            if (dropOnDestroyed != null)
            {
                AddTable(dropOnDestroyed.m_dropWhenDestroyed, rank);
            }
            var mineRock = go.GetComponentInChildren<MineRock>(true);
            if (mineRock != null)
            {
                AddTable(mineRock.m_dropItems, rank);
            }
            var mineRock5 = go.GetComponentInChildren<MineRock5>(true);
            if (mineRock5 != null)
            {
                AddTable(mineRock5.m_dropItems, rank);
            }
            var tree = go.GetComponentInChildren<TreeBase>(true);
            if (tree != null)
            {
                AddTable(tree.m_dropWhenDestroyed, rank);
                CollectDrops(tree.m_logPrefab, rank, depth + 1);
            }
            var log = go.GetComponentInChildren<TreeLog>(true);
            if (log != null)
            {
                AddTable(log.m_dropWhenDestroyed, rank);
                CollectDrops(log.m_subLogPrefab, rank, depth + 1);
            }
            var destructible = go.GetComponentInChildren<Destructible>(true);
            if (destructible != null)
            {
                CollectDrops(destructible.m_spawnWhenDestroyed, rank, depth + 1);
            }
        }
        catch (Exception e)
        {
            // Modded prefab with broken parts: skip it, keep scanning.
            ReportScanError(go, e);
        }
    }

    private static void AddTable(DropTable table, BiomeRank rank)
    {
        if (table == null || table.m_drops == null)
        {
            return;
        }
        foreach (var drop in table.m_drops)
        {
            AddItem(drop.m_item, rank);
        }
    }

    // Only GameObject with ItemDrop count as item.
    private static void AddItem(GameObject go, BiomeRank rank)
    {
        if (go != null && go.GetComponent<ItemDrop>() != null)
        {
            SetMin(go.name, (int)rank, Source.Scan);
        }
    }

    private static void ScanSpawns()
    {
        var lists = new List<SpawnSystemList>();
        var zs = ZoneSystem.instance;
        if (zs != null && zs.m_zoneCtrlPrefab != null)
        {
            var spawnSystem = zs.m_zoneCtrlPrefab.GetComponent<SpawnSystem>();
            if (spawnSystem != null && spawnSystem.m_spawnLists != null)
            {
                foreach (var list in spawnSystem.m_spawnLists)
                {
                    if (list != null && !lists.Contains(list))
                    {
                        lists.Add(list);
                    }
                }
            }
        }
        foreach (var list in Object.FindObjectsByType<SpawnSystemList>(FindObjectsSortMode.None))
        {
            if (list != null && !lists.Contains(list))
            {
                lists.Add(list);
            }
        }

        foreach (var list in lists)
        {
            if (list.m_spawners == null)
            {
                continue;
            }
            foreach (var spawn in list.m_spawners)
            {
                if (spawn == null || !spawn.m_enabled || spawn.m_prefab == null)
                {
                    continue;
                }
                var rank = FromMask(spawn.m_biome);
                if (rank == BiomeRank.Unknown)
                {
                    continue;
                }
                try
                {
                    // Fish spawn as item themselves.
                    AddItem(spawn.m_prefab, rank);
                    var drops = spawn.m_prefab.GetComponent<CharacterDrop>();
                    if (drops != null && drops.m_drops != null)
                    {
                        foreach (var drop in drops.m_drops)
                        {
                            if (drop != null)
                            {
                                AddItem(drop.m_prefab, rank);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    ReportScanError(spawn.m_prefab, e);
                }
            }
        }
    }

    private static void ReportScanError(GameObject go, Exception e)
    {
        _scanErrors++;
        if (_scanErrors <= 3)
        {
            Log.Debug($"Biome index: skipped prefab {(go != null ? go.name : "?")}: {e.Message}");
        }
    }

    // First biome of progression order present in the mask.
    private static BiomeRank FromMask(Heightmap.Biome mask)
    {
        if ((mask & Heightmap.Biome.Meadows) != 0)
        {
            return BiomeRank.Meadows;
        }
        if ((mask & Heightmap.Biome.BlackForest) != 0)
        {
            return BiomeRank.BlackForest;
        }
        if ((mask & Heightmap.Biome.Swamp) != 0)
        {
            return BiomeRank.Swamp;
        }
        if ((mask & Heightmap.Biome.Ocean) != 0)
        {
            return BiomeRank.Ocean;
        }
        if ((mask & Heightmap.Biome.Mountain) != 0)
        {
            return BiomeRank.Mountain;
        }
        if ((mask & Heightmap.Biome.Plains) != 0)
        {
            return BiomeRank.Plains;
        }
        if ((mask & Heightmap.Biome.Mistlands) != 0)
        {
            return BiomeRank.Mistlands;
        }
        if ((mask & Heightmap.Biome.AshLands) != 0)
        {
            return BiomeRank.Ashlands;
        }
        if ((mask & Heightmap.Biome.DeepNorth) != 0)
        {
            return BiomeRank.DeepNorth;
        }
        return BiomeRank.Unknown;
    }

    // Lower rank win. Table never change. Same rank: exact value replace approximate one.
    private static bool SetMin(string name, int rank, Source src)
    {
        if (string.IsNullOrEmpty(name) || rank < 0 || rank >= (int)BiomeRank.Unknown)
        {
            return false;
        }
        if (Map.TryGetValue(name, out var e))
        {
            if (e.Src == Source.Table || (int)e.Rank < rank)
            {
                return false;
            }
            if ((int)e.Rank == rank && !(e.Src == Source.DerivedApprox && src != Source.DerivedApprox))
            {
                return false;
            }
        }
        Map[name] = new Entry { Rank = (BiomeRank)rank, Src = src };
        return true;
    }

    // ---------------------------------------------------------------- derivation

    private static void Derive(ObjectDB db)
    {
        var candidates = new List<Candidate>();
        var stations = new Dictionary<GameObject, string[]>();

        if (db.m_recipes != null)
        {
            foreach (var recipe in db.m_recipes)
            {
                try
                {
                    AddRecipe(recipe, candidates, stations);
                }
                catch (Exception e)
                {
                    ReportScanError(recipe != null && recipe.m_item != null ? recipe.m_item.gameObject : null, e);
                }
            }
        }

        var scene = ZNetScene.instance;
        if (scene != null && scene.m_prefabs != null)
        {
            foreach (var prefab in scene.m_prefabs)
            {
                if (prefab == null)
                {
                    continue;
                }
                try
                {
                    AddConversions(prefab, candidates, stations);
                }
                catch (Exception e)
                {
                    ReportScanError(prefab, e);
                }
            }
        }

        // Pass A: exact. Candidate count only when all inputs and station materials known. Repeat until calm.
        if (PassA(candidates))
        {
            Log.Warning($"Biome index: exact derivation stopped after {PassCap} passes (some items may be placed too late).");
        }

        // Pass B: items still unknown. Unknown inputs ignored (approximate). Compute all, then write, then exact again.
        var pending = new Dictionary<string, int>(StringComparer.Ordinal);
        var round = 0;
        for (; round < PassCap; round++)
        {
            pending.Clear();
            foreach (var c in candidates)
            {
                if (Map.ContainsKey(c.Output) || !Evaluate(c, loose: true, out var value, out _))
                {
                    continue;
                }
                if (!pending.TryGetValue(c.Output, out var old) || value < old)
                {
                    pending[c.Output] = value;
                }
            }
            if (pending.Count == 0)
            {
                break;
            }
            foreach (var kv in pending)
            {
                SetMin(kv.Key, kv.Value, Source.DerivedApprox);
            }
            PassA(candidates);
        }
        if (round >= PassCap)
        {
            Log.Warning($"Biome index: fallback derivation stopped after {PassCap} rounds.");
        }
    }

    private static void AddRecipe(Recipe recipe, List<Candidate> candidates, Dictionary<GameObject, string[]> stations)
    {
        if (recipe == null || !recipe.m_enabled || recipe.m_item == null)
        {
            return;
        }
        // Base craft only (m_amount): upgrade-only materials would push item later than where it is first made.
        var inputs = RequirementNames(recipe.m_resources);
        if (inputs.Length == 0)
        {
            return;
        }
        var station = recipe.m_craftingStation != null ? StationMaterials(recipe.m_craftingStation.gameObject, stations) : null;
        candidates.Add(new Candidate
        {
            Output = recipe.m_item.gameObject.name,
            Inputs = inputs,
            Station = station,
            OneOf = recipe.m_requireOnlyOneIngredient,
        });
    }

    // Smelter family (smelter, blast furnace, kiln, windmill, spinning wheel, eitr refinery...), cooking stations,
    // fermenter: each conversion = candidate with one input, station = that piece.
    private static void AddConversions(GameObject prefab, List<Candidate> candidates, Dictionary<GameObject, string[]> stations)
    {
        var smelter = prefab.GetComponent<Smelter>();
        if (smelter != null && smelter.m_conversion != null)
        {
            foreach (var c in smelter.m_conversion)
            {
                if (c != null)
                {
                    AddConversion(c.m_from, c.m_to, prefab, candidates, stations);
                }
            }
        }
        var cooking = prefab.GetComponent<CookingStation>();
        if (cooking != null && cooking.m_conversion != null)
        {
            foreach (var c in cooking.m_conversion)
            {
                if (c != null)
                {
                    AddConversion(c.m_from, c.m_to, prefab, candidates, stations);
                }
            }
        }
        var fermenter = prefab.GetComponent<Fermenter>();
        if (fermenter != null && fermenter.m_conversion != null)
        {
            foreach (var c in fermenter.m_conversion)
            {
                if (c != null)
                {
                    AddConversion(c.m_from, c.m_to, prefab, candidates, stations);
                }
            }
        }
    }

    private static void AddConversion(ItemDrop from, ItemDrop to, GameObject station, List<Candidate> candidates,
        Dictionary<GameObject, string[]> stations)
    {
        if (from == null || to == null)
        {
            return;
        }
        candidates.Add(new Candidate
        {
            Output = to.gameObject.name,
            Inputs = new[] { from.gameObject.name },
            Station = StationMaterials(station, stations),
        });
    }

    // Build materials of a station piece (names only, fixed data). Rank read fresh each pass: materials often
    // derived themselves (forge = copper, fermenter = bronze...).
    private static string[] StationMaterials(GameObject station, Dictionary<GameObject, string[]> cache)
    {
        if (cache.TryGetValue(station, out var names))
        {
            return names;
        }
        var piece = station.GetComponent<Piece>();
        names = piece != null ? RequirementNames(piece.m_resources) : Array.Empty<string>();
        cache[station] = names;
        return names;
    }

    private static string[] RequirementNames(Piece.Requirement[] requirements)
    {
        if (requirements == null || requirements.Length == 0)
        {
            return Array.Empty<string>();
        }
        var names = new List<string>(requirements.Length);
        foreach (var req in requirements)
        {
            if (req != null && req.m_resItem != null && req.m_amount > 0)
            {
                names.Add(req.m_resItem.gameObject.name);
            }
        }
        return names.ToArray();
    }

    // Full passes until nothing change. True = cap hit.
    private static bool PassA(List<Candidate> candidates)
    {
        for (var pass = 0; pass < PassCap; pass++)
        {
            var changed = false;
            foreach (var c in candidates)
            {
                if (Map.TryGetValue(c.Output, out var e) && e.Src == Source.Table)
                {
                    continue;
                }
                if (Evaluate(c, loose: false, out var value, out var approx)
                    && SetMin(c.Output, value, approx ? Source.DerivedApprox : Source.Derived))
                {
                    changed = true;
                }
            }
            if (!changed)
            {
                return false;
            }
        }
        return true;
    }

    // Value of a candidate = latest of its inputs (earliest one for "one of" recipes) and of its station materials.
    // Exact (loose false): any needed unknown = no value yet. Loose: unknowns ignored, but at least one input known.
    private static bool Evaluate(Candidate c, bool loose, out int value, out bool approx)
    {
        value = -1;
        approx = loose;
        int inputs;
        if (c.OneOf)
        {
            inputs = int.MaxValue;
            var bestApprox = false;
            foreach (var name in c.Inputs)
            {
                if (TryRank(name, out var r, out var a) && r < inputs)
                {
                    inputs = r;
                    bestApprox = a;
                }
            }
            if (inputs == int.MaxValue)
            {
                return false;
            }
            approx |= bestApprox;
        }
        else
        {
            inputs = -1;
            foreach (var name in c.Inputs)
            {
                if (TryRank(name, out var r, out var a))
                {
                    inputs = Math.Max(inputs, r);
                    approx |= a;
                }
                else if (!loose)
                {
                    return false;
                }
            }
            if (inputs < 0)
            {
                return false;
            }
        }

        var station = -1;
        if (c.Station != null)
        {
            foreach (var name in c.Station)
            {
                if (TryRank(name, out var r, out var a))
                {
                    station = Math.Max(station, r);
                    approx |= a;
                }
                else if (!loose)
                {
                    return false;
                }
            }
        }
        value = Math.Max(inputs, station);
        return true;
    }

    private static bool TryRank(string name, out int rank, out bool approx)
    {
        if (Map.TryGetValue(name, out var e))
        {
            rank = (int)e.Rank;
            approx = e.Src == Source.DerivedApprox;
            return true;
        }
        rank = -1;
        approx = false;
        return false;
    }

    // ---------------------------------------------------------------- log

    private static void LogResult(ObjectDB db, long ms)
    {
        int table = 0, scan = 0, derived = 0, approx = 0, unknown = 0;
        var debugLines = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (db.m_items != null)
        {
            foreach (var go in db.m_items)
            {
                if (go == null || !seen.Add(go.name))
                {
                    continue;
                }
                var name = go.name;
                string where;
                if (Map.TryGetValue(name, out var e))
                {
                    switch (e.Src)
                    {
                        case Source.Table:
                            table++;
                            break;
                        case Source.Scan:
                            scan++;
                            break;
                        case Source.Derived:
                            derived++;
                            break;
                        default:
                            derived++;
                            approx++;
                            break;
                    }
                    where = $"{e.Rank} ({SourceLabel(e.Src)})";
                }
                else
                {
                    unknown++;
                    where = "Unknown";
                }
                debugLines.Add($"Biome index: {name} -> {where} | type {TypeText(go)}");
            }
        }

        Log.Info($"Biome index built in {ms} ms: {table} table, {scan} scan, {derived} derived ({approx} approximate), "
                 + $"{unknown} items unknown.");
        foreach (var line in debugLines)
        {
            Log.Debug(line);
        }

        // Table name the game does not know = typo or item removed by an update.
        var missing = new List<string>();
        foreach (var name in BiomeTable.Entries.Keys)
        {
            if (!seen.Contains(name))
            {
                missing.Add(name);
            }
        }
        if (missing.Count > 0)
        {
            Log.Debug($"Biome index: {missing.Count} table names are not items of this game: {string.Join(", ", missing)}");
        }
    }

    private static string SourceLabel(Source src)
    {
        switch (src)
        {
            case Source.Table:
                return "Table";
            case Source.Scan:
                return "Scan";
            case Source.Derived:
                return "Derived";
            default:
                return "Derived?";
        }
    }

    // "Material/Swords/OneHanded -> 7.0 Materials": check that type order agree with Crafting Search and Sort.
    private static string TypeText(GameObject go)
    {
        var drop = go.GetComponent<ItemDrop>();
        var shared = drop != null && drop.m_itemData != null ? drop.m_itemData.m_shared : null;
        if (shared == null)
        {
            return "none";
        }
        TypeGroups.Rank(shared, out var typeRank, out var subRank);
        return $"{shared.m_itemType}/{shared.m_skillType}/{shared.m_animationState} -> {typeRank}.{subRank} {TypeGroups.Label(typeRank)}";
    }
}
