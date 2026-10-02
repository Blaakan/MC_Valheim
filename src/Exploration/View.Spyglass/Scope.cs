using MC.Shared;
using UnityEngine;

namespace MC.Exploration.ViewSpyglassMod;

internal enum ScopeState : byte
{
    Idle,     // spyglass down (or not held): vanilla
    Raising,  // arm coming up, camera sliding to the eye, zoom growing
    Raised,   // at the eye: zoomed round view, feet stay, slow aim
    Lowering, // back to normal; you may walk again already
}

// Me = the local player's spyglass state. Input come from Player.SetControls prefix (every physics tick, click
// edges already found by PlayerController), work happen in Player.Update postfix (Tick, every frame). Progress 0..1 =
// how far the spyglass is up; ScopeCamera, ScopeOverlay and ArmPose read it to place camera, overlay and arm.
// Everything lowers by itself when it no longer make sense (item gone, menu open, hit, swimming...): Tick check every
// frame, never trust events alone. Player ZDO bool Raised tell other games with me to pose this player's arm.
internal static class Scope
{
    internal const float MinMagnification = 1.5f;
    internal const float DefaultStartMagnification = 4f;
    internal const float RaiseSeconds = 0.6f;
    internal const float LowerSeconds = 0.4f;
    private const float ZoomStep = 1.25f;      // one wheel notch
    private const float PadZoomPerSecond = 2f; // gamepad zoom buttons: x2 per second

    internal static readonly int RaisedKey = (ModInfo.Guid + ".Raised").GetStableHashCode();

    private static ScopeState _state;
    private static float _progress;
    private static float _magnification = -1f;
    private static bool _raiseRequest;
    private static bool _lowerRequest;
    private static bool _held;
    private static Player _player;
    private static float _pendingMessageAt = float.NegativeInfinity;

    internal static ScopeState State => _state;
    internal static float Progress => _progress;

    // Spyglass up or coming up: feet stay, actions blocked, aim slowed.
    internal static bool Engaged => _state == ScopeState.Raising || _state == ScopeState.Raised;

    // Anything shown (camera, overlay, pose): not Idle.
    internal static bool Active => _state != ScopeState.Idle;

    internal static Player Player => _player;

    // Look input factor: the view moves on screen as fast as without zoom (x AimSensitivity). Zoom of the last frame
    // (look runs before the camera).
    internal static float LookScale
    {
        get
        {
            var aim = Plugin.AimSensitivity != null ? Plugin.AimSensitivity.Value : 1f;
            return aim / Mathf.Max(1f, ScopeCamera.CurrentMagnification);
        }
    }

    // Zoom the player picked this session (wheel), never above the rules' cap.
    internal static float Magnification
    {
        get
        {
            var rules = ServerRules.Current;
            var cap = rules.IsPending ? SpyglassRules.MagnificationMin : rules.MaxMagnification;
            if (_magnification < 0f)
            {
                _magnification = Plugin.StartMagnification != null ? Plugin.StartMagnification.Value : DefaultStartMagnification;
            }
            return Mathf.Clamp(_magnification, MinMagnification, Mathf.Max(MinMagnification, cap));
        }
    }

    // Activation / new world: nothing up.
    internal static void Reset()
    {
        _state = ScopeState.Idle;
        _progress = 0f;
        _raiseRequest = false;
        _lowerRequest = false;
        _held = false;
        _player = null;
    }

    // Feature off or game quit: everything back now (no animation).
    internal static void Shutdown()
    {
        Abort();
        ScopeHud.Restore();
        ScopeOverlay.Destroy();
        ScopeCapture.Remove();
    }

