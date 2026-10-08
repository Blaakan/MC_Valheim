using System;
using BepInEx.Configuration;
using MC.Shared;
using UnityEngine;

namespace MC.UX.AutoPickupFilterMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, server check, status, live on/off. Patches in Patches/ apply only while Active.
// No Awake/Start/Update/OnDestroy here (they hide ModPlugin ones). Use BindConfig / OnActivated / OnDeactivated.
// Me filter what auto pickup collect (Everything / Skip ignored / Only selected), lists per character, edited in the
// inventory. Vanilla auto pickup key stay master switch; manual pickup and hand harvest never filtered.
internal sealed partial class Plugin : ModPlugin
{
    internal static ConfigEntry<bool> ExemptHarvest;
    internal static ConfigEntry<KeyboardShortcut> MarkKey;
    internal static ConfigEntry<bool> GamepadControls;
    internal static ConfigEntry<bool> ShowMarkers;
    internal static ConfigEntry<bool> ShowInHoverText;
    internal static ConfigEntry<float> ButtonOffsetX;
    internal static ConfigEntry<float> ButtonOffsetY;
    internal static ConfigEntry<string> DefaultIgnored;
    internal static ConfigEntry<string> DefaultSelected;

    // ---------------------------------------------------------------- settings as mod code read them
    // One accessor per setting. Release build: plain config read. Debug build: self test may force a value in memory
    // (TestHooks), so test never write the config file.

    internal static bool ExemptHarvestOn
    {
        get
        {
#if DEBUG
            if (TestHooks.ExemptHarvest.HasValue)
            {
                return TestHooks.ExemptHarvest.Value;
            }
#endif
            return ExemptHarvest.Value;
        }
    }

    internal static bool GamepadControlsOn
    {
        get
        {
#if DEBUG
            if (TestHooks.GamepadControls.HasValue)
            {
                return TestHooks.GamepadControls.Value;
            }
#endif
            return GamepadControls.Value;
        }
    }

    internal static bool ShowMarkersOn
    {
        get
        {
#if DEBUG
            if (TestHooks.ShowMarkers.HasValue)
            {
                return TestHooks.ShowMarkers.Value;
            }
#endif
            return ShowMarkers.Value;
        }
    }

    internal static bool ShowInHoverTextOn
    {
        get
        {
#if DEBUG
            if (TestHooks.ShowInHoverText.HasValue)
            {
                return TestHooks.ShowInHoverText.Value;
            }
#endif
            return ShowInHoverText.Value;
        }
    }

    internal static float ButtonOffsetXNow
    {
        get
        {
#if DEBUG
            if (TestHooks.ButtonOffsetX.HasValue)
            {
                return TestHooks.ButtonOffsetX.Value;
            }
#endif
            return ButtonOffsetX.Value;
        }
    }

    internal static float ButtonOffsetYNow
    {
        get
        {
#if DEBUG
            if (TestHooks.ButtonOffsetY.HasValue)
            {
                return TestHooks.ButtonOffsetY.Value;
            }
#endif
            return ButtonOffsetY.Value;
        }
    }

    // Null = setting not bound yet (very early call).
    internal static string DefaultIgnoredText
    {
        get
        {
#if DEBUG
            if (TestHooks.DefaultIgnored != null)
            {
                return TestHooks.DefaultIgnored;
            }
#endif
            return DefaultIgnored != null ? DefaultIgnored.Value : null;
        }
    }

    internal static string DefaultSelectedText
    {
        get
        {
#if DEBUG
            if (TestHooks.DefaultSelected != null)
            {
                return TestHooks.DefaultSelected;
            }
#endif
            return DefaultSelected != null ? DefaultSelected.Value : null;
        }
    }

    internal static KeyboardShortcut MarkKeyNow
    {
        get
        {
#if DEBUG
            if (TestHooks.MarkKey.HasValue)
            {
                return TestHooks.MarkKey.Value;
            }
#endif
            return MarkKey.Value;
        }
    }

#if DEBUG
    // Self test turn me off and on through the framework (patches really removed), without writing Enabled.
    protected override string LocalBlocker() => TestHooks.ForceOff ? "Inactive: turned off by a self test." : null;
#endif

