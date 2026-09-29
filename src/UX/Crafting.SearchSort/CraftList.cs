using System;
using System.Collections.Generic;
using System.Text;
using MC.Shared;
using UnityEngine;

namespace MC.UX.CraftingSearchSortMod;

// Me = the UpdateRecipeList postfix work. Vanilla just built, sorted and placed every row (m_availableRecipes).
// Me: count categories on full list, drop rows that miss the search, reorder the rest by chosen sort (stable),
// place rows again, size the list. Then vanilla UpdateCraftingPanel pick selection on the final list.
// Filter and order go into buffers first; list is only written at the end, so a throw leave vanilla list usable.
internal static class CraftList
{
    // Per rebuild, reused: no per-row allocation.
    private static readonly List<InventoryGui.RecipeDataPair> Ordered = new List<InventoryGui.RecipeDataPair>(128);
    private static readonly List<GameObject> Dropped = new List<GameObject>(128);
    private static readonly List<int> Kept = new List<int>(128);
    private static readonly Comparison<int> ByName = CompareByName;
    private static StringComparer _nameComparer = StringComparer.InvariantCultureIgnoreCase;
    private static int[] _first = new int[128];
    private static int[] _second = new int[128];
    private static bool[] _named = new bool[128];
    private static string[] _names = new string[128];
    private static int[] _quality = new int[128];

    // Rows per option on the full tab list (before search), and icon of first row: the menu show them.
    internal static readonly int[] Counts = new int[RecipeCategory.Count];
    internal static readonly Sprite[] Icons = new Sprite[RecipeCategory.Count];

    // Debug dump done for these "station/tab" keys (per InventoryGui).
    private static readonly HashSet<string> Dumped = new HashSet<string>();

    internal static void Clear()
    {
        Array.Clear(Counts, 0, Counts.Length);
        Array.Clear(Icons, 0, Icons.Length);
        Array.Clear(_names, 0, _names.Length);
        Dumped.Clear();
    }

    internal static void Organize(InventoryGui gui)
    {
        var player = Player.m_localPlayer;
        if (!CraftSearch.Active || gui == null || player == null)
        {
            return;
        }

        SearchUi.Ensure(gui);
        var station = player.GetCurrentCraftingStation();
        var atStation = station != null;
        var upgrader = atStation && station.m_upgrader;
        CraftSearch.UpdateStation(player, station);

        var rows = gui.m_availableRecipes;
        var count = rows.Count;
        Grow(count);

        // 1. Classify + count on full list. Foreign row (null recipe or item, added by another mod) = Other,
        //    never filtered, nothing on it dereferenced.
        Array.Clear(Counts, 0, Counts.Length);
        Array.Clear(Icons, 0, Icons.Length);
        for (var i = 0; i < count; i++)
        {
            var row = rows[i];
            var recipe = row.Recipe;
            var named = recipe != null && recipe.m_item != null && recipe.m_item.m_itemData?.m_shared != null;
            _named[i] = named;
            int first, second;
            if (named)
            {
                var shared = recipe.m_item.m_itemData.m_shared;
                RecipeCategory.Of(ItemKinds.Classify(shared), shared.m_skillType, out first, out second);
            }
            else
            {
                first = RecipeCategory.Other;
                second = -1;
            }
            _first[i] = first;
            _second[i] = second;
            Count(first, named ? recipe : null);
            if (second >= 0)
            {
                Count(second, recipe);
            }
        }

        DumpRows(gui, rows);

        // 2. Filter (term not empty).
        var term = CraftSearch.Term;
        Kept.Clear();
        Dropped.Clear();
        for (var i = 0; i < count; i++)
        {
            if (term.Length == 0 || !_named[i])
            {
                Kept.Add(i);
                continue;
            }
            var row = rows[i];
            var quality = row.ItemData == null ? 1 : row.ItemData.m_quality + 1;
            if (RecipeTerms.Matches(row.Recipe, quality, term, atStation, upgrader, player))
            {
                Kept.Add(i);
            }
            else
            {
                Dropped.Add(row.InterfaceElement);
            }
        }

        // 3. Order kept rows.
        var option = CraftSearch.Option;
        var changed = Dropped.Count > 0;
        if (option == RecipeCategory.Name)
        {
            _nameComparer = StringComparer.CurrentCultureIgnoreCase; // getter may allocate: once per sort
            for (var k = 0; k < Kept.Count; k++)
            {
                var i = Kept[k];
                if (_named[i])
                {
                    var row = rows[i];
                    _names[i] = RecipeTerms.DisplayName(row.Recipe);
                    _quality[i] = row.ItemData == null ? 0 : row.ItemData.m_quality;
                }
            }
            Kept.Sort(ByName);
            changed = true;
        }

        Ordered.Clear();
        if (option != RecipeCategory.Default && option != RecipeCategory.Name)
        {
            // Stable partition: rows of the option in their order, then all others in their order. Never List.Sort.
            for (var k = 0; k < Kept.Count; k++)
            {
                var i = Kept[k];
                if (_first[i] == option || _second[i] == option)
                {
                    Ordered.Add(rows[Kept[k]]);
                }
            }
            for (var k = 0; k < Kept.Count; k++)
            {
                var i = Kept[k];
                if (_first[i] != option && _second[i] != option)
                {
                    Ordered.Add(rows[Kept[k]]);
                }
            }
            changed = true;
        }
        else
        {
            for (var k = 0; k < Kept.Count; k++)
            {
                Ordered.Add(rows[Kept[k]]);
            }
        }

        // 4. Write back, only now.
        if (changed)
        {
            rows.Clear();
            rows.AddRange(Ordered);
            var space = gui.m_recipeListSpace;
            for (var i = 0; i < rows.Count; i++)
            {
                var go = rows[i].InterfaceElement;
                if (go != null && go.transform is RectTransform rt)
                {
                    rt.anchoredPosition = new Vector2(0f, -i * space);
                }
            }
            // Hidden now, gone at end of frame (like vanilla own rows).
            for (var i = 0; i < Dropped.Count; i++)
            {
                var go = Dropped[i];
                if (go != null)
                {
                    go.SetActive(false);
                    UnityEngine.Object.Destroy(go);
                }
            }
        }
        Ordered.Clear();
        Dropped.Clear();

        // Always: vanilla sized content before us, and on first build with old base size (Ensure lowered it after).
        if (gui.m_recipeListRoot != null)
        {
            var height = Mathf.Max(gui.m_recipeListBaseSize, rows.Count * gui.m_recipeListSpace);
            gui.m_recipeListRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }

        // Any rebuild the menu did not start close it (counts and icons changed); menu close itself before its rebuild.
        SearchUi.CloseMenu();
        SearchUi.RefreshButtonLabel();
        CraftSearch.OnListBuilt();
    }

