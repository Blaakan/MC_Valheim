using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.CompendiumEncyclopediaMod;

/// <summary>What an entry is.</summary>
internal enum EntryKind : byte
{
    Item,
    Piece,
    Creature,
}

/// <summary>Tabs of the window, in strip order. <see cref="Tabs.Count"/> = how many.</summary>
internal enum CatalogTab : byte
{
    Weapons,
    Armor,
    Tools,
    Food,
    Materials,
    Trophies,
    Building,
    Creatures,
}

/// <summary>How UI draw icon slot. Never real sprite for undiscovered thing (goal 4).</summary>
internal enum IconKind : byte
{
    /// <summary>Show the sprite given.</summary>
    Sprite,

    /// <summary>Undiscovered: "?" mark, no sprite.</summary>
    Unknown,

    /// <summary>Discovered creature with no discovered trophy: generic creature icon (paw print, <see cref="PawIcon"/>).</summary>
    GenericCreature,

    /// <summary>No icon (biome references).</summary>
    None,
}

/// <summary>Tab labels and helpers.</summary>
internal static class Tabs
{
    internal const int Count = 8;

    /// <summary>Label token/text of tab (localize before show).</summary>
    internal static string Label(CatalogTab tab)
    {
        switch (tab)
        {
            case CatalogTab.Weapons:
                return Labels.TabWeapons;
            case CatalogTab.Armor:
                return Labels.TabArmor;
            case CatalogTab.Tools:
                return Labels.TabTools;
            case CatalogTab.Food:
                return Labels.TabFood;
            case CatalogTab.Materials:
                return Labels.TabMaterials;
            case CatalogTab.Trophies:
                return Labels.TabTrophies;
            case CatalogTab.Building:
                return Labels.TabBuilding;
            default:
                return Labels.TabCreatures;
        }
    }

    /// <summary>Label in game language.</summary>
    internal static string LocalizedLabel(CatalogTab tab) => Names.Localize(Label(tab));
}

/// <summary>
/// Me = header block inside a tab (Swords, Helmets, Hammer · Crafting, Meadows...). Entries in game order; list
/// builder sort them per knowledge. Header text can be spoiler (tool name, biome), so ask <see cref="HeaderText"/> with
/// knowledge, never print <see cref="LabelToken"/> alone.
/// </summary>
internal sealed class SubGroup
{
    internal CatalogTab Tab;

    /// <summary>Order inside tab (lower first).</summary>
    internal int Order;

    /// <summary>Token or English text. Empty = no header row (Trophies tab).</summary>
    internal string LabelToken = "";

    /// <summary>Building: tool item whose piece table hold these pieces (name go through knowledge).</summary>
    internal Entry Tool;

    /// <summary>Creatures: first habitat biome. None = habitat unknown.</summary>
    internal Heightmap.Biome Biome;

    internal bool IsBiomeGroup;

    /// <summary>Entries in game order.</summary>
    internal readonly List<Entry> Entries = new List<Entry>();

    /// <summary>
    /// Header text resolved against knowledge: biome name only when biome known (else "???"); tool name only when tool
    /// known. Empty = no header row.
    /// </summary>
    internal string HeaderText(Knowledge k)
    {
        if (IsBiomeGroup)
        {
            return Biome == Heightmap.Biome.None ? Names.Localize(Labels.GroupHabitatUnknown) : Presentation.BiomeName(Biome, k);
        }
        var label = Names.Localize(LabelToken);
        if (Tool == null)
        {
            return label;
        }
        var tool = Presentation.Name(Tool, k);
        return label.Length == 0 ? tool : tool + Labels.Separator + label;
    }

    /// <summary>Label without tool or biome (for subtitles and logs). Never spoiler.</summary>
    internal string PlainLabel => IsBiomeGroup ? "" : Names.Localize(LabelToken);
}

/// <summary>
/// Me = one row of the catalog: one item (per token), one build piece (per m_name) or one creature (per m_name).
/// Never show <see cref="DisplayName"/> or <see cref="Icon"/> without knowledge check: use
/// <see cref="Presentation.Name"/> / <see cref="Presentation.Icon"/>.
/// </summary>
internal sealed class Entry
{
    /// <summary>Position in <see cref="Catalog.Entries"/> (dense, from 0). Knowledge index by it.</summary>
    internal int Index;

