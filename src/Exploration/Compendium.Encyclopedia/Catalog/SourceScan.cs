using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Exploration.CompendiumEncyclopediaMod;

// Me = other half of CatalogBuilder: where items come from (gather, chests, conversions, producers, fish, traders) and
// where creatures live (spawn lists, alternate biomes, nests, offspring, trophy table). Same scan idea as Sort Chest
// BiomeIndex (copied, not shared). Networked prefabs keep these scripts on root: flat pass use root TryGetComponent;
// vegetation entries look in children too (depth <= 3), like BiomeIndex.CollectDrops.
internal sealed partial class CatalogBuilder
{
    private const int MaxDepth = 3;

    private readonly HashSet<GameObject> _visited = new HashSet<GameObject>();
    private readonly HashSet<Entry> _nestHabitat = new HashSet<Entry>();
    private readonly Dictionary<Entry, List<string>> _spawnKeys = new Dictionary<Entry, List<string>>();
    private int _offeringBowls;
    private int _tradersInPrefabs;
    private int _tradersInNonNet;
    private readonly Dictionary<string, int> _sourceCounts = new Dictionary<string, int>(StringComparer.Ordinal);

    // ---------------------------------------------------------------- 5. world sources (flat pass)

    private IEnumerator StageWorldSources()
    {
        var prefabs = PrefabsCopy();
        for (var i = 0; i < prefabs.Length; i++)
        {
            try
            {
                ScanPrefab(prefabs[i]);
                _tradersInPrefabs += ScanTraders(prefabs[i]);
            }
            catch (Exception e)
            {
                ScanError(prefabs[i], e);
            }
            if (_b.Over)
            {
                yield return null;
            }
        }

        // Trader has no ZNetView (local logic): only m_nonNetViewPrefabs can hold one. Maybe none there (logged).
        var nonNet = _scene.m_nonNetViewPrefabs != null ? _scene.m_nonNetViewPrefabs.ToArray() : Array.Empty<GameObject>();
        for (var i = 0; i < nonNet.Length; i++)
        {
            try
            {
                _tradersInNonNet += ScanTraders(nonNet[i]);
            }
            catch (Exception e)
            {
                ScanError(nonNet[i], e);
            }
            if (_b.Over)
            {
                yield return null;
            }
        }
        _c.TraderCount = _tradersInPrefabs + _tradersInNonNet;
    }

