#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Exploration.MusicInstrumentsMod;

// Debug build only. Self tests with REAL input (same class as SelfTests.cs): the probe changes the state of the real
// keyboard and mouse and of a test gamepad (SelfTest.Key / Pad / PadStick...), so ZInput, the game and the mod read a
// press like from a person. One small test per thing: real input is new, one test can be fixed or dropped alone.
//   music.input.pad-window    T22  test gamepad in the song window: D-pad chooses, Y toggles Repeat, X says "keyboard",
//                                  A plays, B closes (no menu, no jump: six closes on default layout, where B is game's
//                                  jump button too; me was bug test music.bug.pad-close-jump before 0.4.3)
//   music.input.pad-open      T22  left stick held forward, window opened with the gamepad's attack button: the
//                                  selection does not run up the list; pushed again it moves
//   music.input.w-held        T22  W held, window opened with a real left click: selection and feet stay; real Up,
//                                  Right and Left arrow keys
//   music.input.pad-hud       T23  rhythm game: stop hint names Start once the gamepad is touched; Start stops
//   music.input.pad-freeplay  T36  gamepad touched = in use; first song selected, not Free play; A on Free play says
//                                  "keyboard"; free play started with the mouse, pad touched: hint names Start
//   music.input.hide-hud      T23  real Ctrl+F3 while performing: the game's HUD hides, the rhythm game stays
//   music.chair               T12  a real chair (the game's Use on it) and real mouse clicks: window, stops and
//                                  closes never stand the player up or leave the block up; lyre on the lap on a chair
// The game's PlayerController stays on in these tests: what the same press does in the game is part of the check.
internal static partial class SelfTests
{
    private const string InputPadWindowName = "music.input.pad-window";
    private const string InputPadOpenName = "music.input.pad-open";
    private const string InputKeysHeldName = "music.input.w-held";
    private const string InputPadHudName = "music.input.pad-hud";
    private const string InputPadFreeName = "music.input.pad-freeplay";
    private const string InputHideHudName = "music.input.hide-hud";
    private const string ChairName = "music.chair";
    private const string PadJumpPath = "<Gamepad>/buttonEast"; // B: JoyJump of the default layout

    private const string LeftMousePath = "<Mouse>/leftButton";   // ZInput.MouseButtonToPath(MouseButton.Left)
    private const string RightMousePath = "<Mouse>/rightButton";
    private const string ForwardKeyPath = "<Keyboard>/w";

    // ---------- helpers ----------

    // Probe's input driver there? (Every input call says false when no probe drives the game.)
    private static bool InputReady(Checks c) =>
        c.Check(SelfTest.ReleaseInput() && ZInput.instance != null,
            "the probe's input driver is there (real key and gamepad presses need a run of tools/Test-InWorld.ps1)");

    // What a game button is bound to now ("<Mouse>/leftButton"), null = no such button.
    private static string ButtonPath(string name)
    {
        var def = Btn(name);
        return def != null ? def.GetActionPath() : null;
    }

    // Test gamepad control (SelfTest.Pad name) of a gamepad binding path. Null = one me cannot press.
    private static string PadControlOf(string path)
    {
        switch (path)
        {
            case "<Gamepad>/rightTrigger": return "RightTrigger";
            case "<Gamepad>/leftTrigger": return "LeftTrigger";
            case "<Gamepad>/rightShoulder": return "RightShoulder";
            case "<Gamepad>/leftShoulder": return "LeftShoulder";
            default: return null;
        }
    }

