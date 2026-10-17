#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Exploration.MusicInstrumentsMod;

// Debug build only. Self tests that need a real log out, a real restart with the mod off, or a real death and the
// game's own respawn (same class as SelfTests.cs), and the registration of every test added with them (files
// SelfTests.Input.cs, SelfTests.Real.cs, SelfTests.Mods.cs, SelfTests.Multi2.cs). One small test per thing:
//   music.relog.playing    T19  song playing (and another player heard), real log out to the main menu: no sound
//                               source, window or HUD of the mod left there; world loaded again: flute still there,
//                               window, Play and sound work
//   music.disabled.items   T18  second game, every MC mod Enabled = false from the start: the three instruments put
//                               in the inventory in the first game are still there, no instrument recipe, status off
//   music.death.auto       T37  killed (a real Greyling, health 1) while a song plays by itself
//   music.death.perform    T37  killed while performing
//   music.death.window     T37  killed with the song window open
//                               each: all stops on the death screen, keys free, nothing left on the new body, the
//                               instrument lies in the tombstone with the other items, taken back it plays
// Relog test registered in OnActivated; disabled-start test (and the multiplayer test of a server without the mod)
// in BindConfig: the mod is never active there. Items left in the inventory over a log out or a restart carry a mark
// (custom data), so the part after finds and removes them.
internal static partial class SelfTests
{
    private const string RelogPlayName = "music.relog.playing";
    private const string DisabledItemsName = "music.disabled.items";
    private const string DeathAutoName = "music.death.auto";
    private const string DeathPerformName = "music.death.perform";
    private const string DeathWindowName = "music.death.window";

    private const string TestMarkKey = ModInfo.Guid + ".SelfTest";

    // In-world tests of the new files, in running order. Deaths last: each leaves a new player object.
    private static readonly KeyValuePair<string, Func<IEnumerator>>[] AutoTests =
    {
        new KeyValuePair<string, Func<IEnumerator>>(DropRestName, RunDropRest),
        new KeyValuePair<string, Func<IEnumerator>>(RhythmDrawnName, RunRhythmDrawn),
        new KeyValuePair<string, Func<IEnumerator>>(HudScaleName, RunHudScale),
        new KeyValuePair<string, Func<IEnumerator>>(VolumeHeardName, RunVolumeHeard),
        new KeyValuePair<string, Func<IEnumerator>>(ListenRestName, RunListenRest),
        new KeyValuePair<string, Func<IEnumerator>>(ChairName, RunChair),
        new KeyValuePair<string, Func<IEnumerator>>(InputPadWindowName, RunInputPadWindow),
        new KeyValuePair<string, Func<IEnumerator>>(InputPadOpenName, RunInputPadOpen),
        new KeyValuePair<string, Func<IEnumerator>>(InputKeysHeldName, RunInputKeysHeld),
        new KeyValuePair<string, Func<IEnumerator>>(InputPadHudName, RunInputPadHud),
        new KeyValuePair<string, Func<IEnumerator>>(InputPadFreeName, RunInputPadFree),
        new KeyValuePair<string, Func<IEnumerator>>(InputHideHudName, RunInputHideHud),
        new KeyValuePair<string, Func<IEnumerator>>(XSpyglassName, RunXSpyglass),
        new KeyValuePair<string, Func<IEnumerator>>(XSearchName, RunXSearch),
        new KeyValuePair<string, Func<IEnumerator>>(XSortName, RunXSort),
        new KeyValuePair<string, Func<IEnumerator>>(XDualName, RunXDual),
        new KeyValuePair<string, Func<IEnumerator>>(DeathAutoName, () => RunDeath(DeathAutoName, 0)),
        new KeyValuePair<string, Func<IEnumerator>>(DeathPerformName, () => RunDeath(DeathPerformName, 1)),
        new KeyValuePair<string, Func<IEnumerator>>(DeathWindowName, () => RunDeath(DeathWindowName, 2)),
    };

    // OnActivated (SelfTests.Register), after every other test of the mod.
    private static void RegisterAuto()
    {
        foreach (var t in AutoTests)
        {
            SelfTest.Register(t.Key, t.Value);
        }
        // music.cleanlog look back at the whole run: registered again = moved behind the new tests.
        SelfTest.Register(CleanLogName, RunCleanLog);
        SelfTest.RegisterRelog(RelogPlayName, RelogPlayBefore, RelogPlayMenu, RelogPlayAfter);
        SelfTest.RegisterMultiplayer(MpLeaveOffName, SelfTest.Modded, RunMpLeaveOff);
        SelfTest.RegisterServerStep(SrvOffFor, ServerOffFor);
        // music.mp.server ask the server for every warning of the run: same, kept last.
        SelfTest.RegisterMultiplayer(MpServerName, SelfTest.Modded, RunMpServer);
    }

