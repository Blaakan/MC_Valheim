#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MC.Shared;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MC.Core.ProbeWorldMod;

// Staged tests of a single-player run. First game, after the normal tests:
//   probe.input           input driver itself: key, mouse button, wheel, test gamepad reach ZInput; all released after
//   probe.relog           one real log out to main menu and load of the same world, log watched all the way
//   probe.relog.<GUID>    one per MC mod: no warning or error line of that mod in that time
//   <mod relog tests>     SelfTest.RegisterRelog: before -> log out -> atMenu -> load world -> after, one relog each
//   probe.stage.relog     the relog test API itself (RegisterRelog): three parts in order, statics kept, an item in the
//                         inventory and the place of the player survive the log out
//   <leave parts>         SelfTest.RegisterDisabledStart: things put in world or inventory for the second game
//                         (probe.stage.disabled: the API itself, an item and a carried value reach the second game)
// then "NOTE probe: second game wanted: disabled (n test(s))" tell the script to set every MC mod Enabled = false and
// start the game again on the same saves (MC_INWORLD_STAGE=disabled). Second game:
//   probe.baseline, probe.disabled.<GUID> (mod is off with the "disabled in settings" status, its log is clean since
//   game start), then every registered check part.
// Filter (-Only) pick these like any test; probe.baseline and probe.runner are the only ones that always run.
internal static class StageTests
{
    internal const string Input = "probe.input";
    internal const string Relog = "probe.relog";
    internal const string RelogPrefix = "probe.relog.";
    internal const string DisabledPrefix = "probe.disabled.";
    private const float LogoutTimeout = 150f;
    internal const string StageRelog = "probe.stage.relog";
    internal const string StageDisabled = "probe.stage.disabled";
    private const string StageItem = "Wood";

    private static readonly string ProbeGuid = typeof(StageTests).Assembly.GetName().Name;

    // Probe's own staged tests, through the same calls a mod uses.
    internal static void RegisterOwn()
    {
        SelfTest.RegisterRelog(StageRelog, RelogBefore, RelogMenu, RelogAfter);
        SelfTest.RegisterDisabledStart(StageDisabled, DisabledLeave, DisabledCheck);
    }

    private static string _relogTrace = "";
    private static Vector3 _relogPos;
    private static int _relogWood;

    private static int CountItem(Player p, string prefab)
    {
        var n = 0;
        foreach (var item in p.GetInventory().GetAllItems())
        {
            if (item.m_dropPrefab != null && item.m_dropPrefab.name == prefab)
            {
                n += item.m_stack;
            }
        }
        return n;
    }

    private static IEnumerator RelogBefore()
    {
        var p = Player.m_localPlayer;
        _relogTrace = "before";
        _relogPos = p.transform.position;
        var prefab = ObjectDB.instance.GetItemPrefab(StageItem);
        p.GetInventory().AddItem(prefab, 3);
        _relogWood = CountItem(p, StageItem);
        Check(StageRelog, prefab != null && _relogWood >= 3, $"before: in the world, {_relogWood} {StageItem} in the inventory",
            $"before: could not put {StageItem} in the inventory ({_relogWood})");
        yield return null;
    }

    private static IEnumerator RelogMenu()
    {
        Check(StageRelog, FejdStartup.instance != null && Player.m_localPlayer == null && _relogTrace == "before",
            "at the main menu: no local player, statics of the part before still there",
            $"menu part: main menu {FejdStartup.instance != null}, player {Player.m_localPlayer != null}, trace '{_relogTrace}'");
        _relogTrace += " menu";
        yield return null;
    }

