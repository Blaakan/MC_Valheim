using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.WeaponsDualWieldMod;

// Me = which weapon can sit in a pair (design 2.1, G1, D5). Same rule for both hands:
//   one-handed weapon, skill Swords / Axes / Clubs / Knives (spear, bomb, unarmed out), primary attack with an
//   animation that is a melee swing (Horizontal / Vertical) and no consume (tankard, bomb out), not tame-only (butcher
//   knife out: in off hand it would cut own tames), not in ExcludedWeapons.
// ExcludedWeapons = prefab names. Me turn each into its prefab and compare references with the item's m_dropPrefab:
// in a build every Instantiate of an item prefab (load, pickup, craft, Inventory.AddItem(name)) deep-copy SharedData
// (ItemDrop.Awake relink m_shared only in editor), so the item's m_shared is its own copy, but Awake set m_dropPrefab
// to the prefab. Item with no m_dropPrefab (prefab's own ItemData, ItemData.Clone of it) = compare m_shared with the
// prefab's. So "AxeBronze" no exclude "FW_AxeBronze" (same display name, own prefab).
// Set rebuilt when rules object or ObjectDB change (reference compare, no alloc on hot path).
// Creature attack items pass too (design D24): only spawn command give them to a player, pair with one work.
internal static class Eligibility
{
    // One excluded prefab: the prefab and its own SharedData object.
    private struct Entry
    {
        internal GameObject Prefab;
        internal ItemDrop.ItemData.SharedData Shared;
    }

    private static DualRules _rules;
    private static ObjectDB _db;
    private static bool _built;
    private static readonly List<Entry> Excluded = new List<Entry>();
    // Unknown names me already warned about (once each per session).
    private static readonly HashSet<string> Warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // Hot path (every equip, stance, swing): reference compares, then field reads.
    internal static bool IsEligible(ItemDrop.ItemData item)
    {
        if (item == null || item.m_shared == null)
        {
            return false;
        }
        var shared = item.m_shared;
        if (shared.m_itemType != ItemDrop.ItemData.ItemType.OneHandedWeapon || shared.m_tamedOnly)
        {
            return false;
        }
        var skill = shared.m_skillType;
        if (skill != Skills.SkillType.Swords && skill != Skills.SkillType.Axes && skill != Skills.SkillType.Clubs
            && skill != Skills.SkillType.Knives)
        {
            return false;
        }
        var attack = shared.m_attack;
        if (attack == null || string.IsNullOrEmpty(attack.m_attackAnimation) || attack.m_consumeItem
            || (attack.m_attackType != Attack.AttackType.Horizontal && attack.m_attackType != Attack.AttackType.Vertical))
        {
            return false;
        }
        return !IsExcluded(item);
    }

    // Weapon of one of the families that pair, whatever the exclusion list say (Revalidate use plain type check).
    internal static bool IsOneHanded(ItemDrop.ItemData item) =>
        item != null && item.m_shared != null && item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon;

    // Item made from an excluded prefab: its m_dropPrefab is that prefab (Instantiate copy), or its m_shared is the
    // prefab's own object (prefab's ItemData, ItemData.Clone of it).
    internal static bool IsExcluded(ItemDrop.ItemData item)
    {
        Refresh(ServerRules.Current);
        for (var i = 0; i < Excluded.Count; i++)
        {
            var entry = Excluded[i];
            if (ReferenceEquals(item.m_dropPrefab, entry.Prefab) || ReferenceEquals(item.m_shared, entry.Shared))
            {
                return true;
            }
        }
        return false;
    }

    // Rules or ObjectDB changed = parse list again. No ObjectDB yet (main menu) = empty set, parse again when it come.
    internal static void Refresh(DualRules rules)
    {
        var db = ObjectDB.instance;
        if (_built && ReferenceEquals(rules, _rules) && ReferenceEquals(db, _db))
        {
            return;
        }
        _built = true;
        _rules = rules;
        _db = db;
        Excluded.Clear();
        if (rules == null || db == null || string.IsNullOrEmpty(rules.ExcludedWeapons))
        {
            return;
        }
        foreach (var raw in rules.ExcludedWeapons.Split(','))
        {
            var name = raw.Trim();
            if (name.Length == 0)
            {
                continue;
            }
            var prefab = db.GetItemPrefab(name);
            var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null)
            {
                if (Warned.Add(name))
                {
                    Log.Warning($"ExcludedWeapons: no item named '{name}' in this game; it is ignored. Use prefab "
                                + "names as the spawn command does (for example AxeBronze).");
                }
                continue;
            }
            if (!Contains(prefab))
            {
                Excluded.Add(new Entry { Prefab = prefab, Shared = drop.m_itemData.m_shared });
            }
        }
        if (Excluded.Count > 0)
        {
            Log.Debug($"{Excluded.Count} weapon(s) excluded from dual wielding.");
        }
    }

    private static bool Contains(GameObject prefab)
    {
        for (var i = 0; i < Excluded.Count; i++)
        {
            if (ReferenceEquals(Excluded[i].Prefab, prefab))
            {
                return true;
            }
        }
        return false;
    }

    // OnDeactivated: forget everything (next activation parse again).
    internal static void Clear()
    {
        _built = false;
        _rules = null;
        _db = null;
        Excluded.Clear();
    }
}
