#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.CreaturesMoraleMod;

// Debug build only. More multiplayer self tests (tools/Test-Multiplayer.ps1; TESTING.md items in brackets). The
// dedicated server's own log is read through a server step (session log listener, SelfTestsStaged.cs):
//   morale.mp.typed-settings  scenario modded: the server owner types a name into the Meadows list one letter at a
//                             time (the real setting, set once per key as a settings window does): the edits apply
//                             once, one warning for the finished name and none for the half-typed ones, the client's
//                             Greydwarf turn afraid; then a typo: its warning (T26)
//   morale.mp.server-log      scenario modded: no error line of the mod in the dedicated server's log since it started,
//                             none in this client's (L04, M10)
//   morale.mp.other-version   scenario modded: this client say the hello of a build with another network version;
//                             the server name "has another version of the mod (network version N, the server have M)",
//                             let the player stay while it allows such players, refuse otherwise (M12)
//   morale.mp.refused-log     scenario each-off (after the probe turned the mod off on this client, in the game and
//                             before a join): the server's log have the turned-off notice, the refusal about a second
//                             later, a second refusal about a second after the join, both naming "has the mod turned
//                             off"; the client's log have its two lines (M08, M09)
internal static partial class SelfTests
{
    private const string MpTypedName = "morale.mp.typed-settings";
    private const string MpServerLogName = "morale.mp.server-log";
    private const string MpVersionName = "morale.mp.other-version";
    private const string MpRefusedLogName = "morale.mp.refused-log";
    private const string EachOffScenario = "each-off";

    private const string SrvLog = "morale.mp.srv.log";
    private const string SrvType = "morale.mp.srv.type";
    private const string SrvVersion = "morale.mp.srv.version";

    private const string TurnedOffLine = " turned " + ModInfo.Name + " off on their game.";
    private const string RefusedLine = "Refused ";
    private const string OffProblem = "has the mod turned off";
    private const string SettingsWarning = "In the creature settings";                 // the server about its own settings
    private const string ClientSettingsWarning = "In the server's creature settings"; // a client about the rules it was sent

    // Seconds (session log clock) at which a player finished joining this server (ZNet.RPC_PeerInfo, peer ready).
    private static readonly List<double> JoinTimes = new List<double>();
    private static Harmony _joinWatch;

    private static void PeerInfoPostfix(ZNet __instance, ZRpc rpc)
    {
        try
        {
            if (__instance == null || !__instance.IsServer() || rpc == null)
            {
                return;
            }
            var peer = __instance.GetPeer(rpc);
            if (peer != null && peer.IsReady())
            {
                lock (JoinTimes)
                {
                    JoinTimes.Add(SessionLog.Seconds);
                }
            }
        }
        catch
        {
            // Test bookkeeping must never reach the game.
        }
    }

    // From Plugin.BindConfig: the each-off test is registered while the mod may be off, and the join clock must run
    // from the server's start.
    private static void RegisterMultiplayerAtStart()
    {
        SelfTest.RegisterMultiplayer(MpRefusedLogName, EachOffScenario, RunMpRefusedLog);
        if (_joinWatch != null)
        {
            return;
        }
        try
        {
            var target = AccessTools.Method(typeof(ZNet), nameof(ZNet.RPC_PeerInfo));
            if (target != null)
            {
                _joinWatch = new Harmony(ModInfo.Guid + ".selftest.session");
                _joinWatch.Patch(target, postfix: new HarmonyMethod(typeof(SelfTests), nameof(PeerInfoPostfix)));
            }
        }
        catch (Exception e)
        {
            _joinWatch = null;
            Log.Info($"{SelfTest.Prefix} NOTE {MpRefusedLogName}: the join clock could not be installed ({e.Message})");
        }
    }

    private static void RegisterMultiplayerAuto()
    {
        SelfTest.RegisterMultiplayer(MpTypedName, SelfTest.Modded, RunMpTyped);
        SelfTest.RegisterMultiplayer(MpServerLogName, SelfTest.Modded, RunMpServerLog);
        SelfTest.RegisterMultiplayer(MpVersionName, SelfTest.Modded, RunMpOtherVersion);
        SelfTest.RegisterServerStep(SrvLog, ServerLogStep);
        SelfTest.RegisterServerStep(SrvType, ServerTypeStep);
        SelfTest.RegisterServerStep(SrvVersion, ServerVersionStep);
    }

