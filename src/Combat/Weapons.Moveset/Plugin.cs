using System;
using BepInEx.Configuration;
using MC.Shared;

namespace MC.Combat.WeaponsMovesetMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, server check, status, live on/off. Patches in Patches/ apply only while Active.
// No Awake/Start/Update/OnDestroy here (they hide ModPlugin ones). Use BindConfig / OnActivated / OnDeactivated.
// Me give melee weapons two moves on the primary attack: jump attack (jump, attack before landing) and roll attack
// (roll, attack: cut into end of roll or right after it, Animator cross-faded straight from roll into swing,
// RollFlow; never roll, stand-up, then roll attack). Move = other
// vanilla animation + own multipliers on the per-swing Attack clone; vanilla code do the rest (stamina, facing,
// trigger RPC, hits, combo). Nothing saved anywhere: toggle has nothing to put back. Both side: server refuse players who cannot fight by its moves (PlayerCheck) and send its rules to everyone
// (ServerRules). Game code read rules only through ServerRules.Current (server's when client, else own config).
internal sealed partial class Plugin : ModPlugin
{
    private const string ServerWins = " In multiplayer the setting of the server (or host) is used for everyone.";
    private const string General = "General";
    private const string MovesSection = "Moves";

    internal static ConfigEntry<bool> AllowPlayersWithoutMod;
    internal static ConfigEntry<bool> JumpAttack;
    internal static ConfigEntry<bool> RollAttack;
    internal static ConfigEntry<float> Cooldown;
    internal static MoveConfig Jump;
    internal static MoveConfig Roll;
    internal static ConfigEntry<float> AimAngle;
    internal static ConfigEntry<float> Window;
    internal static ConfigEntry<float> FlowStart;
    internal static ConfigEntry<float> FlowBlend;
    internal static readonly ConfigEntry<string>[] JumpAnimation = new ConfigEntry<string>[Families.Count];
    internal static readonly ConfigEntry<string>[] RollAnimation = new ConfigEntry<string>[Families.Count];

    // Numbers of one move (section "Jump attack" or "Roll attack").
    internal sealed class MoveConfig
    {
        internal ConfigEntry<float> Damage;
        internal ConfigEntry<float> Stagger;
        internal ConfigEntry<float> Push;
        internal ConfigEntry<float> Stamina;
    }

