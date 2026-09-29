using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using MC.Shared;
using UnityEngine;
#endif

namespace MC.Exploration.SleepThroughDayMod;

// Debug build only (calls vanish in Release): in-world self tests, run by world probe (tools/Test-InWorld.ps1) in
// throwaway single-player world (local game = server, so server and client part both run).
// Each test: set world time, spawn real "bed", claim it and lie down through vanilla Bed.Interact (roof, fire,
// enemy and wet checks skipped for THIS bed only, by temporary Harmony prefix), let vanilla server loop + mod start
// the sleep, wait for wake-up, check where time landed, which messages showed in wake-up call (temporary recorder
// on MessageHud.ShowMessage), Rested. Then put world back: player awake, out of bed and back in place, bed gone,
// time, spawn point, cooldowns, Rested as before. Settings forced only in memory (overrides, read by
// Plugin.ReadDaySettings): config file never written.
internal static class SleepSelfTests
{
#if DEBUG
    private const string Morning = "sleep.morning";
    private const string Night = "sleep.night";
    private const string AfternoonDefault = "sleep.afternoon.default";
    private const string AfternoonIncluded = "sleep.afternoon.included";

    private const float FlagWaitSeconds = 5f;      // smoothed clock snapped; flags follow at next FixedUpdate
    private const float StartWaitSeconds = 15f;    // server tick every 2 s
    private const float WakeWaitSeconds = 75f;     // skip ~12 s (longer at low frame rate) + next tick; whole test under probe 120 s
    private const float FadeShotDelay = 2f;
    private const float AwakeShotDelay = 2.5f;
    private const double TargetTolerance = 0.5;    // world seconds (vanilla target math is float)
    private const double WakeLateTolerance = 15.0; // world seconds after target: stop come at next 2 s tick
    private const string RestedStartToken = "$se_rested_start"; // SE_Rested.Setup message, when Rested is new

    private static Bed _testBed;    // only this bed skip vanilla roof/fire/enemy/wet checks
    private static Harmony _bypass; // bed check bypass + wake message recorder, gone in Undo
    private static Action _undo;    // put world back: at test end, at next test start (test cut short), on deactivate

    private static bool _woke;
    private static double _wakeTime;
    private static bool _wakeDaySleep;

    // Messages MessageHud got during local SetSleeping(false) call (raw text, before localize), in order.
    private static readonly List<KeyValuePair<MessageHud.MessageType, string>> WakeMessages =
        new List<KeyValuePair<MessageHud.MessageType, string>>();
    private static bool _recordingWake;
    private static bool _restedBeforeWake;

    // Set = Plugin.ReadDaySettings use this instead of config value. Memory only; Undo and Unregister put back null.
    internal static float? WakeHourOverride;
    internal static bool? IncludeAfternoonOverride;

    // What one test run need, made by Prepare.
    private sealed class Case
    {
        internal Player Player;
        internal EnvMan Env;
        internal ZNet Net;
        internal int Day;
        internal bool ExpectDaySleep;
    }
#endif

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        SelfTest.Register(Morning, () => Run(Morning, 8f, true, null));
        SelfTest.Register(Night, () => Run(Night, 22f, false, null));
        SelfTest.Register(AfternoonDefault, () => Run(AfternoonDefault, 14f, false, false));
        SelfTest.Register(AfternoonIncluded, () => Run(AfternoonIncluded, 14f, true, true));
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        SelfTest.Unregister(Morning);
        SelfTest.Unregister(Night);
        SelfTest.Unregister(AfternoonDefault);
        SelfTest.Unregister(AfternoonIncluded);
        Cleanup();
        WakeHourOverride = null;
        IncludeAfternoonOverride = null;
#endif
    }

    // WakeMessage call me on every local wake-up (SetSleeping postfix). Messages of that call: recorder below.
    [Conditional("DEBUG")]
    internal static void RecordWake(double time, bool daySleep)
    {
#if DEBUG
        _woke = true;
        _wakeTime = time;
        _wakeDaySleep = daySleep;
#endif
    }

