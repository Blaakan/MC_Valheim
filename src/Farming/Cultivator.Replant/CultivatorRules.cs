using System;
using System.Globalization;
using BepInEx.Configuration;

namespace MC.Farming.CultivatorReplantMod;

// Me = every gameplay number of the mod in one snapshot. Server send its own to every player (ServerRules): same
// upgrade costs, same grow time, same root sap cost and range for everybody. Server-only AllowPlayersWithoutMod is not
// in here. Snapshot never change after build: new rules = new object. Pending = client still wait for server rules:
// built in code, never from config, never sent. Game code must check IsPending (saplings hidden, cultivator vanilla,
// Replant refused, roots never drained), not trust the numbers.
internal sealed class CultivatorRules
{
    // Ranges. Config (Plugin) and wire clamp (TryRead) use same numbers.
    internal const float GrowTimeMultiplierMin = 0.1f;
    internal const float GrowTimeMultiplierMax = 10f;
    // Root hold 50 and ResourceRoot.CanDrain want level > cost: 49 = most a full root can give.
    internal const int RootSapCostMin = 1;
    internal const int RootSapCostMax = 49;
    // Placement want root mesh closer than RootRange, growth want it farther than sapling grow radius (Yggdrasil
    // GrowRadius 2, Plant.HaveGrowSpace see root as blocker). Min must stay above that radius plus a band wide enough
    // to plant in: 4 = 2 m band. Lower = green ghost, sapling "needs more room" forever. Const: config range want it.
    internal const float RootRangeMin = 4f;
    internal const float RootRangeMax = 20f;
    internal const int TextMax = 1000; // cost strings from wire: longer = cut

    internal const string DefaultBlackMetalLevel = "BlackMetal:5,LinenThread:10";
    internal const string DefaultEitrLevel = "Eitr:15";
    internal const string DefaultFlametalLevel = "FlametalNew:5";
    internal const string DefaultBloodgoldLevel = "Gold:5";

    // Upgrade cost from the level before, "Name:amount,..." item prefab names. Level 4 black metal, 5 eitr,
    // 6 flametal, 7 bloodgold. No valid material = cultivator stop below that level (CultivatorTiers).
    internal string BlackMetalLevel = DefaultBlackMetalLevel;
    internal string EitrLevel = DefaultEitrLevel;
    internal string FlametalLevel = DefaultFlametalLevel;
    internal string BloodgoldLevel = DefaultBloodgoldLevel;

    // Transplant grow time = plant's own time (PlantCatalog.GrowMinutes) times this.
    internal float GrowTimeMultiplier = 1f;

    // Sap a Yggdrasil transplant take from its Ancient Root when it grow.
    internal int RootSapCost = 20;

    // How near an Ancient Root a Yggdrasil transplant must be planted (Piece.m_connectRadius), metres. Also must be
    // farther than its grow radius from root, or no room to grow (see RootRangeMin).
    internal float RootRange = 6f;

    // True only on Pending: client wait for server rules, everything of me stay vanilla.
    internal bool IsPending { get; private set; }

    // Defaults of the design. Config Bind read them. Never change it (shared object).
    internal static readonly CultivatorRules Default = new CultivatorRules();

    // Client of a server with me, before server rules came.
    internal static readonly CultivatorRules Pending = new CultivatorRules
    {
        IsPending = true,
        BlackMetalLevel = "",
        EitrLevel = "",
        FlametalLevel = "",
        BloodgoldLevel = "",
    };

    // This game's own config. Entry not bound (bind blew up) = default.
    internal static CultivatorRules Own()
    {
        var d = Default;
        return new CultivatorRules
        {
            BlackMetalLevel = V(Plugin.BlackMetalLevel, d.BlackMetalLevel) ?? "",
            EitrLevel = V(Plugin.EitrLevel, d.EitrLevel) ?? "",
            FlametalLevel = V(Plugin.FlametalLevel, d.FlametalLevel) ?? "",
            BloodgoldLevel = V(Plugin.BloodgoldLevel, d.BloodgoldLevel) ?? "",
            GrowTimeMultiplier = V(Plugin.GrowTimeMultiplier, d.GrowTimeMultiplier),
            RootSapCost = V(Plugin.RootSapCost, d.RootSapCost),
            RootRange = V(Plugin.RootRange, d.RootRange),
        };
    }

    // Cost string of the upgrade TO this quality (4..7). Other quality = "" (vanilla levels, no tier).
    internal string CostOf(int quality)
    {
        switch (quality)
        {
            case 4:
                return BlackMetalLevel ?? "";
            case 5:
                return EitrLevel ?? "";
            case 6:
                return FlametalLevel ?? "";
            case 7:
                return BloodgoldLevel ?? "";
            default:
                return "";
        }
    }

    // Wire: layout, then every value in fixed order. Layout bump = other order (ModNetworkVersion too).
    internal const int Layout = 1;

    internal void Write(ZPackage pkg)
    {
        pkg.Write(Layout);
        pkg.Write(BlackMetalLevel ?? "");
        pkg.Write(EitrLevel ?? "");
        pkg.Write(FlametalLevel ?? "");
        pkg.Write(BloodgoldLevel ?? "");
        pkg.Write(GrowTimeMultiplier);
        pkg.Write(RootSapCost);
        pkg.Write(RootRange);
    }

    // Never trust the wire: unknown layout or broken package = false (caller keep what it had); values outside the
    // config ranges (or not a number) are pulled in (clamped = true).
    internal static bool TryRead(ZPackage pkg, out CultivatorRules rules, out bool clamped)
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
            var r = new CultivatorRules();
            r.BlackMetalLevel = Text(pkg.ReadString(), ref clamped);
            r.EitrLevel = Text(pkg.ReadString(), ref clamped);
            r.FlametalLevel = Text(pkg.ReadString(), ref clamped);
            r.BloodgoldLevel = Text(pkg.ReadString(), ref clamped);
            r.GrowTimeMultiplier = Clamp(pkg.ReadSingle(), GrowTimeMultiplierMin, GrowTimeMultiplierMax,
                d.GrowTimeMultiplier, ref clamped);
            r.RootSapCost = Clamp(pkg.ReadInt(), RootSapCostMin, RootSapCostMax, ref clamped);
            r.RootRange = Clamp(pkg.ReadSingle(), RootRangeMin, RootRangeMax, d.RootRange, ref clamped);
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
            return "waiting for the server's rules (no cultivator levels above 3, no transplant saplings and no "
                   + "replanting until they arrive)";
        }
        return "black metal level " + Cost(BlackMetalLevel) + ", eitr level " + Cost(EitrLevel)
               + ", flametal level " + Cost(FlametalLevel) + ", bloodgold level " + Cost(BloodgoldLevel)
               + ", grow time x" + F(GrowTimeMultiplier) + ", root sap cost " + RootSapCost
               + ", root range " + F(RootRange) + " m";
    }

    private static string Cost(string value) => string.IsNullOrEmpty(value) ? "(none)" : value;

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

    private static int Clamp(int value, int min, int max, ref bool clamped)
    {
        var c = value < min ? min : value > max ? max : value;
        clamped |= c != value;
        return c;
    }

    private static string Text(string value, ref bool clamped)
    {
        if (value == null)
        {
            return "";
        }
        if (value.Length > TextMax)
        {
            clamped = true;
            return value.Substring(0, TextMax);
        }
        return value;
    }
}
