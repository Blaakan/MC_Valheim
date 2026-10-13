#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.TrinketsOnDemandMod;

// Debug build only. Self tests that need a new player object, a real log out or a real restart (same class as
// SelfTests.cs). One small test per thing:
//   trinkets.respawn              T20: trinket worn, half bar, real death (god mode off) and the game's respawn: key
//                                 does nothing while dead, new player has an empty bar, mod works for new player
//   trinkets.relog.bar            T20: trinket worn, half bar, real log out and back in (relog test): trinket still
//                                 worn, bar empty, full bar waits and key fires, clean log all the way
//   trinkets.disabled.vanilla-bar L02: second game, every MC mod Enabled = false from the start: trinket put on in
//                                 the first game still worn, status off, game's bar (fires at once, drains, no income),
//                                 no tooltip line, no error since that game started
//   trinkets.disabled.key         L02: same game, real Y key: nothing
//   trinkets.disabled.first-log   L03: second game reads whole log of the first game (all tests played, then quit
//                                 from the world): no warning or error line of the mod (F13 one aside), no Unity
//                                 warning about missing Flash parameter, no error that names mod's code. Fails when
//                                 the tests of T01-T20 did not all pass in that game (filtered run: nothing played),
//                                 or when the file holds no Unity line at all (BepInEx WriteUnityLog off)
// Relog test registered in OnActivated; disabled-start tests in BindConfig (mod never active in the second game).
// Items left in the bag over a log out or restart carry a mark (custom data) so the part after finds and removes them.
internal static partial class SelfTests
{
    private const string RespawnName = "trinkets.respawn";
    private const string RelogBarName = "trinkets.relog.bar";
    private const string DisabledBarName = "trinkets.disabled.vanilla-bar";
    private const string DisabledKeyName = "trinkets.disabled.key";
    private const string DisabledLogName = "trinkets.disabled.first-log";

    private const string TestMarkKey = ModInfo.Guid + ".SelfTest";
    private const string FirstLogFile = "LogOutput.first.log"; // tools/Test-InWorld.ps1 write it before the second game

    // ---------- registration (tests of SelfTestsInput, SelfTestsMods, SelfTestsMpVersion and this file) ----------

    // In-world tests in running order. Respawn last: it leaves a new player object.
    private static readonly string[] AutoTests =
    {
        InputKeysName, InputPadName, InputLayoutsName, InputDualName, InputMusicName, DualCreatureName, MovesetName,
        SmokeName, MoraleFleeName, MoraleAfraidName, DodgeArrowName, RespawnName,
    };

    // OnActivated (RegisterMore), before trinkets.clean-log.
    private static void RegisterAuto()
    {
        SelfTest.Register(InputKeysName, RunInputKeys);
        SelfTest.Register(InputPadName, RunInputPadDefault);
        SelfTest.Register(InputLayoutsName, RunInputPadLayouts);
        SelfTest.Register(InputDualName, RunInputDualWield);
        SelfTest.Register(InputMusicName, RunInputMusic);
        SelfTest.Register(DualCreatureName, RunDualWieldCreature);
        SelfTest.Register(MovesetName, RunMoveset);
        SelfTest.Register(SmokeName, RunSmokeScreen);
        SelfTest.Register(MoraleFleeName, RunMoraleFlee);
        SelfTest.Register(MoraleAfraidName, RunMoraleAfraid);
        SelfTest.Register(DodgeArrowName, RunDodgeArrow);
        SelfTest.Register(RespawnName, RunRespawn);
        SelfTest.RegisterRelog(RelogBarName, RelogBarBefore, null, RelogBarAfter);
    }

    // OnActivated (RegisterMore), after every other multiplayer test of the mod.
    private static void RegisterAutoMp()
    {
        SelfTest.RegisterMultiplayer(MpVersionName, SelfTest.Modded, RunMpOtherVersion);
        SelfTest.RegisterServerStep(SrvVersionStep, ServerVersionStep);
    }

    // OnDeactivated (UnregisterMore).
    private static void UnregisterAuto()
    {
        foreach (var name in AutoTests)
        {
            SelfTest.Unregister(name);
        }
        SelfTest.UnregisterRelog(RelogBarName);
        SelfTest.UnregisterMultiplayer(MpVersionName);
        SelfTest.UnregisterServerStep(SrvVersionStep);
    }

