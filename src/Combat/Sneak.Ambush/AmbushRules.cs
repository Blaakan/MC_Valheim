using System;
using System.Globalization;
using System.Text;
using BepInEx.Configuration;

namespace MC.Combat.SneakAmbushMod;

// Me say which creature health bars smoke hide.
internal enum HealthBarMode
{
    OnlyInside,    // creature in cloud, local player not in that cloud = no bar (request, default)
    ThroughSmoke,  // smoke geometry say hidden = no bar (both ways, and cloud between)
    Off,           // vanilla bars
}

// Me = every gameplay number of the mod in one snapshot. Server send its own to every player (ServerRules): same
// stealth, smoke and XP for everybody. Personal choices (ShowStealthCues, ShowSneakAttackMessage) and server-only
// AllowPlayersWithoutMod are not in here. Snapshot never change after build: new rules = new object.
// Pending = client still wait for server rules: built in code, never from config, never sent. Feature act vanilla
// then (numbers neutral too, but code must check IsPending, not trust numbers).
internal sealed class AmbushRules
{
    // Ranges. Config (Plugin) and wire clamp (TryRead) use same numbers.
    internal const float XpMax = 50f;
    internal const float ReferenceHealthMin = 10f;
    internal const float ReferenceHealthMax = 2000f;
    internal const float HealthScaleMin = 0.1f;
    internal const float HealthScaleMax = 10f;
    internal const float XpCooldownMax = 3600f;
    internal const int EarlyGameBonusMax = 25;   // percent; above ~28 more skill = more visible in full light
    internal const int BonusMax = 90;            // percent: StillBonus, FoliageBonus, FogBonus
    internal const float StillDelayMax = 5f;
    internal const float FoliageReachMax = 1.5f;
    internal const float FogDensityNoBonusMax = 0.2f;
    internal const float FogDensityFullBonusMin = 0.01f;
    internal const float FogDensityFullBonusMax = 0.3f;
    internal const float VisibilityFloorMax = 0.5f;
    internal const int RecipeAmountMin = 1;
    internal const int RecipeAmountMax = 50;
    internal const int StationLevelMin = 1;
    internal const int StationLevelMax = 10;
    internal const float CloudSizeMin = 2f;
    internal const float CloudSizeMax = 10f;
    internal const float CloudDurationMin = 3f;
    internal const float CloudDurationMax = 60f;
    internal const float ActivationDelayMax = 3f;
    internal const float InsideSightRangeMax = 10f;
    internal const float RevealSecondsMax = 60f;
    internal const float BlindMarginMax = 20f;
    internal const float BlindSecondsMax = 30f;
    internal const float ForgetSecondsMax = 30f;
    internal const int TextMax = 1000;           // recipe strings from wire: longer = cut

    internal const string DefaultRecipeResources = "Resin:2,Coal:1,LeatherScraps:1";
    internal const string DefaultRecipeStation = "piece_workbench";

    // Sneak attacks (XP in "seconds of sneaking near unaware enemies", vanilla RaiseSkill unit).
    internal float SneakAttackXpFlat = 3f;
    internal float SneakAttackXp = 10f;
    internal float ReferenceHealth = 100f;
    internal float MaxHealthScale = 3f;
    internal float RangedXpFactor = 0.5f;
    internal float SneakAttackXpCooldown = 300f;   // seconds
    internal bool PayAlongsideOtherSneakXpMods;

    // Stealth (bonuses in percent).
    internal int EarlyGameBonus = 15;
    internal int StillBonus = 70;
    internal float StillDelay = 1f;                // seconds
    internal bool StillEndsAtOnce = true;
    internal int FoliageBonus;
    internal float FoliageReach = 0.5f;            // metres
    internal int FogBonus = 30;
    internal float FogDensityNoBonus = 0.01f;
    internal float FogDensityFullBonus = 0.08f;
    internal float VisibilityFloor = 0.1f;         // stealth factor

    // Smoke Screen.
    internal string RecipeResources = DefaultRecipeResources;
    internal int RecipeAmount = 2;
    internal string RecipeStation = DefaultRecipeStation;
    internal int RecipeStationLevel = 1;
    internal float CloudRadius = 4f;               // metres
    internal float CloudHeight = 4f;               // metres
    internal float CloudDuration = 15f;            // seconds from impact to end of hiding (build-up inside)
    internal float ActivationDelay = 0.5f;         // seconds
    internal float InsideSightRange = 2.5f;        // metres
    internal bool BlocksLineOfSight = true;
    internal bool BlocksHearing = true;
    internal float RevealSeconds = 8f;
    internal float BlindMargin = 5f;               // metres
    internal float BlindSeconds = 6f;
    internal float ForgetSeconds = 3f;
    internal HealthBarMode HealthBars = HealthBarMode.OnlyInside;

