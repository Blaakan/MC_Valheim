#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MC.Shared;
using UnityEngine;

namespace MC.Farming.CultivatorReplantMod;

// Debug build only. Self tests with REAL input (same class as SelfTests.cs): the probe changes the state of the real
// keyboard and of a test gamepad (SelfTest.Key / Pad...), so ZInput, the game and my UpdatePlacement prefix read a
// press like from a person. One small test per TESTING.md item: real input is new, one test can be fixed or dropped
// alone.
//   replant.input.pad          T22, Default layout: hint = the game's own glyph of the X button + "Replant", drawn as a
//                              sprite; LT held + X and LB dig nothing; X digs and the player does not sit; A opens the
//                              build menu. Player controller script ON (the older replant.pad has it off).
//   replant.input.pad-layouts  T22, Alternative 1 and 2: hint glyph, X digs
//   replant.input.rebind       T23 b: Use bound to another key by the game's own key-binding step (the call the
//                              Settings window makes) from a real key press, with the real pause menu open; menu
//                              closed: hint shows the new key, the real old key digs nothing, the real new key digs
// Gamepad layout only in memory (no saved preference), key binding never saved (ZInput.Save not called) and put back.
// Always SelfTest.ReleaseInput in finally.
internal static partial class SelfTests
{
    private const string InputPadName = "replant.input.pad";
    private const string InputLayoutsName = "replant.input.pad-layouts";
    private const string InputRebindName = "replant.input.rebind";

    private const string PadAltKeysButton = "JoyAltKeys";

    // Probe's input driver there? (Every input call say false when no probe drive the game.)
    private static bool InputReady(Checks c) =>
        c.Check(SelfTest.ReleaseInput() && ZInput.instance != null,
            "an input driver drives this game (run of tools/Test-InWorld.ps1): real key and gamepad presses can be made");

    // Gamepad layout in memory, the way the older replant.pad does it: layout static + button set of that layout, no
    // preference written. Nothing may be held on the pad meanwhile (buttons are made new).
    private static void SetPadLayoutInMemory(InputLayout layout)
    {
        if (ZInput.InputLayout != layout)
        {
            ZInput.InputLayout = layout;
            ZInput.instance.UpdateGamepadInputLayout();
        }
        ReplantHint.Reset();
    }

    // Player controller script on again (PrepareInput turned it off): the game reads sticks, triggers and bumpers of
    // the pad like for a player. Rig puts its old state back.
    private static void ControllerOn(Rig rig)
    {
        var controller = rig.Player.GetComponent<PlayerController>();
        if (controller != null)
        {
            controller.enabled = true;
        }
    }

    // Test gamepad there and in use: right stick pushed up for a moment (view goes up a little; caller aims again).
    private static IEnumerator WakePad(bool[] awake)
    {
        awake[0] = false;
        for (var attempt = 0; attempt < 3 && !awake[0]; attempt++)
        {
            SelfTest.PadStick(true, new Vector2(0f, 0.9f));
            yield return WaitFor(() => ZInput.IsGamepadActive() && ZInput.GetButton("JoyRStickUp"), 0.8f);
            awake[0] = ZInput.IsGamepadActive() && ZInput.GetButton("JoyRStickUp");
            SelfTest.PadStick(true, Vector2.zero);
            yield return Frames(4);
        }
    }

    // Real press of one control of the test gamepad: down until the game has read the named button for two frames (at
    // most 0.5 s), up, three more frames. seen[0] = the game read it as held. A press my prefix takes is reset in that
    // same frame (ZInput.ResetButtonStatus): then seen stays false and the dig tells.
    private static IEnumerator TapPad(string control, string zinputName, bool[] seen)
    {
        seen[0] = false;
        SelfTest.Pad(control, 1f);
        var until = Time.realtimeSinceStartup + 0.5f;
        var held = 0;
        while (Time.realtimeSinceStartup < until && held < 2)
        {
            yield return null;
            if (ZInput.GetButton(zinputName))
            {
                seen[0] = true;
                held++;
            }
            else if (Time.realtimeSinceStartup > until - 0.25f)
            {
                // Never read as held for a quarter of a second: a full press all the same.
                break;
            }
        }
        SelfTest.Pad(control, 0f);
        yield return Frames(3);
    }

