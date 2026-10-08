using System;
using BepInEx;
using BepInEx.Configuration;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.WeaponsDualWieldMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, server check, status, live on/off. Patches in Patches/ apply only while Active.
// No Awake/Start/Update/OnDestroy here (they hide ModPlugin ones). Use BindConfig / OnActivated / OnDeactivated,
// plus LocalBlocker (other dual wield mod = me stand aside, framework show why and tell server).
// Me let player hold a one-handed sword, axe, club or knife in each hand: second weapon go to off hand (vanilla
// m_leftItem), pair swing with vanilla dual moves (Berserkir axes, Skoll and Hati), each hit struck by one weapon or
// both. Two knives also block together like Skoll and Hati (KnifeBlock); every other pair block with the off-hand
// weapon, as vanilla. Both side: server refuse players whose game no run me (PlayerCheck) and send its combat rules to everyone
// (ServerRules). Game code read rules only through ServerRules.Current.
// Smoothbrain DualWield (known GUID): BepInEx no load me next to it. Soft dependency on same GUID = it always
// processed first, so incompatibility seen whatever list BepInEx check (design 6.3, R22).
[BepInIncompatibility(SmoothbrainDualWieldGuid)]
[BepInDependency(SmoothbrainDualWieldGuid, BepInDependency.DependencyFlags.SoftDependency)]
internal sealed partial class Plugin : ModPlugin
{
    internal const string SmoothbrainDualWieldGuid = "org.bepinex.plugins.dualwield";

    // Server rules (DualRules): server value used for everyone in multiplayer.
    internal static ConfigEntry<int> OffHandDamage;
    internal static ConfigEntry<HitPatternMode> HitPattern;
    internal static ConfigEntry<int> BothHandsDamage;
    internal static ConfigEntry<int> SwingStamina;
    internal static ConfigEntry<SecondaryMovesMode> SecondaryMoves;
    internal static ConfigEntry<int> KnifePairBlock;
    internal static ConfigEntry<string> ExcludedWeapons;
    internal static ConfigEntry<string> PairMoves;
    internal static ConfigEntry<string> KnifePairMoves;

    // Server only.
    internal static ConfigEntry<bool> AllowPlayersWithoutMod;

    // Personal (never synced).
    internal static ConfigEntry<KeyCode> MainHandKey;
    internal static ConfigEntry<KeyCode> SwapHandsKey;
    internal static ConfigEntry<bool> LeftHandTrails;
    internal static ConfigEntry<bool> CrossSheathedPair;

    private const string CombatSection = "Combat";
    private const string MovesSection = "Moves";
    private const string ServerWins = " In multiplayer the setting of the server (or host) is used for everyone.";
    private const string Personal = " Each player's own setting.";

