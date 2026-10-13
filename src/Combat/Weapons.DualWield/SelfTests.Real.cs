#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Bootstrap;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.WeaponsDualWieldMod;

// Debug build only. "The real thing" where the older tests took a shortcut (same class and helper as SelfTests.cs):
//   dual.real.death        T20: real death in a normal world (god mode off, the game's own respawn): both weapon in
//                          the tombstone, nothing equipped on the new player; the weapons taken out pair again
//   dual.real.death-keep   T20: the same with the world key DeathKeepEquip: the same pair on the new player
//   dual.real.sleep        T18: a real night's sleep in a claimed bed (the bed's own Use, the game's sleep and time
//                          skip): both weapons put away while lying, the same hand after waking
//   dual.real.barber       T18: the barber station's own Use: its window put the pair away; cancelled, then the same
//                          hands
//   dual.real.swim         T18: real water (vanilla teleport to the nearest deep water and back): swimming put both
//                          weapon away, nothing can be drawn there, after leaving the water the same hands
//   dual.real.swim-sheath  T33: a sword pair put away by swimming is crossed on the back like after R
//   dual.real.feast-water  T17: a feast eaten on a shore, then into deep water at once: the main weapon hidden for the
//                          eat no lost while swimming, after leaving the water the same hands
// Death recipe = MC Trinkets on Demand's trinkets.respawn (bag kept aside and given back to the new player, skills,
// food, place, death pin and death point put back). Sleep recipe = MC Creature Morale's morale.sleep (the test bed's
// roof, fire and wet checks answered "fine" by a temporary Harmony prefix: vanilla shelter rules, nothing of this mod;
// time of day, spawn point, sleep timers, Rested, weather put back). Water recipe = MC Swim Dive's live tests.
internal static partial class SelfTests
{
    private const string RealDeathName = "dual.real.death";
    private const string RealDeathKeepName = "dual.real.death-keep";
    private const string RealSleepName = "dual.real.sleep";
    private const string RealBarberName = "dual.real.barber";
    private const string RealSwimName = "dual.real.swim";
    private const string RealSwimSheathName = "dual.real.swim-sheath";
    private const string RealFeastWaterName = "dual.real.feast-water";

    // Vanilla teleport over a long way: at least 8 s, then the zone and the floor.
    private const float TravelTimeout = 35f;

    // ---------- helpers ----------

    // Player on a spot, still (no fall counted from the old height).
    private static void MoveTo(Player p, Vector3 position)
    {
        p.transform.position = position;
        if (p.m_body != null)
        {
            p.m_body.position = position;
            p.m_body.linearVelocity = Vector3.zero;
        }
        p.m_currentVel = Vector3.zero;
        p.m_maxAirAltitude = position.y;
    }

    private static void DestroyWorldObject(GameObject go)
    {
        if (go == null)
        {
            return;
        }
        var view = go.GetComponent<ZNetView>();
        if (view != null && view.IsValid() && ZNetScene.instance != null)
        {
            ZNetScene.instance.Destroy(go);
        }
        else
        {
            UnityEngine.Object.Destroy(go);
        }
    }

    // ---------- dual.real.death, dual.real.death-keep (T20) ----------

    private static IEnumerator RunRealDeath() => RealDeath(RealDeathName, false);

    private static IEnumerator RunRealDeathKeep() => RealDeath(RealDeathKeepName, true);

    // Bag given back to the player that spawns later (the test was cut short while the player was dead).
    private static IEnumerator GiveBackLater(Action<Player> giveBack, bool god)
    {
        var until = Time.realtimeSinceStartup + 180f;
        while (Time.realtimeSinceStartup < until)
        {
            var player = Player.m_localPlayer;
            if (player != null && !player.IsDead() && !player.IsTeleporting())
            {
                try
                {
                    giveBack(player);
                    player.SetGodMode(god);
                }
                catch (Exception e)
                {
                    Log.Warning($"Self test clean-up (bag back after a death) failed: {e}");
                }
                yield break;
            }
            yield return null;
        }
    }

