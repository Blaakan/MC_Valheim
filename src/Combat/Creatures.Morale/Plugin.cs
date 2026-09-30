using System;
using BepInEx.Configuration;
using MC.Shared;

namespace MC.Combat.CreaturesMoraleMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, server check, status, live on/off. Patches in Patches/ apply only while Active.
// No Awake/Start/Update/OnDestroy here (they hide ModPlugin ones). Use BindConfig / OnActivated / OnDeactivated.
// Me make weak creatures afraid of the players who outgrew them (two bosses past their biome's boss, by that player's
// boss kills + kill counts; biome = where the creature spawned, never easier than its home): they never attack that
// player and run when the player come close, unless the player provoke or corner them. A pack run when its leader die.
// Design: docs/design/combat-creatures-morale.md.
// Both side: server refuse players without me, or with me off, or other network version (PlayerCheck), and send its
// rules to everyone (ServerRules). Game code read rules only through ServerRules.Current (server's when client of a
// server with me, else own config); ServerRules.Pending = client still wait for server rules = every creature hostile.
internal sealed partial class Plugin : ModPlugin
{
    internal static readonly ConfigEntry<string>[] HomeLists = new ConfigEntry<string>[MoraleRules.HomeCount];
    internal static ConfigEntry<int> BossesAhead;
    internal static ConfigEntry<string> Elites;
    internal static ConfigEntry<int> EliteExtraBosses;
    internal static ConfigEntry<int> StarRank;
    internal static ConfigEntry<string> KillSteps;
    internal static ConfigEntry<int> MaxBossesSkippedByKills;
    internal static ConfigEntry<float> FearRange;
    internal static ConfigEntry<bool> NightHuntersCanBeAfraid;
    internal static ConfigEntry<float> ProvokedSeconds;
    internal static ConfigEntry<float> NearMissRange;
    internal static ConfigEntry<float> CorneredRange;
    internal static ConfigEntry<float> CorneredSeconds;
    internal static ConfigEntry<string> Packs;
    internal static ConfigEntry<float> RoutRadius;
    internal static ConfigEntry<float> RoutSeconds;
    internal static ConfigEntry<float> ShakenSeconds;
    internal static ConfigEntry<bool> ShowProgressMessages;
    internal static ConfigEntry<bool> AllowPlayersWithoutMod;
#if DEBUG
    internal static ConfigEntry<int> ForceBossRank;
#endif

    // Sections of personal settings (never sent, never rules). Every other section but General = rules.
    private const string DisplaySection = "Display";
    private const string DebugSection = "Debug";

    private const string ServerWins = " In multiplayer the setting of the server (or host) is used for everyone.";