#if DEBUG
    // includeAfternoon: null = use config; else force it (memory only) for this test, with WakeUpHour 18 when on.
    private static IEnumerator Run(string name, float hour, bool expectDaySleep, bool? includeAfternoon)
    {
        Cleanup(); // leftovers of a test cut short
        var settle = Time.realtimeSinceStartup + 6f;
        while (Game.instance != null && Game.instance.m_sleeping && Time.realtimeSinceStartup < settle)
        {
            yield return null; // sleep not ours (Undo wake ours at once): vanilla stop branch end it at next 2 s tick
        }
        var error = Prepare(name, hour, expectDaySleep, includeAfternoon, out var c);
        if (error != null)
        {
            SelfTest.Fail(name, error);
            Cleanup();
            yield break;
        }

        try
        {
            // 1. Flags follow new time (clock snapped in Prepare). Wait a moment at least (several FixedUpdate): CanSleep
            // flag computed in same FixedUpdate as day flags, must see new cooldown too.
            var until = Time.realtimeSinceStartup + 0.25f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
            until = Time.realtimeSinceStartup + FlagWaitSeconds;
            while (!FlagsMatch(hour) && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
            if (!FlagsMatch(hour))
            {
                SelfTest.Fail(name, $"the day flags did not follow the time change (clock {Clock(c.Env, c.Net.GetTimeSeconds())}, "
                                    + $"afternoon={EnvMan.IsAfternoon()}, night={EnvMan.IsNight()})");
                yield break;
            }

            // 2. Client permission (morning: the mod's; afternoon, night: vanilla's).
            var canSleep = EnvMan.CanSleep();
            if (hour < DayClock.NoonHour)
            {
                Report(name, canSleep, $"EnvMan.CanSleep() at {Clock(c.Env, c.Net.GetTimeSeconds())} is {canSleep} (expected true; vanilla says false in the morning)");
            }
            else if (!canSleep)
            {
                SelfTest.Fail(name, $"EnvMan.CanSleep() is false at {Clock(c.Env, c.Net.GetTimeSeconds())} (vanilla allows it)");
            }

            // 3. Claim the bed and set spawn point (first E), then lie down (second E): vanilla Bed.Interact.
            _testBed.Interact(c.Player, false, false);
            yield return null;
            if (_testBed == null || !_testBed.IsCurrent())
            {
                SelfTest.Fail(name, "the first bed interaction did not claim the bed as the spawn point");
                yield break;
            }
            _testBed.Interact(c.Player, false, false);
            yield return null;
            if (c.Player.IsAttached() && c.Player.InBed())
            {
                SelfTest.Pass(name, $"lay down through Bed.Interact at {Clock(c.Env, c.Net.GetTimeSeconds())}");
            }
            else
            {
                SelfTest.Fail(name, $"Bed.Interact did not lay the player down at {Clock(c.Env, c.Net.GetTimeSeconds())} "
                                    + $"(message: \"{CenterText()}\"); continuing with the same AttachStart call the bed uses");
                AttachDirect(c.Player);
                yield return null;
            }

            // 4. Server loop start the sleep (every 2 s).
            _woke = false;
            until = Time.realtimeSinceStartup + StartWaitSeconds;
            while (!c.Player.IsSleeping() && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
            if (!c.Player.IsSleeping())
            {
                SelfTest.Fail(name, $"no sleep started within {StartWaitSeconds:0} s with the player in bed "
                                    + $"(Game.UpdateSleeping; clock {Clock(c.Env, c.Net.GetTimeSeconds())})");
                yield break;
            }
            var startTime = c.Net.GetTimeSeconds();
            var skipTo = c.Env.m_skipToTime;
            var expected = ExpectedWake(c, startTime);
            var expectedDay = c.ExpectDaySleep ? c.Day : c.Day + 1;
            Report(name, c.Env.IsTimeSkipping() && Math.Abs(skipTo - expected) <= TargetTolerance,
                $"sleep started at {Clock(c.Env, startTime)}; time skip aims at {Clock(c.Env, skipTo)} of day {c.Env.GetDay(skipTo)} "
                + $"(expected {Clock(c.Env, expected)} of day {expectedDay}, {(c.ExpectDaySleep ? "same day" : "next morning")})");

            // 5. Screenshot while screen fade to black.
            until = Time.realtimeSinceStartup + FadeShotDelay;
            while (Time.realtimeSinceStartup < until && c.Player.IsSleeping())
            {
                yield return null;
            }
            SelfTest.Screenshot(name, "fade");
            yield return null;
            yield return null;

            // 6. Wake-up (server stop branch -> SleepStop -> SetSleeping(false) -> WakeMessage -> RecordWake).
            until = Time.realtimeSinceStartup + WakeWaitSeconds;
            while (!_woke && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
            if (!_woke)
            {
                SelfTest.Fail(name, $"the player did not wake up within {WakeWaitSeconds:0} s (clock {Clock(c.Env, c.Net.GetTimeSeconds())}, "
                                    + $"skipping={c.Env.IsTimeSkipping()})");
                yield break;
            }

            var wakeDay = c.Env.GetDay(_wakeTime);
            Report(name, wakeDay == expectedDay && _wakeTime >= expected - TargetTolerance && _wakeTime <= expected + WakeLateTolerance,
                $"woke up at {Clock(c.Env, _wakeTime)} of day {wakeDay} (expected {Clock(c.Env, expected)} of day {expectedDay})");

            // Vanilla SetSleeping(false): Center "$msg_goodmorning", then Rested start message when Rested is new
            // (it replace the first in same frame). Day sleep: WakeUpMessage in place of the first, order kept.
            var centre = CentreMessages();
            var wakeText = ExpectedWakeText(c.ExpectDaySleep);
            var vanillaText = wakeText == WakeMessage.GoodMorningToken;
            var wakeOk = centre.Count > 0 && centre[0] == wakeText
                         && (vanillaText || !centre.Contains(WakeMessage.GoodMorningToken));
            Report(name, _wakeDaySleep == c.ExpectDaySleep && wakeOk,
                $"wake-up messages {DescribeMessages()}, {(_wakeDaySleep ? "daytime" : "night")} sleep (expected "
                + $"\"{Localize(wakeText)}\" first{(vanillaText ? "" : " and no \"Good morning\"")}, "
                + $"{(c.ExpectDaySleep ? "daytime" : "night")} sleep)");
            if (_restedBeforeWake)
            {
                Report(name, centre.Count == 1, "Rested was already on before waking up, so vanilla only resets its time "
                    + $"and shows no Rested message: {centre.Count} center message(s) (expected 1)");
            }
            else
            {
                var restedOk = centre.Count > 1 && centre[1].StartsWith(RestedStartToken, StringComparison.Ordinal);
                Report(name, restedOk, "Rested message right after the wake-up text, as in vanilla: "
                    + $"{(centre.Count > 1 ? $"\"{Localize(centre[1])}\"" : "none")} (expected \"{Localize(RestedStartToken)} ...\")");
            }

            var rested = c.Player.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectRested);
            Report(name, rested, $"Rested after waking up: {rested}");

            // 7. Screenshot after screen come back.
            until = Time.realtimeSinceStartup + AwakeShotDelay;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
            SelfTest.Screenshot(name, "awake");
            yield return null;
            yield return null;
        }
        finally
        {
            Cleanup();
        }
    }

    // No yield here: can catch. Save what test change (in _undo), set time, spawn bed, install bypass and recorder.
    private static string Prepare(string name, float hour, bool expectDaySleep, bool? includeAfternoon, out Case c)
    {
        c = null;
        try
        {
            var player = Player.m_localPlayer;
            var env = EnvMan.instance;
            var net = ZNet.instance;
            var game = Game.instance;
            var scene = ZNetScene.instance;
            if (player == null || env == null || net == null || game == null || scene == null || MessageHud.instance == null)
            {
                return "no world or no local player";
            }
            if (!net.IsServer())
            {
                return "needs a single-player world (the local game must be the server)";
            }
            if (game.m_sleeping || env.IsTimeSkipping() || player.IsSleeping() || player.IsAttached())
            {
                return "a sleep or time skip is already running, or the player is attached to something";
            }
            if (env.m_debugTimeOfDay)
            {
                return "the time of day is forced (console tod); run tod -1 first";
            }
            var prefab = scene.GetPrefab("bed");
            if (prefab == null || prefab.GetComponent<Bed>() == null)
            {
                return "prefab 'bed' with a Bed component not found";
            }

            // Save state first: _undo put it back whatever happen next.
            var profile = game.GetPlayerProfile();
            var hadSpawn = profile.HaveCustomSpawnPoint();
            var oldSpawn = profile.GetCustomSpawnPoint();
            var oldTime = net.GetTimeSeconds();
            var oldWakeup = player.m_wakeupTime;
            var oldLastSleep = game.m_lastSleepTime;
            var hadRested = player.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectRested);
            var oldPos = player.transform.position;
            var oldRot = player.transform.rotation;
            _undo = () => Undo(player, env, net, game, profile, hadSpawn, oldSpawn, oldTime, oldWakeup, oldLastSleep, hadRested,
                oldPos, oldRot);

            // Settings this test depend on: memory only (Plugin.ReadDaySettings), never ConfigEntry.Value (that write
            // user's .cfg at once, and killed game never put it back). IncludeAfternoon on: WakeUpHour 18 always, so
            // window (until 17:00) surely hold 14:00 start, even with 2 s tick lag.
            if (includeAfternoon.HasValue)
            {
                IncludeAfternoonOverride = includeAfternoon.Value;
                WakeHourOverride = includeAfternoon.Value ? DayClock.NightfallHour : (float?)null;
            }
            Plugin.ReadDaySettings(out var wakeHour, out var afternoon);

            // Time: `hour` of current date. Cooldown (30 s) and 10 s guard over, no Rested yet.
            var day = env.GetDay(oldTime);
            var len = (double)env.m_dayLengthSec;
            var start = day * len + DayClock.RawFractionForHour(env, hour) * len;
            SetTime(env, net, start);
            player.m_wakeupTime = start - 1000.0;
            game.m_lastSleepTime = start - 1000.0;
            player.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectRested, true);

            // Bed 2.5 m ahead, on the ground.
            var forward = player.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude < 0.01f ? Vector3.forward : forward.normalized;
            var pos = player.transform.position + forward * 2.5f;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(pos, out var ground))
            {
                pos.y = ground;
            }
            var go = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.LookRotation(-forward));
            _testBed = go.GetComponent<Bed>();

            // Roof, fire, enemy, wet checks: true for this bed only. Time check (EnvMan.CanSleep) stay real.
            _bypass = new Harmony(ModInfo.Guid + ".selftest");
            var prefix = new HarmonyMethod(typeof(SleepSelfTests), nameof(BypassForTestBed));
            _bypass.Patch(Method(nameof(Bed.CheckEnemies)), prefix: prefix);
            _bypass.Patch(Method(nameof(Bed.CheckExposure)), prefix: prefix);
            _bypass.Patch(Method(nameof(Bed.CheckFire)), prefix: prefix);
            _bypass.Patch(Method(nameof(Bed.CheckWet)), prefix: prefix);

            // Wake message recorder: SetSleeping first prefix / last postfix frame the wake call, ShowMessage prefix
            // record what reach HUD (after the mod's swap in Player.Message).
            _bypass.Patch(Find(typeof(Player), nameof(Player.SetSleeping)),
                prefix: new HarmonyMethod(typeof(SleepSelfTests), nameof(WakeCallBegin)) { priority = Priority.First },
                postfix: new HarmonyMethod(typeof(SleepSelfTests), nameof(WakeCallEnd)) { priority = Priority.Last });
            _bypass.Patch(Find(typeof(MessageHud), nameof(MessageHud.ShowMessage)),
                prefix: new HarmonyMethod(typeof(SleepSelfTests), nameof(RecordMessage)));

            c = new Case
            {
                Player = player,
                Env = env,
                Net = net,
                Day = day,
                ExpectDaySleep = expectDaySleep,
            };
            Log.Info($"Self-test {name}: world time set to {Clock(env, start)} of day {day}, bed at {pos}; WakeUpHour "
                     + $"{wakeHour:0.##}, IncludeAfternoon {afternoon} "
                     + $"{(includeAfternoon.HasValue ? "(forced for this test, config file untouched)" : "(from the config)")}.");
            return null;
        }
        catch (Exception e)
        {
            return $"setup failed: {e}";
        }
    }

    private static System.Reflection.MethodInfo Method(string name) => Find(typeof(Bed), name);

    private static System.Reflection.MethodInfo Find(Type type, string name) =>
        AccessTools.Method(type, name) ?? throw new MissingMethodException(type.Name, name);

    private static bool BypassForTestBed(Bed __instance, ref bool __result)
    {
        if (_testBed == null || __instance != _testBed)
        {
            return true;
        }
        __result = true;
        return false;
    }

    // Local player asleep, call say wake: start recording (clear old list), note Rested before.
    private static void WakeCallBegin(Player __instance, bool sleep)
    {
        try
        {
            if (!sleep && __instance.m_sleeping && __instance == Player.m_localPlayer)
            {
                WakeMessages.Clear();
                _restedBeforeWake = __instance.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectRested);
                _recordingWake = true;
            }
        }
        catch (Exception e)
        {
            Log.Error($"{SelfTest.Prefix} wake message recorder failed: {e}");
        }
    }

    private static void WakeCallEnd()
    {
        _recordingWake = false;
    }

    private static void RecordMessage(MessageHud.MessageType type, string text)
    {
        if (_recordingWake)
        {
            WakeMessages.Add(new KeyValuePair<MessageHud.MessageType, string>(type, text));
        }
    }

    private static List<string> CentreMessages()
    {
        var list = new List<string>();
        foreach (var m in WakeMessages)
        {
            if (m.Key == MessageHud.MessageType.Center)
            {
                list.Add(m.Value ?? "");
            }
        }
        return list;
    }

    // [Center "Good evening", Center "You feel rested (Comfort:1)"], localized as player read it.
    private static string DescribeMessages()
    {
        var sb = new StringBuilder("[");
        for (var i = 0; i < WakeMessages.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }
            sb.Append(WakeMessages[i].Key).Append(" \"").Append(Localize(WakeMessages[i].Value)).Append('"');
        }
        return sb.Append(']').ToString();
    }

    private static string Localize(string text) =>
        Localization.instance != null ? Localization.instance.Localize(text ?? "") : text ?? "";

    // Same call Bed.Interact make when every check pass.
    private static void AttachDirect(Player player)
    {
        try
        {
            if (_testBed != null && !player.IsAttached())
            {
                player.AttachStart(_testBed.m_spawnPoint, _testBed.gameObject, true, true, false, "attach_bed",
                    new Vector3(0f, 0.5f, 0f));
            }
        }
        catch (Exception e)
        {
            Log.Error($"{SelfTest.Prefix} direct AttachStart failed: {e}");
        }
    }

    private static bool FlagsMatch(float hour)
    {
        if (hour < DayClock.NoonHour)
        {
            return DayClock.IsMorning();
        }
        return hour < DayClock.NightfallHour ? EnvMan.IsAfternoon() && !EnvMan.IsNight() : EnvMan.IsNight();
    }

    private static double ExpectedWake(Case c, double startTime)
    {
        if (c.ExpectDaySleep)
        {
            Plugin.ReadDaySettings(out var wakeHour, out var includeAfternoon);
            return DayClock.Today(c.Env, startTime, wakeHour, includeAfternoon).Target;
        }
        var len = (double)c.Env.m_dayLengthSec;
        return (c.Day + 1) * len + DayClock.RawFractionForHour(c.Env, DayClock.DawnHour) * len;
    }

    // Raw text of first Center message of wake call: WakeUpMessage after day sleep (when set), else vanilla token.
    private static string ExpectedWakeText(bool daySleep)
    {
        var text = (Plugin.WakeUpMessage.Value ?? "").Trim();
        return daySleep && text.Length > 0 ? text : WakeMessage.GoodMorningToken;
    }

    private static void Report(string name, bool ok, string detail)
    {
        if (ok)
        {
            SelfTest.Pass(name, detail);
        }
        else
        {
            SelfTest.Fail(name, detail);
        }
    }

    private static string CenterText()
    {
        var hud = MessageHud.instance;
        return hud != null && hud.m_messageCenterText != null ? hud.m_messageCenterText.text : "";
    }

    // Test clock: net time jump + snap smoothed clock (else flags need many seconds to follow). Test only.
    private static void SetTime(EnvMan env, ZNet net, double t)
    {
        net.SetNetTime(t);
        var fraction = DayClock.FractionAt(env, t);
        if (!float.IsNaN(fraction))
        {
            env.m_smoothDayFraction = fraction;
        }
    }

    private static string Clock(EnvMan env, double t) => DayClock.ClockText(DayClock.FractionAt(env, t));

    private static void Cleanup()
    {
        var undo = _undo;
        _undo = null;
        if (undo != null)
        {
            undo();
        }
        _woke = false;
        _recordingWake = false;
        WakeMessages.Clear();
    }

    // Each step alone: one failing must not skip the others.
    private static void Undo(Player player, EnvMan env, ZNet net, Game game, PlayerProfile profile, bool hadSpawn,
        Vector3 oldSpawn, double oldTime, double oldWakeup, double oldLastSleep, bool hadRested, Vector3 oldPos,
        Quaternion oldRot)
    {
        // Test cut short while asleep: stop skip and wake NOW, as vanilla stop branch do (SleepStop to everybody:
        // SetSleeping(false) + AttachStop). Else that stop come after Undo and put Rested and wake-up time back.
        // Steps below then undo what the wake did (time, cooldowns, Rested, position).
        Safe("sleep", () =>
        {
            if (env != null && env.IsTimeSkipping())
            {
                env.m_skipTime = false;
            }
            if (ZNet.instance == null || Game.instance == null)
            {
                return; // world gone: nothing to wake
            }
            if (game != null && game.m_sleeping && ZRoutedRpc.instance != null)
            {
                game.m_sleeping = false;
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "SleepStop");
            }
            if (player != null && player.IsSleeping())
            {
                player.SetSleeping(false); // no SleepStop reached this player
            }
        });
        Safe("detach", () =>
        {
            if (player != null && player.IsAttached() && !player.IsSleeping())
            {
                player.AttachStop();
            }
        });
        Safe("bed", () =>
        {
            var bed = _testBed;
            _testBed = null;
            if (bed != null)
            {
                if (ZNetScene.instance != null && bed.m_nview != null && bed.m_nview.IsValid())
                {
                    ZNetScene.instance.Destroy(bed.gameObject);
                }
                else
                {
                    UnityEngine.Object.Destroy(bed.gameObject);
                }
            }
        });
        Safe("bed check bypass and message recorder", () =>
        {
            _bypass?.UnpatchSelf();
            _bypass = null;
        });
        Safe("spawn point", () =>
        {
            if (ZNet.instance == null)
            {
                return; // world gone: profile data of this world not reachable
            }
            if (hadSpawn)
            {
                profile.SetCustomSpawnPoint(oldSpawn);
            }
            else
            {
                profile.ClearCustomSpawnPoint();
            }
        });
        Safe("time", () =>
        {
            if (net == null || env == null || game == null)
            {
                return;
            }
            SetTime(env, net, oldTime);
            game.m_lastSleepTime = oldLastSleep;
            if (player != null)
            {
                player.m_wakeupTime = oldWakeup;
            }
        });
        Safe("rested", () =>
        {
            if (player != null && !hadRested)
            {
                player.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectRested, true);
            }
        });
        Safe("position", () =>
        {
            if (player != null && !player.IsAttached() && !player.IsSleeping())
            {
                player.transform.SetPositionAndRotation(oldPos, oldRot);
                if (player.m_body != null)
                {
                    player.m_body.position = oldPos;
                    player.m_body.linearVelocity = Vector3.zero;
                }
            }
        });
        Safe("setting overrides", () =>
        {
            WakeHourOverride = null;
            IncludeAfternoonOverride = null;
        });
    }

    private static void Safe(string what, Action step)
    {
        try
        {
            step();
        }
        catch (Exception e)
        {
            Log.Error($"{SelfTest.Prefix} cleanup of {what} failed: {e}");
        }
    }
#endif
}
