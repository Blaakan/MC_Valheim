using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;

namespace MC.Shared;

// Me = list of all MC mods alive in game, shared by every MC mod.
// Trick: each mod dll carry OWN copy of this code (Shared compiled in), so our types differ per mod.
// So shared data live in AppDomain slot and hold only BCL + BepInEx types (Dictionary, Func, ConfigEntry).
// Me never rename keys. New keys ok. Bump Contract when meaning change.
internal static class FeatureRegistry
{
    // Me framework version. Highest version mod become "leader" (draw panel, show notices).
    public const int FrameworkVersion = 1;

    private const string Slot = "MC.FeatureRegistry.v1";

    private static Dictionary<string, object> Root
    {
        get
        {
            var domain = AppDomain.CurrentDomain;
            if (domain.GetData(Slot) is Dictionary<string, object> root)
            {
                return root;
            }

            root = new Dictionary<string, object>
            {
                ["entries"] = new List<Dictionary<string, object>>(),
                ["refreshing"] = false,
            };
            domain.SetData(Slot, root);
            return root;
        }
    }

    private static List<Dictionary<string, object>> Entries => (List<Dictionary<string, object>>)Root["entries"];

    public static void Register(ModPlugin plugin)
    {
        var d = plugin.Mod;
        var entry = new Dictionary<string, object>
        {
            ["framework"] = FrameworkVersion,
            ["guid"] = d.Guid,
            ["name"] = d.Name,
            ["version"] = d.Version,
            ["build"] = d.Build,
            ["category"] = d.Category,
            ["scope"] = d.Scope,
            ["side"] = d.Side,
            ["multiplayer"] = d.Multiplayer,
            ["multiplayerNotes"] = d.MultiplayerNotes,
            ["requires"] = d.Requires,
            ["plugin"] = (BaseUnityPlugin)plugin,
            ["enabled"] = plugin.Enabled,
            ["state"] = (Func<string>)(() => plugin.State.ToString()),
            ["status"] = (Func<string>)(() => plugin.StatusText),
            ["refresh"] = (Func<bool>)plugin.Refresh,
        };

        var list = Entries;
        list.RemoveAll(e => e.TryGetValue("guid", out var g) && (string)g == d.Guid);
        list.Add(entry);
    }

    public static List<FeatureView> All()
    {
        var result = new List<FeatureView>();
        foreach (var e in Entries.ToArray())
        {
            result.Add(new FeatureView(e));
        }
        return result;
    }

    public static FeatureView? Find(string guid)
    {
        foreach (var e in Entries)
        {
            if (e.TryGetValue("guid", out var g) && (string)g == guid)
            {
                return new FeatureView(e);
            }
        }
        return null;
    }

    // Me re-check every MC mod until nothing change (dependency chains: C off -> B off -> A off).
    // Guard live in shared slot, so nested call from ANY mod copy just return.
    public static void RefreshAll()
    {
        var root = Root;
        if ((bool)root["refreshing"])
        {
            return;
        }

        root["refreshing"] = true;
        try
        {
            for (var pass = 0; pass < 10; pass++)
            {
                var changed = false;
                foreach (var e in Entries.ToArray())
                {
                    try
                    {
                        if (e.TryGetValue("refresh", out var r) && ((Func<bool>)r)())
                        {
                            changed = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Refresh of {e["guid"]} failed: {ex}");
                    }
                }

                if (!changed)
                {
                    break;
                }
            }
        }
        finally
        {
            root["refreshing"] = false;
        }
    }

    // Leader = highest framework version, tie -> smallest guid. Everybody compute same answer.
    public static bool IsLeader(string guid)
    {
        string best = null;
        var bestVersion = -1;
        foreach (var e in Entries)
        {
            var v = e.TryGetValue("framework", out var fv) ? (int)fv : 0;
            var g = (string)e["guid"];
            if (v > bestVersion || (v == bestVersion && string.CompareOrdinal(g, best) < 0))
            {
                best = g;
                bestVersion = v;
            }
        }
        return best == guid;
    }
}

// Me = read-only window on one registry entry. Safe across mod copies (only reads dictionary).
internal readonly struct FeatureView
{
    private readonly Dictionary<string, object> _e;

    public FeatureView(Dictionary<string, object> entry) => _e = entry;

    private string Str(string key) => _e.TryGetValue(key, out var v) && v is string s ? s : "";

    public string Guid => Str("guid");
    public string Name => Str("name");
    public string Version => Str("version");
    public string Build => Str("build");
    public string Category => Str("category");
    public string Scope => Str("scope");
    public string Side => Str("side");
    public string Multiplayer => Str("multiplayer");
    public string MultiplayerNotes => Str("multiplayerNotes");
    public string[] Requires => _e.TryGetValue("requires", out var v) && v is string[] a ? a : new string[0];
    public ConfigEntry<bool> Enabled => _e.TryGetValue("enabled", out var v) ? v as ConfigEntry<bool> : null;
    public string State => _e.TryGetValue("state", out var v) && v is Func<string> f ? f() : "Starting";
    public string Status => _e.TryGetValue("status", out var v) && v is Func<string> f ? f() : "";
    public bool IsActive => State == nameof(ModState.Active);
    public bool IsDisabledByUser => State == nameof(ModState.Disabled);

    // Short words for "needs X, which is ___".
    public string ShortState
    {
        get
        {
            switch (State)
            {
                case nameof(ModState.Active): return "active";
                case nameof(ModState.Disabled): return "turned off";
                case nameof(ModState.Error): return "failing to start";
                default: return "inactive";
            }
        }
    }
}
