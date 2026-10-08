using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using MC.Shared;

namespace MC.Exploration.SwimmingDiveMod;

// What me do about another loaded mod.
internal enum ForeignKind : byte
{
    None,
    Blocks,     // other dive controller: two mods would fight over the vertical speed and the camera -> me stand aside
    Composes,   // camera or swim tweak that work next to me: log only
}

// Me = other swim and dive mods (design 6.3). GUIDs read in their sources or their incompatibility attributes
// (research 2026-10-01). Plugin.LocalBlocker give the framework the Status text for a blocking mod: framework remove my
// patches, show it in the MC Mods panel, and tell the server "off" (a server that requires me refuse that player).
// Framework ask at every refresh (config, world, peer events, Start after all plugins loaded), never per frame.
internal static class ForeignMods
{
    // Dive controllers: Crouch dive, m_swimDepth, UpdateSwimming / UpdateMotion, camera, fog, surface flip.
    private static readonly string[] BlockingGuids =
    {
        "MainStreetGaming.BetterDiving",
        "sighsorry.DiveIn",
        "Searica.Valheim.UnderTheSea",
        "blacks7ar.VikingsDoSwim",
        "ch.easy.develope.vh.diving.mod",
    };

    // Work next to me: Crystal's Underwater (camera ignores water, walk on the sea floor), Aegir (camera in debug fly),
    // Improved Swimming (old method names, likely dead on 1.0), ImpactfulSkills (transpilers on UpdateSwimming,
    // OnSwimming and Jump, read in its source 2026-10-01: its swim speed bonus replace the m_swimSpeed read inside
    // UpdateSwimming, so horizontal only, my dive speed read m_swimSpeed itself; its lower swim cost scale the drain
    // right before UseStamina, so it scale my still-diver drain too; its jump force never run, my Jump prefix skip Jump
    // while diving). Nothing fight: log only.
    private static readonly string[] ComposingGuids =
    {
        "dev.crystal.underwater",
        "Aegir",
        "projjm.improvedswimming",
        "MidnightsFX.ImpactfulSkills",
    };

    // No known GUID: plugin name match. It push the camera back above water: my camera then never go under.
    private const string NoUnderwaterCameraName = "NoUnderwaterCamera";

    private static readonly HashSet<string> Logged = new HashSet<string>();

#if DEBUG
    // Self test: made-up loaded plugins (guid, name) the scan see next to the real ones. Null = none. A blocking one
    // turn me off like the real mod would (Status text, warning once), with no other dive mod installed.
    internal static List<KeyValuePair<string, string>> TestPlugins;

    // Self test: made-up plugin gone = its "logged once" mark gone too (next run of the test warn again).
    internal static void ForgetTest(string guid) => Logged.Remove(guid);
#endif

    // Pure (self test hammer it).
    internal static ForeignKind Classify(string guid, string name)
    {
        if (!string.IsNullOrEmpty(guid))
        {
            foreach (var g in BlockingGuids)
            {
                if (string.Equals(g, guid, StringComparison.OrdinalIgnoreCase))
                {
                    return ForeignKind.Blocks;
                }
            }
            foreach (var g in ComposingGuids)
            {
                if (string.Equals(g, guid, StringComparison.OrdinalIgnoreCase))
                {
                    return ForeignKind.Composes;
                }
            }
        }
        var any = (name ?? "") + " " + (guid ?? "");
        return any.IndexOf(NoUnderwaterCameraName, StringComparison.OrdinalIgnoreCase) >= 0
            ? ForeignKind.Composes
            : ForeignKind.None;
    }

    // Status text for Plugin.LocalBlocker, or null. Log once per found mod (player easy miss Status alone).
    internal static string BlockerText()
    {
        string blocker = null;
        foreach (var pair in Chainloader.PluginInfos)
        {
            var info = pair.Value;
            if (info == null || info.Metadata == null || info.Instance == null
                || string.Equals(info.Metadata.GUID, ModInfo.Guid, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            Consider(info.Metadata.GUID, info.Metadata.Name, ref blocker);
        }
#if DEBUG
        if (TestPlugins != null)
        {
            foreach (var fake in TestPlugins)
            {
                Consider(fake.Key, fake.Value, ref blocker);
            }
        }
#endif
        return blocker == null ? null : $"Inactive: {blocker} also handles diving. Remove one of them.";
    }

    // One loaded plugin: first blocking one give the Status text, each known one logged once.
    private static void Consider(string guid, string pluginName, ref string blocker)
    {
        var name = string.IsNullOrEmpty(pluginName) ? guid : pluginName;
        switch (Classify(guid, pluginName))
        {
            case ForeignKind.Blocks:
                if (Logged.Add(guid))
                {
                    Log.Warning($"{name} also handles diving, so {ModInfo.Name} stays off. Remove one of them. "
                                + $"A server that requires {ModInfo.Name} refuses this game while both are installed.");
                }
                blocker ??= name;
                break;
            case ForeignKind.Composes:
                if (Logged.Add(guid))
                {
                    Log.Info(ComposeText(guid, name));
                }
                break;
        }
    }

    private static string ComposeText(string guid, string name)
    {
        if (string.Equals(guid, "dev.crystal.underwater", StringComparison.OrdinalIgnoreCase))
        {
            return $"{name} is installed: while it lets you walk on the sea floor you do not swim, so {ModInfo.Name} "
                   + "cannot dive; its under-water camera gets this mod's under-water fog and surface.";
        }
        if (string.Equals(guid, "projjm.improvedswimming", StringComparison.OrdinalIgnoreCase))
        {
            return $"{name} is installed: it changes swim speed and stamina; {ModInfo.Name} dives at the swim speed "
                   + "and drain that result from it.";
        }
        if (string.Equals(guid, "MidnightsFX.ImpactfulSkills", StringComparison.OrdinalIgnoreCase))
        {
            return $"{name} is installed: its Swim speed bonus applies to swimming, not to diving; its lower swim "
                   + $"stamina cost applies to diving with {ModInfo.Name} too.";
        }
        if (name.IndexOf(NoUnderwaterCameraName, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return $"{name} is installed: it keeps the camera above the water, so the {ModInfo.Name} camera never "
                   + "goes under the surface. Diving still works.";
        }
        return $"{name} is installed: it changes the camera near water; {ModInfo.Name} works next to it.";
    }
}
