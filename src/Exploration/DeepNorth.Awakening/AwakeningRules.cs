using System;
using System.Globalization;
using BepInEx.Configuration;

namespace MC.Exploration.DeepNorthAwakeningMod;

// Me = every gameplay number the players need, in one snapshot. Server send its own to every player (ServerRules): same
// areas, same stars, same storms for everybody. Server-only settings (AllowPlayersWithoutMod, InvasionsAtThirdStone)
// are not in here. Snapshot never change after build: new rules = new object.
// Pending = client still wait for server rules: built in code, never from config, never sent. Game code must check
// IsPending (= act vanilla), not trust the numbers.
internal sealed class AwakeningRules
{
    // Ranges. Config (Plugin) and wire clamp (TryRead) use same numbers.
    internal const int PercentMax = 100;
    internal const int DensityMin = 25;
    internal const int DensityMax = 400;
    internal const float StormMinutesMin = 1f;
    internal const float StormMinutesMax = 60f;

    // Share of Deep North cells awake at stage 1, 2, 3 (percent).
    internal int CoverageStage1 = 20;
    internal int CoverageStage2 = 40;
    internal int CoverageStage3 = 70;

    // Chance per star roll for area Jotun at stage 1, 2, 3 (percent; vanilla base is 10).
    internal float StarChanceStage1 = 10f;
    internal float StarChanceStage2 = 25f;
    internal float StarChanceStage3 = 45f;

    // Jotun caps x this percent.
    internal int JotunDensity = 100;

    // Gammeltroll, Barka, frost Greydwarfs fight Jotun, areas spawn some.
    internal bool NatureFightsBack = true;

    // Chance (percent) of a nature band each time a zone's band check comes up (AreaSpawns.BandInterval).
    internal int NatureBandChance = 10;

    // Share of time an area storms (percent), storm length range (minutes), meteors during storms.
    internal int StormShare = 33;
    internal float StormMinMinutes = 5f;
    internal float StormMaxMinutes = 10f;
    internal bool Meteors = true;

    // Invaded areas shown on the map and minimap (purple, only where explored; cleared ones go away).
    internal bool MapAreas = true;

    // True only on Pending: client wait for server rules, feature act vanilla.
    internal bool IsPending { get; private set; }

    // Defaults of the design. Config Bind read them. Never change it (shared object).
    internal static readonly AwakeningRules Default = new AwakeningRules();

    // Client of a server with me, before server rules came.
    internal static readonly AwakeningRules Pending = new AwakeningRules { IsPending = true };

    // Coverage of a stage as 0..1. Stage 0 (dormant) = nothing awake. Never less than the stage before (a lower value
    // for a later stage would put awake areas back to sleep): cells keep waking, never sleep again.
    internal float Coverage(int stage)
    {
        switch (stage)
        {
            case <= 0:
                return 0f;
            case 1:
                return CoverageStage1 / 100f;
            case 2:
                return Math.Max(CoverageStage1, CoverageStage2) / 100f;
            default:
                return Math.Max(Math.Max(CoverageStage1, CoverageStage2), CoverageStage3) / 100f;
        }
    }

    // Star roll chance of a stage (percent). Stage 0 never spawn, give stage 1.
    internal float StarChance(int stage)
    {
        switch (stage)
        {
            case <= 1:
                return StarChanceStage1;
            case 2:
                return StarChanceStage2;
            default:
                return StarChanceStage3;
        }
    }

    // Storm length range in seconds, low end first (config may hold them swapped).
    internal void StormSeconds(out float min, out float max)
    {
        min = Math.Min(StormMinMinutes, StormMaxMinutes) * 60f;
        max = Math.Max(StormMinMinutes, StormMaxMinutes) * 60f;
    }

