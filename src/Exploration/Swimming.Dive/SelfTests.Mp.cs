#if DEBUG
using System;
using System.Collections;
using System.Globalization;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SwimmingDiveMod;

// Debug build only. Multiplayer self tests (tools/Test-Multiplayer.ps1): one client joined to a real dedicated server.
// Scenario modded (server and client run me):
//   dive.mp.rules       server rules reach the player and win over the player's own settings; the server changes
//                       DiveSpeedMultiplier to 2 (player logs it, dives twice as fast), then back to 1 while the player is
//                       under water (M01, and the client half of M07)
//   dive.mp.diver       the server sees the diver's character go down and come back up (M03: what another player's game
//                       gets; pose and ripple need eyes)
//   dive.mp.server-off  the server turns me off while the player is under water: player lifted, view back, Crouch does
//                       nothing; on again: rules come again, dive works, nobody disconnected (M09)
// Scenario vanilla-server (server without me):
//   dive.mp.server-missing  I am inactive there, the player swims normally and Crouch does nothing (M05). Me inactive =
//                       OnDeactivated ran: this one test stays registered on purpose.
// Server halves: dive.mp.set-rules (server owner changes the Diving settings), dive.mp.watch (server reads the height of
// the player's character for some seconds). Turning me off and on on the server = the server probe's own step.
internal static partial class SelfTests
{
    private const string MpRulesName = "dive.mp.rules";
    private const string MpDiverName = "dive.mp.diver";
    private const string MpServerOffName = "dive.mp.server-off";
    private const string MpServerMissingName = "dive.mp.server-missing";
    private const string VanillaServerScenario = "vanilla-server";

    private const string SetRulesStep = "dive.mp.set-rules";
    private const string WatchStep = "dive.mp.watch";
    // Server probe's own step: "<guid>=on|off".
    private const string SetEnabledStep = "probe.set-enabled";
    private const string DefaultRulesArg = "1;0;1";

    private static void RegisterMultiplayer()
    {
        SelfTest.RegisterMultiplayer(MpRulesName, SelfTest.Modded, RunMpRules);
        SelfTest.RegisterMultiplayer(MpDiverName, SelfTest.Modded, RunMpDiver);
        SelfTest.RegisterMultiplayer(MpServerOffName, SelfTest.Modded, RunMpServerOff);
        SelfTest.RegisterMultiplayer(MpServerMissingName, VanillaServerScenario, RunMpServerMissing);
        SelfTest.RegisterServerStep(SetRulesStep, StepSetRules);
        SelfTest.RegisterServerStep(WatchStep, StepWatch);
    }

    private static void UnregisterMultiplayer()
    {
        SelfTest.UnregisterMultiplayer(MpRulesName);
        SelfTest.UnregisterMultiplayer(MpDiverName);
        SelfTest.UnregisterMultiplayer(MpServerOffName);
        // dive.mp.server-missing stays: it runs exactly when me is inactive (server without me), after this call.
        SelfTest.UnregisterServerStep(SetRulesStep);
        SelfTest.UnregisterServerStep(WatchStep);
    }

    // ---------- server halves ----------

