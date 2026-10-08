using System;
using BepInEx.Configuration;
using MC.Shared;

namespace MC.Exploration.SailingSkillMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, server check, status, live on/off. Patches in Patches/ apply only while Active.
// No Awake/Start/Update/OnDestroy here (they hide ModPlugin ones). Use BindConfig / OnActivated / OnDeactivated.
// Me add a Sailing skill (custom id, SailingSkill), earned at the helm (SailingXp). Higher level: ship catch wind at
// worse angles, turn, stop and speed up faster (owner physics, helmsman's level), wider map reveal aboard (own level),
// less ship damage (best level aboard). Skill registration = [AlwaysOnPatch] (level kept while feature off); XP and
// every effect follow the toggle. Each game publish own level on own player ZDO (LevelPublisher) for ship owners.
// Both side: server refuse players who cannot play by its rules (PlayerCheck) and send its rules to everyone
// (ServerRules). Game code read rules only through ServerRules.Current, check IsPending first (pending = vanilla).
internal sealed partial class Plugin : ModPlugin
{
    // General (server only).
    internal static ConfigEntry<bool> AllowPlayersWithoutMod;

    // Skill.
    internal static ConfigEntry<float> XpPerKm;

    // Handling.
    internal static ConfigEntry<float> WindFloorAtMax;
    internal static ConfigEntry<float> NoGoShiftAtMax;
    internal static ConfigEntry<float> AccelerationBonusAtMax;
    internal static ConfigEntry<float> SailResponseAtMax;
    internal static ConfigEntry<float> TurnBonusAtMax;
    internal static ConfigEntry<float> RudderBonusAtMax;
    internal static ConfigEntry<float> BrakeAtMax;

    // Map.
    internal static ConfigEntry<float> RevealBonusAtMax;

    // Damage.
    internal static ConfigEntry<float> DamageReductionAtMax;

    private const string SkillSection = "Skill";
    private const string HandlingSection = "Handling";
    private const string MapSection = "Map";
    private const string DamageSection = "Damage";
    private const string ServerWins = " In multiplayer the setting of the server (or host) is used for everyone.";
    private const string Linear = " Lower levels get a share in proportion (Sailing 50 = half).";

#if DEBUG
    // Self test turn the feature off and on like the MC Mods tick, in memory only: never write Enabled (config file).
    // Framework ask LocalBlocker at every refresh: test set this, then call FeatureRegistry.RefreshAll().
    internal const string TestBlockedText = "Inactive: turned off by a self test.";
    internal static bool TestBlocked;

    protected override string LocalBlocker() => TestBlocked ? TestBlockedText : null;
#endif

