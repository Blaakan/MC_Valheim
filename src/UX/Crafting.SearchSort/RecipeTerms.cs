using System;
using System.Collections.Generic;
using System.Text;

namespace MC.UX.CraftingSearchSortMod;

// Me = search words of each recipe, and the name shown in its row. Rules copy vanilla build menu search
// (BuildUi.UpdateSearch + BuildUiPieceButton.RefreshSearchTerm): lower case, no spaces, "contains".
// Words = item name, prefab name, recipe name, and ingredient names. Ingredient count only when the row SHOW it
// (mirror of InventoryGui.SetupRequirementList), so every match can be seen after clicking the row.
// Cached per Recipe: game Localize cache hold only 100 strings, hundreds of names per keystroke would thrash it.
// Cache dropped on language change, new InventoryGui, mod off; one recipe rebuilt when a mod swap its m_resources.
internal static class RecipeTerms
{
    private sealed class Ingredient
    {
        internal string Term;
        internal Piece.Requirement Req;
        internal string MaterialName; // $token, for the "known material" check
    }

    private sealed class Entry
    {
        internal string[] Names;
        internal Ingredient[] Ingredients;
        internal string DisplayName;
        internal Piece.Requirement[] Resources; // array me read ingredients from. Other mod swap it = me build again.
    }

    private static readonly Dictionary<Recipe, Entry> Cache = new Dictionary<Recipe, Entry>();
    private static readonly List<string> TempNames = new List<string>(3);
    private static readonly List<Ingredient> TempIngredients = new List<Ingredient>(8);

    // Lower case, every whitespace gone. Empty/null = "".
    internal static string Normalise(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "";
        }
        var lower = s.ToLowerInvariant();
        var spaces = 0;
        for (var i = 0; i < lower.Length; i++)
        {
            if (char.IsWhiteSpace(lower[i]))
            {
                spaces++;
            }
        }
        if (spaces == 0)
        {
            return lower;
        }
        var sb = new StringBuilder(lower.Length - spaces);
        for (var i = 0; i < lower.Length; i++)
        {
            if (!char.IsWhiteSpace(lower[i]))
            {
                sb.Append(lower[i]);
            }
        }
        return sb.ToString();
    }

    internal static void Clear() => Cache.Clear();

    // Name shown in row (without " xN"), localized. Recipe and m_item must be alive (caller check).
    internal static string DisplayName(Recipe recipe) => Get(recipe).DisplayName;

    // Row match term? term already normalised and not empty. Recipe and m_item alive (caller check).
    // quality = level the row show ingredients for: 1 in craft tab, item level + 1 in upgrade tab.
    internal static bool Matches(Recipe recipe, int quality, string term, bool atStation, bool upgrader, Player player)
    {
        var e = Get(recipe);
        var names = e.Names;
        for (var i = 0; i < names.Length; i++)
        {
            if (names[i].Contains(term))
            {
                return true;
            }
        }

        var ingredients = e.Ingredients;
        for (var i = 0; i < ingredients.Length; i++)
        {
            var ing = ingredients[i];
            if (!ing.Term.Contains(term))
            {
                continue;
            }
            // Shown? Same three tests as vanilla SetupRequirementList.
            var req = ing.Req;
            if (atStation ? upgrader != req.m_upgraderResource : req.m_upgraderResource)
            {
                continue;
            }
            if (req.GetAmount(quality) <= 0)
            {
                continue;
            }
            if (recipe.m_requireOnlyOneIngredient && (player == null || !player.IsKnownMaterial(ing.MaterialName)))
            {
                continue; // unknown "any one of" material: vanilla hide it, me no spoil it
            }
            return true;
        }
        return false;
    }

    private static Entry Get(Recipe recipe)
    {
        // Recipe-config mod (ItemManager + ServerSync, recipe editor) put new m_resources array on same Recipe
        // while game run. Vanilla read it live, so me build words again when array not same.
        if (Cache.TryGetValue(recipe, out var e) && ReferenceEquals(e.Resources, recipe.m_resources))
        {
            return e;
        }

        var loc = Localization.instance;
        var shared = recipe.m_item.m_itemData.m_shared;
        e = new Entry { DisplayName = loc.Localize(shared.m_name) ?? "" };

        TempNames.Clear();
        AddName(Normalise(e.DisplayName));
        AddName(Normalise(recipe.m_item.name));
        // Recipe asset look like "Recipe_SwordSilver": drop prefix, else "recipe" match every row.
        var recipeName = recipe.name ?? "";
        if (recipeName.StartsWith("recipe_", StringComparison.OrdinalIgnoreCase))
        {
            recipeName = recipeName.Substring("recipe_".Length);
        }
        AddName(Normalise(recipeName));
        e.Names = TempNames.ToArray();

        TempIngredients.Clear();
        var resources = recipe.m_resources;
        e.Resources = resources;
        if (resources != null)
        {
            foreach (var req in resources)
            {
                // Modded recipe can have empty slot: skip it, row still found by name.
                if (req == null || req.m_resItem == null || req.m_resItem.m_itemData?.m_shared == null)
                {
                    continue;
                }
                var token = req.m_resItem.m_itemData.m_shared.m_name;
                TempIngredients.Add(new Ingredient { Term = Normalise(loc.Localize(token)), Req = req, MaterialName = token });
            }
        }
        e.Ingredients = TempIngredients.ToArray();

        Cache[recipe] = e;
        return e;
    }

    private static void AddName(string name)
    {
        if (name.Length > 0 && !TempNames.Contains(name))
        {
            TempNames.Add(name);
        }
    }
}
