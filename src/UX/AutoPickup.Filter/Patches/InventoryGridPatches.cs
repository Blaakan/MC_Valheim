using System;
using HarmonyLib;
using MC.Shared;

namespace MC.UX.AutoPickupFilterMod.Patches;

// Vanilla repaint every slot of a visible grid each frame in UpdateGui: right after, me show/hide the red/green
// badge on slots whose item type is in the active list. Only the two vanilla grids (player, container panel).
[HarmonyPatch]
internal static class InventoryGridPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.UpdateGui), new[] { typeof(Player), typeof(ItemDrop.ItemData) })]
    private static void UpdateGui_Postfix(InventoryGrid __instance)
    {
        try
        {
            MarkerOverlay.Update(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(UpdateGui_Postfix), e);
        }
    }
}
