#if DEBUG
using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace MC.Core.ProbeWorldMod;

// Real input for tests (SelfTest.Key / MouseWheel / Pad ...). Me change device state of the real keyboard and mouse
// and of one test gamepad through Unity InputSystem state events, same road a real press take: ZInput (GetKey,
// GetButton, sticks), InputActions and every other reader see it from next input update. No patch on game or mod.
// Each mod carry own SelfTest copy, so they reach me through AppDomain slot (SelfTest.InputSlot).
// Event built from device's current state has not the presses queued this same frame: me keep own list of what
// me hold per device and write all of it into every event.
internal static class InputDriver
{
    private const string PadName = "MC Test Pad";

    private static readonly Dictionary<InputControl, float> Held = new Dictionary<InputControl, float>();
    private static readonly Dictionary<InputControl, Vector2> HeldSticks = new Dictionary<InputControl, Vector2>();
    private static Gamepad _pad;
    private static bool _installed;

    internal static bool PadExists => _pad != null && _pad.added;

    internal static void Install()
    {
        if (_installed)
        {
            return;
        }
        _installed = true;
        try
        {
            // Game window often has no focus in a test run: default setting turn keyboard and mouse off then.
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        }
        catch (Exception e)
        {
            Log.Warning($"input driver: could not set background behavior ({e.Message}); input tests need the game window focused");
        }
        AppDomain.CurrentDomain.SetData(SelfTest.InputSlot, (Func<string, object[], bool>)Handle);
    }

    private static bool Handle(string op, object[] args)
    {
        try
        {
            switch (op)
            {
                case "key":
                    return KeyOp((KeyCode)args[0], (bool)args[1]);
                case "wheel":
                    return Once(Mouse.current != null ? Mouse.current.scroll : null, new Vector2(0f, (float)args[0] * 120f));
                case "move":
                    return Once(Mouse.current != null ? Mouse.current.delta : null, (Vector2)args[0]);
                case "pad":
                    return PadOp((string)args[0], (float)args[1]);
                case "stick":
                    return StickOp((bool)args[0], (Vector2)args[1]);
                case "release":
                    return Release();
                default:
                    Log.Warning($"input driver: unknown op '{op}'");
                    return false;
            }
        }
        catch (Exception e)
        {
            Log.Warning($"input driver: {op} failed: {e.Message}");
            return false;
        }
    }

    private static bool KeyOp(KeyCode key, bool down)
    {
        InputControl<float> control = null;
        if (key >= KeyCode.Mouse0 && key <= KeyCode.Mouse4)
        {
            // Game's own lookup give the LEFT button for a mouse key it does not know: me ask first.
            control = Mouse.current != null && ZInput.TryKeyCodeToMouseButton(key, out _) ? ZInput.KeyCodeToMouseButtonControl(key, false) : null;
        }
        else if (key < KeyCode.Mouse0 && Keyboard.current != null && ZInput.TryKeyCodeToKey(key, out var k) && k != UnityEngine.InputSystem.Key.None)
        {
            control = Keyboard.current[k];
        }
        if (control == null)
        {
            Log.Warning($"input driver: no keyboard or mouse control for KeyCode.{key}");
            return false;
        }
        return Set(control, down ? 1f : 0f);
    }

    private static bool PadOp(string name, float value)
    {
        var pad = EnsurePad();
        if (pad == null)
        {
            return false;
        }
        InputControl<float> control;
        switch ((name ?? "").ToLowerInvariant())
        {
            case "lefttrigger":
                control = pad.leftTrigger;
                break;
            case "righttrigger":
                control = pad.rightTrigger;
                break;
            default:
                if (!Enum.TryParse<GamepadButton>(name, true, out var button))
                {
                    Log.Warning($"input driver: unknown gamepad control '{name}'");
                    return false;
                }
                control = pad[button];
                value = value > 0.5f ? 1f : 0f;
                break;
        }
        return Set(control, Mathf.Clamp01(value));
    }

