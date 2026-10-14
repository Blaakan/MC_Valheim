using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
#if DEBUG
using System.IO;
using UnityEngine;
#endif

namespace MC.Shared;

// Debug build only (calls vanish in Release). In-world self tests: mod register coroutine here (usually in
// OnActivated, remove in OnDeactivated). World probe (tests/Probes/Core.Probe.World, run by tools/Test-InWorld.ps1)
// load throwaway world, then run every registered test one after other, with timeout, catch what they throw.
// Me keep list in AppDomain slot with BCL types only: every mod carry own copy of this class (like FeatureRegistry).
// Test tell result with Pass / Fail / Note: log line "[selftest] PASS|FAIL|NOTE <name>: <detail>", script read them.
// Test must leave world like before (destroy what it spawn, put back time, close window it open).
// How to write one: docs/modding/framework.md, section "In-world self-tests".
// Multiplayer (tools/Test-Multiplayer.ps1): client joined to dedicated server, both with probe. Mod register client
// test with RegisterMultiplayer (run only in its scenario) and server half with RegisterServerStep; client test call
// server half with CallServer. Same docs, section "Multiplayer self-tests".
// Staged tests (single-player run only, after the normal tests): RegisterRelog = test go through a real log out to
// main menu and load of same world; RegisterDisabledStart = test go through a real game restart with every MC mod
// Enabled = false. Carry / Carried keep a value over that restart. Same docs, section "Staged tests".
// Real input: Key, KeyTap, MouseWheel, MouseMove, Pad, PadStick, PadTap, ReleaseInput change device state of
// keyboard, mouse and a test gamepad (probe do it), so game and mod read a press like from a person.
internal static class SelfTest
{
    // Slot names never change: probe and every mod read same slot.
    internal const string TestsSlot = "MC.SelfTest.Tests.v1";     // List<KeyValuePair<string, Func<IEnumerator>>>
    internal const string ShotDirSlot = "MC.SelfTest.ShotDir.v1"; // string: folder for screenshots, set by probe
    // List<Dictionary<string, object>>: "name" string, "scenario" string, "run" Func<IEnumerator>. Client side.
    internal const string MpTestsSlot = "MC.SelfTest.MpTests.v1";
    // Dictionary<string, Func<string, object[], IEnumerator>>: server half by name. Step get arg, put answer in
    // object[2] (bool ok, string detail) with Answer.
    internal const string ServerStepsSlot = "MC.SelfTest.ServerSteps.v1";
    // Func<string, string, object[], IEnumerator>: set by client probe in multiplayer run. (step, arg, reply[3]):
    // reply[0] bool answered, [1] bool ok, [2] string detail.
    internal const string ServerCallSlot = "MC.SelfTest.ServerCall.v1";
    // string: scenario of this run, set by probe. Missing/empty = single-player run (Test-InWorld).
    internal const string ScenarioSlot = "MC.SelfTest.Scenario.v1";
    // List<Dictionary<string, object>>: "name" string, "before" / "menu" / "after" Func<IEnumerator> (each may be null).
    internal const string RelogTestsSlot = "MC.SelfTest.RelogTests.v1";
    // List<Dictionary<string, object>>: "name" string, "leave" / "check" Func<IEnumerator> (leave may be null).
    internal const string DisabledTestsSlot = "MC.SelfTest.DisabledTests.v1";
    // string: stage of this game process, set by probe. "" = first game of a run, DisabledStage = second game.
    internal const string StageSlot = "MC.SelfTest.Stage.v1";
    // string: run folder, set by probe (Carry files live there, same folder for both games of a run).
    internal const string RunDirSlot = "MC.SelfTest.RunDir.v1";
    // Func<string, object[], bool>: probe's input driver (op, args). Missing = no probe run: input calls say false.
    internal const string InputSlot = "MC.SelfTest.Input.v1";
    internal const string DisabledStage = "disabled";
    internal const string Prefix = "[selftest]";

    // Multiplayer scenarios (tools/Test-Multiplayer.ps1 -Scenario). Modded = server and client run every MC mod.
    internal const string Modded = "modded";

