using System;
using System.Collections.Generic;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.DeepNorthAwakeningMod.Patches;

// Me = area spawns (design 2.3).
//   UpdateSpawning postfix:   run the area entries (AreaSpawns.Run): zone-control owner, awake north, zone near an
//                             awake cell. Vanilla call me every second per owned zone with a player in it.
//   IsSpawnPointGood prefix:  our entries only (dictionary lookup on the SpawnData reference, vanilla entries pass
//                             at once): point must be in an awake cell (meteors: storming, held). Run for each centre
//                             try and each group member.
//   GetNrOfZDOInstances prefix: only while an after-Kall burst run one of our Jotun entries (one bool read otherwise):
//                             the cap count = the burst cell's own living Jotun of that kind (AreaSpawns.BurstCount).
[HarmonyPatch(typeof(SpawnSystem))]
internal static class SpawnSystemPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(SpawnSystem.UpdateSpawning))]
    private static void UpdateSpawning_Postfix(SpawnSystem __instance)
    {
        try
        {
            AreaSpawns.Run(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("SpawnSystem.UpdateSpawning postfix", e);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(SpawnSystem.IsSpawnPointGood))]
    private static bool IsSpawnPointGood_Prefix(SpawnSystem.SpawnData spawn, ref Vector3 spawnPoint, ref bool __result)
    {
        try
        {
            if (!AreaSpawns.IsOurs(spawn, out var kind) || AreaSpawns.Gate(kind, spawnPoint))
            {
                return true;
            }
            __result = false;
            return false;
        }
        catch (Exception e)
        {
            PatchGuard.Report("SpawnSystem.IsSpawnPointGood prefix", e);
            return true;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(SpawnSystem.GetNrOfZDOInstances))]
    private static bool GetNrOfZDOInstances_Prefix(GameObject prefab, List<ZDO> ZDOs, ref int __result)
    {
        if (!AreaSpawns.Bursting)
        {
            return true;
        }
        try
        {
            var n = AreaSpawns.BurstCount(prefab, ZDOs);
            if (n < 0)
            {
                return true;
            }
            __result = n;
            return false;
        }
        catch (Exception e)
        {
            PatchGuard.Report("SpawnSystem.GetNrOfZDOInstances prefix", e);
            return true;
        }
    }
}
