#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MC.Exploration.CompendiumEncyclopediaMod;

// Debug build only (whole file; Release has nothing of me): UI self tests with REAL controller input. The probe change
// the state of a test gamepad (SelfTest.Pad / PadStick), so ZInput, the game and our code read a press like from a
// person: me never call the mod in place of a press. Which control of the pad a game button sit on, me read from the
// game's own binding (layout, swapped triggers), never assume. One small test per part of a TESTING.md item: real
// input is new, so one test can be fixed or dropped alone. Settings forced in memory only (UiRun), never a ConfigEntry
// write. Every test end with SelfTest.ReleaseInput (test gamepad gone, game back on keyboard and mouse).
// T15 (controller in the Encyclopedia):
//   compendium.input.move       = D-pad and left stick move the selection row by row, over headers; inventory inert.
//   compendium.input.rumble     = each such move start the game's select vibration effect (none when nothing moved).
//   compendium.input.category   = LB / RB change the category tab (wrap), the inventory's own LB / RB focus stay.
//   compendium.input.scroll     = the right stick scroll a long details page, the selection stay.
//   compendium.input.close      = B close only the Encyclopedia.
//   compendium.input.hints      = key hints hidden while open, back after B and after a mouse click on Close.
//   compendium.input.glyphs     = on controller input the window and the two top tabs draw nothing more (no glyph).
//   compendium.input.select     = optional button: View/Select open from the side panel only.
// T37 (controller, the two top tabs):
//   compendium.input.triggers   = RT = Encyclopedia, LT = Texts, the other trigger do nothing, both ways twice.
//   compendium.input.nav        = after trigger switch: LB / RB in the Encyclopedia, D-pad / sticks in Texts.
//   compendium.input.crafttabs  = at a workbench: RT / LT switch Craft / Upgrade with the crafting panel focused
//                                 (control), and never from Texts or the Encyclopedia.
//   compendium.input.close-both = B close Texts, B close the Encyclopedia reached with RT; the inventory stay.
//   compendium.input.othermods  = Sort Chest, Loot Pickup Filter, Crafting Search and Sort: nothing of theirs change
//                                 from LT / RT presses over a chest.
internal static class UiInputSelfTests
{
    private const string MoveName = "compendium.input.move";
    private const string RumbleName = "compendium.input.rumble";
    private const string CategoryName = "compendium.input.category";
    private const string ScrollName = "compendium.input.scroll";
    private const string CloseName = "compendium.input.close";
    private const string HintsName = "compendium.input.hints";
    private const string GlyphsName = "compendium.input.glyphs";
    private const string SelectName = "compendium.input.select";
    private const string TriggersName = "compendium.input.triggers";
    private const string NavName = "compendium.input.nav";
    private const string CraftTabsName = "compendium.input.crafttabs";
    private const string CloseBothName = "compendium.input.close-both";
    private const string OtherModsName = "compendium.input.othermods";

    private const string CraftSearchGuid = "MC.UX.Crafting.SearchSort";
    private const string LootFilterGuid = "MC.UX.AutoPickup.Filter";
    private const string SortChestGuid = "MC.UX.Container.Sort";

    // Game buttons (ZInput names) me press. All of them sit on the same control in every controller layout.
    private const string DpadDown = "JoyDPadDown";
    private const string DpadUp = "JoyDPadUp";
    private const string TabLeft = "JoyTabLeft";
    private const string TabRight = "JoyTabRight";
    private const string ButtonB = "JoyButtonB";

    // Under the 0.3 s after which the game presses a held D-pad button again.
    private const float MaxHold = 0.25f;
    // Over the 0.1 s our window and the game's Texts list wait between two move.
    private const float Gap = 0.18f;

    private static readonly Vector2 StickDown = new Vector2(0f, -1f);
    private static readonly Vector2 StickUp = new Vector2(0f, 1f);

    private static readonly KeyValuePair<string, Func<IEnumerator>>[] Tests =
    {
        new KeyValuePair<string, Func<IEnumerator>>(MoveName, RunMove),
        new KeyValuePair<string, Func<IEnumerator>>(RumbleName, RunRumble),
        new KeyValuePair<string, Func<IEnumerator>>(CategoryName, RunCategory),
        new KeyValuePair<string, Func<IEnumerator>>(ScrollName, RunScroll),
        new KeyValuePair<string, Func<IEnumerator>>(CloseName, RunClose),
        new KeyValuePair<string, Func<IEnumerator>>(HintsName, RunHints),
        new KeyValuePair<string, Func<IEnumerator>>(GlyphsName, RunGlyphs),
        new KeyValuePair<string, Func<IEnumerator>>(SelectName, RunSelect),
        new KeyValuePair<string, Func<IEnumerator>>(TriggersName, RunTriggers),
        new KeyValuePair<string, Func<IEnumerator>>(NavName, RunNav),
        new KeyValuePair<string, Func<IEnumerator>>(CraftTabsName, RunCraftTabs),
        new KeyValuePair<string, Func<IEnumerator>>(CloseBothName, RunCloseBoth),
        new KeyValuePair<string, Func<IEnumerator>>(OtherModsName, RunOtherMods),
    };

    // Called by UiSelfTests.Register / Unregister (inside their Debug-only part).
    internal static void Register()
    {
        foreach (var t in Tests)
        {
            SelfTest.Register(t.Key, t.Value);
        }
    }

    internal static void Unregister()
    {
        foreach (var t in Tests)
        {
            SelfTest.Unregister(t.Key);
        }
    }

    // ---------------------------------------------------------------- the test gamepad

    /// <summary>One real press: what went in, and whether the game took it as a controller press.</summary>
    private sealed class PadPress
    {
        internal string Button = "";
        internal string Control = "";
        internal bool PadMode;
        internal bool PadAfter;
        internal bool Seen;
        internal string Why = "";

        internal void Reset(string button)
        {
            Button = button;
            Control = "";
            PadMode = false;
            PadAfter = false;
            Seen = false;
            Why = "";
        }

        internal bool Made => Why.Length == 0;

        internal string Tell => Why.Length > 0
            ? $"no press was made: {Why}"
            : $"'{Button}' pressed as {Control} of the test gamepad; game on controller input before {PadMode} and after {PadAfter}; "
              + $"read by the game {Seen}";
    }

    private static bool Need(List<string> problems, bool ok, string problem)
    {
        if (!ok)
        {
            problems.Add(problem);
        }
        return ok;
    }

    // Press reached the game as a controller press? No = said why (a "nothing happened" check after it proves nothing).
    private static bool Arrived(List<string> problems, PadPress r, string when, bool mustSee = true)
    {
        if (r.Made && r.PadMode && r.PadAfter && (r.Seen || !mustSee))
        {
            return true;
        }
        problems.Add($"{when}: the press did not reach the game as a controller press ({r.Tell})");
        return false;
    }

    // Probe's input driver there? (Every input call say false when no probe drive the game.)
    private static bool Driver(List<string> problems)
    {
        if (SelfTest.ReleaseInput() && ZInput.instance != null)
        {
            return true;
        }
        problems.Add("no input driver drives this game (a run of tools/Test-InWorld.ps1 is needed): no real controller press can be made");
        return false;
    }

    /// <summary>
    /// Control of the test gamepad the game read this button from, by the game's own binding (controller layout,
    /// swapped triggers). Null = the button has no controller binding in this layout.
    /// </summary>
    private static string PadControl(string button)
    {
        var zin = ZInput.instance;
        if (zin == null || zin.m_buttons == null || !zin.m_buttons.TryGetValue(button, out var def) || def == null)
        {
            return null;
        }
        string path;
        try
        {
            path = def.GetActionPath();
        }
        catch (Exception)
        {
            return null; // button without binding (unused in this layout)
        }
        switch (path)
        {
            case "<Gamepad>/buttonSouth":
                return "South";
            case "<Gamepad>/buttonEast":
                return "East";
            case "<Gamepad>/buttonWest":
                return "West";
            case "<Gamepad>/buttonNorth":
                return "North";
            case "<Gamepad>/leftShoulder":
                return "LeftShoulder";
            case "<Gamepad>/rightShoulder":
                return "RightShoulder";
            case "<Gamepad>/leftTrigger":
                return "LeftTrigger";
            case "<Gamepad>/rightTrigger":
                return "RightTrigger";
            case "<Gamepad>/select":
                return "Select";
            case "<Gamepad>/start":
                return "Start";
            case "<Gamepad>/leftStickPress":
                return "LeftStick";
            case "<Gamepad>/rightStickPress":
                return "RightStick";
            case "<Gamepad>/dpad/up":
                return "DpadUp";
            case "<Gamepad>/dpad/down":
                return "DpadDown";
            case "<Gamepad>/dpad/left":
                return "DpadLeft";
            case "<Gamepad>/dpad/right":
                return "DpadRight";
            default:
                return null;
        }
    }

    // Game on controller input with no mouse = what a controller player has. Me push the test gamepad's right stick
    // sideways for a moment: nothing on the inventory screen, the Texts dialog or our window read that axis (all read
    // up / down only). A real mouse move take the game back to the mouse: then me wake the pad again.
    // Only call me while the inventory is shown (in the world that axis turn the camera).
    private static IEnumerator EnsurePad(PadPress r)
    {
        r.PadMode = ZInput.IsExclusiveGamepadActive();
        for (var attempt = 0; attempt < 3 && !r.PadMode; attempt++)
        {
            SelfTest.PadStick(true, new Vector2(0.9f, 0f));
            var until = Time.realtimeSinceStartup + 0.8f;
            while (Time.realtimeSinceStartup < until && !ZInput.IsExclusiveGamepadActive())
            {
                yield return null;
            }
            SelfTest.PadStick(true, Vector2.zero);
            yield return TestKit.Frames(3);
            r.PadMode = ZInput.IsExclusiveGamepadActive();
        }
        if (!r.PadMode)
        {
            r.Why = $"the game did not switch to controller input (controller allowed in the game settings: {ZInput.IsGamepadEnabled()}, "
                    + $"mouse in use: {ZInput.IsMouseActive()})";
        }
    }

