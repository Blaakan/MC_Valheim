using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.SailingSkillMod.Patches;

// Local player's timers: Sailing XP at the helm (every 1 s) and own level on own player ZDO (checked every 2 s), plus
// the late look at other mods (Compat, first frame in a world). Spawn (join, respawn, reconnect = new player ZDO):
// publish at once, XP session start over. Other players' Player objects: one reference compare and out.
[HarmonyPatch]
internal static class PlayerPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Player), nameof(Player.Update))]
    private static void Update_Postfix(Player __instance)
    {
        if (!ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            Compat.Ensure();
            SailingXp.Tick(__instance);
            LevelPublisher.Tick(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.Update postfix", e);
        }
    }

    // Vanilla Game.SpawnPlayer order: SetLocalPlayer, LoadPlayerData (skills), then OnSpawned: level right, ZDO ours.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned), new[] { typeof(bool) })]
    private static void OnSpawned_Postfix(Player __instance)
    {
        try
        {
            if (ReferenceEquals(__instance, Player.m_localPlayer))
            {
                HelmSkill.Invalidate();
                SailingXp.Reset();
                LevelPublisher.PublishNow();
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.OnSpawned postfix", e);
        }
    }
}
