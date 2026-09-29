using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.CompendiumEncyclopediaMod.Patches;

// UIGamePad.ButtonPressed run BEFORE the shared 2-frame pad lock is taken, so a false here never block another pad:
//   - while our search field has the keyboard: no pad button fire (a typed letter must not click a hotkey);
//   - our side button's pad (View/Select): only while the inventory is shown with the side panel focused, the window
//     closed and the player alive (SideButton.PadAllowed). View/Select on the chest grid stay Sort Chest's, and in the
//     world it stay the map.
// Idle cost: one int compare + one reference compare per pad that is about to read its key.
[HarmonyPatch]
internal static class UIGamePadPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(UIGamePad), nameof(UIGamePad.ButtonPressed))]
    private static bool ButtonPressed_Prefix(UIGamePad __instance, ref bool __result)
    {
        try
        {
            if (FocusGuard.Active || (ReferenceEquals(__instance, SideButton.Pad) && !SideButton.PadAllowed()))
            {
                __result = false;
                return false;
            }
            return true;
        }
        catch (Exception e)
        {
            PatchGuard.Report("UIGamePad.ButtonPressed prefix", e);
            return true;
        }
    }
}
