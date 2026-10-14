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
//   (registration order, filter, timeout) -> staged tests (StageTests: relog tests with a real log out and world
//   load each, then the "leave" parts of disabled-start tests) -> "[selftest] DONE pass=<tests passed>
//   fail=<tests failed> tests=<run>" -> quit. Every phase go through SafeRunner: a throw or a hang end as FAIL,
//   never as a silent stuck game. Second game of a run (MC_INWORLD_STAGE=disabled, every MC mod off): same start,
//   then only probe.baseline and the disabled-start checks.
internal static class ProbeDriver
{
    internal const string SetupTest = "probe.setup";

#if DEBUG
    internal const float MenuTimeout = 300f;
    internal const float SpawnTimeout = 900f;
    private const float SettleSeconds = 8f;
    internal const float SettleTimeout = 120f;

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
                 + $"filter '{string.Join(",", ProbeSettings.Filters)}', keep running {ProbeSettings.Keep}"
                 + (ProbeSettings.IsMultiplayer ? $", multiplayer: join {ProbeSettings.Join}, scenario '{ProbeSettings.Scenario}'." : "."));
        // Mods read scenario through SelfTest.Scenario ("" = single player).
        AppDomain.CurrentDomain.SetData(SelfTest.ScenarioSlot, ProbeSettings.Scenario);
        AppDomain.CurrentDomain.SetData(SelfTest.StageSlot, ProbeSettings.Stage);
        AppDomain.CurrentDomain.SetData(SelfTest.RunDirSlot, ProbeSettings.RunDir);
        InputDriver.Install();
        StageTests.RegisterOwn();
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

            RememberHome();
            if (ProbeSettings.Parked.Count > 0)
            {
                SelfTest.Note("probe", $"{ProbeSettings.Parked.Count} parked test(s) (tests/parked-selftests.txt) do not run unless -Only names one exactly");
            }
            var skipped = new List<string>();
            var tests = ProbeSettings.IsMultiplayer ? MultiplayerTestList(skipped, out var registeredCount)
                : ProbeSettings.DisabledStage ? StageTests.DisabledStageList(skipped, out registeredCount)
                : SinglePlayerTestList(skipped, out registeredCount);
            SelfTest.Note("probe", $"{tests.Count} test(s) to run ({registeredCount} registered by mods, {skipped.Count} filtered out)");
            if (skipped.Count > 0)
            {
                // Script read this line: "<name> (<mod GUID>), ..." tell which mod lost its tests to the filter.
                SelfTest.Note("probe", "filtered out: " + string.Join(", ", skipped));
            }

            foreach (var t in tests)
            {
                if (ProbeSettings.IsMultiplayer && MpTests.NeedsWorld(t.Key))
                {
                    // Test before may have left the server (refused, kicked): join again so this one start in the world.
                    var back = new RunResult();
                    yield return SafeRunner.Run(MpTests.EnsureInGame(timings), MenuTimeout + SpawnTimeout + SettleTimeout, back);
                    if (back.TimedOut || back.Error != null)
                    {
                        SelfTest.Note("probe", $"could not get back into the server before {t.Key}: "
                                               + (back.TimedOut ? $"timed out after {back.Seconds:F0} s" : back.Error.Message));
                    }
                }
                var outcome = new bool[1];
                var skippedBefore = Skipped;
                yield return RunTest(t.Key, t.Value, outcome);
                run++;
                if (Skipped > skippedBefore)
                {
                    // Neither passed nor failed.
                }
                else if (outcome[0])
                {
                    passed++;
                }
                else
                {
                    failed++;
                }
                yield return BetweenTests(t.Key);
            }

            if (!ProbeSettings.IsMultiplayer && !ProbeSettings.DisabledStage)
            {
                // Staged tests last: each relog test leave and load the world, leave parts put things in the save.
                var counts = new int[3];
                yield return StageTests.RunFirstGameStages(timings, counts);
                run += counts[0];
                passed += counts[1];
                failed += counts[2];
            }
        }
        else
        {
            run = 1;
            failed = 1;
        }

        _listener?.Stop();
        Log.Info($"{SelfTest.Prefix} DONE pass={passed} fail={failed} tests={run}" + (Skipped > 0 ? $" skipped={Skipped}" : ""));
        if (ProbeSettings.IsMultiplayer && !ProbeSettings.Keep)
        {
            // Server probe save world and quit (script kill it otherwise).
            MpLink.SendQuit();
            MpLink.Uninstall();
        }
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
        FlushLog();
        yield return new WaitForSecondsRealtime(1f);
        FlushLog();
        Application.Quit();
    }

    // BepInEx write its log file every 2 s. Quit from main menu (refused player) is instant: last lines never
    // reached the file and script said "client never logged DONE" (seen 2026-10-08, vanilla-client scenario, about
    // one run in two). In a world the slow shutdown hid it. Me write them out before the quit.
    internal static void FlushLog()
    {
        try
        {
            foreach (var listener in BepInEx.Logging.Logger.Listeners)
            {
                if (listener is BepInEx.Logging.DiskLogListener disk && disk.LogWriter != null)
                {
                    disk.LogWriter.Flush();
                }
            }
        }
        catch (Exception e)
        {
            Log.Warning($"log flush before quit failed: {e.Message}");
        }
    }

    private static List<KeyValuePair<string, Func<IEnumerator>>> SinglePlayerTestList(List<string> skipped, out int registeredCount)
    {
        var tests = new List<KeyValuePair<string, Func<IEnumerator>>>
        {
            new KeyValuePair<string, Func<IEnumerator>>(ProbeTests.Baseline, ProbeTests.RunBaseline),
            new KeyValuePair<string, Func<IEnumerator>>(ProbeTests.Runner, ProbeTests.RunRunnerCheck),
        };
        var registered = SelfTest.GetTests();
        lock (registered)
        {
            registeredCount = registered.Count;
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
        return tests;
    }

    // Probe's own multiplayer tests, then mods' multiplayer tests of this scenario (other scenarios not counted).
    private static List<KeyValuePair<string, Func<IEnumerator>>> MultiplayerTestList(List<string> skipped, out int registeredCount)
    {
        var tests = MpTests.ProbeTests();
        var registered = SelfTest.GetMultiplayerTests();
        var otherScenario = new List<string>();
        registeredCount = 0;
        lock (registered)
        {
            foreach (var t in registered)
            {
                var name = t.TryGetValue("name", out var n) ? n as string : null;
                var scenario = t.TryGetValue("scenario", out var s) ? s as string : null;
                var run = t.TryGetValue("run", out var r) ? r as Func<IEnumerator> : null;
                if (name == null || run == null)
                {
                    continue;
                }
                if (scenario != ProbeSettings.Scenario)
                {
                    otherScenario.Add(name);
                    continue;
                }
                registeredCount++;
                if (!ProbeSettings.Wanted(name))
                {
                    skipped.Add($"{name} ({OwnerOf(run)})");
                    continue;
                }
                tests.Add(new KeyValuePair<string, Func<IEnumerator>>(name, run));
            }
        }
        if (otherScenario.Count > 0)
        {
            SelfTest.Note("probe", $"{otherScenario.Count} multiplayer test(s) of other scenarios not run: {string.Join(", ", otherScenario)}");
        }
        tests.AddRange(MpTests.LateTests());
        return tests;
    }

    private static IEnumerator Setup(ProbeTimings timings)
    {
        yield return MenuDriver.StartWorld(timings);
        if (ProbeSettings.IsMultiplayer && ProbeSettings.Scenario == MC.Core.ProbeShared.MpProtocol.VanillaClient)
        {
            // Player without mods: the server should refuse it. No spawn to wait for.
            yield return MpTests.WaitRefusal(timings);
            yield break;
        }
        yield return WaitSpawnAndSettle(timings);
    }

    // After world start or server join: wait local player, area loaded, then god mode + no stamina drain. Multiplayer
    // tests use it again after a rejoin.
    internal static IEnumerator WaitSpawnAndSettle(ProbeTimings timings)
    {
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
        yield return WaitStoodUp(SetupTest);
        timings.Settled = Time.realtimeSinceStartup;
        SelfTest.Note(SetupTest, $"world settled; god mode {Player.m_localPlayer.InGodMode()}, stamina rate {Game.m_staminaRate}");
    }

    // Mod GUID = assembly name. Test method or lambda live in mod dll, so delegate tell who own the test.
    internal static string OwnerOf(Delegate test)
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

    internal static ResultListener Listener => _listener;

    // Tests that ended "skipped" since the game started (they are in the run count, in neither pass nor fail).
    internal static int Skipped;

    // One test = one or more parts (staged tests: before / relog / after), one BEGIN and one END line. A part that
    // throw or time out end the test: later parts skipped. timeouts: seconds per part (0 = the test timeout).
    internal static IEnumerator RunTest(string name, Func<IEnumerator> factory, bool[] outcome) =>
        RunParts(name, OwnerOf(factory), new[] { factory }, new[] { 0f }, outcome);

    internal static IEnumerator RunParts(string name, string owner, Func<IEnumerator>[] parts, float[] timeouts, bool[] outcome)
    {
        // "(<mod GUID>)" let script check -Mod pick really ran.
        Log.Info($"{SelfTest.Prefix} BEGIN {name} ({owner})");
        _listener.Begin();
        var result = new RunResult();
        var seconds = 0f;
        outcome[0] = true;
        for (var i = 0; i < parts.Length && outcome[0]; i++)
        {
            if (parts[i] == null)
            {
                continue;
            }
            result = new RunResult();
            IEnumerator root = null;
            try
            {
                root = parts[i]();
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
                yield return SafeRunner.Run(root, timeouts[i] > 0f ? timeouts[i] : ProbeSettings.TestTimeout, result);
            }
            seconds += result.Seconds;
            outcome[0] = ReportPhase(name, result);
        }
        result.Seconds = seconds;

        _listener.Snapshot(out var pass, out var fail, out var errors, out var firstError);
        // Only SKIP lines and it ran to its end: premise did not hold here. Not passed, not failed: "skipped"
        // (scripts read it as not run).
        if (pass == 0 && fail == 0 && _listener.Skips > 0 && outcome[0])
        {
            Skipped++;
            Log.Info($"{SelfTest.Prefix} END {name}: skipped (pass=0 fail=0, {result.Seconds:F1} s)");
            yield break;
        }
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
    internal static bool ReportPhase(string name, RunResult result)
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
    private static Vector3 _home;
    private static bool _homeSet;

    // Place the run started at (after spawn and settle): tests are written for it.
    internal static void RememberHome()
    {
        var player = Player.m_localPlayer;
        if (player != null)
        {
            _home = player.transform.position;
            _homeSet = true;
        }
    }

    // New body (respawn, world load) plays the game's 'Standing Up' animation: Player.InCutscene is true and the game
    // takes no input until it ends (some seconds). Me wait for it, at most 30 s.
    internal static IEnumerator WaitStoodUp(string name)
    {
        var body = Player.m_localPlayer;
        if (body == null || body.IsDead() || !body.InCutscene() || body.IsSleeping() || body.InIntro())
        {
            yield break;
        }
        var from = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - from < 30f)
        {
            body = Player.m_localPlayer;
            if (body == null || body.IsDead() || !body.InCutscene())
            {
                break;
            }
            yield return null;
        }
        SelfTest.Note(name, $"the player was still standing up (new body after a respawn or a world load: the game takes no input meanwhile); "
                            + $"waited {Time.realtimeSinceStartup - from:F1} s for it");
    }

    internal static IEnumerator BetweenTests(string name)
    {
        yield return new WaitForSecondsRealtime(0.5f);
        // Test left the player dead (death screen, respawn pending): next test need a living one.
        if (Player.m_localPlayer == null || Player.m_localPlayer.IsDead())
        {
            SelfTest.Note(name, "left the player dead; waiting for the respawn");
            var until = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < until && (Player.m_localPlayer == null || Player.m_localPlayer.IsDead()))
            {
                yield return null;
            }
            yield return new WaitForSecondsRealtime(2f);
            if (Player.m_localPlayer != null && !Player.m_localPlayer.IsDead())
            {
                Player.m_localPlayer.SetGodMode(true);
            }
        }
        yield return WaitStoodUp(name);
        var p = Player.m_localPlayer;
        // Far from the start: bring the player back (teleport like a portal does, area loaded before going on).
        if (p != null && _homeSet && !p.IsDead())
        {
            var away = Utils.DistanceXZ(p.transform.position, _home);
            var high = Mathf.Abs(p.transform.position.y - _home.y);
            if (away > 40f || high > 60f)
            {
                SelfTest.Note(name, $"left the player {away:F0} m from the place the run started ({high:F0} m higher or lower); brought back");
                var until = Time.realtimeSinceStartup + 20f;
                while (Time.realtimeSinceStartup < until && !p.TeleportTo(_home, p.transform.rotation, true))
                {
                    yield return null;
                    p = Player.m_localPlayer;
                    if (p == null)
                    {
                        break;
                    }
                }
                until = Time.realtimeSinceStartup + 90f;
                yield return null;
                while (Time.realtimeSinceStartup < until && (Player.m_localPlayer == null || Player.m_localPlayer.IsTeleporting()
                                                             || ZNetScene.instance == null || !ZNetScene.instance.IsAreaReady(_home)))
                {
                    yield return null;
                }
                yield return new WaitForSecondsRealtime(1f);
                p = Player.m_localPlayer;
                if (p != null && Utils.DistanceXZ(p.transform.position, _home) > 40f)
                {
                    SelfTest.Note(name, $"could not bring the player back: still {Utils.DistanceXZ(p.transform.position, _home):F0} m from the start");
                }
            }
        }
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
        // Real input: every key, button and stick up, test gamepad gone (cheap when nothing is held).
        InputDriver.Release();
        // Weather, wind and hour a test forced (game's own debug fields) and did not give back.
        var env = EnvMan.instance;
        if (env != null)
        {
            if (!string.IsNullOrEmpty(env.m_debugEnv))
            {
                SelfTest.Note(name, $"left the weather forced to '{env.m_debugEnv}'; released");
                env.m_debugEnv = "";
            }
            if (env.m_debugWind)
            {
                SelfTest.Note(name, "left the wind forced; released");
                env.ResetDebugWind();
            }
            if (env.m_debugTimeOfDay)
            {
                SelfTest.Note(name, $"left the time of day forced to {env.m_debugTime:F2}; released");
                env.m_debugTimeOfDay = false;
            }
        }
        // Player still at a helm, in a saddle, on a chair or in a bed: next tests could not walk, attack or swim.
        if (p != null && p.GetDoodadController() != null)
        {
            SelfTest.Note(name, "left the player steering (ship helm or saddle); let go");
            p.StopDoodadControl();
        }
        if (p != null && p.IsAttached())
        {
            SelfTest.Note(name, "left the player attached (seat, bed, helm or saddle); detached");
            p.AttachStop();
        }
        // Windows that stop the game from taking input.
        if (Menu.instance != null && Menu.IsVisible())
        {
            SelfTest.Note(name, "left the pause menu open; closed it");
            Menu.instance.Hide();
        }
        if (Game.IsPaused())
        {
            Game.Unpause();
        }
        if (TextViewer.instance != null && TextViewer.instance.IsVisible())
        {
            SelfTest.Note(name, "left a text window open (rune stone, raven or compendium); closed it");
            TextViewer.instance.Hide();
        }
        if (StoreGui.instance != null && StoreGui.IsVisible())
        {
            SelfTest.Note(name, "left a trader window open; closed it");
            StoreGui.instance.Hide();
        }
        if (Minimap.instance != null && Minimap.IsOpen())
        {
            SelfTest.Note(name, "left the large map open; closed it");
            Minimap.instance.SetMapMode(Minimap.MapMode.Small);
        }
        if (p != null && p.InPlaceMode())
        {
            SelfTest.Note(name, "left the build menu or build mode on (tool in hand); tool put away");
            Hud.HidePieceSelection();
            p.HideHandItems();
        }
        if (Console.IsVisible() && Console.instance != null)
        {
            SelfTest.Note(name, "left the console open; closed it");
            Console.instance.m_chatWindow.gameObject.SetActive(false);
        }
        // Still no input taken (or the player's own input reader off): say every gate, once per change, so the
        // first test that leaves the player unable to act is named.
        if (p != null)
        {
            var controller = p.GetComponent<PlayerController>();
            var state = InputGates(p, controller);
            if (state != _lastGates)
            {
                _lastGates = state;
                if (state.Length > 0)
                {
                    SelfTest.Note(name, "after this test the game takes no input from the player: " + state);
                }
                else
                {
                    SelfTest.Note(name, "after this test the game takes input from the player again");
                }
            }
        }
    }

    private static string _lastGates = "";

    // "" = player can act. Else the gates that are closed (the game's own Player.TakeInput list and what stops a body).
    private static string InputGates(Player p, PlayerController controller)
    {
        var closed = new List<string>();
        if (controller == null || !controller.enabled)
        {
            closed.Add(controller == null ? "no PlayerController" : "PlayerController turned off");
        }
        if (p.IsDead()) { closed.Add("dead"); }
        if (p.IsTeleporting()) { closed.Add("teleporting"); }
        if (p.InCutscene())
        {
            // Which animation hold the body: name of the clip the Animator plays on its base layer.
            var clip = "?";
            var time = 0f;
            try
            {
                var clips = p.m_animator.GetCurrentAnimatorClipInfo(0);
                clip = clips != null && clips.Length > 0 && clips[0].clip != null ? clips[0].clip.name : "none";
                time = p.m_animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            }
            catch (Exception)
            {
            }
            closed.Add($"cutscene (sleeping {p.IsSleeping()}, intro {p.InIntro()}, animation clip '{clip}' at {time:F2} of its length)");
        }
        if (Chat.instance != null && Chat.instance.HasFocus()) { closed.Add("chat has focus"); }
        if (TextInput.IsVisible()) { closed.Add("text input window"); }
        if (InventoryGui.IsVisible()) { closed.Add("inventory"); }
        if (GameCamera.InFreeFly()) { closed.Add("free fly camera"); }
        if (PlayerCustomizaton.IsBarberGuiVisible()) { closed.Add("barber window"); }
        if (Hud.instance != null && Hud.instance.m_buildUi != null && Hud.instance.m_buildUi.SearchFieldFocused) { closed.Add("build menu search field"); }
        if (p.IsAttached()) { closed.Add("attached"); }
        if (p.IsEncumbered()) { closed.Add("encumbered"); }
        if (Time.timeScale < 0.5f) { closed.Add($"time scale {Time.timeScale:F2}"); }
        if (closed.Count == 0 && !p.TakeInput()) { closed.Add("Player.TakeInput is false for another reason"); }
        return string.Join(", ", closed);
    }
#endif
}
