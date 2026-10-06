using System;
using BepInEx.Configuration;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.MusicInstrumentsMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, server check, status, live on/off. Patches in Patches/ apply only while Active.
// No Awake/Start/Update/OnDestroy here (they hide ModPlugin ones). Use BindConfig / OnActivated / OnDeactivated.
// Me add three craftable instruments (flute, lyre, tambourine; models and icons made in code) and a synthesizer that
// plays them (no audio files). Attack with one in hand = song window: play a built-in song or a MIDI file (autoplay),
// or perform it in a rhythm mini-game. Notes go to the server, server send them to players in hearing range, every
// game synthesize them at the player. A good mini-game run (SuccessSeconds at SuccessAccuracy) = Encore: performer
// and every player in BonusRange get the Music status effect (+ComfortBonus comfort). Both side: server refuse players
// without me (PlayerCheck) and send its rules to everyone (ServerRules). Items and status effect stay registered while
// feature off ([AlwaysOnPatch] classes read FeatureActive themselves).
internal sealed partial class Plugin : ModPlugin
{
    internal const string General = "General";
    internal const string RecipesSection = "Recipes";
    internal const string ComfortSection = "Comfort";
    internal const string HearingSection = "Hearing";
    internal const string SoundSection = "Sound";
    internal const string MiniGameSection = "MiniGame";
    internal const string SongsSection = "Songs";
    private const string ServerWins = " In multiplayer the setting of the server (or host) is used for everyone.";
    private const string Personal = " Each player's own choice.";

    // General (server only).
    internal static ConfigEntry<bool> AllowPlayersWithoutMod;

    // Recipes (rules, server wins).
    internal static ConfigEntry<string> FluteResources;
    internal static ConfigEntry<string> FluteStation;
    internal static ConfigEntry<int> FluteStationLevel;
    internal static ConfigEntry<string> LyreResources;
    internal static ConfigEntry<string> LyreStation;
    internal static ConfigEntry<int> LyreStationLevel;
    internal static ConfigEntry<string> TambourineResources;
    internal static ConfigEntry<string> TambourineStation;
    internal static ConfigEntry<int> TambourineStationLevel;

    // Comfort (rules, server wins).
    internal static ConfigEntry<int> ComfortBonus;
    internal static ConfigEntry<float> SuccessSeconds;
    internal static ConfigEntry<float> SuccessAccuracy;
    internal static ConfigEntry<float> BonusMinutes;
    internal static ConfigEntry<float> BonusRange;

    // Hearing (rules, server wins).
    internal static ConfigEntry<float> HearingRange;

    // Sound (personal, never sent).
    internal static ConfigEntry<float> Volume;
    internal static ConfigEntry<float> GameMusicVolume;

    // MiniGame (personal, never sent).
    internal static ConfigEntry<KeyCode> Lane1Key;
    internal static ConfigEntry<KeyCode> Lane2Key;
    internal static ConfigEntry<KeyCode> Lane3Key;
    internal static ConfigEntry<KeyCode> Lane4Key;
    internal static ConfigEntry<float> NoteSpeed;

    // Songs (personal, never sent).
    internal static ConfigEntry<string> SongsFolder;

    private static bool _featureActive;

#if DEBUG
    // Self test play "feature off" without the toggle. Never in release.
    internal static bool TestInactive { get; set; }
#endif

    // Feature on (patches applied) and not faked off by a self test. Always-on code (registration) read this: its
    // patches stay while the feature is off.
    internal static bool FeatureActive
    {
        get
        {
#if DEBUG
            if (TestInactive)
            {
                return false;
            }
#endif
            return _featureActive;
        }
    }

