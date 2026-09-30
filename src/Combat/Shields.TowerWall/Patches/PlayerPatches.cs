using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.ShieldsTowerWallMod.Patches;

// Me = sprint floor (design 2.2, decision 27). Armor slow add up with the tower's; below -0.46 sprint get slower than
// jog, below -0.667 negative. With a tower in hand (local player) me raise the run factor to at least jog speed:
// m_speed x jog factor / m_runSpeed. Sprint still cost sprint stamina (vanilla).
// Per movement update of every player: one reference compare for others.
[HarmonyPatch]
internal static class PlayerPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Player), nameof(Player.GetRunSpeedFactor))]
    private static void GetRunSpeedFactor_Postfix(Player __instance, ref float __result)
    {
        if (!ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            if (__instance.m_runSpeed <= 0f || TowerCatalog.Held(__instance) == null)
            {
                return;
            }
            var floor = __instance.m_speed * __instance.GetJogSpeedFactor() / __instance.m_runSpeed;
            if (__result < floor)
            {
                __result = floor;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.GetRunSpeedFactor postfix", e);
        }
    }
}
