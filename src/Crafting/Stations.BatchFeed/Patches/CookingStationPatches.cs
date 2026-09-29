using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Crafting.StationsBatchFeedMod.Patches;

// Hover of cooking stations WITHOUT food switch (the station body take the food). With a food switch vanilla
// return "" here and the switch hover (SwitchPatches) carry the hint. Never patch GetFreeSlot/HaveDoneItem:
// other mods replace them; me only call them.
[HarmonyPatch]
internal static class CookingStationPatches
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Low)]
    [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.GetHoverText))]
    private static void GetHoverText_Postfix(CookingStation __instance, ref string __result)
    {
        try
        {
            HoverHint.Append(__instance, ref __result);
        }
        catch (Exception e)
        {
            PatchGuard.Report("CookingStation.GetHoverText postfix", e);
        }
    }
}
