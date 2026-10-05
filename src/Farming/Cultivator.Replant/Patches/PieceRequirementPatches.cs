using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Farming.CultivatorReplantMod.Patches;

// Cultivator upgrade cost per level (CultivatorTiers). Only our own requirement objects change (by reference): tier
// rows give their amount at their level, vanilla Bronze / Corewood rows give 0 from level 4. Every other requirement
// (other recipes, pieces placed at quality 0) keep the vanilla number.
[HarmonyPatch]
internal static class PieceRequirementPatches
{
    // Hot path (requirement checks of every recipe, crafting panel each frame, every placed piece): TryAmount look at
    // one flag first, then one dictionary lookup. No allocation.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Piece.Requirement), nameof(Piece.Requirement.GetAmount))]
    private static void GetAmount_Postfix(Piece.Requirement __instance, int qualityLevel, ref int __result)
    {
        try
        {
            if (CultivatorTiers.TryAmount(__instance, qualityLevel, out var amount))
            {
                __result = amount;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Piece.Requirement.GetAmount postfix", e);
        }
    }
}
