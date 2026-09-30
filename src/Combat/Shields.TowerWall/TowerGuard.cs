using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BepInEx.Bootstrap;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.ShieldsTowerWallMod;

// Me = warn once when other mods that change shields, blocking or attacks are installed (design 6.3). No stand-down:
// me only name them in one log Warning. Two ways, so unknown or unverified GUIDs still get caught:
//   1. known shield/block mods in Chainloader.PluginInfos, by GUID or by name (letters only, any case);
//   2. any Harmony owner that is not MC with a prefix or transpiler on Humanoid.BlockAttack, a prefix on
//      Humanoid.StartAttack, or any patch on Humanoid.GetCurrentWeapon.
// Called at first ObjectDB pass with items (main menu: every plugin loaded and patched) and from OnActivated when the
// ObjectDB already has items (mod turned on later). Reset on each activation: one Warning per activation.
internal static class TowerGuard
{
    private readonly struct Known
    {
        internal readonly string Guid;   // exact GUID when known (null = unknown)
        internal readonly string Key;    // letters of the plugin name or GUID, lower case

        internal Known(string guid, string key)
        {
            Guid = guid;
            Key = key;
        }
    }

    // Design 6.3 table. GUIDs marked unverified there are matched by name too.
    private static readonly Known[] KnownMods =
    {
        new Known("sighsorry.CaptainValheim", "captainvalheim"),
        new Known("sighsorry.SecondaryAttacks", "secondaryattacks"),
        new Known(null, "zencombat"),
        new Known(null, "combatoverhaul"),          // Goo's Combat Overhaul
        new Known(null, "shieldbash"),
        new Known(null, "reliableblock"),           // ReliableBlock, ReliableBlockRebuilt
        new Known("WackyMole.WackysDatabase", "wackysdatabase"),
        new Known(null, "weaponarts"),
        new Known("vapok.mods.shieldmebruh", "shieldmebruh"),
        new Known(null, "smartshield"),
        new Known(null, "autoshield"),
        new Known("randyknapp.mods.epicloot", "epicloot"),
        new Known(null, "combatadjustments"),
    };

    private static bool _done;

    internal static void Reset() => _done = false;

    // OnActivated: mod turned on after the main menu = ObjectDB already full, check now.
    internal static void WarnIfLoaded()
    {
        var db = ObjectDB.instance;
        if (db != null && db.m_items != null && db.m_items.Count > 0)
        {
            WarnOnce();
        }
    }

    // ObjectDB postfix (first pass with items) and WarnIfLoaded. Never throw: a broken scan only lose the warning.
    internal static void WarnOnce()
    {
        if (_done)
        {
            return;
        }
        _done = true;
        try
        {
            var names = new List<string>();
            var reasons = new Dictionary<string, List<string>>();
            FindKnownPlugins(names, reasons);
            FindForeignPatches(names, reasons);
            if (names.Count == 0)
            {
                return;
            }
            var sb = new StringBuilder();
            foreach (var name in names)
            {
                if (sb.Length > 0)
                {
                    sb.Append(", ");
                }
                sb.Append(name);
                if (reasons.TryGetValue(name, out var why) && why.Count > 0)
                {
                    sb.Append(" (").Append(string.Join(", ", why.ToArray())).Append(')');
                }
            }
            Log.Warning($"Other mods that change shields, blocking or attacks are installed: {sb}. {ModInfo.Name} makes "
                        + "tower shields two-handed and changes how they block and attack, so these mods may treat "
                        + "tower shields differently or change the same values. The Compatibility section of the "
                        + $"{ModInfo.Name} README says how they combine.");
        }
        catch (Exception e)
        {
            PatchGuard.Report("TowerGuard.WarnOnce", e);
        }
    }

    private static void FindKnownPlugins(List<string> names, Dictionary<string, List<string>> reasons)
    {
        foreach (var pair in Chainloader.PluginInfos)
        {
            var guid = pair.Key ?? "";
            if (IsOurs(guid))
            {
                continue;
            }
            var info = pair.Value;
            var name = info != null && info.Metadata != null ? info.Metadata.Name : null;
            var letters = Letters(name) + "|" + Letters(guid);
            foreach (var known in KnownMods)
            {
                if (string.Equals(known.Guid, guid, StringComparison.OrdinalIgnoreCase)
                    || letters.IndexOf(known.Key, StringComparison.Ordinal) >= 0)
                {
                    Add(names, reasons, string.IsNullOrEmpty(name) ? guid : name, null);
                    break;
                }
            }
        }
    }

    private static void FindForeignPatches(List<string> names, Dictionary<string, List<string>> reasons)
    {
        var block = AccessTools.DeclaredMethod(typeof(Humanoid), nameof(Humanoid.BlockAttack),
            new[] { typeof(HitData), typeof(Character) });
        var attack = AccessTools.DeclaredMethod(typeof(Humanoid), nameof(Humanoid.StartAttack),
            new[] { typeof(Character), typeof(bool) });
        var weapon = AccessTools.DeclaredMethod(typeof(Humanoid), nameof(Humanoid.GetCurrentWeapon), Type.EmptyTypes);

        var blockInfo = Info(block);
        if (blockInfo != null)
        {
            AddOwners(blockInfo.Prefixes, "changes blocking", names, reasons);
            AddOwners(blockInfo.Transpilers, "changes blocking", names, reasons);
        }
        var attackInfo = Info(attack);
        if (attackInfo != null)
        {
            AddOwners(attackInfo.Prefixes, "changes attacks", names, reasons);
        }
        var weaponInfo = Info(weapon);
        if (weaponInfo != null)
        {
            const string why = "changes which weapon is used";
            AddOwners(weaponInfo.Prefixes, why, names, reasons);
            AddOwners(weaponInfo.Postfixes, why, names, reasons);
            AddOwners(weaponInfo.Transpilers, why, names, reasons);
            AddOwners(weaponInfo.Finalizers, why, names, reasons);
        }
    }

    // Full name: plain "Patches" is our own Patches namespace here.
    private static HarmonyLib.Patches Info(MethodBase method) => method != null ? Harmony.GetPatchInfo(method) : null;

    private static void AddOwners(IEnumerable<Patch> patches, string why, List<string> names,
        Dictionary<string, List<string>> reasons)
    {
        if (patches == null)
        {
            return;
        }
        foreach (var p in patches)
        {
            var owner = p != null ? p.owner : null;
            if (string.IsNullOrEmpty(owner) || IsOurs(owner))
            {
                continue;
            }
            // Harmony id is often the plugin GUID: show the plugin name then.
            var name = Chainloader.PluginInfos.TryGetValue(owner, out var info) && info != null && info.Metadata != null
                ? info.Metadata.Name
                : owner;
            Add(names, reasons, name, why);
        }
    }

    private static void Add(List<string> names, Dictionary<string, List<string>> reasons, string name, string why)
    {
        if (!reasons.TryGetValue(name, out var list))
        {
            list = new List<string>();
            reasons[name] = list;
            names.Add(name);
        }
        if (why != null && !list.Contains(why))
        {
            list.Add(why);
        }
    }

    // Every MC mod (and its framework / always-on Harmony ids) start with "MC.".
    private static bool IsOurs(string id) => id.StartsWith("MC.", StringComparison.Ordinal);

    private static string Letters(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }
        return sb.ToString();
    }
}
