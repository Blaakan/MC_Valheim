using System;
using BepInEx.Configuration;
using MC.Shared;

namespace MC.Exploration.DeepNorthAwakeningMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, server check, status, live on/off. Patches in Patches/ apply only while Active.
// No Awake/Start/Update/OnDestroy here (they hide ModPlugin ones). Use BindConfig / OnActivated / OnDeactivated.
// Me wake the Deep North: broken Malicious Ice of Morkhalla count on the server (key mc_dn_stones); stage 1-3 awake more
// of the north (seeded cells), area spawns of the Jotun army with stage star odds, storms with blizzard and meteors,
// nature hostile to the Jotun; stone 3 start the held-back vanilla invasions, later stones one each; after Kall the
// areas stop spawning and clear when their Jotun die. Both side: server refuse players who cannot play by its rules
// (PlayerCheck) and send its rules to everyone (ServerRules). Game code read rules only through ServerRules.Current.
internal sealed partial class Plugin : ModPlugin
{
    private const string General = "General";
    private const string AwakeningSection = "Awakening";
    private const string StormsSection = "Storms";
    private const string InvasionsSection = "Invasions";
    private const string MapSection = "Map";
    private const string ServerWins = " In multiplayer the setting of the server (or host) is used for everyone.";
    private const string ServerOnly = " Used only by the server (or the host).";

    // General (server only).
    internal static ConfigEntry<bool> AllowPlayersWithoutMod;

    // Awakening (rules, server wins).
    internal static ConfigEntry<int> CoverageStage1;
    internal static ConfigEntry<int> CoverageStage2;
    internal static ConfigEntry<int> CoverageStage3;
    internal static ConfigEntry<float> StarChanceStage1;
    internal static ConfigEntry<float> StarChanceStage2;
    internal static ConfigEntry<float> StarChanceStage3;
    internal static ConfigEntry<int> JotunDensity;
    internal static ConfigEntry<bool> NatureFightsBack;
    internal static ConfigEntry<int> NatureBandChance;
    internal static ConfigEntry<int> ClearKillsMin;
    internal static ConfigEntry<int> ClearKillsMax;

    // Storms (rules, server wins).
    internal static ConfigEntry<int> StormShare;
    internal static ConfigEntry<float> StormMinMinutes;
    internal static ConfigEntry<float> StormMaxMinutes;
    internal static ConfigEntry<bool> Meteors;

    // Map (rules, server wins).
    internal static ConfigEntry<bool> MapAreas;

    // Invasions (server only, never sent).
    internal static ConfigEntry<int> InvasionsAtThirdStone;

