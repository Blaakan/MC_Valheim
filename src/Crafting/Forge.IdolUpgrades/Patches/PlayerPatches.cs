using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Crafting.ForgeIdolUpgradesMod.Patches;

// Requirement check at the Forge (upgrade list colours, Refine button, vanilla DoCrafting check): count the idol the
// item level need (IdolTierRule), not the recipe's own. Discovery (discover = true) stay vanilla: it ask which
// materials are known, not what one level cost.
[HarmonyPatch]
internal static class PlayerPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirementItems))]
    private static void HaveRequirementItems_Prefix(Player __instance, Recipe piece, bool discover, int qualityLevel, out bool __state)
    {
        __state = false;
        if (discover)
        {
            return;
        }
        try
        {
            __state = IdolSwap.Begin(__instance, piece, qualityLevel);
        }
        catch (Exception e)
        {
            IdolSwap.End();
            __state = false;
            PatchGuard.Report("Player.HaveRequirementItems prefix", e);
        }
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirementItems))]
    private static void HaveRequirementItems_Finalizer(bool __state)
    {
        if (__state)
        {
            IdolSwap.End();
        }
    }
}