    private static IEnumerator RelogAfter()
    {
        var p = Player.m_localPlayer;
        try
        {
            var wood = p != null ? CountItem(p, StageItem) : -1;
            var moved = p != null ? Vector3.Distance(p.transform.position, _relogPos) : -1f;
            Check(StageRelog, _relogTrace == "before menu", "parts ran in order: before, menu, after", $"parts ran as '{_relogTrace}' (expected 'before menu')");
            Check(StageRelog, p != null && wood == _relogWood, $"after: the character was saved and loaded ({wood} {StageItem} kept)",
                $"after: {wood} {StageItem} in the inventory (expected {_relogWood})");
            Check(StageRelog, p != null && moved < 5f && p.InGodMode(), $"after: back at the log out point ({moved:F1} m away), god mode on again",
                $"after: {moved:F1} m from the log out point, god mode {p != null && p.InGodMode()}");
        }
        finally
        {
            if (p != null)
            {
                var have = CountItem(p, StageItem);
                var take = Mathf.Min(3, have);
                if (take > 0)
                {
                    p.GetInventory().RemoveItem(ObjectDB.instance.GetItemPrefab(StageItem).GetComponent<ItemDrop>().m_itemData.m_shared.m_name, take);
                }
            }
            _relogTrace = "";
        }
        yield return null;
    }

