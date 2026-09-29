using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.SleepThroughDayMod.Patches;

// Client part of lying down. EnvMan.CalculateCanSleep = only place bed ask "may I sleep now?" (Bed.Interact read
// EnvMan.CanSleep, which return this frame's result). Me turn false into true in morning, when vanilla 30 s
// cooldown is over. Bed.Interact stay vanilla: every other check and message still run.
// Only where local player exist: dedicated server and loading screen keep vanilla value.
// Framework only patch this while feature Active. Every body catch own errors: never throw into game.
[HarmonyPatch]
internal static class EnvManPatches
{
    // Every frame on every machine: cheap reads first, no allocation. Never turn true into false.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.CalculateCanSleep))]
    private static void CalculateCanSleep_Postfix(EnvMan __instance, ref bool __result)
    {
        if (__result)
        {
            return; // afternoon, night: vanilla already allow
        }
        try
        {
            if (!DayClock.IsMorning())
            {
                return; // flags lag or forced time; vanilla false stand
            }
            var player = Player.m_localPlayer;
            var net = ZNet.instance;
            if (player == null || net == null)
            {
                return;
            }
            if (net.GetTimeSeconds() > player.m_wakeupTime + __instance.m_sleepCooldownSeconds)
            {
                __result = true;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(CalculateCanSleep_Postfix), e);
        }
    }
}