    private static void UnregisterMultiplayerAuto()
    {
        foreach (var name in new[] { MpTypedName, MpServerLogName, MpVersionName })
        {
            SelfTest.UnregisterMultiplayer(name);
        }
        foreach (var step in new[] { SrvLog, SrvType, SrvVersion })
        {
            SelfTest.UnregisterServerStep(step);
        }
    }

    // ================================================================ server halves

    // Text safe inside a "key=value|key=value" answer.
    private static string Flat(string text) => Short(text).Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ');

    private static bool Has(SessionLog.Line line, string part) => line.Text.IndexOf(part, StringComparison.Ordinal) >= 0;

    // The server's own log since it started: errors, and what it wrote about players who turned the mod off.
    private static IEnumerator ServerLogStep(string arg, object[] reply)
    {
        yield return null;
        if (!SessionLog.Started)
        {
            SelfTest.Answer(reply, false, "the server's session log listener was never started");
            yield break;
        }
        if (SessionLog.Overflowed)
        {
            SelfTest.Answer(reply, false, "the server's session log is full: lines logged after that were not kept, so its log cannot be judged");
            yield break;
        }
        var errors = SessionErrors();
        var own = SessionLog.Lines(line => line.Own);
        var turnedOff = own.FindAll(line => Has(line, TurnedOffLine));
        var refusedOff = own.FindAll(line => Has(line, RefusedLine) && Has(line, OffProblem));
        var refusedVersion = own.FindAll(line => Has(line, RefusedLine) && Has(line, "has another version of the mod"));
        var refusedNoMod = own.FindAll(line => Has(line, RefusedLine) && Has(line, "does not have the mod"));
        // Live refusal: the first refusal after the first turned-off notice.
        var liveGap = -1.0;
        if (turnedOff.Count > 0)
        {
            foreach (var line in refusedOff)
            {
                if (line.At >= turnedOff[0].At)
                {
                    liveGap = line.At - turnedOff[0].At;
                    break;
                }
            }
        }
        // Refusal at a join: the last such refusal, counted from the last join that finished before it.
        var joinGap = -1.0;
        int joins;
        lock (JoinTimes)
        {
            joins = JoinTimes.Count;
            if (refusedOff.Count > 0)
            {
                var last = refusedOff[refusedOff.Count - 1].At;
                for (var i = JoinTimes.Count - 1; i >= 0; i--)
                {
                    if (JoinTimes[i] <= last)
                    {
                        joinGap = last - JoinTimes[i];
                        break;
                    }
                }
            }
        }
        SelfTest.Answer(reply, true,
            $"errors={errors.Count}|firstError={(errors.Count > 0 ? Flat(errors[0].Text) : "")}|lines={own.Count}|seconds={Inv((float)SessionLog.Seconds)}"
            + $"|turnedOff={turnedOff.Count}|refusedOff={refusedOff.Count}|refusedVersion={refusedVersion.Count}|refusedNoMod={refusedNoMod.Count}"
            + $"|liveGap={Inv((float)liveGap)}|joinGap={Inv((float)joinGap)}|joins={joins}|joinClock={(_joinWatch != null ? 1 : 0)}"
            + $"|lastRefusedOff={(refusedOff.Count > 0 ? Flat(refusedOff[refusedOff.Count - 1].Text) : "")}");
    }

