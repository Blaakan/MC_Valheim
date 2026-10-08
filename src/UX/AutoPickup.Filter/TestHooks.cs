#if DEBUG
using BepInEx.Configuration;
using UnityEngine;

namespace MC.UX.AutoPickupFilterMod;

// Debug build only (file gone in Release). Self tests (SelfTests.cs) force a setting or play an input here, in memory:
// config file never written, no real key or controller needed. Mod code read each one through ONE accessor
// (settings: Plugin.*On / *Now / *Text; input: FilterUi.Pad*, Pointer, HoveredSlot, mark key read). Null / false =
// real setting, real input. Clear() put everything back.
internal static class TestHooks
{
    // ---------------------------------------------------------------- settings (null = config value)
    internal static bool? ExemptHarvest;
    internal static bool? GamepadControls;
    internal static bool? ShowMarkers;
    internal static bool? ShowInHoverText;
    internal static float? ButtonOffsetX;
    internal static float? ButtonOffsetY;
    internal static string DefaultIgnored;
    internal static string DefaultSelected;
    internal static KeyboardShortcut? MarkKey;

    // ---------------------------------------------------------------- feature toggle
    // True = Plugin.LocalBlocker say "off": framework remove patches like the MC Mods panel do (after RefreshAll).
    internal static bool ForceOff;

    // ---------------------------------------------------------------- input
    // Mouse pointer on screen (pixels). Null = real pointer.
    internal static Vector2? Pointer;

    // Controller in use / controller alone in use. Null = ask ZInput.
    internal static bool? PadActive;
    internal static bool? PadOnly;

    // Held controller buttons.
    internal static bool LeftTrigger;
    internal static bool RightTrigger;

    // One-frame presses (like GetKeyDown / GetButtonDown: true during one frame only, read or not).
    private static int _markFrame = -1;
    private static int _stickFrame = -1;

    // Test coroutine run after this frame's Update: press land in next frame's inventory Update.
    internal static void PressMark() => _markFrame = Time.frameCount + 1;

    internal static void PressStick() => _stickFrame = Time.frameCount + 1;

    internal static bool MarkPressedNow => _markFrame == Time.frameCount;

    internal static bool StickPressedNow => _stickFrame == Time.frameCount;

    internal static void ClearInput()
    {
        Pointer = null;
        PadActive = null;
        PadOnly = null;
        LeftTrigger = false;
        RightTrigger = false;
        _markFrame = -1;
        _stickFrame = -1;
    }

    internal static void ClearSettings()
    {
        ExemptHarvest = null;
        GamepadControls = null;
        ShowMarkers = null;
        ShowInHoverText = null;
        ButtonOffsetX = null;
        ButtonOffsetY = null;
        DefaultIgnored = null;
        DefaultSelected = null;
        MarkKey = null;
    }
}
#endif
