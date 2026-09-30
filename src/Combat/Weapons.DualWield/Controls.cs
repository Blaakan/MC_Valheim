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
#endif

    // BindConfig, setting change, OnActivated. Static map: work before ZInput exist (BindConfig run early).
    internal static void CacheKeys()
    {
        _mainKey = Validate(Plugin.MainHandKey != null ? Plugin.MainHandKey.Value : KeyCode.None, "MainHandKey",
            ref _mainWarned, "holding it to equip a weapon in your main hand");
        _swapKey = Validate(Plugin.SwapHandsKey != null ? Plugin.SwapHandsKey.Value : KeyCode.None, "SwapHandsKey",
            ref _swapWarned, "swapping your weapons between your hands");
    }

    // Main-hand key held right now (equip request in progress).
    internal static bool MainHandHeld()
    {
#if DEBUG
        if (TestMainHandHeld.HasValue)
        {
            return TestMainHandHeld.Value;
        }
#endif
        return _mainKey != KeyCode.None && ZInput.GetKey(_mainKey, false);
    }

    // Swap key went down this frame.
    internal static bool SwapPressed() => _swapKey != KeyCode.None && ZInput.GetKeyDown(_swapKey, false);

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
            Log.Warning(IsGamepadButton(key)
                ? $"Controls.{setting} = {key}: gamepad buttons are not supported yet, so {what} is off. "
                  + "Pick a keyboard key or a mouse button."
                : $"Controls.{setting} = {key}: the game cannot read this key, so {what} is off. "
                  + "Pick another key (keyboard or mouse).");
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
