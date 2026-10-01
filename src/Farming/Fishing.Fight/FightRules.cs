using System;
using System.Globalization;
using BepInEx.Configuration;

namespace MC.Farming.FishingFightMod;

// Me = every gameplay number of the fight in one snapshot. Server send its own to every player (ServerRules): same
// bar, same stamina costs, same fish for everybody. Personal choices (section Display: where the bar sit, how big,
// struggle arrow) and server-only AllowPlayersWithoutMod are not in here. Snapshot never change after build: new
// rules = new object. Pending = client still wait for server rules: built in code, never from config, never sent.
// Game code must check IsPending (vanilla reel), not trust the numbers.
internal sealed class FightRules
{
    // Ranges. Config (Plugin) and wire clamp (TryRead) use same numbers.
    internal const float BarSizeMin = 0.1f;
    internal const float BarSizeMax = 0.8f;
    internal const float DifficultyMin = 0.25f;
    internal const float DifficultyMax = 2f;
    internal const float StaminaMultiplierMax = 5f;
    internal const float WrongSideMin = 1f;
    internal const float WrongSideMax = 10f;
    internal const float RodAngleMin = 10f;
    internal const float RodAngleMax = 90f;
    internal const float CalmSecondsMin = 2f;
    internal const float CalmSecondsMax = 30f;
    internal const float StruggleSecondsMin = 1f;
    internal const float StruggleSecondsMax = 10f;
    internal const float LineRunSpeedMax = 5f;   // m/s
    internal const float ReelSpeedMin = 0.25f;
    internal const float ReelSpeedMax = 4f;

    // Catch bar height, part of the track, at Fishing 0 and at Fishing 100 (lerp between).
    internal float BarSize = 0.24f;
    internal float BarSizeAtMaxSkill = 0.38f;

    // Fish difficulty x this: how wild the fish move in the bar, how often and how long they fight, how fast they take
    // line.
    internal float FishDifficulty = 1f;

    // Fish outside the bar: stamina per second = vanilla reel cost x this.
    internal float OffBarStamina = 1f;

    // Struggle, reel with rod on the good side: vanilla reel cost x this.
    internal float StruggleStamina = 1f;

    // Struggle, reel with rod on the bad side: good-side cost x this, and no line come in.
    internal float WrongSideStamina = 4f;

    // Rod must point at least this many degrees off the line, on the side away from the run.
    internal float RodAngle = 30f;

    // Mean calm time between fights, mean fight time (s). Fish and stars change them (FightLogic).
    internal float CalmSeconds = 7f;
    internal float StruggleSeconds = 3f;

    // Struggle, no reel: fish take line this fast (m/s) for a middle fish.
    internal float LineRunSpeed = 1.5f;

    // Line in speed x this (vanilla reel speed: 1 m/s at Fishing 0, 2 m/s at 100).
    internal float ReelSpeed = 1f;

    // True only on Pending: client wait for server rules, fight stay vanilla (design 4.3).
    internal bool IsPending { get; private set; }

    // Defaults of the design. Config Bind read them. Never change it (shared object).
    internal static readonly FightRules Default = new FightRules();

    // Client of a server with me, before server rules came.
    internal static readonly FightRules Pending = new FightRules { IsPending = true };

    // This game's own config. Entry not bound (bind blew up) = default.
    internal static FightRules Own()
    {
        var d = Default;
        return new FightRules
        {
            BarSize = V(Plugin.BarSize, d.BarSize),
            BarSizeAtMaxSkill = V(Plugin.BarSizeAtMaxSkill, d.BarSizeAtMaxSkill),
            FishDifficulty = V(Plugin.FishDifficulty, d.FishDifficulty),
            OffBarStamina = V(Plugin.OffBarStamina, d.OffBarStamina),
            StruggleStamina = V(Plugin.StruggleStamina, d.StruggleStamina),
            WrongSideStamina = V(Plugin.WrongSideStamina, d.WrongSideStamina),
            RodAngle = V(Plugin.RodAngle, d.RodAngle),
            CalmSeconds = V(Plugin.CalmSeconds, d.CalmSeconds),
            StruggleSeconds = V(Plugin.StruggleSeconds, d.StruggleSeconds),
            LineRunSpeed = V(Plugin.LineRunSpeed, d.LineRunSpeed),
            ReelSpeed = V(Plugin.ReelSpeed, d.ReelSpeed),
        };
    }

