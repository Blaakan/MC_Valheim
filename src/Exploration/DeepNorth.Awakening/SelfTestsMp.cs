#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx.Configuration;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.DeepNorthAwakeningMod;

// Debug build only. Multiplayer self tests (tools/Test-Multiplayer.ps1, scenario "modded": this game is a player on a
// real dedicated server, both with the mod). Client tests:
//   dn.mp.rules    the server's rules reach the player: a changed CoverageStage1 (log line, awake share on the console),
//                  ShowAreas off on the server wins over the player's own setting, on again = purple again
//   dn.mp.stone    a Malicious Ice broken by a player: a game that runs it like the normal game (hand-off: banner and
//                  invasion request from the player) is counted and its request dropped; a game with the rules: the
//                  server counts, one banner, no invasion; areas and storms computed alike on both sides
//   dn.mp.command  deepnorth_stones: state on the own console; set refused without admin rights ("You are not
//                  admin"), applied by the server for an admin
//   dn.mp.kall     after Kall: rules and area lists as a joining player gets them; an area not cleared spawns its Jotun
//                  on arrival with its blizzard; the player's kills count on the server; weakening and "The Jotun
//                  Retreat" reach the player; cleared = no blizzard
//   dn.mp.cleared  after Kall: a cleared area (cleared on the server) gets no Jotun and no blizzard for a player who
//                  asks like at join; the mod turned off and on again there: still no blizzard
// Server halves (run by the server probe on the dedicated server):
//   dn.mp.state    what the server holds: count, Kall, rules, awake areas, invasions, requests waiting, area lists, its
//                  own log line counts; asked cells and storms
//   dn.mp.set      change the server: count, Kall key, settings (its throwaway config), events, admin list, area marks
//   dn.mp.kill     deaths of Jotun at a place, as the server sees them (no creature)
// The server is put back at the end of each test (settings to default, count, Kall key, marks).
internal static partial class SelfTests
{
    private const float MpTravel = 45f;

    // ---------- server halves ----------

    private static ConfigEntryBase Setting(string name)
    {
        switch (name)
        {
            case "AllowPlayersWithoutMod": return Plugin.AllowPlayersWithoutMod;
            case "CoverageStage1": return Plugin.CoverageStage1;
            case "CoverageStage2": return Plugin.CoverageStage2;
            case "CoverageStage3": return Plugin.CoverageStage3;
            case "JotunDensity": return Plugin.JotunDensity;
            case "NatureFightsBack": return Plugin.NatureFightsBack;
            case "NatureBandChance": return Plugin.NatureBandChance;
            case "ClearKillsMin": return Plugin.ClearKillsMin;
            case "ClearKillsMax": return Plugin.ClearKillsMax;
            case "StormShare": return Plugin.StormShare;
            case "Meteors": return Plugin.Meteors;
            case "ShowAreas": return Plugin.MapAreas;
            case "InvasionsAtThirdStone": return Plugin.InvasionsAtThirdStone;
            default: return null;
        }
    }

    private static readonly string[] SettingNames =
    {
        "AllowPlayersWithoutMod", "CoverageStage1", "CoverageStage2", "CoverageStage3", "JotunDensity", "NatureFightsBack",
        "NatureBandChance", "ClearKillsMin", "ClearKillsMax", "StormShare", "Meteors", "ShowAreas", "InvasionsAtThirdStone",
    };

    // Awake areas of the stage in force as one short text (count and a running hash of their ids): two games that agree
    // on seed, count and rules give the same text.
    private static string AwakeDigest(AwakeningRules rules)
    {
        var seed = WorldState.Seed;
        var coverage = rules != null && !rules.IsPending ? rules.Coverage(WorldState.Stage) : 0f;
        var n = 0;
        long sum = 17;
        for (var i = -Cells.WorldSquares; i <= Cells.WorldSquares; i++)
        {
            for (var j = -Cells.WorldSquares; j <= Cells.WorldSquares; j++)
            {
                if (Cells.IsAwake(seed, i, j, coverage))
                {
                    n++;
                    sum = unchecked(sum * 31 + Cells.Id(i, j));
                }
            }
        }
        return n + ":" + sum.ToString("X", CultureInfo.InvariantCulture);
    }

    private static string StormBits(AwakeningRules rules, double time, IEnumerable<int> cells)
    {
        rules.StormSeconds(out var min, out var max);
        var sb = new StringBuilder();
        foreach (var cell in cells)
        {
            sb.Append(Cells.IsStorming(WorldState.Seed, cell, time, rules.StormShare / 100f, min, max) ? '1' : '0');
        }
        return sb.ToString();
    }

    private static List<int> ParseCells(string text)
    {
        var list = new List<int>();
        foreach (var part in (text ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                list.Add(id);
            }
        }
        return list;
    }

