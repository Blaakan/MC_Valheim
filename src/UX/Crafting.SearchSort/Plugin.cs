using BepInEx.Configuration;
using MC.Shared;
using UnityEngine;

namespace MC.UX.CraftingSearchSortMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, server check, status, live on/off. Patches in Patches/ apply only while Active.
// No Awake/Start/Update/OnDestroy here (they hide ModPlugin ones). Use BindConfig / OnActivated / OnDeactivated.
// Me add search field + sort button row above crafting list (SearchUi), and filter + reorder list after vanilla
// build it (CraftList). Off = row gone, list layout back, vanilla list rebuilt at once.
internal sealed partial class Plugin : ModPlugin
{
    internal static ConfigEntry<KeyboardShortcut> FocusSearchKey;
    internal static ConfigEntry<bool> RememberSort;
    internal static ConfigEntry<bool> KeepSearchText;

    protected override void BindConfig()
    {
        FocusSearchKey = Config.Bind("General", "FocusSearchKey", new KeyboardShortcut(KeyCode.F), new ConfigDescription(
            "Key that puts the cursor in the crafting search field while your inventory or a crafting station is open "
            + "(the same key as the build menu search). Leave it empty to only click the field.",
            null, new ConfigurationManagerAttributes { Order = 90 }));
        RememberSort = Config.Bind("General", "RememberSort", true, new ConfigDescription(
            "Remember the sort you picked for each type of crafting station (saved with your character). "
            + "Off: every station opens in the game's normal order.",
            null, new ConfigurationManagerAttributes { Order = 80 }));
        KeepSearchText = Config.Bind("General", "KeepSearchText", false, new ConfigDescription(
            "Keep the search text when you close and reopen the same crafting station, like the build menu does. "
            + "Off: the search is cleared every time the inventory closes.",
            null, new ConfigurationManagerAttributes { Order = 70 }));

        // Me read key once per change (panel, ConfigurationManager, file edit), not every frame.
        CraftSearch.ReadFocusKey(FocusSearchKey.Value);
        FocusSearchKey.SettingChanged += (_, _) => CraftSearch.ReadFocusKey(FocusSearchKey.Value);
        // RememberSort flip (panel, ConfigurationManager, file edit): me load or drop sort of station we at now.
        RememberSort.SettingChanged += (_, _) => CraftSearch.OnRememberSortChanged();
    }

    // Only door to RememberSort value. Debug build: self test may force it in memory (SelfTests override), never in
    // config file.
    internal static bool ReadRememberSort()
    {
#if DEBUG
        if (SelfTests.RememberSortOverride.HasValue)
        {
            return SelfTests.RememberSortOverride.Value;
        }
#endif
        return RememberSort.Value;
    }

    // Only door to KeepSearchText value. Same Debug override rule.
    internal static bool ReadKeepSearchText()
    {
#if DEBUG
        if (SelfTests.KeepSearchTextOverride.HasValue)
        {
            return SelfTests.KeepSearchTextOverride.Value;
        }
#endif
        return KeepSearchText.Value;
    }

    // Me just turned on (maybe with inventory open). Make row now, apply remembered sort.
    // Debug build: in-world self tests join the list (call vanish in Release).
    protected override void OnActivated()
    {
        CraftSearch.Activate();
        SelfTests.Register();
    }

    // Me going off (patches still on during this call). Kill row and menu, give layout back, vanilla list again.
    // Debug build: self tests leave the list, forced settings forgotten (call vanish in Release).
    protected override void OnDeactivated()
    {
        CraftSearch.Deactivate();
        SelfTests.Unregister();
    }
}
