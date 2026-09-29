using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.UX.CraftingSearchSortMod.Patches;

// Panel buttons can have a keyboard hotkey (UIGamePad.m_keyCode, prefab data). A typed letter must not click one.
// While our field has keyboard, no UIGamePad button fire. One int compare per UIGamePad per frame.
[HarmonyPatch]
internal static class UIGamePadPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(UIGamePad), nameof(UIGamePad.ButtonPressed))]
    private static bool ButtonPressed_Prefix(ref bool __result)
    {
        try
        {
            if (Time.frameCount > FocusGuard.FieldUntilFrame)
            {
                return true;
            }
            __result = false;
            return false;
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(ButtonPressed_Prefix), e);
            return true;
        }
    }
}
