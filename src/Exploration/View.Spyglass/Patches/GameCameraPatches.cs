using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.ViewSpyglassMod.Patches;

// Me = spyglass camera, last of all UpdateCamera patches (first-person and camera mods place their camera first, me
// then slide from whatever they made to the eye and zoom from their field of view). Prefix keep the camera distance
// (vanilla wheel zoom) so the wheel can zoom the spyglass instead; postfix put it back while the spyglass is up.
// Swim Dive also patch UpdateCamera (underwater view): the spyglass never goes up while swimming.
[HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateCamera))]
internal static class GameCameraPatches
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    private static void Prefix(GameCamera __instance, out float __state)
    {
        __state = __instance.m_distance;
    }

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyAfter("Azumatt.FirstPersonMode", "Landoria.FirstPerson", "com.geronimo.valheim.immersivefirstperson",
        "gameprog.bettercharactercontroller", "MC.Exploration.Swimming.Dive")]
    private static void Postfix(GameCamera __instance, float __state)
    {
        try
        {
            ScopeCamera.After(__instance, __state);
        }
        catch (Exception e)
        {
            PatchGuard.Report("GameCamera.UpdateCamera postfix", e);
            Scope.Abort();
        }
    }
}

// Me = world camera gone (logout, disconnect, quit: the game scene unloads). The overlay canvas outlives scenes
// (DontDestroyOnLoad) and nothing would ever hide it again in the main menu: drop the spyglass now.
[HarmonyPatch(typeof(GameCamera), nameof(GameCamera.OnDestroy))]
internal static class GameCameraDestroyPatches
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        try
        {
            if (Scope.Active || ScopeOverlay.Shown)
            {
                Scope.Abort();
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("GameCamera.OnDestroy postfix", e);
        }
    }
}
