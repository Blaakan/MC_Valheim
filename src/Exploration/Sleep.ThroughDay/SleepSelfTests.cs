using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using HarmonyLib;
using MC.Shared;
using UnityEngine;
#endif

namespace MC.Exploration.SleepThroughDayMod;

// Debug build only (calls vanish in Release): in-world self tests, run by world probe (tools/Test-InWorld.ps1) in
// throwaway single-player world (local game = server, so server and client part both run), and multiplayer self
// tests, run by tools/Test-Multiplayer.ps1 (one client joined to real dedicated server).
// This file = rig every test stand on. Tests live in SleepSelfTests.Single.cs (every day flows), .Cases.cs (second
// player stand-ins, edge cases, other mods) and .Multiplayer.cs.
// One run: set world time, spawn real "bed", claim it and lie down through vanilla Bed.Interact (roof, fire, enemy
// and wet checks skipped for test beds only, by temporary Harmony prefix), let vanilla server loop + mod start the
// sleep, wait for wake-up, check where time landed, which messages showed in wake-up call (temporary recorder on
// MessageHud.ShowMessage), which lines mod wrote in log (temporary log listener), Rested. Then put world back: player
// awake, out of bed and back in place, bed gone, time, spawn point, cooldowns, Rested as before.
// Settings forced only in memory (overrides, read by Plugin.ReadDaySettings / Plugin.ReadWakeUpMessage), feature
// taken down only in memory (Plugin.TestSetPatched): single-player test never write config file.
internal static partial class SleepSelfTests
{
#if DEBUG
    private const float FlagWaitSeconds = 5f;       // smoothed clock snapped; flags follow at next FixedUpdate
    private const float StartWaitSeconds = 15f;     // server tick every 2 s
    private const float WakeWaitSeconds = 60f;      // skip ~12 s (longer at low frame rate) + next tick
    private const float FastSeconds = 5f;           // test make rest of skip this long (target stay, only speed)
    private const float FastWakeWaitSeconds = 30f;
    private const float PauseAllowance = 30f;       // game paused while test wait (Esc menu): this much not counted
    private const float FadeShotDelay = 2f;
    private const float AwakeShotDelay = 2.5f;
    private const double TargetTolerance = 0.5;     // world seconds (vanilla target math is float)
    private const double WakeLateTolerance = 15.0;  // world seconds after target: stop come at next 2 s tick
    private const float ClockTolerance = 0.002f;    // game clock fraction (0.002 = under 3 clock minutes)
    private const float ClockLate = 0.02f;          // wake may come this much clock after target (2 s tick, night rate)
    private const double VanillaSkipSeconds = 12.0; // EnvMan.c_TimeSkipDuration

    private const string DefaultMessage = "Good evening";         // WakeUpMessage default
    private const string RestedStartToken = "$se_rested_start";    // SE_Rested.Setup message, when Rested is new
    private const string SpawnSetToken = "$msg_spawnpointset";
    private const string CantSleepToken = "$msg_cantsleep";
    private const string NewDayToken = "$msg_newday";              // EnvMan.OnMorning, localized with day number

    // Lines mod write (DaySleep, WakeMessage): start of text.
    private const string StartLine = "Day sleep: everyone is in bed at";
    private const string RetargetLine = "Day sleep: a sleep started at";
    private const string OutsideLine = "Day sleep not started:";
    private const string WokeLine = "Woke up at";
    private const string CycleWarning = "Could not read the day cycle";

    // Set = Plugin.ReadDaySettings / ReadWakeUpMessage use this instead of config value. Memory only.
    internal static float? WakeHourOverride;
    internal static bool? IncludeAfternoonOverride;
    internal static string WakeUpMessageOverride;

    // True = a test took the mod's patches off in memory (Plugin.TestSetPatched): beds are vanilla until it put them back.
    private static bool _patchesOff;

    // True = Unregister do nothing: running test turn feature off and on itself, must keep what it built.
    private static bool _holdTests;

    private static readonly List<Bed> TestBeds = new List<Bed>();              // only these skip vanilla bed checks
    private static readonly List<GameObject> Spawned = new List<GameObject>(); // every object test made
    private static BedCheck _realChecks;  // checks NOT skipped for test beds
    private static Harmony _bypass;       // bed check bypass, recorders, stand-ins: gone in Undo
    private static Action _undo;          // put world back: at test end, at next test start (test cut short), on deactivate
    private static LogWatch _watch;

    // Recorder (patches below). One local player per game.
    private struct Shown
    {
        internal MessageHud.MessageType Type;
        internal string Text; // raw, before MessageHud localize
        internal float Real;
    }

    private static readonly List<Shown> Messages = new List<Shown>();
    private static bool _inWakeCall;
    private static int _wakeFrom;
    private static int _wakeTo;
    private static bool _restedBeforeWake;
    private static string _wakeScreenText; // centre text on screen when wake call end (null = HUD hidden by player)
    private static bool _asleep;
    private static double _sleepTime;
    private static float _sleepReal;
    private static bool _woke;
    private static double _wakeTime;
    private static float _wakeReal;
    private static bool _modWoke; // WakeMessage told me (RecordWake)
    private static bool _modWakeDay;
    private static int _sleepStops;      // Game.SleepStop calls
    private static int _everybodyAsked;  // Game.EverybodyIsTryingToSleep calls
    private static bool? _everybodyForced; // set = answer of that call (stand-in for player who is up / other mod)
    private static bool _noSkip;         // EnvMan.SkipToMorning do nothing (stand-in for other mod)
    private static bool _anyTime;        // Game.UpdateSleeping replaced: vanilla without time test (stand-in for BedRules)
    private static int _cycle;           // EnvMan.RescaleDayFraction changed: 1 = 40 % night, 2 = broken (go down)

    [Flags]
    private enum BedCheck
    {
        None = 0,
        Enemies = 1,
        Exposure = 2,
        Fire = 4,
        Wet = 8,
    }

    private enum Starter
    {
        Mod,      // morning: mod start it (Info line "everyone is in bed")
        Retarget, // game or other mod start it, mod move its end (Info line "a sleep started")
        Vanilla,  // mod no touch: no Info line
    }

    // Settings one test force in memory + how rig is built.
    private sealed class Setup
    {
        internal float WakeHour = DayClock.NightfallHour;
        internal bool Afternoon;
        internal string Message = DefaultMessage;
        internal bool KeepRested;         // Rested on when lying down (added when missing)
        internal string BedPrefab = "bed";
        internal double ExtraSeconds;     // world seconds after start hour
        internal Action<Harmony> Patches; // more temporary patches, on before time is set
    }

    // What one run (lie down -> wake) must do.
    private sealed class Plan
    {
        internal float Hour;       // start hour: say which day flags to wait for
        internal bool Morning;     // end = game's own morning (EnvMan.GetMorningStartSec) of date + EndDay
        internal int EndDay;       // wake date - start date
        internal float EndClock;   // game clock at wake (0.75 = 18:00)
        internal float WakeHour;   // same-day target hour
        internal bool Day;         // mod must call it daytime sleep
        internal string First;     // raw text of first Center message of wake call
        internal Starter Starter;
        internal float Fast = FastSeconds; // 0 = game's own skip length
        internal bool Shots;
        internal bool Screen;      // check black screen and ZZZ
        internal bool CheckLength; // planned skip = vanilla 12 s
        internal bool CheckDelay;  // start within one server tick of lying down
        internal bool InBed;       // player already lying: no flags wait, no lie down
        internal bool SkipFlags;   // flags are what they are (run chained to wake before)
        internal bool ViaPlayer;   // press E through Player.Interact (other mods' prefixes run)
        internal bool ModSilent;   // feature off at wake: mod say nothing

        // Day sleep: end at wakeHour of same date, WakeUpMessage shown.
        internal static Plan ToHour(float hour, float wakeHour, string message = DefaultMessage, Starter starter = Starter.Mod) =>
            new Plan
            {
                Hour = hour,
                EndDay = 0,
                EndClock = wakeHour / 24f,
                WakeHour = wakeHour,
                Day = true,
                First = ShownText(message),
                Starter = starter,
            };