    private static bool TryFloat(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    // Server owner sets the three Diving settings ("speed;idleRise;stamina"). Answer = the server's own rules line.
    private static IEnumerator StepSetRules(string arg, object[] reply)
    {
        var parts = (arg ?? "").Split(';');
        if (parts.Length != 3 || !TryFloat(parts[0], out var speed) || !TryFloat(parts[1], out var idle)
            || !TryFloat(parts[2], out var stamina))
        {
            SelfTest.Answer(reply, false, $"bad argument '{arg}' (wanted speed;idleRise;stamina)");
            yield break;
        }
        var net = ZNet.instance;
        // Only in a Test-Multiplayer run (own throwaway config folder): never a real settings file.
        if (!SelfTest.IsMultiplayerRun || net == null || !net.IsServer() || Plugin.DiveSpeedMultiplier == null
            || Plugin.IdleRiseSpeed == null || Plugin.UnderwaterStaminaMultiplier == null)
        {
            SelfTest.Answer(reply, false, "not the server of a multiplayer test run, or the Diving settings are not bound");
            yield break;
        }
        // The run's own throwaway config on the server: like the owner changing the settings.
        Plugin.DiveSpeedMultiplier.Value = speed;
        Plugin.IdleRiseSpeed.Value = idle;
        Plugin.UnderwaterStaminaMultiplier.Value = stamina;
        yield return null;
        SelfTest.Answer(reply, true, DiveRules.Own().Describe());
    }

    // Server reads the height of the (only) player's character for some seconds, from its own copy of the world.
    private static IEnumerator StepWatch(string arg, object[] reply)
    {
        if (!TryFloat(arg, out var seconds))
        {
            seconds = 3f;
        }
        seconds = Mathf.Clamp(seconds, 0.2f, 20f);
        var net = ZNet.instance;
        ZNetPeer peer = null;
        if (net != null && net.IsServer())
        {
            foreach (var p in net.GetPeers())
            {
                if (p != null && p.IsReady())
                {
                    peer = p;
                    break;
                }
            }
        }
        if (peer == null || ZDOMan.instance == null || ZoneSystem.instance == null)
        {
            SelfTest.Answer(reply, false, "no player connected to this server");
            yield break;
        }
        var min = float.MaxValue;
        var max = float.MinValue;
        var last = 0f;
        var n = 0;
        var end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            var zdo = !peer.m_characterID.IsNone() ? ZDOMan.instance.GetZDO(peer.m_characterID) : null;
            if (zdo != null && zdo.IsValid())
            {
                last = zdo.GetPosition().y;
                min = Mathf.Min(min, last);
                max = Mathf.Max(max, last);
                n++;
            }
            yield return null;
        }
        if (n == 0)
        {
            SelfTest.Answer(reply, false, $"the server never had the character of {peer.m_playerName} in {F(seconds)} s");
            yield break;
        }
        SelfTest.Answer(reply, true, "min=" + F(min) + ";max=" + F(max) + ";last=" + F(last) + ";level="
                                     + F(ZoneSystem.instance.m_waterLevel) + ";n=" + n.ToString(CultureInfo.InvariantCulture));
    }

    // ---------- client tools ----------

    // "key=value" out of a server answer like "min=1;max=2".
    private static bool Field(string detail, string key, out float value)
    {
        value = 0f;
        foreach (var part in (detail ?? "").Split(';'))
        {
            var eq = part.IndexOf('=');
            if (eq > 0 && part.Substring(0, eq) == key)
            {
                return TryFloat(part.Substring(eq + 1), out value);
            }
        }
        return false;
    }

    // Server step sent without waiting for the answer: clean-up from a finally, where a test cannot wait.
    private static void FireServer(string step, string arg)
    {
        try
        {
            var call = SelfTest.CallServer(step, arg, new SelfTest.ServerReply());
            if (call.MoveNext() && call.Current is IEnumerator inner)
            {
                inner.MoveNext();
                (inner as IDisposable)?.Dispose();
            }
            (call as IDisposable)?.Dispose();
        }
        catch (Exception)
        {
            // Connection gone: nothing to clean on the server from here.
        }
    }

    // Wait until the rules in force read like this line (the server's). Value = seconds waited.
    private static IEnumerator WaitRules(string describe, float timeout, Box result)
    {
        var t0 = Time.realtimeSinceStartup;
        while (ServerRules.Current.Describe() != describe && Time.realtimeSinceStartup - t0 < timeout)
        {
            yield return null;
        }
        result.Value = Time.realtimeSinceStartup - t0;
        result.Ok = ServerRules.Current.Describe() == describe;
    }

    private static bool ConnectedClient()
    {
        var net = ZNet.instance;
        return net != null && !net.IsServer() && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected;
    }

    private static string MyState()
    {
        var view = FeatureRegistry.Find(ModInfo.Guid);
        return view != null ? view.Value.State : "?";
    }

    private static string MyStatus()
    {
        var view = FeatureRegistry.Find(ModInfo.Guid);
        return view != null ? view.Value.Status : "?";
    }

    // ---------- dive.mp.rules ----------

