using UnityEngine;

namespace MC.Exploration.SwimmingDiveMod;

// Me = camera follow the diver under the surface (design 2.6, hook A, personal setting UnderwaterCamera). Vanilla
// GameCamera.GetCameraPosition clamp the camera to liquid level + m_minWaterDistance (0.4 m on the 1.0.16 camera
// prefab, in-world NOTE; 0.3 in code), so it never go under.
//   Prefix: local player diving and eye under water (hysteresis EnterEyeDepth / ExitEyeDepth) -> m_minWaterDistance
//           far below for this one call (clamp never fire). Finalizer put the value back, also on exception: other
//           code and mods always see the real value (Aegir change it in debug fly).
//   Postfix: camera kept under the surface with a clearance (half the eye depth, 0.05-0.5 m), pulled along the
//           eye->camera line like vanilla wall collision, so it stay in line of sight; within 1 m of the surface the
//           near clip plane go small (m_waterClipping), else a near plane of 0.5 m would cut a hole in the surface.
// Not diving = vanilla clamp untouched. No zoom, distance, tilt or rotation change.
internal static class DiveCamera
{
    internal const float NoClamp = -10000f;
    internal const float EnterEyeDepth = 0.35f;
    internal const float ExitEyeDepth = 0.15f;
    internal const float MinClearance = 0.05f;
    internal const float MaxClearance = 0.5f;
    internal const float WaterClipDepth = 1f;

    private static bool _lifted;

    internal static bool Lifted => _lifted;

    // GetCameraPosition prefix, every frame while the local player dive.
    internal static bool ShouldLift(Player player)
    {
        if (player == null || !Visuals.Camera || !DiveState.DivingFor(player) || player.m_eye == null)
        {
            _lifted = false;
            return false;
        }
        _lifted = LiftNext(_lifted, player.GetLiquidLevel() - player.m_eye.position.y);
        return _lifted;
    }

    internal static void Clear() => _lifted = false;

    // Pure: camera may go under (eye depth in m, hysteresis).
    internal static bool LiftNext(bool lifted, float eyeDepth) => eyeDepth > (lifted ? ExitEyeDepth : EnterEyeDepth);

    // Pure: gap between camera and surface for this eye depth.
    internal static float Clearance(float eyeDepth) => Mathf.Clamp(eyeDepth * 0.5f, MinClearance, MaxClearance);

    // Pure: point of the eye->pos line at height maxY when pos is above it (eye above maxY too = only y cut).
    internal static Vector3 BelowSurface(Vector3 eye, Vector3 pos, float maxY)
    {
        if (pos.y <= maxY)
        {
            return pos;
        }
        var rise = pos.y - eye.y;
        if (eye.y >= maxY || rise < 0.0001f)
        {
            pos.y = maxY;
            return pos;
        }
        return eye + (pos - eye) * ((maxY - eye.y) / rise);
    }

    // GetCameraPosition postfix while lifted.
    internal static void KeepBelowSurface(GameCamera camera, Player player, ref Vector3 pos)
    {
        var level = player.GetLiquidLevel();
        var surface = Floating.GetLiquidLevel(pos, 1f, LiquidType.Water);
        if (surface < -9000f)
        {
            surface = level;
        }
        var eye = player.m_eye.position;
        pos = BelowSurface(eye, pos, surface - Clearance(level - eye.y));
        if (surface - pos.y < WaterClipDepth)
        {
            camera.m_waterClipping = true;
            var cam = camera.m_camera;
            if (cam != null && cam.nearClipPlane > camera.m_nearClipPlaneMin)
            {
                cam.nearClipPlane = camera.m_nearClipPlaneMin;
            }
        }
    }
}