    private void ScanPrefab(GameObject go)
    {
        if (go == null)
        {
            return;
        }
        if (go.TryGetComponent<Pickable>(out var pickable))
        {
            Count("Pickable");
            AddGather(pickable.m_itemPrefab, GatherKind.Picked, Heightmap.Biome.None);
            AddTable(pickable.m_extraDrops, GatherKind.Picked, Heightmap.Biome.None);
        }
        if (go.TryGetComponent<PickableItem>(out var pickableItem))
        {
            Count("PickableItem");
            AddPickableItem(pickableItem, Heightmap.Biome.None);
        }
        if (go.TryGetComponent<MineRock>(out var mineRock))
        {
            Count("MineRock");
            AddTable(mineRock.m_dropItems, GatherKind.Mined, Heightmap.Biome.None);
        }
        if (go.TryGetComponent<MineRock5>(out var mineRock5))
        {
            Count("MineRock5");
            AddTable(mineRock5.m_dropItems, GatherKind.Mined, Heightmap.Biome.None);
        }
        if (go.TryGetComponent<TreeBase>(out var tree))
        {
            Count("TreeBase");
            AddTable(tree.m_dropWhenDestroyed, GatherKind.Chopped, Heightmap.Biome.None);
        }
        if (go.TryGetComponent<TreeLog>(out var log))
        {
            Count("TreeLog");
            AddTable(log.m_dropWhenDestroyed, GatherKind.Chopped, Heightmap.Biome.None);
        }
        if (go.TryGetComponent<Destructible>(out var destructible) && destructible.m_spawnWhenDestroyed != null)
        {
            Count("Destructible");
            // Spawned prefab is itself in ZNetScene (scanned on its own); only a spawned item counts here.
            AddGather(destructible.m_spawnWhenDestroyed, GatherKind.Broken, Heightmap.Biome.None);
        }
        if (go.TryGetComponent<DropOnDestroyed>(out var dropOnDestroyed))
        {
            Count("DropOnDestroyed");
            AddTable(dropOnDestroyed.m_dropWhenDestroyed, GatherKind.Broken, Heightmap.Biome.None);
        }
        if (go.TryGetComponent<Container>(out var container) && container.m_defaultItems != null
            && container.m_defaultItems.m_drops != null && container.m_defaultItems.m_drops.Count > 0)
        {
            Count("Container");
            foreach (var d in container.m_defaultItems.m_drops)
            {
                var item = _c.ItemOf(d.m_item);
                if (item != null)
                {
                    item.Item.InChests = true;
                }
            }
        }
        if (go.TryGetComponent<Smelter>(out var smelter) && smelter.m_conversion != null)
        {
            Count("Smelter");
            foreach (var cv in smelter.m_conversion)
            {
                if (cv != null)
                {
                    AddConversion(cv.m_from, cv.m_to, go);
                }
            }
        }
        if (go.TryGetComponent<CookingStation>(out var cooking) && cooking.m_conversion != null)
        {
            Count("CookingStation");
            foreach (var cv in cooking.m_conversion)
            {
                if (cv != null)
                {
                    AddConversion(cv.m_from, cv.m_to, go);
                }
            }
        }
        if (go.TryGetComponent<Fermenter>(out var fermenter) && fermenter.m_conversion != null)
        {
            Count("Fermenter");
            foreach (var cv in fermenter.m_conversion)
            {
                if (cv != null)
                {
                    AddConversion(cv.m_from, cv.m_to, go);
                }
            }
        }
        if (go.TryGetComponent<Beehive>(out var hive) && hive.m_honeyItem != null)
        {
            Count("Beehive");
            AddProduced(hive.m_honeyItem, go);
        }
        if (go.TryGetComponent<SapCollector>(out var sap) && sap.m_spawnItem != null)
        {
            Count("SapCollector");
            AddProduced(sap.m_spawnItem, go);
        }
        if (go.TryGetComponent<OfferingBowl>(out _))
        {
            _offeringBowls++;
        }
    }

    // Trader on root or children (depth <= 3). Every item it sell = one "sold" source. Return traders found.
    private int ScanTraders(GameObject go)
    {
        if (go == null)
        {
            return 0;
        }
        var found = 0;
        ScanTradersIn(go.transform, 0, ref found);
        return found;
    }

    private void ScanTradersIn(Transform t, int depth, ref int found)
    {
        if (t.TryGetComponent<Trader>(out var trader))
        {
            found++;
            _debug.Add($"Trader found in prefab {t.root.name}: {t.name}.");
            if (trader.m_items != null)
            {
                foreach (var ti in trader.m_items)
                {
                    if (ti == null)
                    {
                        continue;
                    }
                    var item = _c.ItemOf(ti.m_prefab);
                    if (item != null)
                    {
                        item.Item.SoldByTraders++;
                    }
                }
            }
        }
        if (depth >= MaxDepth)
        {
            return;
        }
        for (var i = 0; i < t.childCount; i++)
        {
            ScanTradersIn(t.GetChild(i), depth + 1, ref found);
        }
    }

    private void AddConversion(ItemDrop from, ItemDrop to, GameObject stationPrefab)
    {
        var fromEntry = _c.ItemOf(from);
        var toEntry = _c.ItemOf(to);
        if (fromEntry == null || toEntry == null)
        {
            return;
        }
        _c.PiecesByPrefab.TryGetValue(stationPrefab.name, out var station);
        foreach (var existing in toEntry.Item.MadeFrom)
        {
            if (existing.From == fromEntry && existing.StationPrefab == stationPrefab.name)
            {
                return;
            }
        }
        var cv = new Conversion { From = fromEntry, To = toEntry, Station = station, StationPrefab = stationPrefab.name };
        toEntry.Item.MadeFrom.Add(cv);
        AddUse(fromEntry, UseKind.Conversion, toEntry, station);
        if (station != null)
        {
            station.Piece.Conversions.Add(cv);
        }
    }

