using System;
using System.Globalization;
using BepInEx.Configuration;

namespace MC.Exploration.SwimmingDiveMod;

// Me = every gameplay number of the mod in one snapshot. Server send its own to every player (ServerRules): same
// dive speed, idle rule and stamina for everybody. Personal choices (section Visuals: camera, fog, surface from below)
// and server-only AllowPlayersWithoutMod are not in here. Snapshot never change after build: new rules = new object.
// Pending = client still wait for server rules: built in code, never from config, never sent. Game code must check
// IsPending (dive refused = vanilla swimming), not trust the numbers.
internal sealed class DiveRules
{
    // Ranges. Config (Plugin) and wire clamp (TryRead) use same numbers.
    internal const float DiveSpeedMin = 0.25f;
    internal const float DiveSpeedMax = 3f;
    internal const float IdleRiseMax = 2f;        // m/s
    internal const float StaminaMultiplierMax = 5f;

    // Vertical speed = vanilla swim speed (gear, status effects) x this.
    internal float DiveSpeedMultiplier = 1f;

    // No key under water: 0 = hold depth, else rise this fast (m/s).
    internal float IdleRiseSpeed;

    // Swim stamina drain while diving x this (surface swimming never touched).
    internal float UnderwaterStaminaMultiplier = 1f;

    // True only on Pending: client wait for server rules, feature act vanilla (design 4.3).
    internal bool IsPending { get; private set; }

    // Defaults of the design. Config Bind read them. Never change it (shared object).
    internal static readonly DiveRules Default = new DiveRules();

    // Client of a server with me, before server rules came.
    internal static readonly DiveRules Pending = new DiveRules { IsPending = true };

    // This game's own config. Entry not bound (bind blew up) = default.
    internal static DiveRules Own()
    {
        var d = Default;
        return new DiveRules
        {
            DiveSpeedMultiplier = V(Plugin.DiveSpeedMultiplier, d.DiveSpeedMultiplier),
            IdleRiseSpeed = V(Plugin.IdleRiseSpeed, d.IdleRiseSpeed),
            UnderwaterStaminaMultiplier = V(Plugin.UnderwaterStaminaMultiplier, d.UnderwaterStaminaMultiplier),
        };
    }

    // Wire: layout, then every value in fixed order. Layout bump = other order (ModNetworkVersion too).
    internal const int Layout = 1;

    internal void Write(ZPackage pkg)
    {
        pkg.Write(Layout);
        pkg.Write(DiveSpeedMultiplier);
        pkg.Write(IdleRiseSpeed);
        pkg.Write(UnderwaterStaminaMultiplier);
    }

    // Never trust the wire: unknown layout or broken package = false (caller keep what it had); values outside the
    // config ranges (or not a number) are pulled in (clamped = true).
    internal static bool TryRead(ZPackage pkg, out DiveRules rules, out bool clamped)
    {
        rules = null;
        clamped = false;
        try
        {
            if (pkg == null || pkg.ReadInt() != Layout)
            {
                return false;
            }
            var d = Default;
            var r = new DiveRules();
            r.DiveSpeedMultiplier = Clamp(pkg.ReadSingle(), DiveSpeedMin, DiveSpeedMax, d.DiveSpeedMultiplier, ref clamped);
            r.IdleRiseSpeed = Clamp(pkg.ReadSingle(), 0f, IdleRiseMax, d.IdleRiseSpeed, ref clamped);
            r.UnderwaterStaminaMultiplier = Clamp(pkg.ReadSingle(), 0f, StaminaMultiplierMax,
                d.UnderwaterStaminaMultiplier, ref clamped);
            rules = r;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // One log line with every value (also the "did rules change" test of ServerRules: same text = same rules).
    internal string Describe()
    {
        if (IsPending)
        {
            return "waiting for the server's rules (normal swimming until they arrive)";
        }
        var idle = IdleRiseSpeed <= 0f ? "hold depth" : "rise " + F(IdleRiseSpeed) + " m/s";
        return "dive speed x" + F(DiveSpeedMultiplier) + ", no key under water: " + idle
               + ", stamina drain while diving x" + F(UnderwaterStaminaMultiplier);
    }

    private static T V<T>(ConfigEntry<T> entry, T fallback) => entry != null ? entry.Value : fallback;

    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    // Not a number (NaN, infinity) = default value.
    private static float Clamp(float value, float min, float max, float fallback, ref bool clamped)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            clamped = true;
            return fallback;
        }
        var c = value < min ? min : value > max ? max : value;
        clamped |= c != value;
        return c;
    }
}
