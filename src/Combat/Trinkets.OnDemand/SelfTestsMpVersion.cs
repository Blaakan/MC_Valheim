#if DEBUG
using System;
using System.Collections;
using BepInEx.Logging;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.TrinketsOnDemandMod;

// Debug build only. Multiplayer self test of TESTING.md M08 (same class as SelfTests.cs; tools/Test-Multiplayer.ps1,
// scenario "modded"): a copy of the mod with another network version. No second build in the run, so the test puts
// on the wire what such a copy and such a server say (framework hello protocol v1, NetworkGate):
//   client -> server  "<guid>.Hello"      "1|<netVersion>|<version>|<on|off>"
//   client -> server  "<guid>.HelloState" "on" | "off"
//   server -> client  "<guid>.HelloAck"   "1|on|<netVersion>|<version>"
//   trinkets.mp.other-version  server answers with another network version: player's copy goes inactive ("cannot talk
//                              to each other"), bar like the normal game; real answer: active again. Player's hello
//                              says another network version: with AllowPlayersWithoutMod the server warns and lets it
//                              stay; without, it refuses it (Incompatible version) and its log names both versions.
// Registered LAST of the mod's "modded" tests: it leaves a 'Refused' line in the server log and ends refused (probe
// joins again before the next test). Real hello sent again in finally when the test stops half way.
internal static partial class SelfTests
{
    private const string MpVersionName = "trinkets.mp.other-version";
    private const string SrvVersionStep = "trinkets.mp.srv-version";

    private const string HelloRpc = ModInfo.Guid + ".Hello";
    private const string HelloStateRpc = ModInfo.Guid + ".HelloState";
    private const string HelloAckRpc = ModInfo.Guid + ".HelloAck";

    private static string VersionReason(int theirs) =>
        $"has another version of the mod (network version {theirs}, the server has {ModInfo.NetworkVersion})";

    // Server half.
    //   ack=<net>     send the player the answer of a server whose copy has that network version
    //   log=<net>     what me logged about a player with that network version (let in / refused)
    //   refuse=<net>  server owner turns AllowPlayersWithoutMod off; answer with my 'Refused' line about that version
    //                 (waits for it up to 8 s)
    private static IEnumerator ServerVersionStep(string arg, object[] reply)
    {
        yield return null;
        EnsureLog();
        var eq = (arg ?? "").IndexOf('=');
        var what = eq > 0 ? arg.Substring(0, eq) : "";
        if (!int.TryParse(eq > 0 ? arg.Substring(eq + 1) : "", out var net))
        {
            SelfTest.Answer(reply, false, $"bad argument '{arg}'");
            yield break;
        }
        var reason = VersionReason(net);
        switch (what)
        {
            case "ack":
            {
                var peer = OnlyPlayer();
                if (peer == null || peer.m_rpc == null)
                {
                    SelfTest.Answer(reply, false, "no player with a character on the server");
                    yield break;
                }
                peer.m_rpc.Invoke(HelloAckRpc, $"1|on|{net}|{ModInfo.Version}");
                SelfTest.Answer(reply, true, $"sent {peer.m_playerName} the answer of a server with network version {net}");
                yield break;
            }
            case "log":
            {
                var letIn = 0;
                var refused = 0;
                string first = null;
                foreach (var line in _seen.Since(0))
                {
                    if (!line.Mine || (line.Level & LogLevel.Warning) == 0 || !line.Text.Contains(reason))
                    {
                        continue;
                    }
                    first = first ?? FirstLine(line.Text);
                    if (line.Text.StartsWith("Refused ", StringComparison.Ordinal))
                    {
                        refused++;
                    }
                    else if (line.Text.Contains(LetInLine))
                    {
                        letIn++;
                    }
                }
                SelfTest.Answer(reply, true, $"letin={letIn};refused={refused};first={first ?? ""}");
                yield break;
            }
            case "refuse":
            {
                var mark = _seen.Mark();
                Plugin.AllowPlayersWithoutMod.Value = false;
                string found = null;
                var end = Time.realtimeSinceStartup + 8f;
                while (found == null && Time.realtimeSinceStartup < end)
                {
                    foreach (var line in _seen.Since(mark))
                    {
                        if (line.Mine && (line.Level & LogLevel.Warning) != 0 && line.Text.StartsWith("Refused ", StringComparison.Ordinal)
                            && line.Text.Contains(reason))
                        {
                            found = FirstLine(line.Text);
                            break;
                        }
                    }
                    if (found == null)
                    {
                        yield return null;
                    }
                }
                SelfTest.Answer(reply, found != null, found ?? $"no 'Refused ...: {reason}' line within 8 s of AllowPlayersWithoutMod = false");
                yield break;
            }
            default:
                SelfTest.Answer(reply, false, $"unknown request '{arg}'");
                yield break;
        }
    }

