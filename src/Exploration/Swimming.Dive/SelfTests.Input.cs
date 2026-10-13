#if DEBUG
using System;
using System.Collections;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SwimmingDiveMod;

// Debug build only. Live tests with REAL input: the probe change the state of the real keyboard and of a test gamepad
// (SelfTest.Key / Pad ...), so ZInput, the game and me read a press like from a person. One small test per TESTING.md
// item (real input is new: one test can be fixed or dropped alone):
//   dive.input.pad              T11: gamepad on the Default layout: its crouch button (left stick press) held dives at
//                               swim speed, its jump button held rises at the same speed, handed back at the surface,
//                               no jump, never sneaking
//   dive.input.pad-alt          T11: the same on the Alternative 1 layout with the buttons that layout binds
//   dive.input.rebind           T12: Crouch = C and Jump = V, set the way the game loads a saved rebind
//                               (ZInput.ButtonDef.Rebind): real C dives, real V rises, the keys bound before (Left Ctrl,
//                               Space) do nothing at the surface nor under water
//   dive.input.rebind-settings  T12: the same, the binding made the way the Settings screen makes it (ZInput.StartBindKey,
//                               then the next real key press is the new key)
//   dive.input.storm            T34: real keys in the game's stormiest weather: Crouch a moment before Jump on rising
//                               waves, short Crouch taps, W and A while both keys are held, Crouch alone
// Gamepad layout and key bindings changed in memory only (no saved preference written), put back by the rig.
internal static partial class SelfTests
{
    private const string PadName = "dive.input.pad";
    private const string PadAltName = "dive.input.pad-alt";
    private const string RebindName = "dive.input.rebind";
    private const string RebindSettingsName = "dive.input.rebind-settings";
    private const string StormKeysName = "dive.input.storm";

    // Keys of TESTING.md T12.
    private const KeyCode NewCrouchKey = KeyCode.C;
    private const KeyCode NewJumpKey = KeyCode.V;

    // ---------- real input tools ----------

    // Probe's input driver there? (Every input call say false when no probe drive the game.)
    private static bool InputReady(Checks c) =>
        c.Check(SelfTest.ReleaseInput() && ZInput.instance != null,
            "no input driver: real key and gamepad presses need a run of tools/Test-InWorld.ps1");

    private static IEnumerator WaitFor(Func<bool> done, float seconds)
    {
        var t0 = Time.realtimeSinceStartup;
        while (!done() && Time.realtimeSinceStartup - t0 < seconds)
        {
            yield return null;
        }
    }

    private static bool SamePath(string a, string b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);

    // Keyboard key or mouse button a game button is bound to right now (the player's own binding), found with the
    // game's own KeyCode -> control path table. False = bound to nothing the driver can press.
    private static bool KeyOf(string button, out KeyCode key, out string path)
    {
        key = KeyCode.None;
        path = "";
        var def = Button(button);
        if (def == null)
        {
            return false;
        }
        path = def.GetActionPath() ?? "";
        if (path.Length == 0)
        {
            return false;
        }
        foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
        {
            if (k == KeyCode.None || k >= KeyCode.JoystickButton0 || k == KeyCode.Mouse5 || k == KeyCode.Mouse6)
            {
                continue;
            }
            if (SamePath(ZInput.KeyCodeToPath(k, false), path))
            {
                key = k;
                return true;
            }
        }
        return false;
    }

    // Control of the test gamepad (SelfTest.Pad name) a gamepad button of the game is bound to in the layout set now.
    private static bool PadControlOf(string button, out string control, out string path)
    {
        control = "";
        path = "";
        var def = Button(button);
        if (def == null)
        {
            return false;
        }
        path = def.GetActionPath() ?? "";
        switch (path.ToLowerInvariant())
        {
            case "<gamepad>/buttonsouth":
                control = "South";
                return true;
            case "<gamepad>/buttoneast":
                control = "East";
                return true;
            case "<gamepad>/buttonwest":
                control = "West";
                return true;
            case "<gamepad>/buttonnorth":
                control = "North";
                return true;
            case "<gamepad>/leftstickpress":
                control = "LeftStick";
                return true;
            case "<gamepad>/rightstickpress":
                control = "RightStick";
                return true;
            case "<gamepad>/leftshoulder":
                control = "LeftShoulder";
                return true;
            case "<gamepad>/rightshoulder":
                control = "RightShoulder";
                return true;
            case "<gamepad>/lefttrigger":
                control = "LeftTrigger";
                return true;
            case "<gamepad>/righttrigger":
                control = "RightTrigger";
                return true;
            default:
                return false;
        }
    }

    // Gamepad layout in memory, the way Settings > Controls switch it (ZInput.ChangeInputLayout) minus the saved
    // preference. Nothing may be held on the pad meanwhile (the layout's buttons are made new).
    private static void SetPadLayout(InputLayout layout)
    {
        ZInput.InputLayout = layout;
        ZInput.instance.UpdateGamepadInputLayout();
    }

