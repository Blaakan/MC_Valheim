using System;
using UnityEngine;

namespace MC.Crafting.StationsBatchFeedMod;

// Me = hint line "[L-Shift + E] Add item x5" put right below vanilla Use line of a covered spot.
// Hover text is asked every frame: one-slot cache (last hovered component -> spot), line rebuilt only when
// spot, input device, controller layout or settings change, or every RebuildInterval (catch key rebinds).
// Anchor = first "[<color=yellow><b>" (vanilla key line style), never key text: vanilla hover can come from
// Localize cache with the OLD key after a rebind, so text match would miss.
internal static class HoverHint
{
    private const string Anchor = "[<color=yellow><b>";
    private const float RebuildInterval = 0.5f;
    private const float BatchableInterval = 0.25f;

    private static Component _component;
    private static bool _covered;
    private static FeedTarget _target;
    private static bool _batchable;
    private static float _batchableAt;

    private static string _insert; // "\n" + line
    private static float _builtAt;
    private static bool _builtGamepad;
    private static InputLayout _builtLayout;
    private static int _builtVersion;
    private static int _version;

    // Settings changed: rebuild line next frame.
    internal static void Invalidate() => _version++;

    // Feature off: forget cached spot (may be destroyed later) and line.
    internal static void Clear()
    {
        _component = null;
        _covered = false;
        _target = default;
        _insert = null;
        _version++;
    }

    internal static void Append(Component instance, ref string text)
    {
        if (!Plugin.ShowHint.Value || string.IsNullOrEmpty(text))
        {
            return;
        }

        if (!ReferenceEquals(instance, _component))
        {
            _component = instance;
            _covered = FeedTarget.TryResolve(instance, out _target);
            _insert = null;
            _batchable = true;
            _batchableAt = -1000f;
        }
        if (!_covered)
        {
            return;
        }

        var now = Time.time;
        if (_target.Kind == FeedKind.CookFood && now - _batchableAt >= BatchableInterval)
        {
            // Finished item on station: E take it first, batch not possible, hint hidden. Check build strings: throttle.
            _batchableAt = now;
            _batchable = _target.IsLive && _target.IsBatchable();
        }
        if (!_batchable)
        {
            return;
        }

        var gamepad = ZInput.IsGamepadActive();
        if (_insert == null || gamepad != _builtGamepad || ZInput.InputLayout != _builtLayout || _builtVersion != _version
            || now - _builtAt >= RebuildInterval)
        {
            Build(gamepad, now);
        }

        var start = text.IndexOf(Anchor, StringComparison.Ordinal);
        if (start < 0)
        {
            return; // no Use line shown (no access, other mod restyled): no hint
        }
        var end = text.IndexOf('\n', start);
        text = end < 0 ? text + _insert : text.Insert(end, _insert);
    }

    private static void Build(bool gamepad, float now)
    {
        var loc = Localization.instance;
        _insert = "\n[<color=yellow><b>" + ModifierLabel(gamepad) + " + " + BoundKey("Use", gamepad) + "</b></color>] "
                  + loc.Localize(_target.ActionLabel()) + " x" + Plugin.Amount.Value;
        _builtAt = now;
        _builtGamepad = gamepad;
        _builtLayout = ZInput.InputLayout;
        _builtVersion = _version;
    }

    // Modifier as it really work: game binding (vanilla ItemStand rule: alt keys on alternative controller layouts),
    // or mod-only key on keyboard (only when the game can read it, else game binding too).
    private static string ModifierLabel(bool gamepad)
    {
        var key = Plugin.ModifierKey.Value;
        if (key == KeyCode.None || gamepad || !BatchFeeder.KeyUsable(key))
        {
            return BoundKey(ZInput.IsNonClassicFunctionality() && gamepad ? "AltKeys" : "AltPlace", gamepad);
        }
        return KeyLabel(key);
    }

    // Copy of Localization.Translate "$KEY_" rule, but live binding (skip Localize cache, which rebind no clear).
    private static string BoundKey(string name, bool gamepad)
    {
        var loc = Localization.instance;
        if (gamepad)
        {
            var joy = loc.GetBoundKeyString("Joy" + name, emptyStringOnMissing: true);
            if (joy.Length > 0)
            {
                return joy;
            }
        }
        return loc.GetBoundKeyString(name);
    }

    // Vanilla names for keys vanilla names ("L-Shift"...), else Input System display name.
    private static string KeyLabel(KeyCode key)
    {
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
            case KeyCode.Return: token = "$button_return"; break;
            case KeyCode.Mouse0: token = "$button_mouse0"; break;
            case KeyCode.Mouse1: token = "$button_mouse1"; break;
            case KeyCode.Mouse2: token = "$button_mouse2"; break;
            default: token = null; break;
        }
        if (token != null)
        {
            return Localization.instance.Localize(token);
        }
        var name = ZInput.KeyCodeToDisplayName(key);
        return string.IsNullOrEmpty(name) || name.StartsWith("$KeyCode", StringComparison.Ordinal) ? key.ToString() : name;
    }
}