    protected override void BindConfig()
    {
        ExemptHarvest = Config.Bind("Filter", "ExemptHarvest", true, new ConfigDescription(
            "Items that drop when you harvest or collect by hand (the use key on berry bushes, mushrooms, crops, pickable "
            + "stones, a fermenter, a cooking station or stone oven, a beehive, a sap collector, an item or armor stand, an "
            + "archery target, the empty switch of a production station, or a scythe swing) are picked up whatever the filter "
            + "mode, like picking them up by hand. Turn this off to filter them like any other item. Mining, chopping and "
            + "fighting drops are filtered, except a drop that lands within 4 m of something you harvested in the last "
            + "3 seconds.",
            null, new ConfigurationManagerAttributes { Order = 100 }));

        MarkKey = Config.Bind("Controls", "MarkKey", new KeyboardShortcut(KeyCode.Mouse2), new ConfigDescription(
            "Key or mouse button that adds the item under the mouse pointer to the list of the current filter mode, or "
            + "removes it, in your inventory or an open chest. Default: middle mouse button (Mouse2). Avoid Mouse0 and "
            + "Mouse1: the inventory already uses the left and right buttons. Set to None to turn marking off. Mouse5, "
            + "Mouse6 and some keys (F13-F15, some symbol keys such as # or @) cannot be read by the game; if you pick "
            + "one, marking is turned off and the log says so.",
            null, new ConfigurationManagerAttributes { Order = 100 }));
        GamepadControls = Config.Bind("Controls", "GamepadControls", true, new ConfigDescription(
            "Controller, with your inventory grid selected: click the right stick to add or remove the selected item; "
            + "hold LT and click the right stick to change the filter mode; hold RT and click the right stick to show "
            + "both lists. Chest slots can only be marked with the mouse. Turn this off if another mod uses the right "
            + "stick in the inventory.",
            null, new ConfigurationManagerAttributes { Order = 90 }));

        ShowMarkers = Config.Bind("Display", "ShowMarkers", true, new ConfigDescription(
            "Show a small mark on items of the active list in your inventory and in chests (red: ignored, green: selected).",
            null, new ConfigurationManagerAttributes { Order = 100 }));
        ShowInHoverText = Config.Bind("Display", "ShowInHoverText", true, new ConfigDescription(
            "When you look at an item on the ground that auto pickup will skip, say so under its name.",
            null, new ConfigurationManagerAttributes { Order = 90 }));
        ButtonOffsetX = Config.Bind("Display", "ButtonOffsetX", 0f, new ConfigDescription(
            "Moves the Auto pickup button sideways (pixels, positive = right) if it overlaps something added by another mod.",
            null, new ConfigurationManagerAttributes { Order = 80 }));
        ButtonOffsetY = Config.Bind("Display", "ButtonOffsetY", 0f, new ConfigDescription(
            "Moves the Auto pickup button up or down (pixels, positive = up).",
            null, new ConfigurationManagerAttributes { Order = 70 }));

        DefaultIgnored = Config.Bind("Defaults", "IgnoredItems", "", new ConfigDescription(
            "Ignored items for a character that has never used the filter: comma-separated item names as used by the "
            + "spawn command (for example Stone,Wood,Resin). Once you change the filter in game, that character keeps "
            + "its own lists.",
            null, new ConfigurationManagerAttributes { Order = 100 }));
        DefaultSelected = Config.Bind("Defaults", "SelectedItems", "", new ConfigDescription(
            "Selected items for a character that has never used the filter, same format (for example Coins,Amber,Ruby).",
            null, new ConfigurationManagerAttributes { Order = 90 }));

        // Me read key once per change (panel, ConfigurationManager, file edit), not every frame.
        FilterUi.CacheMarkKey(MarkKeyNow);
        MarkKey.SettingChanged += (_, _) => FilterUi.CacheMarkKey(MarkKeyNow);
        ButtonOffsetX.SettingChanged += (_, _) => FilterUi.PlacementDirty = true;
        ButtonOffsetY.SettingChanged += (_, _) => FilterUi.PlacementDirty = true;
        // Verdict or shown text depend on these: caches out, label/tooltip redraw.
        ExemptHarvest.SettingChanged += (_, _) => FilterState.BumpVersion();
        ShowMarkers.SettingChanged += (_, _) => FilterState.BumpVersion();
        ShowInHoverText.SettingChanged += (_, _) => FilterState.BumpVersion();
        GamepadControls.SettingChanged += (_, _) => FilterState.BumpVersion();
        DefaultIgnored.SettingChanged += (_, _) => FilterState.OnDefaultsChanged();
        DefaultSelected.SettingChanged += (_, _) => FilterState.OnDefaultsChanged();
    }

    // Me just turned on (game start, or live from panel/config, maybe with inventory open).
    protected override void OnActivated()
    {
        // Throw = game changed under us: framework turn feature off with an error, nothing else made yet.
        AutoPickupGuard.Verify();
        AutoPickupGuard.ForeignCheckDone = false;
        AutoPickupScope.Active = false;
        AutoPickupScope.LastEnabled = null;
        FilterState.Reset(); // reload from character on next use
        HarvestGrace.Clear();
        LootFilterCommand.Register();
        var gui = InventoryGui.instance;
        if (gui != null)
        {
            FilterUi.EnsureButton(gui);
            FilterUi.PlacementDirty = true;
        }
        // Debug build only (call vanish in Release): in-world self tests.
        SelfTests.Register();
    }

    // Me going off (patches still on during this call). Every object me made go away; lists stay in character.
    protected override void OnDeactivated()
    {
        SelfTests.Unregister();
        AutoPickupScope.Active = false;
        AutoPickupScope.LastEnabled = null;
        Safe(FilterUi.DestroyButton, "button");
        Safe(MarkerOverlay.DestroyAll, "markers");
        Safe(LootFilterCommand.Unregister, "commands");
        FilterState.Reset();
        HarvestGrace.Clear();
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
