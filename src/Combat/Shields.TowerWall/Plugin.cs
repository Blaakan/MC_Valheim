using System;
using BepInEx.Configuration;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.ShieldsTowerWallMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, server check, status, live on/off. Patches in Patches/ apply only while Active.
// No Awake/Start/Update/OnDestroy here (they hide ModPlugin ones). Use BindConfig / OnActivated / OnDeactivated.
// Me make tower shields a two-handed wall (docs/design/combat-shields-tower-wall.md): very slow in hand, much more
// block armor, no parry; braced = frontal unblockable attacks blocked, little stagger, no push while stamina last;
// attack button = shield bash (low damage, heavy stagger). Tower data written into item SharedData from a vanilla
// snapshot, healed on sight, reverted when off (TowerSync).
// Both side: server refuse players who cannot play by its rules (PlayerCheck) and send its rules to everyone
// (ServerRules). Rules in force = TowerRules.InForce (server's when client of a server with me, none while waiting for
// them, else own config); game code read the rules written into items: TowerSync.Applied.
internal sealed partial class Plugin : ModPlugin
{
    internal const string TowersSection = "Tower shields";
    internal const string BracingSection = "Bracing";
    internal const string BashSection = "Bash";

    private const string GeneralSection = "General";
    private const string ServerWins = " In multiplayer the setting of the server (or host) is used for everyone.";

    internal static ConfigEntry<bool> AllowPlayersWithoutMod;
    internal static ConfigEntry<string> Towers;
    internal static ConfigEntry<float> BlockArmorMultiplier;
    internal static ConfigEntry<int> BlockForcePercent;
    internal static ConfigEntry<int> CarrySlowPercent;
    internal static ConfigEntry<int> BraceSlowPercent;
    internal static ConfigEntry<int> BraceStaggerResistPercent;
    internal static ConfigEntry<int> BraceKnockbackResistPercent;
    internal static ConfigEntry<bool> BlockUnblockableAttacks;
    internal static ConfigEntry<string> BashAnimation;
    internal static ConfigEntry<string> BashCustomTrigger;
    internal static ConfigEntry<float> BashAnimationSpeed;
    internal static ConfigEntry<float> BashStamina;
    internal static ConfigEntry<float> BashCooldown;
    internal static ConfigEntry<float> BashStagger;
    internal static ConfigEntry<float> BashStaggerLock;
    internal static ConfigEntry<float> BashKnockback;
    internal static ConfigEntry<float> BashRange;
    internal static ConfigEntry<float> BashAngle;

    // Game quitting: plugins get destroyed and call OnDeactivated; me skip the world-wide revert then.
    internal static bool Quitting { get; private set; }