    // Test gamepad made and taken by the game as the device in use: right stick pushed up for a moment (first input
    // of a new pad can land in a frame where the game still looks at the mouse: let go, push again). Nothing in the
    // game hangs on that push but the camera, and the mod's screens hold the camera still.
    private static IEnumerator WakePad()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            SelfTest.PadStick(true, new Vector2(0f, 0.9f));
            yield return WaitReal(() => ZInput.IsGamepadActive() && ZInput.GetButton("JoyRStickUp"), 0.7f);
            var awake = ZInput.IsGamepadActive() && ZInput.GetButton("JoyRStickUp");
            SelfTest.PadStick(true, Vector2.zero);
            yield return Frames(4);
            if (awake)
            {
                yield break;
            }
        }
    }

    private static string PadState() =>
        $"gamepad in use {ZInput.IsGamepadActive()}, gamepad allowed by the game's settings {ZInput.IsGamepadEnabled()}";

    // One tap of a test gamepad control, then a few frames for the window to act.
    private static IEnumerator PadPress(string control)
    {
        yield return SelfTest.PadTap(control);
        yield return Frames(3);
    }

    // True while the player shot upward (a jump) in the next frames.
    private static IEnumerator WatchJump(Player player, int frames, bool[] jumped, bool[] menu)
    {
        for (var i = 0; i < frames; i++)
        {
            jumped[0] |= player.m_body != null && player.m_body.linearVelocity.y > 3f;
            if (menu != null)
            {
                menu[0] |= MenuShown;
            }
            yield return null;
        }
    }

    // ---------- music.input.pad-window (T22: gamepad in the song window) ----------

    private static IEnumerator RunInputPadWindow()
    {
        var c = new Checks(InputPadWindowName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(InputPadWindowName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        var layoutBefore = ZInput.InputLayout;
        var layoutForced = false;
        try
        {
            if (!InputReady(c))
            {
                c.Report();
                yield break;
            }
            if (layoutBefore != InputLayout.Default)
            {
                // Me switch layout in memory only, like Settings > Controls do, but no saved choice.
                ZInput.InputLayout = InputLayout.Default;
                ZInput.instance.UpdateGamepadInputLayout();
                layoutForced = true;
                c.Note($"this player's gamepad layout is {layoutBefore}: the default layout (jump on B) is used for the test, in memory, and put back after");
            }
            rig.TakeControls();
            ServerRules.TestRules = TestRules(60f);
            x.NoShare();
            x.SongsFolder("MC_MusicPadWindowTest");
            // Free play was the last thing played: on a pad the window still opens on the first song.
            Performance.LastSongId = SongLibrary.FreePlayId;
            Performance.Repeat = false;
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            // The game reads keyboard, mouse and gamepad itself from here on.
            x.Controller(true);
            yield return WakePad();
            if (!c.Check(ZInput.IsGamepadActive() && PadInput.Active, "the test gamepad is the device in use once it is touched (" + PadState() + ")"))
            {
                c.Report();
                yield break;
            }
            Performance.TestRequestOpen();
            yield return Frames(3);
            var ids = SongIds();
            if (!c.Check(SongWindow.IsOpen && ids.Count >= 9 && ids[0] == SongLibrary.FreePlayId && SongWindow.SelectedId == ids[1],
                    "window open on the first song, not on Free play: " + SongWindow.SelectedId))
            {
                c.Report();
                yield break;
            }
            c.Check(SongWindow.HelpNavText == "D-pad: choose a song and part    A: play    Y: repeat    B: close",
                "gamepad help line (no rhythm game on it): " + SongWindow.HelpNavText);
            var at = player.transform.position;

            // D-pad down, down, up.
            yield return PadPress("DpadDown");
            c.Check(SongWindow.SelectedId == ids[2], "D-pad down chooses the next song: " + SongWindow.SelectedId + " (" + PadState() + ")");
            yield return PadPress("DpadDown");
            c.Check(SongWindow.SelectedId == ids[3], "D-pad down again: " + SongWindow.SelectedId);
            yield return PadPress("DpadUp");
            c.Check(SongWindow.SelectedId == ids[2], "D-pad up chooses the song before: " + SongWindow.SelectedId);
            // Y: Repeat on and off.
            var repeat = Performance.Repeat;
            yield return PadPress("North");
            c.Check(Performance.Repeat == !repeat && SongWindow.RepeatText == (Performance.Repeat ? "Repeat: On" : "Repeat: Off"),
                "Y toggles Repeat: " + SongWindow.RepeatText);
            c.Check(SongWindow.IsOpen && !InventoryGui.IsVisible(), "Y opens no inventory, the window stays");
            yield return PadPress("North");
            c.Check(Performance.Repeat == repeat, "Y again puts it back");
            // X: the rhythm game is not for the pad.
            yield return PadPress("West");
            c.Check(SongWindow.IsOpen && Performance.Mode == PerformanceMode.None && SongWindow.StatusText != null
                    && SongWindow.StatusText.StartsWith("The rhythm game is played on the keyboard (", StringComparison.Ordinal),
                "X starts nothing and says why: " + SongWindow.StatusText);
            c.Check(player.InEmote() == false && Flat(player.transform.position, at) < 0.05f, "X does not sit the player down or move them");
            // A: plays the selected song; the same press does nothing else in the game.
            var selected = SongWindow.SelectedId;
            var jumped = new bool[1];
            SelfTest.Pad("South", 1f);
            yield return WaitReal(() => Performance.Mode == PerformanceMode.Auto, 1f);
            yield return WatchJump(player, 8, jumped, null);
            SelfTest.Pad("South", 0f);
            yield return Frames(3);
            c.Check(Performance.Mode == PerformanceMode.Auto && Performance.LastSongId == selected && !SongWindow.IsOpen, "A plays the selected song: " + Performance.LastSongId);
            c.Check(!jumped[0] && !InventoryGui.IsVisible() && Flat(player.transform.position, at) < 0.05f, "the same press does nothing else (no jump, no screen)");
            Performance.TestRequestStop();
            yield return Frames(3);
            // B close: no menu, no jump. Default layout: B is game's jump button too (JoyJump), PlayerController read it
            // in physics step. If press reach it hang on how frames and physics steps fall, so me close six times, each
            // at other moment (before 0.4.3: 1 jump in 6 closes).
            var jumpPath = ButtonPath("JoyJump");
            if (!c.Check(jumpPath == PadJumpPath, $"B is the game's jump button on this layout (bound to '{jumpPath}')"))
            {
                c.Report();
                yield break;
            }
            var opened = 0;
            var closes = 0;
            var jumps = 0;
            var menus = 0;
            for (var round = 0; round < 6; round++)
            {
                yield return WaitReal(() => !GameScreens.AnyOpen() && player.IsOnGround(), 3f);
                Performance.TestRequestOpen();
                yield return Frames(3 + round);
                if (!SongWindow.IsOpen)
                {
                    continue;
                }
                opened++;
                var jumpedB = new bool[1];
                var menu = new bool[1];
                SelfTest.Pad("East", 1f);
                yield return WaitReal(() => !SongWindow.IsOpen, 1f);
                yield return WatchJump(player, 20, jumpedB, menu);
                SelfTest.Pad("East", 0f);
                yield return WatchJump(player, 10, jumpedB, menu);
                if (!SongWindow.IsOpen && !Performance.WindowOpen)
                {
                    closes++;
                }
                else
                {
                    Performance.CloseWindow();
                }
                if (jumpedB[0])
                {
                    jumps++;
                }
                if (menu[0] || MenuShown)
                {
                    menus++;
                    if (Menu.instance != null && MenuShown)
                    {
                        Menu.instance.Hide();
                    }
                    Game.Unpause();
                    yield return Real(0.3f);
                }
            }
            c.Check(opened >= 4 && closes == opened, $"B closes the window: {closes} closes in {opened} tries (window opened in {opened} of 6 rounds)");
            c.Check(menus == 0, $"B opens no menu ({menus} time(s))");
            c.Check(jumps == 0, $"the press that closes the window does not make the character jump ({jumps} jump(s) in {opened} tries; B is also the game's jump button on this layout)");
            c.Report();
        }
        finally
        {
            SelfTest.ReleaseInput();
            try
            {
                if (layoutForced && ZInput.instance != null)
                {
                    ZInput.InputLayout = layoutBefore;
                    ZInput.instance.UpdateGamepadInputLayout();
                }
            }
            catch (Exception e)
            {
                Log.Warning($"Self test clean-up (gamepad layout) failed: {e.Message}");
            }
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.input.pad-open (T22: stick held forward while the window opens) ----------

    private static IEnumerator RunInputPadOpen()
    {
        var c = new Checks(InputPadOpenName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(InputPadOpenName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            if (!InputReady(c))
            {
                c.Report();
                yield break;
            }
            rig.TakeControls();
            ServerRules.TestRules = TestRules(60f);
            x.NoShare();
            x.SongsFolder("MC_MusicPadOpenTest");
            Performance.LastSongId = null;
            Performance.Repeat = false;
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            x.Controller(true);
            // Left stick forward and held (retry: first input of a new pad).
            for (var attempt = 0; attempt < 3; attempt++)
            {
                SelfTest.PadStick(false, new Vector2(0f, 1f));
                yield return WaitReal(() => ZInput.IsGamepadActive() && ZInput.GetJoyLeftStickY() < -0.5f, 0.7f);
                if (ZInput.IsGamepadActive() && ZInput.GetJoyLeftStickY() < -0.5f)
                {
                    break;
                }
                SelfTest.PadStick(false, Vector2.zero);
                yield return Frames(4);
            }
            if (!c.Check(ZInput.IsGamepadActive() && ZInput.GetJoyLeftStickY() < -0.5f,
                    $"the game reads the left stick held forward (stick {F(ZInput.GetJoyLeftStickY())}, " + PadState() + ")"))
            {
                c.Report();
                yield break;
            }
            // The gamepad's attack button, as this player's controller layout has it.
            var path = ButtonPath("JoyAttack");
            var attack = PadControlOf(path);
            var opened = false;
            if (c.Check(attack != null, $"the gamepad attack button is one the test can press (bound to '{path}', layout {ZInput.InputLayout})"))
            {
                SelfTest.Pad(attack, 1f);
                yield return WaitReal(() => SongWindow.IsOpen, 1.5f);
                opened = SongWindow.IsOpen;
                SelfTest.Pad(attack, 0f);
                yield return Frames(2);
                c.Check(opened, $"the gamepad's attack button ({attack}) opens the song window while the stick is held forward");
            }
            if (!opened)
            {
                // Still worth knowing what the held stick does: window opened without the button.
                Performance.TestRequestOpen();
                yield return Frames(3);
            }
            var ids = SongIds();
            var first = SongWindow.SelectedId;
            if (!c.Check(SongWindow.IsOpen && ids.Count >= 9 && first == ids[1], "window open on the first song (Free play is the entry above it): " + first))
            {
                c.Report();
                yield break;
            }
            // Feet come to a stop first (the player was walking when the window opened).
            yield return Real(0.4f);
            var at = player.transform.position;
            yield return Real(0.9f);
            c.Check(ZInput.GetJoyLeftStickY() < -0.5f, "the stick is still held forward");
            c.Check(SongWindow.SelectedId == first, "a stick held since before the window does not run the selection up the list: " + SongWindow.SelectedId);
            c.Check(Flat(player.transform.position, at) < 0.05f, $"and does not walk the player while the window is open ({F(Flat(player.transform.position, at))} m)");
            // Let go, push back: one step down. Push forward again: back up.
            SelfTest.PadStick(false, Vector2.zero);
            yield return Frames(4);
            SelfTest.PadStick(false, new Vector2(0f, -1f));
            yield return WaitReal(() => SongWindow.SelectedId != first, 1f);
            SelfTest.PadStick(false, Vector2.zero);
            yield return Frames(4);
            c.Check(SongWindow.SelectedId == ids[2], "stick let go and pulled back: the next song is chosen: " + SongWindow.SelectedId);
            SelfTest.PadStick(false, new Vector2(0f, 1f));
            yield return WaitReal(() => SongWindow.SelectedId == first, 1f);
            SelfTest.PadStick(false, Vector2.zero);
            yield return Frames(4);
            c.Check(SongWindow.SelectedId == first, "pushed forward again: the song before: " + SongWindow.SelectedId);
            c.Report();
        }
        finally
        {
            SelfTest.ReleaseInput();
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.input.w-held (T22: W held while the window opens; real arrow keys) ----------

    private static IEnumerator RunInputKeysHeld()
    {
        var c = new Checks(InputKeysHeldName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(InputKeysHeldName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            if (!InputReady(c))
            {
                c.Report();
                yield break;
            }
            rig.TakeControls();
            ServerRules.TestRules = TestRules(60f);
            x.NoShare();
            PadInput.Test = false;
            var folder = x.SongsFolder("MC_MusicHeldKeysTest");
            File.WriteAllBytes(Path.Combine(folder, "selftest tune.mid"), MidiOf(Preset("greensleeves")));
            const string tuneId = "midi:selftest tune.mid";
            const string start = "preset:ravens-jig";
            Performance.LastSongId = start;
            Performance.Repeat = false;
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            var attackPath = ButtonPath("Attack");
            var forwardPath = ButtonPath("Forward");
            if (!c.Check(attackPath == LeftMousePath, $"the attack button is the left mouse button (bound to '{attackPath}'): the test can only press that one"))
            {
                c.Report();
                yield break;
            }
            x.Controller(true);
            var from = player.transform.position;
            SelfTest.Key(KeyCode.W, true);
            yield return WaitReal(() => ZInput.GetKey(KeyCode.W, false), 1f);
            c.Check(ZInput.GetKey(KeyCode.W, false), "the game reads the real W key as held");
            yield return new WaitForSeconds(0.5f);
            if (forwardPath == ForwardKeyPath)
            {
                c.Check(Flat(player.transform.position, from) > 0.2f, $"W walks the player before the window ({F(Flat(player.transform.position, from))} m)");
            }
            else
            {
                c.Note($"this player's Forward key is '{forwardPath}', not W: W does not walk, the rest is checked all the same");
            }
            // Real left click with W still down.
            SelfTest.Key(KeyCode.Mouse0, true);
            yield return WaitReal(() => SongWindow.IsOpen, 1.5f);
            SelfTest.Key(KeyCode.Mouse0, false);
            yield return Frames(2);
            var ids = SongIds();
            var index = ids.IndexOf(start);
            if (!c.Check(SongWindow.IsOpen && index >= 1 && SongWindow.SelectedId == start, "a real left click opens the window on the last song played: " + SongWindow.SelectedId))
            {
                c.Report();
                yield break;
            }
            // Feet come to a stop first (the player was walking when the window opened).
            yield return Real(0.4f);
            var at = player.transform.position;
            yield return Real(0.9f);
            c.Check(ZInput.GetKey(KeyCode.W, false), "W is still held");
            c.Check(SongWindow.SelectedId == start && SongWindow.IsOpen, "W held since before the window does not run the selection up the list: " + SongWindow.SelectedId);
            c.Check(Flat(player.transform.position, at) < 0.05f, $"and does not walk the player while the window is open ({F(Flat(player.transform.position, at))} m)");
            SelfTest.Key(KeyCode.W, false);
            yield return Frames(3);
            // Real arrow keys: Up one song; Right and Left the part of a MIDI file.
            yield return SelfTest.KeyTap(KeyCode.UpArrow);
            yield return Frames(2);
            c.Check(SongWindow.SelectedId == ids[index - 1], "the real Up arrow key chooses the song before: " + SongWindow.SelectedId);
            c.Check(SongWindow.TestSelect(tuneId), "MIDI file picked");
            yield return Frames(2);
            c.Check(SongWindow.SelectedPart == -1, "part: automatic");
            yield return SelfTest.KeyTap(KeyCode.RightArrow);
            yield return Frames(2);
            c.Check(SongWindow.SelectedPart == 0, "the real Right arrow key picks the first part (one step per press): " + SongWindow.SelectedPart);
            yield return SelfTest.KeyTap(KeyCode.RightArrow);
            yield return Frames(2);
            c.Check(SongWindow.SelectedPart == 1, "Right again: the second part: " + SongWindow.SelectedPart);
            yield return SelfTest.KeyTap(KeyCode.LeftArrow);
            yield return Frames(2);
            c.Check(SongWindow.SelectedPart == 0 && SongWindow.PartText != null && SongWindow.PartText.StartsWith("Part: ", StringComparison.Ordinal),
                "the real Left arrow key goes back to the first part (" + SongWindow.PartText + ")");
            c.Check(Flat(player.transform.position, at) < 0.05f, "the arrow keys move nothing in the world");
            c.Check(SongWindow.TestPress("Close"), "window closed");
            yield return Frames(3);
            c.Report();
        }
        finally
        {
            SelfTest.ReleaseInput();
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.input.pad-hud (T23: stop hint once the gamepad is touched) ----------

    private static IEnumerator RunInputPadHud()
    {
        var c = new Checks(InputPadHudName);
        var player = Player.m_localPlayer;
        if (player == null || Menu.instance == null)
        {
            SelfTest.Fail(InputPadHudName, "no player or menu");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            if (!InputReady(c))
            {
                c.Report();
                yield break;
            }
            rig.TakeControls();
            ServerRules.TestRules = TestRules(60f);
            x.NoShare();
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            yield return WaitReal(() => !GameScreens.AnyOpen() && !ZInput.IsGamepadActive(), 2f);
            x.Controller(true);
            c.Check(Performance.StartMiniGame(Preset("kjerringa-med-staven"), -1, out var error), "performs: " + error);
            yield return Frames(4);
            c.Check(!ZInput.IsGamepadActive() && MiniGameHud.MiniShown && MiniGameHud.HintText == "Right click or Esc: stop",
                "keyboard and mouse in use: stop hint '" + MiniGameHud.HintText + "'");
            yield return WakePad();
            yield return Frames(3);
            c.Check(ZInput.IsGamepadActive(), "the gamepad is the device in use once it is touched (" + PadState() + ")");
            c.Check(Performance.Mode == PerformanceMode.MiniGame, "touching the gamepad does not stop the rhythm game");
            c.Check(MiniGameHud.HintText == "Start or right click: stop", "stop hint once the gamepad is touched: '" + MiniGameHud.HintText + "'");
            // Start: the menu opens, which stops the run (what the hint says).
            if (ZInput.IsGamepadActive())
            {
                SelfTest.Pad("Start", 1f);
                yield return WaitReal(() => Performance.Mode == PerformanceMode.None, 1.5f);
                SelfTest.Pad("Start", 0f);
                yield return Real(0.2f);
                c.Check(Performance.Mode == PerformanceMode.None, "the gamepad's Start button stops the rhythm game");
                c.Check(MenuShown, "(the menu opens with it, as on Esc)");
                if (MenuShown)
                {
                    Menu.instance.Hide();
                }
                Game.Unpause();
                yield return Real(0.4f);
            }
            // Back on keyboard and mouse: the hint names them again.
            SelfTest.ReleaseInput();
            yield return WaitReal(() => !ZInput.IsGamepadActive() && !GameScreens.AnyOpen(), 3f);
            c.Check(!ZInput.IsGamepadActive(), "gamepad gone: keyboard and mouse in use again");
            c.Check(Performance.StartMiniGame(Preset("kjerringa-med-staven"), -1, out error), "performs again: " + error);
            yield return Frames(4);
            c.Check(MiniGameHud.HintText == "Right click or Esc: stop", "stop hint back to '" + MiniGameHud.HintText + "'");
            Performance.TestRequestStop();
            yield return Frames(3);
            c.Report();
        }
        finally
        {
            SelfTest.ReleaseInput();
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.input.pad-freeplay (T36) ----------

    private static IEnumerator RunInputPadFree()
    {
        var c = new Checks(InputPadFreeName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(InputPadFreeName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            if (!InputReady(c))
            {
                c.Report();
                yield break;
            }
            rig.TakeControls();
            ServerRules.TestRules = TestRules(60f);
            x.NoShare();
            x.SongsFolder("MC_MusicPadFreeTest");
            // Free play was the last thing played.
            Performance.LastSongId = SongLibrary.FreePlayId;
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            yield return WaitReal(() => !GameScreens.AnyOpen() && !ZInput.IsGamepadActive(), 2f);
            x.Controller(true);
            c.Check(!ZInput.IsGamepadActive(), "before the gamepad is touched the game is on keyboard and mouse");
            yield return WakePad();
            if (!c.Check(ZInput.IsGamepadActive() && PadInput.Active, "the game reports the gamepad as in use once it is touched (" + PadState() + ")"))
            {
                c.Report();
                yield break;
            }
            Performance.TestRequestOpen();
            yield return Frames(3);
            var ids = SongIds();
            if (!c.Check(SongWindow.IsOpen && ids.Count >= 9 && ids[0] == SongLibrary.FreePlayId && SongWindow.SelectedId == ids[1],
                    "with a gamepad in use the first song is selected, not Free play: " + SongWindow.SelectedId))
            {
                c.Report();
                yield break;
            }
            // Pick Free play with the D-pad (the entry above), press A.
            yield return PadPress("DpadUp");
            c.Check(SongWindow.SelectedId == SongLibrary.FreePlayId, "D-pad up picks Free play: " + SongWindow.SelectedId);
            yield return PadPress("South");
            c.Check(SongWindow.IsOpen && Performance.Mode == PerformanceMode.None && SongWindow.StatusText == "Free play is played on the keyboard.",
                "A on Free play starts nothing and says: '" + SongWindow.StatusText + "'");
            yield return PadPress("East");
            yield return WaitReal(() => !SongWindow.IsOpen, 1f);
            c.Check(!SongWindow.IsOpen, "B closes the window");

            // Back on the mouse: free play started with the window's Play button, then the pad touched.
            SelfTest.ReleaseInput();
            yield return WaitReal(() => !ZInput.IsGamepadActive() && !GameScreens.AnyOpen(), 3f);
            c.Check(!ZInput.IsGamepadActive(), "gamepad gone: keyboard and mouse in use again");
            Performance.TestRequestOpen();
            yield return Frames(3);
            c.Check(SongWindow.IsOpen && SongWindow.TestSelect(SongLibrary.FreePlayId) && SongWindow.TestPress("Play"), "Free play started with the mouse (Play button)");
            yield return Frames(4);
            c.Check(Performance.Mode == PerformanceMode.FreePlay && MiniGameHud.FreeHintText == "Hold Space: octave up    Right click or Esc: stop",
                "keyboard hint under the piano: '" + MiniGameHud.FreeHintText + "'");
            yield return WakePad();
            yield return Frames(3);
            c.Check(ZInput.IsGamepadActive() && Performance.Mode == PerformanceMode.FreePlay, "the gamepad touched: in use, free play goes on (" + PadState() + ")");
            c.Check(MiniGameHud.FreeHintText == "Hold Space: octave up    Start or right click: stop", "hint under the piano: '" + MiniGameHud.FreeHintText + "'");
            Performance.TestRequestStop();
            yield return Frames(3);
            c.Report();
        }
        finally
        {
            SelfTest.ReleaseInput();
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.input.hide-hud (T23: Ctrl+F3) ----------

    private static IEnumerator RunInputHideHud()
    {
        var c = new Checks(InputHideHudName);
        var player = Player.m_localPlayer;
        if (player == null || Hud.instance == null)
        {
            SelfTest.Fail(InputHideHudName, "no player or HUD");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            if (!InputReady(c))
            {
                c.Report();
                yield break;
            }
            rig.TakeControls();
            ServerRules.TestRules = TestRules(60f);
            x.NoShare();
            PadInput.Test = false;
            Hud.instance.m_userHidden = false;
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            x.Controller(true);
            c.Check(Performance.StartMiniGame(Preset("kjerringa-med-staven"), -1, out var error), "performs: " + error);
            yield return Frames(4);
            c.Check(MiniGameHud.MiniShown && MiniGameHud.CanvasOn && !Hud.IsUserHidden(), "rhythm game up, the game's HUD shown");
            // Ctrl held, F3 tapped (Hud.Update reads exactly these two keys).
            SelfTest.Key(KeyCode.LeftControl, true);
            yield return Frames(2);
            yield return SelfTest.KeyTap(KeyCode.F3);
            SelfTest.Key(KeyCode.LeftControl, false);
            yield return Frames(4);
            c.Check(Hud.IsUserHidden(), "real Ctrl+F3 hides the game's HUD while performing (the keys are not held back)");
            c.Check(Performance.Mode == PerformanceMode.MiniGame && MiniGameHud.MiniShown && MiniGameHud.CanvasOn, "the rhythm game stays visible and goes on");
            var box = MiniGameHud.ScreenBox("mini");
            c.Check(box.width > 10f && box.xMin >= 0f && box.xMax <= Screen.width + 1f, "its lanes are still on the screen");
            SelfTest.Key(KeyCode.LeftControl, true);
            yield return Frames(2);
            yield return SelfTest.KeyTap(KeyCode.F3);
            SelfTest.Key(KeyCode.LeftControl, false);
            yield return Frames(4);
            c.Check(!Hud.IsUserHidden() && Performance.Mode == PerformanceMode.MiniGame, "Ctrl+F3 again shows the HUD, still performing");
            Performance.TestRequestStop();
            yield return Frames(3);
            // A song playing by itself: its small line stays too.
            c.Check(Performance.StartAuto(Preset("ravens-jig"), -1, out error), "plays by itself: " + error);
            yield return new WaitForSeconds(0.6f);
            SelfTest.Key(KeyCode.LeftControl, true);
            yield return Frames(2);
            yield return SelfTest.KeyTap(KeyCode.F3);
            SelfTest.Key(KeyCode.LeftControl, false);
            yield return Frames(4);
            c.Check(Hud.IsUserHidden() && MiniGameHud.AutoShown && MiniGameHud.CanvasOn && Performance.Mode == PerformanceMode.Auto,
                "Ctrl+F3 while a song plays by itself: HUD hidden, the status line stays, the song goes on");
            Performance.TestRequestStop();
            yield return Frames(3);
            c.Report();
        }
        finally
        {
            SelfTest.ReleaseInput();
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.chair (T12: a real chair, real clicks) ----------

    // A chair the game can build: the wooden chair by its name, else the first buildable piece with a seat.
    private static GameObject ChairPrefab()
    {
        var scene = ZNetScene.instance;
        var named = scene.GetPrefab("piece_chair");
        if (named != null && named.GetComponentInChildren<Chair>(true) != null)
        {
            return named;
        }
        foreach (var prefab in scene.m_prefabs)
        {
            if (prefab == null || prefab.GetComponent<Piece>() == null || prefab.GetComponent<Ship>() != null || prefab.GetComponent<Vagon>() != null)
            {
                continue;
            }
            var chair = prefab.GetComponentInChildren<Chair>(true);
            if (chair != null && !chair.m_inShip && chair.m_attachPoint != null)
            {
                return prefab;
            }
        }
        return null;
    }

    private static bool OnChair(Player player, Chair chair) =>
        player.IsAttached() && chair != null && player.GetAttachPoint() == chair.m_attachPoint;

    private static IEnumerator RunChair()
    {
        var c = new Checks(ChairName);
        var player = Player.m_localPlayer;
        if (player == null || ZNetScene.instance == null || ZoneSystem.instance == null)
        {
            SelfTest.Fail(ChairName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        var toggleBlock = player.m_toggleBlock;
        try
        {
            if (!InputReady(c))
            {
                c.Report();
                yield break;
            }
            rig.TakeControls();
            ServerRules.TestRules = TestRules();
            x.NoShare();
            PadInput.Test = false;
            var attackPath = ButtonPath("Attack");
            var blockPath = ButtonPath("Block");
            if (!c.Check(attackPath == LeftMousePath && blockPath == RightMousePath,
                    $"attack is the left and block the right mouse button (bound to '{attackPath}' and '{blockPath}'): the test presses those"))
            {
                c.Report();
                yield break;
            }
            var prefab = ChairPrefab();
            if (!c.Check(prefab != null, "the game has a chair that can be built"))
            {
                c.Report();
                yield break;
            }
            var forward = player.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            var spot = player.transform.position + forward * 1.2f;
            spot.y = ZoneSystem.instance.GetGroundHeight(spot);
            var chairObject = Object.Instantiate(prefab, spot, Quaternion.LookRotation(-forward));
            rig.Spawned(chairObject);
            yield return new WaitForSeconds(0.8f);
            var chair = chairObject != null ? chairObject.GetComponentInChildren<Chair>() : null;
            if (!c.Check(chair != null, $"a chair stands in front of the player ({prefab.name})"))
            {
                c.Report();
                yield break;
            }
            c.Note($"chair piece: {prefab.name}");
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            // The Use key on the chair.
            player.Interact(chair.gameObject, false, false);
            yield return WaitFor(() => OnChair(player, chair), 3f);
            if (!c.Check(OnChair(player, chair), "the Use key seats the player on the chair, flute in hand"))
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(0.6f);
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            // The game reads the mouse itself from here on.
            x.Controller(true);
            foreach (var toggle in new[] { false, true })
            {
                // Settings, Accessibility, Toggle block: the game keeps it on the player.
                player.m_toggleBlock = toggle;
                player.m_blocking = false;
                var label = toggle ? "Toggle block on: " : "";
                // Window by a real left click; Play; real left click stops.
                yield return SelfTest.KeyTap(KeyCode.Mouse0);
                yield return WaitReal(() => SongWindow.IsOpen, 1.5f);
                c.Check(SongWindow.IsOpen && OnChair(player, chair), label + "a real left click opens the window, still on the chair");
                c.Check(SongWindow.TestSelect("preset:hearthfire-lullaby") && SongWindow.TestPress("Play"), label + "Play");
                yield return new WaitForSeconds(0.5f);
                c.Check(Performance.Mode == PerformanceMode.Auto && OnChair(player, chair), label + "playing, still on the chair");
                yield return SelfTest.KeyTap(KeyCode.Mouse0);
                yield return WaitReal(() => Performance.Mode == PerformanceMode.None, 1.5f);
                yield return new WaitForSeconds(0.4f);
                c.Check(Performance.Mode == PerformanceMode.None && OnChair(player, chair) && !player.m_blocking && !player.IsBlocking(),
                    label + "a real left click stops the song: still on the chair, not blocking");
                if (SongWindow.IsOpen)
                {
                    c.Note(label + "the click that stopped the song also opened the window again; closed for the next step");
                    SongWindow.TestPress("Close");
                    yield return Frames(3);
                }
                // Real right click stops.
                yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
                c.Check(Performance.StartAuto(Preset("hearthfire-lullaby"), -1, out var error), label + "plays again: " + error);
                yield return new WaitForSeconds(0.4f);
                yield return SelfTest.KeyTap(KeyCode.Mouse1, 0.3f);
                yield return WaitReal(() => Performance.Mode == PerformanceMode.None, 1.5f);
                yield return new WaitForSeconds(0.4f);
                c.Check(Performance.Mode == PerformanceMode.None && OnChair(player, chair) && !player.m_blocking && !player.IsBlocking(),
                    label + "a real right click stops the song: still on the chair, not left in the block stance");
                // Window closed with the right mouse button.
                yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
                yield return SelfTest.KeyTap(KeyCode.Mouse0);
                yield return WaitReal(() => SongWindow.IsOpen, 1.5f);
                c.Check(SongWindow.IsOpen, label + "window open again");
                yield return SelfTest.KeyTap(KeyCode.Mouse1, 0.3f);
                yield return WaitReal(() => !SongWindow.IsOpen, 1.5f);
                yield return new WaitForSeconds(0.4f);
                c.Check(!SongWindow.IsOpen && OnChair(player, chair) && !player.m_blocking && !player.IsBlocking(),
                    label + "the right mouse button closes the window: still on the chair, not blocking");
                // Rhythm game, stopped with the right mouse button.
                yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
                c.Check(Performance.StartMiniGame(Preset("hearthfire-lullaby"), -1, out error), label + "performs: " + error);
                yield return new WaitForSeconds(0.5f);
                c.Check(Performance.Mode == PerformanceMode.MiniGame && OnChair(player, chair), label + "performing on the chair");
                yield return SelfTest.KeyTap(KeyCode.Mouse1, 0.3f);
                yield return WaitReal(() => Performance.Mode == PerformanceMode.None, 1.5f);
                yield return new WaitForSeconds(0.4f);
                c.Check(Performance.Mode == PerformanceMode.None && OnChair(player, chair) && !player.m_blocking && !player.IsBlocking(),
                    label + "a real right click stops the rhythm game: still on the chair, not blocking");
                yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            }
            player.m_toggleBlock = toggleBlock;
            player.m_blocking = false;
            x.Controller(false);
            rig.TakeControls();

            // Lyre: sitting down on a chair mid-song puts it on the lap; walking off stands up and brings it back.
            player.AttachStop();
            yield return new WaitForSeconds(0.8f);
            player.UnequipItem(player.GetRightItem(), false);
            yield return Frames(2);
            rig.Hold(InstrumentKind.Lyre);
            yield return new WaitForSeconds(0.5f);
            c.Check(Performance.StartAuto(Preset("greensleeves"), -1, out var lyreError), "lyre plays, standing: " + lyreError);
            yield return new WaitForSeconds(1.2f);
            c.Check(InstrumentPose.Seat(player) < 0.05f, $"standing: the lyre is against the body (seat {F(InstrumentPose.Seat(player))})");
            player.Interact(chair.gameObject, false, false);
            yield return WaitFor(() => OnChair(player, chair), 3f);
            yield return new WaitForSeconds(1.4f);
            yield return new WaitForEndOfFrame();
            c.Check(OnChair(player, chair) && Performance.Mode == PerformanceMode.Auto, "sat down on the chair mid-song: the song goes on");
            c.Check(InstrumentPose.Seat(player) > 0.95f, $"on the chair the lyre goes onto the lap (seat {F(InstrumentPose.Seat(player))})");
            var gap = InstrumentPose.LeftHandGap(player);
            c.Check(gap >= 0f && gap < 0.08f, $"the left hand is still on it ({F(gap)} m)");
            SelfTest.Screenshot(ChairName, "lyre-on-chair");
            yield return null;
            yield return null;
            var moved = new float[2];
            yield return Push(rig, player, 0.4f, moved);
            yield return new WaitForSeconds(1.2f);
            c.Check(!player.IsAttached() && Performance.Mode == PerformanceMode.Auto, "walking stands up from the chair, the song goes on");
            c.Check(InstrumentPose.Seat(player) < 0.05f, $"the lyre is back against the body (seat {F(InstrumentPose.Seat(player))})");
            c.Report();
        }
        finally
        {
            SelfTest.ReleaseInput();
            player.m_toggleBlock = toggleBlock;
            player.m_blocking = false;
            try
            {
                if (player.IsAttached())
                {
                    player.AttachStop();
                }
            }
            catch (Exception e)
            {
                Log.Warning($"Self test clean-up (chair) failed: {e.Message}");
            }
            x.Restore();
            rig.Restore();
        }
    }
}
#endif
