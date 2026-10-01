using System;
using System.Linq;
using System.Text;
using BepInEx.Bootstrap;
using MC.Shared;

namespace MC.Combat.TrinketsOnDemandMod;

// Me = other adrenaline mods that compose with me: log only, no block (design 6.3). GUIDs unverified (but Surge's):
// name or GUID markers (ForeignMods.Flatten). Long marker = substring. Short marker "surge" = whole flat name or exact
// GUID only (its source: ezomic.valheim.surge), else "Resurgence" and friends match too.
// Me look late (first use in world), not in OnActivated: first activation run inside our Awake, and BepInEx put a
// plugin in PluginInfos only when it load it (plugins after "MC." not there yet). OnActivated reset me; Info lines
// once per activation. Same first look log every loaded plugin (GUID, name, version) at Debug level, so a tester can
// check the guessed markers here. Only while me active: blocker (ForeignMods) keep me off, its Warning name its GUID.
internal static class Compat
{
    internal const string SurgeMarker = "surge";
    internal const string SurgeGuid = "ezomic.valheim.surge";

    private static bool _detected;

    internal static void Reset() => _detected = false;

    // First use in a session (Player.Update postfix). Cheap after first time: one bool.
    internal static void Ensure()
    {
        if (!_detected)
        {
            _detected = true;
            Detect();
        }
    }

    private static void Detect()
    {
        Say("adrenalinemodifier", "it scales adrenaline gains; this mod's income goes through the same gain, so its "
                                  + "scaling applies to the income too.");
        Say("keepadrenalinelonger", "it lengthens the delay before the bar drains; while a trinket is equipped this "
                                    + "mod keeps the bar from draining at all, so its setting only matters without a "
                                    + "trinket.");
        Say("ragenadrenaline", "it adds its own adrenaline abilities and keys (by default F, G, D-pad up and left); "
                               + "keep this mod's trigger on another key.");
        Say("multitrinket", "it lets you wear several trinkets; one press of this mod's key fires every equipped "
                            + "trinket, as the normal game's full bar does.");
        Say(SurgeMarker, "it changes trinket costs; this mod's full bar follows the cost of the trinkets you wear.",
            SurgeGuid);
        LogPlugins();
    }

    private static void Say(string marker, string what, string exactGuid = null)
    {
        var name = FindByMarker(marker, exactGuid);
        if (name != null)
        {
            Log.Info($"{name} is installed: {what}");
        }
    }

    private static string FindByMarker(string marker, string exactGuid)
    {
        foreach (var info in Chainloader.PluginInfos.Values)
        {
            var meta = info?.Metadata;
            if (meta == null || string.Equals(meta.GUID, ModInfo.Guid, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (IsMatch(marker, exactGuid, meta.Name, meta.GUID))
            {
                return string.IsNullOrEmpty(meta.Name) ? meta.GUID : meta.Name;
            }
        }
        return null;
    }

    // Pure (self test): plugin (name, GUID) is the mod of this marker. exactGuid set (short marker): whole flat name
    // = marker, or GUID = exactGuid. Else marker inside flat name or flat GUID.
    internal static bool IsMatch(string marker, string exactGuid, string name, string guid)
    {
        if (string.IsNullOrEmpty(marker))
        {
            return false;
        }
        if (exactGuid != null)
        {
            return string.Equals(guid, exactGuid, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(ForeignMods.Flatten(name), marker, StringComparison.Ordinal);
        }
        return ForeignMods.Flatten(name).IndexOf(marker, StringComparison.Ordinal) >= 0
               || ForeignMods.Flatten(guid).IndexOf(marker, StringComparison.Ordinal) >= 0;
    }

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
}