    protected override void BindConfig()
    {
        // General (Enabled 100, Status 99: framework).
        AllowPlayersWithoutMod = Config.Bind(GeneralSection, "AllowPlayersWithoutMod", false, new ConfigDescription(
            "Used only by the server (or the host). Off (default): a player whose game does not have this mod, has it "
            + "turned off, or has a version that cannot talk to the server's, is refused about a second after joining "
            + "(or after turning it off), and their game shows \"Incompatible version\", so everyone plays with the "
            + "same tower shields. On: such players may play; for them tower shields are normal shields.",
            null, new ConfigurationManagerAttributes { Order = 90 }));

        // Tower shields.
        Towers = Config.Bind(TowersSection, "Towers", TowerRules.DefaultTowers, new ConfigDescription(
            "Which shields are tower shields: comma-separated prefab names (the names the spawn command uses), each "
            + "with the blunt damage of its bash after a colon (0 to 200; 10 when left out). Only shields are "
            + "accepted; a shield that can parry loses its parry. Names the game does not know are reported in the "
            + "log." + ServerWins,
            null, new ConfigurationManagerAttributes { Order = 100 }));
        BlockArmorMultiplier = Config.Bind(TowersSection, "BlockArmorMultiplier", TowerRules.DefaultBlockArmorMultiplier,
            new ConfigDescription(
                "Tower shield block armor is multiplied by this (also the armor gained per quality level)." + ServerWins,
                new AcceptableValueRange<float>(TowerRules.MinBlockArmorMultiplier, TowerRules.MaxBlockArmorMultiplier),
                new ConfigurationManagerAttributes { Order = 90 }));
        BlockForcePercent = Config.Bind(TowersSection, "BlockForcePercent", TowerRules.DefaultBlockForcePercent,
            new ConfigDescription(
                "How hard a block pushes the attacker back, in percent of the tower shield's own block force. Because "
                + "tower shields block more of each hit, 100 already pushes somewhat harder than a normal tower shield "
                + "would; lower keeps attackers within bash range." + ServerWins,
                new AcceptableValueRange<int>(0, TowerRules.MaxBlockForcePercent),
                new ConfigurationManagerAttributes { Order = 80 }));
        CarrySlowPercent = Config.Bind(TowersSection, "CarrySlowPercent", TowerRules.DefaultCarrySlowPercent,
            new ConfigDescription(
                "How much slower you jog while a tower shield is in your hands, in percent (sprinting is slowed 1.5 "
                + "times as much, but never below jogging speed). Not while it is put away on your back. Armor "
                + "slowness adds to it. The normal game value is 10." + ServerWins,
                new AcceptableValueRange<int>(0, TowerRules.MaxCarrySlowPercent),
                new ConfigurationManagerAttributes { Order = 70 }));

        // Bracing.
        BraceSlowPercent = Config.Bind(BracingSection, "BraceSlowPercent", TowerRules.DefaultBraceSlowPercent,
            new ConfigDescription(
                "Extra slowdown while you block with a tower shield, in percent, on every movement speed and on "
                + "turning." + ServerWins,
                new AcceptableValueRange<int>(0, TowerRules.MaxBraceSlowPercent),
                new ConfigurationManagerAttributes { Order = 100 }));
        BraceStaggerResistPercent = Config.Bind(BracingSection, "BraceStaggerResistPercent",
            TowerRules.DefaultBraceStaggerResistPercent, new ConfigDescription(
                "While you block with a tower shield and have stamina for at least one more full block, you take this "
                + "much less stagger, in percent, so blocked hits do not break your guard." + ServerWins,
                new AcceptableValueRange<int>(0, TowerRules.MaxResistPercent),
                new ConfigurationManagerAttributes { Order = 90 }));
        BraceKnockbackResistPercent = Config.Bind(BracingSection, "BraceKnockbackResistPercent",
            TowerRules.DefaultBraceKnockbackResistPercent, new ConfigDescription(
                "Under the same conditions, hits and area attacks from the front push you back this much less, in "
                + "percent (100 = not at all)." + ServerWins,
                new AcceptableValueRange<int>(0, TowerRules.MaxResistPercent),
                new ConfigurationManagerAttributes { Order = 80 }));
        BlockUnblockableAttacks = Config.Bind(BracingSection, "BlockUnblockableAttacks",
            TowerRules.DefaultBlockUnblockableAttacks, new ConfigDescription(
                "While you block with a tower shield, enemy attacks from the front that normally cannot be blocked "
                + "(poison clouds, sprays, area attacks) are blocked too. Spells of your allies (such as the Staff of "
                + "Protection's shield) are never blocked." + ServerWins,
                null, new ConfigurationManagerAttributes { Order = 70 }));

        // Bash. Animation = text with a list of names (not the enum): a name in any case is read as the list spell it
        // (the enum setting did so too); a file that still say ShieldUp (0.1.0 test builds) is read as the first name,
        // ShieldPunch, with no warning; any other value too, with one Warning (AnimationNameList clamp).
        BashAnimation = Config.Bind(BashSection, "BashAnimation", TowerRules.DefaultBashAnimation.ToString(),
            new ConfigDescription(
                "Animation of the shield bash. ShieldPunch: a punch with the shield arm (the arm that holds the "
                + "shield). OtherPunch: the other arm. Kick: a kick. Custom: the animation named in BashCustomTrigger. "
                + "If the chosen animation cannot land its hit or does not end, the mod switches to another one and "
                + "says so in the log: Custom to ShieldPunch, then OtherPunch; Kick to OtherPunch. Names can be written "
                + "in any case; any other value is read as ShieldPunch (with a warning in the log)." + ServerWins
                + " Try the options in single player or as the host.",
                new AnimationNameList(TowerRules.AnimationNames),
                new ConfigurationManagerAttributes { Order = 100 }));
        BashCustomTrigger = Config.Bind(BashSection, "BashCustomTrigger", TowerRules.DefaultBashCustomTrigger,
            new ConfigDescription(
                "With BashAnimation = Custom: the attack animation to play (for example throw_bomb, spear_poke, "
                + "mace_secondary). Names that are not player attack animations, and animations of attacks that are "
                + "held, aimed or reloaded (such as staff_rapidfire, bow_fire, crossbow_fire), fall back to ShieldPunch "
                + "and are reported in the log." + ServerWins,
                null, new ConfigurationManagerAttributes { Order = 90 }));
        BashAnimationSpeed = Config.Bind(BashSection, "BashAnimationSpeed", TowerRules.DefaultBashAnimationSpeed,
            new ConfigDescription(
                "Speed of the bash animation, as a multiple of its normal speed (1 = as fast as a normal punch). "
                + "Lower is slower: the whole swing, its hit included, takes longer, and other players see the same "
                + "speed." + ServerWins,
                new AcceptableValueRange<float>(TowerRules.MinBashAnimationSpeed, TowerRules.MaxBashAnimationSpeed),
                new ConfigurationManagerAttributes { Order = 85 }));
        BashStamina = Config.Bind(BashSection, "BashStamina", TowerRules.DefaultBashStamina, new ConfigDescription(
            "Stamina cost of a bash, spent when the swing starts, whether it hits or not (the Blocking skill lowers it "
            + "by up to a third)." + ServerWins,
            new AcceptableValueRange<float>(TowerRules.MinBashStamina, TowerRules.MaxBashStamina),
            new ConfigurationManagerAttributes { Order = 80 }));
        BashCooldown = Config.Bind(BashSection, "BashCooldown", TowerRules.DefaultBashCooldown, new ConfigDescription(
            "Shortest time between the starts of two bashes, in seconds. Pressing attack before the time is up (once "
            + "the swing is over, or in its last half second) starts the next bash as soon as it is. 0 = the next "
            + "bash can start as soon as the previous one ends." + ServerWins,
            new AcceptableValueRange<float>(0f, TowerRules.MaxBashCooldown),
            new ConfigurationManagerAttributes { Order = 75 }));
        BashStagger = Config.Bind(BashSection, "BashStagger", TowerRules.DefaultBashStagger, new ConfigDescription(
            "How much a bash fills a creature's stagger bar, as a multiple of its blunt damage before the creature's "
            + "armor (only for the enemy nearest the middle of the swing; the others are staggered as by a punch). The "
            + "heaviest normal-game attacks use 6." + ServerWins,
            new AcceptableValueRange<float>(TowerRules.MinBashStagger, TowerRules.MaxBashStagger),
            new ConfigurationManagerAttributes { Order = 70 }));
        BashStaggerLock = Config.Bind(BashSection, "BashStaggerLock", TowerRules.DefaultBashStaggerLock,
            new ConfigDescription(
                "Seconds after a bash staggers a creature during which bashes add no heavy stagger to it (for every "
                + "player), so it recovers and fights between staggers. 0 = no limit." + ServerWins,
                new AcceptableValueRange<float>(0f, TowerRules.MaxBashStaggerLock),
                new ConfigurationManagerAttributes { Order = 60 }));
        BashKnockback = Config.Bind(BashSection, "BashKnockback", TowerRules.DefaultBashKnockback, new ConfigDescription(
            "How far a bash pushes enemies (grows with the Blocking skill)." + ServerWins,
            new AcceptableValueRange<float>(0f, TowerRules.MaxBashKnockback),
            new ConfigurationManagerAttributes { Order = 50 }));
        BashRange = Config.Bind(BashSection, "BashRange", TowerRules.DefaultBashRange, new ConfigDescription(
            "Reach of the bash in meters." + ServerWins,
            new AcceptableValueRange<float>(TowerRules.MinBashRange, TowerRules.MaxBashRange),
            new ConfigurationManagerAttributes { Order = 40 }));
        BashAngle = Config.Bind(BashSection, "BashAngle", TowerRules.DefaultBashAngle, new ConfigDescription(
            "Width of the bash arc in degrees. Every enemy in it takes the bash's damage and push, but only the one "
            + "nearest the middle of the swing takes its heavy stagger." + ServerWins,
            new AcceptableValueRange<float>(TowerRules.MinBashAngle, TowerRules.MaxBashAngle),
            new ConfigurationManagerAttributes { Order = 30 }));

        // Every rule setting (all sections but General): new own snapshot, new generation, server send it again.
        Config.SettingChanged += OnSettingChanged;
        AllowPlayersWithoutMod.SettingChanged += OnAllowChanged;
        Application.quitting += OnQuitting;
#if DEBUG
        _self = this;
#endif
        SelfTests.RegisterAlways(); // Debug build only: call vanish in Release
    }

#if DEBUG
    private static Plugin _self;

