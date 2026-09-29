using System;
using BepInEx.Configuration;
using MC.Shared;

namespace MC.Exploration.CompendiumEncyclopediaMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, server check, status, live on/off. Patches in Patches/ apply only while Active.
// No Awake/Start/Update/OnDestroy here (they hide ModPlugin ones). Use BindConfig / OnActivated / OnDeactivated.
// Encyclopedia: window with every item, piece and creature, "???" until discovered. Data side = catalog
// (CatalogService), knowledge (Knowledge, OwnRecords), details (DetailBuilder), list rows (ListBuilder). UI side = two
// top tabs in the game's Valheim Compendium (TopTabs) + our window, and the opt-in side button (SideButton).
internal sealed partial class Plugin : ModPlugin
{
    /// <summary>List undiscovered entries as "???" rows (false = only discovered ones). Code read <see cref="ShowUndiscoveredOn"/>.</summary>
    internal static ConfigEntry<bool> ShowUndiscovered;

    /// <summary>Spoiler mode: every entry and detail shown. Code read <see cref="RevealAllOn"/>.</summary>
    internal static ConfigEntry<bool> RevealAll;

    /// <summary>Opt-in: our own button in the inventory side row too. Code read <see cref="SideButtonOn"/>.</summary>
    internal static ConfigEntry<bool> SideButtonSetting;

    /// <summary>
    /// Fire when ShowUndiscovered or RevealAll change (panel, ConfigurationManager, file). Each subscriber guarded.
    /// UI: refresh an open window.
    /// </summary>
    internal static event Action DisplaySettingsChanged;

    /// <summary>Live plugin (self tests toggle its Enabled entry).</summary>
    internal static Plugin Instance { get; private set; }

    /// <summary>ShowUndiscovered as the window use it. Read it here, never the entry (self tests force it).</summary>
    internal static bool ShowUndiscoveredOn
    {
        get
        {
#if DEBUG
            if (TestDisplay.HasValue)
            {
                return TestDisplay.Value.ShowUndiscovered;
            }
#endif
            return ShowUndiscovered == null || ShowUndiscovered.Value;
        }
    }

    /// <summary>RevealAll as the window use it. Read it here, never the entry (self tests force it).</summary>
    internal static bool RevealAllOn
    {
        get
        {
#if DEBUG
            if (TestDisplay.HasValue)
            {
                return TestDisplay.Value.RevealAll;
            }
#endif
            return RevealAll != null && RevealAll.Value;
        }
    }

    /// <summary>SideButton as the UI use it. Read it here, never the entry (self tests force it).</summary>
    internal static bool SideButtonOn
    {
        get
        {
#if DEBUG
            if (TestSideButton.HasValue)
            {
                return TestSideButton.Value;
            }
#endif
            return SideButtonSetting != null && SideButtonSetting.Value;
        }
    }

#if DEBUG
    /// <summary>Display values forced by self tests, in memory only (never the .cfg). Null = the real settings.</summary>
    internal struct DisplayOverride
    {
        internal bool ShowUndiscovered;
        internal bool RevealAll;
    }

    /// <summary>Self tests set me (and put back null in finally). Me never touch the ConfigEntry.</summary>
    internal static DisplayOverride? TestDisplay;

    /// <summary>SideButton forced by self tests, in memory only (then SideButton.Sync). Null = the real setting.</summary>
    internal static bool? TestSideButton;
#endif

    protected override void BindConfig()
    {
        Instance = this;
        ShowUndiscovered = Config.Bind("Display", "ShowUndiscovered", true, new ConfigDescription(
            "Show entries you have not discovered yet as \"???\". Turn off to list only what you have discovered.",
            null, new ConfigurationManagerAttributes { Order = 100 }));
        RevealAll = Config.Bind("Display", "RevealAll", false, new ConfigDescription(
            "Spoiler mode: show every entry and every detail, discovered or not. Your discoveries are still recorded.",
            null, new ConfigurationManagerAttributes { Order = 90 }));
        SideButtonSetting = Config.Bind("Display", "SideButton", false, new ConfigDescription(
            "Also add an Encyclopedia button to the inventory's side panel, right after the Valheim Compendium button (the "
            + "side panel's buttons move a little closer together to make room). Without it, open the Encyclopedia from the "
            + "Encyclopedia tab at the top of the Valheim Compendium.",
            null, new ConfigurationManagerAttributes { Order = 80 }));
        ShowUndiscovered.SettingChanged += (_, _) => RaiseDisplaySettingsChanged();
        RevealAll.SettingChanged += (_, _) => RaiseDisplaySettingsChanged();
        SideButtonSetting.SettingChanged += (_, _) => OnSideButtonSettingChanged();
    }

