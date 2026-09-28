using BepInEx.Configuration;
using MC.Shared;

namespace MC.Combat.CrossbowStaysLoadedMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin.
internal sealed partial class Plugin : ModPlugin
{
    internal static ConfigEntry<bool> ShowLoadedInTooltip;

    protected override void BindConfig()
    {
        ShowLoadedInTooltip = Config.Bind("UI", "ShowLoadedInTooltip", true,
            "Show \"Loaded\" in the tooltip of a crossbow that still holds a bolt.");
    }

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
