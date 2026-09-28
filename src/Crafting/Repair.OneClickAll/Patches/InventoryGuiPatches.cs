using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Crafting.RepairOneClickAllMod.Patches;

// Repair button (mouse, gamepad, Auga, SeneaL UI) all go through InventoryGui.OnRepairPressed. Me hook there only.
// Prefix remember worn items, vanilla repair one, postfix press vanilla again for the rest (see BulkRepair).
// Framework only patch these while feature Active. Every body catch own errors: never throw into game.
[HarmonyPatch]
internal static class InventoryGuiPatches
{
    // First, before other mods' prefixes: a mod that repair in its own prefix still count in the summary.
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnRepairPressed))]
    private static void OnRepairPressed_Prefix(out BulkRepair.Snapshot __state)
    {
        __state = null;
        try
        {
            __state = BulkRepair.Take();
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(OnRepairPressed_Prefix), e);
        }
    }

    // Postfix run even when another mod skip vanilla: BulkRepair judge from durability, not from "vanilla ran".
    [HarmonyPostfix]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnRepairPressed))]
    private static void OnRepairPressed_Postfix(InventoryGui __instance, BulkRepair.Snapshot __state)
    {
        try
        {
            BulkRepair.Finish(__instance, __state);
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(OnRepairPressed_Postfix), e);
        }
    }
}
