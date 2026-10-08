using MC.Shared;
using UnityEngine;

namespace MC.Exploration.ViewSpyglassMod;

// Me = what the spyglass does to the camera, run at the end of GameCamera.UpdateCamera (LateUpdate, after vanilla
// placed the camera this frame). Me never touch GameCamera's own fields (m_fov, m_distance...): vanilla rewrite
// position, rotation and field of view every frame, so lowering the spyglass give the normal camera back by itself.
// From Scope.Progress p (0 down .. 1 at the eye):
//   camera   slide from the vanilla spot into the eye (p 0.10 -> 0.70), turn to the eye's look
//   zoom     field of view narrow from vanilla to vanilla / magnification (p 0.30 -> 1)
//   body     own body hidden once the camera is in the head (closer than 0.45 m to the eye: it would fill the view),
//            or within 2 m of the feet like vanilla does (Player.FixedUpdate)
//   near     near plane down to vanilla's minimum while moved (walls and roofs right before the face stay drawn)
//   overlay  round view closes in from the screen edge, blur and dark edge fade in (p 0.40 -> 1)
//   fog      thinner in clear weather, only with Distant Horizons (same curve as the overlay)
//   far view Distant Horizons told where we look and how strong the zoom (ViewBoost)
// Mouse wheel zoom of vanilla (camera distance) kept still while the spyglass is up (prefix save, postfix put back).
internal static class ScopeCamera
{
    internal const string DistantHorizonsGuid = "MC.Exploration.View.DistantHorizons";
    private const float BodyHideDistance = 0.45f;   // camera this close to the eye = inside the head
    private const float VanillaHideDistance = 2f;   // Player.FixedUpdate: camera this close to the feet = body hidden

    private static bool _applied;
    private static bool _bodyHidden;
    private static Player _bodyOwner;
    private static float _fogBase;
    private static float _fogWritten = -1f;
    private static bool _fogApplied;
    private static float _lastMagnification = 1f;
    private static float _lastFov;
    private static bool _dhChecked;
    private static bool _dhActive;

    // Zoom now on screen (1 = none) and the camera's field of view: look speed and self tests read them.
    internal static float CurrentMagnification => _applied ? _lastMagnification : 1f;
    internal static float CurrentFov => _lastFov;
    internal static bool BodyHidden => _bodyHidden;

    internal static float Smooth(float from, float to, float t)
    {
        var x = Mathf.Clamp01((t - from) / (to - from));
        return x * x * (3f - 2f * x);
    }

    // UpdateCamera prefix: keep the camera distance (vanilla wheel zoom) while the spyglass is up.
    internal static float Before(GameCamera cam)
    {
        return cam.m_distance;
    }

    // UpdateCamera postfix.
    internal static void After(GameCamera cam, float savedDistance)
    {
#if DEBUG
        if (TestPoseView)
        {
            // Self test watch the arm pose from outside: camera parked, no zoom, body shown, no overlay.
            if (_applied)
            {
                Restore();
            }
            if (Scope.Active)
            {
                cam.m_distance = savedDistance;
            }
            cam.transform.position = TestCameraPosition;
            cam.transform.rotation = TestCameraRotation;
            return;
        }
#endif
        if (!Scope.Active)
        {
            if (_applied)
            {
                Restore();
            }
            _dhChecked = false;
            return;
        }
        var player = Player.m_localPlayer;
        if (player == null || player.IsDead() || GameCamera.InFreeFly() || cam.m_camera == null
            || (Game.instance != null && Game.instance.IsShuttingDown()))
        {
            Scope.Abort();
            return;
        }
        if (!_dhChecked)
        {
            // Once per raise (the registry state string allocate): Distant Horizons on and running here?
            _dhChecked = true;
            _dhActive = DistantHorizonsActive();
        }
        _applied = true;
        cam.m_distance = savedDistance;

        var p = Scope.Progress;
        var c = Smooth(0.10f, 0.70f, p);
        var z = Smooth(0.30f, 1.00f, p);
        var o = Smooth(0.40f, 1.00f, p);

        var t = cam.transform;
        var eye = player.m_eye;
        t.position = Vector3.Lerp(t.position, eye.position, c);
        t.rotation = Quaternion.Slerp(t.rotation, eye.rotation, c);
        // Vanilla near plane (0.5 m in the open) was picked for its own spot behind the player: at the eye a wall or
        // roof right before the face would be cut away. Vanilla write its value again next frame.
        if (c > 0f && cam.m_camera.nearClipPlane > cam.m_nearClipPlaneMin)
        {
            cam.m_camera.nearClipPlane = cam.m_nearClipPlaneMin;
        }

        var baseFov = cam.m_camera.fieldOfView;
        var m = Mathf.Pow(Mathf.Max(1f, Scope.Magnification), z);
        var fov = ZoomedFov(baseFov, m);
        cam.m_camera.fieldOfView = fov;
        if (cam.m_skyCamera != null)
        {
            cam.m_skyCamera.fieldOfView = fov;
        }
        _lastMagnification = m;
        _lastFov = fov;

        // Hidden in the head, and wherever vanilla would hide it too (camera within 2 m of the feet, Player.FixedUpdate:
        // a wall or the ground pulled the camera in).
        var camPos = t.position;
        SetBodyHidden(player, (camPos - eye.position).sqrMagnitude < BodyHideDistance * BodyHideDistance
                              || (camPos - player.transform.position).sqrMagnitude < VanillaHideDistance * VanillaHideDistance);
        ScopeOverlay.Show(o);
        var rules = ServerRules.Current;
        ApplyFog(_dhActive && !rules.IsPending ? o * rules.FogClearing : 0f);
        ViewBoost.Write(m, o, t.forward, fov, cam.m_camera.aspect);
    }

