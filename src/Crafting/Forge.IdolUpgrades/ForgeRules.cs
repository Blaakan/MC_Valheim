using System;
using System.Text;
using UnityEngine;

namespace MC.Crafting.ForgeIdolUpgradesMod;

// What a failed refinement cost besides the idol.
internal enum FailureMode
{
    LoseLevels,
    Destroy,
}

// Me = every gameplay number of the mod in one snapshot: chances, failure, upgrade costs, tier lists. Server send its
// own to every player (ServerRules): same Forge for everybody. Personal choice (IdolChoice) is not in here.
internal sealed class ForgeRules
{
    internal const int MaxLevelsLost = 100;
    internal const int MaxCost = 100;
    internal const int MaxLevel = 1000;

    internal readonly int[] Chance = new int[IdolLevels.Max + 1];      // percent, level 0..3
    internal FailureMode Failure;
    internal int LevelsLost;                                           // 1..MaxLevelsLost
    // Idol tier by item level (IdolTierRule). Field defaults = config defaults, so a test snapshot start sane.
    internal bool TierByLevel = true;
    internal int BaseLevels = 5;                                       // levels 1..BaseLevels: the recipe's own idol
    internal int LevelsPerTier = 4;                                    // then one tier up every this many levels
    internal readonly int[] Material = new int[IdolLevels.Max + 1];    // index = target level 1..3
    internal readonly int[] Trophies = new int[IdolLevels.Max + 1];
    internal readonly string[] TierMaterial = new string[IdolTierDefaults.Count];
    internal readonly string[] TierCommon = new string[IdolTierDefaults.Count];
    internal readonly string[] TierElite = new string[IdolTierDefaults.Count];
    internal readonly string[] TierBoss = new string[IdolTierDefaults.Count];

    // This game's own config.
    internal static ForgeRules Own()
    {
        var r = new ForgeRules();
        for (var level = 0; level <= IdolLevels.Max; level++)
        {
            r.Chance[level] = Plugin.Chance[level].Value;
        }
        r.Failure = Plugin.Failure.Value;
        r.LevelsLost = Plugin.LevelsLost.Value;
        r.TierByLevel = Plugin.TierByLevel.Value;
        r.BaseLevels = Plugin.BaseLevels.Value;
        r.LevelsPerTier = Plugin.LevelsPerTier.Value;
        for (var level = 1; level <= IdolLevels.Max; level++)
        {
            r.Material[level] = Plugin.MaterialCost[level].Value;
            r.Trophies[level] = Plugin.TrophyCost[level].Value;
        }
        for (var t = 0; t < IdolTierDefaults.Count; t++)
        {
            r.TierMaterial[t] = Plugin.Tiers[t].Material.Value;
            r.TierCommon[t] = Plugin.Tiers[t].Base.Value;
            r.TierElite[t] = Plugin.Tiers[t].Elite.Value;
            r.TierBoss[t] = Plugin.Tiers[t].Boss.Value;
        }
        return r;
    }

    // Wire: version, then every value in fixed order. Version bump = other layout (ModNetworkVersion too).
    // Layout 2: idol tier by level (after LevelsLost).
    internal const int Layout = 2;

    internal void Write(ZPackage pkg)
    {
        pkg.Write(Layout);
        foreach (var c in Chance)
        {
            pkg.Write(c);
        }
        pkg.Write((int)Failure);
        pkg.Write(LevelsLost);
        pkg.Write(TierByLevel);
        pkg.Write(BaseLevels);
        pkg.Write(LevelsPerTier);
        for (var level = 1; level <= IdolLevels.Max; level++)
        {
            pkg.Write(Material[level]);
            pkg.Write(Trophies[level]);
        }
        for (var t = 0; t < IdolTierDefaults.Count; t++)
        {
            pkg.Write(TierMaterial[t] ?? "");
            pkg.Write(TierCommon[t] ?? "");
            pkg.Write(TierElite[t] ?? "");
            pkg.Write(TierBoss[t] ?? "");
        }
    }

