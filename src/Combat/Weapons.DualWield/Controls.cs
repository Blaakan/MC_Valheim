using MC.Shared;
using UnityEngine;

namespace MC.Combat.WeaponsDualWieldMod;

// Me = the two personal keys: MainHandKey (hold while equip = vanilla equip, no pair) and SwapHandsKey (press = swap
// hands). Keyboard (or mouse) only; gamepad later (design L5).
// Me check each key once (bind, setting change): ZInput throw every frame on keyboard key it no can map (F13, Hash,
// At...), and Mouse5/6 never fire. Bad key = that key off, warning once (copy of MC Loot Pickup Filter's check).
internal static class Controls
{
    private static KeyCode _mainKey = KeyCode.None;
    private static KeyCode _swapKey = KeyCode.None;
    private static KeyCode _mainWarned = KeyCode.None;
    private static KeyCode _swapWarned = KeyCode.None;

    // Usable key or None (off). Hot path read: plain fields.
    internal static KeyCode MainKey => _mainKey;
    internal static KeyCode SwapKey => _swapKey;

#if DEBUG
    // Self test hold the main-hand key without a keyboard. Null = real key.
    internal static bool? TestMainHandHeld;

    // Self test bind the two keys in memory (never the config file). Null = the player's setting. Test call CacheKeys
    // after it set or clear them.
    internal static KeyCode? TestMainKey;
    internal static KeyCode? TestSwapKey;

    // Self test keyboard: this key is held (main-hand key read), this key went down this frame (swap key read, one
    // shot: the read use it up). Null = real keyboard. Other key than the bound one = nothing, like a real keyboard.
    internal static KeyCode? TestKeyHeld;
    internal static KeyCode? TestKeyDown;

    // Self test read me: last key warning, and how many were logged this session.
    internal static string LastWarning;
    internal static int WarningCount;

    // Self test: "already warned about this key" forgotten, so the next bad key warn again (and after the test too).
    internal static void TestForgetWarned()
    {
        _mainWarned = KeyCode.None;
        _swapWarned = KeyCode.None;
    }
#endif

    // BindConfig, setting change, OnActivated. Static map: work before ZInput exist (BindConfig run early).
    internal static void CacheKeys()
    {
        var main = Plugin.MainHandKey != null ? Plugin.MainHandKey.Value : KeyCode.None;
        var swap = Plugin.SwapHandsKey != null ? Plugin.SwapHandsKey.Value : KeyCode.None;
#if DEBUG
        main = TestMainKey ?? main;
        swap = TestSwapKey ?? swap;
#endif
        _mainKey = Validate(main, "MainHandKey", ref _mainWarned, "holding it to equip a weapon in your main hand");
        _swapKey = Validate(swap, "SwapHandsKey", ref _swapWarned, "swapping your weapons between your hands");
    }

    // Main-hand key held right now (equip request in progress).
    internal static bool MainHandHeld()
    {
#if DEBUG
        if (TestMainHandHeld.HasValue)
        {
            return TestMainHandHeld.Value;
        }
        if (TestKeyHeld.HasValue)
        {
            return _mainKey != KeyCode.None && TestKeyHeld.Value == _mainKey;
        }
#endif
        return _mainKey != KeyCode.None && ZInput.GetKey(_mainKey, false);
    }

    // Swap key went down this frame.
    internal static bool SwapPressed()
    {
#if DEBUG
        if (TestKeyDown.HasValue)
        {
            var down = TestKeyDown.Value;
            TestKeyDown = null;
            return _swapKey != KeyCode.None && down == _swapKey;
        }
#endif
        return _swapKey != KeyCode.None && ZInput.GetKeyDown(_swapKey, false);
    }

    // Player-facing key name for messages ("Left Alt", "H"). None = null.
    internal static string KeyName(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.None:
                return null;
            case KeyCode.LeftAlt:
                return "Left Alt";
            case KeyCode.RightAlt:
                return "Right Alt";
            case KeyCode.LeftControl:
                return "Left Ctrl";
            case KeyCode.RightControl:
                return "Right Ctrl";
            case KeyCode.LeftShift:
                return "Left Shift";
            case KeyCode.RightShift:
                return "Right Shift";
            default:
                return key.ToString();
        }
    }

    private static KeyCode Validate(KeyCode key, string setting, ref KeyCode warned, string what)
    {
        if (key == KeyCode.None || IsUsableKey(key))
        {
            return key;
        }
        if (warned != key)
        {
            warned = key;
            var text = IsGamepadButton(key)
                ? $"Controls.{setting} = {key}: gamepad buttons are not supported yet, so {what} is off. "
                  + "Pick a keyboard key or a mouse button."
                : $"Controls.{setting} = {key}: the game cannot read this key, so {what} is off. "
                  + "Pick another key (keyboard or mouse).";
#if DEBUG
            LastWarning = text;
            WarningCount++;
#endif
            Log.Warning(text);
        }
        return KeyCode.None;
    }

    // Every KeyCode from JoystickButton0 up = gamepad button (any pad number).
    private static bool IsGamepadButton(KeyCode k) => k >= KeyCode.JoystickButton0;

    private static bool IsUsableKey(KeyCode k)
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
            return false; // gamepad buttons: me no support them yet (design L5)
        }
        return ZInput.TryKeyCodeToKey(k, out _);
    }
}
