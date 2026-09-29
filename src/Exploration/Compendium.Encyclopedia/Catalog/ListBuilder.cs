using System.Collections.Generic;
using UnityEngine;

namespace MC.Exploration.CompendiumEncyclopediaMod;

/// <summary>Kind of list row.</summary>
internal enum ListRowKind : byte
{
    /// <summary>Sub-group header (orange, not clickable).</summary>
    Header,

    /// <summary>Entry row (icon + name, clickable).</summary>
    Entry,
}

/// <summary>
/// One row of the left list, already spoiler-safe: <see cref="Text"/> = real name only when known, else "???";
/// <see cref="Icon"/> null unless <see cref="IconKind"/> is Sprite. UI bind rows from these, never from the entry.
/// </summary>
internal readonly struct ListRow
{
    internal readonly ListRowKind Kind;
    internal readonly string Text;
    internal readonly Entry Entry;
    internal readonly bool Known;
    internal readonly IconKind IconKind;
    internal readonly Sprite Icon;

    internal ListRow(ListRowKind kind, string text, Entry entry, bool known, IconKind iconKind, Sprite icon)
    {
        Kind = kind;
        Text = text;
        Entry = entry;
        Known = known;
        IconKind = iconKind;
        Icon = icon;
    }

    internal static ListRow Header(string text) => new ListRow(ListRowKind.Header, text, null, false, IconKind.None, null);

    internal static ListRow ForEntry(Entry e, Knowledge k)
    {
        var kind = Presentation.Icon(e, k, out var sprite);
        return new ListRow(ListRowKind.Entry, Presentation.Name(e, k), e, k.IsKnown(e), kind, sprite);
    }
}

/// <summary>
/// Me make the rows of the list (design 3.2.6-3.2.7, 3.8 search). Per sub-group: discovered entries A→Z (tags out,
/// invariant, ignore case and accents), then undiscovered ones in game order (never by name: position must not tell
/// the first letter). Hidden-until-known entries appear only once known. Empty sub-groups get no header.
/// </summary>
internal static class ListBuilder
{
    /// <summary>Rows of one tab. <paramref name="showUndiscovered"/> false = only discovered entries.</summary>
    internal static List<ListRow> BuildTab(Catalog c, Knowledge k, CatalogTab tab, bool showUndiscovered)
    {
        var rows = new List<ListRow>(c.CountOf(tab) + 16);
        var known = new List<Entry>();
        var unknown = new List<Entry>();
        foreach (var g in c.SubGroups(tab))
        {
            known.Clear();
            unknown.Clear();
            foreach (var e in g.Entries)
            {
                if (k.IsKnown(e))
                {
                    known.Add(e);
                }
                else if (showUndiscovered && !e.HiddenUntilKnown)
                {
                    unknown.Add(e); // already in game order
                }
            }
            if (known.Count == 0 && unknown.Count == 0)
            {
                continue;
            }
            var header = g.HeaderText(k);
            if (header.Length > 0)
            {
                rows.Add(ListRow.Header(header));
            }
            known.Sort((a, b) => Names.CompareNames(a.SortKey, b.SortKey));
            foreach (var e in known)
            {
                rows.Add(ListRow.ForEntry(e, k));
            }
            foreach (var e in unknown)
            {
                rows.Add(ListRow.ForEntry(e, k));
            }
        }
        return rows;
    }

    /// <summary>
    /// Search across every tab: one header per tab with matches, discovered entries only (goal 4: an undiscovered
    /// entry never match). Match = normalized query inside translated name or prefab name. Empty query = no rows.
    /// </summary>
    internal static List<ListRow> BuildSearch(Catalog c, Knowledge k, string query)
    {
        var rows = new List<ListRow>();
        var q = Names.SearchKey(query ?? "");
        if (q.Length == 0)
        {
            return rows;
        }
        var hits = new List<Entry>();
        for (var t = 0; t < Tabs.Count; t++)
        {
            var tab = (CatalogTab)t;
            hits.Clear();
            foreach (var e in c.EntriesOf(tab))
            {
                if (k.IsKnown(e) && (e.SearchKey.Contains(q) || e.PrefabSearchKey.Contains(q)))
                {
                    hits.Add(e);
                }
            }
            if (hits.Count == 0)
            {
                continue;
            }
            rows.Add(ListRow.Header(Tabs.LocalizedLabel(tab)));
            hits.Sort((a, b) => Names.CompareNames(a.SortKey, b.SortKey));
            foreach (var e in hits)
            {
                rows.Add(ListRow.ForEntry(e, k));
            }
        }
        return rows;
    }

    /// <summary>Discovered / listed counts of one tab (for a per-tab counter, optional).</summary>
    internal static void CountTab(Catalog c, Knowledge k, CatalogTab tab, out int discovered, out int listed)
    {
        discovered = 0;
        listed = 0;
        foreach (var e in c.EntriesOf(tab))
        {
            var known = k.IsKnown(e);
            if (known)
            {
                discovered++;
            }
            if (known || !e.HiddenUntilKnown)
            {
                listed++;
            }
        }
    }
}
