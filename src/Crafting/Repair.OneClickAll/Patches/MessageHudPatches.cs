using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Crafting.RepairOneClickAllMod.Patches;

// Each vanilla repair say "Repaired X" in the center. Ten items = ten messages in one frame = flicker, and each one
// queue fade work in MessageHud. While BulkRepair press vanilla again, me swallow center messages and keep the last
// one (it tell why a press was blocked). Top-left messages pass: cost mods' "Used 2 coins" receipts stay.
// Hook MessageHud, not Player.Message: some mods talk to MessageHud directly.
[HarmonyPatch]
internal static class MessageHudPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(MessageHud), nameof(MessageHud.ShowMessage))]
    private static bool ShowMessage_Prefix(MessageHud.MessageType type, string text)
    {
        // Cheap check first: this run for every message in the game.
        if (!BulkRepair.Muting || type != MessageHud.MessageType.Center)
        {
            return true;
        }
        try
        {
            // Vanilla "nothing left" come last when a cost mod say no inside CanRepair: never hide the real reason.
            if (BulkRepair.LastMuted == null || text != BulkRepair.VanillaNothingLeft)
            {
                BulkRepair.LastMuted = text;
            }
            return false;
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(ShowMessage_Prefix), e);
            return true;
        }
    }
}
