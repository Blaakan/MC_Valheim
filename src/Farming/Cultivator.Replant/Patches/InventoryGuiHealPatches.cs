using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Farming.CultivatorReplantMod.Patches;

// ALWAYS ON. Crafting list about to be built (panel open, tab switch, inventory change, after a craft; never per frame):
// - Resync: other mod swap the cultivator recipe array while tiers on = tier rows on top again (does nothing unless
//   tiers in force, so nothing while feature off).
// - HealLocal: cultivators in the player's inventory get the prefab's max quality (7 while tiers on, vanilla 3 when
//   off). Copy made while tiers were on and picked up later from a chest or the ground keep its own 7: without me
//   feature off = phantom "Upgrade to 4" row. So me stay on when feature off.
// Code here never need bound config or feature Active: Resync gated by InForce, heal read only the prefab. Cheap: int
// compare per inventory item first.
[AlwaysOnPatch]
[HarmonyPatch]
internal static class InventoryGuiHealPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateRecipeList))]
    private static void UpdateRecipeList_Prefix()
    {
        try
        {
            CultivatorTiers.Resync();
            CultivatorTiers.HealLocal();
        }
        catch (Exception e)
        {
            PatchGuard.Report("InventoryGui.UpdateRecipeList prefix (always on)", e);
        }
    }
}