    // Probe's server call, one frame at a time, for at most <seconds>: test goes on when the answer is lost (player
    // refused meanwhile), where SelfTest.CallServer would wait its whole time-out. raw = probe's answer slots
    // (answered, ok, detail).
    private static IEnumerator ServerCallFor(string step, string arg, object[] raw, float seconds, Func<bool> stop)
    {
        if (!(AppDomain.CurrentDomain.GetData(SelfTest.ServerCallSlot) is Func<string, string, object[], IEnumerator> call))
        {
            raw[2] = "no server to call (not a multiplayer run)";
            yield break;
        }
        var pending = call(step, arg ?? "", raw);
        try
        {
            var end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end && pending.MoveNext())
            {
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
        var rig = Rig.Create(MpVersionName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(MpVersionName);
        var mark = LogMark();
        var view = FeatureRegistry.Find(ModInfo.Guid);
        var theirs = ModInfo.NetworkVersion + 1;
        var helloSent = false;
        var restored = false;
        try
        {
            var p = rig.P;
            if (!OnServerRules(c) || !c.Check(view != null && view.Value.IsActive, "the mod is not active"))
            {
                c.Report();
                yield break;
            }
            var feature = view.Value;
            var rpc = ZNet.instance.GetServerRPC();
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            if (!c.Check(rpc != null && rpc.IsConnected() && item != null, "no connection to the server, or could not equip a trinket"))
            {
                c.Report();
                yield break;
            }
            var hash = item.m_shared.m_fullAdrenalineSE.NameHash();
            rig.Effect(hash);
            var max = p.GetMaxAdrenaline();
            var reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(SrvSetStep, "allow=true", reply);
            if (!c.Check(reply.Answered && reply.Ok && reply.Detail.Contains("allow=True"), $"server set allow=true: {reply}"))
            {
                c.Report();
                yield break;
            }

            // (1) The server's copy answers with another network version: player's copy stands aside, the panel says
            // why, the bar is the normal game's.
            yield return SelfTest.CallServer(SrvVersionStep, "ack=" + theirs, reply);
            c.Check(reply.Answered && reply.Ok, $"server answer with network version {theirs}: {reply}");
            yield return Until(() => feature.State == nameof(ModState.ServerMismatch), 5f);
            var plugin = Self();
            c.Check(feature.State == nameof(ModState.ServerMismatch) && feature.Status.Contains("cannot talk to each other") && plugin != null && !plugin.IsActive,
                $"server with network version {theirs}: state {feature.State}, status '{feature.Status}' (expected inactive, 'they cannot talk to each other')");
            p.m_adrenaline = 0f;
            ConsoleAdrenaline(p, 999f);
            c.Check(Near(p.m_adrenaline, 0f) && rig.Has(hash), $"other version, a full bar: bar {F(p.m_adrenaline)}, effect {rig.Has(hash)} (the game fires at once)");
            rig.Effect(hash);
            p.m_adrenaline = 20f;
            p.m_adrenalineDegenTimer = 0f;
            yield return new WaitForSeconds(1.5f);
            c.Check(p.m_adrenaline < 20f - 0.3f && max > 20f, $"other version, bar 20 with the drain due: {F(p.m_adrenaline)} after 1.5 s (the game drains it)");
            var bar = p.m_adrenaline;
            rig.MarkCenter();
            yield return Press();
            c.Check(FullBar.LastResult == TriggerResult.None && p.m_adrenaline <= bar && !rig.Has(hash) && Rig.Center() == Rig.NoMessage,
                $"other version: the trigger did something (answer {FullBar.LastResult}, message '{Rig.Center()}')");
            c.Check(!TipOf(item).Contains(TooltipMark), "other version: the tooltip has the trigger line");

            // The server's real answer again: active, server's rules back.
            yield return SelfTest.CallServer(SrvVersionStep, "ack=" + ModInfo.NetworkVersion, reply);
            c.Check(reply.Answered && reply.Ok, $"server answer with its real network version: {reply}");
            yield return Until(() => feature.IsActive && ServerRules.UsingServer, 8f);
            if (!c.Check(feature.IsActive && ServerRules.UsingServer, $"same version again: state {feature.State}, status '{feature.Status}', server rules {ServerRules.UsingServer}"))
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSecondsRealtime(PlayerCheck.GraceSeconds + 1f); // server's check of the off / on is through

            // (2) This player's hello says another network version (what such a copy sends), then "on": the server
            // checks the player again after its grace. AllowPlayersWithoutMod: warned, stays.
            rpc.Invoke(HelloRpc, $"1|{theirs}|{ModInfo.Version}|off");
            rpc.Invoke(HelloStateRpc, "on");
            helloSent = true;
            yield return new WaitForSecondsRealtime(PlayerCheck.GraceSeconds + 1.5f);
            yield return SelfTest.CallServer(SrvVersionStep, "log=" + theirs, reply);
            c.Check(reply.Answered && reply.Ok && Number(reply.Detail, "letin") >= 1 && Number(reply.Detail, "refused") == 0,
                $"server log: '{reply.Detail}' (expected its Warning '<player> {VersionReason(theirs)}; AllowPlayersWithoutMod is on...')");
            yield return new WaitForSecondsRealtime(5f);
            c.Check(Joined() && feature.IsActive, $"other version with AllowPlayersWithoutMod on: the player did not stay (connection {ZNet.GetConnectionStatus()})");

            // Player's things back before the refusal: the game saves the character when it is thrown out.
            rig.Restore();
            restored = true;

            // (3) The server requires the mod again: refused about a second later, the server's log names both versions.
            var raw = new object[] { false, false, "" };
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
            yield return ServerCallFor(SrvVersionStep, "refuse=" + theirs, raw, 20f, Refused);
            yield return Until(() => Refused() || AtMainMenu(), 20f);
            Refused();
            var answered = raw[0] is bool a && a;
            var ok = raw[1] is bool o && o;
            var detail = raw[2] as string ?? "";
            c.Check(refusedAfter >= 0f && AtMainMenu(),
                $"other version, AllowPlayersWithoutMod off: the player was not refused (connection {ZNet.GetConnectionStatus()}, at the main menu {AtMainMenu()})");
            c.Check(refusedAfter >= 0.5f && refusedAfter <= 6f,
                $"refused {F(refusedAfter)} s after the server required the mod (expected about {F(PlayerCheck.GraceSeconds)} s)");
            c.Check(answered && ok && detail.StartsWith("Refused ", StringComparison.Ordinal) && detail.Contains(VersionReason(theirs)),
                $"the server's own line about the refusal: answered {answered}, '{detail}' (expected 'Refused <player>: {VersionReason(theirs)}...'; "
                + "no answer = it was lost while the game logged out: run again)");
            var problems = LogProblems(mark);
            c.Check(problems.Count == 0, $"{problems.Count} warning or error line(s) from the mod on the player's game during the test, first: {(problems.Count > 0 ? problems[0] : "")}");
        }
        finally
        {
            try
            {
                // Stopped half way and still in: the server must hear the real hello again (it answers with its real
                // version, so the player's copy is back too).
                var net = ZNet.instance;
                var rpc = net != null && !net.IsServer() ? net.GetServerRPC() : null;
                if (helloSent && rpc != null && rpc.IsConnected() && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected)
                {
                    rpc.Invoke(HelloRpc, $"1|{ModInfo.NetworkVersion}|{ModInfo.Version}|on");
                }
            }
            catch (Exception e)
            {
                Log.Warning($"Self test clean-up (hello) failed: {e}");
            }
            if (!restored)
            {
                rig.Restore();
            }
            else
            {
                ClearOverrides();
            }
        }
        c.Report();
    }
}
#endif