    // True only on Pending: client wait for server rules, feature act vanilla (4.3 of design).
    internal bool IsPending { get; private set; }

    // Defaults of the design. Config Bind read them; always-on code fall back on them when config not bound.
    // Never change it (shared object).
    internal static readonly AmbushRules Default = new AmbushRules();

    // Client of a server with me, before server rules came: everything neutral, recipe empty.
    internal static readonly AmbushRules Pending = new AmbushRules
    {
        IsPending = true,
        SneakAttackXpFlat = 0f,
        SneakAttackXp = 0f,
        SneakAttackXpCooldown = XpCooldownMax,
        EarlyGameBonus = 0,
        StillBonus = 0,
        FoliageBonus = 0,
        FogBonus = 0,
        VisibilityFloor = 0f,
        RecipeResources = "",
        RecipeStation = "",
        BlocksLineOfSight = false,
        BlocksHearing = false,
        RevealSeconds = 0f,
        BlindSeconds = 0f,
        HealthBars = HealthBarMode.Off,
    };

    // This game's own config. Entry not bound (always-on code before BindConfig, or BindConfig blew up) = default.
    internal static AmbushRules Own()
    {
        var d = Default;
        return new AmbushRules
        {
            SneakAttackXpFlat = V(Plugin.SneakAttackXpFlat, d.SneakAttackXpFlat),
            SneakAttackXp = V(Plugin.SneakAttackXp, d.SneakAttackXp),
            ReferenceHealth = V(Plugin.ReferenceHealth, d.ReferenceHealth),
            MaxHealthScale = V(Plugin.MaxHealthScale, d.MaxHealthScale),
            RangedXpFactor = V(Plugin.RangedXpFactor, d.RangedXpFactor),
            SneakAttackXpCooldown = V(Plugin.SneakAttackXpCooldown, d.SneakAttackXpCooldown),
            PayAlongsideOtherSneakXpMods = V(Plugin.PayAlongsideOtherSneakXpMods, d.PayAlongsideOtherSneakXpMods),
            EarlyGameBonus = V(Plugin.EarlyGameBonus, d.EarlyGameBonus),
            StillBonus = V(Plugin.StillBonus, d.StillBonus),
            StillDelay = V(Plugin.StillDelay, d.StillDelay),
            StillEndsAtOnce = V(Plugin.StillEndsAtOnce, d.StillEndsAtOnce),
            FoliageBonus = V(Plugin.FoliageBonus, d.FoliageBonus),
            FoliageReach = V(Plugin.FoliageReach, d.FoliageReach),
            FogBonus = V(Plugin.FogBonus, d.FogBonus),
            FogDensityNoBonus = V(Plugin.FogDensityNoBonus, d.FogDensityNoBonus),
            FogDensityFullBonus = V(Plugin.FogDensityFullBonus, d.FogDensityFullBonus),
            VisibilityFloor = V(Plugin.VisibilityFloor, d.VisibilityFloor),
            RecipeResources = V(Plugin.RecipeResources, d.RecipeResources) ?? "",
            RecipeAmount = V(Plugin.RecipeAmount, d.RecipeAmount),
            RecipeStation = V(Plugin.RecipeStation, d.RecipeStation) ?? "",
            RecipeStationLevel = V(Plugin.RecipeStationLevel, d.RecipeStationLevel),
            CloudRadius = V(Plugin.CloudRadius, d.CloudRadius),
            CloudHeight = V(Plugin.CloudHeight, d.CloudHeight),
            CloudDuration = V(Plugin.CloudDuration, d.CloudDuration),
            ActivationDelay = V(Plugin.ActivationDelay, d.ActivationDelay),
            InsideSightRange = V(Plugin.InsideSightRange, d.InsideSightRange),
            BlocksLineOfSight = V(Plugin.BlocksLineOfSight, d.BlocksLineOfSight),
            BlocksHearing = V(Plugin.BlocksHearing, d.BlocksHearing),
            RevealSeconds = V(Plugin.RevealSeconds, d.RevealSeconds),
            BlindMargin = V(Plugin.BlindMargin, d.BlindMargin),
            BlindSeconds = V(Plugin.BlindSeconds, d.BlindSeconds),
            ForgetSeconds = V(Plugin.ForgetSeconds, d.ForgetSeconds),
            HealthBars = V(Plugin.HealthBars, d.HealthBars),
        };
    }

