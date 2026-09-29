using System;
using HarmonyLib;
using MC.Shared;

namespace MC.UX.CraftingSearchSortMod.Patches;

// UpdateRecipeList = only place where final vanilla list exist (vanilla build, sort, place rows inside it).
// Prefix reorder would be undone by vanilla sort: me work in postfix (CraftList). Covers both tabs, every station,
// hand crafting, and mods that call UpdateRecipeList themselves.
// Framework only patch these while feature Active. Every body catch own errors: never throw into game.
[HarmonyPatch]
internal static class InventoryGuiPatches
{
    // Low: me go after other mods' normal postfixes, so rows they add get filtered and sorted too.
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Low)]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateRecipeList))]
    private static void UpdateRecipeList_Postfix(InventoryGui __instance)
    {
        try
        {
            CraftList.Organize(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(UpdateRecipeList_Postfix), e);
        }
    }

    // Inventory closed (E, Tab, Esc, walking away, death): menu shut, keyboard released, search cleared unless kept.
    // Vanilla call Hide every frame while dead/teleporting: idle path is a few bool checks.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
    private static void Hide_Postfix()
    {
        if (!CraftSearch.Active)
        {
            return;
        }
        try
        {
            CraftSearch.OnInventoryHidden();
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(Hide_Postfix), e);
        }
    }
}