    private void AddProduced(ItemDrop drop, GameObject producer)
    {
        var item = _c.ItemOf(drop);
        if (item == null)
        {
            return;
        }
        if (_c.PiecesByPrefab.TryGetValue(producer.name, out var piece))
        {
            if (!item.Item.ProducedBy.Contains(piece))
            {
                item.Item.ProducedBy.Add(piece);
            }
            if (!piece.Piece.Produces.Contains(item))
            {
                piece.Piece.Produces.Add(item);
            }
        }
        else
        {
            item.Item.ProducedUnlisted = true;
        }
    }

    private void AddPickableItem(PickableItem pi, Heightmap.Biome biome)
    {
        if (pi.m_itemPrefab != null)
        {
            AddGather(pi.m_itemPrefab.gameObject, GatherKind.Picked, biome);
        }
        if (pi.m_randomItemPrefabs != null)
        {
            foreach (var r in pi.m_randomItemPrefabs)
            {
                if (r.m_itemPrefab != null)
                {
                    AddGather(r.m_itemPrefab.gameObject, GatherKind.Picked, biome);
                }
            }
        }
    }

    private void AddTable(DropTable table, GatherKind kind, Heightmap.Biome biome)
    {
        if (table == null || table.m_drops == null)
        {
            return;
        }
        foreach (var d in table.m_drops)
        {
            AddGather(d.m_item, kind, biome);
        }
    }

    // Only a game object with ItemDrop counts as item.
    private void AddGather(GameObject go, GatherKind kind, Heightmap.Biome biome)
    {
        var item = _c.ItemOf(go);
        if (item == null)
        {
            return;
        }
        var g = item.Item.Gather(kind);
        g.Biomes |= biome & Biomes.AllKnownFlags;
    }

    private void Count(string what)
    {
        _sourceCounts.TryGetValue(what, out var n);
        _sourceCounts[what] = n + 1;
    }

    // ---------------------------------------------------------------- 6. vegetation (biomes of gathered items, nests)

    private IEnumerator StageVegetation()
    {
        var zs = ZoneSystem.instance;
        var entries = new List<KeyValuePair<ZoneSystem.ZoneVegetation, Heightmap.Biome>>();
        if (zs != null && zs.m_vegetation != null)
        {
            foreach (var veg in zs.m_vegetation)
            {
                if (veg != null)
                {
                    entries.Add(new KeyValuePair<ZoneSystem.ZoneVegetation, Heightmap.Biome>(veg, veg.m_biome));
                }
            }
        }
        foreach (var alt in AltBiomeList.m_altBiomes.ToArray())
        {
            if (alt == null || !alt.m_enabled || alt.m_addVegetation == null)
            {
                continue;
            }
            foreach (var veg in alt.m_addVegetation)
            {
                if (veg != null)
                {
                    var mask = veg.m_biome & alt.m_biome;
                    entries.Add(new KeyValuePair<ZoneSystem.ZoneVegetation, Heightmap.Biome>(veg,
                        mask != Heightmap.Biome.None ? mask : alt.m_biome));
                }
            }
        }

        foreach (var kv in entries)
        {
            var veg = kv.Key;
            var mask = kv.Value & Biomes.AllKnownFlags;
            if (!veg.m_enable || veg.m_prefab == null || mask == Heightmap.Biome.None)
            {
                continue;
            }
            try
            {
                _visited.Clear();
                CollectVeg(veg.m_prefab, mask, 0, GatherKind.Picked);
                CollectNests(veg.m_prefab, mask);
            }
            catch (Exception e)
            {
                ScanError(veg.m_prefab, e);
            }
            if (_b.Over)
            {
                yield return null;
            }
        }
        _visited.Clear();
    }

