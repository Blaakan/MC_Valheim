using System;
using System.Globalization;
using BepInEx.Configuration;
using MC.Shared;

namespace MC.Farming.CultivatorReplantMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, server check, status, live on/off. Patches in Patches/ apply only while Active.
// No Awake/Start/Update/OnDestroy here (they hide ModPlugin ones). Use BindConfig / OnActivated / OnDeactivated.
// Me give the vanilla cultivator a Replant action (E, gamepad X, in build mode): aim at a wild plant, plant come out of
// the ground as a transplant item, transplant go in the ground again as a sapling that grow into the vanilla plant.
// Four new cultivator levels (4 black metal, 5 eitr, 6 flametal, 7 bloodgold, Forge upgrade tab) unlock more plants.
// Yggdrasil transplant grow only near an Ancient Root and drink its sap. Both side: server refuse players who cannot
// play by its rules (PlayerCheck) and send its rules to everyone (ServerRules). Items, saplings and table entries stay
// registered while feature off ([AlwaysOnPatch] classes read FeatureActive themselves).
internal sealed partial class Plugin : ModPlugin
{
    private const string General = "General";
    private const string UpgradesSection = "Upgrades";
    private const string GrowingSection = "Growing";
    private const string ServerWins = " In multiplayer the setting of the server (or host) is used for everyone.";

    // General (server only).
    internal static ConfigEntry<bool> AllowPlayersWithoutMod;

    // Upgrades (rules, server wins): materials of each new cultivator level.
    internal static ConfigEntry<string> BlackMetalLevel;
    internal static ConfigEntry<string> EitrLevel;
    internal static ConfigEntry<string> FlametalLevel;
    internal static ConfigEntry<string> BloodgoldLevel;

    // Growing (rules, server wins).
    internal static ConfigEntry<float> GrowTimeMultiplier;
    internal static ConfigEntry<int> RootSapCost;
    internal static ConfigEntry<float> RootRange;

    private static bool _featureActive;

#if DEBUG
    // Self test play "feature off" without the toggle. Never in release.
    internal static bool TestInactive { get; set; }

    // Self test (multiplayer run, server): players who cannot play by my rules may stay until this
    // Time.realtimeSinceStartup, like AllowPlayersWithoutMod on. Own end time: test that die never leave server open.
    // 0 = normal. Never in release.
    internal static float TestAllowUntil { get; set; }

    // Self test turned me off the real way (TestTurnOff) and not on again yet.
    internal static bool TestTurnedOff { get; private set; }

    // Self test: real turn off, same two steps as framework Refresh (OnDeactivated, then every feature patch away).
    // No Enabled write (config file stay as is), no state or Status change, server not told. Single player tests.
    internal static void TestTurnOff()
    {
        var self = TestSelf();
        if (self == null || TestTurnedOff)
        {
            return;
        }
        TestTurnedOff = true;
        self.OnDeactivated();
        self.Harmony.UnpatchSelf();
    }

    // Self test: on again, same two steps as framework Refresh (feature patches on, then OnActivated).
    internal static void TestTurnOn()
    {
        var self = TestSelf();
        if (self == null || !TestTurnedOff)
        {
            return;
        }
        TestTurnedOff = false;
        self.ApplyPatches(self.Harmony);
        self.OnActivated();
    }

    // Enabled entry of this plugin (multiplayer self tests only: their config files are throwaway).
    internal static ConfigEntry<bool> TestEnabledEntry => TestSelf()?.Enabled;

    private static Plugin TestSelf()
    {
        return BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(ModInfo.Guid, out var info)
            ? info.Instance as Plugin
            : null;
    }
#endif

    // AllowPlayersWithoutMod as the join check read it (one place). Entry not bound = refuse.
    internal static bool AllowsPlayersWithoutMod
    {
        get
        {
#if DEBUG
            if (TestAllowUntil > 0f && UnityEngine.Time.realtimeSinceStartup < TestAllowUntil)
            {
                return true;
            }
#endif
            return AllowPlayersWithoutMod != null && AllowPlayersWithoutMod.Value;
        }
    }

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
#if DEBUG
        // Self test log watch (replant.log): keep my own log lines from the start.
        SelfTestLog.Install(Logger);
#endif
        // Me pin ItemData.GetIcon as not inlined first (tier gem postfix must always run).
        IconInlineGuard.Apply();

        var d = CultivatorRules.Default;

        AllowPlayersWithoutMod = Config.Bind(General, "AllowPlayersWithoutMod", false, new ConfigDescription(
            "Used only by the server (or the host). Off (default): a player whose game does not have this mod, has it "
            + "turned off or has another version of it is refused about a second after joining, or about a second after "
            + "turning it off, and their game shows \"Incompatible version\", so every game knows the transplants and "
            + "the new cultivator levels and plays by the same rules. On: they may play. A player without the mod loses "
            + "any transplant items they receive and never sees transplant saplings, and a chest loses its transplants "
            + "when such a player takes or adds an item in it. A player with the mod turned off or with another version "
            + "of it keeps the transplants their game knows, but does not play by this server's rules: transplant "
            + "saplings in areas their game runs grow on that player's own timing, and Yggdrasil transplants there grow "
            + "without an Ancient Root and without taking sap.",
            null, new ConfigurationManagerAttributes { Order = 90 }));

