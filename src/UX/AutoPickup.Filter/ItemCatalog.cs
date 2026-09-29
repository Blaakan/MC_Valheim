using System;
using System.Collections.Generic;

namespace MC.UX.AutoPickupFilterMod;

// Me = item names from ObjectDB: exact spawn name from any casing (commands, config defaults), display token of a
// key (tooltip, messages), sorted name list (console Tab). Rebuilt only when ObjectDB change (new instance or item
// count change): no cost per Tab press or per frame.
internal static class ItemCatalog
{
    private static ObjectDB _db;
    private static int _count = -1;
    private static readonly Dictionary<string, string> ExactByAnyCase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> Names = new List<string>();
    private static readonly Dictionary<string, string> TokenByKey = new Dictionary<string, string>(StringComparer.Ordinal);

    // Bump each rebuild: command Tab list rebuild when me change.
    internal static int Stamp { get; private set; }

    private static bool Refresh()
    {
        var db = ObjectDB.instance;
        if (db == null || db.m_items == null)
        {
            return false;
        }
        if (ReferenceEquals(db, _db) && db.m_items.Count == _count)
        {
            return true;
        }
        _db = db;
        _count = db.m_items.Count;
        ExactByAnyCase.Clear();
        Names.Clear();
        TokenByKey.Clear();
        foreach (var go in db.m_items)
        {
            if (go == null)
            {
                continue;
            }
            var name = go.name;
            if (!ExactByAnyCase.ContainsKey(name))
            {
                ExactByAnyCase[name] = name;
                Names.Add(name);
            }
        }
        Names.Sort(StringComparer.OrdinalIgnoreCase);
        Stamp++;
        return true;
    }

    // Spawn name as ObjectDB spell it, from any casing. Null = not an item (or ObjectDB not ready).
    internal static string Resolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !Refresh())
        {
            return null;
        }
        return ExactByAnyCase.TryGetValue(name.Trim(), out var exact) ? exact : null;
    }

    // Every item spawn name, sorted. Same list instance each call (read only for callers).
    internal static List<string> AllNames()
    {
        Refresh();
        return Names;
    }

    // "$item_stone" for "Stone". Unknown key (item of a mod not installed) = key itself.
    internal static string Token(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return key;
        }
        if (!Refresh())
        {
            return key;
        }
        if (TokenByKey.TryGetValue(key, out var token))
        {
            return token;
        }
        token = key;
        var prefab = _db.GetItemPrefab(key);
        var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
        if (drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null
            && !string.IsNullOrEmpty(drop.m_itemData.m_shared.m_name))
        {
            token = drop.m_itemData.m_shared.m_name;
        }
        TokenByKey[key] = token;
        return token;
    }

    // Name in game language ("Stone", "Stein"...). Unknown key = key.
    internal static string DisplayName(string key)
    {
        var token = Token(key);
        var loc = Localization.instance;
        return loc != null && token != null && token.IndexOf('$') >= 0 ? loc.Localize(token) : token;
    }
}