    // The game's own text for the physical X button (sprite tag of the glyph set in use), "" when it has none.
    private static string PadGlyphOfX()
    {
        try
        {
            return ZInput.instance.GetBoundKeyString(Replant.PadButtonX, true) ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    // Hint over the bush while the gamepad is in use: plant name, the game's glyph of X (never the keyboard key),
    // "Replant", and the glyph really laid out as a sprite by the hint text.
    private static void CheckPadHint(Checks c, string tag, Aimed bush, PlantKind kind, string keyboardKey)
    {
        var hint = HintText();
        var glyph = PadGlyphOfX();
        c.Note($"{tag}: hint '{OneLine(hint)}'; the game's text for the X button: '{glyph}'");
        c.Check(ZInput.IsGamepadActive(), $"{tag}: the game is on gamepad input while the hint is read");
        c.Check(hint.Length > 0 && !hint.Contains(keyboardKey), $"{tag}: the hint never shows the keyboard key {keyboardKey}");
        var plant = HoverNameOf(bush.Go, kind);
        if (glyph.StartsWith("<sprite=", StringComparison.Ordinal))
        {
            var want = plant + "\n" + glyph + " Replant";
            c.Check(hint == want, $"{tag}: the hint is the plant name, the game's X button glyph and 'Replant' ('{OneLine(hint)}', want '{OneLine(want)}')");
        }
        else
        {
            // The game has no glyph text for X with this glyph set: the layout's button on X, else a plain X.
            c.Check(hint == plant + "\n[<color=yellow><b>X</b></color>] Replant" || (hint.StartsWith(plant + "\n<sprite=", StringComparison.Ordinal) && hint.EndsWith(" Replant", StringComparison.Ordinal)),
                $"{tag}: the game has no glyph text for the X button: the hint shows the glyph of the layout's button on X or a plain X, and 'Replant' ('{OneLine(hint)}')");
        }
        if (hint.Contains("<sprite="))
        {
            var text = Hud.instance.m_hoverName;
            text.ForceMeshUpdate();
            var sprites = text.textInfo != null ? text.textInfo.spriteCount : 0;
            c.Check(sprites == 1 && text.gameObject.activeInHierarchy, $"{tag}: the hint text draws one glyph picture ({sprites} sprite(s) laid out, text shown {text.gameObject.activeInHierarchy})");
        }
        c.Check(CrosshairYellow(), $"{tag}: crosshair yellow");
    }

    // ---------- replant.input.pad (T22, Default layout) ----------

    private static IEnumerator RunInputPad()
    {
        if (!WorldReady(InputPadName))
        {
            yield break;
        }
        var c = new Checks(InputPadName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, InputPadName);
        var taken = new List<Vector3>();
        var layout0 = ZInput.InputLayout;
        try
        {
            if (!InputReady(c))
            {
                c.Report();
                yield break;
            }
            PrepareInput(rig);
            rig.Undo(() => SetPadLayoutInMemory(layout0));
            rig.Undo(() => SelfTest.ReleaseInput());
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var name = rasp.ItemDisplayName;
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 1);
            yield return Hold(rig, tool);
            SetPadLayoutInMemory(InputLayout.Default);
            yield return Frames(2);
            var bush = new Aimed();
            yield return SpawnInReach(rig, taken, "RaspberryBush", 1.2f, bush);
            if (!c.Check(tool != null && bush.Seen && ZInput.InputLayout == InputLayout.Default, "Default layout, a ripe raspberry bush is under the crosshair within reach" + bush.Why))
            {
                c.Report();
                yield break;
            }
            var keyboardKey = "<b>" + Localization.instance.GetBoundKeyString(Replant.KeyboardButton) + "</b>";
            c.Check(HintText() == KeyHint(HoverNameOf(bush.Go, rasp)), $"control, before any gamepad input: keyboard hint ('{OneLine(HintText())}')");

            ControllerOn(rig);
            var awake = new bool[1];
            yield return WakePad(awake);
            if (!c.Check(awake[0], "the game reads the test gamepad and switched to gamepad input"))
            {
                c.Report();
                yield break;
            }
            yield return rig.AimAt(bush.AimAt);
            yield return Frames(4);
            c.Check(Replant.Target.IsFresh && ReferenceEquals(Replant.Target.View, bush.View), "the bush is still under the crosshair after the stick push");
            CheckPadHint(c, "Default", bush, rasp, keyboardKey);

            // LT (alternate keys in the Default layout) held + X: nothing dug.
            var seen = new bool[1];
            yield return ReadyForKey(player);
            SelfTest.Pad("LeftTrigger", 1f);
            yield return WaitFor(() => ZInput.GetButton(PadAltKeysButton), 0.8f);
            var altHeld = ZInput.GetButton(PadAltKeysButton);
            yield return TapPad("West", Replant.PadButtonX, seen);
            SelfTest.Pad("LeftTrigger", 0f);
            yield return Frames(4);
            c.Check(altHeld && seen[0], $"the game read LT as the alternate-keys button ({altHeld}) and the X press under it ({seen[0]})");
            c.Check(Alive(bush.View) && CountByName(rig.Inv, name) == 0, "LT held + X digs nothing");

            // LB: nothing dug.
            yield return TapPad("LeftShoulder", "JoyLBumper", seen);
            c.Check(seen[0] && Alive(bush.View) && CountByName(rig.Inv, name) == 0, $"LB (read by the game: {seen[0]}) digs nothing");

            // X: dug, player not sitting.
            yield return rig.AimAt(bush.AimAt);
            yield return Frames(3);
            yield return ReadyForKey(player);
            yield return TapPad("West", Replant.PadButtonX, seen);
            yield return WaitGone(bush.View, 1.5f);
            c.Check(!Alive(bush.View) && CountByName(rig.Inv, name) == 1, $"X digs the bush up and gives one transplant ({CountByName(rig.Inv, name)})");
            yield return new WaitForSeconds(0.8f);
            c.Check(!player.IsSitting() && !player.InEmote(), $"the player did not sit down (sitting {player.IsSitting()}, emote {player.InEmote()})");

            // Build menu still opens with its own button (A in the Default layout), nothing under the crosshair.
            rig.Look(player.m_lookYaw.eulerAngles.y, -60f);
            yield return Frames(3);
            yield return ReadyForKey(player);
            var opened = false;
            SelfTest.Pad("South", 1f);
            var openUntil = Time.realtimeSinceStartup + 0.5f;
            while (Time.realtimeSinceStartup < openUntil && !opened)
            {
                yield return null;
                opened = Hud.IsPieceSelectionVisible();
            }
            SelfTest.Pad("South", 0f);
            yield return Frames(4);
            c.Check(opened, "A (the Default layout's build menu button) opens the build menu");
            c.Note($"build menu still open after A was let go: {Hud.IsPieceSelectionVisible()}");
            Hud.HidePieceSelection();
            yield return Frames(3);
            c.Report();
        }
        finally
        {
            SelfTest.ReleaseInput();
            if (Hud.instance != null && Hud.IsPieceSelectionVisible())
            {
                Hud.HidePieceSelection();
            }
            rig.Restore();
        }
    }

    // ---------- replant.input.pad-layouts (T22, Alternative 1 and 2) ----------

    private static IEnumerator RunInputLayouts()
    {
        if (!WorldReady(InputLayoutsName))
        {
            yield break;
        }
        var c = new Checks(InputLayoutsName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, InputLayoutsName);
        var taken = new List<Vector3>();
        var layout0 = ZInput.InputLayout;
        try
        {
            if (!InputReady(c))
            {
                c.Report();
                yield break;
            }
            PrepareInput(rig);
            rig.Undo(() => SetPadLayoutInMemory(layout0));
            rig.Undo(() => SelfTest.ReleaseInput());
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var name = rasp.ItemDisplayName;
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 1);
            yield return Hold(rig, tool);
            if (!c.Check(tool != null, "cultivator given"))
            {
                c.Report();
                yield break;
            }
            var keyboardKey = "<b>" + Localization.instance.GetBoundKeyString(Replant.KeyboardButton) + "</b>";
            ControllerOn(rig);
            var dug = 0;
            var seen = new bool[1];
            foreach (var layout in new[] { InputLayout.Alternative1, InputLayout.Alternative2 })
            {
                var tag = layout.ToString();
                // Nothing held while the layout's buttons are made new; the game back on keyboard and mouse, so the
                // bush is found the same way each time.
                SelfTest.ReleaseInput();
                yield return Frames(6);
                SetPadLayoutInMemory(layout);
                yield return Frames(2);
                var bush = new Aimed();
                yield return SpawnInReach(rig, taken, "RaspberryBush", 1.2f, bush);
                if (!c.Check(bush.Seen && ZInput.InputLayout == layout, $"{tag}: a bush is under the crosshair within reach" + bush.Why))
                {
                    continue;
                }
                var awake = new bool[1];
                yield return WakePad(awake);
                if (!c.Check(awake[0], $"{tag}: the game reads the test gamepad and switched to gamepad input"))
                {
                    continue;
                }
                yield return rig.AimAt(bush.AimAt);
                yield return Frames(4);
                c.Check(Replant.Target.IsFresh && ReferenceEquals(Replant.Target.View, bush.View), $"{tag}: the bush is still under the crosshair after the stick push");
                CheckPadHint(c, tag, bush, rasp, keyboardKey);
                yield return ReadyForKey(player);
                yield return TapPad("West", Replant.PadButtonX, seen);
                yield return WaitGone(bush.View, 1.5f);
                if (c.Check(!Alive(bush.View), $"{tag}: X digs the bush up"))
                {
                    dug++;
                }
                c.Check(CountByName(rig.Inv, name) == dug, $"{tag}: one transplant given ({CountByName(rig.Inv, name)} carried, {dug} dug)");
                yield return new WaitForSeconds(0.6f);
                c.Check(!Hud.IsPieceSelectionVisible() && !player.IsSitting(), $"{tag}: the same press opened no build menu and did not make the player sit");
            }
            c.Report();
        }
        finally
        {
            SelfTest.ReleaseInput();
            if (Hud.instance != null && Hud.IsPieceSelectionVisible())
            {
                Hud.HidePieceSelection();
            }
            rig.Restore();
        }
    }

