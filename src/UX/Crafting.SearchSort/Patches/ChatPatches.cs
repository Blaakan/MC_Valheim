using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.UX.CraftingSearchSortMod.Patches;

// Vanilla gates (InventoryGui close keys, PlayerController movement, hotkey bar, minimap...) ask Chat.HasFocus
// before reading keys. Me OR our field focus into it: typing E, Tab, Esc, WASD only type. Chat itself never call it.
// Sort menu open: true ONLY on the frame Esc or B is pressed, so that key close the menu and not the inventory,
// while WASD still walk and E/Tab still close the inventory (vanilla feel).
[HarmonyPatch]
internal static class ChatPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chat), nameof(Chat.HasFocus))]
    private static void HasFocus_Postfix(ref bool __result)
    {
        if (__result)
        {
            return;
        }
        try
        {
            // Idle cost: two int compares.
            var frame = Time.frameCount;
            if (frame <= FocusGuard.FieldUntilFrame)
            {
                __result = true;
            }
            else if (frame <= FocusGuard.MenuUntilFrame
                     && (ZInput.GetKeyDown(KeyCode.Escape, logWarning: false) || ZInput.GetButtonDown("JoyButtonB")))
            {
                __result = true;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(HasFocus_Postfix), e);
        }
    }
}
