using System;

namespace MC.Farming.FishingFightMod;

// Me = the catch bar minigame of the calm phase, pure maths (no Unity object: self test step it alone). Track 0..1,
// 0 = bottom. Zone = the bar the player move: hold reel = it climb, let go = it fall and bounce a bit on the bottom.
// Fish = marker that wander on its own, wild as its difficulty. Fish centre inside the zone = InZone (Fight reel line
// for free); outside = stamina drain, no line.
// Maths = Stardew Valley's BobberBar (1.5) turned from 60 ticks per second and pixels into seconds and track parts:
// same chances per second, same speeds. Fish track 548 px there, so 1 px = 1/548 of the track.
internal sealed class CatchBar
{
    private const float Ticks = 60f;
    private const float TrackPx = 548f;

    // Zone push while held / fall when let go, track per s^2 (Stardew 0.25 px per tick^2 on a 568 px bar track).
    internal const float Lift = 1.6f;
    internal const float Gravity = 1.6f;

    // Fish inside the zone: zone push and fall softer (Stardew x0.6), easier to keep it.
    internal const float InZoneAccelScale = 0.6f;

    // Zone hit bottom: bounce back up with this part of its speed (Stardew 2/3).
    internal const float Bounce = 2f / 3f;

    // Sinker / floater drift: speed change per s^2 and cap per s (Stardew 0.01 px per tick^2, 1.5 px per tick).
    private const float DriftAccel = 0.01f * Ticks * Ticks / TrackPx;
    private const float DriftMax = 1.5f * Ticks / TrackPx;

    // Fish at target: closer than this (Stardew 3 px).
    private const float TargetReached = 3f / TrackPx;

    // Stardew ease of fish speed: 1/5 of the gap per tick = this rate per second.
    private static readonly float SpeedEase = -(float)Math.Log(0.8) * Ticks;

    private readonly Random _rng;
    private float _target = -1f;
    private float _drift;

    internal readonly float Difficulty;
    internal readonly FishMotion Motion;

    internal float ZoneSize;
    internal float ZonePos;
    internal float ZoneSpeed;
    internal float FishPos;
    internal float FishSpeed;

    internal CatchBar(FishProfile profile, float zoneSize, Random rng)
    {
        _rng = rng ?? new Random();
        Difficulty = Math.Max(0f, Math.Min(100f, profile.Difficulty));
        Motion = profile.Motion;
        ZoneSize = Clamp(zoneSize, 0.05f, 1f);
        ZonePos = 0f;
        // Stardew start: easy fish near the bottom (inside the zone), hard fish high.
        FishPos = Clamp(0.05f + Difficulty / 100f * 0.9f, 0f, 1f);
    }

    internal bool InZone => FishPos >= ZonePos && FishPos <= ZonePos + ZoneSize;

    internal void Step(float dt, bool hold)
    {
        if (dt <= 0f)
        {
            return;
        }
        StepFish(dt);
        StepZone(dt, hold);
    }

    // Zone height change (skill went up mid fight): keep bottom, keep inside track.
    internal void Resize(float zoneSize)
    {
        ZoneSize = Clamp(zoneSize, 0.05f, 1f);
        ZonePos = Clamp(ZonePos, 0f, 1f - ZoneSize);
    }

    private void StepZone(float dt, bool hold)
    {
        var top = 1f - ZoneSize;
        var accel = hold ? Lift : -Gravity;
        // Stardew: press at a wall = stand still first (no slow start from a falling speed).
        if (hold && (ZonePos <= 0f || ZonePos >= top) && ZoneSpeed < 0f)
        {
            ZoneSpeed = 0f;
        }
        if (InZone)
        {
            accel *= InZoneAccelScale;
        }
        ZoneSpeed += accel * dt;
        ZonePos += ZoneSpeed * dt;
        if (ZonePos <= 0f)
        {
            ZonePos = 0f;
            ZoneSpeed = ZoneSpeed < 0f ? -ZoneSpeed * Bounce : ZoneSpeed;
            // Tiny bounce never end alone: settle.
            if (ZoneSpeed < 0.05f)
            {
                ZoneSpeed = 0f;
            }
        }
        else if (ZonePos >= top)
        {
            ZonePos = top;
            if (ZoneSpeed > 0f)
            {
                ZoneSpeed = 0f;
            }
        }
    }

    private void StepFish(float dt)
    {
        var d = Difficulty;
        var smooth = Motion == FishMotion.Smooth;

        // New target now and then (smooth fish: more often, only when it has none).
        var newTargetRate = d * (smooth ? 20f : 1f) / 4000f * Ticks;
        if ((!smooth || _target < 0f) && Chance(newTargetRate, dt))
        {
            var percent = Math.Min(99f, d + Range(10f, 45f)) / 100f;
            _target = Clamp(FishPos + Range(-FishPos, 1f - FishPos) * percent, 0f, 1f);
        }

        if (_target >= 0f && Math.Abs(_target - FishPos) > TargetReached)
        {
            var want = (_target - FishPos) * Ticks / (Range(10f, 30f) + 100f - d);
            FishSpeed += (want - FishSpeed) * Ease(SpeedEase, dt);
        }
        else if (!smooth && Chance(d / 2000f * Ticks, dt))
        {
            _target = Clamp(FishPos + Sign() * Range(50f, 100f) / TrackPx, 0f, 1f);
        }
        else
        {
            _target = -1f;
        }

        if (Motion == FishMotion.Dart && Chance(d / 1000f * Ticks, dt))
        {
            _target = Clamp(FishPos + Sign() * Range(50f, 100f + 2f * d) / TrackPx, 0f, 1f);
        }

        if (Motion == FishMotion.Sinker)
        {
            _drift = Math.Max(_drift - DriftAccel * dt, -DriftMax);
        }
        else if (Motion == FishMotion.Floater)
        {
            _drift = Math.Min(_drift + DriftAccel * dt, DriftMax);
        }

        FishPos += (FishSpeed + _drift) * dt;
        if (FishPos < 0f)
        {
            FishPos = 0f;
            FishSpeed = Math.Max(0f, FishSpeed);
        }
        else if (FishPos > 1f)
        {
            FishPos = 1f;
            FishSpeed = Math.Min(0f, FishSpeed);
        }
    }

    // Event with this rate per second happen in dt.
    private bool Chance(float ratePerSecond, float dt) =>
        ratePerSecond > 0f && _rng.NextDouble() < 1.0 - Math.Exp(-ratePerSecond * dt);

    private float Range(float min, float max) => min + (float)_rng.NextDouble() * (max - min);

    private float Sign() => _rng.NextDouble() < 0.5 ? -1f : 1f;

    private static float Ease(float rate, float dt) => 1f - (float)Math.Exp(-rate * dt);

    private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
}