    [Conditional("DEBUG")]
    internal static void Register(string name, Func<IEnumerator> run)
    {
#if DEBUG
        if (string.IsNullOrEmpty(name) || run == null)
        {
            return;
        }
        var list = GetTests();
        lock (list)
        {
            list.RemoveAll(t => t.Key == name);
            list.Add(new KeyValuePair<string, Func<IEnumerator>>(name, run));
        }
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister(string name)
    {
#if DEBUG
        var list = GetTests();
        lock (list)
        {
            list.RemoveAll(t => t.Key == name);
        }
#endif
    }

    // Client test for multiplayer run: run only when client joined dedicated server in this scenario.
    [Conditional("DEBUG")]
    internal static void RegisterMultiplayer(string name, string scenario, Func<IEnumerator> run)
    {
#if DEBUG
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(scenario) || run == null)
        {
            return;
        }
        var list = GetMultiplayerTests();
        lock (list)
        {
            list.RemoveAll(t => t.TryGetValue("name", out var n) && (string)n == name);
            list.Add(new Dictionary<string, object> { ["name"] = name, ["scenario"] = scenario, ["run"] = run });
        }
#endif
    }

    [Conditional("DEBUG")]
    internal static void UnregisterMultiplayer(string name)
    {
#if DEBUG
        var list = GetMultiplayerTests();
        lock (list)
        {
            list.RemoveAll(t => t.TryGetValue("name", out var n) && (string)n == name);
        }
#endif
    }

    // Relog test (single-player run, after the normal tests, one real relog per test): before run in the world, then
    // probe log out to main menu like Esc > Log out (world and character saved), run atMenu there, start same world
    // with same character again, wait spawn, run after. Each part may be null. Statics of the mod live on (same game
    // process); objects of the world are gone. after must clean up what before left.
    [Conditional("DEBUG")]
    internal static void RegisterRelog(string name, Func<IEnumerator> before, Func<IEnumerator> atMenu, Func<IEnumerator> after)
    {
#if DEBUG
        if (string.IsNullOrEmpty(name) || (before == null && atMenu == null && after == null))
        {
            return;
        }
        var list = GetRelogTests();
        lock (list)
        {
            list.RemoveAll(t => t.TryGetValue("name", out var n) && (string)n == name);
            list.Add(new Dictionary<string, object> { ["name"] = name, ["before"] = before, ["menu"] = atMenu, ["after"] = after });
        }
#endif
    }

    [Conditional("DEBUG")]
    internal static void UnregisterRelog(string name)
    {
#if DEBUG
        var list = GetRelogTests();
        lock (list)
        {
            list.RemoveAll(t => t.TryGetValue("name", out var n) && (string)n == name);
        }
#endif
    }

    // Disabled-start test: leave run at end of first game (mod on: put things in world or inventory, null = nothing),
    // world and character saved, game closed, every MC mod set Enabled = false, game started again, same world loaded,
    // check run there (mod off from the start). Register from BindConfig: OnActivated never run in the second game.
    // Never unregistered. Values for check that are not in the save: Carry in leave, Carried in check.
    [Conditional("DEBUG")]
    internal static void RegisterDisabledStart(string name, Func<IEnumerator> leave, Func<IEnumerator> check)
    {
#if DEBUG
        if (string.IsNullOrEmpty(name) || check == null)
        {
            return;
        }
        var list = GetDisabledTests();
        lock (list)
        {
            list.RemoveAll(t => t.TryGetValue("name", out var n) && (string)n == name);
            list.Add(new Dictionary<string, object> { ["name"] = name, ["leave"] = leave, ["check"] = check });
        }
#endif
    }

    // Server half of a multiplayer test. Server probe run it when client test call CallServer(name, arg, ...).
    // Step must call Answer(reply, ok, detail) before end (no answer = probe answer "no answer").
    [Conditional("DEBUG")]
    internal static void RegisterServerStep(string name, Func<string, object[], IEnumerator> step)
    {
#if DEBUG
        if (string.IsNullOrEmpty(name) || step == null)
        {
            return;
        }
        var steps = GetServerSteps();
        lock (steps)
        {
            steps[name] = step;
        }
#endif
    }