        // Vanilla sleep: next 06:00 (same date when start is before dawn), "Good morning".
        internal static Plan ToMorning(float hour) =>
            new Plan
            {
                Hour = hour,
                Morning = true,
                EndDay = hour < DayClock.DawnHour ? 0 : 1,
                EndClock = 0.25f,
                WakeHour = DayClock.DawnHour,
                Day = false,
                First = WakeMessage.GoodMorningToken,
                Starter = Starter.Vanilla,
            };

        internal Plan Real()
        {
            Fast = 0f;
            return this;
        }
    }

    // World before test.
    private sealed class Snapshot
    {
        internal double Time;
        internal bool HadSpawn;
        internal Vector3 Spawn;
        internal double Wakeup;
        internal double LastSleep;
        internal bool HadRested;
        internal float RestedTime;
        internal float RestedTtl;
        internal Vector3 Pos;
        internal Quaternion Rot;
        internal bool DebugTod;
        internal float DebugTime;
        internal string Dream;
    }

    // What one test need, made by Arm.
    private sealed class Case
    {
        internal string Name;
        internal Player Player;
        internal EnvMan Env;
        internal ZNet Net;
        internal Game Game;
        internal PlayerProfile Profile;
        internal Snapshot Before;
        internal Vector3 Forward;
        internal Bed Bed;
        internal int Day0;       // date test began on
        internal double Length;  // day length, world seconds

        // Current run.
        internal string Tag = "";
        internal int Day;        // date run start on
        internal int MsgMark;
        internal int LogMark;
        internal int ErrMark;
        internal int StopMark;
        internal bool OK = true; // false = step failed, run cannot go on
        internal float LieDownReal;
        internal bool LayDown;   // bed laid player down (attached, in-bed flag on its ZDO)
        internal string AttachAnimation = "";
        internal double StartTime;
        internal float StartReal;
        internal double SkipTo;
        internal bool Skipping;
        internal double PlannedSeconds;
        internal float MaxAlpha;
        internal bool SawZzz;
        internal float BlackAfter = -1f;
        internal float SaveTimerAsleep;
        internal string AllowedWarning; // deliberate warning of this test (start of text)

        // Multiplayer run (this game = client): server own time and sleep, its answers come as key=value text.
        internal bool Remote;
        internal Dictionary<string, string> Answer = new Dictionary<string, string>();
    }
#endif

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        foreach (var t in SingleTests())
        {
            SelfTest.Register(t.Key, t.Value);
        }
        foreach (var t in CaseTests())
        {
            SelfTest.Register(t.Key, t.Value);
        }
        RegisterMultiplayer();
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        if (_holdTests)
        {
            return; // running test turned feature off itself: stay in list, keep bed and recorders
        }
        foreach (var t in SingleTests())
        {
            SelfTest.Unregister(t.Key);
        }
        foreach (var t in CaseTests())
        {
            SelfTest.Unregister(t.Key);
        }
        UnregisterMultiplayer();
        Cleanup();
        ClearOverrides();
#endif
    }

    // WakeMessage call me on every local wake-up it see (SetSleeping postfix). Messages of that call: recorder below.
    [Conditional("DEBUG")]
    internal static void RecordWake(double time, bool daySleep)
    {
#if DEBUG
        _modWoke = true;
        _modWakeDay = daySleep;
#endif
    }

