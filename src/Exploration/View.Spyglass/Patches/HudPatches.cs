using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.ViewSpyglassMod.Patches;

// Me = no crosshair, hover text or piece health bar while the round spyglass view is shown (the dot would sit in
// the middle of the lens). Vanilla re-shows the crosshair object every frame, so me switch its Image off instead
// (vanilla only ever writes its colour) and back on when done (ScopeHud).
[HarmonyPatch(typeof(Hud), nameof(Hud.UpdateCrosshair))]
internal static class HudPatches
{
    [HarmonyPostfix]
    private static void Postfix(Hud __instance)
    {
        try
        {
            ScopeHud.Update(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Hud.UpdateCrosshair postfix", e);
        }
    }
}