    private static IEnumerator DisabledLeave()
    {
        var p = Player.m_localPlayer;
        p.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab(StageItem), 2);
        var wood = CountItem(p, StageItem);
        var carried = SelfTest.Carry("probe.stage.wood", wood.ToString());
        Check(StageDisabled, carried && wood >= 2 && SelfTest.Stage == "",
            $"first game: {wood} {StageItem} left in the inventory and the count carried to the second game",
            $"first game: carry {carried}, {wood} {StageItem}, stage '{SelfTest.Stage}'");
        yield return null;
    }

    private static IEnumerator DisabledCheck()
    {
        var p = Player.m_localPlayer;
        var text = SelfTest.Carried("probe.stage.wood");
        var wood = p != null ? CountItem(p, StageItem) : -1;
        Check(StageDisabled, SelfTest.Stage == SelfTest.DisabledStage, "second game: stage is 'disabled'", $"second game: stage is '{SelfTest.Stage}'");
        Check(StageDisabled, text != null && int.TryParse(text, out var want) && want == wood,
            $"second game: the carried count ({text}) is what the loaded character holds ({wood} {StageItem})",
            $"second game: carried '{text}', the character holds {wood} {StageItem}");
        yield return null;
    }

    // counts: [0] tests run, [1] passed, [2] failed.
    internal static IEnumerator RunFirstGameStages(ProbeTimings timings, int[] counts)
    {
        if (ProbeSettings.Wanted(Input))
        {
            yield return One(Input, ProbeGuid, new Func<IEnumerator>[] { RunInputCheck }, new[] { 0f }, counts, timings);
        }

        var mods = Mods();
        if (ProbeSettings.Wanted(Relog) || mods.Any(m => ProbeSettings.Wanted(RelogPrefix + m.Guid)))
        {
            List<LogEntry> watched = null;
            var relogOk = new bool[1];
            yield return One(Relog, ProbeGuid, new Func<IEnumerator>[]
            {
                () => StartWatch(),
                () => Logout(),
                () => StartAgain(timings),
                () => EndWatch(list => watched = list, relogOk),
            }, new[] { 0f, LogoutTimeout, ProbeDriver.MenuTimeout + ProbeDriver.SpawnTimeout + ProbeDriver.SettleTimeout, 0f }, counts, timings);
            foreach (var m in mods)
            {
                var name = RelogPrefix + m.Guid;
                if (!ProbeSettings.Wanted(name))
                {
                    continue;
                }
                var mod = m;
                yield return One(name, ProbeGuid, new Func<IEnumerator>[] { () => ModVerdict(name, mod, watched, relogOk[0],
                    "while logging out to the main menu and loading the world again") }, new[] { 0f }, counts, timings);
            }
        }

        foreach (var t in Snapshot(SelfTest.GetRelogTests()))
        {
            var name = t.TryGetValue("name", out var n) ? n as string : null;
            var before = t.TryGetValue("before", out var b) ? b as Func<IEnumerator> : null;
            var menu = t.TryGetValue("menu", out var mn) ? mn as Func<IEnumerator> : null;
            var after = t.TryGetValue("after", out var a) ? a as Func<IEnumerator> : null;
            if (name == null || !ProbeSettings.Wanted(name))
            {
                continue;
            }
            var owner = ProbeDriver.OwnerOf((Delegate)before ?? (Delegate)after ?? menu);
            yield return One(name, owner, new Func<IEnumerator>[] { before, () => Logout(), menu, () => StartAgain(timings), after },
                new[] { 0f, LogoutTimeout, 0f, ProbeDriver.MenuTimeout + ProbeDriver.SpawnTimeout + ProbeDriver.SettleTimeout, 0f }, counts, timings);
        }

        var second = 0;
        foreach (var m in mods)
        {
            if (ProbeSettings.Wanted(DisabledPrefix + m.Guid))
            {
                second++;
            }
        }
        foreach (var t in Snapshot(SelfTest.GetDisabledTests()))
        {
            var name = t.TryGetValue("name", out var n) ? n as string : null;
            var leave = t.TryGetValue("leave", out var l) ? l as Func<IEnumerator> : null;
            var check = t.TryGetValue("check", out var c) ? c as Func<IEnumerator> : null;
            if (name == null || check == null || !ProbeSettings.Wanted(name))
            {
                continue;
            }
            second++;
            if (leave != null)
            {
                yield return One(name, ProbeDriver.OwnerOf(leave), new[] { leave }, new[] { 0f }, counts, timings);
            }
        }
        if (second > 0)
        {
            // Script read this line.
            SelfTest.Note("probe", $"second game wanted: disabled ({second} test(s))");
        }
    }

    // Second game: every MC mod is off. Only the disabled-start checks.
    internal static List<KeyValuePair<string, Func<IEnumerator>>> DisabledStageList(List<string> skipped, out int registeredCount)
    {
        var tests = new List<KeyValuePair<string, Func<IEnumerator>>>
        {
            new KeyValuePair<string, Func<IEnumerator>>(ProbeTests.Baseline, ProbeTests.RunBaseline),
        };
        foreach (var m in Mods())
        {
            var name = DisabledPrefix + m.Guid;
            if (ProbeSettings.Wanted(name))
            {
                var mod = m;
                tests.Add(new KeyValuePair<string, Func<IEnumerator>>(name, () => DisabledVerdict(name, mod)));
            }
        }
        registeredCount = 0;
        foreach (var t in Snapshot(SelfTest.GetDisabledTests()))
        {
            var name = t.TryGetValue("name", out var n) ? n as string : null;
            var check = t.TryGetValue("check", out var c) ? c as Func<IEnumerator> : null;
            if (name == null || check == null)
            {
                continue;
            }
            registeredCount++;
            if (!ProbeSettings.Wanted(name))
            {
                skipped.Add($"{name} ({ProbeDriver.OwnerOf(check)})");
                continue;
            }
            tests.Add(new KeyValuePair<string, Func<IEnumerator>>(name, check));
        }
        return tests;
    }

    private static IEnumerator One(string name, string owner, Func<IEnumerator>[] parts, float[] timeouts, int[] counts, ProbeTimings timings)
    {
        var outcome = new bool[1];
        yield return ProbeDriver.RunParts(name, owner, parts, timeouts, outcome);
        counts[0]++;
        counts[outcome[0] ? 1 : 2]++;
        // A part that failed half way can leave the game at the main menu: next test need the world.
        var back = new RunResult();
        yield return SafeRunner.Run(EnsureInWorld(timings), ProbeDriver.MenuTimeout + ProbeDriver.SpawnTimeout + ProbeDriver.SettleTimeout, back);
        if (back.TimedOut || back.Error != null)
        {
            SelfTest.Note("probe", $"could not get back into the world after {name}: "
                                   + (back.TimedOut ? $"timed out after {back.Seconds:F0} s" : back.Error.Message));
        }
        InputDriver.Release();
        yield return ProbeDriver.BetweenTests(name);
    }

    private static List<Dictionary<string, object>> Snapshot(List<Dictionary<string, object>> list)
    {
        lock (list)
        {
            return new List<Dictionary<string, object>>(list);
        }
    }

    private static List<FeatureView> Mods() => FeatureRegistry.All()
        .Where(f => !f.Guid.StartsWith("MC.Core.Probe", StringComparison.Ordinal))
        .OrderBy(f => f.Guid, StringComparer.Ordinal).ToList();

    // ---------- relog ----------

    // Like Esc > Log out: character and world saved, back at the main menu, menu ready.
    private static IEnumerator Logout()
    {
        var game = Game.instance;
        if (game == null || game.m_shuttingDown || Player.m_localPlayer == null)
        {
            throw new InvalidOperationException("not in a world: cannot log out");
        }
        SelfTest.Note("probe", "relog: logging out to the main menu");
        game.Logout(save: true);
        var end = Time.realtimeSinceStartup + LogoutTimeout - 10f;
        var seenAt = -1f;
        while (true)
        {
            var f = FejdStartup.instance;
            var ready = f != null && f.m_profiles != null && ZSteamMatchmaking.instance != null && Player.m_localPlayer == null;
            seenAt = !ready ? -1f : seenAt < 0f ? Time.realtimeSinceStartup : seenAt;
            if (seenAt >= 0f && Time.realtimeSinceStartup - seenAt >= 2f)
            {
                break;
            }
            if (Time.realtimeSinceStartup > end)
            {
                throw new TimeoutException("not back at the main menu after logging out");
            }
            yield return null;
        }
        SelfTest.Note("probe", "relog: at the main menu");
    }

    private static IEnumerator StartAgain(ProbeTimings timings)
    {
        yield return MenuDriver.StartWorld(timings, again: true);
        yield return ProbeDriver.WaitSpawnAndSettle(timings);
        SelfTest.Note("probe", "relog: back in the world");
    }

    private static IEnumerator EnsureInWorld(ProbeTimings timings)
    {
        if (Player.m_localPlayer != null && Game.instance != null && !Game.instance.m_shuttingDown)
        {
            yield break;
        }
        // Between scenes: wait for the menu.
        var end = Time.realtimeSinceStartup + 120f;
        while (FejdStartup.instance == null && Player.m_localPlayer == null)
        {
            if (Time.realtimeSinceStartup > end)
            {
                throw new TimeoutException("neither in a world nor at the main menu for 120 s");
            }
            yield return null;
        }
        if (Player.m_localPlayer != null)
        {
            yield break;
        }
        SelfTest.Note("probe", "at the main menu between tests: loading the world again");
        yield return StartAgain(timings);
    }

    private static IEnumerator StartWatch()
    {
        ProbeDriver.Listener.StartWatch();
        yield break;
    }

    private static IEnumerator EndWatch(Action<List<LogEntry>> give, bool[] ok)
    {
        // Late lines of the load (first frames in the world) belong to the relog too.
        yield return new WaitForSecondsRealtime(3f);
        var list = ProbeDriver.Listener.StopWatch();
        give(list);
        ok[0] = Player.m_localPlayer != null;
        if (ok[0])
        {
            SelfTest.Pass(Relog, $"logged out to the main menu and loaded the world again ({list.Count} warning or error line(s) logged meanwhile, judged per mod)");
        }
        else
        {
            SelfTest.Fail(Relog, "no local player after the relog");
        }
    }

    // ---------- per-mod verdicts ----------

    private static IEnumerator ModVerdict(string test, FeatureView mod, List<LogEntry> entries, bool windowOk, string when)
    {
        if (!windowOk || entries == null)
        {
            SelfTest.Fail(test, "the relog itself did not work (see probe.relog): nothing judged");
            yield break;
        }
        Judge(test, mod, entries, when);
    }

    // Line is the mod's when its log source is the mod (warning or error), or when an error of another source name
    // the mod's code (namespace or GUID, as in a stack trace).
    private static void Judge(string test, FeatureView mod, List<LogEntry> entries, string when)
    {
        var ns = Namespace(mod.Guid);
        var mine = entries.Where(e => e.Source == mod.Name
                                      || (e.Error && (e.Text.IndexOf(ns + ".", StringComparison.Ordinal) >= 0
                                                      || e.Text.IndexOf(mod.Guid, StringComparison.Ordinal) >= 0))).ToList();
        if (mine.Count == 0)
        {
            SelfTest.Pass(test, $"no warning or error line of {mod.Name} and no error naming its code {when} ({entries.Count} such line(s) of others)");
            return;
        }
        SelfTest.Fail(test, $"{mine.Count} warning or error line(s) of {mod.Name} {when}, first: "
                            + string.Join(" || ", mine.Take(3).Select(e => $"[{(e.Error ? "Error" : "Warning")}:{e.Source}] {e.FirstLine}")));
    }

    // GUID MC.<Category>.<System>.<Feature> -> root namespace MC.<Category>.<SystemFeature>Mod.
    private static string Namespace(string guid)
    {
        var parts = guid.Split('.');
        return parts.Length < 3 ? guid : $"{parts[0]}.{parts[1]}.{string.Concat(parts.Skip(2))}Mod";
    }

    private static IEnumerator DisabledVerdict(string test, FeatureView mod)
    {
        yield return null;
        var off = mod.IsDisabledByUser && mod.Status.IndexOf("disabled in settings", StringComparison.OrdinalIgnoreCase) >= 0;
        if (off)
        {
            SelfTest.Pass(test, $"{mod.Name} is off from the start: state {mod.State}, status \"{mod.Status}\"");
        }
        else
        {
            SelfTest.Fail(test, $"{mod.Name} should be off (Enabled = false before the game started): state {mod.State}, status \"{mod.Status}\"");
        }
        var entries = ReadLogFile(out var problem);
        if (entries == null)
        {
            SelfTest.Fail(test, "could not read the log file of this game: " + problem);
            yield break;
        }
        Judge(test, mod, entries, "since the game started with the mod turned off");
    }

    private static readonly Regex LogLine = new Regex(@"^\[(Debug|Info|Message|Warning|Error|Fatal)\s*:\s*([^\]]*?)\]\s?(.*)$", RegexOptions.Compiled);

    // Whole log of this game process from disk (probe may load after other mods: a listener would miss their first lines).
    private static List<LogEntry> ReadLogFile(out string problem)
    {
        problem = null;
        try
        {
            ProbeDriver.FlushLog();
            var path = Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log");
            string text;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs))
            {
                text = reader.ReadToEnd();
            }
            var list = new List<LogEntry>();
            LogEntry current = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                var m = LogLine.Match(line);
                if (!m.Success)
                {
                    if (current != null)
                    {
                        current.Text += "\n" + line;
                    }
                    continue;
                }
                current = null;
                var level = m.Groups[1].Value;
                if (level != "Warning" && level != "Error" && level != "Fatal")
                {
                    continue;
                }
                if (m.Groups[3].Value.StartsWith("[selftest] FAIL ", StringComparison.Ordinal))
                {
                    continue;
                }
                current = new LogEntry { Error = level != "Warning", Source = m.Groups[2].Value.Trim(), Text = m.Groups[3].Value };
                list.Add(current);
            }
            return list;
        }
        catch (Exception e)
        {
            problem = e.Message;
            return null;
        }
    }

    // ---------- probe.input ----------

    // Input driver reach the game: what a test press is what ZInput read, and all of it is gone after ReleaseInput.
    private static IEnumerator RunInputCheck()
    {
        const string T = Input;
        var player = Player.m_localPlayer;
        var startPos = player != null ? player.transform.position : Vector3.zero;
        try
        {
            SelfTest.Note(T, $"game window focused: {Application.isFocused}, keyboard {Keyboard.current != null}, mouse {Mouse.current != null}, "
                             + $"gamepad active before: {ZInput.IsGamepadActive()}");

            // Keyboard key nobody binds.
            var key = KeyCode.None;
            foreach (var candidate in new[] { KeyCode.ScrollLock, KeyCode.Pause, KeyCode.F11, KeyCode.F9 })
            {
                if (ZInput.TryKeyCodeToKey(candidate, out var k) && k != UnityEngine.InputSystem.Key.None)
                {
                    key = candidate;
                    break;
                }
            }
            if (key == KeyCode.None)
            {
                SelfTest.Fail(T, "no test key the game can read");
                yield break;
            }
            yield return PressCheck(T, key, "keyboard key");
            // Side button of the mouse (no game action on it); middle button when the game knows no side button.
            var mouseKey = KeyCode.None;
            foreach (var candidate in new[] { KeyCode.Mouse4, KeyCode.Mouse3, KeyCode.Mouse2 })
            {
                if (ZInput.TryKeyCodeToMouseButton(candidate, out _))
                {
                    mouseKey = candidate;
                    break;
                }
            }
            if (mouseKey == KeyCode.None)
            {
                SelfTest.Fail(T, "no test mouse button the game can read");
                yield break;
            }
            yield return PressCheck(T, mouseKey, "mouse button");

            // KeyTap: one press, one release.
            var downs = 0;
            var ups = 0;
            var tap = SelfTest.KeyTap(key, 0.2f);
            while (tap.MoveNext())
            {
                yield return tap.Current;
                downs += ZInput.GetKeyDown(key) ? 1 : 0;
                ups += ZInput.GetKeyUp(key) ? 1 : 0;
            }
            for (var i = 0; i < 5; i++)
            {
                yield return null;
                downs += ZInput.GetKeyDown(key) ? 1 : 0;
                ups += ZInput.GetKeyUp(key) ? 1 : 0;
            }
            Check(T, downs == 1 && ups == 1 && !ZInput.GetKey(key), $"KeyTap({key}): one press and one release seen by ZInput",
                $"KeyTap({key}): {downs} press(es) and {ups} release(s) seen, still down {ZInput.GetKey(key)} (expected 1, 1, False)");

            // Wheel: one notch up, then one down (camera zoom back where it was).
            var wheel = 0f;
            SelfTest.MouseWheel(1f);
            for (var i = 0; i < 5; i++)
            {
                yield return null;
                wheel += ZInput.GetMouseScrollWheel();
            }
            Check(T, wheel > 0f, $"mouse wheel up seen by ZInput (sum {wheel:F2})", $"mouse wheel up not seen by ZInput (sum {wheel:F2})");
            var back = 0f;
            SelfTest.MouseWheel(-1f);
            for (var i = 0; i < 5; i++)
            {
                yield return null;
                back += ZInput.GetMouseScrollWheel();
            }
            Check(T, back < 0f, $"mouse wheel down seen by ZInput (sum {back:F2})", $"mouse wheel down not seen by ZInput (sum {back:F2})");

            // Test gamepad: stick, trigger, button; the game switches to gamepad input.
            SelfTest.PadStick(false, new Vector2(0f, 0.8f));
            var stick = 0f;
            var padActive = false;
            for (var i = 0; i < 20; i++)
            {
                yield return null;
                stick = Mathf.Max(stick, ZInput.GetJoyLeftStickY() * -1f, ZInput.GetJoyLeftStickY());
                padActive |= ZInput.IsGamepadActive();
            }
            SelfTest.PadStick(false, Vector2.zero);
            SelfTest.Pad("LeftTrigger", 1f);
            var trigger = 0f;
            var triggerButton = false;
            for (var i = 0; i < 10; i++)
            {
                yield return null;
                trigger = Mathf.Max(trigger, ZInput.GetJoyLTrigger());
                triggerButton |= ZInput.GetButton("JoyLTrigger");
                padActive |= ZInput.IsGamepadActive();
            }
            SelfTest.Pad("LeftTrigger", 0f);
            var pressed = false;
            var namedDown = false;
            var namedHeld = false;
            SelfTest.Pad("North", 1f);
            for (var i = 0; i < 10; i++)
            {
                yield return null;
                pressed |= Gamepad.current != null && Gamepad.current.buttonNorth.isPressed;
                namedDown |= ZInput.GetButtonDown("JoyButtonY");
                namedHeld |= ZInput.GetButton("JoyButtonY");
                padActive |= ZInput.IsGamepadActive();
            }
            SelfTest.Pad("North", 0f);
            var namedUp = false;
            for (var i = 0; i < 5; i++)
            {
                yield return null;
                namedUp |= ZInput.GetButtonUp("JoyButtonY");
            }
            // Named buttons are what the game and the mods read ("JoyButtonY" = the physical top face button).
            Check(T, namedDown && namedHeld && namedUp && !ZInput.GetButton("JoyButtonY"),
                "test gamepad top face button: press, hold and release seen through ZInput.GetButton(\"JoyButtonY\")",
                $"test gamepad top face button through ZInput \"JoyButtonY\": press {namedDown}, held {namedHeld}, release {namedUp}, down after {ZInput.GetButton("JoyButtonY")}");
            Check(T, triggerButton, "test gamepad left trigger seen through ZInput.GetButton(\"JoyLTrigger\")",
                "test gamepad left trigger never seen through ZInput.GetButton(\"JoyLTrigger\")");
            Check(T, InputDriver.PadExists && stick > 0.5f, $"test gamepad left stick seen by ZInput ({stick:F2})",
                $"test gamepad left stick not seen by ZInput ({stick:F2}, pad exists {InputDriver.PadExists})");
            Check(T, trigger > 0.5f, $"test gamepad left trigger seen by ZInput ({trigger:F2})", $"test gamepad left trigger not seen by ZInput ({trigger:F2})");
            Check(T, pressed, "test gamepad button seen as pressed on the current gamepad", "test gamepad button never pressed on the current gamepad");
            Check(T, padActive, "the game switched to gamepad input", "the game never switched to gamepad input (ZInput.IsGamepadActive stayed false)");

            // All gone again.
            SelfTest.ReleaseInput();
            var stillPad = true;
            for (var i = 0; i < 30 && stillPad; i++)
            {
                yield return null;
                stillPad = ZInput.IsGamepadActive();
            }
            Check(T, !InputDriver.PadExists && !stillPad && !ZInput.GetKey(key) && !ZInput.GetKey(mouseKey),
                "after ReleaseInput: test gamepad removed, game back on keyboard and mouse, nothing held",
                $"after ReleaseInput: pad exists {InputDriver.PadExists}, gamepad active {stillPad}, key down {ZInput.GetKey(key)}, mouse button down {ZInput.GetKey(mouseKey)}");
        }
        finally
        {
            InputDriver.Release();
            if (player != null)
            {
                // Stick push walked the player a step.
                player.transform.position = startPos;
            }
        }
    }

    private static IEnumerator PressCheck(string test, KeyCode key, string what)
    {
        var down = false;
        SelfTest.Key(key, true);
        for (var i = 0; i < 5; i++)
        {
            yield return null;
            down |= ZInput.GetKeyDown(key);
        }
        var held = ZInput.GetKey(key);
        var up = false;
        SelfTest.Key(key, false);
        for (var i = 0; i < 5; i++)
        {
            yield return null;
            up |= ZInput.GetKeyUp(key);
        }
        Check(test, down && held && up && !ZInput.GetKey(key), $"{what} {key}: press, hold and release seen by ZInput",
            $"{what} {key}: press {down}, held {held}, release {up}, down after {ZInput.GetKey(key)} (expected True, True, True, False)");
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
