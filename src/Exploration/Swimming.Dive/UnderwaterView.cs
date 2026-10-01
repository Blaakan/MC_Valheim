using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SwimmingDiveMod;

// Me = what the camera see under water (design 2.7, hook B, personal settings UnderwaterFog / SurfaceFromBelow).
// GameCamera.UpdateCamera postfix (LateUpdate, after this frame's EnvMan.SetEnv if any). View on = the CAMERA (not the
// player) is under the water surface at its own spot (LiquidType.Water; tar and "no water" = off), not free fly. So a
// death under water keep the view on the ragdoll camera, and another mod's under-water camera get the fog too.
// Teleporting = state held (liquid depth is 0 while teleporting), decided again at the destination.
//   Fog: RenderSettings fog colour and density replaced by an under-water tint and a visibility of
//     UnderwaterVisibility m (vanilla 1.0.16 fog is Exponential, in-world NOTE). Start/end touched only in Linear mode
//     (a fog mod may set it). Computed from a cached base each frame, never from the current value (EnvMan.SetEnv run
//     at most once per frame, in FixedUpdate, and not at all in frames without a physics step): no compounding, no
//     flicker. Each of the five fields (four RenderSettings + sun fog below) on its own: its base taken again when it
//     no longer hold what me wrote there (SetEnv write only colour, density and sun fog), and put back only if it still
//     hold my value. So SetEnv never make me lose the vanilla start/end. Never EnvMan.SetForceEnvironment (would wipe a
//     dungeon's forced environment), never ambientLight (StealthSystem read it).
//     Sun fog: shader global _SunFogColor (SetEnv write EnvMan.m_sunFogColor there with Shader.SetGlobalColor). Fog
//     shader blend toward it in the sun's direction (post fx FogComponent know _SunDir + _SunFogColor; shader source
//     not in .ref) = olive haze when looking up at noon. Same rule as
//     the fog colour: me write the same tint there (SetGlobalColor, so same colour space step as vanilla), base
//     taken again when the global no longer hold what me wrote, put back only if it still hold my value. Base and
//     compare use the RAW stored vector (Shader.GetGlobalVector / SetGlobalVector: no colour space step), so the put
//     back is exact in Linear space whatever GetGlobalColor give back. _SunColor, _AmbientColor and the camera's
//     SunShafts.sunColor (SetEnv too) = light, not fog: me leave them.
//   Surface from below: the one-sided surface mesh of each WaterVolume (m_waterSurface) turned 180 degrees about its
//     local X, with the _depth corners swapped ([3],[2],[1],[0]) so zone borders match. Same material, same position.
//     Saved rotation put back exactly. A surface whose transform carry the volume's collider is never turned (the
//     trigger would move; warn once). In 1.0.16 surface = own object, not the volume's (in-world NOTE: 201 of 201
//     loaded volumes safe).
// Every exit (camera back above, setting off, free fly, logout, camera replaced, feature off) put everything back.
internal static class UnderwaterView
{
    // Camera this far under the surface = view on.
    internal const float ViewMargin = 0.02f;
    // Fog colour: tint x brightness of the vanilla fog (night = dark), darker with depth.
    internal const float ReferenceBrightness = 0.5f;
    internal const float MinBrightness = 0.08f;
    internal const float DarkeningPerMetre = 0.015f;
    internal const float MinDepthLight = 0.35f;

    // Frames after the turn in which the swapped _depth is written again (volume's Start may run after the turn).
    internal const int FreshFrames = 2;

    // One turned surface: its rotation before, and the frame it was turned.
    private struct Turned
    {
        internal Quaternion Rotation;
        internal int Frame;
    }

    private static readonly int DepthId = Shader.PropertyToID("_depth");
    // Fog colour toward the sun (EnvMan.SetEnv, MenuScene.Update, post fx FogComponent read it).
    internal static readonly int SunFogId = Shader.PropertyToID("_SunFogColor");
    private static readonly Quaternion Flip = Quaternion.Euler(180f, 0f, 0f);
    private static readonly Dictionary<WaterVolume, Turned> Flipped = new Dictionary<WaterVolume, Turned>();
    private static readonly HashSet<WaterVolume> Unsafe = new HashSet<WaterVolume>();
    private static readonly float[] Corners4 = new float[4];
    private static readonly float[] DepthOut = new float[4];

    private static GameCamera _camera;
    private static bool _unsafeWarned;

    // Fog state. _fogApplied = colour and density mine; _rangeApplied = start and end mine (Linear mode only).
    private static bool _fogApplied;
    private static bool _rangeApplied;
    private static Color _baseColor;
    private static float _baseDensity;
    private static float _baseStart;
    private static float _baseEnd;
    private static Color _lastColor;
    private static float _lastDensity;
    private static float _lastStart;
    private static float _lastEnd;
    // Sun fog global, raw stored value (goes with _fogApplied).
    private static Vector4 _baseSunFog;
    private static Vector4 _lastSunFog;

