using System;
using BepInEx.Configuration;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SwimmingDiveMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, server check, status, live on/off. Patches in Patches/ apply only while Active.
// No Awake/Start/Update/OnDestroy here (they hide ModPlugin ones). Use BindConfig / OnActivated / OnDeactivated.
// Me let the player dive: while swimming, hold Crouch = go down, hold Jump = go up, under water only up or down, swim
// stamina drain even while still. Camera follow under the surface with fog and the surface seen from below (personal
// settings). Both side: server refuse players who cannot play by its rules (PlayerCheck) and send its rules to
// everyone (ServerRules). Game code read rules only through ServerRules.Current. Nothing saved anywhere, no item, no
// ZDO: turning off only has to stop the dive and put the camera, fog and water surfaces back.
internal sealed partial class Plugin : ModPlugin
{
    private const string General = "General";
    private const string DivingSection = "Diving";
    private const string VisualsSection = "Visuals";
    private const string ServerWins = " In multiplayer the setting of the server (or host) is used for everyone.";
    private const string Personal = " Each player's own choice.";

    // General (server only).
    internal static ConfigEntry<bool> AllowPlayersWithoutMod;

    // Diving (rules, server wins).
    internal static ConfigEntry<float> DiveSpeedMultiplier;
    internal static ConfigEntry<float> IdleRiseSpeed;
    internal static ConfigEntry<float> UnderwaterStaminaMultiplier;

    // Visuals (personal, never sent).
    internal static ConfigEntry<bool> UnderwaterCamera;
    internal static ConfigEntry<bool> UnderwaterFog;
    internal static ConfigEntry<bool> SurfaceFromBelow;
    internal static ConfigEntry<float> UnderwaterVisibility;
    internal static ConfigEntry<Color> UnderwaterFogColor;

    protected override void BindConfig()
    {
        var d = DiveRules.Default;

        AllowPlayersWithoutMod = Config.Bind(General, "AllowPlayersWithoutMod", false, new ConfigDescription(
            "Used only by the server (or the host). Off (default): a player whose game does not have this mod, has it "
            + "turned off or has another version of it is refused about a second after joining, or about a second after "
            + "turning it off, and their game shows \"Incompatible version\", so everyone plays by the same diving "
            + "rules. On: they may play; they swim like in the normal game and cannot dive.",
            null, new ConfigurationManagerAttributes { Order = 90 }));

        DiveSpeedMultiplier = Config.Bind(DivingSection, "DiveSpeedMultiplier", d.DiveSpeedMultiplier,
            new ConfigDescription(
                "How fast you go down (Crouch) and up (Jump) under water, as a multiple of your swimming speed "
                + "(swimming gear and effects count, as when swimming)." + ServerWins,
                new AcceptableValueRange<float>(DiveRules.DiveSpeedMin, DiveRules.DiveSpeedMax),
                new ConfigurationManagerAttributes { Order = 100 }));
        IdleRiseSpeed = Config.Bind(DivingSection, "IdleRiseSpeed", d.IdleRiseSpeed, new ConfigDescription(
            "What happens under water when you hold neither Crouch nor Jump: 0 (default) = you stay at your depth; above "
            + "0 = you float up this many metres per second." + ServerWins,
            new AcceptableValueRange<float>(0f, DiveRules.IdleRiseMax),
            new ConfigurationManagerAttributes { Order = 95 }));
        UnderwaterStaminaMultiplier = Config.Bind(DivingSection, "UnderwaterStaminaMultiplier",
            d.UnderwaterStaminaMultiplier, new ConfigDescription(
                "Stamina used while diving, as a multiple of the normal swimming drain (1 = the same; your Swim skill, "
                + "gear and effects count as when swimming). Swimming at the surface is not changed." + ServerWins,
                new AcceptableValueRange<float>(0f, DiveRules.StaminaMultiplierMax),
                new ConfigurationManagerAttributes { Order = 90 }));

        UnderwaterCamera = Config.Bind(VisualsSection, "UnderwaterCamera", true, new ConfigDescription(
            "The camera follows you under the surface when you dive. Off: it stays above the water as in the normal "
            + "game." + Personal,
            null, new ConfigurationManagerAttributes { Order = 100 }));
        UnderwaterFog = Config.Bind(VisualsSection, "UnderwaterFog", true, new ConfigDescription(
            "While the camera is under water, the view is tinted and you see only UnderwaterVisibility metres far."
            + Personal,
            null, new ConfigurationManagerAttributes { Order = 95 }));
        SurfaceFromBelow = Config.Bind(VisualsSection, "SurfaceFromBelow", true, new ConfigDescription(
            "While the camera is under water, the water surface is shown from below (in the normal game it cannot be "
            + "seen from below and the sky shows through). Turn it off if the surface looks wrong on your graphics card."
            + Personal,
            null, new ConfigurationManagerAttributes { Order = 90 }));
        UnderwaterVisibility = Config.Bind(VisualsSection, "UnderwaterVisibility", Visuals.DefaultVisibility,
            new ConfigDescription(
                "How far you see under water, in metres (when the normal fog is thicker, it stays)." + Personal,
                new AcceptableValueRange<float>(Visuals.VisibilityMin, Visuals.VisibilityMax),
                new ConfigurationManagerAttributes { Order = 85 }));
        UnderwaterFogColor = Config.Bind(VisualsSection, "UnderwaterFogColor", Visuals.DefaultFogColor,
            new ConfigDescription(
                "Colour of the water in daylight; it gets darker at night and deeper down." + Personal,
                null, new ConfigurationManagerAttributes { Order = 80 }));

        // Every rule setting (section Diving): new own snapshot, server send it again. General and Visuals: not rules.
        Config.SettingChanged += OnSettingChanged;
        AllowPlayersWithoutMod.SettingChanged += OnAllowChanged;
    }

    protected override string LocalBlocker() => ForeignMods.BlockerText();

    protected override void OnActivated()
    {
        DiveState.Reset();
        DiveCamera.Clear();
        ServerRules.Start();
        PlayerCheck.Start();
        SelfTests.Register();
    }

    // Patches still on during this call (also at game quit). Each step alone: one failure never skip the rest. Dive
    // stopped (gravity back; vanilla buoyancy lift the player from the next tick), camera clamp back at once
    // (finalizer never leave it changed), fog and surfaces put back.
    protected override void OnDeactivated()
    {
        // Conditional method (gone in Release): no delegate to it, so own try instead of Step().
        try
        {
            SelfTests.Unregister();
        }
        catch (Exception e)
        {
            PatchGuard.Report("Plugin.OnDeactivated SelfTests.Unregister", e);
        }
        Step("DiveController.Shutdown", DiveController.Shutdown);
        Step("DiveCamera.Clear", DiveCamera.Clear);
        Step("UnderwaterView.RestoreAll", UnderwaterView.RestoreAll);
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

    private static void OnSettingChanged(object sender, SettingChangedEventArgs e)
    {
        try
        {
            var setting = e?.ChangedSetting;
            if (setting == null || setting.Definition.Section != DivingSection)
            {
                return;
            }
            ServerRules.OwnChanged();
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
