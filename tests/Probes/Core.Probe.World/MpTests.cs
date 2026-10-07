#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MC.Core.ProbeShared;
using MC.Shared;
using UnityEngine;

namespace MC.Core.ProbeWorldMod;

// Probe own tests of a multiplayer run (always run first, filter never skip them):
//   probe.mp.baseline = joined the dedicated server, server probe answer hello with same scenario, every MC mod in the
//                       state the scenario want (modded: Both mods active on both sides; vanilla-server: Both mods
//                       ServerMissing, Client mods active; open-server: player without MC mods stay in);
//   probe.mp.refused  = vanilla-client: server refused the player without mods ("Incompatible version").
internal static class MpTests
{
    internal const string Baseline = "probe.mp.baseline";
    internal const string Refused = "probe.mp.refused";
    private const string T = ProbeDriver.SetupTest;

    // Vanilla-client setup outcome (WaitRefusal).
    private static ZNet.ConnectionStatus _status = ZNet.ConnectionStatus.None;
    private static float _refusedAfter = -1f;
    private static bool _spawnedFirst;
    private static bool _backAtMenu;

    internal const string ServerToggle = "probe.mp.server-toggle";
    internal const string OffPrefix = "probe.mp.off.";

    // Probe tests before the mods' tests.
    internal static List<KeyValuePair<string, Func<IEnumerator>>> ProbeTests()
    {
        var list = new List<KeyValuePair<string, Func<IEnumerator>>>();
        if (ProbeSettings.Scenario == MpProtocol.VanillaClient)
        {
            list.Add(new KeyValuePair<string, Func<IEnumerator>>(Refused, RunRefused));
            return list;
        }
        list.Add(new KeyValuePair<string, Func<IEnumerator>>(Baseline, RunBaseline));
        if (ProbeSettings.Scenario == MpProtocol.EachOff)
        {
            // One test per Both mod: turn it off on this client, see the server refuse (or not).
            foreach (var guid in BothMods())
            {
                var g = guid;
                list.Add(new KeyValuePair<string, Func<IEnumerator>>(OffPrefix + g, () => RunOff(g)));
            }
        }
        return list;
    }

    // Probe tests after the mods' tests (they toggle mods: nothing may run after them in that state).
    internal static List<KeyValuePair<string, Func<IEnumerator>>> LateTests()
    {
        var list = new List<KeyValuePair<string, Func<IEnumerator>>>();
        if (ProbeSettings.Scenario == MpProtocol.Modded)
        {
            list.Add(new KeyValuePair<string, Func<IEnumerator>>(ServerToggle, RunServerToggle));
        }
        return list;
    }

    private static List<string> BothMods() => FeatureRegistry.All()
        .Where(f => f.Side == MpProtocol.BothSide && !f.Guid.StartsWith("MC.Core.Probe", StringComparison.Ordinal))
        .Select(f => f.Guid).OrderBy(g => g, StringComparer.Ordinal).ToList();

    private static bool InGame => Player.m_localPlayer != null && ZNet.instance != null
                                  && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected;

    private static bool AtMenu => FejdStartup.instance != null && Player.m_localPlayer == null;

    // Tests that need the player in the server's world (all but the refusal check of a player without mods).
    internal static bool NeedsWorld(string test) => ProbeSettings.Scenario != MpProtocol.VanillaClient && test != Refused;

    // Before a test: player not in the server (refused by the test before, kicked, player object gone) -> log out when
    // still in the game scene, join again from the main menu, wait spawn + settle, link the probes again.
    internal static IEnumerator EnsureInGame(ProbeTimings timings)
    {
        if (InGame)
        {
            yield break;
        }
        SelfTest.Note("probe", $"not in the server (main menu {FejdStartup.instance != null}, status {ZNet.GetConnectionStatus()}, "
                               + $"player {Player.m_localPlayer != null}): joining again");
        if (FejdStartup.instance == null)
        {
            // Game scene without player or connection: leave like the menu's Log out button.
            var game = Game.instance;
            if (game != null && !game.m_shuttingDown)
            {
                game.Logout(save: false);
            }
            var end = Time.realtimeSinceStartup + 60f;
            while (FejdStartup.instance == null)
            {
                if (Time.realtimeSinceStartup > end)
                {
                    throw new TimeoutException("not back at the main menu 60 s after logging out");
                }
                yield return null;
            }
        }
        yield return MenuDriver.Rejoin(timings);
        yield return ProbeDriver.WaitSpawnAndSettle(timings);
        MpLink.Install();
    }

