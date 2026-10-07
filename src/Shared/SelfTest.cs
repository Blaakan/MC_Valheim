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
