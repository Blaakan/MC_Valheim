#if DEBUG
using System;
using System.Collections.Generic;
using HarmonyLib;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Core.ProbeContentMod;

// Me put the stand-in content (ContentSpecs.All) in the game the way real libraries do (hooks: Patches/ContentPatches.cs):
//   ItemManager    (blaxxun, Warfare): ObjectDB.Awake PREFIX priority 700, ObjectDB.CopyOtherDB POSTFIX priority 700
//                  (+ UpdateRegisters), ZNetScene.Awake prefix. Me add clones made in an earlier world also to a main
//                  menu database that has no game item: real ItemManager does same (main menu trap).
//   JotunnLateEdit (Jotunn ItemManager): item in same Awake prefix, its data changed in ObjectDB.Awake POSTFIX
//                  priority 0 (Jotunn fire OnItemsRegistered there; Jotunn mods edit stats in it).
//   JotunnPiece    (Jotunn PieceManager): piece put in piece table in ObjectDB.Awake POSTFIX priority 200.
//   AfterLoad      (server-synced item editors): item put in ObjectDB + ZNetScene a few seconds after world load
//                  (client: local player spawned; dedicated server: world up).
// Clones live under one inactive DontDestroyOnLoad object (like Jotunn): made once per game session, never Awake as
// prefab, added again to every new ObjectDB / ZNetScene.
internal static class ContentRegistry
{
    private const string InWorldVar = "MC_INWORLD_DIR";
    private const string ServerVar = "MC_MP_SERVER_DIR";
    private const string VanillaWood = "Wood";

    private static bool? _on;
    private static GameObject _container;
    private static readonly Dictionary<string, GameObject> Clones = new Dictionary<string, GameObject>();
    private static readonly Dictionary<string, Recipe> Recipes = new Dictionary<string, Recipe>();
    private static readonly HashSet<string> LateEdited = new HashSet<string>();
    private static readonly List<string> Problems = new List<string>();