    protected override void BindConfig()
    {
        AllowPlayersWithoutMod = Config.Bind("General", "AllowPlayersWithoutMod", false, new ConfigDescription(
            "Used only by the server (or the host). Off (default): a player whose game does not run this mod (not "
            + "installed, turned off, or a version that cannot talk to this one) is refused about a second after "
            + "joining, or after turning it off while connected, and their game shows \"Incompatible version\", so "
            + "everyone fights with the same rules. The server log says why. On: such players may play, with a warning "
            + "in the server log; they play without this mod's rules (a player running another dual wield mod may "
            + "still dual wield with that mod), and they see other players' pairs normally.",
            null, new ConfigurationManagerAttributes { Order = 90 }));

        MainHandKey = Config.Bind("Controls", "MainHandKey", KeyCode.LeftAlt, new ConfigDescription(
            "Hold this key while you equip a one-handed weapon (hotbar, inventory or radial menu) to put it in your "
            + "main hand the normal game way instead of pairing it with the weapon you hold. The weapon in your off "
            + "hand is put away. None = off. Keyboard or mouse only (no gamepad button yet)." + Personal,
            null, new ConfigurationManagerAttributes { Order = 100 }));
        SwapHandsKey = Config.Bind("Controls", "SwapHandsKey", KeyCode.H, new ConfigDescription(
            "Press this key while dual wielding to swap the weapons between your hands. The swap is an equip like "
            + "any other: it takes as long as equipping a weapon, plays the game's equip animation and then the draw "
            + "animation of the hide key. You cannot attack meanwhile (hold the attack button to swing as soon as the "
            + "swap and its draw animation allow). It does not start while you sprint, and a dodge, a jump or a "
            + "sprint cancels it. Your next attack starts the combo from its first swing. The off-hand weapon is the one that blocks and "
            + "parries, and the main-hand weapon strikes first. None = off. Keyboard or mouse only (no gamepad button "
            + "yet)." + Personal,
            null, new ConfigurationManagerAttributes { Order = 90 }));

        OffHandDamage = Config.Bind(CombatSection, "OffHandDamage", DualRules.DefaultOffHandDamage, new ConfigDescription(
            "Damage of the hits struck by your off-hand weapon, in percent of that weapon's normal damage. 100 = full "
            + "damage, like the game's own dual weapons, which deal one weapon's damage on every hit." + ServerWins,
            new AcceptableValueRange<int>(DualRules.MinOffHandDamage, DualRules.MaxOffHandDamage),
            new ConfigurationManagerAttributes { Order = 100 }));
        HitPattern = Config.Bind(CombatSection, "HitPattern", HitPatternMode.Alternate, new ConfigDescription(
            "Which weapon strikes each hit of a dual attack. Alternate: the hits of the combo alternate between your "
            + "weapons like the game's own dual axes (one axe, the other, then one hit from each), and the special "
            + "attack and the last knife stab strike with both at once. BothHands: both weapons strike on every hit, "
            + "each dealing BothHandsDamage percent (the feel of other dual wield mods)." + ServerWins,
            null, new ConfigurationManagerAttributes { Order = 90 }));
        BothHandsDamage = Config.Bind(CombatSection, "BothHandsDamage", DualRules.DefaultBothHandsDamage, new ConfigDescription(
            "When both weapons strike the same hit, the damage of each, in percent of its normal damage. 50 = together "
            + "they deal about one weapon's hit, like the game's own dual weapons. Higher values make pairs much "
            + "stronger. The off-hand weapon's share is also scaled by OffHandDamage." + ServerWins,
            new AcceptableValueRange<int>(DualRules.MinBothHandsDamage, DualRules.MaxBothHandsDamage),
            new ConfigurationManagerAttributes { Order = 80 }));
        SwingStamina = Config.Bind(CombatSection, "SwingStamina", DualRules.DefaultSwingStamina, new ConfigDescription(
            "Stamina of a dual wield attack, in percent of the higher normal attack cost of your two weapons. The "
            + "special attack costs as many times more as the game's dual weapon it copies (2 times for the axe moves, "
            + "3 times for the knife moves). Your skill and equipment still reduce it as usual." + ServerWins,
            new AcceptableValueRange<int>(DualRules.MinSwingStamina, DualRules.MaxSwingStamina),
            new ConfigurationManagerAttributes { Order = 70 }));
        SecondaryMoves = Config.Bind(CombatSection, "SecondaryMoves", SecondaryMovesMode.PairMoves, new ConfigDescription(
            "Special attack of a pair. PairMoves: the special attack of the dual weapon whose moves the pair uses (the "
            + "Berserkir axes' cleave, or Skoll and Hati's leap), struck by both weapons. MainWeapon: the normal "
            + "special attack of your main-hand weapon, struck by that weapon only. A pair has no special attack when "
            + "its main-hand weapon has none (the wooden club)." + ServerWins,
            null, new ConfigurationManagerAttributes { Order = 60 }));
        KnifePairBlock = Config.Bind(CombatSection, "KnifePairBlock", DualRules.DefaultKnifePairBlock, new ConfigDescription(
            "Block power of a pair of two knives, in percent of the block power of the item whose moves two knives use "
            + "(KnifePairMoves, Skoll and Hati by default), scaled to the knives' slash, pierce and blunt damage: two "
            + "knives that hit half as hard as Skoll and Hati block half as well. The pair parries with that item's "
            + "parry bonus, and never blocks worse than its off-hand knife alone. 0 = the off-hand knife blocks on its "
            + "own, as in the game. A knife paired with another kind of weapon blocks with the off-hand weapon, as in "
            + "the game." + ServerWins,
            new AcceptableValueRange<int>(DualRules.MinKnifePairBlock, DualRules.MaxKnifePairBlock),
            new ConfigurationManagerAttributes { Order = 55 }));
        ExcludedWeapons = Config.Bind(CombatSection, "ExcludedWeapons", DualRules.DefaultExcludedWeapons, new ConfigDescription(
            "One-handed weapons that can never be dual wielded, as prefab names (as used by the spawn command), "
            + "comma-separated, for example modded weapons that look wrong in the dual moves. Spears, the butcher "
            + "knife, tankards and bombs are never paired anyway. When a weapon of the pair you hold becomes excluded, "
            + "your off-hand weapon is put away." + ServerWins,
            null, new ConfigurationManagerAttributes { Order = 50 }));

        PairMoves = Config.Bind(MovesSection, "PairMoves", DualRules.DefaultPairMoves, new ConfigDescription(
            "The game item whose moves (stance, attack combo, special attack, reach and damage multipliers) a pair "
            + "uses, unless both weapons are knives. Default: the Berserkir axes. If the item does not exist or its "
            + "animations are missing, the default is used and the log says why. With an item that is not a dual "
            + "weapon (a sword, an axe), the swings of its combo alternate between your weapons (main hand first) and "
            + "its special attack strikes with both." + ServerWins,
            null, new ConfigurationManagerAttributes { Order = 100 }));
        KnifePairMoves = Config.Bind(MovesSection, "KnifePairMoves", DualRules.DefaultKnifePairMoves, new ConfigDescription(
            "The game item whose moves a pair of two knives uses. Default: Skoll and Hati. Same checks as PairMoves."
            + ServerWins,
            null, new ConfigurationManagerAttributes { Order = 90 }));

        LeftHandTrails = Config.Bind("Visuals", "LeftHandTrails", true, new ConfigDescription(
            "Show the swing trail on off-hand weapons too, for every player you see dual wielding." + Personal,
            null, new ConfigurationManagerAttributes { Order = 100 }));
        CrossSheathedPair = Config.Bind("Visuals", "CrossSheathedPair", true, new ConfigDescription(
            "Place a sheathed pair (weapons put away with the hide key, at a crafting station, in water...) by weapon "
            + "kind, for every player you see with one: two swords, axes or maces crossed in an X on the back, two "
            + "knives one on each hip. A knife with a sword, axe or mace hangs where the game puts each (the knife at "
            + "the hip, the other weapon on the back) either way. Off: two weapons of the same kind sit on the same "
            + "spot and overlap, as the game places them. Only what you see changes." + Personal,
            null, new ConfigurationManagerAttributes { Order = 90 }));

        Controls.CacheKeys();

        // Every rule setting (sections Combat and Moves): new own snapshot, server send it again. Keys: check again.
        Config.SettingChanged += (_, e) =>
        {
            try
            {
                var setting = e.ChangedSetting;
                if (setting == null)
                {
                    return;
                }
                var section = setting.Definition.Section;
                if (section == CombatSection || section == MovesSection)
                {
                    ServerRules.OwnChanged();
                }
                else if (ReferenceEquals(setting, MainHandKey) || ReferenceEquals(setting, SwapHandsKey))
                {
                    Controls.CacheKeys();
                }
                else if (ReferenceEquals(setting, CrossSheathedPair))
                {
                    // Sheathed pairs drawn again at the next frame, placed by kind or as the game places them.
                    BackCross.RebuildAll();
                }
            }
            catch (Exception ex)
            {
                PatchGuard.Report("Plugin.SettingChanged", ex);
            }
        };
        AllowPlayersWithoutMod.SettingChanged += OnAllowChanged;
#if DEBUG
        // Debug build only: self test that must run while the feature is off (server without the mod) register here.
        SelfTests.RegisterAlways();
#endif
    }

#if DEBUG
    // Debug build only, self test: feature off and on in memory, the two steps the framework take when the player
    // untick and tick the mod (OnDeactivated then patches gone; patches back then OnActivated). The Enabled setting
    // and the config file stay untouched, so the framework still call the feature Active meanwhile: the test put it
    // back on in its finally.
    internal void TestSwitchOff()
    {
        OnDeactivated();
        Harmony.UnpatchSelf();
    }

