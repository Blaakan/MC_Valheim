#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SleepThroughDayMod;

// Debug build only. Multiplayer self tests (tools/Test-Multiplayer.ps1): ONE game joined to a real dedicated server,
// both with the mod. Client test drive its own player (bed, lying down, messages, own clock) with the same rig as
// single player; everything the server own (world time, settings, who is in bed, the sleep itself, its log) go
// through server steps below (SelfTest.CallServer). No second player exist: a player who is up is stood in for by
// the server's "is everybody in bed?" answer, a player without the mod by this game with the mod turned off.
// Config files of a multiplayer run are throwaway copies: here (and only here) tests may write real settings
// (Enabled of this game, WakeUpHour / IncludeAfternoon of the server).
internal static partial class SleepSelfTests
{
    private const string MpDay = "sleep.mp.day";
    private const string MpSettings = "sleep.mp.settings";
    private const string MpDedicated = "sleep.mp.dedicated";
    private const string MpMessage = "sleep.mp.message";
    private const string MpServerOff = "sleep.mp.server-off";
    private const string MpClientOff = "sleep.mp.client-off";
    private const string MpHandoff = "sleep.mp.handoff";
    private const string MpVanillaServer = "sleep.mp.vanilla-server";
    private const string VanillaServerScenario = "vanilla-server"; // Test-Multiplayer scenario: server without MC mods

    // Server halves.
    private const string StepReset = "sleep.mp.server-reset";   // stop sleep, settings and time as before the tests
    private const string StepMark = "sleep.mp.server-mark";     // counters at zero (before a sleep can start)
    private const string StepSet = "sleep.mp.server-set";       // wake=|afternoon= (memory), cfgwake=|cfgafternoon= (config)
    private const string StepTime = "sleep.mp.server-time";     // hour= : world time to that hour of current date
    private const string StepBlock = "sleep.mp.server-block";   // 1 = "everybody in bed?" answer no (a player is up)
    private const string StepWatch = "sleep.mp.server-watch";   // seconds: did a sleep start meanwhile?
    private const string StepState = "sleep.mp.server-state";   // sleep, skip target, Info lines since mark
    private const string StepFast = "sleep.mp.server-fast";     // seconds: rest of running skip take this long
    private const string StepFacts = "sleep.mp.server-facts";   // dedicated? version, mod state, what its log file say
    private const string ProbeSetEnabled = "probe.set-enabled"; // server probe's own step: "<guid>=on|off"

    private const float MpFastSeconds = 3f;

    private static readonly string[] MpTests = { MpDay, MpSettings, MpDedicated, MpMessage, MpServerOff, MpClientOff, MpHandoff };
    private static readonly string[] ServerSteps = { StepReset, StepMark, StepSet, StepTime, StepBlock, StepWatch, StepState, StepFast, StepFacts };

    // Server side of the rig (statics of the server process).
    private static Harmony _srvPatches;
    private static LogWatch _srvWatch;
    private static int _srvMark;
    private static int _srvTicks;
    private static bool _srvWasSleeping;
    private static int _srvStarts;
    private static double _srvStartTime;
    private static double _srvStartSkipTo;
    private static bool _srvStartSkipping;
    private static double _srvStartPlanned;
    private static double? _srvOldTime;
    private static double _srvOldLastSleep;
    private static string _srvCfgWake;      // server config values before a test wrote them
    private static string _srvCfgAfternoon;

    private static void RegisterMultiplayer()
    {
        SelfTest.RegisterMultiplayer(MpDay, SelfTest.Modded, RunMpDay);
        SelfTest.RegisterMultiplayer(MpSettings, SelfTest.Modded, RunMpSettings);
        SelfTest.RegisterMultiplayer(MpDedicated, SelfTest.Modded, RunMpDedicated);
        SelfTest.RegisterMultiplayer(MpMessage, SelfTest.Modded, RunMpMessage);
        SelfTest.RegisterMultiplayer(MpServerOff, SelfTest.Modded, RunMpServerOff);
        SelfTest.RegisterMultiplayer(MpClientOff, SelfTest.Modded, RunMpClientOff);
        SelfTest.RegisterMultiplayer(MpHandoff, SelfTest.Modded, RunMpHandoff);
        SelfTest.RegisterMultiplayer(MpVanillaServer, VanillaServerScenario, RunMpVanillaServer);

        SelfTest.RegisterServerStep(StepReset, ServerReset);
        SelfTest.RegisterServerStep(StepMark, ServerMark);
        SelfTest.RegisterServerStep(StepSet, ServerSet);
        SelfTest.RegisterServerStep(StepTime, ServerTime);
        SelfTest.RegisterServerStep(StepBlock, ServerBlock);
        SelfTest.RegisterServerStep(StepWatch, ServerWatch);
        SelfTest.RegisterServerStep(StepState, ServerState);
        SelfTest.RegisterServerStep(StepFast, ServerFast);
        SelfTest.RegisterServerStep(StepFacts, ServerFacts);
    }

    private static void UnregisterMultiplayer()
    {
        foreach (var name in MpTests)
        {
            SelfTest.UnregisterMultiplayer(name);
        }
        // Test of "server has no mod" check the OFF state: it must stay in list when the mod go off on a joined game.
        var joined = ZNet.instance != null && !ZNet.instance.IsServer();
        if (!joined)
        {
            SelfTest.UnregisterMultiplayer(MpVanillaServer);
        }
        foreach (var step in ServerSteps)
        {
            SelfTest.UnregisterServerStep(step);
        }
        ServerRigOff();
    }

    // ---------- server halves ----------

    // Step that need no wait: do the work, answer. Throw = answer "not ok" with the reason.
    private static IEnumerator Step(object[] reply, Func<string> work)
    {
        string detail;
        bool ok;
        try
        {
            detail = work();
            ok = true;
        }
        catch (Exception e)
        {
            detail = $"{e.GetType().Name}: {e.Message}";
            ok = false;
        }
        SelfTest.Answer(reply, ok, detail);
        yield break;
    }

    private static void RequireServer(out ZNet net, out EnvMan env, out Game game)
    {
        net = ZNet.instance;
        env = EnvMan.instance;
        game = Game.instance;
        if (net == null || env == null || game == null || ZRoutedRpc.instance == null || !net.IsServer())
        {
            throw new InvalidOperationException("no server world here");
        }
    }