    private static IEnumerator RealDeath(string name, bool keepEquip)
    {
        var c = new Checks(name);
        var old = Player.m_localPlayer;
        if (old == null || Game.instance == null || ZNet.instance == null || ZNetScene.instance == null
            || ObjectDB.instance == null || ZoneSystem.instance == null)
        {
            SelfTest.Fail(name, "no local player or no world");
            yield break;
        }
        yield return WaitIdle(old);
        var warnings = LogWatch.WarningLines().Length;
        var errors = LogWatch.ErrorLines().Length;
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
        var keySet = false;
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
                player.m_timeSinceDeath = sinceDeath;
                if (sinceDeath > player.m_hardDeathCooldown)
                {
                    player.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectSoftDeath, true);
                }
                MoveTo(player, position + Vector3.up * 0.1f);
                player.transform.rotation = rotation;
                if (player.m_body != null)
                {
                    player.m_body.rotation = rotation;
                }
                if (Minimap.instance != null)
                {
                    Minimap.instance.RemovePin(position, 3f);
                }
                world.m_haveDeathPoint = hadDeathPoint;
                world.m_deathPoint = deathPoint;
            }
            PutFoodsBack(player, foods, health);
        }

        try
        {
            ServerRules.TestRules = DualRules.Defaults;
            Controls.TestMainHandHeld = false;
            foreach (var view in NewGraves())
            {
                gravesBefore.Add(view);
            }
            var keys = ZoneSystem.instance;
            var plain = !keys.GetGlobalKey(GlobalKeys.DeathKeepEquip) && !keys.GetGlobalKey(GlobalKeys.DeathKeepInventory)
                        && !keys.GetGlobalKey(GlobalKeys.DeathDeleteItems) && !keys.GetGlobalKey(GlobalKeys.DeathDeleteUnequipped);
            c.Check(plain, "the world has no death rule of its own before the test (normal world: items go to the tombstone)");
            if (!plain)
            {
                c.Report();
                yield break;
            }

            // Bag aside, then only the pair, held.
            old.m_shownTutorials.Add("death");
            old.UnequipAllItems();
            old.GetInventory().RemoveAll();
            emptied = true;
            var sword = old.GetInventory().AddItem(Sword, 1, 1, 0, 0L, "", false);
            var axe = old.GetInventory().AddItem(Axe, 1, 1, 0, 0L, "", false);
            if (sword == null || axe == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            old.EquipItem(sword, false);
            old.EquipItem(axe, false);
            yield return null;
            yield return null;
            c.Check(Holds(old, sword, axe) && Hands.IsMarked(axe), $"before the death: {Sword} main, {Axe} off ({HandsText(old)})");
            if (keepEquip)
            {
                keys.SetGlobalKey(GlobalKeys.DeathKeepEquip);
                keySet = true;
                yield return WaitFor(() => keys.GetGlobalKey(GlobalKeys.DeathKeepEquip), 3f);
                c.Check(keys.GetGlobalKey(GlobalKeys.DeathKeepEquip), "world key DeathKeepEquip set for the test");
            }

            // Die like the console's "die", with god mode off.
            old.SetGodMode(false);
            var hit = new HitData { m_hitType = HitData.HitType.Self };
            hit.m_damage.m_damage = 99999f;
            old.Damage(hit);
            var died = Time.time;
            yield return WaitFor(() => old == null || old.IsDead(), 3f);
            if (old != null && !old.IsDead())
            {
                c.Check(false, "the player did not die");
                c.Report();
                yield break;
            }
            Player now = null;
            yield return WaitFor(() =>
            {
                now = Player.m_localPlayer;
                return now != null && !ReferenceEquals(now, old) && !now.IsDead();
            }, 70f);
            if (now == null || ReferenceEquals(now, old) || now.IsDead())
            {
                c.Check(false, "no new player 70 s after the death");
                c.Report();
                yield break;
            }
            SelfTest.Note(name, $"respawned {F2(Time.time - died)} s after the death");
            yield return WaitFor(() => !now.IsTeleporting() && ZNetScene.instance.IsAreaReady(now.transform.position), 20f);
            yield return new WaitForSeconds(1f);
            var newBag = now.GetInventory();
            var graves = NewGraves();

            if (!keepEquip)
            {
                // T20, normal world: both weapon in the tombstone, nothing equipped after the respawn.
                Inventory graveItems = null;
                foreach (var view in graves)
                {
                    var container = view.GetComponent<Container>();
                    var items = container != null ? container.GetInventory() : null;
                    if (items != null && items.NrOfItems() > 0)
                    {
                        graveItems = items;
                    }
                }
                var inGrave = graveItems != null ? graveItems.GetAllItems().ToList() : new List<ItemDrop.ItemData>();
                var graveSword = inGrave.FirstOrDefault(i => PrefabOf(i) == Sword);
                var graveAxe = inGrave.FirstOrDefault(i => PrefabOf(i) == Axe);
                c.Check(graves.Count == 1 && inGrave.Count == 2 && graveSword != null && graveAxe != null
                        && !graveSword.m_equipped && !graveAxe.m_equipped,
                    $"death in a normal world: both weapons are in the tombstone, not flagged equipped ({graves.Count} new "
                    + $"tombstone(s), {inGrave.Count} item(s): {string.Join(", ", inGrave.Select(PrefabOf).ToArray())})");
                c.Check(now.m_rightItem == null && now.m_leftItem == null && now.m_hiddenRightItem == null
                        && now.m_hiddenLeftItem == null && newBag.NrOfItems() == 0,
                    $"after the respawn nothing is equipped and the bag is empty ({HandsText(now)}, {newBag.NrOfItems()} item(s))");
                // What the screen draw: no weapon in the hands, none on the back.
                var bare = now.m_visEquipment;
                c.Check(bare != null && bare.m_currentRightItemHash == 0 && bare.m_currentLeftItemHash == 0
                        && bare.m_currentRightBackItemHash == 0 && bare.m_currentLeftBackItemHash == 0,
                    "the new character shows no weapon in the hands and none on the back");
                Invariant(c, now, "after the respawn");
                if (graveSword != null && graveAxe != null)
                {
                    SelfTest.Note(name, $"in the tombstone the former off-hand axe still carries the off-hand marker = {Hands.IsMarked(graveAxe)}");
                    // Taken out of the tombstone, the weapons pair again for the new player object.
                    graveItems.RemoveItem(graveSword);
                    graveItems.RemoveItem(graveAxe);
                    var taken = newBag.AddItem(graveSword) && newBag.AddItem(graveAxe);
                    now.EquipItem(graveSword, false);
                    now.EquipItem(graveAxe, false);
                    yield return null;
                    yield return null;
                    c.Check(taken && Holds(now, graveSword, graveAxe) && Hands.IsMarked(graveAxe) && !Hands.IsMarked(graveSword),
                        $"taken out of the tombstone, the two weapons pair again: sword main, axe off ({HandsText(now)})");
                    Invariant(c, now, "pair again after the respawn");
                }
            }
            else
            {
                // T20 with DeathKeepEquip: the same pair after the respawn (the new player load it from the save).
                var kept = newBag.GetAllItems().ToList();
                var keptSword = kept.FirstOrDefault(i => PrefabOf(i) == Sword);
                var keptAxe = kept.FirstOrDefault(i => PrefabOf(i) == Axe);
                c.Check(kept.Count == 2 && keptSword != null && keptAxe != null,
                    $"death with DeathKeepEquip: both weapons stay with the player ({kept.Count} item(s) in the bag)");
                if (keptSword != null && keptAxe != null)
                {
                    c.Check(Holds(now, keptSword, keptAxe) && keptSword.m_equipped && keptAxe.m_equipped
                            && Hands.IsMarked(keptAxe) && !Hands.IsMarked(keptSword),
                        $"death with DeathKeepEquip: the same pair after the respawn, sword main and axe off ({HandsText(now)})");
                    Invariant(c, now, "after the respawn with DeathKeepEquip");
                    var vis = now.m_visEquipment;
                    c.Check(vis != null && vis.m_currentRightItemHash == Sword.GetStableHashCode()
                            && vis.m_currentLeftItemHash == Axe.GetStableHashCode(),
                        "the new character shows the sword in the right hand and the axe in the left hand");
                    now.HideHandItems();
                    yield return null;
                    yield return null;
                    now.ShowHandItems();
                    yield return null;
                    yield return null;
                    c.Check(Holds(now, keptSword, keptAxe), $"put away and drawn again by the new player: the same hands ({HandsText(now)})");
                }
                var inGraves = graves.Select(v => v.GetComponent<Container>()).Where(k => k != null)
                    .Sum(k => k.GetInventory().NrOfItems());
                c.Check(inGraves == 0, $"death with DeathKeepEquip: no weapon went to a tombstone ({inGraves} item(s) in {graves.Count} tombstone(s))");
            }
            foreach (var view in NewGraves())
            {
                DestroyWorldObject(view.gameObject);
            }
            if (keySet)
            {
                keys.RemoveGlobalKey(GlobalKeys.DeathKeepEquip);
                keySet = false;
            }
            GiveBack(now);
            now.SetGodMode(god);
            yield return null;
            yield return null;
            var problems = LogProblemsSince(warnings, errors);
            c.Check(problems.Length == 0, $"no warning or error from {ModInfo.Name} through the death and the respawn ({problems.Length}: {string.Join(" | ", problems)})");
            c.Report();
        }
        finally
        {
            try
            {
                if (keySet && ZoneSystem.instance != null)
                {
                    ZoneSystem.instance.RemoveGlobalKey(GlobalKeys.DeathKeepEquip);
                }
                foreach (var view in NewGraves())
                {
                    DestroyWorldObject(view.gameObject);
                }
                Bench.ClearOverrides();
                Hands.ResetState();
                var player = Player.m_localPlayer;
                if (player != null && !player.IsDead())
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
                else if (emptied && !given)
                {
                    // Cut short while dead: the bag go back to the player the game spawns next.
                    var plugin = Chainloader.PluginInfos.TryGetValue(ModInfo.Guid, out var info) ? info.Instance as Plugin : null;
                    if (plugin != null)
                    {
                        plugin.StartCoroutine(GiveBackLater(GiveBack, god));
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning($"Self test clean-up (death) failed: {e}");
            }
        }
    }

    // ---------- dual.real.sleep (T18) ----------

    private static Bed _testBed;

    // Harmony prefix (test bed only): roof, fire, wet = fine.
    private static bool BedShelterFine(Bed __instance, ref bool __result)
    {
        if (ReferenceEquals(_testBed, null) || !ReferenceEquals(__instance, _testBed))
        {
            return true;
        }
        __result = true;
        return false;
    }

    private static IEnumerator RunRealSleep()
    {
        var c = new Checks(RealSleepName);
        var player = Player.m_localPlayer;
        var env = EnvMan.instance;
        var net = ZNet.instance;
        var game = Game.instance;
        var profile = game != null ? game.GetPlayerProfile() : null;
        if (player == null || env == null || net == null || game == null || profile == null || ZoneSystem.instance == null
            || ZRoutedRpc.instance == null)
        {
            SelfTest.Fail(RealSleepName, "no local player or no world");
            yield break;
        }
        yield return WaitIdle(player);
        var free = net.IsServer() && !game.m_sleeping && !env.IsTimeSkipping() && !player.IsSleeping() && !player.IsAttached()
                   && !env.m_debugTimeOfDay && env.m_dayLengthSec > 0;
        if (!free)
        {
            SelfTest.Fail(RealSleepName, "a sleep or time skip is running, the player is attached to something, the time of day is forced, "
                                         + "or this game does not run the world: no sleep test now");
            yield break;
        }
        Bench bench = null;
        Harmony bypass = null;
        var spot = player.transform.position;
        var facing = player.transform.rotation;
        var time = net.GetTimeSeconds();
        var length = (double)env.m_dayLengthSec;
        var day0 = env.GetDay(time);
        var hadSpawn = profile.HaveCustomSpawnPoint();
        var spawn = profile.GetCustomSpawnPoint();
        var wakeup = player.m_wakeupTime;
        var lastSleep = game.m_lastSleepTime;
        var rested = player.GetSEMan().GetStatusEffect(SEMan.s_statusEffectRested);
        var hadRested = rested != null;
        var restedTime = rested != null ? rested.m_time : 0f;
        var restedTtl = rested != null ? rested.m_ttl : 0f;
        var dream = CinematicsManager.m_dreamCinematic;
        var debugEnv = env.m_debugEnv;

        void SetTime(double t)
        {
            net.SetNetTime(t);
            var raw = Mathf.Clamp01((float)(t % length / length));
            env.m_smoothDayFraction = env.RescaleDayFraction(raw);
        }

        try
        {
            bench = new Bench(player);
            var sword = bench.Give(Sword);
            var axe = bench.Give(Axe);
            if (sword == null || axe == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            // A dream video waiting (a boss died earlier) would play in this sleep; rain would make the player wet.
            CinematicsManager.m_dreamCinematic = "";
            env.m_debugEnv = "Clear";
            bypass = new Harmony(ModInfo.Guid + ".selftest.bed");
            var prefix = new HarmonyMethod(typeof(SelfTests), nameof(BedShelterFine));
            bypass.Patch(AccessTools.Method(typeof(Bed), nameof(Bed.CheckExposure)), prefix: prefix);
            bypass.Patch(AccessTools.Method(typeof(Bed), nameof(Bed.CheckFire)), prefix: prefix);
            bypass.Patch(AccessTools.Method(typeof(Bed), nameof(Bed.CheckWet)), prefix: prefix);
            var bedGo = bench.SpawnAt("bed", Ground(spot - RightOf(player) * 2.5f), facing);
            yield return null;
            yield return null;
            var bed = bedGo != null ? bedGo.GetComponentInChildren<Bed>(true) : null;
            c.Check(bed != null, "a bed spawned");
            if (bed == null)
            {
                c.Report();
                yield break;
            }
            _testBed = bed;

            // Night, sleep cooldown over (30 s after a wake-up, 10 s between two sleeps), nobody hunting the player.
            SetTime(day0 * length + 0.95 * length);
            yield return WaitFor(() => EnvMan.IsNight() && !EnvMan.IsDay(), 5f);
            c.Check(EnvMan.IsNight() && !EnvMan.IsDay(), $"it is night for the test (night {EnvMan.IsNight()}, day {EnvMan.IsDay()})");
            var nowSeconds = net.GetTimeSeconds();
            player.m_wakeupTime = nowSeconds - 1000.0;
            game.m_lastSleepTime = nowSeconds - 1000.0;
            player.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectRested, true);
            yield return WaitFor(() => !player.IsSensed(), 10f);
            c.Check(!player.IsSensed(), "no creature senses the player (the bed would refuse)");

            bench.Pair(sword, axe);
            yield return WaitIdle(player);
            c.Check(Holds(player, sword, axe), $"pair in hand before the bed ({HandsText(player)})");

            // The bed's own Use: the first one claim it, the second one lie down.
            player.Interact(bed.gameObject, false, false);
            yield return WaitFor(() => bed.IsCurrent(), 3f);
            c.Check(bed.IsCurrent(), "the first use claims the bed and makes it the spawn point");
            yield return new WaitForSeconds(0.3f);
            player.Interact(bed.gameObject, false, false);
            yield return WaitFor(() => player.IsAttached() && player.InBed(), 2f);
            if (!player.IsAttached() || !player.InBed())
            {
                var answer = MessageHud.instance != null && MessageHud.instance.m_messageCenterText != null
                    ? MessageHud.instance.m_messageCenterText.text
                    : "";
                c.Check(false, $"the bed did not let the player lie down (it answered \"{answer}\")");
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
            c.Check(player.m_rightItem == null && player.m_leftItem == null && ReferenceEquals(player.m_hiddenRightItem, sword)
                    && ReferenceEquals(player.m_hiddenLeftItem, axe),
                $"lying in the bed puts both weapons away ({HandsText(player)})");

            // The game's own sleep: everybody in bed at night, time skipped to the morning, wake-up.
            var lay = Time.time;
            yield return WaitFor(() => player.IsSleeping(), 15f);
            if (!player.IsSleeping())
            {
                c.Check(false, $"the game did not start the sleep within 15 s of lying down (still lying {player.InBed()})");
                c.Report();
                yield break;
            }
            SelfTest.Note(RealSleepName, $"asleep {F2(Time.time - lay)} s after lying down");
            var slept = Time.time;
            yield return WaitFor(() => !player.IsSleeping(), 45f);
            var dayNow = env.GetDay(net.GetTimeSeconds());
            c.Check(!player.IsSleeping() && dayNow == day0 + 1,
                $"the player slept through the night and woke the next morning (awake {!player.IsSleeping()} after "
                + $"{F2(Time.time - slept)} s, day {day0} -> {dayNow})");
            yield return new WaitForSeconds(1f);
            if (player.IsAttached())
            {
                player.AttachStop();
                yield return null;
            }
            PutBack(player, spot, facing);
            yield return new WaitForSeconds(0.5f);
            var drawn = Holds(player, sword, axe);
            SelfTest.Note(RealSleepName, drawn
                ? "after getting up the weapons are in hand without R"
                : $"after getting up the weapons are still put away ({HandsText(player)}): R draws them");
            if (!drawn)
            {
                player.ShowHandItems();
                yield return null;
                yield return null;
            }
            c.Check(Holds(player, sword, axe) && Hands.IsMarked(axe) && !Hands.IsMarked(sword),
                $"after a real night's sleep: the same hands, sword main and axe off ({HandsText(player)})");
            Invariant(c, player, "after the sleep");
            yield return NoteIfStuck(RealSleepName, player, "the night's sleep");
            c.Report();
        }
        finally
        {
            _testBed = null;
            try
            {
                // Cut short while asleep: stop the skip and wake now, as vanilla's stop branch does.
                if (env != null && env.IsTimeSkipping())
                {
                    env.m_skipTime = false;
                }
                if (game != null && game.m_sleeping && ZRoutedRpc.instance != null)
                {
                    game.m_sleeping = false;
                    ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "SleepStop");
                }
                if (player != null && player.IsSleeping())
                {
                    player.SetSleeping(false);
                }
                if (player != null && player.IsAttached())
                {
                    player.AttachStop();
                }
                if (bypass != null)
                {
                    bypass.UnpatchSelf();
                }
                if (ZNet.instance != null && env != null)
                {
                    if (hadSpawn)
                    {
                        profile.SetCustomSpawnPoint(spawn);
                    }
                    else
                    {
                        profile.ClearCustomSpawnPoint();
                    }
                    SetTime(time);
                    game.m_lastSleepTime = lastSleep;
                    env.m_debugEnv = debugEnv;
                }
                CinematicsManager.m_dreamCinematic = dream ?? "";
                if (player != null)
                {
                    player.m_wakeupTime = wakeup;
                    var seman = player.GetSEMan();
                    if (!hadRested)
                    {
                        seman.RemoveStatusEffect(SEMan.s_statusEffectRested, true);
                    }
                    else
                    {
                        if (!seman.HaveStatusEffect(SEMan.s_statusEffectRested))
                        {
                            seman.AddStatusEffect(SEMan.s_statusEffectRested, true);
                        }
                        var again = seman.GetStatusEffect(SEMan.s_statusEffectRested);
                        if (again != null)
                        {
                            again.m_ttl = restedTtl;
                            again.m_time = restedTime;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning($"Self test clean-up (sleep) failed: {e}");
            }
            if (bench != null)
            {
                bench.TakeBack();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
            if (player != null)
            {
                PutBack(player, spot, facing);
            }
        }
    }

    // ---------- dual.real.barber (T18) ----------

    private static IEnumerator RunRealBarber()
    {
        var c = new Checks(RealBarberName);
        var player = Player.m_localPlayer;
        if (player == null || ZNetScene.instance == null || ZoneSystem.instance == null)
        {
            SelfTest.Fail(RealBarberName, "no local player or no world");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        var spot = player.transform.position;
        var facing = player.transform.rotation;
        var hair = player.GetHair();
        var beard = player.GetBeard();
        var hairColor = player.GetHairColor();
        try
        {
            bench = new Bench(player);
            var sword = bench.Give(Sword);
            var axe = bench.Give(Axe);
            if (sword == null || axe == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            var prefab = ZNetScene.instance.m_prefabs.FirstOrDefault(p => p != null && p.GetComponent<Piece>() != null
                                                                          && p.GetComponentInChildren<Barber>(true) != null);
            c.Check(prefab != null && PlayerCustomizaton.m_barberInstance != null,
                $"a barber station piece and the barber window exist in this game (piece {(prefab != null ? prefab.name : "none")}, "
                + $"window {PlayerCustomizaton.m_barberInstance != null})");
            if (prefab == null || PlayerCustomizaton.m_barberInstance == null)
            {
                c.Report();
                yield break;
            }
            var barberGo = bench.SpawnAt(prefab.name, Ground(spot + RightOf(player) * 1.2f), facing);
            yield return null;
            yield return null;
            var barber = barberGo != null ? barberGo.GetComponentInChildren<Barber>(true) : null;
            c.Check(barber != null && barber.m_attachPoint != null, $"the barber station spawned ({prefab.name})");
            if (barber == null || barber.m_attachPoint == null)
            {
                c.Report();
                yield break;
            }
            var away = Vector3.Distance(player.transform.position, barber.m_attachPoint.position);
            bench.Pair(sword, axe);
            yield return WaitIdle(player);
            yield return WaitMinor(player);
            c.Check(Holds(player, sword, axe) && away < barber.m_useDistance,
                $"pair in hand, {F2(away)} m from the barber's seat (use distance {F2(barber.m_useDistance)} m; {HandsText(player)})");

            // The station's own Use (Player.Interact -> Barber.Interact): seated, the barber window open and put the
            // weapon away.
            player.Interact(barber.gameObject, false, false);
            yield return WaitFor(() => player.IsAttached() && PlayerCustomizaton.IsBarberGuiVisible(), 3f);
            var seated = player.IsAttached();
            var window = PlayerCustomizaton.IsBarberGuiVisible();
            yield return new WaitForSeconds(0.5f);
            c.Check(seated && window, $"using the barber station seats the player and opens its window (seated {seated}, window {window})");
            c.Check(player.m_rightItem == null && player.m_leftItem == null && ReferenceEquals(player.m_hiddenRightItem, sword)
                    && ReferenceEquals(player.m_hiddenLeftItem, axe),
                $"the barber puts both weapons away ({HandsText(player)})");

            // Its Cancel button: hair as before, the player stand up.
            if (PlayerCustomizaton.IsBarberGuiVisible())
            {
                PlayerCustomizaton.m_barberInstance.OnCancel();
            }
            yield return WaitFor(() => !player.IsAttached() && !PlayerCustomizaton.IsBarberGuiVisible(), 2f);
            c.Check(!player.IsAttached() && !PlayerCustomizaton.IsBarberGuiVisible(), "Cancel closes the window and the player stands up");
            PutBack(player, spot, facing);
            yield return new WaitForSeconds(0.5f);
            var drawn = Holds(player, sword, axe);
            SelfTest.Note(RealBarberName, drawn
                ? "after the barber the weapons are in hand without R"
                : $"after the barber the weapons are still put away ({HandsText(player)}): R draws them");
            if (!drawn)
            {
                player.ShowHandItems();
                yield return null;
                yield return null;
            }
            c.Check(Holds(player, sword, axe) && Hands.IsMarked(axe) && !Hands.IsMarked(sword),
                $"after the barber: the same hands, sword main and axe off ({HandsText(player)})");
            Invariant(c, player, "after the barber");
            yield return NoteIfStuck(RealBarberName, player, "the barber station");
            c.Report();
        }
        finally
        {
            try
            {
                if (PlayerCustomizaton.IsBarberGuiVisible())
                {
                    PlayerCustomizaton.m_barberInstance.OnCancel();
                }
                if (player.IsAttached())
                {
                    player.ResetAttachCameraPoint();
                    player.AttachStop();
                }
                player.SetHair(hair);
                player.SetBeard(beard);
                player.SetHairColor(hairColor);
            }
            catch (Exception e)
            {
                Log.Warning($"Self test clean-up (barber) failed: {e}");
            }
            if (bench != null)
            {
                bench.TakeBack();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
            PutBack(player, spot, facing);
        }
    }

    // ---------- real water (dual.real.swim, dual.real.swim-sheath, dual.real.feast-water) ----------

    // Result of the last Travel.
    private static bool _tripOk;
    private static string _tripDetail = "";

    // First thing of a water test: a test before may still be sending the player home (a test cut short leave that
    // to the game).
    private static IEnumerator SettleTrip()
    {
        var t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 40f)
        {
            var player = Player.m_localPlayer;
            if (player == null || ZNetScene.instance == null
                || (!player.IsTeleporting() && ZNetScene.instance.IsAreaReady(player.transform.position)))
            {
                break;
            }
            yield return null;
        }
    }

    // Vanilla teleport (loads the far zones, wait for the floor).
    private static IEnumerator Travel(Player p, Vector3 target, Quaternion rotation)
    {
        _tripOk = false;
        _tripDetail = "";
        var start = Time.time;
        while (!p.TeleportTo(target, rotation, true))
        {
            if (Time.time - start > 6f)
            {
                _tripDetail = "Player.TeleportTo refused for 6 s";
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
        }
        yield return null;
        while (p != null && p.IsTeleporting())
        {
            if (Time.time - start > TravelTimeout)
            {
                _tripDetail = $"still teleporting after {F2(TravelTimeout)} s";
                yield break;
            }
            yield return new WaitForSeconds(0.25f);
        }
        _tripOk = true;
        _tripDetail = $"{F2(Time.time - start)} s";
    }

    // Test cut short away from home: the player is sent back (vanilla finish the teleport after the test).
    private static void SendHome(Player p, Vector3 origin, Quaternion rotation)
    {
        if (p == null)
        {
            return;
        }
        if (p.IsTeleporting())
        {
            p.m_teleportTargetPos = origin;
            p.m_teleportTargetRot = rotation;
        }
        else if (!p.TeleportTo(origin, rotation, true))
        {
            // Teleport cooldown: put there directly (the area around the spawn load again around the player).
            MoveTo(p, origin + Vector3.up * 0.5f);
        }
    }

    private static bool WaterBiome(Heightmap.Biome biome) => biome != Heightmap.Biome.AshLands && biome != Heightmap.Biome.DeepNorth;

    // Nearest water whose floor is between min and max m under the water level (WorldGenerator heights, no zone
    // needed), the 4 points 'around' m away at least 'min' - 1 m deep; not Ashlands (hot water) nor Deep North.
    private static bool FindWater(Vector3 origin, float min, float max, float around, out Vector3 spot, out float depth)
    {
        var gen = WorldGenerator.instance;
        var level = ZoneSystem.instance.m_waterLevel;
        for (var r = 40f; r <= 5000f; r += 20f)
        {
            var step = r < 800f ? 4 : 8;
            for (var a = 0; a < 360; a += step)
            {
                var x = origin.x + Mathf.Sin(a * Mathf.Deg2Rad) * r;
                var z = origin.z + Mathf.Cos(a * Mathf.Deg2Rad) * r;
                if (x * x + z * z > 9500f * 9500f)
                {
                    continue;
                }
                var d = level - gen.GetHeight(x, z);
                if (d < min || d > max || !WaterBiome(gen.GetBiome(x, z)))
                {
                    continue;
                }
                if (level - gen.GetHeight(x + around, z) < min - 1f || level - gen.GetHeight(x - around, z) < min - 1f
                    || level - gen.GetHeight(x, z + around) < min - 1f || level - gen.GetHeight(x, z - around) < min - 1f)
                {
                    continue;
                }
                spot = new Vector3(x, level, z);
                depth = d;
                return true;
            }
        }
        spot = Vector3.zero;
        depth = 0f;
        return false;
    }

    // Deep water with dry, gentle ground 10-48 m from it (same heights): a shore to eat on and water to jump into,
    // both inside one loaded area.
    private static bool FindShore(Vector3 origin, out Vector3 land, out Vector3 water)
    {
        var gen = WorldGenerator.instance;
        var level = ZoneSystem.instance.m_waterLevel;
        for (var r = 40f; r <= 3000f; r += 20f)
        {
            var step = r < 800f ? 6 : 12;
            for (var a = 0; a < 360; a += step)
            {
                var x = origin.x + Mathf.Sin(a * Mathf.Deg2Rad) * r;
                var z = origin.z + Mathf.Cos(a * Mathf.Deg2Rad) * r;
                if (x * x + z * z > 9000f * 9000f)
                {
                    continue;
                }
                var d = level - gen.GetHeight(x, z);
                if (d < 3f || d > 40f || !WaterBiome(gen.GetBiome(x, z)))
                {
                    continue;
                }
                if (level - gen.GetHeight(x + 3f, z) < 2.5f || level - gen.GetHeight(x - 3f, z) < 2.5f
                    || level - gen.GetHeight(x, z + 3f) < 2.5f || level - gen.GetHeight(x, z - 3f) < 2.5f)
                {
                    continue;
                }
                for (var away = 10f; away <= 48f; away += 4f)
                {
                    for (var b = 0; b < 360; b += 30)
                    {
                        var lx = x + Mathf.Sin(b * Mathf.Deg2Rad) * away;
                        var lz = z + Mathf.Cos(b * Mathf.Deg2Rad) * away;
                        var h = gen.GetHeight(lx, lz) - level;
                        if (h < 1f || h > 10f || !WaterBiome(gen.GetBiome(lx, lz)))
                        {
                            continue;
                        }
                        var h1 = gen.GetHeight(lx + 2f, lz) - level;
                        var h2 = gen.GetHeight(lx - 2f, lz) - level;
                        var h3 = gen.GetHeight(lx, lz + 2f) - level;
                        var h4 = gen.GetHeight(lx, lz - 2f) - level;
                        if (Mathf.Min(Mathf.Min(h1, h2), Mathf.Min(h3, h4)) < 0.6f
                            || Mathf.Max(Mathf.Max(h1, h2), Mathf.Max(h3, h4)) - h > 1.2f)
                        {
                            continue;
                        }
                        land = new Vector3(lx, level + h, lz);
                        water = new Vector3(x, level, z);
                        return true;
                    }
                }
            }
        }
        land = Vector3.zero;
        water = Vector3.zero;
        return false;
    }

    private static Vector3 FlatOf(Vector3 v) => new Vector3(v.x, 0f, v.z);

    private static IEnumerator RunRealSwim() => RealSwim(RealSwimName, false);

    private static IEnumerator RunRealSwimSheath() => RealSwim(RealSwimSheathName, true);

    // Pair on land, vanilla teleport into deep water, swim, teleport home, R. sheath = a sword pair and the crossed
    // placement while swimming (T33) instead of the sword + axe pair (T18).
    private static IEnumerator RealSwim(string name, bool sheath)
    {
        yield return SettleTrip();
        var c = new Checks(name);
        var player = Player.m_localPlayer;
        if (player == null || ZoneSystem.instance == null || WorldGenerator.instance == null || ZNetScene.instance == null
            || player.m_visEquipment == null)
        {
            SelfTest.Fail(name, "no local player or no world");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        var origin = player.transform.position;
        var originRotation = player.transform.rotation;
        var travelled = false;
        var back = false;
        var wetBefore = player.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectWet);
        try
        {
            if (sheath)
            {
                _visName = name;
            }
            bench = new Bench(player);
            var main = bench.Give(Sword);
            var off = bench.Give(sheath ? Sword : Axe);
            if (main == null || off == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            if (!FindWater(origin, 4f, 40f, 6f, out var spot, out var mapDepth))
            {
                // Nothing swum = nothing proved: a fail, never a quiet pass.
                c.Check(false, "no water 4 m deep within 5 km of the start: swimming with a pair not tested");
                c.Report();
                yield break;
            }
            var level = ZoneSystem.instance.m_waterLevel;
            SelfTest.Note(name, $"water at ({F2(spot.x)}, {F2(spot.z)}), {F2(Vector3.Distance(FlatOf(spot), FlatOf(origin)))} m from "
                                + $"the start, about {F2(mapDepth)} m deep (WorldGenerator)");
            if (sheath)
            {
                BackCross.TestEnabled = true;
                BackCross.RebuildAll();
            }
            bench.Pair(main, off);
            yield return WaitIdle(player);
            yield return WaitMinor(player);
            c.Check(Holds(player, main, off), $"pair in hand on land ({HandsText(player)})");

            // Into the water.
            travelled = true;
            BackCross.ResetRecord();
            yield return Travel(player, new Vector3(spot.x, level + 1.5f, spot.z), Quaternion.identity);
            c.Check(_tripOk, "travel to the water: " + _tripDetail);
            if (!_tripOk)
            {
                c.Report();
                yield break;
            }
            yield return WaitFor(() => player.IsSwimming(), 12f);
            c.Check(player.IsSwimming() && player.InWater(),
                $"swimming in real water (feet {F2(player.transform.position.y)}, water level {F2(player.GetLiquidLevel())})");
            if (!player.IsSwimming())
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(1f);
            c.Check(player.m_rightItem == null && player.m_leftItem == null && ReferenceEquals(player.m_hiddenRightItem, main)
                    && ReferenceEquals(player.m_hiddenLeftItem, off) && !main.m_equipped && !off.m_equipped,
                $"swimming puts both weapons away ({HandsText(player)})");
            Invariant(c, player, "swimming");
            // The game draw nothing while swimming (its R needs ground); an equip there is refused, the pair stays put away.
            var equipped = player.EquipItem(main, false);
            yield return null;
            c.Check(!equipped && player.m_rightItem == null && ReferenceEquals(player.m_hiddenRightItem, main)
                    && ReferenceEquals(player.m_hiddenLeftItem, off),
                $"an equip while swimming is refused as in the game, the pair stays put away ({HandsText(player)})");

            if (sheath)
            {
                // T33: swim with a pair: placed the same way. The X is made when the back items are built with the body
                // upright; a body lying flat at that moment is tried again every few frames.
                var vis = player.m_visEquipment;
                var floated = Time.time;
                yield return WaitFor(() => BackCross.LastLayout == BackCross.Layout.Crossed, 6f);
                var right = vis.m_rightBackItemInstance;
                var left = vis.m_leftBackItemInstance;
                var joint = vis.m_backMelee;
                c.Check(BackCross.LastLayout == BackCross.Layout.Crossed,
                    $"swimming with a sword pair: crossed by the SetBackEquipped patch (layout {BackCross.LastLayout} after "
                    + $"{F2(Time.time - floated)} s afloat; {BackCross.LastRecord ?? "nothing placed"})");
                if (right != null && left != null && joint != null
                    && BackCross.Measure(right.transform, vis.transform.up, out var rs)
                    && BackCross.Measure(left.transform, vis.transform.up, out var ls))
                {
                    var r = right.transform;
                    var l = left.transform;
                    c.Check(r.parent == joint && l.parent == joint,
                        $"swimming: both swords on {joint.name} (main on {ParentName(r)}, off-hand on {ParentName(l)})");
                    var angle = Vector3.Angle(r.rotation * rs.Long, l.rotation * ls.Long);
                    c.Check(angle >= 2f * BackCross.MinAngle - 2f && angle <= 2f * BackCross.MaxAngle + 2f,
                        $"swimming: the two swords are crossed, {Deg(angle)} degrees between them (not on one spot)");
                    var gap = Vector3.Distance(r.TransformPoint(rs.Center), l.TransformPoint(ls.Center));
                    c.Check(gap <= BackCross.Lift + 0.01f, $"swimming: they cross at their centres ({Cm(gap)} cm apart)");
                }
                else
                {
                    c.Check(false, $"swimming: two measurable swords on the back (main {right != null}, off-hand {left != null})");
                }
                SelfTest.Screenshot(name, "swimming");
                yield return null;
                yield return null;
            }

            // Out of the water (home), then R.
            yield return Travel(player, origin, originRotation);
            back = _tripOk;
            c.Check(_tripOk, "travel back to the start: " + _tripDetail);
            yield return WaitFor(() => !player.IsSwimming() && player.IsOnGround(), 6f);
            yield return new WaitForSeconds(0.5f);
            c.Check(!player.IsSwimming() && player.IsOnGround(), "back on dry ground");
            var drawn = Holds(player, main, off);
            SelfTest.Note(name, drawn
                ? "after leaving the water the weapons are in hand without R"
                : $"after leaving the water the weapons are still put away ({HandsText(player)}): R draws them");
            if (!drawn)
            {
                player.ShowHandItems();
                yield return null;
                yield return null;
            }
            c.Check(Holds(player, main, off) && Hands.IsMarked(off) && !Hands.IsMarked(main),
                $"after swimming: the same hands ({HandsText(player)})");
            Invariant(c, player, "after swimming");
            yield return NoteIfStuck(name, player, "the swim");
            c.Report();
        }
        finally
        {
            _visName = VisualsName;
            if (travelled && !back)
            {
                SendHome(player, origin, originRotation);
            }
            if (!wetBefore && player != null)
            {
                player.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectWet, true);
            }
            if (bench != null)
            {
                bench.TakeBack();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
        }
    }

    // ---------- dual.real.feast-water (T17) ----------

    private static IEnumerator RunRealFeastWater()
    {
        yield return SettleTrip();
        var c = new Checks(RealFeastWaterName);
        var player = Player.m_localPlayer;
        if (player == null || ZoneSystem.instance == null || WorldGenerator.instance == null || ZNetScene.instance == null)
        {
            SelfTest.Fail(RealFeastWaterName, "no local player or no world");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        List<Player.Food> foods = null;
        var healthBefore = player.GetHealth();
        var origin = player.transform.position;
        var originRotation = player.transform.rotation;
        var travelled = false;
        var back = false;
        var wetBefore = player.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectWet);
        try
        {
            bench = new Bench(player);
            foods = new List<Player.Food>(player.m_foods);
            var sword = bench.Give(Sword);
            var axe = bench.Give(Axe);
            if (sword == null || axe == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            var scene = ZNetScene.instance;
            var feastPrefab = scene.GetPrefab("FeastMeadows");
            if (feastPrefab == null || feastPrefab.GetComponent<Feast>() == null)
            {
                feastPrefab = scene.m_prefabs.FirstOrDefault(p => p != null && p.GetComponent<Feast>() != null);
            }
            c.Check(feastPrefab != null, "a feast piece exists in the game");
            if (feastPrefab == null)
            {
                c.Report();
                yield break;
            }
            if (!FindShore(origin, out var land, out var water))
            {
                c.Check(false, "no shore (dry ground 10-48 m from water 3 m deep) within 3 km of the start: eating next to deep water not tested");
                c.Report();
                yield break;
            }
            var level = ZoneSystem.instance.m_waterLevel;
            SelfTest.Note(RealFeastWaterName, $"shore at ({F2(land.x)}, {F2(land.z)}), {F2(land.y - level)} m above the water, "
                                              + $"{F2(Vector3.Distance(FlatOf(land), FlatOf(origin)))} m from the start; water "
                                              + $"{F2(Vector3.Distance(FlatOf(land), FlatOf(water)))} m from it");
            bench.Pair(sword, axe);
            yield return WaitIdle(player);
            c.Check(Holds(player, sword, axe), $"pair in hand ({HandsText(player)})");

            // To the shore.
            travelled = true;
            yield return Travel(player, land + Vector3.up * 0.5f, Quaternion.identity);
            c.Check(_tripOk, "travel to the shore: " + _tripDetail);
            if (!_tripOk)
            {
                c.Report();
                yield break;
            }
            yield return WaitFor(() => player.IsOnGround() && !player.IsSwimming(), 6f);
            yield return new WaitForSeconds(0.5f);
            var dry = player.IsOnGround() && !player.IsSwimming() && Holds(player, sword, axe);
            c.Check(dry, $"standing on the shore with the pair in hand (on the ground {player.IsOnGround()}, swimming "
                         + $"{player.IsSwimming()}; {HandsText(player)})");
            if (!dry)
            {
                c.Report();
                yield break;
            }
            var shore = player.transform.position;
            var feastGo = bench.SpawnAt(feastPrefab.name, Ground(shore + Forward(player) * 1.1f), Quaternion.identity);
            var drop = feastGo != null ? feastGo.GetComponent<ItemDrop>() : null;
            if (drop != null)
            {
                drop.m_autoPickup = false;
                if (feastGo.GetComponent<Piece>() != null)
                {
                    // As when a player place it: a piece, no longer an item lying around.
                    drop.MakePiece();
                }
            }
            yield return new WaitForSeconds(0.3f);
            c.Check(feastGo != null, $"a feast ({feastPrefab.name}) stands on the shore");
            if (feastGo == null)
            {
                c.Report();
                yield break;
            }

            // Eat (the feast's own Use), then into the deep water at once.
            yield return WaitMinor(player);
            player.m_foods.Clear();
            player.Interact(HoverOf(feastGo), false, false);
            yield return null;
            var ate = player.m_foods.Count > 0;
            var hidden = player.m_useItemTime > 0f && player.m_rightItem == null;
            c.Check(ate, $"eating at the feast worked (foods now {player.m_foods.Count})");
            SelfTest.Note(RealFeastWaterName, $"main weapon hidden for the eat animation = {hidden} (eat timer {F2(player.m_useItemTime)} s)");
            MoveTo(player, new Vector3(water.x, level - Mathf.Max(0.5f, player.m_swimDepth - 0.3f), water.z));
            var eatingInWater = false;
            var waitedReturn = false;
            var until = Time.time + 6f;
            while (Time.time < until && (!player.IsSwimming() || player.m_useItemTime > 0f))
            {
                eatingInWater |= player.IsSwimming() && player.m_useItemTime > 0f;
                waitedReturn |= Hands.PendingEatReturn;
                yield return null;
            }
            yield return new WaitForSeconds(0.6f);
            waitedReturn |= Hands.PendingEatReturn;
            c.Check(player.IsSwimming() && player.InWater(),
                $"swimming in real water after the jump in (feet {F2(player.transform.position.y)}, water level {F2(player.GetLiquidLevel())})");
            SelfTest.Note(RealFeastWaterName, $"the eat was still running in the water = {eatingInWater}; the mod holds the main "
                                              + $"weapon's return until the water is left = {waitedReturn}");
            c.Check(player.m_rightItem == null && player.m_leftItem == null,
                $"in the water, the eat over: both hands empty ({HandsText(player)})");
            c.Check(ReferenceEquals(player.m_hiddenRightItem, sword) && ReferenceEquals(player.m_hiddenLeftItem, axe),
                $"in the water, the eat over: both weapons are still the player's put-away pair, the main weapon was not dropped "
                + $"from the hands for good ({HandsText(player)})");
            Invariant(c, player, "swimming after the feast");

            // Out of the water, back on the shore.
            MoveTo(player, shore + Vector3.up * 0.2f);
            yield return WaitFor(() => !player.IsSwimming() && player.IsOnGround(), 6f);
            yield return new WaitForSeconds(1f);
            c.Check(!player.IsSwimming() && player.IsOnGround(), "back on the shore");
            var drawn = Holds(player, sword, axe);
            SelfTest.Note(RealFeastWaterName, drawn
                ? "after leaving the water the weapons came back to the hands by themselves (the held eat return)"
                : $"after leaving the water the weapons are still put away ({HandsText(player)}): R draws them");
            if (!drawn)
            {
                player.ShowHandItems();
                yield return null;
                yield return null;
            }
            c.Check(Holds(player, sword, axe) && Hands.IsMarked(axe) && !Hands.IsMarked(sword),
                $"feast, then deep water: after leaving the water the same hands, sword main and axe off ({HandsText(player)})");
            Invariant(c, player, "after the feast and the water");

            // Home.
            yield return Travel(player, origin, originRotation);
            back = _tripOk;
            c.Check(_tripOk, "travel back to the start: " + _tripDetail);
            yield return WaitFor(() => player.IsOnGround(), 6f);
            c.Check(Holds(player, sword, axe), $"back at the start: the same hands ({HandsText(player)})");
            yield return NoteIfStuck(RealFeastWaterName, player, "the feast and the swim");
            c.Report();
        }
        finally
        {
            if (travelled && !back)
            {
                SendHome(player, origin, originRotation);
            }
            if (foods != null)
            {
                PutFoodsBack(player, foods, healthBefore);
            }
            if (!wetBefore && player != null)
            {
                player.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectWet, true);
            }
            if (bench != null)
            {
                bench.TakeBack();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
        }
    }
}
#endif
