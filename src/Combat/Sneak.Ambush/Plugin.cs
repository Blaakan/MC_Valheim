using System;
using BepInEx.Configuration;
using MC.Shared;

namespace MC.Combat.SneakAmbushMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, server check, status, live on/off. Patches in Patches/ apply only while Active.
// No Awake/Start/Update/OnDestroy here (they hide ModPlugin ones). Use BindConfig / OnActivated / OnDeactivated.
// Me make sneaking worth it: sneak attack (vanilla backstab) give Sneak XP, low Sneak a bit harder to see (vanilla
// at Sneak 100), crouch and hold still hide much better, fog hide you and status icons show fog/foliage/mist/smoke,
// new Smoke Screen bomb make cloud that hide players from creature AI and blind creatures chasing someone near burst.
// Both side: server refuse players who cannot play by its rules (PlayerCheck) and send its rules to everyone
// (ServerRules). Game code read rules only through ServerRules.Current. Smoke Screen item and its prefabs stay
// registered while feature off ([AlwaysOnPatch] classes): those read FeatureActive and ServerRules themselves.
internal sealed partial class Plugin : ModPlugin
{
    // General (server only).
    internal static ConfigEntry<bool> AllowPlayersWithoutMod;

    // Sneak attacks.
    internal static ConfigEntry<float> SneakAttackXpFlat;
    internal static ConfigEntry<float> SneakAttackXp;
    internal static ConfigEntry<float> ReferenceHealth;
    internal static ConfigEntry<float> MaxHealthScale;
    internal static ConfigEntry<float> RangedXpFactor;
    internal static ConfigEntry<float> SneakAttackXpCooldown;
    internal static ConfigEntry<bool> PayAlongsideOtherSneakXpMods;
    internal static ConfigEntry<bool> ShowSneakAttackMessage;     // personal

    // Stealth.
    internal static ConfigEntry<int> EarlyGameBonus;
    internal static ConfigEntry<int> StillBonus;
    internal static ConfigEntry<float> StillDelay;
    internal static ConfigEntry<bool> StillEndsAtOnce;
    internal static ConfigEntry<int> FoliageBonus;
    internal static ConfigEntry<float> FoliageReach;
    internal static ConfigEntry<int> FogBonus;
    internal static ConfigEntry<float> FogDensityNoBonus;
    internal static ConfigEntry<float> FogDensityFullBonus;
    internal static ConfigEntry<float> VisibilityFloor;
    internal static ConfigEntry<bool> ShowStealthCues;            // personal

    // Smoke Screen.
    internal static ConfigEntry<string> RecipeResources;
    internal static ConfigEntry<int> RecipeAmount;
    internal static ConfigEntry<string> RecipeStation;
    internal static ConfigEntry<int> RecipeStationLevel;
    internal static ConfigEntry<float> CloudRadius;
    internal static ConfigEntry<float> CloudHeight;
    internal static ConfigEntry<float> CloudDuration;
    internal static ConfigEntry<float> ActivationDelay;
    internal static ConfigEntry<float> InsideSightRange;
    internal static ConfigEntry<bool> BlocksLineOfSight;
    internal static ConfigEntry<bool> BlocksHearing;
    internal static ConfigEntry<float> RevealSeconds;
    internal static ConfigEntry<float> BlindMargin;
    internal static ConfigEntry<float> BlindSeconds;
    internal static ConfigEntry<float> ForgetSeconds;
    internal static ConfigEntry<HealthBarMode> HealthBars;

    private const string SneakSection = "Sneak attacks";
    private const string StealthSection = "Stealth";
    private const string SmokeSection = "Smoke Screen";
    private const string ServerWins = " In multiplayer the setting of the server (or host) is used for everyone.";

    private static bool _featureActive;

#if DEBUG
    // Self test (sneak.guard) play "feature off" without the toggle. Never in release.
    internal static bool TestInactive { get; set; }
#endif

    // Feature on (patches applied) and not faked off by a self test. Always-on code (registration, throw guard, XP
    // handler) read this: their patches stay while the feature is off.
    internal static bool FeatureActive
    {
        get
        {
#if DEBUG
            if (TestInactive)
            {
                return false;
            }
#endif
            return _featureActive;
        }
    }

