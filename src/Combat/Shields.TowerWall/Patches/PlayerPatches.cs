using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.ShieldsTowerWallMod.Patches;

// Me = sprint floor (design 2.2, decision 27). Armor slow add up with the tower's; below -0.46 sprint get slower than
// jog, below -0.667 negative. With a tower in hand (local player) me raise the run factor to at least jog speed:
// m_speed x jog factor / m_runSpeed. Sprint still cost sprint stamina (vanilla).
// Per movement update of every player: one reference compare for others.
// Me also = end of the kept press (design 2.6, decision 30). PlayerAttackInput prefix: vanilla call it every
// FixedUpdate for the local player only, right after the action queue (queued equip / unequip) and before it read the
// attack press. A press BashWatch keep for the bash cooldown go back to its vanilla 0.5 s there once the tower is no
// more in the left hand (BashWatch.ReleasePress), so fists or the next weapon never swing by themselves. One reference
// compare when no press is kept. Void prefix, skip nothing: other mods' prefixes on this method (MC Spyglass, MC Music
// Instruments skip it for their own item) and mine do not care about each other.
[HarmonyPatch]
internal static class PlayerPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Player), nameof(Player.PlayerAttackInput))]
    private static void PlayerAttackInput_Prefix(Player __instance)
    {
        if (!BashWatch.KeepsPress)
        {
            return;
        }
        try
        {
            BashWatch.ReleasePress(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.PlayerAttackInput prefix", e);
        }
    }

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
