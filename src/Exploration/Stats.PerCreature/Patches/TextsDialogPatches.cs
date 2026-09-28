using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.StatsPerCreatureMod.Patches;

// Compendium build its list once per open (TextsDialog.UpdateTextsList); AddStats add "Player Statistics" last.
// Me put "Creatures" block on top of that entry text. No new UI object: vanilla list, scroll, gamepad stay same.
// Postfix of AddStats (not UpdateTextsList): me run before other mods' UpdateTextsList postfixes that move entries.
// Framework only patch this while feature Active. Body catch own errors: never throw into game.
[HarmonyPatch]
internal static class TextsDialogPatches
{
    private static bool _missingLogged;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TextsDialog), nameof(TextsDialog.AddStats))]
    private static void AddStats_Postfix(TextsDialog __instance)
    {
        try
        {
            var texts = __instance.m_texts;
            if (texts == null || Localization.instance == null)
            {
                return;
            }
            // Same topic string vanilla give the entry. From end: vanilla add it last.
            var topic = Localization.instance.Localize("$inventory_stats");
            for (var i = texts.Count - 1; i >= 0; i--)
            {
                var entry = texts[i];
                if (entry != null && entry.m_topic == topic)
                {
                    entry.m_text = StatsSection.Build() + entry.m_text;
                    return;
                }
            }
            // Other mod took entry away (or replaced AddStats). No section then; API still work.
            if (!_missingLogged)
            {
                _missingLogged = true;
                Log.Debug("Player Statistics entry not found in the Compendium; creature section not added.");
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("TextsDialog.AddStats postfix", e);
        }
    }
}
