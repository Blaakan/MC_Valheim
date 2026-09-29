using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.UX.CraftingSearchSortMod.Patches;

// Console key binds (bind j say hi) fire from Chat.Update, gated only by chat's own field. Only caller with
// skipAllowedCheck = true is that bind loop: me skip those while our field has keyboard. Typed commands and other
// mods' calls untouched.
[HarmonyPatch]
internal static class TerminalPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Terminal), nameof(Terminal.TryRunCommand))]
    private static bool TryRunCommand_Prefix(bool skipAllowedCheck)
    {
        try
        {
            return !(skipAllowedCheck && Time.frameCount <= FocusGuard.FieldUntilFrame);
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(TryRunCommand_Prefix), e);
            return true;
        }
    }
}
