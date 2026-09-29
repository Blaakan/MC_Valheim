using System;
using HarmonyLib;
using MC.Shared;

namespace MC.UX.AutoPickupFilterMod.Patches;

// Inventory screen: Show make the button (lazy, also after a scene reload = new InventoryGui), Hide close the
// controller lists panel, Update = one per-frame entry point (after both grids repainted): place button, mark
// gestures, refresh label/tooltip. Framework only patch these while feature Active. Every body catch own errors.
[HarmonyPatch]
internal static class InventoryGuiPatches
{
    // No geometry here: panel may still sit at its hidden or animating transform.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show), new[] { typeof(Container), typeof(int) })]
    private static void Show_Postfix(InventoryGui __instance)
    {
        try
        {
            FilterUi.EnsureButton(__instance);
            FilterUi.PlacementDirty = true;
            FilterUi.Refresh(force: true);
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(Show_Postfix), e);
        }
    }

    // Vanilla call Hide every frame while dead/teleporting: idle path is one bool read.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
    private static void Hide_Postfix()
    {
        if (!FilterUi.ListsShown)
        {
            return;
        }
        try
        {
            FilterUi.HideListsPanel();
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(Hide_Postfix), e);
        }
    }

    // Closed inventory: one static read, out.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Update))]
    private static void Update_Postfix(InventoryGui __instance)
    {
        if (!InventoryGui.IsVisible())
        {
            return;
        }
        try
        {
            FilterUi.Tick(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(Update_Postfix), e);
        }
    }
}