    // Wire: layout, then every value in fixed order. Layout bump = other order (ModNetworkVersion too).
    internal const int Layout = 1;

    internal void Write(ZPackage pkg)
    {
        pkg.Write(Layout);
        pkg.Write(SneakAttackXpFlat);
        pkg.Write(SneakAttackXp);
        pkg.Write(ReferenceHealth);
        pkg.Write(MaxHealthScale);
        pkg.Write(RangedXpFactor);
        pkg.Write(SneakAttackXpCooldown);
        pkg.Write(PayAlongsideOtherSneakXpMods);
        pkg.Write(EarlyGameBonus);
        pkg.Write(StillBonus);
        pkg.Write(StillDelay);
        pkg.Write(StillEndsAtOnce);
        pkg.Write(FoliageBonus);
        pkg.Write(FoliageReach);
        pkg.Write(FogBonus);
        pkg.Write(FogDensityNoBonus);
        pkg.Write(FogDensityFullBonus);
        pkg.Write(VisibilityFloor);
        pkg.Write(RecipeResources ?? "");
        pkg.Write(RecipeAmount);
        pkg.Write(RecipeStation ?? "");
        pkg.Write(RecipeStationLevel);
        pkg.Write(CloudRadius);
        pkg.Write(CloudHeight);
        pkg.Write(CloudDuration);
        pkg.Write(ActivationDelay);
        pkg.Write(InsideSightRange);
        pkg.Write(BlocksLineOfSight);
        pkg.Write(BlocksHearing);
        pkg.Write(RevealSeconds);
        pkg.Write(BlindMargin);
        pkg.Write(BlindSeconds);
        pkg.Write(ForgetSeconds);
        pkg.Write((int)HealthBars);
    }