    // Counter of server ticks and of "everybody in bed?" questions, forced answer, log listener.
    private static void ServerRigOn()
    {
        if (_srvPatches == null)
        {
            var self = typeof(SleepSelfTests);
            var h = new Harmony(ModInfo.Guid + ".selftest.server");
            try
            {
                h.Patch(Find(typeof(Game), nameof(Game.EverybodyIsTryingToSleep)), postfix: new HarmonyMethod(self, nameof(Everybody)));
                // Last: after the mod's own postfix moved the end of the skip.
                h.Patch(Find(typeof(Game), nameof(Game.UpdateSleeping)),
                    postfix: new HarmonyMethod(self, nameof(ServerTick)) { priority = Priority.Last });
            }
            catch
            {
                h.UnpatchSelf();
                throw;
            }
            _srvPatches = h;
            _srvWasSleeping = Game.instance != null && Game.instance.m_sleeping;
        }
        if (_srvWatch == null)
        {
            _srvWatch = new LogWatch();
            BepInEx.Logging.Logger.Listeners.Add(_srvWatch);
            _srvMark = 0;
        }
    }

    // Mod going off on the server (or reset): stand-ins and forced settings gone. World time stay (reset step put
    // that back): a test may turn the server's mod off and on in the middle of a morning.
    private static void ServerRigOff()
    {
        _everybodyForced = null;
        try
        {
            _srvPatches?.UnpatchSelf();
        }
        catch (Exception e)
        {
            Log.Error($"{SelfTest.Prefix} server rig unpatch failed: {e}");
        }
        _srvPatches = null;
        var watch = _srvWatch;
        _srvWatch = null;
        if (watch != null)
        {
            BepInEx.Logging.Logger.Listeners.Remove(watch);
        }
    }

    private static void ServerTick(Game __instance)
    {
        try
        {
            _srvTicks++;
            var sleeping = __instance.m_sleeping;
            if (sleeping && !_srvWasSleeping)
            {
                var env = EnvMan.instance;
                var net = ZNet.instance;
                _srvStarts++;
                _srvStartTime = net.GetTimeSeconds();
                _srvStartSkipping = env.IsTimeSkipping();
                _srvStartSkipTo = _srvStartSkipping ? env.m_skipToTime : 0.0;
                _srvStartPlanned = _srvStartSkipping && env.m_timeSkipSpeed > 0.0 ? (_srvStartSkipTo - _srvStartTime) / env.m_timeSkipSpeed : 0.0;
            }
            _srvWasSleeping = sleeping;
        }
        catch (Exception e)
        {
            Log.Error($"{SelfTest.Prefix} server tick recorder failed: {e}");
        }
    }

    private static void ServerMarkNow()
    {
        ServerRigOn();
        _srvMark = _srvWatch.Mark();
        _srvStarts = 0;
        _srvStartSkipping = false;
        _srvStartSkipTo = 0.0;
        _srvStartTime = 0.0;
        _srvStartPlanned = 0.0;
    }

    private static void ServerConfigBack()
    {
        if (_srvCfgWake != null)
        {
            Plugin.WakeUpHour.SetSerializedValue(_srvCfgWake);
            _srvCfgWake = null;
        }
        if (_srvCfgAfternoon != null)
        {
            Plugin.IncludeAfternoon.SetSerializedValue(_srvCfgAfternoon);
            _srvCfgAfternoon = null;
        }
    }