    // BindConfig (RegisterInactive: every start). Disabled-start tests are never unregistered: in the second game
    // the mod is off from the start, OnActivated never runs.
    private static void RegisterStaged()
    {
        SelfTest.RegisterDisabledStart(DisabledBarName, DisabledBarLeave, DisabledBarCheck);
        SelfTest.RegisterDisabledStart(DisabledKeyName, null, DisabledKeyCheck);
        SelfTest.RegisterDisabledStart(DisabledLogName, null, DisabledLogCheck);
    }

    // ---------- marked items ----------

    private static ItemDrop.ItemData GiveMarked(Player p, string prefab, string mark)
    {
        var item = p.GetInventory().AddItem(prefab, 1, 1, 0, 0L, "", false);
        if (item != null)
        {
            item.m_customData[TestMarkKey] = mark;
        }
        return item;
    }

    private static List<ItemDrop.ItemData> Marked(Player p, string mark) =>
        p.GetInventory().GetAllItems()
            .Where(i => i != null && i.m_customData.TryGetValue(TestMarkKey, out var m) && m == mark).ToList();

    private static void RemoveMarked(Player p, string mark)
    {
        foreach (var item in Marked(p, mark))
        {
            if (p.IsItemEquiped(item))
            {
                p.UnequipItem(item, false);
            }
            p.GetInventory().RemoveItem(item);
        }
    }

    // Trinket the player wore before a staged test took its slot: by prefab name, "" = none.
    private static string WornTrinketName(Player p)
    {
        var worn = p.m_trinketItem;
        return worn != null && worn.m_dropPrefab != null ? worn.m_dropPrefab.name : "";
    }

    private static void WearAgain(Player p, string prefab)
    {
        if (string.IsNullOrEmpty(prefab) || p.m_trinketItem != null)
        {
            return;
        }
        foreach (var item in p.GetInventory().GetAllItems())
        {
            if (item != null && item.m_dropPrefab != null && item.m_dropPrefab.name == prefab && !item.m_customData.ContainsKey(TestMarkKey))
            {
                p.EquipItem(item, false);
                return;
            }
        }
    }

    // ---------- trinkets.respawn (T20, death) ----------

    // Real death and the game's own respawn, recipe of Sneak Ambush's sneak.death: bag kept aside (given back to the
    // new player), death tutorial marked seen for the death (no raven), then skills, food, health, place, death pin
    // and death point put back. Only the test trinket is in the bag at death, so the grave holds only that: grave
    // destroyed after.
    private static IEnumerator RunRespawn()
    {
        var c = new Checks(RespawnName);
        var old = Player.m_localPlayer;
        if (old == null || Game.instance == null || ZNet.instance == null || ZNetScene.instance == null || ObjectDB.instance == null)
        {
            SelfTest.Fail(RespawnName, "no local player or no world");
            yield break;
        }
        try
        {
            var plugin = Self();
            if (plugin != null && plugin.TestOff)
            {
                plugin.TestTurnOn();
            }
        }
        catch (Exception e)
        {
            Log.Warning($"Self test set-up (feature back on) failed: {e}");
        }
        var mark = LogMark();
        var god = old.InGodMode();
        var position = old.transform.position;
        var rotation = old.transform.rotation;
        var skills = old.GetSkills().m_skillData.ToDictionary(s => s.Key, s => new Vector2(s.Value.m_level, s.Value.m_accumulator));
        var foods = old.m_foods.ToList();
        var health = old.GetHealth();
        var sinceDeath = old.m_timeSinceDeath;
        var profile = Game.instance.GetPlayerProfile();
        var world = profile.GetWorldData(ZNet.instance.GetWorldUID());
        var hadDeathPoint = world.m_haveDeathPoint;
        var deathPoint = world.m_deathPoint;
        var bag = new ZPackage();
        old.GetInventory().Save(bag);
        var sawTutorial = old.m_shownTutorials.Contains("death");
        var emptied = false;
        var given = false;
        var gravesBefore = new HashSet<ZNetView>();

        List<ZNetView> NewGraves()
        {
            var list = new List<ZNetView>();
            var scene = ZNetScene.instance;
            if (scene == null)
            {
                return list;
            }
            foreach (var view in scene.m_instances.Values)
            {
                if (view != null && !gravesBefore.Contains(view) && view.GetComponent<TombStone>() != null)
                {
                    list.Add(view);
                }
            }
            return list;
        }

        void GiveBack(Player player)
        {
            given = true;
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
            foreach (var type in data.Keys.ToList())
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
                Put(player, position + Vector3.up * 0.1f);
                player.transform.rotation = rotation;
                player.m_body.rotation = rotation;
                player.m_maxAirAltitude = player.transform.position.y;
                if (Minimap.instance != null)
                {
                    Minimap.instance.RemovePin(position, 3f);
                }
                world.m_haveDeathPoint = hadDeathPoint;
                world.m_deathPoint = deathPoint;
            }
            player.UpdateModifiers();
            player.SetHealth(Mathf.Min(health, player.GetMaxHealth()));
        }

