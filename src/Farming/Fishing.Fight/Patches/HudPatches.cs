using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Farming.FishingFightMod.Patches;

// Me = draw the catch bar every frame (FightHud). No fight = one null check.
[HarmonyPatch(typeof(Hud), nameof(Hud.Update))]
internal static class HudPatches
{
    [HarmonyPostfix]
    private static void Update_Postfix()
    {
        if (Fight.Current == null && !FightHud.Shown)
        {
            return;
        }
        try
        {
            // Float or fish gone without a tick of mine (logout, unload): fight over, no frozen bar.
            Fight.DropStale();
            FightHud.Tick();
        }
        catch (Exception e)
        {
            PatchGuard.Report("Hud.Update postfix", e);
        }
    }
}
