using System;
using System.Collections.Generic;
using UnityEngine;

namespace MC.Exploration.CompendiumEncyclopediaMod;

/// <summary>
/// Me = what exist in this world's game data: entries, tabs, sub-groups and relation indexes. No player data.
/// Built by <see cref="CatalogBuilder"/> over frames, published by <see cref="CatalogService"/> only when complete, never
/// changed after. Get it with <see cref="CatalogService.EnsureReady"/>.
/// </summary>
internal sealed class Catalog
{
    /// <summary>Every entry (items, then pieces, then creatures); <see cref="Entry.Index"/> = position.</summary>
    internal readonly List<Entry> Entries = new List<Entry>();

    internal readonly Dictionary<string, Entry> ItemsByToken = new Dictionary<string, Entry>(StringComparer.Ordinal);
    internal readonly Dictionary<string, Entry> ItemsByPrefab = new Dictionary<string, Entry>(StringComparer.Ordinal);
    internal readonly Dictionary<string, Entry> PiecesByName = new Dictionary<string, Entry>(StringComparer.Ordinal);
    internal readonly Dictionary<string, Entry> PiecesByPrefab = new Dictionary<string, Entry>(StringComparer.Ordinal);

    /// <summary>CraftingStation.m_name → station piece entry.</summary>
    internal readonly Dictionary<string, Entry> StationsByName = new Dictionary<string, Entry>(StringComparer.Ordinal);

    internal readonly Dictionary<string, Entry> CreaturesByName = new Dictionary<string, Entry>(StringComparer.Ordinal);
    internal readonly Dictionary<string, Entry> CreaturesByPrefab = new Dictionary<string, Entry>(StringComparer.Ordinal);

    /// <summary>Recipes enabled only by a seasonal group (shown with "Seasonal").</summary>
    internal readonly HashSet<Recipe> SeasonalRecipes = new HashSet<Recipe>();

    /// <summary>Piece tables found (tool order). Fingerprint sum their sizes.</summary>
    internal readonly List<PieceTable> PieceTables = new List<PieceTable>();

    private readonly List<SubGroup>[] _groups = new List<SubGroup>[Tabs.Count];

    internal Fingerprint Fingerprint;

    // Counts for log and self tests.
    internal int ItemCount;
    internal int PieceCount;
    internal int CreatureCount;
    internal int HiddenCount;
    internal int NoSourceCount;
    internal int ExcludedItemPrefabs;
    internal int TraderCount;
    internal int UpgradeOnlyRecipes;

    /// <summary>Sum of the piece table sizes the pieces stage read (fingerprint of what was built from).</summary>
    internal int PieceSumSeen;
    internal long BuildMs;
    internal int BuildFrames;

    internal Catalog()
    {
        for (var i = 0; i < _groups.Length; i++)
        {
            _groups[i] = new List<SubGroup>();
        }
    }

    /// <summary>Sub-groups of a tab in display order. Entries inside in game order.</summary>
    internal IReadOnlyList<SubGroup> SubGroups(CatalogTab tab) => _groups[(int)tab];

    internal List<SubGroup> GroupsMutable(CatalogTab tab) => _groups[(int)tab];

    /// <summary>Entries of a tab (every sub-group, display order, hidden ones included).</summary>
    internal IEnumerable<Entry> EntriesOf(CatalogTab tab)
    {
        foreach (var g in _groups[(int)tab])
        {
            foreach (var e in g.Entries)
            {
                yield return e;
            }
        }
    }

    internal int CountOf(CatalogTab tab)
    {
        var n = 0;
        foreach (var g in _groups[(int)tab])
        {
            n += g.Entries.Count;
        }
        return n;
    }

    internal int HiddenCountOf(EntryKind kind)
    {
        var n = 0;
        foreach (var e in Entries)
        {
            if (e.Kind == kind && e.HiddenUntilKnown)
            {
                n++;
            }
        }
        return n;
    }

    internal Entry FindItem(string token) => token != null && ItemsByToken.TryGetValue(token, out var e) ? e : null;

