using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Crafting.StationsBatchFeedMod.Patches;

// Hover of station switches: smelter input/fuel, cooking station food/fuel switch, shield generator fuel.
// Every frame while hovering a switch: HoverHint keep it cheap (one-slot cache, no search when not covered).
// Low: run after other mods' postfixes, so me see (and anchor in) their final text.
[HarmonyPatch]
internal static class SwitchPatches
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Low)]
    [HarmonyPatch(typeof(Switch), nameof(Switch.GetHoverText))]
    private static void GetHoverText_Postfix(Switch __instance, ref string __result)
    {
        try
        {
            HoverHint.Append(__instance, ref __result);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Switch.GetHoverText postfix", e);
        }
    }
}