    // arg "Meadows|<text>": the server owner types <text> at the end of that home list, one key at a time (the real
    // setting of the server, set once per key as a settings window do for a text setting). Answer after the edits
    // settled: what the server logged meanwhile.
    private static IEnumerator ServerTypeStep(string arg, object[] reply)
    {
        var parts = (arg ?? "").Split(new[] { '|' }, 2);
        var entry = parts.Length == 2 && parts[0] == "Meadows" ? Plugin.HomeLists[0] : null;
        if (entry == null || parts[1].Length == 0 || !SessionLog.Started)
        {
            SelfTest.Answer(reply, false, $"cannot type '{arg}' (only the Meadows list is known here, and the session log must run)");
            yield break;
        }
        var from = SessionLog.Seconds;
        var start = entry.Value;
        var typed = parts[1];
        for (var i = 1; i <= typed.Length; i++)
        {
            entry.Value = start + typed.Substring(0, i);
            yield return new WaitForSecondsRealtime(0.08f);
        }
        yield return new WaitForSecondsRealtime(ServerRules.EditSettle + 1f);
        var since = SessionLog.Lines(line => line.Own && line.At >= from);
        var warnings = since.FindAll(line => (line.Level & LogLevel.Warning) != 0);
        var settings = warnings.FindAll(line => Has(line, SettingsWarning));
        var sent = since.FindAll(line => Has(line, "Sent the creature rules")).Count;
        SelfTest.Answer(reply, true,
            $"keys={typed.Length}|warnings={warnings.Count}|settings={settings.Count}|sent={sent}|value={Flat(entry.Value)}"
            + $"|last={(settings.Count > 0 ? Flat(settings[settings.Count - 1].Text) : "")}");
    }

