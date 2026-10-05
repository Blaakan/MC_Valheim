using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.MusicInstrumentsMod.Patches;

// Me = the comfort bonus. Postfix on Player.GetComfortLevel (what Resting icon, Rested length and Rested message
// read), not on SE_Rested.CalculateComfortLevel: the bonus then never raise the MaxComfort stat or the platform
// "comfort" achievement (user-facing decision, design doc D4), and it shows at once (no 2 s wait for UpdateBaseValue).
// Local player only (remote Player objects return 0 anyway), while it has the Music effect; rules decide amount and
// the shelter rule. Called every frame by the HUD while Resting: hash set lookup + few compares.
[HarmonyPatch(typeof(Player), nameof(Player.GetComfortLevel))]
internal static class ComfortPatches
{
    [HarmonyPostfix]
    private static void Postfix(Player __instance, ref int __result)
    {
        if (__result <= 0 || !ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            __result += MusicBonus.ComfortFor(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.GetComfortLevel postfix", e);
        }
    }
}
