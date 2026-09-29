using MC.Shared;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#endif

namespace MC.Core.ProbeWorldMod;

#if DEBUG
// Seconds (Time.realtimeSinceStartup) of each run milestone, for timing notes.
internal sealed class ProbeTimings
{
    internal float Start;
    internal float MenuReady;
    internal float WorldRequested;
    internal float Spawned;
    internal float Settled;
}
#endif

// Me run whole probe session as one coroutine on plugin object (BepInEx manager, survive scene change):
//   menu (MenuDriver) -> wait spawn -> settle -> god mode + no stamina drain -> probe tests -> every SelfTest
//   (registration order, filter, timeout) -> "[selftest] DONE pass=<tests passed> fail=<tests failed> tests=<run>"
//   -> quit. Every phase go through SafeRunner: a throw or a hang end as FAIL, never as a silent stuck game.
internal static class ProbeDriver
{
    internal const string SetupTest = "probe.setup";

#if DEBUG
    private const float MenuTimeout = 300f;
    private const float SpawnTimeout = 900f;
    private const float SettleSeconds = 8f;
    private const float SettleTimeout = 120f;

    private static Coroutine _run;
    private static ResultListener _listener;
#endif

    internal static void Begin(Plugin plugin)
    {
#if DEBUG
        if (!ProbeSettings.Load())
        {
            Log.Info($"{ProbeSettings.DirVar} not set: world probe idle (it only runs under tools/Test-InWorld.ps1).");
            return;
        }
        var refused = SaveIsolation.Apply();
        if (refused != null)
        {
            SelfTest.Fail(SetupTest, refused);
            Log.Info($"{SelfTest.Prefix} DONE pass=0 fail=1 tests=1");
            return;
        }
        Log.Info($"World probe on. Save data redirected to '{ProbeSettings.SaveDir}' (game default '{Utils.persistantDataPath}'), "
                 + $"cloud saves off for this session, screenshots to '{ProbeSettings.ShotDir}', test timeout {ProbeSettings.TestTimeout:F0} s, "
                 + $"filter '{string.Join(",", ProbeSettings.Filters)}', keep running {ProbeSettings.Keep}.");
        if (_listener == null)
        {
            _listener = new ResultListener();
            BepInEx.Logging.Logger.Listeners.Add(_listener);
        }
        if (_run == null)
        {
            _run = plugin.StartCoroutine(Run(plugin));
        }
#else
        Log.Info("Release build: world probe does nothing (self tests exist in Debug builds only).");
#endif
    }

