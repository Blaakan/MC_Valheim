using System;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.CompendiumEncyclopediaMod;

// Me live on the window root (only run while it is open) and do the per-frame work: search focus guard and its
// 0.1 s refresh, the blocker size, the gamepad (design 3.8, LT/RT top tabs too), the one-time layout dump. The catalog build does NOT run
// here (window gets SetActive(false) on close, which would kill a coroutine; it runs on the plugin object).
// No allocation per frame.
internal sealed class WindowController : MonoBehaviour
{
    private const float MoveDelay = 0.1f;
    private static readonly int VisibleParam = Animator.StringToHash("visible");

    private bool _wasFocused;
    private float _moveDelay;
    private bool _firstFrameDone;

    private void Update()
    {
        try
        {
            Tick();
        }
        catch (Exception e)
        {
            PatchGuard.Report($"{nameof(WindowController)}.{nameof(Update)}", e);
        }
    }

    private void Tick()
    {
        // Inventory gone by a path that skipped our Hide postfix: close too.
        var gui = InventoryGui.instance;
        if (gui == null || gui.m_animator == null || !gui.m_animator.GetBool(VisibleParam))
        {
            CompendiumWindow.Close(selectButton: false);
            return;
        }
        if (!_firstFrameDone)
        {
            _firstFrameDone = true;
            CompendiumWindow.AfterFirstFrame();
        }

        var frame = Time.frameCount;
        var field = CompendiumWindow.SearchField;
        var focused = field != null && field.isFocused;
        if (focused)
        {
            // Controller cursor user: B leave the field (like chat). Guard keep this B away from InventoryGui.
            if (ZInput.GetButtonDown("JoyButtonB"))
            {
                CompendiumWindow.DropSearchFocus();
                focused = false;
            }
            FocusGuard.FieldUntilFrame = frame + 1;
        }
        else if (_wasFocused)
        {
            // Focus just ended (Esc, Enter, click elsewhere): guard one more frame, deselect, apply now.
            FocusGuard.FieldUntilFrame = frame + 1;
            CompendiumWindow.ReleaseSearchSelection();
            CompendiumWindow.ApplySearchNow();
        }
        _wasFocused = focused;

        CompendiumWindow.TickSearch();
        CompendiumWindow.UpdateBlocker();
        if (!focused && ZInput.IsExclusiveGamepadActive())
        {
            Gamepad();
        }
    }

    // LT = Texts top tab (back to the vanilla dialog; RT = Encyclopedia, already shown); D-pad / left stick (> 0.1 =
    // down, like TextsDialog) move the selection with a 0.1 s repeat and a rumble; LB/RB change category tab; right
    // stick scroll the details. B is handled in the InventoryGui.Update prefix. Y is not used.
    private void Gamepad()
    {
        if (ZInput.GetButtonDown(TopTabs.LeftKey))
        {
            TopTabs.Select(TopTabs.Choice.Texts);
            return; // window closed now
        }
        if (ZInput.GetButtonDown(TopTabs.RightKey))
        {
            TopTabs.Select(TopTabs.Choice.Encyclopedia);
        }
        if (_moveDelay > 0f)
        {
            _moveDelay -= Time.unscaledDeltaTime;
        }
        else
        {
            var stick = ZInput.GetJoyLeftStickY();
            var down = ZInput.GetButtonDown("JoyDPadDown") || stick > 0.1f;
            var up = ZInput.GetButtonDown("JoyDPadUp") || stick < -0.1f;
            if (down || up)
            {
                if (CompendiumWindow.MoveSelection(down ? 1 : -1) && GamepadRumble.instance != null)
                {
                    GamepadRumble.instance.PlayGlobalSelectVibration();
                }
                _moveDelay = MoveDelay;
            }
        }
        if (ZInput.GetButtonDown("JoyTabLeft"))
        {
            CompendiumWindow.CycleTab(-1);
        }
        else if (ZInput.GetButtonDown("JoyTabRight"))
        {
            CompendiumWindow.CycleTab(1);
        }
        var right = ZInput.GetJoyRightStickY();
        if (right > 0.1f || right < -0.1f)
        {
            CompendiumWindow.ScrollDetails(right);
        }
    }

    // Window closed or destroyed: never leave the keyboard guard up.
    private void OnDisable()
    {
        try
        {
            if (_wasFocused)
            {
                FocusGuard.FieldUntilFrame = Time.frameCount + 1;
            }
            _wasFocused = false;
            _moveDelay = 0f;
        }
        catch (Exception e)
        {
            PatchGuard.Report($"{nameof(WindowController)}.{nameof(OnDisable)}", e);
        }
    }
}