    // Never trust the wire: unknown layout or broken package = false (caller keep what it had); values outside the
    // config ranges (or not a number) are pulled in (clamped = true).
    internal static bool TryRead(ZPackage pkg, out AmbushRules rules, out bool clamped)
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
            var r = new AmbushRules();
            r.SneakAttackXpFlat = Clamp(pkg.ReadSingle(), 0f, XpMax, d.SneakAttackXpFlat, ref clamped);
            r.SneakAttackXp = Clamp(pkg.ReadSingle(), 0f, XpMax, d.SneakAttackXp, ref clamped);
            r.ReferenceHealth = Clamp(pkg.ReadSingle(), ReferenceHealthMin, ReferenceHealthMax, d.ReferenceHealth, ref clamped);
            r.MaxHealthScale = Clamp(pkg.ReadSingle(), HealthScaleMin, HealthScaleMax, d.MaxHealthScale, ref clamped);
            r.RangedXpFactor = Clamp(pkg.ReadSingle(), 0f, 1f, d.RangedXpFactor, ref clamped);
            r.SneakAttackXpCooldown = Clamp(pkg.ReadSingle(), 0f, XpCooldownMax, d.SneakAttackXpCooldown, ref clamped);
            r.PayAlongsideOtherSneakXpMods = pkg.ReadBool();
            r.EarlyGameBonus = Clamp(pkg.ReadInt(), 0, EarlyGameBonusMax, ref clamped);
            r.StillBonus = Clamp(pkg.ReadInt(), 0, BonusMax, ref clamped);
            r.StillDelay = Clamp(pkg.ReadSingle(), 0f, StillDelayMax, d.StillDelay, ref clamped);
            r.StillEndsAtOnce = pkg.ReadBool();
            r.FoliageBonus = Clamp(pkg.ReadInt(), 0, BonusMax, ref clamped);
            r.FoliageReach = Clamp(pkg.ReadSingle(), 0f, FoliageReachMax, d.FoliageReach, ref clamped);
            r.FogBonus = Clamp(pkg.ReadInt(), 0, BonusMax, ref clamped);
            r.FogDensityNoBonus = Clamp(pkg.ReadSingle(), 0f, FogDensityNoBonusMax, d.FogDensityNoBonus, ref clamped);
            r.FogDensityFullBonus = Clamp(pkg.ReadSingle(), FogDensityFullBonusMin, FogDensityFullBonusMax,
                d.FogDensityFullBonus, ref clamped);
            r.VisibilityFloor = Clamp(pkg.ReadSingle(), 0f, VisibilityFloorMax, d.VisibilityFloor, ref clamped);
            r.RecipeResources = Text(pkg.ReadString(), ref clamped);
            r.RecipeAmount = Clamp(pkg.ReadInt(), RecipeAmountMin, RecipeAmountMax, ref clamped);
            r.RecipeStation = Text(pkg.ReadString(), ref clamped);
            r.RecipeStationLevel = Clamp(pkg.ReadInt(), StationLevelMin, StationLevelMax, ref clamped);
            r.CloudRadius = Clamp(pkg.ReadSingle(), CloudSizeMin, CloudSizeMax, d.CloudRadius, ref clamped);
            r.CloudHeight = Clamp(pkg.ReadSingle(), CloudSizeMin, CloudSizeMax, d.CloudHeight, ref clamped);
            r.CloudDuration = Clamp(pkg.ReadSingle(), CloudDurationMin, CloudDurationMax, d.CloudDuration, ref clamped);
            r.ActivationDelay = Clamp(pkg.ReadSingle(), 0f, ActivationDelayMax, d.ActivationDelay, ref clamped);
            r.InsideSightRange = Clamp(pkg.ReadSingle(), 0f, InsideSightRangeMax, d.InsideSightRange, ref clamped);
            r.BlocksLineOfSight = pkg.ReadBool();
            r.BlocksHearing = pkg.ReadBool();
            r.RevealSeconds = Clamp(pkg.ReadSingle(), 0f, RevealSecondsMax, d.RevealSeconds, ref clamped);
            r.BlindMargin = Clamp(pkg.ReadSingle(), 0f, BlindMarginMax, d.BlindMargin, ref clamped);
            r.BlindSeconds = Clamp(pkg.ReadSingle(), 0f, BlindSecondsMax, d.BlindSeconds, ref clamped);
            r.ForgetSeconds = Clamp(pkg.ReadSingle(), 0f, ForgetSecondsMax, d.ForgetSeconds, ref clamped);
            var bars = pkg.ReadInt();
            if (!Enum.IsDefined(typeof(HealthBarMode), bars))
            {
                clamped = true;
                bars = (int)d.HealthBars;
            }
            r.HealthBars = (HealthBarMode)bars;
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
            return "waiting for the server's rules (normal game until they arrive)";
        }
        var sb = new StringBuilder(512);
        sb.Append("sneak-attack XP ").Append(F(SneakAttackXpFlat)).Append(" + ").Append(F(SneakAttackXp))
            .Append(" per ").Append(F(ReferenceHealth)).Append(" health (health part up to x").Append(F(MaxHealthScale))
            .Append(", ranged x").Append(F(RangedXpFactor)).Append(", once per ").Append(F(SneakAttackXpCooldown))
            .Append(" s per creature, ").Append(PayAlongsideOtherSneakXpMods ? "also" : "not")
            .Append(" alongside other sneak-XP mods)");
        sb.Append("; stealth: early game ").Append(EarlyGameBonus).Append("%, holding still ").Append(StillBonus)
            .Append("% after ").Append(F(StillDelay)).Append(" s (").Append(StillEndsAtOnce ? "ends at once" : "fades")
            .Append("), foliage ").Append(FoliageBonus).Append("% within ").Append(F(FoliageReach))
            .Append(" m, fog up to ").Append(FogBonus).Append("% (density ").Append(F(FogDensityNoBonus)).Append(" to ")
            .Append(F(FogDensityFullBonus)).Append("), floor ").Append(F(VisibilityFloor));
        sb.Append("; Smoke Screen: ").Append(RecipeResources).Append(" -> ").Append(RecipeAmount).Append(" at ")
            .Append(RecipeStation).Append(" level ").Append(RecipeStationLevel).Append(", cloud ").Append(F(CloudRadius))
            .Append(" m radius x ").Append(F(CloudHeight)).Append(" m for ").Append(F(CloudDuration))
            .Append(" s (active after ").Append(F(ActivationDelay)).Append(" s), sight inside ")
            .Append(F(InsideSightRange)).Append(" m, blocks sight across ").Append(BlocksLineOfSight ? "yes" : "no")
            .Append(", blocks hearing ").Append(BlocksHearing ? "yes" : "no").Append(", reveal ")
            .Append(F(RevealSeconds)).Append(" s, blind ").Append(F(BlindSeconds)).Append(" s within ")
            .Append(F(BlindMargin)).Append(" m, forget after ").Append(F(ForgetSeconds)).Append(" s, health bars ")
            .Append(HealthBars);
        return sb.ToString();
    }

    private static T V<T>(ConfigEntry<T> entry, T fallback) => entry != null ? entry.Value : fallback;

    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

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
