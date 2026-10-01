using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.TrinketsOnDemandMod;

// Gamepad button held with the trigger button (None = trigger button alone).
internal enum GamepadModifier
{
    None,
    LeftTrigger,
    LeftBumper,
    RightBumper,
    RightTrigger,
}

// Gamepad button that fires (None = no gamepad trigger).
internal enum GamepadButton
{
    None,
    RightStick,
    LeftStick,
    DPadUp,
    DPadDown,
    DPadLeft,
    DPadRight,
    ButtonA,
    ButtonB,
    ButtonX,
    ButtonY,
    Back,
}

// Me = the personal trigger input: keyboard/mouse key (TriggerKey) and gamepad combination (GamepadModifier held +
// GamepadButton pressed). Gamepad read through the layout-independent physical ZInput names (ZInput
// AddGenericGamepadButtons: JoyLTrigger, JoyRStick...), so same physical buttons in every vanilla layout.
// Me check keyboard key once (bind, setting change, OnActivated): ZInput throw every frame on keyboard key it no can
// map (F13...), and Mouse5/6 never fire. Bad key = key off, warning once (copy of Dual Wielding's check).
// After a gamepad fire me swallow the press for every vanilla action on that physical button (RS = hide weapons tap,
// radial hold, crouch in alternative layouts): ResetButtonStatus, like vanilla InventoryGui do.
internal static class Controls
{
    private static KeyCode _key = KeyCode.None;
    private static KeyCode _warned = KeyCode.None;
    private static string _padModifier;   // ZInput name or null (button alone)
    private static string _padButton;     // ZInput name or null (no gamepad trigger)

    internal static KeyCode Key => _key;
    internal static string PadModifierName => _padModifier;
    internal static string PadButtonName => _padButton;

#if DEBUG
    // Self test press the trigger once without a keyboard (next Player.Update). Consumed by Pressed.
    internal static bool TestPress;
#endif

    // BindConfig, setting change, OnActivated. Static map: work before ZInput exist (BindConfig run early).
    internal static void CacheKeys()
    {
        _key = Validate(Plugin.TriggerKey != null ? Plugin.TriggerKey.Value : KeyCode.None);
        _padModifier = ModifierName(Plugin.GamepadModifier != null ? Plugin.GamepadModifier.Value : GamepadModifier.None);
        _padButton = ButtonName(Plugin.GamepadButton != null ? Plugin.GamepadButton.Value : GamepadButton.None);
    }

    // Trigger input went down this frame (key, or gamepad combination while the gamepad is in use). Every frame: plain
    // field reads first, no alloc.
    internal static bool Pressed()
    {
#if DEBUG
        if (TestPress)
        {
            TestPress = false;
            return true;
        }
#endif
        if (_key != KeyCode.None && ZInput.GetKeyDown(_key, false))
        {
            return true;
        }
        return PadPressed();
    }

    // Gamepad combination went down this frame: modifier held (or none), button pressed now.
    internal static bool PadPressed()
    {
        if (_padButton == null || !ZInput.IsGamepadActive())
        {
            return false;
        }
        if (_padModifier != null && !ZInput.GetButton(_padModifier))
        {
            return false;
        }
        return ZInput.GetButtonDown(_padButton);
    }

    // After a gamepad fire: every vanilla action bound to the same physical button forget this press (no hide tap on
    // release, no radial on hold, no crouch toggle). Modifier stays held (LT = block: must keep working).
    internal static void SwallowPadButton()
    {
        if (_padButton == null || ZInput.instance == null)
        {
            return;
        }
        foreach (var def in SameButton(_padButton))
        {
            def.ResetState();
        }
    }

    // Every ZInput button (vanilla actions and the physical name itself) bound to the same physical button as the
    // named one. Alloc: only on a fire (and self test).
    internal static List<ZInput.ButtonDef> SameButton(string zinputName)
    {
        var list = new List<ZInput.ButtonDef>();
        var zinput = ZInput.instance;
        var own = zinput != null ? zinput.GetButtonDef(zinputName) : null;
        if (own == null)
        {
            return list;
        }
        var path = EffectivePath(own);
        if (string.IsNullOrEmpty(path))
        {
            list.Add(own);
            return list;
        }
        foreach (var def in zinput.m_buttons.Values)
        {
            if (def != null && string.Equals(EffectivePath(def), path, StringComparison.Ordinal))
            {
                list.Add(def);
            }
        }
        return list;
    }

    // First binding path after player swaps (SwapTriggers/SwapFaceButtons override it), or null.
    private static string EffectivePath(ZInput.ButtonDef def)
    {
        if (def == null || def.ButtonAction == null)
        {
            return null;
        }
        var bindings = def.ButtonAction.bindings;
        return bindings.Count > 0 ? bindings[0].effectivePath : null;
    }

    // Label for messages and tooltip: the input the player uses now (gamepad while it is active), else the other one.
    // Null = no input set at all. Built on events only (full bar, tooltip, press), never per frame.
    internal static string Label()
    {
        var pad = PadLabel();
        var key = KeyLabel(_key);
        if (ZInput.IsGamepadActive())
        {
            return pad ?? key;
        }
        return key ?? pad;
    }