    protected override void BindConfig()
    {
        AllowPlayersWithoutMod = Config.Bind(General, "AllowPlayersWithoutMod", false, new ConfigDescription(
            "Used only by the server (or the host). Off (default): a player whose game does not have this mod, has a "
            + "version that cannot talk to this one, or has it turned off, is refused about a second after joining or "
            + "after turning it off (their game shows \"Incompatible version\"), so every player fights with the same "
            + "moves and numbers. On: such players may play; their own jumps and rolls stay normal.",
            null, new ConfigurationManagerAttributes { Order = 90 }));

        JumpAttack = Config.Bind(MovesSection, "JumpAttack", true, new ConfigDescription(
            "Jump, then attack before you land: the first attack of the jump becomes a jump attack with its own "
            + "animation and bonuses. Off: attacks in the air after a jump stay normal swings, even right after a "
            + "roll." + ServerWins,
            null, new ConfigurationManagerAttributes { Order = 100 }));
        RollAttack = Config.Bind(MovesSection, "RollAttack", true, new ConfigDescription(
            "Roll, then attack: the first attack of the roll becomes a roll attack with its own animation and "
            + "bonuses, and it flows straight out of the end of the roll." + ServerWins,
            null, new ConfigurationManagerAttributes { Order = 99 }));
        Cooldown = Number(MovesSection, "Cooldown", MoveRules.DefaultCooldown, MoveRules.MinCooldown,
            MoveRules.MaxCooldown, 98,
            "Minimum time in seconds between the start of one jump or roll attack and the next one (either kind); an "
            + "attack inside that time is a normal swing. A normal roll or jump already takes longer, so this mostly "
            + "limits instant dash mods and fast jump-attack spam. 0 = no limit.");

        Jump = new MoveConfig
        {
            Damage = Number(Moves.JumpSection, "DamageMultiplier", MoveRules.DefaultJumpDamage, MoveRules.MinDamage,
                MoveRules.MaxDamage, 100,
                "Damage of a jump attack compared with a normal swing of the same weapon (1 = the same). It also "
                + "applies to trees and rocks."),
            Stagger = Number(Moves.JumpSection, "StaggerMultiplier", MoveRules.DefaultJumpStagger,
                MoveRules.MinStagger, MoveRules.MaxStagger, 99,
                "How much faster a jump attack fills the target's stagger bar (1 = like a normal swing)."),
            Push = Number(Moves.JumpSection, "PushMultiplier", MoveRules.DefaultJumpPush, MoveRules.MinPush,
                MoveRules.MaxPush, 98,
                "Knockback of a jump attack (1 = like a normal swing)."),
            Stamina = Number(Moves.JumpSection, "StaminaMultiplier", MoveRules.DefaultJumpStamina,
                MoveRules.MinStamina, MoveRules.MaxStamina, 97,
                "Stamina of a jump attack's swing, on top of the jump itself (1 = the swing's normal cost). If you can "
                + "pay a normal swing but not the jump attack, you get a normal swing."),
        };
        AimAngle = Number(Moves.JumpSection, "AimAngle", MoveRules.DefaultAimAngle, MoveRules.MinAimAngle,
            MoveRules.MaxAimAngle, 96,
            "Up to how many degrees a jump attack's swing tilts toward where you look (down or up) while you are in "
            + "the air, so you can strike an enemy below you (or a flyer above). The game tilts a swing less than your "
            + "view (looking 30 degrees down tilts it about 27 degrees) and never more than 45 degrees, hence the "
            + "limit. Once you land the swing is level again. 0 = always level, like a normal swing. No effect on "
            + "sledges (their slam hits an area).");

        Roll = new MoveConfig
        {
            Damage = Number(Moves.RollSection, "DamageMultiplier", MoveRules.DefaultRollDamage, MoveRules.MinDamage,
                MoveRules.MaxDamage, 100,
                "Damage of a roll attack compared with a normal swing of the same weapon (1 = the same). It also "
                + "applies to trees and rocks."),
            Stagger = Number(Moves.RollSection, "StaggerMultiplier", MoveRules.DefaultRollStagger,
                MoveRules.MinStagger, MoveRules.MaxStagger, 99,
                "How much faster a roll attack fills the target's stagger bar (1 = like a normal swing)."),
            Push = Number(Moves.RollSection, "PushMultiplier", MoveRules.DefaultRollPush, MoveRules.MinPush,
                MoveRules.MaxPush, 98,
                "Knockback of a roll attack (1 = like a normal swing)."),
            Stamina = Number(Moves.RollSection, "StaminaMultiplier", MoveRules.DefaultRollStamina,
                MoveRules.MinStamina, MoveRules.MaxStamina, 97,
                "Stamina of a roll attack's swing, on top of the roll itself (1 = the swing's normal cost). If you can "
                + "pay a normal swing but not the roll attack, you get a normal swing."),
        };
        Window = Number(Moves.RollSection, "Window", MoveRules.DefaultWindow, MoveRules.MinWindow,
            MoveRules.MaxWindow, 96,
            "How long (seconds) after a roll ends a new press of the attack button still makes a roll attack. After a "
            + "normal roll only while you are still getting up from it (about the first 0.2 seconds), so a roll attack "
            + "always flows out of the roll: a later press is a normal swing, whatever this value. The whole time counts "
            + "after a dash from a dash mod (it has no roll animation). A press during the roll (the game remembers a "
            + "press for half a second) or holding the button makes the roll attack cut into the end of the roll "
            + "instead (see FlowStart).");
        FlowStart = Number(Moves.RollSection, "FlowStart", MoveRules.DefaultFlowStart, MoveRules.MinFlowStart,
            MoveRules.MaxFlowStart, 95,
            "How long (seconds) after the start of a roll an attack can cut into it: from then on a pressed, "
            + "remembered or held attack ends the roll and the roll attack flows straight out of it, with no stop in "
            + "between. A normal roll lasts about 0.9 seconds, so the default plays almost all of it and only removes "
            + "the stand-up; a lower value (for example 0.7) gives a snappier roll attack that cuts the end of the roll "
            + "short. The roll's invulnerability ends about 0.4 seconds in, and the attack never starts until 0.2 "
            + "seconds after that, whatever this value, so no other player's game still sees you invulnerable. Longer "
            + "than the roll = the attack starts as the roll ends, still without the stop.");
        FlowBlend = Number(Moves.RollSection, "FlowBlend", MoveRules.DefaultFlowBlend, MoveRules.MinFlowBlend,
            MoveRules.MaxFlowBlend, 94,
            "How long (seconds) the animation blends from the roll into the roll attack. Only the look changes: the "
            + "swing hits at the same time. 0 = an instant switch.");

        foreach (var family in Families.All)
        {
            var f = Families.Index(family);
            JumpAnimation[f] = Animation(MoveKind.Jump, family, 100 - f);
            RollAnimation[f] = Animation(MoveKind.Roll, family, 100 - f);
        }

        // Every rule setting (all sections but General): new own snapshot, server send it again. Trigger cache follow
        // the new snapshot by reference.
        Config.SettingChanged += OnSettingChanged;
        AllowPlayersWithoutMod.SettingChanged += OnAllowChanged;
        // Debug build only (call vanish in Release): log tap from the start, and the self test that run while me
        // inactive (server without the mod: tests of OnActivated are gone then).
        SelfTests.RegisterAlways();
    }

#if DEBUG
    // Self test: turn me off in memory, like the Enabled switch (never the config file). Null = normal. Test set it,
    // call FeatureRegistry.RefreshAll(), and clear it + refresh again in its finally.
    internal static string TestBlocker;