    // Test gamepad made and in use before the press that count (first input of a new pad can come in a frame where the
    // game still look at the mouse): right stick pushed up for a moment. No dive key hang on that push; the view turn
    // a little, the rig put the look back.
    private static IEnumerator WakePad(Box result)
    {
        result.Ok = false;
        for (var attempt = 0; attempt < 3 && !result.Ok; attempt++)
        {
            SelfTest.PadStick(true, new Vector2(0f, 0.9f));
            yield return WaitFor(() => ZInput.IsGamepadActive() && ZInput.GetButton("JoyRStickUp"), 0.7f);
            result.Ok = ZInput.IsGamepadActive() && ZInput.GetButton("JoyRStickUp");
            SelfTest.PadStick(true, Vector2.zero);
            for (var i = 0; i < 4; i++)
            {
                yield return null;
            }
        }
    }

    // The player take keyboard and gamepad input (PlayerController on, no window open).
    private static bool TakesInput(Checks c, Player player)
    {
        var controller = player.GetComponent<PlayerController>();
        return c.Check(controller != null && controller.enabled && player.TakeInput(),
            $"the player takes input (controller on {controller != null && controller.enabled}, no menu open {player.TakeInput()})");
    }

    // ---------- dive.input.pad, dive.input.pad-alt (T11) ----------

    private static IEnumerator RunPad() =>
        RunPadLayout(PadName, InputLayout.Default, "LeftStick", "East", "the Default gamepad layout");

    private static IEnumerator RunPadAlt() =>
        RunPadLayout(PadAltName, InputLayout.Alternative1, "RightStick", "South", "the Alternative 1 gamepad layout");

