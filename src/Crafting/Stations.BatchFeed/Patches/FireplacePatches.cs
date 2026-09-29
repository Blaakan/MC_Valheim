using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Crafting.StationsBatchFeedMod.Patches;

// Hover of fires (campfire, hearth, braziers, torches...). Hint only on refillable, non-infinite fires.
// Hot tub no fire: it Smelter fuel switch, hint come from SwitchPatches.
[HarmonyPatch]
internal static class FireplacePatches
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Low)]
    [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.GetHoverText))]
    private static void GetHoverText_Postfix(Fireplace __instance, ref string __result)
    {
        try
        {
            HoverHint.Append(__instance, ref __result);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Fireplace.GetHoverText postfix", e);
        }
    }
}