    // Item itself, or what it drop when picked, mined, chopped, broken (logs and sub-logs too).
    private void CollectVeg(GameObject go, Heightmap.Biome mask, int depth, GatherKind itemKind)
    {
        if (go == null || depth > MaxDepth || !_visited.Add(go))
        {
            return;
        }
        if (go.TryGetComponent<ItemDrop>(out _))
        {
            AddGather(go, itemKind, mask);
            return;
        }
        var pickable = go.GetComponentInChildren<Pickable>(true);
        if (pickable != null)
        {
            AddGather(pickable.m_itemPrefab, GatherKind.Picked, mask);
            AddTable(pickable.m_extraDrops, GatherKind.Picked, mask);
        }
        var pickableItem = go.GetComponentInChildren<PickableItem>(true);
        if (pickableItem != null)
        {
            AddPickableItem(pickableItem, mask);
        }
        var dropOnDestroyed = go.GetComponentInChildren<DropOnDestroyed>(true);
        if (dropOnDestroyed != null)
        {
            AddTable(dropOnDestroyed.m_dropWhenDestroyed, GatherKind.Broken, mask);
        }
        var mineRock = go.GetComponentInChildren<MineRock>(true);
        if (mineRock != null)
        {
            AddTable(mineRock.m_dropItems, GatherKind.Mined, mask);
        }
        var mineRock5 = go.GetComponentInChildren<MineRock5>(true);
        if (mineRock5 != null)
        {
            AddTable(mineRock5.m_dropItems, GatherKind.Mined, mask);
        }
        var tree = go.GetComponentInChildren<TreeBase>(true);
        if (tree != null)
        {
            AddTable(tree.m_dropWhenDestroyed, GatherKind.Chopped, mask);
            CollectVeg(tree.m_logPrefab, mask, depth + 1, GatherKind.Chopped);
        }
        var log = go.GetComponentInChildren<TreeLog>(true);
        if (log != null)
        {
            AddTable(log.m_dropWhenDestroyed, GatherKind.Chopped, mask);
            CollectVeg(log.m_subLogPrefab, mask, depth + 1, GatherKind.Chopped);
        }
        var destructible = go.GetComponentInChildren<Destructible>(true);
        if (destructible != null)
        {
            CollectVeg(destructible.m_spawnWhenDestroyed, mask, depth + 1, GatherKind.Broken);
        }
    }

    // Nests (SpawnArea) and creature spawners standing in the world give their creatures a habitat.
    private void CollectNests(GameObject go, Heightmap.Biome mask)
    {
        foreach (var area in go.GetComponentsInChildren<SpawnArea>(true))
        {
            if (area.m_prefabs == null)
            {
                continue;
            }
            foreach (var sd in area.m_prefabs)
            {
                if (sd != null && sd.m_prefab != null && _c.CreaturesByPrefab.TryGetValue(sd.m_prefab.name, out var creature))
                {
                    AddHabitat(creature, mask, "nest");
                    _nestHabitat.Add(creature);
                }
            }
        }
        foreach (var spawner in go.GetComponentsInChildren<CreatureSpawner>(true))
        {
            if (spawner.m_creaturePrefab != null
                && _c.CreaturesByPrefab.TryGetValue(spawner.m_creaturePrefab.name, out var creature))
            {
                AddHabitat(creature, mask, "spawner");
                _nestHabitat.Add(creature);
            }
        }
    }

    private static void AddHabitat(Entry creature, Heightmap.Biome mask, string source)
    {
        mask &= Biomes.AllKnownFlags;
        if (mask == Heightmap.Biome.None)
        {
            return;
        }
        var info = creature.Creature;
        info.Habitat |= mask;
        if (info.HabitatSource.IndexOf(source, StringComparison.Ordinal) < 0)
        {
            info.HabitatSource = info.HabitatSource.Length == 0 ? source : info.HabitatSource + "+" + source;
        }
    }

    // ---------------------------------------------------------------- 7. spawn lists (creature habitats, fish)

