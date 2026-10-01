using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Bootstrap;
using MC.Shared;

namespace MC.Combat.TrinketsOnDemandMod;

// Me = other trinket mods that change when or what trinkets fire. They lean on the vanilla full-bar moment (pop at
// once), which me hold back for the key, so two of us would fight over Player.AddAdrenaline: me stand aside
// (design 6.3). GUIDs unverified: me find them by name or GUID markers in Chainloader.PluginInfos (letters and digits
// only, lower case, so "Balrond Battle Flow", "balrond.BattleFlow" and "battle_flow" all match):
//   BetterTrinkets (Schwifty, and the Deep North compat patch by Gabadur), Passive_Trinket_Modifiers (Gabadur),
//   Balrond Battle Flow.
// Plugin.LocalBlocker give framework the Status text: framework remove my patches, show it in MC Mods panel, and tell
// server "off" (server refuse player like one who turned me off). Framework ask at every refresh (config, world, peer
// events, Start after all plugins loaded), never per frame.
internal static class ForeignMods
{
    private static readonly string[] Markers = { "bettertrinkets", "passivetrinket", "battleflow" };

    private static readonly HashSet<string> Warned = new HashSet<string>();

    // Pure (self test): plugin name or GUID hold a marker. Our own name ("Trinkets on Demand") no.
    internal static bool Matches(string nameOrGuid)
    {
        var flat = Flatten(nameOrGuid);
        if (flat.Length == 0)
        {
            return false;
        }
        foreach (var marker in Markers)
        {
            if (flat.IndexOf(marker, StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    // Lower-case letters and digits only.
    internal static string Flatten(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }
        return sb.ToString();
    }

    // First such mod loaded in this game (live instance, not me), else null. Name = its plugin name, guid = its GUID.
    internal static string Find(out string guid)
    {
        foreach (var pair in Chainloader.PluginInfos)
        {
            var info = pair.Value;
            if (info == null || info.Metadata == null || info.Instance == null
                || string.Equals(info.Metadata.GUID, ModInfo.Guid, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (Matches(info.Metadata.Name) || Matches(info.Metadata.GUID))
            {
                guid = info.Metadata.GUID;
                return string.IsNullOrEmpty(info.Metadata.Name) ? info.Metadata.GUID : info.Metadata.Name;
            }
        }
        guid = null;
        return null;
    }

    // Status text for Plugin.LocalBlocker, or null. Log warning once per found mod, with its GUID: while it is here me
    // never run in world, so Compat's Debug plugin list never come; tester read the real GUID here (design 9).
    internal static string BlockerText()
    {
        var other = Find(out var guid);
        if (other == null)
        {
            return null;
        }
        if (Warned.Add(other))
        {
            Log.Warning($"{other} ({guid}) also changes when or what trinkets fire, so {ModInfo.Name} stays off. Remove "
                        + $"one of them. A server that requires {ModInfo.Name} refuses this game while both are "
                        + "installed.");
        }
        return $"Inactive: {other} also changes when trinkets fire. Remove one of them.";
    }
}