        try
        {
            ServerRules.TestRules = new TrinketRules();
            CombatState.Reset();
            FullBar.LastResult = TriggerResult.None;
            foreach (var view in NewGraves())
            {
                gravesBefore.Add(view);
            }

            // Bag aside, then only the trinket, worn, bar half full.
            old.m_shownTutorials.Add("death");
            old.UnequipAllItems();
            old.GetInventory().RemoveAll();
            emptied = true;
            var trinketName = PrefabShared(BronzeTrinket) != null ? BronzeTrinket : FindTrinket(c, false);
            var item = trinketName != null ? old.GetInventory().AddItem(trinketName, 1, 1, 0, 0L, "", false) : null;
            if (!c.Check(item != null && old.EquipItem(item, false), "could not equip a trinket"))
            {
                c.Report();
                yield break;
            }
            old.UpdateModifiers();
            var max = old.GetMaxAdrenaline();
            old.m_adrenaline = max * 0.5f;
            yield return FixedTicks(3);
            c.Check(max > 0f && Near(old.m_adrenaline, max * 0.5f), $"before the death: bar {F(old.m_adrenaline)} of {F(max)} (expected half)");

            // Die like the console's "die" with god mode off.
            old.SetGodMode(false);
            var hit = new HitData { m_hitType = HitData.HitType.Self };
            hit.m_damage.m_damage = 99999f;
            old.Damage(hit);
            var died = Time.time;
            yield return Until(() => old == null || old.IsDead(), 3f);
            if (!c.Check(old != null && old.IsDead(), "the player did not die"))
            {
                c.Report();
                yield break;
            }

            // Dead: trigger does nothing.
            yield return Press();
            c.Check(old == null || FullBar.LastResult == TriggerResult.Blocked,
                $"dead: the trigger answered {FullBar.LastResult} (expected Blocked: it does nothing)");

            Player now = null;
            yield return Until(() =>
            {
                now = Player.m_localPlayer;
                return now != null && !ReferenceEquals(now, old) && !now.IsDead();
            }, 70f);
            if (!c.Check(now != null && !ReferenceEquals(now, old) && !now.IsDead(), "no new player 70 s after the death"))
            {
                c.Report();
                yield break;
            }
            c.Note($"respawned {F(Time.time - died)} s after the death");
            yield return Until(() => !now.IsTeleporting() && ZNetScene.instance.IsAreaReady(now.transform.position), 20f);
            yield return new WaitForSeconds(1f);

            // The normal game: trinket lies in the grave, bar empty.
            c.Check(Near(now.GetAdrenaline(), 0f) && Near(now.GetMaxAdrenaline(), 0f) && now.m_trinketItem == null,
                $"after the respawn: bar {F(now.GetAdrenaline())} of {F(now.GetMaxAdrenaline())}, trinket worn {now.m_trinketItem != null} (expected an empty bar, the trinket is in the grave)");
            var graves = NewGraves();
            c.Note($"{graves.Count} grave(s) made by this death, removed with the test trinket inside");
            foreach (var view in graves)
            {
                DestroyObject(view.gameObject);
            }

            // Trinket on the new player: bar still empty; then mod works for the new player object.
            var again = now.GetInventory().AddItem(trinketName, 1, 1, 0, 0L, "", false);
            if (c.Check(again != null && now.EquipItem(again, false), "could not equip a trinket on the new player"))
            {
                now.UpdateModifiers();
                var newMax = now.GetMaxAdrenaline();
                var hash = again.m_shared.m_fullAdrenalineSE.NameHash();
                c.Check(Near(newMax, max) && Near(now.GetAdrenaline(), 0f),
                    $"trinket worn again after the respawn: bar {F(now.GetAdrenaline())} of {F(newMax)} (expected empty)");
                now.GetSEMan().RemoveStatusEffect(hash, true);
                ConsoleAdrenaline(now, 999f);
                c.Check(Near(now.m_adrenaline, newMax) && !now.GetSEMan().HaveStatusEffect(hash),
                    $"new player: a full bar does not wait (bar {F(now.m_adrenaline)} of {F(newMax)})");
                // Fresh respawn: game take no input for a moment (first run: trigger said Blocked). Player cannot press
                // then either, so me wait like a player has to. Still closed after 20 s = fail that say which gate.
                yield return Until(() => now.TakeInput() && !Hud.InRadial() && !now.InPlaceMode(), 20f);
                c.Check(now.TakeInput() && !Hud.InRadial() && !now.InPlaceMode(),
                    $"new player: the game takes no input 20 s after the respawn (TakeInput {now.TakeInput()}, dead {now.IsDead()}, "
                    + $"teleporting {now.IsTeleporting()}, cutscene {now.InCutscene()}, inventory {InventoryGui.IsVisible()}, menu {Menu.IsVisible()}, "
                    + $"text viewer {TextViewer.instance != null && TextViewer.instance.IsVisible()}, map {Minimap.IsOpen()}, radial {Hud.InRadial()}, "
                    + $"build mode {now.InPlaceMode()}, console {Console.IsVisible()})");
                yield return Press();
                c.Check(FullBar.LastResult == TriggerResult.Fired && now.GetSEMan().HaveStatusEffect(hash) && Near(now.m_adrenaline, 0f),
                    $"new player: the trigger answered {FullBar.LastResult} (expected Fired)");
                now.GetSEMan().RemoveStatusEffect(hash, true);
            }
            GiveBack(now);
            now.SetGodMode(god);
            yield return FixedTicks(3);
            CheckCleanLog(c, mark);
        }
        finally
        {
            try
            {
                ClearOverrides();
                var player = Player.m_localPlayer;
                if (player != null)
                {
                    if (emptied && !given)
                    {
                        GiveBack(player);
                    }
                    if (!sawTutorial)
                    {
                        player.m_shownTutorials.Remove("death");
                    }
                    player.SetGodMode(god);
                }
                CombatState.Reset();
                Feedback.Reset();
                FullBar.LastResult = TriggerResult.None;
            }
            catch (Exception e)
            {
                Log.Warning($"Self test clean-up (respawn) failed: {e}");
            }
        }
        c.Report();
    }

    // ---------- trinkets.relog.bar (T20, log out and back in) ----------

    private static int _relogMark;
    private static float _relogMax;
    private static string _relogWorn = "";

    private static IEnumerator RelogBarBefore()
    {
        var p = Player.m_localPlayer;
        if (p == null || ObjectDB.instance == null || PrefabShared(BronzeTrinket) == null)
        {
            SelfTest.Fail(RelogBarName, $"before the log out: no local player, or no {BronzeTrinket} in this game");
            yield break;
        }
        _relogMark = LogMark();
        _relogWorn = WornTrinketName(p);
        if (p.m_trinketItem != null)
        {
            p.UnequipItem(p.m_trinketItem, false);
        }
        var item = GiveMarked(p, BronzeTrinket, RelogBarName);
        var worn = item != null && p.EquipItem(item, false);
        p.UpdateModifiers();
        _relogMax = p.GetMaxAdrenaline();
        p.m_adrenaline = _relogMax * 0.5f;
        yield return FixedTicks(3);
        if (worn && _relogMax > 0f && Near(p.m_adrenaline, _relogMax * 0.5f))
        {
            SelfTest.Pass(RelogBarName, $"before the log out: {BronzeTrinket} worn, bar {F(p.m_adrenaline)} of {F(_relogMax)}");
        }
        else
        {
            SelfTest.Fail(RelogBarName, $"before the log out: trinket worn {worn}, bar {F(p.m_adrenaline)} of {F(_relogMax)} (expected half)");
        }
    }

    private static IEnumerator RelogBarAfter()
    {
        var c = new Checks(RelogBarName);
        var p = Player.m_localPlayer;
        if (p == null)
        {
            SelfTest.Fail(RelogBarName, "after logging back in: no local player");
            yield break;
        }
        try
        {
            var mine = Marked(p, RelogBarName);
            var item = mine.Count > 0 ? mine[0] : null;
            c.Check(mine.Count == 1 && ReferenceEquals(p.m_trinketItem, item),
                $"after logging back in: {mine.Count} test trinket(s) in the bag, worn {item != null && ReferenceEquals(p.m_trinketItem, item)} (expected the one worn at the log out, still worn)");
            if (item != null && !ReferenceEquals(p.m_trinketItem, item))
            {
                p.EquipItem(item, false);
            }
            p.UpdateModifiers();
            var max = p.GetMaxAdrenaline();
            c.Check(max > 0f && Near(max, _relogMax) && Near(p.GetAdrenaline(), 0f),
                $"after logging back in: bar {F(p.GetAdrenaline())} of {F(max)} (expected empty; it was {F(_relogMax * 0.5f)} of {F(_relogMax)} at the log out)");
            yield return new WaitForSeconds(2.2f);
            c.Check(Near(p.GetAdrenaline(), 0f), $"two seconds later the bar is {F(p.GetAdrenaline())} (expected still empty: no fight)");

            // Mod works for the new player object of this log in.
            var plugin = Self();
            c.Check(plugin != null && plugin.IsActive, "the mod is not active after logging back in");
            if (item != null && item.m_shared.m_fullAdrenalineSE != null)
            {
                var hash = item.m_shared.m_fullAdrenalineSE.NameHash();
                ServerRules.TestRules = new TrinketRules();
                p.GetSEMan().RemoveStatusEffect(hash, true);
                ConsoleAdrenaline(p, 999f);
                c.Check(Near(p.m_adrenaline, max) && !p.GetSEMan().HaveStatusEffect(hash),
                    $"after logging back in: a full bar does not wait (bar {F(p.m_adrenaline)} of {F(max)})");
                yield return Press();
                c.Check(FullBar.LastResult == TriggerResult.Fired && p.GetSEMan().HaveStatusEffect(hash),
                    $"after logging back in: the trigger answered {FullBar.LastResult} (expected Fired)");
                p.GetSEMan().RemoveStatusEffect(hash, true);
            }
            CheckCleanLog(c, _relogMark);
        }
        finally
        {
            try
            {
                ClearOverrides();
                RemoveMarked(p, RelogBarName);
                WearAgain(p, _relogWorn);
                p.UpdateModifiers();
                p.m_adrenaline = 0f;
                FullBar.LastResult = TriggerResult.None;
                Feedback.Reset();
            }
            catch (Exception e)
            {
                Log.Warning($"Self test clean-up (relog) failed: {e}");
            }
        }
        c.Report();
    }

    // ---------- trinkets.disabled.vanilla-bar (L02) ----------

    // First game, mod on, last thing before the quit: trinket worn (saved with the character).
    private static IEnumerator DisabledBarLeave()
    {
        var p = Player.m_localPlayer;
        if (p == null || ObjectDB.instance == null || PrefabShared(BronzeTrinket) == null)
        {
            SelfTest.Fail(DisabledBarName, $"first game: no local player, or no {BronzeTrinket} in this game");
            yield break;
        }
        SelfTest.Carry(DisabledBarName + ".worn", WornTrinketName(p));
        if (p.m_trinketItem != null)
        {
            p.UnequipItem(p.m_trinketItem, false);
        }
        var item = GiveMarked(p, BronzeTrinket, DisabledBarName);
        var worn = item != null && p.EquipItem(item, false);
        p.UpdateModifiers();
        yield return FixedTicks(2);
        if (worn && p.GetMaxAdrenaline() > 0f)
        {
            SelfTest.Pass(DisabledBarName, $"first game (mod on): {BronzeTrinket} worn for the restart, bar holds {F(p.GetMaxAdrenaline())}");
        }
        else
        {
            SelfTest.Fail(DisabledBarName, $"first game: could not wear {BronzeTrinket} for the restart");
        }
    }

    // Second game: Enabled = false in the cfg since before the start.
    private static IEnumerator DisabledBarCheck()
    {
        var rig = Rig.Create(DisabledBarName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(DisabledBarName);
        var p = rig.P;
        try
        {
            var plugin = Self();
            var view = FeatureRegistry.Find(ModInfo.Guid);
            c.Check(SelfTest.Stage == SelfTest.DisabledStage && plugin != null && !plugin.IsActive && view != null
                    && view.Value.State == nameof(ModState.Disabled) && view.Value.Status == "Off (disabled in settings).",
                $"started with Enabled = false: stage '{SelfTest.Stage}', state {(view != null ? view.Value.State : "?")}, status '{(view != null ? view.Value.Status : "?")}'");
            var mine = Marked(p, DisabledBarName);
            var item = mine.Count > 0 ? mine[0] : null;
            c.Check(item != null && ReferenceEquals(p.m_trinketItem, item),
                $"the trinket put on in the first game: {mine.Count} in the bag, worn {item != null && ReferenceEquals(p.m_trinketItem, item)} (expected still worn after the restart)");
            if (item != null && !ReferenceEquals(p.m_trinketItem, item))
            {
                p.EquipItem(item, false);
            }
            if (item == null)
            {
                item = EquipNamedTrinket(c, rig, BronzeTrinket);
            }
            if (!c.Check(item != null && item.m_shared.m_fullAdrenalineSE != null, "no trinket to check the bar with"))
            {
                c.Report();
                yield break;
            }
            p.UpdateModifiers();
            var hash = item.m_shared.m_fullAdrenalineSE.NameHash();
            rig.Effect(hash);
            var max = p.GetMaxAdrenaline();

            // The game's bar: fires at once, drains, no income, no key, no tooltip line.
            p.m_adrenaline = 0f;
            ConsoleAdrenaline(p, 999f);
            c.Check(Near(p.m_adrenaline, 0f) && rig.Has(hash), $"a full bar: bar {F(p.m_adrenaline)}, effect {rig.Has(hash)} (the game fires at once)");
            rig.Effect(hash);
            p.m_adrenaline = 20f;
            p.m_adrenalineDegenTimer = 0f;
            yield return new WaitForSeconds(1.5f);
            c.Check(p.m_adrenaline < 20f - 0.3f && max > 20f, $"bar 20 with the drain due: {F(p.m_adrenaline)} after 1.5 s (the game drains it)");
            CombatState.MarkExchange();
            p.m_adrenaline = 5f;
            p.m_adrenalineDegenTimer = 100f;
            yield return new WaitForSeconds(2.3f);
            c.Check(p.m_adrenaline <= 5f + 0.0001f, $"income in a fight: bar 5 -> {F(p.m_adrenaline)}");
            var bar = p.m_adrenaline;
            rig.MarkCenter();
            yield return Press();
            c.Check(FullBar.LastResult == TriggerResult.None && Near(p.m_adrenaline, bar) && !rig.Has(hash) && Rig.Center() == Rig.NoMessage,
                $"the trigger did something (answer {FullBar.LastResult}, message '{Rig.Center()}')");
            c.Check(!TipOf(item).Contains(TooltipMark), "the tooltip has the trigger line");
            CheckCleanLog(c, 0); // since this game started
        }
        finally
        {
            try
            {
                RemoveMarked(p, DisabledBarName);
                WearAgain(p, SelfTest.Carried(DisabledBarName + ".worn"));
            }
            catch (Exception e)
            {
                Log.Warning($"Self test clean-up (disabled start) failed: {e}");
            }
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.disabled.key (L02, real key) ----------

    private static IEnumerator DisabledKeyCheck()
    {
        var rig = Rig.Create(DisabledKeyName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(DisabledKeyName);
        var mark = LogMark();
        try
        {
            var p = rig.P;
            var plugin = Self();
            if (!InputReady(c) || !c.Check(plugin != null && !plugin.IsActive, "the mod is active: this is not a game started with Enabled = false"))
            {
                c.Report();
                yield break;
            }
            // Other test's trinket may still be worn (its check ran, or was filtered out): mine takes the slot, Rig
            // puts the old one back.
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            if (!c.Check(item != null && p.TakeInput(), "could not equip a trinket, or the player cannot take input"))
            {
                c.Report();
                yield break;
            }
            var hash = item.m_shared.m_fullAdrenalineSE.NameHash();
            rig.Effect(hash);
            rig.Moves();
            var seen = new bool[1];
            var keys = new List<KeyCode> { KeyCode.Y };
            var own = Plugin.TriggerKey != null ? Plugin.TriggerKey.Value : KeyCode.None;
            if (own != KeyCode.None && own != KeyCode.Y && Controls.IsUsableKey(own))
            {
                keys.Add(own); // player's own key setting too
            }
            foreach (var key in keys)
            {
                p.m_adrenaline = 20f;
                p.m_adrenalineDegenTimer = 100f; // game's drain held off for the compare
                rig.MarkCenter();
                FullBar.LastResult = TriggerResult.None;
                yield return Frames(2);
                yield return TapKey(key, seen);
                c.Check(seen[0], $"the game did not read the real {key} key press (input driver)");
                c.Check(FullBar.LastResult == TriggerResult.None && Near(p.m_adrenaline, 20f, 0.01f) && !rig.Has(hash) && Rig.Center() == Rig.NoMessage,
                    $"mod off since the start: a real {key} key press did something (answer {FullBar.LastResult}, bar {F(p.m_adrenaline)}, effect {rig.Has(hash)}, message '{Rig.Center()}')");
            }
            CheckCleanLog(c, mark);
        }
        finally
        {
            SelfTest.ReleaseInput();
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.disabled.first-log (L03) ----------

    // BepInEx log file line: "[Warning:Trinkets on Demand] text" (level padded to 7, source to 10). Lines without
    // that head belong to the entry above (stack traces).
    private static readonly Regex LogHead = new Regex(@"^\[(?<level>[A-Za-z]+)\s*:\s*(?<source>[^\]]*?)\s*\] ?(?<text>.*)$", RegexOptions.Compiled);

    // Tests that play TESTING.md T01 to T20 (ones their "Automated" notes name, single-player run). L03 talk about
    // log of that play-through: each must run to its end and pass in first game. Else filtered run (-Only) with
    // nothing played read as clean log.
    private static readonly string[] PlayThroughTests =
    {
        ParityName, HoldName, BlockFightName, IncomeName, TargetingName, CombatName, NoFightName, DrainName, FullBarName,
        AnswersName, GatesName, ControlsName, KeysName, InputPadName, InputLayoutsName, InputKeysName, FeedbackName,
        SwapName, BowHitsName, ShotName, RangedName, CrossbowShotName, OtherRangedName, RespawnName, RelogBarName,
    };

    private const string UnityLogSource = "Unity Log"; // BepInEx name for Unity's own messages in log file

    private static IEnumerator DisabledLogCheck()
    {
        var c = new Checks(DisabledLogName);
        var dir = AppDomain.CurrentDomain.GetData(SelfTest.RunDirSlot) as string;
        var path = string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, FirstLogFile);
        if (!c.Check(path != null && File.Exists(path),
                $"the copy of the first game's log was not found ({path ?? "no run folder"}): tools/Test-InWorld.ps1 writes it before the second game"))
        {
            c.Report();
            yield break;
        }
        string[] lines = null;
        string readError = null;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception e)
        {
            readError = e.Message;
        }
        if (!c.Check(lines != null, $"could not read {path}: {readError}"))
        {
            c.Report();
            yield break;
        }
        yield return null;

        var myCode = typeof(Plugin).Namespace + ".";
        var endHead = SelfTest.Prefix + " END ";
        var problems = new List<string>();
        var passedTests = new HashSet<string>();
        var mine = 0;
        var mineAfterDone = 0;
        var unityLines = 0;
        var doneAt = -1;
        var entryError = false; // entry above is an error of another source
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var head = LogHead.Match(line);
            if (!head.Success)
            {
                // Continuation (stack trace) of the entry above.
                if (entryError && line.IndexOf(myCode, StringComparison.Ordinal) >= 0)
                {
                    problems.Add($"line {i + 1}: error naming the mod's code: {FirstLine(line.Trim())}");
                    entryError = false; // one line per stack
                }
                continue;
            }
            var level = head.Groups["level"].Value;
            var source = head.Groups["source"].Value;
            var text = head.Groups["text"].Value;
            var bad = level == "Warning" || level == "Error" || level == "Fatal";
            var error = level == "Error" || level == "Fatal";
            entryError = false;
            if (text.IndexOf(SelfTest.Prefix + " DONE ", StringComparison.Ordinal) >= 0)
            {
                doneAt = i;
            }
            if (source == UnityLogSource)
            {
                unityLines++;
            }
            // Probe's line "[selftest] END <name>: passed (...)": that test ran to its end in first game.
            if (source != ModInfo.Name && text.StartsWith(endHead, StringComparison.Ordinal))
            {
                var colon = text.IndexOf(": ", endHead.Length, StringComparison.Ordinal);
                if (colon > endHead.Length && string.CompareOrdinal(text, colon + 2, "passed", 0, 6) == 0)
                {
                    passedTests.Add(text.Substring(endHead.Length, colon - endHead.Length));
                }
            }
            if (source == ModInfo.Name)
            {
                mine++;
                if (doneAt >= 0)
                {
                    mineAfterDone++;
                }
                // Results of the tests themselves ("[selftest] FAIL ...") are judged by the run, not here.
                if (bad && !text.StartsWith(SelfTest.Prefix, StringComparison.Ordinal) && !text.StartsWith(F13Warning, StringComparison.Ordinal))
                {
                    problems.Add($"line {i + 1}: {level}: {FirstLine(text)}");
                }
                continue;
            }
            if (text.IndexOf("Flash", StringComparison.Ordinal) >= 0 && text.IndexOf("does not exist", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                problems.Add($"line {i + 1}: Unity: {FirstLine(text)}");
                continue;
            }
            if (error)
            {
                if (text.IndexOf(myCode, StringComparison.Ordinal) >= 0)
                {
                    problems.Add($"line {i + 1}: error naming the mod's code: {FirstLine(text)}");
                }
                else
                {
                    entryError = true;
                }
            }
        }
        c.Note($"first game's log: {lines.Length} lines, {mine} from {ModInfo.Name}, {mineAfterDone} of them after the tests ended (the quit from the world)");
        foreach (var problem in problems.Take(10))
        {
            c.Note("log: " + problem);
        }
        c.Check(mine > 0 && doneAt >= 0, $"the first game's log does not hold a whole run ({mine} lines from {ModInfo.Name}, tests-ended line found {doneAt >= 0})");
        var notPlayed = PlayThroughTests.Where(t => !passedTests.Contains(t)).ToList();
        c.Check(notPlayed.Count == 0,
            $"{notPlayed.Count} of the {PlayThroughTests.Length} tests that play TESTING.md T01 to T20 did not run to their end and pass in the first game "
            + $"({string.Join(", ", notPlayed.Take(6).ToArray())}{(notPlayed.Count > 6 ? ", ..." : "")}): this log is not the log of a whole play-through "
            + "(run tools/Test-InWorld.ps1 without -Only, or with -Only trinkets.)");
        c.Check(unityLines > 0,
            "the first game's log file holds no line of Unity itself: BepInEx does not write them to the file ([Logging.Disk] WriteUnityLog = true "
            + "in BepInEx.cfg is needed), so a missing Flash parameter warning or an exception in the mod's code would not show here");
        c.Check(problems.Count == 0,
            $"{problems.Count} warning or error line(s) from {ModInfo.Name} (the F13 warning of the keys test aside), about a missing Flash "
            + "animator parameter or naming the mod's code in the whole first game, quit included"
            + (problems.Count > 0 ? ", first: " + problems[0] : ""));
        c.Report();
    }
}
#endif