#if DEBUG
    private static KeyValuePair<string, Func<IEnumerator>> T(string name, Func<IEnumerator> run) =>
        new KeyValuePair<string, Func<IEnumerator>>(name, run);

    private static void ClearOverrides()
    {
        WakeHourOverride = null;
        IncludeAfternoonOverride = null;
        WakeUpMessageOverride = null;
    }

    // ---------- rig: build, new run, put back ----------

    // Leftovers of test cut short gone, feature on, sleep that is not ours over (vanilla stop branch end it at next
    // 2 s tick; Undo wake ours at once).
    private static IEnumerator Settle()
    {
        Cleanup();
        if (_patchesOff)
        {
            SetOff(false);
        }
        var settle = Time.realtimeSinceStartup + 6f;
        while (Game.instance != null && Game.instance.m_sleeping && Time.realtimeSinceStartup < settle)
        {
            yield return null;
        }
    }

    // Single player: rig + first run at `hour` of current date. No yield here: can catch.
    private static string Begin(string name, float hour, Setup s, out Case c, string tag = "")
    {
        c = null;
        try
        {
            var net = ZNet.instance;
            if (net == null || !net.IsServer())
            {
                return "needs a single-player world (the local game must be the server)";
            }
            var error = Arm(name, s, out c);
            if (error != null)
            {
                return error;
            }
            SetRun(c, hour, s, tag);
            return null;
        }
        catch (Exception e)
        {
            return $"setup failed: {e}";
        }
    }

    // Save what test change (in _undo), spawn bed, install bypass, recorders and log listener. Time not touched.
    private static string Arm(string name, Setup s, out Case c, bool requireActive = true)
    {
        c = null;
        var player = Player.m_localPlayer;
        var env = EnvMan.instance;
        var net = ZNet.instance;
        var game = Game.instance;
        var scene = ZNetScene.instance;
        if (player == null || env == null || net == null || game == null || scene == null || MessageHud.instance == null
            || ZRoutedRpc.instance == null)
        {
            return "no world or no local player";
        }
        var feature = FeatureRegistry.Find(ModInfo.Guid);
        if (requireActive && (feature == null || !feature.Value.IsActive))
        {
            return $"the mod is not active here ({feature?.State}: {feature?.Status})";
        }
        if (game.m_sleeping || env.IsTimeSkipping() || player.IsSleeping() || player.IsAttached())
        {
            return "a sleep or time skip is already running, or the player is attached to something";
        }
        if (env.m_debugTimeOfDay)
        {
            return "the time of day is forced (console tod); run tod -1 first";
        }
        if (env.m_dayLengthSec <= 0)
        {
            return $"day length is {env.m_dayLengthSec}";
        }
        var prefab = scene.GetPrefab(s.BedPrefab);
        if (prefab == null || prefab.GetComponent<Bed>() == null)
        {
            return $"prefab '{s.BedPrefab}' with a Bed component not found";
        }

        // Save state first: _undo put it back whatever happen next.
        var profile = game.GetPlayerProfile();
        var rested = player.GetSEMan().GetStatusEffect(SEMan.s_statusEffectRested);
        var b = new Snapshot
        {
            Time = net.GetTimeSeconds(),
            HadSpawn = profile.HaveCustomSpawnPoint(),
            Spawn = profile.GetCustomSpawnPoint(),
            Wakeup = player.m_wakeupTime,
            LastSleep = game.m_lastSleepTime,
            HadRested = rested != null,
            RestedTime = rested != null ? rested.m_time : 0f,
            RestedTtl = rested != null ? rested.m_ttl : 0f,
            Pos = player.transform.position,
            Rot = player.transform.rotation,
            DebugTod = env.m_debugTimeOfDay,
            DebugTime = env.m_debugTime,
            Dream = CinematicsManager.m_dreamCinematic,
        };
        _undo = () => Undo(b, player, env, net, game, profile);

        // Pending dream video (boss killed by other test) would play in our sleep and pause the game: hold it back.
        CinematicsManager.m_dreamCinematic = "";

        ResetRecorder();
        _bypass = new Harmony(ModInfo.Guid + ".selftest");
        InstallTestPatches(_bypass);
        s.Patches?.Invoke(_bypass);
        _watch = new LogWatch();
        BepInEx.Logging.Logger.Listeners.Add(_watch);

        var forward = player.transform.forward;
        forward.y = 0f;
        forward = forward.sqrMagnitude < 0.01f ? Vector3.forward : forward.normalized;
        c = new Case
        {
            Name = name,
            Player = player,
            Env = env,
            Net = net,
            Game = game,
            Profile = profile,
            Before = b,
            Forward = forward,
            Day0 = env.GetDay(b.Time),
            Length = env.m_dayLengthSec,
        };
        c.Day = c.Day0;
        c.Bed = SpawnBed(c, s.BedPrefab, 2.5f, 0f);
        return null;
    }

    // Bed `ahead` m in front of where player stood at test start, `side` m to the right, on the ground.
    private static Bed SpawnBed(Case c, string prefabName, float ahead, float side)
    {
        var prefab = ZNetScene.instance.GetPrefab(prefabName);
        if (prefab == null || prefab.GetComponent<Bed>() == null)
        {
            return null;
        }
        var go = SpawnObject(c, prefab, ahead, side);
        var bed = go.GetComponent<Bed>();
        TestBeds.Add(bed);
        return bed;
    }

    private static GameObject SpawnObject(Case c, GameObject prefab, float ahead, float side)
    {
        var right = Vector3.Cross(Vector3.up, c.Forward);
        var pos = c.Before.Pos + c.Forward * ahead + right * side;
        if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(pos, out var ground))
        {
            pos.y = ground;
        }
        var go = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.LookRotation(-c.Forward));
        Spawned.Add(go);
        return go;
    }

    private static void ApplySettings(Setup s)
    {
        WakeHourOverride = s.WakeHour;
        IncludeAfternoonOverride = s.Afternoon;
        WakeUpMessageOverride = s.Message ?? "";
    }

    // Single player: new run at `hour` of date test began on (+ dayOffset). Time jump, cooldowns over, Rested as
    // run want, records fresh.
    private static void SetRun(Case c, float hour, Setup s, string tag, int dayOffset = 0)
    {
        ApplySettings(s);
        var day = c.Day0 + dayOffset;
        var start = day * c.Length + DayClock.RawFractionForHour(c.Env, hour) * c.Length + s.ExtraSeconds;
        SetTime(c.Env, c.Net, start);
        FreshRun(c, s, tag, start);
        Plugin.ReadDaySettings(out var wakeHour, out var afternoon);
        Log.Info($"Self-test {c.Name}{(tag.Length > 0 ? $" [{tag}]" : "")}: world time set to {Clock(c.Env, start)} of day {day}; "
                 + $"WakeUpHour {wakeHour:0.##}, IncludeAfternoon {afternoon}, WakeUpMessage \"{Plugin.ReadWakeUpMessage()}\" "
                 + "(forced in memory for this test, config file untouched).");
    }

    // Player out of bed, 30 s cooldown and 10 s guard over, Rested as run want, marks at "now".
    private static void FreshRun(Case c, Setup s, string tag, double now)
    {
        if (c.Player.IsAttached() && !c.Player.IsSleeping())
        {
            c.Player.AttachStop();
        }
        c.Player.m_wakeupTime = now - 1000.0;
        if (c.Net.IsServer())
        {
            c.Game.m_lastSleepTime = now - 1000.0;
        }
        var seman = c.Player.GetSEMan();
        if (!s.KeepRested)
        {
            seman.RemoveStatusEffect(SEMan.s_statusEffectRested, true);
        }
        else if (!seman.HaveStatusEffect(SEMan.s_statusEffectRested))
        {
            seman.AddStatusEffect(SEMan.s_statusEffectRested, true);
        }
        MarkRun(c, tag, c.Env.GetDay(now));
    }

    // New run begin here: everything recorded before is not its business.
    private static void MarkRun(Case c, string tag, int day)
    {
        c.Tag = tag ?? "";
        c.Day = day;
        c.MsgMark = Messages.Count;
        c.LogMark = _watch != null ? _watch.Mark() : 0;
        c.ErrMark = _watch != null ? _watch.ErrorMark() : 0;
        c.StopMark = _sleepStops;
        c.OK = true;
        c.Skipping = false;
        c.LieDownReal = 0f;
        c.LayDown = false;
        c.MaxAlpha = 0f;
        c.SawZzz = false;
        c.BlackAfter = -1f;
        _asleep = false;
        _woke = false;
        _modWoke = false;
        _wakeFrom = 0;
        _wakeTo = 0;
        _wakeScreenText = null;
    }

    private static void ResetRecorder()
    {
        Messages.Clear();
        _inWakeCall = false;
        _wakeFrom = 0;
        _wakeTo = 0;
        _asleep = false;
        _woke = false;
        _modWoke = false;
        _wakeScreenText = null;
        _sleepStops = 0;
        _everybodyAsked = 0;
        _everybodyForced = null;
        _noSkip = false;
        _anyTime = false;
        _realChecks = BedCheck.None;
    }

    private static void Cleanup()
    {
        var undo = _undo;
        _undo = null;
        if (undo != null)
        {
            undo();
        }
        ResetRecorder();
        _cycle = 0;
    }

    // Each step alone: one failing must not skip the others.
    private static void Undo(Snapshot b, Player player, EnvMan env, ZNet net, Game game, PlayerProfile profile)
    {
        var server = net != null && net.IsServer();
        Safe("forced time of day", () =>
        {
            if (env != null)
            {
                env.m_debugTimeOfDay = b.DebugTod;
                env.m_debugTime = b.DebugTime;
            }
        });
        // Test cut short while asleep: stop skip and wake NOW, as vanilla stop branch do (SleepStop to everybody:
        // SetSleeping(false) + AttachStop). Else that stop come after Undo and put Rested and wake-up time back.
        // Steps below then undo what the wake did (time, cooldowns, Rested, position).
        Safe("sleep", () =>
        {
            if (server && env != null && env.IsTimeSkipping())
            {
                env.m_skipTime = false;
            }
            if (ZNet.instance == null || Game.instance == null)
            {
                return; // world gone: nothing to wake
            }
            if (server && game != null && game.m_sleeping && ZRoutedRpc.instance != null)
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
        Safe("spawned objects", () =>
        {
            var objects = Spawned.ToArray();
            Spawned.Clear();
            TestBeds.Clear();
            foreach (var go in objects)
            {
                if (go == null)
                {
                    continue;
                }
                var view = go.GetComponent<ZNetView>();
                if (ZNetScene.instance != null && view != null && view.IsValid())
                {
                    ZNetScene.instance.Destroy(go);
                }
                else
                {
                    UnityEngine.Object.Destroy(go);
                }
            }
        });
        Safe("bed check bypass, recorders and stand-ins", () =>
        {
            _bypass?.UnpatchSelf();
            _bypass = null;
            _cycle = 0;
        });
        Safe("log listener", () =>
        {
            var watch = _watch;
            _watch = null;
            if (watch != null)
            {
                BepInEx.Logging.Logger.Listeners.Remove(watch);
            }
        });
        Safe("feature back on", () =>
        {
            if (_patchesOff)
            {
                SetOff(false);
            }
        });
        Safe("spawn point", () =>
        {
            if (ZNet.instance == null)
            {
                return; // world gone: profile data of this world not reachable
            }
            if (b.HadSpawn)
            {
                profile.SetCustomSpawnPoint(b.Spawn);
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
            if (server)
            {
                SetTime(env, net, b.Time);
                game.m_lastSleepTime = b.LastSleep;
            }
            if (player != null)
            {
                player.m_wakeupTime = b.Wakeup;
            }
        });
        Safe("rested", () =>
        {
            if (player == null)
            {
                return;
            }
            var seman = player.GetSEMan();
            if (!b.HadRested)
            {
                seman.RemoveStatusEffect(SEMan.s_statusEffectRested, true);
                return;
            }
            if (!seman.HaveStatusEffect(SEMan.s_statusEffectRested))
            {
                seman.AddStatusEffect(SEMan.s_statusEffectRested, true);
            }
            var rested = seman.GetStatusEffect(SEMan.s_statusEffectRested);
            if (rested != null)
            {
                rested.m_ttl = b.RestedTtl;
                rested.m_time = b.RestedTime;
            }
        });
        Safe("position", () =>
        {
            if (player != null && !player.IsAttached() && !player.IsSleeping())
            {
                player.transform.SetPositionAndRotation(b.Pos, b.Rot);
                if (player.m_body != null)
                {
                    player.m_body.position = b.Pos;
                    player.m_body.linearVelocity = Vector3.zero;
                }
            }
        });
        Safe("pending dream", () => CinematicsManager.m_dreamCinematic = b.Dream ?? "");
        Safe("setting overrides", ClearOverrides);
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

    // After Cleanup: world is as before test (T17). Called in same frame as Cleanup.
    private static void CheckRestored(Case c)
    {
        if (c == null)
        {
            return;
        }
        var b = c.Before;
        var problems = new List<string>();
        try
        {
            var p = c.Player;
            if (p == null)
            {
                problems.Add("the player is gone");
            }
            else
            {
                if (p.IsSleeping() || p.IsAttached() || p.InBed())
                {
                    problems.Add("the player is still asleep or in bed");
                }
                var rested = p.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectRested);
                if (rested != b.HadRested)
                {
                    problems.Add($"Rested is {rested}, was {b.HadRested}");
                }
                var moved = Vector3.Distance(p.transform.position, b.Pos);
                if (moved > 1f)
                {
                    problems.Add($"the player is {moved:0.0} m from where it stood");
                }
                if (Math.Abs(p.m_wakeupTime - b.Wakeup) > 0.001)
                {
                    problems.Add("the 30 s bed cooldown time was not put back");
                }
            }
            if (c.Net.IsServer())
            {
                if (c.Game.m_sleeping || c.Env.IsTimeSkipping())
                {
                    problems.Add("a sleep or time skip is still running");
                }
                var dt = c.Net.GetTimeSeconds() - b.Time;
                if (dt < -0.5 || dt > 3.0)
                {
                    problems.Add($"the world time is {dt:0.0} s from where it was");
                }
                if (Math.Abs(c.Game.m_lastSleepTime - b.LastSleep) > 0.001)
                {
                    problems.Add("the 10 s guard between two sleeps was not put back");
                }
            }
            if (c.Profile.HaveCustomSpawnPoint() != b.HadSpawn
                || (b.HadSpawn && Vector3.Distance(c.Profile.GetCustomSpawnPoint(), b.Spawn) > 0.01f))
            {
                problems.Add("the spawn point was not put back");
            }
            if (TestBeds.Count > 0 || Spawned.Count > 0 || _bypass != null || _watch != null)
            {
                problems.Add("test objects, patches or the log listener are still there");
            }
            if (WakeHourOverride.HasValue || IncludeAfternoonOverride.HasValue || WakeUpMessageOverride != null || _patchesOff)
            {
                problems.Add("a forced setting is still set");
            }
            if (c.Env.m_debugTimeOfDay != b.DebugTod)
            {
                problems.Add("the forced time of day was not put back");
            }
            var feature = FeatureRegistry.Find(ModInfo.Guid);
            if (feature == null || !feature.Value.IsActive)
            {
                problems.Add($"the mod is {feature?.State}");
            }
        }
        catch (Exception e)
        {
            problems.Add($"check threw {e.GetType().Name}: {e.Message}");
        }
        Report(c.Name, problems.Count == 0, problems.Count == 0
            ? "world put back after the test: player awake and in place, test bed gone, world time, spawn point, Rested, cooldowns and forced settings as before"
            : "world not put back after the test: " + string.Join("; ", problems.ToArray()));
    }

    // ---------- steps of one run ----------

    private static IEnumerator Sleep(Case c, Plan r)
    {
        yield return FallAsleep(c, r);
        if (!c.OK)
        {
            yield break;
        }
        yield return WakeUp(c, r);
    }

    // Flags follow new time, client say yes, player lie down through the bed, server start the sleep.
    private static IEnumerator FallAsleep(Case c, Plan r)
    {
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
        yield return WaitStart(c, StartWaitSeconds);
        if (!c.OK)
        {
            yield break;
        }
        CheckStart(c, r);
    }

    private static IEnumerator WakeUp(Case c, Plan r)
    {
        if (r.Screen || r.Shots)
        {
            yield return WatchScreen(c, r);
        }
        if (r.Fast > 0f)
        {
            Fast(c, r.Fast);
        }
        yield return WaitWake(c, r.Fast > 0f ? FastWakeWaitSeconds : WakeWaitSeconds, r.Fast);
        if (!c.OK)
        {
            yield break;
        }
        CheckWake(c, r);
        if (r.Shots)
        {
            yield return WaitReal(AwakeShotDelay);
            SelfTest.Screenshot(c.Name, ShotLabel(c, "awake"));
            yield return null;
            yield return null;
        }
    }

    // Wait a moment at least (several FixedUpdate): CanSleep flag computed in same FixedUpdate as day flags, must
    // see new cooldown too. Then until flags say what `hour` say.
    private static IEnumerator WaitFlags(Case c, float hour)
    {
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
            Fail(c, $"the day flags did not follow the time change (clock {Clock(c.Env, c.Net.GetTimeSeconds())}, "
                    + $"afternoon={EnvMan.IsAfternoon()}, night={EnvMan.IsNight()})");
            c.OK = false;
        }
    }

    private static bool FlagsMatch(float hour)
    {
        if (hour < DayClock.DawnHour || hour >= DayClock.NightfallHour)
        {
            return EnvMan.IsNight();
        }
        if (hour < DayClock.NoonHour)
        {
            return DayClock.IsMorning();
        }
        return EnvMan.IsAfternoon() && !EnvMan.IsNight();
    }

    // Client permission (morning: the mod's; afternoon, night: vanilla's).
    private static void CheckCanSleep(Case c, float hour)
    {
        var canSleep = EnvMan.CanSleep();
        if (hour >= DayClock.DawnHour && hour < DayClock.NoonHour)
        {
            Report(c, canSleep, $"EnvMan.CanSleep() at {Clock(c.Env, c.Net.GetTimeSeconds())} is {canSleep} (expected true; vanilla says false in the morning)");
        }
        else if (!canSleep)
        {
            Fail(c, $"EnvMan.CanSleep() is false at {Clock(c.Env, c.Net.GetTimeSeconds())} (vanilla allows it)");
        }
    }

    // E on the bed, as player: Bed.Interact, or Player.Interact (door of the Use key: other mods' prefixes run).
    private static void Use(Case c, Bed bed, bool viaPlayer)
    {
        if (viaPlayer)
        {
            c.Player.Interact(bed.gameObject, false, false);
        }
        else
        {
            bed.Interact(c.Player, false, false);
        }
    }

    // Claim the bed and set spawn point (first E, when bed not current yet), then lie down (next E).
    private static IEnumerator LieDown(Case c, bool viaPlayer = false)
    {
        var bed = c.Bed;
        if (bed == null)
        {
            Fail(c, "the test bed is gone");
            c.OK = false;
            yield break;
        }
        if (!bed.IsCurrent())
        {
            Use(c, bed, viaPlayer);
            yield return null;
            if (c.Bed == null || !c.Bed.IsCurrent())
            {
                Fail(c, "the first bed interaction did not claim the bed as the spawn point");
                c.OK = false;
                yield break;
            }
        }
        Use(c, bed, viaPlayer);
        c.LieDownReal = Time.realtimeSinceStartup;
        c.AttachAnimation = c.Player.m_attachAnimation ?? "";
        yield return null;
        if (c.Player.IsAttached() && c.Player.InBed())
        {
            c.LayDown = true;
            Pass(c, $"lay down through {(viaPlayer ? "Player.Interact (the Use key)" : "Bed.Interact")} at {Clock(c.Env, c.Net.GetTimeSeconds())} "
                    + $"(attach animation \"{c.AttachAnimation}\")");
        }
        else
        {
            Fail(c, $"the bed did not lay the player down at {Clock(c.Env, c.Net.GetTimeSeconds())} "
                    + $"(message: \"{CenterText()}\"); continuing with the same AttachStart call the bed uses");
            AttachDirect(c);
            yield return null;
        }
    }

    // Same call Bed.Interact make when every check pass.
    private static void AttachDirect(Case c)
    {
        try
        {
            if (c.Bed != null && !c.Player.IsAttached())
            {
                c.Player.AttachStart(c.Bed.m_spawnPoint, c.Bed.gameObject, true, true, false, "attach_bed", new Vector3(0f, 0.5f, 0f));
                c.LieDownReal = Time.realtimeSinceStartup;
            }
        }
        catch (Exception e)
        {
            Log.Error($"{SelfTest.Prefix} direct AttachStart failed: {e}");
        }
    }

    // Server loop start the sleep (every 2 s): SleepStart -> SetSleeping(true) -> recorder.
    // Game paused (Esc menu in single player: time scale 0) = no server tick: that time not counted, up to PauseAllowance.
    private static IEnumerator WaitStart(Case c, float seconds)
    {
        var until = Time.realtimeSinceStartup + seconds;
        var paused = 0f;
        while (!_asleep && Time.realtimeSinceStartup < until + Mathf.Min(paused, PauseAllowance))
        {
            if (Time.timeScale <= 0f)
            {
                paused += Time.unscaledDeltaTime;
            }
            yield return null;
        }
        if (!_asleep)
        {
            Fail(c, $"no sleep started within {seconds:0} s (Game.UpdateSleeping; clock {Clock(c.Env, c.Net.GetTimeSeconds())}, "
                    + $"player in bed {c.Player.InBed()}{PausedText(paused)})");
            c.OK = false;
            yield break;
        }
        c.StartTime = _sleepTime;
        c.StartReal = _sleepReal;
        c.SkipTo = c.Env.m_skipToTime;
        c.Skipping = c.Env.IsTimeSkipping();
        c.PlannedSeconds = c.Skipping && c.Env.m_timeSkipSpeed > 0.0 ? (c.SkipTo - c.StartTime) / c.Env.m_timeSkipSpeed : 0.0;
    }

    // Where the skip aim (against game's own clock and morning, not only mod's window math), who started it (mod's
    // Info lines), how long it is planned, how soon after lying down it came.
    private static void CheckStart(Case c, Plan r)
    {
        CheckTarget(c, r);
        CheckStarter(c, r, _watch.Count(c.LogMark, StartLine), _watch.Count(c.LogMark, RetargetLine),
            _watch.First(c.LogMark, StartLine), _watch.First(c.LogMark, RetargetLine));
        if (r.CheckLength)
        {
            Report(c, Math.Abs(c.PlannedSeconds - VanillaSkipSeconds) <= 0.6,
                $"the time skip is planned to take {c.PlannedSeconds:0.0} s of game time (vanilla: {VanillaSkipSeconds:0} s)");
        }
        if (r.CheckDelay && c.LieDownReal > 0f)
        {
            var delay = c.StartReal - c.LieDownReal;
            Report(c, delay <= 3.5f, $"the sleep started {delay:0.0} s after lying down (the server checks every 2 s)");
        }
    }

    private static void CheckTarget(Case c, Plan r)
    {
        var env = c.Env;
        if (r.Morning)
        {
            var day = c.Day + r.EndDay;
            var morning = env.GetMorningStartSec(day);
            Report(c, c.Skipping && Math.Abs(c.SkipTo - morning) <= TargetTolerance,
                $"sleep started at {Clock(env, c.StartTime)}; time skip aims at {Clock(env, c.SkipTo)} of day {env.GetDay(c.SkipTo)} "
                + $"({c.SkipTo:0.#} s; expected the game's own morning of day {day}, EnvMan.GetMorningStartSec = {morning:0.#} s)");
        }
        else
        {
            var clock = GameClock(env, c.SkipTo);
            var ok = c.Skipping && env.GetDay(c.SkipTo) == c.Day + r.EndDay && Mathf.Abs(clock - r.EndClock) <= ClockTolerance;
            if (!c.Remote)
            {
                // Mod's own window math, with settings of this game (on a client: server's settings count, not ours).
                Plugin.ReadDaySettings(out var wakeHour, out var includeAfternoon);
                ok = ok && Math.Abs(c.SkipTo - DayClock.Today(env, c.StartTime, wakeHour, includeAfternoon).Target) <= TargetTolerance;
            }
            var into = "";
            if (VanillaCycle(env))
            {
                var vanilla = c.Day * c.Length + VanillaRaw(r.EndClock) * c.Length;
                ok = ok && Math.Abs(c.SkipTo - vanilla) <= 1.0;
                into = $", {c.SkipTo - c.Day * c.Length:0} s into the day (vanilla day cycle: {vanilla - c.Day * c.Length:0} s)";
            }
            Report(c, ok,
                $"sleep started at {Clock(env, c.StartTime)}; time skip aims at {Clock(env, c.SkipTo)} of day {env.GetDay(c.SkipTo)} "
                + $"(game clock {clock:0.000}{into}; expected {DayClock.HourText(r.WakeHour)} of the same day {c.Day}, clock {r.EndClock:0.000})");
        }
    }

    // Who started the sleep, told by the Info lines the server part wrote since the run began (single player: this
    // game's log; multiplayer: counted on the server).
    private static void CheckStarter(Case c, Plan r, int starts, int retargets, string startLine, string retargetLine)
    {
        var place = c.Remote ? "Info line in the server's log" : "Info line of the mod";
        switch (r.Starter)
        {
            case Starter.Mod:
                var wanted = $"(day {c.Day}). Waking up at {DayClock.HourText(r.WakeHour)}";
                Report(c, starts == 1 && retargets == 0 && startLine != null && startLine.IndexOf(wanted, StringComparison.Ordinal) >= 0,
                    $"{place}: {Quote(startLine)} (expected one \"{StartLine} ... {wanted}\", found {starts}; retarget lines {retargets})");
                break;
            case Starter.Retarget:
                wanted = $"now ends at {DayClock.HourText(r.WakeHour)}";
                Report(c, starts == 0 && retargets == 1 && retargetLine != null && retargetLine.IndexOf(wanted, StringComparison.Ordinal) >= 0,
                    $"{place}: {Quote(retargetLine)} (expected one \"{RetargetLine} ... {wanted}\", found {retargets}; \"everyone is in bed\" lines {starts})");
                break;
            default:
                Report(c, starts == 0 && retargets == 0,
                    $"no \"Day sleep:\" {place} for this sleep (found {starts + retargets})");
                break;
        }
    }

    // Keep the end, shorten the rest of the skip (test time). Mod already aimed it; it never look at it again.
    private static void Fast(Case c, float seconds)
    {
        var env = c.Env;
        if (!env.IsTimeSkipping())
        {
            return;
        }
        var left = env.m_skipToTime - c.Net.GetTimeSeconds();
        if (left > 0.0)
        {
            env.m_timeSkipSpeed = Math.Max(env.m_timeSkipSpeed, left / seconds);
        }
    }

    // Sleep screen while asleep: black screen (Hud.m_loadingScreen alpha, Hud.UpdateBlackScreen) and "ZZZ" object
    // (Hud.m_sleepingProgress). Screenshot while screen fade to black.
    private static IEnumerator WatchScreen(Case c, Plan r)
    {
        var hud = Hud.instance;
        var fade = c.Game.m_fadeTimeSleep;
        var until = c.StartReal + (r.Screen ? Mathf.Clamp(fade + 1.5f, FadeShotDelay + 0.5f, 8f) : FadeShotDelay);
        var shotAt = c.StartReal + FadeShotDelay;
        var shotDone = !r.Shots;
        while (c.Player.IsSleeping() && (Time.realtimeSinceStartup < until || !shotDone))
        {
            if (hud != null && hud.m_loadingScreen != null && hud.m_sleepingProgress != null)
            {
                var alpha = hud.m_loadingScreen.alpha;
                c.MaxAlpha = Mathf.Max(c.MaxAlpha, alpha);
                if (hud.m_sleepingProgress.activeSelf)
                {
                    c.SawZzz = true;
                }
                if (alpha >= 0.99f && c.BlackAfter < 0f)
                {
                    c.BlackAfter = Time.realtimeSinceStartup - c.StartReal;
                }
            }
            if (!shotDone && Time.realtimeSinceStartup >= shotAt)
            {
                shotDone = true;
                SelfTest.Screenshot(c.Name, ShotLabel(c, "fade"));
                yield return null;
            }
            yield return null;
        }
        if (r.Screen)
        {
            Report(c, c.SawZzz && c.BlackAfter >= 0f,
                $"sleep screen: \"ZZZ\" object (Hud.m_sleepingProgress) shown {c.SawZzz}; screen "
                + (c.BlackAfter >= 0f ? $"fully black {c.BlackAfter:0.0} s after falling asleep" : $"never fully black (alpha {c.MaxAlpha:0.00})")
                + $" (the game's fade time is {fade:0.#} s)");
        }
    }

    // Wake-up (server stop branch -> SleepStop -> SetSleeping(false) -> recorder, WakeMessage -> RecordWake).
    // Time skip is no real-time thing: EnvMan.FixedUpdate move it one step (0.02 s x speed) per rendered frame, at
    // most 50 a second, and not at all while game is paused (Esc menu in single player). Under 50 frames a second it
    // run late (run 1: sleep.compat.ratio got 4.3 s of skip in 30 s). So: fast > 0 = me keep the shortened skip on its
    // plan whatever the frame rate; paused time not counted (up to PauseAllowance); failure say frame rate and pause.
    private static IEnumerator WaitWake(Case c, float seconds, float fast = 0f)
    {
        var start = Time.realtimeSinceStartup;
        var until = start + seconds;
        var paused = 0f;
        var frames = 0;
        var rate = 50f;
        while (!_woke && Time.realtimeSinceStartup < until + Mathf.Min(paused, PauseAllowance))
        {
            c.SaveTimerAsleep = c.Game.m_saveTimer;
            var dt = Time.unscaledDeltaTime;
            frames++;
            if (Time.timeScale <= 0f)
            {
                paused += dt;
            }
            else if (dt > 0f)
            {
                rate = Mathf.Lerp(rate, 1f / dt, 0.2f);
                if (fast > 0f)
                {
                    KeepPace(c, start + fast + paused, rate);
                }
            }
            yield return null;
        }
        if (!_woke)
        {
            var real = Mathf.Max(Time.realtimeSinceStartup - start, 0.01f);
            Fail(c, $"the player did not wake up within {seconds:0} s (clock {Clock(c.Env, c.Net.GetTimeSeconds())}, "
                    + $"skipping={c.Env.IsTimeSkipping()}, {frames / real:0} frames a second{PausedText(paused)})");
            c.OK = false;
        }
        else if (paused > 0.5f)
        {
            Note(c, $"the game was paused for {paused:0.0} s while the player slept (menu open?): that time was not counted");
        }
    }

    // Rest of running skip end at real time `end`: speed for the skip steps that will run until then (one per frame,
    // 50 a second at most). Only ever faster, target never touched. Server side only (single player: this game).
    private static void KeepPace(Case c, float end, float frameRate)
    {
        var env = c.Env;
        if (!c.Net.IsServer() || !env.IsTimeSkipping())
        {
            return;
        }
        var left = env.m_skipToTime - c.Net.GetTimeSeconds();
        var steps = Mathf.Max(end - Time.realtimeSinceStartup, 0.25f) * Mathf.Clamp(frameRate, 1f, 1f / Time.fixedDeltaTime);
        if (left > 0.0)
        {
            env.m_timeSkipSpeed = Math.Max(env.m_timeSkipSpeed, left / (steps * Time.fixedDeltaTime));
        }
    }

    private static string PausedText(float paused) =>
        paused > 0.5f ? $", game paused for {paused:0.0} s of the wait (menu open?)" : "";

    // Where and when player woke, what wake-up call showed, what mod said, Rested.
    private static void CheckWake(Case c, Plan r)
    {
        var env = c.Env;
        var wakeDay = env.GetDay(_wakeTime);
        var clock = GameClock(env, _wakeTime);
        var timeOk = wakeDay == c.Day + r.EndDay && clock >= r.EndClock - ClockTolerance && clock <= r.EndClock + ClockLate;
        if (c.Skipping)
        {
            timeOk = timeOk && _wakeTime >= c.SkipTo - TargetTolerance && _wakeTime <= c.SkipTo + WakeLateTolerance;
        }
        Report(c, timeOk,
            $"woke up at {Clock(env, _wakeTime)} of day {wakeDay} (game clock {clock:0.000}; expected {DayClock.ClockText(r.EndClock)} of day "
            + $"{c.Day + r.EndDay}, clock {r.EndClock:0.000}), {_wakeReal - c.StartReal:0.0} s after falling asleep");

        // Vanilla SetSleeping(false): Center "$msg_goodmorning", then Rested start message when Rested is new
        // (it replace the first in same frame). Day sleep: WakeUpMessage in place of the first, order kept.
        var centre = WakeCentre();
        var vanillaText = r.First == WakeMessage.GoodMorningToken;
        var wakeOk = centre.Count > 0 && centre[0] == r.First && (vanillaText || !centre.Contains(WakeMessage.GoodMorningToken));
        var verdictOk = r.ModSilent ? !_modWoke : _modWoke && _modWakeDay == r.Day;
        var verdict = !_modWoke ? "the mod said nothing" : _modWakeDay ? "the mod calls it a daytime sleep" : "the mod calls it a night sleep";
        var wanted = r.ModSilent ? "the mod says nothing (it is off)" : r.Day ? "daytime sleep" : "night sleep";
        Report(c, wakeOk && verdictOk,
            $"wake-up messages {DescribeWake()}, {verdict} (expected \"{Localize(r.First)}\" first"
            + $"{(vanillaText ? "" : " and no \"Good morning\"")}, {wanted})");
        if (_restedBeforeWake)
        {
            Report(c, centre.Count == 1, "Rested was already on before waking up, so vanilla only resets its time and shows no Rested "
                                         + $"message: {centre.Count} center message(s) (expected 1)");
        }
        else
        {
            var restedOk = centre.Count > 1 && centre[1].StartsWith(RestedStartToken, StringComparison.Ordinal);
            Report(c, restedOk, "Rested message right after the wake-up text, as in vanilla: "
                                + $"{(centre.Count > 1 ? $"\"{Localize(centre[1])}\"" : "none")} (expected \"{Localize(RestedStartToken)} ...\")");
        }
        if (_wakeScreenText != null && centre.Count > 0)
        {
            var last = Localize(centre[centre.Count - 1]);
            Report(c, _wakeScreenText == last, $"center text on screen after the wake-up: \"{_wakeScreenText}\" (expected \"{last}\")");
        }

        var rested = c.Player.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectRested);
        Report(c, rested, $"Rested after waking up: {rested}");

        // Debug line of WakeMessage (written whatever the BepInEx log levels are: listener get every level).
        var woke = _watch.Count(c.LogMark, WokeLine);
        if (r.ModSilent)
        {
            Report(c, woke == 0, $"no \"{WokeLine}\" Debug line of the mod (it is off): found {woke}");
        }
        else
        {
            var part = !r.Day ? "night sleep, vanilla message."
                : vanillaText ? "daytime sleep, WakeUpMessage empty, vanilla message kept."
                : $"daytime sleep, message \"{r.First}\" instead of \"Good morning\".";
            Report(c, woke == 1 && _watch.Has(c.LogMark, WokeLine, part),
                $"Debug line of the mod: {Quote(_watch.First(c.LogMark, WokeLine))} (expected one \"{WokeLine} ...: {part}\", found {woke})");
        }
    }

    // "Day N" message (EnvMan.OnMorning, when smoothed clock pass 06:00). expect: wait until it show (up to
    // `seconds` after wake). !expect: wait `seconds` after wake, none of this date or next may have shown.
    private static IEnumerator WaitDayMessage(Case c, float seconds, bool expect)
    {
        var loc = Localization.instance;
        var next = loc.Localize(NewDayToken, (c.Day + 1).ToString());
        var same = loc.Localize(NewDayToken, c.Day.ToString());
        var until = _wakeReal + seconds;
        while (Time.realtimeSinceStartup < until && !(expect && HasCentre(c.MsgMark, next)))
        {
            yield return null;
        }
        if (expect)
        {
            var at = CentreAt(c.MsgMark, next);
            Report(c, at >= 0f, at >= 0f
                ? $"\"{next}\" message shown {at - _wakeReal:0.0} s after waking up (the game's own new-day message)"
                : $"no \"{next}\" message within {seconds:0} s after waking up");
        }
        else
        {
            var shown = HasCentre(c.MsgMark, next) || HasCentre(c.MsgMark, same);
            Report(c, !shown, $"no \"{next}\" (or \"{same}\") message from falling asleep until {seconds:0} s after waking up: "
                              + (shown ? "it was shown" : "none shown"));
        }
    }

    // End of a test, before Cleanup: mod wrote no warning and no error (T16), day cycle read from the game.
    private static void EndChecks(Case c)
    {
        c.Tag = "";
        var problems = _watch.Problems(c.AllowedWarning);
        Report(c, problems.Count == 0 && !DayClock.WarnedFallback,
            $"log of the mod during this test: {problems.Count} warning or error line(s)"
            + (problems.Count > 0 ? $", first: {Quote(problems[0])}" : "")
            + $"; day-cycle fallback warning shown: {DayClock.WarnedFallback} (expected none)");
    }

    // Mod down / up in memory: the two steps framework do when Enabled change (OnDeactivated + patches gone, patches
    // on + OnActivated), nothing written to config. Framework still say "Active" meanwhile: tests look at patches.
    // Unregister (called by OnDeactivated) must not clean up under the running test.
    private static void SetOff(bool off)
    {
        if (off == _patchesOff)
        {
            return;
        }
        if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(ModInfo.Guid, out var info) || !(info.Instance is Plugin plugin))
        {
            throw new InvalidOperationException("plugin instance not found");
        }
        var hold = _holdTests;
        _holdTests = true;
        try
        {
            plugin.TestSetPatched(!off);
            _patchesOff = off;
        }
        finally
        {
            _holdTests = hold;
        }
    }

    // Methods that carry a patch of the mod itself (not of this test rig, not of framework handshake).
    private static int PatchedByMod()
    {
        var count = 0;
        foreach (var method in Harmony.GetAllPatchedMethods())
        {
            var info = Harmony.GetPatchInfo(method);
            if (info != null && (Owns(info.Prefixes, ModInfo.Guid) || Owns(info.Postfixes, ModInfo.Guid)
                                 || Owns(info.Transpilers, ModInfo.Guid) || Owns(info.Finalizers, ModInfo.Guid)))
            {
                count++;
            }
        }
        return count;
    }

    private static bool Owns(IEnumerable<Patch> patches, string owner)
    {
        if (patches == null)
        {
            return false;
        }
        foreach (var p in patches)
        {
            if (p != null && p.owner == owner)
            {
                return true;
            }
        }
        return false;
    }

    // ---------- temporary patches ----------

    private static void InstallTestPatches(Harmony h)
    {
        var self = typeof(SleepSelfTests);
        // Roof, fire, enemy, wet checks: true for test beds only. Time check (EnvMan.CanSleep) stay real.
        h.Patch(Find(typeof(Bed), nameof(Bed.CheckEnemies)), prefix: new HarmonyMethod(self, nameof(BypassEnemies)));
        h.Patch(Find(typeof(Bed), nameof(Bed.CheckExposure)), prefix: new HarmonyMethod(self, nameof(BypassExposure)));
        h.Patch(Find(typeof(Bed), nameof(Bed.CheckFire)), prefix: new HarmonyMethod(self, nameof(BypassFire)));
        h.Patch(Find(typeof(Bed), nameof(Bed.CheckWet)), prefix: new HarmonyMethod(self, nameof(BypassWet)));

        // Sleep recorder: SetSleeping first prefix / last postfix frame the call (mod's own patches run inside),
        // ShowMessage prefix record what reach HUD (after the mod's swap in Player.Message).
        h.Patch(Find(typeof(Player), nameof(Player.SetSleeping)),
            prefix: new HarmonyMethod(self, nameof(SleepCallBegin)) { priority = Priority.First },
            postfix: new HarmonyMethod(self, nameof(SleepCallEnd)) { priority = Priority.Last });
        h.Patch(Find(typeof(MessageHud), nameof(MessageHud.ShowMessage)), prefix: new HarmonyMethod(self, nameof(RecordMessage)));
        h.Patch(Find(typeof(Game), nameof(Game.SleepStop)), postfix: new HarmonyMethod(self, nameof(CountSleepStop)));
        // Count "is everybody in bed?" questions; answer forced only while a test set _everybodyForced.
        h.Patch(Find(typeof(Game), nameof(Game.EverybodyIsTryingToSleep)), postfix: new HarmonyMethod(self, nameof(Everybody)));
    }

    private static MethodInfo Find(Type type, string name) =>
        AccessTools.Method(type, name) ?? throw new MissingMethodException(type.Name, name);

    private static bool Bypass(Bed bed, BedCheck check, ref bool result)
    {
        if (bed == null || !TestBeds.Contains(bed) || (_realChecks & check) != 0)
        {
            return true;
        }
        result = true;
        return false;
    }

    private static bool BypassEnemies(Bed __instance, ref bool __result) => Bypass(__instance, BedCheck.Enemies, ref __result);

    private static bool BypassExposure(Bed __instance, ref bool __result) => Bypass(__instance, BedCheck.Exposure, ref __result);

    private static bool BypassFire(Bed __instance, ref bool __result) => Bypass(__instance, BedCheck.Fire, ref __result);

    private static bool BypassWet(Bed __instance, ref bool __result) => Bypass(__instance, BedCheck.Wet, ref __result);

    // Local player, call say wake while asleep: messages from here to end of call = wake-up messages.
    private static void SleepCallBegin(Player __instance, bool sleep, out bool __state)
    {
        __state = false;
        try
        {
            if (__instance != Player.m_localPlayer)
            {
                return;
            }
            __state = __instance.m_sleeping;
            if (!sleep && __instance.m_sleeping)
            {
                _wakeFrom = Messages.Count;
                _wakeTo = _wakeFrom;
                _restedBeforeWake = __instance.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectRested);
                _inWakeCall = true;
            }
        }
        catch (Exception e)
        {
            Log.Error($"{SelfTest.Prefix} sleep recorder failed: {e}");
        }
    }

    private static void SleepCallEnd(Player __instance, bool __state)
    {
        try
        {
            var inWake = _inWakeCall;
            _inWakeCall = false;
            if (__instance != Player.m_localPlayer || ZNet.instance == null)
            {
                return;
            }
            if (inWake)
            {
                _wakeTo = Messages.Count;
            }
            if (__state && !__instance.m_sleeping)
            {
                _wakeTime = ZNet.instance.GetTimeSeconds();
                _wakeReal = Time.realtimeSinceStartup;
                _wakeScreenText = Hud.IsUserHidden() ? null : CenterText();
                _woke = true;
            }
            else if (!__state && __instance.m_sleeping)
            {
                _sleepTime = ZNet.instance.GetTimeSeconds();
                _sleepReal = Time.realtimeSinceStartup;
                _asleep = true;
            }
        }
        catch (Exception e)
        {
            Log.Error($"{SelfTest.Prefix} sleep recorder failed: {e}");
        }
    }

    private static void RecordMessage(MessageHud.MessageType type, string text)
    {
        Messages.Add(new Shown { Type = type, Text = text ?? "", Real = Time.realtimeSinceStartup });
    }

    private static void CountSleepStop()
    {
        _sleepStops++;
    }

    private static void Everybody(ref bool __result)
    {
        _everybodyAsked++;
        if (_everybodyForced.HasValue)
        {
            __result = _everybodyForced.Value;
        }
    }

    // ---------- log listener ----------

    // Me hear every BepInEx log line while a test run (every level, whatever console/disk filters say). Me keep
    // lines of this mod, and error lines of anybody. Log events can come from any thread: me lock.
    private sealed class LogWatch : ILogListener
    {
        private readonly object _gate = new object();
        private readonly List<KeyValuePair<LogLevel, string>> _mod = new List<KeyValuePair<LogLevel, string>>();
        private readonly List<string> _errors = new List<string>();
        private int _savedRecently;

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            try
            {
                if (eventArgs == null)
                {
                    return;
                }
                var text = eventArgs.Data as string ?? eventArgs.Data?.ToString();
                if (text == null)
                {
                    return;
                }
                var source = eventArgs.Source != null ? eventArgs.Source.SourceName : "";
                lock (_gate)
                {
                    if (source == ModInfo.Name)
                    {
                        _mod.Add(new KeyValuePair<LogLevel, string>(eventArgs.Level, text));
                    }
                    if ((eventArgs.Level & (LogLevel.Error | LogLevel.Fatal)) != 0 && !text.StartsWith(SelfTest.Prefix, StringComparison.Ordinal))
                    {
                        var cut = text.IndexOf('\n');
                        _errors.Add(source + ": " + (cut > 0 ? text.Substring(0, cut).TrimEnd() : text));
                    }
                    if (text.IndexOf("Saved recently, skipping sleep save", StringComparison.Ordinal) >= 0)
                    {
                        _savedRecently++;
                    }
                }
            }
            catch
            {
                // Me never throw inside logger.
            }
        }

        public void Dispose()
        {
        }

        internal int Mark()
        {
            lock (_gate)
            {
                return _mod.Count;
            }
        }

        internal int ErrorMark()
        {
            lock (_gate)
            {
                return _errors.Count;
            }
        }

        internal int SavedRecently
        {
            get
            {
                lock (_gate)
                {
                    return _savedRecently;
                }
            }
        }

        // Lines of the mod from mark on that start with `start`.
        internal int Count(int from, string start)
        {
            lock (_gate)
            {
                var n = 0;
                for (var i = Math.Max(0, from); i < _mod.Count; i++)
                {
                    if (_mod[i].Value.StartsWith(start, StringComparison.Ordinal))
                    {
                        n++;
                    }
                }
                return n;
            }
        }

        internal bool Has(int from, string start, string part)
        {
            lock (_gate)
            {
                for (var i = Math.Max(0, from); i < _mod.Count; i++)
                {
                    if (_mod[i].Value.StartsWith(start, StringComparison.Ordinal) && _mod[i].Value.IndexOf(part, StringComparison.Ordinal) >= 0)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        internal string First(int from, string start)
        {
            lock (_gate)
            {
                for (var i = Math.Max(0, from); i < _mod.Count; i++)
                {
                    if (_mod[i].Value.StartsWith(start, StringComparison.Ordinal))
                    {
                        return _mod[i].Value;
                    }
                }
                return null;
            }
        }

        // Warning and error lines of the mod (test result lines and one allowed warning left out).
        internal List<string> Problems(string allowedStart)
        {
            lock (_gate)
            {
                var list = new List<string>();
                foreach (var line in _mod)
                {
                    if ((line.Key & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) == 0
                        || line.Value.StartsWith(SelfTest.Prefix, StringComparison.Ordinal)
                        || (allowedStart != null && line.Value.StartsWith(allowedStart, StringComparison.Ordinal)))
                    {
                        continue;
                    }
                    list.Add(line.Value);
                }
                return list;
            }
        }

        // Error lines of any mod or the game from mark on (test FAIL lines left out).
        internal List<string> Errors(int from)
        {
            lock (_gate)
            {
                var list = new List<string>();
                for (var i = Math.Max(0, from); i < _errors.Count; i++)
                {
                    list.Add(_errors[i]);
                }
                return list;
            }
        }
    }

    // ---------- small helpers ----------

    private static IEnumerator WaitReal(float seconds)
    {
        var until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until)
        {
            yield return null;
        }
    }

    // Day flags and CanSleep are computed in EnvMan.FixedUpdate: let a few pass.
    private static IEnumerator FixedSteps(int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return new WaitForFixedUpdate();
        }
        yield return null;
    }

    private static List<string> WakeCentre()
    {
        var list = new List<string>();
        for (var i = _wakeFrom; i < _wakeTo && i < Messages.Count; i++)
        {
            if (Messages[i].Type == MessageHud.MessageType.Center)
            {
                list.Add(Messages[i].Text);
            }
        }
        return list;
    }

    // [Center "Good evening", Center "You feel rested (Comfort:1)"], localized as player read it.
    private static string DescribeWake()
    {
        var sb = new StringBuilder("[");
        for (var i = _wakeFrom; i < _wakeTo && i < Messages.Count; i++)
        {
            if (sb.Length > 1)
            {
                sb.Append(", ");
            }
            sb.Append(Messages[i].Type).Append(" \"").Append(Localize(Messages[i].Text)).Append('"');
        }
        return sb.Append(']').ToString();
    }

    // Raw text of last Center message since mark (null = none).
    private static string LastCentre(int from)
    {
        for (var i = Messages.Count - 1; i >= from && i >= 0; i--)
        {
            if (Messages[i].Type == MessageHud.MessageType.Center)
            {
                return Messages[i].Text;
            }
        }
        return null;
    }

    private static bool HasCentre(int from, string text) => CentreAt(from, text) >= 0f;

    // Real time a Center message with this text (raw or as shown) came since mark; -1 = never.
    private static float CentreAt(int from, string text)
    {
        for (var i = Math.Max(0, from); i < Messages.Count; i++)
        {
            if (Messages[i].Type == MessageHud.MessageType.Center && (Messages[i].Text == text || Localize(Messages[i].Text) == text))
            {
                return Messages[i].Real;
            }
        }
        return -1f;
    }

    // Raw text of first Center message of wake call: WakeUpMessage after day sleep (when not empty), else vanilla token.
    private static string ShownText(string message)
    {
        var text = (message ?? "").Trim();
        return text.Length > 0 ? text : WakeMessage.GoodMorningToken;
    }

    private static string Localize(string text) =>
        Localization.instance != null ? Localization.instance.Localize(text ?? "") : text ?? "";

    private static string Quote(string text) => text == null ? "none" : "\"" + text + "\"";

    private static string CenterText()
    {
        var hud = MessageHud.instance;
        return hud != null && hud.m_messageCenterText != null ? hud.m_messageCenterText.text : "";
    }

    private static string ShotLabel(Case c, string label) => c.Tag.Length > 0 ? c.Tag + "-" + label : label;

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

    // Game clock (0.25 = 06:00) of raw world time, computed the way EnvMan.FixedUpdate do it (not with DayClock).
    private static float GameClock(EnvMan env, double t)
    {
        var len = env.m_dayLengthSec;
        var raw = Mathf.Clamp01((float)(t * 1000.0 % (len * 1000L) / 1000.0) / len);
        return env.RescaleDayFraction(raw);
    }

    // Game run its own day cycle (night 30 % of the day): no day-length mod, no test stand-in.
    private static bool VanillaCycle(EnvMan env) =>
        Mathf.Abs(env.RescaleDayFraction(0.15f) - 0.25f) < 0.0001f && Mathf.Abs(env.RescaleDayFraction(0.5f) - 0.5f) < 0.0001f
        && Mathf.Abs(env.RescaleDayFraction(0.85f) - 0.75f) < 0.0001f;

    // Part of the day (0-1 of day length) at which vanilla clock show `clock`. Numbers of vanilla
    // EnvMan.RescaleDayFraction, written again here on purpose: check not made with mod's own maths.
    private static float VanillaRaw(float clock)
    {
        if (clock < 0.25f)
        {
            return clock * 0.6f;
        }
        return clock <= 0.75f ? 0.15f + (clock - 0.25f) * 1.4f : 0.85f + (clock - 0.75f) * 0.6f;
    }

    private static string Clock(EnvMan env, double t) => DayClock.ClockText(DayClock.FractionAt(env, t));

    private static string Tagged(Case c, string detail) => c.Tag.Length > 0 ? $"[{c.Tag}] {detail}" : detail;

    private static void Pass(Case c, string detail) => SelfTest.Pass(c.Name, Tagged(c, detail));

    private static void Fail(Case c, string detail) => SelfTest.Fail(c.Name, Tagged(c, detail));

    private static void Note(Case c, string detail) => SelfTest.Note(c.Name, Tagged(c, detail));

    private static void Report(Case c, bool ok, string detail) => Report(c.Name, ok, Tagged(c, detail));

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
#endif
}