    // OnDeactivated (SelfTests.Unregister).
    private static void UnregisterAuto()
    {
        foreach (var t in AutoTests)
        {
            SelfTest.Unregister(t.Key);
        }
        SelfTest.UnregisterRelog(RelogPlayName);
        SelfTest.UnregisterMultiplayer(MpLeaveOffName);
        SelfTest.UnregisterServerStep(SrvOffFor);
    }

    // BindConfig (SelfTests.RegisterEarly: every start, also with the mod off). Never unregistered: in the second
    // game of a disabled-start run, and on a server without the mod, OnActivated never runs.
    private static void RegisterStaged()
    {
        // Me listen to own log lines from the very start (not only from first activation): leave tests count
        // warnings "since the game started".
        LogTap.Install();
        SelfTest.RegisterDisabledStart(DisabledItemsName, DisabledItemsLeave, DisabledItemsCheck);
        SelfTest.RegisterMultiplayer(MpVanillaLeaveName, VanillaServerScenario, RunMpVanillaLeave);
    }

    // ---------- marked items ----------

    private static ItemDrop.ItemData GiveMarked(Player player, InstrumentKind kind, string mark)
    {
        var item = player.GetInventory().AddItem(InstrumentContent.ItemName(kind), 1, 1, 0, 0L, "", false);
        if (item != null)
        {
            item.m_customData[TestMarkKey] = mark;
        }
        return item;
    }

    private static List<ItemDrop.ItemData> Marked(Player player, string mark)
    {
        var list = new List<ItemDrop.ItemData>();
        foreach (var item in player.GetInventory().GetAllItems())
        {
            if (item != null && item.m_customData.TryGetValue(TestMarkKey, out var m) && m == mark)
            {
                list.Add(item);
            }
        }
        return list;
    }

    private static void RemoveMarked(Player player, string mark)
    {
        foreach (var item in Marked(player, mark))
        {
            if (player.IsItemEquiped(item))
            {
                player.UnequipItem(item, false);
            }
            player.GetInventory().RemoveItem(item);
        }
    }

    // ---------- music.relog.playing (T19) ----------

    private static bool _relogStarted;
    private static bool _relogRepeat;
    private static string _relogLastSong;
    private static int _relogLastPart = -1;
    // Own warnings and errors so far when the part before began: part after read what came since.
    private static int _relogLogMark;

    // Unexpected warnings and errors of this mod's own log source since a mark (test lines and clean-up notes left out).
    private static List<string> OwnProblemsSince(int mark)
    {
        var bad = new List<string>();
        foreach (var line in LogTap.ProblemsSince(mark))
        {
            if (!LogTap.Expected(line))
            {
                bad.Add(line.Length > 200 ? line.Substring(0, 200) + "..." : line);
            }
        }
        return bad;
    }