    // ---------- replant.input.rebind (T23 b) ----------

    private static readonly KeyValuePair<KeyCode, string>[] SpareKeys =
    {
        new KeyValuePair<KeyCode, string>(KeyCode.O, "<Keyboard>/o"),
        new KeyValuePair<KeyCode, string>(KeyCode.P, "<Keyboard>/p"),
        new KeyValuePair<KeyCode, string>(KeyCode.N, "<Keyboard>/n"),
        new KeyValuePair<KeyCode, string>(KeyCode.I, "<Keyboard>/i"),
    };

    // "<Keyboard>/e" -> KeyCode.E (letters only: what the test can press for sure). None = not such a key.
    private static KeyCode LetterKeyOf(string path)
    {
        const string prefix = "<Keyboard>/";
        if (path == null || !path.StartsWith(prefix, StringComparison.Ordinal) || path.Length != prefix.Length + 1)
        {
            return KeyCode.None;
        }
        var letter = char.ToUpperInvariant(path[prefix.Length]);
        return letter >= 'A' && letter <= 'Z' ? (KeyCode)((int)KeyCode.A + (letter - 'A')) : KeyCode.None;
    }

    // Real tap of a keyboard key; seen[0] = the game read it as held meanwhile.
    private static IEnumerator TapRealKey(KeyCode key, bool[] seen)
    {
        seen[0] = false;
        var tap = SelfTest.KeyTap(key);
        while (tap.MoveNext())
        {
            yield return tap.Current;
            seen[0] |= ZInput.GetKey(key, false);
        }
        yield return Frames(3);
    }