    // "key=value;" text of what this game (the server) holds. query: "cells=<id>,<id>" and "storm=<time>:<id>,<id>".
    private static string DescribeServer(string query)
    {
        var sb = new StringBuilder();
        void Put(string key, object value) =>
            sb.Append(key).Append('=').Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append(';');
        WorldState.Refresh();
        var rules = ServerRules.Current;
        var pes = PersistentEventSystem.instance;
        var index = Stones.InvasionIndex(pes);
        var tap = EnsureTap();
        var net = ZNet.instance;
        Put("stones", WorldState.Stones);
        Put("haskey", WorldState.HasStonesKey ? 1 : 0);
        Put("kall", WorldState.KallDefeated ? 1 : 0);
        Put("seed", WorldState.Seed);
        Put("time", net != null ? net.GetTimeSeconds().ToString("R", CultureInfo.InvariantCulture) : "0");
        WorldState.CountAwake(rules, out var awake, out var total);
        Put("awake", awake);
        Put("total", total);
        Put("awakehash", AwakeDigest(rules));
        Put("invasions", index >= 0 ? Stones.ActiveInvasions(pes, index) : -1);
        Put("held", Stones.HeldCount);
        Put("scanned", HeldCells.Scanned ? 1 : 0);
        Put("cleared", HeldCells.ClearedCount);
        Put("engaged", HeldCells.EngagedCount);
        Put("counting", HeldCells.CountingCount);
        Put("peers", net != null ? net.GetPeerConnections() : 0);
        Put("stonezdos", Stones.AllZdos(Stones.StonePrefab).Count);
        Put("allow", Plugin.AllowPlayersWithoutMod != null && Plugin.AllowPlayersWithoutMod.Value ? 1 : 0);
        Put("console", global::Console.instance != null ? 1 : 0);
        Put("log.broken", tap.Count(0, "A Malicious Ice was broken at"));
        Put("log.set", tap.Count(0, "Deep North set:"));
        Put("log.weak", tap.Count(0, "is weakening"));
        Put("log.cleared", tap.Count(0, "the area is cleared for good"));
        Put("log.allowed", tap.Count(0, "AllowPlayersWithoutMod is on"));
        Put("rules", rules.Describe());
        foreach (var part in (query ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }
            var key = part.Substring(0, eq);
            var value = part.Substring(eq + 1);
            if (key == "cells")
            {
                foreach (var cell in ParseCells(value))
                {
                    Put("cell." + cell.ToString(CultureInfo.InvariantCulture),
                        $"{HeldCells.TestKills(cell)}/{(HeldCells.TestServerEngaged(cell) ? 1 : 0)}/{(HeldCells.TestServerCleared(cell) ? 1 : 0)}/"
                        + $"{(HeldCells.TestMarkedAnywhere(cell) ? 1 : 0)}");
                }
            }
            else if (key == "storm")
            {
                var colon = value.IndexOf(':');
                if (colon > 0 && double.TryParse(value.Substring(0, colon), NumberStyles.Float, CultureInfo.InvariantCulture, out var time))
                {
                    Put("storms", StormBits(rules, time, ParseCells(value.Substring(colon + 1))));
                }
            }
        }
        return sb.ToString();
    }

    private static IEnumerator ServerState(string arg, object[] reply)
    {
        yield return null;
        SelfTest.Answer(reply, true, DescribeServer(arg));
    }

    // One change on the server. Null = done, else what went wrong.
    private static string ApplyServer(string key, string value)
    {
        var zs = ZoneSystem.instance;
        var net = ZNet.instance;
        switch (key)
        {
            case "stones":
                WorldState.WriteStones(int.Parse(value, CultureInfo.InvariantCulture));
                return null;
            case "kall":
                if (value == "1")
                {
                    zs.SetGlobalKey(WorldState.KallKey);
                }
                else
                {
                    zs.RemoveGlobalKey(WorldState.KallKey);
                }
                WorldState.Refresh();
                return null;
            case "events":
                var pes = PersistentEventSystem.instance;
                pes.m_activePersistentEvents.list.Clear();
                pes.UpdateClientEventsList(0L);
                return null;
            case "defaults":
                // The server's own test config (thrown away after the run).
                foreach (var name in SettingNames)
                {
                    var entry = Setting(name);
                    if (entry != null)
                    {
                        entry.BoxedValue = entry.DefaultValue;
                    }
                }
                return null;
            case "unmark":
                HeldCells.TestUnmarkEverywhere(int.Parse(value, CultureInfo.InvariantCulture));
                return null;
            case "rescan":
                HeldCells.TestRescan();
                HeldCells.TestClearEngaged();
                return null;
            case "admin":
                var peers = net.GetPeers();
                if (peers.Count == 0 || peers[0].m_socket == null || net.m_adminList == null)
                {
                    return "no player connected or no admin list";
                }
                var host = peers[0].m_socket.GetHostName();
                if (value == "add")
                {
                    net.m_adminList.Add(host);
                }
                else
                {
                    net.m_adminList.Remove(host);
                }
                return null;
            default:
                var setting = Setting(key);
                if (setting == null)
                {
                    return "unknown key '" + key + "'";
                }
                setting.SetSerializedValue(value);
                return null;
        }
    }

    private static IEnumerator ServerSet(string arg, object[] reply)
    {
        var problems = new List<string>();
        foreach (var part in (arg ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }
            try
            {
                var problem = ApplyServer(part.Substring(0, eq).Trim(), part.Substring(eq + 1).Trim());
                if (problem != null)
                {
                    problems.Add(problem);
                }
            }
            catch (Exception e)
            {
                problems.Add(part + ": " + e.Message);
            }
        }
        // The server's next ticks take the change in (key read, area scan).
        yield return null;
        yield return null;
        yield return null;
        SelfTest.Answer(reply, problems.Count == 0, problems.Count == 0 ? DescribeServer("") : string.Join(", ", problems.ToArray()));
    }