    // In the world: a song started from the window, another player's notes heard. No clean-up here: the log out
    // must meet the mod in the middle of it.
    private static IEnumerator RelogPlayBefore()
    {
        _relogStarted = false;
        LogTap.Install();
        _relogLogMark = LogTap.ProblemMark;
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(RelogPlayName, "before the log out: no player");
            yield break;
        }
        // Nothing forced by an earlier test: the log out meets the mod as a player has it.
        ClearOverrides();
        _relogRepeat = Performance.Repeat;
        _relogLastSong = Performance.LastSongId;
        _relogLastPart = Performance.LastPart;
        yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
        var flute = GiveMarked(player, InstrumentKind.Flute, RelogPlayName);
        if (flute == null || !player.EquipItem(flute, false))
        {
            RemoveMarked(player, RelogPlayName);
            SelfTest.Fail(RelogPlayName, "before the log out: could not hold a flute");
            yield break;
        }
        yield return new WaitForSeconds(0.6f);
        // Left click with the flute in hand, the way the game hands it to the player.
        for (var i = 0; i < 3; i++)
        {
            player.SetControls(Vector3.zero, true, true, false, false, false, false, false, false, false, false);
            yield return null;
        }
        yield return Frames(3);
        // Repeat on: the song is still running when the world goes, however long the log out takes to start.
        Performance.Repeat = true;
        var played = SongWindow.IsOpen && SongWindow.TestSelect("preset:greensleeves") && SongWindow.TestPress("Play");
        var other = new ZDOID(626262L, 5u);
        var batch = BatchOf(other, 81, player.transform.position + player.transform.right * 6f, InstrumentKind.Lyre, 0f);
        for (var i = 0; i < 12; i++)
        {
            batch.Notes.Add(new Note(0.2f + i * 0.4f, 0.8f, (byte)(57 + i), 100));
        }
        Listeners.Receive(batch);
        yield return new WaitForSeconds(1.3f);
        var rms = new float[1];
        yield return Measure(Performance.LocalEmitter, 0.5f, rms);
        var running = played && Performance.Mode == PerformanceMode.Auto && EmitterCount() >= 2 && SongWindow.Built && MiniGameHud.Built
                      && MiniGameHud.AutoShown && Listeners.RemoteCount == 1 && rms[0] > 0.002f;
        if (running)
        {
            _relogStarted = true;
            SelfTest.Pass(RelogPlayName, $"before the log out: a song plays by itself (rms {F(rms[0])}), another player is heard, window and HUD are built");
        }
        else
        {
            Performance.Abort(null);
            Performance.CloseWindow();
            Listeners.Shutdown();
            Performance.Repeat = _relogRepeat;
            Performance.LastSongId = _relogLastSong;
            Performance.LastPart = _relogLastPart;
            RemoveMarked(player, RelogPlayName);
            SelfTest.Fail(RelogPlayName, $"before the log out: the song did not get going (Play pressed {played}, mode {Performance.Mode}, sound sources {EmitterCount()}, "
                                         + $"window built {SongWindow.Built}, HUD built {MiniGameHud.Built}, other players heard {Listeners.RemoteCount}, rms {F(rms[0])})");
        }
    }

    // At the main menu, right after the real log out.
    private static IEnumerator RelogPlayMenu()
    {
        var c = new Checks(RelogPlayName);
        if (!c.Check(_relogStarted, "at the main menu: no song was playing at the log out (the part before failed), so nothing was checked here"))
        {
            c.Report();
            yield break;
        }
        yield return Frames(3);
        c.Check(FejdStartup.instance != null && Player.m_localPlayer == null, "at the main menu, no player");
        c.Check(Performance.Mode == PerformanceMode.None && Performance.Game == null && !Performance.WindowOpen, $"main menu: no song running (mode {Performance.Mode})");
        c.Check(EmitterCount() == 0 && Performance.LocalEmitter == null && Listeners.RemoteCount == 0,
            $"main menu: no sound source of the mod left ({EmitterCount()} found, other players heard {Listeners.RemoteCount})");
        c.Check(!SongWindow.Built && !SongWindow.IsOpen, "main menu: no song window left");
        c.Check(!MiniGameHud.Built && !MiniGameHud.Shown, "main menu: no HUD of the mod left");
        c.Check(!KeyCapture.Active && Listeners.Duck > 0.999f, $"main menu: the mod holds no keys and leaves the game's music alone (music factor {F(Listeners.Duck)})");
        yield return Real(1.5f);
        c.Check(EmitterCount() == 0 && !SongWindow.Built && !MiniGameHud.Built && Performance.Mode == PerformanceMode.None,
            "main menu: still nothing a moment later");
        c.Report();
    }

    // The same world loaded again.
    private static IEnumerator RelogPlayAfter()
    {
        var c = new Checks(RelogPlayName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(RelogPlayName, "after logging back in: no player");
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
            var mine = Marked(player, RelogPlayName);
            var flute = mine.Count > 0 ? mine[0] : null;
            c.Check(mine.Count == 1 && InstrumentContent.KindOf(flute) == InstrumentKind.Flute,
                $"after logging back in: the flute held at the log out is in the inventory ({mine.Count} found)");
            if (flute == null)
            {
                flute = rig.Give(InstrumentContent.ItemName(InstrumentKind.Flute));
            }
            if (flute != null && !player.IsItemEquiped(flute))
            {
                player.EquipItem(flute, false);
            }
            yield return new WaitForSeconds(0.6f);
            c.Check(Performance.Mode == PerformanceMode.None && EmitterCount() == 0 && PlayingFlag(player) == 0 && InstrumentPose.Weight(player) < 0.01f
                    && !MiniGameHud.Shown && !SongWindow.IsOpen && !KeyCapture.Active,
                $"back in the world: nothing plays by itself (mode {Performance.Mode}, sound sources {EmitterCount()}, playing flag {PlayingFlag(player)})");
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            yield return OpenWindow(rig);
            c.Check(SongWindow.IsOpen && SongWindow.Built, "the click opens the song window again");
            c.Check(SongWindow.TestSelect("preset:greensleeves") && SongWindow.TestPress("Play"), "Play pressed");
            yield return new WaitForSeconds(1.3f);
            var rms = new float[1];
            yield return Measure(Performance.LocalEmitter, 0.6f, rms);
            c.Check(Performance.Mode == PerformanceMode.Auto && rms[0] > 0.002f && MiniGameHud.AutoShown && InstrumentPose.Weight(player) > 0.9f,
                $"the song plays and sounds again (mode {Performance.Mode}, rms {F(rms[0])})");
            // music.cleanlog ran before this log out: the log of this one is read here.
            var bad = OwnProblemsSince(_relogLogMark);
            for (var i = 0; i < bad.Count && i < 5; i++)
            {
                c.Note("in the log: " + bad[i]);
            }
            c.Check(bad.Count == 0, $"no warning or error from Music Instruments from the song before the log out to the song after loading the world again ({bad.Count}"
                                    + (bad.Count > 0 ? ", first: " + bad[0] : "") + ")");
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
            try
            {
                // After the Extra: it put back what it saw at its start (my Repeat of the part before).
                Performance.Repeat = _relogRepeat;
                Performance.LastSongId = _relogLastSong;
                Performance.LastPart = _relogLastPart;
                RemoveMarked(player, RelogPlayName);
            }
            catch (Exception e)
            {
                Log.Warning($"Self test clean-up (relog) failed: {e.Message}");
            }
        }
    }

    // ---------- music.disabled.items (T18) ----------

    private static bool IsInstrumentPrefabName(string name)
    {
        foreach (var kind in InstrumentContent.Kinds)
        {
            if (InstrumentContent.ItemName(kind) == name)
            {
                return true;
            }
        }
        return false;
    }

    // Enabled instrument recipes in the game's recipe list (what the crafting list is made from).
    private static int ShownInstrumentRecipes()
    {
        var db = ObjectDB.instance;
        var n = 0;
        if (db == null)
        {
            return 0;
        }
        foreach (var recipe in db.m_recipes)
        {
            if (recipe != null && recipe.m_enabled && recipe.m_item != null && IsInstrumentPrefabName(recipe.m_item.gameObject.name))
            {
                n++;
            }
        }
        return n;
    }

    // First game, mod on, last thing before the quit: the three instruments in the inventory (saved with the character).
    private static IEnumerator DisabledItemsLeave()
    {
        var player = Player.m_localPlayer;
        if (player == null || !ModActive)
        {
            SelfTest.Fail(DisabledItemsName, $"first game: no player, or the mod is not active (active {ModActive})");
            yield break;
        }
        RemoveMarked(player, DisabledItemsName);
        var given = 0;
        foreach (var kind in InstrumentContent.Kinds)
        {
            if (GiveMarked(player, kind, DisabledItemsName) != null)
            {
                given++;
            }
        }
        yield return null;
        var recipes = ShownInstrumentRecipes();
        SelfTest.Carry(DisabledItemsName + ".given", given.ToString());
        SelfTest.Carry(DisabledItemsName + ".recipes", recipes.ToString());
        if (given == 3)
        {
            SelfTest.Pass(DisabledItemsName, $"first game (mod on): flute, lyre and tambourine put in the inventory for the restart; {recipes} instrument recipe(s) shown");
        }
        else
        {
            SelfTest.Fail(DisabledItemsName, $"first game: only {given} of 3 instruments could be put in the inventory");
        }
    }

    // Second game: Enabled = false in the cfg since before the start.
    private static IEnumerator DisabledItemsCheck()
    {
        var c = new Checks(DisabledItemsName);
        var player = Player.m_localPlayer;
        var db = ObjectDB.instance;
        if (player == null || db == null)
        {
            SelfTest.Fail(DisabledItemsName, "second game: no player");
            yield break;
        }
        try
        {
            var view = FeatureRegistry.Find(ModInfo.Guid);
            c.Check(SelfTest.Stage == SelfTest.DisabledStage && view != null && view.Value.State == nameof(ModState.Disabled)
                    && view.Value.Status == "Off (disabled in settings)." && !Plugin.FeatureActive,
                $"started with Enabled = false: stage '{SelfTest.Stage}', state {(view != null ? view.Value.State : "?")}, status '{(view != null ? view.Value.Status : "?")}'");
            c.Check(SelfTest.Carried(DisabledItemsName + ".given") == "3", $"the first game left three instruments (it says '{SelfTest.Carried(DisabledItemsName + ".given")}')");
            var mine = Marked(player, DisabledItemsName);
            foreach (var kind in InstrumentContent.Kinds)
            {
                var name = InstrumentContent.ItemName(kind);
                var found = 0;
                var whole = false;
                foreach (var item in mine)
                {
                    if (item.m_dropPrefab != null && item.m_dropPrefab.name == name)
                    {
                        found++;
                        whole = db.GetItemPrefab(name) == item.m_dropPrefab && item.m_shared != null && !string.IsNullOrEmpty(item.m_shared.m_name)
                                && item.m_stack == 1 && InstrumentContent.KindOf(item) == kind;
                    }
                }
                c.Check(found == 1 && whole, $"the {kind} is still in the inventory after the restart with the mod off ({found} found, whole item {whole})");
            }
            c.Check(mine.Count == 3, $"three instruments in the inventory, none lost ({mine.Count})");
            var known = true;
            foreach (var kind in InstrumentContent.Kinds)
            {
                var name = InstrumentContent.ItemName(kind);
                known &= db.GetItemPrefab(name) != null && ZNetScene.instance != null && ZNetScene.instance.GetPrefab(name) != null;
            }
            c.Check(known, "the game still knows the three items (that is what keeps them)");
            var shown = ShownInstrumentRecipes();
            // Same count found recipes while the mod was on: only then "none now" say something.
            var shownBefore = SelfTest.Carried(DisabledItemsName + ".recipes");
            c.Check(int.TryParse(shownBefore, out var before) && before >= 1,
                $"with the mod on, the first game had instrument recipes in the game's recipe list ({shownBefore ?? "nothing carried"}; 3 with the default settings)");
            c.Check(shown == 0, $"no instrument recipe is offered ({shown} enabled in the game's recipe list; the first game had {shownBefore})");
            var craftable = new List<Recipe>();
            player.GetAvailableRecipes(ref craftable);
            var offered = 0;
            foreach (var recipe in craftable)
            {
                if (recipe != null && recipe.m_item != null && IsInstrumentPrefabName(recipe.m_item.gameObject.name))
                {
                    offered++;
                }
            }
            c.Check(offered == 0, $"the character's crafting list has no instrument ({offered})");
            yield return null;
        }
        finally
        {
            try
            {
                RemoveMarked(player, DisabledItemsName);
            }
            catch (Exception e)
            {
                Log.Warning($"Self test clean-up (disabled start) failed: {e.Message}");
            }
        }
        c.Report();
    }

    // ---------- music.death.* (T37) ----------

    private static List<ZNetView> Graves()
    {
        var list = new List<ZNetView>();
        var scene = ZNetScene.instance;
        if (scene == null)
        {
            return list;
        }
        foreach (var view in scene.m_instances.Values)
        {
            if (view != null && view.GetComponent<TombStone>() != null)
            {
                list.Add(view);
            }
        }
        return list;
    }

    // Wood in an inventory, every stack.
    private static int WoodIn(Inventory inventory)
    {
        var n = 0;
        foreach (var item in inventory.GetAllItems())
        {
            if (item.m_dropPrefab != null && item.m_dropPrefab.name == InstrumentContent.VanillaWoodName)
            {
                n += item.m_stack;
            }
        }
        return n;
    }

    private static void PutPlayer(Player player, Vector3 position)
    {
        player.transform.position = position;
        if (player.m_body != null)
        {
            player.m_body.position = position;
            player.m_body.linearVelocity = Vector3.zero;
        }
    }

    // Real death and the game's own respawn. which: 0 = a song plays by itself (killer: a real Greyling with its AI
    // on, the player at 1 health), 1 = performing, 2 = song window open (killer: one deadly hit through the game's
    // damage call). Kept aside and put back after: inventory, skills, food, health, place, death pin and death point,
    // god mode; the death tutorial is marked seen for the death (no raven).
    private static IEnumerator RunDeath(string name, int which)
    {
        var c = new Checks(name);
        var old = Player.m_localPlayer;
        if (old == null || Game.instance == null || ZNet.instance == null || ZNetScene.instance == null || Chat.instance == null || ZoneSystem.instance == null)
        {
            SelfTest.Fail(name, "no player or no world");
            yield break;
        }
        var god = old.InGodMode();
        var position = old.transform.position;
        var rotation = old.transform.rotation;
        var health = old.GetHealth();
        var sinceDeath = old.m_timeSinceDeath;
        var skills = new Dictionary<Skills.SkillType, Vector2>();
        foreach (var s in old.GetSkills().m_skillData)
        {
            skills[s.Key] = new Vector2(s.Value.m_level, s.Value.m_accumulator);
        }
        var foods = new List<Player.Food>(old.m_foods);
        var world = Game.instance.GetPlayerProfile().GetWorldData(ZNet.instance.GetWorldUID());
        var hadDeathPoint = world.m_haveDeathPoint;
        var deathPoint = world.m_deathPoint;
        var bag = new ZPackage();
        old.GetInventory().Save(bag);
        var sawTutorial = old.m_shownTutorials.Contains("death");
        var repeat = Performance.Repeat;
        var lastSong = Performance.LastSongId;
        var lastPart = Performance.LastPart;
        var gravesBefore = new HashSet<ZNetView>(Graves());
        var changed = false;
        var corpseRun = 0;
        GameObject foe = null;

        List<ZNetView> NewGraves()
        {
            var list = new List<ZNetView>();
            foreach (var view in Graves())
            {
                if (!gravesBefore.Contains(view))
                {
                    list.Add(view);
                }
            }
            return list;
        }

        void GiveBack(Player player)
        {
            if (!sawTutorial)
            {
                player.m_shownTutorials.Remove("death");
            }
            player.UnequipAllItems();
            player.GetInventory().RemoveAll();
            bag.SetPos(0);
            player.GetInventory().Load(bag);
            player.EquipInventoryItems();
            var data = player.GetSkills().m_skillData;
            foreach (var type in new List<Skills.SkillType>(data.Keys))
            {
                if (!skills.ContainsKey(type))
                {
                    data.Remove(type);
                }
            }
            foreach (var saved in skills)
            {
                var skill = player.GetSkills().GetSkill(saved.Key);
                skill.m_level = saved.Value.x;
                skill.m_accumulator = saved.Value.y;
            }
            if (!ReferenceEquals(player, old))
            {
                player.m_foods.Clear();
                player.m_foods.AddRange(foods);
                player.m_timeSinceDeath = sinceDeath;
                if (sinceDeath > player.m_hardDeathCooldown)
                {
                    player.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectSoftDeath, true);
                }
                PutPlayer(player, position + Vector3.up * 0.1f);
                player.transform.rotation = rotation;
                if (player.m_body != null)
                {
                    player.m_body.rotation = rotation;
                }
                player.m_maxAirAltitude = player.transform.position.y;
                if (Minimap.instance != null)
                {
                    Minimap.instance.RemovePin(position, 3f);
                }
                world.m_haveDeathPoint = hadDeathPoint;
                world.m_deathPoint = deathPoint;
            }
            player.SetHealth(Mathf.Min(health, player.GetMaxHealth()));
        }

        try
        {
            ClearOverrides();
            ServerRules.TestRules = TestRules(60f);
            Plugin.TestGameMusicVolume = 0.3f;
            SongShare.TestShare = false;
            SongShare.ServerSettingsChanged();
            old.m_shownTutorials.Add("death");
            changed = true;
            // Wood counted before and after: the character may carry Wood already (loot an earlier test of the run
            // picked up); the game stack ours on it.
            var woodBefore = WoodIn(old.GetInventory());
            var flute = old.GetInventory().AddItem(InstrumentContent.ItemName(InstrumentKind.Flute), 1, 1, 0, 0L, "", false);
            var wood = old.GetInventory().AddItem("Wood", 3, 1, 0, 0L, "", false);
            var woodCarried = WoodIn(old.GetInventory());
            if (!c.Check(flute != null && wood != null && woodCarried == woodBefore + 3 && old.EquipItem(flute, false),
                    $"a flute in the hand and some wood in the inventory (wood {woodBefore} before, {woodCarried} now)"))
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(0.6f);
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            string error = null;
            switch (which)
            {
                case 0:
                    // Repeat on: the song must not end by itself while the Greyling comes.
                    Performance.Repeat = true;
                    c.Check(Performance.StartAuto(Preset("greensleeves"), -1, out error), "a song plays by itself: " + error);
                    break;
                case 1:
                    c.Check(Performance.StartMiniGame(Preset("kjerringa-med-staven"), -1, out error), "performing: " + error);
                    break;
                default:
                    Performance.TestRequestOpen();
                    break;
            }
            yield return new WaitForSeconds(1.2f);
            bool Running() => which == 0 ? Performance.Mode == PerformanceMode.Auto
                : which == 1 ? Performance.Mode == PerformanceMode.MiniGame
                : SongWindow.IsOpen && Performance.WindowOpen;
            var what = which == 0 ? "a song playing by itself" : which == 1 ? "the rhythm game" : "the song window";
            var up = which == 0 ? MiniGameHud.AutoShown && EmitterCount() >= 1 && InstrumentPose.Weight(old) > 0.9f
                : which == 1 ? MiniGameHud.MiniShown && KeyCapture.Active
                : KeyCapture.Active && ZCursor.IsRequested;
            if (!c.Check(Running() && up, $"{what} is up before the death (mode {Performance.Mode}, window {SongWindow.IsOpen})"))
            {
                c.Report();
                yield break;
            }
            var items = old.GetInventory().NrOfItems();

            // The death.
            old.SetGodMode(false);
            var wasRunning = Running();
            var byCreature = false;
            if (which == 0)
            {
                var prefab = ZNetScene.instance.GetPrefab("Greyling");
                if (prefab != null)
                {
                    var ahead = old.transform.forward;
                    ahead.y = 0f;
                    ahead.Normalize();
                    var spot = old.transform.position + ahead * 2.5f;
                    spot.y = ZoneSystem.instance.GetGroundHeight(spot) + 0.2f;
                    foe = Object.Instantiate(prefab, spot, Quaternion.LookRotation(-ahead));
                }
                c.Check(foe != null, "a Greyling stands next to the player");
                var until = Time.time + 20f;
                while (foe != null && Time.time < until && old != null && !old.IsDead())
                {
                    // One health: its first hit kills, so the song is not cut short by a hit before the death.
                    if (old.GetHealth() > 1f)
                    {
                        old.SetHealth(1f);
                    }
                    if (old.GetHealth() > 0f)
                    {
                        wasRunning = Running();
                    }
                    yield return null;
                }
                byCreature = old != null && old.IsDead();
            }
            if (old != null && !old.IsDead())
            {
                wasRunning = Running();
                var hit = new HitData { m_hitType = HitData.HitType.EnemyHit };
                hit.m_damage.m_blunt = 99999f;
                hit.m_point = old.GetCenterPoint();
                old.Damage(hit);
                yield return WaitFor(() => old == null || old.IsDead(), 3f);
            }
            if (foe != null)
            {
                ZNetScene.instance.Destroy(foe);
                foe = null;
            }
            if (!c.Check(old != null && old.IsDead(), "the player died"))
            {
                c.Report();
                yield break;
            }
            c.Note(which != 0 ? "killed by one deadly hit given through the game's own damage call"
                : byCreature ? "killed by the Greyling's own hit"
                : "the Greyling landed no hit within 20 s: killed by one deadly hit given through the game's own damage call instead");
            c.Check(wasRunning, $"{what} was still up when the deadly hit came");

            // The death screen.
            yield return Frames(4);
            c.Check(Performance.Mode == PerformanceMode.None && Performance.Game == null, $"dead: the music and the rhythm game stop (mode {Performance.Mode})");
            c.Check(!SongWindow.IsOpen && !Performance.WindowOpen, "dead: the song window is closed");
            c.Check(!MiniGameHud.Shown, "dead: no HUD of the mod is shown");
            c.Check(!KeyCapture.Active && !Chat.instance.HasFocus(), "dead: the mod holds no keys");
            var controller = old != null ? old.GetComponent<PlayerController>() : null;
            c.Check(controller != null && controller.TakeInput(false) && controller.TakeInput(true), "dead: the game reads keys and mouse (death screen)");
            c.Check(old == null || PlayingFlag(old) <= 0, "dead: the playing flag other players' games read is down");
            yield return new WaitForSeconds(0.8f);
            var rms = new float[1];
            yield return Measure(Performance.LocalEmitter, 0.4f, rms);
            c.Check(rms[0] < 0.0002f, $"dead: no sound from the instrument (rms {F(rms[0])})");

            // The new body.
            Player now = null;
            yield return WaitReal(() =>
            {
                now = Player.m_localPlayer;
                return now != null && !ReferenceEquals(now, old) && !now.IsDead();
            }, 60f);
            if (!c.Check(now != null && !ReferenceEquals(now, old) && !now.IsDead(), "a new player within 60 s of the death"))
            {
                c.Report();
                yield break;
            }
            yield return WaitReal(() => !now.IsTeleporting() && ZNetScene.instance.IsAreaReady(now.transform.position), 20f);
            yield return new WaitForSeconds(1f);
            // New body stand up first (game's own Standing Up animation, Player.InCutscene): no input taken and no walking
            // until it ends. First runs looked too early and saw frozen keys that were the normal game.
            yield return WaitReal(() => !now.InCutscene(), 30f);
            c.Check(Performance.Mode == PerformanceMode.None && !Performance.WindowOpen && !SongWindow.IsOpen && !MiniGameHud.Shown,
                $"after the respawn: nothing plays, no window, no HUD (mode {Performance.Mode})");
            c.Check(EmitterCount() == 0, $"after the respawn: no sound source of the mod left ({EmitterCount()})");
            c.Check(PlayingFlag(now) == 0 && InstrumentPose.Weight(now) < 0.01f, $"after the respawn: no playing pose on the new body (flag {PlayingFlag(now)}, pose {F(InstrumentPose.Weight(now))})");
            var nowController = now.GetComponent<PlayerController>();
            c.Check(!KeyCapture.Active && !Chat.instance.HasFocus() && nowController != null && nowController.TakeInput(false) && now.TakeInput(),
                "after the respawn: no frozen keys (the game reads them)");
            // The new body walks when told to (controller off for the push, so its own empty input does not fight it).
            if (nowController != null)
            {
                var from = now.transform.position;
                var controllerOn = nowController.enabled;
                nowController.enabled = false;
                // Forward, then back (something may stand in the way on one side).
                foreach (var way in new[] { 1f, -1f })
                {
                    var pushUntil = Time.time + 0.6f;
                    while (Time.time < pushUntil && Flat(now.transform.position, from) <= 0.2f)
                    {
                        now.SetControls(new Vector3(0f, 0f, way), false, false, false, false, false, false, false, false, false, false);
                        yield return null;
                    }
                }
                now.SetControls(Vector3.zero, false, false, false, false, false, false, false, false, false, false);
                nowController.enabled = controllerOn;
                c.Check(Flat(now.transform.position, from) > 0.2f, $"after the respawn: the new body walks ({F(Flat(now.transform.position, from))} m)");
            }

            // The tombstone.
            var graves = NewGraves();
            var grave = graves.Count > 0 ? graves[0].GetComponent<TombStone>() : null;
            var container = grave != null ? grave.GetComponent<Container>() : null;
            var inside = container != null ? container.GetInventory() : null;
            var fluteInside = 0;
            if (inside != null)
            {
                foreach (var item in inside.GetAllItems())
                {
                    if (InstrumentContent.KindOf(item) == InstrumentKind.Flute)
                    {
                        fluteInside++;
                    }
                }
            }
            var woodInside = inside != null ? WoodIn(inside) : 0;
            c.Check(graves.Count == 1 && inside != null && fluteInside == 1 && woodInside == woodCarried && inside.NrOfItems() == items,
                $"the instrument is in the tombstone with the other items ({graves.Count} tombstone(s), flute {fluteInside}, wood {woodInside} "
                + $"of {woodCarried} carried, {(inside != null ? inside.NrOfItems() : 0)} of {items} items)");
            c.Check(CountKind(now, InstrumentKind.Flute) == 0, "and not on the new body");
            if (grave != null)
            {
                corpseRun = grave.m_lootStatusEffect != null ? grave.m_lootStatusEffect.NameHash() : 0;
                // The Use key on the tombstone.
                grave.Interact(now, false, false);
                yield return WaitFor(() => CountKind(now, InstrumentKind.Flute) > 0, 3f);
                if (CountKind(now, InstrumentKind.Flute) == 0 && InventoryGui.instance != null && InventoryGui.instance.m_currentContainer == container)
                {
                    // It did not fit at once: the tombstone opened like a chest; its Take all button.
                    InventoryGui.instance.OnTakeAll();
                    yield return WaitFor(() => CountKind(now, InstrumentKind.Flute) > 0, 3f);
                }
                if (InventoryGui.instance != null && InventoryGui.IsVisible())
                {
                    InventoryGui.instance.Hide();
                }
            }
            ItemDrop.ItemData back = null;
            foreach (var item in now.GetInventory().GetAllItems())
            {
                if (InstrumentContent.KindOf(item) == InstrumentKind.Flute)
                {
                    back = item;
                }
            }
            c.Check(back != null && now.GetInventory().NrOfItems() == items,
                $"taken back from the tombstone: the instrument and the other items are in the inventory ({now.GetInventory().NrOfItems()} of {items} items)");
            // Emptied tombstone goes by itself within 2 s and gives its "corpse run" effect: me take it away first.
            foreach (var view in NewGraves())
            {
                ZNetScene.instance.Destroy(view.gameObject);
            }
            if (back != null)
            {
                c.Check(now.EquipItem(back, false), "the flute goes back in the hand");
                yield return new WaitForSeconds(0.6f);
                yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
                c.Check(Performance.StartAuto(Preset("greensleeves"), -1, out error), "taken back, the flute plays: " + error);
                yield return new WaitForSeconds(1.3f);
                yield return Measure(Performance.LocalEmitter, 0.6f, rms);
                c.Check(Performance.Mode == PerformanceMode.Auto && rms[0] > 0.002f && InstrumentPose.Weight(now) > 0.9f
                        && PlayingFlag(now) == (int)InstrumentKind.Flute && MiniGameHud.AutoShown,
                    $"it plays as before: sound, pose and status line (mode {Performance.Mode}, rms {F(rms[0])}, pose {F(InstrumentPose.Weight(now))})");
                Performance.Abort(null);
                yield return Frames(3);
            }
            c.Report();
        }
        finally
        {
            try
            {
                Performance.Abort(null);
                Performance.CloseWindow();
                ClearOverrides();
                SongShare.TestShare = null;
                SongShare.ServerSettingsChanged();
                Performance.Repeat = repeat;
                Performance.LastSongId = lastSong;
                Performance.LastPart = lastPart;
                if (foe != null && ZNetScene.instance != null)
                {
                    ZNetScene.instance.Destroy(foe);
                }
                if (ZNetScene.instance != null)
                {
                    foreach (var view in NewGraves())
                    {
                        ZNetScene.instance.Destroy(view.gameObject);
                    }
                }
                var player = Player.m_localPlayer;
                if (player != null)
                {
                    if (changed)
                    {
                        GiveBack(player);
                    }
                    if (corpseRun != 0)
                    {
                        player.GetSEMan().RemoveStatusEffect(corpseRun, true);
                    }
                    player.SetGodMode(god);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"Self test clean-up (death) failed: {e.Message}");
            }
        }
    }
}
#endif