    protected override void BindConfig()
    {
        // Debug build only (call gone in Release): log watcher of the self tests, and the tests that must be there
        // while the feature is off (server without me).
        SelfTests.RegisterAlways();

        var d = SailingRules.Default;

        AllowPlayersWithoutMod = Config.Bind("General", "AllowPlayersWithoutMod", false, new ConfigDescription(
            "Used only by the server (or the host). Off (default): a player whose game does not have this mod, has it "
            + "turned off or has another version of it is refused about a second after joining, or about a second after "
            + "turning it off, and their game shows \"Incompatible version\", so everyone sails by the same rules. On: "
            + "they may play; a ship their game simulates (the game of a player aboard) sails and takes damage like in "
            + "the normal game (only the rudder of a helmsman who has the mod still swings faster), they earn no "
            + "Sailing, and their level gives no bonus to ships they steer.",
            null, new ConfigurationManagerAttributes { Order = 90 }));

        XpPerKm = Number(SkillSection, "XpPerKm", d.XpPerKm, 0f, SailingRules.XpPerKmMax, 100,
            "Sailing XP the helmsman earns per kilometre the ship travels (paddling counts too; passengers earn "
            + "nothing). The Rested bonus and the world's skill-gain setting apply as for any skill. With the default, "
            + "Sailing 20 takes about 8 km at the helm, Sailing 50 about 73 km. 0 = no XP.");

        WindFloorAtMax = Number(HandlingSection, "WindFloorAtMax", d.WindFloorAtMax, SailingRules.WindFloorMin,
            SailingRules.WindFloorMax, 100,
            "How well the sail pulls at Sailing 100 with the wind straight from behind or close to the bow, compared "
            + "with the best angle (wind from the side). The normal game gives 0.7 there; 1 = every usable angle pulls "
            + "like the best one." + Linear);
        NoGoShiftAtMax = Number(HandlingSection, "NoGoShiftAtMax", d.NoGoShiftAtMax, 0f, SailingRules.NoGoShiftMax, 95,
            "Shrinks the no-go zone at Sailing 100: the angle around the bow where the wind gives no pull at all. The "
            + "normal game's zone is about 37 degrees on each side of the bow; 0.1 makes it about 26, 0.15 (the most) "
            + "about 18. Sailing straight into the wind is never possible." + Linear);
        AccelerationBonusAtMax = Number(HandlingSection, "AccelerationBonusAtMax", d.AccelerationBonusAtMax, 0f,
            SailingRules.BonusMax, 90,
            "How much faster the ship picks up speed at Sailing 100, under sail or paddling (0.5 = 50% more push along "
            + "the bow; the sail's sideways push is unchanged). It does not raise the top speed: the ship just reaches "
            + "it sooner, and also slows down sooner when the push stops." + Linear);
        SailResponseAtMax = Number(HandlingSection, "SailResponseAtMax", d.SailResponseAtMax, 0f,
            SailingRules.SailResponseMax, 85,
            "How much faster the sail fills and empties at Sailing 100 when you change speed or the wind changes, per "
            + "second (the normal game takes about 2 seconds; 1.5 makes it about 1). 0 = normal game." + Linear);
        TurnBonusAtMax = Number(HandlingSection, "TurnBonusAtMax", d.TurnBonusAtMax, 0f, SailingRules.BonusMax, 80,
            "How much faster the ship turns at Sailing 100, under sail and paddling (0.5 = 50% more steering force)."
            + Linear);
        RudderBonusAtMax = Number(HandlingSection, "RudderBonusAtMax", d.RudderBonusAtMax, 0f, SailingRules.BonusMax, 75,
            "How much faster the rudder swings when you steer, at Sailing 100 (0.5 = 50% faster)." + Linear);
        BrakeAtMax = Number(HandlingSection, "BrakeAtMax", d.BrakeAtMax, 0f, SailingRules.BrakeMax, 70,
            "Braking while the speed setting is Stop, at Sailing 100: share of the forward speed the ship loses per "
            + "second on top of the normal game's water drag (0.8 = the speed halves in about a second). Needs someone "
            + "at the helm. 0 = the ship drifts like in the normal game." + Linear);

        RevealBonusAtMax = Number(MapSection, "RevealBonusAtMax", d.RevealBonusAtMax, 0f, SailingRules.RevealBonusMax,
            100,
            "How much wider you reveal the map around you while aboard a ship (on its deck or at its helm), at Sailing "
            + "100: 1 = twice the normal radius (the normal game reveals 50 m around you, so 100 m). Each player's own "
            + "Sailing counts for their own map." + Linear);

        DamageReductionAtMax = Number(DamageSection, "DamageReductionAtMax", d.DamageReductionAtMax, 0f,
            SailingRules.DamageReductionMax, 100,
            "How much less damage a ship takes at Sailing 100, from rocks, water impacts, creatures, fire and the "
            + "Ashlands sea (0.5 = half damage). The best Sailing level aboard counts. Damage from players, a capsized "
            + "ship and weather wear are never reduced." + Linear);

        // Every rule setting (all sections but General): new own snapshot, server send it again once settled.
        Config.SettingChanged += OnSettingChanged;
        AllowPlayersWithoutMod.SettingChanged += OnAllowChanged;

        // Last (after every Bind): skill name words by reflection. Throw = framework Error status, feature off; the
        // always-on registration stay, so the level is kept. Console set up before me = Tab list now.
        SailingSkill.InitWords();
        SailingSkill.WrapCommandsIfReady();
    }

    protected override void OnActivated()
    {
        Compat.Reset();
        ServerRules.Start();
        PlayerCheck.Start();
        HelmSkill.Invalidate();
        SailingXp.Reset();
        LevelPublisher.Reset();
        LevelPublisher.PublishNow();
        SelfTests.Register();
    }

    // Patches still on during this call. Nothing vanilla to put back: scaled ship and map fields are put back inside
    // each call (finalizers). Own level withdrawn (-1) so ship owners stop counting it.
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
        Step("LevelPublisher.Withdraw", LevelPublisher.Withdraw);
        Step("SailingXp.Reset", SailingXp.Reset);
        Step("HelmSkill.Invalidate", HelmSkill.Invalidate);
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

    private ConfigEntry<float> Number(string section, string key, float value, float min, float max, int order,
        string text)
    {
        return Config.Bind(section, key, value, new ConfigDescription(text + ServerWins,
            new AcceptableValueRange<float>(min, max), new ConfigurationManagerAttributes { Order = order }));
    }

    private static void OnSettingChanged(object sender, SettingChangedEventArgs e)
    {
        try
        {
            var setting = e?.ChangedSetting;
            if (setting == null || setting.Definition.Section == "General")
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