    // Wire: layout, then every value in fixed order. Layout bump = other order (ModNetworkVersion too).
    internal const int Layout = 1;

    internal void Write(ZPackage pkg)
    {
        pkg.Write(Layout);
        pkg.Write(BarSize);
        pkg.Write(BarSizeAtMaxSkill);
        pkg.Write(FishDifficulty);
        pkg.Write(OffBarStamina);
        pkg.Write(StruggleStamina);
        pkg.Write(WrongSideStamina);
        pkg.Write(RodAngle);
        pkg.Write(CalmSeconds);
        pkg.Write(StruggleSeconds);
        pkg.Write(LineRunSpeed);
        pkg.Write(ReelSpeed);
    }

    // Never trust the wire: unknown layout or broken package = false (caller keep what it had); values outside the
    // config ranges (or not a number) are pulled in (clamped = true).
    internal static bool TryRead(ZPackage pkg, out FightRules rules, out bool clamped)
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
            var r = new FightRules();
            r.BarSize = Clamp(pkg.ReadSingle(), BarSizeMin, BarSizeMax, d.BarSize, ref clamped);
            r.BarSizeAtMaxSkill = Clamp(pkg.ReadSingle(), BarSizeMin, BarSizeMax, d.BarSizeAtMaxSkill, ref clamped);
            r.FishDifficulty = Clamp(pkg.ReadSingle(), DifficultyMin, DifficultyMax, d.FishDifficulty, ref clamped);
            r.OffBarStamina = Clamp(pkg.ReadSingle(), 0f, StaminaMultiplierMax, d.OffBarStamina, ref clamped);
            r.StruggleStamina = Clamp(pkg.ReadSingle(), 0f, StaminaMultiplierMax, d.StruggleStamina, ref clamped);
            r.WrongSideStamina = Clamp(pkg.ReadSingle(), WrongSideMin, WrongSideMax, d.WrongSideStamina, ref clamped);
            r.RodAngle = Clamp(pkg.ReadSingle(), RodAngleMin, RodAngleMax, d.RodAngle, ref clamped);
            r.CalmSeconds = Clamp(pkg.ReadSingle(), CalmSecondsMin, CalmSecondsMax, d.CalmSeconds, ref clamped);
            r.StruggleSeconds = Clamp(pkg.ReadSingle(), StruggleSecondsMin, StruggleSecondsMax, d.StruggleSeconds,
                ref clamped);
            r.LineRunSpeed = Clamp(pkg.ReadSingle(), 0f, LineRunSpeedMax, d.LineRunSpeed, ref clamped);
            r.ReelSpeed = Clamp(pkg.ReadSingle(), ReelSpeedMin, ReelSpeedMax, d.ReelSpeed, ref clamped);
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
            return "waiting for the server's rules (normal fishing until they arrive)";
        }
        return "catch bar " + P(BarSize) + " to " + P(BarSizeAtMaxSkill) + " of the track, fish difficulty x"
               + F(FishDifficulty) + ", stamina off the bar x" + F(OffBarStamina) + ", reeling in a fight x"
               + F(StruggleStamina) + " (wrong side x" + F(WrongSideStamina) + "), rod at least " + F(RodAngle)
               + " degrees off the line, calm about " + F(CalmSeconds) + " s, fights about " + F(StruggleSeconds)
               + " s, fish take line at " + F(LineRunSpeed) + " m/s, reel speed x" + F(ReelSpeed);
    }

    private static T V<T>(ConfigEntry<T> entry, T fallback) => entry != null ? entry.Value : fallback;

    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string P(float value) => (value * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";

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
