using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.ViewDistantHorizonsMod.Patches;

// Me = take over game TerrainLod (vanilla far ground). OnEnable at world load: me attach my managers and skip vanilla
// CreateMeshes. OnDisable at logout: managers stop. Error = vanilla run as normal (prefix say true).
[HarmonyPatch(typeof(TerrainLod))]
internal static class TerrainLodPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(TerrainLod.OnEnable))]
    private static bool OnEnablePrefix(TerrainLod __instance)
    {
        try
        {
            return TerrainLink.OnTerrainLodEnable(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("TerrainLod.OnEnable prefix", e);
            return true;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(TerrainLod.OnDisable))]
    private static void OnDisablePostfix(TerrainLod __instance)
    {
        try
        {
            TerrainLink.OnTerrainLodDisable(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("TerrainLod.OnDisable postfix", e);
        }
    }
}
