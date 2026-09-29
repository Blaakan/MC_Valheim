using System;
using HarmonyLib;
using MC.Shared;

namespace MC.UX.ContainerSortMod.Patches;

// Awake = make the two buttons (new InventoryGui each world session). Show = only place where the open container
// change: show buttons for it or not, place them, refresh texts. No per-frame patch: buttons live under the vanilla
// container panel and hide with it. Framework only patch these while feature Active. Every body catch own errors.
[HarmonyPatch]
internal static class InventoryGuiPatches
{
    // No measuring here: container panel just got hidden, other UI mods may not have moved things yet.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Awake))]
    private static void Awake_Postfix(InventoryGui __instance)
    {
        try
        {
            SortChestUi.Create(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(Awake_Postfix), e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show), new[] { typeof(Container), typeof(int) })]
    private static void Show_Postfix(InventoryGui __instance)
    {
        try
        {
            SortChestUi.OnShow(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(Show_Postfix), e);
        }
    }
}