    // One real press of the control the game bind to this button: game on controller input first, control down until
    // the game has read the button for two frames (at most MaxHold), up, three more frames. mustSee false = a button
    // its reader reset in the same frame (B): held two frames. eachFrame run after every frame of the press.
    private static IEnumerator Tap(string button, PadPress r, bool mustSee = true, Action eachFrame = null)
    {
        r.Reset(button);
        var control = PadControl(button);
        if (control == null)
        {
            r.Why = $"the game binds '{button}' to no button of a controller in this layout ({ZInput.InputLayout})";
            yield break;
        }
        r.Control = control;
        yield return EnsurePad(r);
        if (!r.PadMode)
        {
            yield break;
        }
        if (!SelfTest.Pad(control, 1f))
        {
            r.Why = $"the input driver did not take '{control}'";
            yield break;
        }
        var until = Time.realtimeSinceStartup + MaxHold;
        var held = 0;
        while (Time.realtimeSinceStartup < until && held < 2)
        {
            yield return null;
            eachFrame?.Invoke();
            if (ZInput.GetButton(button) || ZInput.GetButtonDown(button))
            {
                r.Seen = true;
            }
            if (r.Seen || !mustSee)
            {
                held++;
            }
        }
        SelfTest.Pad(control, 0f);
        for (var i = 0; i < 3; i++)
        {
            yield return null;
            eachFrame?.Invoke();
        }
        r.PadAfter = ZInput.IsExclusiveGamepadActive();
    }