    // Player.SetControls prefix (local player, every physics tick). Returns true when the spyglass took the inputs:
    // caller then zeroes them (Engaged: all of them, feet included; else only the attack ones).
    internal static bool OnControls(Player player, bool attack, bool attackHold, bool block, out bool zeroAll)
    {
        zeroAll = false;
        var held = SpyglassContent.IsSpyglass(player.GetRightItem());
        if (!held)
        {
            return false;
        }
        // Sitting, emote, ship wheel, saddle: vanilla use the click to stand up / let go; me leave it alone.
        if (_state == ScopeState.Idle && (player.IsAttached() || player.InEmote() || player.GetDoodadController() != null))
        {
            return false;
        }
        _held = attackHold;
        switch (_state)
        {
            case ScopeState.Idle:
            case ScopeState.Lowering:
                if (attack)
                {
                    _raiseRequest = true;
                }
                break;
            default:
                var hold = Plugin.HoldToLook != null && Plugin.HoldToLook.Value;
                if ((attack && !hold) || block)
                {
                    _lowerRequest = true;
                }
                zeroAll = true;
                break;
        }
        return true;
    }

    // Player.OnDamaged postfix (local player): a real hit lowers the spyglass (damage over time does not).
    internal static void OnDamaged(HitData hit)
    {
        if (!Engaged || hit == null)
        {
            return;
        }
        switch (hit.m_hitType)
        {
            case HitData.HitType.Burning:
            case HitData.HitType.Freezing:
            case HitData.HitType.Poisoned:
            case HitData.HitType.Smoke:
            case HitData.HitType.Drowning:
                return;
        }
        _lowerRequest = true;
    }

    // Player.Update postfix, local player, every frame.
    internal static void Tick(Player player, float dt)
    {
        // Nothing up, nothing asked: one compare per frame.
        if (_state == ScopeState.Idle && !_raiseRequest && ReferenceEquals(player, _player))
        {
            _lowerRequest = false;
            return;
        }
        if (!ReferenceEquals(player, _player))
        {
            // New local player (spawn, respawn): whatever the old one had is gone.
            if (_state != ScopeState.Idle)
            {
                Abort();
            }
            _player = player;
            SetRaisedFlag(player, false);
        }
        var holding = Holding(player);
        if (_state != ScopeState.Idle && !holding)
        {
            Abort();
            return;
        }

        if (_raiseRequest)
        {
            _raiseRequest = false;
            TryRaise(player);
        }
        if (_state == ScopeState.Idle)
        {
            _lowerRequest = false;
            return;
        }

        if (Engaged)
        {
            if (MustAbort(player))
            {
                Abort();
                return;
            }
            var hold = Plugin.HoldToLook != null && Plugin.HoldToLook.Value;
            if (_lowerRequest || (hold && !_held) || MustLower(player))
            {
                StartLowering(player);
            }
            else
            {
                UpdateZoom(dt);
            }
        }
        _lowerRequest = false;

        switch (_state)
        {
            case ScopeState.Raising:
                _progress = Mathf.Min(1f, _progress + dt / RaiseSeconds);
                if (_progress >= 1f)
                {
                    _state = ScopeState.Raised;
                }
                break;
            case ScopeState.Lowering:
                _progress = Mathf.Max(0f, _progress - dt / LowerSeconds);
                if (_progress <= 0f || MustAbort(player))
                {
                    Abort();
                }
                break;
        }
    }

    // Right hand hold the spyglass (R, swimming and eating put it away = not in the right hand any more).
    private static bool Holding(Player player)
    {
        return player != null && !player.IsDead() && SpyglassContent.IsSpyglass(player.GetRightItem());
    }

    private static void TryRaise(Player player)
    {
        if (!CanRaise(player))
        {
            return;
        }
        if (ServerRules.IsPending)
        {
            if (Time.unscaledTime - _pendingMessageAt > 3f)
            {
                _pendingMessageAt = Time.unscaledTime;
                player.Message(MessageHud.MessageType.TopLeft, "The server has not sent the spyglass settings yet.");
            }
            return;
        }
        if (_state == ScopeState.Idle)
        {
            ArmPose.ForgetLocal();
        }
        _state = ScopeState.Raising;
        // Stop what the feet were doing: auto-run keeps running without input, a toggled block stays up.
        player.m_autoRun = false;
        player.m_moveDir = Vector3.zero;
        player.m_run = false;
        player.m_blocking = false;
        SetRaisedFlag(player, true);
    }