    // True between DebugSwitch(false) and DebugSwitch(true).
    internal static bool DebugOff { get; private set; }

    // This game's own settings file (multiplayer self test read it before and after: server rules never touch it).
    internal static string DebugConfigPath => _self != null ? _self.Config.ConfigFilePath : null;

    // Self test: what the framework do when the feature go off and on (ModPlugin.Refresh: OnDeactivated then patches
    // off; patches on then OnActivated), with no Enabled write (config file untouched, Status line too). Framework
    // state stay Active meanwhile. False = nothing done (not active, or already there).
    internal static bool DebugSwitch(bool on)
    {
        var self = _self;
        if (self == null)
        {
            return false;
        }
        if (!on)
        {
            if (DebugOff || !self.IsActive)
            {
                return false;
            }
            DebugOff = true;
            try
            {
                self.OnDeactivated();
            }
            catch (Exception e)
            {
                Log.Error($"OnDeactivated failed; patches removed anyway. {e}");
            }
            self.Harmony.UnpatchSelf();
            return true;
        }
        if (!DebugOff)
        {
            return false;
        }
        DebugOff = false;
        self.ApplyPatches(self.Harmony);
        self.OnActivated();
        return true;
    }
#endif

    // Design 7.3. Patches already on. Rules in force (vanilla while a client waits for the server's) onto prefabs and
    // live items, hands fixed, Braced ensured: TowerSync. Then network part, then foreign-mod warning.
    protected override void OnActivated()
    {
        TowerSync.Activate();
        ServerRules.Start();
        PlayerCheck.Start();
        TowerGuard.Reset();
        TowerGuard.WarnIfLoaded();
        SelfTests.Register(); // Debug build only: call vanish in Release
    }

