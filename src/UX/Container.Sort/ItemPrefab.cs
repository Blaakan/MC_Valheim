using System;
using System.Collections.Generic;

namespace MC.UX.ContainerSortMod;

// Me = prefab name of an item ("Wood", "SwordBronze"): the name spawn command and biome table use.
// Item from save, chest or ground have m_dropPrefab (ItemDrop.Awake set it). Rare item without it (other mod made
// it by hand): me look up ObjectDB by item token ("$item_wood"), first item with that token win.
internal static class ItemPrefab
{
    private static readonly Dictionary<string, string> ByToken = new Dictionary<string, string>(StringComparer.Ordinal);
    private static ObjectDB _builtFor;
    private static int _builtCount = -1;

    internal static string Name(ItemDrop.ItemData item)
    {
        if (item == null)
        {
            return null;
        }
        if (item.m_dropPrefab != null)
        {
            return item.m_dropPrefab.name;
        }
        var token = item.m_shared != null ? item.m_shared.m_name : null;
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }
        Refresh();
        return ByToken.TryGetValue(token, out var name) ? name : null;
    }

    internal static void Clear()
    {
        ByToken.Clear();
        _builtFor = null;
        _builtCount = -1;
    }

    // New world = new ObjectDB. Mods may add items late: count change = rebuild too.
    private static void Refresh()
    {
        var db = ObjectDB.instance;
        if (db == null || db.m_items == null)
        {
            Clear();
            return;
        }
        if (ReferenceEquals(_builtFor, db) && _builtCount == db.m_items.Count)
        {
            return;
        }
        ByToken.Clear();
        _builtFor = db;
        _builtCount = db.m_items.Count;
        foreach (var go in db.m_items)
        {
            if (go == null)
            {
                continue;
            }
            var drop = go.GetComponent<ItemDrop>();
            var token = drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null
                ? drop.m_itemData.m_shared.m_name
                : null;
            if (!string.IsNullOrEmpty(token) && !ByToken.ContainsKey(token))
            {
                ByToken[token] = go.name;
            }
        }
    }
}
