using System;
using System.Globalization;
using MC.Shared;

namespace MC.Exploration.DeepNorthAwakeningMod;

// Me = console command "deepnorth_stones [count]" (design 2.7, E1). Run on this game: no argument = state (every game
// know the key, the server's rules and the seed; held areas only on the server); a number = set the stone count (no
// message, no invasion), on the server (single player, host) here, on a client sent to the server as a vanilla remote
// command (admin list checked there; the server's answer stay in the server's console and log). Registered while
// Active.
internal static class AdminCommand
{
    internal const string Name = "deepnorth_stones";
    private static Terminal.ConsoleCommand _command;

    internal static void Register()
    {
        _command = new Terminal.ConsoleCommand(Name,
            "[count] - Deep North Awakening: show the broken Malicious Ice count and the stage, or set the count "
            + "(admin; no message, no invasion)",
            new Terminal.ConsoleEvent(Run), isCheat: false, isNetwork: false, onlyServer: false, isSecret: false,
            allowInDevBuild: false, hideBehindDevCommands: false, optionsFetcher: null, alwaysRefreshTabOptions: false,
            remoteCommand: false);
    }

    internal static void Unregister()
    {
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
            var net = ZNet.instance;
            if (net == null || ZoneSystem.instance == null)
            {
                Say(args, "No world loaded.");
                return;
            }
            WorldState.Refresh();
            if (args.Length < 2)
            {
                Say(args, "Deep North: " + WorldState.Describe(ServerRules.Current) + Held() + Here() + ".");
                return;
            }
            if (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
                || count < 0 || count > WorldState.MaxStones)
            {
                Say(args, $"Syntax: {Name} [count], count from 0 to {WorldState.MaxStones}.");
                return;
            }
            if (!net.IsServer())
            {
                // Vanilla remote command: the server check the admin list, then run me there.
                net.RemoteCommand(args.FullLine);
                Say(args, $"Sent to the server (needs admin rights). Run {Name} in a moment to see the new count.");
                return;
            }
            WorldState.WriteStones(count);
            Say(args, "Deep North set: " + WorldState.Describe(ServerRules.Current) + ".");
        }
        catch (Exception e)
        {
            PatchGuard.Report(Name, e);
        }
    }

    private static string Held()
    {
        if (!WorldState.KallDefeated)
        {
            return "";
        }
        if (ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return " (cleared areas are counted on the server)";
        }
        return HeldCells.Scanned
            ? $", {HeldCells.ClearedCount} area(s) cleared, {HeldCells.CountingCount} with Jotun defeated toward their "
              + $"clearing, {HeldCells.EngagedCount} waiting to be defeated"
            : ", areas not counted yet";
    }

    // After Kall: the area the local player stand in (none on a dedicated server console).
    private static string Here()
    {
        var player = Player.m_localPlayer;
        return player != null ? HeldCells.DescribeArea(player.transform.position, ServerRules.Current) : "";
    }

    private static void Say(Terminal.ConsoleEventArgs args, string text)
    {
        Log.Info(text);
        if (args.Context != null)
        {
            args.Context.AddString(text);
        }
    }
}