    protected override void BindConfig()
    {
        var d = AmbushRules.Default;

        AllowPlayersWithoutMod = Config.Bind("General", "AllowPlayersWithoutMod", false, new ConfigDescription(
            "Used only by the server (or the host). Off (default): a player whose game does not have this mod, has it "
            + "turned off or has another version of it is refused about a second after joining, or about a second after "
            + "turning it off, and their game shows \"Incompatible version\", so everyone plays by the same stealth and "
            + "smoke rules. On: they may play; creatures their game controls ignore smoke and give nobody Sneak XP for "
            + "sneak attacks, their stealth follows the normal game, and a player without the mod loses any Smoke "
            + "Screen that reaches their inventory.",
            null, new ConfigurationManagerAttributes { Order = 90 }));

        // Sneak attacks.
        SneakAttackXpFlat = Config.Bind(SneakSection, "SneakAttackXpFlat", d.SneakAttackXpFlat, new ConfigDescription(
            "Sneak XP every sneak attack gives (a sneak attack is the normal game's backstab: a hit on a creature that "
            + "has not noticed you), in seconds of sneaking near unaware enemies, before the bonus for the creature's "
            + "health. Set this and SneakAttackXp to 0 to turn sneak-attack XP off." + ServerWins,
            new AcceptableValueRange<float>(0f, AmbushRules.XpMax),
            new ConfigurationManagerAttributes { Order = 100 }));
        SneakAttackXp = Config.Bind(SneakSection, "SneakAttackXp", d.SneakAttackXp, new ConfigDescription(
            "Extra Sneak XP for a sneak attack on a creature with ReferenceHealth health. Bigger creatures pay more, up "
            + "to MaxHealthScale times this; small ones less. Stars count, world level does not." + ServerWins,
            new AcceptableValueRange<float>(0f, AmbushRules.XpMax),
            new ConfigurationManagerAttributes { Order = 95 }));
        ReferenceHealth = Config.Bind(SneakSection, "ReferenceHealth", d.ReferenceHealth, new ConfigDescription(
            "Health of a creature that pays exactly SneakAttackXp extra." + ServerWins,
            new AcceptableValueRange<float>(AmbushRules.ReferenceHealthMin, AmbushRules.ReferenceHealthMax),
            new ConfigurationManagerAttributes { Order = 90 }));
        MaxHealthScale = Config.Bind(SneakSection, "MaxHealthScale", d.MaxHealthScale, new ConfigDescription(
            "Cap on the health bonus: a creature pays at most this many times SneakAttackXp extra (by default a "
            + "600-health Troll pays 3 times SneakAttackXp)." + ServerWins,
            new AcceptableValueRange<float>(AmbushRules.HealthScaleMin, AmbushRules.HealthScaleMax),
            new ConfigurationManagerAttributes { Order = 85 }));
        RangedXpFactor = Config.Bind(SneakSection, "RangedXpFactor", d.RangedXpFactor, new ConfigDescription(
            "Share of the XP for sneak attacks with arrows, bolts and thrown weapons (0.5 = half)." + ServerWins,
            new AcceptableValueRange<float>(0f, 1f),
            new ConfigurationManagerAttributes { Order = 80 }));
        SneakAttackXpCooldown = Config.Bind(SneakSection, "SneakAttackXpCooldown", d.SneakAttackXpCooldown,
            new ConfigDescription(
                "A creature pays sneak-attack XP at most once in this many seconds, whoever hits it. A sneak attack "
                + "counts even when the attacker's game pays nothing for it (for example because another mod pays "
                + "Sneak XP there instead)." + ServerWins,
                new AcceptableValueRange<float>(0f, AmbushRules.XpCooldownMax),
                new ConfigurationManagerAttributes { Order = 75 }));
        PayAlongsideOtherSneakXpMods = Config.Bind(SneakSection, "PayAlongsideOtherSneakXpMods",
            d.PayAlongsideOtherSneakXpMods, new ConfigDescription(
                "Off: when SecondaryAttacks or SmartSkills is installed on a player's game, they pay that player's "
                + "sneak-attack XP and this mod does not. On: both pay." + ServerWins,
                null, new ConfigurationManagerAttributes { Order = 70 }));
        ShowSneakAttackMessage = Config.Bind(SneakSection, "ShowSneakAttackMessage", true, new ConfigDescription(
            "Show \"Sneak attack!\" and the Sneak progress gained at the top left of the screen. Each player's own "
            + "choice.",
            null, new ConfigurationManagerAttributes { Order = 65 }));

        // Stealth.
        EarlyGameBonus = Config.Bind(StealthSection, "EarlyGameBonus", d.EarlyGameBonus, new ConfigDescription(
            "How much harder you are to see while sneaking at Sneak 0, in percent. The bonus shrinks evenly as Sneak "
            + "rises and is gone at Sneak 100, where sneaking is exactly as in the normal game." + ServerWins,
            new AcceptableValueRange<int>(0, AmbushRules.EarlyGameBonusMax),
            new ConfigurationManagerAttributes { Order = 100 }));
        StillBonus = Config.Bind(StealthSection, "StillBonus", d.StillBonus, new ConfigDescription(
            "Crouched and not moving: creatures see you at this much less distance, in percent (70 = they must come "
            + "three times closer)." + ServerWins,
            new AcceptableValueRange<int>(0, AmbushRules.BonusMax),
            new ConfigurationManagerAttributes { Order = 95 }));
        StillDelay = Config.Bind(StealthSection, "StillDelay", d.StillDelay, new ConfigDescription(
            "How long you must hold still before the holding-still bonus starts, in seconds." + ServerWins,
            new AcceptableValueRange<float>(0f, AmbushRules.StillDelayMax),
            new ConfigurationManagerAttributes { Order = 90 }));
        StillEndsAtOnce = Config.Bind(StealthSection, "StillEndsAtOnce", d.StillEndsAtOnce, new ConfigDescription(
            "On: moving ends the holding-still bonus immediately. Off: it fades out at the normal speed of the stealth "
            + "bar." + ServerWins,
            null, new ConfigurationManagerAttributes { Order = 85 }));
        FoliageBonus = Config.Bind(StealthSection, "FoliageBonus", d.FoliageBonus, new ConfigDescription(
            "Extra bonus while crouched touching a bush or low branches: creatures see you at this much less distance, "
            + "in percent. Foliage already blocks sight and shades you in the normal game, which the In foliage icon "
            + "shows; 0 (default) adds nothing to that." + ServerWins,
            new AcceptableValueRange<int>(0, AmbushRules.BonusMax),
            new ConfigurationManagerAttributes { Order = 80 }));
        FoliageReach = Config.Bind(StealthSection, "FoliageReach", d.FoliageReach, new ConfigDescription(
            "How close to foliage your body must be to count as touching it, in metres." + ServerWins,
            new AcceptableValueRange<float>(0f, AmbushRules.FoliageReachMax),
            new ConfigurationManagerAttributes { Order = 75 }));
        FogBonus = Config.Bind(StealthSection, "FogBonus", d.FogBonus, new ConfigDescription(
            "Bonus in the thickest fog, outdoors, in percent: creatures see you at this much less distance. Thinner fog "
            + "gives less." + ServerWins,
            new AcceptableValueRange<int>(0, AmbushRules.BonusMax),
            new ConfigurationManagerAttributes { Order = 70 }));
        FogDensityNoBonus = Config.Bind(StealthSection, "FogDensityNoBonus", d.FogDensityNoBonus, new ConfigDescription(
            "Fog density at or below which fog gives nothing. A clear night is 0.01." + ServerWins,
            new AcceptableValueRange<float>(0f, AmbushRules.FogDensityNoBonusMax),
            new ConfigurationManagerAttributes { Order = 65 }));
        FogDensityFullBonus = Config.Bind(StealthSection, "FogDensityFullBonus", d.FogDensityFullBonus,
            new ConfigDescription(
                "Fog density at or above which fog gives the full bonus. Misty dawn, dusk and night are thicker than "
                + "this. If it is not above FogDensityNoBonus, any fog thicker than FogDensityNoBonus gives the full "
                + "bonus." + ServerWins,
                new AcceptableValueRange<float>(AmbushRules.FogDensityFullBonusMin, AmbushRules.FogDensityFullBonusMax),
                new ConfigurationManagerAttributes { Order = 60 }));
        VisibilityFloor = Config.Bind(StealthSection, "VisibilityFloor", d.VisibilityFloor, new ConfigDescription(
            "This mod's bonuses never take your visibility below this (1 = fully visible, 0.1 = creatures see you at a "
            + "tenth of the normal distance)." + ServerWins,
            new AcceptableValueRange<float>(0f, AmbushRules.VisibilityFloorMax),
            new ConfigurationManagerAttributes { Order = 55 }));
        ShowStealthCues = Config.Bind(StealthSection, "ShowStealthCues", true, new ConfigDescription(
            "Show status icons for holding still, foliage, fog, mist and smoke. The bonuses apply either way. Each "
            + "player's own choice.",
            null, new ConfigurationManagerAttributes { Order = 50 }));

        // Smoke Screen.
        RecipeResources = Config.Bind(SmokeSection, "RecipeResources", d.RecipeResources, new ConfigDescription(
            "Materials to craft Smoke Screens: comma-separated Name:amount pairs, with prefab names as used by the "
            + "spawn command (for example Resin:2,Coal:1,LeatherScraps:1). Unknown names are skipped." + ServerWins,
            null, new ConfigurationManagerAttributes { Order = 100 }));
        RecipeAmount = Config.Bind(SmokeSection, "RecipeAmount", d.RecipeAmount, new ConfigDescription(
            "Smoke Screens per craft." + ServerWins,
            new AcceptableValueRange<int>(AmbushRules.RecipeAmountMin, AmbushRules.RecipeAmountMax),
            new ConfigurationManagerAttributes { Order = 95 }));
        RecipeStation = Config.Bind(SmokeSection, "RecipeStation", d.RecipeStation, new ConfigDescription(
            "Crafting station (prefab name, as used by the spawn command)." + ServerWins,
            null, new ConfigurationManagerAttributes { Order = 90 }));
        RecipeStationLevel = Config.Bind(SmokeSection, "RecipeStationLevel", d.RecipeStationLevel,
            new ConfigDescription(
                "Station level needed." + ServerWins,
                new AcceptableValueRange<int>(AmbushRules.StationLevelMin, AmbushRules.StationLevelMax),
                new ConfigurationManagerAttributes { Order = 85 }));
        CloudRadius = Config.Bind(SmokeSection, "CloudRadius", d.CloudRadius, new ConfigDescription(
            "Radius of the smoke cloud, in metres. Applies to the next throw." + ServerWins,
            new AcceptableValueRange<float>(AmbushRules.CloudSizeMin, AmbushRules.CloudSizeMax),
            new ConfigurationManagerAttributes { Order = 80 }));
        CloudHeight = Config.Bind(SmokeSection, "CloudHeight", d.CloudHeight, new ConfigDescription(
            "Height of the smoke cloud, in metres. Applies to the next throw." + ServerWins,
            new AcceptableValueRange<float>(AmbushRules.CloudSizeMin, AmbushRules.CloudSizeMax),
            new ConfigurationManagerAttributes { Order = 75 }));
        CloudDuration = Config.Bind(SmokeSection, "CloudDuration", d.CloudDuration, new ConfigDescription(
            "How long the cloud lasts, in seconds, counted from the impact: it hides you from the end of "
            + "ActivationDelay until then, and then fades. Applies to the next throw." + ServerWins,
            new AcceptableValueRange<float>(AmbushRules.CloudDurationMin, AmbushRules.CloudDurationMax),
            new ConfigurationManagerAttributes { Order = 70 }));
        ActivationDelay = Config.Bind(SmokeSection, "ActivationDelay", d.ActivationDelay, new ConfigDescription(
            "Time for the smoke to build up after the impact before it hides anyone, in seconds. It is part of "
            + "CloudDuration, and is shortened when needed so that a cloud always hides for at least 1 second."
            + ServerWins,
            new AcceptableValueRange<float>(0f, AmbushRules.ActivationDelayMax),
            new ConfigurationManagerAttributes { Order = 65 }));
        InsideSightRange = Config.Bind(SmokeSection, "InsideSightRange", d.InsideSightRange, new ConfigDescription(
            "Inside the same cloud, you and creatures notice each other only this close, in metres." + ServerWins,
            new AcceptableValueRange<float>(0f, AmbushRules.InsideSightRangeMax),
            new ConfigurationManagerAttributes { Order = 60 }));
        BlocksLineOfSight = Config.Bind(SmokeSection, "BlocksLineOfSight", d.BlocksLineOfSight, new ConfigDescription(
            "Creatures cannot see you through a cloud even when neither of you is inside it." + ServerWins,
            null, new ConfigurationManagerAttributes { Order = 55 }));
        BlocksHearing = Config.Bind(SmokeSection, "BlocksHearing", d.BlocksHearing, new ConfigDescription(
            "Creatures cannot hear you through the smoke either." + ServerWins,
            null, new ConfigurationManagerAttributes { Order = 50 }));
        RevealSeconds = Config.Bind(SmokeSection, "RevealSeconds", d.RevealSeconds, new ConfigDescription(
            "A creature you hit while a smoke cloud is nearby can see you through smoke for this many seconds (each "
            + "hit starts it again)." + ServerWins,
            new AcceptableValueRange<float>(0f, AmbushRules.RevealSecondsMax),
            new ConfigurationManagerAttributes { Order = 45 }));
        BlindMargin = Config.Bind(SmokeSection, "BlindMargin", d.BlindMargin, new ConfigDescription(
            "Creatures chasing a player who is within this many metres of the cloud are blinded by the burst."
            + ServerWins,
            new AcceptableValueRange<float>(0f, AmbushRules.BlindMarginMax),
            new ConfigurationManagerAttributes { Order = 40 }));
        BlindSeconds = Config.Bind(SmokeSection, "BlindSeconds", d.BlindSeconds, new ConfigDescription(
            "How long the burst blinds them, in seconds, counted from the impact (they cannot see or hear any "
            + "player, except one who hits them). The blind never lasts longer than the cloud and its fade "
            + "(CloudDuration + 3 seconds)." + ServerWins,
            new AcceptableValueRange<float>(0f, AmbushRules.BlindSecondsMax),
            new ConfigurationManagerAttributes { Order = 35 }));
        ForgetSeconds = Config.Bind(SmokeSection, "ForgetSeconds", d.ForgetSeconds, new ConfigDescription(
            "A creature that cannot sense its target because of the smoke gives up the chase after this many seconds. "
            + "Creatures that hunt you in the normal game (raids and other hunters) never give up; an arrow landing "
            + "near a pursuer starts its count again." + ServerWins,
            new AcceptableValueRange<float>(0f, AmbushRules.ForgetSecondsMax),
            new ConfigurationManagerAttributes { Order = 30 }));
        HealthBars = Config.Bind(SmokeSection, "HealthBars", d.HealthBars, new ConfigDescription(
            "OnlyInside: you do not see the health bars of creatures inside a smoke cloud while you are outside it. "
            + "ThroughSmoke: bars are hidden through smoke either way (also creatures outside while you are inside, "
            + "and through a cloud between you). Off: normal game. Bosses and tamed creatures always keep their bar."
            + ServerWins,
            null, new ConfigurationManagerAttributes { Order = 25 }));

        // Every rule setting (all sections but General, personal ones aside): new own snapshot, server send it again.
        Config.SettingChanged += OnSettingChanged;
        AllowPlayersWithoutMod.SettingChanged += OnAllowChanged;
        // Rules in force changed (server rules came, own config, pending over): recipe follow. Whole session: rebuild
        // read feature state itself (off = recipe hidden).
        ServerRules.Changed -= OnRulesChanged;
        ServerRules.Changed += OnRulesChanged;
    }

