using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using MC.Shared;

namespace MC.Exploration.ViewDistantHorizonsMod;

// What me do about another loaded mod.
internal enum ForeignKind : byte
{
    None,
    Blocks,     // also draw far terrain: both draw land twice, fight over far clip and fog -> me stand aside
    Composes,   // work next to me but worth one log line
}

// Me = other far-view mods (research 2026-10-02, GUIDs read in their plugin attributes). Plugin.LocalBlocker give the
// framework the Status text for a blocking mod: framework remove my patches and show why in the MC Mods panel.
// Framework ask at every refresh (config, world, Start after all plugins loaded), never per frame.
internal static class ForeignMods
{
    // Standalone Distant Horizons: me come from it.
    private const string StandaloneGuid = "com.distanthorizons.valheim";

    private static readonly string[] BlockingGuids =
    {
        StandaloneGuid,
        "marc.donegalhorizonlift",      // New Horizons: Treelines (far terrain underlay, far tree cards)
    };

    private static readonly string[] ComposingGuids =
    {
        "com.Skarif.ValheimPerformanceOverhaul_WATER", // its "Distant Terrain LOD Improvements" thin the fog too
    };

    private static readonly HashSet<string> Logged = new HashSet<string>();

    // Pure.
    internal static ForeignKind Classify(string guid)
    {
        if (string.IsNullOrEmpty(guid))
        {
            return ForeignKind.None;
        }
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
        return ForeignKind.None;
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
        foreach (var fake in TestPlugins)
        {
            Consider(fake.Key, fake.Value, ref blocker);
        }
#endif
        return blocker == null ? null : $"Inactive: {blocker} also draws the distant terrain. Remove one of them.";
    }

    // One loaded plugin: log once what me do about it, first blocking one give the Status text.
    private static void Consider(string guid, string pluginName, ref string blocker)
    {
        var name = string.IsNullOrEmpty(pluginName) ? guid : pluginName;
        if (string.Equals(guid, StandaloneGuid, StringComparison.OrdinalIgnoreCase))
        {
            name = "The standalone Distant Horizons (BepInEx/plugins/DistantHorizons)"; // same name as me: say which
        }
        switch (Classify(guid))
        {
            case ForeignKind.Blocks:
                if (Logged.Add(guid))
                {
                    Log.Warning($"{name} also draws the distant terrain, so {ModInfo.Name} stays off. Remove one of them.");
                }
                blocker ??= name;
                break;
            case ForeignKind.Composes:
                if (Logged.Add(guid))
                {
                    Log.Info($"{name} is installed: its distant terrain option also thins the fog, so with both the fog "
                             + $"is thinned twice. Set one of the two fog settings back to 1 if the fog looks too thin.");
                }
                break;
        }
    }

#if DEBUG
    // Self test: plugins that count as loaded here though they are not (GUID, name). In memory only.
    internal static readonly List<KeyValuePair<string, string>> TestPlugins = new List<KeyValuePair<string, string>>();

    // Self test: log line for this GUID may come again (it come once per game session).
    internal static void ForgetLoggedForTest(string guid) => Logged.Remove(guid);
#endif
}
