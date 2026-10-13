#if DEBUG
using System;
using System.Collections;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.TrinketsOnDemandMod;

// Debug build only. Self tests with REAL input (same class as SelfTests.cs): probe change state of the real keyboard
// and of a test gamepad (SelfTest.Key / Pad...), so ZInput, the game and every mod read a press like from a person.
// One small test per TESTING.md item: real input is new, one test can be fixed or dropped alone.
//   trinkets.input.keys         T14: real Y fires with the default key; TriggerKey = U: real U fires, real Y does nothing
//   trinkets.input.pad-default  T12: default gamepad layout. Left trigger held (shield up) + right stick fires; trigger
//                               let go first = weapons stay out; stick held on = no radial menu; stick alone still puts
//                               weapons away (tap) and opens the radial menu (hold); texts name the gamepad pair while
//                               the gamepad is in use (text compared; picture "gamepad-message" saved for a person:
//                               how the buttons are drawn is not for a test to say)
//   trinkets.input.pad-layouts  T13: Alternative 1 and 2 layouts: left trigger + right stick, then right bumper +
//                               d-pad right: the trinket fires; NOTE lines say what else the game did with the press
//   trinkets.input.dual-wield   X01: two KnifeCopper dual wielded: real swap key of Dual Wielding swaps the hands and
//                               fires nothing, real Y fires and swaps nothing
//   trinkets.input.music        X09: real free play of Music Instruments: real Y (and U with TriggerKey = U) plays its
//                               note and the trinket stays; right click stops playing; then Y fires again
// Me force keys and gamepad pair only in memory (Controls.TestKey...), gamepad layout only in memory too (no saved
// preference touched). Always SelfTest.ReleaseInput in finally.
internal static partial class SelfTests
{
    private const string InputKeysName = "trinkets.input.keys";
    private const string InputPadName = "trinkets.input.pad-default";
    private const string InputLayoutsName = "trinkets.input.pad-layouts";
    private const string InputDualName = "trinkets.input.dual-wield";
    private const string InputMusicName = "trinkets.input.music";

    private const string FluteName = "MC_Flute"; // Music Instruments' item (its InstrumentContent)

    // ---------- helpers ----------

    // Probe's input driver there? (Every input call say false when no probe drive the game.)
    private static bool InputReady(Checks c) =>
        c.Check(SelfTest.ReleaseInput() && ZInput.instance != null,
            "no input driver: real key and gamepad presses need a run of tools/Test-InWorld.ps1");

    // Bar full, effect gone, center message marked, last answer forgotten: the next press tells all.
    private static void ArmFull(Rig rig, int hash, float max)
    {
        rig.Effect(hash);
        rig.P.m_adrenaline = max;
        rig.MarkCenter();
        FullBar.LastResult = TriggerResult.None;
    }

    private static bool PressFired(Rig rig, int hash) =>
        FullBar.LastResult == TriggerResult.Fired && rig.Has(hash) && Near(rig.P.m_adrenaline, 0f);

    // Mod never saw a press: no answer, bar still full, no effect, no message.
    private static bool PressUnseen(Rig rig, int hash, float max) =>
        FullBar.LastResult == TriggerResult.None && Near(rig.P.m_adrenaline, max) && !rig.Has(hash) && Rig.Center() == Rig.NoMessage;

    private static string PressText(Rig rig, int hash, float max) =>
        $"answer {FullBar.LastResult}, bar {F(rig.P.m_adrenaline)} of {F(max)}, effect {rig.Has(hash)}, message '{Rig.Center()}'";

    // Real tap of keyboard key or mouse button: down, 0.15 s, up, few frames. seen[0] = ZInput read it as held
    // meanwhile (press reached the game: a "nothing happened" check after it means something).
    private static IEnumerator TapKey(KeyCode key, bool[] seen)
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