    // wantCrouch / wantJump: the controls ZInput binds JoyCrouch and JoyJump to in that layout (AddGamepadClassicButtons,
    // AddGamepadAlt1Buttons). The test press what the game really bound; another binding = failed check, not a wrong press.
    private static IEnumerator RunPadLayout(string name, InputLayout layout, string wantCrouch, string wantJump, string layoutText)
    {
        yield return Settle();
        var s = Begin(name);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        var tap = s.Tap;
        var layoutBefore = ZInput.InputLayout;
        var layoutChanged = false;
        try
        {
            // Real buttons: no test key override. Rules and view forced in memory.
            ApplyDefaults();
            DiveInput.ClearTest();
            CalmWind(rig);
            if (!InputReady(c))
            {
                c.Report();
                yield break;
            }
            if (!c.Check(ZInput.IsGamepadEnabled(),
                    "the gamepad is turned off in the game's settings (Settings, Controls): the game ignores every gamepad, nothing "
                    + "tested"))
            {
                c.Report();
                yield break;
            }
            if (!DeepSpot(s, out var spot))
            {
                c.Report();
                yield break;
            }
            var ok = new Box();
            yield return EnterWater(s, spot, false, ok);
            if (!ok.Ok || !TakesInput(c, player))
            {
                c.Report();
                yield break;
            }
            if (layoutBefore != layout)
            {
                SelfTest.ReleaseInput();
                // Flag first: layout change that throw half way still get put back in finally.
                layoutChanged = true;
                SetPadLayout(layout);
                c.Note($"this player's gamepad layout is {layoutBefore}: {layoutText} is set in memory for the test (the saved "
                       + "setting is not touched) and put back after");
                yield return null;
                yield return null;
            }
            var haveCrouch = PadControlOf("JoyCrouch", out var crouchControl, out var crouchPath);
            var haveJump = PadControlOf("JoyJump", out var jumpControl, out var jumpPath);
            if (!c.Check(ZInput.InputLayout == layout && haveCrouch && haveJump,
                    $"{layoutText} is set and its crouch and jump buttons are gamepad buttons the test can press (layout "
                    + $"{ZInput.InputLayout}, crouch '{crouchPath}', jump '{jumpPath}')"))
            {
                c.Report();
                yield break;
            }
            c.Check(crouchControl == wantCrouch && jumpControl == wantJump,
                $"{layoutText}: crouch is on {crouchControl} and jump on {jumpControl} (expected {wantCrouch} and {wantJump}, as "
                + "the game's code binds them)");
            var wake = new Box();
            yield return WakePad(wake);
            if (!c.Check(wake.Ok, $"the game reads the test gamepad (gamepad in use {ZInput.IsGamepadActive()})"))
            {
                c.Report();
                yield break;
            }

            var profile = Game.instance.GetPlayerProfile();
            var jumps = profile.GetStat(PlayerStatType.Jumps);
            var swim = DiveController.SwimSpeed(player);
            rig.TakeStaminaRate();
            yield return new WaitForSeconds(0.4f);
            rig.Refill();
            var s0 = player.GetStamina();
            tap.Clear();
            var from = Flat(player.transform.position);

            // ---- crouch button held: down at swim speed (T01 with the gamepad) ----
            SelfTest.Pad(crouchControl, 1f);
            var seen = false;
            var sneaked = false;
            var t15 = -1f;
            var t45 = -1f;
            var t0 = Time.time;
            while (Time.time - t0 < 6f && t45 < 0f)
            {
                yield return FixedTick;
                seen |= ZInput.GetButton("JoyCrouch");
                sneaked |= player.IsCrouching();
                var under = Under(player);
                if (t15 < 0f && under >= 1.5f)
                {
                    t15 = Time.time;
                }
                if (under >= 4.5f)
                {
                    t45 = Time.time;
                }
            }
            var deep = DiveState.Diving && DiveState.Deep;
            var underDown = Under(player);
            var downTook = Time.time - t0;
            var s1 = player.GetStamina();
            var padActive = ZInput.IsGamepadActive();
            SelfTest.Pad(crouchControl, 0f);
            var downSpeed = t15 >= 0f && t45 > t15 ? 3f / (t45 - t15) : 0f;
            var drift = Vector3.Distance(from, Flat(player.transform.position));
            c.Check(seen, $"{layoutText}: the game read the crouch button ({crouchControl}) as held (gamepad in use {padActive})");
            c.Check(deep && t45 >= 0f,
                $"{layoutText}, crouch button ({crouchControl}) held: dives ({F(underDown)} m under the rest height after "
                + $"{F(downTook)} s, deep {deep})");
            c.Check(downSpeed >= 0.7f * swim && downSpeed <= 1.3f * swim && drift < 1f,
                $"{layoutText}, crouch button held: straight down at about the swimming speed ({F(downSpeed)} m/s from 1.5 to "
                + $"4.5 m, swim speed {F(swim)} m/s, {F(drift)} m sideways)");
            c.Check(tap.Count(StartedDefault) == 1, $"one \"{StartedDefault}\" Debug line ({tap.Count(StartedDefault)})");
            c.Check(s0 - s1 > 1f, $"{layoutText}, crouch button held: the stamina bar goes down on the way down ({F(s0)} -> {F(s1)})");

            // ---- no button: depth held; jump button held: up at the same speed, handed back, no jump (T02) ----
            yield return Ticks(50);
            c.Check(DiveState.Diving && DiveState.Deep, "crouch button released under water: still diving, depth held");
            var jumpTimer = player.m_jumpTimer;
            var jumped = false;
            var seenJump = false;
            var t35 = -1f;
            var t10 = -1f;
            rig.Refill();
            SelfTest.Pad(jumpControl, 1f);
            t0 = Time.time;
            while (DiveState.Diving && Time.time - t0 < 8f)
            {
                yield return FixedTick;
                seenJump |= ZInput.GetButton("JoyJump");
                sneaked |= player.IsCrouching();
                jumped |= player.m_jumpTimer < jumpTimer;
                jumpTimer = player.m_jumpTimer;
                var under = Under(player);
                if (t35 < 0f && under <= 3.5f)
                {
                    t35 = Time.time;
                }
                if (t10 < 0f && under <= 1f)
                {
                    t10 = Time.time;
                }
            }
            var roseIn = Time.time - t0;
            var handedBack = !DiveState.Diving;
            SelfTest.Pad(jumpControl, 0f);
            var above = float.NegativeInfinity;
            for (var i = 0; i < 75; i++)
            {
                yield return FixedTick;
                above = Mathf.Max(above, -Under(player));
                jumped |= player.m_jumpTimer < jumpTimer;
                jumpTimer = player.m_jumpTimer;
            }
            var upSpeed = t35 >= 0f && t10 > t35 ? 2.5f / (t10 - t35) : 0f;
            c.Check(seenJump, $"{layoutText}: the game read the jump button ({jumpControl}) as held");
            c.Check(handedBack && upSpeed >= 0.7f * swim && upSpeed <= 1.3f * swim,
                $"{layoutText}, jump button ({jumpControl}) held: rises at the same speed ({F(upSpeed)} m/s from 3.5 to 1 m, swim "
                + $"speed {F(swim)} m/s) and is handed back to normal swimming in {F(roseIn)} s (handed back {handedBack})");
            c.Check(tap.Count(EndedSurface) == 1, $"one \"{EndedSurface}\" Debug line ({tap.Count(EndedSurface)})");
            c.Check(!jumped && above < 0.35f && profile.GetStat(PlayerStatType.Jumps) == jumps,
                $"{layoutText}, jump button held: no jump out of the water, floating at the usual height (jump {jumped}, feet at "
                + $"most {F(above)} m above the rest height)");
            c.Check(!sneaked && !player.IsCrouching() && !player.m_crouchToggled,
                $"{layoutText}: the player never sneaks in water (sneak pose seen {sneaked}, crouch toggle {player.m_crouchToggled})");
            rig.GiveStaminaRateBack();
            rig.Refill();

            SelfTest.ReleaseInput();
            yield return WaitFor(() => !ZInput.IsGamepadActive(), 2f);
            c.Note($"test gamepad removed: gamepad in use {ZInput.IsGamepadActive()}");
            yield return GoHome(s);
            CheckLog(s);
        }
        finally
        {
            SelfTest.ReleaseInput();
            try
            {
                if (layoutChanged && ZInput.instance != null)
                {
                    SetPadLayout(layoutBefore);
                }
            }
            catch (Exception e)
            {
                SelfTest.Note(name, $"restore gamepad layout failed: {e.Message}");
            }
            rig.Restore();
        }
        c.Report();
    }

