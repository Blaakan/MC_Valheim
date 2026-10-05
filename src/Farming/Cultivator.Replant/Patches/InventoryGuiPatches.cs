using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Farming.CultivatorReplantMod.Patches;

// Crafting panel for cultivator tiers (CultivatorTiers). Only while Active.
// - AddRecipeToList prefix: no cultivator upgrade row at a Forge of Potential (an idol would skip a tier's cost), and
//   no upgrade row to level 4+ from another mod's cultivator recipe (its rows price new levels with vanilla formula).
// Heal of stale max quality + recipe resync = UpdateRecipeList prefix in InventoryGuiHealPatches (ALWAYS ON, so a
// copy picked up after feature off heal back to 3 too).
// NO DoCrafting patch on purpose: Forge Idol Upgrades (ForgeGuard) take any bool prefix or transpiler there as a
// takeover of the Forge of Potential.
[HarmonyPatch]
internal static class InventoryGuiPatches
{
    // One row of the list. Craft tab rows (no item) always stay; false = this one row dropped cleanly (Crafting Search
    // and Sort's UpdateRecipeList postfix see the final list).
    [HarmonyPrefix]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.AddRecipeToList))]
    private static bool AddRecipeToList_Prefix(InventoryGui __instance, Recipe recipe, ItemDrop.ItemData item)
    {
        if (item == null || recipe == null)
        {
            return true;
        }
        try
        {
            return !CultivatorTiers.HideAtUpgrader(__instance, recipe)
                   && !CultivatorTiers.HideForeignUpgrade(recipe, item);
        }
        catch (Exception e)
        {
            PatchGuard.Report("InventoryGui.AddRecipeToList prefix", e);
            return true;
        }
    }
}
