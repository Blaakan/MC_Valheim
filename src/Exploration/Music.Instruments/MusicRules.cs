using System;
using System.Globalization;
using BepInEx.Configuration;

namespace MC.Exploration.MusicInstrumentsMod;

// Me = every gameplay number of the mod in one snapshot. Server send its own to every player (ServerRules): same
// recipes, same comfort bonus, same mini-game bar, same hearing range for everybody. Personal choices (volume, game
// music, lane keys, note speed) and server-only AllowPlayersWithoutMod are not in here. Snapshot never change after
// build: new rules = new object. Pending = client still wait for server rules: built in code, never from config,
// never sent. Game code must check IsPending (recipes hidden, playing refused), not trust the numbers.
internal sealed class MusicRules
{
    // Ranges. Config (Plugin) and wire clamp (TryRead) use same numbers.
    internal const int StationLevelMin = 1;
    internal const int StationLevelMax = 10;
    internal const int ComfortMin = 0;
    internal const int ComfortMax = 10;
    internal const float SuccessSecondsMin = 5f;
    internal const float SuccessSecondsMax = 120f;
    internal const float AccuracyMin = 0.3f;
    internal const float AccuracyMax = 1f;
    internal const float BonusMinutesMin = 1f;
    internal const float BonusMinutesMax = 60f;
    internal const float BonusRangeMin = 3f;
    internal const float BonusRangeMax = 50f;
    internal const float HearingRangeMin = 10f;
    internal const float HearingRangeMax = 80f;
    internal const int TextMax = 1000; // recipe strings from wire: longer = cut

    internal const string Workbench = "piece_workbench";

    // Recipes: "Name:amount,..." item prefab names, station prefab name ("" = by hand), station level.
    internal string FluteResources = "FineWood:4";
    internal string FluteStation = Workbench;
    internal int FluteStationLevel = 1;
    internal string LyreResources = "Silver:2,LinenThread:8";
    internal string LyreStation = Workbench;
    internal int LyreStationLevel = 2;
    internal string TambourineResources = "FineWood:3,LeatherScraps:4";
    internal string TambourineStation = Workbench;
    internal int TambourineStationLevel = 2;

    // Comfort: a good mini-game run of SuccessSeconds (SuccessAccuracy of the notes hit) gives ComfortBonus comfort
    // for BonusMinutes to the performer and every player within BonusRange m; only in shelter unless off.
    internal int ComfortBonus = 3;
    internal float SuccessSeconds = 20f;
    internal float SuccessAccuracy = 0.7f;
    internal float BonusMinutes = 10f;
    internal float BonusRange = 20f;
    internal bool BonusOnlyInShelter = true;

    // Sound: players farther than this hear nothing (and get no notes over the network).
    internal float HearingRange = 40f;

    // True only on Pending: client wait for server rules, recipes hidden, no playing.
    internal bool IsPending { get; private set; }

    // Defaults of the design. Config Bind read them. Never change it (shared object).
    internal static readonly MusicRules Default = new MusicRules();

    // Client of a server with me, before server rules came.
    internal static readonly MusicRules Pending = new MusicRules
    {
        IsPending = true,
        FluteResources = "",
        FluteStation = "",
        LyreResources = "",
        LyreStation = "",
        TambourineResources = "",
        TambourineStation = "",
        ComfortBonus = 0,
    };

    internal string Resources(InstrumentKind kind) => kind switch
    {
        InstrumentKind.Flute => FluteResources,
        InstrumentKind.Lyre => LyreResources,
        InstrumentKind.Tambourine => TambourineResources,
        _ => "",
    };

    internal string Station(InstrumentKind kind) => kind switch
    {
        InstrumentKind.Flute => FluteStation,
        InstrumentKind.Lyre => LyreStation,
        InstrumentKind.Tambourine => TambourineStation,
        _ => "",
    };

    internal int StationLevel(InstrumentKind kind) => kind switch
    {
        InstrumentKind.Flute => FluteStationLevel,
        InstrumentKind.Lyre => LyreStationLevel,
        InstrumentKind.Tambourine => TambourineStationLevel,
        _ => 1,
    };