    protected override void BindConfig()
    {
        AllowPlayersWithoutMod = Config.Bind("General", "AllowPlayersWithoutMod", false, new ConfigDescription(
            "Used only by the server (or the host). Off: a player whose game does not have this mod, has it turned off, "
            + "or has a version that cannot talk to the server's is refused and their game shows \"Incompatible "
            + "version\", because the creatures their game controls would attack everyone the normal way. This happens "
            + "about a second after they join, or about a second after they turn the mod off while playing. On: such "
            + "players may play; creatures near them behave as in the normal game toward them.",
            null, new ConfigurationManagerAttributes { Order = 90 }));

        // Afraid creatures: who is afraid of whom (bosses ahead, elites, stars, kills), fear range, night hunters.
        const string afraid = "Afraid creatures";
        BossesAhead = Config.Bind(afraid, "BossesAhead", 2, new ConfigDescription(
            "How many bosses past the boss of a creature's biome a player must have helped kill (hit it at least once "
            + "before it died) before that creature is afraid of them. The biome bosses are: Meadows Eikthyr, Black "
            + "Forest The Elder, Swamp and Ocean Bonemass, Mountains Moder, Plains Yagluth, Mistlands The Queen, "
            + "Ashlands Fader, Deep North Kall. With 2, Black Forest creatures are afraid of a player who has helped kill "
            + "Moder. A creature counts as a creature of the biome it spawned in, or of its home biome if that is harder "
            + "(see Home biomes)." + ServerWins,
            new AcceptableValueRange<int>(MoraleRules.MinBossesAhead, MoraleRules.MaxBossesAhead),
            new ConfigurationManagerAttributes { Order = 100 }));
        Elites = Config.Bind(afraid, "Elites", MoraleRules.DefaultElites, new ConfigDescription(
            "Creatures (prefab names, as used by the spawn command, comma-separated) that need EliteExtraBosses more "
            + "bosses than the others of their biome: pack leaders and the biome's big creatures. A creature must also "
            + "be in a Home biomes list." + ServerWins,
            null, new ConfigurationManagerAttributes { Order = 95 }));
        EliteExtraBosses = Config.Bind(afraid, "EliteExtraBosses", 1, new ConfigDescription(
            "How many more bosses the Elites need before they are afraid of a player." + ServerWins,
            new AcceptableValueRange<int>(MoraleRules.MinEliteExtra, MoraleRules.MaxEliteExtra),
            new ConfigurationManagerAttributes { Order = 94 }));
        StarRank = Config.Bind(afraid, "StarRank", 1, new ConfigDescription(
            "Each star of a creature needs this many more bosses before it is afraid of you. 0 = stars do not matter."
            + ServerWins,
            new AcceptableValueRange<int>(MoraleRules.MinStarRank, MoraleRules.MaxStarRank),
            new ConfigurationManagerAttributes { Order = 90 }));
        KillSteps = Config.Bind(afraid, "KillSteps", MoraleRules.DefaultKillSteps, new ConfigDescription(
            $"Up to {MoraleRules.MaxKillSteps} increasing numbers ({MoraleRules.MinKillStep}-{MoraleRules.MaxKillStep}), "
            + "comma-separated. Each number of kills you reach of one kind of creature counts as one more boss toward "
            + "that kind only (see MaxBossesSkippedByKills). Kills are your character's lifetime kills, as the game "
            + "counts them: creatures that share a name share the count (all skeletons; Greydwarfs and Frozen "
            + "Greydwarfs), and killing your own tames counts. Empty = kill counts do not matter." + ServerWins,
            null, new ConfigurationManagerAttributes { Order = 89 }));
        MaxBossesSkippedByKills = Config.Bind(afraid, "MaxBossesSkippedByKills", 1, new ConfigDescription(
            "Kill steps can make a kind of creature afraid of you at most this many bosses early; beyond that they only "
            + "make up for its stars. 0 = kill steps only make up for stars." + ServerWins,
            new AcceptableValueRange<int>(MoraleRules.MinBossesSkipped, MoraleRules.MaxBossesSkipped),
            new ConfigurationManagerAttributes { Order = 88 }));
        FearRange = Config.Bind(afraid, "FearRange", 12f, new ConfigDescription(
            "An afraid creature runs away when a player it is afraid of comes this close (in metres) and it can see or "
            + "hear them, until that player is a few metres farther away; then it calms down. A player who sneaks up "
            + "unseen and unheard can get closer. It never attacks that player unless they attack it or corner it."
            + ServerWins,
            new AcceptableValueRange<float>(MoraleRules.MinFearRange, MoraleRules.MaxFearRange),
            new ConfigurationManagerAttributes { Order = 80 }));
        NightHuntersCanBeAfraid = Config.Bind(afraid, "NightHuntersCanBeAfraid", true, new ConfigDescription(
            "The creatures the game sends at night to hunt players after some boss kills (Fulings after Yagluth, "
            + "Seekers after The Queen, Charred after Fader) follow the same rules: they are afraid of the players who "
            + "outclass them and hunt the others. They count as creatures of their home biome (Plains, Mistlands, "
            + "Ashlands). Off = they hunt everyone as in the normal game. Raids and bosses always behave as in the "
            + "normal game." + ServerWins,
            null, new ConfigurationManagerAttributes { Order = 70 }));

        // Home biomes: where each creature lives (its biome's boss), one list per biome.
        const string homes = "Home biomes";
        for (var i = 0; i < MoraleRules.HomeCount; i++)
        {
            var level = Biomes.HomeLevels[i];
            var text = i == MoraleRules.HomeCount - 1
                ? "Creatures (prefab names, as used by the spawn command, comma-separated) of the open sea. The Ocean has "
                  + $"no boss and counts as the Swamp's level ({MoraleRules.BossNames[level - 1]}). A creature that "
                  + "spawned at sea counts as a creature of its home biome."
                : "Creatures (prefab names, as used by the spawn command, comma-separated) that live in "
                  + $"{MoraleRules.HomeNames[i]}, whose boss is {MoraleRules.BossNames[level - 1]}. A creature counts as a "
                  + "creature of this biome or of the biome it spawned in, whichever is harder. A creature in no list "
                  + "always behaves as in the normal game; a creature in several lists uses the easiest one.";
            HomeLists[i] = Config.Bind(homes, MoraleRules.HomeKeys[i], MoraleRules.DefaultHomeLists[i], new ConfigDescription(
                text + ServerWins, null, new ConfigurationManagerAttributes { Order = 100 - i }));
        }

        const string fightBack = "Fighting back";
        ProvokedSeconds = Config.Bind(fightBack, "ProvokedSeconds", 30f, new ConfigDescription(
            "After you hit an afraid creature (or one of your arrows, bolts or thrown weapons lands within "
            + "NearMissRange of it), it fights you until this many seconds have passed since the last time, then is "
            + "afraid of you again." + ServerWins,
            new AcceptableValueRange<float>(MoraleRules.MinProvokedSeconds, MoraleRules.MaxProvokedSeconds),
            new ConfigurationManagerAttributes { Order = 100 }));
        NearMissRange = Config.Bind(fightBack, "NearMissRange", 4f, new ConfigDescription(
            "One of your projectiles (arrow, bolt, thrown spear, harpoon) that lands this close (in metres) to an "
            + "afraid creature, even on another target, makes it fight you. Farther away it ignores the impact, while "
            + "in the normal game every creature within the weapon's noise range is alerted. That noise range is also "
            + "the limit of this setting: creatures farther from the impact never notice it (8 m for bows and "
            + "crossbows, 4 m for the Huntsman bow, 30 m for thrown spears and the harpoon). 0 = only real hits count."
            + ServerWins,
            new AcceptableValueRange<float>(MoraleRules.MinNearMissRange, MoraleRules.MaxNearMissRange),
            new ConfigurationManagerAttributes { Order = 90 }));
        CorneredRange = Config.Bind(fightBack, "CorneredRange", 3f, new ConfigDescription(
            "An afraid creature that is running from you fights back when you stay this close to it (in metres) for "
            + "CorneredSeconds: it cannot get away, or you keep up with it. 0 = never." + ServerWins,
            new AcceptableValueRange<float>(MoraleRules.MinCorneredRange, MoraleRules.MaxCorneredRange),
            new ConfigurationManagerAttributes { Order = 80 }));
        CorneredSeconds = Config.Bind(fightBack, "CorneredSeconds", 2f, new ConfigDescription(
            "How long (in seconds) you must stay within CorneredRange of a running creature before it fights back."
            + ServerWins,
            new AcceptableValueRange<float>(MoraleRules.MinCorneredSeconds, MoraleRules.MaxCorneredSeconds),
            new ConfigurationManagerAttributes { Order = 79 }));

        const string rout = "Rout";
        Packs = Config.Bind(rout, "Packs", MoraleRules.DefaultPacks, new ConfigDescription(
            "Packs whose followers flee when a leader dies: each pack is \"leader prefabs > follower prefabs\", "
            + "comma-separated, packs separated by semicolons. The rout happens only when a player or a tame took part "
            + "in the kill. Empty = no rout." + ServerWins,
            null, new ConfigurationManagerAttributes { Order = 100 }));
        RoutRadius = Config.Bind(rout, "RoutRadius", 25f, new ConfigDescription(
            "Followers within this distance (in metres) of the dead leader flee." + ServerWins,
            new AcceptableValueRange<float>(MoraleRules.MinRoutRadius, MoraleRules.MaxRoutRadius),
            new ConfigurationManagerAttributes { Order = 90 }));
        RoutSeconds = Config.Bind(rout, "RoutSeconds", 15f, new ConfigDescription(
            "How long (in seconds) the followers run away." + ServerWins,
            new AcceptableValueRange<float>(MoraleRules.MinRoutSeconds, MoraleRules.MaxRoutSeconds),
            new ConfigurationManagerAttributes { Order = 80 }));
        ShakenSeconds = Config.Bind(rout, "ShakenSeconds", 60f, new ConfigDescription(
            "After running away, the followers are afraid of every player for this many seconds unless someone attacks "
            + "them. 0 = they go back to normal at once." + ServerWins,
            new AcceptableValueRange<float>(MoraleRules.MinShakenSeconds, MoraleRules.MaxShakenSeconds),
            new ConfigurationManagerAttributes { Order = 70 }));

        // No name plate setting: a running creature shows the game's own alert icon (FleeFrames), nothing of mine.
        ShowProgressMessages = Config.Bind(DisplaySection, "ShowProgressMessages", true, new ConfigDescription(
            "Show a message when a boss kill or a number of kills makes more creatures afraid of you. Each player's own "
            + "choice.",
            null, new ConfigurationManagerAttributes { Order = 100 }));

#if DEBUG
        ForceBossRank = Config.Bind(DebugSection, "ForceBossRank", -1, new ConfigDescription(
            "For testing (Debug builds only): publish this boss rank instead of your real one. -1 = off.",
            new AcceptableValueRange<int>(-1, MoraleRules.BossCount),
            new ConfigurationManagerAttributes { Order = 100 }));
#endif

        // Rule setting changed (any section but General, Display, Debug): new own snapshot, server send it again,
        // standing published again, once the edits stop for a moment (ServerRules.OwnEdited). Debug rank changed:
        // standing published again. Safe while off (guards inside).
        Config.SettingChanged += OnSettingChanged;
        AllowPlayersWithoutMod.SettingChanged += OnAllowChanged;
    }

