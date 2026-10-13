#if DEBUG
using System.Collections;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.DeepNorthAwakeningMod;

// Debug build only. This mod together with another MC mod loaded in the same game (every MC mod is, in a test run).
// The other mod is found by its GUID in the shared registry; never one of its types: what it does is read from the game
// (creature targets, the player's skill, the ZDO keys its own docs give). Not loaded and active = the test fails.
//   dn.x.morale         X01, Creature Morale: the player ranked as high as that mod can rank (a kill of every boss in the
//                       game's own kill table). Control: a Greydwarf never targets that player. An area Krigen does,
//                       keeps coming, fights on when hit, is never routed
//   dn.x.morale.nature  X01: with that player near, a Gammeltroll and a Barka each fight an area Jotun; the Gammeltroll
//                       then turns on the player like in the normal game (it is in none of Morale's lists)
//   dn.x.sneak          X02, Sneak Ambush: real crouch key; area Jotun do not hunt; one that has not noticed the player
//                       stays unaware with the player crouched right behind it; a sneak attack on it is the game's
//                       backstab and pays Sneak Ambush's Sneak XP
// (dn.x.kills, Creature Kill and Tame Counts, lives in SelfTestsMore.cs.)
internal static partial class SelfTests
{
    private const string MoraleName = "dn.x.morale";
    private const string MoraleNatureName = "dn.x.morale.nature";
    private const string SneakName = "dn.x.sneak";

    private const string MoraleGuid = "MC.Combat.Creatures.Morale";
    private const string SneakGuid = "MC.Combat.Sneak.Ambush";

    // What the other mods leave in the game (their own ZDO keys): the player's standing as Creature Morale publishes
    // it (byte 0 = layout 1, byte 1 = boss rank, 255 = none), a creature's "routed until", the time a creature last
    // paid sneak-attack XP.
    private static readonly int MoraleStandingKey = (MoraleGuid + ".Standing").GetStableHashCode();
    private static readonly int MoraleRoutKey = (MoraleGuid + ".RoutUntil").GetStableHashCode();
    private static readonly int SneakLastXpKey = (SneakGuid + ".LastXp").GetStableHashCode();
    private const int MoraleTopRank = 8;

    // A creature name no rule of any mod knows (the kill that makes Creature Morale publish again).
    private const string JunkKill = "$mc_dn_selftest";

    private static void RegisterOtherMods()
    {
        SelfTest.Register(MoraleName, RunMorale);
        SelfTest.Register(MoraleNatureName, RunMoraleNature);
        SelfTest.Register(SneakName, RunSneak);
    }

    private static void UnregisterOtherMods()
    {
        SelfTest.Unregister(MoraleName);
        SelfTest.Unregister(MoraleNatureName);
        SelfTest.Unregister(SneakName);
    }

    // ---------- tools ----------

    private static bool OtherActive(string guid)
    {
        var view = FeatureRegistry.Find(guid);
        return view != null && view.Value.IsActive;
    }

    // The character's numbers the tests here change (the game's kill tables and counters), copied.
    private sealed class SavedStats
    {
        internal Dictionary<PlayerStatType, float>[] Stats;
        internal Dictionary<string, float>[][] Kills;
    }

    private static SavedStats SaveStats(PlayerProfile profile)
    {
        var all = profile.m_playerStats;
        var saved = new SavedStats
        {
            Stats = new Dictionary<PlayerStatType, float>[all.Length],
            Kills = new Dictionary<string, float>[all.Length][],
        };
        for (var i = 0; i < all.Length; i++)
        {
            if (all[i] == null)
            {
                continue;
            }
            saved.Stats[i] = new Dictionary<PlayerStatType, float>(all[i].m_stats);
            saved.Kills[i] = new Dictionary<string, float>[all[i].m_enemyStats.Length];
            for (var k = 0; k < all[i].m_enemyStats.Length; k++)
            {
                if (all[i].m_enemyStats[k] != null)
                {
                    saved.Kills[i][k] = new Dictionary<string, float>(all[i].m_enemyStats[k]);
                }
            }
        }
        return saved;
    }

