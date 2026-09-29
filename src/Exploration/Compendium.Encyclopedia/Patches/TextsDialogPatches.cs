using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.CompendiumEncyclopediaMod.Patches;

// Valheim Compendium dialog shown (Setup = every path that shows it: raven button, our Texts tab, another mod): me put
// the Texts | Encyclopedia tabs on it (TopTabs), Texts current. Postfix: vanilla (and other mods' Setup / list patches)
// run first, me only add a child. Cheap when the tabs are already there (one reference compare).
// Framework only patch this while feature Active. Body catch own errors: never throw into game.
[HarmonyPatch]
internal static class TextsDialogPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(TextsDialog), nameof(TextsDialog.Setup))]
    private static void Setup_Postfix(TextsDialog __instance)
    {
        try
        {
            TopTabs.OnVanillaShown(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("TextsDialog.Setup postfix", e);
        }
    }
}
