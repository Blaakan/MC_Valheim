#if DEBUG
using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using BepInEx.Logging;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SailingSkillMod;

// Debug build only. Me = more multiplayer self tests (same class as SelfTests.cs; tools/Test-Multiplayer.ps1).
// Scenario "modded":
//   sailing.mp.other-version  TESTING.md M10: a copy of the mod with another network version. No second build in the
//                             run, so the client puts on the wire what such a copy says (framework hello protocol v1,
//                             NetworkGate): "<guid>.Hello" "1|<netVersion>|<version>|off", then "<guid>.HelloState"
//                             "on" (the server checks a player again when its copy says on).
//                             (1) server forced to let it in for the time of one server step: its Warning names both
//                                 versions, the player stays;
//                             (2) server as set (AllowPlayersWithoutMod off): player checked again, refused about a
//                                 second later: this game gets "Incompatible version" and is back at the main menu.
//                                 The server's own line "Refused <player>: has another version of the mod (network
//                                 version 2, the server has 1). This server requires ..." is judged ON THE SERVER
//                                 (a PASS / FAIL line of this test in the server's log, which the script reads): the
//                                 answer to the client is often lost, the client is logging out by then.
//                             Registered LAST of the mod's "modded" tests: it ends refused (the probe joins again
//                             before the next test) and leaves a 'Refused' line in the server log.
// Scenario "vanilla-server" (registered at plugin start: the feature is off there):
//   sailing.mp.vanilla-server.saved  TESTING.md M08: on a server without the mod, the game's own character save writes
//                             the Sailing level into the character file (read back from disk): what a single-player
//                             world loads afterwards
// Server half of the first test = step "sailing.mp.version" ("allow|<uid>|<net>" or "refuse|<uid>|<net>").
internal static partial class SelfTests
{
    private const string MpVersionName = "sailing.mp.other-version";
    private const string MpVanillaSavedName = "sailing.mp.vanilla-server.saved";
    private const string MpVersionStep = "sailing.mp.version";

    private const string HelloRpc = ModInfo.Guid + ".Hello";
    private const string HelloStateRpc = ModInfo.Guid + ".HelloState";

    // BindConfig (RegisterAlways).
    private static void RegisterAlwaysMultiplayerMore()
    {
        SelfTest.RegisterMultiplayer(MpVanillaSavedName, VanillaServerScenario, RunMpVanillaServerSaved);
    }

    // OnActivated (RegisterMore), after every other multiplayer test of the mod.
    private static void RegisterMultiplayerMore()
    {
        SelfTest.RegisterMultiplayer(MpVersionName, SelfTest.Modded, RunMpOtherVersion);
        SelfTest.RegisterServerStep(MpVersionStep, RunVersionStep);
    }

    // OnDeactivated (UnregisterMore).
    private static void UnregisterMultiplayerMore()
    {
        SelfTest.UnregisterMultiplayer(MpVersionName);
        SelfTest.UnregisterServerStep(MpVersionStep);
    }

    private static string VersionReason(int theirs) =>
        $"has another version of the mod (network version {theirs}, the server has {ModInfo.NetworkVersion})";

    // ---------- server half ----------

