using System;
using BepInEx.Configuration;
using MC.Shared;

namespace MC.Exploration.ViewSpyglassMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, server check, status, live on/off. Patches in Patches/ apply only while Active.
// No Awake/Start/Update/OnDestroy here (they hide ModPlugin ones). Use BindConfig / OnActivated / OnDeactivated.
// Me add a craftable spyglass (right hand). Attack = raise it to the eye: arm come up (procedural pose, other players
// see it through a player ZDO bool), camera slide into the eye and zoom, round clear view with blurred dark edge,
// look slower, feet stay. Far land and objects come from Distant Horizons when it run (me ask it for more detail in
// the looked-at direction through a shared slot, ViewBoost). Both side: server refuse players who cannot play by its
// rules (PlayerCheck) and send its rules to everyone (ServerRules). Item stay registered while feature off
// ([AlwaysOnPatch] classes read FeatureActive themselves).
internal sealed partial class Plugin : ModPlugin
{
    private const string General = "General";
    private const string RecipeSection = "Recipe";
    private const string ZoomSection = "Zoom";
    private const string ControlsSection = "Controls";
    private const string ViewSection = "View";
    private const string ServerWins = " In multiplayer the setting of the server (or host) is used for everyone.";
    private const string Personal = " Each player's own choice.";

    // General (server only).
    internal static ConfigEntry<bool> AllowPlayersWithoutMod;

    // Recipe (rules, server wins).
    internal static ConfigEntry<string> RecipeResources;
    internal static ConfigEntry<string> RecipeStation;
    internal static ConfigEntry<int> RecipeStationLevel;

    // Zoom (rules, server wins).
    internal static ConfigEntry<float> MaxMagnification;
    internal static ConfigEntry<float> FogClearing;

    // Controls (personal, never sent).
    internal static ConfigEntry<bool> HoldToLook;
    internal static ConfigEntry<float> StartMagnification;
    internal static ConfigEntry<float> AimSensitivity;

    // View (personal, never sent).
    internal static ConfigEntry<float> ClearViewSize;
    internal static ConfigEntry<bool> EdgeBlur;
    internal static ConfigEntry<float> EdgeDarkness;

    private static bool _featureActive;

#if DEBUG
    // Self test play "feature off" without the toggle. Never in release.
    internal static bool TestInactive { get; set; }
#endif

    // Feature on (patches applied) and not faked off by a self test. Always-on code (registration) read this: its
    // patches stay while the feature is off.
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
        var d = SpyglassRules.Default;

        AllowPlayersWithoutMod = Config.Bind(General, "AllowPlayersWithoutMod", false, new ConfigDescription(
            "Used only by the server (or the host). Off (default): a player whose game does not have this mod, has it "
            + "turned off or has another version of it is refused about a second after joining, or about a second after "
            + "turning it off, and their game shows \"Incompatible version\", so every game knows the spyglass. On: they "
            + "may play, but they never see spyglasses (on the ground, in chests or in hands), and a chest loses its "
            + "spyglasses when such a player takes or adds an item in it.",
            null, new ConfigurationManagerAttributes { Order = 90 }));

        RecipeResources = Config.Bind(RecipeSection, "RecipeResources", d.RecipeResources, new ConfigDescription(
            "Materials of one spyglass, as item names with amounts: Name:amount,Name:amount. The game has no glass, so "
            + "the lenses are made of Crystal by default. Unknown names are skipped (with a warning in the log); with no "
            + "valid material the recipe is hidden." + ServerWins,
            null, new ConfigurationManagerAttributes { Order = 100 }));
        RecipeStation = Config.Bind(RecipeSection, "RecipeStation", d.RecipeStation, new ConfigDescription(
            "Crafting station of the recipe, by its object name: forge (default), piece_workbench, blackforge, "
            + "piece_artisanstation... Empty: crafted by hand from the inventory." + ServerWins,
            null, new ConfigurationManagerAttributes { Order = 95 }));
        RecipeStationLevel = Config.Bind(RecipeSection, "RecipeStationLevel", d.RecipeStationLevel,
            new ConfigDescription(
                "Station level the recipe needs." + ServerWins,
                new AcceptableValueRange<int>(SpyglassRules.StationLevelMin, SpyglassRules.StationLevelMax),
                new ConfigurationManagerAttributes { Order = 90, ShowRangeAsPercent = false }));

