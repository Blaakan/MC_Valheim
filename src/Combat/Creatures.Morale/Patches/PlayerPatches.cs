using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.CreaturesMoraleMod.Patches;

// Me = local character spawned (Game.SpawnPlayer: local player set, profile data loaded): its new player ZDO has no
// standing yet (respawn or reconnection = new ZDO), publish it. Only while Active. Body catch own errors.
[HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
internal static class PlayerPatches
{
    private static void Postfix(Player __instance)
    {
        try
        {
            if (__instance != null && __instance == Player.m_localPlayer)
            {
                Standing.PublishNow();
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.OnSpawned postfix", e);
        }
    }
}
