using System;
using BepInEx.Bootstrap;
using MC.Shared;
#if DEBUG
using System.Linq;
using System.Text;
#endif

namespace MC.Exploration.SailingSkillMod;

// Me = other mods that also change ships or add a sailing skill (design 6.3). None block me: their changes and mine
// stack (delta postfixes; scaled fields put back only while they still hold my value, so a mod that set them once or
// scale and put them back itself keep its value), me only say so in the log once per activation. GUIDs of these
// mods are unverified: me find them by GUID or plugin name markers. Mods that REPLACE ship physics (a prefix that skip
// Ship.CustomFixedUpdate or GetSailForce) silently turn my handling bonuses off: log line say "may".
// Me look late (first frame of the local player, in a world), not in OnActivated: first activation run inside our
// Awake, and BepInEx put a plugin in PluginInfos only when it load it (plugins after "MC." not there yet).
// Debug build: same first look log every loaded plugin (guid, name, version), to check the guessed markers.
internal static class Compat
{
    private sealed class Known
    {
        internal string[] Markers;
        internal string Text;
    }

    // Name or GUID parts (case-insensitive). Texts: what the other mod does (from its page), and what that means here.
    private static readonly Known[] KnownMods =
    {
        new Known
        {
            Markers = new[] { "GrindstoneSkills" },
            Text = "adds its own Sailing skill with ship speed, ship health and map reveal bonuses. Both skills level "
                   + "up side by side and their bonuses stack.",
        },
        new Known
        {
            Markers = new[] { "ImpactfulSkills" },
            Text = "has a Voyager skill (paddle speed, less boat damage, smaller wind penalty). Its bonuses and "
                   + ModInfo.Name + "'s stack.",
        },
        new Known
        {
            Markers = new[] { "ValheimRAFT" },
            Text = "changes how ships are built and simulated. " + ModInfo.Name + "'s handling bonuses may not apply to "
                   + "its ships.",
        },
        new Known
        {
            // Smoothbrain Sailing (blaxxun-boop): plugin name "Sailing". Also other "... Sailing" mods (Smooth Sailing).
            Markers = new[] { "Sailing" },
            Text = "also changes sailing (for example a Sailing skill, ship speed or wind). Its bonuses and "
                   + ModInfo.Name + "'s stack; with two Sailing skills, the console command raiseskill sailing may "
                   + "raise both.",
        },
    };

    private static bool _detected;

    // OnActivated: look again at next use.
    internal static void Reset()
    {
        _detected = false;
    }

    // First local player frame in a world. Cheap after first time: one bool.
    internal static void Ensure()
    {
        if (!_detected)
        {
            Detect();
        }
    }

    // Pure name test (self test): plugin name or GUID hold a marker.
    internal static bool Matches(string nameOrGuid, string[] markers)
    {
        if (string.IsNullOrEmpty(nameOrGuid))
        {
            return false;
        }
        foreach (var marker in markers)
        {
            if (nameOrGuid.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    private static void Detect()
    {
        _detected = true;
        foreach (var pair in Chainloader.PluginInfos)
        {
            var meta = pair.Value != null ? pair.Value.Metadata : null;
            if (meta == null || string.Equals(meta.GUID, ModInfo.Guid, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            foreach (var known in KnownMods)
            {
                if (Matches(meta.Name, known.Markers) || Matches(meta.GUID, known.Markers))
                {
                    var name = string.IsNullOrEmpty(meta.Name) ? meta.GUID : meta.Name;
                    Log.Info($"{name} is installed: it {known.Text}");
                    break;
                }
            }
        }
#if DEBUG
        LogPlugins();
#endif
    }

#if DEBUG
    private static void LogPlugins()
    {
        var sb = new StringBuilder("Loaded plugins (GUID, name, version): ");
        var first = true;
        foreach (var info in Chainloader.PluginInfos.Values.OrderBy(i => i?.Metadata?.GUID, StringComparer.Ordinal))
        {
            var meta = info?.Metadata;
            if (meta == null)
            {
                continue;
            }
            sb.Append(first ? "" : "; ").Append(meta.GUID).Append(", ").Append(meta.Name).Append(", ")
                .Append(meta.Version);
            first = false;
        }
        Log.Debug(sb.ToString());
    }
#endif
}
