using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using MC.Shared;

namespace MC.Farming.FishingFightMod;

// What me do about another loaded mod.
internal enum ForeignKind : byte
{
    None,
    Blocks,          // other owner of the hooked fight (both would reel, catch or destroy the float): me stand aside
    BlocksIfFishing, // owner of the fight only while its own fishing switch is on (read in its config)
    Composes,        // stamina, bait, rod or HUD mod that work next to me: log only
}

// Me = other fishing mods (design 6.3). GUIDs read in their sources (research 2026-10-01) unless marked.
// Plugin.LocalBlocker give the framework the Status text for a blocking mod: framework remove my patches, show it in
// the MC Mods panel, and tell the server "off" (a server that requires me refuse that player). Framework ask at every
// refresh (config, world, peer events, Start after all plugins loaded), never per frame; a change of the other mod's
// own fishing switch refresh every mod at once (FishingSwitchOn listen to it).
// Safety net for fight mods me not know: my FixedUpdate prefix stay out when an earlier prefix already skipped the
// tick (FishingFloatPatches).
internal static class ForeignMods
{
    // Own the whole hooked fight, always.
    private static readonly string[] BlockingGuids =
    {
        "sighsorry.TrollingFishing",   // several floats per player, own FixedUpdate prefix that can skip vanilla
    };

    // Own the fight only while their fishing switch is on: GUID, config section, key (read from their sources/README).
    private static readonly string[][] SwitchedGuids =
    {
        new[] { "com.GrindstoneSkills", "50 - Fishing", "Fishing Enabled" },   // tension fight, Escape rewrite
        // Stardew clone (closed source; GUID and key from its README).
        new[] { "Azumatt.Hooked", "2 - Minigame", "Enabled" },
    };

    // Closed source fight mods with no known GUID: plugin name or GUID contains one of these.
    private static readonly string[] BlockingNames =
    {
        "PeasFishing",   // Stardew-style bar on Left Shift
        "ChillHook",     // circle minigame (config file Andejx.ChillHook.cfg)
        "ChillFishing",  // tension and fake nibbles
    };

    // Work next to me. EpicLoot and FeastMaster: void FixedUpdate prefix (flag / field scaling for one tick) that
    // run before mine (my prefix is Low priority), so their stamina discounts count on my costs. ComfyFishing: take
    // over only its own better rods by default (its prefix run first and skip; mine then stay out). Angler's Eye: turn
    // its smart reel off when another mod patch FixedUpdate (me); its struggle hint still show. VHVR: rod top and reel
    // gesture from VR hands. Reely SpecTackleLure: rods and bait (closed source).
    private static readonly string[] ComposingGuids =
    {
        "randyknapp.mods.epicloot",
        "com.FeastMaster",
        "games.loxley.comfyfishing",
        "com.jumpingmushroom.anglerseye",
        "org.bepinex.plugins.valheimvrmod",
        "neobotics.valheim_mod.reelyspectacklelure",
    };

    private static readonly HashSet<string> Logged = new HashSet<string>();