        BlackMetalLevel = BindUpgrade("BlackMetalLevel", d.BlackMetalLevel, 4, "cloudberry bushes", 100);
        EitrLevel = BindUpgrade("EitrLevel", d.EitrLevel, 5, "Yggdrasil shoots", 95);
        FlametalLevel = BindUpgrade("FlametalLevel", d.FlametalLevel, 6, "fiddleheads and smoke puffs", 90);
        BloodgoldLevel = BindUpgrade("BloodgoldLevel", d.BloodgoldLevel, 7, "lingonberry bushes", 85);

        GrowTimeMultiplier = Config.Bind(GrowingSection, "GrowTimeMultiplier", d.GrowTimeMultiplier,
            new ConfigDescription(
                "A transplant grows in the plant's own regrowth time, on the same world clock as crops and berry "
                + "regrowth (bushes 5 h, forage 4 h, fiddlehead 5 h, Yggdrasil 2 h of play; sleeping skips ahead, and "
                + "nothing grows while the world is not running) times this: 0.5 = twice as fast, 2 = twice as slow. "
                + "A change applies at once, also to transplants already planted." + ServerWins,
                new AcceptableValueRange<float>(CultivatorRules.GrowTimeMultiplierMin,
                    CultivatorRules.GrowTimeMultiplierMax),
                new ConfigurationManagerAttributes { Order = 100, ShowRangeAsPercent = false }));
        RootSapCost = Config.Bind(GrowingSection, "RootSapCost", d.RootSapCost, new ConfigDescription(
            "Sap a Yggdrasil transplant takes from its Ancient Root when it grows. A root holds 50 and regains about 9 "
            + "per hour; while the root has too little, the transplant waits." + ServerWins,
            new AcceptableValueRange<int>(CultivatorRules.RootSapCostMin, CultivatorRules.RootSapCostMax),
            new ConfigurationManagerAttributes { Order = 95, ShowRangeAsPercent = false }));
        var room = RootGrowRoom();
        RootRange = Config.Bind(GrowingSection, "RootRange", d.RootRange, new ConfigDescription(
            "The farthest from an Ancient Root a Yggdrasil transplant can be planted, in metres. It also needs " + room
            + " m of room around it to grow, so plant it between " + room + " m and this distance from the root."
            + ServerWins,
            new AcceptableValueRange<float>(CultivatorRules.RootRangeMin, CultivatorRules.RootRangeMax),
            new ConfigurationManagerAttributes { Order = 90, ShowRangeAsPercent = false }));

        // Rule setting (sections Upgrades, Growing): new own snapshot, server send it again. Others: not rules.
        Config.SettingChanged += OnSettingChanged;
        AllowPlayersWithoutMod.SettingChanged += OnAllowChanged;
        // Rules in force changed (server rules came, own config, pending over): content follow. Whole session: rebuild
        // read feature state itself (off = saplings hidden, cultivator vanilla).
        ServerRules.Changed -= OnRulesChanged;
        ServerRules.Changed += OnRulesChanged;
    }

    // One cost setting per new level. Order high to low = levels in order in the config window.
    private ConfigEntry<string> BindUpgrade(string key, string value, int level, string unlocks, int order)
    {
        return Config.Bind(UpgradesSection, key, value, new ConfigDescription(
            $"Materials of the upgrade from level {level - 1} to level {level} (the {PlantCatalog.TierName(level)} "
            + $"cultivator, which can also replant {unlocks}), as item names with amounts: Name:amount,Name:amount. "
            + $"The upgrade is made at the Forge, which needs station level {level}. If the list has no valid "
            + $"material, the cultivator cannot be upgraded to level {level} or above (a warning is written in the log)."
            + ServerWins,
            null, new ConfigurationManagerAttributes { Order = order }));
    }

    // Grow radius of the root plant (Yggdrasil) as text: root mesh closer than this = no room to grow. From catalog,
    // not typed twice; no root plant there (never expected) = 2.
    private static string RootGrowRoom()
    {
        var radius = 2f;
        foreach (var kind in PlantCatalog.All)
        {
            if (kind != null && kind.NeedsRoot)
            {
                radius = kind.GrowRadius;
                break;
            }
        }
        return radius.ToString("0.#", CultureInfo.InvariantCulture);
    }

    protected override void OnActivated()
    {
        _featureActive = true;
        ServerRules.Start();
        PlayerCheck.Start();
        TransplantContent.Rebuild();
        SelfTests.Register();
    }

    // Patches still on during this call (also at game quit). Each step alone: one failure never skip the rest.
    // Replant target and hint forgot, saplings hidden from the build menu, cultivator back to vanilla levels and
    // recipe (Rebuild see feature off). Items and saplings stay known (always-on registration).
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
        Step("Replant.Reset", () => Replant.Reset());
        Step("ReplantHint.Reset", () => ReplantHint.Reset());
        Step("TransplantContent.Rebuild", () => TransplantContent.Rebuild());
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

    // Rebuild waits until the rules stay still (a cost typed in a config window change at every key); pending over or
    // back = at once (TransplantContent.RequestRebuild).
    private static void OnRulesChanged()
    {
        try
        {
            TransplantContent.RequestRebuild();
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
            if (section == UpgradesSection || section == GrowingSection)
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
