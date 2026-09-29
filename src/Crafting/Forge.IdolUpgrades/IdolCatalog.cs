using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Crafting.ForgeIdolUpgradesMod;

// Me = resolved idol table for the current ObjectDB: which items are idols (tier, family), and per tier the metal and
// the 3 trophy pools. Built lazily (first use after ObjectDB change or config change), so hot paths (icon, tooltip,
// grid) pay one reference compare + one dictionary lookup.
internal static class IdolCatalog
{
    internal sealed class Idol
    {
        internal int Tier;
        internal string PrefabName;
        internal ItemDrop Prefab;
    }

    // One pool = trophies of one class for one tier. Keyed by item name ($item_trophy_...): the game count and stack
    // by name, so two prefabs with same name (Forest and Frost Troll trophy) are the same item for the inventory.
    internal sealed class Pool
    {
        internal readonly List<ItemDrop> Items = new List<ItemDrop>();
        internal readonly List<string> Names = new List<string>();
    }

    internal sealed class Tier
    {
        internal int Index;
        internal string Title;
        internal ItemDrop Material;
        internal readonly Pool[] Pools = new Pool[IdolLevels.Max + 1]; // index = target level 1..3
    }

    // Game copy the shared item data per item object (every Instantiate: inventory load, crafting, chests), so shared
    // data is no key. Me key by prefab object (m_dropPrefab, set on every real item), name as fallback (prefab's own
    // data in recipe panels has no drop prefab).
    private static readonly Dictionary<GameObject, Idol> ByPrefab = new Dictionary<GameObject, Idol>();
    private static readonly Dictionary<string, Idol> ByName = new Dictionary<string, Idol>();
    private static readonly Tier[] Tiers = new Tier[IdolTierDefaults.Count];
    private static ObjectDB _builtFor;
    private static bool _dirty = true;
    private static ForgeRules _rulesSeen;

    internal static void Invalidate() => _dirty = true;

    // Idol info of an item (null = not one of the 16 vanilla idols). Also heal the item's own copy of max quality:
    // an idol loaded before the mod turned on (chest, live toggle) still has 1 there, which let drag merge levels.
    internal static Idol IdolOf(ItemDrop.ItemData item)
    {
        if (item == null || item.m_shared == null || !Ensure())
        {
            return null;
        }
        Idol idol;
        if (item.m_dropPrefab != null)
        {
            if (!ByPrefab.TryGetValue(item.m_dropPrefab, out idol))
            {
                return null;
            }
        }
        else if (!ByName.TryGetValue(item.m_shared.m_name, out idol))
        {
            return null;
        }
        if (item.m_shared.m_maxQuality < IdolLevels.Max + 1)
        {
            item.m_shared.m_maxQuality = IdolLevels.Max + 1;
        }
        return idol;
    }

    internal static bool IsIdol(ItemDrop.ItemData item) => IdolOf(item) != null;

    internal static Tier TierOf(Idol idol) => idol != null && Ensure() ? Tiers[idol.Tier] : null;

    // All idol prefabs (self test).
    internal static IEnumerable<Idol> AllIdols()
    {
        if (!Ensure())
        {
            yield break;
        }
        foreach (var idol in ByPrefab.Values)
        {
            yield return idol;
        }
    }

    // Idols can hold level 0..3 = quality 1..4. Game compare quality only when max quality > 1 (drag merge, slot
    // number, tooltip), so me raise it on the 16 idol prefabs (items made later copy it) and on idols already in
    // the player inventory (made before). Idols elsewhere get it when first seen (IdolOf). Never lowered while the
    // game run: a toggle off/on must not let plain and starred idols merge (starred idols for free).
    internal static void RaiseIdolLevels()
    {
        if (!Ensure())
        {
            return;
        }
        var player = Player.m_localPlayer;
        if (player != null)
        {
            foreach (var item in player.GetInventory().GetAllItems())
            {
                IdolOf(item);
            }
        }
    }

    private static bool Ensure()
    {
        var db = ObjectDB.instance;
        // Main menu ObjectDB wake up empty, items come later (CopyOtherDB): not ready yet, stay dirty.
        if (db == null || db.m_items == null || db.m_items.Count == 0)
        {
            return false;
        }
        // Rules object change when own config or server rules change: rebuild only when the tier lists differ.
        var rules = ServerRules.Current;
        if (!ReferenceEquals(rules, _rulesSeen))
        {
            if (_rulesSeen == null || !rules.SameTiers(_rulesSeen))
            {
                _dirty = true;
            }
            _rulesSeen = rules;
        }
        if (!_dirty && ReferenceEquals(db, _builtFor))
        {
            return true;
        }
        try
        {
            Build(db);
        }
        catch (Exception e)
        {
            PatchGuard.Report("IdolCatalog.Build", e);
        }
        _builtFor = db;
        _dirty = false;
        return true;
    }

    private static void Build(ObjectDB db)
    {
        ByPrefab.Clear();
        ByName.Clear();
        var unknown = new List<string>();
        for (var t = 0; t < IdolTierDefaults.Count; t++)
        {
            foreach (var name in new[] { IdolTierDefaults.WeaponIdol(t), IdolTierDefaults.ArmorIdol(t) })
            {
                var drop = Item(db, name);
                if (drop == null)
                {
                    unknown.Add(name);
                    continue;
                }
                var shared = drop.m_itemData.m_shared;
                if (shared.m_maxQuality < IdolLevels.Max + 1)
                {
                    shared.m_maxQuality = IdolLevels.Max + 1;
                }
                var idol = new Idol { Tier = t, PrefabName = name, Prefab = drop };
                ByPrefab[drop.gameObject] = idol;
                ByName[shared.m_name] = idol;
            }

            var rules = ServerRules.Current;
            var tier = new Tier { Index = t, Title = IdolTierDefaults.All[t].Title };
            var material = (rules.TierMaterial[t] ?? "").Trim();
            tier.Material = Item(db, material);
            if (tier.Material == null)
            {
                unknown.Add(material);
            }
            tier.Pools[1] = PoolOf(db, rules.TierCommon[t], unknown);
            tier.Pools[2] = PoolOf(db, rules.TierElite[t], unknown);
            tier.Pools[3] = PoolOf(db, rules.TierBoss[t], unknown);
            Tiers[t] = tier;
        }
        if (unknown.Count > 0)
        {
            Log.Warning($"Unknown item name(s) in the idol settings, ignored: {string.Join(", ", unknown.ToArray())}.");
        }
        Log.Debug($"Idol table ready: {ByPrefab.Count} idols, 8 tiers.");
    }

    private static Pool PoolOf(ObjectDB db, string text, List<string> unknown)
    {
        var pool = new Pool();
        if (string.IsNullOrEmpty(text))
        {
            return pool;
        }
        foreach (var part in text.Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var drop = Item(db, part.Trim());
            if (drop == null)
            {
                unknown.Add(part.Trim());
                continue;
            }
            var itemName = drop.m_itemData.m_shared.m_name;
            if (!pool.Names.Contains(itemName))
            {
                pool.Names.Add(itemName);
                pool.Items.Add(drop);
            }
        }
        return pool;
    }

    private static ItemDrop Item(ObjectDB db, string prefabName)
    {
        if (string.IsNullOrEmpty(prefabName))
        {
            return null;
        }
        var go = db.GetItemPrefab(prefabName);
        return go != null ? go.GetComponent<ItemDrop>() : null;
    }
}