    // ---------- dive.input.rebind, dive.input.rebind-settings (T12) ----------

    // Binding back like before the test: no override when it was the default one, else the override the game itself
    // would load (ZInput.Load call Rebind with the saved path).
    private static void PutBindingBack(string button, string before, string defaultPath)
    {
        var def = Button(button);
        if (def == null)
        {
            return;
        }
        if (SamePath(before, defaultPath))
        {
            def.ResetBinding();
        }
        else
        {
            def.Rebind(before ?? "");
        }
    }

    // The Settings screen's own way (KeyboardMouseSettings.OpenBindDialog): ZInput.StartBindKey, then the next real
    // key press is the new key. A capture that never ends is ended with a real Escape (ZInput.CancelOnAbortInput): else
    // the next key anybody presses would become the binding.
    private static IEnumerator CaptureKey(string button, KeyCode key, Box result)
    {
        result.Ok = false;
        result.Detail = "";
        ZInput.instance.StartBindKey(button);
        if (!ZInput.s_IsRebindActive)
        {
            result.Detail = $"the game did not start the key capture for {button}";
            yield break;
        }
        // Settings screen wait 0.2 s before it look at keys.
        yield return new WaitForSecondsRealtime(0.25f);
        yield return SelfTest.KeyTap(key, 0.2f);
        yield return WaitFor(() => !ZInput.s_IsRebindActive, 2f);
        if (ZInput.s_IsRebindActive)
        {
            yield return SelfTest.KeyTap(KeyCode.Escape, 0.1f);
            yield return WaitFor(() => !ZInput.s_IsRebindActive, 1f);
            if (Menu.IsVisible() && Menu.instance != null)
            {
                Menu.instance.OnClose();
            }
            result.Detail = $"the key capture for {button} did not take the real {key} press within 2 s (ended with Escape: "
                            + $"{!ZInput.s_IsRebindActive})";
            yield break;
        }
        result.Ok = true;
    }

    // Real key held for a number of physics ticks. box.Ok = the game read the key as down meanwhile; box.Value = feet
    // furthest under the rest height; box.Point.x = a dive was on at some tick (1 / 0), box.Point.y = height moved.
    private static IEnumerator HoldKey(Player player, KeyCode key, int ticks, Box box)
    {
        box.Ok = false;
        box.Value = float.NegativeInfinity;
        var dived = false;
        var y0 = player.transform.position.y;
        SelfTest.Key(key, true);
        for (var i = 0; i < ticks; i++)
        {
            yield return FixedTick;
            box.Ok |= ZInput.GetKey(key, false);
            dived |= DiveState.Diving;
            box.Value = Mathf.Max(box.Value, Under(player));
        }
        SelfTest.Key(key, false);
        yield return FixedTick;
        box.Point = new Vector3(dived ? 1f : 0f, player.transform.position.y - y0, 0f);
    }

    private static IEnumerator RunRebind() => RunRebindCase(RebindName, false);

    private static IEnumerator RunRebindSettings() => RunRebindCase(RebindSettingsName, true);