    private static void Count(int option, Recipe recipe)
    {
        Counts[option]++;
        if (Icons[option] == null && recipe != null)
        {
            Icons[option] = recipe.m_item.m_itemData.GetIcon();
        }
    }

    // Name sort: named rows by shown name, then higher quality first (vanilla byLevel), then old place (stable).
    // Craftable and greyed rows mix (Decision 1). Foreign rows after named ones.
    private static int CompareByName(int a, int b)
    {
        if (a == b)
        {
            return 0;
        }
        var na = _named[a];
        var nb = _named[b];
        if (na != nb)
        {
            return na ? -1 : 1;
        }
        if (na)
        {
            var c = _nameComparer.Compare(_names[a], _names[b]);
            if (c != 0)
            {
                return c;
            }
            if (_quality[a] != _quality[b])
            {
                return _quality[b].CompareTo(_quality[a]);
            }
        }
        return a.CompareTo(b);
    }

    private static void Grow(int count)
    {
        if (_first.Length >= count)
        {
            return;
        }
        var size = Math.Max(count, _first.Length * 2);
        _first = new int[size];
        _second = new int[size];
        _named = new bool[size];
        _names = new string[size];
        _quality = new int[size];
    }

    private static string DumpKey(InventoryGui gui) => CraftSearch.StationKey + (gui.InCraftTab() ? "/craft" : "/upgrade");

    // Debug aid: first build per station and tab, one line per row with the classifier inputs and result, to
    // check unverified prefab values (tools, torches, tankards, food) in game. Same ItemKind format as Sort Chest.
    private static void DumpRows(InventoryGui gui, List<InventoryGui.RecipeDataPair> rows)
    {
        var key = DumpKey(gui);
        if (!Dumped.Add(key))
        {
            return;
        }
        try
        {
            var sb = new StringBuilder();
            sb.Append("Crafting list at station '").Append(CraftSearch.StationKey).Append("' (")
                .Append(gui.InCraftTab() ? "craft" : "upgrade").Append(" tab), ").Append(rows.Count).Append(" rows:");
            for (var i = 0; i < rows.Count; i++)
            {
                var recipe = rows[i].Recipe;
                sb.Append("\n  ");
                if (recipe == null || recipe.m_item == null || recipe.m_item.m_itemData?.m_shared == null)
                {
                    sb.Append("(foreign row) -> Other");
                    continue;
                }
                var s = recipe.m_item.m_itemData.m_shared;
                var kind = ItemKinds.Classify(s);
                RecipeCategory.Of(kind, s.m_skillType, out var first, out var second);
                sb.Append(recipe.m_item.name).Append(' ').Append(s.m_itemType).Append(' ').Append(s.m_skillType)
                    .Append(' ').Append(s.m_animationState)
                    .Append(" food=").Append(s.m_food).Append('/').Append(s.m_foodStamina).Append('/').Append(s.m_foodEitr)
                    .Append(" -> ").Append(kind).Append(" -> ").Append(RecipeCategory.Id(first));
                if (second >= 0)
                {
                    sb.Append(", ").Append(RecipeCategory.Id(second));
                }
            }
            Log.Debug(sb.ToString());
        }
        catch (Exception e)
        {
            Log.Debug($"Crafting list dump failed: {e.Message}");
        }
    }
}
