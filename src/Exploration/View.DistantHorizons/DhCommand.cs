using System;
using System.Globalization;
using BepInEx.Configuration;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.ViewDistantHorizonsMod;

// Me = F5 console command "dh" (console need -console launch option). Not a cheat. Me register while feature is
// Active, remove on deactivate (only our own entry). Release: stats, rebuild, envs, objects, get/set, off.
// Debug build also: dump, atlas, shot, terrain (tile inspector and live render experiments).
internal static class DhCommand
{
    private const string Name = "dh";
    private const string Prefix = "DistantHorizons: ";
#if DEBUG
    private const string Help =
        "Distant Horizons: dh [stats] | dh rebuild | dh envs | dh objects [on|off|rebuild] | dh get|set <Setting> [value]"
        + " | dh off | debug: dh dump | dh atlas | dh shot [seconds] | dh terrain [depth0|depthreset|hide <m>|shadows on|off"
        + "|layer <n>|material zone|lod|keyword on|off|vanilla on|off|copies off|zone|far|lod|event before|after|seabg on|off"
        + "|seafog on|off|water on|off|ambient on|off|paintonly on|off|paintlift <m>|skirt on|off]";
#else
    private const string Help =
        "Distant Horizons: dh [stats] | dh rebuild | dh envs | dh objects [on|off|rebuild] | dh get|set <Setting> [value]"
        + " | dh off";
#endif

    private static Terminal.ConsoleCommand _command;

    internal static void Register()
    {
        // Constructor put itself in static Terminal.commands.
        _command = new Terminal.ConsoleCommand(Name, Help, new Terminal.ConsoleEvent(Run), isCheat: false);
    }

    internal static void Unregister()
    {
        // Only our own entry: another mod may have taken the name after us. Safe from inside our own Run (dh off):
        // the game already hold the command it is running.
        if (_command != null && Terminal.commands.TryGetValue(Name, out var current) && ReferenceEquals(current, _command))
        {
            Terminal.commands.Remove(Name);
        }
        _command = null;
    }

    private static void Run(Terminal.ConsoleEventArgs args)
    {
        try
        {
            Dispatch(args);
        }
        catch (Exception e)
        {
            PatchGuard.Report("DhCommand.Run", e);
            args.Context.AddString(Prefix + "command failed, see BepInEx/LogOutput.log");
        }
    }

    private static void Dispatch(Terminal.ConsoleEventArgs args)
    {
        var sub = args.Args.Length > 1 ? args.Args[1].ToLowerInvariant() : "stats";
        var mgr = LodTerrainManager.Instance;
        switch (sub)
        {
            case "off":
                // Same switch as MC Mods panel: whole feature go off (this command too).
                args.Context.AddString(Prefix + "OFF (vanilla distant terrain restored). Turn it back on in the MC Mods panel "
                                       + "(Esc menu), a configuration manager (F1) or General.Enabled in the config file.");
                Plugin.Instance.Enabled.Value = false;
                break;
            case "rebuild":
                if (mgr == null)
                {
                    args.Context.AddString(Prefix + "not active (not in a world?)");
                    break;
                }
                mgr.Rebuild();
                args.Context.AddString(Prefix + "rebuilding all LOD tiles");
                break;
            case "envs":
                Lines(args, Diagnostics.DescribeEnvironments(), log: true);
                break;
            case "get":
            case "set":
                GetSet(args, sub == "set");
                break;
            case "objects":
                Objects(args);
                break;
#if DEBUG
            case "dump":
                Diagnostics.DumpOnce(force: true);
                args.Context.AddString(Prefix + "diagnostics written to BepInEx/LogOutput.log");
                break;
            case "atlas":
            {
                var om = DistantObjectManager.Instance;
                var path = om != null ? om.DumpAtlas() : null;
                args.Context.AddString(path != null ? Prefix + "atlas written to " + path : Prefix + "no atlas yet");
                break;
            }
            case "shot":
            {
                if (mgr == null)
                {
                    args.Context.AddString(Prefix + "not active (not in a world?)");
                    break;
                }
                var delay = args.Args.Length > 2 ? ParseFloat(args.Args[2], 2f) : 2f;
                var path = mgr.Screenshot(delay);
                args.Context.AddString($"{Prefix}screenshot in {delay:0.#} s (close the console) -> {path}");
                break;
            }
            case "terrain":
                if (mgr == null)
                {
                    args.Context.AddString(Prefix + "not active (not in a world?)");
                    break;
                }
                Terrain(args, mgr);
                break;
#endif
            default:
                args.Context.AddString(mgr == null ? Prefix + "not active (not in a world?)" : mgr.GetStats());
                break;
        }
    }