    // Vanilla states where raising makes no sense (each one would also lower it, see MustAbort / MustLower).
    internal static bool CanRaise(Player player)
    {
        return Holding(player) && player.TakeInput() && !MustAbort(player) && !MustLower(player)
               && !player.InAttack() && !player.InDodge() && !player.InMinorAction() && !player.InEmote()
               && !Hud.InRadial() && !Hud.IsPieceSelectionVisible() && !PlayerController.HasInputDelay;
    }

    // Instant: the view must jump back (dead, teleporting, sat down, swimming, building...).
    private static bool MustAbort(Player player)
    {
        return !Holding(player) || player.IsTeleporting() || player.InCutscene() || player.IsAttached()
               || player.InBed() || player.IsSleeping() || player.GetDoodadController() != null || player.IsRiding()
               || player.IsSwimming() || player.InPlaceMode() || player.IsDebugFlying() || GameCamera.InFreeFly();
    }

    // Animated: lowered like a click (menus, map, chat, hit, knocked about).
    private static bool MustLower(Player player)
    {
        return !player.TakeInput() || player.IsStaggering() || player.IsKnockedBack() || Hud.InRadial()
               || Hud.IsPieceSelectionVisible();
    }

    private static void StartLowering(Player player)
    {
        if (_state == ScopeState.Lowering || _state == ScopeState.Idle)
        {
            return;
        }
        _state = ScopeState.Lowering;
        SetRaisedFlag(player, false);
    }

    // Everything back at once: camera (vanilla rewrite it next frame), body, overlay, fog, crosshair, slot, flag.
    internal static void Abort()
    {
        var player = _player != null ? _player : Player.m_localPlayer;
        _state = ScopeState.Idle;
        _progress = 0f;
        _raiseRequest = false;
        _lowerRequest = false;
        ScopeCamera.Restore();
        ArmPose.ReleaseLocal();
        if (player != null)
        {
            SetRaisedFlag(player, false);
        }
    }

    // Owner write; ZDO only send a change.
    private static void SetRaisedFlag(Player player, bool raised)
    {
        var view = player != null ? player.m_nview : null;
        if (view == null || !view.IsValid() || !view.IsOwner())
        {
            return;
        }
        var zdo = view.GetZDO();
        if (zdo.GetBool(RaisedKey) != raised)
        {
            zdo.Set(RaisedKey, raised);
        }
    }

    // Wheel (and gamepad zoom buttons, like vanilla camera zoom: hold the alt keys) change the zoom while looking.
    private static void UpdateZoom(float dt)
    {
        var scroll = ZInput.GetMouseScrollWheel();
        var m = Magnification;
        if (scroll > 0.001f)
        {
            m *= ZoomStep;
        }
        else if (scroll < -0.001f)
        {
            m /= ZoomStep;
        }
        if (ZInput.GetButton("JoyAltKeys") && !Hud.InRadial())
        {
            if (ZInput.GetButton("JoyCamZoomIn"))
            {
                m *= Mathf.Pow(PadZoomPerSecond, dt);
            }
            else if (ZInput.GetButton("JoyCamZoomOut"))
            {
                m /= Mathf.Pow(PadZoomPerSecond, dt);
            }
        }
        var rules = ServerRules.Current;
        _magnification = Mathf.Clamp(m, MinMagnification, Mathf.Max(MinMagnification, rules.MaxMagnification));
    }

#if DEBUG
    // Self tests: drive the spyglass without mouse clicks.
    internal static void TestRequestRaise() => _raiseRequest = true;

    internal static void TestRequestLower() => _lowerRequest = true;

    internal static void TestSetMagnification(float m) => _magnification = m;
#endif
}
