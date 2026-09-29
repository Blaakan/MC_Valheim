using System;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SleepThroughDayMod;

// Me = clock helper. Two clocks, never mix them in one test:
//   flags (EnvMan.IsAfternoon / IsNight, from smoothed fraction) = "does vanilla time test pass?"
//   raw world time (ZNet seconds)                                 = "is this day sleep, when does it end?"
// Me never hardcode 1200, 0.15, 0.85 or 12: Seasons and day-length mods change them. Me ask live game:
// GetDay first (Seasons set its day length in GetDay prefix), then m_dayLengthSec, then inverse of live
// RescaleDayFraction.
internal static class DayClock
{
    internal const float DawnHour = 6f;
    internal const float NoonHour = 12f;
    internal const float NightfallHour = 18f;
    internal const float MinWakeHour = 13f;
    internal const float MaxWakeHour = 23f;

    private const int BisectSteps = 32;
    private const int MonotonicSamples = 48;
    private const float LastRaw = 0.99999f;

    private static bool _warnedFallback;

    // Smoothed flags strictly between 06:00 and noon: exactly where vanilla time test (IsAfternoon || IsNight,
    // in CalculateCanSleep and UpdateSleeping) fail. Two static reads: ok every frame.
    internal static bool IsMorning() => !EnvMan.IsAfternoon() && !EnvMan.IsNight();

    // Setting value, made safe: range 13-23 (BepInEx clamp too), NaN = 18.
    internal static float WakeHour(float configured)
    {
        if (float.IsNaN(configured) || float.IsInfinity(configured))
        {
            return NightfallHour;
        }
        return Mathf.Clamp(configured, MinWakeHour, MaxWakeHour);
    }

    // Game clock fraction (0.25 = 06:00) -> "HH:MM", for logs.
    internal static string ClockText(float fraction)
    {
        if (float.IsNaN(fraction) || float.IsInfinity(fraction))
        {
            return "??:??";
        }
        var minutes = (int)Math.Round(fraction * 1440.0) % 1440;
        if (minutes < 0)
        {
            minutes += 1440;
        }
        return $"{minutes / 60:00}:{minutes % 60:00}";
    }

    internal static string HourText(float hour) => ClockText(hour / 24f);

    // Game clock fraction of raw world time t, as game show it (live rescale, no smoothing lag). For logs and tests.
    internal static float FractionAt(EnvMan env, double t)
    {
        var day = env.GetDay(t);
        var len = (double)env.m_dayLengthSec;
        if (len <= 0.0)
        {
            return float.NaN;
        }
        var raw = Mathf.Clamp01((float)((t - day * len) / len));
        return env.RescaleDayFraction(raw);
    }

    // Raw fraction of day length at which game clock show `hour`: smallest r in [0, 1] with
    // RescaleDayFraction(r) >= hour / 24, by bisection on live method. Vanilla: 6 -> 0.15, 17 -> 0.792, 18 -> 0.85.
    // Method look broken (other mod, NaN, go down) = vanilla inverse + one warning per session.
    internal static float RawFractionForHour(EnvMan env, float hour)
    {
        var target = Mathf.Clamp01(hour / 24f);
        if (TryInvert(env, target, out var raw))
        {
            return raw;
        }
        if (!_warnedFallback)
        {
            _warnedFallback = true;
            Log.Warning("Could not read the day cycle from the game (another mod changed it?); using the vanilla day and night lengths.");
        }
        return VanillaInverse(target);
    }

    // Day window of the date that hold `now` (all raw world seconds):
    //   Dawn        = 06:00 of that date
    //   LatestStart = last moment a sleep may start and still be day sleep: noon when IncludeAfternoon off,
    //                 else one hour before wake hour (never after 18:00)
    //   Target      = wake hour of that date
    internal static DayWindow Today(EnvMan env, double now, float wakeHour, bool includeAfternoon)
    {
        var day = env.GetDay(now); // first: Seasons write its m_dayLengthSec from a GetDay prefix
        var len = (double)env.m_dayLengthSec;
        if (len <= 0.0)
        {
            throw new InvalidOperationException($"day length is {env.m_dayLengthSec}");
        }
        var start = day * len;
        var latestHour = includeAfternoon ? Mathf.Min(wakeHour - 1f, NightfallHour) : NoonHour;
        return new DayWindow(
            day,
            len,
            start,
            start + RawFractionForHour(env, DawnHour) * len,
            start + RawFractionForHour(env, latestHour) * len,
            start + RawFractionForHour(env, wakeHour) * len,
            latestHour,
            wakeHour);
    }

    // Raw time t is 06:00 or later on its own date (client: did this sleep start in the day?).
    internal static bool IsAfterDawn(EnvMan env, double t)
    {
        var day = env.GetDay(t);
        var len = (double)env.m_dayLengthSec;
        return t >= day * len + RawFractionForHour(env, DawnHour) * len;
    }

    private static bool TryInvert(EnvMan env, float target, out float result)
    {
        result = 0f;
        var first = env.RescaleDayFraction(0f);
        var last = env.RescaleDayFraction(LastRaw);
        if (float.IsNaN(first) || float.IsNaN(last) || target < first || target > last)
        {
            return false;
        }

        // Must not go down between two ends, else bisection lie.
        var previous = first;
        for (var i = 1; i <= MonotonicSamples; i++)
        {
            var value = env.RescaleDayFraction(LastRaw * i / MonotonicSamples);
            if (float.IsNaN(value) || value < previous - 1e-5f)
            {
                return false;
            }
            previous = value;
        }

        if (first >= target)
        {
            return true; // result 0
        }
        var lo = 0f;
        var hi = LastRaw;
        for (var i = 0; i < BisectSteps; i++)
        {
            var mid = (lo + hi) * 0.5f;
            if (env.RescaleDayFraction(mid) >= target)
            {
                hi = mid;
            }
            else
            {
                lo = mid;
            }
        }
        result = hi;
        return true;
    }

    // Inverse of vanilla EnvMan.RescaleDayFraction (night 30 %, day 70 %).
    private static float VanillaInverse(float fraction)
    {
        if (fraction < 0.25f)
        {
            return fraction * 0.6f;
        }
        if (fraction <= 0.75f)
        {
            return 0.15f + (fraction - 0.25f) * 1.4f;
        }
        return 0.85f + (fraction - 0.75f) * 0.6f;
    }
}

// Me = one date's day window, raw world seconds (double).
internal readonly struct DayWindow
{
    internal DayWindow(int day, double dayLength, double dayStart, double dawn, double latestStart, double target,
        float latestStartHour, float wakeHour)
    {
        Day = day;
        DayLength = dayLength;
        DayStart = dayStart;
        Dawn = dawn;
        LatestStart = latestStart;
        Target = target;
        LatestStartHour = latestStartHour;
        WakeHour = wakeHour;
    }

    internal int Day { get; }
    internal double DayLength { get; }
    internal double DayStart { get; }
    internal double Dawn { get; }
    internal double LatestStart { get; }
    internal double Target { get; }
    internal float LatestStartHour { get; }
    internal float WakeHour { get; }

    // Sleep starting at `now` is day sleep (judged at start, raw time).
    internal bool InWindow(double now) => now >= Dawn && now <= LatestStart;
}
