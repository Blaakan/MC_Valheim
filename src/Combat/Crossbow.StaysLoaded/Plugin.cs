using BepInEx.Configuration;
using MC.Shared;

namespace MC.Combat.CrossbowStaysLoadedMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin.
internal sealed partial class Plugin : ModPlugin
{
    internal static ConfigEntry<bool> ShowLoadedInTooltip;
    internal static ConfigEntry<bool> KeepCrossbows;
    internal static ConfigEntry<string> ExtraItems;
    internal static ConfigEntry<string> ExcludedItems;

    protected override void BindConfig()
    {
        // Order: ConfigurationManager list a section by Order (high first), else by name; "two settings above" need it.
        KeepCrossbows = Config.Bind("Weapons", "Crossbows", true, new ConfigDescription(
            "All crossbows keep their load: every weapon that uses the Crossbows skill, including crossbows added by other mods.",
            null, new ConfigurationManagerAttributes { Order = 100 }));
        ExtraItems = Config.Bind("Weapons", "ExtraItems", "GrapplingHook", new ConfigDescription(
            "Other weapons that must be reloaded and should also keep their load: comma-separated item names, as used by "
            + "the spawn command. Examples: GrapplingHook, StaffLightning (Dundr), or reload weapons from other mods.",
            null, new ConfigurationManagerAttributes { Order = 90 }));
        ExcludedItems = Config.Bind("Weapons", "ExcludedItems", "", new ConfigDescription(
            "Weapons that must never keep their load: comma-separated item names. Wins over the two settings above.",
            null, new ConfigurationManagerAttributes { Order = 80 }));
        ShowLoadedInTooltip = Config.Bind("UI", "ShowLoadedInTooltip", true, new ConfigDescription(
            "Show \"Loaded\" in the tooltip of a crossbow that still holds a bolt.",
            null, new ConfigurationManagerAttributes { Order = 100 }));

        // Me rebuild name lists now and on every change (panel, ConfigurationManager, file edit).
        RebuildWeaponFilter();
        ExtraItems.SettingChanged += (_, _) => RebuildWeaponFilter();
        ExcludedItems.SettingChanged += (_, _) => RebuildWeaponFilter();
    }

    private static void RebuildWeaponFilter() => WeaponFilter.Rebuild(ExtraItems.Value, ExcludedItems.Value);

    // Me just turned on (maybe mid-game). Catch up on what happened while me was off.
    protected override void OnActivated()
    {
        var player = Player.m_localPlayer;
        if (player == null)
        {
            return;
        }

        // Crossbow reloaded while me was off: stamp it now, so stand/logout keep it.
        var weapon = player.m_weaponLoaded;
        if (weapon != null && LoadedState.IsEligible(weapon))
        {
            LoadedState.Mark(weapon);
        }

        // Stamps gone stale while me was off (fired): drop them before a repair could make them look valid.
        foreach (var item in player.GetInventory().GetAllItems())
        {
            if (!ReferenceEquals(item, weapon) && LoadedState.HasStamp(item) && !LoadedState.IsLoaded(item))
            {
                LoadedState.Clear(item);
            }
        }
    }
}
