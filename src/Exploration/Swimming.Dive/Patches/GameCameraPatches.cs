using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SwimmingDiveMod.Patches;

// Me = camera and view under water (design 2.6, 2.7). Local only, nothing sent.
//   GetCameraPosition prefix:    diver's eye under water -> vanilla water clamp off for this call (DiveCamera).
//   GetCameraPosition postfix:   camera kept under the surface with a clearance.
//   GetCameraPosition finalizer: m_minWaterDistance back, also when vanilla or another patch throw. NaN = me did
//                                nothing this call.
//   UpdateCamera postfix:        view state from the final camera position: fog, surface from below (UnderwaterView).
// Not diving: one bool read in the prefix, one liquid query per frame in the UpdateCamera postfix (vanilla do the same
// query in GetCameraPosition).
[HarmonyPatch(typeof(GameCamera))]
internal static class GameCameraPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(GameCamera.GetCameraPosition))]
    private static void GetCameraPosition_Prefix(GameCamera __instance, out float __state)
    {
        __state = float.NaN;
        if (!DiveState.Diving)
        {
            DiveCamera.Clear();
            return;
        }
        try
        {
            if (DiveCamera.ShouldLift(Player.m_localPlayer))
            {
                __state = __instance.m_minWaterDistance;
                __instance.m_minWaterDistance = DiveCamera.NoClamp;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("GameCamera.GetCameraPosition prefix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(GameCamera.GetCameraPosition))]
    private static void GetCameraPosition_Postfix(GameCamera __instance, ref Vector3 pos, float __state)
    {
        if (float.IsNaN(__state))
        {
            return;
        }
        try
        {
            var player = Player.m_localPlayer;
            if (player != null)
            {
                DiveCamera.KeepBelowSurface(__instance, player, ref pos);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("GameCamera.GetCameraPosition postfix", e);
        }
    }

    // Void finalizer: an exception still go up as without me.
    [HarmonyFinalizer]
    [HarmonyPatch(nameof(GameCamera.GetCameraPosition))]
    private static void GetCameraPosition_Finalizer(GameCamera __instance, float __state)
    {
        if (float.IsNaN(__state))
        {
            return;
        }
        try
        {
            __instance.m_minWaterDistance = __state;
        }
        catch (Exception e)
        {
            PatchGuard.Report("GameCamera.GetCameraPosition finalizer", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(GameCamera.UpdateCamera))]
    private static void UpdateCamera_Postfix(GameCamera __instance)
    {
        try
        {
            UnderwaterView.Tick(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("GameCamera.UpdateCamera postfix", e);
        }
    }
}
