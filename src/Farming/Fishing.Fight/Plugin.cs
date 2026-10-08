using System;
using BepInEx.Configuration;
using MC.Shared;

namespace MC.Farming.FishingFightMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, server check, status, live on/off. Patches in Patches/ apply only while Active.
// No Awake/Start/Update/OnDestroy here (they hide ModPlugin ones). Use BindConfig / OnActivated / OnDeactivated.
// Me change the reel once a fish is hooked: calm = catch bar (fish in the bar = line in for free, out = stamina
// drain), struggle = fish run to one side, rod the other way + reel (wrong side = big stamina, no line; no reel = fish
// take line). Cast, bait, bite and hook stay vanilla. Both side: server refuse players who cannot play by its rules
// (PlayerCheck) and send its rules to everyone (ServerRules). Game code read rules only through ServerRules.Current.
// Nothing saved anywhere: turning off only end the fight in progress (vanilla reel take the hooked fish) and remove
// the bar.
internal sealed partial class Plugin : ModPlugin
{
    private const string General = "General";
    private const string FightSection = "Fight";
    private const string DisplaySection = "Display";
    private const string ServerWins = " In multiplayer the setting of the server (or host) is used for everyone.";
    private const string Personal = " Each player's own choice.";

    // General (server only).
    internal static ConfigEntry<bool> AllowPlayersWithoutMod;

    // Fight (rules, server wins).
    internal static ConfigEntry<float> BarSize;
    internal static ConfigEntry<float> BarSizeAtMaxSkill;
    internal static ConfigEntry<float> FishDifficulty;
    internal static ConfigEntry<float> OffBarStamina;
    internal static ConfigEntry<float> StruggleStamina;
    internal static ConfigEntry<float> WrongSideStamina;
    internal static ConfigEntry<float> RodAngle;
    internal static ConfigEntry<float> CalmSeconds;
    internal static ConfigEntry<float> StruggleSeconds;
    internal static ConfigEntry<float> LineRunSpeed;
    internal static ConfigEntry<float> ReelSpeed;

    // Display (personal, never sent).
    internal static ConfigEntry<bool> ShowStruggleArrow;
    internal static ConfigEntry<float> BarScale;
    internal static ConfigEntry<float> BarOffsetX;
    internal static ConfigEntry<float> BarOffsetY;

