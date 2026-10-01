using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.DeepNorthAwakeningMod.Patches;

// Me = invaded areas on the map (design 2.8, MapOverlay).
//   Update postfix:                 every frame on a game with a map: scan a slice, or a few compares when nothing
//                                   changed (paint only on change).
//   GenerateWorldMap postfix,
//   TryLoadMinimapTextureData post: the map colours were made again (fresh, no tint): scan again.
// A throw stop the overlay for this world (no exception every frame).
[HarmonyPatch(typeof(Minimap))]
internal static class MinimapPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(Minimap.Update))]
    private static void Update_Postfix(Minimap __instance)
    {
        try
        {
            MapOverlay.Update(__instance);
        }
        catch (Exception e)
        {
            MapOverlay.Fail();
            PatchGuard.Report("Minimap.Update postfix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Minimap.GenerateWorldMap))]
    private static void GenerateWorldMap_Postfix(Minimap __instance)
    {
        try
        {
            MapOverlay.BaseChanged(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Minimap.GenerateWorldMap postfix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Minimap.TryLoadMinimapTextureData))]
    private static void TryLoadMinimapTextureData_Postfix(Minimap __instance, bool __result)
    {
        try
        {
            if (__result)
            {
                MapOverlay.BaseChanged(__instance);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Minimap.TryLoadMinimapTextureData postfix", e);
        }
    }
}