    protected override void BindConfig()
    {
        var d = AwakeningRules.Default;

        AllowPlayersWithoutMod = Config.Bind(General, "AllowPlayersWithoutMod", false, new ConfigDescription(
            "Used only by the server (or the host). Off (default): a player whose game does not have this mod, has it "
            + "turned off or has another version of it is refused about a second after joining, or about a second after "
            + "turning it off, and their game shows \"Incompatible version\", so everyone plays the same Deep North. "
            + "On: they may play; their game runs the Deep North like the normal game (no blizzard for them, no area "
            + "creatures from the places they load).",
            null, new ConfigurationManagerAttributes { Order = 90 }));

        CoverageStage1 = Config.Bind(AwakeningSection, "CoverageStage1", d.CoverageStage1, new ConfigDescription(
            "Share of the Deep North invaded by the Jotun after the first Malicious Ice of a Morkhalla is broken, in "
            + "percent. Every stage keeps the areas of the stage before (a later stage never covers less)." + ServerWins,
            new AcceptableValueRange<int>(0, AwakeningRules.PercentMax),
            new ConfigurationManagerAttributes { Order = 100 }));
        CoverageStage2 = Config.Bind(AwakeningSection, "CoverageStage2", d.CoverageStage2, new ConfigDescription(
            "Share of the Deep North invaded after the second Malicious Ice, in percent (at least CoverageStage1)."
            + ServerWins,
            new AcceptableValueRange<int>(0, AwakeningRules.PercentMax),
            new ConfigurationManagerAttributes { Order = 99 }));
        CoverageStage3 = Config.Bind(AwakeningSection, "CoverageStage3", d.CoverageStage3, new ConfigDescription(
            "Share of the Deep North invaded after the third Malicious Ice and later, in percent (at least the stage "
            + "before)." + ServerWins,
            new AcceptableValueRange<int>(0, AwakeningRules.PercentMax),
            new ConfigurationManagerAttributes { Order = 98 }));
        StarChanceStage1 = Config.Bind(AwakeningSection, "StarChanceStage1", d.StarChanceStage1, new ConfigDescription(
            "Chance per star roll of the Jotun in the invaded areas at stage 1, in percent (the normal game uses 10: "
            + "about 9 % one star, 1 % two stars). Krigen can get two stars, Hexen and Elaking one. The world modifiers "
            + "for enemy level-ups still apply. 0 = no stars." + ServerWins,
            new AcceptableValueRange<float>(0f, AwakeningRules.PercentMax),
            new ConfigurationManagerAttributes { Order = 95 }));
        StarChanceStage2 = Config.Bind(AwakeningSection, "StarChanceStage2", d.StarChanceStage2, new ConfigDescription(
            "Chance per star roll at stage 2, in percent (25: about 19 % one star, 6 % two stars)." + ServerWins,
            new AcceptableValueRange<float>(0f, AwakeningRules.PercentMax),
            new ConfigurationManagerAttributes { Order = 94 }));
        StarChanceStage3 = Config.Bind(AwakeningSection, "StarChanceStage3", d.StarChanceStage3, new ConfigDescription(
            "Chance per star roll at stage 3 and later, in percent (45: about 25 % one star, 20 % two stars)."
            + ServerWins,
            new AcceptableValueRange<float>(0f, AwakeningRules.PercentMax),
            new ConfigurationManagerAttributes { Order = 93 }));
        JotunDensity = Config.Bind(AwakeningSection, "JotunDensity", d.JotunDensity, new ConfigDescription(
            "How many Jotun an invaded area holds around each player, in percent of the normal amount (100: up to 3 "
            + "Krigen, 2 dual-axe Krigen, 2 Hexen and 6 Elaking within about 160 m, like a Jotun invasion)."
            + ServerWins,
            new AcceptableValueRange<int>(AwakeningRules.DensityMin, AwakeningRules.DensityMax),
            new ConfigurationManagerAttributes { Order = 90 }));
        NatureFightsBack = Config.Bind(AwakeningSection, "NatureFightsBack", d.NatureFightsBack, new ConfigDescription(
            "Once the Deep North is awake, Gammeltroll, Barka and frost Greydwarfs fight the Jotun army (Krigen, "
            + "Hexen, Elaking), and the invaded areas sometimes spawn a band of them (see NatureBandChance)."
            + ServerWins,
            null, new ConfigurationManagerAttributes { Order = 85 }));
        NatureBandChance = Config.Bind(AwakeningSection, "NatureBandChance", d.NatureBandChance, new ConfigDescription(
            "Chance, in percent, that a band of nature appears when a place in an invaded area checks for one (once "
            + "every 20 minutes per place, at once on the first visit): 10 to 20 frost Greydwarfs, 2 to 4 of them "
            + "shamans, and 1 to 3 Gammeltroll or Barka. Never while a band is already near, never in a cleared area "
            + "(bands keep coming after Kall until the area is cleared). 0 = no bands." + ServerWins,
            new AcceptableValueRange<int>(0, AwakeningRules.PercentMax),
            new ConfigurationManagerAttributes { Order = 84 }));
        ClearKillsMin = Config.Bind(AwakeningSection, "ClearKillsMin", d.ClearKillsMin, new ConfigDescription(
            "After Kall Fimbulbringer is defeated, an invaded area is cleared for good once enough Jotun (Krigen, "
            + "Hexen, Elaking, from any area) have been killed inside it: each area needs its own fixed number "
            + "between ClearKillsMin and ClearKillsMax. Half way, players there see \"The Jotun army is weakening\"."
            + ServerWins,
            new AcceptableValueRange<int>(AwakeningRules.KillsMin, AwakeningRules.KillsMax),
            new ConfigurationManagerAttributes { Order = 80 }));
        ClearKillsMax = Config.Bind(AwakeningSection, "ClearKillsMax", d.ClearKillsMax, new ConfigDescription(
            "Most Jotun kills an area can need to be cleared (see ClearKillsMin)." + ServerWins,
            new AcceptableValueRange<int>(AwakeningRules.KillsMin, AwakeningRules.KillsMax),
            new ConfigurationManagerAttributes { Order = 79 }));

        StormShare = Config.Bind(StormsSection, "StormShare", d.StormShare, new ConfigDescription(
            "Share of the time an invaded area storms (Deep North blizzard and Fimbul meteors), in percent. Each area "
            + "has its own storms, the same for every player. 0 = no storms." + ServerWins,
            new AcceptableValueRange<int>(0, AwakeningRules.PercentMax),
            new ConfigurationManagerAttributes { Order = 100 }));
        StormMinMinutes = Config.Bind(StormsSection, "StormMinMinutes", d.StormMinMinutes, new ConfigDescription(
            "Shortest storm, in real minutes." + ServerWins,
            new AcceptableValueRange<float>(AwakeningRules.StormMinutesMin, AwakeningRules.StormMinutesMax),
            new ConfigurationManagerAttributes { Order = 95 }));
        StormMaxMinutes = Config.Bind(StormsSection, "StormMaxMinutes", d.StormMaxMinutes, new ConfigDescription(
            "Longest storm, in real minutes." + ServerWins,
            new AcceptableValueRange<float>(AwakeningRules.StormMinutesMin, AwakeningRules.StormMinutesMax),
            new ConfigurationManagerAttributes { Order = 94 }));
        Meteors = Config.Bind(StormsSection, "Meteors", d.Meteors, new ConfigDescription(
            "Fimbul meteors fall in a storming area, like in a Jotun invasion (never within 40 m of a player or inside "
            + "a base, but they hurt and can break what they hit). Off = blizzard only." + ServerWins,
            null, new ConfigurationManagerAttributes { Order = 90 }));

        MapAreas = Config.Bind(MapSection, "ShowAreas", d.MapAreas, new ConfigDescription(
            "Show the invaded areas on the map and the minimap: a purple tint over the Deep North land they cover, only "
            + "where the map is explored. Areas that touch merge into one region. After Kall is defeated, a cleared area "
            + "disappears from it. Off = the areas stay hidden; you find them by the Jotun and the storms." + ServerWins,
            null, new ConfigurationManagerAttributes { Order = 100 }));

        InvasionsAtThirdStone = Config.Bind(InvasionsSection, "InvasionsAtThirdStone", 3, new ConfigDescription(
            "Jotun invasions that start in the world when the third Malicious Ice breaks (the first two awaken the "
            + "Deep North instead). Every later Malicious Ice starts one, as in the normal game. The normal game never "
            + "runs more than 3 invasions at once." + ServerOnly,
            new AcceptableValueRange<int>(0, 10),
            new ConfigurationManagerAttributes { Order = 100 }));

        // Every rule setting (sections Awakening, Storms, Map): new own snapshot, server send it again.
        Config.SettingChanged += OnSettingChanged;
        AllowPlayersWithoutMod.SettingChanged += OnAllowChanged;
    }

