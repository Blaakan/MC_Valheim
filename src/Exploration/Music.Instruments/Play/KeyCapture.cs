using System;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MC.Exploration.MusicInstrumentsMod;

// Me = "our screen own the keys" flag. Up while the song window or the mini-game run (Hold every frame). While up,
// Chat.HasFocus postfix say "typing": every vanilla gate that ask it stop (Player.TakeInput: hotbar, use, hide, sit,
// power...; PlayerController.TakeInput: walk, attack, block, jump, mouse look; HotkeyBar, InventoryGui, Minimap, camera
// zoom). Menu.Update not ask it: Esc still open the menu. Console binds held back too (Terminal.TryRunCommand prefix).
// "Frame + 1" like Compendium's FocusGuard: the frame we let go is still covered whatever the script order.
internal static class KeyCapture
{
    private static int _untilFrame = -1;

    internal static bool Active => Time.frameCount <= _untilFrame;

    internal static void Hold() => _untilFrame = Time.frameCount + 1;

    internal static void Release() => _untilFrame = -1;
}

// Me = the four mini-game lane keys (personal settings). Each key checked once (bind, setting change, activation):
// ZInput throw every frame on a key it cannot map (F13...) and Mouse5/6 never fire, so a bad key = lane off, one
// warning (copy of Trinkets on Demand's check). Read with raw ZInput.GetKeyDown (exact frame, not the player's
// rebinds). No gamepad lanes in v1 (design doc, Later).
internal static class LaneKeys
{
    internal const int Count = 4;
    private static readonly KeyCode[] Keys = new KeyCode[Count];
    private static readonly KeyCode[] Warned = new KeyCode[Count];
    private static readonly string[] Labels = new string[Count];

#if DEBUG
    // Self test press a lane without a keyboard (next read). Consumed by Down.
    internal static readonly bool[] TestPress = new bool[Count];
#endif

    internal static void Cache()
    {
        Set(0, Plugin.Lane1Key);
        Set(1, Plugin.Lane2Key);
        Set(2, Plugin.Lane3Key);
        Set(3, Plugin.Lane4Key);
    }

    private static void Set(int lane, BepInEx.Configuration.ConfigEntry<KeyCode> entry)
    {
        var key = entry != null ? entry.Value : KeyCode.None;
        Keys[lane] = Validate(lane, key);
        Labels[lane] = null;
    }

    internal static KeyCode Key(int lane) => lane >= 0 && lane < Count ? Keys[lane] : KeyCode.None;

    // Lane key went down this frame. Never while a text field (chat, console, another mod's search) has the keyboard.
    internal static bool Down(int lane)
    {
#if DEBUG
        if (TestPress[lane])
        {
            TestPress[lane] = false;
            return true;
        }
#endif
        var key = Keys[lane];
        return key != KeyCode.None && ZInput.GetKeyDown(key, false);
    }

    // "[D]" for the HUD (built once per key change).
    internal static string Label(int lane)
    {
        if (lane < 0 || lane >= Count)
        {
            return "";
        }
        if (Labels[lane] != null)
        {
            return Labels[lane];
        }
        var key = Keys[lane];
        string name = null;
        if (key != KeyCode.None)
        {
            try
            {
                name = ZInput.KeyCodeToDisplayName(key);
            }
            catch (Exception)
            {
                // Plain enum name below.
            }
            if (string.IsNullOrEmpty(name) || name.StartsWith("$", StringComparison.Ordinal))
            {
                name = key.ToString();
            }
        }
        Labels[lane] = name ?? "-";
        return Labels[lane];
    }

    private static KeyCode Validate(int lane, KeyCode key)
    {
        if (key == KeyCode.Escape || key == KeyCode.Mouse1)
        {
            if (Warned[lane] != key)
            {
                Warned[lane] = key;
                Log.Warning($"MiniGame.Lane{lane + 1}Key = {key}: this key stops the rhythm game (Esc opens the menu, the "
                            + $"right mouse button stops it), so lane {lane + 1} has no key. Pick another key.");
            }
            return KeyCode.None;
        }
        if (key == KeyCode.None || IsUsableKey(key))
        {
            return key;
        }
        if (Warned[lane] != key)
        {
            Warned[lane] = key;
            Log.Warning($"MiniGame.Lane{lane + 1}Key = {key}: the game cannot read this key, so lane {lane + 1} has no "
                        + "key. Pick another keyboard key.");
        }
        return KeyCode.None;
    }

    // ZInput static maps only: keyboard keys it can map, mouse buttons 0-4. No gamepad KeyCodes.
    internal static bool IsUsableKey(KeyCode k)
    {
        if (!ZInput.IsKeyCodeValid(k) || k >= KeyCode.JoystickButton0)
        {
            return false;
        }
        if (k >= KeyCode.Mouse0 && k <= KeyCode.Mouse4)
        {
            return true;
        }
        return ZInput.TryKeyCodeToKey(k, out _);
    }
}

// Me = "a game screen or a text field is open" from the raw game state. Never Player.TakeInput() or
// Chat.HasFocus(): our own KeyCapture raise those. Research list (input-ui §2.5, §5).
internal static class GameScreens
{
    internal static bool AnyOpen()
    {
        if (Menu.IsVisible() || UnifiedPopup.IsVisible() || Feedback.IsVisible())
        {
            return true;
        }
        if (InventoryGui.IsVisible() || StoreGui.IsVisible() || Minimap.IsOpen())
        {
            return true;
        }
        if (Console.IsVisible() || TextInput.IsVisible())
        {
            return true;
        }
        if (TextViewer.instance != null && TextViewer.instance.IsVisible())
        {
            return true;
        }
        if (Hud.IsPieceSelectionVisible() || Hud.InRadial())
        {
            return true;
        }
        var hud = Hud.instance;
        if (hud != null && hud.m_buildUi != null && hud.m_buildUi.SearchFieldFocused)
        {
            return true;
        }
        if (PlayerCustomizaton.IsBarberGuiVisible() || GameCamera.InFreeFly())
        {
            return true;
        }
        return TextFieldHasKeyboard();
    }

    // Some text field own the keyboard: chat (real field), console, map pin name, any TMP input field, IMGUI field
    // (configuration manager window).
    internal static bool TextFieldHasKeyboard()
    {
        var chat = Chat.instance;
        if (chat != null && chat.m_input != null && (chat.m_input.isFocused || chat.m_wasFocused))
        {
            return true;
        }
        if (Minimap.InTextInput())
        {
            return true;
        }
        var system = EventSystem.current;
        var selected = system != null ? system.currentSelectedGameObject : null;
        if (selected != null && selected.TryGetComponent<TMP_InputField>(out var field) && field.isFocused)
        {
            return true;
        }
        return GUIUtility.keyboardControl != 0;
    }
}