    [Conditional("DEBUG")]
    internal static void UnregisterServerStep(string name)
    {
#if DEBUG
        var steps = GetServerSteps();
        lock (steps)
        {
            steps.Remove(name);
        }
#endif
    }

    [Conditional("DEBUG")]
    internal static void Answer(object[] reply, bool ok, string detail)
    {
        if (reply != null && reply.Length >= 2)
        {
            reply[0] = ok;
            reply[1] = detail ?? "";
        }
    }

    [Conditional("DEBUG")]
    internal static void Pass(string name, string detail) => Log.Info($"{Prefix} PASS {name}: {detail}");

    [Conditional("DEBUG")]
    internal static void Fail(string name, string detail) => Log.Error($"{Prefix} FAIL {name}: {detail}");

    [Conditional("DEBUG")]
    internal static void Note(string name, string detail) => Log.Info($"{Prefix} NOTE {name}: {detail}");

    // Premise of the test does not hold on this PC and the test cannot make it hold (player's own settings are not
    // the ones the item is about, an external mod is not installed): say why and end the test. A test that only
    // skipped is neither passed nor failed: it counts as not run, its TESTING.md item stays for a person. Never for
    // a check that could not be set up by bad luck (that is a Fail), never after a Pass or Fail of the same test.
    [Conditional("DEBUG")]
    internal static void Skip(string name, string why) => Log.Info($"{Prefix} SKIP {name}: {why}");

    // Screenshot of end of this frame (yield one frame after, before change screen again). Only while probe run.
    // One shot per frame: Unity keep only last request of a frame. Same name + label again = file overwritten.
    [Conditional("DEBUG")]
    internal static void Screenshot(string name, string label)
    {
#if DEBUG
        var path = ShotPath(name, label);
        if (path == null)
        {
            return;
        }
        ScreenCapture.CaptureScreenshot(path);
        Log.Info($"{Prefix} SHOT {name}: {path}");
#endif
    }

#if DEBUG
    // Probe read list with this too (same slot).
    internal static List<KeyValuePair<string, Func<IEnumerator>>> GetTests() => Slot(TestsSlot, () => new List<KeyValuePair<string, Func<IEnumerator>>>());

    internal static List<Dictionary<string, object>> GetRelogTests() => Slot(RelogTestsSlot, () => new List<Dictionary<string, object>>());

    internal static List<Dictionary<string, object>> GetDisabledTests() => Slot(DisabledTestsSlot, () => new List<Dictionary<string, object>>());

    // Stage of this game process: "" = first game of the run (mods as configured), DisabledStage = second game
    // (every MC mod Enabled = false, only disabled-start checks run).
    internal static string Stage => AppDomain.CurrentDomain.GetData(StageSlot) as string ?? "";

    // Value kept from first game to second game of one run (file in run folder). False / null = no probe run.
    internal static bool Carry(string key, string value)
    {
        var path = CarryPath(key);
        if (path == null)
        {
            return false;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, value ?? "");
        return true;
    }

