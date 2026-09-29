using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.CompendiumEncyclopediaMod.Patches;

// Search focus guard (design 3.8): vanilla gates (inventory close keys, movement, hotbar, map...) ask Chat.HasFocus
// before reading keys. Me OR our search field's focus into it, so typing E, Tab, Esc, WASD only type. Crafting Search
// and Sort OR its own field the same way; both coexist. Idle cost: one int compare.
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
            if (FocusGuard.Active)
            {
                __result = true;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Chat.HasFocus postfix", e);
        }
    }
}