    // True = test run: content on. First call log what me add and start the after-load runner. No test run = idle
    // for good (env var only set by the test scripts, at game start).
    internal static bool Start()
    {
        if (_on.HasValue)
        {
            return _on.Value;
        }
        _on = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(InWorldVar))
              || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ServerVar));
        if (!_on.Value)
        {
            Log.Info($"{InWorldVar} and {ServerVar} not set: stand-in content idle (it only runs under the test scripts).");
            return false;
        }
        var runner = new GameObject("MCProbeContentRunner");
        Object.DontDestroyOnLoad(runner);
        runner.AddComponent<AfterLoadRunner>();
        ContentSelfTest.Register();
        Log.Info($"Stand-in third-party content on: {ContentSpecs.All.Count} prefab(s): "
                 + $"{string.Join(", ", ContentSpecs.Describe().ToArray())}.");
        return true;
    }

    private static GameObject Container
    {
        get
        {
            if (_container == null)
            {
                _container = new GameObject("MCProbeContent");
                _container.SetActive(false);
                Object.DontDestroyOnLoad(_container);
            }
            return _container;
        }
    }

    internal static GameObject CloneOf(string name) => Clones.TryGetValue(name, out var go) && go != null ? go : null;

    // ---------- ObjectDB ----------

    internal static void DbAwakePrefix(ObjectDB __instance)
    {
        try
        {
            AddItems(__instance);
        }
        catch (Exception e)
        {
            Problem("ObjectDB.Awake prefix", e);
        }
    }

    internal static void DbCopyPostfix(ObjectDB __instance)
    {
        try
        {
            if (AddItems(__instance))
            {
                __instance.UpdateRegisters();
            }
        }
        catch (Exception e)
        {
            Problem("ObjectDB.CopyOtherDB postfix", e);
        }
    }

    // Item specs of the ItemManager and JotunnLateEdit ways (both put the item in before Awake). True = list changed.
    private static bool AddItems(ObjectDB db)
    {
        if (db == null || db.m_items == null)
        {
            return false;
        }
        var changed = false;
        foreach (var spec in ContentSpecs.All)
        {
            if (spec.Kind != ContentKind.Item || (spec.Way != Way.ItemManager && spec.Way != Way.JotunnLateEdit))
            {
                continue;
            }
            var clone = EnsureClone(spec, name => Find(db.m_items, name));
            changed |= AddTo(db, spec, clone);
        }
        return changed;
    }

    private static bool AddTo(ObjectDB db, ContentSpec spec, GameObject clone)
    {
        if (clone == null)
        {
            return false;
        }
        var changed = false;
        if (!db.m_items.Contains(clone))
        {
            db.m_items.Add(clone);
            changed = true;
        }
        if (spec.Recipe && db.m_recipes != null)
        {
            var recipe = RecipeFor(spec, clone, db);
            if (recipe != null && !db.m_recipes.Contains(recipe))
            {
                db.m_recipes.Add(recipe);
            }
        }
        return changed;
    }

    // Jotunn PieceManager: piece in its tool's piece table, prefab in ZNetScene.
    internal static void DbPiecePostfix(ObjectDB __instance)
    {
        try
        {
            if (!HasVanilla(__instance))
            {
                return;
            }
            foreach (var spec in ContentSpecs.All)
            {
                if (spec.Way != Way.JotunnPiece)
                {
                    continue;
                }
                var table = PieceTableOf(__instance, spec.PieceTable);
                if (table == null)
                {
                    Problem($"{spec.Name}: no piece table on tool {spec.PieceTable}");
                    continue;
                }
                var clone = EnsureClone(spec, name => Find(table.m_pieces, name) ?? SceneFind(name));
                if (clone == null)
                {
                    continue;
                }
                if (!table.m_pieces.Contains(clone))
                {
                    table.m_pieces.Add(clone);
                }
                RegisterInScene(clone);
            }
        }
        catch (Exception e)
        {
            Problem("ObjectDB.Awake piece postfix", e);
        }
    }

    // Jotunn OnItemsRegistered: edit data of items already in the database. Once per clone (prefab live all session).
    internal static void DbLatePostfix(ObjectDB __instance)
    {
        try
        {
            foreach (var spec in ContentSpecs.All)
            {
                if (spec.Way != Way.JotunnLateEdit || LateEdited.Contains(spec.Name))
                {
                    continue;
                }
                var clone = CloneOf(spec.Name);
                if (clone == null || !__instance.m_items.Contains(clone))
                {
                    continue;
                }
                spec.Edit?.Invoke(clone);
                LateEdited.Add(spec.Name);
            }
        }
        catch (Exception e)
        {
            Problem("ObjectDB.Awake late postfix", e);
        }
    }

    // ---------- ZNetScene ----------

    // Before Awake build its name table: every clone of the start-up ways (items made here when ZNetScene wake before
    // ObjectDB; creatures and other prefabs of the ItemManager way).
    internal static void ScenePrefix(ZNetScene __instance)
    {
        try
        {
            foreach (var spec in ContentSpecs.All)
            {
                if (spec.Way == Way.AfterLoad)
                {
                    continue;
                }
                var clone = spec.Way == Way.JotunnPiece ? CloneOf(spec.Name) : EnsureClone(spec, name => Find(__instance.m_prefabs, name));
                if (clone != null && !__instance.m_prefabs.Contains(clone))
                {
                    __instance.m_prefabs.Add(clone);
                }
            }
        }
        catch (Exception e)
        {
            Problem("ZNetScene.Awake prefix", e);
        }
    }

    // Prefab into a ZNetScene that already woke (its name table is built in Awake).
    internal static void RegisterInScene(GameObject go)
    {
        var scene = ZNetScene.instance;
        if (scene == null || go == null)
        {
            return;
        }
        if (!scene.m_prefabs.Contains(go))
        {
            scene.m_prefabs.Add(go);
        }
        var hash = go.name.GetStableHashCode();
        if (!scene.m_namedPrefabs.ContainsKey(hash))
        {
            scene.m_namedPrefabs.Add(hash, go);
        }
    }

    // ---------- Spawn list (Jotunn CreatureManager way) ----------

    private static SpawnSystemList _spawnList;

    // List on the inactive container (never Awake), one entry per creature spec: Meadows, needs a global key nobody
    // sets (SelfTestContent.NeverKey), so it never spawns but every scanner sees it. Null = no creature clone yet.
    private static SpawnSystemList SpawnList()
    {
        if (_spawnList != null)
        {
            return _spawnList;
        }
        var creature = CloneOf(SelfTestContent.Creature);
        if (creature == null)
        {
            return null;
        }
        _spawnList = Container.AddComponent<SpawnSystemList>();
        _spawnList.m_spawners.Add(new SpawnSystem.SpawnData
        {
            m_name = SelfTestContent.Creature,
            m_enabled = true,
            m_prefab = creature,
            m_biome = Heightmap.Biome.Meadows,
            m_biomeArea = Heightmap.BiomeArea.Everything,
            m_maxSpawned = 1,
            m_spawnInterval = 1000f,
            m_requiredGlobalKey = SelfTestContent.NeverKey,
        });
        return _spawnList;
    }

    // SpawnSystem.Awake prefix: put the list in this instance (Jotunn's CreatureManager does the same).
    internal static void SpawnPrefix(SpawnSystem __instance)
    {
        try
        {
            var list = SpawnList();
            if (list != null && !__instance.m_spawnLists.Contains(list))
            {
                __instance.m_spawnLists.Add(list);
            }
        }
        catch (Exception e)
        {
            Problem("SpawnSystem.Awake prefix", e);
        }
    }

    // probe.content: the list is in every live SpawnSystem (null = fine).
    internal static string SpawnListProblem()
    {
        if (_spawnList == null)
        {
            return "no spawn list made";
        }
        var count = 0;
        foreach (var s in SpawnSystem.m_instances)
        {
            if (s == null)
            {
                continue;
            }
            count++;
            if (!s.m_spawnLists.Contains(_spawnList))
            {
                return $"spawn list missing in SpawnSystem {s.name}";
            }
        }
        return count == 0 ? "no live SpawnSystem to check" : null;
    }

    // probe.content: the piece is in its tool's table (null = fine).
    internal static string PieceProblem(GameObject go)
    {
        foreach (var spec in ContentSpecs.All)
        {
            if (spec.Name != go.name)
            {
                continue;
            }
            var table = PieceTableOf(ObjectDB.instance, spec.PieceTable);
            if (table == null)
            {
                return $"no piece table on {spec.PieceTable}";
            }
            return table.m_pieces.Contains(go) ? null : $"not in the {spec.PieceTable} piece table";
        }
        return "no spec";
    }

    // ---------- After load ----------

    // Server-synced editor way: called once per ObjectDB by AfterLoadRunner.
    internal static List<string> AddAfterLoad(ObjectDB db)
    {
        var added = new List<string>();
        var changed = false;
        foreach (var spec in ContentSpecs.All)
        {
            if (spec.Way != Way.AfterLoad)
            {
                continue;
            }
            var clone = EnsureClone(spec, name => Find(db.m_items, name) ?? SceneFind(name));
            if (clone == null)
            {
                continue;
            }
            if (spec.Kind == ContentKind.Item)
            {
                changed |= AddTo(db, spec, clone);
            }
            RegisterInScene(clone);
            added.Add(spec.Name);
        }
        if (changed)
        {
            db.UpdateRegisters();
        }
        return added;
    }

    // ---------- helpers ----------

    private static GameObject EnsureClone(ContentSpec spec, Func<string, GameObject> find)
    {
        var have = CloneOf(spec.Name);
        if (have != null)
        {
            return have;
        }
        var source = find(spec.Source);
        if (source == null)
        {
            return null;
        }
        var go = Object.Instantiate(source, Container.transform, false);
        go.name = spec.Name;
        if (spec.Way != Way.JotunnLateEdit)
        {
            spec.Edit?.Invoke(go);
        }
        Clones[spec.Name] = go;
        return go;
    }

    private static Recipe RecipeFor(ContentSpec spec, GameObject clone, ObjectDB db)
    {
        if (Recipes.TryGetValue(spec.Name, out var have) && have != null)
        {
            return have;
        }
        Recipe source = null;
        foreach (var r in db.m_recipes)
        {
            if (r != null && r.m_item != null && r.m_item.name == spec.Source)
            {
                source = r;
                break;
            }
        }
        if (source == null)
        {
            return null;
        }
        var recipe = Object.Instantiate(source);
        recipe.name = "Recipe_" + spec.Name;
        recipe.m_item = clone.GetComponent<ItemDrop>();
        recipe.m_enabled = true;
        Recipes[spec.Name] = recipe;
        return recipe;
    }

    private static PieceTable PieceTableOf(ObjectDB db, string tool)
    {
        var go = Find(db.m_items, tool);
        var drop = go != null ? go.GetComponent<ItemDrop>() : null;
        return drop != null ? drop.m_itemData.m_shared.m_buildPieces : null;
    }

    private static GameObject Find(List<GameObject> list, string name)
    {
        if (list == null)
        {
            return null;
        }
        foreach (var go in list)
        {
            if (go != null && go.name == name)
            {
                return go;
            }
        }
        return null;
    }

    private static GameObject SceneFind(string name) =>
        ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;

    internal static bool HasVanilla(ObjectDB db) =>
        db != null && db.m_items != null && db.m_items.Count > 0 && Find(db.m_items, VanillaWood) != null;

    private static void Problem(string site, Exception e = null)
    {
        var text = e == null ? site : $"{site}: {e}";
        Problems.Add(text);
        Log.Error($"Stand-in content: {text}");
    }
}