    private static IEnumerator RunMpRules()
    {
        yield return Settle();
        var s = Begin(MpRulesName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        var tap = s.Tap;
        var serverChanged = false;
        try
        {
            // The real rules: nothing forced but the keys.
            ClearOverrides();
            CalmWind(rig);
            // Own settings are written below: only in a Test-Multiplayer run (own throwaway config folder).
            if (!c.Check(SelfTest.IsMultiplayerRun && ConnectedClient() && MyState() == nameof(ModState.Active)
                         && Plugin.DiveSpeedMultiplier != null && Plugin.IdleRiseSpeed != null
                         && Plugin.UnderwaterStaminaMultiplier != null,
                    $"player connected to a server in a multiplayer test run, {ModInfo.Name} active (connected "
                    + $"{ConnectedClient()}, state {MyState()})"))
            {
                c.Report();
                yield break;
            }
            var reply = new SelfTest.ServerReply();
            var wait = new Box();
            var box = new Box();

            // ---- the server's rules are in force (defaults first: an earlier cut-short test may have left others) ----
            serverChanged = true;
            yield return SelfTest.CallServer(SetRulesStep, DefaultRulesArg, reply);
            if (!c.Check(reply.Answered && reply.Ok, "server step: Diving settings back to the defaults: " + reply))
            {
                c.Report();
                yield break;
            }
            var defaults = reply.Detail;
            yield return WaitRules(defaults, 6f, wait);
            c.Check(wait.Ok && ServerRules.UsingServer && !ServerRules.IsPending,
                $"the player uses the server's rules: {ServerRules.Current.Describe()} (the server says: {defaults})");

            // ---- the player's own Diving settings are ignored while connected (this run's throwaway config) ----
            var ownSpeed = Plugin.DiveSpeedMultiplier.Value;
            var ownIdle = Plugin.IdleRiseSpeed.Value;
            var ownStamina = Plugin.UnderwaterStaminaMultiplier.Value;
            rig.Undo("own settings", () =>
            {
                Plugin.DiveSpeedMultiplier.Value = ownSpeed;
                Plugin.IdleRiseSpeed.Value = ownIdle;
                Plugin.UnderwaterStaminaMultiplier.Value = ownStamina;
            });
            Plugin.DiveSpeedMultiplier.Value = 3f;
            Plugin.IdleRiseSpeed.Value = 1f;
            Plugin.UnderwaterStaminaMultiplier.Value = 0f;
            yield return null;
            yield return null;
            c.Check(DiveRules.Own().Describe() != defaults && ServerRules.Current.Describe() == defaults,
                $"own settings changed to '{DiveRules.Own().Describe()}': still the server's rules in force "
                + $"('{ServerRules.Current.Describe()}')");

            // ---- every Diving setting of the server reaches the player ----
            yield return SelfTest.CallServer(SetRulesStep, "1.5;0.5;2", reply);
            if (c.Check(reply.Answered && reply.Ok, "server step: all three Diving settings changed: " + reply))
            {
                yield return WaitRules(reply.Detail, 6f, wait);
                var got = ServerRules.Current;
                c.Check(wait.Ok && Near(got.DiveSpeedMultiplier, 1.5f) && Near(got.IdleRiseSpeed, 0.5f)
                        && Near(got.UnderwaterStaminaMultiplier, 2f),
                    $"server settings 1.5 / 0.5 / 2: the player has them {F(wait.Value)} s later ({got.Describe()})");
            }

            // ---- the server sets DiveSpeedMultiplier 2: the player gets and logs it ----
            tap.Clear();
            yield return SelfTest.CallServer(SetRulesStep, "2;0;1", reply);
            if (!c.Check(reply.Answered && reply.Ok, "server step: DiveSpeedMultiplier 2: " + reply))
            {
                c.Report();
                yield break;
            }
            yield return WaitRules(reply.Detail, 6f, wait);
            c.Check(wait.Ok && wait.Value < 3f && Near(ServerRules.Current.DiveSpeedMultiplier, 2f),
                $"DiveSpeedMultiplier 2 on the server: the player has the new rules {F(wait.Value)} s after the server's answer "
                + $"({ServerRules.Current.Describe()})");
            c.Check(tap.Count("Using the server's rules: dive speed x2,") == 1,
                $"the player's log says \"Using the server's rules: dive speed x2, ...\" once "
                + $"({tap.Count("Using the server's rules: dive speed x2,")})");

            // ---- and dives twice as fast ----
            if (!DeepSpot(s, out var spot))
            {
                c.Report();
                yield break;
            }
            var ok = new Box();
            yield return EnterWater(s, spot, true, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            var dt = Time.fixedDeltaTime;
            var swim = DiveController.SwimSpeed(player);
            DiveInput.TestUp = false;
            DiveInput.TestDown = true;
            yield return Ticks(70);
            var vy = DiveState.Vy;
            var y0 = player.transform.position.y;
            yield return Ticks(12);
            var real = (y0 - player.transform.position.y) / (12 * dt);
            DiveInput.TestDown = false;
            c.Check(DiveState.Deep && Mathf.Abs(vy + 2f * swim) <= 0.06f * 2f * swim && Mathf.Abs(real - 2f * swim) <= 0.15f * 2f * swim,
                $"with the server's x2 the player goes down at {F(real)} m/s (own speed {F(vy)}): twice the swimming speed {F(swim)}, "
                + "not the x3 of the player's own setting");

            // ---- back to 1 on the server while the player is under water ----
            tap.Clear();
            yield return SelfTest.CallServer(SetRulesStep, DefaultRulesArg, reply);
            c.Check(reply.Answered && reply.Ok, "server step: DiveSpeedMultiplier back to 1: " + reply);
            yield return WaitRules(defaults, 6f, wait);
            serverChanged = !wait.Ok;
            c.Check(wait.Ok && wait.Value < 3f && Near(ServerRules.Current.DiveSpeedMultiplier, 1f),
                $"DiveSpeedMultiplier back to 1 on the server while connected: new rules {F(wait.Value)} s after the server's answer");
            c.Check(tap.Count("Using the server's rules: dive speed x1,") == 1,
                $"the player's log says \"Using the server's rules: dive speed x1, ...\" once "
                + $"({tap.Count("Using the server's rules: dive speed x1,")})");
            DiveInput.TestUp = true;
            yield return Ticks(125);
            vy = DiveState.Vy;
            c.Check(DiveState.Diving && Mathf.Abs(vy - swim) <= 0.06f * swim,
                $"the player dives at the normal speed again: Jump goes up at {F(vy)} m/s (swimming speed {F(swim)})");
            yield return Surface(player, 8f, box);
            c.Check(box.Ok && ConnectedClient(), "back at the surface, still connected: " + box.Detail);

            yield return GoHome(s);
            CheckLog(s);
        }
        finally
        {
            if (serverChanged)
            {
                FireServer(SetRulesStep, DefaultRulesArg);
            }
            rig.Restore();
        }
        c.Report();
    }

    // ---------- dive.mp.diver ----------

    private static IEnumerator RunMpDiver()
    {
        yield return Settle();
        var s = Begin(MpDiverName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        try
        {
            ClearOverrides();
            CalmWind(rig);
            if (!c.Check(ConnectedClient() && MyState() == nameof(ModState.Active),
                    $"player connected to a server, {ModInfo.Name} active (connected {ConnectedClient()}, state {MyState()})"))
            {
                c.Report();
                yield break;
            }
            var reply = new SelfTest.ServerReply();
            var wait = new Box();
            var box = new Box();
            yield return SelfTest.CallServer(SetRulesStep, DefaultRulesArg, reply);
            if (!c.Check(reply.Answered && reply.Ok, "server step: Diving settings on the defaults: " + reply))
            {
                c.Report();
                yield break;
            }
            yield return WaitRules(reply.Detail, 6f, wait);
            c.Check(wait.Ok, $"the player has the server's default rules ({ServerRules.Current.Describe()})");
            if (!DeepSpot(s, out var spot))
            {
                c.Report();
                yield break;
            }
            var ok = new Box();
            yield return EnterWater(s, spot, true, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            var swimHeight = ZoneSystem.instance.m_waterLevel - player.m_swimDepth;

            // ---- at the surface: the server knows where the swimmer is ----
            yield return SelfTest.CallServer(WatchStep, "1", reply);
            var have = Field(reply.Detail, "last", out var serverY);
            c.Check(reply.Answered && reply.Ok && have && Mathf.Abs(serverY - player.transform.position.y) < 1.5f,
                $"the server has the swimmer's character at height {F(serverY)} (the player's game: {F(player.transform.position.y)}): "
                + reply);

            // ---- Crouch held while the server watches for 3 s ----
            DiveInput.TestUp = false;
            DiveInput.TestDown = true;
            yield return SelfTest.CallServer(WatchStep, "3", reply);
            DiveInput.TestDown = false;
            var under = Under(player);
            have = Field(reply.Detail, "min", out var serverMin);
            c.Check(reply.Answered && reply.Ok && have && swimHeight - serverMin > 3f && DiveState.Deep,
                $"the server sees the diver's character go down to {F(swimHeight - serverMin)} m under the swimming height (the "
                + $"player's game: {F(under)} m): " + reply);

            // ---- Jump held while the server watches for 5 s ----
            DiveInput.TestUp = true;
            yield return SelfTest.CallServer(WatchStep, "5", reply);
            DiveInput.TestUp = false;
            have = Field(reply.Detail, "last", out serverY);
            c.Check(reply.Answered && reply.Ok && have && swimHeight - serverY < 1f && !DiveState.Diving,
                $"the server sees the diver's character back at the surface ({F(swimHeight - serverY)} m under the swimming "
                + $"height, dive over on the player's game {!DiveState.Diving}): " + reply);
            c.Check(ConnectedClient(), "still connected after the dive");
            yield return new WaitForSeconds(1f);

            yield return GoHome(s);
            CheckLog(s);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- dive.mp.server-off ----------

    private static IEnumerator RunMpServerOff()
    {
        yield return Settle();
        var s = Begin(MpServerOffName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        var tap = s.Tap;
        var serverOff = false;
        try
        {
            ClearOverrides();
            CalmWind(rig);
            // Me go off and on in this test: the view override must stay.
            KeepOverrides = true;
            rig.Undo("keep overrides", () => KeepOverrides = false);
            Visuals.TestAllOn = true;
            if (!c.Check(ConnectedClient() && MyState() == nameof(ModState.Active),
                    $"player connected to a server, {ModInfo.Name} active (connected {ConnectedClient()}, state {MyState()})"))
            {
                c.Report();
                yield break;
            }
            var reply = new SelfTest.ServerReply();
            var box = new Box();
            var cam = GameCamera.instance;
            if (!DeepSpot(s, out var spot))
            {
                c.Report();
                yield break;
            }
            var ok = new Box();
            yield return EnterWater(s, spot, true, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            yield return DiveTo(player, 3f, 6f, box);
            c.Check(box.Ok, "dive to 3 m with the server's rules: " + box.Detail);
            yield return Ticks(65);
            yield return ViewSettled();
            c.Check(DiveState.Diving && DiveState.Deep && UnderwaterView.Active && UnderwaterView.FogApplied
                    && UnderwaterView.FlippedCount > 0 && DiveCamera.Lifted,
                "under water with camera, tint and turned surfaces before the server turns the mod off");

            // ---- the server owner turns the mod off ----
            tap.Clear();
            serverOff = true;
            yield return SelfTest.CallServer(SetEnabledStep, ModInfo.Guid + "=off", reply);
            if (!c.Check(reply.Answered && reply.Ok, "server step: mod turned off on the server: " + reply))
            {
                c.Report();
                yield break;
            }
            var t0 = Time.realtimeSinceStartup;
            while (MyState() != nameof(ModState.ServerMissing) && Time.realtimeSinceStartup - t0 < 10f)
            {
                yield return null;
            }
            c.Check(MyState() == nameof(ModState.ServerMissing) && MyStatus().IndexOf("turned off", StringComparison.OrdinalIgnoreCase) >= 0,
                $"the player's {ModInfo.Name} goes inactive {F(Time.realtimeSinceStartup - t0)} s later: {MyState()}, \"{MyStatus()}\"");
            c.Check(!DiveState.Diving && player.m_body.useGravity && !UnderwaterView.Active && !UnderwaterView.FogApplied
                    && UnderwaterView.FlippedCount == 0 && TurnedSurfaces() == 0 && !DiveCamera.Lifted,
                "server off while under water: the dive stops, tint and surfaces are back at once");
            var lift = Time.time;
            while (Under(player) > 0.4f && Time.time - lift < 4f)
            {
                yield return FixedTick;
            }
            c.Check(Under(player) <= 0.4f, $"the normal game lifts the swimmer to the surface in {F(Time.time - lift)} s");
            yield return ViewSettled(2);
            var camPos = cam.transform.position;
            var surface = Floating.GetLiquidLevel(camPos, 1f, LiquidType.Water);
            // Crouch (the game's own crouch input too) does nothing now.
            DiveInput.TestDown = true;
            rig.Drive(Vector3.zero, true, false, false);
            var maxUnder = 0f;
            for (var i = 0; i < 100; i++)
            {
                yield return FixedTick;
                rig.Drive(Vector3.zero);
                maxUnder = Mathf.Max(maxUnder, Under(player));
            }
            DiveInput.TestDown = false;
            c.Check(camPos.y > surface && maxUnder < 0.45f && !DiveState.Diving && !UnderwaterView.Active && ConnectedClient(),
                $"while the server has the mod off: camera above the water, Crouch held 2 s does nothing (feet at most "
                + $"{F(maxUnder)} m under the rest height), still connected");

            // ---- on again ----
            tap.Clear();
            yield return SelfTest.CallServer(SetEnabledStep, ModInfo.Guid + "=on", reply);
            serverOff = !(reply.Answered && reply.Ok);
            c.Check(reply.Answered && reply.Ok, "server step: mod turned on again on the server: " + reply);
            t0 = Time.realtimeSinceStartup;
            while ((MyState() != nameof(ModState.Active) || ServerRules.IsPending) && Time.realtimeSinceStartup - t0 < 12f)
            {
                yield return null;
            }
            c.Check(MyState() == nameof(ModState.Active) && !ServerRules.IsPending && ServerRules.UsingServer,
                $"server on again: the player's {ModInfo.Name} is active with the server's rules {F(Time.realtimeSinceStartup - t0)} s "
                + $"later ({MyState()}, {ServerRules.Current.Describe()})");
            c.Check(tap.Count("Using the server's rules:") >= 1, "the player's log says \"Using the server's rules: ...\" again");
            Visuals.TestAllOn = true;
            yield return Ticks(5);
            yield return DiveTo(player, 1.5f, 5f, box);
            c.Check(box.Ok, "server on again: Crouch dives again: " + box.Detail);
            yield return Surface(player, 6f, box);
            c.Check(ConnectedClient() && ReferenceEquals(Player.m_localPlayer, player), "nobody was disconnected");
            yield return new WaitForSeconds(1f);

            yield return GoHome(s);
            CheckLog(s);
        }
        finally
        {
            if (serverOff)
            {
                FireServer(SetEnabledStep, ModInfo.Guid + "=on");
            }
            rig.Restore();
        }
        c.Report();
    }

    // ---------- dive.mp.server-missing (scenario vanilla-server) ----------

    private static IEnumerator RunMpServerMissing()
    {
        yield return Settle();
        var s = Begin(MpServerMissingName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        try
        {
            ClearOverrides();
            CalmWind(rig);
            if (!c.Check(ConnectedClient() && MyState() == nameof(ModState.ServerMissing)
                         && MyStatus().IndexOf("server does not have this mod", StringComparison.OrdinalIgnoreCase) >= 0,
                    $"joined a server without the mod: {ModInfo.Name} is inactive because of that (connected {ConnectedClient()}, "
                    + $"state {MyState()}, \"{MyStatus()}\")"))
            {
                c.Report();
                yield break;
            }
            if (!DeepSpot(s, out var spot))
            {
                c.Report();
                yield break;
            }
            var ok = new Box();
            yield return EnterWater(s, spot, true, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            var cam = GameCamera.instance;

            // ---- swimming is the normal game's ----
            var from = Flat(player.transform.position);
            for (var i = 0; i < 75; i++)
            {
                rig.Drive(Vector3.forward);
                yield return FixedTick;
            }
            rig.Drive(Vector3.zero);
            var swam = Vector3.Distance(from, Flat(player.transform.position));
            c.Check(swam > 1f, $"W swims forward as in the normal game ({F(swam)} m in 1.5 s)");
            yield return new WaitForSeconds(1.5f);

            // ---- Crouch does nothing (the game's own crouch input, and the mod's test key) ----
            DiveInput.TestDown = true;
            rig.Drive(Vector3.zero, true, false, false);
            var maxUnder = 0f;
            var dived = false;
            for (var i = 0; i < 125; i++)
            {
                yield return FixedTick;
                rig.Drive(Vector3.zero);
                maxUnder = Mathf.Max(maxUnder, Under(player));
                dived |= DiveState.Diving;
            }
            DiveInput.TestDown = false;
            yield return ViewSettled();
            var camPos = cam.transform.position;
            var surface = Floating.GetLiquidLevel(camPos, 1f, LiquidType.Water);
            c.Check(!dived && maxUnder < 0.45f && camPos.y > surface && !UnderwaterView.Active && !UnderwaterView.FogApplied
                    && UnderwaterView.FlippedCount == 0 && !DiveCamera.Lifted,
                $"Crouch held 2.5 s in deep water: nothing (feet at most {F(maxUnder)} m under the rest height, camera "
                + $"{F(camPos.y - surface)} m above the water, no tint)");
            c.Check(ConnectedClient() && MyState() == nameof(ModState.ServerMissing), "still connected, still inactive");

            yield return GoHome(s);
            CheckLog(s);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }
}
#endif
