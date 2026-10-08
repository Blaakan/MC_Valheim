using System;
using BepInEx.Configuration;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.TrinketsOnDemandMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, server check, status, live on/off. Patches in Patches/ apply only while Active.
// No Awake/Start/Update/OnDestroy here (they hide ModPlugin ones). Use BindConfig / OnActivated / OnDeactivated.
// Me make trinkets fire on demand: adrenaline build while fighting (income), never drain while a trinket give the bar
// room, a full bar wait (no auto pop) until the player press the trigger key (or gamepad combination), and bow and
// crossbow hits pay more for their slow cycle. Trinket stats (effect, cost) untouched.
// Both side: server refuse players who cannot play by its rules (PlayerCheck) and send its rules to everyone
// (ServerRules). Game code read rules only through ServerRules.Current, and play vanilla while it is pending.
internal sealed partial class Plugin : ModPlugin
{
    // General (server only).
    internal static ConfigEntry<bool> AllowPlayersWithoutMod;

    // Controls (personal).
    internal static ConfigEntry<KeyCode> TriggerKey;
    internal static ConfigEntry<GamepadModifier> GamepadModifier;
    internal static ConfigEntry<GamepadButton> GamepadButton;

    // Feedback (personal).
    internal static ConfigEntry<bool> ShowFullMessage;
    internal static ConfigEntry<float> FullFlashInterval;

    // Rules.
    internal static ConfigEntry<float> IncomePerSecond;
    internal static ConfigEntry<float> CombatLingerSeconds;
    internal static ConfigEntry<bool> RefuseWhileActive;
    internal static ConfigEntry<float> RangedReferenceSeconds;
    internal static ConfigEntry<float> RangedMaxMultiplier;

    private const string ControlsSection = "Controls";
    private const string FeedbackSection = "Feedback";
    private const string AdrenalineSection = "Adrenaline";
    private const string TriggerSection = "Trigger";
    private const string RangedSection = "Ranged";
    private const string ServerWins = " In multiplayer the setting of the server (or host) is used for everyone.";
    private const string Personal = " Each player's own choice.";
    internal const float FlashIntervalMax = 60f;