    protected override void BindConfig()
    {
        var d = MusicRules.Default;

        AllowPlayersWithoutMod = Config.Bind(General, "AllowPlayersWithoutMod", false, new ConfigDescription(
            "Used only by the server (or the host). Off (default): a player whose game does not have this mod, has it "
            + "turned off or has another version of it is refused about a second after joining, or about a second after "
            + "turning it off, and their game shows \"Incompatible version\", so every game knows the instruments. On: "
            + "they may play. Players without the mod never see instruments (on the ground, in chests or in hands), a "
            + "chest loses its instruments when such a player takes or adds an item in it, and they hear no music; players "
            + "with the mod turned off or another version of it see and keep instruments, but hear no music and get no "
            + "comfort bonus.",
            null, new ConfigurationManagerAttributes { Order = 90 }));

        FluteResources = BindResources(InstrumentKind.Flute, "Flute", d.FluteResources, 100);
        FluteStation = BindStation("Flute", d.FluteStation, 99);
        FluteStationLevel = BindLevel("Flute", d.FluteStationLevel, 98);
        LyreResources = BindResources(InstrumentKind.Lyre, "Lyre", d.LyreResources, 90);
        LyreStation = BindStation("Lyre", d.LyreStation, 89);
        LyreStationLevel = BindLevel("Lyre", d.LyreStationLevel, 88);
        TambourineResources = BindResources(InstrumentKind.Tambourine, "Tambourine", d.TambourineResources, 80);
        TambourineStation = BindStation("Tambourine", d.TambourineStation, 79);
        TambourineStationLevel = BindLevel("Tambourine", d.TambourineStationLevel, 78);

        ComfortBonus = Config.Bind(ComfortSection, "ComfortBonus", d.ComfortBonus, new ConfigDescription(
            "Comfort added by the Music effect (each point of comfort makes Rested last one minute longer). A player "
            + "gets the effect by playing the mini-game well (see SuccessSeconds), and so does every player within "
            + "BonusRange. 0 = no bonus." + ServerWins,
            new AcceptableValueRange<int>(MusicRules.ComfortMin, MusicRules.ComfortMax),
            new ConfigurationManagerAttributes { Order = 100, ShowRangeAsPercent = false }));
        SuccessSeconds = Config.Bind(ComfortSection, "SuccessSeconds", d.SuccessSeconds, new ConfigDescription(
            "Seconds of good mini-game play needed for the Music effect. Each further stretch this long renews it."
            + ServerWins,
            new AcceptableValueRange<float>(MusicRules.SuccessSecondsMin, MusicRules.SuccessSecondsMax),
            new ConfigurationManagerAttributes { Order = 95 }));
        SuccessAccuracy = Config.Bind(ComfortSection, "SuccessAccuracy", d.SuccessAccuracy, new ConfigDescription(
            "How well you must play while the meter fills: the share of your last ten notes hit in time (0.7 = seven "
            + "out of ten; a near-perfect hit counts fully, a less exact one three quarters, a missed note or a key pressed "
            + "with no note to hit not at all)." + ServerWins,
            new AcceptableValueRange<float>(MusicRules.AccuracyMin, MusicRules.AccuracyMax),
            new ConfigurationManagerAttributes { Order = 90, ShowRangeAsPercent = false }));
        BonusMinutes = Config.Bind(ComfortSection, "BonusMinutes", d.BonusMinutes, new ConfigDescription(
            "How long the Music effect lasts, in minutes." + ServerWins,
            new AcceptableValueRange<float>(MusicRules.BonusMinutesMin, MusicRules.BonusMinutesMax),
            new ConfigurationManagerAttributes { Order = 85 }));
        BonusRange = Config.Bind(ComfortSection, "BonusRange", d.BonusRange, new ConfigDescription(
            "Players within this many metres of the performer get the Music effect too." + ServerWins,
            new AcceptableValueRange<float>(MusicRules.BonusRangeMin, MusicRules.BonusRangeMax),
            new ConfigurationManagerAttributes { Order = 80 }));

        HearingRange = Config.Bind(HearingSection, "HearingRange", d.HearingRange, new ConfigDescription(
            "Players farther than this many metres from a performer do not hear the music (it fades out toward this "
            + "distance)." + ServerWins,
            new AcceptableValueRange<float>(MusicRules.HearingRangeMin, MusicRules.HearingRangeMax),
            new ConfigurationManagerAttributes { Order = 100 }));

        Volume = Config.Bind(SoundSection, "Volume", 0.8f, new ConfigDescription(
            "Loudness of every instrument you hear, yours and other players' (the game's Master and Sound effects "
            + "volume apply on top). 0 = silent." + Personal,
            new AcceptableValueRange<float>(0f, 1f),
            new ConfigurationManagerAttributes { Order = 100, ShowRangeAsPercent = false }));
        GameMusicVolume = Config.Bind(SoundSection, "GameMusicVolume", 0.3f, new ConfigDescription(
            "Loudness of the game's own background music while you hear someone play: 1 = unchanged, 0 = silent. It "
            + "fades back afterwards." + Personal,
            new AcceptableValueRange<float>(0f, 1f),
            new ConfigurationManagerAttributes { Order = 95, ShowRangeAsPercent = false }));

        Lane1Key = BindLane(1, KeyCode.D, 100);
        Lane2Key = BindLane(2, KeyCode.F, 99);
        Lane3Key = BindLane(3, KeyCode.J, 98);
        Lane4Key = BindLane(4, KeyCode.K, 97);
        NoteSpeed = Config.Bind(MiniGameSection, "NoteSpeed", 1f, new ConfigDescription(
            "How fast the notes fall in the mini-game: higher = faster and farther apart, easier to read in fast songs. "
            + "It does not change the song's tempo." + Personal,
            new AcceptableValueRange<float>(0.5f, 2f),
            new ConfigurationManagerAttributes { Order = 90 }));

        SongsFolder = Config.Bind(SongsSection, "SongsFolder", "", new ConfigDescription(
            "Folder with your own MIDI songs (.mid, .midi, .kar, .rmi). Empty (default): the folder MC_Valheim/Songs next to this "
            + "config file (BepInEx/config/MC_Valheim/Songs), made on first use. The song window lists the files "
            + "every time it opens." + Personal,
            null, new ConfigurationManagerAttributes { Order = 100 }));

        SongLibrary.DefaultFolder = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "MC_Valheim", "Songs");
        SongLibrary.ConfiguredFolder = SongsFolder.Value;
        SongsFolder.SettingChanged += (_, _) => SongLibrary.ConfiguredFolder = SongsFolder.Value;
        Volume.SettingChanged += (_, _) => OnVolumeChanged();