    // Mod on on this game (Attitudes.Decide step 1). Patches only run while Active anyway; this also cover the
    // OnDeactivated call (patches still on) and code reached outside patches (rpc handlers, rules change).
    internal static bool Live { get; private set; }

    // Design 7.5. Also run at plugin start (inside our Awake), before any world: everything here cope with no world.
    protected override void OnActivated()
    {
        Live = true;
        Compat.Reset();
        ServerRules.Start();
        Rout.Start();
        PlayerCheck.Start();
        Standing.PublishNow();
        SelfTests.Register();
    }

    // Patches still on during this call (also at quit). Creatures routing or running here go back to vanilla at next
    // tick (still alerted, no target: vanilla find a target or give up); afraid creatures notice players again at their
    // next search. ZDO keys stay (time-bounded, harmless). Nothing in ObjectDB, prefabs, items or the HUD was changed:
    // nothing else to put back.
    protected override void OnDeactivated()
    {
        Live = false;
        SelfTests.Unregister();
        PlayerCheck.Stop();
        ServerRules.Stop();
        Standing.Withdraw();
        CreatureState.Clear();
        StandingCache.Clear();
        BossFights.Clear();
        BossLadder.Clear();
        PrefabTokens.Clear();
        Standing.Clear();
        Rout.Stop();
    }

    private static void OnSettingChanged(object sender, SettingChangedEventArgs e)
    {
        try
        {
            var setting = e != null ? e.ChangedSetting : null;
            if (setting == null)
            {
                return;
            }
            var section = setting.Definition.Section;
            if (section == DebugSection)
            {
                Standing.PublishNow();
            }
            else if (section != "General" && section != DisplaySection)
            {
                ServerRules.OwnEdited();
            }
        }
        catch (Exception ex)
        {
            PatchGuard.Report("Plugin.OnSettingChanged", ex);
        }
    }

    // Switched back to refuse: players already in who are not compatible get checked (after the grace) too.
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