    // This game's own config. Entry not bound (bind blew up) = default.
    internal static AwakeningRules Own()
    {
        var d = Default;
        return new AwakeningRules
        {
            CoverageStage1 = V(Plugin.CoverageStage1, d.CoverageStage1),
            CoverageStage2 = V(Plugin.CoverageStage2, d.CoverageStage2),
            CoverageStage3 = V(Plugin.CoverageStage3, d.CoverageStage3),
            StarChanceStage1 = V(Plugin.StarChanceStage1, d.StarChanceStage1),
            StarChanceStage2 = V(Plugin.StarChanceStage2, d.StarChanceStage2),
            StarChanceStage3 = V(Plugin.StarChanceStage3, d.StarChanceStage3),
            JotunDensity = V(Plugin.JotunDensity, d.JotunDensity),
            NatureFightsBack = V(Plugin.NatureFightsBack, d.NatureFightsBack),
            NatureBandChance = V(Plugin.NatureBandChance, d.NatureBandChance),
            StormShare = V(Plugin.StormShare, d.StormShare),
            StormMinMinutes = V(Plugin.StormMinMinutes, d.StormMinMinutes),
            StormMaxMinutes = V(Plugin.StormMaxMinutes, d.StormMaxMinutes),
            Meteors = V(Plugin.Meteors, d.Meteors),
            MapAreas = V(Plugin.MapAreas, d.MapAreas),
        };
    }

    // Wire: layout, then every value in fixed order. Layout bump = other order (ModNetworkVersion too).
    internal const int Layout = 1;

    internal void Write(ZPackage pkg)
    {
        pkg.Write(Layout);
        pkg.Write(CoverageStage1);
        pkg.Write(CoverageStage2);
        pkg.Write(CoverageStage3);
        pkg.Write(StarChanceStage1);
        pkg.Write(StarChanceStage2);
        pkg.Write(StarChanceStage3);
        pkg.Write(JotunDensity);
        pkg.Write(NatureFightsBack);
        pkg.Write(NatureBandChance);
        pkg.Write(StormShare);
        pkg.Write(StormMinMinutes);
        pkg.Write(StormMaxMinutes);
        pkg.Write(Meteors);
        pkg.Write(MapAreas);
    }

    // Never trust the wire: unknown layout or broken package = false (caller keep what it had); values outside the
    // config ranges (or not a number) are pulled in (clamped = true).
    internal static bool TryRead(ZPackage pkg, out AwakeningRules rules, out bool clamped)
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
            var r = new AwakeningRules();
            r.CoverageStage1 = Clamp(pkg.ReadInt(), 0, PercentMax, ref clamped);
            r.CoverageStage2 = Clamp(pkg.ReadInt(), 0, PercentMax, ref clamped);
            r.CoverageStage3 = Clamp(pkg.ReadInt(), 0, PercentMax, ref clamped);
            r.StarChanceStage1 = Clamp(pkg.ReadSingle(), 0f, PercentMax, d.StarChanceStage1, ref clamped);
            r.StarChanceStage2 = Clamp(pkg.ReadSingle(), 0f, PercentMax, d.StarChanceStage2, ref clamped);
            r.StarChanceStage3 = Clamp(pkg.ReadSingle(), 0f, PercentMax, d.StarChanceStage3, ref clamped);
            r.JotunDensity = Clamp(pkg.ReadInt(), DensityMin, DensityMax, ref clamped);
            r.NatureFightsBack = pkg.ReadBool();
            r.NatureBandChance = Clamp(pkg.ReadInt(), 0, PercentMax, ref clamped);
            r.StormShare = Clamp(pkg.ReadInt(), 0, PercentMax, ref clamped);
            r.StormMinMinutes = Clamp(pkg.ReadSingle(), StormMinutesMin, StormMinutesMax, d.StormMinMinutes, ref clamped);
            r.StormMaxMinutes = Clamp(pkg.ReadSingle(), StormMinutesMin, StormMinutesMax, d.StormMaxMinutes, ref clamped);
            r.Meteors = pkg.ReadBool();
            r.MapAreas = pkg.ReadBool();
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
            return "waiting for the server's rules (the Deep North stays like the normal game until they arrive)";
        }
        return $"areas {CoverageStage1}/{CoverageStage2}/{CoverageStage3} %, star chance {F(StarChanceStage1)}/"
               + $"{F(StarChanceStage2)}/{F(StarChanceStage3)} %, Jotun density {JotunDensity} %, nature fights back "
               + (NatureFightsBack ? $"on (band chance {NatureBandChance} %)" : "off") + $", storms {StormShare} % of the time for {F(StormMinMinutes)}-"
               + $"{F(StormMaxMinutes)} min, meteors " + (Meteors ? "on" : "off") + ", areas on the map "
               + (MapAreas ? "on" : "off");
    }

    private static T V<T>(ConfigEntry<T> entry, T fallback) => entry != null ? entry.Value : fallback;

    private static string F(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static int Clamp(int value, int min, int max, ref bool clamped)
    {
        var c = value < min ? min : value > max ? max : value;
        clamped |= c != value;
        return c;
    }

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
