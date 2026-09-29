using System;
using BepInEx.Configuration;
using MC.Farming.BreedingStarInheritanceMod.Patches;
using MC.Shared;

namespace MC.Farming.BreedingStarInheritanceMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, server check, status, live on/off. Patches in Patches/ apply only while Active.
// No Awake/Start/Update/OnDestroy here (they hide ModPlugin ones). Use BindConfig / OnActivated / OnDeactivated.
// Baby start from weaker parent level, chance of one more level (up to MaxStars) grow with best farmer's Farming.
// Both side: server refuse players without me (PlayerCheck) and send its birth numbers to everyone (ServerSettings).
// Numbers read at every birth (CurrentSettings, nothing cached). SettingChanged only tell server to send them again.
// Off = patches gone = vanilla births.
internal sealed partial class Plugin : ModPlugin
{
    internal static ConfigEntry<float> ChanceAtFarming0;
    internal static ConfigEntry<float> ChanceAtFarming100;
    internal static ConfigEntry<float> ChanceWithoutFarmer;
    internal static ConfigEntry<float> FarmerRange;
    internal static ConfigEntry<int> MaxStars;
    internal static ConfigEntry<bool> AllowPlayersWithoutMod;

    private const string ServerWins = " In multiplayer the setting of the server (or host) is used for everyone.";

    protected override void BindConfig()
    {
        var defaults = RuleSettings.Defaults;
        ChanceAtFarming0 = Config.Bind("General", "ChanceAtFarming0", defaults.ChanceAtFarming0, new ConfigDescription(
            "Chance (percent) that a baby gets one star more than its weaker parent when the best farmer has Farming 0. "
            + "It rises in a straight line to the next setting at Farming 100. The Farming of the player whose game "
            + "handles the birth always counts." + ServerWins,
            new AcceptableValueRange<float>(ServerSettings.MinChance, ServerSettings.MaxChance),
            new ConfigurationManagerAttributes { Order = 90 }));
        ChanceAtFarming100 = Config.Bind("General", "ChanceAtFarming100", defaults.ChanceAtFarming100, new ConfigDescription(
            "Chance (percent) of the extra star when the best farmer has Farming 100." + ServerWins,
            new AcceptableValueRange<float>(ServerSettings.MinChance, ServerSettings.MaxChance),
            new ConfigurationManagerAttributes { Order = 80 }));
        ChanceWithoutFarmer = Config.Bind("General", "ChanceWithoutFarmer", defaults.ChanceWithoutFarmer, new ConfigDescription(
            "Chance (percent) of the extra star when no farmer is known at all: the game handling the birth has no "
            + "character at that moment (for example while its player respawns) and no other player with this mod is "
            + "within FarmerRange. Rare in normal play." + ServerWins,
            new AcceptableValueRange<float>(ServerSettings.MinChance, ServerSettings.MaxChance),
            new ConfigurationManagerAttributes { Order = 70 }));
        FarmerRange = Config.Bind("General", "FarmerRange", 60f, new ConfigDescription(
            "How close (in metres) another player must be to the animal giving birth for their Farming skill to count. "
            + "Only players who have this mod count, and only while the game handling the birth has them loaded. The "
            + "Farming of the player whose game handles the birth counts at any distance." + ServerWins,
            new AcceptableValueRange<float>(ServerSettings.MinFarmerRange, ServerSettings.MaxFarmerRange),
            new ConfigurationManagerAttributes { Order = 60 }));
        MaxStars = Config.Bind("General", "MaxStars", defaults.MaxStars, new ConfigDescription(
            "The extra star never takes a baby above this many stars (2 is the most the game shows). Babies whose "
            + "weaker parent already has this many stars or more keep that level and get no extra star. 0 turns the "
            + "extra star off." + ServerWins,
            new AcceptableValueRange<int>(ServerSettings.MinMaxStars, ServerSettings.MaxMaxStars),
            new ConfigurationManagerAttributes { Order = 50 }));
        AllowPlayersWithoutMod = Config.Bind("General", "AllowPlayersWithoutMod", false, new ConfigDescription(
            "Used only by the server (or the host). Off (default): a player whose game does not have this mod "
            + "installed is refused about a second after joining, and their game shows \"Incompatible version\", "
            + "because the animals their game simulates would breed the normal game way. The check only looks at "
            + "whether the mod is installed: a player who turned it off on their own game is not refused. On: players "
            + "without the mod may play, and the animals their game simulates breed the normal game way (the baby "
            + "takes the level of the parent that gives birth).",
            null, new ConfigurationManagerAttributes { Order = 40 }));
    }