    internal Entry FindItemByPrefab(string prefabName) =>
        prefabName != null && ItemsByPrefab.TryGetValue(prefabName, out var e) ? e : null;

    internal Entry FindPiece(string name) => name != null && PiecesByName.TryGetValue(name, out var e) ? e : null;

    internal Entry FindStation(string stationName) =>
        stationName != null && StationsByName.TryGetValue(stationName, out var e) ? e : null;

    internal Entry FindCreature(string name) => name != null && CreaturesByName.TryGetValue(name, out var e) ? e : null;

    /// <summary>Item entry of a game object (prefab name first, then token). Null = not listed.</summary>
    internal Entry ItemOf(GameObject go)
    {
        if (go == null)
        {
            return null;
        }
        if (ItemsByPrefab.TryGetValue(go.name, out var e))
        {
            return e;
        }
        return go.TryGetComponent<ItemDrop>(out var drop) && drop.m_itemData != null && drop.m_itemData.m_shared != null
            ? FindItem(drop.m_itemData.m_shared.m_name)
            : null;
    }

    internal Entry ItemOf(ItemDrop drop) => drop == null ? null : ItemOf(drop.gameObject);

    /// <summary>Station piece of a crafting station component (null station or not listed = null).</summary>
    internal Entry StationOf(CraftingStation station) => station == null ? null : FindStation(station.m_name);

    /// <summary>Same world data as when built? (else rebuild).</summary>
    internal bool Matches(Fingerprint now) => Fingerprint.Equals(now);
}

/// <summary>
/// Me = cheap check that game data did not change: ObjectDB and ZNetScene instances (new world = new ones), item,
/// recipe, prefab counts, sum of piece table sizes. In-place edits (recipe amounts) need no rebuild: details read live
/// objects at click time.
/// </summary>
internal readonly struct Fingerprint : IEquatable<Fingerprint>
{
    internal readonly ObjectDB Db;
    internal readonly ZNetScene Scene;
    internal readonly int Items;
    internal readonly int Recipes;
    internal readonly int Prefabs;
    internal readonly int PieceSum;

    private Fingerprint(ObjectDB db, ZNetScene scene, int items, int recipes, int prefabs, int pieceSum)
    {
        Db = db;
        Scene = scene;
        Items = items;
        Recipes = recipes;
        Prefabs = prefabs;
        PieceSum = pieceSum;
    }

    /// <summary>Fingerprint now. Tables = the catalog's piece tables (null = none known yet).</summary>
    internal static Fingerprint Take(ObjectDB db, ZNetScene scene, List<PieceTable> tables)
    {
        var sum = 0;
        if (tables != null)
        {
            foreach (var t in tables)
            {
                if (t != null && t.m_pieces != null)
                {
                    sum += t.m_pieces.Count;
                }
            }
        }
        return new Fingerprint(db, scene,
            db != null && db.m_items != null ? db.m_items.Count : -1,
            db != null && db.m_recipes != null ? db.m_recipes.Count : -1,
            scene != null && scene.m_prefabs != null ? scene.m_prefabs.Count : -1,
            sum);
    }

    /// <summary>Same world and counts, other piece sum (the sizes the build really read).</summary>
    internal Fingerprint WithPieceSum(int pieceSum) => new Fingerprint(Db, Scene, Items, Recipes, Prefabs, pieceSum);

    public bool Equals(Fingerprint o) =>
        ReferenceEquals(Db, o.Db) && ReferenceEquals(Scene, o.Scene) && Items == o.Items && Recipes == o.Recipes
        && Prefabs == o.Prefabs && PieceSum == o.PieceSum;

    public override bool Equals(object obj) => obj is Fingerprint f && Equals(f);

    public override int GetHashCode() => Items ^ (Recipes << 8) ^ (Prefabs << 16) ^ PieceSum;

    public override string ToString() => $"items {Items}, recipes {Recipes}, prefabs {Prefabs}, pieces {PieceSum}";
}