    // dh get|set <Setting> [value]: only my own settings (General = framework Enabled and Status, not mine).
    private static void GetSet(Terminal.ConsoleEventArgs args, bool set)
    {
        if (args.Args.Length < 3)
        {
            args.Context.AddString("usage: dh get <Setting> | dh set <Setting> <value>");
            return;
        }
        var name = args.Args[2];
        ConfigEntryBase found = null;
        foreach (var kv in Plugin.Cfg.File)
        {
            if (kv.Key.Section != "General" && string.Equals(kv.Key.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                found = kv.Value;
                break;
            }
        }
        if (found == null)
        {
            args.Context.AddString(Prefix + "unknown setting " + name);
            return;
        }
        if (set)
        {
            if (args.Args.Length < 4)
            {
                args.Context.AddString("usage: dh set <Setting> <value>");
                return;
            }
            try
            {
                found.SetSerializedValue(string.Join(" ", args.Args, 3, args.Args.Length - 3));
            }
            catch (Exception e)
            {
                args.Context.AddString(Prefix + e.Message);
                return;
            }
        }
        args.Context.AddString($"{Prefix}{found.Definition.Key} = {found.GetSerializedValue()}");
    }

    private static void Objects(Terminal.ConsoleEventArgs args)
    {
        var action = args.Args.Length > 2 ? args.Args[2].ToLowerInvariant() : "stats";
        if (action == "on" || action == "off")
        {
            Plugin.Cfg.ObjectsEnabled.Value = action == "on";
            args.Context.AddString(Prefix + "far objects " + action.ToUpperInvariant());
            return;
        }
        var om = DistantObjectManager.Instance;
        if (om == null)
        {
            args.Context.AddString(Prefix + "objects not active");
            return;
        }
        if (action == "rebuild")
        {
            om.Rebuild();
            args.Context.AddString(Prefix + "rebuilding all object tiles");
            return;
        }
        args.Context.AddString(om.GetStats());
    }

#if DEBUG
    // Tile inspector and live render experiments (dh rebuild or mod off then on reset their state).
    private static void Terrain(Terminal.ConsoleEventArgs args, LodTerrainManager mgr)
    {
        var action = args.Args.Length > 2 ? args.Args[2].ToLowerInvariant() : "dump";
        var arg = args.Args.Length > 3 ? args.Args[3].ToLowerInvariant() : "";
        switch (action)
        {
            case "depth0":
                args.Context.AddString($"{Prefix}_depth zeroed on {mgr.SetDepthExperiment(true)} tiles");
                break;
            case "depthreset":
                args.Context.AddString($"{Prefix}_depth restored on {mgr.SetDepthExperiment(false)} tiles");
                break;
            case "hide":
            {
                var m = arg.Length > 0 ? ParseFloat(arg, -1f) : -1f;
                mgr.SetHideOverride(m);
                args.Context.AddString($"{Prefix}hide distance now {mgr.HideDistance():0.#} m" + (m < 0f ? " (from config)" : " (override)"));
                break;
            }
            case "shadows":
                args.Context.AddString($"{Prefix}shadows {arg} on {mgr.SetShadowExperiment(arg == "on", arg == "on")} tiles");
                break;
            case "layer":
            {
                int.TryParse(arg, out var layer);
                args.Context.AddString($"{Prefix}layer {layer} on {mgr.SetLayerExperiment(layer)} tiles");
                break;
            }
            case "copies":
            {
                var mode = arg == "off" ? 0 : arg == "zone" ? 1 : arg == "far" ? 2 : arg == "lod" ? 3 : -1;
                if (mode < 0)
                {
                    args.Context.AddString("usage: dh terrain copies off|zone|far|lod");
                    break;
                }
                mgr.CopyMode = mode;
                args.Context.AddString(Prefix + "real-zone copies " + arg);
                break;
            }
            case "event":
                mgr.SetCommandBufferEvent(arg == "before");
                args.Context.AddString(Prefix + "command buffer " + (arg == "before" ? "BEFORE" : "AFTER") + " the opaque pass");
                break;
            case "seabg":
                mgr.SeaBlackBackground = arg != "off";
                args.Context.AddString(Prefix + "far sea black background " + (mgr.SeaBlackBackground ? "ON" : "OFF"));
                break;
            case "seafog":
                Plugin.Cfg.FarWaterFog.Value = arg != "off";
                args.Context.AddString(Prefix + "far sea fog scaling " + (Plugin.Cfg.FarWaterFog.Value ? "ON" : "OFF"));
                break;
            case "water":
                if (arg == "on" || arg == "off")
                {
                    Plugin.Cfg.FarWater.Value = arg == "on";
                }
                args.Context.AddString(Prefix + mgr.Water.Describe());
                break;
            case "ambient":
                mgr.SkipAmbient = arg == "off";
                args.Context.AddString(Prefix + "painted ambient probe " + (mgr.SkipAmbient ? "OFF" : "ON"));
                break;
            case "paintonly":
                mgr.PaintOnly = arg == "on";
                args.Context.AddString(Prefix + "paint only " + (mgr.PaintOnly ? "ON (depth renderers hidden)" : "OFF"));
                break;
            case "paintlift":
            {
                var lift = arg.Length > 0 ? ParseFloat(arg, -1f) : -1f;
                mgr.PaintLift = lift;
                args.Context.AddString($"{Prefix}paint lift {(lift < 0f ? "default" : lift.ToString("0.##") + " m")}");
                break;
            }
            case "skirt":
                mgr.SetSkirtDisabled(arg == "off");
                args.Context.AddString(Prefix + "skirt " + (arg == "off" ? "OFF" : "ON"));
                break;
            case "vanilla":
                mgr.SetVanillaCompare(arg == "on");
                args.Context.AddString(Prefix + "vanilla 3x3 LOD compare " + (arg == "on"
                    ? "ON (our tiles hidden, fog thinning and far objects kept)"
                    : "OFF (our tiles rebuild)"));
                break;
            case "material":
            {
                var zone = arg == "zone";
                args.Context.AddString($"{Prefix}{(zone ? "real-zone" : "LOD")} material on {mgr.SetMaterialExperiment(zone)} tiles");
                break;
            }
            case "keyword":
                args.Context.AddString($"{Prefix}_ISDISTANTLOD_ON {arg} on {mgr.SetKeywordExperiment(arg == "on")} tiles");
                break;
            default:
            {
                var cam = Utils.GetMainCamera();
                Lines(args, mgr.DescribeTileAt(cam != null ? cam.transform.position : Vector3.zero), log: true);
                break;
            }
        }
    }

    private static float ParseFloat(string text, float fallback)
    {
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
#endif

    private static void Lines(Terminal.ConsoleEventArgs args, string text, bool log)
    {
        if (log)
        {
            Log.Info(text);
        }
        foreach (var line in text.Split('\n'))
        {
            if (line.Trim().Length > 0)
            {
                args.Context.AddString(line.TrimEnd('\r'));
            }
        }
    }
}