    // This game's own config values (sent by the server; used in single player, as host, as server).
    internal static BreedingSettings OwnSettings()
    {
        return new BreedingSettings(
            new RuleSettings(ChanceAtFarming0.Value, ChanceAtFarming100.Value, ChanceWithoutFarmer.Value, MaxStars.Value),
            FarmerRange.Value, SettingsSource.Own);
    }

    // Only door to birth numbers, read at every birth. Server's numbers when this game is a client of a server with me,
    // else own. Debug build: self test may force rule numbers (never the config file); range stay effective one.
    internal static BreedingSettings CurrentSettings()
    {
        var effective = ServerSettings.Effective(OwnSettings());
#if DEBUG
        if (SelfTests.Override.HasValue)
        {
            return new BreedingSettings(SelfTests.Override.Value, effective.FarmerRange, SettingsSource.Test);
        }
#endif
        return effective;
    }

    // Turned on (game start, or live): look for Star Level System again at next use, publish Farming now if in world,
    // start network part (server: check players already in, send numbers; client: ask numbers).
    protected override void OnActivated()
    {
        Compat.Reset();
        ProcreationPatches.ClearStandAsideLog();
        FarmerSkill.Reset();
        FarmerSkill.PublishNow();
        ServerSettings.Start();
        PlayerCheck.Start();
        Subscribe(true);
        SelfTests.Register();
    }

    // Turned off (patches still on during this call; also at quit): not a farmer anymore (-1, guarded), pending join
    // checks cancelled, server numbers forgotten. Notes on pregnant animals stay and go stale: harmless.
    protected override void OnDeactivated()
    {
        SelfTests.Unregister();
        Subscribe(false);
        PlayerCheck.Stop();
        ServerSettings.Stop();
        FarmerSkill.Withdraw();
        FarmerSkill.Reset();
        ProcreationPatches.ClearStandAsideLog();
    }

    // Off first: never twice.
    private static void Subscribe(bool on)
    {
        ChanceAtFarming0.SettingChanged -= OnRuleSettingChanged;
        ChanceAtFarming100.SettingChanged -= OnRuleSettingChanged;
        ChanceWithoutFarmer.SettingChanged -= OnRuleSettingChanged;
        FarmerRange.SettingChanged -= OnRuleSettingChanged;
        MaxStars.SettingChanged -= OnRuleSettingChanged;
        AllowPlayersWithoutMod.SettingChanged -= OnAllowChanged;
        if (!on)
        {
            return;
        }
        ChanceAtFarming0.SettingChanged += OnRuleSettingChanged;
        ChanceAtFarming100.SettingChanged += OnRuleSettingChanged;
        ChanceWithoutFarmer.SettingChanged += OnRuleSettingChanged;
        FarmerRange.SettingChanged += OnRuleSettingChanged;
        MaxStars.SettingChanged += OnRuleSettingChanged;
        AllowPlayersWithoutMod.SettingChanged += OnAllowChanged;
    }

    // Server: send numbers again (next ZNet.Update, once for many changes). Elsewhere: flag cleared, nothing sent.
    private static void OnRuleSettingChanged(object sender, EventArgs e)
    {
        try
        {
            ServerSettings.MarkChanged();
        }
        catch (Exception ex)
        {
            PatchGuard.Report("Plugin.OnRuleSettingChanged", ex);
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