    internal static void End(Plugin plugin)
    {
#if DEBUG
        if (_run != null)
        {
            plugin.StopCoroutine(_run);
            _run = null;
            Log.Warning("World probe turned off: run stopped (saves stay redirected until the game exits).");
        }
#endif
    }

#if DEBUG
    private static IEnumerator Run(Plugin plugin)
    {
        var timings = new ProbeTimings { Start = Time.realtimeSinceStartup };
        var passed = 0;
        var failed = 0;
        var run = 0;

        // Setup = menu + spawn + settle. Counted as one test only when it fails.
        var setup = new RunResult();
        yield return SafeRunner.Run(Setup(timings), MenuTimeout + SpawnTimeout + SettleTimeout, setup);
        var setupOk = ReportPhase(SetupTest, setup);
        if (setupOk)
        {
            SelfTest.Note("probe", $"timings: menu ready {timings.MenuReady - timings.Start:F1} s, world start to spawn "
                                   + $"{timings.Spawned - timings.WorldRequested:F1} s, settled {timings.Settled - timings.Spawned:F1} s after spawn");

            var tests = new List<KeyValuePair<string, Func<IEnumerator>>>
            {
                new KeyValuePair<string, Func<IEnumerator>>(ProbeTests.Baseline, ProbeTests.RunBaseline),
                new KeyValuePair<string, Func<IEnumerator>>(ProbeTests.Runner, ProbeTests.RunRunnerCheck),
            };
            var registered = SelfTest.GetTests();
            var skipped = new List<string>();
            lock (registered)
            {
                foreach (var t in registered)
                {
                    if (!ProbeSettings.Wanted(t.Key))
                    {
                        skipped.Add($"{t.Key} ({OwnerOf(t.Value)})");
                        continue;
                    }
                    tests.Add(t);
                }
            }
            SelfTest.Note("probe", $"{tests.Count} test(s) to run ({registered.Count} registered by mods, {skipped.Count} filtered out)");
            if (skipped.Count > 0)
            {
                // Script read this line: "<name> (<mod GUID>), ..." tell which mod lost its tests to the filter.
                SelfTest.Note("probe", "filtered out: " + string.Join(", ", skipped));
            }

            foreach (var t in tests)
            {
                var outcome = new bool[1];
                yield return RunTest(t.Key, t.Value, outcome);
                run++;
                if (outcome[0])
                {
                    passed++;
                }
                else
                {
                    failed++;
                }
                yield return BetweenTests(t.Key);
            }
        }
        else
        {
            run = 1;
            failed = 1;
        }

        _listener?.Stop();
        Log.Info($"{SelfTest.Prefix} DONE pass={passed} fail={failed} tests={run}");
        var restored = PrefsGuard.Restore();
        if (restored > 0)
        {
            SelfTest.Note("probe", $"menu prefs put back before quit: {restored} changed");
        }
        _run = null;
        if (ProbeSettings.Keep)
        {
            SelfTest.Note("probe", "MC_INWORLD_KEEP=1: game stays open");
            yield break;
        }
        SelfTest.Note("probe", "quitting the game");
        yield return new WaitForSecondsRealtime(1f);
        Application.Quit();
    }

    private static IEnumerator Setup(ProbeTimings timings)
    {
        yield return MenuDriver.StartWorld(timings);

        // Main scene load: menu object go away, then local player come.
        var lastNote = Time.realtimeSinceStartup;
        var leftMenu = false;
        while (Player.m_localPlayer == null)
        {
            if (FejdStartup.instance == null)
            {
                leftMenu = true;
            }
            else if (leftMenu)
            {
                throw new InvalidOperationException("back at the main menu before spawning: the world failed to load (see log)");
            }
            if (Time.realtimeSinceStartup - timings.WorldRequested > SpawnTimeout)
            {
                throw new TimeoutException($"no local player {SpawnTimeout:F0} s after world start");
            }
            var game = Game.instance;
            if (game != null && game.InIntro(includeQueued: true))
            {
                SelfTest.Note(SetupTest, "skipping the first-spawn intro");
                game.SkipIntro();
            }
            if (Time.realtimeSinceStartup - lastNote >= 30f)
            {
                lastNote = Time.realtimeSinceStartup;
                SelfTest.Note(SetupTest, $"still loading the world ({Time.realtimeSinceStartup - timings.WorldRequested:F0} s)");
            }
            yield return null;
        }
        timings.Spawned = Time.realtimeSinceStartup;
        SelfTest.Note(SetupTest, $"local player spawned {timings.Spawned - timings.WorldRequested:F1} s after world start");

        // Settle: area around player loaded, no teleport, then SettleSeconds more.
        var readySince = -1f;
        while (true)
        {
            var p = Player.m_localPlayer;
            if (p == null)
            {
                throw new InvalidOperationException("local player vanished while settling");
            }
            var ready = !p.IsTeleporting() && ZNetScene.instance != null && ZNetScene.instance.IsAreaReady(p.transform.position)
                        && Game.instance != null && !Game.instance.WaitingForRespawn();
            var now = Time.realtimeSinceStartup;
            if (!ready)
            {
                readySince = -1f;
            }
            else if (readySince < 0f)
            {
                readySince = now;
            }
            if (readySince >= 0f && now - timings.Spawned >= SettleSeconds && now - readySince >= 2f)
            {
                break;
            }
            if (now - timings.Spawned > SettleTimeout)
            {
                throw new TimeoutException($"world did not settle within {SettleTimeout:F0} s (teleporting {p.IsTeleporting()})");
            }
            yield return null;
        }

        // Throwaway character: god mode (vanilla setter, no console, profile not marked as cheater) and world
        // modifier StaminaRate 0 (Game.m_staminaRate = 0: no stamina use at all).
        Player.m_localPlayer.SetGodMode(true);
        ZoneSystem.instance.SetGlobalKey(GlobalKeys.StaminaRate, 0f);
        yield return new WaitForSecondsRealtime(1f);
        timings.Settled = Time.realtimeSinceStartup;
        SelfTest.Note(SetupTest, $"world settled; god mode {Player.m_localPlayer.InGodMode()}, stamina rate {Game.m_staminaRate}");
    }