    // Hold control of the test gamepad until game read it and look at the gamepad (first input of a new pad can come
    // in a frame where game still look at the mouse: let go, press again).
    private static IEnumerator HoldPad(string control, string zinputName)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            SelfTest.Pad(control, 1f);
            yield return Until(() => ZInput.IsGamepadActive() && ZInput.GetButton(zinputName), 0.7f);
            if (ZInput.IsGamepadActive() && ZInput.GetButton(zinputName))
            {
                yield break;
            }
            SelfTest.Pad(control, 0f);
            yield return Frames(4);
        }
    }

    // Press pad control (what is held already stays held) and wait for mod's answer to that press. No answer (game
    // looked at the mouse in that frame) = one more try. Control stays down after an answer, is up after none.
    private static IEnumerator PressPadForAnswer(string control)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            FullBar.LastResult = TriggerResult.None;
            SelfTest.Pad(control, 1f);
            yield return Until(() => FullBar.LastResult != TriggerResult.None, 0.6f);
            if (FullBar.LastResult != TriggerResult.None)
            {
                yield break;
            }
            SelfTest.Pad(control, 0f);
            yield return Frames(4);
        }
    }

    // Test gamepad there and in use before a press that must be quick: right stick pushed up for a moment (view goes
    // up a little, Rig puts it back). No game action hangs on that push outside the chat window.
    private static IEnumerator WakePad()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            SelfTest.PadStick(true, new Vector2(0f, 0.9f));
            yield return Until(() => ZInput.IsGamepadActive() && ZInput.GetButton("JoyRStickUp"), 0.7f);
            var awake = ZInput.IsGamepadActive() && ZInput.GetButton("JoyRStickUp");
            SelfTest.PadStick(true, Vector2.zero);
            yield return Frames(4);
            if (awake)
            {
                yield break;
            }
        }
    }

    // Gamepad layout in memory, the way Settings > Controls switch it (ZInput.ChangeInputLayout) minus the saved
    // preference: test never write player's settings. Nothing may be held on the pad meanwhile (buttons are made new).
    private static void SetPadLayout(InputLayout layout)
    {
        ZInput.InputLayout = layout;
        ZInput.instance.UpdateGamepadInputLayout();
    }

    private static bool Holds(Player p, ItemDrop.ItemData right, ItemDrop.ItemData left) =>
        ReferenceEquals(p.m_rightItem, right) && ReferenceEquals(p.m_leftItem, left) && p.m_hiddenRightItem == null
        && p.m_hiddenLeftItem == null;

    private static string HandsText(Player p) =>
        $"right {(p.m_rightItem != null ? p.m_rightItem.m_shared.m_name : "empty")}, left {(p.m_leftItem != null ? p.m_leftItem.m_shared.m_name : "empty")}, "
        + $"put away {(p.m_hiddenRightItem != null || p.m_hiddenLeftItem != null)}";

    // Top-left message line holds this text and no copy of it still waits in the queue: the message just asked for
    // is the one on screen (same text left over from an earlier test, faded out, does not count while new one waits).
    private static bool TopLeftShows(string text)
    {
        var hud = MessageHud.instance;
        if (hud == null || hud.m_messageText == null || hud.m_messageText.text == null || string.IsNullOrEmpty(text))
        {
            return false;
        }
        var want = Localization.instance != null ? Localization.instance.Localize(text) : text;
        foreach (var message in hud.m_msgQeue)
        {
            if (message != null && message.m_text == want)
            {
                return false;
            }
        }
        return hud.m_messageText.text.StartsWith(want, StringComparison.Ordinal);
    }

    private static void CloseRadial()
    {
        var radial = Hud.instance != null ? Hud.instance.m_radialMenu : null;
        if (radial != null && Hud.InRadial())
        {
            radial.Close(true);
        }
    }

    // Current value and default of a KeyCode setting of another plugin (BepInEx config, no type of theirs).
    private static bool KeySettingOf(string guid, string section, string key, out KeyCode value, out KeyCode byDefault)
    {
        value = KeyCode.None;
        byDefault = KeyCode.None;
        if (!Chainloader.PluginInfos.TryGetValue(guid, out var info) || info.Instance == null
            || !info.Instance.Config.TryGetEntry<KeyCode>(section, key, out var entry))
        {
            return false;
        }
        value = entry.Value;
        byDefault = (KeyCode)entry.DefaultValue;
        return true;
    }

    // ---------- trinkets.input.keys (T14) ----------

    private static IEnumerator RunInputKeys()
    {
        var rig = Rig.Create(InputKeysName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(InputKeysName);
        var mark = LogMark();
        try
        {
            var p = rig.P;
            if (!InputReady(c))
            {
                c.Report();
                yield break;
            }
            ServerRules.TestRules = new TrinketRules();
            Feedback.TestShowFullMessage = false; // texts are trinkets.keys' business
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            if (!c.Check(item != null, "could not equip a trinket"))
            {
                c.Report();
                yield break;
            }
            var hash = item.m_shared.m_fullAdrenalineSE.NameHash();
            rig.Effect(hash);
            var max = p.GetMaxAdrenaline();
            rig.Moves();
            var byDefault = Plugin.TriggerKey != null ? (KeyCode)Plugin.TriggerKey.DefaultValue : KeyCode.None;
            c.Check(byDefault == KeyCode.Y, $"the default trigger key is {byDefault} (TESTING.md T14 says Y)");
            if (!c.Check(p.TakeInput() && !Hud.InRadial() && !p.InPlaceMode(),
                    "the player cannot take input now (a window is open or build mode is on): a key press would be ignored"))
            {
                c.Report();
                yield break;
            }
            var seen = new bool[1];

            // Default setting: real Y fires.
            Controls.TestKey = KeyCode.Y;
            Controls.CacheKeys();
            ArmFull(rig, hash, max);
            yield return Frames(2);
            yield return TapKey(KeyCode.Y, seen);
            c.Check(seen[0], "the game did not read the real Y key press (input driver)");
            c.Check(PressFired(rig, hash), $"TriggerKey Y (default): a real Y key press did not fire the trinket ({PressText(rig, hash, max)})");

            // Real U does nothing while key is Y.
            ArmFull(rig, hash, max);
            yield return Frames(2);
            yield return TapKey(KeyCode.U, seen);
            c.Check(seen[0], "the game did not read the real U key press (input driver)");
            c.Check(PressUnseen(rig, hash, max), $"TriggerKey Y: a real U key press did something ({PressText(rig, hash, max)})");

            // TriggerKey = U (what a changed setting does: key cached again): Y does nothing, U fires.
            Controls.TestKey = KeyCode.U;
            Controls.CacheKeys();
            c.Check(Controls.Key == KeyCode.U, $"TriggerKey U: key in use is {Controls.Key}");
            ArmFull(rig, hash, max);
            yield return Frames(2);
            yield return TapKey(KeyCode.Y, seen);
            c.Check(seen[0] && PressUnseen(rig, hash, max),
                $"TriggerKey U: a real Y key press did something (key read {seen[0]}, {PressText(rig, hash, max)})");
            ArmFull(rig, hash, max);
            yield return Frames(2);
            yield return TapKey(KeyCode.U, seen);
            c.Check(seen[0] && PressFired(rig, hash),
                $"TriggerKey U: a real U key press did not fire the trinket (key read {seen[0]}, {PressText(rig, hash, max)})");
            var fired = LogCount(mark, TriggeredLine);
            c.Check(fired == 2, $"Debug line '{TriggeredLine}' logged {fired} time(s), expected 2 (one per key that fired)");
            CheckCleanLog(c, mark);
        }
        finally
        {
            SelfTest.ReleaseInput();
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.input.pad-default (T12) ----------

    private static IEnumerator RunInputPadDefault()
    {
        var rig = Rig.Create(InputPadName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(InputPadName);
        var mark = LogMark();
        var p = rig.P;
        var layoutBefore = ZInput.InputLayout;
        var layoutForced = false;
        var hiddenRight = p.m_hiddenRightItem;
        var hiddenLeft = p.m_hiddenLeftItem;
        try
        {
            if (!InputReady(c))
            {
                c.Report();
                yield break;
            }
            if (layoutBefore != InputLayout.Default)
            {
                SetPadLayout(InputLayout.Default);
                layoutForced = true;
                c.Note($"this player's gamepad layout is {layoutBefore}: the default layout is used for the test (in memory) and put back after");
            }
            ServerRules.TestRules = new TrinketRules();
            Feedback.TestShowFullMessage = true;
            // Me force pair below: must be same pair fresh install has, T12 talk about default settings.
            var modifierByDefault = Plugin.GamepadModifier != null ? (GamepadModifier)Plugin.GamepadModifier.DefaultValue : GamepadModifier.None;
            var buttonByDefault = Plugin.GamepadButton != null ? (GamepadButton)Plugin.GamepadButton.DefaultValue : GamepadButton.None;
            c.Check(modifierByDefault == GamepadModifier.LeftTrigger && buttonByDefault == GamepadButton.RightStick,
                $"the default gamepad trigger is {modifierByDefault} + {buttonByDefault} (TESTING.md T12 says left trigger + right stick)");
            Controls.TestKey = KeyCode.Y;
            Controls.TestPadModifier = GamepadModifier.LeftTrigger;
            Controls.TestPadButton = GamepadButton.RightStick;
            Controls.CacheKeys();
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            var sword = rig.Give(SwordName);
            var shield = rig.Give(ShieldName);
            if (!c.Check(item != null && sword != null && shield != null, $"could not equip a trinket or give {SwordName} and {ShieldName}"))
            {
                c.Report();
                yield break;
            }
            var hash = item.m_shared.m_fullAdrenalineSE.NameHash();
            rig.Effect(hash);
            var max = p.GetMaxAdrenaline();
            rig.EmptyHands();
            rig.Moves();
            c.Check(p.EquipItem(sword, false) && p.EquipItem(shield, false), $"could not hold {SwordName} and {ShieldName}");
            yield return FixedTicks(3);
            if (!c.Check(Holds(p, sword, shield) && p.TakeInput() && !Hud.InRadial() && !p.InPlaceMode(),
                    $"the player is not ready (hands: {HandsText(p)}; a window open or build mode on makes the game ignore the gamepad)"))
            {
                c.Report();
                yield break;
            }

            // Left trigger held: gamepad in use, shield goes up (left trigger = block in this layout).
            yield return HoldPad("LeftTrigger", "JoyLTrigger");
            if (!c.Check(ZInput.IsGamepadActive() && ZInput.GetButton("JoyLTrigger"),
                    $"the game did not read the test gamepad's left trigger (gamepad in use {ZInput.IsGamepadActive()})"))
            {
                c.Report();
                yield break;
            }
            yield return Until(() => p.IsBlocking(), 1.5f);
            c.Check(p.IsBlocking() && ReferenceEquals(p.GetCurrentBlocker(), shield), "left trigger held: the shield did not go up");

            // Gamepad in use: message and tooltip name the gamepad pair.
            var pad = Controls.PadLabel();
            var key = Controls.KeyLabel(Controls.Key);
            c.Check(pad != null && pad.Contains(" + ") && Controls.Label() == pad && pad != key,
                $"gamepad in use: the trigger is shown as '{Controls.Label()}' (gamepad pair '{pad}', key '{key}')");
            if (pad != null)
            {
                var tip = TipOf(item);
                c.Check(tip.Contains(TriggerLine(pad)) && !tip.Contains(TriggerLine("[Y]")), "gamepad in use: the tooltip line does not name the gamepad buttons");
                yield return FullEdge(p, max, true);
                c.Check(Feedback.LastFullMessage == FullMessage(pad) && TopLeftHas(FullMessage(pad)),
                    $"gamepad in use: full-bar message '{Feedback.LastFullMessage}', expected the gamepad buttons");
                // Me compare text only (game's sprite tags). How buttons are drawn = for a person: picture of the
                // message right after game put it on screen (full at once, then fade over 4 s; earlier message hold
                // it back about 1 s).
                c.Note($"gamepad pair as text: {pad}");
                yield return Until(() => TopLeftShows(FullMessage(pad)), 5f);
                if (TopLeftShows(FullMessage(pad)))
                {
                    yield return Frames(4);
                    SelfTest.Screenshot(InputPadName, "gamepad-message");
                    yield return null;
                    yield return null;
                }
                else
                {
                    c.Note("the full-bar message was still waiting behind another message after 5 s: no picture of the gamepad buttons taken");
                }
            }

            // Left trigger + right stick: fires. Trigger let go before the stick: weapons stay in the hands.
            ArmFull(rig, hash, max);
            yield return PressPadForAnswer("RightStick");
            var stickDownAt = Time.unscaledTime; // answer come in frame where game read press
            c.Check(PressFired(rig, hash), $"left trigger + right stick: the trinket did not fire ({PressText(rig, hash, max)}, gamepad in use {ZInput.IsGamepadActive()})");
            SelfTest.Pad("LeftTrigger", 0f);
            yield return Frames(3);
            SelfTest.Pad("RightStick", 0f);
            yield return null; // game read release in this frame
            var stickDownFor = Time.unscaledTime - stickDownAt;
            yield return Frames(7);
            // Game put weapons away only after press shorter than 0.33 s (Player.Update, JoyHide). Longer press leave
            // hands alone whatever me do: then check below prove nothing, so me say it.
            c.Check(stickDownFor < 0.3f,
                $"the right stick stayed down {F(stickDownFor)} s (frames too slow): the game only puts weapons away after a press under 0.33 s, "
                + "so this run cannot show that the trigger press leaves them in the hands");
            c.Check(Holds(p, sword, shield) && !Hud.InRadial(),
                $"left trigger let go before the right stick: weapons were put away or the radial menu opened ({HandsText(p)}, radial {Hud.InRadial()})");

            // Again, right stick held on for longer than the game's hold time (0.33 s): no radial menu.
            yield return HoldPad("LeftTrigger", "JoyLTrigger");
            ArmFull(rig, hash, max);
            yield return PressPadForAnswer("RightStick");
            c.Check(PressFired(rig, hash), $"left trigger + right stick, second time: the trinket did not fire ({PressText(rig, hash, max)})");
            SelfTest.Pad("LeftTrigger", 0f);
            var radial = false;
            for (var t = 0f; t < 0.9f; t += Time.unscaledDeltaTime)
            {
                radial |= Hud.InRadial();
                yield return null;
            }
            SelfTest.Pad("RightStick", 0f);
            yield return Frames(8);
            radial |= Hud.InRadial();
            c.Check(!radial, "right stick held on after the trigger fired: the radial menu opened");
            c.Check(Holds(p, sword, shield), $"right stick held on after the trigger fired: weapons were put away ({HandsText(p)})");
            CloseRadial();

            // Right stick alone, tap: weapons put away as in the normal game, trinket not fired.
            ArmFull(rig, hash, max);
            yield return SelfTest.PadTap("RightStick", 0.12f);
            yield return Until(() => p.m_rightItem == null && p.m_hiddenRightItem != null, 1.5f);
            c.Check(p.m_rightItem == null && p.m_leftItem == null && ReferenceEquals(p.m_hiddenRightItem, sword),
                $"right stick alone (tap): the weapons were not put away ({HandsText(p)})");
            c.Check(FullBar.LastResult == TriggerResult.None && Near(p.m_adrenaline, max) && !rig.Has(hash),
                $"right stick alone fired the trinket or answered ({PressText(rig, hash, max)})");
            // Tap again: weapons back out (and hands as before for the next step).
            yield return SelfTest.PadTap("RightStick", 0.12f);
            yield return Until(() => Holds(p, sword, shield), 2f);
            c.Check(Holds(p, sword, shield), $"right stick alone (second tap): the weapons did not come back out ({HandsText(p)})");

            // Right stick alone, held: radial menu opens as in the normal game.
            SelfTest.Pad("RightStick", 1f);
            yield return Until(() => Hud.InRadial(), 1.5f);
            var opened = Hud.InRadial();
            CloseRadial();
            SelfTest.Pad("RightStick", 0f);
            yield return Frames(8);
            c.Check(opened, "right stick alone (held): the radial menu did not open");
            c.Check(!Hud.InRadial() && Holds(p, sword, shield), $"after the radial menu: still open {Hud.InRadial()}, hands: {HandsText(p)}");
            // Bar still full from tap above: second tap and hold fire nothing too.
            c.Check(FullBar.LastResult == TriggerResult.None && Near(p.m_adrenaline, max) && !rig.Has(hash),
                $"right stick alone (second tap, hold) fired the trinket or answered ({PressText(rig, hash, max)})");

            // Gamepad gone: texts name the key again.
            SelfTest.ReleaseInput();
            yield return Until(() => !ZInput.IsGamepadActive(), 2f);
            c.Check(!ZInput.IsGamepadActive() && Controls.Label() == key,
                $"back on keyboard and mouse: gamepad in use {ZInput.IsGamepadActive()}, trigger shown as '{Controls.Label()}' (expected '{key}')");
            CheckCleanLog(c, mark);
        }
        finally
        {
            SelfTest.ReleaseInput();
            try
            {
                CloseRadial();
                if (layoutForced && ZInput.instance != null)
                {
                    SetPadLayout(layoutBefore);
                }
                // Hands hidden by my taps point at items Rig takes away: put back what was hidden before the test.
                p.m_hiddenRightItem = hiddenRight;
                p.m_hiddenLeftItem = hiddenLeft;
            }
            catch (Exception e)
            {
                Log.Warning($"Self test clean-up (gamepad) failed: {e}");
            }
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.input.pad-layouts (T13) ----------

    // One gamepad pair pressed on a full bar in the layout set now: modifier down, button down, button up, modifier
    // up, all within the game's 0.33 s hold time, like a quick thumb and finger. Check: trinket fires. NOTE: all else
    // the game did with that press (for the README's advice). Then player put back as before the press.
    private static IEnumerator PadPairCase(Checks c, Rig rig, int hash, float max, string what, string modifier, string modifierZinput,
        string button, ItemDrop.ItemData right, ItemDrop.ItemData left)
    {
        var p = rig.P;
        yield return Until(() => Calm(p) && Holds(p, right, left), 4f);
        // Hands like at start, nothing running. Else NOTE line below blame this press for what earlier press left
        // behind (weapons still put away, attack still on).
        c.Check(Calm(p) && Holds(p, right, left) && !Hud.InRadial(),
            $"{what}: the player was not ready before the press (hands: {HandsText(p)}, radial menu {Hud.InRadial()}): the note about what else the press did cannot be trusted");
        yield return WakePad();
        var crouch = p.m_crouchToggled;
        var altPlace = p.m_altPlace;
        ArmFull(rig, hash, max);
        yield return Frames(2);
        var blocked = false;
        var attacked = false;
        var radial = false;
        SelfTest.Pad(modifier, 1f);
        yield return Until(() => ZInput.GetButton(modifierZinput), 0.2f);
        yield return PressPadForAnswer(button);
        var fired = PressFired(rig, hash);
        var detail = PressText(rig, hash, max);
        var padActive = ZInput.IsGamepadActive();
        for (var frame = 0; frame < 4; frame++)
        {
            if (frame == 2)
            {
                SelfTest.Pad(button, 0f);
            }
            blocked |= p.m_blocking;
            attacked |= p.InAttack();
            radial |= Hud.InRadial();
            yield return null;
        }
        blocked |= p.m_blocking;
        SelfTest.Pad(modifier, 0f);
        for (var t = 0f; t < 1f; t += Time.unscaledDeltaTime)
        {
            attacked |= p.InAttack();
            radial |= Hud.InRadial();
            yield return null;
        }
        var putAway = p.m_rightItem == null && p.m_hiddenRightItem != null;
        var crouched = p.m_crouchToggled != crouch;
        var alt = p.m_altPlace != altPlace;
        c.Check(fired, $"{what}: the trinket did not fire ({detail}, gamepad in use {padActive})");
        c.Note($"{what}: fired {fired}; what else the press did: shield raised {blocked}, attack started {attacked}, weapons put away {putAway}, "
               + $"radial menu opened {radial}, crouch toggled {crouched}, alternative placement toggled {alt}");

        // As before the press.
        CloseRadial();
        if (crouched)
        {
            p.SetCrouch(crouch);
        }
        p.m_altPlace = altPlace;
        yield return Until(() => !p.InAttack(), 4f);
        if (putAway)
        {
            p.ShowHandItems();
        }
        yield return FixedTicks(3);
    }

    private static IEnumerator RunInputPadLayouts()
    {
        var rig = Rig.Create(InputLayoutsName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(InputLayoutsName);
        var mark = LogMark();
        var p = rig.P;
        var layoutBefore = ZInput.InputLayout;
        var layoutChanged = false;
        var hiddenRight = p.m_hiddenRightItem;
        var hiddenLeft = p.m_hiddenLeftItem;
        var crouchBefore = p.m_crouchToggled;
        var altPlaceBefore = p.m_altPlace;
        try
        {
            if (!InputReady(c))
            {
                c.Report();
                yield break;
            }
            ServerRules.TestRules = new TrinketRules();
            Feedback.TestShowFullMessage = false;
            Controls.TestKey = KeyCode.Y;
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            var sword = rig.Give(SwordName);
            var shield = rig.Give(ShieldName);
            if (!c.Check(item != null && sword != null && shield != null, $"could not equip a trinket or give {SwordName} and {ShieldName}"))
            {
                c.Report();
                yield break;
            }
            var hash = item.m_shared.m_fullAdrenalineSE.NameHash();
            rig.Effect(hash);
            var max = p.GetMaxAdrenaline();
            rig.EmptyHands();
            rig.Moves();
            c.Check(p.EquipItem(sword, false) && p.EquipItem(shield, false), $"could not hold {SwordName} and {ShieldName}");
            yield return FixedTicks(3);
            if (!c.Check(Holds(p, sword, shield) && p.TakeInput() && !Hud.InRadial() && !p.InPlaceMode(),
                    $"the player is not ready (hands: {HandsText(p)}; a window open or build mode on makes the game ignore the gamepad)"))
            {
                c.Report();
                yield break;
            }

            foreach (var layout in new[] { InputLayout.Alternative1, InputLayout.Alternative2 })
            {
                SelfTest.ReleaseInput(); // nothing held while the layout's buttons are made new
                yield return Frames(4);
                SetPadLayout(layout);
                layoutChanged = true;
                yield return Frames(2);
                c.Check(ZInput.InputLayout == layout && ZInput.instance.GetButtonDef("JoyRStick") != null,
                    $"the gamepad layout could not be set to {layout}");
                var same = string.Join(", ", Controls.SameButton("JoyRStick").ConvertAll(def => def.Name).ToArray());
                var sameMod = string.Join(", ", Controls.SameButton("JoyLTrigger").ConvertAll(def => def.Name).ToArray());
                c.Note($"{layout}: the right stick press is {same}; the left trigger is {sameMod}");

                // Default pair: left trigger + right stick.
                Controls.TestPadModifier = GamepadModifier.LeftTrigger;
                Controls.TestPadButton = GamepadButton.RightStick;
                Controls.CacheKeys();
                yield return PadPairCase(c, rig, hash, max, $"{layout}, left trigger + right stick", "LeftTrigger", "JoyLTrigger", "RightStick", sword, shield);

                // The pair TESTING.md T13 names: right bumper + d-pad right.
                Controls.TestPadModifier = GamepadModifier.RightBumper;
                Controls.TestPadButton = GamepadButton.DPadRight;
                Controls.CacheKeys();
                c.Check(Controls.PadModifierName == "JoyRBumper" && Controls.PadButtonName == "JoyDPadRight",
                    $"GamepadModifier RightBumper + GamepadButton DPadRight read as {Controls.PadModifierName} + {Controls.PadButtonName}");
                yield return PadPairCase(c, rig, hash, max, $"{layout}, right bumper + d-pad right", "RightShoulder", "JoyRBumper", "DpadRight", sword, shield);
            }
            CheckCleanLog(c, mark);
        }
        finally
        {
            SelfTest.ReleaseInput();
            try
            {
                CloseRadial();
                if (layoutChanged && ZInput.instance != null)
                {
                    SetPadLayout(layoutBefore);
                }
                if (p.m_crouchToggled != crouchBefore)
                {
                    p.SetCrouch(crouchBefore);
                }
                p.m_altPlace = altPlaceBefore;
                p.m_hiddenRightItem = hiddenRight;
                p.m_hiddenLeftItem = hiddenLeft;
            }
            catch (Exception e)
            {
                Log.Warning($"Self test clean-up (gamepad) failed: {e}");
            }
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.input.dual-wield (X01, real keys) ----------

    private static IEnumerator RunInputDualWield()
    {
        if (!ModActive(DualWieldGuid))
        {
            SelfTest.Fail(InputDualName, "Dual Wielding is not loaded and active: the real keys of TESTING.md X01 were not checked");
            yield break;
        }
        var rig = Rig.Create(InputDualName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(InputDualName);
        var mark = LogMark();
        try
        {
            var p = rig.P;
            if (!InputReady(c))
            {
                c.Report();
                yield break;
            }
            ServerRules.TestRules = new TrinketRules();
            Feedback.TestShowFullMessage = false;
            Controls.TestKey = Plugin.TriggerKey != null ? (KeyCode)Plugin.TriggerKey.DefaultValue : KeyCode.Y;
            Controls.CacheKeys();
            var trigger = Controls.Key;
            var known = KeySettingOf(DualWieldGuid, "Controls", "SwapHandsKey", out var swap, out var swapDefault);
            if (!c.Check(known && swap != KeyCode.None && trigger != KeyCode.None && swap != trigger,
                    $"Dual Wielding's swap key is {swap} (default {swapDefault}), the trinket trigger is {trigger}: they must be set and differ"))
            {
                c.Report();
                yield break;
            }
            if (swap != swapDefault)
            {
                c.Note($"this player's Dual Wielding swap key is {swap}, not the default {swapDefault}: {swap} is pressed");
            }
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            var first = rig.Give(KnifeName);
            var second = rig.Give(KnifeName);
            if (!c.Check(item != null && first != null && second != null, $"could not equip a trinket or give two {KnifeName}"))
            {
                c.Report();
                yield break;
            }
            var hash = item.m_shared.m_fullAdrenalineSE.NameHash();
            rig.Effect(hash);
            var max = p.GetMaxAdrenaline();
            rig.EmptyHands();
            rig.Moves();
            p.EquipItem(first, false);
            p.EquipItem(second, false);
            yield return new WaitForSeconds(0.6f);
            if (!c.Check(ReferenceEquals(p.m_rightItem, first) && ReferenceEquals(p.m_leftItem, second) && p.TakeInput(),
                    $"two {KnifeName} are not held one in each hand (Dual Wielding active), or the player cannot take input"))
            {
                c.Report();
                yield break;
            }
            var seen = new bool[1];

            // Real swap key on a full bar: hands swap (the game's equip time), trinket untouched.
            ArmFull(rig, hash, max);
            yield return Frames(2);
            yield return TapKey(swap, seen);
            yield return Until(() => ReferenceEquals(p.m_rightItem, second) && ReferenceEquals(p.m_leftItem, first), 5f);
            c.Check(seen[0], $"the game did not read the real {swap} key press (input driver)");
            c.Check(ReferenceEquals(p.m_rightItem, second) && ReferenceEquals(p.m_leftItem, first),
                $"a real {swap} key press did not swap the hands within 5 s");
            c.Check(PressUnseen(rig, hash, max), $"the swap key {swap} did something to the trinket ({PressText(rig, hash, max)})");

            // Real trigger key: fires, hands stay.
            yield return Until(() => p.GetActionQueueCount() == 0, 2f);
            var right = p.m_rightItem;
            var left = p.m_leftItem;
            ArmFull(rig, hash, max);
            yield return Frames(2);
            yield return TapKey(trigger, seen);
            c.Check(seen[0] && PressFired(rig, hash),
                $"a real {trigger} key press did not fire the trinket while dual wielding (key read {seen[0]}, {PressText(rig, hash, max)})");
            yield return new WaitForSeconds(2f); // a swap would be through by now
            c.Check(ReferenceEquals(p.m_rightItem, right) && ReferenceEquals(p.m_leftItem, left) && p.GetActionQueueCount() == 0,
                $"the trigger key {trigger} swapped the hands or queued something");
            CheckCleanLog(c, mark);
        }
        finally
        {
            SelfTest.ReleaseInput();
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.input.music (X09) ----------

    private static IEnumerator RunInputMusic()
    {
        if (!ModActive(MusicGuid))
        {
            SelfTest.Fail(InputMusicName, "Music Instruments is not loaded and active: nothing of TESTING.md X09 was checked");
            yield break;
        }
        var rig = Rig.Create(InputMusicName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(InputMusicName);
        var mark = LogMark();
        // Music Instruments through reflection only (its Debug counters and the calls its song window makes).
        var perf = AccessTools.TypeByName("MC.Exploration.MusicInstrumentsMod.Performance");
        MethodInfo start = perf != null ? AccessTools.Method(perf, "StartFreePlay") : null;
        MethodInfo stop = perf != null ? AccessTools.Method(perf, "Stop", new[] { typeof(string) }) : null;
        MethodInfo mode = perf != null ? AccessTools.PropertyGetter(perf, "Mode") : null;
        FieldInfo notes = perf != null ? AccessTools.Field(perf, "NoteOnsSent") : null;
        string Mode() => mode.Invoke(null, null).ToString();
        int Notes() => (int)notes.GetValue(null);
        try
        {
            var p = rig.P;
            if (!InputReady(c))
            {
                c.Report();
                yield break;
            }
            if (!c.Check(start != null && stop != null && mode != null && notes != null,
                    "Music Instruments is active but its free play (Performance.StartFreePlay, Stop, Mode, NoteOnsSent of a Debug build) was not found"))
            {
                c.Report();
                yield break;
            }
            ServerRules.TestRules = new TrinketRules();
            Feedback.TestShowFullMessage = false;
            Controls.TestKey = KeyCode.Y;
            Controls.CacheKeys();
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            var flute = PrefabShared(FluteName) != null ? rig.Give(FluteName) : null;
            if (!c.Check(item != null && flute != null, $"could not equip a trinket or give {FluteName} (Music Instruments' flute)"))
            {
                c.Report();
                yield break;
            }
            var hash = item.m_shared.m_fullAdrenalineSE.NameHash();
            rig.Effect(hash);
            var max = p.GetMaxAdrenaline();
            rig.EmptyHands();
            rig.Moves();
            p.EquipItem(flute, false);
            yield return new WaitForSeconds(0.6f);
            if (!c.Check(ReferenceEquals(p.GetRightItem(), flute) && p.TakeInput(), "the flute is not in the right hand, or the player cannot take input"))
            {
                c.Report();
                yield break;
            }

            // Free play, as the song window's first entry starts it.
            var args = new object[] { null };
            var started = (bool)start.Invoke(null, args);
            yield return Frames(4);
            if (!c.Check(started && Mode() == "FreePlay", $"free play did not start: {args[0] ?? "no reason given"} (mode {Mode()})"))
            {
                c.Report();
                yield break;
            }
            var seen = new bool[1];

            // The Y key of the upper octave: its note plays, the trinket stays, no answer on screen.
            ArmFull(rig, hash, max);
            var before = Notes();
            yield return TapKey(KeyCode.Y, seen);
            c.Check(seen[0], "the game did not read the real Y key press (input driver)");
            c.Check(Notes() == before + 1 && Mode() == "FreePlay", $"free play: the Y key played {Notes() - before} note(s), expected 1 (mode {Mode()})");
            c.Check(FullBar.LastResult != TriggerResult.Fired && Near(p.m_adrenaline, max) && !rig.Has(hash) && Rig.Center() == Rig.NoMessage,
                $"free play: the Y key reached the trinket ({PressText(rig, hash, max)})");

            // TriggerKey = U: same for the U key.
            Controls.TestKey = KeyCode.U;
            Controls.CacheKeys();
            ArmFull(rig, hash, max);
            before = Notes();
            yield return TapKey(KeyCode.U, seen);
            c.Check(seen[0] && Notes() == before + 1, $"free play: the U key played {Notes() - before} note(s), expected 1 (key read {seen[0]})");
            c.Check(FullBar.LastResult != TriggerResult.Fired && Near(p.m_adrenaline, max) && !rig.Has(hash) && Rig.Center() == Rig.NoMessage,
                $"free play, TriggerKey U: the U key reached the trinket ({PressText(rig, hash, max)})");

            // Stop playing the player's way: right mouse button.
            yield return TapKey(KeyCode.Mouse1, seen);
            yield return Until(() => Mode() == "None", 2f);
            c.Check(seen[0] && Mode() == "None", $"a real right click did not stop free play (click read {seen[0]}, mode {Mode()})");
            if (Mode() != "None")
            {
                stop.Invoke(null, new object[] { null });
            }
            yield return Until(() => p.TakeInput(), 2f);
            yield return Frames(5);

            // Not playing: Y fires again.
            Controls.TestKey = KeyCode.Y;
            Controls.CacheKeys();
            ArmFull(rig, hash, max);
            yield return Frames(2);
            before = Notes();
            yield return TapKey(KeyCode.Y, seen);
            c.Check(seen[0] && PressFired(rig, hash) && Notes() == before,
                $"after free play: a real Y key press did not fire the trinket (key read {seen[0]}, {PressText(rig, hash, max)}, notes played {Notes() - before})");
            CheckCleanLog(c, mark);
        }
        finally
        {
            SelfTest.ReleaseInput();
            try
            {
                if (stop != null && mode != null && Mode() != "None")
                {
                    stop.Invoke(null, new object[] { null });
                }
            }
            catch (Exception e)
            {
                Log.Warning($"Self test clean-up (free play) failed: {e}");
            }
            rig.Restore();
        }
        c.Report();
    }
}
#endif