    internal EntryKind Kind;

    /// <summary>Item token m_shared.m_name / Piece.m_name / Character.m_name (kill stats key).</summary>
    internal string Key;

    /// <summary>Token to localize for the name (same as Key).</summary>
    internal string NameToken;

    /// <summary>Representative prefab (live game object: read fields at click time).</summary>
    internal GameObject Prefab;

    internal string PrefabName;

    internal CatalogTab Tab;
    internal SubGroup SubGroup;

    /// <summary>ObjectDB order (items), table order (pieces), ZNetScene order (creatures). Undiscovered rows use it.</summary>
    internal int GameOrder;

    /// <summary>Real sprite (items, pieces). Creatures: null (icon = trophy rule).</summary>
    internal Sprite Icon;

    /// <summary>Listed only once discovered (no "???" row). Rule in <see cref="HiddenRule"/>.</summary>
    internal bool HiddenUntilKnown;

    internal string HiddenRule;

    /// <summary>Item: every recipe seasonal. Piece: seasonal piece.</summary>
    internal bool Seasonal;

    internal ItemInfo Item;
    internal PieceInfo Piece;
    internal CreatureInfo Creature;

    private int _nameStamp;
    private string _displayName;
    private string _sortKey;
    private string _searchKey;
    private string _prefabSearchKey;

    /// <summary>Translated name (may carry TMP tags). Cached per language. Spoiler: check knowledge first.</summary>
    internal string DisplayName
    {
        get
        {
            EnsureNames();
            return _displayName;
        }
    }

    /// <summary>Name without tags, for sort compare.</summary>
    internal string SortKey
    {
        get
        {
            EnsureNames();
            return _sortKey;
        }
    }

    /// <summary>Normalized name for search (lower-case, no space, no tags).</summary>
    internal string SearchKey
    {
        get
        {
            EnsureNames();
            return _searchKey;
        }
    }

    /// <summary>Normalized prefab name for search.</summary>
    internal string PrefabSearchKey => _prefabSearchKey ??= Names.SearchKey(PrefabName ?? "");

    private void EnsureNames()
    {
        if (_nameStamp == Names.Stamp && _displayName != null)
        {
            return;
        }
        _nameStamp = Names.Stamp;
        var name = Names.Localize(NameToken);
        _displayName = Names.IsMissing(name) ? PrefabName ?? NameToken ?? "" : name;
        _sortKey = Names.StripTags(_displayName);
        _searchKey = Names.SearchKey(_displayName);
    }

    public override string ToString() => $"{Kind} {Key} ({PrefabName})";
}

/// <summary>Where an item is gathered from (details "Picked / Mined / Chopped / Found by breaking").</summary>
internal enum GatherKind : byte
{
    Picked,
    Mined,
    Chopped,
    Broken,
}

/// <summary>One gathering way of an item: biomes known from vegetation (None = seen only outside vegetation).</summary>
internal sealed class GatherInfo
{
    internal GatherKind Kind;
    internal Heightmap.Biome Biomes;
}

/// <summary>Creature drop: live Drop object (numbers read at click time).</summary>
internal sealed class DropLink
{
    internal Entry Creature;
    internal Entry Item;
    internal CharacterDrop.Drop Drop;
}

/// <summary>Smelter / kiln / cooking station / fermenter conversion. Station = piece entry (null = not a listed piece).</summary>
internal sealed class Conversion
{
    internal Entry From;
    internal Entry To;
    internal Entry Station;
    internal string StationPrefab;
}

/// <summary>What a "used in" line point at.</summary>
internal enum UseKind : byte
{
    Recipe,
    Piece,
    Conversion,
}

/// <summary>Reverse index row: this item is used to make <see cref="Target"/> (at <see cref="Station"/> for conversions).</summary>
internal sealed class UseLink
{
    internal UseKind Kind;
    internal Entry Target;
    internal Entry Station;
}

