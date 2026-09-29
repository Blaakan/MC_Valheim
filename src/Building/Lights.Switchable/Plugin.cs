using System;
using BepInEx.Configuration;
using MC.Shared;

namespace MC.Building.LightsSwitchableMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, server check, status, live on/off. Patches in Patches/ apply only while Active.
// No Awake/Start/Update/OnDestroy here (they hide ModPlugin ones). Use BindConfig / OnActivated / OnDeactivated.
// Me make light pieces (torch, sconce, lantern, candle) free: E switch on or off, no fuel ever. Fires that can cook
// (campfire, hearth, bonfire, braziers) stay normal fires. On = fuel full, off = fuel 0 (candle: vanilla on/off
// state). Only vanilla ZDO keys: friends without mod see same. Both side: server refuse players without me
// (PlayerCheck) and send its light list to everyone (ServerRules).
internal sealed partial class Plugin : ModPlugin
{
    // Vanilla light pieces, checked in 1.0.16 game data (Fireplace pieces with no Burning area: no cooking fire).
    internal const string DefaultLights =
        "piece_groundtorch_wood, piece_groundtorch, piece_groundtorch_green, piece_groundtorch_blue, piece_walltorch, "
        + "piece_jackoturnip, piece_snowlantern, Candle_resin, CastleKit_groundtorch_unlit";

    internal static ConfigEntry<string> Lights;
    internal static ConfigEntry<bool> AllowPlayersWithoutMod;

    protected override void BindConfig()
    {
        Lights = Config.Bind("General", "Lights", DefaultLights, new ConfigDescription(
            "Pieces that become switchable lights: comma-separated prefab names (the names the spawn command uses). "
            + "The default list holds every vanilla torch, sconce, lantern and candle: standing wood, iron, green and blue "
            + "torches, the sconce, the Jack-o-turnip, the snow lantern, the resin candle and the unlit castle torch found "
            + "in the world. A fire that can be used for cooking (campfires, the iron fire pit, the hearth, the bonfire, "
            + "the braziers) always keeps burning fuel, even when listed. Pieces from other mods can be added if they are "
            + "fires the game treats like a torch. In multiplayer the list of the server (or host) is used for everyone.",
            null, new ConfigurationManagerAttributes { Order = 90 }));
        AllowPlayersWithoutMod = Config.Bind("General", "AllowPlayersWithoutMod", false, new ConfigDescription(
            "Used only by the server (or the host). Off (default): a player whose game does not have this mod installed "
            + "is refused about a second after joining, and their game shows \"Incompatible version\", because the "
            + "lights their game runs would burn fuel and go out. The check only looks at whether the mod is installed: "
            + "a player who turned it off on their own game is not refused. On: players without the mod may play; the "
            + "lights their game runs burn fuel the normal game way.",
            null, new ConfigurationManagerAttributes { Order = 80 }));

        Lights.SettingChanged += (_, _) => ServerRules.OwnChanged();
        AllowPlayersWithoutMod.SettingChanged += OnAllowChanged;
    }

    // Turned on mid-game: lights already in world got made before patch, so fix their flags now.
    protected override void OnActivated()
    {
        ServerRules.Start();
        PlayerCheck.Start();
        LiveLights.ApplyAll();
        SelfTests.Register();
    }

    // Turned off: put vanilla flags back on every loaded light. Fuel and state stay as they are (vanilla values).
    protected override void OnDeactivated()
    {
        SelfTests.Unregister();
        PlayerCheck.Stop();
        ServerRules.Stop();
        LiveLights.RestoreAll();
        LightSwitch.Clear();
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