    private static IEnumerator ServerReset(string arg, object[] reply) => Step(reply, () =>
    {
        RequireServer(out var net, out var env, out var game);
        _everybodyForced = null;
        ClearOverrides();
        ServerConfigBack();
        if (env.IsTimeSkipping())
        {
            env.m_skipTime = false;
        }
        if (game.m_sleeping)
        {
            game.m_sleeping = false;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "SleepStop");
        }
        var restored = false;
        if (_srvOldTime.HasValue)
        {
            SetTime(env, net, _srvOldTime.Value);
            game.m_lastSleepTime = _srvOldLastSleep;
            net.SendNetTime();
            _srvOldTime = null;
            restored = true;
        }
        ServerMarkNow();
        return $"time={F(net.GetTimeSeconds())};restored={(restored ? 1 : 0)}";
    });

    private static IEnumerator ServerMark(string arg, object[] reply) => Step(reply, () =>
    {
        RequireServer(out _, out _, out _);
        ServerMarkNow();
        return "marked=1";
    });

    // Every call say all four: wake / afternoon forced in memory ("none" = as config say), cfgwake / cfgafternoon
    // written to the server's (throwaway) config the way the file loader do ("" = value from before the tests).
    private static IEnumerator ServerSet(string arg, object[] reply) => Step(reply, () =>
    {
        RequireServer(out _, out _, out _);
        var a = Args(arg);
        WakeHourOverride = a.TryGetValue("wake", out var wake) && wake != "none" ? (float)Num(a, "wake") : (float?)null;
        IncludeAfternoonOverride = a.TryGetValue("afternoon", out var afternoon) && afternoon != "none" ? afternoon == "1" : (bool?)null;
        WakeUpMessageOverride = null;
        a.TryGetValue("cfgwake", out var cfgWake);
        a.TryGetValue("cfgafternoon", out var cfgAfternoon);
        if (string.IsNullOrEmpty(cfgWake) && string.IsNullOrEmpty(cfgAfternoon))
        {
            ServerConfigBack();
        }
        else
        {
            _srvCfgWake ??= Plugin.WakeUpHour.GetSerializedValue();
            _srvCfgAfternoon ??= Plugin.IncludeAfternoon.GetSerializedValue();
            Plugin.WakeUpHour.SetSerializedValue(string.IsNullOrEmpty(cfgWake) ? _srvCfgWake : cfgWake);
            Plugin.IncludeAfternoon.SetSerializedValue(string.IsNullOrEmpty(cfgAfternoon) ? _srvCfgAfternoon : cfgAfternoon);
        }
        Plugin.ReadDaySettings(out var wakeHour, out var includeAfternoon);
        return $"wake={F(wakeHour)};afternoon={(includeAfternoon ? 1 : 0)};cfgwake={F(Plugin.WakeUpHour.Value)};"
               + $"cfgafternoon={(Plugin.IncludeAfternoon.Value ? 1 : 0)}";
    });

    private static IEnumerator ServerTime(string arg, object[] reply) => Step(reply, () =>
    {
        RequireServer(out var net, out var env, out var game);
        var a = Args(arg);
        if (!_srvOldTime.HasValue)
        {
            _srvOldTime = net.GetTimeSeconds();
            _srvOldLastSleep = game.m_lastSleepTime;
        }
        var len = (double)env.m_dayLengthSec;
        var day = env.GetDay(net.GetTimeSeconds());
        var t = day * len + DayClock.RawFractionForHour(env, (float)Num(a, "hour")) * len;
        SetTime(env, net, t);
        game.m_lastSleepTime = t - 1000.0;
        net.SendNetTime(); // players get new time now, not at next 2 s send
        return $"time={F(t)};day={day};len={F(len)}";
    });

    private static IEnumerator ServerBlock(string arg, object[] reply) => Step(reply, () =>
    {
        RequireServer(out _, out _, out _);
        ServerRigOn();
        _everybodyForced = arg == "1" ? false : (bool?)null;
        return $"blocked={(arg == "1" ? 1 : 0)}";
    });

    private static IEnumerator ServerWatch(string arg, object[] reply)
    {
        var net = ZNet.instance;
        var env = EnvMan.instance;
        var game = Game.instance;
        string problem = null;
        try
        {
            RequireServer(out net, out env, out game);
            ServerRigOn();
        }
        catch (Exception e)
        {
            problem = $"{e.GetType().Name}: {e.Message}";
        }
        if (problem != null)
        {
            SelfTest.Answer(reply, false, problem);
            yield break;
        }
        var seconds = Mathf.Clamp((float)Num(Args("s=" + arg), "s", 5.0), 1f, 20f);
        var ticks = _srvTicks;
        var asked = _everybodyAsked;
        var t0 = net.GetTimeSeconds();
        var started = false;
        var until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until)
        {
            started = started || game.m_sleeping || env.IsTimeSkipping();
            yield return null;
        }
        InBed(net, out var inBed, out var players);
        SelfTest.Answer(reply, true, $"started={(started ? 1 : 0)};ticks={_srvTicks - ticks};asked={_everybodyAsked - asked};inbed={inBed};"
                                     + $"players={players};moved={F(net.GetTimeSeconds() - t0)}");
    }

    private static IEnumerator ServerState(string arg, object[] reply) => Step(reply, () =>
    {
        RequireServer(out var net, out var env, out var game);
        ServerRigOn();
        InBed(net, out var inBed, out var players);
        return $"sleeping={(game.m_sleeping ? 1 : 0)};skipping={(env.IsTimeSkipping() ? 1 : 0)};time={F(net.GetTimeSeconds())};"
               + $"day={env.GetDay(net.GetTimeSeconds())};inbed={inBed};players={players};sleeps={_srvStarts};"
               + $"startTime={F(_srvStartTime)};startSkipTo={F(_srvStartSkipTo)};startSkipping={(_srvStartSkipping ? 1 : 0)};"
               + $"planned={F(_srvStartPlanned)};starts={_srvWatch.Count(_srvMark, StartLine)};retargets={_srvWatch.Count(_srvMark, RetargetLine)};"
               + $"startLine={Wire(_srvWatch.First(_srvMark, StartLine))};retargetLine={Wire(_srvWatch.First(_srvMark, RetargetLine))}";
    });

    private static IEnumerator ServerFast(string arg, object[] reply) => Step(reply, () =>
    {
        RequireServer(out var net, out var env, out _);
        var seconds = Mathf.Clamp((float)Num(Args("s=" + arg), "s", MpFastSeconds), 1f, 12f);
        var left = env.m_skipToTime - net.GetTimeSeconds();
        // Skip move one 0.02 s step per server frame, 50 a second at most (EnvMan.FixedUpdate): server under 50
        // frames a second need more speed for the same real time.
        var smooth = Time.smoothDeltaTime;
        var rate = Mathf.Clamp(smooth > 0f ? 1f / smooth : 50f, 5f, 1f / Time.fixedDeltaTime);
        if (env.IsTimeSkipping() && left > 0.0)
        {
            env.m_timeSkipSpeed = Math.Max(env.m_timeSkipSpeed, left / (seconds * rate * Time.fixedDeltaTime));
        }
        return $"skipping={(env.IsTimeSkipping() ? 1 : 0)};skipTo={F(env.m_skipToTime)};rate={F(rate)}";
    });

    private static IEnumerator ServerFacts(string arg, object[] reply) => Step(reply, () =>
    {
        RequireServer(out var net, out _, out _);
        var feature = FeatureRegistry.Find(ModInfo.Guid);
        var ready = false;
        var activated = false;
        var warnings = 0;
        var errors = 0;
        var first = "";
        // Server's own BepInEx log file, as a person would read it. BepInEx line: "[Level  :Source name] text".
        var source = ":" + ModInfo.Name.PadLeft(10) + "]";
        var path = Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log");
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(file))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.IndexOf(source, StringComparison.Ordinal) < 0)
                {
                    continue;
                }
                if (line.IndexOf(Log.ReadyMarker + " " + ModInfo.Guid + " ", StringComparison.Ordinal) >= 0)
                {
                    ready = true;
                }
                if (line.IndexOf(source + " Activated.", StringComparison.Ordinal) >= 0)
                {
                    activated = true;
                }
                if (line.IndexOf(SelfTest.Prefix, StringComparison.Ordinal) >= 0)
                {
                    continue;
                }
                var warning = line.StartsWith("[Warning", StringComparison.Ordinal);
                var error = line.StartsWith("[Error", StringComparison.Ordinal) || line.StartsWith("[Fatal", StringComparison.Ordinal);
                if (warning)
                {
                    warnings++;
                }
                if (error)
                {
                    errors++;
                }
                if ((warning || error) && first.Length == 0)
                {
                    first = line;
                }
            }
        }
        var version = global::Version.CurrentVersion.ToString();
        return $"dedicated={(net.IsDedicated() ? 1 : 0)};version={version};state={feature?.State};ready={(ready ? 1 : 0)};"
               + $"activated={(activated ? 1 : 0)};warnings={warnings};errors={errors};first={Wire(first)}";
    });

    private static void InBed(ZNet net, out int inBed, out int players)
    {
        inBed = 0;
        players = 0;
        foreach (var zdo in net.GetAllCharacterZDOS())
        {
            players++;
            if (zdo.GetBool(ZDOVars.s_inBed))
            {
                inBed++;
            }
        }
    }

    // ---------- "key=value;key=value" on the wire ----------

    private static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    // Free text as one value: no separators inside.
    private static string Wire(string text) => (text ?? "").Replace(';', ',').Replace('\n', ' ').Replace('\r', ' ');

    private static Dictionary<string, string> Args(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in (text ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq > 0)
            {
                result[part.Substring(0, eq)] = part.Substring(eq + 1);
            }
        }
        return result;
    }

    private static double Num(Dictionary<string, string> a, string key, double fallback = 0.0) =>
        a.TryGetValue(key, out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static bool Flag(Dictionary<string, string> a, string key) => a.TryGetValue(key, out var text) && text == "1";

    private static string Text(Dictionary<string, string> a, string key) => a.TryGetValue(key, out var text) && text.Length > 0 ? text : null;

    // ---------- client side of the rig ----------

    // Run a server step, keep its answer in c.Answer. No answer or "not ok" = FAIL, run cannot go on.
    private static IEnumerator Server(Case c, string step, string arg)
    {
        var reply = new SelfTest.ServerReply();
        yield return SelfTest.CallServer(step, arg, reply);
        var ok = reply.Answered && reply.Ok;
        c.Answer = Args(ok ? reply.Detail : "");
        if (!ok)
        {
            Fail(c, $"server step {step}(\"{arg}\"): {reply}");
            c.OK = false;
        }
    }

    // From a `finally` (no yield there): send the step, wait for nothing. SelfTest.CallServer hand out the probe's
    // call coroutine; its first move send the rpc.
    private static void FireServer(string step, string arg)
    {
        try
        {
            var outer = SelfTest.CallServer(step, arg, new SelfTest.ServerReply());
            if (outer.MoveNext() && outer.Current is IEnumerator inner)
            {
                inner.MoveNext();
                (inner as IDisposable)?.Dispose();
            }
            (outer as IDisposable)?.Dispose();
        }
        catch (Exception e)
        {
            Log.Info($"{SelfTest.Prefix} NOTE sleep.mp: could not send server step {step} from clean-up: {e.Message}");
        }
    }

    // This game's own Enabled setting, as MC Mods panel write it. Multiplayer run only (throwaway config).
    private static void SetEnabledHere(bool on)
    {
        if (!SelfTest.IsMultiplayerRun)
        {
            return;
        }
        var feature = FeatureRegistry.Find(ModInfo.Guid);
        if (feature != null && feature.Value.Enabled != null && feature.Value.Enabled.Value != on)
        {
            feature.Value.Enabled.Value = on;
        }
    }

    private static bool ActiveHere()
    {
        var feature = FeatureRegistry.Find(ModInfo.Guid);
        return feature != null && feature.Value.IsActive;
    }

    private static IEnumerator WaitActiveHere(bool active, float seconds)
    {
        var until = Time.realtimeSinceStartup + seconds;
        while (ActiveHere() != active && Time.realtimeSinceStartup < until)
        {
            yield return null;
        }
    }

    // Before a multiplayer test: leftovers gone, mod on here and on the server, server as before the tests.
    // ok[0] = may go on.
    private static IEnumerator MpSettle(string name, bool[] ok)
    {
        ok[0] = false;
        Cleanup();
        var net = ZNet.instance;
        if (!SelfTest.IsMultiplayerRun || net == null || net.IsServer() || Player.m_localPlayer == null)
        {
            SelfTest.Fail(name, "needs a game joined to a dedicated server (tools/Test-Multiplayer.ps1)");
            yield break;
        }
        SetEnabledHere(true);
        var reply = new SelfTest.ServerReply();
        yield return SelfTest.CallServer(StepReset, "", reply);
        if (!reply.Answered || !reply.Ok)
        {
            // Step missing = mod off on the server (test before cut short there): turn it on, ask again.
            SelfTest.Note(name, $"server reset: {reply}; turning the mod on on the server and asking again");
            var on = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(ProbeSetEnabled, ModInfo.Guid + "=on", on);
            yield return WaitReal(1f);
            reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepReset, "", reply);
            if (!reply.Answered || !reply.Ok)
            {
                SelfTest.Fail(name, $"the server did not run the reset step: {reply}");
                yield break;
            }
        }
        yield return WaitActiveHere(true, 10f);
        var settle = Time.realtimeSinceStartup + 6f;
        while (Player.m_localPlayer != null && Player.m_localPlayer.IsSleeping() && Time.realtimeSinceStartup < settle)
        {
            yield return null;
        }
        if (!ActiveHere())
        {
            var feature = FeatureRegistry.Find(ModInfo.Guid);
            SelfTest.Fail(name, $"the mod is not active on this game ({feature?.State}: {feature?.Status})");
            yield break;
        }
        ok[0] = true;
    }

    // Rig on this game (bed, bypass, recorders). World time is the server's: RemoteRun ask for it.
    private static string BeginClient(string name, Setup s, out Case c, bool requireActive = true)
    {
        c = null;
        try
        {
            var net = ZNet.instance;
            if (net == null || net.IsServer())
            {
                return "needs a game joined to a server";
            }
            var error = Arm(name, s, out c, requireActive);
            if (error != null)
            {
                return error;
            }
            c.Remote = true;
            ApplySettings(s);
            MarkRun(c, "", c.Day0);
            return null;
        }
        catch (Exception e)
        {
            return $"setup failed: {e}";
        }
    }

    // New run: `s` = settings of THIS game (memory), serverSettings = what server use (StepSet text; null = its
    // config, defaults). Server jump to `hour` of its current date; this game's clock follow (NetTime), own smoothed
    // clock snapped like single-player rig do.
    private static IEnumerator RemoteRun(Case c, float hour, Setup s, string tag, string serverSettings)
    {
        c.Tag = tag ?? "";
        ApplySettings(s);
        yield return Server(c, StepSet, serverSettings ?? "wake=none;afternoon=none");
        if (!c.OK)
        {
            yield break;
        }
        var serverWake = Num(c.Answer, "wake");
        var serverAfternoon = Flag(c.Answer, "afternoon");
        yield return Server(c, StepTime, $"hour={F(hour)}");
        if (!c.OK)
        {
            yield break;
        }
        var t = Num(c.Answer, "time");
        var until = Time.realtimeSinceStartup + 8f;
        while (Math.Abs(c.Net.GetTimeSeconds() - t) > 5.0 && Time.realtimeSinceStartup < until)
        {
            yield return null;
        }
        if (Math.Abs(c.Net.GetTimeSeconds() - t) > 5.0)
        {
            Fail(c, $"this game's clock did not follow the server's time change within 8 s (here {c.Net.GetTimeSeconds():0} s, server {t:0} s)");
            c.OK = false;
            yield break;
        }
        var now = c.Net.GetTimeSeconds();
        var fraction = DayClock.FractionAt(c.Env, now);
        if (!float.IsNaN(fraction))
        {
            c.Env.m_smoothDayFraction = fraction;
        }
        FreshRun(c, s, tag, now);
        Plugin.ReadDaySettings(out var wakeHour, out var afternoon);
        Log.Info($"Self-test {c.Name}{(c.Tag.Length > 0 ? $" [{c.Tag}]" : "")}: server set the world time to {Clock(c.Env, now)} of day {c.Day}; "
                 + $"server uses WakeUpHour {serverWake:0.##}, IncludeAfternoon {serverAfternoon}; this game has WakeUpHour {wakeHour:0.##}, "
                 + $"IncludeAfternoon {afternoon}, WakeUpMessage \"{Plugin.ReadWakeUpMessage()}\".");
    }

    private static IEnumerator RemoteSleep(Case c, Plan r, bool unblock = false)
    {
        yield return RemoteFallAsleep(c, r, unblock);
        if (!c.OK)
        {
            yield break;
        }
        yield return RemoteWakeUp(c, r);
    }

    // Like FallAsleep, but the sleep is the server's: its counters at zero first, its numbers after the start.
    // unblock: server stop saying "a player is up" only now (counters already at zero).
    private static IEnumerator RemoteFallAsleep(Case c, Plan r, bool unblock = false)
    {
        yield return Server(c, StepMark, "");
        if (!c.OK)
        {
            yield break;
        }
        if (!r.InBed)
        {
            if (!r.SkipFlags)
            {
                yield return WaitFlags(c, r.Hour);
                if (!c.OK)
                {
                    yield break;
                }
            }
            CheckCanSleep(c, r.Hour);
            yield return LieDown(c, r.ViaPlayer);
            if (!c.OK)
            {
                yield break;
            }
        }
        if (unblock)
        {
            yield return Server(c, StepBlock, "0");
            if (!c.OK)
            {
                yield break;
            }
        }
        yield return WaitStart(c, 20f); // in-bed flag travel to the server, then its 2 s tick
        if (!c.OK)
        {
            yield break;
        }
        yield return Server(c, StepState, "");
        if (!c.OK)
        {
            yield break;
        }
        var a = c.Answer;
        var hereAtStart = c.StartTime;
        c.StartTime = Num(a, "startTime");
        c.SkipTo = Num(a, "startSkipTo");
        c.Skipping = Flag(a, "startSkipping");
        c.PlannedSeconds = Num(a, "planned");
        Report(c, (int)Num(a, "sleeps") == 1 && Math.Abs(hereAtStart - c.StartTime) < 5.0,
            $"the server started {Num(a, "sleeps"):0} sleep(s) at {Clock(c.Env, c.StartTime)}; this game fell asleep with its clock at {Clock(c.Env, hereAtStart)}");
        CheckTarget(c, r);
        CheckStarter(c, r, (int)Num(a, "starts"), (int)Num(a, "retargets"), Text(a, "startLine"), Text(a, "retargetLine"));
        if (r.CheckLength)
        {
            Report(c, Math.Abs(c.PlannedSeconds - VanillaSkipSeconds) <= 0.6,
                $"the server plans the time skip to take {c.PlannedSeconds:0.0} s of game time (vanilla: {VanillaSkipSeconds:0} s)");
        }
    }

    private static IEnumerator RemoteWakeUp(Case c, Plan r)
    {
        if (r.Fast > 0f)
        {
            yield return Server(c, StepFast, F(MpFastSeconds));
            if (!c.OK)
            {
                yield break;
            }
        }
        yield return WaitWake(c, WakeWaitSeconds);
        if (!c.OK)
        {
            yield break;
        }
        CheckWake(c, r);
    }

    // ---------- client tests ----------

    // M01: remote player's game show the mod Active. Morning: this player lie down while another is up (server's
    // "everybody in bed?" = no): nothing happen, player stay lying. Then everybody in bed: day sleep, wake at 18:00
    // of same day with "Good evening" and Rested, "Day sleep:" line in the server's log.
    private static IEnumerator RunMpDay()
    {
        const string N = MpDay;
        var go = new bool[1];
        yield return MpSettle(N, go);
        if (!go[0])
        {
            yield break;
        }
        var setup = new Setup();
        var error = BeginClient(N, setup, out var c);
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        var done = false;
        try
        {
            var feature = FeatureRegistry.Find(ModInfo.Guid);
            Report(c, feature != null && feature.Value.IsActive, $"on the joined player's game the mod is {feature?.State} (\"{feature?.Status}\")");
            yield return RemoteRun(c, 8f, setup, "", null);
            if (!c.OK)
            {
                yield break;
            }
            yield return Server(c, StepBlock, "1"); // another player is up
            if (!c.OK)
            {
                yield break;
            }
            yield return WaitFlags(c, 8f);
            if (!c.OK)
            {
                yield break;
            }
            CheckCanSleep(c, 8f);
            yield return LieDown(c);
            if (!c.OK)
            {
                yield break;
            }
            yield return Server(c, StepWatch, "5");
            if (!c.OK)
            {
                yield break;
            }
            var a = c.Answer;
            Report(c, !Flag(a, "started") && !_asleep && c.Player.InBed() && Num(a, "asked") >= 2 && (int)Num(a, "inbed") == 1,
                $"another player is up: over 5 s the server started a sleep {Flag(a, "started")}, asked \"is everybody in bed?\" "
                + $"{Num(a, "asked"):0} time(s) in {Num(a, "ticks"):0} check(s), sees {Num(a, "inbed"):0} of {Num(a, "players"):0} player(s) in bed; "
                + $"this player still lies {c.Player.InBed()} (expected no sleep, still lying)");

            c.LieDownReal = Time.realtimeSinceStartup;
            var plan = Plan.ToHour(8f, 18f);
            plan.InBed = true;
            plan.CheckLength = true;
            yield return RemoteSleep(c, plan, true); // the other player lies down too
            if (!c.OK)
            {
                yield break;
            }
            EndChecks(c);
            yield return Server(c, StepReset, "");
            done = c.OK;
        }
        finally
        {
            Cleanup();
            if (!done)
            {
                FireServer(StepReset, "");
            }
        }
    }

    // M02: server's WakeUpHour and IncludeAfternoon count for every player, whatever the player's own game say;
    // the wake-up message is the player's own.
    private static IEnumerator RunMpSettings()
    {
        const string N = MpSettings;
        const string own = "Evening on this game";
        var go = new bool[1];
        yield return MpSettle(N, go);
        if (!go[0])
        {
            yield break;
        }
        var setup = new Setup { Message = own };
        var error = BeginClient(N, setup, out var c);
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        var done = false;
        try
        {
            yield return RemoteRun(c, 8f, setup, "server WakeUpHour 21, this game 18", "wake=21;afternoon=0");
            if (!c.OK)
            {
                yield break;
            }
            yield return RemoteSleep(c, Plan.ToHour(8f, 21f, own));
            if (!c.OK)
            {
                yield break;
            }

            yield return RemoteRun(c, 14f, setup, "server IncludeAfternoon on, this game off", "wake=18;afternoon=1");
            if (!c.OK)
            {
                yield break;
            }
            yield return RemoteSleep(c, Plan.ToHour(14f, 18f, own, Starter.Retarget));
            if (!c.OK)
            {
                yield break;
            }

            var on = new Setup { Message = own, Afternoon = true };
            yield return RemoteRun(c, 14f, on, "server IncludeAfternoon off, this game on", "wake=18;afternoon=0");
            if (!c.OK)
            {
                yield break;
            }
            yield return RemoteSleep(c, Plan.ToMorning(14f));
            if (!c.OK)
            {
                yield break;
            }
            EndChecks(c);
            yield return Server(c, StepReset, "");
            done = c.OK;
        }
        finally
        {
            Cleanup();
            if (!done)
            {
                FireServer(StepReset, "");
            }
        }
    }

    // M10 (and T11 "30 in the file"): the server is a real dedicated server of this game version, the mod loaded
    // and Active there with a clean log. WakeUpHour = 20 and IncludeAfternoon = true written to the SERVER's config:
    // morning and afternoon sleeps wake at 20:00, "Day sleep:" lines in the server's log. 30 written there = 23.
    private static IEnumerator RunMpDedicated()
    {
        const string N = MpDedicated;
        var go = new bool[1];
        yield return MpSettle(N, go);
        if (!go[0])
        {
            yield break;
        }
        var setup = new Setup();
        var error = BeginClient(N, setup, out var c);
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        var done = false;
        try
        {
            yield return Server(c, StepFacts, "");
            if (!c.OK)
            {
                yield break;
            }
            var a = c.Answer;
            var here = global::Version.CurrentVersion.ToString();
            a.TryGetValue("version", out var there);
            a.TryGetValue("state", out var state);
            Report(c, Flag(a, "dedicated") && there == here,
                $"the server is a dedicated server: {Flag(a, "dedicated")}; its game version {there}, this game {here} (expected the same)");
            Report(c, state == nameof(ModState.Active) && Flag(a, "ready") && Flag(a, "activated") && (int)Num(a, "warnings") == 0 && (int)Num(a, "errors") == 0,
                $"server log file: [MC:ready] line of the mod {Flag(a, "ready")}, \"Activated.\" {Flag(a, "activated")}, warnings of the mod "
                + $"{Num(a, "warnings"):0}, errors {Num(a, "errors"):0}{(Text(a, "first") != null ? $" (first: {Text(a, "first")})" : "")}; mod {state} on the server");

            const string config = "cfgwake=20;cfgafternoon=true";
            yield return RemoteRun(c, 8f, setup, "server config WakeUpHour = 20, IncludeAfternoon = true; 08:00", config);
            if (!c.OK)
            {
                yield break;
            }
            yield return RemoteSleep(c, Plan.ToHour(8f, 20f));
            if (!c.OK)
            {
                yield break;
            }

            yield return RemoteRun(c, 14f, setup, "server config WakeUpHour = 20, IncludeAfternoon = true; 14:00", config);
            if (!c.OK)
            {
                yield break;
            }
            yield return RemoteSleep(c, Plan.ToHour(14f, 20f, DefaultMessage, Starter.Retarget));
            if (!c.OK)
            {
                yield break;
            }

            // Value outside the range written the way the config file loader write it.
            yield return Server(c, StepSet, "cfgwake=30;cfgafternoon=false");
            if (!c.OK)
            {
                yield break;
            }
            c.Tag = "server config WakeUpHour = 30";
            var kept = Num(c.Answer, "cfgwake");
            Report(c, Math.Abs(kept - 23.0) < 0.001 && Math.Abs(Num(c.Answer, "wake") - 23.0) < 0.001,
                $"WakeUpHour = 30 written to the server's config is kept as {kept:0.##} and used as {Num(c.Answer, "wake"):0.##} (expected 23)");
            yield return RemoteRun(c, 8f, setup, "server config WakeUpHour = 30", "cfgwake=30;cfgafternoon=false");
            if (!c.OK)
            {
                yield break;
            }
            yield return RemoteSleep(c, Plan.ToHour(8f, 23f));
            if (!c.OK)
            {
                yield break;
            }
            EndChecks(c);
            yield return Server(c, StepReset, "");
            done = c.OK;
        }
        finally
        {
            Cleanup();
            if (!done)
            {
                FireServer(StepReset, "");
            }
        }
    }

    // M03: default settings, afternoon sleep: next morning. The joined player's clock trail the server's by up to
    // 2 s (a third of a day at skip speed): server send the final time just before the wake-up, so that game see
    // the new date and say "Good morning", not "Good evening".
    private static IEnumerator RunMpMessage()
    {
        const string N = MpMessage;
        var go = new bool[1];
        yield return MpSettle(N, go);
        if (!go[0])
        {
            yield break;
        }
        var setup = new Setup();
        var error = BeginClient(N, setup, out var c);
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        var done = false;
        try
        {
            yield return RemoteRun(c, 14f, setup, "", null);
            if (!c.OK)
            {
                yield break;
            }
            yield return RemoteSleep(c, Plan.ToMorning(14f));
            if (!c.OK)
            {
                yield break;
            }
            Report(c, c.Env.GetDay(_wakeTime) == c.Day + 1 && _wakeTime >= c.SkipTo - TargetTolerance,
                $"when the wake-up came this game's clock was at {Clock(c.Env, _wakeTime)} of day {c.Env.GetDay(_wakeTime)}, "
                + $"{_wakeTime - c.SkipTo:0.0} s after the server's target (expected not before it: the server sends the final time first)");
            EndChecks(c);
            yield return Server(c, StepReset, "");
            done = c.OK;
        }
        finally
        {
            Cleanup();
            if (!done)
            {
                FireServer(StepReset, "");
            }
        }
    }

    // M08: server owner turn the mod off while the player is in: player's game follow ("the server has this mod
    // turned off"), morning bed refused. On again: Active, morning sleep work.
    private static IEnumerator RunMpServerOff()
    {
        const string N = MpServerOff;
        var go = new bool[1];
        yield return MpSettle(N, go);
        if (!go[0])
        {
            yield break;
        }
        var setup = new Setup();
        var error = BeginClient(N, setup, out var c);
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        var done = false;
        var serverOff = false;
        var held = _holdTests;
        _holdTests = true; // this game's copy go off and on under the running test
        try
        {
            yield return RemoteRun(c, 8f, setup, "", null);
            if (!c.OK)
            {
                yield break;
            }
            yield return WaitFlags(c, 8f);
            if (!c.OK)
            {
                yield break;
            }
            CheckCanSleep(c, 8f);
            c.Bed.Interact(c.Player, false, false); // claim
            yield return null;

            c.Tag = "server turned it off";
            serverOff = true;
            yield return Server(c, ProbeSetEnabled, ModInfo.Guid + "=off");
            if (!c.OK)
            {
                yield break;
            }
            yield return WaitActiveHere(false, 10f);
            var feature = FeatureRegistry.Find(ModInfo.Guid);
            var status = feature?.Status ?? "";
            Report(c, feature != null && feature.Value.State == nameof(ModState.ServerMissing)
                      && status.IndexOf("the server has this mod turned off", StringComparison.Ordinal) >= 0 && PatchedByMod() == 0,
                $"this game follows: {feature?.State}, \"{status}\", {PatchedByMod()} game methods still carry a patch of the mod (expected 0)");
            yield return FixedSteps(3);
            var mark = Messages.Count;
            var can = EnvMan.CanSleep();
            c.Bed.Interact(c.Player, false, false);
            yield return null;
            Report(c, !can && LastCentre(mark) == CantSleepToken && !c.Player.IsAttached(),
                $"morning bed: Can sleep {can}, bed says \"{Localize(LastCentre(mark))}\", lying {c.Player.IsAttached()} "
                + $"(expected refused with \"{Localize(CantSleepToken)}\")");

            c.Tag = "server turned it on again";
            yield return Server(c, ProbeSetEnabled, ModInfo.Guid + "=on");
            if (!c.OK)
            {
                yield break;
            }
            serverOff = false;
            yield return WaitActiveHere(true, 10f);
            feature = FeatureRegistry.Find(ModInfo.Guid);
            Report(c, feature != null && feature.Value.IsActive && PatchedByMod() == 4,
                $"this game follows: {feature?.State}, {PatchedByMod()} game methods carry the mod's patches again (expected 4)");
            if (!ActiveHere())
            {
                yield break;
            }
            MarkRun(c, "server turned it on again", c.Env.GetDay(c.Net.GetTimeSeconds()));
            yield return RemoteSleep(c, Plan.ToHour(8f, 18f));
            if (!c.OK)
            {
                yield break;
            }
            EndChecks(c);
            yield return Server(c, StepReset, "");
            done = c.OK;
        }
        finally
        {
            Cleanup();
            if (serverOff)
            {
                FireServer(ProbeSetEnabled, ModInfo.Guid + "=on");
            }
            if (!done)
            {
                FireServer(StepReset, "");
            }
            _holdTests = held;
        }
    }

    // M09, T13 and T14 with the real Enabled setting of this game (what the MC Mods panel write): off = this
    // player cannot lie down in the morning and the server start nothing; on = morning sleep work again, no restart;
    // off during a day sleep = still wake at 18:00, with vanilla "Good morning", no error.
    private static IEnumerator RunMpClientOff()
    {
        const string N = MpClientOff;
        var go = new bool[1];
        yield return MpSettle(N, go);
        if (!go[0])
        {
            yield break;
        }
        var setup = new Setup();
        var error = BeginClient(N, setup, out var c);
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        var done = false;
        var held = _holdTests;
        _holdTests = true;
        try
        {
            yield return RemoteRun(c, 8f, setup, "", null);
            if (!c.OK)
            {
                yield break;
            }
            yield return WaitFlags(c, 8f);
            if (!c.OK)
            {
                yield break;
            }
            CheckCanSleep(c, 8f);
            c.Bed.Interact(c.Player, false, false); // claim
            yield return null;

            c.Tag = "Enabled = false on this game";
            SetEnabledHere(false);
            var feature = FeatureRegistry.Find(ModInfo.Guid);
            Report(c, feature != null && feature.Value.State == nameof(ModState.Disabled) && PatchedByMod() == 0,
                $"{feature?.State} (\"{feature?.Status}\"), {PatchedByMod()} game methods still carry a patch of the mod (expected 0)");
            yield return FixedSteps(3);
            var mark = Messages.Count;
            var can = EnvMan.CanSleep();
            c.Bed.Interact(c.Player, false, false);
            yield return null;
            Report(c, !can && LastCentre(mark) == CantSleepToken && !c.Player.IsAttached(),
                $"morning bed: Can sleep {can}, bed says \"{Localize(LastCentre(mark))}\", lying {c.Player.IsAttached()} "
                + $"(expected refused with \"{Localize(CantSleepToken)}\")");
            yield return Server(c, StepWatch, "5");
            if (!c.OK)
            {
                yield break;
            }
            var a = c.Answer;
            Report(c, !Flag(a, "started") && (int)Num(a, "inbed") == 0 && !_asleep,
                $"server with the mod on, over 5 s: sleep started {Flag(a, "started")}, {Num(a, "inbed"):0} of {Num(a, "players"):0} player(s) in bed "
                + "(expected no sleep: the player who cannot lie down blocks it)");

            c.Tag = "Enabled = true again";
            SetEnabledHere(true);
            yield return WaitActiveHere(true, 10f);
            feature = FeatureRegistry.Find(ModInfo.Guid);
            Report(c, feature != null && feature.Value.IsActive && PatchedByMod() == 4,
                $"{feature?.State}, {PatchedByMod()} game methods carry the mod's patches again (expected 4), no restart");
            if (!ActiveHere())
            {
                yield break;
            }
            MarkRun(c, "Enabled = true again", c.Env.GetDay(c.Net.GetTimeSeconds()));
            yield return RemoteSleep(c, Plan.ToHour(8f, 18f));
            if (!c.OK)
            {
                yield break;
            }

            // Off while asleep in a day sleep.
            yield return RemoteRun(c, 8f, setup, "Enabled = false during the sleep", null);
            if (!c.OK)
            {
                yield break;
            }
            var plan = Plan.ToHour(8f, 18f);
            yield return RemoteFallAsleep(c, plan);
            if (!c.OK)
            {
                yield break;
            }
            yield return WaitReal(1f);
            SetEnabledHere(false);
            feature = FeatureRegistry.Find(ModInfo.Guid);
            Report(c, feature != null && feature.Value.State == nameof(ModState.Disabled) && PatchedByMod() == 0 && c.Player.IsSleeping(),
                $"turned off 1 s into the sleep: {feature?.State}, {PatchedByMod()} patches of the mod left, still asleep {c.Player.IsSleeping()}");
            plan.First = WakeMessage.GoodMorningToken;
            plan.ModSilent = true;
            yield return RemoteWakeUp(c, plan);
            if (!c.OK)
            {
                yield break;
            }
            var errors = _watch.Errors(c.ErrMark);
            Report(c, errors.Count == 0, $"error lines in this game's log during this sleep: {errors.Count}" + (errors.Count > 0 ? $", first: {errors[0]}" : ""));
            SetEnabledHere(true);
            yield return WaitActiveHere(true, 10f);
            EndChecks(c);
            yield return Server(c, StepReset, "");
            done = c.OK;
        }
        finally
        {
            Cleanup();
            SetEnabledHere(true);
            if (!done)
            {
                FireServer(StepReset, "");
            }
            _holdTests = held;
        }
    }

    // M05 / M06, hand-off: this game with the mod turned off behave like a game without it (vanilla beds, vanilla
    // wake-up). Server IncludeAfternoon on: its afternoon sleep is cut to 18:00 by the server, it see "Good morning"
    // and get Rested. In bed since before dawn while another player is up, then everybody in bed in the morning:
    // day sleep, wake at 18:00 with "Good morning" and Rested. No error.
    private static IEnumerator RunMpHandoff()
    {
        const string N = MpHandoff;
        var go = new bool[1];
        yield return MpSettle(N, go);
        if (!go[0])
        {
            yield break;
        }
        var setup = new Setup();
        var error = BeginClient(N, setup, out var c);
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        var done = false;
        var held = _holdTests;
        _holdTests = true;
        try
        {
            SetEnabledHere(false);
            var feature = FeatureRegistry.Find(ModInfo.Guid);
            Report(c, feature != null && !feature.Value.IsActive && PatchedByMod() == 0,
                $"this game stands in for a player without the mod: {feature?.State}, {PatchedByMod()} patches of the mod on its game methods");

            yield return RemoteRun(c, 14f, setup, "afternoon, server IncludeAfternoon on", "wake=18;afternoon=1");
            if (!c.OK)
            {
                yield break;
            }
            var afternoon = Plan.ToHour(14f, 18f, "", Starter.Retarget); // vanilla wake-up text on this game
            afternoon.ModSilent = true;
            yield return RemoteSleep(c, afternoon);
            if (!c.OK)
            {
                yield break;
            }

            // In bed since before dawn.
            yield return Server(c, StepBlock, "1"); // the modded player is still up
            if (!c.OK)
            {
                yield break;
            }
            yield return RemoteRun(c, 5.75f, setup, "in bed since before dawn", null);
            if (!c.OK)
            {
                yield break;
            }
            yield return WaitFlags(c, 5.75f);
            if (!c.OK)
            {
                yield break;
            }
            CheckCanSleep(c, 5.75f);
            yield return LieDown(c);
            if (!c.OK)
            {
                yield break;
            }
            yield return Server(c, StepTime, "hour=6.5");
            if (!c.OK)
            {
                yield break;
            }
            yield return WaitReal(2.5f); // one server check in the morning with the other player still up
            Report(c, c.Player.InBed() && !_asleep, $"still lying at {Clock(c.Env, c.Net.GetTimeSeconds())} (in bed since before dawn), no sleep while the other player is up");
            var morning = Plan.ToHour(6.5f, 18f, "");
            morning.InBed = true;
            morning.ModSilent = true;
            yield return RemoteSleep(c, morning, true); // the modded player lies down in the morning
            if (!c.OK)
            {
                yield break;
            }
            var errors = _watch.Errors(0);
            Report(c, errors.Count == 0, $"error lines in this game's log during the test: {errors.Count}" + (errors.Count > 0 ? $", first: {errors[0]}" : ""));
            SetEnabledHere(true);
            yield return WaitActiveHere(true, 10f);
            EndChecks(c);
            yield return Server(c, StepReset, "");
            done = c.OK;
        }
        finally
        {
            Cleanup();
            SetEnabledHere(true);
            if (!done)
            {
                FireServer(StepReset, "");
            }
            _holdTests = held;
        }
    }

    // M07 (scenario vanilla-server: the server has no MC mod): status say why the mod is off, no patch of it left,
    // morning bed refused. World time is the server's and nobody there can move it for us: this game's own day
    // flags are held at morning (they are local, like console `tod`), which is all the bed look at.
    private static IEnumerator RunMpVanillaServer()
    {
        const string N = MpVanillaServer;
        Cleanup();
        var net = ZNet.instance;
        if (!SelfTest.IsMultiplayerRun || net == null || net.IsServer() || Player.m_localPlayer == null)
        {
            SelfTest.Fail(N, "needs a game joined to a dedicated server without the mod (tools/Test-Multiplayer.ps1 -Scenario vanilla-server)");
            yield break;
        }
        var feature = FeatureRegistry.Find(ModInfo.Guid);
        var status = feature?.Status ?? "";
        Report(N, feature != null && feature.Value.State == nameof(ModState.ServerMissing)
                  && status.StartsWith("Inactive: the server does not have this mod", StringComparison.Ordinal),
            $"status of the mod on a server without it: {feature?.State}, \"{status}\" (expected \"Inactive: the server does not have this mod ...\")");
        Report(N, PatchedByMod() == 0, $"game methods that carry a patch of the mod: {PatchedByMod()} (expected 0)");

        var error = BeginClient(N, new Setup(), out var c, false);
        if (error != null)
        {
            SelfTest.Fail(N, error);
            Cleanup();
            yield break;
        }
        try
        {
            c.Env.m_debugTime = 0.35f;
            c.Env.m_debugTimeOfDay = true;
            var until = Time.realtimeSinceStartup + FlagWaitSeconds;
            while (!DayClock.IsMorning() && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
            c.Player.m_wakeupTime = c.Net.GetTimeSeconds() - 1000.0;
            yield return FixedSteps(3);
            var mark = Messages.Count;
            c.Bed.Interact(c.Player, false, false); // claim
            yield return null;
            Report(c, c.Bed.IsCurrent() && LastCentre(mark) == SpawnSetToken, $"bed claimed, spawn point set: {c.Bed.IsCurrent()}");
            mark = Messages.Count;
            var can = EnvMan.CanSleep();
            c.Bed.Interact(c.Player, false, false);
            yield return null;
            Report(c, DayClock.IsMorning() && !can && LastCentre(mark) == CantSleepToken && !c.Player.IsAttached(),
                $"morning (day flags of this game held at morning: {DayClock.IsMorning()}): Can sleep {can}, bed says \"{Localize(LastCentre(mark))}\", "
                + $"lying {c.Player.IsAttached()} (expected refused with \"{Localize(CantSleepToken)}\", as in vanilla)");
            var problems = _watch.Problems(null);
            Report(c, problems.Count == 0, $"warnings or errors of the mod during this test: {problems.Count}" + (problems.Count > 0 ? $", first: {Quote(problems[0])}" : ""));
        }
        finally
        {
            Cleanup();
        }
    }
}
#endif
