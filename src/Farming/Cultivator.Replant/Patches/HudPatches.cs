using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Farming.CultivatorReplantMod.Patches;

// Me = Replant hint under the crosshair in build mode (vanilla blank the hover text there): "[E] Replant" or the
// cultivator level needed, and the Ancient Root distance ("Plant 2 to 6 m from an Ancient Root") while placing a
// Yggdrasil transplant (ReplantHint). No Replant hint at ship helm or on lox saddle (Replant set no target there).
[HarmonyPatch(typeof(Hud), nameof(Hud.UpdateCrosshair))]
internal static class HudPatches
{
    [HarmonyPostfix]
    private static void Postfix(Hud __instance, Player player)
    {
        try
        {
            ReplantHint.Update(__instance, player);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Hud.UpdateCrosshair postfix", e);
        }
    }
}