/// <summary>Item data: recipes, sources, uses. Live game objects inside (Recipe, Drop): read their fields at click time.</summary>
internal sealed class ItemInfo
{
    /// <summary>Every included prefab of the token, ObjectDB order.</summary>
    internal readonly List<GameObject> Prefabs = new List<GameObject>();

    internal ItemDrop Drop;
    internal ItemKind Kind;

    /// <summary>Enabled or seasonal recipes making this item (ObjectDB order).</summary>
    internal readonly List<Recipe> Recipes = new List<Recipe>();

    internal readonly List<DropLink> DroppedBy = new List<DropLink>();
    internal readonly List<GatherInfo> Gathered = new List<GatherInfo>();
    internal bool InChests;
    internal readonly List<Conversion> MadeFrom = new List<Conversion>();
    internal readonly List<Entry> ProducedBy = new List<Entry>();

    /// <summary>Creatures whose Procreation give birth to this item (tamed hen: egg).</summary>
    internal readonly List<Entry> LaidBy = new List<Entry>();

    /// <summary>Produced by a beehive/sap collector prefab that is not a listed piece.</summary>
    internal bool ProducedUnlisted;

    internal bool Caught;
    internal Heightmap.Biome CaughtIn;
    internal int SoldByTraders;

    /// <summary>Dish placed like a piece: tool item whose piece table hold it (may be the item itself).</summary>
    internal Entry PlaceableWith;

    internal readonly List<UseLink> UsedIn = new List<UseLink>();

    /// <summary>Creatures that eat it for taming.</summary>
    internal readonly List<Entry> Tames = new List<Entry>();

    /// <summary>Crafted, dropped, gathered, chest, processed, produced, laid, caught or sold.</summary>
    internal bool HasSource;

    /// <summary>A recipe the crafting panel offer (not upgrade-only, m_noCraftOnlyUpgrade).</summary>
    internal bool Craftable;

    /// <summary>Source other than a recipe.</summary>
    internal bool HasNonRecipeSource;

    internal GatherInfo Gather(GatherKind kind)
    {
        foreach (var g in Gathered)
        {
            if (g.Kind == kind)
            {
                return g;
            }
        }
        var info = new GatherInfo { Kind = kind };
        Gathered.Add(info);
        return info;
    }
}

/// <summary>Piece data. Station pieces also carry recipes, upgrades and conversions.</summary>
internal sealed class PieceInfo
{
    internal Piece Piece;

    /// <summary>Tool item whose piece table hold it (first table wins).</summary>
    internal Entry Tool;

    internal string CategoryLabel = "";

    /// <summary>CraftingStation.m_name on this prefab (null = not a station). m_knownStations key.</summary>
    internal string StationName;

    /// <summary>Station extension: the station it upgrades.</summary>
    internal Entry ExtensionOf;

    internal string ExtensionOfStation;

    internal readonly List<Entry> Extensions = new List<Entry>();

    /// <summary>Recipes whose crafting station is this station.</summary>
    internal readonly List<Recipe> RecipesHere = new List<Recipe>();

    internal readonly List<Conversion> Conversions = new List<Conversion>();

    /// <summary>Beehive / sap collector: items it produce.</summary>
    internal readonly List<Entry> Produces = new List<Entry>();
}

/// <summary>Creature group data (all prefabs sharing m_name).</summary>
internal sealed class CreatureInfo
{
    internal readonly List<GameObject> Prefabs = new List<GameObject>();
    internal Character Character;
    internal readonly List<DropLink> Drops = new List<DropLink>();

    /// <summary>First trophy-type item it drop (null = none). Icon rule and discovery by trophy.</summary>
    internal Entry Trophy;

    internal Heightmap.Biome Habitat;
    internal string HabitatSource = "";

    /// <summary>Bosses whose defeat key every world spawn entry of this creature need.</summary>
    internal readonly List<Entry> AfterBosses = new List<Entry>();

    internal bool Tameable;
    internal readonly List<Entry> Eats = new List<Entry>();
    internal Entry Saddle;
    internal bool Boss;
    internal string DefeatKey;
}
