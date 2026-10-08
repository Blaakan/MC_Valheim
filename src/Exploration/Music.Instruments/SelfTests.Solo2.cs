#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Exploration.MusicInstrumentsMod;

// Debug build only. In-world self tests, third part (list of SelfTests.Solo.cs goes on):
//   music.settings        T15  Volume, GameMusicVolume, NoteSpeed, lane keys changed while playing (memory only)
//   music.toggle          T17 T18  mod really turned off (framework) while playing / performing / window open, and on
//   music.logout          T19  what the mod does when the world goes: nothing left; works again after
//   music.pause           T20  Esc while performing = stops, silent; while a song plays = plays on in time
//   music.windowkeys      T22  window keys (arrows, Enter), key held at open, double-click on a row
//   music.pad             T22 T23 T36  the gamepad paths of window and HUD (pad said to be in use; game's own buttons)
//   music.hud             T23  where the lanes are, countdown, key labels, judgements, meter states, hidden HUD
//   music.swap            T24  hotbar key of another instrument stops the song at once
//   music.freeze          T25  the game frozen two seconds: song goes on in time, late notes dropped
//   music.hostsongs       T26  server songs of the own game in the window
//   music.playersongs     T27  own MIDI songs not allowed: hint, other songs play
//   music.sharefolder     T28  server songs folder: too big, not MIDI, twice, folder made again
//   music.freeplay.piano  T30  piano drawn, nothing else works, note held up to 8 s, fast taps
//   music.freeplay.more   T31 T32 T34  lyre and tambourine, the whole key map, Space, key names
//   music.freeplay.keys   T33  game keys held back; keys held at the stop are forgotten
//   music.freeplay.stops  T35  no meter / Encore / Music; put away, swim, hit: stops and falls silent
//   music.nobonus         T38  ComfortBonus 0: Encore with no comfort, no Music, also from another player's Encore
//   music.badrecipe       T39  mistakes in the recipe settings: one warning each, recipe hidden, crafted by hand
//   music.listen.more     M02 M04 M10 M11 M18 M19 (listener side with made-up players), T16
//   music.send            M03 M05 M11 M18 (sender side: what goes into the batches)
//   music.keys            real key and mouse events (Input System): T04 T08 T15 T22 T30 T32 T33
//   music.sleep           X02  waking up with Music on
//   music.crossmod        X03 X04  nothing left holding the keys; instruments count as tools
//   music.spyglass        X01  spyglass and instrument swapped while playing
//   music.cleanlog        T21  no warning or error of this mod in the log (last)
internal static partial class SelfTests
{
    private const string SettingsName = "music.settings";
    private const string ToggleName = "music.toggle";
    private const string LogoutName = "music.logout";
    private const string PauseName = "music.pause";
    private const string WindowKeysName = "music.windowkeys";
    private const string PadName = "music.pad";
    private const string HudName = "music.hud";
    private const string SwapName = "music.swap";
    private const string FreezeName = "music.freeze";
    private const string HostSongsName = "music.hostsongs";
    private const string PlayerSongsName = "music.playersongs";
    private const string ShareFolderName = "music.sharefolder";
    private const string PianoName = "music.freeplay.piano";
    private const string FreeMoreName = "music.freeplay.more";
    private const string FreeKeysName = "music.freeplay.keys";
    private const string FreeStopsName = "music.freeplay.stops";

    private static readonly KeyValuePair<string, Func<IEnumerator>>[] Solo2Tests =
    {
        new KeyValuePair<string, Func<IEnumerator>>(SettingsName, RunSettings),
        new KeyValuePair<string, Func<IEnumerator>>(ToggleName, RunToggle),
        new KeyValuePair<string, Func<IEnumerator>>(LogoutName, RunLogout),
        new KeyValuePair<string, Func<IEnumerator>>(PauseName, RunPause),
        new KeyValuePair<string, Func<IEnumerator>>(WindowKeysName, RunWindowKeys),
        new KeyValuePair<string, Func<IEnumerator>>(PadName, RunPad),
        new KeyValuePair<string, Func<IEnumerator>>(HudName, RunHud),
        new KeyValuePair<string, Func<IEnumerator>>(SwapName, RunSwap),
        new KeyValuePair<string, Func<IEnumerator>>(FreezeName, RunFreeze),
        new KeyValuePair<string, Func<IEnumerator>>(HostSongsName, RunHostSongs),
        new KeyValuePair<string, Func<IEnumerator>>(PlayerSongsName, RunPlayerSongs),
        new KeyValuePair<string, Func<IEnumerator>>(ShareFolderName, RunShareFolder),
        new KeyValuePair<string, Func<IEnumerator>>(PianoName, RunPiano),
        new KeyValuePair<string, Func<IEnumerator>>(FreeMoreName, RunFreeMore),
        new KeyValuePair<string, Func<IEnumerator>>(FreeKeysName, RunFreeKeys),
        new KeyValuePair<string, Func<IEnumerator>>(FreeStopsName, RunFreeStops),
    };

    private static void RegisterSolo2()
    {
        foreach (var t in Solo2Tests)
        {
            SelfTest.Register(t.Key, t.Value);
        }
        RegisterSolo3();
    }

    private static void UnregisterSolo2()
    {
        foreach (var t in Solo2Tests)
        {
            SelfTest.Unregister(t.Key);
        }
        UnregisterSolo3();
    }

    // A made-up other player's notes (one batch).
    private static NoteBatch BatchOf(ZDOID performer, int id, Vector3 place, InstrumentKind kind, float sentAt, BatchFlags flags = BatchFlags.None)
    {
        return new NoteBatch { Performance = id, Performer = performer, Position = place, Instrument = kind, SentAt = sentAt, Flags = flags };
    }

    // ---------- music.settings (T15) ----------