    // "<prefab>|<x>|<z>|<count>": that many deaths of that creature at that place, as the server sees them.
    private static IEnumerator ServerKill(string arg, object[] reply)
    {
        var parts = (arg ?? "").Split('|');
        if (parts.Length < 4 || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                             || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)
                             || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
                             || ZDOMan.instance == null)
        {
            SelfTest.Answer(reply, false, "bad argument '" + arg + "'");
            yield break;
        }
        var at = new Vector3(x, 0f, z);
        var cell = WorldState.CellAt(at);
        for (var k = 0; k < count; k++)
        {
            FakeKill(parts[0], at);
        }
        for (var k = 0; k < 6; k++)
        {
            yield return null;
        }
        SelfTest.Answer(reply, true, DescribeServer("cells=" + cell.ToString(CultureInfo.InvariantCulture)));
    }

    // ---------- client tools ----------

    private sealed class Server
    {
        internal readonly SelfTest.ServerReply Reply = new SelfTest.ServerReply();
        internal Dictionary<string, string> Fields = new Dictionary<string, string>(StringComparer.Ordinal);

        internal bool Ok => Reply.Answered && Reply.Ok;

        internal int Num(string key) =>
            Fields.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : int.MinValue;

        internal string Text(string key) => Fields.TryGetValue(key, out var v) ? v : "";

        // "kills/engaged/cleared/marked" of a cell asked with cells=.
        internal bool Cell(int cell, out int kills, out bool engaged, out bool cleared, out bool marked)
        {
            kills = 0;
            engaged = false;
            cleared = false;
            marked = false;
            var parts = Text("cell." + cell.ToString(CultureInfo.InvariantCulture)).Split('/');
            if (parts.Length < 4 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out kills))
            {
                return false;
            }
            engaged = parts[1] == "1";
            cleared = parts[2] == "1";
            marked = parts[3] == "1";
            return true;
        }
    }

    private static IEnumerator Ask(Server s, string step, string arg)
    {
        yield return SelfTest.CallServer(step, arg, s.Reply);
        s.Fields.Clear();
        foreach (var part in (s.Reply.Detail ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq > 0)
            {
                s.Fields[part.Substring(0, eq)] = part.Substring(eq + 1);
            }
        }
    }

    // Ask the server's state until a number in it is at least this (or time is up).
    private static IEnumerator WaitServerNumber(Server s, string field, int atLeast, float seconds)
    {
        var end = Time.realtimeSinceStartup + seconds;
        while (true)
        {
            yield return Ask(s, StepState, "");
            if ((s.Ok && s.Num(field) >= atLeast) || Time.realtimeSinceStartup >= end)
            {
                yield break;
            }
            yield return new WaitForSeconds(0.3f);
        }
    }

    // Ask the server about a cell until it has that many kills, or is cleared (or time is up).
    private static IEnumerator WaitServerCell(Server s, int cell, int kills, bool cleared, float seconds)
    {
        var end = Time.realtimeSinceStartup + seconds;
        var query = "cells=" + cell.ToString(CultureInfo.InvariantCulture);
        while (true)
        {
            yield return Ask(s, StepState, query);
            if (s.Ok && s.Cell(cell, out var k, out _, out var c, out _) && (cleared ? c : k >= kills))
            {
                yield break;
            }
            if (Time.realtimeSinceStartup >= end)
            {
                yield break;
            }
            yield return new WaitForSeconds(0.3f);
        }
    }

    private static bool AsPlayer(Checks c)
    {
        var net = ZNet.instance;
        return c.Check(net != null && !net.IsServer() && SelfTest.IsMultiplayerRun && ModActive() && Player.m_localPlayer != null,
            "this game is a player on a dedicated server, with the mod on");
    }

    private static bool StonesAre(int count)
    {
        WorldState.Refresh();
        return WorldState.Stones == count;
    }

    private static IEnumerator GoHomeMp(Checks c, Player player, Vector3 home)
    {
        var back = new Box();
        yield return TeleportAndWait(player, home, MpTravel, back);
        c.Check(back.Ok, "travelled back to the start");
    }

    // The server as the test found it: settings to default, Kall key off, marks of the test areas gone, count back.
    private static IEnumerator PutServerBack(Checks c, Server s, int stones, params int[] cells)
    {
        var sb = new StringBuilder("defaults=1;kall=0;");
        foreach (var cell in cells)
        {
            if (cell != HeldCells.NoTag)
            {
                sb.Append("unmark=").Append(cell.ToString(CultureInfo.InvariantCulture)).Append(';');
            }
        }
        sb.Append("rescan=1;stones=").Append(Mathf.Max(0, stones).ToString(CultureInfo.InvariantCulture));
        yield return Ask(s, StepSet, sb.ToString());
        c.Check(s.Ok, "the server is put back as before (" + (s.Ok ? "done" : s.Reply.ToString()) + ")");
    }

    // ---------- dn.mp.rules ----------

    private static IEnumerator RunMpRules()
    {
        var c = new Checks(MpRulesName);
        var s = new Server();
        var tap = EnsureTap();
        var stones0 = 0;
        var changed = false;
        try
        {
            if (!AsPlayer(c))
            {
                c.Report();
                yield break;
            }
            yield return Ask(s, StepState, "");
            if (!c.Check(s.Ok, "the server answers (" + s.Reply + ")"))
            {
                c.Report();
                yield break;
            }
            stones0 = s.Num("stones");
            changed = true;
            yield return Ask(s, StepSet, "defaults=1;kall=0;rescan=1;stones=1");
            yield return Until(() => StonesAre(1) && ServerRules.UsingServer && ServerRules.Current.Describe() == s.Text("rules"), 6f);
            c.Check(s.Ok && StonesAre(1) && ServerRules.UsingServer && ServerRules.Current.Describe() == s.Text("rules"),
                $"this player uses the rules the server sent, and sees its count (1): {ServerRules.Current.Describe()}");

            // M04: CoverageStage1 changed on the server.
            var mark = tap.Mark();
            yield return Ask(s, StepSet, "CoverageStage1=35");
            yield return Until(() => ServerRules.Current.CoverageStage1 == 35, 6f);
            c.Check(s.Ok && ServerRules.UsingServer && ServerRules.Current.CoverageStage1 == 35,
                $"CoverageStage1 set to 35 on the server reaches the player ({ServerRules.Current.CoverageStage1})");
            var used = tap.Last(mark, "Using the server's rules:");
            c.Check(tap.Count(mark, "Using the server's rules:") == 1 && used.Contains("areas 35/40/70 %"),
                $"the player's log says so once: '{used}'");
            WorldState.Refresh();
            WorldState.CountAwake(ServerRules.Current, out var awake, out var total);
            var line = First(RunConsole(AdminCommand.Name));
            yield return Ask(s, StepState, "");
            c.Check(ReadOf(line, AreasAwake, out var a, out var t) && a == awake && t == total && a == s.Num("awake") && t == s.Num("total")
                    && t > 0 && Mathf.Abs((float)a / t - 0.35f) <= 0.1f,
                $"deepnorth_stones on the player shows the new share: {a} of {t} awake (server {s.Num("awake")} of {s.Num("total")})");
            var atDefault = 0;
            for (var i = -Cells.WorldSquares; i <= Cells.WorldSquares; i++)
            {
                for (var j = -Cells.WorldSquares; j <= Cells.WorldSquares; j++)
                {
                    Cells.SeedPoint(WorldState.Seed, i, j, out var x, out var z);
                    if (x * x + z * z <= 10500f * 10500f && Cells.IsAwake(WorldState.Seed, i, j, AwakeningRules.Default.Coverage(1)))
                    {
                        atDefault++;
                    }
                }
            }
            c.Check(a > atDefault, $"more areas than with the default 20 % ({a} against {atDefault})");

            // M09: the map setting of the server wins over the player's own.
            yield return Ask(s, StepSet, "stones=3");
            yield return Until(() => StonesAre(3), 6f);
            yield return Until(() => MapOverlay.Ready, 30f);
            yield return Frames(4);
            var found = MapOverlay.TestFind(ServerRules.Current.Coverage(3), true, out var spot);
            c.Check(found && MapOverlay.Tinted && MapOverlay.TestPixel(spot) == 2,
                $"server ShowAreas on: an invaded area is purple on the player's map (pixel state {MapOverlay.TestPixel(spot)})");
            yield return Ask(s, StepSet, "ShowAreas=false");
            yield return Until(() => !ServerRules.Current.MapAreas, 6f);
            yield return Frames(4);
            c.Check(s.Ok && !ServerRules.Current.MapAreas && Plugin.MapAreas.Value && !MapOverlay.Tinted && MapOverlay.TestPixel(spot) == 1,
                $"server ShowAreas off, the player's own setting on: no purple on the player's map (pixel state {MapOverlay.TestPixel(spot)})");
            yield return Ask(s, StepSet, "ShowAreas=true");
            yield return Until(() => ServerRules.Current.MapAreas, 6f);
            yield return Frames(4);
            c.Check(s.Ok && MapOverlay.Tinted && MapOverlay.TestPixel(spot) == 2, "server ShowAreas on again: the purple is back");
            c.Check(AwakeDigest(ServerRules.Current) == s.Text("awakehash"),
                $"the player and the server work out the same invaded areas ({AwakeDigest(ServerRules.Current)} / {s.Text("awakehash")})");
        }
        finally
        {
            ClearOverrides();
        }
        if (changed)
        {
            yield return PutServerBack(c, s, stones0);
        }
        c.Report();
    }

    // ---------- dn.mp.stone ----------

    private static IEnumerator RunMpStone()
    {
        var c = new Checks(MpStoneName);
        var s = new Server();
        var player = Player.m_localPlayer;
        var stones = new List<GameObject>();
        var hud = new HudLog();
        var stones0 = 0;
        var changed = false;
        try
        {
            if (!AsPlayer(c))
            {
                c.Report();
                yield break;
            }
            yield return Ask(s, StepState, "");
            if (!c.Check(s.Ok, "the server answers (" + s.Reply + ")"))
            {
                c.Report();
                yield break;
            }
            stones0 = s.Num("stones");
            changed = true;
            yield return Ask(s, StepSet, "defaults=1;kall=0;rescan=1;stones=0");
            yield return Until(() => StonesAre(0) && ServerRules.UsingServer, 6f);
            c.Check(s.Ok && StonesAre(0) && ServerRules.UsingServer, "stage 0 on the server and on the player");
            var broken0 = s.Num("log.broken");
            var zdos0 = s.Num("stonezdos");
            var invasions0 = s.Num("invasions");
            hud.Begin();
            var advance = Localize(Stones.StoneText);
            var fwd = Flat(player.transform.forward).normalized;
            if (fwd == Vector3.zero)
            {
                fwd = Vector3.forward;
            }
            var side = Vector3.Cross(Vector3.up, fwd);
            var box = new Box();

            // Hand-off (M03): this game runs the stone like the normal game (as one that has no rules from the server):
            // its own banner and its own invasion request go out, then the stone goes.
            var first = Spawn(Stones.StonePrefab, player.transform.position + fwd * 9f - side * 4f);
            stones.Add(first);
            yield return WaitServerNumber(s, "stonezdos", zdos0 + 1, 8f);
            c.Check(s.Ok && s.Num("stonezdos") >= zdos0 + 1, "the server knows the stone the player placed");
            ServerRules.TestPending = true;
            yield return HitStone(first, box);
            ServerRules.TestPending = false;
            yield return Ask(s, StepState, "");
            var heldAtOnce = s.Num("held");
            yield return Until(() => StonesAre(1), 6f);
            yield return new WaitForSeconds(1.5f);
            var shown = hud.Count(advance);
            c.Check(box.Ok && heldAtOnce == 1, $"hand-off: the player's invasion request waits on the server ({heldAtOnce} held)");
            c.Check(StonesAre(1), $"hand-off: the server counted the stone, stage 1 for everyone ({WorldState.Stones})");
            c.Check(shown >= 1 && shown <= 2, $"hand-off: the player sees \"{advance}\" (once or twice: {shown})");
            yield return new WaitForSecondsRealtime(Stones.HoldSeconds);
            yield return Ask(s, StepState, "");
            c.Check(s.Ok && s.Num("held") == 0 && s.Num("invasions") == invasions0 && s.Num("stones") == 1 && s.Num("log.broken") == broken0 + 1,
                $"hand-off: about 5 s later the request is dropped, no new invasion (held {s.Num("held")}, invasions {s.Num("invasions")}, "
                + $"was {invasions0}); the server log counted it once");

            // A player with the rules (M01): the server counts, one banner, nothing asked of the normal game.
            var second = Spawn(Stones.StonePrefab, player.transform.position + fwd * 9f + side * 4f);
            stones.Add(second);
            yield return WaitServerNumber(s, "stonezdos", zdos0 + 1, 8f);
            var before = hud.Count(advance);
            yield return HitStone(second, box);
            yield return Until(() => StonesAre(2), 6f);
            yield return new WaitForSeconds(2f);
            c.Check(box.Ok && StonesAre(2) && hud.Count(advance) == before + 1,
                $"a Malicious Ice broken by a player with the mod: stage 2 on the player, \"{advance}\" once ({hud.Count(advance) - before})");
            // Same areas and same storms as the server works out, for the same moment.
            var rules = ServerRules.Current;
            var cells = new List<int>();
            for (var i = -Cells.WorldSquares; i <= Cells.WorldSquares && cells.Count < 24; i++)
            {
                for (var j = -Cells.WorldSquares; j <= Cells.WorldSquares && cells.Count < 24; j++)
                {
                    if (Cells.IsAwake(WorldState.Seed, i, j, rules.Coverage(WorldState.Stage)))
                    {
                        cells.Add(Cells.Id(i, j));
                    }
                }
            }
            var now = ZNet.instance.GetTimeSeconds();
            var ids = string.Join(",", cells.ConvertAll(id => id.ToString(CultureInfo.InvariantCulture)).ToArray());
            yield return Ask(s, StepState, "storm=" + now.ToString("R", CultureInfo.InvariantCulture) + ":" + ids);
            c.Check(s.Ok && s.Num("stones") == 2 && s.Num("log.broken") == broken0 + 2 && s.Num("held") == 0 && s.Num("invasions") == invasions0,
                $"the server counted it (log line), no request, no invasion at stage 2 (held {s.Num("held")}, invasions {s.Num("invasions")})");
            WorldState.CountAwake(rules, out var awake, out var total);
            c.Check(AwakeDigest(rules) == s.Text("awakehash") && awake == s.Num("awake") && total == s.Num("total") && awake > 0,
                $"player and server work out the same invaded areas ({awake} of {total}; {AwakeDigest(rules)} / {s.Text("awakehash")})");
            var mine = StormBits(rules, now, cells);
            double.TryParse(s.Text("time"), NumberStyles.Float, CultureInfo.InvariantCulture, out var serverTime);
            c.Check(cells.Count > 0 && mine == s.Text("storms") && Math.Abs(serverTime - now) < 5.0,
                $"and the same storms for the same moment in {cells.Count} areas ({mine} / {s.Text("storms")}; clocks {F(serverTime - now)} s apart)");
        }
        finally
        {
            ServerRules.TestPending = false;
            foreach (var go in stones)
            {
                Kill(go);
            }
            hud.End();
            ClearOverrides();
        }
        if (changed)
        {
            // A stone left standing was just removed: its destroy reaches the server before this call.
            yield return PutServerBack(c, s, stones0);
        }
        c.Report();
    }

    // ---------- dn.mp.command ----------

    private static IEnumerator RunMpCommand()
    {
        var c = new Checks(MpCommandName);
        var s = new Server();
        var hud = new HudLog();
        var stones0 = 0;
        var changed = false;
        var admin = false;
        try
        {
            if (!AsPlayer(c) || !c.Check(global::Console.instance != null, "the console exists"))
            {
                c.Report();
                yield break;
            }
            yield return Ask(s, StepState, "");
            if (!c.Check(s.Ok, "the server answers (" + s.Reply + ")"))
            {
                c.Report();
                yield break;
            }
            stones0 = s.Num("stones");
            changed = true;
            c.Note($"the dedicated server has a console object: {s.Num("console") == 1}");
            yield return Ask(s, StepSet, "defaults=1;kall=0;rescan=1;stones=0;admin=remove");
            yield return Until(() => StonesAre(0), 6f);
            var set0 = s.Num("log.set");
            hud.Begin();
            var advance = Localize(Stones.StoneText);

            // State: on the player's own console.
            var lines = RunConsole(AdminCommand.Name);
            c.Check(lines.Count == 1 && First(lines).StartsWith("Deep North: 0 Malicious Ice broken, stage 0", StringComparison.Ordinal),
                $"deepnorth_stones shows the state on the player's own console: '{First(lines)}'");

            // Not an admin: sent, refused by the server, nothing changes.
            var mark = MarkConsole();
            lines = RunConsole(AdminCommand.Name + " 2");
            c.Check(AnyStarts(lines, "Sent to the server (needs admin rights)"), $"deepnorth_stones 2 without admin rights: '{First(lines)}'");
            yield return Until(() => AnyHolds(ConsoleSince(mark), "You are not admin"), 5f);
            c.Check(AnyHolds(ConsoleSince(mark), "You are not admin"), "the server answers \"You are not admin\" on the player's console");
            yield return new WaitForSeconds(1f);
            yield return Ask(s, StepState, "");
            c.Check(s.Ok && s.Num("stones") == 0 && StonesAre(0) && s.Num("log.set") == set0,
                $"nothing changed (server count {s.Num("stones")})");

            // Admin: sent, applied by the server.
            yield return Ask(s, StepSet, "admin=add");
            admin = s.Ok;
            c.Check(s.Ok, "the player is on the server's admin list now (" + (s.Ok ? "done" : s.Reply.ToString()) + ")");
            mark = MarkConsole();
            lines = RunConsole(AdminCommand.Name + " 1");
            c.Check(AnyStarts(lines, "Sent to the server (needs admin rights)"), $"deepnorth_stones 1 as admin: '{First(lines)}'");
            yield return Until(() => StonesAre(1), 6f);
            yield return Ask(s, StepState, "");
            c.Check(s.Ok && s.Num("stones") == 1 && s.Num("log.set") == set0 + 1 && StonesAre(1),
                $"the server applied it: count {s.Num("stones")}, its log says \"Deep North set: ...\" once more");
            c.Check(!AnyHolds(ConsoleSince(mark), "You are not admin"), "no refusal this time");
            lines = RunConsole(AdminCommand.Name);
            c.Check(First(lines).StartsWith("Deep North: 1 Malicious Ice broken, stage 1", StringComparison.Ordinal),
                $"deepnorth_stones on the player shows 1: '{First(lines)}'");
            yield return new WaitForSeconds(1.2f);
            c.Check(hud.Count(advance) == 0 && s.Num("invasions") >= 0, $"setting the count shows no \"{advance}\" ({hud.Count(advance)})");
        }
        finally
        {
            hud.End();
            ClearOverrides();
        }
        if (admin)
        {
            yield return Ask(s, StepSet, "admin=remove");
            c.Check(s.Ok, "the player is off the admin list again");
        }
        if (changed)
        {
            yield return PutServerBack(c, s, stones0);
        }
        c.Report();
    }

    // ---------- dn.mp.kall ----------

    // Rules and area lists asked again like a game that just connected: all it knew forgotten first.
    private static IEnumerator AskLikeAtJoin(Checks c)
    {
        var rpc = ZNet.instance.GetServerRPC();
        HeldCells.Clear();
        ServerRules.RegisterClient(rpc, forget: true);
        var blank = ServerRules.Current.IsPending && !HeldCells.TestKnown;
        ServerRules.Request(rpc);
        yield return Until(() => ServerRules.UsingServer && HeldCells.TestKnown, 6f);
        c.Check(blank && ServerRules.UsingServer && HeldCells.TestKnown,
            "a game that asks like at join (nothing known before) gets the server's rules and its area lists");
    }

    private static IEnumerator RunMpKall()
    {
        var c = new Checks(MpKallName);
        var s = new Server();
        var player = Player.m_localPlayer;
        var clock = new Clock();
        var hud = new HudLog();
        var spawned = new List<GameObject>();
        var stones0 = 0;
        var changed = false;
        var travelled = false;
        var cell = HeldCells.NoTag;
        var home = Vector3.zero;
        try
        {
            if (!AsPlayer(c))
            {
                c.Report();
                yield break;
            }
            _home ??= player.transform.position;
            home = _home.Value;
            yield return Ask(s, StepState, "");
            if (!c.Check(s.Ok, "the server answers (" + s.Reply + ")"))
            {
                c.Report();
                yield break;
            }
            stones0 = s.Num("stones");
            changed = true;
            // Stage 3, storms always, 4 kills clear an area, no bands.
            yield return Ask(s, StepSet, "defaults=1;kall=0;rescan=1;stones=3;StormShare=100;NatureBandChance=0;ClearKillsMin=4;ClearKillsMax=4");
            yield return Until(() => StonesAre(3) && ServerRules.UsingServer && ServerRules.Current.StormShare == 100
                                     && ServerRules.Current.ClearKillsMin == 4 && ServerRules.Current.ClearKillsMax == 4, 8f);
            c.Check(s.Ok && StonesAre(3) && ServerRules.Current.StormShare == 100 && ServerRules.Current.ClearKillsMax == 4,
                "the server's stage 3 and its rules (storms always, 4 kills) are on the player");
            yield return Ask(s, StepSet, "kall=1");
            yield return Until(() => WorldState.KallDefeated && HeldCells.TestKnown, 8f);
            c.Check(s.Ok && WorldState.KallDefeated && HeldCells.TestKnown, "Kall defeated on the server: the player knows, the area lists came");
            yield return AskLikeAtJoin(c);
            if (!c.Check(LandSlot(SlotStorm, out var point, out cell), "a dry invaded Deep North area exists"))
            {
                c.Report();
                yield break;
            }
            hud.Begin();
            var box = new Box();
            travelled = true;
            yield return TeleportAndWait(player, point, MpTravel, box);
            if (c.Check(box.Ok, $"travelled to the invaded area {cell} at {F(point)}"))
            {
                var pos = player.transform.position;
                var blizzard = Storms.Env();
                var weak = HeldCells.WeakeningText;
                var retreat = Localize(HeldCells.ClearedText);
                yield return Until(() => TaggedOf(cell).Count > 0, clock.Cap(30f));
                yield return WaitTagged(pos, 400f, 1, clock.Cap(4f));
                c.Check(TaggedOf(cell).Count > 0 && OneSet(TaggedOf(cell)),
                    $"an area that is not cleared spawns its Jotun when the player arrives, one set ({Kinds(TaggedOf(cell))})");
                yield return Until(() => Storms.Override() == blizzard, 3f);
                c.Check(blizzard != null && Storms.Override() == blizzard, "and its blizzard blows (StormShare 100 on the server)");
                yield return Ask(s, StepState, "cells=" + cell.ToString(CultureInfo.InvariantCulture));
                c.Check(s.Ok && s.Cell(cell, out var kills, out var engaged, out var cleared, out _) && engaged && !cleared && kills == 0,
                    "the server holds the area as waiting to be defeated");
                var line = First(RunConsole(AdminCommand.Name));
                c.Check(ReadOf(line, JotunDefeated, out var k0, out var n0) && k0 == 0 && n0 == 4, $"deepnorth_stones on the player: '{line}'");

                // The player's kills count on the server; the news come back.
                var need = 5 - ArmyNear(pos, 90f, cell).Count;
                if (need > 0)
                {
                    SpawnRing(Hostility.Krigen, pos, need, 16f, spawned);
                    // The server must know them before they die.
                    yield return new WaitForSeconds(2f);
                }
                HeldCells.TestResetNews();
                var slain = SlayArmy(pos, cell, 2, null);
                yield return WaitServerCell(s, cell, 2, false, clock.Cap(10f));
                yield return Until(() => HeldCells.KillsIn(cell) == 2, 4f);
                HeldCells.TestLastNews(out var kind, out var newsCell, out var shown);
                s.Cell(cell, out kills, out engaged, out cleared, out _);
                c.Check(slain == 2 && kills == 2 && HeldCells.KillsIn(cell) == 2,
                    $"2 Jotun killed by the player: the server counts 2, the player's count follows ({kills} / {HeldCells.KillsIn(cell)})");
                c.Check(kind == HeldCells.NewsWeakening && newsCell == cell && shown && hud.Count(weak) == 1,
                    $"half way: \"{weak}\" reaches the player in the area, once ({hud.Count(weak)})");
                HeldCells.TestResetNews();
                slain = SlayArmy(pos, cell, 2, null);
                yield return WaitServerCell(s, cell, 0, true, clock.Cap(10f));
                yield return Until(() => HeldCells.IsCleared(cell), 4f);
                HeldCells.TestLastNews(out kind, out newsCell, out shown);
                s.Cell(cell, out kills, out engaged, out cleared, out var marked);
                c.Check(slain == 2 && cleared && HeldCells.IsCleared(cell),
                    $"4 kills: the server cleared the area (kept in the world: {marked}), the player knows");
                c.Check(kind == HeldCells.NewsCleared && newsCell == cell && shown && hud.Count(retreat) == 1,
                    $"\"{retreat}\" reaches the player in the area, once ({hud.Count(retreat)})");
                yield return Until(() => Storms.Override() == null, 2f);
                c.Check(Storms.Override() == null, "the blizzard stops in the cleared area");
            }
        }
        finally
        {
            hud.End();
            ClearOverrides();
            KillAll(spawned);
            SweepArea(player);
        }
        if (travelled)
        {
            yield return GoHomeMp(c, player, home);
        }
        if (changed)
        {
            yield return PutServerBack(c, s, stones0, cell);
        }
        c.Report();
    }

    // ---------- dn.mp.cleared ----------

    private const string MpBlocker = "Inactive: turned off for a moment by the dn.mp.cleared self test.";

    private static IEnumerator RunMpCleared()
    {
        var c = new Checks(MpClearedName);
        var s = new Server();
        var player = Player.m_localPlayer;
        var clock = new Clock();
        var stones0 = 0;
        var changed = false;
        var travelled = false;
        var cell = HeldCells.NoTag;
        var otherCell = HeldCells.NoTag;
        var home = Vector3.zero;
        try
        {
            if (!AsPlayer(c))
            {
                c.Report();
                yield break;
            }
            _home ??= player.transform.position;
            home = _home.Value;
            yield return Ask(s, StepState, "");
            if (!c.Check(s.Ok, "the server answers (" + s.Reply + ")"))
            {
                c.Report();
                yield break;
            }
            stones0 = s.Num("stones");
            changed = true;
            // Stage 3, storms always, one kill clears an area, players with the mod off may stay (for the toggle).
            yield return Ask(s, StepSet,
                "defaults=1;kall=0;rescan=1;stones=3;StormShare=100;NatureBandChance=0;ClearKillsMin=1;ClearKillsMax=1;AllowPlayersWithoutMod=true");
            var allowed = s.Ok && s.Num("allow") == 1;
            yield return Until(() => StonesAre(3) && ServerRules.UsingServer && ServerRules.Current.StormShare == 100
                                     && ServerRules.Current.ClearKillsMax == 1, 8f);
            c.Check(allowed && StonesAre(3) && ServerRules.Current.StormShare == 100 && ServerRules.Current.ClearKillsMax == 1,
                "the server's stage 3 and its rules (storms always, 1 kill) are on the player; it lets players with the mod off stay");
            if (!c.Check(LandSlot(SlotBands, out var point, out cell) && LandSlot(SlotKallFight, out _, out _), "two dry invaded Deep North areas exist"))
            {
                c.Report();
                yield break;
            }
            LandSlot(SlotKallFight, out var otherPoint, out otherCell);
            yield return Ask(s, StepSet, "kall=1");
            yield return Until(() => WorldState.KallDefeated && HeldCells.TestKnown, 8f);
            // One area cleared on the server (a Jotun death there, as the server sees it).
            yield return Ask(s, StepKill, string.Format(CultureInfo.InvariantCulture, "{0}|{1:R}|{2:R}|1", Hostility.Krigen, point.x, point.z));
            c.Check(s.Ok && s.Cell(cell, out _, out _, out var serverCleared, out _) && serverCleared, "Kall defeated and one area cleared on the server");
            yield return Until(() => HeldCells.IsCleared(cell), 5f);
            c.Check(HeldCells.IsCleared(cell), "the player hears of the cleared area");
            yield return AskLikeAtJoin(c);
            c.Check(HeldCells.IsCleared(cell) && !HeldCells.IsCleared(otherCell) && HeldCells.MayBurst(otherCell),
                "after asking like at join: the cleared area is known as cleared, another one still spawns when entered");
            var box = new Box();
            travelled = true;
            yield return TeleportAndWait(player, point, MpTravel, box);
            if (c.Check(box.Ok, $"travelled to the cleared area {cell} at {F(point)}"))
            {
                var pos = player.transform.position;
                var blizzard = Storms.Env();
                var known = new HashSet<Character>(Character.GetAllCharacters());
                var quiet = new Quiet();
                yield return WatchQuiet(player, clock.Cap(10f), known, quiet);
                c.Check(quiet.NewTagged == 0 && !quiet.Storm && !quiet.Meteor,
                    $"in the cleared area: no Jotun appear, no blizzard, no meteor ({quiet.NewTagged} Jotun, blizzard {quiet.Storm}; StormShare 100)");
                c.Check(WorldState.IsStormingCell(cell, ServerRules.Current) && Storms.StormAt(otherPoint, ServerRules.Current)
                        && !Storms.StormAt(pos, ServerRules.Current),
                    "an area that is not cleared storms at this very moment; the cleared one does not");
                if (MapOverlay.Ready)
                {
                    c.Check(MapOverlay.TestPixel(pos) == 1, $"the cleared area is not purple on the player's map (pixel state {MapOverlay.TestPixel(pos)})");
                }

                // M07: the mod turned off and on again while standing in the cleared area.
                if (c.Check(allowed && clock.Room(9f), "the server lets a player with the mod off stay, and there is time for the toggle"))
                {
                    Plugin.TestBlocked = MpBlocker;
                    FeatureRegistry.RefreshAll();
                    c.Check(!ModActive(), "the mod is off on the player");
                    yield return new WaitForSeconds(2.5f);
                    var stayed = ZNet.instance != null && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected && Player.m_localPlayer != null;
                    c.Check(stayed, "the player stays connected with the mod off (AllowPlayersWithoutMod on the server)");
                    Plugin.TestBlocked = null;
                    FeatureRegistry.RefreshAll();
                    var storm = false;
                    var end = Time.realtimeSinceStartup + 4f;
                    while (Time.realtimeSinceStartup < end)
                    {
                        storm |= Storms.Override() != null;
                        yield return null;
                    }
                    c.Check(ModActive() && ServerRules.UsingServer && HeldCells.TestKnown && HeldCells.IsCleared(cell),
                        "the mod is on again: the server's rules and its area lists are back");
                    c.Check(!storm && Storms.Override() == null, "still no blizzard in the cleared area, at no moment of the 4 s after turning on");
                    c.Check(Storms.StormAt(otherPoint, ServerRules.Current) && blizzard != null,
                        "an area that is not cleared has its blizzard again");
                    // The server hears that the mod is on again before its setting goes back to refuse.
                    yield return new WaitForSeconds(1.5f);

                    // And for real: the player goes to an area that is not cleared, the blizzard blows there (the weather
                    // patch is back on, the lists tell the two areas apart). Its Jotun come too: swept after, and the
                    // sweep is a kill for the server (1 kill clears here), so that area is unmarked with the other.
                    if (c.Check(clock.Room(TripSeconds + 6f), "time left to walk into an area that is not cleared"))
                    {
                        yield return TeleportAndWait(player, otherPoint, MpTravel, box);
                        if (c.Check(box.Ok, $"travelled to the area {otherCell} that is not cleared, at {F(otherPoint)}"))
                        {
                            // What the game is told to show (its own question, answered by the mod's patch again).
                            yield return Until(() => Storms.Override() == blizzard && EnvMan.instance.GetEnvironmentOverride() == blizzard, 6f);
                            c.Check(!HeldCells.IsCleared(otherCell) && Storms.Override() == blizzard
                                    && EnvMan.instance.GetEnvironmentOverride() == blizzard,
                                $"after the mod was turned on again: in an area that is not cleared the game is given the blizzard (weather now {CurrentEnv()})");
                        }
                    }
                }
            }
        }
        finally
        {
            if (Plugin.TestBlocked != null)
            {
                Plugin.TestBlocked = null;
                FeatureRegistry.RefreshAll();
            }
            ClearOverrides();
            SweepArea(player);
        }
        if (travelled)
        {
            yield return GoHomeMp(c, player, home);
        }
        if (changed)
        {
            yield return PutServerBack(c, s, stones0, cell, otherCell);
        }
        c.Report();
    }
}
#endif