    internal static string KeyLabel(KeyCode key)
    {
        if (key == KeyCode.None)
        {
            return null;
        }
        string token;
        switch (key)
        {
            case KeyCode.LeftShift: token = "$button_lshift"; break;
            case KeyCode.RightShift: token = "$button_rshift"; break;
            case KeyCode.LeftControl: token = "$button_lctrl"; break;
            case KeyCode.RightControl: token = "$button_rctrl"; break;
            case KeyCode.LeftAlt: token = "$button_lalt"; break;
            case KeyCode.RightAlt: token = "$button_ralt"; break;
            case KeyCode.Space: token = "$button_space"; break;
            case KeyCode.Mouse0: token = "$button_mouse0"; break;
            case KeyCode.Mouse1: token = "$button_mouse1"; break;
            case KeyCode.Mouse2: token = "$button_mouse2"; break;
            default: token = null; break;
        }
        string name = null;
        try
        {
            name = token != null && Localization.instance != null
                ? Localization.instance.Localize(token)
                : ZInput.KeyCodeToDisplayName(key);
        }
        catch (Exception)
        {
            // Unknown display name: plain enum name below.
        }
        if (string.IsNullOrEmpty(name) || name.StartsWith("$", StringComparison.Ordinal))
        {
            name = key.ToString();
        }
        return "[" + name + "]";
    }

    // "LT + RS" as the game draws gamepad buttons (glyph sprites), plain names when the game has no glyph for it.
    internal static string PadLabel()
    {
        if (_padButton == null)
        {
            return null;
        }
        var button = Glyph(_padButton, PlainName(Plugin.GamepadButton != null ? Plugin.GamepadButton.Value : GamepadButton.None));
        if (_padModifier == null)
        {
            return button;
        }
        var modifier = Glyph(_padModifier,
            PlainName(Plugin.GamepadModifier != null ? Plugin.GamepadModifier.Value : GamepadModifier.None));
        return modifier + " + " + button;
    }

    private static string Glyph(string zinputName, string plain)
    {
        try
        {
            var zinput = ZInput.instance;
            var text = zinput != null ? zinput.GetBoundKeyString(zinputName, emptyStringOnMissing: true) : null;
            return string.IsNullOrEmpty(text) ? plain : text;
        }
        catch (Exception)
        {
            return plain; // no sprite for this button in the glyph map
        }
    }

    // Pure (self test): enum -> physical ZInput name.
    internal static string ModifierName(GamepadModifier m)
    {
        switch (m)
        {
            case GamepadModifier.LeftTrigger: return "JoyLTrigger";
            case GamepadModifier.LeftBumper: return "JoyLBumper";
            case GamepadModifier.RightBumper: return "JoyRBumper";
            case GamepadModifier.RightTrigger: return "JoyRTrigger";
            default: return null;
        }
    }

    internal static string ButtonName(GamepadButton b)
    {
        switch (b)
        {
            case GamepadButton.RightStick: return "JoyRStick";
            case GamepadButton.LeftStick: return "JoyLStick";
            case GamepadButton.DPadUp: return "JoyDPadUp";
            case GamepadButton.DPadDown: return "JoyDPadDown";
            case GamepadButton.DPadLeft: return "JoyDPadLeft";
            case GamepadButton.DPadRight: return "JoyDPadRight";
            case GamepadButton.ButtonA: return "JoyButtonA";
            case GamepadButton.ButtonB: return "JoyButtonB";
            case GamepadButton.ButtonX: return "JoyButtonX";
            case GamepadButton.ButtonY: return "JoyButtonY";
            case GamepadButton.Back: return "JoyBack";
            default: return null;
        }
    }

    private static string PlainName(GamepadModifier m)
    {
        switch (m)
        {
            case GamepadModifier.LeftTrigger: return "LT";
            case GamepadModifier.LeftBumper: return "LB";
            case GamepadModifier.RightBumper: return "RB";
            case GamepadModifier.RightTrigger: return "RT";
            default: return "";
        }
    }

    private static string PlainName(GamepadButton b)
    {
        switch (b)
        {
            case GamepadButton.RightStick: return "RS";
            case GamepadButton.LeftStick: return "LS";
            case GamepadButton.DPadUp: return "D-pad up";
            case GamepadButton.DPadDown: return "D-pad down";
            case GamepadButton.DPadLeft: return "D-pad left";
            case GamepadButton.DPadRight: return "D-pad right";
            case GamepadButton.Back: return "Back";
            default: return b.ToString().Replace("Button", "");
        }
    }

    private static KeyCode Validate(KeyCode key)
    {
        if (key == KeyCode.None || IsUsableKey(key))
        {
            return key;
        }
        if (_warned != key)
        {
            _warned = key;
            Log.Warning(IsGamepadButton(key)
                ? $"Controls.TriggerKey = {key}: gamepad buttons go in GamepadModifier and GamepadButton, so the "
                  + "keyboard trigger is off. Pick a keyboard key or a mouse button."
                : $"Controls.TriggerKey = {key}: the game cannot read this key, so the keyboard trigger is off. "
                  + "Pick another key (keyboard or mouse).");
        }
        return KeyCode.None;
    }

    // Every KeyCode from JoystickButton0 up = gamepad button (any pad number).
    private static bool IsGamepadButton(KeyCode k) => k >= KeyCode.JoystickButton0;

    // Pure-ish (self test): ZInput static maps only.
    internal static bool IsUsableKey(KeyCode k)
    {
        if (!ZInput.IsKeyCodeValid(k))
        {
            return false; // None, Mouse5, Mouse6, Joystick1Button0 and up
        }
        if (k >= KeyCode.Mouse0 && k <= KeyCode.Mouse4)
        {
            return true;
        }
        if (IsGamepadButton(k))
        {
            return false;
        }
        return ZInput.TryKeyCodeToKey(k, out _);
    }
}