    protected override void BindConfig()
    {
        var d = TrinketRules.Default;

        AllowPlayersWithoutMod = Config.Bind("General", "AllowPlayersWithoutMod", false, new ConfigDescription(
            "Used only by the server (or the host). Off (default): a player whose game does not have this mod, has it "
            + "turned off or has another version of it is refused about a second after joining, or about a second after "
            + "turning it off, and their game shows \"Incompatible version\", so everyone plays by the same adrenaline "
            + "and trinket rules. On: they may play; their adrenaline and trinkets work like the normal game (the bar "
            + "drains, a full bar fires the trinket at once, no income while fighting, no ranged bonus).",
            null, new ConfigurationManagerAttributes { Order = 90 }));

        // Controls (personal).
        TriggerKey = Config.Bind(ControlsSection, "TriggerKey", KeyCode.Y, new ConfigDescription(
            "Keyboard key (or mouse button) that fires your equipped trinkets when the adrenaline bar is full. None = "
            + "no keyboard trigger. Gamepad buttons go in GamepadModifier and GamepadButton." + Personal,
            null, new ConfigurationManagerAttributes { Order = 100 }));
        GamepadModifier = Config.Bind(ControlsSection, "GamepadModifier", TrinketsOnDemandMod.GamepadModifier.LeftTrigger,
            new ConfigDescription(
                "Gamepad button to hold while pressing GamepadButton. None = GamepadButton alone. LeftTrigger is also "
                + "block in the default gamepad layout." + Personal,
                null, new ConfigurationManagerAttributes { Order = 95 }));
        GamepadButton = Config.Bind(ControlsSection, "GamepadButton", TrinketsOnDemandMod.GamepadButton.RightStick,
            new ConfigDescription(
                "Gamepad button that fires your trinkets (while GamepadModifier is held). None = no gamepad trigger. "
                + "The default, left trigger + right stick press, is free in the default gamepad layout; in the "
                + "Alternative 1 and 2 layouts the right stick press also crouches, and in Alternative 1 the left "
                + "trigger puts your weapons away or opens the radial menu, so pick another pair there." + Personal,
                null, new ConfigurationManagerAttributes { Order = 90 }));

        // Feedback (personal).
        ShowFullMessage = Config.Bind(FeedbackSection, "ShowFullMessage", true, new ConfigDescription(
            "When the adrenaline bar becomes full, show \"Adrenaline full: press ... to trigger your trinket\" at the "
            + "top left (at most every 20 seconds). The bar flashes either way." + Personal,
            null, new ConfigurationManagerAttributes { Order = 100 }));
        FullFlashInterval = Config.Bind(FeedbackSection, "FullFlashInterval", 4f, new ConfigDescription(
            "While the bar stays full, flash it again every this many seconds. 0 = flash once when it becomes full."
            + Personal,
            new AcceptableValueRange<float>(0f, FlashIntervalMax),
            new ConfigurationManagerAttributes { Order = 95 }));

        // Rules.
        IncomePerSecond = Config.Bind(AdrenalineSection, "IncomePerSecond", d.IncomePerSecond, new ConfigDescription(
            "Adrenaline gained every second while you are in a fight and wear a trinket, on top of the normal game's "
            + "gains (hits, blocks, parries, dodges...). World modifiers and effects that change adrenaline gains "
            + "apply to it too. 0 = no income." + ServerWins,
            new AcceptableValueRange<float>(0f, TrinketRules.IncomeMax),
            new ConfigurationManagerAttributes { Order = 100 }));
        CombatLingerSeconds = Config.Bind(AdrenalineSection, "CombatLingerSeconds", d.CombatLingerSeconds,
            new ConfigDescription(
                "A fight lasts this many seconds after the last hit you gave to or took from a hostile creature (or a "
                + "perfect dodge). While an alerted creature keeps hunting you it lasts longer, up to 20 seconds after "
                + "the last hit. Training dummies, tamed creatures and other players do not count." + ServerWins,
                new AcceptableValueRange<float>(TrinketRules.LingerMin, TrinketRules.LingerMax),
                new ConfigurationManagerAttributes { Order = 95 }));
        RefuseWhileActive = Config.Bind(TriggerSection, "RefuseWhileActive", d.RefuseWhileActive,
            new ConfigDescription(
                "On: the trigger is refused (\"Trinket effect still active\") while the effect of a trinket you wear "
                + "still runs, so the bar is not spent. Off (default): as in the normal game, triggering again "
                + "restarts the running effect and spends the bar." + ServerWins,
                null, new ConfigurationManagerAttributes { Order = 100 }));
        RangedReferenceSeconds = Config.Bind(RangedSection, "RangedReferenceSeconds", d.RangedReferenceSeconds,
            new ConfigDescription(
                "Bow and crossbow hits earn more adrenaline when a shot takes longer than this many seconds: a shot "
                + "that takes twice as long earns twice the normal amount per hit. The time of a shot is the draw you "
                + "actually held (bows, shorter with Bows skill) or the reload (crossbows, shorter with Crossbows "
                + "skill), plus half a second to shoot. The default, 1.5, makes shooting earn about as much adrenaline "
                + "per second as melee (an arrow or bolt hit pays 2 in the normal game, a one-handed weapon hit 1 "
                + "about every second, a two-handed weapon hit 2 per slower swing)." + ServerWins,
                new AcceptableValueRange<float>(TrinketRules.ReferenceMin, TrinketRules.ReferenceMax),
                new ConfigurationManagerAttributes { Order = 100 }));
        RangedMaxMultiplier = Config.Bind(RangedSection, "RangedMaxMultiplier", d.RangedMaxMultiplier,
            new ConfigDescription(
                "Most a bow or crossbow hit can earn, as a multiple of the normal amount. 1 = no ranged bonus (normal "
                + "game)." + ServerWins,
                new AcceptableValueRange<float>(TrinketRules.MultiplierMin, TrinketRules.MultiplierMax),
                new ConfigurationManagerAttributes { Order = 95 }));

        Controls.CacheKeys();
        // Every rule setting (all sections but General and the personal ones): new own snapshot, server send it again.
        Config.SettingChanged += OnSettingChanged;
        AllowPlayersWithoutMod.SettingChanged += OnAllowChanged;
        // Debug build only (call vanish in Release): the one self test that must run while me inactive.
        SelfTests.RegisterInactive();
    }

#if DEBUG
    // Self test (trinkets.toggle, trinkets.vanilla-parity): feature off and on again the way ModPlugin.Refresh do it
    // (OnDeactivated, then patches gone; patches back, then OnActivated), with no setting written. Framework state stay
    // Active meanwhile: only for a few seconds inside one test, put back in its finally.
    internal bool TestOff { get; private set; }

    internal void TestTurnOff()
    {
        if (TestOff || !IsActive)
        {
            return;
        }
        TestOff = true;
        try
        {
            OnDeactivated();
        }
        finally
        {
            Harmony.UnpatchSelf();
        }
    }

    internal void TestTurnOn()
    {
        if (!TestOff)
        {
            return;
        }
        TestOff = false;
        ApplyPatches(Harmony);
        OnActivated();
    }
#endif

    protected override void OnActivated()
    {
        Compat.Reset();
        Controls.CacheKeys();
        FullBar.Reset();
        Feedback.Reset();
        Income.Reset();
        CombatState.Reset();
        RangedBonus.Reset();
        ServerRules.Start();
        PlayerCheck.Start();
        SelfTests.Register();
    }

    // Patches still on during this call (also at game quit). Each step alone: one failure never skip the rest.
    // Nothing of the game to put back but effects hidden mid-call (never, single thread; belt and braces).
    // Full bar stay full until next vanilla change: gain or 0 call pop it, drain (timer run out, 1 s or more) lower it,
    // no pop.
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
        Step("FullBar.Reset", FullBar.Reset);
        Step("Feedback.Reset", Feedback.Reset);
        Step("Income.Reset", Income.Reset);
        Step("CombatState.Reset", CombatState.Reset);
        Step("RangedBonus.Reset", RangedBonus.Reset);
        Step("PlayerCheck.Stop", PlayerCheck.Stop);
        Step("ServerRules.Stop", ServerRules.Stop);
    }

    // Another trinket mod changes when trinkets fire: me stand aside (ForeignMods).
    protected override string LocalBlocker() => ForeignMods.BlockerText();

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
            if (setting == null)
            {
                return;
            }
            var section = setting.Definition.Section;
            if (section == ControlsSection)
            {
                Controls.CacheKeys();
                return;
            }
            if (section == "General" || section == FeedbackSection)
            {
                return; // personal or framework: never in the rules
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