    // Me just turned on (game start, or live from panel/config, maybe in a world with inventory open).
    protected override void OnActivated()
    {
        // Data side. Catalog build run on this plugin object (always active), never on the window.
        CatalogService.SetHost(this);
        OwnRecords.ClearCache();
        TamesReader.ClearCache();
        Localization.OnLanguageChange += Names.OnLanguageChanged;
        OwnRecords.RecordCurrentBiome();
        SelfTests.Register();
        DebugCommand.Register();

        // UI side: Esc gate check, window refresh hooks, then at once if a world is loaded: the top tabs on an open
        // Valheim Compendium, the opt-in side button (it appears even with the inventory open). The window is built on
        // its first open.
        EscGuard.Verify();
        CatalogService.CatalogReady += CompendiumWindow.OnCatalogReady;
        DisplaySettingsChanged += CompendiumWindow.OnDisplaySettingsChanged;
        UiSelfTests.Register();
        var gui = InventoryGui.instance;
        if (gui != null)
        {
            TopTabs.OnActivated(gui);
            SideButton.Sync(gui);
        }
    }

    // Me going off (patches still on during this call). Undo what OnActivated made; records stay in the character.
    protected override void OnDeactivated()
    {
        // UI first: window (with its canvas, blocker, focus group, pools), top tabs on the vanilla dialog, button (with
        // its pad and drawn icon), the drawn paw icon of creatures.
        Safe(CompendiumWindow.Destroy, "window");
        Safe(TopTabs.Destroy, "top tabs");
        Safe(SideButton.Destroy, "button");
        Safe(PawIcon.Destroy, "paw icon");
        Safe(() => CatalogService.CatalogReady -= CompendiumWindow.OnCatalogReady, "catalog hook");
        Safe(() => DisplaySettingsChanged -= CompendiumWindow.OnDisplaySettingsChanged, "settings hook");
        Safe(FocusGuard.Reset, "focus guard");
        Safe(CloneUtil.Reset, "clone log");
        Safe(() => UiSelfTests.Unregister(), "UI self tests");
        Safe(() => Localization.OnLanguageChange -= Names.OnLanguageChanged, "language hook");
        Safe(CatalogService.Stop, "catalog");
        Safe(OwnRecords.ClearCache, "records cache");
        Safe(TamesReader.ClearCache, "tames cache");
        Safe(() => SelfTests.Unregister(), "self tests");
        Safe(() => DebugCommand.Unregister(), "debug command");
    }

    // SideButton changed (panel, ConfigurationManager, file): button added or removed at once, row put back exactly.
    // Only while Active (off = no button anyway; OnActivated sync it).
    private static void OnSideButtonSettingChanged()
    {
        try
        {
            if (Instance != null && Instance.IsActive)
            {
                SideButton.Sync(InventoryGui.instance);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("SideButton setting changed", e);
        }
    }

    private static void RaiseDisplaySettingsChanged()
    {
        var handlers = DisplaySettingsChanged;
        if (handlers == null)
        {
            return;
        }
        foreach (var d in handlers.GetInvocationList())
        {
            try
            {
                ((Action)d)();
            }
            catch (Exception e)
            {
                PatchGuard.Report("DisplaySettingsChanged subscriber", e);
            }
        }
    }

    // One cleanup failing must not skip the others.
    private static void Safe(Action step, string what)
    {
        try
        {
            step();
        }
        catch (Exception e)
        {
            Log.Error($"Cleanup of {what} failed: {e}");
        }
    }
}