    // Design 7.3. Patches still on during this call. Each step alone: one broken step never stop the others.
    // Nothing sent to the server: framework tell it this copy turned off (HelloState).
    protected override void OnDeactivated()
    {
        SelfTests.Unregister(); // Debug build only: call vanish in Release
        Step("Plugin.OnDeactivated towers", () => TowerSync.Deactivate(Quitting));
        Step("Plugin.OnDeactivated join check", PlayerCheck.Stop);
        Step("Plugin.OnDeactivated rules", ServerRules.Stop);
    }

    // One step of a life-cycle sequence in its own try/catch (reported once per site).
    internal static void Step(string site, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            PatchGuard.Report(site, e);
        }
    }

    private static void OnSettingChanged(object sender, SettingChangedEventArgs e)
    {
        try
        {
            var setting = e != null ? e.ChangedSetting : null;
            if (setting != null && setting.Definition.Section != GeneralSection)
            {
                ServerRules.OwnChanged();
            }
        }
        catch (Exception ex)
        {
            PatchGuard.Report("Plugin.OnSettingChanged", ex);
        }
    }

    // Switched back to refuse: players who cannot play by the rules and are already in get checked (after the grace).
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

    private static void OnQuitting() => Quitting = true;
}

// Me = the BashAnimation list. BepInEx's AcceptableValueList match case-sensitive (Equals) and clamp anything else to
// the first name, so "kick" became ShieldPunch and was written back. Me match any case (spaces around ignored) and give
// the name as the list spell it. Unknown = first name (ShieldPunch): silent for the old ShieldUp (0.1.0 test builds),
// one Warning for anything else (ConfigEntry clamp every value set, so a bad file value warn once per load or edit).
internal sealed class AnimationNameList : AcceptableValueList<string>
{
    internal const string OldShieldUp = "ShieldUp";

    internal AnimationNameList(params string[] names)
        : base(names)
    {
    }

    public override bool IsValid(object value) => TowerRules.AnimationName(value as string) != null;

    public override object Clamp(object value)
    {
        var text = value as string;
        var name = TowerRules.AnimationName(text);
        if (name != null)
        {
            return name;
        }
        var first = AcceptableValues[0];
        if (!string.IsNullOrEmpty(text) && !string.Equals(text.Trim(), OldShieldUp, StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning($"BashAnimation '{text}' is not one of {string.Join(", ", AcceptableValues)}; {first} is used.");
        }
        return first;
    }
}