    private static void RestoreStats(PlayerProfile profile, SavedStats saved)
    {
        var all = profile.m_playerStats;
        for (var i = 0; i < all.Length && i < saved.Stats.Length; i++)
        {
            if (all[i] == null || saved.Stats[i] == null)
            {
                continue;
            }
            all[i].m_stats.Clear();
            foreach (var pair in saved.Stats[i])
            {
                all[i].m_stats[pair.Key] = pair.Value;
            }
            for (var k = 0; k < all[i].m_enemyStats.Length && k < saved.Kills[i].Length; k++)
            {
                if (all[i].m_enemyStats[k] == null || saved.Kills[i][k] == null)
                {
                    continue;
                }
                all[i].m_enemyStats[k].Clear();
                foreach (var pair in saved.Kills[i][k])
                {
                    all[i].m_enemyStats[k][pair.Key] = pair.Value;
                }
            }
        }
    }

    // Creature Morale reads the player's standing from the game's own kill table and publishes it after a kill
    // credited to the player. everyBoss = first give the table a kill of every boss the game has (prefabs marked as
    // boss with an order) and 100000 kills of each kind of the Jotun army: no player can stand higher toward them. Then
    // one credited kill through the game's own call (a name no rule knows): that mod publishes. Returns the boss rank
    // it left on the player (-1 = none).
    private static int PublishStanding(Player player, bool everyBoss)
    {
        var game = Game.instance;
        var scene = ZNetScene.instance;
        if (game == null || scene == null || player == null)
        {
            return -1;
        }
        var kills = game.GetPlayerProfile().m_playerStats[0].m_enemyStats[(int)KillModifiers.MixedAndTotal];
        if (everyBoss && kills != null)
        {
            foreach (var prefab in scene.m_prefabs)
            {
                var boss = prefab != null ? prefab.GetComponent<Character>() : null;
                if (boss == null || !boss.m_boss || boss.m_bossOrder <= 0 || string.IsNullOrEmpty(boss.m_name))
                {
                    continue;
                }
                if (!kills.TryGetValue(boss.m_name, out var n) || n < 1f)
                {
                    kills[boss.m_name] = 1f;
                }
            }
            // And more kills of every kind of the Jotun army than any kill step of that mod asks for.
            foreach (var name in Hostility.Army)
            {
                var prefab = scene.GetPrefab(name);
                var jotun = prefab != null ? prefab.GetComponent<Character>() : null;
                if (jotun != null && !string.IsNullOrEmpty(jotun.m_name))
                {
                    kills[jotun.m_name] = 100000f;
                }
            }
        }
        game.RPC_RegisterKill(0L, JunkKill, 0, (int)KillModifiers.MixedAndTotal, 1, true);
        var zdo = ZdoOf(player);
        var bytes = zdo != null ? zdo.GetByteArray(MoraleStandingKey) : null;
        return bytes != null && bytes.Length >= 3 && bytes[0] == 1 && bytes[1] != 255 ? bytes[1] : -1;
    }

    // The character's numbers as before, and Creature Morale told so (it publishes from the table as it is then).
    private static void EndStanding(PlayerProfile profile, SavedStats saved)
    {
        RestoreStats(profile, saved);
        PublishStanding(Player.m_localPlayer, false);
        RestoreStats(profile, saved);
    }

