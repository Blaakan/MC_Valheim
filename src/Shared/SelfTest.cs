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
internal static class SelfTest
{
    // Slot names never change: probe and every mod read same slot.
    internal const string TestsSlot = "MC.SelfTest.Tests.v1";     // List<KeyValuePair<string, Func<IEnumerator>>>
    internal const string ShotDirSlot = "MC.SelfTest.ShotDir.v1"; // string: folder for screenshots, set by probe
    internal const string Prefix = "[selftest]";

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
    internal static List<KeyValuePair<string, Func<IEnumerator>>> GetTests()
    {
        var domain = AppDomain.CurrentDomain;
        lock (domain)
        {
            if (!(domain.GetData(TestsSlot) is List<KeyValuePair<string, Func<IEnumerator>>> list))
            {
                list = new List<KeyValuePair<string, Func<IEnumerator>>>();
                domain.SetData(TestsSlot, list);
            }
            return list;
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
