using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.ViewDistantHorizonsMod.Patches;

// Me = ZNet.ApplySimulationDistance end with Heightmap.ApplySettingsOnAll, which write vanilla _LodHideDistance into
// property block of every enabled Heightmap, my far tiles too (simulation distance change in graphics menu,
// or server answer on a client). Me put my values back after.
[HarmonyPatch(typeof(Heightmap), nameof(Heightmap.ApplySettingsOnAll))]
internal static class HeightmapPatches
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        try
        {
            var manager = LodTerrainManager.Instance;
            if (manager != null)
            {
                manager.ReapplyHideDistances();
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Heightmap.ApplySettingsOnAll postfix", e);
        }
    }
}