    // Modded: the server owner turns each Both mod off, then on again, while the player is in. The client's copy must
    // follow (inactive "the server has this mod turned off", then active again), live.
    private static IEnumerator RunServerToggle()
    {
        const string N = ServerToggle;
        var problems = new List<string>();
        var done = new List<string>();
        foreach (var guid in BothMods())
        {
            var view = FeatureRegistry.Find(guid);
            if (view == null || !view.Value.IsActive)
            {
                problems.Add($"{guid} not active on the client before the toggle ({view?.State})");
                continue;
            }
            var f = view.Value;
            var reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(MpProtocol.SetEnabledStep, guid + "=off", reply);
            if (!reply.Answered || !reply.Ok)
            {
                problems.Add($"{guid}: server could not turn it off ({reply})");
                continue;
            }
            var t0 = Time.realtimeSinceStartup;
            while (f.State != nameof(ModState.ServerMissing) && Time.realtimeSinceStartup - t0 < 10f)
            {
                yield return null;
            }
            var offAfter = Time.realtimeSinceStartup - t0;
            var offState = f.State;
            var offStatus = f.Status;
            reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(MpProtocol.SetEnabledStep, guid + "=on", reply);
            if (!reply.Answered || !reply.Ok)
            {
                problems.Add($"{guid}: server could not turn it back on ({reply})");
                continue;
            }
            t0 = Time.realtimeSinceStartup;
            while (!f.IsActive && Time.realtimeSinceStartup - t0 < 10f)
            {
                yield return null;
            }
            var onAfter = Time.realtimeSinceStartup - t0;
            if (offState != nameof(ModState.ServerMissing) || offStatus.IndexOf("turned off", StringComparison.OrdinalIgnoreCase) < 0)
            {
                problems.Add($"{guid}: server off -> client {offState} '{offStatus}' after {offAfter:F1} s (expected ServerMissing, 'turned off')");
            }
            else if (!f.IsActive)
            {
                problems.Add($"{guid}: server on again -> client still {f.State} after 10 s: {f.Status}");
            }
            else
            {
                done.Add($"{guid} off {offAfter:F1} s / on {onAfter:F1} s");
            }
            if (!InGame)
            {
                problems.Add($"lost the connection while toggling {guid}");
                break;
            }
        }
        SelfTest.Note(N, "followed: " + string.Join("; ", done));
        if (problems.Count == 0)
        {
            SelfTest.Pass(N, $"{done.Count} Both mod(s) followed the server live: inactive ('the server has this mod turned off') then active again");
        }
        else
        {
            foreach (var p in problems)
            {
                SelfTest.Note(N, "problem: " + p);
            }
            SelfTest.Fail(N, $"{problems.Count} problem(s), first: {problems[0]}");
        }
    }

