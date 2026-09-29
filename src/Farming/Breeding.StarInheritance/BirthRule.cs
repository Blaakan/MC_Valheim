using System;

namespace MC.Farming.BreedingStarInheritanceMod;

// Me = rule settings, read fresh at every birth (nothing cached). Chances in percent.
internal readonly struct RuleSettings
{
    internal readonly float ChanceAtFarming0;
    internal readonly float ChanceAtFarming100;
    internal readonly float ChanceWithoutFarmer;
    internal readonly int MaxStars;

    internal RuleSettings(float chanceAtFarming0, float chanceAtFarming100, float chanceWithoutFarmer, int maxStars)
    {
        ChanceAtFarming0 = chanceAtFarming0;
        ChanceAtFarming100 = chanceAtFarming100;
        ChanceWithoutFarmer = chanceWithoutFarmer;
        MaxStars = maxStars;
    }

    // Same numbers as config defaults. Self test use them too.
    internal static RuleSettings Defaults => new RuleSettings(15f, 50f, 10f, 2);
}

// Me = what rule decided for one birth. Chance in percent (0 when at cap: no roll).
internal readonly struct BirthDecision
{
    internal readonly int Level;
    internal readonly int Base;
    internal readonly int Cap;
    internal readonly float Chance;
    internal readonly float Roll;
    internal readonly bool Bonus;
    internal readonly bool AtCap;

    internal BirthDecision(int level, int baseLevel, int cap, float chance, float roll, bool bonus, bool atCap)
    {
        Level = level;
        Base = baseLevel;
        Cap = cap;
        Chance = chance;
        Roll = roll;
        Bonus = bonus;
        AtCap = atCap;
    }
}

// Me = the birth rule. Pure: no Unity call, no state. Caller bring the roll (0..1), so self test can hammer me.
//   base   = max(minOffspring, no partner ? own : min(own, partner))
//   cap    = MaxStars + 1 (level, not stars)
//   chance = 0 at cap / ChanceWithoutFarmer when no farmer / line from Farming 0 to Farming 100
//   level  = base + 1 when roll win, else base. Me never lower level to cap: cap only stop the bonus.
internal static class BirthRule
{
    // Partner level 0 (or less) = no partner known (or bred alone).
    internal const int NoPartner = 0;

    internal static int BaseLevel(int own, int partner, int minOffspring)
    {
        var lower = partner > NoPartner ? Math.Min(own, partner) : own;
        return Math.Max(minOffspring, lower);
    }

    internal static int Cap(int maxStars)
    {
        return Math.Max(0, maxStars) + 1;
    }

    // Percent. Farming above 100 count as 100, below 0 as 0. Settings clamped 0..100 (NaN = 0).
    internal static float Chance(bool farmerKnown, float farmingLevel, in RuleSettings settings)
    {
        if (!farmerKnown)
        {
            return ClampPercent(settings.ChanceWithoutFarmer);
        }
        var at0 = ClampPercent(settings.ChanceAtFarming0);
        var at100 = ClampPercent(settings.ChanceAtFarming100);
        return at0 + (at100 - at0) * Clamp01(farmingLevel / 100f);
    }

    // 100 always win, 0 never (roll can be exactly 0 or 1). Roll must be 0..1.
    internal static bool Wins(float chancePercent, float roll)
    {
        if (chancePercent >= 100f)
        {
            return true;
        }
        if (!(chancePercent > 0f))
        {
            return false;
        }
        return roll < chancePercent / 100f;
    }

    internal static BirthDecision Decide(int own, int partner, int minOffspring, in RuleSettings settings,
        bool farmerKnown, float farmingLevel, float roll)
    {
        var baseLevel = BaseLevel(own, partner, minOffspring);
        var cap = Cap(settings.MaxStars);
        if (baseLevel >= cap)
        {
            return new BirthDecision(baseLevel, baseLevel, cap, 0f, roll, bonus: false, atCap: true);
        }
        var chance = Chance(farmerKnown, farmingLevel, settings);
        var bonus = Wins(chance, roll);
        return new BirthDecision(bonus ? baseLevel + 1 : baseLevel, baseLevel, cap, chance, roll, bonus, atCap: false);
    }

    private static float Clamp01(float value)
    {
        if (!(value > 0f))
        {
            return 0f; // also NaN
        }
        return value > 1f ? 1f : value;
    }

    private static float ClampPercent(float value)
    {
        if (!(value > 0f))
        {
            return 0f;
        }
        return value > 100f ? 100f : value;
    }
}