        // Rule setting (sections Recipes, Comfort, Hearing): new own snapshot, server send it again. Others: not rules.
        Config.SettingChanged += OnSettingChanged;
        AllowPlayersWithoutMod.SettingChanged += OnAllowChanged;
        // Rules in force changed (server rules came, own config, pending over): recipes follow. Whole session: rebuild
        // read feature state itself (off = recipes hidden).
        ServerRules.Changed -= OnRulesChanged;
        ServerRules.Changed += OnRulesChanged;
        LaneKeys.Cache();
    }

    private ConfigEntry<string> BindResources(InstrumentKind kind, string name, string value, int order)
    {
        var extra = kind == InstrumentKind.Lyre
            ? " Silver and linen thread by default: a silver frame with linen strings."
            : kind == InstrumentKind.Tambourine ? " Fine wood for the frame, leather scraps for the skin by default." : "";
        return Config.Bind(RecipesSection, name + "Recipe", value, new ConfigDescription(
            $"Materials of one {name.ToLowerInvariant()}, as item names with amounts: Name:amount,Name:amount." + extra
            + " Unknown names are skipped (with a warning in the log); with no valid material the recipe is hidden."
            + ServerWins,
            null, new ConfigurationManagerAttributes { Order = order }));
    }

    private ConfigEntry<string> BindStation(string name, string value, int order) =>
        Config.Bind(RecipesSection, name + "Station", value, new ConfigDescription(
            $"Crafting station of the {name.ToLowerInvariant()} recipe, by its object name: piece_workbench (default), "
            + "forge, piece_artisanstation... Empty: crafted by hand from the inventory." + ServerWins,
            null, new ConfigurationManagerAttributes { Order = order }));

    private ConfigEntry<int> BindLevel(string name, int value, int order) =>
        Config.Bind(RecipesSection, name + "StationLevel", value, new ConfigDescription(
            $"Station level the {name.ToLowerInvariant()} recipe needs." + ServerWins,
            new AcceptableValueRange<int>(MusicRules.StationLevelMin, MusicRules.StationLevelMax),
            new ConfigurationManagerAttributes { Order = order, ShowRangeAsPercent = false }));

    private ConfigEntry<KeyCode> BindLane(int lane, KeyCode value, int order)
    {
        var entry = Config.Bind(MiniGameSection, "Lane" + lane + "Key", value, new ConfigDescription(
            $"Key of mini-game lane {lane} (lanes from left to right). Keys the game uses while you walk (W A S D) are "
            + "fine: you stand still during the mini-game, and the game's own keys are held back while it runs. Esc and "
            + "the right mouse button cannot be lane keys (they stop the mini-game), and a key the game cannot read "
            + "cannot either: such a lane is turned off (warning in the log)." + Personal,
            null, new ConfigurationManagerAttributes { Order = order }));
        entry.SettingChanged += (_, _) => LaneKeys.Cache();
        return entry;
    }

    protected override void OnActivated()
    {
        _featureActive = true;
        LaneKeys.Cache();
        ServerRules.Start();
        PlayerCheck.Start();
        NoteRelay.Start();
        InstrumentContent.Rebuild();
        Performance.Reset();
        SelfTests.Register();
    }

    // Patches still on during this call (also at game quit). Each step alone: one failure never skip the rest.
    // Performance stopped at once: sound, window, mini-game, pose and the ZDO flag back. Recipes hidden; the items
    // and the status effect stay known (always-on registration).
    protected override void OnDeactivated()
    {
        _featureActive = false;
        // Conditional method (gone in Release): no delegate to it, so own try instead of Step().
        try
        {
            SelfTests.Unregister();
        }
        catch (Exception e)
        {
            PatchGuard.Report("Plugin.OnDeactivated SelfTests.Unregister", e);
        }
        Step("Performance.Shutdown", Performance.Shutdown);
        Step("Listeners.Shutdown", Listeners.Shutdown);
        Step("InstrumentPose.Shutdown", InstrumentPose.Shutdown);
        Step("InstrumentContent.Rebuild", () => InstrumentContent.Rebuild());
        Step("NoteRelay.Stop", NoteRelay.Stop);
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

    // Rebuild waits until the rules stay still (a recipe typed in a config window change at every key).
    private static void OnRulesChanged()
    {
        try
        {
            InstrumentContent.RequestRebuild();
        }
        catch (Exception ex)
        {
            PatchGuard.Report("Plugin.OnRulesChanged", ex);
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
            if (section == RecipesSection || section == ComfortSection || section == HearingSection)
            {
                ServerRules.OwnChanged();
            }
        }
        catch (Exception ex)
        {
            PatchGuard.Report("Plugin.OnSettingChanged", ex);
        }
    }

    private static void OnVolumeChanged()
    {
        try
        {
            Performance.ApplyVolume();
            Listeners.ApplyVolume();
        }
        catch (Exception ex)
        {
            PatchGuard.Report("Plugin.OnVolumeChanged", ex);
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
