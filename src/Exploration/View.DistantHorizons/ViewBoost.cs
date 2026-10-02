using System;
using UnityEngine;

namespace MC.Exploration.ViewDistantHorizonsMod;

// Me = read the MC Spyglass's message while a spyglass is at the player's eye: zoom and look direction. Both managers
// then treat tiles in that direction as if they were zoom times nearer (capped by SpyglassMaxBoost): terrain splits
// finer there, objects get their nearer band (trees as cards farther out, buildings and big rocks drawn farther).
// The boost fades out over Falloff degrees around the view, so neighbouring tiles change detail step by step.
// No reference to the spyglass mod: a double[] in an AppDomain slot (BCL type only, like FeatureRegistry), written
// by the spyglass every frame while it is up; older than StaleFrames = no boost (removed or crashed spyglass never
// leave one on). Layout of "MC.ViewBoost.v1" never changes (new layout = new slot name):
//   [0] layout (1)   [1] Time.frameCount   [2] zoom (>= 1)   [3] weight 0..1 (raise transition)
//   [4..6] camera forward (world)   [7] half field of view, horizontal (degrees)   [8] half, vertical (degrees)
internal static class ViewBoost
{
    internal const string SlotName = "MC.ViewBoost.v1";
    private const int StaleFrames = 5;
    private const float Margin = 4f;       // degrees added around the view (tiles straddling its edge)
    private const float Falloff = 20f;     // degrees over which the boost fades out
    private const float MinRecomputeGap = 0.2f; // seconds between recomputes asked by a moving boost

    // One reading of the slot, ready for per-tile factors (horizontal plane only: tiles are judged by XZ).
    internal struct State
    {
        internal bool Active;
        internal float Boost;      // factor at the centre of the view, already capped and weighted
        internal Vector2 Forward;  // XZ, normalised
        internal float HalfAngle;  // degrees, margin included

        // How many times nearer a tile with this XZ box counts. 1 = no boost.
        internal float Factor(Vector3 cam, float minX, float minZ, float maxX, float maxZ)
        {
            if (!Active)
            {
                return 1f;
            }
            var cx = (minX + maxX) * 0.5f - cam.x;
            var cz = (minZ + maxZ) * 0.5f - cam.z;
            var dist = Mathf.Sqrt(cx * cx + cz * cz);
            var radius = 0.5f * Mathf.Sqrt((maxX - minX) * (maxX - minX) + (maxZ - minZ) * (maxZ - minZ));
            if (dist <= radius)
            {
                return Boost; // camera inside or touching the tile
            }
            var cos = (cx * Forward.x + cz * Forward.y) / dist;
            var angle = Mathf.Acos(Mathf.Clamp(cos, -1f, 1f)) * Mathf.Rad2Deg;
            angle -= Mathf.Asin(Mathf.Clamp01(radius / dist)) * Mathf.Rad2Deg;
            if (angle <= HalfAngle)
            {
                return Boost;
            }
            var w = 1f - Mathf.Clamp01((angle - HalfAngle) / Falloff);
            w = w * w * (3f - 2f * w);
            return 1f + (Boost - 1f) * w;
        }

        // Most any tile can get (limits of the search area).
        internal float Max => Active ? Boost : 1f;
    }

    // What a manager last recomputed with; Poll says when a new recompute is worth it.
    internal struct Applied
    {
        internal bool Active;
        internal float Boost;
        internal Vector2 Forward;
        internal float At;
    }

    private static int _readFrame = -1;
    private static State _state;

#if DEBUG
    private static double[] _testSlot;

    // Self test: pretend a spyglass wrote this (null = read the slot). Setting it drop this frame's reading.
    internal static double[] TestSlot
    {
        get => _testSlot;
        set
        {
            _testSlot = value;
            _readFrame = -1;
        }
    }
#endif

    // This frame's state (slot read once per frame). enabled: the SpyglassDetail setting; maxBoost: the cap.
    internal static State Read(bool enabled, float maxBoost)
    {
        if (_readFrame == Time.frameCount)
        {
            return Apply(enabled, maxBoost);
        }
        _readFrame = Time.frameCount;
        _state = default;
        var slot = AppDomain.CurrentDomain.GetData(SlotName) as double[];
#if DEBUG
        if (_testSlot != null)
        {
            slot = _testSlot;
        }
#endif
        if (slot == null || slot.Length < 9 || (int)slot[0] != 1)
        {
            return Apply(enabled, maxBoost);
        }
        var age = Time.frameCount - slot[1];
        var zoom = (float)slot[2];
        var weight = Mathf.Clamp01((float)slot[3]);
        var fwd = new Vector2((float)slot[4], (float)slot[6]);
        if (age < 0 || age > StaleFrames || weight <= 0.01f || zoom <= 1.01f || float.IsNaN(zoom)
            || fwd.sqrMagnitude < 0.04f || float.IsNaN(fwd.x) || float.IsNaN(fwd.y))
        {
            return Apply(enabled, maxBoost);
        }
        _state.Active = true;
        _state.Boost = zoom * weight + (1f - weight); // raw, capped in Apply
        _state.Forward = fwd.normalized;
        _state.HalfAngle = Mathf.Clamp((float)slot[7], 1f, 89f) + Margin;
        return Apply(enabled, maxBoost);
    }

    private static State Apply(bool enabled, float maxBoost)
    {
        if (!enabled || !_state.Active || maxBoost <= 1.01f)
        {
            return default;
        }
        var s = _state;
        s.Boost = Mathf.Clamp(s.Boost, 1f, maxBoost);
        s.Active = s.Boost > 1.01f;
        return s;
    }

    // True when the boost moved enough since `last` to recompute the wanted tiles (turned on or off at once; else
    // view turned by a third of its width or zoom changed by 15 %, at most every MinRecomputeGap s). Updates last.
    internal static bool Poll(ref Applied last, State now, float time)
    {
        if (now.Active != last.Active)
        {
            last = new Applied { Active = now.Active, Boost = now.Boost, Forward = now.Forward, At = time };
            return true;
        }
        if (!now.Active || time - last.At < MinRecomputeGap)
        {
            return false;
        }
        var turned = Vector2.Angle(last.Forward, now.Forward) > Mathf.Max(2f, now.HalfAngle / 3f);
        var zoomed = Mathf.Abs(now.Boost / Mathf.Max(1f, last.Boost) - 1f) > 0.15f;
        if (!turned && !zoomed)
        {
            return false;
        }
        last = new Applied { Active = true, Boost = now.Boost, Forward = now.Forward, At = time };
        return true;
    }
}