    // Each-off: this player turns mod <guid> off while in. Refusing mods: the server must refuse within seconds
    // (live re-check, "Incompatible version"), and again when the player joins with it still off (hello "off").
    // Other Both mods (no refusal): player stays in. Mod back on at the end; next test join again.
    private static IEnumerator RunOff(string guid)
    {
        var n = OffPrefix + guid;
        var timings = new ProbeTimings { Start = Time.realtimeSinceStartup };
        yield return EnsureInGame(timings);
        var view = FeatureRegistry.Find(guid);
        if (view == null || view.Value.Enabled == null)
        {
            SelfTest.Fail(n, "mod not found on the client");
            yield break;
        }
        var f = view.Value;
        var refuses = ProbeSettings.Refusing.Contains(guid);
        var installOnly = ProbeSettings.InstallOnly.Contains(guid);
        var problems = new List<string>();
        try
        {
            if (!f.IsActive)
            {
                problems.Add($"not active before the test: {f.State} {f.Status}");
            }
            var t0 = Time.realtimeSinceStartup;
            f.Enabled.Value = false;
            if (refuses)
            {
                while (!AtMenu && Time.realtimeSinceStartup - t0 < 30f)
                {
                    yield return null;
                }
                var status = ZNet.GetConnectionStatus();
                var after = Time.realtimeSinceStartup - t0;
                if (!AtMenu || status != ZNet.ConnectionStatus.ErrorVersion)
                {
                    problems.Add($"turned off while in: at menu {AtMenu}, status {status} after {after:F1} s (expected refused, ErrorVersion)");
                }
                else
                {
                    SelfTest.Note(n, $"turned off while in: refused after {after:F1} s ({status})");
                    // Join again with the mod still off: hello says "off", server refuse again.
                    t0 = Time.realtimeSinceStartup;
                    yield return MenuDriver.Rejoin(timings);
                    var left = false;
                    while (Time.realtimeSinceStartup - t0 < 90f)
                    {
                        if (FejdStartup.instance == null)
                        {
                            left = true;
                        }
                        else if (left)
                        {
                            break;
                        }
                        yield return null;
                    }
                    status = ZNet.GetConnectionStatus();
                    after = Time.realtimeSinceStartup - t0;
                    if (!left || FejdStartup.instance == null || status != ZNet.ConnectionStatus.ErrorVersion)
                    {
                        problems.Add($"joined with it off: back at menu {left && FejdStartup.instance != null}, status {status} after {after:F1} s (expected refused, ErrorVersion)");
                    }
                    else
                    {
                        SelfTest.Note(n, $"joined with it off: refused {after:F1} s after the join ({status})");
                    }
                }
            }
            else
            {
                // No refusal: player stays in, mod off on this game only.
                yield return new WaitForSecondsRealtime(8f);
                if (!InGame)
                {
                    problems.Add($"turned off while in: lost the connection ({ZNet.GetConnectionStatus()}), but this mod does not refuse players");
                }
                else
                {
                    SelfTest.Note(n, installOnly
                        ? "turned off while in: still connected 8 s later (older player check: it refuses a player who does not have the mod, not one who turned it off)"
                        : "turned off while in: still connected 8 s later (this mod lets players without it in)");
                }
            }
        }
        finally
        {
            f.Enabled.Value = true;
        }
        if (problems.Count == 0)
        {
            SelfTest.Pass(n, refuses ? "refused when turned off while in and when joining with it off"
                : installOnly ? "turned off: player stayed in (older player check: only a player without the mod installed is refused)"
                : "turned off: player stayed in");
        }
        else
        {
            SelfTest.Fail(n, string.Join("; ", problems));
        }
    }

    // Vanilla-client setup: after join, wait until the game is back at the main menu (refused) or 120 s pass.
    internal static IEnumerator WaitRefusal(ProbeTimings timings)
    {
        var leftMenu = false;
        var end = Time.realtimeSinceStartup + 120f;
        while (Time.realtimeSinceStartup < end)
        {
            if (FejdStartup.instance == null)
            {
                leftMenu = true;
            }
            else if (leftMenu)
            {
                _backAtMenu = true;
                break;
            }
            if (Player.m_localPlayer != null && !_spawnedFirst)
            {
                _spawnedFirst = true;
                timings.Spawned = Time.realtimeSinceStartup;
                SelfTest.Note(T, $"local player spawned {timings.Spawned - timings.WorldRequested:F1} s after the join");
            }
            yield return null;
        }
        _status = ZNet.GetConnectionStatus();
        _refusedAfter = Time.realtimeSinceStartup - timings.WorldRequested;
        timings.Settled = Time.realtimeSinceStartup;
        SelfTest.Note(T, $"back at the main menu {_backAtMenu}, connection status {_status}, {_refusedAfter:F1} s after the join, "
                         + $"spawned first {_spawnedFirst}");
        // Main menu show the error popup: me wait so the screenshot of probe.mp.refused has it.
        yield return new WaitForSecondsRealtime(1f);
    }

    private static IEnumerator RunRefused()
    {
        const string N = Refused;
        SelfTest.Screenshot(N, "menu");
        yield return null;
        yield return null;
        var problems = new List<string>();
        if (!_backAtMenu)
        {
            problems.Add("the game never went back to the main menu within 120 s (not refused)");
        }
        if (_status != ZNet.ConnectionStatus.ErrorVersion)
        {
            problems.Add($"connection status {_status}, expected ErrorVersion (\"Incompatible version\")");
        }
        var popup = FejdStartup.instance != null && FejdStartup.instance.m_connectionFailedPanel != null
                    && FejdStartup.instance.m_connectionFailedPanel.activeInHierarchy;
        var text = popup ? FejdStartup.instance.m_connectionFailedError.text : "";
        SelfTest.Note(N, $"menu error panel shown {popup}, text '{text}'");
        if (problems.Count == 0)
        {
            SelfTest.Pass(N, $"refused after {_refusedAfter:F1} s with {_status} (spawned first {_spawnedFirst}); the server log names each mod that refused");
        }
        else
        {
            SelfTest.Fail(N, string.Join("; ", problems));
        }
    }