    // Area Jotun around a place that walk and fight on the ground with the game's usual brain (both Krigen, Hexen),
    // nearest first. calm = only those that have noticed nobody.
    private static List<Character> AreaFighters(Vector3 center, float radius, bool calm)
    {
        var list = new List<Character>();
        var krigen = Hostility.Krigen.GetStableHashCode();
        var dual = Hostility.KrigenDual.GetStableHashCode();
        var hexen = Hostility.Hexen.GetStableHashCode();
        foreach (var ch in TaggedNear(center, radius))
        {
            var prefab = Hostility.PrefabOf(ch);
            var ai = ch.GetComponent<MonsterAI>();
            if (ai == null || (prefab != krigen && prefab != dual && prefab != hexen))
            {
                continue;
            }
            if (calm && (ai.IsAlerted() || ai.m_targetCreature != null))
            {
                continue;
            }
            list.Add(ch);
        }
        list.Sort((a, b) => Flat(a.transform.position - center).sqrMagnitude.CompareTo(Flat(b.transform.position - center).sqrMagnitude));
        return list;
    }

    private static Vector3 Forward(Component c)
    {
        var f = Flat(c.transform.forward);
        return f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
    }

    private static void Face(Player player, Vector3 target)
    {
        var dir = Flat(target - player.transform.position);
        if (dir.sqrMagnitude < 0.01f)
        {
            return;
        }
        player.transform.rotation = Quaternion.LookRotation(dir.normalized);
        player.SetLookDir(dir.normalized);
    }

    private sealed class Chase
    {
        internal bool Kept = true;
        internal float Start;
        internal float Farthest;
        internal float Seconds;
    }

