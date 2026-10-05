using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Farming.CultivatorReplantMod.Patches;

// Me = transplant sapling growth (design 2.2, 2.6, RootGate). Framework patch me only while feature Active: feature
// off = saplings grow like vanilla, no sap drained.
//   Grow          prefix (Low): client still wait for server rules = none of our saplings grow (own config grow time
//                 may sit on them until Rebuild write the server's). Our Yggdrasil sapling grow only when an Ancient
//                 Root near give its sap. Vanilla call Grow every SlowUpdater pass once a plant is due: every other
//                 plant = one null check and one int set lookup, out.
//   GetHoverText  postfix: our Yggdrasil sapling say which root it draw from and the root's sap, or that it need one.
[HarmonyPatch(typeof(Plant))]
internal static class PlantPatches
{
    // Low: other mods' prefixes first. One of them skipped vanilla (__runOriginal false) = me do nothing (no drain
    // for a tree that does not grow).
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Low)]
    [HarmonyPatch(nameof(Plant.Grow))]
    private static bool Grow_Prefix(Plant __instance, ref GameObject __result, bool __runOriginal)
    {
        if (!__runOriginal)
        {
            return false;
        }
        try
        {
            return RootGate.BeforeGrow(__instance, ref __result);
        }
        catch (Exception e)
        {
            // Me broke: vanilla grow the plant (no drain) rather than a sapling stuck forever.
            PatchGuard.Report("Plant.Grow prefix", e);
            return true;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Plant.GetHoverText))]
    private static void GetHoverText_Postfix(Plant __instance, ref string __result)
    {
        try
        {
            var lines = RootGate.HoverLines(__instance);
            if (lines != null)
            {
                __result += lines;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Plant.GetHoverText postfix", e);
        }
    }
}
