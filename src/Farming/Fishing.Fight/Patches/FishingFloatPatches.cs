using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Farming.FishingFightMod.Patches;

// Me = the take-over. Float tick with a hooked fish on the local player's float = Fight do the tick, vanilla skip it.
// Anything else (no fish yet, pending rules, other player's float): vanilla. Only while Active.
// Priority Low: stamina mods that set a flag or scale the float's stamina fields in their own FixedUpdate prefix
// (EpicLoot shard, FeastMaster) run first, so me read their numbers and their UseStamina discount see my calls.
// Another prefix already skipped the original (a fight mod me not know): me stay out, never two owners of one tick.
[HarmonyPatch(typeof(FishingFloat), nameof(FishingFloat.FixedUpdate))]
internal static class FishingFloatPatches
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Low)]
    private static bool FixedUpdate_Prefix(FishingFloat __instance, bool __runOriginal)
    {
        if (!__runOriginal)
        {
            return false;
        }
        try
        {
            return !Fight.TryStep(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("FishingFloat.FixedUpdate prefix", e);
            // Broken tick: drop the fight, vanilla reel this float from now on (fish stay hooked).
            Fight.MarkBroken(__instance);
            return true;
        }
    }
}