    internal static string Carried(string key)
    {
        var path = CarryPath(key);
        return path != null && File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static string CarryPath(string key)
    {
        var dir = AppDomain.CurrentDomain.GetData(RunDirSlot) as string;
        return string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(key) ? null : Path.Combine(dir, "carry", Safe(key, "key") + ".txt");
    }

    // ---- Real input (probe run only; every call say false when no probe drive the game) ----
    // Device state change like a person's press: ZInput (keys, buttons, sticks), Unity InputSystem actions and every
    // other reader of the device see it from the next frame. Game window need no focus. Always ReleaseInput in finally.

    // Keyboard key or mouse button (KeyCode.Mouse0 .. Mouse4) down / up. Stay so until changed.
    internal static bool Key(KeyCode key, bool down) => SendInput("key", key, down);

    // Down, hold (real time), up, one more frame.
    internal static IEnumerator KeyTap(KeyCode key, float holdSeconds = 0.15f)
    {
        Key(key, true);
        yield return new WaitForSecondsRealtime(holdSeconds);
        Key(key, false);
        yield return null;
    }

    // Mouse wheel for one frame: +1 = one notch up.
    internal static bool MouseWheel(float notches) => SendInput("wheel", notches);

    // Mouse movement for one frame (pixels).
    internal static bool MouseMove(Vector2 delta) => SendInput("move", delta);

    // Test gamepad (made on first call; game switch to gamepad input like with a real one). control: South, East,
    // West, North (A, B, X, Y of an Xbox pad), LeftShoulder, RightShoulder, LeftStick, RightStick (stick press),
    // Start, Select, DpadUp, DpadDown, DpadLeft, DpadRight, LeftTrigger, RightTrigger (value 0..1). Buttons: > 0.5 = down.
    internal static bool Pad(string control, float value) => SendInput("pad", control, value);

    internal static bool PadStick(bool right, Vector2 value) => SendInput("stick", right, value);

    internal static IEnumerator PadTap(string control, float holdSeconds = 0.15f)
    {
        Pad(control, 1f);
        yield return new WaitForSecondsRealtime(holdSeconds);
        Pad(control, 0f);
        yield return null;
    }

    // Every key, button, trigger and stick this run pressed go up; test gamepad removed (game back on keyboard and
    // mouse after the next keyboard or mouse input, which this call also gives).
    internal static bool ReleaseInput() => SendInput("release");

    private static bool SendInput(string op, params object[] args) =>
        AppDomain.CurrentDomain.GetData(InputSlot) is Func<string, object[], bool> driver && driver(op, args);

    internal static List<Dictionary<string, object>> GetMultiplayerTests() => Slot(MpTestsSlot, () => new List<Dictionary<string, object>>());

    internal static Dictionary<string, Func<string, object[], IEnumerator>> GetServerSteps() =>
        Slot(ServerStepsSlot, () => new Dictionary<string, Func<string, object[], IEnumerator>>());

    // Scenario of this run: "" = single player (Test-InWorld), else Test-Multiplayer scenario (client and server).
    internal static string Scenario => AppDomain.CurrentDomain.GetData(ScenarioSlot) as string ?? "";

    internal static bool IsMultiplayerRun => Scenario.Length > 0;

    // Client test: run server step <step> with <arg>, wait answer (or probe timeout). Use:
    //   var reply = new ServerReply(); yield return SelfTest.CallServer("x.step", "arg", reply);
    //   if (!reply.Answered || !reply.Ok) ... reply.Detail
    internal static IEnumerator CallServer(string step, string arg, ServerReply reply)
    {
        if (!(AppDomain.CurrentDomain.GetData(ServerCallSlot) is Func<string, string, object[], IEnumerator> call))
        {
            reply.Answered = false;
            reply.Ok = false;
            reply.Detail = "no server to call (not a multiplayer run)";
            yield break;
        }
        var raw = new object[] { false, false, "" };
        yield return call(step, arg ?? "", raw);
        reply.Answered = raw[0] is bool a && a;
        reply.Ok = raw[1] is bool o && o;
        reply.Detail = raw[2] as string ?? "";
    }

    internal sealed class ServerReply
    {
        internal bool Answered;
        internal bool Ok;
        internal string Detail = "";

        public override string ToString() => Answered ? $"{(Ok ? "ok" : "not ok")}: {Detail}" : $"no answer: {Detail}";
    }

    private static T Slot<T>(string slot, Func<T> make) where T : class
    {
        var domain = AppDomain.CurrentDomain;
        lock (domain)
        {
            if (!(domain.GetData(slot) is T value))
            {
                value = make();
                domain.SetData(slot, value);
            }
            return value;
        }
    }

    // File for Screenshot(name, label): "<shot dir>/<name>__<label>.png", odd characters made '_'. Null when no probe
    // run (no shot dir). Probe use same method to check file got written.
    internal static string ShotPath(string name, string label)
    {
        var dir = AppDomain.CurrentDomain.GetData(ShotDirSlot) as string;
        if (string.IsNullOrEmpty(dir))
        {
            return null;
        }
        return Path.Combine(dir, $"{Safe(name, "test")}__{Safe(label, "shot")}.png");
    }

    private static string Safe(string text, string fallback)
    {
        if (string.IsNullOrEmpty(text))
        {
            return fallback;
        }
        var safe = new char[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            safe[i] = char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '_';
        }
        return new string(safe);
    }
#endif
}