    private static IEnumerator RunVersionStep(string arg, object[] reply)
    {
        var parts = (arg ?? "").Split('|');
        var uid = 0L;
        var theirs = 0;
        if (parts.Length < 3 || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uid)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out theirs))
        {
            SelfTest.Answer(reply, false, $"bad argument '{arg}'");
            yield break;
        }
        var net = ZNet.instance;
        var peer = net != null ? net.GetPeer(uid) : null;
        if (peer == null || peer.m_rpc == null)
        {
            SelfTest.Answer(reply, false, "the player is not connected to this server");
            yield break;
        }
        var who = PeerName(peer);
        var reason = VersionReason(theirs);
        var mark = LogMark();
        switch (parts[0])
        {
            // Let in for the time of this step only (in memory, never the setting): the Warning about a player whose
            // copy said another network version on the wire.
            case "allow":
                try
                {
                    PlayerCheck.TestAllowWithoutMod = true;
                    string line = null;
                    var end = Time.realtimeSinceStartup + 8f;
                    while (line == null && Time.realtimeSinceStartup < end)
                    {
                        yield return null;
                        line = MyLines(mark, LogLevel.Warning).FirstOrDefault(l => l.StartsWith(who + " " + reason, StringComparison.Ordinal));
                    }
                    // No check of this player may still wait when the forced setting goes.
                    end = Time.realtimeSinceStartup + 3f;
                    while (PlayerCheck.HasWork && Time.realtimeSinceStartup < end)
                    {
                        yield return null;
                    }
                    var refused = MyLines(mark, LogLevel.Warning).Count(l => l.StartsWith("Refused ", StringComparison.Ordinal));
                    var said = MyLines(mark, LogLevel.Info).Count(l => l.Contains($"turned {ModInfo.Name} on on their game"));
                    SelfTest.Answer(reply, line != null && refused == 0 && !PlayerCheck.HasWork, string.Join(Sep, new[]
                    {
                        "who=" + who, "warning=" + (line ?? ""), "refused=" + refused, "saidon=" + said,
                        "heard=" + NetworkGate.PeerNetworkVersion(peer).ToString(CultureInfo.InvariantCulture),
                        "kicked=" + ZNet.PeersToDisconnectAfterKick.ContainsKey(peer),
                    }));
                }
                finally
                {
                    PlayerCheck.TestAllowWithoutMod = null;
                }
                break;

            // Server as its owner set it: the player is checked again after the grace (what switching
            // AllowPlayersWithoutMod back off does for players already in). Verdict about the server's own line goes
            // into the server's log: the answer may not reach a client that is logging out.
            case "refuse":
            {
                var record = NetworkGate.PeerProblem(peer) ?? "none (compatible)";
                var allow = Plugin.AllowPlayersWithoutMod != null && Plugin.AllowPlayersWithoutMod.Value;
                PlayerCheck.Schedule(peer);
                var head = "Refused " + who + ": " + reason + ".";
                string line = null;
                var end = Time.realtimeSinceStartup + 8f;
                while (line == null && Time.realtimeSinceStartup < end)
                {
                    yield return null;
                    line = MyLines(mark, LogLevel.Warning).FirstOrDefault(l => l.StartsWith(head, StringComparison.Ordinal));
                }
                var ok = line != null && line.Contains($"This server requires {ModInfo.Name} on every player");
                if (ok)
                {
                    SelfTest.Pass(MpVersionName, "server: its own line about the refusal: " + FirstLine(line));
                }
                else
                {
                    SelfTest.Fail(MpVersionName,
                        $"server: no Warning '{head} This server requires {ModInfo.Name} on every player ...' within 8 s of checking the player again "
                        + $"(what the server knows of the player's copy: {record}; AllowPlayersWithoutMod {allow}; line found: '{(line != null ? FirstLine(line) : "")}')");
                }
                SelfTest.Answer(reply, ok, string.Join(Sep, new[] { "who=" + who, "refused=" + (line ?? ""), "record=" + record }));
                break;
            }

            default:
                SelfTest.Answer(reply, false, $"unknown job '{parts[0]}'");
                break;
        }
    }

    // ---------- client half ----------

    // Probe's server call stepped by hand, one frame at a time, for at most <seconds>: the test acts while the server
    // step runs (eachFrame get the seconds since the call), and goes on when the answer is lost (stop). raw = the
    // probe's answer slots (answered, ok, detail).
    private static IEnumerator ServerCallFor(string step, string arg, object[] raw, float seconds, Action<float> eachFrame, Func<bool> stop)
    {
        if (!(AppDomain.CurrentDomain.GetData(SelfTest.ServerCallSlot) is Func<string, string, object[], IEnumerator> call))
        {
            raw[2] = "no server to call (not a multiplayer run)";
            yield break;
        }
        var pending = call(step, arg ?? "", raw);
        try
        {
            var start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < seconds && pending.MoveNext())
            {
                eachFrame?.Invoke(Time.realtimeSinceStartup - start);
                if (stop != null && stop())
                {
                    break;
                }
                yield return null;
            }
        }
        finally
        {
            (pending as IDisposable)?.Dispose();
        }
    }

    private static bool AtMainMenu() => FejdStartup.instance != null && Player.m_localPlayer == null;

    private static IEnumerator RunMpOtherVersion()
    {
        var c = new Checks(MpVersionName);
        var watch = LogMark();
        if (Player.m_localPlayer == null || !Connected)
        {
            SelfTest.Fail(MpVersionName, "not in a world as a client");
            yield break;
        }
        var view = FeatureRegistry.Find(ModInfo.Guid);
        var rpc = ZNet.instance.GetServerRPC();
        var uid = ZDOMan.GetSessionID().ToString(CultureInfo.InvariantCulture);
        var theirs = ModInfo.NetworkVersion + 1;
        var helloSent = false;
        try
        {
            if (!c.Check(view != null && view.Value.IsActive && rpc != null && rpc.IsConnected(),
                    "the mod is not active on the client, or there is no connection to the server"))
            {
                c.Report();
                yield break;
            }

            // (1) What a copy with another network version says, on the wire; the server (forced to let it in for this
            // step) names both versions and the player stays.
            var raw = new object[] { false, false, "" };
            void SayOtherVersion(float since)
            {
                // Half a second after the call: the server step has its place in the log by then.
                if (!helloSent && since > 0.5f)
                {
                    rpc.Invoke(HelloRpc, $"1|{theirs}|{ModInfo.Version}|off");
                    rpc.Invoke(HelloStateRpc, "on");
                    helloSent = true;
                }
            }
            yield return ServerCallFor(MpVersionStep, $"allow|{uid}|{theirs}", raw, 20f, SayOtherVersion, null);
            var kv = Parse(raw[2] as string ?? "");
            var answered = raw[0] is bool a1 && a1;
            var ok = raw[1] is bool o1 && o1;
            var want = $"{Txt(kv, "who")} {VersionReason(theirs)}; AllowPlayersWithoutMod is on, so they may play";
            c.Check(helloSent, "the hello of a copy with another network version was not sent");
            c.Check(answered && ok && Txt(kv, "warning").StartsWith(want, StringComparison.Ordinal) && Num(kv, "heard") == theirs,
                $"server, let in for the check: answered {answered}, '{raw[2]}' (expected its Warning to start with '<player> {VersionReason(theirs)}; "
                + "AllowPlayersWithoutMod is on, so they may play' and the network version it heard to be " + theirs + ")");
            c.Check(Num(kv, "refused") == 0f && !Yes(kv, "kicked"), "server, let in for the check: it refused the player all the same: " + raw[2]);
            yield return new WaitForSecondsRealtime(1f);
            if (!c.Check(Connected && view.Value.IsActive, $"other version, let in: the player did not stay (connection {ZNet.GetConnectionStatus()}, mod {view.Value.State})"))
            {
                c.Report();
                yield break;
            }
            // Own log looked at here: after the refusal the game logs out, which is not this test's business.
            CheckQuiet(c, watch);

            // (2) The server as set: checked again, refused about a second later, this game shows "Incompatible
            // version" (connection status ErrorVersion) and goes back to the main menu.
            raw = new object[] { false, false, "" };
            var t0 = Time.realtimeSinceStartup;
            var refusedAfter = -1f;
            bool Refused()
            {
                if (refusedAfter < 0f && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.ErrorVersion)
                {
                    refusedAfter = Time.realtimeSinceStartup - t0;
                }
                // Answer lost: go on one second after the game is back at the menu.
                return refusedAfter >= 0f && AtMainMenu() && Time.realtimeSinceStartup - t0 > refusedAfter + 1f;
            }
            yield return ServerCallFor(MpVersionStep, $"refuse|{uid}|{theirs}", raw, 20f, null, Refused);
            var menu = new Box();
            yield return WaitFor(() => Refused() || AtMainMenu(), 20f, menu);
            Refused();
            c.Check(refusedAfter >= 0f && AtMainMenu(),
                $"other version, server requires the mod: the player was not refused (connection {ZNet.GetConnectionStatus()}, at the main menu {AtMainMenu()})");
            c.Check(refusedAfter >= 0.5f && refusedAfter <= 6f,
                $"refused {F(refusedAfter)} s after the server checked the player again (expected about {F(PlayerCheck.GraceSeconds)} s; -1 = never)");
            answered = raw[0] is bool a2 && a2;
            ok = raw[1] is bool o2 && o2;
            kv = Parse(raw[2] as string ?? "");
            if (answered)
            {
                c.Check(ok && Txt(kv, "refused").StartsWith($"Refused {Txt(kv, "who")}: {VersionReason(theirs)}.", StringComparison.Ordinal),
                    $"the server's own line about the refusal: '{raw[2]}' (expected 'Refused <player>: {VersionReason(theirs)}. ...')");
            }
            else
            {
                c.Note("the server's answer did not arrive (this game was logging out): its own line about the refusal is judged on the server, "
                       + $"see the server's PASS / FAIL line of {MpVersionName} (a server FAIL fails the scenario)");
            }
            c.Report();
        }
        finally
        {
            try
            {
                // Stopped half way and still in: the server must hear the real hello again.
                var net = ZNet.instance;
                var server = net != null && !net.IsServer() ? net.GetServerRPC() : null;
                if (helloSent && server != null && server.IsConnected() && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected)
                {
                    server.Invoke(HelloRpc, $"1|{ModInfo.NetworkVersion}|{ModInfo.Version}|on");
                }
            }
            catch (Exception e)
            {
                Log.Error($"Sailing self test clean-up failed: {e}");
            }
        }
    }

    // ---------- sailing.mp.vanilla-server.saved (M08) ----------

    private static IEnumerator RunMpVanillaServerSaved()
    {
        var c = new Checks(MpVanillaSavedName);
        var watch = LogMark();
        var player = Player.m_localPlayer;
        var profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
        if (player == null || player.GetSkills() == null || profile == null)
        {
            SelfTest.Fail(MpVanillaSavedName, "no local player or no character profile");
            yield break;
        }
        var before = SailingState(player);
        var touched = false;
        try
        {
            var view = FeatureRegistry.Find(ModInfo.Guid);
            if (!c.Check(Connected && view != null && !view.Value.IsActive && view.Value.State == nameof(ModState.ServerMissing),
                    $"not on a server without the mod (connected {Connected}, mod {(view != null ? view.Value.State : "not registered")})"))
            {
                c.Report();
                yield break;
            }
            touched = true;
            CheckLevelReachesFile(c, player, profile, "on a server without the mod");
            yield return null;
            c.Check(Connected, "the client lost the connection during the test");
            CheckQuiet(c, watch);
            c.Report(" (the character file written on a server without the mod holds the level and progress: what a single-player world loads)");
        }
        finally
        {
            if (touched)
            {
                try
                {
                    PutSailingState(player, before);
                    var error = SaveCharacterNow();
                    if (error != null)
                    {
                        Log.Error($"Sailing self test clean-up failed: character save threw {error}");
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"Sailing self test clean-up failed: {e}");
                }
            }
        }
    }
}
#endif
