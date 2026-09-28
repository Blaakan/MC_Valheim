using BepInEx.Configuration;
using MC.Shared;

namespace MC.Exploration.StatsPerCreatureMod;

// Order of creature list in Player Statistics.
internal enum SortOrder
{
    MostKilled,
    Name,
}

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, server check, status, live on/off. Patches in Patches/ apply only while Active.
// No Awake/Start/Update/OnDestroy here (they hide ModPlugin ones). Use BindConfig / OnActivated / OnDeactivated.
// Me make no UI object: me only add text to vanilla Player Statistics entry. So nothing to destroy when off.
internal sealed partial class Plugin : ModPlugin
{
    internal static ConfigEntry<SortOrder> SortBy;
    internal static ConfigEntry<bool> ShowWeaponTypes;

    // Both read each time Compendium open: change show next open, no restart.
    protected override void BindConfig()
    {
        SortBy = Config.Bind("Display", "SortBy", SortOrder.MostKilled,
            "Order of the creature list in Player Statistics. MostKilled: the creatures you killed most come first. "
            + "Name: alphabetical.");
        ShowWeaponTypes = Config.Bind("Display", "ShowWeaponTypes", true,
            "After each kill count, show how the kills were made: melee, ranged, magic, unarmed, or other (several "
            + "weapon types, no weapon, or kills from before the game recorded weapon types, that is before the Deep "
            + "North update). Left out when only other is known.");
    }

    // Me turned on mid-game: start tame counting now for this character (spawn patch missed it).
    protected override void OnActivated()
    {
        var player = Player.m_localPlayer;
        if (player != null)
        {
            CounterStore.EnsureStarted(player);
        }
    }

    // Me turned off: drop parse cache. Stored data stay on character.
    protected override void OnDeactivated()
    {
        CounterStore.ClearCache();
    }
}