    // Pure (self test hammer it).
    internal static ForeignKind Classify(string guid, string name)
    {
        if (!string.IsNullOrEmpty(guid))
        {
            if (Contains(BlockingGuids, guid))
            {
                return ForeignKind.Blocks;
            }
            foreach (var s in SwitchedGuids)
            {
                if (string.Equals(s[0], guid, StringComparison.OrdinalIgnoreCase))
                {
                    return ForeignKind.BlocksIfFishing;
                }
            }
            if (Contains(ComposingGuids, guid))
            {
                return ForeignKind.Composes;
            }
        }
        var any = (name ?? "") + " " + (guid ?? "");
        foreach (var n in BlockingNames)
        {
            if (any.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ForeignKind.Blocks;
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
            var guid = info.Metadata.GUID;
            var name = string.IsNullOrEmpty(info.Metadata.Name) ? guid : info.Metadata.Name;
            var kind = Classify(guid, info.Metadata.Name);
            if (kind == ForeignKind.BlocksIfFishing)
            {
                kind = FishingSwitchOn(info.Instance, guid) ? ForeignKind.Blocks : ForeignKind.Composes;
            }
            switch (kind)
            {
                case ForeignKind.Blocks:
                    if (Logged.Add("block:" + guid))
                    {
                        Log.Warning($"{name} also handles the fishing fight, so {ModInfo.Name} stays off. Remove one "
                                    + $"of them{SwitchHint(guid)}. A server that requires {ModInfo.Name} refuses this "
                                    + "game while both are on.");
                    }
                    blocker ??= name;
                    break;
                case ForeignKind.Composes:
                    if (Logged.Add("compose:" + guid))
                    {
                        Log.Info(ComposeText(guid, name));
                    }
                    break;
            }
        }
        return blocker == null ? null : $"Inactive: {blocker} also handles the fishing fight. Remove one of them.";
    }

    // Their fishing switch; not found or unreadable = on (safe side: never two fight owners). Bool, or a toggle enum
    // whose name say On/Off (closed source mods: type not known). Me listen to their config once: their server can
    // push a new value while connected (GrindstoneSkills sync it), so every mod refresh at once (me on or off, and the
    // server told) instead of at the next join.
    private static bool FishingSwitchOn(BaseUnityPlugin plugin, string guid)
    {
        try
        {
            foreach (var s in SwitchedGuids)
            {
                if (!string.Equals(s[0], guid, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var config = plugin.Config;
                var definition = new ConfigDefinition(s[1], s[2]);
                if (config == null || !config.ContainsKey(definition))
                {
                    return true;
                }
                var entry = config[definition];
                Listen(config);
                var value = entry.BoxedValue;
                if (value is bool on)
                {
                    return on;
                }
                var text = value != null ? value.ToString() : "";
                return !(string.Equals(text, "Off", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(text, "False", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(text, "Disabled", StringComparison.OrdinalIgnoreCase));
            }
        }
        catch (Exception)
        {
            // Their config changed shape: assume on.
        }
        return true;
    }

    private static readonly HashSet<ConfigFile> Listened = new HashSet<ConfigFile>();

    // Their whole config file (only ConfigEntry<T> and ConfigFile have the event; the switch type is not known).
    private static void Listen(ConfigFile config)
    {
        if (config == null || !Listened.Add(config))
        {
            return;
        }
        config.SettingChanged += OnSwitchChanged;
    }

    // Plain event (not a patch: my patches are off while their fight block me). Only their fishing switch count.
    // Raised by their config code: never throw back.
    private static void OnSwitchChanged(object sender, SettingChangedEventArgs e)
    {
        try
        {
            var definition = e != null && e.ChangedSetting != null ? e.ChangedSetting.Definition : null;
            if (definition == null || !IsSwitch(definition))
            {
                return;
            }
            FeatureRegistry.RefreshAll();
        }
        catch (Exception ex)
        {
            PatchGuard.Report("ForeignMods switch changed", ex);
        }
    }

    private static bool IsSwitch(ConfigDefinition definition)
    {
        foreach (var s in SwitchedGuids)
        {
            if (definition.Section == s[1] && definition.Key == s[2])
            {
                return true;
            }
        }
        return false;
    }
    private static string SwitchHint(string guid)
    {
        foreach (var s in SwitchedGuids)
        {
            if (string.Equals(s[0], guid, StringComparison.OrdinalIgnoreCase))
            {
                return $", or turn its own fishing fight off ([{s[1]}] {s[2]} = false)";
            }
        }
        return "";
    }

    private static string ComposeText(string guid, string name)
    {
        if (string.Equals(guid, "com.GrindstoneSkills", StringComparison.OrdinalIgnoreCase)
            || string.Equals(guid, "Azumatt.Hooked", StringComparison.OrdinalIgnoreCase))
        {
            return $"{name} is installed with its own fishing fight turned off: {ModInfo.Name} runs the fight.";
        }
        if (string.Equals(guid, "games.loxley.comfyfishing", StringComparison.OrdinalIgnoreCase))
        {
            return $"{name} is installed: its own better rods use its auto-reel (when its VanillaRodVanillaReel "
                   + $"setting is on, the default); the normal fishing rod uses the {ModInfo.Name} fight.";
        }
        if (string.Equals(guid, "com.jumpingmushroom.anglerseye", StringComparison.OrdinalIgnoreCase))
        {
            return $"{name} is installed: its smart reel turns itself off next to {ModInfo.Name}; its struggle "
                   + "hint still shows when the fish fights.";
        }
        if (string.Equals(guid, "org.bepinex.plugins.valheimvrmod", StringComparison.OrdinalIgnoreCase))
        {
            return $"{name} is installed: in a fight the rod direction is the way your body faces (not tested in VR).";
        }
        return $"{name} is installed: it changes fishing stamina, rods or bait; {ModInfo.Name} works next to it.";
    }

    private static bool Contains(string[] list, string guid)
    {
        foreach (var g in list)
        {
            if (string.Equals(g, guid, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