    private static IEnumerator RunSettings()
    {
        var c = new Checks(SettingsName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(SettingsName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules(60f);
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            c.Check(Performance.StartAuto(Preset("greensleeves"), -1, out var error), "plays: " + error);
            yield return new WaitForSeconds(0.5f);
            var emitter = Performance.LocalEmitter;
            var other = new ZDOID(515151L, 3u);
            var batch = BatchOf(other, 61, player.transform.position + player.transform.right * 5f, InstrumentKind.Lyre, 0f);
            for (var i = 0; i < 6; i++)
            {
                batch.Notes.Add(new Note(0.2f + i * 0.3f, 0.5f, (byte)(60 + i), 100));
            }
            Listeners.Receive(batch);
            var remote = Listeners.EmitterOf(other);
            if (!c.Check(emitter != null && emitter.Source != null && remote != null && remote.Source != null, "own and another player's sound source"))
            {
                c.Report();
                yield break;
            }
            // Volume: at once, for every instrument heard.
            Plugin.TestVolume = 0.2f;
            Plugin.TestVolumeChanged();
            c.Check(Mathf.Approximately(emitter.Source.volume, 0.2f) && Mathf.Approximately(remote.Source.volume, 0.2f),
                $"Volume 0.2 applies at once (own {F(emitter.Source.volume)}, other player's {F(remote.Source.volume)})");
            Plugin.TestVolume = 0.65f;
            Plugin.TestVolumeChanged();
            c.Check(Mathf.Approximately(emitter.Source.volume, 0.65f) && Mathf.Approximately(remote.Source.volume, 0.65f), "and again at 0.65");
            Plugin.TestVolume = null;
            Plugin.TestVolumeChanged();
            // GameMusicVolume: the game's music follows.
            Plugin.TestGameMusicVolume = 0.3f;
            yield return WaitFor(() => Listeners.Duck < 0.32f, 2f);
            c.Check(Listeners.Duck < 0.32f, $"GameMusicVolume 0.3: the game's music goes down ({F(Listeners.Duck)})");
            Plugin.TestGameMusicVolume = 1f;
            yield return WaitFor(() => Listeners.Duck > 0.995f, 2f);
            c.Check(Listeners.Duck > 0.995f && Performance.Mode == PerformanceMode.Auto, $"GameMusicVolume 1: it comes back while the song still plays ({F(Listeners.Duck)})");
            Plugin.TestGameMusicVolume = null;
            Performance.TestRequestStop();
            yield return Frames(3);

            // NoteSpeed and the lane keys, while performing.
            c.Check(Performance.StartMiniGame(Preset("kjerringa-med-staven"), -1, out error), "performs: " + error);
            yield return Frames(3);
            var game = Performance.Game;
            if (!c.Check(game != null, "rhythm game running"))
            {
                c.Report();
                yield break;
            }
            Plugin.TestNoteSpeed = 2f;
            c.Check(Mathf.Approximately(game.LookAhead, MiniGame.LookAheadBase / 2f), $"NoteSpeed 2: notes twice as fast ({F(game.LookAhead)} s shown)");
            Plugin.TestNoteSpeed = 0.5f;
            c.Check(Mathf.Approximately(game.LookAhead, MiniGame.LookAheadBase * 2f), "NoteSpeed 0.5: half as fast");
            Plugin.TestNoteSpeed = null;

            var oldLabel = LaneKeys.Label(0);
            LaneKeys.TestSet(0, KeyCode.D);
            yield return Frames(2);
            LaneKeys.TestSet(0, KeyCode.A);
            c.Check(LaneKeys.Key(0) == KeyCode.A, "Lane1Key = A: lane 1 is on A at once");
            yield return Frames(2);
            c.Check(MiniGameHud.LaneCaption(0) == LaneKeys.Label(0) && LaneKeys.Label(0).Length > 0 && LaneKeys.Label(0) != "-",
                $"the key shown under lane 1 changes at once (was '{oldLabel}', now '{MiniGameHud.LaneCaption(0)}')");
            // A is also a walking key: the game's Left button held, the controller reading it.
            x.Controller(true);
            var at = player.transform.position;
            Press("Left");
            yield return new WaitForSeconds(0.4f);
            Let("Left");
            x.Controller(false);
            rig.TakeControls();
            c.Check(Flat(player.transform.position, at) < 0.05f && Performance.Mode == PerformanceMode.MiniGame, "the game does not walk left during the rhythm game");

            // Keys that cannot be lane keys: lane off, one warning each.
            var mark = LogTap.ProblemMark;
            LaneKeys.TestSet(0, KeyCode.F15);
            LaneKeys.TestSet(0, KeyCode.F15);
            c.Check(LaneKeys.Key(0) == KeyCode.None && LogTap.CountProblems(mark, "MiniGame.Lane1Key = F15") == 1, "F15 (a key the game cannot read): lane off, one warning");
            yield return Frames(2);
            c.Check(MiniGameHud.LaneCaption(0) == "-", "the lane shows no key: '" + MiniGameHud.LaneCaption(0) + "'");
            LaneKeys.TestSet(0, KeyCode.Escape);
            LaneKeys.TestSet(0, KeyCode.Escape);
            c.Check(LaneKeys.Key(0) == KeyCode.None && LogTap.CountProblems(mark, "MiniGame.Lane1Key = Escape") == 1, "Escape: lane off, one warning");
            LaneKeys.TestSet(0, KeyCode.Mouse1);
            LaneKeys.TestSet(0, KeyCode.Mouse1);
            c.Check(LaneKeys.Key(0) == KeyCode.None && LogTap.CountProblems(mark, "MiniGame.Lane1Key = Mouse1") == 1, "Mouse1: lane off, one warning");
            c.Check(LogTap.ProblemsSince(mark).Count == 3, $"three warnings in all ({LogTap.ProblemsSince(mark).Count})");

            // F5, the console key: a lane key like another; the console stays shut during the rhythm game.
            LaneKeys.TestSet(0, KeyCode.F5);
            c.Check(LaneKeys.Key(0) == KeyCode.F5 && LogTap.ProblemsSince(mark).Count == 3, "F5 is taken as a lane key (no warning)");
            x.Console(true);
            yield return Tap("Console");
            var shown = false;
            for (var i = 0; i < 5; i++)
            {
                shown |= global::Console.IsVisible();
                yield return null;
            }
            c.Check(!shown && Performance.Mode == PerformanceMode.MiniGame, "the console key does not open the console during the rhythm game");
            Performance.TestRequestStop();
            yield return Frames(4);
            // The same press with no rhythm game opens it (the press me fake is one the game obeys).
            yield return Tap("Console");
            yield return Frames(2);
            c.Check(global::Console.IsVisible(), "without the rhythm game the same press opens the console");
            if (global::Console.instance != null)
            {
                global::Console.instance.m_chatWindow.gameObject.SetActive(false);
            }
            yield return Frames(3);
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.toggle (T17, part of T18) ----------

    private static IEnumerator RunToggle()
    {
        var c = new Checks(ToggleName);
        var player = Player.m_localPlayer;
        var db = ObjectDB.instance;
        if (player == null || db == null)
        {
            SelfTest.Fail(ToggleName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            x.NoShare();
            ServerRules.TestRules = TestRules();
            Plugin.TestGameMusicVolume = 0.3f;
            var controller = x.ControllerObject;
            var seman = player.GetSEMan();
            seman.RemoveStatusEffect(InstrumentContent.EffectHash, true);
            var flute = rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            var cursorBefore = ZCursor.IsRequested;

            // (a) while a song plays by itself.
            c.Check(Performance.StartAuto(Preset("greensleeves"), -1, out var error), "plays: " + error);
            yield return new WaitForSeconds(1.3f);
            c.Check(Performance.Mode == PerformanceMode.Auto && EmitterCount() >= 1 && InstrumentPose.Weight(player) > 0.9f
                    && Listeners.Duck < 0.999f && PlayingFlag(player) == (int)InstrumentKind.Flute && MiniGameHud.AutoShown, "playing, posed, game music down");
            RealOff();
            c.Check(Performance.Mode == PerformanceMode.None, "(a) turned off: the music stops at once");
            yield return Frames(3);
            c.Check(!ModActive && !Plugin.FeatureActive, "the mod is off");
            c.Check(EmitterCount() == 0, "no sound source left");
            c.Check(PlayingFlag(player) == 0 && Performance.PoseWeight <= 0f && InstrumentPose.Weight(player) < 0.01f, "the arms come down");
            c.Check(Listeners.Duck > 0.999f, "the game's music comes back");
            c.Check(!MiniGameHud.Shown && !MiniGameHud.Built, "status line gone");
            c.Check(!KeyCapture.Active && !Chat.instance.HasFocus() && controller != null && controller.TakeInput(false) && controller.TakeInput(true),
                "the game's keys and mouse work");
            c.Check(player.GetRightItem() == flute && InstrumentContent.KindOf(flute) == InstrumentKind.Flute, "the instrument stays in the hand");
            var punched = false;
            for (var i = 0; i < 20; i++)
            {
                player.SetControls(Vector3.zero, i == 0, true, false, false, false, false, false, false, false, false);
                punched |= player.InAttack();
                yield return null;
            }
            rig.TakeControls();
            c.Check(!punched && !SongWindow.IsOpen && !Performance.WindowOpen, "clicking does nothing (no punch, no window)");
            var hidden = true;
            var known = true;
            foreach (var kind in InstrumentContent.Kinds)
            {
                hidden &= !InstrumentContent.RecipeOf(kind).m_enabled;
                var name = InstrumentContent.ItemName(kind);
                known &= db.GetItemPrefab(name) != null && ZNetScene.instance.GetPrefab(name) != null;
            }
            c.Check(hidden, "the recipes are gone");
            c.Check(known && db.GetStatusEffect(InstrumentContent.EffectHash) != null,
                "the items and the Music effect stay known to the game (instruments are kept)");
            // The flute in the inventory is still that item: the name the game saves it under still finds its prefab
            // (an item whose prefab is gone is lost when the character loads). Not m_shared by reference: every item
            // has its own copy of it.
            var saved = flute.m_dropPrefab != null ? flute.m_dropPrefab.name : "";
            c.Check(player.GetInventory().ContainsItem(flute) && saved == InstrumentContent.ItemName(InstrumentKind.Flute)
                    && db.GetItemPrefab(saved) == flute.m_dropPrefab && flute.m_shared != null
                    && flute.m_shared.m_name == SharedOf(InstrumentKind.Flute).m_name,
                $"the flute in the inventory keeps its item (saved as '{saved}')");
            RealOn();
            yield return Frames(2);
            ServerRules.TestRules = TestRules();
            Plugin.TestGameMusicVolume = 0.3f;
            yield return new WaitForSeconds(InstrumentContent.RebuildDelaySeconds + 0.3f);
            var shownAgain = true;
            foreach (var kind in InstrumentContent.Kinds)
            {
                shownAgain &= InstrumentContent.RecipeOf(kind).m_enabled;
            }
            c.Check(ModActive && shownAgain, "back on: active, recipes shown");

            // (b) while performing.
            c.Check(Performance.StartMiniGame(Preset("kjerringa-med-staven"), -1, out error), "performs: " + error);
            yield return new WaitForSeconds(0.5f);
            c.Check(Performance.Mode == PerformanceMode.MiniGame && MiniGameHud.MiniShown && KeyCapture.Active, "rhythm game running");
            RealOff();
            c.Check(Performance.Mode == PerformanceMode.None && Performance.Game == null, "(b) turned off: the rhythm game stops at once");
            yield return Frames(3);
            c.Check(!MiniGameHud.Built && !KeyCapture.Active && !Chat.instance.HasFocus() && controller != null && controller.TakeInput(false),
                "its HUD is gone and the game's keys work");
            c.Check(EmitterCount() == 0 && PlayingFlag(player) == 0, "no sound, arms down");
            RealOn();
            yield return Frames(2);
            ServerRules.TestRules = TestRules();
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);

            // (c) with the song window open.
            yield return OpenWindow(rig);
            c.Check(SongWindow.IsOpen && ZCursor.IsRequested, "window open");
            RealOff();
            yield return Frames(3);
            c.Check(!SongWindow.IsOpen && !SongWindow.Built && !Performance.WindowOpen, "(c) turned off: the window closes");
            c.Check(!KeyCapture.Active && ZCursor.IsRequested == cursorBefore && controller != null && controller.TakeInput(true),
                "keys and mouse back to the game");
            RealOn();
            yield return Frames(2);
            ServerRules.TestRules = TestRules();

            // With Music running.
            yield return WaitFor(() => player.m_comfortLevel > 0, 4f);
            var baseComfort = player.m_comfortLevel;
            c.Check(MusicBonus.Grant(player, false), "Music given (Encore)");
            var effect = seman.GetStatusEffect(InstrumentContent.EffectHash);
            c.Check(effect != null && effect.GetIconText().StartsWith("+3  ", StringComparison.Ordinal)
                    && effect.GetTooltipString().StartsWith("A good performance warmed you: +3 comfort", StringComparison.Ordinal)
                    && player.GetComfortLevel() == baseComfort + 3, "on: icon +3, tooltip, comfort 3 higher");
            RealOff();
            yield return Frames(2);
            effect = seman.GetStatusEffect(InstrumentContent.EffectHash);
            if (c.Check(effect != null, "off: the Music icon stays"))
            {
                var ran = effect.m_time;
                yield return new WaitForSeconds(1.2f);
                var icon = effect.GetIconText();
                c.Check(effect.m_time > ran + 0.8f, "it keeps counting down");
                c.Check(icon.IndexOf('+') < 0 && icon.IndexOf(':') > 0, $"it shows no +3: '{icon}'");
                c.Check(effect.GetTooltipString() == "Music heard nearby.", "tooltip: " + effect.GetTooltipString());
                c.Check(player.GetComfortLevel() == baseComfort, $"comfort drops by 3 ({player.GetComfortLevel()})");
            }
            RealOn();
            yield return Frames(2);
            ServerRules.TestRules = TestRules();
            effect = seman.GetStatusEffect(InstrumentContent.EffectHash);
            c.Check(effect != null && effect.GetIconText().StartsWith("+3  ", StringComparison.Ordinal) && player.GetComfortLevel() == baseComfort + 3,
                "back on: +3 returns");
            // Everything works again.
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            yield return OpenWindow(rig);
            c.Check(SongWindow.IsOpen && SongWindow.TestSelect("preset:greensleeves") && SongWindow.TestPress("Play"), "window and Play work again");
            yield return new WaitForSeconds(1.2f);
            var rms = new float[1];
            yield return Measure(Performance.LocalEmitter, 0.6f, rms);
            c.Check(Performance.Mode == PerformanceMode.Auto && rms[0] > 0.002f && MiniGameHud.AutoShown, $"the song plays and sounds (rms {F(rms[0])})");
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.logout (T19) ----------

    private static IEnumerator RunLogout()
    {
        var c = new Checks(LogoutName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(LogoutName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            x.NoShare();
            ServerRules.TestRules = TestRules();
            Plugin.TestGameMusicVolume = 0.3f;
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            yield return OpenWindow(rig);
            c.Check(SongWindow.IsOpen && SongWindow.TestSelect("preset:greensleeves") && SongWindow.TestPress("Play"), "a song started from the window");
            var other = new ZDOID(616161L, 4u);
            var batch = BatchOf(other, 71, player.transform.position + player.transform.right * 6f, InstrumentKind.Lyre, 0f);
            for (var i = 0; i < 8; i++)
            {
                batch.Notes.Add(new Note(0.2f + i * 0.3f, 0.6f, (byte)(57 + i), 100));
            }
            Listeners.Receive(batch);
            yield return new WaitForSeconds(1f);
            c.Check(Performance.Mode == PerformanceMode.Auto && EmitterCount() >= 2 && SongWindow.Built && MiniGameHud.Built && Listeners.RemoteCount == 1,
                "playing, another player heard, window and HUD built");
            // What the mod does when the world's camera is destroyed (logout, disconnect, quit to menu).
            Patches.GameCameraDestroyPatches.TestRun();
            c.Check(Performance.Mode == PerformanceMode.None && !Performance.WindowOpen, "the song is over");
            yield return Frames(3);
            c.Check(EmitterCount() == 0 && Listeners.RemoteCount == 0, "no sound source left");
            c.Check(!SongWindow.Built && !MiniGameHud.Built, "no window and no HUD left");
            c.Check(!KeyCapture.Active && Listeners.Duck > 0.999f && PlayingFlag(player) == 0, "keys free, game music untouched, arms down");
            // Everything works after (as after loading the world again).
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            yield return OpenWindow(rig);
            c.Check(SongWindow.IsOpen && SongWindow.Built, "the window opens again");
            c.Check(SongWindow.TestSelect("preset:greensleeves") && SongWindow.TestPress("Play"), "Play pressed");
            yield return new WaitForSeconds(1.3f);
            var rms = new float[1];
            yield return Measure(Performance.LocalEmitter, 0.6f, rms);
            c.Check(Performance.Mode == PerformanceMode.Auto && rms[0] > 0.002f && MiniGameHud.AutoShown, $"the song plays again (rms {F(rms[0])})");
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.pause (T20) ----------

    private static IEnumerator RunPause()
    {
        var c = new Checks(PauseName);
        var player = Player.m_localPlayer;
        if (player == null || Menu.instance == null)
        {
            SelfTest.Fail(PauseName, "no player or menu");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules(60f);
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            yield return WaitReal(() => !Menu.IsVisible(), 2f);

            // (a) performing, then Esc.
            c.Check(Performance.StartMiniGame(Preset("kjerringa-med-staven"), -1, out var error), "performs: " + error);
            yield return Frames(2);
            var game = Performance.Game;
            var pressed = new HashSet<long>();
            var began = Time.realtimeSinceStartup;
            while (game != null && Time.realtimeSinceStartup - began < Judge.LeadIn + 2.5f && Performance.Mode == PerformanceMode.MiniGame)
            {
                PressDue(game, pressed, 0);
                yield return null;
            }
            var emitter = Performance.LocalEmitter;
            c.Check(game != null && game.Judge.Hits >= 2, "notes hit before the pause");
            yield return Tap("JoyMenu");
            yield return WaitReal(() => MenuShown, 1.5f);
            yield return Frames(3);
            if (!c.Check(MenuShown && Game.IsPaused(), $"Esc opens the menu and the game pauses (menu {MenuShown}, paused {Game.IsPaused()})"))
            {
                c.Report();
                yield break;
            }
            c.Check(Performance.Mode == PerformanceMode.None && !MiniGameHud.MiniShown, "(a) the rhythm game stops");
            yield return Real(0.6f);
            var rms = new float[1];
            yield return Measure(emitter, 0.4f, rms);
            c.Check(rms[0] < 0.0002f, $"no note keeps sounding (rms {F(rms[0])})");
            Menu.instance.Hide();
            yield return Real(0.4f);
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);

            // (b) a song playing by itself, then Esc: it plays on in time.
            c.Check(Performance.StartAuto(Preset("greensleeves"), -1, out error), "plays by itself: " + error);
            yield return new WaitForSeconds(1.2f);
            yield return Tap("JoyMenu");
            yield return WaitReal(() => MenuShown, 1.5f);
            yield return Frames(3);
            c.Check(MenuShown && Game.IsPaused() && Time.timeScale == 0f, "menu open, game paused");
            emitter = Performance.LocalEmitter;
            if (emitter != null)
            {
                emitter.ResetMeasure();
            }
            var clock = Performance.TestClock;
            var real = Time.realtimeSinceStartup;
            var playedBefore = Performance.NotesPlayed;
            var always = true;
            while (Time.realtimeSinceStartup - real < 2.2f)
            {
                always &= Performance.Mode == PerformanceMode.Auto;
                yield return null;
            }
            var songGone = Performance.TestClock - clock;
            var realGone = Time.realtimeSinceStartup - real;
            c.Check(always, "(b) it plays on behind the menu");
            c.Check(Mathf.Abs(songGone - realGone) < 0.25f, $"in time ({F(songGone)} s of song in {F(realGone)} s)");
            c.Check(emitter != null && emitter.Rms > 0.002f && Performance.NotesPlayed > playedBefore, $"and is heard (rms {F(emitter != null ? emitter.Rms : 0f)})");
            Menu.instance.Hide();
            clock = Performance.TestClock;
            real = Time.realtimeSinceStartup;
            yield return Real(1.2f);
            songGone = Performance.TestClock - clock;
            realGone = Time.realtimeSinceStartup - real;
            c.Check(Performance.Mode == PerformanceMode.Auto && Mathf.Abs(songGone - realGone) < 0.25f && !Game.IsPaused(),
                $"menu closed: it goes on in time ({F(songGone)} s in {F(realGone)} s)");
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.windowkeys (T22: keyboard and mouse part) ----------

    private static IEnumerator KeyStep(KeyCode key)
    {
        SongWindow.TestHeldKey = key;
        yield return Frames(2);
        SongWindow.TestHeldKey = KeyCode.None;
        yield return Frames(2);
    }

    private static List<string> SongIds()
    {
        var ids = new List<string>();
        foreach (var item in SongWindow.TestItems())
        {
            if (item.StartsWith("song|", StringComparison.Ordinal))
            {
                var rest = item.Substring(5);
                ids.Add(rest.Substring(0, rest.IndexOf('|')));
            }
        }
        return ids;
    }

    private static IEnumerator RunWindowKeys()
    {
        var c = new Checks(WindowKeysName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(WindowKeysName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules();
            x.NoShare();
            PadInput.Test = false;
            var folder = x.SongsFolder("MC_MusicKeysTest");
            File.WriteAllBytes(Path.Combine(folder, "selftest tune.mid"), MidiOf(Preset("greensleeves")));
            const string tuneId = "midi:selftest tune.mid";
            Performance.LastSongId = null;
            Performance.Repeat = false;
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);

            // A key held since before the window: the selection does not run down the list.
            SongWindow.TestHeldKey = KeyCode.DownArrow;
            Performance.TestRequestOpen();
            yield return Frames(3);
            var ids = SongIds();
            var first = SongWindow.SelectedId;
            c.Check(SongWindow.IsOpen && ids.Count == 10 && first == ids[0], "window open on the first entry: " + first);
            yield return Real(0.7f);
            c.Check(SongWindow.SelectedId == first, "a key held since before the window does not move the selection");
            SongWindow.TestHeldKey = KeyCode.None;
            yield return Frames(2);
            // Down, Down, Up.
            yield return KeyStep(KeyCode.DownArrow);
            c.Check(SongWindow.SelectedId == ids[1], "Down chooses the next song");
            yield return KeyStep(KeyCode.DownArrow);
            c.Check(SongWindow.SelectedId == ids[2], "Down again");
            yield return KeyStep(KeyCode.UpArrow);
            c.Check(SongWindow.SelectedId == ids[1], "Up chooses the song before");
            SongWindow.TestHeldKey = KeyCode.DownArrow;
            yield return Real(0.8f);
            SongWindow.TestHeldKey = KeyCode.None;
            yield return Frames(2);
            var index = ids.IndexOf(SongWindow.SelectedId);
            c.Check(index >= 4, $"Down held runs down the list (now on entry {index + 1})");
            c.Check(SongWindow.SelectedInView, "the selection stays in view");

            // Left / Right: the parts of a MIDI file, one step per press.
            c.Check(SongWindow.TestSelect(tuneId), "MIDI file picked");
            yield return Frames(2);
            c.Check(SongWindow.SelectedPart == -1, "part: automatic");
            yield return KeyStep(KeyCode.RightArrow);
            c.Check(SongWindow.SelectedPart == 0, "Right: first part");
            SongWindow.TestHeldKey = KeyCode.RightArrow;
            yield return Real(0.5f);
            SongWindow.TestHeldKey = KeyCode.None;
            yield return Frames(2);
            c.Check(SongWindow.SelectedPart == 1, "Right held: one step only (second part)");
            yield return KeyStep(KeyCode.LeftArrow);
            c.Check(SongWindow.SelectedPart == 0 && SongWindow.PartText != null && SongWindow.PartText.StartsWith("Part: ", StringComparison.Ordinal),
                "Left: back to the first part (" + SongWindow.PartText + ")");

            // Enter plays; the chat does not open (its button pressed with it, as when Enter is the chat key).
            Press("Chat");
            SongWindow.TestDownKey = KeyCode.Return;
            var typing = false;
            for (var i = 0; i < 6; i++)
            {
                typing |= ChatTyping;
                yield return null;
            }
            Let("Chat");
            c.Check(Performance.Mode == PerformanceMode.Auto && Performance.LastSongId == tuneId && Performance.LastPart == 0 && !SongWindow.IsOpen,
                "Enter plays the selected song and part");
            c.Check(!typing, "the chat does not open");
            Performance.TestRequestStop();
            yield return Frames(3);

            // Mouse: one click selects, two slow clicks do not play, a double-click plays.
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            yield return OpenWindow(rig);
            const string song = "preset:ravens-jig";
            c.Check(SongWindow.IsOpen && SongWindow.TestClickRow(song), "row clicked");
            yield return Frames(2);
            c.Check(SongWindow.SelectedId == song && Performance.Mode == PerformanceMode.None && SongWindow.IsOpen, "one click selects the row");
            yield return Real(0.5f);
            c.Check(SongWindow.TestClickRow(song), "clicked again later");
            yield return Frames(2);
            c.Check(Performance.Mode == PerformanceMode.None && SongWindow.IsOpen, "two slow clicks do not play");
            yield return Real(0.5f);
            SongWindow.TestClickRow(song);
            yield return null;
            SongWindow.TestClickRow(song);
            yield return Frames(3);
            c.Check(Performance.Mode == PerformanceMode.Auto && Performance.LastSongId == song && !SongWindow.IsOpen, "a double-click plays the row");
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.pad (T22, T23, T36: the gamepad paths) ----------

    // No gamepad here: the mod is told a gamepad is in use (PadInput.Test) and the game's own gamepad buttons are
    // pressed the way the game presses them. A real pad still has to be tried by hand.
    private static IEnumerator RunPad()
    {
        var c = new Checks(PadName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(PadName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules(60f);
            x.NoShare();
            x.SongsFolder("MC_MusicPadTest");
            var buttons = true;
            foreach (var name in new[] { "JoyDPadDown", "JoyDPadUp", "JoyButtonA", "JoyButtonB", "JoyButtonX", "JoyButtonY" })
            {
                buttons &= Btn(name) != null;
            }
            if (!c.Check(buttons, "the game has its gamepad buttons"))
            {
                c.Report();
                yield break;
            }
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            PadInput.Test = true;
            // Free play was the last thing played: on a pad the window still opens on the first song.
            Performance.LastSongId = SongLibrary.FreePlayId;
            Performance.TestRequestOpen();
            yield return Frames(3);
            var ids = SongIds();
            c.Check(SongWindow.IsOpen && ids.Count >= 9 && SongWindow.SelectedId == ids[1] && ids[0] == SongLibrary.FreePlayId,
                "on a gamepad the first song is selected, not Free play: " + SongWindow.SelectedId);
            c.Check(SongWindow.HelpNavText == "D-pad: choose a song and part    A: play    Y: repeat    B: close",
                "gamepad help line (no rhythm game on it): " + SongWindow.HelpNavText);
            // D-pad.
            Press("JoyDPadDown");
            yield return Frames(2);
            Let("JoyDPadDown");
            yield return Frames(2);
            c.Check(SongWindow.SelectedId == ids[2], "D-pad down chooses the next song");
            Press("JoyDPadUp");
            yield return Frames(2);
            Let("JoyDPadUp");
            yield return Frames(2);
            c.Check(SongWindow.SelectedId == ids[1], "D-pad up chooses the song before");
            // Y: Repeat.
            var repeat = Performance.Repeat;
            yield return Tap("JoyButtonY");
            yield return Frames(2);
            c.Check(Performance.Repeat == !repeat && SongWindow.RepeatText == (Performance.Repeat ? "Repeat: On" : "Repeat: Off"),
                "Y toggles Repeat: " + SongWindow.RepeatText);
            yield return Tap("JoyButtonY");
            yield return Frames(2);
            c.Check(Performance.Repeat == repeat, "Y again puts it back");
            // X: the rhythm game is not for the pad.
            yield return Tap("JoyButtonX");
            yield return Frames(2);
            c.Check(SongWindow.IsOpen && Performance.Mode == PerformanceMode.None && SongWindow.StatusText != null
                    && SongWindow.StatusText.StartsWith("The rhythm game is played on the keyboard (", StringComparison.Ordinal),
                "X starts nothing and says why: " + SongWindow.StatusText);
            // A on Free play: keyboard only.
            c.Check(SongWindow.TestSelect(SongLibrary.FreePlayId), "Free play picked");
            yield return Tap("JoyButtonA");
            yield return Frames(2);
            c.Check(SongWindow.IsOpen && Performance.Mode == PerformanceMode.None && SongWindow.StatusText == "Free play is played on the keyboard.",
                "A on Free play starts nothing: " + SongWindow.StatusText);
            // A on a song: plays.
            c.Check(SongWindow.TestSelect(ids[1]), "first song picked");
            yield return Tap("JoyButtonA");
            yield return Frames(2);
            c.Check(Performance.Mode == PerformanceMode.Auto && Performance.LastSongId == ids[1] && !SongWindow.IsOpen, "A plays the selected song");
            Performance.TestRequestStop();
            yield return Frames(3);
            // B closes, and the menu stays shut.
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            Performance.TestRequestOpen();
            yield return Frames(3);
            c.Check(SongWindow.IsOpen, "window open again");
            yield return Tap("JoyButtonB");
            var menu = false;
            for (var i = 0; i < 6; i++)
            {
                menu |= MenuShown;
                yield return null;
            }
            c.Check(!SongWindow.IsOpen && !menu, "B closes the window, no menu");

            // Free play started with the mouse, then the pad touched: the hint names Start.
            PadInput.Test = false;
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            Performance.TestRequestOpen();
            yield return Frames(3);
            c.Check(SongWindow.IsOpen && SongWindow.TestSelect(SongLibrary.FreePlayId) && SongWindow.TestPress("Play"), "Free play started with the mouse");
            yield return Frames(3);
            c.Check(Performance.Mode == PerformanceMode.FreePlay && MiniGameHud.FreeHintText == "Hold Space: octave up    Right click or Esc: stop",
                "keyboard hint: " + MiniGameHud.FreeHintText);
            PadInput.Test = true;
            yield return Frames(2);
            c.Check(MiniGameHud.FreeHintText == "Hold Space: octave up    Start or right click: stop", "gamepad hint: " + MiniGameHud.FreeHintText);
            Performance.TestRequestStop();
            yield return Frames(3);
            // Rhythm game: same for its stop hint.
            PadInput.Test = false;
            c.Check(Performance.StartMiniGame(Preset("kjerringa-med-staven"), -1, out var error), "performs: " + error);
            yield return Frames(3);
            c.Check(MiniGameHud.HintText == "Right click or Esc: stop", "stop hint: " + MiniGameHud.HintText);
            PadInput.Test = true;
            yield return Frames(2);
            c.Check(MiniGameHud.HintText == "Start or right click: stop", "stop hint once a gamepad is used: " + MiniGameHud.HintText);
            Performance.TestRequestStop();
            yield return Frames(3);
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.hud (T23) ----------

    private static IEnumerator RunHud()
    {
        var c = new Checks(HudName);
        var player = Player.m_localPlayer;
        var camera = Utils.GetMainCamera();
        if (player == null || camera == null || Hud.instance == null)
        {
            SelfTest.Fail(HudName, "no player, camera or HUD");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules(60f);
            x.NoShare();
            PadInput.Test = false;
            var folder = x.SongsFolder("MC_MusicHudTest");
            // Sixteen notes, a four second rest, six notes.
            var notes = new List<Note>();
            AddRun(notes, 0.3f, 16, 0.35f);
            AddRun(notes, 10f, 6, 0.35f);
            File.WriteAllBytes(Path.Combine(folder, "selftest rest.mid"), MidiFrom(notes));
            var list = SongLibrary.ScanMidiFolder(out _);
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            if (!c.Check(list.Count == 1 && Performance.StartMiniGame(list[0], -1, out var error), "performs the test song"))
            {
                c.Report();
                yield break;
            }
            yield return Frames(3);
            var game = Performance.Game;
            c.Check(MiniGameHud.MiniShown && MiniGameHud.CanvasOn && MiniGameHud.TitleText == list[0].Title, "rhythm game HUD up with the song's title");
            var box = MiniGameHud.ScreenBox("mini");
            var at = camera.WorldToScreenPoint(player.GetCenterPoint());
            c.Note($"lanes on screen x {F(box.xMin)}..{F(box.xMax)}, y {F(box.yMin)}..{F(box.yMax)}; character at x {F(at.x)}; screen {Screen.width}x{Screen.height}");
            c.Check(at.z > 0f && box.width > 10f && box.xMin > at.x, "the lanes sit to the right of the character (it stays visible)");
            c.Check(box.xMax <= Screen.width + 1f && box.yMin >= -1f && box.yMax <= Screen.height + 1f, "the lanes are on the screen");
            for (var lane = 0; lane < Chart.Lanes; lane++)
            {
                c.Check(MiniGameHud.LaneCaption(lane) == LaneKeys.Label(lane) && LaneKeys.Label(lane).Length > 0, $"key label under lane {lane + 1}: '{MiniGameHud.LaneCaption(lane)}'");
            }
            c.Check(MiniGameHud.HintText == "Right click or Esc: stop", "stop hint: " + MiniGameHud.HintText);

            // First five notes left, the next eleven hit, then the rest.
            var order = new Dictionary<long, int>();
            var pressed = new HashSet<long>();
            var counts = new List<string>();
            var states = new List<string>();
            var colours = new Dictionary<string, Color32>();
            var judged = new HashSet<string>();
            var hiddenOk = false;
            var hiddenTried = false;
            var accuracyOk = true;
            var began = Time.realtimeSinceStartup;
            while (game != null && Time.realtimeSinceStartup - began < 12.5f && Performance.Mode == PerformanceMode.MiniGame)
            {
                var count = MiniGameHud.CountdownShown;
                if (count.Length > 0 && (counts.Count == 0 || counts[counts.Count - 1] != count))
                {
                    counts.Add(count);
                }
                foreach (var v in game.Visible)
                {
                    var key = NoteKey(v);
                    if (!order.ContainsKey(key))
                    {
                        order[key] = order.Count;
                    }
                    if (order[key] >= 5 && order[key] < 16 && !pressed.Contains(key) && Due(game, game.NoteTime(v.Key, v.Value), -0.005f))
                    {
                        pressed.Add(key);
                        LaneKeys.TestPress[game.Lane(v.Value)] = true;
                    }
                }
                var state = MiniGameHud.MeterStateText ?? "";
                if (states.Count == 0 || states[states.Count - 1] != state)
                {
                    states.Add(state);
                    colours[state] = MiniGameHud.MeterStateColor;
                }
                var shown = MiniGameHud.JudgementShown;
                if (shown.Length > 0)
                {
                    judged.Add(shown);
                }
                if (game.Accuracy >= 0f)
                {
                    accuracyOk &= MiniGameHud.AccuracyText == "Accuracy  " + Mathf.RoundToInt(game.Accuracy * 100f) + "%";
                }
                // Hide the HUD (what Ctrl+F3 switches) for a few frames in the middle.
                if (!hiddenTried && pressed.Count == 6)
                {
                    hiddenTried = true;
                    Hud.instance.m_userHidden = true;
                    yield return Frames(4);
                    hiddenOk = Hud.IsUserHidden() && MiniGameHud.MiniShown && MiniGameHud.CanvasOn;
                    Hud.instance.m_userHidden = false;
                }
                yield return null;
            }
            c.Check(counts.Count == 3 && counts[0] == "3" && counts[1] == "2" && counts[2] == "1", "3-2-1 countdown: " + string.Join(" ", counts.ToArray()));
            c.Note("meter states: " + string.Join(" > ", states.ToArray()));
            c.Check(states.Count >= 4 && states[0] == "Hit a few notes to start" && states[1] == "Hit more notes: the meter waits"
                    && states[2] == "In tune: the meter fills" && states.Contains("Rest: the meter waits"),
                "meter states in order: start, red wait, gold fill, rest");
            Color32 Of(string state) => colours.TryGetValue(state, out var col) ? col : new Color32(0, 0, 0, 0);
            var red = Of("Hit more notes: the meter waits");
            var gold = Of("In tune: the meter fills");
            var grey = Of("Rest: the meter waits");
            c.Check(red.r > 200 && red.g < 150, "\"Hit more notes\" is red");
            c.Check(gold.r == NoteField.MeterFlow.r && gold.g == NoteField.MeterFlow.g && gold.b == NoteField.MeterFlow.b, "\"In tune\" is gold");
            c.Check(grey.r == NoteField.MeterWait.r && grey.g == NoteField.MeterWait.g && grey.b == NoteField.MeterWait.b, "the rest is grey");
            c.Check(judged.Contains("Miss") && (judged.Contains("Perfect") || judged.Contains("Good")), "judgements shown: " + string.Join(", ", new List<string>(judged).ToArray()));
            c.Check(accuracyOk, "the Accuracy text follows the notes hit");
            c.Check(hiddenTried && hiddenOk, "HUD hidden (Ctrl+F3): the rhythm game stays visible");
            Performance.TestRequestStop();
            yield return Frames(3);

            // A song playing by itself: the small line.
            c.Check(Performance.StartAuto(Preset("ravens-jig"), -1, out error), "plays by itself: " + error);
            yield return new WaitForSeconds(1.3f);
            var line = MiniGameHud.AutoText ?? "";
            c.Check(MiniGameHud.AutoShown && line.StartsWith(Preset("ravens-jig").Title + "   0:01 / ", StringComparison.Ordinal) && ClockIn(line, false) > 5,
                "line with title and time: " + line);
            c.Check(MiniGameHud.AutoHintText == "Attack or Block: stop", "its stop hint: " + MiniGameHud.AutoHintText);
            box = MiniGameHud.ScreenBox("auto");
            at = camera.WorldToScreenPoint(player.GetCenterPoint());
            c.Check(box.width > 10f && box.xMin > at.x && box.xMax <= Screen.width + 1f, "it sits to the right of the character too");
            Hud.instance.m_userHidden = true;
            yield return Frames(4);
            c.Check(MiniGameHud.AutoShown && MiniGameHud.CanvasOn, "and stays with the HUD hidden");
            Hud.instance.m_userHidden = false;
            Performance.TestRequestStop();
            yield return Frames(3);
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.swap (T24) ----------

    private static IEnumerator RunSwap()
    {
        var c = new Checks(SwapName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(SwapName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules();
            var lyre = rig.Give(InstrumentContent.ItemName(InstrumentKind.Lyre));
            var flute = rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            c.Check(Performance.StartAuto(Preset("greensleeves"), -1, out var error), "plays on the flute: " + error);
            yield return new WaitForSeconds(1.2f);
            var emitter = Performance.LocalEmitter;
            c.Check(Performance.Mode == PerformanceMode.Auto && InstrumentPose.Weight(player) > 0.9f, "playing, posed");
            var hotbar = ToHotbar(player, lyre);
            if (!c.Check(hotbar != null, "the lyre is on a hotbar key: " + hotbar))
            {
                c.Report();
                yield break;
            }
            yield return Tap(hotbar);
            yield return WaitFor(() => player.GetRightItem() == lyre, 4f);
            c.Check(player.GetRightItem() == lyre && !player.IsItemEquiped(flute), "the hotbar key puts the lyre in hand");
            yield return Frames(2);
            c.Check(Performance.Mode == PerformanceMode.None, "the flute song stops at once");
            c.Check(PlayingFlag(player) == 0 && Performance.PoseWeight <= 0f, "nothing stays posed (flag and pose weight 0)");
            yield return new WaitForSeconds(1f);
            c.Check(Performance.Mode == PerformanceMode.None && InstrumentPose.Weight(player) < 0.01f && !InstrumentPose.InstanceMoved(player),
                "no flute song on the lyre, arms as animated");
            var rms = new float[1];
            yield return Measure(emitter, 0.4f, rms);
            c.Check(rms[0] < 0.0002f, $"no note keeps sounding (rms {F(rms[0])})");
            c.Check(!MiniGameHud.AutoShown, "status line gone");
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.freeze (T25) ----------

    private static int NotesBetween(Note[] notes, float from, float to)
    {
        var n = 0;
        foreach (var note in notes)
        {
            if (note.Time > from && note.Time <= to)
            {
                n++;
            }
        }
        return n;
    }

    // The game's main thread stands still for two seconds (what holding the window's title bar does); sound runs on.
    private static IEnumerator RunFreeze()
    {
        var c = new Checks(FreezeName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(FreezeName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules();
            Performance.Repeat = false;
            rig.Hold(InstrumentKind.Lyre);
            yield return new WaitForSeconds(0.5f);
            c.Check(Performance.StartAuto(Preset("mead-hall-reel"), -1, out var error), "Mead Hall Reel on the lyre: " + error);
            yield return new WaitForSeconds(2f);
            var notes = Performance.TestNotes;
            const float lookAhead = 0.5f;
            var clockBefore = Performance.TestClock;
            var dspBefore = AudioKit.DspNow;
            var playedBefore = Performance.NotesPlayed;
            var skippedBefore = Performance.NotesSkippedLate;
            System.Threading.Thread.Sleep(2000);
            yield return null;
            var clockAfter = Performance.TestClock;
            var dspAfter = AudioKit.DspNow;
            var played = Performance.NotesPlayed - playedBefore;
            var skipped = Performance.NotesSkippedLate - skippedBefore;
            var gone = clockAfter - clockBefore;
            c.Note($"froze {F(gone)} s of song; first frame after: {played} notes queued, {skipped} dropped as too late");
            c.Check(Performance.Mode == PerformanceMode.Auto, "still playing after the freeze");
            c.Check(gone > 1.9f && Mathf.Abs(gone - (float)(dspAfter - dspBefore)) < 0.05f, $"the song carried on in time ({F(gone)} s)");
            // Notes of the frozen stretch: dropped. Queued in that frame: only what is due now.
            var late = NotesBetween(notes, clockBefore + lookAhead, clockAfter - Listeners.LateDrop);
            var due = NotesBetween(notes, clockAfter - Listeners.LateDrop - 0.05f, clockAfter + lookAhead + 0.05f);
            c.Check(late > 3 && Mathf.Abs(skipped - late) <= 2, $"the notes of the frozen stretch are dropped ({skipped} of {late})");
            c.Check(played <= due, $"no burst: only the notes due now are queued ({played}, at most {due})");
            // And it goes on: notes keep coming in time.
            playedBefore = Performance.NotesPlayed;
            clockBefore = Performance.TestClock;
            var real = Time.realtimeSinceStartup;
            yield return Real(1.5f);
            var after = Performance.TestClock - clockBefore;
            c.Check(Performance.Mode == PerformanceMode.Auto && Performance.NotesPlayed > playedBefore
                    && Mathf.Abs(after - (Time.realtimeSinceStartup - real)) < 0.2f, "it plays on in time afterwards");
            var rms = new float[1];
            yield return Measure(Performance.LocalEmitter, 0.5f, rms);
            c.Check(rms[0] > 0.002f, $"and is heard (rms {F(rms[0])})");
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.hostsongs (T26) ----------

    private static IEnumerator RunHostSongs()
    {
        var c = new Checks(HostSongsName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(HostSongsName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules(60f);
            var shared = x.Folder("MC_MusicHostShare");
            var own = x.SongsFolder("MC_MusicHostOwn");
            var one = MidiOf(Preset("greensleeves"));
            var two = MidiOf(Preset("ravens-jig"));
            var three = MidiOf(Preset("drunken-sailor"));
            File.WriteAllBytes(Path.Combine(shared, "selftest one.mid"), one);
            File.WriteAllBytes(Path.Combine(shared, "selftest two.mid"), two);
            File.WriteAllBytes(Path.Combine(own, "selftest own.mid"), MidiOf(Preset("vem-kan-segla")));
            var oneId = SongShare.IdOf(MusicMath.Fnv1a(one));
            var twoId = SongShare.IdOf(MusicMath.Fnv1a(two));
            var threeId = SongShare.IdOf(MusicMath.Fnv1a(three));
            x.Share(shared);
            Performance.LastSongId = null;
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            yield return OpenWindow(rig);
            var items = SongWindow.TestItems();
            c.Note("list: " + string.Join(" / ", items.ToArray()));
            var lastPreset = items.IndexOf("song|" + SongLibrary.Presets[SongLibrary.Presets.Count - 1].Id + "|" + SongLibrary.Presets[SongLibrary.Presets.Count - 1].Title);
            var header = items.IndexOf("header|Server songs");
            var first = items.IndexOf("song|" + oneId + "|selftest one");
            var second = items.IndexOf("song|" + twoId + "|selftest two");
            var midi = items.IndexOf("header|Your MIDI songs");
            var ownAt = items.IndexOf("song|midi:selftest own.mid|selftest own");
            c.Check(SongWindow.IsOpen && lastPreset >= 0 && header == lastPreset + 1, "\"Server songs\" comes right after the built-in songs");
            c.Check(first > header && second > header && first < midi && second < midi && midi == header + 3, "it lists both files, before \"Your MIDI songs\"");
            c.Check(ownAt == midi + 1, "own files come after");
            // Picked: parts and length at once, nothing downloaded.
            var requests = SongShare.RequestsMade;
            c.Check(SongWindow.TestSelect(oneId), "server song picked");
            var details = SongWindow.DetailsText ?? "";
            c.Check(details.Contains("2 parts") && details.Contains(":") && !details.Contains("Downloading") && SongShare.RequestsMade == requests,
                "its parts and length show at once (no download): " + details);
            c.Check(SongWindow.TestPress("Play"), "Play pressed");
            yield return Frames(3);
            c.Check(Performance.Mode == PerformanceMode.Auto && Performance.LastSongId == oneId, "Play works");
            Performance.TestRequestStop();
            yield return Frames(3);
            yield return OpenWindow(rig);
            c.Check(SongWindow.TestSelect(twoId) && SongWindow.TestPress("Perform"), "the other one, Perform");
            yield return Frames(3);
            c.Check(Performance.Mode == PerformanceMode.MiniGame && Performance.LastSongId == twoId, "Perform works");
            Performance.TestRequestStop();
            yield return Frames(3);
            // A file copied in with the window closed (the folder is read again when it opens, at most every 2 s).
            File.WriteAllBytes(Path.Combine(shared, "selftest three.mid"), three);
            yield return Real(2.3f);
            yield return OpenWindow(rig);
            items = SongWindow.TestItems();
            c.Check(SongWindow.IsOpen && items.Contains("song|" + threeId + "|selftest three") && items.Contains("song|" + oneId + "|selftest one"),
                "a file copied in meanwhile is listed when the window opens again");
            Performance.CloseWindow();
            yield return Frames(3);
            // ShareSongs off: no section.
            x.NoShare();
            yield return OpenWindow(rig);
            items = SongWindow.TestItems();
            c.Check(SongWindow.IsOpen && !items.Contains("header|Server songs") && !items.Contains("song|" + oneId + "|selftest one")
                    && items.Contains("header|Your MIDI songs"), "ShareSongs off: the section is gone");
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.playersongs (T27) ----------

    private static IEnumerator RunPlayerSongs()
    {
        var c = new Checks(PlayerSongsName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(PlayerSongsName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            var shared = x.Folder("MC_MusicAllowShare");
            var own = x.SongsFolder("MC_MusicAllowOwn");
            var one = MidiOf(Preset("greensleeves"));
            File.WriteAllBytes(Path.Combine(shared, "selftest one.mid"), one);
            File.WriteAllBytes(Path.Combine(own, "selftest own.mid"), MidiOf(Preset("vem-kan-segla")));
            var oneId = SongShare.IdOf(MusicMath.Fnv1a(one));
            x.Share(shared);
            var strict = TestRules(60f);
            strict.AllowPlayerSongs = false;
            ServerRules.TestRules = strict;
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            yield return OpenWindow(rig);
            var items = SongWindow.TestItems();
            c.Note("list: " + string.Join(" / ", items.ToArray()));
            var midi = items.IndexOf("header|Your MIDI songs");
            c.Check(SongWindow.IsOpen && midi >= 0 && midi == items.Count - 2
                    && items[midi + 1] == "hint|This server allows only the built-in songs and its own songs.",
                "under \"Your MIDI songs\" only the hint");
            c.Check(!items.Exists(i => i.StartsWith("song|midi:", StringComparison.Ordinal)), "no own file listed");
            var ownEntry = SongLibrary.ScanMidiFolder(out _);
            c.Check(ownEntry.Count == 1 && !Performance.StartAuto(ownEntry[0], -1, out var refused) && refused == Performance.PlayerSongsOff,
                "an own file is refused when asked for directly");
            c.Check(SongWindow.TestSelect("preset:greensleeves") && SongWindow.TestPress("Play"), "a built-in song, Play");
            yield return Frames(3);
            c.Check(Performance.Mode == PerformanceMode.Auto, "built-in songs still play");
            Performance.TestRequestStop();
            yield return Frames(3);
            yield return OpenWindow(rig);
            c.Check(SongWindow.TestSelect(oneId) && SongWindow.TestPress("Play"), "a server song, Play");
            yield return Frames(3);
            c.Check(Performance.Mode == PerformanceMode.Auto && Performance.LastSongId == oneId, "server songs still play");
            Performance.TestRequestStop();
            yield return Frames(3);
            // Put back: own files are listed again.
            ServerRules.TestRules = TestRules(60f);
            yield return OpenWindow(rig);
            c.Check(SongWindow.TestItems().Contains("song|midi:selftest own.mid|selftest own"), "allowed again: own files are back");
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.sharefolder (T28) ----------

    private static IEnumerator RunShareFolder()
    {
        var c = new Checks(ShareFolderName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(ShareFolderName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules(60f);
            var shared = x.Folder("MC_MusicFolderTest");
            x.SongsFolder("MC_MusicFolderOwn");
            var tune = MidiOf(Preset("greensleeves"));
            var text = System.Text.Encoding.ASCII.GetBytes("selftest: this file is text, not MIDI. " + new string('x', 200));
            var big = PadMidi(MidiOf(Preset("ravens-jig")), MidiReader.MaxFileBytes + 16);
            File.WriteAllBytes(Path.Combine(shared, "selftest a.mid"), tune);
            File.WriteAllBytes(Path.Combine(shared, "selftest b.mid"), tune);
            File.WriteAllBytes(Path.Combine(shared, "selftest text.mid"), text);
            File.WriteAllBytes(Path.Combine(shared, "selftest big.mid"), big);
            c.Check(big.Length > MidiReader.MaxFileBytes && MidiReader.MaxFileBytes == 2 * 1024 * 1024, $"test file larger than 2 MB ({big.Length} bytes)");
            var mark = LogTap.ProblemMark;
            x.Share(shared);
            SongShare.TestScan();
            SongShare.TestScan();
            var listed = SongShare.DescribeShared();
            c.Note("shared: " + listed);
            c.Check(SongShare.SharedCount == 2, $"two songs shared: the twin once, the text file, not the big one ({SongShare.SharedCount})");
            c.Check(!listed.Contains("selftest big"), "the file larger than 2 MB is not listed");
            c.Check(LogTap.CountProblems(mark, "\"selftest big.mid\" is larger than 2 MB") == 1, "one warning for it (also after reading the folder twice)");
            c.Check(listed.Contains("selftest a=") != listed.Contains("selftest b="), "the same file under two names is listed once");
            // The text file: listed, says why it cannot play when picked.
            var textId = SongShare.IdOf(MusicMath.Fnv1a(text));
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            yield return OpenWindow(rig);
            c.Check(SongWindow.IsOpen && SongWindow.TestItems().Contains("song|" + textId + "|selftest text"), "the text file renamed .mid is listed");
            c.Check(SongWindow.TestSelect(textId), "picked");
            yield return Frames(2);
            var details = SongWindow.DetailsText ?? "";
            c.Check(details.StartsWith("selftest text: The file ", StringComparison.Ordinal) && !SongWindow.TestPress("Play") && SongWindow.IsOpen,
                "it shows why it cannot play: " + details);
            Performance.CloseWindow();
            yield return Frames(3);
            // Folder deleted, ShareSongs off and on: made again.
            Directory.Delete(shared, true);
            var info = LogTap.InfoMark;
            SongShare.TestShare = false;
            SongShare.ServerSettingsChanged();
            c.Check(!Directory.Exists(shared), "folder deleted, sharing off: not made");
            SongShare.TestShare = true;
            SongShare.ServerSettingsChanged();
            c.Check(Directory.Exists(shared), "ShareSongs on again: the folder is made again");
            c.Check(LogTap.InfoSince(info, "Made the server songs folder " + shared), "log line \"Made the server songs folder\"");
            c.Check(LogTap.ProblemsSince(mark).Count == 1, "no other warning");
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.freeplay.piano (T30) ----------

    private static IEnumerator RunPiano()
    {
        var c = new Checks(PianoName);
        var player = Player.m_localPlayer;
        var camera = Utils.GetMainCamera();
        if (player == null || camera == null)
        {
            SelfTest.Fail(PianoName, "no player or camera");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules();
            x.NoShare();
            PadInput.Test = false;
            FreePlayKeys.ClearTest();
            Performance.LastSongId = null;
            var torch = rig.Give("Torch");
            var flute = rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            yield return OpenWindow(rig);
            c.Check(SongWindow.IsOpen && SongWindow.TestItems()[0] == "song|" + SongLibrary.FreePlayId + "|Free play" && SongWindow.SelectedId == SongLibrary.FreePlayId,
                "\"Free play\" is the first entry, selected");
            c.Check(SongWindow.TestPress("Play"), "Play");
            yield return Frames(4);
            if (!c.Check(Performance.Mode == PerformanceMode.FreePlay && MiniGameHud.FreeShown, "free play runs"))
            {
                c.Report();
                yield break;
            }
            // The piano.
            c.Check(MiniGameHud.FreeBoxCount(false) == 17 && MiniGameHud.FreeBoxCount(true) == 12, "17 white keys and 12 black keys");
            c.Check(MiniGameHud.FreeTitle == "Free play: Wooden Flute", "title: " + MiniGameHud.FreeTitle);
            var labels = true;
            var names = true;
            var between = true;
            for (var key = 0; key < FreePlayMap.KeyCount; key++)
            {
                labels &= MiniGameHud.FreeKeyLabel(key) == FreePlayKeys.Label(key) && FreePlayKeys.Label(key).Length > 0;
                if (FreePlayMap.IsBlack(key))
                {
                    names &= MiniGameHud.FreeNoteLabel(key) == null;
                    // A black key sits between the white keys before and after it.
                    var mid = MiniGameHud.FreeKeyCentreX(key);
                    between &= key > 0 && key + 1 < FreePlayMap.KeyCount && mid > MiniGameHud.FreeKeyCentreX(key - 1) && mid < MiniGameHud.FreeKeyCentreX(key + 1);
                }
                else
                {
                    names &= MiniGameHud.FreeNoteLabel(key) == MusicMath.PitchName(60 + key);
                }
            }
            c.Check(labels, "each key shows its keyboard key");
            c.Check(names && MiniGameHud.FreeNoteLabel(0) == "C4" && MiniGameHud.FreeNoteLabel(28) == "E6", "white keys show the note, C4 to E6");
            c.Check(between, "black keys between the white ones, like a piano");
            var box = MiniGameHud.ScreenBox("free");
            var at = camera.WorldToScreenPoint(player.GetCenterPoint());
            c.Note($"piano on screen x {F(box.xMin)}..{F(box.xMax)}; character at x {F(at.x)}; screen {Screen.width}x{Screen.height}");
            c.Check(at.z > 0f && box.width > 10f && box.xMin > at.x && box.xMax <= Screen.width + 1f && box.yMin >= -1f, "the piano is right of the character, on the screen");

            // No walking, no jumping, no hotbar.
            var controller = x.ControllerObject;
            c.Check(KeyCapture.Active && controller != null && !controller.TakeInput(false), "game keys held back");
            var moved = new float[2];
            yield return Push(rig, player, 0.5f, moved, jump: true);
            c.Check(moved[0] < 0.05f && moved[1] > 0.5f, $"no walking, no jumping ({F(moved[0])} m)");
            var hotbar = ToHotbar(player, torch);
            c.Check(hotbar != null, "torch on a hotbar key: " + hotbar);
            if (hotbar != null)
            {
                yield return Tap(hotbar);
                yield return new WaitForSeconds(0.5f);
            }
            c.Check(player.GetRightItem() == flute && !player.IsItemEquiped(torch) && Performance.Mode == PerformanceMode.FreePlay, "the hotbar key equips nothing");

            // Q held: C5 while the key is down, at most 8 seconds.
            var emitter = Performance.LocalEmitter;
            FreePlayKeys.TestDown[12] = true;
            yield return Frames(3);
            c.Check(Performance.FreeFlutePitch == 72 && MiniGameHud.FreeKeyLit(12) && MiniGameHud.FreeLitCount == 1, "Q held: C5, its piano key lit");
            // Level one second in, and again near seven seconds: a steady note, not one that fades or swells.
            var early = new float[1];
            yield return Real(0.8f);
            yield return Measure(emitter, 0.6f, early);
            yield return Real(5.4f);
            var rms = new float[1];
            yield return Measure(emitter, 0.6f, rms);
            c.Check(rms[0] > 0.001f && MiniGameHud.FreeKeyLit(12), $"still sounding after 7 seconds (rms {F(rms[0])})");
            c.Check(early[0] > 0.001f && rms[0] > early[0] * 0.6f && rms[0] < early[0] * 1.6f,
                $"a steady note: as loud near 7 seconds as after 1 second (rms {F(early[0])} then {F(rms[0])})");
            yield return Real(1.2f);
            yield return Measure(emitter, 0.4f, rms);
            c.Check(rms[0] < 0.0002f, $"silent after 8 seconds though the key is down (rms {F(rms[0])})");
            FreePlayKeys.TestUp[12] = true;
            yield return Frames(3);
            c.Check(Performance.FreeFlutePitch < 0 && MiniGameHud.FreeLitCount == 0, "key up: nothing held, nothing lit");

            // Fast taps: each one sounds, none drones.
            var ons = Performance.NoteOnsSent;
            if (emitter != null)
            {
                emitter.ResetMeasure();
            }
            for (var i = 0; i < 20; i++)
            {
                var key = i % 2 == 0 ? 14 : 16;
                FreePlayKeys.TestDown[key] = true;
                yield return null;
                yield return null;
                FreePlayKeys.TestUp[key] = true;
                yield return null;
            }
            yield return Frames(2);
            c.Check(Performance.NoteOnsSent == ons + 20, $"20 fast taps: 20 notes ({Performance.NoteOnsSent - ons})");
            c.Check(emitter != null && emitter.Rms > 0.001f, "the taps sound");
            c.Check(Performance.FreeFlutePitch < 0 && MiniGameHud.FreeLitCount == 0, "nothing left held");
            yield return Real(0.5f);
            yield return Measure(emitter, 0.4f, rms);
            c.Check(rms[0] < 0.0002f, $"none keeps droning (rms {F(rms[0])})");
            Performance.TestRequestStop();
            yield return Frames(3);
            c.Report();
        }
        finally
        {
            FreePlayKeys.ClearTest();
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.freeplay.more (T31, T32, T34) ----------

    private static readonly KeyCode[] PianoKeys =
    {
        // Lower octave: white Z X C V B N M, black S D, G H J.
        KeyCode.Z, KeyCode.S, KeyCode.X, KeyCode.D, KeyCode.C, KeyCode.V, KeyCode.G, KeyCode.B, KeyCode.H, KeyCode.N, KeyCode.J, KeyCode.M,
        // Upper octave: white Q W E R T Y U I O P, black 2 3, 5 6 7, 9 0.
        KeyCode.Q, KeyCode.Alpha2, KeyCode.W, KeyCode.Alpha3, KeyCode.E, KeyCode.R, KeyCode.Alpha5, KeyCode.T, KeyCode.Alpha6, KeyCode.Y,
        KeyCode.Alpha7, KeyCode.U, KeyCode.I, KeyCode.Alpha9, KeyCode.O, KeyCode.Alpha0, KeyCode.P,
    };

    private static IEnumerator RunFreeMore()
    {
        var c = new Checks(FreeMoreName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(FreeMoreName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules();
            x.NoShare();
            PadInput.Test = false;
            FreePlayKeys.ClearTest();
            // The whole map: Z X C V B N M = C D E F G A B, S D G H J the black keys, Q to P and the numbers an octave up.
            var map = PianoKeys.Length == FreePlayMap.KeyCount;
            var whites = "";
            var blacks = "";
            for (var key = 0; map && key < FreePlayMap.KeyCount; key++)
            {
                map &= FreePlayKeys.Key(key) == PianoKeys[key] && FreePlayMap.Pitch(InstrumentKind.Flute, key, false) == 60 + key
                       && FreePlayMap.Pitch(InstrumentKind.Flute, key, true) == 72 + key && FreePlayMap.Pitch(InstrumentKind.Lyre, key, false) == 48 + key;
                if (key < 12)
                {
                    if (FreePlayMap.IsBlack(key))
                    {
                        blacks += MusicMath.PitchName(60 + key) + " ";
                    }
                    else
                    {
                        whites += MusicMath.PitchName(60 + key) + " ";
                    }
                }
            }
            c.Check(map, "every key plays the next semitone: flute from C4, lyre from C3, Space an octave up");
            c.Check(whites == "C4 D4 E4 F4 G4 A4 B4 " && blacks == "C#4 D#4 F#4 G#4 A#4 ", $"Z X C V B N M = {whites}; S D G H J = {blacks}");
            c.Check(FreePlayMap.Pitch(InstrumentKind.Flute, 28, false) == 88 && MusicMath.PitchName(88) == "E6" && FreePlayMap.Pitch(InstrumentKind.Lyre, 28, false) == 76
                    && MusicMath.PitchName(48) == "C3" && MusicMath.PitchName(76) == "E5", "upper octave up to E: flute E6, lyre C3 to E5");
            // Key names by the player's keyboard layout (what the game calls each key).
            var layout = true;
            for (var key = 0; key < FreePlayMap.KeyCount; key++)
            {
                string name = null;
                try
                {
                    name = ZInput.KeyCodeToDisplayName(FreePlayKeys.Key(key));
                }
                catch (Exception)
                {
                    // No name from the game: the mod falls back to the key's own name.
                }
                if (!string.IsNullOrEmpty(name) && !name.StartsWith("$", StringComparison.Ordinal))
                {
                    layout &= FreePlayKeys.Label(key) == name.ToUpperInvariant();
                }
            }
            c.Check(layout, "key labels are the game's names of the keys at those places (keyboard layout)");

            // Lyre: strings ring on, several at once make a chord.
            rig.Hold(InstrumentKind.Lyre);
            yield return new WaitForSeconds(0.4f);
            yield return OpenWindow(rig);
            var wantedInfo = "Play the keyboard like a piano: " + FreePlayKeys.Label(0) + " to " + FreePlayKeys.Label(11) + " and " + FreePlayKeys.Label(12)
                             + " to " + FreePlayKeys.Label(28) + ", black keys on the row above; Space: octave up";
            c.Check(SongWindow.IsOpen && SongWindow.TestRowInfo(SongLibrary.FreePlayId) == wantedInfo, "the window's Free play line names those keys: " + SongWindow.TestRowInfo(SongLibrary.FreePlayId));
            c.Check(SongWindow.TestSelect(SongLibrary.FreePlayId) && SongWindow.TestPress("Play"), "free play on the lyre");
            yield return Frames(4);
            c.Check(Performance.Mode == PerformanceMode.FreePlay && MiniGameHud.FreeTitle == "Free play: Silver Lyre", "title: " + MiniGameHud.FreeTitle);
            c.Check(MiniGameHud.FreeNoteLabel(0) == "C3" && MiniGameHud.FreeNoteLabel(28) == "E5", "piano shows C3 to E5");
            var emitter = Performance.LocalEmitter;
            var ons = Performance.NoteOnsSent;
            var offs = Performance.NoteOffsSent;
            FreePlayKeys.TestDown[0] = true;
            FreePlayKeys.TestDown[4] = true;
            FreePlayKeys.TestDown[7] = true;
            yield return Frames(3);
            c.Check(Performance.NoteOnsSent == ons + 3 && MiniGameHud.FreeLitCount == 3, "three keys at once: a chord of three strings, three keys lit");
            var sent = new HashSet<byte> { Performance.SentBack(1).Key, Performance.SentBack(2).Key, Performance.SentBack(3).Key };
            c.Check(sent.Contains(48) && sent.Contains(52) && sent.Contains(55), "C3, E3 and G3");
            FreePlayKeys.TestUp[0] = true;
            FreePlayKeys.TestUp[4] = true;
            FreePlayKeys.TestUp[7] = true;
            yield return Frames(3);
            c.Check(Performance.NoteOffsSent == offs && MiniGameHud.FreeLitCount == 0, "keys up: no note off (the strings ring by themselves)");
            yield return Real(0.8f);
            var rms = new float[1];
            yield return Measure(emitter, 0.4f, rms);
            c.Check(rms[0] > 0.0005f, $"the strings ring on a second after the keys are up (rms {F(rms[0])})");
            // Space: everything an octave up, shown on the piano.
            FreePlayKeys.TestOctave = true;
            yield return Frames(3);
            c.Check(MiniGameHud.FreeNoteLabel(0) == "C4" && MiniGameHud.FreeNoteLabel(28) == "E6" && MiniGameHud.FreeTitle == "Free play: Silver Lyre   (octave up)",
                "Space held: note names and title show the octave up: " + MiniGameHud.FreeTitle);
            FreePlayKeys.TestDown[0] = true;
            yield return Frames(3);
            c.Check(Performance.SentBack(1).Key == 60, "and the keys play an octave higher");
            FreePlayKeys.TestUp[0] = true;
            FreePlayKeys.TestOctave = false;
            yield return Frames(3);
            c.Check(MiniGameHud.FreeNoteLabel(0) == "C3" && MiniGameHud.FreeTitle == "Free play: Silver Lyre", "Space let go: back");
            Performance.TestRequestStop();
            yield return Frames(3);
            player.UnequipItem(player.GetRightItem(), false);
            yield return Frames(2);

            // Flute: a note held while Space is let go keeps its pitch.
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            c.Check(Performance.StartFreePlay(out var error), "free play on the flute: " + error);
            yield return Frames(3);
            FreePlayKeys.TestOctave = true;
            FreePlayKeys.TestDown[12] = true;
            yield return Frames(3);
            c.Check(Performance.FreeFlutePitch == 84, "Space + Q: C6");
            FreePlayKeys.TestOctave = false;
            offs = Performance.NoteOffsSent;
            yield return Frames(4);
            c.Check(Performance.FreeFlutePitch == 84 && Performance.NoteOffsSent == offs && MiniGameHud.FreeKeyLit(12), "Space let go: the held note keeps its pitch");
            FreePlayKeys.TestUp[12] = true;
            yield return Frames(3);
            c.Check(Performance.FreeFlutePitch < 0 && Performance.SentBack(1).Key == 84 && Performance.SentBack(1).Value == 0, "key up ends that note (note off for C6)");
            Performance.TestRequestStop();
            yield return Frames(3);
            player.UnequipItem(player.GetRightItem(), false);
            yield return Frames(2);

            // Tambourine: Z X C V only.
            rig.Hold(InstrumentKind.Tambourine);
            yield return new WaitForSeconds(0.4f);
            c.Check(Performance.StartFreePlay(out error), "free play on the tambourine: " + error);
            yield return Frames(4);
            c.Check(MiniGameHud.FreeTitle == "Free play: Tambourine" && MiniGameHud.FreeHintText == "Right click or Esc: stop", "title and hint (no octave): " + MiniGameHud.FreeHintText);
            var four = new[] { 0, 2, 4, 5 };
            var hitNames = new[] { "Thump", "Hit", "Jingle", "Shake" };
            var grey = true;
            for (var key = 0; key < FreePlayMap.KeyCount; key++)
            {
                var used = Array.IndexOf(four, key) >= 0;
                grey &= MiniGameHud.FreeKeyGreyed(key) == !used && (used || MiniGameHud.FreeKeyLabel(key) == "");
            }
            c.Check(grey, "every key but Z X C V is greyed and shows no key");
            c.Check(FreePlayKeys.Key(0) == KeyCode.Z && FreePlayKeys.Key(2) == KeyCode.X && FreePlayKeys.Key(4) == KeyCode.C && FreePlayKeys.Key(5) == KeyCode.V,
                "the four keys are Z X C V");
            emitter = Performance.LocalEmitter;
            for (var i = 0; i < four.Length; i++)
            {
                ons = Performance.NoteOnsSent;
                yield return Measure(emitter, 0.05f, rms);
                if (emitter != null)
                {
                    emitter.ResetMeasure();
                }
                FreePlayKeys.TestDown[four[i]] = true;
                yield return Frames(3);
                c.Check(MiniGameHud.FreeKeyLit(four[i]) && MiniGameHud.FreeLitCount == 1 && Performance.NoteOnsSent == ons + 1
                        && Performance.SentBack(1).Key == i && MiniGameHud.FreeNoteLabel(four[i]) == hitNames[i],
                    $"{FreePlayKeys.Key(four[i])} lights up and plays the {hitNames[i]}");
                yield return Real(0.15f);
                c.Check(emitter != null && emitter.Rms > 0.0005f, $"the {hitNames[i]} sounds (rms {F(emitter != null ? emitter.Rms : 0f)})");
                FreePlayKeys.TestUp[four[i]] = true;
                yield return Frames(3);
            }
            ons = Performance.NoteOnsSent;
            foreach (var key in new[] { 1, 3, 6, 12, 20, 28 })
            {
                FreePlayKeys.TestDown[key] = true;
            }
            yield return Frames(3);
            c.Check(Performance.NoteOnsSent == ons && MiniGameHud.FreeLitCount == 0, "the other keys do nothing");
            foreach (var key in new[] { 1, 3, 6, 12, 20, 28 })
            {
                FreePlayKeys.TestUp[key] = true;
            }
            yield return Frames(2);
            Performance.TestRequestStop();
            yield return Frames(3);
            c.Report();
        }
        finally
        {
            FreePlayKeys.ClearTest();
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.freeplay.keys (T33) ----------

    private static IEnumerator RunFreeKeys()
    {
        var c = new Checks(FreeKeysName);
        var player = Player.m_localPlayer;
        if (player == null || Menu.instance == null)
        {
            SelfTest.Fail(FreeKeysName, "no player or menu");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            ServerRules.TestRules = TestRules();
            x.Console(true);
            PadInput.Test = false;
            FreePlayKeys.ClearTest();
            var torch = rig.Give("Torch");
            var flute = rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            c.Check(Performance.StartFreePlay(out var error), "free play: " + error);
            yield return Frames(3);
            // The game reads the keys itself from here on.
            x.Controller(true);
            var at = player.transform.position;
            var map = Minimap.instance != null ? Minimap.instance.m_mode : Minimap.MapMode.None;
            var hotbar = ToHotbar(player, torch);
            var opened = "";
            foreach (var name in new[] { "Inventory", "Map", "Use", "Hide", hotbar, "Jump", "Chat", "Console", "Sit" })
            {
                if (name == null)
                {
                    continue;
                }
                Press(name);
                for (var i = 0; i < 5; i++)
                {
                    if (InventoryGui.IsVisible())
                    {
                        opened += " inventory(" + name + ")";
                    }
                    if (Minimap.instance != null && Minimap.instance.m_mode != map)
                    {
                        opened += " map(" + name + ")";
                    }
                    if (ChatTyping)
                    {
                        opened += " chat(" + name + ")";
                    }
                    if (global::Console.IsVisible())
                    {
                        opened += " console(" + name + ")";
                    }
                    if (player.m_body != null && player.m_body.linearVelocity.y > 3f)
                    {
                        opened += " jump(" + name + ")";
                    }
                    yield return null;
                }
                Let(name);
                yield return null;
            }
            c.Check(hotbar != null, "torch on a hotbar key: " + hotbar);
            c.Check(opened.Length == 0, "Tab, M, E, R, a number key, Space, Enter, F5: nothing opens, no jump" + (opened.Length > 0 ? " -" + opened : ""));
            c.Check(player.GetRightItem() == flute && player.m_hiddenRightItem == null && !player.IsItemEquiped(torch) && !player.InEmote(),
                "the flute stays in hand (R and the number key do nothing)");
            c.Check(Performance.Mode == PerformanceMode.FreePlay, "still free playing");

            // Q, Space, E and W held (also the game's auto-run, jump, use and forward), then a right click.
            foreach (var name in new[] { "AutoRun", "Jump", "Use", "Forward" })
            {
                Press(name);
            }
            yield return new WaitForSeconds(0.4f);
            c.Check(Flat(player.transform.position, at) < 0.05f && !player.m_autoRun, "held while playing: no step, no auto-run");
            Performance.TestRightClick = true;
            yield return Frames(3);
            c.Check(Performance.Mode == PerformanceMode.None && !MiniGameHud.FreeShown, "right click stops free play");
            var still = !ZInput.GetButton("AutoRun") && !ZInput.GetButton("Jump") && !ZInput.GetButton("Use") && !ZInput.GetButton("Forward");
            c.Check(still, "the keys still held are forgotten by the game");
            var jumped = false;
            var began = Time.time;
            while (Time.time - began < 0.6f)
            {
                jumped |= player.m_body != null && player.m_body.linearVelocity.y > 3f;
                yield return null;
            }
            c.Check(Flat(player.transform.position, at) < 0.1f && !player.m_autoRun && !jumped, "the character neither runs off nor jumps");
            // Pressed again: they work.
            foreach (var name in new[] { "AutoRun", "Jump", "Use", "Forward" })
            {
                Let(name);
            }
            yield return Frames(3);
            c.Check(!KeyCapture.Active && x.ControllerObject.TakeInput(false), "the game takes keys again");
            Press("Forward");
            yield return new WaitForSeconds(0.6f);
            Let("Forward");
            c.Check(Flat(player.transform.position, at) > 0.3f, $"pressed again, Forward walks ({F(Flat(player.transform.position, at))} m)");
            yield return Tap("Inventory");
            yield return Frames(3);
            c.Check(InventoryGui.IsVisible(), "and the inventory key opens the inventory");
            InventoryGui.instance.Hide();
            x.Controller(false);
            rig.TakeControls();
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);

            // Esc: menu, and free play is over.
            c.Check(Performance.StartFreePlay(out error), "free play again: " + error);
            yield return Frames(3);
            yield return Tap("JoyMenu");
            yield return WaitReal(() => MenuShown, 1f);
            yield return Frames(3);
            c.Check(MenuShown && Performance.Mode == PerformanceMode.None, "Esc opens the menu and stops free play");
            if (MenuShown)
            {
                Menu.instance.Hide();
            }
            yield return Real(0.3f);
            c.Report();
        }
        finally
        {
            FreePlayKeys.ClearTest();
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.freeplay.stops (T35) ----------

    private static IEnumerator RunFreeStops()
    {
        var c = new Checks(FreeStopsName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(FreeStopsName, "no player");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            // The Encore bar as low as the rules allow: free play runs twice that long here.
            ServerRules.TestRules = TestRules(MusicRules.SuccessSecondsMin);
            PadInput.Test = false;
            FreePlayKeys.ClearTest();
            var seman = player.GetSEMan();
            seman.RemoveStatusEffect(InstrumentContent.EffectHash, true);
            Seen.Clear();
            var flute = rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            c.Check(Performance.StartFreePlay(out var error), "free play: " + error);
            yield return Frames(3);
            var meter = false;
            var encore = false;
            var effect = false;
            var ons = Performance.NoteOnsSent;
            var began = Time.realtimeSinceStartup;
            var step = 0;
            while (Time.realtimeSinceStartup - began < MusicRules.SuccessSecondsMin * 2f + 1f && Performance.Mode == PerformanceMode.FreePlay)
            {
                step++;
                if (step % 20 == 1)
                {
                    FreePlayKeys.TestDown[12 + (step / 20) % 8] = true;
                }
                else if (step % 20 == 12)
                {
                    FreePlayKeys.TestUp[12 + (step / 20) % 8] = true;
                }
                meter |= MiniGameHud.MiniShown || Performance.Game != null;
                encore |= (Performance.LastFlags & BatchFlags.Encore) != 0 || MiniGameHud.EncoreShown.Length > 0;
                effect |= seman.HaveStatusEffect(InstrumentContent.EffectHash);
                yield return null;
            }
            FreePlayKeys.ClearTest();
            c.Check(Performance.Mode == PerformanceMode.FreePlay && Performance.NoteOnsSent > ons + 10, $"notes played for {F(Time.realtimeSinceStartup - began)} s");
            c.Check(!meter && !encore, "no meter, no Encore");
            c.Check(!effect && Seen.Count(MessageHud.MessageType.TopLeft, "warms") == 0, "no Music effect");

            // Put away, swim, hit: each stops it at once, with a note held, and nothing keeps sounding.
            var rms = new float[1];
            for (var way = 0; way < 3; way++)
            {
                if (Performance.Mode != PerformanceMode.FreePlay)
                {
                    if (player.GetRightItem() != flute)
                    {
                        player.EquipItem(flute, false);
                        yield return new WaitForSeconds(0.4f);
                    }
                    c.Check(Performance.StartFreePlay(out error), "free play again: " + error);
                    yield return Frames(3);
                }
                var emitter = Performance.LocalEmitter;
                FreePlayKeys.TestDown[12] = true;
                yield return new WaitForSeconds(0.4f);
                c.Check(Performance.FreeFlutePitch == 72, "a note held");
                string what;
                if (way == 0)
                {
                    what = "putting the flute away";
                    player.UnequipItem(flute, false);
                }
                else if (way == 1)
                {
                    what = "swimming";
                    player.m_swimTimer = 0f;
                }
                else
                {
                    what = "a hit";
                    yield return new WaitForSeconds(1.2f);
                    player.Damage(Hit(player, HitData.HitType.EnemyHit, blunt: 6f));
                }
                yield return Frames(3);
                player.m_swimTimer = 999f;
                FreePlayKeys.ClearTest();
                c.Check(Performance.Mode == PerformanceMode.None && !MiniGameHud.FreeShown && PlayingFlag(player) == 0, what + " stops free play at once");
                yield return Real(0.5f);
                yield return Measure(emitter, 0.4f, rms);
                c.Check(rms[0] < 0.0002f, $"{what}: nothing keeps sounding (rms {F(rms[0])})");
                yield return Frames(2);
                c.Check(!KeyCapture.Active, what + ": keys given back");
            }
            c.Check(Seen.Count(MessageHud.MessageType.TopLeft, "The hit cut your song short.") == 1, "the hit says why: " + Seen.Tail());
            c.Report();
        }
        finally
        {
            FreePlayKeys.ClearTest();
            x.Restore();
            rig.Restore();
        }
    }
}
#endif