    // Never trust the wire: unknown layout or broken package = false (caller keep what it had); values outside the
    // config ranges are pulled in (clamped = true).
    internal static bool TryRead(ZPackage pkg, out ForgeRules rules, out bool clamped)
    {
        rules = null;
        clamped = false;
        try
        {
            if (pkg == null || pkg.ReadInt() != Layout)
            {
                return false;
            }
            var r = new ForgeRules();
            for (var level = 0; level <= IdolLevels.Max; level++)
            {
                r.Chance[level] = Clamp(pkg.ReadInt(), 0, 100, ref clamped);
            }
            var failure = pkg.ReadInt();
            if (!Enum.IsDefined(typeof(FailureMode), failure))
            {
                clamped = true;
                failure = (int)FailureMode.LoseLevels;
            }
            r.Failure = (FailureMode)failure;
            r.LevelsLost = Clamp(pkg.ReadInt(), 1, MaxLevelsLost, ref clamped);
            r.TierByLevel = pkg.ReadBool();
            r.BaseLevels = Clamp(pkg.ReadInt(), 1, MaxLevel, ref clamped);
            r.LevelsPerTier = Clamp(pkg.ReadInt(), 1, MaxLevel, ref clamped);
            for (var level = 1; level <= IdolLevels.Max; level++)
            {
                r.Material[level] = Clamp(pkg.ReadInt(), 0, MaxCost, ref clamped);
                r.Trophies[level] = Clamp(pkg.ReadInt(), 0, MaxCost, ref clamped);
            }
            for (var t = 0; t < IdolTierDefaults.Count; t++)
            {
                r.TierMaterial[t] = pkg.ReadString();
                r.TierCommon[t] = pkg.ReadString();
                r.TierElite[t] = pkg.ReadString();
                r.TierBoss[t] = pkg.ReadString();
            }
            rules = r;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Same tier lists = idol table need no rebuild.
    internal bool SameTiers(ForgeRules other)
    {
        if (other == null)
        {
            return false;
        }
        for (var t = 0; t < IdolTierDefaults.Count; t++)
        {
            if (TierMaterial[t] != other.TierMaterial[t] || TierCommon[t] != other.TierCommon[t]
                || TierElite[t] != other.TierElite[t] || TierBoss[t] != other.TierBoss[t])
            {
                return false;
            }
        }
        return true;
    }

    // One log line (the tier lists are long: only say whether they are the defaults).
    internal string Describe()
    {
        var sb = new StringBuilder();
        sb.Append("chances ").Append(Chance[0]).Append('/').Append(Chance[1]).Append('/').Append(Chance[2]).Append('/')
            .Append(Chance[3]).Append("%, failure ")
            .Append(Failure == FailureMode.Destroy ? "destroys the item" : "loses " + LevelsLost + " level(s)")
            .Append(TierByLevel
                ? ", idol one tier higher from level " + (BaseLevels + 1) + ", then every " + LevelsPerTier + " level(s)"
                : ", idol tier fixed")
            .Append(", costs ").Append(Material[1]).Append('+').Append(Trophies[1]).Append(", ")
            .Append(Material[2]).Append('+').Append(Trophies[2]).Append(", ").Append(Material[3]).Append('+')
            .Append(Trophies[3]);
        var defaults = true;
        foreach (var d in IdolTierDefaults.All)
        {
            defaults &= TierMaterial[d.Tier] == d.Material && TierCommon[d.Tier] == d.Base && TierElite[d.Tier] == d.Elite
                        && TierBoss[d.Tier] == d.Boss;
        }
        sb.Append(defaults ? ", default trophy lists" : ", custom trophy lists");
        return sb.ToString();
    }

    private static int Clamp(int value, int min, int max, ref bool clamped)
    {
        var c = Mathf.Clamp(value, min, max);
        clamped |= c != value;
        return c;
    }
}