        MaxMagnification = Config.Bind(ZoomSection, "MaxMagnification", d.MaxMagnification, new ConfigDescription(
            "Strongest zoom of the spyglass (how many times closer things look). The mouse wheel goes from x"
            + Scope.MinMagnification.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
            + " to this." + ServerWins,
            new AcceptableValueRange<float>(SpyglassRules.MagnificationMin, SpyglassRules.MagnificationMax),
            new ConfigurationManagerAttributes { Order = 100 }));
        FogClearing = Config.Bind(ZoomSection, "FogClearing", d.FogClearing, new ConfigDescription(
            "How much of the haze the spyglass looks through in clear weather: 0 = none, 1 = all of it. Only with the "
            + "Distant Horizons mod, which draws the land beyond the normal view distance; without it there is nothing "
            + "more to see behind the fog, so the fog is left as it is. Rain, storms and mist are never cleared."
            + ServerWins,
            new AcceptableValueRange<float>(0f, 1f),
            new ConfigurationManagerAttributes { Order = 95, ShowRangeAsPercent = false }));

        HoldToLook = Config.Bind(ControlsSection, "HoldToLook", false, new ConfigDescription(
            "Off (default): click (Attack) to raise the spyglass, click again to lower it. On: the spyglass stays at "
            + "your eye only while you hold Attack. Block (right mouse button) always lowers it." + Personal,
            null, new ConfigurationManagerAttributes { Order = 100 }));
        StartMagnification = Config.Bind(ControlsSection, "StartMagnification", Scope.DefaultStartMagnification,
            new ConfigDescription(
                "Zoom when you first raise the spyglass; the mouse wheel changes it while you look, and it is kept until "
                + "you quit the game. Never more than MaxMagnification." + Personal,
                new AcceptableValueRange<float>(Scope.MinMagnification, SpyglassRules.MagnificationMax),
                new ConfigurationManagerAttributes { Order = 95 }));
        AimSensitivity = Config.Bind(ControlsSection, "AimSensitivity", 1f, new ConfigDescription(
            "How fast you aim while looking through the spyglass. 1 = the view moves on screen as fast as without the "
            + "spyglass (the mouse turns you more slowly the stronger the zoom); lower = slower, higher = faster."
            + Personal,
            new AcceptableValueRange<float>(0.25f, 2f),
            new ConfigurationManagerAttributes { Order = 90 }));

        ClearViewSize = Config.Bind(ViewSection, "ClearViewSize", ScopeOverlay.DefaultClearSize, new ConfigDescription(
            "Size of the sharp round view in the middle of the screen, as a share of the screen height: 1 = as tall "
            + "as the screen. Around it the view is blurred and darker." + Personal,
            new AcceptableValueRange<float>(0.3f, 1f),
            new ConfigurationManagerAttributes { Order = 100, ShowRangeAsPercent = false }));
        EdgeBlur = Config.Bind(ViewSection, "EdgeBlur", true, new ConfigDescription(
            "Blur the view outside the sharp circle. Off: the edge is only darkened." + Personal,
            null, new ConfigurationManagerAttributes { Order = 95 }));
        EdgeDarkness = Config.Bind(ViewSection, "EdgeDarkness", ScopeOverlay.DefaultEdgeDarkness,
            new ConfigDescription(
                "How dark the screen gets outside the sharp circle: 0 = not at all, 1 = black." + Personal,
                new AcceptableValueRange<float>(0f, 1f),
                new ConfigurationManagerAttributes { Order = 90, ShowRangeAsPercent = false }));

        // Rule setting (sections Recipe, Zoom): new own snapshot, server send it again. Others: not rules.
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
        Scope.Reset();
        ServerRules.Start();
        PlayerCheck.Start();
        SpyglassContent.Rebuild();
        SelfTests.Register();
    }

    // Patches still on during this call (also at game quit). Each step alone: one failure never skip the rest.
    // Spyglass lowered at once: camera, fog, body, crosshair, overlay, Distant Horizons slot and the ZDO flag back.
    // Recipe hidden; the item stays known (always-on registration).
    protected override void OnDeactivated()
    {
        _featureActive = false;
        // Conditional method (gone in Release): no delegate to it, so own try instead of Step().
        try
        {
            SelfTests.Unregister();
        }
        catch (Exception e)
        {
            PatchGuard.Report("Plugin.OnDeactivated SelfTests.Unregister", e);
        }
        Step("Scope.Shutdown", Scope.Shutdown);
        Step("ArmPose.Shutdown", ArmPose.Shutdown);
        Step("SpyglassContent.Rebuild", () => SpyglassContent.Rebuild());
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

    // Rebuild waits until the rules stay still (a recipe typed in a config window change at every key).
    private static void OnRulesChanged()
    {
        try
        {
            SpyglassContent.RequestRebuild();
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
            if (setting == null)
            {
                return;
            }
            var section = setting.Definition.Section;
            if (section == RecipeSection || section == ZoomSection)
            {
                ServerRules.OwnChanged();
            }
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
