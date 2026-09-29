using System;
using BepInEx.Configuration;
using MC.Shared;

namespace MC.UX.ContainerSortMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, server check, status, live on/off. Patches in Patches/ apply only while Active.
// No Awake/Start/Update/OnDestroy here (they hide ModPlugin ones). Use BindConfig / OnActivated / OnDeactivated.
// Me add Sort + criterion buttons to the container panel (SortChestUi); Sort rearrange the open chest from top left
// to bottom right (ContainerSorter). Off = buttons gone at once, vanilla panel.
internal sealed partial class Plugin : ModPlugin
{
    internal static ConfigEntry<SortCriterion> SortBy;
    internal static ConfigEntry<bool> MergeStacks;

    protected override void BindConfig()
    {
        SortBy = Config.Bind("General", "SortBy", SortCriterion.Type, new ConfigDescription(
            "How the Sort button orders a container. Name: alphabetical in your game language. Type: weapons, ammo, "
            + "shields, armour, utility and trinkets, tools and light, food and potions, materials, fish, trophies, then "
            + "everything else; inside a group by kind (weapon kind, armour slot...), then alphabetical. Biome: "
            + "Meadows, Black Forest, Swamp, Ocean, Mountain, Plains, Mistlands, Ashlands, Deep North, then items of "
            + "unknown biome; by type and name inside each biome. "
            + "The button next to Sort changes this setting.",
            null, new ConfigurationManagerAttributes { Order = 90 }));
        MergeStacks = Config.Bind("General", "MergeStacks", true, new ConfigDescription(
            "Before sorting, combine partial stacks of the same item into full stacks. Stacks only combine when the items "
            + "are exactly the same (quality, world level, crafter, extra data from other mods, cheated mark), so nothing "
            + "is ever lost.",
            null, new ConfigurationManagerAttributes { Order = 80 }));
    }

    // Me just turned on (game start, or live with a chest open: buttons show at once).
    protected override void OnActivated()
    {
        SortBy.SettingChanged += OnSortByChanged;
        var gui = InventoryGui.instance;
        if (gui == null)
        {
            return; // no world yet: InventoryGui.Awake postfix make the buttons
        }
        try
        {
            SortChestUi.Create(gui);
            SortChestUi.OnShow(gui);
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(OnActivated), e);
        }
    }

    // Me going off (patches still on during this call). Buttons destroyed, biome index dropped.
    protected override void OnDeactivated()
    {
        SortBy.SettingChanged -= OnSortByChanged;
        Safe(SortChestUi.Destroy, "buttons");
        BiomeIndex.Clear();
        ItemPrefab.Clear();
    }

    // Setting edited in ConfigurationManager, MC Mods panel or file: labels and tooltips follow.
    private static void OnSortByChanged(object sender, EventArgs e)
    {
        try
        {
            SortChestUi.RefreshTexts();
        }
        catch (Exception ex)
        {
            PatchGuard.Report(nameof(OnSortByChanged), ex);
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