    private IEnumerator StageSpawns()
    {
        var lists = new List<SpawnSystemList>();
        var zs = ZoneSystem.instance;
        if (zs != null && zs.m_zoneCtrlPrefab != null
            && zs.m_zoneCtrlPrefab.TryGetComponent<SpawnSystem>(out var spawnSystem) && spawnSystem.m_spawnLists != null)
        {
            foreach (var list in spawnSystem.m_spawnLists)
            {
                if (list != null && !lists.Contains(list))
                {
                    lists.Add(list);
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
            foreach (var spawn in list.m_spawners.ToArray())
            {
                try
                {
                    AddSpawn(spawn, spawn != null ? spawn.m_biome : Heightmap.Biome.None, "spawn");
                }
                catch (Exception e)
                {
                    ScanError(spawn != null ? spawn.m_prefab : null, e);
                }
            }
            if (_b.Over)
            {
                yield return null;
            }
        }

        foreach (var alt in AltBiomeList.m_altBiomes.ToArray())
        {
            if (alt == null || !alt.m_enabled || alt.m_spawn == null)
            {
                continue;
            }
            foreach (var spawn in alt.m_spawn)
            {
                if (spawn == null)
                {
                    continue;
                }
                var mask = spawn.m_biome & alt.m_biome;
                try
                {
                    AddSpawn(spawn, mask != Heightmap.Biome.None ? mask : alt.m_biome, "alternate biome spawn");
                }
                catch (Exception e)
                {
                    ScanError(spawn.m_prefab, e);
                }
            }
        }
    }

    private void AddSpawn(SpawnSystem.SpawnData spawn, Heightmap.Biome biome, string source)
    {
        if (spawn == null || !spawn.m_enabled || spawn.m_prefab == null)
        {
            return;
        }
        biome &= Biomes.AllKnownFlags;
        if (biome == Heightmap.Biome.None)
        {
            return;
        }
        // Fish spawn as items themselves.
        var item = _c.ItemOf(spawn.m_prefab);
        if (item != null)
        {
            item.Item.Caught = true;
            item.Item.CaughtIn |= biome;
        }
        if (_c.CreaturesByPrefab.TryGetValue(spawn.m_prefab.name, out var creature))
        {
            AddHabitat(creature, biome, source);
            if (!_spawnKeys.TryGetValue(creature, out var keys))
            {
                keys = new List<string>();
                _spawnKeys[creature] = keys;
            }
            keys.Add(spawn.m_requiredGlobalKey ?? "");
        }
    }

    // ---------------------------------------------------------------- 8. habitats (inheritance, trophy fallback, bosses)

    private IEnumerator StageHabitats()
    {
        // Offspring share parent's habitat (Boar -> Piggy); babies share their grown form's (chick -> Hen).
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var link in _offspring)
            {
                if (_c.CreaturesByPrefab.TryGetValue(link.Value, out var child) && child != link.Key)
                {
                    Inherit(child, link.Key);
                }
            }
            foreach (var link in _grown)
            {
                if (_c.CreaturesByPrefab.TryGetValue(link.Value, out var adult) && adult != link.Key)
                {
                    Inherit(link.Key, adult);
                }
            }
        }
        if (_b.Over)
        {
            yield return null;
        }

        foreach (var e in _c.CreaturesByName.Values)
        {
            var info = e.Creature;
            if (info.Habitat == Heightmap.Biome.None && info.Trophy != null)
            {
                foreach (var p in info.Trophy.Item.Prefabs)
                {
                    if (TrophyBiomes.TryGet(p.name, out var biome))
                    {
                        AddHabitat(e, biome, "trophy table");
                        break;
                    }
                }
            }
            // "Appears after defeating": every world spawn entry need a boss key, and no nest or spawner.
            if (!_nestHabitat.Contains(e) && _spawnKeys.TryGetValue(e, out var keys) && keys.Count > 0
                && keys.All(k => k.Length > 0 && _bossByKey.ContainsKey(k)))
            {
                foreach (var key in keys.Distinct())
                {
                    var boss = _bossByKey[key];
                    if (boss != e && !info.AfterBosses.Contains(boss))
                    {
                        info.AfterBosses.Add(boss);
                    }
                }
            }
        }
    }

    private static void Inherit(Entry to, Entry from)
    {
        var mask = from.Creature.Habitat & ~to.Creature.Habitat;
        if (mask != Heightmap.Biome.None)
        {
            AddHabitat(to, mask, "inherited");
        }
    }

    /// <summary>Log lines about the scan itself (component counts, traders, offering bowls).</summary>
    internal string ScanSummary()
    {
        var parts = _sourceCounts.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key} {kv.Value}");
        return $"Encyclopedia catalog scan: source components on prefab roots: {string.Join(", ", parts)}; "
               + $"Trader components: {_tradersInPrefabs} in networked prefabs, {_tradersInNonNet} in non-networked prefabs; "
               + $"OfferingBowl components: {_offeringBowls}; scan errors: {_scanErrors}.";
    }
}