    protected override void OnActivated()
    {
        ForgetWorld();
        // Held rpc live before the rules request goes out: the server answer it with the held areas after Kall.
        HeldCells.Start();
        ServerRules.Start();
        PlayerCheck.Start();
        AdminCommand.Register();
        Compat.LogOnce();
        WorldState.Refresh();
        SelfTests.Register();
    }

    // Patches still on during this call (also at game quit). Each step alone: one failure never skip the rest. Held
    // invasion requests run now (vanilla would have run them); delegate off; weather and spawns go vanilla at once.
    protected override void OnDeactivated()
    {
        // Conditional method (gone in Release): no delegate to it, so own try instead of Step().
        try
        {
            SelfTests.Unregister();
        }
        catch (Exception e)
        {
            PatchGuard.Report("Plugin.OnDeactivated SelfTests.Unregister", e);
        }
        Step("Stones.ReleaseHeld", Stones.ReleaseHeld);
        Step("ServerWorld.Unhook", ServerWorld.Unhook);
        Step("AdminCommand.Unregister", AdminCommand.Unregister);
        Step("HeldCells.Stop", HeldCells.Stop);
        Step("MapOverlay.Restore", MapOverlay.Restore);
        Step("ForgetWorld", ForgetWorld);
        Step("PlayerCheck.Stop", PlayerCheck.Stop);
        Step("ServerRules.Stop", ServerRules.Stop);
    }

    // World end or feature off: every memory of the world go (key stays in the world itself).
    internal static void ForgetWorld()
    {
        Stones.Clear();
        HeldCells.Clear();
        AreaSpawns.Clear();
        Storms.Clear();
        MapOverlay.Forget();
        WorldState.Clear();
    }

    private static void Step(string site, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            PatchGuard.Report("Plugin.OnDeactivated " + site, e);
        }
    }

    private static void OnSettingChanged(object sender, SettingChangedEventArgs e)
    {
        try
        {
            var section = e?.ChangedSetting?.Definition.Section;
            if (section != AwakeningSection && section != StormsSection && section != MapSection)
            {
                return;
            }
            ServerRules.OwnChanged();
            WorldState.Refresh();
        }
        catch (Exception ex)
        {
            PatchGuard.Report("Plugin.OnSettingChanged", ex);
        }
    }

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
