#if DEBUG
using System;
using System.Collections;
using System.IO;
using MC.Shared;
using UnityEngine;

namespace MC.Core.ProbeWorldMod;

// Probe own tests, always run first (filter never skip them):
//   probe.baseline = world usable: local player, god mode, no stamina drain, position/biome/time, screenshots of world
//                    view and of inventory (InventoryGui.Show(null)), inventory closed again.
//   probe.runner   = SafeRunner itself: nested enumerators, WaitForSeconds, WaitForSecondsRealtime, WaitUntil work;
//                    a throw is caught; a never-ending test is stopped at timeout and its finally block runs.
internal static class ProbeTests
{
    internal const string Baseline = "probe.baseline";
    internal const string Runner = "probe.runner";

    internal static IEnumerator RunBaseline()
    {
        const string T = Baseline;
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(T, "no local player");
            yield break;
        }
        try
        {
            SelfTest.Pass(T, $"local player '{player.GetPlayerName()}' exists");
            var pos = player.transform.position;
            var env = EnvMan.instance;
            var envName = env != null && env.GetCurrentEnvironment() != null ? env.GetCurrentEnvironment().m_name : "?";
            SelfTest.Note(T, $"position ({pos.x:F1}, {pos.y:F1}, {pos.z:F1}), biome {player.GetCurrentBiome()}, "
                             + (env != null ? $"day {env.GetDay()} at fraction {env.GetDayFraction():F3} ({(EnvMan.IsDay() ? "day" : "night")}), " : "")
                             + $"environment {envName}");
            Check(T, player.InGodMode(), "god mode is on", "god mode is off");
            Check(T, Game.m_staminaRate == 0f, "no stamina drain (world modifier StaminaRate 0)",
                $"stamina rate is {Game.m_staminaRate} (expected 0)");

            SelfTest.Screenshot(T, "world");
            yield return null;
            yield return null;
            yield return WaitForShot(T, "world");

            var gui = InventoryGui.instance;
            if (gui == null)
            {
                SelfTest.Fail(T, "no InventoryGui");
                yield break;
            }
            gui.Show(null);
            yield return new WaitForSecondsRealtime(1.5f); // open animation
            Check(T, InventoryGui.IsVisible(), "inventory opened with InventoryGui.Show(null)", "inventory did not open");
            SelfTest.Screenshot(T, "inventory");
            yield return null;
            yield return null;
            yield return WaitForShot(T, "inventory");

            gui.Hide();
            yield return new WaitForSecondsRealtime(1f);
            Check(T, !InventoryGui.IsVisible(), "inventory closed again", "inventory still open after Hide()");
        }
        finally
        {
            if (InventoryGui.instance != null && InventoryGui.IsVisible())
            {
                InventoryGui.instance.Hide();
            }
        }
    }

    internal static IEnumerator RunRunnerCheck()
    {
        const string T = Runner;

        // Positive: every supported yield kind, timed.
        var trace = new System.Text.StringBuilder();
        var r = new RunResult();
        var t0 = Time.realtimeSinceStartup;
        yield return SafeRunner.Run(AllYieldKinds(trace), 10f, r);
        var took = Time.realtimeSinceStartup - t0;
        if (r.Error == null && !r.TimedOut && trace.ToString() == "a b c d e" && took >= 0.6f)
        {
            SelfTest.Pass(T, $"nested IEnumerator, WaitForSeconds, WaitForSecondsRealtime, WaitUntil ran in order ({took:F2} s)");
        }
        else
        {
            SelfTest.Fail(T, $"yield kinds: trace '{trace}', {took:F2} s, error {r.Error?.GetType().Name}, timed out {r.TimedOut}");
        }

        // Throw inside a nested level: caught, outer finally ran.
        var cleaned = false;
        r = new RunResult();
        yield return SafeRunner.Run(Throws(() => cleaned = true), 10f, r);
        Check(T, r.Error is InvalidOperationException && cleaned, "exception from a nested level caught, finally ran",
            $"throw not handled (error {r.Error?.GetType().Name}, finally ran {cleaned})");

        // Never ends: stopped at timeout, finally ran.
        cleaned = false;
        r = new RunResult();
        yield return SafeRunner.Run(NeverEnds(() => cleaned = true), 1f, r);
        Check(T, r.TimedOut && cleaned && r.Seconds < 5f, $"endless test stopped at timeout after {r.Seconds:F1} s, finally ran",
            $"timeout not handled (timed out {r.TimedOut}, finally ran {cleaned}, {r.Seconds:F1} s)");
    }

    private static IEnumerator AllYieldKinds(System.Text.StringBuilder trace)
    {
        trace.Append('a');
        yield return Nested(trace);
        trace.Append(" c");
        yield return new WaitForSeconds(0.3f);
        trace.Append(" d");
        yield return new WaitForSecondsRealtime(0.3f);
        var frame = Time.frameCount;
        yield return new WaitUntil(() => Time.frameCount >= frame + 2);
        trace.Append(" e");
    }

    private static IEnumerator Nested(System.Text.StringBuilder trace)
    {
        yield return null;
        trace.Append(" b");
    }

    private static IEnumerator Throws(Action onFinally)
    {
        try
        {
            yield return null;
            yield return ThrowNow();
        }
        finally
        {
            onFinally();
        }
    }

    private static IEnumerator ThrowNow()
    {
        yield return null;
        throw new InvalidOperationException("expected by probe.runner");
    }

    private static IEnumerator NeverEnds(Action onFinally)
    {
        try
        {
            while (true)
            {
                yield return new WaitForSeconds(0.1f);
            }
        }
        finally
        {
            onFinally();
        }
    }

    // ScreenCapture write file a little after end of frame: me wait up to 5 s for it.
    private static IEnumerator WaitForShot(string test, string label)
    {
        var path = SelfTest.ShotPath(test, label);
        if (path == null)
        {
            SelfTest.Fail(test, $"screenshot '{label}': no screenshot folder set");
            yield break;
        }
        var until = Time.realtimeSinceStartup + 5f;
        while (!File.Exists(path) && Time.realtimeSinceStartup < until)
        {
            yield return null;
        }
        if (File.Exists(path))
        {
            yield return new WaitForSecondsRealtime(0.2f); // file may still be written
            SelfTest.Pass(test, $"screenshot '{label}' written ({new FileInfo(path).Length / 1024} KB, {Screen.width}x{Screen.height})");
        }
        else
        {
            SelfTest.Fail(test, $"screenshot '{label}' not written within 5 s: {path}");
        }
    }

    private static void Check(string test, bool ok, string pass, string fail)
    {
        if (ok)
        {
            SelfTest.Pass(test, pass);
        }
        else
        {
            SelfTest.Fail(test, fail);
        }
    }
}
#endif