    // Game button with no binding at all make ZInput.ButtonDef.GetActionPath throw (first run): such a button uses no key.
    private static string PathOf(ZInput.ButtonDef b)
    {
        try
        {
            return b.GetActionPath();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IEnumerator RunInputRebind()
    {
        if (!WorldReady(InputRebindName))
        {
            yield break;
        }
        var c = new Checks(InputRebindName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, InputRebindName);
        var taken = new List<Vector3>();
        var zin = ZInput.instance;
        var def = zin.GetButtonDef(Replant.KeyboardButton);
        var oldOverride = def != null && def.ButtonAction.bindings.Count > 0 ? def.ButtonAction.bindings[0].overridePath : null;
        var oldPath = def != null ? def.GetActionPath() : null;
        var changed = false;
        void PutBack()
        {
            if (!changed || def == null)
            {
                return;
            }
            changed = false;
            if (string.IsNullOrEmpty(oldOverride))
            {
                def.ResetBinding();
            }
            else
            {
                def.Rebind(oldOverride);
            }
            ReplantHint.Reset();
        }
        try
        {
            if (!InputReady(c))
            {
                c.Report();
                yield break;
            }
            PrepareInput(rig);
            rig.Undo(PutBack);
            rig.Undo(() => SelfTest.ReleaseInput());
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var name = rasp.ItemDisplayName;
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 1);
            yield return Hold(rig, tool);
            var bush = new Aimed();
            yield return SpawnInReach(rig, taken, "RaspberryBush", 1.2f, bush);
            var oldKey = LetterKeyOf(oldPath);
            var spare = SpareKeys.FirstOrDefault(k => k.Value != oldPath && !zin.m_buttons.Values.Any(b => b != null && PathOf(b) == k.Value));
            if (!c.Check(tool != null && def != null && bush.Seen, "cultivator in hand, a ripe raspberry bush under the crosshair within reach" + bush.Why)
                || !c.Check(oldKey != KeyCode.None, $"the Use key of this player is a letter key the test can press ({oldPath})")
                || !c.Check(spare.Value != null, "a letter key no game button uses is free for the test (o, p, n or i)"))
            {
                c.Report();
                yield break;
            }
            var oldShown = Localization.instance.GetBoundKeyString(Replant.KeyboardButton);
            var plantName = HoverNameOf(bush.Go, rasp);
            c.Check(HintText() == KeyHint(plantName) && HintText().Contains("<b>" + oldShown + "</b>"), $"before: the hint shows the Use key '{oldShown}' ('{OneLine(HintText())}')");

            // Pause menu open with the real Esc key (Settings is reached from there).
            var seen = new bool[1];
            yield return TapRealKey(KeyCode.Escape, seen);
            yield return WaitFor(Menu.IsVisible, 1.5f);
            if (!Menu.IsVisible() && Menu.instance != null)
            {
                c.Note($"the real Esc key (read by the game: {seen[0]}) did not open the pause menu: opened by its own call");
                Menu.instance.Show();
                yield return Frames(3);
            }
            rig.Undo(() =>
            {
                if (Menu.instance != null && Menu.IsVisible())
                {
                    Menu.instance.OnClose();
                }
            });
            c.Check(Menu.IsVisible(), "pause menu open");

            // The key-binding step of the Settings window (KeyboardMouseSettings calls ZInput.StartBindKey for the row
            // clicked, then the next key pressed is taken): a real key press does the binding. Never saved.
            changed = true;
            zin.StartBindKey(Replant.KeyboardButton);
            yield return Frames(3);
            c.Check(ZInput.s_IsRebindActive, "the game waits for the new Use key");
            yield return TapRealKey(spare.Key, seen);
            yield return WaitFor(() => !ZInput.s_IsRebindActive, 2f);
            if (ZInput.s_IsRebindActive)
            {
                // One more press; still waiting = the game's own way out (Esc ends the binding step, the menu is
                // open and ignores it), so no later key press of the run gets bound to Use.
                yield return TapRealKey(spare.Key, seen);
                yield return WaitFor(() => !ZInput.s_IsRebindActive, 2f);
                if (ZInput.s_IsRebindActive)
                {
                    yield return TapRealKey(KeyCode.Escape, seen);
                    yield return WaitFor(() => !ZInput.s_IsRebindActive, 2f);
                }
            }
            var newShown = Localization.instance.GetBoundKeyString(Replant.KeyboardButton);
            if (!c.Check(!ZInput.s_IsRebindActive && def.GetActionPath() != oldPath && newShown != oldShown,
                    $"the real {spare.Key} key press bound Use to {def.GetActionPath()} (shown '{newShown}', before '{oldShown}'; still waiting for a key: {ZInput.s_IsRebindActive})"))
            {
                c.Report();
                yield break;
            }
            // A few frames with the menu open: the hint forgets its texts there.
            yield return Frames(5);
            if (Menu.instance != null && Menu.IsVisible())
            {
                Menu.instance.OnClose();
            }
            yield return WaitFor(() => !Menu.IsVisible(), 1.5f);
            c.Check(!Menu.IsVisible(), "pause menu closed again (its Continue button)");
            yield return new WaitForSecondsRealtime(0.4f);

            // Back in the world: hint shows the new key, the old key digs nothing, the new key digs.
            yield return rig.AimAt(bush.AimAt);
            yield return Frames(4);
            var hint = HintText();
            c.Check(Replant.Target.IsFresh && ReferenceEquals(Replant.Target.View, bush.View), "the bush is still under the crosshair");
            c.Check(hint == KeyHint(plantName) && hint.Contains("<b>" + newShown + "</b>") && !hint.Contains("<b>" + oldShown + "</b>"),
                $"after the rebind and the menu: the hint shows the new key '{newShown}' ('{OneLine(hint)}')");
            var snap = player.m_manualSnapPoint;
            rig.Undo(() => player.m_manualSnapPoint = snap);
            yield return ReadyForKey(player);
            yield return TapRealKey(oldKey, seen);
            yield return new WaitForSeconds(0.4f);
            c.Check(seen[0] && Alive(bush.View) && CountByName(rig.Inv, name) == 0, $"the real old key {oldKey} (read by the game: {seen[0]}) digs nothing");
            player.m_manualSnapPoint = snap;
            yield return rig.AimAt(bush.AimAt);
            yield return ReadyForKey(player);
            yield return TapRealKey(spare.Key, seen);
            yield return WaitGone(bush.View, 1.5f);
            c.Check(!Alive(bush.View) && CountByName(rig.Inv, name) == 1, $"the real new key {spare.Key} digs the bush up ({CountByName(rig.Inv, name)} transplant)");

            PutBack();
            c.Check(def.GetActionPath() == oldPath && Localization.instance.GetBoundKeyString(Replant.KeyboardButton) == oldShown, $"Use key put back ({def.GetActionPath()}), nothing saved");
            c.Report();
        }
        finally
        {
            SelfTest.ReleaseInput();
            if (Menu.instance != null && Menu.IsVisible())
            {
                Menu.instance.OnClose();
            }
            PutBack();
            rig.Restore();
        }
    }
}
#endif
