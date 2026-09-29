using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.SleepThroughDayMod.Patches;

// Server part. Game.UpdateSleeping run every 2 s on server/host only (InvokeRepeating by name: Harmony detour
// still apply). Prefix record "sleep already ran?" and send final time before stop; postfix see what vanilla (or
// other mod's replacing prefix) decided this tick and start or aim day sleep (DaySleep).
// Framework only patch this while feature Active. Every body catch own errors: never throw into game.
[HarmonyPatch]
internal static class GamePatches
{
    // First: record state before other mod's replacing prefix (BedRules, Sleepover) start a sleep. Never skip original.
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(Game), nameof(Game.UpdateSleeping))]
    private static void UpdateSleeping_Prefix(Game __instance, out bool __state)
    {
        __state = false;
        try
        {
            __state = __instance.m_sleeping;
            DaySleep.BeforeTick(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(UpdateSleeping_Prefix), e);
        }
    }

    // Postfix run even when other mod's prefix skipped original: see their decision too.
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Low)]
    [HarmonyPatch(typeof(Game), nameof(Game.UpdateSleeping))]
    private static void UpdateSleeping_Postfix(Game __instance, bool __state)
    {
        try
        {
            DaySleep.AfterTick(__instance, __state);
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(UpdateSleeping_Postfix), e);
        }
    }
}
