using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.CompendiumEncyclopediaMod.Patches;

// Search focus guard: console key binds (bind j say hi) fire from Chat.Update through TryRunCommand with
// skipAllowedCheck = true, gated only by chat's own field. Me skip those while our field has the keyboard. Typed
// commands and other mods' calls untouched.
[HarmonyPatch]
internal static class TerminalPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Terminal), nameof(Terminal.TryRunCommand))]
    private static bool TryRunCommand_Prefix(bool skipAllowedCheck)
    {
        try
        {
            return !(skipAllowedCheck && FocusGuard.Active);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Terminal.TryRunCommand prefix", e);
            return true;
        }
    }
}