    // A stick of the test gamepad held one way for some time (real time), then let go, three more frames.
    private static IEnumerator Stick(bool right, Vector2 value, float seconds, PadPress r, Action eachFrame = null)
    {
        r.Reset(right ? "right stick" : "left stick");
        r.Control = $"the {(right ? "right" : "left")} stick at ({value.x:F1}, {value.y:F1})";
        yield return EnsurePad(r);
        if (!r.PadMode)
        {
            yield break;
        }
        if (!SelfTest.PadStick(right, value))
        {
            r.Why = "the input driver did not take the stick";
            yield break;
        }
        var until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until)
        {
            yield return null;
            eachFrame?.Invoke();
            var y = right ? ZInput.GetJoyRightStickY() : ZInput.GetJoyLeftStickY();
            if (Mathf.Abs(y) > 0.5f)
            {
                r.Seen = true;
            }
        }
        SelfTest.PadStick(right, Vector2.zero);
        for (var i = 0; i < 3; i++)
        {
            yield return null;
            eachFrame?.Invoke();
        }
        r.PadAfter = ZInput.IsExclusiveGamepadActive();
    }

    private static IEnumerator Pause()
    {
        yield return new WaitForSecondsRealtime(Gap);
    }

    // Test gamepad gone and the game back on keyboard and mouse (the tests after me expect that).
    private static IEnumerator BackToMouse()
    {
        SelfTest.ReleaseInput();
        for (var i = 0; i < 90 && ZInput.IsGamepadActive(); i++)
        {
            yield return null;
        }
    }

    // ---------------------------------------------------------------- small helpers

    private static string PadNote() => $"controller layout {ZInput.InputLayout}, triggers swapped {ZInput.SwapTriggers}";

    // Inventory shown, Encyclopedia open from its tab (clicks, as the other tests do), list ready. Not open = said why.
    private static IEnumerator OpenWindow(UiRun run, string when)
    {
        yield return TestKit.ShowInventory(run.Gui);
        yield return TestKit.OpenByTab(run.Gui, run.Problems, when);
    }

    // The window object itself is switched on = what the player see. The mod's own open flag alone prove nothing:
    // every "open" / "closed" check below ask both.
    private static bool WindowOnScreen => CompendiumWindow.Root != null && CompendiumWindow.Root.activeInHierarchy;

    private static bool WindowReady => CompendiumWindow.IsOpen && WindowOnScreen && !CompendiumWindow.Preparing;

    // Window gone for the player: flag off and its object off (or destroyed).
    private static bool WindowGone => !CompendiumWindow.IsOpen && !WindowOnScreen;

    // Entry rows of the open tab: the entries, and the list row each one sit on.
    private static List<Entry> EntryList(List<int> rowOf)
    {
        var rows = CompendiumWindow.Rows;
        var entries = new List<Entry>();
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Kind == ListRowKind.Entry)
            {
                entries.Add(rows[i].Entry);
                rowOf?.Add(i);
            }
        }
        return entries;
    }

    private static int SelectedAt(List<Entry> entries)
    {
        var selected = CompendiumWindow.SelectedEntry;
        for (var i = 0; i < entries.Count; i++)
        {
            if (ReferenceEquals(entries[i], selected))
            {
                return i;
            }
        }
        return -1;
    }

    private static void SelectEntry(Entry e) => CompendiumWindow.SelectWhere(r => ReferenceEquals(r.Entry, e));

    private static bool On(GameObject go) => go != null && go.activeSelf;

    // Any of the game's key hint groups at the bottom of the screen switched on.
    private static bool AnyHints(KeyHints h) =>
        On(h.m_inventoryHints) || On(h.m_inventoryWithContainerHints) || On(h.m_combatHints) || On(h.m_buildHints)
        || On(h.m_fishingHints) || On(h.m_barberHints) || On(h.m_radialHints);

    // ---------------------------------------------------------------- compendium.input.move (T15)

    private static IEnumerator RunMove()
    {
        var run = new UiRun();
        yield return run.Begin(MoveName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        try
        {
            run.Force(sideButton: false);
            if (!Driver(p))
            {
                run.Report();
                yield break;
            }
            yield return OpenWindow(run, "open");
            if (!WindowReady)
            {
                run.Report();
                yield break;
            }
            CompendiumWindow.SelectTab((int)CatalogTab.Weapons);
            yield return null;
            var rowOf = new List<int>();
            var entries = EntryList(rowOf);
            // An entry with a group header right under it (the next entry sit two rows lower).
            var beforeHeader = -1;
            for (var i = 1; i + 1 < entries.Count && beforeHeader < 0; i++)
            {
                if (rowOf[i + 1] - rowOf[i] > 1)
                {
                    beforeHeader = i;
                }
            }
            if (entries.Count < 12 || beforeHeader < 0)
            {
                p.Add($"test setup: the Weapons tab lists {entries.Count} entries (12 needed), a group header between two of them: {beforeHeader >= 0}");
                run.Report();
                yield break;
            }
            var press = new PadPress();
            yield return EnsurePad(press);
            if (!press.PadMode)
            {
                p.Add($"test setup: {press.Why}");
                run.Report();
                yield break;
            }
            run.Note(PadNote());
            SelectEntry(entries[0]);
            yield return null;
            // What the inventory behind must keep: its focus group, the grid's selected slot, the selected control.
            var group = gui.ActiveGroup;
            var grid = gui.m_playerGrid != null ? gui.m_playerGrid.SelectionGridPosition : default;
            var es = EventSystem.current;
            var uiSelected = es != null ? es.currentSelectedGameObject : null;

            // D-pad: one press = one entry row.
            yield return Tap(DpadDown, press);
            Need(p, SelectedAt(entries) == 1, $"D-pad down on the first entry: selection on entry {SelectedAt(entries)} (expected 1); {press.Tell}");
            yield return Pause();
            yield return Tap(DpadDown, press);
            Need(p, SelectedAt(entries) == 2, $"D-pad down again: selection on entry {SelectedAt(entries)} (expected 2); {press.Tell}");
            yield return Pause();
            yield return Tap(DpadUp, press);
            Need(p, SelectedAt(entries) == 1, $"D-pad up: selection on entry {SelectedAt(entries)} (expected 1); {press.Tell}");

            // Over a group header: the header row is never selected.
            SelectEntry(entries[beforeHeader]);
            yield return Pause();
            yield return Tap(DpadDown, press);
            Need(p, SelectedAt(entries) == beforeHeader + 1,
                $"D-pad down on the last entry of a group (list row {rowOf[beforeHeader]}): selection on entry {SelectedAt(entries)} "
                + $"(expected {beforeHeader + 1}, list row {rowOf[beforeHeader + 1]}, the header skipped); {press.Tell}");
            yield return Pause();
            yield return Tap(DpadUp, press);
            Need(p, SelectedAt(entries) == beforeHeader, $"D-pad up over the header: selection on entry {SelectedAt(entries)} (expected {beforeHeader}); {press.Tell}");

            // Left stick: held = row after row.
            SelectEntry(entries[0]);
            yield return Pause();
            yield return Stick(false, StickDown, 0.45f, press);
            var down = SelectedAt(entries);
            Need(p, down >= 2, $"left stick held down for 0.45 s from the first entry: selection on entry {down} (expected 2 or more); {press.Tell}");
            yield return Pause();
            yield return Stick(false, StickUp, 0.3f, press);
            var up = SelectedAt(entries);
            Need(p, up >= 0 && up < down, $"left stick held up for 0.3 s: selection went from entry {down} to {up} (expected a lower one); {press.Tell}");

            var gridNow = gui.m_playerGrid != null ? gui.m_playerGrid.SelectionGridPosition : default;
            var uiNow = es != null ? es.currentSelectedGameObject : null;
            Need(p, CompendiumWindow.IsOpen && gui.ActiveGroup == group && gridNow.x == grid.x && gridNow.y == grid.y && uiNow == uiSelected
                    && (gui.m_inventoryGroup == null || !gui.m_inventoryGroup.IsActive),
                $"the inventory reacted to the D-pad or the left stick behind the Encyclopedia: Encyclopedia open {CompendiumWindow.IsOpen}, focus group "
                + $"{group} -> {gui.ActiveGroup}, grid slot ({grid.x},{grid.y}) -> ({gridNow.x},{gridNow.y}), selected control "
                + $"'{(uiSelected != null ? uiSelected.name : "none")}' -> '{(uiNow != null ? uiNow.name : "none")}', inventory focus live "
                + $"{gui.m_inventoryGroup != null && gui.m_inventoryGroup.IsActive}");
            run.Note($"left stick: {down} entries down in 0.45 s, back up to entry {up} in 0.3 s");
            yield return BackToMouse();
            run.Detail = "real D-pad presses move the selection one entry down and up, and over a group header; the left stick held down or up "
                         + "moves row after row; the inventory behind keeps its focus group, grid slot and selected control";
        }
        finally
        {
            SelfTest.ReleaseInput();
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.input.rumble (T15)

    private static readonly List<GameObject> Roots = new List<GameObject>();

    // Objects the game made from its select vibration effect (GamepadRumble.PlayGlobalSelectVibration instantiate it:
    // root objects named "<effect>(Clone)"). fresh null = remember every one alive now; else add the ones not known.
    private static void Vibrations(GameObject effect, HashSet<int> known, HashSet<int> fresh)
    {
        var name = effect.name + "(Clone)";
        for (var s = 0; s <= SceneManager.sceneCount; s++)
        {
            Scene scene;
            if (s < SceneManager.sceneCount)
            {
                scene = SceneManager.GetSceneAt(s);
            }
            else
            {
                // The scene of the objects that outlive scene loads (GamepadRumble itself live there).
                scene = GamepadRumble.instance != null ? GamepadRumble.instance.gameObject.scene : default;
            }
            if (!scene.IsValid() || !scene.isLoaded)
            {
                continue;
            }
            Roots.Clear();
            scene.GetRootGameObjects(Roots);
            foreach (var go in Roots)
            {
                if (go == null || go.name != name)
                {
                    continue;
                }
                var id = go.GetInstanceID();
                if (fresh == null)
                {
                    known.Add(id);
                }
                else if (!known.Contains(id))
                {
                    fresh.Add(id);
                }
            }
        }
        Roots.Clear();
    }

    private static IEnumerator RunRumble()
    {
        var run = new UiRun();
        yield return run.Begin(RumbleName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        try
        {
            run.Force(sideButton: false);
            if (!Driver(p))
            {
                run.Report();
                yield break;
            }
            yield return OpenWindow(run, "open");
            if (!WindowReady)
            {
                run.Report();
                yield break;
            }
            CompendiumWindow.SelectTab((int)CatalogTab.Weapons);
            yield return null;
            var entries = EntryList(null);
            var rumble = GamepadRumble.instance;
            var effect = rumble != null ? rumble.GetGlobalSelectSfxVibration() : null;
            if (entries.Count < 12 || effect == null)
            {
                p.Add($"test setup: the Weapons tab lists {entries.Count} entries (12 needed), the game's select vibration effect exists: {effect != null}");
                run.Report();
                yield break;
            }
            // Can me see the effect at all? The game's own call must give exactly one new object.
            var known = new HashSet<int>();
            var fresh = new HashSet<int>();
            Vibrations(effect, known, null);
            rumble.PlayGlobalSelectVibration();
            Vibrations(effect, known, fresh);
            if (fresh.Count != 1)
            {
                p.Add($"test setup: after the game's own select vibration call the test finds {fresh.Count} new '{effect.name}' object(s) "
                      + "(expected 1): it cannot watch the vibration");
                run.Report();
                yield break;
            }
            yield return null;
            yield return null;
            var press = new PadPress();
            yield return EnsurePad(press);
            if (!press.PadMode)
            {
                p.Add($"test setup: {press.Why}");
                run.Report();
                yield break;
            }
            Action watch = () => Vibrations(effect, known, fresh);
            Action mark = () =>
            {
                known.UnionWith(fresh);
                fresh.Clear();
                Vibrations(effect, known, null);
            };

            SelectEntry(entries[0]);
            yield return Pause();
            mark();
            yield return Tap(DpadDown, press, eachFrame: watch);
            Need(p, SelectedAt(entries) == 1 && fresh.Count == 1,
                $"D-pad down: selection on entry {SelectedAt(entries)} (expected 1), {fresh.Count} select vibration(s) started (expected 1); {press.Tell}");
            yield return Pause();
            mark();
            yield return Tap(DpadUp, press, eachFrame: watch);
            Need(p, SelectedAt(entries) == 0 && fresh.Count == 1,
                $"D-pad up: selection on entry {SelectedAt(entries)} (expected 0), {fresh.Count} select vibration(s) started (expected 1); {press.Tell}");

            // Left stick held: one vibration per row moved.
            SelectEntry(entries[0]);
            yield return Pause();
            mark();
            yield return Stick(false, StickDown, 0.35f, press, watch);
            var moved = SelectedAt(entries);
            Need(p, moved >= 2 && fresh.Count == moved,
                $"left stick held down for 0.35 s: {moved} entries moved (expected 2 or more), {fresh.Count} select vibration(s) started (expected one per entry); {press.Tell}");

            // Nothing moved (last entry, down): no vibration.
            var last = entries.Count - 1;
            SelectEntry(entries[last]);
            yield return Pause();
            mark();
            yield return Tap(DpadDown, press, eachFrame: watch);
            if (Arrived(p, press, "D-pad down on the last entry"))
            {
                Need(p, SelectedAt(entries) == last && fresh.Count == 0,
                    $"D-pad down on the last entry: selection on entry {SelectedAt(entries)} (expected {last}), {fresh.Count} select vibration(s) started (expected none)");
            }
            run.Note($"select vibration effect '{effect.name}'; left stick moved {moved} entries; {PadNote()}");
            yield return BackToMouse();
            run.Detail = "every move of the selection by a real D-pad press or by the held left stick starts the game's select vibration "
                         + "effect once (the one the game's own lists start); a press that moves nothing starts none";
        }
        finally
        {
            SelfTest.ReleaseInput();
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.input.category (T15)

    private static IEnumerator RunCategory()
    {
        var run = new UiRun();
        yield return run.Begin(CategoryName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        try
        {
            run.Force(sideButton: false);
            if (!Driver(p))
            {
                run.Report();
                yield break;
            }
            yield return TestKit.ShowInventory(gui);
            var groups = gui.m_uiGroups;
            if (groups == null || groups.Length <= 3)
            {
                p.Add("test setup: the inventory has no focus groups");
                run.Report();
                yield break;
            }
            var press = new PadPress();
            // Control: Encyclopedia closed, player grid focused: the same RB / LB move the inventory's own focus.
            gui.SetActiveGroup(groups[1], playSound: false);
            yield return TestKit.Frames(3);
            yield return Tap(TabRight, press);
            var afterRb = gui.ActiveGroup;
            yield return Pause();
            yield return Tap(TabLeft, press);
            var afterLb = gui.ActiveGroup;
            if (!Need(p, afterRb == SideButton.SidePanelGroup && afterLb == 1,
                    $"control: without the Encyclopedia, RB then LB moved the inventory focus 1 -> {afterRb} -> {afterLb} (expected 1 -> 2 -> 1): "
                    + $"the checks below would prove nothing; {press.Tell}"))
            {
                run.Report();
                yield break;
            }
            run.Note(PadNote());

            yield return TestKit.OpenByTab(gui, p, "open");
            if (!WindowReady)
            {
                run.Report();
                yield break;
            }
            CompendiumWindow.SelectTab(0);
            yield return null;
            var group = gui.ActiveGroup;
            var visited = new List<int>();
            foreach (var step in new[] { TabRight, TabRight, TabLeft, TabLeft, TabLeft, TabRight })
            {
                yield return Pause();
                yield return Tap(step, press);
                visited.Add(CompendiumWindow.CurrentTab);
                if (!press.Made || !press.Seen)
                {
                    p.Add($"{(step == TabRight ? "RB" : "LB")} in the Encyclopedia: {press.Tell}");
                }
            }
            var last = Tabs.Count - 1;
            Need(p, visited.SequenceEqual(new[] { 1, 2, 1, 0, last, 0 }),
                $"RB RB LB LB LB RB from the first category tab showed tabs [{string.Join(", ", visited)}] (expected 1, 2, 1, 0, {last}, 0)");
            Need(p, CompendiumWindow.IsOpen && gui.ActiveGroup == group && (gui.m_inventoryGroup == null || !gui.m_inventoryGroup.IsActive),
                $"the inventory reacted to LB / RB behind the Encyclopedia: Encyclopedia open {CompendiumWindow.IsOpen}, inventory focus group "
                + $"{group} -> {gui.ActiveGroup}, inventory focus live {gui.m_inventoryGroup != null && gui.m_inventoryGroup.IsActive}");
            yield return BackToMouse();
            run.Detail = "real RB / LB presses change the Encyclopedia's category tab, wrapping at both ends; the inventory's own focus, which the "
                         + "same presses move when the Encyclopedia is closed, stays where it was";
        }
        finally
        {
            SelfTest.ReleaseInput();
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.input.scroll (T15)

    // How much taller than the pane the shown details are (window units).
    private static float DetailsOverflow()
    {
        var content = CompendiumWindow.DetailContent;
        var view = CompendiumWindow.DetailViewport;
        return content != null && view != null ? content.rect.height - view.rect.height : 0f;
    }

    private static IEnumerator RunScroll()
    {
        var run = new UiRun();
        yield return run.Begin(ScrollName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        try
        {
            run.Force(sideButton: false);
            if (!Driver(p))
            {
                run.Report();
                yield break;
            }
            yield return OpenWindow(run, "open");
            if (!WindowReady)
            {
                run.Report();
                yield break;
            }
            // Every detail shown (in memory only): long pages exist whatever this character knows.
            Plugin.SetTestDisplay(new Plugin.DisplayOverride { ShowUndiscovered = true, RevealAll = true });
            yield return null;
            Entry longEntry = null;
            var looked = 0;
            // Materials first: a material many recipes use has a long "Used in" list.
            foreach (var tab in new[] { CatalogTab.Materials, CatalogTab.Creatures, CatalogTab.Weapons, CatalogTab.Armor, CatalogTab.Building,
                         CatalogTab.Food, CatalogTab.Tools, CatalogTab.Trophies })
            {
                CompendiumWindow.SelectTab((int)tab);
                yield return null;
                var entries = EntryList(null);
                for (var i = 0; i < entries.Count && i < 150 && longEntry == null; i++)
                {
                    SelectEntry(entries[i]);
                    looked++;
                    if (DetailsOverflow() > 30f)
                    {
                        longEntry = entries[i];
                    }
                    else if (i % 15 == 14)
                    {
                        yield return null;
                    }
                }
                if (longEntry != null)
                {
                    break;
                }
            }
            var scroll = CompendiumWindow.DetailScroll;
            if (longEntry == null || scroll == null)
            {
                p.Add($"test setup: none of {looked} entries has details longer than the details pane (nothing to scroll)");
                run.Report();
                yield break;
            }
            yield return TestKit.Frames(3);
            var tabBefore = CompendiumWindow.CurrentTab;
            var top = scroll.verticalNormalizedPosition;
            var press = new PadPress();
            yield return Stick(true, StickDown, 0.4f, press);
            var lower = scroll.verticalNormalizedPosition;
            Need(p, lower < top - 0.05f,
                $"right stick held down for 0.4 s on '{longEntry}' (details {DetailsOverflow():F0} units taller than the pane): scroll position {top:F2} -> {lower:F2} "
                + $"(expected lower); {press.Tell}");
            yield return Stick(true, StickUp, 0.25f, press);
            var back = scroll.verticalNormalizedPosition;
            Need(p, back > lower + 0.02f, $"right stick held up for 0.25 s: scroll position {lower:F2} -> {back:F2} (expected higher); {press.Tell}");
            Need(p, CompendiumWindow.IsOpen && ReferenceEquals(CompendiumWindow.SelectedEntry, longEntry) && CompendiumWindow.CurrentTab == tabBefore,
                $"the right stick did more than scroll: Encyclopedia open {CompendiumWindow.IsOpen}, selected '{CompendiumWindow.SelectedEntry}' (was '{longEntry}'), "
                + $"category tab {tabBefore} -> {CompendiumWindow.CurrentTab}");
            run.Note($"entry '{longEntry}' (tab {tabBefore}), scroll position {top:F2} -> {lower:F2} -> {back:F2}; {PadNote()}");
            yield return BackToMouse();
            run.Detail = "the right stick held down scrolls a long details page down, held up scrolls it back; the selected entry and the category "
                         + "tab stay";
        }
        finally
        {
            SelfTest.ReleaseInput();
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.input.close (T15)

    private static IEnumerator RunClose()
    {
        var run = new UiRun();
        yield return run.Begin(CloseName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        try
        {
            run.Force(sideButton: false);
            if (!Driver(p))
            {
                run.Report();
                yield break;
            }
            yield return OpenWindow(run, "open");
            if (!WindowReady)
            {
                run.Report();
                yield break;
            }
            var press = new PadPress();
            yield return Tap(ButtonB, press, mustSee: false);
            var closed = WindowGone;
            var inventory = TestKit.InventoryShown(gui);
            var texts = TopTabs.VanillaShown(gui);
            // The same press must not close the inventory a moment later either.
            yield return new WaitForSecondsRealtime(0.4f);
            Need(p, closed && inventory && !texts && TestKit.InventoryShown(gui) && WindowGone && !TopTabs.VanillaShown(gui),
                $"B with the Encyclopedia open: Encyclopedia closed {closed} (0.4 s later: open {CompendiumWindow.IsOpen}, its window on screen {WindowOnScreen}), "
                + $"inventory still shown {inventory} (0.4 s later {TestKit.InventoryShown(gui)}), Valheim Compendium shown {texts} (0.4 s later "
                + $"{TopTabs.VanillaShown(gui)}) (expected only the Encyclopedia closed); {press.Tell}");
            run.Note(PadNote());
            yield return BackToMouse();
            run.Detail = "a real B press closes the Encyclopedia and nothing else: the inventory stays open, the Valheim Compendium stays closed";
        }
        finally
        {
            SelfTest.ReleaseInput();
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.input.hints (T15)

    private static IEnumerator RunHints()
    {
        var run = new UiRun();
        yield return run.Begin(HintsName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        var hints = KeyHints.instance;
        var hintsOn = hints != null && hints.m_keyHintsEnabled;
        try
        {
            run.Force(sideButton: false);
            if (!Driver(p))
            {
                run.Report();
                yield break;
            }
            var raven = TopTabs.RavenButton(gui);
            if (hints == null || hints.m_inventoryHints == null || raven == null)
            {
                p.Add($"test setup: key hints object {hints != null}, Valheim Compendium button {raven != null}");
                run.Report();
                yield break;
            }
            hints.m_keyHintsEnabled = true; // "key hints on in the game settings", in memory
            yield return TestKit.ShowInventory(gui);
            var press = new PadPress();
            yield return EnsurePad(press);
            if (!press.PadMode)
            {
                p.Add($"test setup: {press.Why}");
                run.Report();
                yield break;
            }
            yield return TestKit.Frames(3);
            // Controls: hints shown with the plain inventory, hidden by the game for its own side dialog.
            var plain = On(hints.m_inventoryHints);
            raven.onClick.Invoke();
            yield return TestKit.Frames(3);
            var withTexts = AnyHints(hints);
            if (!Need(p, plain && !withTexts && TopTabs.VanillaShown(gui) && TopTabs.VanillaEncyclopedia != null,
                    $"control: inventory key hints shown with the plain inventory {plain} (expected true), any key hints shown with the game's Valheim "
                    + $"Compendium open {withTexts} (expected false), dialog shown {TopTabs.VanillaShown(gui)}: the checks below would prove nothing"))
            {
                run.Report();
                yield break;
            }
            TopTabs.VanillaEncyclopedia.onClick.Invoke();
            yield return TestKit.Frames(2);
            yield return TestKit.WaitPrepared();
            // Still on controller input when me look (a real mouse move in between take the game back to the mouse).
            yield return EnsurePad(press);
            yield return TestKit.Frames(3);
            var whileOpen = AnyHints(hints);
            Need(p, WindowReady && ZInput.IsExclusiveGamepadActive() && !whileOpen,
                $"Encyclopedia open on controller input: open {CompendiumWindow.IsOpen} (its window on screen {WindowOnScreen}), game on controller input "
                + $"{ZInput.IsExclusiveGamepadActive()}, key hints shown {whileOpen} (expected open, on controller input, hints hidden as for the game's own "
                + "side dialogs)");

            // B: back at once (looked at five frames after the press at the latest: two held, three after).
            yield return Tap(ButtonB, press, mustSee: false);
            Need(p, WindowGone && TestKit.InventoryShown(gui) && On(hints.m_inventoryHints),
                $"after B: Encyclopedia open {CompendiumWindow.IsOpen} (its window on screen {WindowOnScreen}), inventory shown {TestKit.InventoryShown(gui)}, "
                + $"inventory key hints back {On(hints.m_inventoryHints)} (expected closed, shown, back); {press.Tell}");

            // Mouse: the same after a click on the Close button.
            yield return BackToMouse();
            yield return TestKit.OpenByTab(gui, p, "open again");
            yield return TestKit.Frames(3);
            var close = CompendiumWindow.CloseButton;
            if (Need(p, WindowReady && close != null && !ZInput.IsGamepadActive() && !AnyHints(hints),
                    $"Encyclopedia open again with the mouse: open {CompendiumWindow.IsOpen} (its window on screen {WindowOnScreen}), Close button "
                    + $"{close != null}, game on controller input {ZInput.IsGamepadActive()}, key hints shown {AnyHints(hints)} (expected open, a button, "
                    + "mouse, hidden)"))
            {
                var hit = TestKit.Click(TestKit.Center((RectTransform)close.transform), PointerEventData.InputButton.Left);
                yield return TestKit.Frames(3);
                Need(p, WindowGone && TestKit.InventoryShown(gui) && On(hints.m_inventoryHints),
                    $"after a mouse click on Close (landed on '{hit}'): Encyclopedia open {CompendiumWindow.IsOpen} (its window on screen {WindowOnScreen}), "
                    + $"inventory shown {TestKit.InventoryShown(gui)}, inventory key hints back {On(hints.m_inventoryHints)}");
            }
            run.Detail = "with key hints on: shown with the plain inventory, hidden while the Encyclopedia is open on controller input and with the "
                         + "mouse (as while the game's Valheim Compendium is open), back within five frames of a real B press and within three "
                         + "frames of a pointer click on the Close button";
        }
        finally
        {
            if (hints != null)
            {
                hints.m_keyHintsEnabled = hintsOn;
            }
            SelfTest.ReleaseInput();
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.input.glyphs (T15)

    // Every graphic under root that is switched on and enabled now: instance id -> where and what.
    private static Dictionary<int, string> Drawn(GameObject root)
    {
        var drawn = new Dictionary<int, string>();
        if (root == null)
        {
            return drawn;
        }
        foreach (var g in root.GetComponentsInChildren<Graphic>(false))
        {
            if (g != null && g.enabled && g.gameObject.activeInHierarchy)
            {
                drawn[g.GetInstanceID()] = $"{UiUtil.Path(g.transform)} [{g.GetType().Name}]";
            }
        }
        return drawn;
    }

    // Texts under root that draw a sprite character (how the game write a button glyph into a text).
    private static List<string> SpriteTexts(GameObject root)
    {
        var found = new List<string>();
        if (root == null)
        {
            return found;
        }
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(false))
        {
            if (t == null || !t.enabled)
            {
                continue;
            }
            t.ForceMeshUpdate();
            var sprites = t.textInfo != null ? t.textInfo.spriteCount : 0;
            if (sprites > 0 || (t.text ?? "").IndexOf("<sprite", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                found.Add(UiUtil.Path(t.transform));
            }
        }
        return found;
    }

    // Things the game itself show on controller input only, on the inventory screen and in the key hints: the
    // selected slot of the player grid (InventoryGrid.UpdateGui show it only then, while the grid has the focus), focus
    // frames of the panels, controller hint objects, shortcut hints. Mouse = fewer (the control of the glyph test).
    private static int ControllerOnlyShown(InventoryGui gui)
    {
        var n = 0;
        if (gui.m_playerGrid != null && gui.m_playerGrid.m_elements != null)
        {
            foreach (var e in gui.m_playerGrid.m_elements)
            {
                if (e != null && e.m_selected != null && e.m_selected.activeInHierarchy)
                {
                    n++;
                }
            }
        }
        foreach (var g in gui.GetComponentsInChildren<UIGroupHandler>(true))
        {
            if (g != null && g.m_enableWhenActiveAndGamepad != null && g.m_enableWhenActiveAndGamepad.activeInHierarchy)
            {
                n++;
            }
        }
        foreach (var h in Object.FindObjectsByType<UIInputHint>(FindObjectsSortMode.None))
        {
            if (h != null && h.m_gamepadHint != null && h.m_gamepadHint.activeInHierarchy)
            {
                n++;
            }
        }
        foreach (var pad in Object.FindObjectsByType<UIGamePad>(FindObjectsSortMode.None))
        {
            if (pad != null && pad.m_hint != null && pad.m_hint.activeInHierarchy)
            {
                n++;
            }
        }
        return n;
    }

    private static IEnumerator RunGlyphs()
    {
        var run = new UiRun();
        yield return run.Begin(GlyphsName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        var hints = KeyHints.instance;
        var hintsOn = hints != null && hints.m_keyHintsEnabled;
        try
        {
            run.Force(sideButton: false);
            if (!Driver(p))
            {
                run.Report();
                yield break;
            }
            if (hints != null)
            {
                hints.m_keyHintsEnabled = true; // the game's own controller hints count in the control below
            }
            yield return TestKit.ShowInventory(gui);
            for (var i = 0; i < 90 && ZInput.IsGamepadActive(); i++)
            {
                yield return null;
            }
            var groups = gui.m_uiGroups;
            if (ZInput.IsGamepadActive() || groups == null || groups.Length <= 3)
            {
                p.Add($"test setup: the game is on controller input before the test gamepad was used {ZInput.IsGamepadActive()} (a controller in use? "
                      + $"no mouse picture to compare with), inventory focus groups {groups != null && groups.Length > 3}");
                run.Report();
                yield break;
            }
            gui.SetActiveGroup(groups[1], playSound: false); // player grid has the focus
            yield return TestKit.Frames(3);
            var mouseOnly = ControllerOnlyShown(gui);

            // The window as drawn with the mouse, then on controller input: the very same graphics.
            yield return TestKit.OpenByTab(gui, p, "open");
            if (!WindowReady)
            {
                run.Report();
                yield break;
            }
            CompendiumWindow.SelectTab((int)CatalogTab.Weapons);
            yield return TestKit.Frames(3);
            var root = CompendiumWindow.Root;
            var withMouse = Drawn(root);
            var press = new PadPress();
            yield return EnsurePad(press);
            if (!press.PadMode)
            {
                p.Add($"test setup: {press.Why}");
                run.Report();
                yield break;
            }
            yield return TestKit.Frames(5);
            var withPad = Drawn(root);
            var extra = withPad.Where(kv => !withMouse.ContainsKey(kv.Key)).Select(kv => kv.Value).ToList();
            var gone = withMouse.Count(kv => !withPad.ContainsKey(kv.Key));
            Need(p, ZInput.IsExclusiveGamepadActive() && extra.Count == 0,
                $"on controller input (still on it: {ZInput.IsExclusiveGamepadActive()}) the Encyclopedia draws {extra.Count} thing(s) it does not draw "
                + $"with the mouse: {string.Join(", ", extra.Take(4))}");
            var sprites = SpriteTexts(root);
            Need(p, sprites.Count == 0, $"{sprites.Count} text(s) of the Encyclopedia draw a glyph character: {string.Join(", ", sprites.Take(4))}");
            var parts = root.GetComponentsInChildren<UIGamePad>(true).Length + root.GetComponentsInChildren<UIInputHint>(true).Length;
            Need(p, parts == 0, $"{parts} controller shortcut or hint component(s) inside the Encyclopedia window");
            var tabs = CompendiumWindow.TabButtonList.Count(b => b != null && b.gameObject.activeInHierarchy);
            var listRows = CompendiumWindow.ListPool.Count(r => r.Go != null && r.Go.activeInHierarchy);
            Need(p, tabs == Tabs.Count && listRows >= 5 && CompendiumWindow.TopTextsButton != null && CompendiumWindow.TopEncyclopediaButton != null
                    && withPad.Count > 20,
                $"test setup: {tabs} category tabs shown (expected {Tabs.Count}), {listRows} list rows shown, {withPad.Count} graphics drawn: too little was looked at");

            // The two tabs on the game's Valheim Compendium, on controller input.
            CompendiumWindow.TopTextsButton.onClick.Invoke();
            yield return TestKit.Frames(3);
            yield return EnsurePad(press);
            yield return TestKit.Frames(5);
            var strip = TopTabs.VanillaStrip;
            var stripPad = Drawn(strip).Values.OrderBy(x => x, StringComparer.Ordinal).ToList();
            var stripSprites = SpriteTexts(strip);
            var stripParts = strip != null ? strip.GetComponentsInChildren<UIGamePad>(true).Length + strip.GetComponentsInChildren<UIInputHint>(true).Length : 0;
            var stripOnPad = ZInput.IsExclusiveGamepadActive();

            // Control: with the plain inventory (player grid focused, as for the mouse count) the game show
            // controller-only things in this mode.
            if (TopTabs.VanillaShown(gui))
            {
                gui.m_textsDialog.OnClose();
            }
            gui.SetActiveGroup(groups[1], playSound: false);
            yield return TestKit.Frames(5);
            yield return EnsurePad(press);
            yield return TestKit.Frames(3);
            var padOnly = ControllerOnlyShown(gui);
            Need(p, ZInput.IsExclusiveGamepadActive() && padOnly > mouseOnly,
                $"control: the game shows {padOnly} controller-only thing(s) with the plain inventory on controller input and {mouseOnly} with the mouse "
                + "(expected more on controller input): the comparison above would prove nothing");
            // Can the glyph text check see a glyph at all? The game's own key hints write some on controller input.
            // Noted only (first game run show the number): the checks above do not rest on it alone.
            var hintGlyphs = hints != null && hints.m_inventoryHints != null ? SpriteTexts(hints.m_inventoryHints).Count : -1;

            // The same two tabs with the mouse.
            yield return BackToMouse();
            var raven = TopTabs.RavenButton(gui);
            if (raven != null)
            {
                raven.onClick.Invoke();
            }
            yield return TestKit.Frames(5);
            var stripMouse = Drawn(TopTabs.VanillaStrip).Values.OrderBy(x => x, StringComparer.Ordinal).ToList();
            Need(p, strip != null && stripOnPad && stripPad.Count > 0 && stripPad.SequenceEqual(stripMouse) && stripSprites.Count == 0 && stripParts == 0
                    && !ZInput.IsGamepadActive(),
                $"the two tabs on the Valheim Compendium: found {strip != null}, on controller input when looked at {stripOnPad}, {stripPad.Count} "
                + $"graphic(s) drawn on controller input and {stripMouse.Count} with the mouse (expected the same ones), {stripSprites.Count} glyph "
                + $"text(s), {stripParts} shortcut or hint component(s), back on the mouse {!ZInput.IsGamepadActive()}");
            run.Note($"window: {withPad.Count} graphics on controller input, {withMouse.Count} with the mouse ({gone} only with the mouse); tabs on the "
                     + $"Valheim Compendium: {stripPad.Count}; the game's controller-only things on the inventory screen: {padOnly} (mouse {mouseOnly}); "
                     + $"glyph texts the same text check finds in the game's own inventory key hints on controller input: {hintGlyphs} (0 = that check "
                     + "sees no glyph of this game: then only the graphics comparison and the component count speak)");
            run.Detail = "on controller input the Encyclopedia (category tabs, top tabs, list and details rows) draws no graphic it does not draw "
                         + "with the mouse, the two tabs on the Valheim Compendium draw exactly the graphics they draw with the mouse, no text of "
                         + "either draws a glyph character and no shortcut or hint part exists in them, while the game itself shows "
                         + "controller-only things on the inventory screen";
        }
        finally
        {
            if (hints != null)
            {
                hints.m_keyHintsEnabled = hintsOn;
            }
            SelfTest.ReleaseInput();
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.input.select (T15)

    private static IEnumerator RunSelect()
    {
        var run = new UiRun();
        yield return run.Begin(SelectName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        try
        {
            run.Force(sideButton: true);
            if (!Driver(p))
            {
                run.Report();
                yield break;
            }
            yield return TestKit.ShowInventory(gui);
            var groups = gui.m_uiGroups;
            if (groups == null || groups.Length <= 3 || !SideButton.Exists || SideButton.Pad == null || SideButton.Pad.m_zinputKey != SideButton.PadKey)
            {
                p.Add($"test setup: inventory focus groups {groups != null && groups.Length > 3}, Encyclopedia side button {SideButton.Exists}, its "
                      + $"View/Select shortcut {SideButton.Pad != null}");
                run.Report();
                yield break;
            }
            var press = new PadPress();
            var mapOpened = false;

            // Side panel selected: View/Select open the Encyclopedia.
            gui.SetActiveGroup(groups[SideButton.SidePanelGroup], playSound: false);
            yield return TestKit.Frames(4);
            // Closed before the press: only the press can have opened it.
            var closedBefore = WindowGone && !TopTabs.VanillaShown(gui);
            yield return Tap(SideButton.PadKey, press);
            yield return TestKit.WaitPrepared();
            Need(p, closedBefore && CompendiumWindow.IsOpen && WindowOnScreen,
                $"View/Select with the side panel selected: Encyclopedia closed before the press {closedBefore}, open after it {CompendiumWindow.IsOpen} "
                + $"(its window on screen {WindowOnScreen}) (expected closed, then open); {press.Tell}");
            mapOpened |= Minimap.instance != null && Minimap.instance.m_mode == Minimap.MapMode.Large;
            CompendiumWindow.Close(selectButton: false);
            yield return TestKit.Frames(3);
            run.Note(PadNote());

            // Player grid, then crafting panel selected: nothing of ours.
            foreach (var other in new[] { 1, 3 })
            {
                var what = other == 1 ? "the player grid" : "the crafting panel";
                gui.SetActiveGroup(groups[other], playSound: false);
                yield return TestKit.Frames(4);
                yield return Tap(SideButton.PadKey, press);
                yield return TestKit.Frames(3);
                if (Arrived(p, press, $"View/Select with {what} selected"))
                {
                    Need(p, gui.ActiveGroup == other && WindowGone && !TopTabs.VanillaShown(gui) && TestKit.InventoryShown(gui),
                        $"View/Select with {what} selected (focus group {gui.ActiveGroup}): Encyclopedia open {CompendiumWindow.IsOpen} (its window on "
                        + $"screen {WindowOnScreen}), Valheim Compendium shown {TopTabs.VanillaShown(gui)}, inventory shown {TestKit.InventoryShown(gui)} "
                        + "(expected nothing of ours)");
                }
                mapOpened |= Minimap.instance != null && Minimap.instance.m_mode == Minimap.MapMode.Large;
                CompendiumWindow.Close(selectButton: false);
            }

            // Side panel again: still open (the refused presses locked nothing).
            gui.SetActiveGroup(groups[SideButton.SidePanelGroup], playSound: false);
            yield return TestKit.Frames(4);
            closedBefore = WindowGone && !TopTabs.VanillaShown(gui);
            yield return Tap(SideButton.PadKey, press);
            yield return TestKit.WaitPrepared();
            Need(p, closedBefore && CompendiumWindow.IsOpen && WindowOnScreen,
                $"View/Select with the side panel selected again: Encyclopedia closed before the press {closedBefore}, open after it "
                + $"{CompendiumWindow.IsOpen} (its window on screen {WindowOnScreen}) (expected closed, then open); {press.Tell}");
            Need(p, !mapOpened, "the map opened on a View/Select press with the inventory shown");
            yield return BackToMouse();
            run.Detail = "with the optional button on, a real View/Select press opens the Encyclopedia while the side panel is selected (twice), "
                         + "and does nothing of ours while the player grid or the crafting panel is selected";
        }
        finally
        {
            SelfTest.ReleaseInput();
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.input.triggers (T37)

    // Valheim Compendium shown with our two tabs on it, Texts current.
    private static bool TextsShown(InventoryGui gui) =>
        TopTabs.VanillaShown(gui) && WindowGone && TopTabs.VanillaStrip != null && TopTabs.VanillaTexts != null
        && !TopTabs.VanillaTexts.interactable && TopTabs.VanillaEncyclopedia != null && TopTabs.VanillaEncyclopedia.interactable;

    // Our window shown in its place (its object on screen, not only the open flag), Encyclopedia current.
    private static bool EncyclopediaShown(InventoryGui gui) =>
        CompendiumWindow.IsOpen && WindowOnScreen && !TopTabs.VanillaShown(gui) && CompendiumWindow.TopEncyclopediaButton != null
        && !CompendiumWindow.TopEncyclopediaButton.interactable && CompendiumWindow.TopTextsButton != null && CompendiumWindow.TopTextsButton.interactable;

    private static string Shown(InventoryGui gui) =>
        $"Valheim Compendium shown {TopTabs.VanillaShown(gui)}, Encyclopedia open {CompendiumWindow.IsOpen} (its window on screen {WindowOnScreen}), "
        + $"inventory shown {TestKit.InventoryShown(gui)}";

    private static IEnumerator RunTriggers()
    {
        var run = new UiRun();
        yield return run.Begin(TriggersName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        try
        {
            run.Force(sideButton: false);
            if (!Driver(p))
            {
                run.Report();
                yield break;
            }
            yield return TestKit.ShowInventory(gui);
            var raven = TopTabs.RavenButton(gui);
            var dialog = gui.m_textsDialog;
            if (raven == null || dialog == null)
            {
                p.Add("test setup: no Valheim Compendium button or dialog");
                run.Report();
                yield break;
            }
            raven.onClick.Invoke();
            yield return TestKit.Frames(3);
            if (!Need(p, TextsShown(gui), $"test setup: the Valheim Compendium with its two tabs is not shown after a click on its button ({Shown(gui)})"))
            {
                run.Report();
                yield break;
            }
            var press = new PadPress();
            var lt = TopTabs.LeftKey;
            var rt = TopTabs.RightKey;

            // LT on Texts: nothing.
            var index = dialog.m_selectionIndex;
            var topic = dialog.m_textAreaTopic != null ? dialog.m_textAreaTopic.text : "";
            yield return Tap(lt, press);
            if (Arrived(p, press, "LT on Texts"))
            {
                Need(p, TextsShown(gui) && dialog.m_selectionIndex == index && (dialog.m_textAreaTopic == null || dialog.m_textAreaTopic.text == topic),
                    $"LT on Texts changed something: {Shown(gui)}, selected text {index} -> {dialog.m_selectionIndex}");
            }
            run.Note($"LT = {press.Control}; {PadNote()}");

            // RT: the Encyclopedia.
            yield return Pause();
            yield return Tap(rt, press);
            yield return TestKit.WaitPrepared();
            if (!Need(p, EncyclopediaShown(gui), $"RT on Texts: {Shown(gui)} (expected the Encyclopedia in its place, Encyclopedia tab current); {press.Tell}"))
            {
                run.Report();
                yield break;
            }

            // RT on the Encyclopedia: nothing.
            var tab = CompendiumWindow.CurrentTab;
            var entry = CompendiumWindow.SelectedEntry;
            yield return Pause();
            yield return Tap(rt, press);
            if (Arrived(p, press, "RT on the Encyclopedia"))
            {
                Need(p, EncyclopediaShown(gui) && CompendiumWindow.CurrentTab == tab && ReferenceEquals(CompendiumWindow.SelectedEntry, entry),
                    $"RT on the Encyclopedia changed something: {Shown(gui)}, category tab {tab} -> {CompendiumWindow.CurrentTab}, selected entry '{entry}' -> "
                    + $"'{CompendiumWindow.SelectedEntry}'");
            }

            // LT: back to Texts, list filled.
            yield return Pause();
            yield return Tap(lt, press);
            Need(p, TextsShown(gui) && dialog.m_texts != null && dialog.m_texts.Count > 0,
                $"LT on the Encyclopedia: {Shown(gui)}, texts listed {(dialog.m_texts != null ? dialog.m_texts.Count : 0)} (expected the Valheim Compendium "
                + $"back, Texts tab current, its list filled); {press.Tell}");

            // Once more both ways.
            yield return Pause();
            yield return Tap(rt, press);
            yield return TestKit.WaitPrepared();
            Need(p, EncyclopediaShown(gui), $"RT on Texts, second time: {Shown(gui)} (expected the Encyclopedia); {press.Tell}");
            yield return Pause();
            yield return Tap(lt, press);
            Need(p, TextsShown(gui), $"LT on the Encyclopedia, second time: {Shown(gui)} (expected the Valheim Compendium); {press.Tell}");
            Need(p, TestKit.InventoryShown(gui), "the inventory closed during the trigger presses");
            yield return BackToMouse();
            run.Detail = "real trigger presses: on the Valheim Compendium RT shows the Encyclopedia and LT changes nothing; on the Encyclopedia LT "
                         + "brings the Valheim Compendium back (list filled) and RT changes nothing (same category tab and entry); both ways twice";
        }
        finally
        {
            SelfTest.ReleaseInput();
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.input.nav (T37)

    private const string LongTopic = "MC Encyclopedia test: a long text";

    private static string LongText()
    {
        var lines = new List<string>();
        for (var i = 1; i <= 90; i++)
        {
            lines.Add($"Line {i} of a text that is long enough to scroll.");
        }
        return string.Join("\n", lines);
    }

    private static IEnumerator RunNav()
    {
        var run = new UiRun();
        yield return run.Begin(NavName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        try
        {
            run.Force(sideButton: false);
            if (!Driver(p))
            {
                run.Report();
                yield break;
            }
            // A text long enough to scroll, learned like a rune stone text (the run's character copy take it back).
            run.Player.AddKnownText(LongTopic, LongText());
            yield return TestKit.ShowInventory(gui);
            var raven = TopTabs.RavenButton(gui);
            var dialog = gui.m_textsDialog;
            if (raven == null || dialog == null)
            {
                p.Add("test setup: no Valheim Compendium button or dialog");
                run.Report();
                yield break;
            }
            raven.onClick.Invoke();
            yield return TestKit.Frames(3);
            var press = new PadPress();

            // Trigger switch to the Encyclopedia: LB / RB still change its category tab.
            yield return Tap(TopTabs.RightKey, press);
            yield return TestKit.WaitPrepared();
            if (!Need(p, EncyclopediaShown(gui) && !CompendiumWindow.Preparing, $"test setup: RT did not show the Encyclopedia ({Shown(gui)}); {press.Tell}"))
            {
                run.Report();
                yield break;
            }
            var tab = CompendiumWindow.CurrentTab;
            yield return Pause();
            yield return Tap(TabRight, press);
            var afterRb = CompendiumWindow.CurrentTab;
            yield return Pause();
            yield return Tap(TabLeft, press);
            var afterLb = CompendiumWindow.CurrentTab;
            Need(p, afterRb == (tab + 1) % Tabs.Count && afterLb == tab && EncyclopediaShown(gui),
                $"after RT, in the Encyclopedia: RB then LB showed category tabs {tab} -> {afterRb} -> {afterLb} (expected {tab} -> {(tab + 1) % Tabs.Count} -> {tab}); {press.Tell}");

            // Trigger switch back to Texts: the game's own list selection and text scrolling.
            yield return Pause();
            yield return Tap(TopTabs.LeftKey, press);
            if (!Need(p, TextsShown(gui), $"test setup: LT did not bring the Valheim Compendium back ({Shown(gui)}); {press.Tell}"))
            {
                run.Report();
                yield break;
            }
            var texts = dialog.m_texts;
            var target = texts != null ? texts.FindIndex(t => t != null && t.m_topic == LongTopic) : -1;
            if (texts == null || texts.Count < 3 || target < 0 || dialog.m_rightScrollbar == null)
            {
                p.Add($"test setup: the Valheim Compendium lists {(texts != null ? texts.Count : 0)} texts, the long test text among them: {target >= 0}");
                run.Report();
                yield break;
            }
            var first = dialog.m_selectionIndex;
            yield return Pause();
            yield return Tap(DpadDown, press);
            var afterDown = dialog.m_selectionIndex;
            var shownTopic = dialog.m_textAreaTopic != null ? dialog.m_textAreaTopic.text : "";
            Need(p, first == 0 && afterDown == 1 && texts[1].m_selected != null && texts[1].m_selected.activeSelf && shownTopic == texts[1].m_topic,
                $"Texts after the trigger switches, D-pad down: selected text {first} -> {afterDown} (expected 0 -> 1), text shown '{shownTopic}' "
                + $"(expected '{texts[1].m_topic}'); {press.Tell}");
            yield return Pause();
            yield return Tap(DpadUp, press);
            Need(p, dialog.m_selectionIndex == 0, $"Texts, D-pad up: selected text {dialog.m_selectionIndex} (expected 0); {press.Tell}");
            yield return Pause();
            yield return Stick(false, StickDown, 0.25f, press);
            var afterStick = dialog.m_selectionIndex;
            Need(p, afterStick >= 1, $"Texts, left stick held down for 0.25 s: selected text 0 -> {afterStick} (expected 1 or more); {press.Tell}");

            // To the long text with D-pad presses, then the right stick scroll it.
            for (var i = 0; i < texts.Count + 2 && dialog.m_selectionIndex != target; i++)
            {
                yield return Pause();
                yield return Tap(dialog.m_selectionIndex < target ? DpadDown : DpadUp, press);
            }
            yield return TestKit.Frames(6);
            var bar = dialog.m_rightScrollbar;
            if (Need(p, dialog.m_selectionIndex == target && bar.gameObject.activeSelf,
                    $"test setup: D-pad presses reached text {dialog.m_selectionIndex} (the long test text is number {target}), its scrollbar shown "
                    + $"{bar.gameObject.activeSelf}; {press.Tell}"))
            {
                // Down, up, down again: wherever the text stood, the last two holds must move it, opposite ways.
                var v0 = bar.value;
                yield return Stick(true, StickDown, 0.6f, press);
                var v1 = bar.value;
                yield return Pause();
                yield return Stick(true, StickUp, 0.4f, press);
                var v2 = bar.value;
                yield return Pause();
                yield return Stick(true, StickDown, 0.4f, press);
                var v3 = bar.value;
                Need(p, Mathf.Abs(v2 - v1) > 0.01f && Mathf.Abs(v3 - v2) > 0.01f && (v2 - v1) * (v3 - v2) < 0f,
                    $"Texts, right stick held down, up, down on the long text: scrollbar {v0:F3} -> {v1:F3} -> {v2:F3} -> {v3:F3} (expected the text to "
                    + $"scroll one way, then back the other way); {press.Tell}");

                // LT here still do nothing else: same text, same scroll place.
                yield return Pause();
                var before = bar.value;
                yield return Tap(TopTabs.LeftKey, press);
                if (Arrived(p, press, "LT on the long text"))
                {
                    Need(p, TextsShown(gui) && dialog.m_selectionIndex == target && Mathf.Abs(bar.value - before) < 0.005f,
                        $"LT on Texts did something: {Shown(gui)}, selected text {target} -> {dialog.m_selectionIndex}, scrollbar {before:F3} -> {bar.value:F3}");
                }
            }
            run.Note($"{texts.Count} texts listed, the long test text is number {target}; left stick moved the selection to {afterStick}; {PadNote()}");
            yield return BackToMouse();
            run.Detail = "after real trigger switches: RB / LB still change the Encyclopedia's category tab; in the Valheim Compendium the D-pad and "
                         + "the left stick still move the list selection, the right stick still scrolls a long text, and LT moves neither";
        }
        finally
        {
            SelfTest.ReleaseInput();
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.input.crafttabs (T37)

    private static string CraftTab(InventoryGui gui) => gui.InCraftTab() ? "Craft" : gui.InUpradeTab() ? "Upgrade" : "none";

    // Piece that is a crafting station showing both the Craft and the Upgrade tab.
    private static bool HasCraftTab(GameObject piece)
    {
        var station = piece != null ? piece.GetComponentInChildren<CraftingStation>(true) : null;
        return station != null && station.m_hasCraftTab;
    }

    private static IEnumerator RunCraftTabs()
    {
        var run = new UiRun();
        yield return run.Begin(CraftTabsName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        var player = run.Player;
        GameObject bench = null;
        // Using a station put away what the player holds: me take it out again after.
        var inHands = player != null && (player.m_leftItem != null || player.m_rightItem != null);
        try
        {
            run.Force(sideButton: false);
            if (!Driver(p))
            {
                run.Report();
                yield break;
            }
            // A crafting station with both tabs, usable anywhere: the Workbench, else the first such station of the
            // Hammer's build list.
            var benchPrefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("piece_workbench") : null;
            if (!HasCraftTab(benchPrefab))
            {
                var hammer = ObjectDB.instance.GetItemPrefab("Hammer");
                var table = hammer != null ? hammer.GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces : null;
                benchPrefab = table != null ? table.m_pieces.FirstOrDefault(HasCraftTab) : null;
            }
            var raven = TopTabs.RavenButton(gui);
            if (benchPrefab == null || raven == null || gui.m_tabCraft == null || gui.m_tabUpgrade == null)
            {
                p.Add($"test setup: crafting station piece in the Hammer's build list {benchPrefab != null}, Valheim Compendium button {raven != null}, "
                      + $"crafting tabs {gui.m_tabCraft != null && gui.m_tabUpgrade != null}");
                run.Report();
                yield break;
            }
            bench = Object.Instantiate(benchPrefab, TestKit.PointNear(player, 3f, 0f), Quaternion.identity);
            var station = bench.GetComponentInChildren<CraftingStation>(true);
            station.m_useDistance = 50f;
            station.m_craftRequireRoof = false;
            station.m_craftRequireFire = false;
            var wear = bench.GetComponent<WearNTear>();
            if (wear != null)
            {
                wear.enabled = false; // piece stand on bare ground: no support check
            }
            yield return new WaitForSeconds(0.3f);
            player.SetCraftingStation(station);
            gui.Show(null, 3);
            yield return new WaitForSecondsRealtime(1.2f);
            gui.OnTabCraftPressed();
            yield return TestKit.Frames(3);
            if (!Need(p, gui.m_tabCraft.gameObject.activeSelf && gui.m_tabUpgrade.gameObject.activeSelf && gui.InCraftTab() && gui.ActiveGroup == 3
                         && player.GetCurrentCraftingStation() == station,
                    $"test setup at '{benchPrefab.name}': Craft tab shown {gui.m_tabCraft.gameObject.activeSelf}, Upgrade tab shown "
                    + $"{gui.m_tabUpgrade.gameObject.activeSelf}, current tab {CraftTab(gui)}, focus group {gui.ActiveGroup}, station in use "
                    + $"{player.GetCurrentCraftingStation() == station}"))
            {
                run.Report();
                yield break;
            }
            var press = new PadPress();
            var lt = TopTabs.LeftKey;
            var rt = TopTabs.RightKey;

            // Control: crafting panel focused, no dialog: the same triggers switch Craft / Upgrade.
            yield return Tap(rt, press);
            var controlRt = CraftTab(gui);
            yield return Pause();
            yield return Tap(lt, press);
            var controlLt = CraftTab(gui);
            if (!Need(p, controlRt == "Upgrade" && controlLt == "Craft",
                    $"control: with the crafting panel focused, RT then LT left the crafting panel on '{controlRt}' then '{controlLt}' (expected Upgrade "
                    + $"then Craft): the checks below would prove nothing; {press.Tell}"))
            {
                run.Report();
                yield break;
            }
            run.Note($"station '{benchPrefab.name}'; {PadNote()}");

            foreach (var current in new[] { "Craft", "Upgrade" })
            {
                if (TopTabs.VanillaShown(gui))
                {
                    gui.m_textsDialog.OnClose();
                }
                CompendiumWindow.Close(selectButton: false);
                yield return TestKit.Frames(2);
                if (current == "Upgrade")
                {
                    gui.OnTabUpgradePressed();
                    yield return TestKit.Frames(3);
                }
                raven.onClick.Invoke();
                yield return TestKit.Frames(3);
                if (!Need(p, CraftTab(gui) == current && TextsShown(gui), $"test setup: crafting tab '{CraftTab(gui)}' (expected {current}), {Shown(gui)}"))
                {
                    break;
                }
                // Every trigger press on both top tabs: LT on Texts, RT, RT on the Encyclopedia, LT, then RT and LT again.
                var steps = new[] { lt, rt, rt, lt, rt, lt };
                var wantEncyclopedia = new[] { false, true, true, false, true, false };
                for (var i = 0; i < steps.Length; i++)
                {
                    yield return Pause();
                    yield return Tap(steps[i], press);
                    yield return TestKit.WaitPrepared();
                    var what = $"{current} tab current, press {i + 1} ({(steps[i] == lt ? "LT" : "RT")})";
                    if (Arrived(p, press, what))
                    {
                        Need(p, (wantEncyclopedia[i] ? EncyclopediaShown(gui) : TextsShown(gui)) && CraftTab(gui) == current,
                            $"{what}: {Shown(gui)} (expected {(wantEncyclopedia[i] ? "the Encyclopedia" : "the Valheim Compendium")}), crafting panel on "
                            + $"'{CraftTab(gui)}' (expected {current})");
                    }
                }
            }
            yield return BackToMouse();
            run.Detail = "at a crafting station RT / LT switch the crafting panel between Craft and Upgrade while it has the focus (control); with "
                         + "the Valheim Compendium or the Encyclopedia shown the same presses switch only the two top tabs: the crafting panel stays "
                         + "on Craft, and stays on Upgrade";
        }
        finally
        {
            SelfTest.ReleaseInput();
            try
            {
                // Craft tab current again, as the game start.
                if (gui != null && TestKit.InventoryShown(gui) && gui.InUpradeTab())
                {
                    CompendiumWindow.Close(selectButton: false);
                    gui.OnTabCraftPressed();
                }
            }
            catch (Exception e)
            {
                SelfTest.Note(CraftTabsName, $"could not put the Craft tab back: {e.GetType().Name}");
            }
            run.End();
            if (player != null)
            {
                player.SetCraftingStation(null);
                if (inHands && player.m_leftItem == null && player.m_rightItem == null)
                {
                    player.ShowHandItems();
                }
            }
            TestKit.Despawn(bench);
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.input.close-both (T37)

    private static IEnumerator RunCloseBoth()
    {
        var run = new UiRun();
        yield return run.Begin(CloseBothName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        try
        {
            run.Force(sideButton: false);
            if (!Driver(p))
            {
                run.Report();
                yield break;
            }
            yield return TestKit.ShowInventory(gui);
            var raven = TopTabs.RavenButton(gui);
            if (raven == null)
            {
                p.Add("test setup: no Valheim Compendium button");
                run.Report();
                yield break;
            }
            var press = new PadPress();

            // Texts shown (our tabs on it): B close it, the inventory stay.
            raven.onClick.Invoke();
            yield return TestKit.Frames(3);
            if (!Need(p, TextsShown(gui), $"test setup: the Valheim Compendium with its two tabs is not shown ({Shown(gui)})"))
            {
                run.Report();
                yield break;
            }
            yield return Tap(ButtonB, press, mustSee: false);
            var closedTexts = !TopTabs.VanillaShown(gui) && WindowGone && TestKit.InventoryShown(gui);
            yield return new WaitForSecondsRealtime(0.4f);
            Need(p, closedTexts && TestKit.InventoryShown(gui) && !TopTabs.VanillaShown(gui) && WindowGone && TopTabs.VanillaStrip == null,
                $"B on Texts: {Shown(gui)}, our tabs still there {TopTabs.VanillaStrip != null} (expected only the Valheim Compendium closed); {press.Tell}");

            // Encyclopedia reached with RT: B close it, the inventory stay, Texts do not come back.
            raven.onClick.Invoke();
            yield return TestKit.Frames(3);
            yield return Tap(TopTabs.RightKey, press);
            yield return TestKit.WaitPrepared();
            if (!Need(p, EncyclopediaShown(gui), $"test setup: RT did not show the Encyclopedia ({Shown(gui)}); {press.Tell}"))
            {
                run.Report();
                yield break;
            }
            yield return Pause();
            yield return Tap(ButtonB, press, mustSee: false);
            var closedOurs = !TopTabs.VanillaShown(gui) && WindowGone && TestKit.InventoryShown(gui);
            yield return new WaitForSecondsRealtime(0.4f);
            Need(p, closedOurs && TestKit.InventoryShown(gui) && !TopTabs.VanillaShown(gui) && WindowGone,
                $"B on the Encyclopedia: {Shown(gui)} (expected only the Encyclopedia closed); {press.Tell}");
            run.Note(PadNote());
            yield return BackToMouse();
            run.Detail = "a real B press closes the Valheim Compendium when Texts is shown, and the Encyclopedia when it was reached with RT; each "
                         + "time the inventory stays open and the other one stays closed";
        }
        finally
        {
            SelfTest.ReleaseInput();
            run.End();
        }
        run.Report();
    }

    // ---------------------------------------------------------------- compendium.input.othermods (T37)

    private static bool PutItem(Inventory inventory, string prefabName, int amount, Vector2i slot)
    {
        var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
        var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
        if (drop == null || inventory == null || inventory.GetItemAt(slot.x, slot.y) != null)
        {
            return false;
        }
        var data = drop.m_itemData.Clone();
        data.m_dropPrefab = prefab;
        data.m_stack = Mathf.Min(amount, data.m_shared.m_maxStackSize);
        data.m_worldLevel = (byte)Game.m_worldLevel;
        return inventory.AddItem(data, slot);
    }

    private static string Items(Inventory inventory) => inventory == null
        ? "none"
        : string.Join("; ", inventory.GetAllItems().OrderBy(i => i.m_gridPos.y * 100 + i.m_gridPos.x)
            .Select(i => $"{i.m_shared.m_name} x{i.m_stack} at {i.m_gridPos.x},{i.m_gridPos.y}"));

    private static string LabelOf(InventoryGui gui, string objectName)
    {
        var t = TestKit.FindUnder(gui, objectName);
        var label = t != null ? t.GetComponentInChildren<TMP_Text>(true) : null;
        return t == null ? "(no object)" : label != null ? label.text ?? "" : "(no text)";
    }

    private static bool ShownByName(string objectName)
    {
        var go = GameObject.Find(objectName);
        return go != null && go.activeInHierarchy;
    }

    // What the three mods' controller shortcuts would change, as words: Sort Chest (Sort, and the game's Take all /
    // Place stacks next to it) = where the items lie; Loot Pickup Filter (mark, lists panel) = its button, panel and
    // the lists on the character; Crafting Search and Sort = its field, sort choice, menu and the recipe list.
    private static List<KeyValuePair<string, string>> OtherModsState(InventoryGui gui, Player player, Container chest)
    {
        var state = new List<KeyValuePair<string, string>>();
        void Add(string what, string value) => state.Add(new KeyValuePair<string, string>(what, value));
        Add("items in the chest", Items(chest != null ? chest.GetInventory() : null));
        Add("items on the player", Items(player.GetInventory()));
        Add("chest open", gui.IsContainerOpen().ToString());
        Add("Sort Chest's Sort button", LabelOf(gui, "MC_ContainerSort_Sort"));
        Add("Sort Chest's sort order button", LabelOf(gui, "MC_ContainerSort_Criterion"));
        Add("Loot Pickup Filter's button", LabelOf(gui, "MC_LootFilterButton"));
        Add("Loot Pickup Filter's lists panel shown", ShownByName("MC_LootFilterLists").ToString());
        Add("Loot Pickup Filter's lists on the character", string.Join(" | ", player.m_customData
            .Where(kv => kv.Key.StartsWith(LootFilterGuid, StringComparison.Ordinal))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value)));
        var fieldT = TestKit.FindUnder(gui, "MC_CraftSearchField");
        var field = fieldT != null ? fieldT.GetComponent<TMP_InputField>() : null;
        Add("Crafting Search and Sort's field", field != null ? $"'{field.text}' typing {field.isFocused}" : "(no field)");
        Add("Crafting Search and Sort's sort button", LabelOf(gui, "MC_CraftSortButton"));
        Add("Crafting Search and Sort's menu shown", ShownByName("MC_CraftSortMenu").ToString());
        Add("recipe list", string.Join(",", gui.m_availableRecipes.Select(r => r.Recipe != null ? r.Recipe.name : "?")));
        return state;
    }

    private static IEnumerator RunOtherMods()
    {
        var run = new UiRun();
        yield return run.Begin(OtherModsName);
        if (run.Cat == null)
        {
            yield break;
        }
        var p = run.Problems;
        var gui = run.Gui;
        var player = run.Player;
        GameObject chest = null;
        Container container = null;
        string wood = null;
        try
        {
            run.Force(sideButton: false);
            if (!Driver(p))
            {
                run.Report();
                yield break;
            }
            foreach (var mod in new[]
                     {
                         new KeyValuePair<string, string>(SortChestGuid, "Sort Chest"), new KeyValuePair<string, string>(LootFilterGuid, "Loot Pickup Filter"),
                         new KeyValuePair<string, string>(CraftSearchGuid, "Crafting Search and Sort"),
                     })
            {
                if (!TestKit.ModActive(mod.Key, out var why))
                {
                    p.Add($"{mod.Value} is needed for this check: {why}");
                }
            }
            if (p.Count > 0)
            {
                run.Report();
                yield break;
            }
            // A chest (first container piece of the Hammer's build list) with two stacks lying apart, and wood on the
            // player that the game's Place stacks would move.
            var hammer = ObjectDB.instance.GetItemPrefab("Hammer");
            var table = hammer != null ? hammer.GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces : null;
            var chestPrefab = table != null ? table.m_pieces.FirstOrDefault(g => g != null && g.GetComponent<Container>() != null) : null;
            var raven = TopTabs.RavenButton(gui);
            if (chestPrefab == null || raven == null)
            {
                p.Add($"test setup: chest piece in the Hammer's build list {chestPrefab != null}, Valheim Compendium button {raven != null}");
                run.Report();
                yield break;
            }
            chest = Object.Instantiate(chestPrefab, TestKit.PointNear(player, 2.2f, 0f), Quaternion.identity);
            container = chest.GetComponent<Container>();
            yield return TestKit.Frames(5);
            var inside = container.GetInventory();
            var putWood = inside != null && PutItem(inside, "Wood", 5, new Vector2i(inside.GetWidth() - 1, inside.GetHeight() - 1));
            var putStone = inside != null && PutItem(inside, "Stone", 3, new Vector2i(1, 0));
            wood = TestKit.GiveItem(player, "Wood", 2);
            gui.Show(container);
            yield return new WaitForSecondsRealtime(1.2f);
            var sort = TestKit.FindUnder(gui, "MC_ContainerSort_Sort");
            var loot = TestKit.FindUnder(gui, "MC_LootFilterButton");
            var search = TestKit.FindUnder(gui, "MC_CraftSearchField");
            if (!Need(p, putWood && putStone && wood != null && gui.IsContainerOpen() && sort != null && sort.gameObject.activeInHierarchy && loot != null
                         && loot.gameObject.activeInHierarchy && search != null,
                    $"test setup: chest '{chestPrefab.name}' filled {putWood && putStone}, wood given {wood != null}, chest open {gui.IsContainerOpen()}, Sort "
                    + $"Chest's button {sort != null && sort.gameObject.activeInHierarchy}, Loot Pickup Filter's button "
                    + $"{loot != null && loot.gameObject.activeInHierarchy}, Crafting Search and Sort's field {search != null}"))
            {
                run.Report();
                yield break;
            }
            raven.onClick.Invoke();
            yield return TestKit.Frames(3);
            if (!Need(p, TextsShown(gui), $"test setup: the Valheim Compendium with its two tabs is not shown over the chest ({Shown(gui)})"))
            {
                run.Report();
                yield break;
            }
            var press = new PadPress();
            yield return EnsurePad(press);
            yield return TestKit.Frames(3);
            var before = OtherModsState(gui, player, container);

            // Every trigger press on both top tabs.
            var lt = TopTabs.LeftKey;
            var rt = TopTabs.RightKey;
            var steps = new[] { lt, rt, rt, lt };
            var wantEncyclopedia = new[] { false, true, true, false };
            var arrived = true;
            for (var i = 0; i < steps.Length; i++)
            {
                yield return Pause();
                yield return Tap(steps[i], press);
                yield return TestKit.WaitPrepared();
                var what = $"press {i + 1} ({(steps[i] == lt ? "LT" : "RT")})";
                arrived &= Arrived(p, press, what);
                Need(p, wantEncyclopedia[i] ? EncyclopediaShown(gui) : TextsShown(gui),
                    $"{what} over the chest: {Shown(gui)} (expected {(wantEncyclopedia[i] ? "the Encyclopedia" : "the Valheim Compendium")}); {press.Tell}");
            }
            yield return TestKit.Frames(3);
            var after = OtherModsState(gui, player, container);
            if (arrived)
            {
                for (var i = 0; i < before.Count && i < after.Count; i++)
                {
                    Need(p, before[i].Value == after[i].Value,
                        $"{before[i].Key} changed during the LT / RT presses: '{before[i].Value}' -> '{after[i].Value}'");
                }
            }
            run.Note($"watched: {string.Join("; ", before.Select(kv => kv.Key + " = '" + kv.Value + "'"))}; {PadNote()}");
            yield return BackToMouse();
            run.Detail = "with Sort Chest, Loot Pickup Filter and Crafting Search and Sort active and a chest open, real LT / RT presses on Texts "
                         + "and on the Encyclopedia only switch the two top tabs: the items in the chest and on the player, the chest, the three "
                         + "mods' buttons, panel, lists, search field, sort choice and the recipe list all stay as they were";
        }
        finally
        {
            SelfTest.ReleaseInput();
            try
            {
                if (container != null && container.GetInventory() != null)
                {
                    container.GetInventory().RemoveAll(); // nothing of the chest left in the world
                }
            }
            catch (Exception e)
            {
                SelfTest.Note(OtherModsName, $"could not empty the test chest: {e.GetType().Name}");
            }
            TestKit.TakeItem(player, wood, 2);
            run.End(); // chest closed, the character's knowledge back (the wood was a pickup)
            TestKit.Despawn(chest);
        }
        run.Report();
    }
}
#endif