    private static IEnumerator RunBaseline()
    {
        const string N = Baseline;
        var problems = new List<string>();
        var net = ZNet.instance;
        if (net == null || net.IsServer() || ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected)
        {
            SelfTest.Fail(N, $"not connected as a client (status {ZNet.GetConnectionStatus()})");
            yield break;
        }
        MpLink.Install();
        yield return MpLink.Hello(20f);
        if (!MpLink.HaveAck)
        {
            SelfTest.Fail(N, "the server probe did not answer the hello within 20 s (server probe not loaded?)");
            yield break;
        }
        if (MpLink.ServerScenario != ProbeSettings.Scenario)
        {
            problems.Add($"server scenario '{MpLink.ServerScenario}', client '{ProbeSettings.Scenario}'");
        }

        var local = FeatureRegistry.All().Where(f => !f.Guid.StartsWith("MC.Core.Probe", StringComparison.Ordinal)).ToList();
        var server = MpLink.ServerMods;
        SelfTest.Note(N, $"client mods {local.Count}: {string.Join(", ", local.Select(f => $"{f.Guid} ({f.Side}) {f.State}"))}");
        SelfTest.Note(N, $"server mods {server.Count}: {string.Join(", ", server.Values.Select(m => $"{m.Guid} ({m.Side}) {m.State}"))}");

        switch (ProbeSettings.Scenario)
        {
            case MpProtocol.Modded:
            case MpProtocol.EachOff:
                foreach (var f in local)
                {
                    if (f.State != nameof(ModState.Active))
                    {
                        problems.Add($"{f.Guid} ({f.Side}) is {f.State} on the client: {f.Status}");
                    }
                    if (f.Side == MpProtocol.BothSide)
                    {
                        if (!server.TryGetValue(f.Guid, out var s))
                        {
                            problems.Add($"{f.Guid} (Both) missing on the server");
                        }
                        else if (s.State != nameof(ModState.Active))
                        {
                            problems.Add($"{f.Guid} is {s.State} on the server: {s.Status}");
                        }
                    }
                }
                foreach (var s in server.Values)
                {
                    if (s.Side == MpProtocol.BothSide && !local.Any(f => f.Guid == s.Guid))
                    {
                        problems.Add($"{s.Guid} (Both) runs on the server but not on the client");
                    }
                }
                break;
            case MpProtocol.VanillaServer:
                if (server.Count > 0)
                {
                    problems.Add($"the server runs MC mods ({string.Join(", ", server.Keys)}), expected none");
                }
                foreach (var f in local)
                {
                    var want = f.Side == MpProtocol.BothSide ? nameof(ModState.ServerMissing)
                        : f.Side == MpProtocol.ServerSide ? nameof(ModState.ServerOnly)
                        : nameof(ModState.Active);
                    if (f.State != want)
                    {
                        problems.Add($"{f.Guid} ({f.Side}) is {f.State} on the client, expected {want}: {f.Status}");
                    }
                }
                break;
            case MpProtocol.OpenServer:
                if (local.Count > 0)
                {
                    problems.Add($"the client runs MC mods ({string.Join(", ", local.Select(f => f.Guid))}), expected none");
                }
                foreach (var s in server.Values)
                {
                    if (s.State != nameof(ModState.Active))
                    {
                        problems.Add($"{s.Guid} is {s.State} on the server: {s.Status}");
                    }
                }
                // Refusal come about 1 s after PeerInfo (+ 4 s cut): still here 15 s later = allowed.
                yield return new WaitForSecondsRealtime(15f);
                if (ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected || Player.m_localPlayer == null)
                {
                    problems.Add($"the player without MC mods did not stay in (status {ZNet.GetConnectionStatus()})");
                }
                break;
            default:
                problems.Add($"unknown scenario '{ProbeSettings.Scenario}'");
                break;
        }

        SelfTest.Screenshot(N, "joined");
        yield return null;
        yield return null;
        if (problems.Count == 0)
        {
            SelfTest.Pass(N, $"joined {ProbeSettings.Join}, scenario '{ProbeSettings.Scenario}': client {local.Count} and server {server.Count} MC mods in the expected state");
        }
        else
        {
            foreach (var p in problems)
            {
                SelfTest.Note(N, "problem: " + p);
            }
            SelfTest.Fail(N, $"{problems.Count} problem(s), first: {problems[0]}");
        }
    }
}
#endif