    // Mod GUID = assembly name. Test method or lambda live in mod dll, so delegate tell who own the test.
    private static string OwnerOf(Delegate test)
    {
        try
        {
            var type = test?.Method.DeclaringType;
            return type != null ? type.Assembly.GetName().Name : "?";
        }
        catch (Exception)
        {
            return "?";
        }
    }

    private static IEnumerator RunTest(string name, Func<IEnumerator> factory, bool[] outcome)
    {
        // "(<mod GUID>)" let script check -Mod pick really ran.
        Log.Info($"{SelfTest.Prefix} BEGIN {name} ({OwnerOf(factory)})");
        _listener.Begin();
        var result = new RunResult();
        IEnumerator root = null;
        try
        {
            root = factory();
            if (root == null)
            {
                result.Error = new InvalidOperationException("the test returned no coroutine");
            }
        }
        catch (Exception e)
        {
            result.Error = e;
        }
        if (root != null)
        {
            yield return SafeRunner.Run(root, ProbeSettings.TestTimeout, result);
        }

        outcome[0] = ReportPhase(name, result);
        _listener.Snapshot(out var pass, out var fail, out var errors, out var firstError);
        if (pass == 0 && fail == 0)
        {
            SelfTest.Fail(name, "the test reported no PASS or FAIL");
            outcome[0] = false;
            fail++;
        }
        if (fail > 0)
        {
            outcome[0] = false;
        }
        if (errors > 0)
        {
            SelfTest.Note(name, $"{errors} error line(s) logged during this test, first: {firstError}");
        }
        Log.Info($"{SelfTest.Prefix} END {name}: {(outcome[0] ? "passed" : "FAILED")} (pass={pass} fail={fail}, {result.Seconds:F1} s)");
    }

    // FAIL line for a throw or timeout. True = phase ran to its end.
    private static bool ReportPhase(string name, RunResult result)
    {
        if (result.Warning != null)
        {
            SelfTest.Note(name, result.Warning);
        }
        if (result.TimedOut)
        {
            SelfTest.Fail(name, $"timed out after {result.Seconds:F0} s (cleanup: finally blocks ran)");
            return false;
        }
        if (result.Error != null)
        {
            SelfTest.Fail(name, $"threw {result.Error}");
            return false;
        }
        return true;
    }

    // Test should clean up; me catch the usual leftovers so next test start from same state, and say so.
    // Wait first: InventoryGui.IsVisible stay true a few frames after Hide (hidden-frame counter).
    private static IEnumerator BetweenTests(string name)
    {
        yield return new WaitForSecondsRealtime(0.5f);
        var p = Player.m_localPlayer;
        if (InventoryGui.instance != null && InventoryGui.IsVisible())
        {
            SelfTest.Note(name, "left the inventory open; closed it");
            InventoryGui.instance.Hide();
            yield return new WaitForSecondsRealtime(0.5f);
        }
        if (p != null && !p.InGodMode())
        {
            SelfTest.Note(name, "left god mode off; turned it back on");
            p.SetGodMode(true);
        }
    }
#endif
}