    internal static bool Active { get; private set; }
    internal static bool FogApplied => _fogApplied;
    internal static int FlippedCount => Flipped.Count;
    internal static int UnsafeCount => Unsafe.Count;
    internal static float BaseDensity => _baseDensity;
    internal static Color BaseColor => _baseColor;
    internal static Vector4 BaseSunFog => _baseSunFog;
    internal static float LastDepth { get; private set; }

    // GameCamera.UpdateCamera postfix, every frame.
    internal static void Tick(GameCamera camera)
    {
        var player = Player.m_localPlayer;
        if (!ReferenceEquals(camera, _camera))
        {
            RestoreAll();
            _camera = camera;
        }
        if (player == null || camera == null)
        {
            if (Active || _fogApplied || Flipped.Count > 0)
            {
                RestoreAll();
            }
            return;
        }
        if (player.IsTeleporting())
        {
            return;
        }
        var on = false;
        var depth = 0f;
        if (!GameCamera.InFreeFly())
        {
            var pos = camera.transform.position;
            var surface = Floating.GetLiquidLevel(pos, 1f, LiquidType.Water);
            if (surface > -9000f)
            {
                depth = surface - pos.y;
                on = depth > ViewMargin;
            }
        }
        Active = on;
        LastDepth = on ? depth : 0f;
        if (on && Visuals.Fog)
        {
            ApplyFog(depth);
        }
        else if (_fogApplied)
        {
            RestoreFog();
        }
        if (on && Visuals.Surface)
        {
            FlipSurfaces();
        }
        else if (Flipped.Count > 0)
        {
            RestoreSurfaces();
        }
    }

    // Feature off, logout, camera replaced: all back.
    internal static void RestoreAll()
    {
        Active = false;
        LastDepth = 0f;
        RestoreFog();
        RestoreSurfaces();
        Unsafe.Clear();
    }

    // ---------- fog ----------

    // Pure: exponential fog density for "you see this far" (2 % contrast left).
    internal static float Density(FogMode mode, float visibility)
    {
        var v = Mathf.Max(1f, visibility);
        return mode == FogMode.ExponentialSquared ? 1.978f / v : 3.912f / v;
    }

    // Pure: under-water fog colour for a vanilla fog colour and a camera depth.
    internal static Color Tint(Color baseColor, Color tint, float depth)
    {
        var bright = Mathf.Clamp(baseColor.grayscale / ReferenceBrightness, MinBrightness, 1f);
        var dark = Mathf.Clamp(1f - Mathf.Max(0f, depth) * DarkeningPerMetre, MinDepthLight, 1f);
        var k = bright * dark;
        return new Color(tint.r * k, tint.g * k, tint.b * k, 1f);
    }

    internal static void ApplyFog(float depth)
    {
        // Each field alone: base taken again only where someone else wrote since my last frame.
        var fresh = !_fogApplied;
        if (fresh || !Same(RenderSettings.fogColor, _lastColor))
        {
            _baseColor = RenderSettings.fogColor;
        }
        if (fresh || !Same(RenderSettings.fogDensity, _lastDensity))
        {
            _baseDensity = RenderSettings.fogDensity;
        }
        var sunFog = Shader.GetGlobalVector(SunFogId);
        if (fresh || !Same(sunFog, _lastSunFog))
        {
            _baseSunFog = sunFog;
        }
        _fogApplied = true;
        var mode = RenderSettings.fogMode;
        var visibility = Visuals.Visibility;
        var tint = Tint(_baseColor, Visuals.FogColor, depth);
        RenderSettings.fogColor = tint;
        RenderSettings.fogDensity = Mathf.Max(_baseDensity, Density(mode, visibility));
        // Toward the sun same tint: no sun-side haze under water.
        Shader.SetGlobalColor(SunFogId, tint);
        // Read back: compare later against what Unity really hold.
        _lastColor = RenderSettings.fogColor;
        _lastDensity = RenderSettings.fogDensity;
        _lastSunFog = Shader.GetGlobalVector(SunFogId);
        if (mode != FogMode.Linear)
        {
            // Exponential (vanilla): start/end do nothing, me leave them (and give back any me wrote in Linear).
            RestoreRange();
            return;
        }
        var freshRange = !_rangeApplied;
        if (freshRange || !Same(RenderSettings.fogStartDistance, _lastStart))
        {
            _baseStart = RenderSettings.fogStartDistance;
        }
        if (freshRange || !Same(RenderSettings.fogEndDistance, _lastEnd))
        {
            _baseEnd = RenderSettings.fogEndDistance;
        }
        _rangeApplied = true;
        RenderSettings.fogStartDistance = 0f;
        RenderSettings.fogEndDistance = Mathf.Min(_baseEnd, visibility);
        _lastStart = RenderSettings.fogStartDistance;
        _lastEnd = RenderSettings.fogEndDistance;
    }

    // Base back, field by field, only where nobody wrote a newer value since (then it stay: SetEnv or another mod own
    // it).
    internal static void RestoreFog()
    {
        if (_fogApplied)
        {
            _fogApplied = false;
            if (Same(RenderSettings.fogColor, _lastColor))
            {
                RenderSettings.fogColor = _baseColor;
            }
            if (Same(RenderSettings.fogDensity, _lastDensity))
            {
                RenderSettings.fogDensity = _baseDensity;
            }
            if (Same(Shader.GetGlobalVector(SunFogId), _lastSunFog))
            {
                // Raw back, no colour space step: exactly the value that was there.
                Shader.SetGlobalVector(SunFogId, _baseSunFog);
            }
        }
        RestoreRange();
    }