// Me add the AfterLoad content once per world: client when the local player is in, dedicated server when the world is
// up. A few seconds later than that, like a server sync arriving after join.
internal sealed class AfterLoadRunner : MonoBehaviour
{
    private const float Delay = 3f;

    private ObjectDB _doneFor;
    private ObjectDB _waitingFor;
    private float _readyAt;

    private void Update()
    {
        try
        {
            var db = ObjectDB.instance;
            if (db == null || ReferenceEquals(db, _doneFor) || !ContentRegistry.HasVanilla(db) || ZNetScene.instance == null)
            {
                return;
            }
            var ready = Player.m_localPlayer != null || (ZNet.instance != null && ZNet.instance.IsDedicated());
            if (!ready)
            {
                _waitingFor = null;
                return;
            }
            if (!ReferenceEquals(db, _waitingFor))
            {
                _waitingFor = db;
                _readyAt = Time.time + Delay;
                return;
            }
            if (Time.time < _readyAt)
            {
                return;
            }
            _doneFor = db;
            var added = ContentRegistry.AddAfterLoad(db);
            Log.Info($"Stand-in content added after load: {(added.Count == 0 ? "nothing" : string.Join(", ", added.ToArray()))}.");
        }
        catch (Exception e)
        {
            Log.Error($"Stand-in content after load: {e}");
            _doneFor = ObjectDB.instance;
        }
    }
}
#endif
