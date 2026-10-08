using System;
using BepInEx.Bootstrap;
using MC.Shared;
#if DEBUG
using System.Linq;
using System.Text;
#endif

namespace MC.Combat.WeaponsMovesetMod;

// Me = other mods me must know about (design 6.2). Goo's Combat Overhaul (GCO) have own jump attack on same input
// (jump + primary): two jump attacks on one swing = mess, so our jump attack stand down when GCO here. Roll attack
// stay (GCO no have one). Me look late (first use, in world), not in OnActivated: first activation run inside our
// Awake, and BepInEx put a plugin in PluginInfos only when it load it (plugins after "MC." not there yet).
// Debug build: same first look log every loaded plugin (guid, name, version), to check GUIDs of design 6.2.
internal static class Compat
{
    // GCO GUID from its config file name (unverified). Also its Harmony id guess for [HarmonyAfter] (unknown id =
    // ignored by Harmony).
    internal const string GcoGuid = "goo.valheim.gooscombatoverhaul";

    private static bool _detected;
    private static bool _gco;

#if DEBUG
    // Self test: pretend GCO is here (true) or not here (false), never a real plugin. Null = look at loaded plugins.
    // Test set it, call Reset(), and clear it + Reset() again in its finally.
    internal static bool? TestGco { get; set; }
#endif

    // First read after activation look at loaded plugins (Info line once per activation when GCO found).
    internal static bool GcoLoaded
    {
        get
        {
            if (!_detected)
            {
                Detect();
            }
            return _gco;
        }
    }

    // OnActivated: look again at next use.
    internal static void Reset()
    {
        _detected = false;
        _gco = false;
    }

    private static void Detect()
    {
        _detected = true;
        _gco = FindGco();
        if (_gco)
        {
            Log.Info($"Goo's Combat Overhaul is installed: it has its own jump attack, so {ModInfo.Name}'s jump attack "
                     + "stays off (the roll attack still works).");
        }
#if DEBUG
        LogPlugins();
#endif
    }

    // By GUID, else by name (GUID unverified): name has "Goo" and "Combat Overhaul".
    private static bool FindGco()
    {
#if DEBUG
        if (TestGco.HasValue)
        {
            return TestGco.Value;
        }
#endif
        if (Chainloader.PluginInfos.ContainsKey(GcoGuid))
        {
            return true;
        }
        foreach (var info in Chainloader.PluginInfos.Values)
        {
            var name = info?.Metadata?.Name;
            if (!string.IsNullOrEmpty(name)
                && name.IndexOf("Goo", StringComparison.OrdinalIgnoreCase) >= 0
                && name.IndexOf("Combat Overhaul", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }
        return false;
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