    private static IEnumerator RunRebindCase(string name, bool capture)
    {
        yield return Settle();
        var s = Begin(name);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        var tap = s.Tap;
        try
        {
            ApplyDefaults();
            DiveInput.ClearTest();
            CalmWind(rig);
            if (!InputReady(c))
            {
                c.Report();
                yield break;
            }
            var crouch = Button("Crouch");
            var jump = Button("Jump");
            if (!c.Check(crouch != null && jump != null && crouch.Rebindable && jump.Rebindable,
                    "the game has the rebindable buttons Crouch and Jump"))
            {
                c.Report();
                yield break;
            }
            var crouchBefore = crouch.GetActionPath() ?? "";
            var crouchDefault = crouch.GetActionPath(false) ?? "";
            var jumpBefore = jump.GetActionPath() ?? "";
            var jumpDefault = jump.GetActionPath(false) ?? "";
            var haveOldCrouch = KeyOf("Crouch", out var oldCrouch, out _);
            var haveOldJump = KeyOf("Jump", out var oldJump, out _);
            if (!c.Check(haveOldCrouch && haveOldJump && oldCrouch != NewCrouchKey && oldCrouch != NewJumpKey
                         && oldJump != NewCrouchKey && oldJump != NewJumpKey,
                    $"Crouch and Jump are bound to keys the test can press, other than {NewCrouchKey} and {NewJumpKey} (Crouch "
                    + $"'{crouchBefore}', Jump '{jumpBefore}')"))
            {
                c.Report();
                yield break;
            }
            if (!DeepSpot(s, out var spot))
            {
                c.Report();
                yield break;
            }
            var ok = new Box();
            yield return EnterWater(s, spot, false, ok);
            if (!ok.Ok || !TakesInput(c, player))
            {
                c.Report();
                yield break;
            }
            // C is the game's walk toggle and V its auto-pickup toggle by default: both put back at the end.
            var walkBefore = player.m_walk;
            var pickupBefore = Player.m_enableAutoPickup;
            rig.Undo("walk and auto-pickup toggles", () =>
            {
                player.SetWalk(walkBefore);
                Player.m_enableAutoPickup = pickupBefore;
            });
            rig.Undo("key bindings", () =>
            {
                PutBindingBack("Crouch", crouchBefore, crouchDefault);
                PutBindingBack("Jump", jumpBefore, jumpDefault);
            });

            // ---- the rebind ----
            if (capture)
            {
                var got = new Box();
                yield return CaptureKey("Crouch", NewCrouchKey, got);
                c.Check(got.Ok, $"key capture for Crouch (as the Settings screen starts it) took the real {NewCrouchKey} press: {got.Detail}");
                if (got.Ok)
                {
                    yield return new WaitForSecondsRealtime(0.3f);
                    yield return CaptureKey("Jump", NewJumpKey, got);
                    c.Check(got.Ok, $"key capture for Jump (as the Settings screen starts it) took the real {NewJumpKey} press: {got.Detail}");
                }
                yield return new WaitForSecondsRealtime(0.3f);
            }
            else
            {
                crouch.Rebind(ZInput.KeyCodeToPath(NewCrouchKey, false));
                jump.Rebind(ZInput.KeyCodeToPath(NewJumpKey, false));
                yield return null;
                yield return null;
            }
            var isCrouch = KeyOf("Crouch", out var crouchNow, out var crouchPath) && crouchNow == NewCrouchKey;
            var isJump = KeyOf("Jump", out var jumpNow, out var jumpPath) && jumpNow == NewJumpKey;
            if (!c.Check(isCrouch && isJump && !ZInput.s_IsRebindActive,
                    $"Crouch is bound to {NewCrouchKey} and Jump to {NewJumpKey} now (Crouch '{crouchPath}', Jump '{jumpPath}', were "
                    + $"'{crouchBefore}' and '{jumpBefore}')"))
            {
                c.Report();
                yield break;
            }
            yield return Ticks(25);
            var key = new Box();

            // ---- the old Crouch key at the surface: nothing ----
            tap.Clear();
            yield return HoldKey(player, oldCrouch, 75, key);
            c.Check(key.Ok && key.Point.x < 0.5f && key.Value < 0.45f && tap.Count("Dive started (") == 0,
                $"the old Crouch key ({oldCrouch}) held 1.5 s at the surface: no dive (key read by the game {key.Ok}, dive "
                + $"{key.Point.x > 0.5f}, feet at most {F(key.Value)} m under the rest height)");

            // ---- the new Crouch key: dives ----
            SelfTest.Key(NewCrouchKey, true);
            var seen = false;
            var t0 = Time.time;
            while (Time.time - t0 < 5f && Under(player) < 3.5f)
            {
                yield return FixedTick;
                seen |= ZInput.GetButton("Crouch");
            }
            var deep = DiveState.Diving && DiveState.Deep;
            var under = Under(player);
            SelfTest.Key(NewCrouchKey, false);
            c.Check(seen && deep && under >= 3.5f && tap.Count("Dive started (") == 1,
                $"the new Crouch key ({NewCrouchKey}) held: dives ({F(under)} m under the rest height in {F(Time.time - t0)} s, deep "
                + $"{deep}, \"Dive started\" lines {tap.Count("Dive started (")})");
            yield return Ticks(65);

            // ---- under water: the old keys move nothing ----
            yield return HoldKey(player, oldJump, 50, key);
            c.Check(key.Ok && Mathf.Abs(key.Point.y) < 0.3f && DiveState.Diving && DiveState.Deep,
                $"the old Jump key ({oldJump}) held 1 s under water: the diver does not rise (key read by the game {key.Ok}, "
                + $"moved {F(key.Point.y)} m)");
            yield return HoldKey(player, oldCrouch, 50, key);
            c.Check(key.Ok && Mathf.Abs(key.Point.y) < 0.3f && DiveState.Diving && DiveState.Deep,
                $"the old Crouch key ({oldCrouch}) held 1 s under water: the diver does not go down (key read by the game "
                + $"{key.Ok}, moved {F(key.Point.y)} m)");

            // ---- the new Jump key: rises, handed back, no jump ----
            var jumpTimer = player.m_jumpTimer;
            var jumped = false;
            var yUp = player.transform.position.y;
            seen = false;
            SelfTest.Key(NewJumpKey, true);
            t0 = Time.time;
            while (DiveState.Diving && Time.time - t0 < 8f)
            {
                yield return FixedTick;
                seen |= ZInput.GetButton("Jump");
                jumped |= player.m_jumpTimer < jumpTimer;
                jumpTimer = player.m_jumpTimer;
            }
            var roseIn = Time.time - t0;
            var rose = player.transform.position.y - yUp;
            SelfTest.Key(NewJumpKey, false);
            c.Check(seen && !DiveState.Diving && rose > 2f && !jumped,
                $"the new Jump key ({NewJumpKey}) held: rises {F(rose)} m and is handed back at the surface in {F(roseIn)} s, no "
                + $"jump (key read as Jump {seen}, jump {jumped})");
            yield return Ticks(50);

            // ---- keys back ----
            SelfTest.ReleaseInput();
            PutBindingBack("Crouch", crouchBefore, crouchDefault);
            PutBindingBack("Jump", jumpBefore, jumpDefault);
            yield return null;
            c.Check(SamePath(Button("Crouch").GetActionPath(), crouchBefore) && SamePath(Button("Jump").GetActionPath(), jumpBefore),
                $"the keys are put back (Crouch '{Button("Crouch").GetActionPath()}', Jump '{Button("Jump").GetActionPath()}')");

            yield return GoHome(s);
            CheckLog(s);
        }
        finally
        {
            SelfTest.ReleaseInput();
            rig.Restore();
        }
        c.Report();
    }

    // ---------- dive.input.storm (T34) ----------

    private static IEnumerator RunStormKeys()
    {
        yield return Settle();
        var s = Begin(StormKeysName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        try
        {
            ApplyDefaults();
            DiveInput.ClearTest();
            if (!InputReady(c))
            {
                c.Report();
                yield break;
            }
            var haveCrouch = KeyOf("Crouch", out var crouchKey, out var crouchPath);
            var haveJump = KeyOf("Jump", out var jumpKey, out var jumpPath);
            var haveForward = KeyOf("Forward", out var forwardKey, out var forwardPath);
            var haveLeft = KeyOf("Left", out var leftKey, out var leftPath);
            if (!c.Check(haveCrouch && haveJump && haveForward && haveLeft,
                    $"Crouch, Jump, Forward and Left are bound to keys the test can press ('{crouchPath}', '{jumpPath}', "
                    + $"'{forwardPath}', '{leftPath}')"))
            {
                c.Report();
                yield break;
            }
            if (!DeepSpot(s, out var spot))
            {
                c.Report();
                yield break;
            }
            // Storm forced like console "env <name>", wind pinned at its strongest before the trip: the game's wind
            // change is done on arrival.
            var env = EnvMan.instance;
            var storm = StormEnv();
            EnvSetup setup = null;
            if (!string.IsNullOrEmpty(storm))
            {
                foreach (var e in env.m_environments)
                {
                    if (e != null && e.m_name == storm)
                    {
                        setup = e;
                        break;
                    }
                }
            }
            if (!c.Check(setup != null, "the game has a weather to force (EnvMan.m_environments)"))
            {
                c.Report();
                yield break;
            }
            var envBefore = env.m_debugEnv;
            rig.Undo("weather", () =>
            {
                if (EnvMan.instance != null)
                {
                    EnvMan.instance.m_debugEnv = envBefore;
                }
            });
            env.m_debugEnv = storm;
            var strongest = Mathf.Clamp(setup.m_windMax, 0.05f, 1f);
            PinWind(rig, strongest);
            var ok = new Box();
            yield return EnterWater(s, spot, false, ok);
            if (!ok.Ok || !TakesInput(c, player))
            {
                c.Report();
                yield break;
            }
            yield return WaitWind(strongest);
            var wind = env.GetWindIntensity();
            c.Note($"storm weather '{storm}' forced, wind pinned at its strongest ({F(strongest)}): wind now {F(wind)}");
            if (!c.Check(Mathf.Abs(wind - strongest) <= 0.05f, $"the wind is the storm's strongest (wanted {F(strongest)}, is {F(wind)})"))
            {
                c.Report();
                yield break;
            }
            var dt = Time.fixedDeltaTime;
            rig.TakeStaminaRate();
            yield return new WaitForSeconds(0.4f);
            var drain = SwimDrain(player);

            // ---- on a rising wave: Crouch a moment before Jump, both held 2.5 s (twice) ----
            var risingRounds = 0;
            var crouchFirstRounds = 0;
            // Same press must have both: wave lift him AND Crouch read before Jump. One of each in two presses prove nothing.
            var waveCrouchFirstRounds = 0;
            var holdTicks = 0;
            var bothTicks = 0;
            var vanillaTicks = 0;
            var deepTicks = 0;
            var worstCost = 0f;
            var maxUnder = float.NegativeInfinity;
            var feetLow = float.PositiveInfinity;
            var feetHigh = float.NegativeInfinity;
            var waterLow = float.PositiveInfinity;
            var waterHigh = float.NegativeInfinity;
            var swamForward = 0f;
            var swamLeft = 0f;
            for (var round = 0; round < 2; round++)
            {
                rig.Refill();
                // Wait (3 s at most) for water that rises at the swimmer: a wave lifting him.
                var rise = 0f;
                var level = player.GetLiquidLevel();
                var waitEnd = Time.time + 3f;
                while (Time.time < waitEnd && rise < 0.3f)
                {
                    yield return Ticks(5);
                    var now = player.GetLiquidLevel();
                    rise = (now - level) / (5f * dt);
                    level = now;
                }
                if (rise >= 0.3f)
                {
                    risingRounds++;
                }
                var before = player.GetStamina();
                SelfTest.Key(crouchKey, true);
                // Crouch alone until the game read it for two physics ticks (0.3 s at most), then Jump too.
                var alone = 0;
                var r0 = Time.realtimeSinceStartup;
                while (alone < 2 && Time.realtimeSinceStartup - r0 < 0.3f)
                {
                    yield return FixedTick;
                    if (ZInput.GetButton("Crouch") && !ZInput.GetButton("Jump"))
                    {
                        alone++;
                    }
                    if (DiveState.Deep)
                    {
                        deepTicks++;
                    }
                }
                SelfTest.Key(jumpKey, true);
                if (alone > 0)
                {
                    crouchFirstRounds++;
                    if (rise >= 0.3f)
                    {
                        waveCrouchFirstRounds++;
                    }
                }
                var holdEnd = Time.time + 2.5f;
                while (Time.time < holdEnd)
                {
                    yield return FixedTick;
                    if (DiveState.Deep)
                    {
                        deepTicks++;
                    }
                    if (!(ZInput.GetButton("Crouch") && ZInput.GetButton("Jump")))
                    {
                        continue;
                    }
                    bothTicks++;
                    holdTicks++;
                    // Normal game has the vertical: dive on but handed back, or the game itself stopped swimming for a
                    // moment (falling wave left the swimmer out of the water: no dive at all then).
                    if (!player.IsSwimming() || (DiveState.Diving && DiveState.HandedBack && player.m_body.useGravity))
                    {
                        vanillaTicks++;
                    }
                    var feet = player.transform.position.y;
                    var water = player.GetLiquidLevel();
                    maxUnder = Mathf.Max(maxUnder, Under(player));
                    feetLow = Mathf.Min(feetLow, feet);
                    feetHigh = Mathf.Max(feetHigh, feet);
                    waterLow = Mathf.Min(waterLow, water);
                    waterHigh = Mathf.Max(waterHigh, water);
                }
                worstCost = Mathf.Max(worstCost, before - player.GetStamina());
                if (round == 1)
                {
                    // Both keys still held: W, then A, still swim the swimmer (the drain of that swimming is the normal
                    // game's, not looked at).
                    var from = Flat(player.transform.position);
                    SelfTest.Key(forwardKey, true);
                    for (var i = 0; i < 75; i++)
                    {
                        yield return FixedTick;
                        if (DiveState.Deep)
                        {
                            deepTicks++;
                        }
                    }
                    SelfTest.Key(forwardKey, false);
                    swamForward = Vector3.Distance(from, Flat(player.transform.position));
                    from = Flat(player.transform.position);
                    SelfTest.Key(leftKey, true);
                    for (var i = 0; i < 75; i++)
                    {
                        yield return FixedTick;
                        if (DiveState.Deep)
                        {
                            deepTicks++;
                        }
                    }
                    SelfTest.Key(leftKey, false);
                    swamLeft = Vector3.Distance(from, Flat(player.transform.position));
                }
                SelfTest.Key(crouchKey, false);
                SelfTest.Key(jumpKey, false);
                yield return Ticks(50);
            }
            var held = $"storm (wind {F(strongest)}), real Crouch key pressed a moment before the real Jump key, both held 2.5 s, "
                       + "twice";
            var facts = $"deep {deepTicks} ticks, left to the normal game {vanillaTicks} of {holdTicks} ticks with both keys, feet at "
                        + $"most {F(maxUnder)} m under the rest height, the swimmer rose and fell {F(feetHigh - feetLow)} m while the "
                        + $"water rose and fell {F(waterHigh - waterLow)} m, a hold cost at most {F(worstCost)} stamina";
            c.Note($"{held}: {facts}; {risingRounds} of 2 presses on rising water, Crouch read alone first in {crouchFirstRounds} of 2, "
                   + $"both in the same press {waveCrouchFirstRounds} of 2");
            c.Check(bothTicks > 0, $"{held}: the game read both keys as held ({bothTicks} ticks)");
            // None of the two presses met the case the item is about = nothing proved: a fail, never a pass by luck.
            c.Check(waveCrouchFirstRounds > 0,
                $"{held}: at least one press came on a wave that lifts the swimmer with Crouch read before Jump in that same "
                + $"press ({waveCrouchFirstRounds} of 2; on rising water {risingRounds} of 2, Crouch read first {crouchFirstRounds} "
                + "of 2); else this run proves nothing, run it again");
            c.Check(deepTicks == 0 && holdTicks > 0 && vanillaTicks == holdTicks,
                $"{held}: never held under a wave, the swimmer stays with the normal game on the waves ({facts})");
            c.Check(feetHigh - feetLow >= 0.6f * (waterHigh - waterLow),
                $"{held}: rises and falls with the waves (the swimmer {F(feetHigh - feetLow)} m, the water under it "
                + $"{F(waterHigh - waterLow)} m)");
            c.Check(worstCost < 1.5f, $"{held}: the stamina bar stays full or nearly full (a hold cost at most {F(worstCost)})");
            c.Check(swamForward > 1f && swamLeft > 0.7f,
                $"Crouch and Jump held at the surface in the storm: the real W key still swims the swimmer ({F(swamForward)} m in "
                + $"1.5 s) and the real A key too ({F(swamLeft)} m in 1.5 s)");

            // ---- three short taps of Crouch (0.2 s each) ----
            yield return new WaitForSeconds(1f);
            rig.Refill();
            var tapBefore = player.GetStamina();
            var tapSeen = 0;
            var tapDeep = 0;
            for (var i = 0; i < 3; i++)
            {
                var seenTap = false;
                SelfTest.Key(crouchKey, true);
                var r0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - r0 < 0.2f)
                {
                    yield return FixedTick;
                    seenTap |= ZInput.GetButton("Crouch");
                    if (DiveState.Deep)
                    {
                        tapDeep++;
                    }
                }
                SelfTest.Key(crouchKey, false);
                if (seenTap)
                {
                    tapSeen++;
                }
                for (var k = 0; k < 50; k++)
                {
                    yield return FixedTick;
                    if (DiveState.Deep)
                    {
                        tapDeep++;
                    }
                }
            }
            yield return Ticks(25);
            var tapCost = tapBefore - player.GetStamina();
            c.Check(tapSeen == 3, $"three real Crouch taps of 0.2 s in the storm: the game read each one ({tapSeen} of 3)");
            c.Check(tapDeep == 0 && !DiveState.Diving,
                $"three real Crouch taps of 0.2 s in the storm: never held under a wave (deep {tapDeep} ticks, still diving 0.5 s "
                + $"after the last tap {DiveState.Diving})");
            c.Check(tapCost < 3f * 0.5f * drain + 0.5f,
                $"three real Crouch taps: they cost a little ({F(tapCost)} stamina; the swim drain is {F(drain)} per second)");

            // ---- for the record only (NOTE, no check): two longer taps, 0.35 s, on rising water. A dive that starts
            // on rising water keeps its upward speed for about 0.3 s (DiveLogic.DivedUnder), so a tap this long is the
            // edge of "a tap": the note says what it did. A dive that stays on is ended with the real Jump key ----
            var longDeep = 0;
            var longHeld = 0f;
            for (var i = 0; i < 2; i++)
            {
                var rise = 0f;
                var level = player.GetLiquidLevel();
                var waitEnd = Time.time + 1.5f;
                while (Time.time < waitEnd && rise < 0.3f)
                {
                    yield return Ticks(5);
                    var now = player.GetLiquidLevel();
                    rise = (now - level) / (5f * dt);
                    level = now;
                }
                SelfTest.Key(crouchKey, true);
                var r0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - r0 < 0.35f)
                {
                    yield return FixedTick;
                    if (DiveState.Deep)
                    {
                        longDeep++;
                    }
                }
                SelfTest.Key(crouchKey, false);
                var after = Time.time;
                while (DiveState.Diving && Time.time - after < 2f)
                {
                    yield return FixedTick;
                    if (DiveState.Deep)
                    {
                        longDeep++;
                    }
                }
                longHeld = Mathf.Max(longHeld, Time.time - after);
                if (DiveState.Diving)
                {
                    SelfTest.Key(jumpKey, true);
                    var up = Time.time;
                    while (DiveState.Diving && Time.time - up < 6f)
                    {
                        yield return FixedTick;
                    }
                    SelfTest.Key(jumpKey, false);
                }
                yield return Ticks(25);
            }
            c.Note($"for the record, two real Crouch taps of 0.35 s on rising water in the storm: counted as deep for {longDeep} "
                   + $"ticks, the dive went on for at most {F(longHeld)} s after the key was let go (2 s = it was ended with Jump)");

            // ---- Crouch alone, held: still a dive. Two seconds on flat water; a wave that falls with the diver can
            // hide the first metre (the depth is counted from the moving surface), so held until 1.5 m under it and
            // deep for half a second, 4 s at most ----
            rig.Refill();
            var deepAlone = 0;
            var aloneTicks = 0;
            var seenAlone = false;
            SelfTest.Key(crouchKey, true);
            while (aloneTicks < 200 && !(deepAlone >= 25 && Under(player) >= 1.5f))
            {
                yield return FixedTick;
                aloneTicks++;
                seenAlone |= ZInput.GetButton("Crouch");
                deepAlone = DiveState.Diving && DiveState.Deep ? deepAlone + 1 : 0;
            }
            var divingAlone = DiveState.Diving;
            var underAlone = Under(player);
            SelfTest.Key(crouchKey, false);
            c.Check(seenAlone && divingAlone && deepAlone >= 25 && underAlone >= 1.5f,
                $"the real Crouch key alone held in the storm: dives ({F(underAlone)} m under the rest height after "
                + $"{F(aloneTicks * dt)} s, diving {divingAlone}, deep for the last {deepAlone} ticks)");
            // Back up with the real Jump key.
            SelfTest.Key(jumpKey, true);
            var t0 = Time.time;
            while (DiveState.Diving && Time.time - t0 < 8f)
            {
                yield return FixedTick;
            }
            SelfTest.Key(jumpKey, false);
            c.Check(!DiveState.Diving, $"the real Jump key held: back at the surface in {F(Time.time - t0)} s");
            rig.GiveStaminaRateBack();
            env.m_debugEnv = envBefore;
            SelfTest.ReleaseInput();

            yield return GoHome(s);
            CheckLog(s);
        }
        finally
        {
            SelfTest.ReleaseInput();
            rig.Restore();
        }
        c.Report();
    }
}
#endif
