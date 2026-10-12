using System;
using System.Collections;
using MC.Shared;
using UnityEngine;

namespace MC.UX.CheatedTagRemoverMod;

// Me = the two console commands (F5 console, or chat as /cheated_clear). Not cheat commands: a cheat command mark the
// character as cheater when it run (Terminal.ConsoleCommand.RunAction), the very thing the player want gone.
//   cheated_scan   count the tags, change nothing
//   cheated_clear  remove them
// Where the work happen depend on this game:
//   hold the world (single player, host, dedicated server)  SweepJob: whole world + own items
//   client, server has me                                   own carried items here (admin or not: only this game can
//                                                           reach them), then ask the server (admin only); server
//                                                           answer later, and tell every player with me to clean
//                                                           its own side
//   client, server has no copy of me (off there, or other   own items + objects loaded around the player, say so
//   version)
// Commands live with the Enabled setting, not with Active: on a server without me the framework call me inactive
// (patches off), and the own-side clean must still work there. Plugin make them in BindConfig.
internal static class ClearCommand
{
    internal const string ClearName = "cheated_clear";
    internal const string ScanName = "cheated_scan";

    private static Terminal.ConsoleCommand _clear;
    private static Terminal.ConsoleCommand _scan;

    internal static void Register()
    {
        if (!Ours(ClearName, _clear))
        {
            _clear = Make(ClearName,
                "- Cheated Tag Remover: remove the cheated tag from the whole world (pieces, creatures, stations, "
                + "stored and dropped items) and from the items you carry (admin on a server)", RunClear);
        }
        if (!Ours(ScanName, _scan))
        {
            _scan = Make(ScanName,
                "- Cheated Tag Remover: count the cheated tags in the world and on the items you carry, without "
                + "changing anything (admin on a server)", RunScan);
        }
    }

    internal static void Unregister()
    {
        Remove(ClearName, _clear);
        Remove(ScanName, _scan);
        _clear = null;
        _scan = null;
    }

    private static Terminal.ConsoleCommand Make(string name, string help, Terminal.ConsoleEvent run)
    {
        return new Terminal.ConsoleCommand(name, help, run, isCheat: false, isNetwork: false,
            onlyServer: false, isSecret: false, allowInDevBuild: false, hideBehindDevCommands: false,
            optionsFetcher: null, alwaysRefreshTabOptions: false, remoteCommand: false);
    }

    private static bool Ours(string name, Terminal.ConsoleCommand command)
    {
        return command != null && Terminal.commands.TryGetValue(name, out var current) && ReferenceEquals(current, command);
    }

    // Only our own entry: another mod may have taken the name after us.
    private static void Remove(string name, Terminal.ConsoleCommand command)
    {
        if (Ours(name, command))
        {
            Terminal.commands.Remove(name);
        }
    }

    private static void RunClear(Terminal.ConsoleEventArgs args) => Run(args, true);

    private static void RunScan(Terminal.ConsoleEventArgs args) => Run(args, false);

    private static void Run(Terminal.ConsoleEventArgs args, bool write)
    {
        try
        {
            Output.SetContext(args != null ? args.Context : null);
            var plugin = Plugin.Instance;
            var net = ZNet.instance;
            var game = Game.instance;
            // Logging out keep the stopped ZNet object for a moment, with empty tables: no world either.
            if (plugin == null || net == null || ZDOMan.instance == null || game == null || net.HaveStopped || game.IsShuttingDown())
            {
                Output.Say("No world loaded.");
                return;
            }
            // Broken or blocked copy do nothing. Server without me (ServerMissing / ServerMismatch) = own side only.
            var state = plugin.State;
            if (state != ModState.Active && state != ModState.ServerMissing && state != ModState.ServerMismatch)
            {
                Output.Say(ModInfo.Name + " is not running. " + plugin.StatusText);
                return;
            }
            if (net.IsServer())
            {
                if (!SweepJob.Start(write, 0L))
                {
                    Output.Say("Still working on the last request. Try again in a moment.");
                }
                return;
            }
            if (plugin.IsActive)
            {
                // Own carried items first: server cannot see them, and a player who is no admin still own them.
                var mine = new SweepCounts();
                LiveSweep.Carried(write, mine);
                if (mine.CarriedItems > 0)
                {
                    Output.Say(write ? Report.Helped(mine) : "On your game: " + Report.List(mine) + ".");
                }
                if (!ServerLink.Ask(write))
                {
                    Output.Say("Not connected to the server yet. Try again in a moment.");
                    return;
                }
                Output.Say(write
                    ? "Asked the server to remove the cheated tags of the whole world (needs admin rights)."
                    : "Asked the server to count the cheated tags of the whole world (needs admin rights).");
                return;
            }
            LocalOnly(plugin, net, write, state == ModState.ServerMismatch);
        }
        catch (Exception e)
        {
            PatchGuard.Report(write ? ClearName : ScanName, e);
            Output.Say(ModInfo.Name + " stopped on an error; see BepInEx/LogOutput.log.");
        }
    }

    // Client alone. Loaded objects only: copies of far places this game still hold can be old, and an old copy that
    // win would roll a chest back. Writes go to the server like any change this game make (no ownership taken).
    // Something written that another player's game own = that game can undo it: me look again before me say "removed".
    private static void LocalOnly(Plugin plugin, ZNet net, bool write, bool mismatch)
    {
        var counts = new SweepCounts();
        LiveSweep.Carried(write, counts);
        if (!write)
        {
            WorldSweep.Run(SweepScope.Loaded, false, counts);
            Output.Say(Report.Local(counts, false, mismatch));
            return;
        }
        var before = new SweepCounts();
        WorldSweep.Run(SweepScope.Loaded, false, before);
        LiveSweep.Loaded();
        WorldSweep.Run(SweepScope.Loaded, true, counts);
        if (counts.WrittenNotOwned == 0)
        {
            Output.Say(Report.Local(counts, true, mismatch));
            return;
        }
        Output.Say("Cleaning the objects loaded around you. Some belong to other players' games: checking again in a few seconds...");
        plugin.StartCoroutine(LocalCheck(net, before, counts.CarriedItems, mismatch));
    }

    private static IEnumerator LocalCheck(ZNet net, SweepCounts before, int carried, bool mismatch)
    {
        yield return new WaitForSecondsRealtime(SweepJob.CheckWait);
        if (net == null || !ReferenceEquals(net, ZNet.instance) || net.HaveStopped || net.IsServer() || ZDOMan.instance == null)
        {
            yield break;
        }
        try
        {
            var after = new SweepCounts();
            WorldSweep.Run(SweepScope.Loaded, false, after);
            Output.Say(Report.LocalChecked(Report.Removed(before, after, carried), after, mismatch));
        }
        catch (Exception e)
        {
            PatchGuard.Report("ClearCommand.LocalCheck", e);
        }
    }
}