    // arg "<N>|allow" or "<N>|refuse": the (one) player's game said network version N in its hello. The server owner
    // switches AllowPlayersWithoutMod on and back off, which checks every player in again after the grace. allow =
    // such players may stay for these seconds (as with the setting on): the warning name the problem; refuse = the
    // refusal name it. The verdict is also logged here (the refused client may never get the answer).
    private static IEnumerator ServerVersionStep(string arg, object[] reply)
    {
        yield return null;
        var parts = (arg ?? "").Split('|');
        var net = ZNet.instance;
        var peers = net != null ? net.GetPeers() : null;
        if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var version)
            || peers == null || peers.Count == 0 || peers[0] == null || !SessionLog.Started)
        {
            SelfTest.Answer(reply, false, $"bad argument '{arg}', no player connected, or no session log");
            yield break;
        }
        var allow = parts[1] == "allow";
        var heard = NetworkGate.PeerNetworkVersion(peers[0]);
        if (heard != version)
        {
            SelfTest.Answer(reply, false, $"the server heard network version {heard} from the player, the test sent {version}");
            yield break;
        }
        var expected = $"has another version of the mod (network version {version}, the server has {ModInfo.NetworkVersion})";
        var from = SessionLog.Seconds;
        if (allow)
        {
            PlayerCheck.TestAllowUntil = Time.realtimeSinceStartup + 8f;
        }
        Plugin.AllowPlayersWithoutMod.Value = true;
        Plugin.AllowPlayersWithoutMod.Value = false;
        var found = "";
        var end = Time.realtimeSinceStartup + 5f;
        while (found.Length == 0 && Time.realtimeSinceStartup < end)
        {
            yield return null;
            var lines = SessionLog.Lines(line => line.Own && line.At >= from && Has(line, expected)
                                                 && (allow ? Has(line, "AllowPlayersWithoutMod is on") : Has(line, RefusedLine)));
            if (lines.Count > 0)
            {
                found = lines[0].Text;
            }
        }
        if (allow)
        {
            PlayerCheck.TestAllowUntil = -1f;
        }
        var what = allow ? "lets the player stay and warns" : "refuses the player";
        if (found.Length > 0)
        {
            SelfTest.Pass(MpVersionName, $"server: it {what}, and its log names the problem: \"{Short(found)}\"");
        }
        else
        {
            SelfTest.Fail(MpVersionName, $"server: no log line that {what} with \"{expected}\" within 5 s");
        }
        SelfTest.Answer(reply, found.Length > 0, found.Length > 0 ? Flat(found) : $"no line with \"{expected}\"");
    }

    // ================================================================ morale.mp.typed-settings

    private static string Val(Dictionary<string, string> fields, string key) => fields != null && fields.TryGetValue(key, out var value) ? value : "";

    private static IEnumerator RunMpTyped()
    {
        var c = new Checks(MpTypedName);
        var stage = new Stage[1];
        try
        {
            yield return MpTypedSteps(c, stage);
        }
        finally
        {
            Finish(stage[0]);
        }
        // The server's Meadows list as it was (every multiplayer test of the mod also start with this reset).
        var back = new SelfTest.ServerReply();
        yield return SelfTest.CallServer(SrvSet, "reset", back);
        if (!back.Answered || !back.Ok)
        {
            SelfTest.Note(MpTypedName, $"the server's settings could not be put back to their defaults at the end ({back}); the next test of the mod does it");
        }
    }

    private static IEnumerator MpTypedSteps(Checks c, Stage[] stage)
    {
        Stage s = null;
        var w = new Waiter();
        var reply = new SelfTest.ServerReply();
        {
            yield return MpBegin(c, MpTypedName, w);
            if (!w.Met)
            {
                c.Report();
                yield break;
            }
            // A reset of the server's settings by the step before may still settle: it must not count below.
            yield return Wait(ServerRules.EditSettle + 1f, null);
            s = Stage.Begin(MpTypedName, c);
            stage[0] = s;
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            Standing.TestOverride = new Standing.Override(3);
            var spot = s.Spot(here, s.Forward, 8f, true);
            var g = SpawnMeadows(s, c, "Greydwarf", spot, here - spot);
            if (g == null)
            {
                c.Report();
                yield break;
            }
            Action hold = () =>
            {
                s.Noise();
                Stage.Place(g.m_character, spot, player.transform.position - spot);
            };
            yield return Until(() => g.m_targetCreature == player, 6f, hold, w);
            c.Check(w.Met && Attitudes.Judge(g, player) == Attitude.Hostile, "T26 setup: with the server's default lists a Greydwarf attacks the rank 3 player");

            // ----- Greydwarf typed into the server's Meadows list, letter by letter -----
            c.Expect("several home biome lists");
            c.Expect("are not creatures of this game");
            var mark = Watch.Mark();
            const string typed = ", Greydwarf";
            yield return SelfTest.CallServer(SrvType, "Meadows|" + typed, reply);
            var f = Fields(reply.Detail);
            var serverWarning = Val(f, "last");
            c.Check(reply.Answered && reply.Ok, $"T26: the server half typed \"{typed}\" at the end of the server's Meadows list, one key at a time ({reply})");
            if (reply.Answered && reply.Ok)
            {
                c.Check(Val(f, "sent") == "1",
                    $"T26: the {typed.Length} edits apply once, after the typing stopped: the server sent its rules once (sent {Val(f, "sent")} time(s))");
                c.Check(Val(f, "settings") == "1" && Val(f, "warnings") == "1" && serverWarning.Contains("Greydwarf")
                        && serverWarning.Contains("several home biome lists") && serverWarning.Contains("the easiest biome is used"),
                    $"T26: the server's log warns once that Greydwarf is in several home biome lists and the easiest biome is used, with no warning for the "
                    + $"half-typed names (warnings {Val(f, "warnings")}, of the settings {Val(f, "settings")}: \"{serverWarning}\")");
            }
            yield return Until(() => Attitudes.Judge(g, player) == Attitude.Afraid, 4f, hold, w);
            c.Check(w.Met, $"T26: about a second after the typing stopped the Greydwarf is afraid of the rank 3 player on this client (Judge {Attitudes.Judge(g, player)})");
            yield return Until(() => FearOf(g) == player && g.m_targetCreature == null, 4f, hold, w);
            c.Check(w.Met, $"T26: it drops the player and runs (runs from {Who(FearOf(g))}, target {Who(g.m_targetCreature)})");
            var clientWarning = Watch.Last(ClientSettingsWarning, mark);
            c.Check(Watch.Count("Using the server's creature rules", mark, LogLevel.Info) == 1 && Watch.Count(ClientSettingsWarning, mark, LogLevel.Warning) == 1
                    && clientWarning.Contains("Greydwarf"),
                $"T26: this client took the server's rules once and warns once about the finished name, never about a half-typed one "
                + $"(rules taken {Watch.Count("Using the server's creature rules", mark, LogLevel.Info)} time(s), warnings {Watch.Count(ClientSettingsWarning, mark, LogLevel.Warning)}: "
                + $"\"{clientWarning}\")");

            // ----- a typo -----
            mark = Watch.Mark();
            yield return SelfTest.CallServer(SrvType, "Meadows|, Boarr", reply);
            f = Fields(reply.Detail);
            serverWarning = Val(f, "last");
            c.Check(reply.Answered && reply.Ok && Val(f, "settings") == "1" && serverWarning.Contains("Boarr") && serverWarning.Contains("not creatures of this game"),
                $"T26: after the typo Boarr is typed the server's log warns once that Boarr is not a creature of this game ({reply})");
            yield return Until(() => Watch.Count("Boarr", mark, LogLevel.Warning) >= 1, 4f, hold, w);
            c.Check(w.Met && Watch.Count(ClientSettingsWarning, mark, LogLevel.Warning) == 1,
                $"T26: this client's log warns once about Boarr too (\"{Watch.Last("Boarr", mark)}\")");
            s.Destroy(g);
            c.Report();
        }
    }

    // ================================================================ morale.mp.server-log

    private static IEnumerator RunMpServerLog()
    {
        if (!SelfTest.IsMultiplayerRun || !ConnectedClient())
        {
            SelfTest.Fail(MpServerLogName, "this test needs a client connected to the dedicated server of a multiplayer run");
            yield break;
        }
        var reply = new SelfTest.ServerReply();
        yield return SelfTest.CallServer(SrvLog, "", reply);
        var f = Fields(reply.Detail);
        f.TryGetValue("errors", out var errors);
        f.TryGetValue("firstError", out var first);
        f.TryGetValue("lines", out var lines);
        f.TryGetValue("seconds", out var seconds);
        if (reply.Answered && reply.Ok && errors == "0")
        {
            SelfTest.Pass(MpServerLogName, $"the dedicated server's log has no error line of {ModInfo.Name} and none naming its code since the server started "
                                           + $"({lines} line(s) of the mod in {seconds} s)");
        }
        else
        {
            SelfTest.Fail(MpServerLogName, reply.Answered && reply.Ok
                ? $"the dedicated server's log has {errors} error line(s) of {ModInfo.Name}, first: {first}"
                : $"the server half did not report its log ({reply})");
        }
        var mine = SessionErrors();
        if (mine.Count == 0 && SessionLog.Started && !SessionLog.Overflowed)
        {
            SelfTest.Pass(MpServerLogName, $"this client's log has no error line of {ModInfo.Name} and none naming its code since the game started");
        }
        else if (mine.Count > 0)
        {
            SelfTest.Fail(MpServerLogName, $"this client's log has {mine.Count} error line(s) of {ModInfo.Name}, first: {Short(mine[0].Text)}");
        }
        else
        {
            SelfTest.Fail(MpServerLogName, SessionLog.Started
                ? "the client's session log is full: lines logged after that were not kept, so its log cannot be judged"
                : "the client's session log listener was never started");
        }
    }

    // ================================================================ morale.mp.other-version

    private static string HelloOf(int networkVersion) => $"1|{networkVersion.ToString(CultureInfo.InvariantCulture)}|{ModInfo.Version}|on";

    private static IEnumerator RunMpOtherVersion()
    {
        var c = new Checks(MpVersionName);
        var w = new Waiter();
        var reply = new SelfTest.ServerReply();
        ZRpc rpc = null;
        var fake = ModInfo.NetworkVersion + 1;
        var problem = $"has another version of the mod (network version {fake}, the server has {ModInfo.NetworkVersion})";
        const string hello = ModInfo.Guid + ".Hello";
        try
        {
            yield return MpBegin(c, MpVersionName, w);
            var server = w.Met && ZNet.instance != null ? ZNet.instance.GetServerPeer() : null;
            rpc = server != null ? server.m_rpc : null;
            if (!c.Check(rpc != null && rpc.IsConnected(), "M12 setup: a connection to the server to say the hello on"))
            {
                c.Report();
                yield break;
            }

            // ----- such players may stay: the warning name the problem -----
            // What a build with another ModNetworkVersion say when it joins (the framework's hello, protocol 1).
            rpc.Invoke(hello, HelloOf(fake));
            yield return SelfTest.CallServer(SrvVersion, fake.ToString(CultureInfo.InvariantCulture) + "|allow", reply);
            c.Check(reply.Answered && reply.Ok && reply.Detail.Contains(problem),
                $"M12: while the server lets such players stay, its log warns with \"{problem}\" ({reply})");
            c.Check(ConnectedClient(), "M12: the player stays in meanwhile");

            // The real hello again: compatible.
            rpc.Invoke(hello, HelloOf(ModInfo.NetworkVersion));
            var peer = new FieldsRef();
            yield return AskUntil(SrvPeer, "", fields => fields["compatible"] == "1", 4f, null, peer, w);
            c.Check(w.Met, $"M12 setup: with the real hello again the server calls the player compatible (problem \"{peer["problem"]}\")");

            // ----- refused the same way -----
            rpc.Invoke(hello, HelloOf(fake));
            yield return SelfTest.CallServer(SrvVersion, fake.ToString(CultureInfo.InvariantCulture) + "|refuse", reply);
            var start = Time.realtimeSinceStartup;
            while (ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.ErrorVersion && Time.realtimeSinceStartup - start < 10f)
            {
                yield return null;
            }
            c.Check(ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.ErrorVersion,
                $"M12: the server refuses the player whose game said another network version: this game is sent back with \"Incompatible version\" "
                + $"(connection status {ZNet.GetConnectionStatus()})");
            if (reply.Answered)
            {
                c.Check(reply.Ok && reply.Detail.Contains(problem) && reply.Detail.Contains(RefusedLine.Trim()),
                    $"M12: the server's refusal names \"{problem}\" ({reply})");
            }
            else
            {
                SelfTest.Note(MpVersionName, "M12: the refused client did not get the server half's answer any more; the server's own verdict on its log line is "
                                             + $"in the server log (PASS or FAIL {MpVersionName}, \"server: ...\")");
            }
            c.Report();
        }
        finally
        {
            // Still in (a step failed before the refusal): the real hello again.
            if (rpc != null && rpc.IsConnected() && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected)
            {
                rpc.Invoke(hello, HelloOf(ModInfo.NetworkVersion));
            }
            ResetHooks();
        }
    }

    // ================================================================ morale.mp.refused-log

    private static double Num(Dictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : -1.0;

    private static IEnumerator RunMpRefusedLog()
    {
        if (!SelfTest.IsMultiplayerRun || !ConnectedClient() || !Plugin.Live)
        {
            SelfTest.Fail(MpRefusedLogName, "this test needs a client connected to the dedicated server of a multiplayer run, with the mod on again");
            yield break;
        }
        var c = new Checks(MpRefusedLogName);
        var reply = new SelfTest.ServerReply();
        yield return SelfTest.CallServer(SrvLog, "", reply);
        if (!c.Check(reply.Answered && reply.Ok, $"the server half reported the server's log ({reply})"))
        {
            c.Report();
            yield break;
        }
        var f = Fields(reply.Detail);
        f.TryGetValue("lastRefusedOff", out var lastRefused);
        var turnedOff = Num(f, "turnedOff");
        var refusedOff = Num(f, "refusedOff");
        var liveGap = Num(f, "liveGap");
        var joinGap = Num(f, "joinGap");
        c.Check(turnedOff >= 1, $"M08: the server log says \"<player>{TurnedOffLine}\" ({F(turnedOff)} time(s))");
        c.Check(refusedOff >= 2 && (lastRefused ?? "").Contains(OffProblem),
            $"M08, M09: the server log has a refusal naming \"{OffProblem}\" for the mod turned off while playing and one for the join with it off "
            + $"({F(refusedOff)} such line(s), last: \"{lastRefused}\")");
        c.Check(liveGap >= 0.5 && liveGap <= 3.5, $"M08: the refusal comes about a second after the turned-off notice ({F(liveGap)} s later)");
        c.Check(Num(f, "joinClock") == 1 && joinGap >= 0.5 && joinGap <= 3.5,
            $"M09: joining with the mod off, the player is refused about a second after the join ({F(joinGap)} s after the server saw the join; "
            + $"{F(Num(f, "joins"))} join(s) seen)");
        c.Check(Num(f, "errors") == 0, $"M10: the dedicated server's log has no error line of {ModInfo.Name} ({F(Num(f, "errors"))}; first: {(f.TryGetValue("firstError", out var first) ? first : "")})");
        // This client's own lines of the turn-off (the probe's test turned the mod off for real, in the game).
        var told = SessionCount($"Told the server that {ModInfo.Name} is now off on this game.");
        var withdrawn = SessionCount("Standing withdrawn:");
        c.Check(told >= 1 && withdrawn >= 1,
            $"M08: this client's log says it told the server the mod is off and that its standing was withdrawn (told {told} time(s), withdrawn {withdrawn} time(s))");
        c.Report();
    }
}
#endif