    internal void TestSwitchOn()
    {
        ApplyPatches(Harmony);
        OnActivated();
    }
#endif

    // Other dual wield mod in this game = me cannot run: framework unpatch me, show text, tell server "off".
    protected override string LocalBlocker() => ForeignMods.BlockerText();

    protected override void OnActivated()
    {
        // Forget last seen rules / ObjectDB / player: first frame rebuild caches and check the hands (stance too).
        Hands.ResetSeen();
        Hands.ResetState();
        DualSwing.Clear();
        // Stale marker on main-hand weapon (reached main hand while me off): gone before first draw.
        Hands.ClearStaleMarkers(Player.m_localPlayer);
        Controls.CacheKeys();
        // Sheathed pairs already on players: built again next frame, placed by kind now (cosmetic, own view only).
        try
        {
            BackCross.RebuildAll();
        }
        catch (Exception e)
        {
            Log.Warning($"Could not redraw the sheathed pairs while turning on: {e.Message}");
        }
        ServerRules.Start();
        PlayerCheck.Start();
        SelfTests.Register();
    }

    protected override void OnDeactivated()
    {
        SelfTests.Unregister();
        PlayerCheck.Stop();
        ServerRules.Stop();
        // Queued swap out of vanilla's queue (else, patch gone, vanilla would equip the off-hand weapon in the main
        // hand), then off-hand weapon back in the inventory (D15). Only in a live world: never while the game quits.
        try
        {
            var player = Player.m_localPlayer;
            if (Hands.CanReleaseNow(player))
            {
                Hands.CancelSwap(player);
                Hands.ReleaseOffHand(player);
            }
        }
        catch (Exception e)
        {
            Log.Warning($"Could not put the off-hand weapon away while turning off: {e.Message}");
        }
        // Always: no state or cache survive the toggle (intents, restore candidate, eat return, queued swap, recorded
        // swing, templates, exclusions, trail caches; left trails still on turned off; placed sheathed pairs drawn
        // again by vanilla next frame, patch gone). A swing in progress finish with the main weapon only
        // (DoMeleeAttack patch gone). Nothing in ObjectDB or prefabs was ever changed; a knife's SharedData only inside
        // one BlockAttack call (its finalizer put it back; me check anyway).
        Hands.ResetSeen();
        Hands.ResetState();
        DualSwing.Clear();
        KnifeBlock.Restore();
        MoveTemplates.Clear();
        Eligibility.Clear();
        try
        {
            LeftTrails.Clear();
        }
        catch (Exception e)
        {
            Log.Warning($"Could not turn the off-hand trails off while turning off: {e.Message}");
        }
        try
        {
            BackCross.RebuildAll();
            BackCross.Clear();
        }
        catch (Exception e)
        {
            Log.Warning($"Could not redraw the sheathed pairs while turning off: {e.Message}");
        }
#if DEBUG
        DualSwing.Recording = false;
        DualSwing.ResetRecords();
        Hands.TestThrowInApply = false;
        ServerRules.TestRules = null;
        Controls.TestMainHandHeld = null;
        Controls.TestKeyHeld = null;
        Controls.TestKeyDown = null;
        Controls.TestMainKey = null;
        Controls.TestSwapKey = null;
        LeftTrails.TestLeftHandTrails = null;
        BackCross.TestEnabled = null;
        BackCross.ResetRecord();
#endif
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