    protected override void BindConfig()
    {
        var d = FightRules.Default;

        AllowPlayersWithoutMod = Config.Bind(General, "AllowPlayersWithoutMod", false, new ConfigDescription(
            "Used only by the server (or the host). Off (default): a player whose game does not have this mod, has it "
            + "turned off or has another version of it is refused about a second after joining, or about a second after "
            + "turning it off, and their game shows \"Incompatible version\", so everyone fishes by the same rules. On: "
            + "they may play; they fish like in the normal game (hold Block to reel, no catch bar).",
            null, new ConfigurationManagerAttributes { Order = 90 }));

        BarSize = Config.Bind(FightSection, "BarSize", d.BarSize, new ConfigDescription(
            "Height of the catch zone (the green part you move) at Fishing skill 0, as a part of the catch bar (0.24 = "
            + "about a quarter). The zone grows with your Fishing skill up to BarSizeAtMaxSkill." + ServerWins,
            new AcceptableValueRange<float>(FightRules.BarSizeMin, FightRules.BarSizeMax),
            new ConfigurationManagerAttributes { Order = 100 }));
        BarSizeAtMaxSkill = Config.Bind(FightSection, "BarSizeAtMaxSkill", d.BarSizeAtMaxSkill, new ConfigDescription(
            "Height of the catch zone at Fishing skill 100, as a part of the catch bar." + ServerWins,
            new AcceptableValueRange<float>(FightRules.BarSizeMin, FightRules.BarSizeMax),
            new ConfigurationManagerAttributes { Order = 95 }));
        FishDifficulty = Config.Bind(FightSection, "FishDifficulty", d.FishDifficulty, new ConfigDescription(
            "How hard every fish is, as a multiple (1 = normal). Harder fish move more wildly in the catch bar, fight "
            + "more often and for longer, and take line faster. Fish from later biomes and fish with stars are harder."
            + ServerWins,
            new AcceptableValueRange<float>(FightRules.DifficultyMin, FightRules.DifficultyMax),
            new ConfigurationManagerAttributes { Order = 90 }));
        OffBarStamina = Config.Bind(FightSection, "OffBarStamina", d.OffBarStamina, new ConfigDescription(
            "Stamina used per second while the fish is outside the catch zone, as a multiple of the normal game's cost "
            + "of reeling (1 = the same; your Fishing skill lowers it as in the normal game). 0 = free." + ServerWins,
            new AcceptableValueRange<float>(0f, FightRules.StaminaMultiplierMax),
            new ConfigurationManagerAttributes { Order = 85 }));
        StruggleStamina = Config.Bind(FightSection, "StruggleStamina", d.StruggleStamina, new ConfigDescription(
            "Stamina used per second while you reel a fighting fish with your rod pointing the right way, as a "
            + "multiple of the normal game's cost of reeling a fighting fish." + ServerWins,
            new AcceptableValueRange<float>(0f, FightRules.StaminaMultiplierMax),
            new ConfigurationManagerAttributes { Order = 80 }));
        WrongSideStamina = Config.Bind(FightSection, "WrongSideStamina", d.WrongSideStamina, new ConfigDescription(
            "While the fish fights, reeling with your rod pointing the wrong way costs this many times more stamina, "
            + "and no line comes in." + ServerWins,
            new AcceptableValueRange<float>(FightRules.WrongSideMin, FightRules.WrongSideMax),
            new ConfigurationManagerAttributes { Order = 75 }));
        RodAngle = Config.Bind(FightSection, "RodAngle", d.RodAngle, new ConfigDescription(
            "While the fish fights, your rod (the way you face) must point at least this many degrees away from the "
            + "line, on the side opposite to where the fish runs." + ServerWins,
            new AcceptableValueRange<float>(FightRules.RodAngleMin, FightRules.RodAngleMax),
            new ConfigurationManagerAttributes { Order = 70 }));
        CalmSeconds = Config.Bind(FightSection, "CalmSeconds", d.CalmSeconds, new ConfigDescription(
            "Average time in seconds between two fights of a hooked fish (the catch bar shows meanwhile). Harder fish "
            + "fight a bit more often." + ServerWins,
            new AcceptableValueRange<float>(FightRules.CalmSecondsMin, FightRules.CalmSecondsMax),
            new ConfigurationManagerAttributes { Order = 65 }));
        StruggleSeconds = Config.Bind(FightSection, "StruggleSeconds", d.StruggleSeconds, new ConfigDescription(
            "Average length in seconds of one fight. Harder fish and fish with stars fight longer." + ServerWins,
            new AcceptableValueRange<float>(FightRules.StruggleSecondsMin, FightRules.StruggleSecondsMax),
            new ConfigurationManagerAttributes { Order = 60 }));
        LineRunSpeed = Config.Bind(FightSection, "LineRunSpeed", d.LineRunSpeed, new ConfigDescription(
            "While the fish fights and you do not reel, it takes this many metres of line per second (an average fish; "
            + "easy fish less, hard fish more). The line breaks when it gets longer than the rod allows (30 m)."
            + ServerWins,
            new AcceptableValueRange<float>(0f, FightRules.LineRunSpeedMax),
            new ConfigurationManagerAttributes { Order = 55 }));
        ReelSpeed = Config.Bind(FightSection, "ReelSpeed", d.ReelSpeed, new ConfigDescription(
            "How fast the line comes in, as a multiple of the normal game's reel speed (it rises with your Fishing "
            + "skill; a hooked fish also slows it down, as in the normal game). While the fish fights, the reel speed "
            + "is halved." + ServerWins,
            new AcceptableValueRange<float>(FightRules.ReelSpeedMin, FightRules.ReelSpeedMax),
            new ConfigurationManagerAttributes { Order = 50 }));

        ShowStruggleArrow = Config.Bind(DisplaySection, "ShowStruggleArrow", false, new ConfigDescription(
            "While the fish fights, show an arrow beside the crosshair pointing the way to turn your rod; it is red "
            + "until your rod points far enough that way, then green. Off (default): watch the fish, the float and the "
            + "line." + Personal,
            null, new ConfigurationManagerAttributes { Order = 100 }));
        BarScale = Config.Bind(DisplaySection, "BarScale", 1f, new ConfigDescription(
            "Size of the catch bar on screen (1 = normal)." + Personal,
            new AcceptableValueRange<float>(0.5f, 2f),
            new ConfigurationManagerAttributes { Order = 95 }));
        BarOffsetX = Config.Bind(DisplaySection, "BarOffsetX", 260f, new ConfigDescription(
            "Where the catch bar sits: distance to the right of the screen centre (negative = to the left)." + Personal,
            new AcceptableValueRange<float>(-1000f, 1000f),
            new ConfigurationManagerAttributes { Order = 90 }));
        BarOffsetY = Config.Bind(DisplaySection, "BarOffsetY", 0f, new ConfigDescription(
            "Where the catch bar sits: distance above the screen centre (negative = below)." + Personal,
            new AcceptableValueRange<float>(-600f, 600f),
            new ConfigurationManagerAttributes { Order = 85 }));

        // Every rule setting (section Fight): new own snapshot, server send it again. General and Display: not rules.
        Config.SettingChanged += OnSettingChanged;
        AllowPlayersWithoutMod.SettingChanged += OnAllowChanged;
    }

#if DEBUG
    // Self test: this game's copy blocked (real OnDeactivated / OnActivated through the framework, Enabled never
    // written; a connected server is told "off" like a real turn-off, so single-player tests only). Test set it, then
    // FeatureRegistry.RefreshAll(). Not cleared with the other overrides (turning off clear those): the test clear it.
    internal static bool TestBlocked { get; set; }
#endif

    protected override string LocalBlocker()
    {
#if DEBUG
        if (TestBlocked)
        {
            return "Inactive: turned off by a self-test.";
        }
#endif
        return ForeignMods.BlockerText();
    }

    protected override void OnActivated()
    {
        Fight.Shutdown();
        ServerRules.Start();
        PlayerCheck.Start();
        SelfTests.Register();
    }

    // Patches still on during this call (also at game quit). Each step alone: one failure never skip the rest. Fight
    // in progress ended (fish stay hooked, vanilla reel from the next tick), bar destroyed.
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
        Step("Fight.Shutdown", Fight.Shutdown);
        Step("FightHud.DestroyAll", FightHud.DestroyAll);
        Step("PlayerCheck.Stop", PlayerCheck.Stop);
        Step("ServerRules.Stop", ServerRules.Stop);
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
            var setting = e?.ChangedSetting;
            if (setting == null || setting.Definition.Section != FightSection)
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
