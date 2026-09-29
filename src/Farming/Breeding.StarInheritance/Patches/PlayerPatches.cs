using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Farming.BreedingStarInheritanceMod.Patches;

// Me publish own Farming when own character spawn: join, respawn, reconnect = new player ZDO without key, so friend
// count again at once (not wait for next breeding tick, 30-45 s after pen load). Vanilla Game.SpawnPlayer order:
// SetLocalPlayer, LoadPlayerData (skills), then OnSpawned: level right, ZDO ours. Only local game call it.
// Later changes (level up, death drain, status effect) still go out on breeding ticks (Procreate prefix).
[HarmonyPatch(typeof(Player), nameof(Player.OnSpawned), new[] { typeof(bool) })]
internal static class PlayerPatches
{
    [HarmonyPostfix]
    private static void OnSpawned_Postfix(Player __instance)
    {
        try
        {
            if (__instance == Player.m_localPlayer)
            {
                FarmerSkill.PublishNow();
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.OnSpawned postfix", e);
        }
    }
}
