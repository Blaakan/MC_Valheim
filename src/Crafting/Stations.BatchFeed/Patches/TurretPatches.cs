using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Crafting.StationsBatchFeedMod.Patches;

// Hover of ballista. No Use line (no ward access, not targeting enemies) = no anchor = no hint.
[HarmonyPatch]
internal static class TurretPatches
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Low)]
    [HarmonyPatch(typeof(Turret), nameof(Turret.GetHoverText))]
    private static void GetHoverText_Postfix(Turret __instance, ref string __result)
    {
        try
        {
            HoverHint.Append(__instance, ref __result);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Turret.GetHoverText postfix", e);
        }
    }
}