    // Watch a creature and the player: does it keep the player as its target, how far does it get.
    private static IEnumerator WatchChase(Character creature, MonsterAI ai, Player player, float seconds, Chase chase)
    {
        chase.Start = Flat(creature.transform.position - player.transform.position).magnitude;
        chase.Farthest = chase.Start;
        var start = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - start < seconds && creature != null && !creature.IsDead())
        {
            chase.Kept &= ai.m_targetCreature == player;
            chase.Farthest = Mathf.Max(chase.Farthest, Flat(creature.transform.position - player.transform.position).magnitude);
            yield return new WaitForSeconds(0.25f);
        }
        chase.Seconds = Time.realtimeSinceStartup - start;
    }

    // ---------- dn.x.morale ----------

    private static IEnumerator RunMorale()
    {
        var c = new Checks(MoraleName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var home = _home.Value;
        var clock = new Clock();
        var away = false;
        var point = Vector3.zero;
        var profile = Game.instance.GetPlayerProfile();
        var saved = SaveStats(profile);
        var spawned = new List<GameObject>();
        var changed = false;
        try
        {
            if (!c.Check(OtherActive(MoraleGuid), "Creature Morale is loaded and active in this run (not loaded: nothing of X01 was checked)"))
            {
                c.Report();
                yield break;
            }
            ServerRules.TestRules = Rules(r =>
            {
                r.StormShare = 0;
                r.NatureBandChance = 0;
            });
            WorldState.TestStones = 3;
            WorldState.Refresh();
            AreaSpawns.TestInvalidate();
            var haveArea = LandSlot(SlotMorale, out point, out var cell);
            changed = true;
            var rank = PublishStanding(player, true);
            if (!c.Check(haveArea && rank == MoraleTopRank,
                    $"a dry invaded Deep North area exists, and with a kill of every boss in the game's kill table Creature Morale ranks the player {rank} of {MoraleTopRank}"))
            {
                c.Report();
                yield break;
            }

            // Control, where the test starts: a weak creature never targets this player (in the normal game it would at
            // once: 9 m away, face to face).
            // Creature Morale read a player's standing at most once a second (its StandingCache): the old one may still
            // be in its memory. Me wait that out, else the Greydwarf can pick the player on its very first look and the
            // control fail for nothing.
            yield return new WaitForSeconds(1.2f);
            var fwd = Forward(player);
            var weak = Spawn("Greydwarf", player.transform.position + fwd * 9f, -fwd);
            spawned.Add(weak);
            yield return Frames(3);
            var greydwarf = weak != null ? weak.GetComponent<Character>() : null;
            var greydwarfAi = weak != null ? weak.GetComponent<MonsterAI>() : null;
            if (c.Check(greydwarf != null && greydwarfAi != null, "spawned a Greydwarf 9 m in front of the player"))
            {
                var hunted = false;
                var gap = Flat(greydwarf.transform.position - player.transform.position).magnitude;
                var farthest = gap;
                var until = Time.realtimeSinceStartup + 5f;
                while (Time.realtimeSinceStartup < until && greydwarf != null)
                {
                    hunted |= greydwarfAi.m_targetCreature == player;
                    farthest = Mathf.Max(farthest, Flat(greydwarf.transform.position - player.transform.position).magnitude);
                    yield return new WaitForSeconds(0.25f);
                }
                c.Check(greydwarf != null && !hunted,
                    $"control: for 5 s the Greydwarf never targets this player (Creature Morale: it is afraid; it went from {F(gap)} m to {F(farthest)} m away)");
            }
            KillAll(spawned);

            var box = new Box();
            away = true;
            yield return TeleportAndWait(player, point, TravelTimeout, box);
            if (c.Check(box.Ok, $"travelled to the invaded area {cell} at {F(point)}"))
            {
                var pos = player.transform.position;
                yield return FreshTimers(pos);
                yield return WaitTagged(pos, 250f, 4, clock.Cap(18f));
                var fighters = AreaFighters(pos, 250f, false);
                Character jotun = null;
                foreach (var ch in fighters)
                {
                    var hash = Hostility.PrefabOf(ch);
                    if (hash == Hostility.Krigen.GetStableHashCode() || hash == Hostility.KrigenDual.GetStableHashCode())
                    {
                        jotun = ch;
                        break;
                    }
                }
                if (c.Check(jotun != null, $"an area Krigen stands near the player ({Kinds(TaggedNear(pos, 250f))})"))
                {
                    var ai = jotun.GetComponent<MonsterAI>();
                    // In front of it, in plain view.
                    Put(player, jotun.transform.position + Forward(jotun) * 9f);
                    Face(player, jotun.transform.position);
                    var t0 = Time.realtimeSinceStartup;
                    yield return Until(() => jotun == null || ai.m_targetCreature == player, 8f);
                    c.Check(jotun != null && ai.m_targetCreature == player,
                        $"the top-ranked player 9 m in front of an area Krigen: it targets the player ({F(Time.realtimeSinceStartup - t0)} s): not afraid");
                    if (jotun != null)
                    {
                        var chase = new Chase();
                        yield return WatchChase(jotun, ai, player, 4f, chase);
                        c.Check(jotun != null && chase.Kept,
                            $"for {F(chase.Seconds)} s it keeps the player as its target (a creature that Creature Morale makes run has none; {F(chase.Start)} m apart at first, {F(chase.Farthest)} m at most)");
                    }
                    if (jotun != null && !jotun.IsDead())
                    {
                        // A hit by the player, small (the game itself makes some creatures flee at low health).
                        var hit = new HitData
                        {
                            m_point = jotun.GetCenterPoint(),
                            m_hitType = HitData.HitType.PlayerHit,
                            m_blockable = false,
                            m_dodgeable = false,
                        };
                        hit.m_damage.m_damage = jotun.GetMaxHealth() * 0.08f;
                        hit.SetAttacker(player);
                        var health = jotun.GetHealth();
                        jotun.Damage(hit);
                        var chase = new Chase();
                        yield return WatchChase(jotun, ai, player, 3f, chase);
                        c.Check(jotun != null && !jotun.IsDead() && jotun.GetHealth() < health && chase.Kept,
                            $"hit by the player (health {F(health)} -> {F(jotun != null ? jotun.GetHealth() : 0f)}): it fights on, the player stays its target "
                            + $"({F(chase.Start)} m at the hit, {F(chase.Farthest)} m at most in {F(chase.Seconds)} s)");
                        var zdo = ZdoOf(jotun);
                        c.Check(zdo != null && zdo.GetLong(MoraleRoutKey, 0L) == 0L, "Creature Morale never made it run (no rout on it)");
                        c.Note($"the game's own flee settings of {jotun.name.Replace("(Clone)", "")}: low health {F(ai.m_fleeIfLowHealth)}, "
                               + $"hurt and target out of reach {ai.m_fleeIfHurtWhenTargetCantBeReached}, not alerted {ai.m_fleeIfNotAlerted}");
                    }
                }

                // The other kinds too: a Hexen and an Elaking (that mod has a rank for the Elaking: no player reaches it).
                foreach (var kind in new[] { Hostility.Hexen, Hostility.Elaking })
                {
                    Character other = null;
                    var hash = kind.GetStableHashCode();
                    var best = float.MaxValue;
                    foreach (var ch in TaggedNear(player.transform.position, 300f))
                    {
                        var d = Flat(ch.transform.position - player.transform.position).sqrMagnitude;
                        if (Hostility.PrefabOf(ch) == hash && ch.GetComponent<MonsterAI>() != null && d < best)
                        {
                            other = ch;
                            best = d;
                        }
                    }
                    if (!c.Check(other != null && clock.Room(7f), $"an area {kind} is near the player and there is time left for it"))
                    {
                        continue;
                    }
                    var brain = other.GetComponent<MonsterAI>();
                    Put(player, other.transform.position + Forward(other) * 9f);
                    Face(player, other.transform.position);
                    var t1 = Time.realtimeSinceStartup;
                    yield return Until(() => other == null || brain.m_targetCreature == player, clock.Cap(7f));
                    var otherZdo = ZdoOf(other);
                    c.Check(other != null && brain.m_targetCreature == player && otherZdo != null && otherZdo.GetLong(MoraleRoutKey, 0L) == 0L,
                        $"the top-ranked player 9 m in front of an area {kind}: it targets the player ({F(Time.realtimeSinceStartup - t1)} s), "
                        + "Creature Morale does not make it run");
                }
            }
        }
        finally
        {
            KillAll(spawned);
            if (changed)
            {
                EndStanding(profile, saved);
            }
            ClearOverrides();
            SweepArea(player);
            PurgeTagged(point, 600f);
        }
        if (away)
        {
            yield return GoHome(c, player, home);
        }
        c.Report();
    }

    // ---------- dn.x.morale.nature ----------

    private sealed class Duel
    {
        internal bool Fought;
        internal float Seconds;
    }

    // A nature creature spawned face to face with an area Jotun: one targets the other within the time?
    private static IEnumerator WatchDuel(Character nature, Character jotun, float seconds, Duel duel)
    {
        var natureAi = nature.GetComponent<MonsterAI>();
        var jotunAi = jotun.GetComponent<MonsterAI>();
        var start = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - start < seconds && nature != null && jotun != null && !duel.Fought)
        {
            duel.Fought = (natureAi != null && natureAi.m_targetCreature == jotun) || (jotunAi != null && jotunAi.m_targetCreature == nature);
            yield return new WaitForSeconds(0.25f);
        }
        duel.Seconds = Time.realtimeSinceStartup - start;
    }

    private static IEnumerator RunMoraleNature()
    {
        var c = new Checks(MoraleNatureName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var home = _home.Value;
        var clock = new Clock();
        var away = false;
        var point = Vector3.zero;
        var profile = Game.instance.GetPlayerProfile();
        var saved = SaveStats(profile);
        var spawned = new List<GameObject>();
        var changed = false;
        try
        {
            if (!c.Check(OtherActive(MoraleGuid), "Creature Morale is loaded and active in this run (not loaded: nothing of X01 was checked)"))
            {
                c.Report();
                yield break;
            }
            ServerRules.TestRules = Rules(r =>
            {
                r.StormShare = 0;
                r.NatureBandChance = 0;
            });
            WorldState.TestStones = 3;
            WorldState.Refresh();
            AreaSpawns.TestInvalidate();
            var haveArea = LandSlot(SlotMorale, out point, out var cell);
            changed = true;
            var rank = PublishStanding(player, true);
            if (!c.Check(haveArea && rank == MoraleTopRank,
                    $"a dry invaded Deep North area exists, and Creature Morale ranks the player {rank} of {MoraleTopRank}"))
            {
                c.Report();
                yield break;
            }
            var box = new Box();
            away = true;
            yield return TeleportAndWait(player, point, TravelTimeout, box);
            if (c.Check(box.Ok, $"travelled to the invaded area {cell} at {F(point)}"))
            {
                var pos = player.transform.position;
                // Same area as dn.x.morale, which ran a minute ago and took its Jotun away: without this, timers of the
                // place are not due and no Jotun come.
                yield return FreshTimers(pos);
                yield return WaitTagged(pos, 250f, 4, clock.Cap(18f));
                var fighters = AreaFighters(pos, 250f, false);
                if (c.Check(fighters.Count > 0 && WorldState.HostilityOn, $"area Jotun stand near the player ({Kinds(TaggedNear(pos, 250f))}), nature fights back"))
                {
                    // Gammeltroll against the nearest one, Barka against the next (or the same one).
                    var first = fighters[0];
                    var second = fighters.Count > 1 ? fighters[1] : fighters[0];
                    var trollObject = Spawn(Hostility.Gammeltroll, first.transform.position + Forward(first) * 7f, -Forward(first));
                    spawned.Add(trollObject);
                    yield return Frames(3);
                    var troll = trollObject != null ? trollObject.GetComponent<Character>() : null;
                    if (c.Check(troll != null, "spawned a Gammeltroll face to face with an area Jotun"))
                    {
                        var duel = new Duel();
                        yield return WatchDuel(troll, first, clock.Cap(14f, 2), duel);
                        c.Check(duel.Fought,
                            $"with the top-ranked player near, the Gammeltroll and the area Jotun fight: one targets the other after {F(duel.Seconds)} s");
                        var zdo = ZdoOf(troll);
                        c.Check(zdo != null && zdo.GetLong(MoraleRoutKey, 0L) == 0L && (first == null || ZdoOf(first) == null || ZdoOf(first).GetLong(MoraleRoutKey, 0L) == 0L),
                            "Creature Morale made neither of them run");
                    }
                    if (second == null || second.IsDead())
                    {
                        second = first;
                    }
                    if (c.Check(second != null && !second.IsDead() && clock.Room(10f), "an area Jotun and time left for the Barka"))
                    {
                        var barkaObject = Spawn(Hostility.Barka, second.transform.position + Forward(second) * 7f, -Forward(second));
                        spawned.Add(barkaObject);
                        yield return Frames(3);
                        var barka = barkaObject != null ? barkaObject.GetComponent<Character>() : null;
                        if (c.Check(barka != null, "spawned a Barka face to face with an area Jotun"))
                        {
                            var duel = new Duel();
                            yield return WatchDuel(barka, second, clock.Cap(12f, 1), duel);
                            c.Check(duel.Fought, $"the Barka and the area Jotun fight too: one targets the other after {F(duel.Seconds)} s");
                            var barkaZdo = ZdoOf(barka);
                            c.Check(barkaZdo != null && barkaZdo.GetLong(MoraleRoutKey, 0L) == 0L, "Creature Morale did not make the Barka run (no rout on it)");
                        }
                    }

                    // In none of Morale's lists: toward the player the Gammeltroll is as in the normal game. Its Jotun
                    // taken away (before Kall: no kill), the player right in front of it: it turns on the player.
                    var trollAi = troll != null ? troll.GetComponent<MonsterAI>() : null;
                    if (c.Check(troll != null && !troll.IsDead() && trollAi != null && clock.Room(9f), "the Gammeltroll still stands and there is time left"))
                    {
                        DestroyAll(TaggedNear(pos, 400f));
                        yield return Frames(3);
                        Put(player, troll.transform.position + Forward(troll) * 10f);
                        Face(player, troll.transform.position);
                        var t0 = Time.realtimeSinceStartup;
                        yield return Until(() => troll == null || trollAi.m_targetCreature == player, clock.Cap(8f));
                        c.Check(troll != null && trollAi.m_targetCreature == player && BaseAI.IsEnemy(troll, player),
                            $"the Gammeltroll turns on the top-ranked player like in the normal game ({F(Time.realtimeSinceStartup - t0)} s): Morale's rule for a creature in none of its lists");
                    }
                }
            }
        }
        finally
        {
            KillAll(spawned);
            if (changed)
            {
                EndStanding(profile, saved);
            }
            ClearOverrides();
            SweepArea(player);
            PurgeTagged(point, 600f);
        }
        if (away)
        {
            yield return GoHome(c, player, home);
        }
        c.Report();
    }

    // ---------- dn.x.sneak ----------

    private static IEnumerator RunSneak()
    {
        var c = new Checks(SneakName);
        var player = Player.m_localPlayer;
        _home ??= player.transform.position;
        var home = _home.Value;
        var clock = new Clock();
        var away = false;
        var point = Vector3.zero;
        var sneak = player.m_skills.GetSkill(Skills.SkillType.Sneak);
        var level = sneak.m_level;
        var accumulator = sneak.m_accumulator;
        try
        {
            if (!c.Check(OtherActive(SneakGuid), "Sneak Ambush is loaded and active in this run (not loaded: nothing of X02 was checked)"))
            {
                c.Report();
                yield break;
            }
            ServerRules.TestRules = Rules(r =>
            {
                r.StormShare = 0;
                r.NatureBandChance = 0;
            });
            WorldState.TestStones = 3;
            WorldState.Refresh();
            AreaSpawns.TestInvalidate();
            var haveArea = LandSlot(SlotSneak, out point, out var cell);
            var bound = BoundKey("Crouch", out var key, out var path);
            var boundForward = BoundKey("Forward", out var forwardKey, out var forwardPath);
            if (!c.Check(haveArea, "a dry invaded Deep North area exists")
                || !c.Check(SelfTest.ReleaseInput() && bound && boundForward,
                    $"an input driver runs the game and the Crouch and Forward buttons are bound to keys the test can press (bound to '{path}' and '{forwardPath}')"))
            {
                c.Report();
                yield break;
            }
            var box = new Box();
            away = true;
            yield return TeleportAndWait(player, point, TravelTimeout, box);
            if (c.Check(box.Ok, $"travelled to the invaded area {cell} at {F(point)}"))
            {
                var pos = player.transform.position;
                // Sneaking from the first moment: the real crouch key.
                yield return SelfTest.KeyTap(key);
                yield return Until(() => player.IsCrouching(), 3f);
                c.Check(player.IsCrouching(), $"the {key} key (the Crouch button) makes the player sneak");
                yield return FreshTimers(pos);
                yield return WaitTagged(pos, 250f, 4, clock.Cap(18f));
                var around = TaggedNear(pos, 250f);
                var hunters = 0;
                foreach (var ch in around)
                {
                    var brain = ch.GetComponent<BaseAI>();
                    if (brain != null && brain.HuntPlayer())
                    {
                        hunters++;
                    }
                }
                c.Check(around.Count > 0 && hunters == 0, $"area Jotun around the sneaking player ({Kinds(around)}), none hunts the player from afar ({hunters})");

                // Right behind one that has noticed nobody. A creature can turn round by chance: up to three of them.
                Character victim = null;
                MonsterAI ai = null;
                var tried = 0;
                var walked = 0f;
                foreach (var ch in AreaFighters(pos, 250f, true))
                {
                    if (tried >= 3 || !clock.Room(6f))
                    {
                        break;
                    }
                    var brain = ch != null ? ch.GetComponent<MonsterAI>() : null;
                    if (brain == null || ch.IsDead() || brain.IsAlerted())
                    {
                        continue;
                    }
                    tried++;
                    // 10 m behind it, then the real Forward key for 1.5 s: the player sneaks up on its back.
                    Put(player, ch.transform.position - Forward(ch) * 10f);
                    Face(player, ch.transform.position);
                    yield return new WaitForSeconds(0.4f);
                    var from = player.transform.position;
                    SelfTest.Key(forwardKey, true);
                    yield return new WaitForSeconds(1.5f);
                    SelfTest.Key(forwardKey, false);
                    yield return new WaitForSeconds(0.6f);
                    walked = Mathf.Max(walked, Flat(player.transform.position - from).magnitude);
                    if (ch != null && !ch.IsDead() && !brain.IsAlerted() && brain.m_targetCreature != player && player.IsCrouching())
                    {
                        victim = ch;
                        ai = brain;
                        break;
                    }
                }
                c.Check(walked >= 0.7f, $"the {forwardKey} key (the Forward button) moves the sneaking player ({F(walked)} m in 1.5 s)");
                var gapNow = victim != null ? Flat(victim.transform.position - player.transform.position).magnitude : 0f;
                if (c.Check(victim != null,
                        $"sneaked up on the back of an area Jotun, {F(gapNow)} m from it: it does not notice the player (sneaking works on it; {tried} tried)"))
                {
                    var zdo = ZdoOf(victim);
                    var paidBefore = zdo != null ? zdo.GetLong(SneakLastXpKey, 0L) : 0L;
                    var stabbed = victim.m_backstabTime;
                    var health = victim.GetHealth();
                    // A sneak attack: a player's hit with a backstab bonus, through the game's damage path.
                    var hit = new HitData
                    {
                        m_point = victim.GetCenterPoint(),
                        m_dir = Flat(victim.transform.position - player.transform.position).normalized,
                        m_backstabBonus = 3f,
                        m_skill = Skills.SkillType.Knives,
                        m_hitType = HitData.HitType.PlayerHit,
                        m_blockable = false,
                        m_dodgeable = false,
                    };
                    hit.m_damage.m_slash = 5f;
                    hit.SetAttacker(player);
                    var levelBefore = sneak.m_level;
                    var accumulatorBefore = sneak.m_accumulator;
                    victim.Damage(hit);
                    // Read at once (the calls above run through to the player's skill before they return): sneaking
                    // itself trains Sneak too, a little every second.
                    var raised = sneak.m_level > levelBefore || sneak.m_accumulator > accumulatorBefore;
                    var gained = sneak.m_level > levelBefore ? "a Sneak level" : F(sneak.m_accumulator - accumulatorBefore) + " Sneak XP";
                    c.Check(victim != null && victim.m_backstabTime != stabbed && victim.GetHealth() < health,
                        $"a sneak attack on it is the game's backstab (health {F(health)} -> {F(victim != null ? victim.GetHealth() : 0f)})");
                    c.Check(raised && zdo != null && zdo.GetLong(SneakLastXpKey, 0L) != paidBefore,
                        $"Sneak Ambush pays for it like for any creature: {gained} at the hit, the creature is marked as having paid");
                    yield return Until(() => victim == null || ai.IsAlerted(), 3f);
                    c.Check(victim != null && ai.IsAlerted(), "after the hit the Jotun is alerted (it only notices now)");
                }
            }
        }
        finally
        {
            SelfTest.ReleaseInput();
            player.SetCrouch(false);
            sneak.m_level = level;
            sneak.m_accumulator = accumulator;
            ClearOverrides();
            SweepArea(player);
            PurgeTagged(point, 600f);
        }
        if (away)
        {
            yield return GoHome(c, player, home);
        }
        c.Report();
    }
}
#endif