    protected override void OnActivated()
    {
        _featureActive = true;
        Compat.Reset();
        ServerRules.Start();
        PlayerCheck.Start();
        StealthState.Reset(null);
        // Recipe on (when built, rules here and valid). XP handler already on every Player (always-on Awake patch).
        SmokeContent.Rebuild();
        SelfTests.Register();
    }

    // Patches still on during this call. Nothing vanilla to put back: me only ever edit my own clones. Clouds already
    // in the world keep their smoke and expire; they stop hiding (sense patches gone).
    protected override void OnDeactivated()
    {
        _featureActive = false;
        // Tests gone and their in-memory overrides cleared (rules back to config or server before the recipe rebuild).
        SelfTests.Unregister();
        SmokeContent.Rebuild();
        var player = Player.m_localPlayer;
        if (player != null)
        {
            StealthCues.RemoveAll(player);
            // Vanilla target back within a frame (factor then ramp up at vanilla speed).
            player.m_stealthFactorUpdateTimer = 0.51f;
        }
        StealthState.Reset(null);
        AiMemory.Clear();
        PlayerCheck.Stop();
        ServerRules.Stop();
    }

    private static void OnRulesChanged()
    {
        try
        {
            SmokeContent.Rebuild();
        }
        catch (Exception ex)
        {
            PatchGuard.Report("Plugin.OnRulesChanged", ex);
        }
    }

    private static void OnSettingChanged(object sender, SettingChangedEventArgs e)
    {
        try
        {
            var setting = e?.ChangedSetting;
            if (setting == null || setting.Definition.Section == "General" || IsPersonal(setting))
            {
                return;
            }
            ServerRules.OwnChanged();
        }
        catch (Exception ex)
        {
            PatchGuard.Report("Plugin.OnSettingChanged", ex);
        }
    }

    // Each player's own choice: never in the rules, never sent.
    private static bool IsPersonal(ConfigEntryBase setting) =>
        ReferenceEquals(setting, ShowStealthCues) || ReferenceEquals(setting, ShowSneakAttackMessage);

    // Switched back to refuse: players without me already in get checked (after the grace) too.
    private static void OnAllowChanged(object sender, EventArgs e)
    {
        try
        {
            if (!AllowPlayersWithoutMod.Value)
            {
                PlayerCheck.ScheduleAllConnected();
            }
        }
        catch (Exception ex)
        {
            PatchGuard.Report("Plugin.OnAllowChanged", ex);
        }
    }
}