    // Distant Horizons installed, on and running in this game? (Self test may say otherwise.)
    private static bool DistantHorizonsActive()
    {
#if DEBUG
        if (TestDistantHorizons.HasValue)
        {
            return TestDistantHorizons.Value;
        }
#endif
        var dh = FeatureRegistry.Find(DistantHorizonsGuid);
        return dh.HasValue && dh.Value.IsActive;
    }

    // Field of view showing things m times bigger (on the vertical field of view, like a lens).
    internal static float ZoomedFov(float baseFov, float m)
    {
        var half = Mathf.Tan(baseFov * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1f, m);
        return Mathf.Atan(half) * 2f * Mathf.Rad2Deg;
    }

    // Everything this class changed back (Scope.Abort, end of lowering, feature off). Camera itself: vanilla next
    // frame.
    internal static void Restore()
    {
        _applied = false;
        _lastMagnification = 1f;
        SetBodyHidden(_bodyOwner, false, owned: false);
        _bodyOwner = null;
        ScopeOverlay.Hide();
        RestoreFog();
        ViewBoost.Clear();
    }

    // Vanilla hides a body by moving its LOD group reference point far away (Character.SetVisible); vanilla also
    // flip that point on and off by itself every physics tick when the camera is near the feet (Player.FixedUpdate
    // hide, Character.CustomFixedUpdate show). While the spyglass is up me own it: written once per rendered frame
    // here, after every physics tick of the frame (shown or hidden, no flicker). On release (owned false) me give back
    // what vanilla wants now.
    private static void SetBodyHidden(Player player, bool hidden, bool owned = true)
    {
        if (player == null)
        {
            _bodyHidden = false;
            return;
        }
        var lod = player.m_lodGroup;
        if (lod == null)
        {
            return;
        }
        var far = new Vector3(999999f, 999999f, 999999f);
        if (owned)
        {
            lod.localReferencePoint = hidden ? far : player.m_originalLocalRef;
            _bodyHidden = hidden;
            _bodyOwner = player;
            return;
        }
        lod.localReferencePoint = player.m_lodVisible ? player.m_originalLocalRef : far;
        _bodyHidden = false;
    }

    // Fog: from the density the game (and other mods, Distant Horizons first) set this frame, me take away a share;
    // base taken again whenever the value is not the one me wrote (EnvMan set a new one). Rain and storms (dense fog)
    // kept: clearing fades out between clear (0.006) and storm (0.02) densities, like Distant Horizons.
    private static void ApplyFog(float clearing)
    {
        if (clearing <= 0f)
        {
            RestoreFog();
            return;
        }
        var current = RenderSettings.fogDensity;
        if (!_fogApplied || !Mathf.Approximately(current, _fogWritten))
        {
            _fogBase = current;
        }
        var weather = 1f - Mathf.Clamp01((_fogBase - 0.006f) / (0.02f - 0.006f));
        _fogWritten = _fogBase * (1f - Mathf.Clamp01(clearing) * weather);
        RenderSettings.fogDensity = _fogWritten;
        _fogApplied = true;
    }

    private static void RestoreFog()
    {
        if (!_fogApplied)
        {
            return;
        }
        if (Mathf.Approximately(RenderSettings.fogDensity, _fogWritten))
        {
            RenderSettings.fogDensity = _fogBase;
        }
        _fogApplied = false;
        _fogWritten = -1f;
    }

#if DEBUG
    internal static bool DistantHorizonsSeen => _dhActive;
    internal static bool FogApplied => _fogApplied;

    // Self test (spyglass.pose): camera parked here while the spyglass is up, to see the arm from outside.
    internal static bool TestPoseView;
    internal static Vector3 TestCameraPosition;
    internal static Quaternion TestCameraRotation = Quaternion.identity;

    // Self test: play "Distant Horizons run" (true) or "not" (false) for next raise; null = ask the registry.
    internal static bool? TestDistantHorizons;

    // Self test: metres vanilla wheel zoom move the camera distance on next camera update (stand-in for the wheel
    // vanilla UpdateCamera read by itself). Used once.
    internal static float TestWheelNudge;

    // UpdateCamera prefix, after the distance was saved.
    internal static void TestNudge(GameCamera cam)
    {
        if (TestWheelNudge != 0f)
        {
            cam.m_distance += TestWheelNudge;
            TestWheelNudge = 0f;
        }
    }
#endif
}
