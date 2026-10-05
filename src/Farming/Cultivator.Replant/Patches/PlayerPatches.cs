using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Farming.CultivatorReplantMod.Patches;

// Me = the player side of Replant. Local player only.
//   UpdatePlacement  prefix, High priority: find the plant under the crosshair, read and eat the Replant press, run
//                    it (Replant.OnUpdatePlacement). Prefix, not postfix: runs before vanilla build menu toggle and
//                    place/remove read the buttons, and m_lastToolUseTime set here block a vanilla place in the same
//                    frame. Never skip the original.
//   UpdateHover      postfix: remember the frame the player held a ship helm or lox saddle (doodad controller).
//                    UpdateHover run before the Use block that let go of it, so release frame count too. Replant
//                    then set no target that frame: no hint, press stay with the game.
[HarmonyPatch(typeof(Player))]
internal static class PlayerPatches
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.High)]
    [HarmonyPatch(nameof(Player.UpdatePlacement))]
    private static void UpdatePlacement_Prefix(Player __instance, bool takeInput)
    {
        if (!ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            Replant.OnUpdatePlacement(__instance, takeInput);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.UpdatePlacement prefix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Player.UpdateHover))]
    private static void UpdateHover_Postfix(Player __instance)
    {
        try
        {
            if (__instance.m_doodadController != null && ReferenceEquals(__instance, Player.m_localPlayer))
            {
                Replant.MarkHelmFrame();
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.UpdateHover postfix", e);
        }
    }
}