    private static bool StickOp(bool right, Vector2 value)
    {
        var pad = EnsurePad();
        if (pad == null)
        {
            return false;
        }
        var stick = right ? pad.rightStick : pad.leftStick;
        if (value == Vector2.zero)
        {
            HeldSticks.Remove(stick);
        }
        else
        {
            HeldSticks[stick] = Vector2.ClampMagnitude(value, 1f);
        }
        Send(stick.device, null, 0f, stick, value);
        return true;
    }

    private static Gamepad EnsurePad()
    {
        if (_pad == null || !_pad.added)
        {
            _pad = InputSystem.AddDevice<Gamepad>(PadName);
            Log.Info($"input driver: test gamepad added ('{_pad.name}')");
        }
        return _pad;
    }

    private static bool Set(InputControl<float> control, float value)
    {
        if (value > 0f)
        {
            Held[control] = value;
        }
        else
        {
            Held.Remove(control);
        }
        Send(control.device, control, value, null, Vector2.zero);
        return true;
    }

    // One-frame control (wheel, mouse movement): written once, never held.
    private static bool Once(InputControl<Vector2> control, Vector2 value)
    {
        if (control == null)
        {
            Log.Warning("input driver: no mouse");
            return false;
        }
        Send(control.device, null, 0f, control, value);
        return true;
    }

    // One state event for the device: its current state + everything me hold on it + this change.
    private static void Send(InputDevice device, InputControl<float> control, float value, InputControl<Vector2> vector, Vector2 vectorValue)
    {
        if (!device.enabled)
        {
            InputSystem.EnableDevice(device);
        }
        using (StateEvent.From(device, out var eventPtr))
        {
            foreach (var h in Held)
            {
                if (h.Key.device == device)
                {
                    ((InputControl<float>)h.Key).WriteValueIntoEvent(h.Value, eventPtr);
                }
            }
            foreach (var h in HeldSticks)
            {
                if (h.Key.device == device)
                {
                    ((InputControl<Vector2>)h.Key).WriteValueIntoEvent(h.Value, eventPtr);
                }
            }
            if (control != null)
            {
                control.WriteValueIntoEvent(value, eventPtr);
            }
            if (vector != null)
            {
                vector.WriteValueIntoEvent(vectorValue, eventPtr);
            }
            InputSystem.QueueEvent(eventPtr);
        }
    }

    // Everything up, test gamepad gone, one pixel of mouse movement so the game go back to keyboard and mouse.
    internal static bool Release()
    {
        var devices = new HashSet<InputDevice>();
        foreach (var h in Held)
        {
            devices.Add(h.Key.device);
        }
        foreach (var h in HeldSticks)
        {
            devices.Add(h.Key.device);
        }
        var floats = new List<InputControl>(Held.Keys);
        var sticks = new List<InputControl>(HeldSticks.Keys);
        Held.Clear();
        HeldSticks.Clear();
        foreach (var device in devices)
        {
            if (!device.added)
            {
                continue;
            }
            using (StateEvent.From(device, out var eventPtr))
            {
                foreach (var c in floats)
                {
                    if (c.device == device)
                    {
                        ((InputControl<float>)c).WriteValueIntoEvent(0f, eventPtr);
                    }
                }
                foreach (var c in sticks)
                {
                    if (c.device == device)
                    {
                        ((InputControl<Vector2>)c).WriteValueIntoEvent(Vector2.zero, eventPtr);
                    }
                }
                InputSystem.QueueEvent(eventPtr);
            }
        }
        if (_pad != null)
        {
            if (_pad.added)
            {
                InputSystem.RemoveDevice(_pad);
                Log.Info("input driver: test gamepad removed");
            }
            _pad = null;
            if (Mouse.current != null)
            {
                Once(Mouse.current.delta, new Vector2(1f, 0f));
            }
        }
        return true;
    }
}
#endif