    // Start/end back (Linear mode only), each only if it still hold my value.
    private static void RestoreRange()
    {
        if (!_rangeApplied)
        {
            return;
        }
        _rangeApplied = false;
        if (Same(RenderSettings.fogStartDistance, _lastStart))
        {
            RenderSettings.fogStartDistance = _baseStart;
        }
        if (Same(RenderSettings.fogEndDistance, _lastEnd))
        {
            RenderSettings.fogEndDistance = _baseEnd;
        }
    }

    private static bool Same(float a, float b) => Mathf.Abs(a - b) <= 1e-5f + Mathf.Abs(b) * 1e-5f;

    private static bool Same(Color a, Color b) =>
        Same(a.r, b.r) && Same(a.g, b.g) && Same(a.b, b.b) && Same(a.a, b.a);

    private static bool Same(Vector4 a, Vector4 b) =>
        Same(a.x, b.x) && Same(a.y, b.y) && Same(a.z, b.z) && Same(a.w, b.w);

    // ---------- surface ----------

    // Pure: _depth corners of a volume, swapped for a surface turned about its local X.
    internal static void Corners(float[] source, bool swapped, float[] target)
    {
        if (swapped)
        {
            target[0] = source[3];
            target[1] = source[2];
            target[2] = source[1];
            target[3] = source[0];
        }
        else
        {
            target[0] = source[0];
            target[1] = source[1];
            target[2] = source[2];
            target[3] = source[3];
        }
    }

    // Surface transform carry the volume's collider (turning it would move the trigger)?
    internal static bool SafeToFlip(WaterVolume volume)
    {
        var surface = volume.m_waterSurface;
        if (surface == null)
        {
            return false;
        }
        var collider = volume.m_collider;
        return collider == null || !collider.transform.IsChildOf(surface.transform);
    }

    // Every loaded volume turned (new zones too); list walked in place, no allocation. A volume joins Instances in
    // OnEnable, before its Start (DetectWaterDepth + SetupMaterial write the vanilla _depth there): turned this frame or
    // the two before = swapped _depth written again, so a zone loaded while under water get the swap after its Start.
    internal static void FlipSurfaces()
    {
        var list = WaterVolume.Instances;
        var frame = Time.frameCount;
        for (var i = 0; i < list.Count; i++)
        {
            var volume = list[i];
            if (volume == null || volume.m_waterSurface == null || Unsafe.Contains(volume))
            {
                continue;
            }
            if (Flipped.TryGetValue(volume, out var turned))
            {
                if (frame - turned.Frame <= FreshFrames)
                {
                    WriteDepth(volume, swapped: true);
                }
                continue;
            }
            if (!SafeToFlip(volume))
            {
                Unsafe.Add(volume);
                if (!_unsafeWarned)
                {
                    _unsafeWarned = true;
                    Log.Warning("A water surface carries its water volume, so " + ModInfo.Name + " does not turn it "
                                + "for the view from below; the sky may show through that surface under water.");
                }
                continue;
            }
            var t = volume.m_waterSurface.transform;
            Flipped[volume] = new Turned { Rotation = t.localRotation, Frame = frame };
            t.localRotation = t.localRotation * Flip;
            WriteDepth(volume, swapped: true);
        }
    }

    internal static void RestoreSurfaces()
    {
        if (Flipped.Count == 0)
        {
            return;
        }
        foreach (var pair in Flipped)
        {
            var volume = pair.Key;
            if (volume == null || volume.m_waterSurface == null)
            {
                continue;
            }
            volume.m_waterSurface.transform.localRotation = pair.Value.Rotation;
            WriteDepth(volume, swapped: false);
        }
        Flipped.Clear();
    }

    // Same values as WaterVolume.SetupMaterial (m_forceDepth x4, else the heightmap corners), maybe swapped.
    internal static void WriteDepth(WaterVolume volume, bool swapped)
    {
        VanillaCorners(volume, Corners4);
        Corners(Corners4, swapped, DepthOut);
        volume.m_waterSurface.material.SetFloatArray(DepthId, DepthOut);
    }

    internal static void VanillaCorners(WaterVolume volume, float[] target)
    {
        if (volume.m_forceDepth >= 0f)
        {
            target[0] = target[1] = target[2] = target[3] = volume.m_forceDepth;
            return;
        }
        var n = volume.m_normalizedDepth;
        target[0] = n[0];
        target[1] = n[1];
        target[2] = n[2];
        target[3] = n[3];
    }

    // Surface rotation this view saved for a volume (self test).
    internal static bool TryGetSaved(WaterVolume volume, out Quaternion rotation)
    {
        var found = Flipped.TryGetValue(volume, out var turned);
        rotation = turned.Rotation;
        return found;
    }
}
