using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using MC.Shared;

namespace MC.Combat.WeaponsDualWieldMod;

// Me = other dual wield mods. Two mods routing same equip and rewriting same attack clone would fight, so me stand
// aside (design 6.3, D17):
//   Smoothbrain DualWield (GUID known): BepInIncompatibility on Plugin, BepInEx no load me at all.
//   RustyMods DualWielder, Ketanol DualWieldCore, balrond DualMastery (GUIDs unverified, R17): me find them by name
//   (or GUID) in Chainloader.PluginInfos. Plugin.LocalBlocker give framework the Status text: framework remove my
//   patches, show it in MC Mods panel, and tell server "off" (server refuse player like one who turned me off).
// Framework ask at every refresh (config, world, peer events), never per frame. BepInEx add plugins to PluginInfos
// as it load them; framework refresh again in Start (all loaded) and at every world change: load order no matter.
internal static class ForeignMods
{
    // Name parts of the known dual wield mods (their plugin names, from their pages; GUIDs unverified).
    private static readonly string[] Markers = { "DualWielder", "DualWieldCore", "DualMastery" };

    // Names me already warned about in the log (once per session each).
    private static readonly HashSet<string> Warned = new HashSet<string>();

    // Pure name test (self test hammer it): plugin name or GUID hold a marker. Our own name ("Dual Wielding") no.
    internal static bool Matches(string nameOrGuid)
    {
        if (string.IsNullOrEmpty(nameOrGuid))
        {
            return false;
        }
        foreach (var marker in Markers)
        {
            if (nameOrGuid.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    // First other dual wield mod loaded in this game (live instance, not me), else null. Name = its plugin name.
    internal static string Find()
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
                return string.IsNullOrEmpty(info.Metadata.Name) ? info.Metadata.GUID : info.Metadata.Name;
            }
        }
        return null;
    }

    // Status text for Plugin.LocalBlocker, or null. Log warning once per found mod (player easy miss Status alone).
    internal static string BlockerText()
    {
        var other = Find();
        if (other == null)
        {
            return null;
        }
        if (Warned.Add(other))
        {
            Log.Warning($"{other} also handles dual wielding, so {ModInfo.Name} stays off. Remove one of them. "
                        + "A server that requires Dual Wielding refuses this game while both are installed.");
        }
        return $"Inactive: {other} also handles dual wielding. Remove one of them.";
    }
}