    // This game's own config. Entry not bound (bind blew up) = default.
    internal static MusicRules Own()
    {
        var d = Default;
        return new MusicRules
        {
            FluteResources = V(Plugin.FluteResources, d.FluteResources) ?? "",
            FluteStation = V(Plugin.FluteStation, d.FluteStation) ?? "",
            FluteStationLevel = V(Plugin.FluteStationLevel, d.FluteStationLevel),
            LyreResources = V(Plugin.LyreResources, d.LyreResources) ?? "",
            LyreStation = V(Plugin.LyreStation, d.LyreStation) ?? "",
            LyreStationLevel = V(Plugin.LyreStationLevel, d.LyreStationLevel),
            TambourineResources = V(Plugin.TambourineResources, d.TambourineResources) ?? "",
            TambourineStation = V(Plugin.TambourineStation, d.TambourineStation) ?? "",
            TambourineStationLevel = V(Plugin.TambourineStationLevel, d.TambourineStationLevel),
            ComfortBonus = V(Plugin.ComfortBonus, d.ComfortBonus),
            SuccessSeconds = V(Plugin.SuccessSeconds, d.SuccessSeconds),
            SuccessAccuracy = V(Plugin.SuccessAccuracy, d.SuccessAccuracy),
            BonusMinutes = V(Plugin.BonusMinutes, d.BonusMinutes),
            BonusRange = V(Plugin.BonusRange, d.BonusRange),
            BonusOnlyInShelter = V(Plugin.BonusOnlyInShelter, d.BonusOnlyInShelter),
            HearingRange = V(Plugin.HearingRange, d.HearingRange),
        };
    }

    // Wire: layout, then every value in fixed order. Layout bump = other order (ModNetworkVersion too).
    internal const int Layout = 1;

    internal void Write(ZPackage pkg)
    {
        pkg.Write(Layout);
        pkg.Write(FluteResources ?? "");
        pkg.Write(FluteStation ?? "");
        pkg.Write(FluteStationLevel);
        pkg.Write(LyreResources ?? "");
        pkg.Write(LyreStation ?? "");
        pkg.Write(LyreStationLevel);
        pkg.Write(TambourineResources ?? "");
        pkg.Write(TambourineStation ?? "");
        pkg.Write(TambourineStationLevel);
        pkg.Write(ComfortBonus);
        pkg.Write(SuccessSeconds);
        pkg.Write(SuccessAccuracy);
        pkg.Write(BonusMinutes);
        pkg.Write(BonusRange);
        pkg.Write(BonusOnlyInShelter);
        pkg.Write(HearingRange);
    }

    // Never trust the wire: unknown layout or broken package = false (caller keep what it had); values outside the
    // config ranges (or not a number) are pulled in (clamped = true).
    internal static bool TryRead(ZPackage pkg, out MusicRules rules, out bool clamped)
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
            var r = new MusicRules();
            r.FluteResources = Text(pkg.ReadString(), ref clamped);
            r.FluteStation = Text(pkg.ReadString(), ref clamped);
            r.FluteStationLevel = Clamp(pkg.ReadInt(), StationLevelMin, StationLevelMax, ref clamped);
            r.LyreResources = Text(pkg.ReadString(), ref clamped);
            r.LyreStation = Text(pkg.ReadString(), ref clamped);
            r.LyreStationLevel = Clamp(pkg.ReadInt(), StationLevelMin, StationLevelMax, ref clamped);
            r.TambourineResources = Text(pkg.ReadString(), ref clamped);
            r.TambourineStation = Text(pkg.ReadString(), ref clamped);
            r.TambourineStationLevel = Clamp(pkg.ReadInt(), StationLevelMin, StationLevelMax, ref clamped);
            r.ComfortBonus = Clamp(pkg.ReadInt(), ComfortMin, ComfortMax, ref clamped);
            r.SuccessSeconds = Clamp(pkg.ReadSingle(), SuccessSecondsMin, SuccessSecondsMax, d.SuccessSeconds, ref clamped);
            r.SuccessAccuracy = Clamp(pkg.ReadSingle(), AccuracyMin, AccuracyMax, d.SuccessAccuracy, ref clamped);
            r.BonusMinutes = Clamp(pkg.ReadSingle(), BonusMinutesMin, BonusMinutesMax, d.BonusMinutes, ref clamped);
            r.BonusRange = Clamp(pkg.ReadSingle(), BonusRangeMin, BonusRangeMax, d.BonusRange, ref clamped);
            r.BonusOnlyInShelter = pkg.ReadBool();
            r.HearingRange = Clamp(pkg.ReadSingle(), HearingRangeMin, HearingRangeMax, d.HearingRange, ref clamped);
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
            return "waiting for the server's rules (no instrument recipes and no playing until they arrive)";
        }
        return "flute " + Recipe(FluteResources, FluteStation, FluteStationLevel)
               + "; lyre " + Recipe(LyreResources, LyreStation, LyreStationLevel)
               + "; tambourine " + Recipe(TambourineResources, TambourineStation, TambourineStationLevel)
               + "; +" + ComfortBonus + " comfort for " + F(BonusMinutes) + " min within " + F(BonusRange) + " m"
               + (BonusOnlyInShelter ? " (in shelter)" : "") + " after " + F(SuccessSeconds) + " s at "
               + F(SuccessAccuracy * 100f) + " % of the notes; heard up to " + F(HearingRange) + " m";
    }

    private static string Recipe(string resources, string station, int level) =>
        resources + " at " + (string.IsNullOrEmpty(station) ? "by hand" : station + " level " + level);

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