    protected override string LocalBlocker() => TestBlocker;
#endif

    protected override void OnActivated()
    {
        Compat.Reset();
        MoveTracker.Reset();
        MoveTriggers.Invalidate();
        RollFlow.Reset();
        ServerRules.Start();
        PlayerCheck.Start();
        SelfTests.Register();
    }

    // Patches still on during this call (also at game quit). Each step alone: one failure never skip the rest.
    // Nothing to put back in the game: no ObjectDB, item or ZDO edit. Move already playing finish with its own clone.
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
        Step("PlayerCheck.Stop", PlayerCheck.Stop);
        Step("ServerRules.Stop", ServerRules.Stop);
        // Move still starting: its trigger reset, only if its Player alive and still local. Move already playing
        // finish with its own clone (private to that swing); next attack vanilla. Roll gate (m_inDodge me cleared
        // for one StartAttack call) closed too, if ever still open.
        Step("MoveTracker.Reset", MoveTracker.Reset);
        Step("MoveTriggers.Invalidate", MoveTriggers.Invalidate);
        Step("RollFlow.Reset", RollFlow.Reset);
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

    private ConfigEntry<float> Number(string section, string key, float value, float min, float max, int order,
        string text)
    {
        return Config.Bind(section, key, value, new ConfigDescription(text + ServerWins,
            new AcceptableValueRange<float>(min, max), new ConfigurationManagerAttributes { Order = order }));
    }

    // One animation drop-down. List start with own default: BepInEx put a value not in list back to the default.
    private ConfigEntry<string> Animation(MoveKind kind, WeaponFamily family, int order)
    {
        var def = MoveTriggers.Default(kind, family);
        var text = $"Animation of the {Moves.Name(kind)} for {Families.Label(family)}: one of the game's melee attack "
                   + "animations, or Off (a normal swing, no bonus). The move keeps the weapon's own reach, hit arc and "
                   + "damage; an animation of another weapon type can look odd or clip through a shield. A value that "
                   + "is not in the list is replaced by the default when the game starts.";
        var why = OffReason(kind, family);
        if (why != null)
        {
            text += " Off by default: " + why;
        }
        return Config.Bind(Moves.AnimationSection(kind), Families.Key(family), def, new ConfigDescription(
            text + ServerWins, MoveTriggers.ValuesFor(def), new ConfigurationManagerAttributes { Order = order }));
    }

    // Why a default is Off (design Decisions 4 and 12). Null = not Off.
    private static string OffReason(MoveKind kind, WeaponFamily family)
    {
        if (MoveTriggers.Default(kind, family) != MoveTriggers.Off)
        {
            return null;
        }
        switch (family)
        {
            case WeaponFamily.Spears:
            case WeaponFamily.Sledges:
                return "this weapon type has a single attack animation, so the move would look exactly like a normal "
                       + "attack.";
            case WeaponFamily.Battleaxes:
                return "the battleaxe's other combo swings hit much sooner than its slow first swing, so a roll attack "
                       + "would open far faster than the weapon normally does.";
            default:
                return "no fitting animation.";
        }
    }

    private static void OnSettingChanged(object sender, SettingChangedEventArgs e)
    {
        try
        {
            var setting = e.ChangedSetting;
            if (setting != null && setting.Definition.Section != General)
            {
                ServerRules.OwnChanged();
            }
        }
        catch (Exception ex)
        {
            PatchGuard.Report("Plugin.OnSettingChanged", ex);
        }
    }

    // Switched back to refuse: players already in who cannot fight by our moves get checked (after the grace) too.
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
