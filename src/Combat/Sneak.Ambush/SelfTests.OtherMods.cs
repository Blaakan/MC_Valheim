#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.SneakAmbushMod;

// Debug build only (whole file). Checks with another MC mod running in the same game (every MC mod is loaded in a
// test run). The other mod is found by its GUID in the shared registry; me never use its types: only what the game
// shows (hands, hits, XP lines, the character's saved data), and for Creature Morale its own Debug self-test
// overrides through reflection (boss rank and default rules, memory only). Other mod not loaded and active = FAIL,
// never a pass without a check.
//   sneak.x-morale        X01 boss rank 6: arrow at an afraid Greydwarf that watches the player = no sneak attack, no
//                         XP (control at rank 0: it is one); knife from behind and arrow from the smoke pay
//   sneak.x-morale-smoke  X01 creatures with their AI on: afraid Greydwarf does not sense or run from the player moving
//                         in the smoke (control: it runs once the smoke is gone); two-star one shot from the smoke
//                         fights, another afraid one does not notice
//   sneak.x-tower         X02 real shield bash on an unaware Greydwarf: no sneak attack, no XP; bash from inside the
//                         smoke: it sees and fights
//   sneak.x-tower-ng      X02 New Game+ (world level 1): bash, then a real knife hit: no sneak attack, no XP
//   sneak.x-dual          X03 KnifeCopper + SwordIron pair: Smoke Screen puts the pair away, never in the off hand;
//                         real paired sneak attack: one message, XP once
//   sneak.x-moveset       X04 Smoke Screen in hand, a jump or a roll, then the attack: the plain throw
//   sneak.x-encyclopedia  X06 creature only ever looked at inside the smoke is not met; met once its bar shows
//   sneak.x-crossbow      X08 real shot with a loaded CrossbowArbalest: ranged sneak attack XP; the crossbow stays
//                         loaded when put away, and reloads after the shot
//   sneak.x-forge         X07 both mods active side by side, Smoke Screen recipe offered (the Forge of Potential
//                         itself is checked by Forge Idol Upgrades' own tests in the same game)
internal static partial class SelfTests
{
    private const string MoraleName = "sneak.x-morale";
    private const string MoraleSmokeName = "sneak.x-morale-smoke";
    private const string TowerName = "sneak.x-tower";
    private const string TowerNgName = "sneak.x-tower-ng";
    private const string DualName = "sneak.x-dual";
    private const string MovesetName = "sneak.x-moveset";
    private const string EncyclopediaName = "sneak.x-encyclopedia";
    private const string CrossbowName = "sneak.x-crossbow";
    private const string ForgeName = "sneak.x-forge";

    private const string MoraleGuid = "MC.Combat.Creatures.Morale";
    private const string MoraleNamespace = "MC.Combat.CreaturesMoraleMod";
    private const string TowerGuid = "MC.Combat.Shields.TowerWall";
    private const string DualGuid = "MC.Combat.Weapons.DualWield";
    private const string MovesetGuid = "MC.Combat.Weapons.Moveset";
    private const string EncyclopediaGuid = "MC.Exploration.Compendium.Encyclopedia";
    private const string CrossbowGuid = "MC.Combat.Crossbow.StaysLoaded";

    // Encyclopedia's record of creatures met, in the character's own saved data: "1", then one creature name per line.
    private const string EncyclopediaSeenKey = EncyclopediaGuid + ".Seen";

    private const string XpLineMelee = "Sneak attack: 7 Sneak XP.";
    private const string XpLineRanged = "Sneak attack: 3.5 Sneak XP (ranged).";

    private static void RegisterOtherMods()
    {
        SelfTest.Register(MoraleName, RunMorale);
        SelfTest.Register(MoraleSmokeName, RunMoraleSmoke);
        SelfTest.Register(TowerName, RunTower);
        SelfTest.Register(TowerNgName, RunTowerNg);
        SelfTest.Register(DualName, RunDual);
        SelfTest.Register(MovesetName, RunMoveset);
        SelfTest.Register(EncyclopediaName, RunEncyclopedia);
        SelfTest.Register(CrossbowName, RunCrossbow);
        SelfTest.Register(ForgeName, RunForge);
    }

    private static void UnregisterOtherMods()
    {
        SelfTest.Unregister(MoraleName);
        SelfTest.Unregister(MoraleSmokeName);
        SelfTest.Unregister(TowerName);
        SelfTest.Unregister(TowerNgName);
        SelfTest.Unregister(DualName);
        SelfTest.Unregister(MovesetName);
        SelfTest.Unregister(EncyclopediaName);
        SelfTest.Unregister(CrossbowName);
        SelfTest.Unregister(ForgeName);
    }

    // ---------- helpers ----------

    // Other MC mod loaded and active, or the test fails here (a skipped check must not read as proof).
    private static bool Needs(string test, string guid, string modName, string item)
    {
        if (ModActive(guid))
        {
            return true;
        }
        SelfTest.Fail(test, $"{modName} is not loaded and active: nothing of TESTING.md {item} was checked");
        return false;
    }

    private static string Hands(Player player)
    {
        var right = player.m_rightItem != null && player.m_rightItem.m_dropPrefab != null ? player.m_rightItem.m_dropPrefab.name : "nothing";
        var left = player.m_leftItem != null && player.m_leftItem.m_dropPrefab != null ? player.m_leftItem.m_dropPrefab.name : "nothing";
        return $"right hand {right}, left hand {left}";
    }

    private static void EmptyHands(Player player)
    {
        foreach (var held in new[] { player.m_rightItem, player.m_leftItem })
        {
            if (held != null)
            {
                player.UnequipItem(held, false);
            }
        }
    }

    // Player standing right behind a creature that looks away, facing its back. Not crouched (a bash is done standing).
    private static IEnumerator StandBehind(Rig rig, Character victim)
    {
        var player = rig.Player;
        var facing = Flat(victim.transform.forward);
        var reach = player.GetRadius() + victim.GetRadius() + 0.3f;
        var spot = victim.transform.position - facing * reach;
        if (ZoneSystem.instance.GetSolidHeight(spot, out var solid, 2))
        {
            spot.y = Mathf.Max(spot.y, solid);
        }
        MovePlayer(player, spot + Vector3.up * 0.05f);
        rig.MarkMoved();
        Face(rig, facing);
        yield return new WaitForSeconds(0.4f);
        Face(rig, facing);
    }

    // Like Swing, for a hit that may take no health (New Game+ armour eats a bash): landed = the game counted a hit of
    // this player on the creature (Character.RPC_Damage sets m_localPlayerHasHit first thing).
    private static IEnumerator SwingTouch(Rig rig, Character victim, Box result, float seconds = 4f)
    {
        var player = rig.Player;
        result.Ok = false;
        result.Detail = "";
        var wait = Time.time + 3f;
        while (Time.time < wait && player.InAttack())
        {
            yield return null;
        }
        Face(rig, victim.transform.position - player.transform.position);
        victim.m_localPlayerHasHit = false;
        if (!player.StartAttack(null, false))
        {
            result.Detail = "StartAttack refused";
            yield break;
        }
        var until = Time.time + seconds;
        while (Time.time < until)
        {
            yield return null;
            if (victim == null || victim.IsDead() || victim.m_localPlayerHasHit)
            {
                result.Ok = true;
                break;
            }
        }
        if (!result.Ok)
        {
            result.Detail = $"no hit within {F(seconds)} s (victim {F(Vector3.Distance(victim.transform.position, player.transform.position))} m away)";
        }
    }

    // Creature Morale's own self-test overrides, by reflection (its Debug build): boss rank of the local player and
    // its default rules, both in memory only. Close puts the real ones back.
    private sealed class MoraleHook
    {
        private PropertyInfo _standing;
        private ConstructorInfo _rank;
        private PropertyInfo _rules;
        private ConstructorInfo _defaults;

        internal string Problem = "";

        internal bool Ready => _standing != null && _rank != null && _rules != null && _defaults != null;

        internal static MoraleHook Open()
        {
            var hook = new MoraleHook();
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue(MoraleGuid, out var info) || info == null || info.Instance == null)
                {
                    hook.Problem = "its plugin is not loaded";
                    return hook;
                }
                const BindingFlags statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                const BindingFlags instances = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var assembly = info.Instance.GetType().Assembly;
                var standing = assembly.GetType(MoraleNamespace + ".Standing");
                var over = standing != null ? standing.GetNestedType("Override", BindingFlags.Public | BindingFlags.NonPublic) : null;
                var server = assembly.GetType(MoraleNamespace + ".ServerRules");
                var rules = assembly.GetType(MoraleNamespace + ".MoraleRules");
                hook._standing = standing != null ? standing.GetProperty("TestOverride", statics) : null;
                hook._rank = over != null ? over.GetConstructor(instances, null, new[] { typeof(int) }, null) : null;
                hook._rules = server != null ? server.GetProperty("TestRules", statics) : null;
                hook._defaults = rules != null ? rules.GetConstructor(instances, null, Type.EmptyTypes, null) : null;
                if (!hook.Ready)
                {
                    hook.Problem = "its self-test overrides (Standing.TestOverride, ServerRules.TestRules) were not found: a Release build of Creature Morale?";
                }
            }
            catch (Exception e)
            {
                hook.Problem = e.Message;
            }
            return hook;
        }

        internal void DefaultRules() => _rules.SetValue(null, _defaults.Invoke(null), null);

        internal void Rank(int rank) => _standing.SetValue(null, _rank.Invoke(new object[] { rank }), null);

        internal void Close(string test)
        {
            try
            {
                if (_standing != null)
                {
                    _standing.SetValue(null, null, null);
                }
                if (_rules != null)
                {
                    _rules.SetValue(null, null, null);
                }
            }
            catch (Exception e)
            {
                SelfTest.Fail(test, $"could not put Creature Morale's rank and rules back: {e.Message}");
            }
        }
    }

    // ---------- sneak.x-morale (X01, creatures that stand still) ----------

    private static IEnumerator RunMorale()
    {
        if (!Needs(MoraleName, MoraleGuid, "Creature Morale", "X01"))
        {
            yield break;
        }
        var c = new Checks(MoraleName);
        var rig = Rig.Create(MoraleName);
        if (rig == null)
        {
            yield break;
        }
        var morale = MoraleHook.Open();
        try
        {
            if (!c.Check(morale.Ready, $"Creature Morale cannot be given a boss rank for the test: {morale.Problem}"))
            {
                c.Report();
                yield break;
            }
            var player = rig.Player;
            var rules = XpRules(c);
            rules.ActivationDelay = 0f;
            rules.CloudDuration = 40f;
            ServerRules.TestRules = rules;
            SneakXp.TestShowMessage = true;
            Tap.Install();
            morale.DefaultRules();
            rig.SaveAllSkills();
            rig.SetSneak(20f);
            yield return Stand(player);
            yield return FreeHands(player);
            var place = new Spot();
            yield return FindOpenSpot(rig, 17f, false, (feet, d) => BareGround(Ground(feet + d * 15f), false), place);
            if (!place.Found)
            {
                c.Note("no open ground with 17 m free ahead found within 48 m: the scenery may be in the way of the arrows");
            }
            var dir = place.Dir;
            var origin = player.transform.position;
            rig.TakeControls();
            rig.Aim(dir, 0f);
            var skill = player.GetSkills().GetSkill(Skills.SkillType.Sneak);
            c.Check(player.GetCurrentBiome() == Heightmap.Biome.Meadows, $"the test stands in {player.GetCurrentBiome()}; TESTING.md X01 is about the Meadows");
            var shot = new Box();

            // Control, boss rank 0 (every creature hostile, as in the normal game): a Greydwarf that looks at the
            // standing player 15 m away and has not been alerted (AI off) takes a sneak attack that pays.
            morale.Rank(0);
            yield return Frames(2);
            var control = rig.Creature("Greydwarf", Ground(origin + dir * 15f), -dir, false);
            if (!c.Check(control != null, "could not spawn a Greydwarf"))
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(0.6f);
            c.Check(control.GetBaseAI().CanSeeTarget(player) && !control.GetBaseAI().IsAlerted(),
                "control: the Greydwarf 15 m in front does not see the standing player, or is alerted (scenery?)");
            var stab = control.m_backstabTime;
            Tap.Clear();
            yield return Shoot(rig, "Bow", "ArrowWood", control, 5f, 0f, shot);
            if (c.Check(shot.Ok && control != null, $"control: the arrow did not hit ({shot.Detail})"))
            {
                yield return Frames(2);
                c.Check(control.m_backstabTime != stab && Tap.Logged(XpLineRanged) == 1,
                    $"control (boss rank 0): the arrow at a Greydwarf that is not alerted was no sneak attack that pays (backstab {control.m_backstabTime != stab}, "
                    + $"'{Tap.FirstLogged("Sneak attack")}'): the rank 6 check below would prove nothing");
            }
            if (control != null)
            {
                rig.Remove(control.gameObject);
            }
            yield return Frames(3);

            // Boss rank 6: the same Greydwarf is afraid of the player. It watches him: no sneak attack, no Sneak XP.
            morale.Rank(6);
            yield return Frames(2);
            var watcher = rig.Creature("Greydwarf", Ground(origin + dir * 15f), -dir, false);
            if (c.Check(watcher != null, "could not spawn the watching Greydwarf"))
            {
                yield return new WaitForSeconds(0.6f);
                c.Check(watcher.GetBaseAI().CanSeeTarget(player) && !watcher.GetBaseAI().IsAlerted(),
                    "boss rank 6: the Greydwarf 15 m in front does not see the standing player, or is alerted");
                stab = watcher.m_backstabTime;
                var progress = skill.m_accumulator;
                var level = skill.m_level;
                Tap.Clear();
                yield return Shoot(rig, "Bow", "ArrowWood", watcher, 5f, 0f, shot);
                if (c.Check(shot.Ok && watcher != null, $"boss rank 6: the arrow did not hit the watching Greydwarf ({shot.Detail})"))
                {
                    yield return Frames(2);
                    c.Check(watcher.m_backstabTime == stab, "boss rank 6: the arrow at an afraid Greydwarf that watches the player was a sneak attack");
                    c.Check(Tap.Logged("Sneak XP") == 0 && Tap.Messages("Sneak attack") == 0 && Near(skill.m_accumulator, progress) && Near(skill.m_level, level),
                        $"boss rank 6: the arrow at an afraid Greydwarf that watches the player paid Sneak XP or showed the message ('{Tap.FirstLogged("Sneak")}', "
                        + $"'{Tap.MessageTexts("Sneak attack")}')");
                }
                if (watcher != null)
                {
                    rig.Remove(watcher.gameObject);
                }
            }
            yield return Frames(3);

            // Boss rank 6, crouched behind another one, out of its sight, KnifeFlint: a sneak attack that pays.
            var knife = rig.Give("KnifeFlint");
            var victim = rig.Creature("Greydwarf", Ground(origin + dir * 4f), dir, false);
            EmptyHands(player);
            if (c.Check(knife != null && victim != null && rig.Equip(knife), $"could not spawn a Greydwarf or equip a KnifeFlint ({Hands(player)})"))
            {
                yield return Frames(3);
                var behind = new Box();
                yield return GetBehind(rig, victim, behind);
                c.Check(behind.Ok && !victim.GetBaseAI().CanSeeTarget(player) && !victim.GetBaseAI().IsAlerted(),
                    "boss rank 6: the player crouched behind the Greydwarf is seen, or the Greydwarf is alerted");
                Tap.Clear();
                var swing = new Box();
                yield return Swing(rig, victim, swing);
                if (c.Check(swing.Ok, $"boss rank 6: the knife swing from behind did not hit ({swing.Detail})"))
                {
                    yield return Frames(2);
                    c.Check(Tap.Logged(XpLineMelee) == 1 && Tap.Messages("Sneak attack", MessageHud.MessageType.TopLeft) == 1,
                        $"boss rank 6: the knife hit from behind, out of the afraid Greydwarf's sight, did not pay as a sneak attack ('{Tap.FirstLogged("Sneak attack")}', "
                        + $"messages '{Tap.MessageTexts("Sneak attack")}')");
                }
            }
            if (victim != null)
            {
                rig.Remove(victim.gameObject);
            }
            EmptyHands(player);
            MovePlayer(player, origin + Vector3.up * 0.05f);
            yield return Stand(player);
            rig.Aim(dir, 0f);

            // Boss rank 6, standing inside a cloud, arrow at a third one outside it that faces the player.
            var slot = new CloudSlot();
            yield return rig.PutCloud(slot, origin);
            yield return WaitFor(() => slot.Cloud == null || slot.Cloud.IsActive(SmokeRegistry.Now, rules), 2f);
            var third = rig.Creature("Greydwarf", Ground(origin + dir * 9f), -dir, false);
            if (c.Check(slot.Cloud != null && third != null, "could not make a cloud or spawn the third Greydwarf"))
            {
                yield return new WaitForSeconds(0.6f);
                var ai = third.GetBaseAI();
                WithoutSmoke(ai, player, out var wouldSee, out _);
                c.Check(InSmoke(player, rules) && wouldSee && !ai.CanSeeTarget(player) && !ai.IsAlerted(),
                    $"boss rank 6: the Greydwarf 9 m away, outside the cloud, sees the player in it, or would not see him without smoke either (in smoke "
                    + $"{InSmoke(player, rules)}, seen without smoke {wouldSee})");
                Tap.Clear();
                yield return Shoot(rig, "Bow", "ArrowWood", third, 5f, 0f, shot);
                if (c.Check(shot.Ok && third != null, $"boss rank 6: the arrow from the smoke did not hit ({shot.Detail})"))
                {
                    yield return Frames(2);
                    c.Check(Tap.Logged(XpLineRanged) == 1,
                        $"boss rank 6: the arrow from inside the smoke at an afraid Greydwarf outside did not pay as a sneak attack ('{Tap.FirstLogged("Sneak attack")}')");
                }
            }
            NoProblems(c);
            c.Report();
        }
        finally
        {
            morale.Close(MoraleName);
            rig.Restore();
        }
    }

    // ---------- sneak.x-morale-smoke (X01, creatures with their AI on) ----------

    private static IEnumerator RunMoraleSmoke()
    {
        if (!Needs(MoraleSmokeName, MoraleGuid, "Creature Morale", "X01"))
        {
            yield break;
        }
        var c = new Checks(MoraleSmokeName);
        var rig = Rig.Create(MoraleSmokeName);
        if (rig == null)
        {
            yield break;
        }
        var morale = MoraleHook.Open();
        try
        {
            if (!c.Check(morale.Ready, $"Creature Morale cannot be given a boss rank for the test: {morale.Problem}"))
            {
                c.Report();
                yield break;
            }
            var player = rig.Player;
            var rules = XpRules(c);
            rules.ActivationDelay = 0f;
            rules.CloudDuration = 60f;
            ServerRules.TestRules = rules;
            Tap.Install();
            morale.DefaultRules();
            morale.Rank(6);
            rig.SaveAllSkills();
            rig.SetSneak(20f);
            yield return Stand(player);
            yield return FreeHands(player);
            var place = new Spot();
            yield return FindOpenSpot(rig, 14f, false, (feet, d) => Lane(feet, -d, 14f), place);
            if (!place.Found)
            {
                c.Note("no open ground with 14 m free ahead and behind found within 48 m: the scenery may be in the way");
            }
            var dir = place.Dir;
            var origin = player.transform.position;
            rig.TakeControls();
            var slot = new CloudSlot();
            yield return rig.PutCloud(slot, origin);
            var cloud = slot.Cloud;
            yield return WaitFor(() => cloud == null || cloud.IsActive(SmokeRegistry.Now, rules), 2f);
            if (!c.Check(cloud != null && InSmoke(player, rules), "the player is not inside an active cloud"))
            {
                c.Report();
                yield break;
            }

            // Afraid Greydwarf 7 m from the cloud's middle (3 m outside it, well inside its fear range), AI on. The
            // player walks, then runs around inside the cloud: it must not sense him, so it does not run.
            var e = rig.Creature("Greydwarf", Ground(origin + dir * 7f), -dir, true);
            if (!c.Check(e != null, "could not spawn the afraid Greydwarf"))
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(1f);
            var aiE = (MonsterAI)e.GetBaseAI();
            var heading = dir;
            var left = false;
            var sensed = false;
            var alerted = false;
            var wouldSee = false;
            var wouldHear = false;
            var move = Time.time;
            while (Time.time - move < 4f && e != null)
            {
                var off = player.transform.position - origin;
                off.y = 0f;
                if (off.magnitude > 1.8f)
                {
                    heading = -off.normalized;
                }
                rig.Aim(heading, 0f);
                player.SetControls(Vector3.forward, false, false, false, false, false, false, false, false, Time.time - move > 2f, false);
                yield return null;
                if (e == null)
                {
                    break;
                }
                left |= !InSmoke(player, rules);
                sensed |= aiE.CanSeeTarget(player) || aiE.CanHearTarget(player) || ReferenceEquals(aiE.m_targetCreature, player);
                alerted |= aiE.IsAlerted();
                if (!wouldSee || !wouldHear)
                {
                    WithoutSmoke(aiE, player, out var s, out var h);
                    wouldSee |= s;
                    wouldHear |= h;
                }
            }
            rig.Drive(Vector3.zero);
            c.Check(!left, "the walking and running player left the cloud: nothing proven");
            // Both senses must have had something to catch: the Greydwarf in sight of him, and him making noise (a
            // player who did not move makes none, and then the hearing half would pass with nothing to hear).
            c.Check(wouldSee, "without smoke the Greydwarf 7 m away would not see the player either (scenery?): nothing proven about its sight");
            c.Check(wouldHear, "without smoke the Greydwarf 7 m away would not hear the walking and running player either (did he move?): nothing proven "
                               + "about its hearing");
            c.Check(e != null && !sensed && !alerted,
                $"boss rank 6: the afraid Greydwarf outside the cloud sensed the player moving inside it ({sensed}) or was alerted ({alerted})");

            // Control: the smoke gone, the player moving the same way. Now it senses him and runs (it is afraid).
            if (e != null)
            {
                if (cloud != null)
                {
                    rig.Remove(cloud.gameObject);
                }
                slot.Cloud = null;
                yield return Frames(2);
                var from = Vector3.Distance(e.transform.position, player.transform.position);
                var fled = false;
                var struck = false;
                move = Time.time;
                while (Time.time - move < 7f && !fled && e != null)
                {
                    var off = player.transform.position - origin;
                    off.y = 0f;
                    if (off.magnitude > 1.8f)
                    {
                        heading = -off.normalized;
                    }
                    rig.Aim(heading, 0f);
                    player.SetControls(Vector3.forward, false, false, false, false, false, false, false, false, true, false);
                    yield return null;
                    if (e == null)
                    {
                        break;
                    }
                    fled |= Vector3.Distance(e.transform.position, player.transform.position) > from + 4f;
                    struck |= e.InAttack();
                }
                rig.Drive(Vector3.zero);
                c.Check(fled && !struck,
                    $"control: without the smoke the afraid Greydwarf did not run from the player within 7 s (ran {fled}, attacked {struck}): is it afraid at boss "
                    + "rank 6? The check in the smoke proves nothing then");
                if (e != null)
                {
                    rig.Remove(e.gameObject);
                }
            }
            MovePlayer(player, origin + Vector3.up * 0.05f);
            yield return Stand(player);
            yield return Frames(3);

            // From inside a new cloud, an arrow at an afraid two-star Greydwarf (120 health: it survives). It fights
            // the player through the smoke; another afraid one on the other side does not notice him.
            yield return rig.PutCloud(slot, origin);
            cloud = slot.Cloud;
            yield return WaitFor(() => cloud == null || cloud.IsActive(SmokeRegistry.Now, rules), 2f);
            var a = rig.Creature("Greydwarf", Ground(origin + dir * 10f), -dir, true);
            var b = rig.Creature("Greydwarf", Ground(origin - dir * 10f), dir, true);
            if (!c.Check(cloud != null && InSmoke(player, rules) && a != null && b != null, "could not make the second cloud or spawn two Greydwarfs"))
            {
                c.Report();
                yield break;
            }
            a.SetLevel(3);
            var aiA = (MonsterAI)a.GetBaseAI();
            var aiB = (MonsterAI)b.GetBaseAI();
            // The other one, watched from its first frame to the end of the fight. Afraid creature that notice the
            // player never take him as target: it run from him (Creature Morale clear the target of an afraid one), so
            // "noticed" = it see or hear him, what its fear ask; target alone could never fail here. Control (it would
            // see or hear him without smoke) taken in the same frames: at first it look at him, later its idle walk
            // may turn it away.
            var otherFound = false;
            var otherCould = false;
            void WatchOther()
            {
                if (b == null || !InSmoke(player, rules)
                    || Vector3.Distance(b.m_eye.position, SmokeRegistry.TracedPoint(player)) <= rules.InsideSightRange + 0.3f)
                {
                    return;
                }
                otherFound |= ReferenceEquals(aiB.m_targetCreature, player) || aiB.CanSeeTarget(player) || aiB.CanHearTarget(player);
                if (!otherCould)
                {
                    WithoutSmoke(aiB, player, out var seeB, out var hearB);
                    otherCould = seeB || hearB;
                }
            }
            var settleB = Time.time;
            while (Time.time - settleB < 1.2f)
            {
                yield return null;
                WatchOther();
            }
            if (!c.Check(a != null && b != null, "a Greydwarf went away before the shot"))
            {
                c.Report();
                yield break;
            }
            c.Check(!aiA.IsAlerted() && !ReferenceEquals(aiA.m_targetCreature, player) && !aiB.IsAlerted() && !ReferenceEquals(aiB.m_targetCreature, player),
                "an afraid Greydwarf outside noticed the player in the smoke before the shot");
            var bow = Shared("Bow");
            var arrow = Shared("ArrowWood");
            var noise = (bow != null && bow.m_attack != null ? bow.m_attack.m_attackHitNoise : 0f)
                        + (arrow != null && arrow.m_attack != null ? arrow.m_attack.m_attackHitNoise : 0f);
            var shot = new Box();
            yield return Shoot(rig, "Bow", "ArrowWood", a, 20f, noise, shot);
            if (c.Check(shot.Ok && a != null && !a.IsDead(), $"the arrow did not hit, or killed the two-star Greydwarf ({shot.Detail})"))
            {
                c.Check(AiMemory.IsRevealed(a.transform, player, SmokeRegistry.Now) && aiA.CanSeeTarget(player),
                    "the afraid Greydwarf that was shot does not see the player in the smoke");
                var from = Vector3.Distance(a.transform.position, player.transform.position);
                var came = false;
                var attacked = false;
                var watch = Time.time;
                while (Time.time - watch < 8f && !(came && attacked) && a != null && b != null)
                {
                    if (!InSmoke(player, rules))
                    {
                        // Pushed out by a hit: back into the smoke.
                        MovePlayer(player, origin + Vector3.up * 0.05f);
                    }
                    yield return null;
                    if (a == null || b == null)
                    {
                        break;
                    }
                    came |= Vector3.Distance(a.transform.position, player.transform.position) < from - 3f;
                    attacked |= a.InAttack();
                    WatchOther();
                }
                c.Check(a != null && ReferenceEquals(aiA.m_targetCreature, player) && aiA.IsAlerted(), "the shot Greydwarf does not have the player as its target");
                c.Check(came, "the shot Greydwarf did not come into the smoke within 8 s");
                c.Check(attacked, "the shot Greydwarf did not attack the player within 8 s");
                c.Check(otherCould, "without smoke the other afraid Greydwarf 10 m away would not have seen or heard the player at any moment either "
                                    + "(scenery?): nothing proven about it");
                c.Check(!otherFound, "the other afraid Greydwarf saw, heard or targeted the player in the smoke from more than 2.5 m");
            }
            NoProblems(c);
            c.Report();
        }
        finally
        {
            morale.Close(MoraleSmokeName);
            rig.Restore();
        }
    }

    // ---------- sneak.x-tower (X02) ----------

    private static IEnumerator RunTower()
    {
        if (!Needs(TowerName, TowerGuid, "Tower Shield Wall", "X02"))
        {
            yield break;
        }
        var c = new Checks(TowerName);
        var rig = Rig.Create(TowerName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            var rules = XpRules(c);
            rules.ActivationDelay = 0f;
            rules.CloudDuration = 40f;
            ServerRules.TestRules = rules;
            SneakXp.TestShowMessage = true;
            Tap.Install();
            rig.SaveAllSkills();
            rig.SetSneak(20f);
            yield return Stand(player);
            yield return FreeHands(player);
            var place = new Spot();
            yield return FindOpenSpot(rig, 12f, false, null, place);
            if (!place.Found)
            {
                c.Note("no open ground with 12 m free ahead found within 48 m: the scenery may be in the way");
            }
            var dir = place.Dir;
            var origin = player.transform.position;
            rig.TakeControls();
            var skill = player.GetSkills().GetSkill(Skills.SkillType.Sneak);
            var tower = rig.Give("ShieldWoodTower");
            EmptyHands(player);
            if (!c.Check(tower != null && rig.Equip(tower), $"could not equip a ShieldWoodTower ({Hands(player)})"))
            {
                c.Report();
                yield break;
            }
            yield return Frames(3);
            c.Check(ReferenceEquals(player.m_leftItem, tower) && player.m_rightItem == null, $"the tower shield is not held alone ({Hands(player)})");

            // A bash on an unaware Greydwarf from behind: no sneak attack, no Sneak XP.
            var a = rig.Creature("Greydwarf", Ground(origin + dir * 4f), dir, false);
            if (!c.Check(a != null, "could not spawn a Greydwarf"))
            {
                c.Report();
                yield break;
            }
            yield return Frames(3);
            yield return StandBehind(rig, a);
            c.Check(!a.GetBaseAI().IsAlerted(), "the Greydwarf is alerted before the bash");
            var stab = a.m_backstabTime;
            var progress = skill.m_accumulator;
            var level = skill.m_level;
            Tap.Clear();
            var swing = new Box();
            yield return Swing(rig, a, swing, 4f);
            if (c.Check(swing.Ok, $"the shield bash did not hit the Greydwarf ({swing.Detail}; {Hands(player)})"))
            {
                var staggered = new Box();
                yield return WaitFor(() => a == null || a.IsStaggering(), 0.6f, staggered);
                c.Check(a != null && staggered.Ok, "the hit was no shield bash (one bash staggers a Greydwarf at once, a punch does not)");
                c.Check(a == null || a.m_backstabTime == stab, "the shield bash on an unaware Greydwarf was a sneak attack");
                c.Check(Tap.Logged("Sneak XP") == 0 && Tap.Messages("Sneak attack") == 0 && Near(skill.m_accumulator, progress) && Near(skill.m_level, level),
                    $"the shield bash on an unaware Greydwarf paid Sneak XP or showed the message ('{Tap.FirstLogged("Sneak")}', '{Tap.MessageTexts("Sneak attack")}')");
            }
            if (a != null)
            {
                rig.Remove(a.gameObject);
            }
            yield return WaitFor(() => !player.InAttack(), 4f);
            // The mod's bash cooldown (2 s between two starts) well over before the next bash.
            yield return new WaitForSeconds(2.2f);

            // From inside a cloud, a bash on a Greydwarf just outside it: it sees the player and fights him.
            MovePlayer(player, origin + Vector3.up * 0.05f);
            yield return Stand(player);
            var slot = new CloudSlot();
            yield return rig.PutCloud(slot, origin);
            var cloud = slot.Cloud;
            yield return WaitFor(() => cloud == null || cloud.IsActive(SmokeRegistry.Now, rules), 2f);
            if (!c.Check(cloud != null, "could not make a cloud"))
            {
                c.Report();
                yield break;
            }
            // Player near the cloud's edge, inside; the Greydwarf an arm's length further out, facing him.
            var inside = Ground(cloud.Base + dir * (cloud.Radius - 0.5f));
            MovePlayer(player, inside + Vector3.up * 0.05f);
            rig.MarkMoved();
            Face(rig, dir);
            yield return new WaitForSeconds(0.4f);
            var b = rig.Creature("Greydwarf", inside, -dir, false);
            if (!c.Check(b != null, "could not spawn the second Greydwarf"))
            {
                c.Report();
                yield break;
            }
            var out1 = Ground(inside + dir * (player.GetRadius() + b.GetRadius() + 0.35f));
            Place(b, out1, -dir);
            yield return new WaitForSeconds(0.5f);
            Face(rig, b.transform.position - player.transform.position);
            var ai = (MonsterAI)b.GetBaseAI();
            WithoutSmoke(ai, player, out var wouldSee, out _);
            c.Check(InSmoke(player, rules) && !cloud.Contains(b.m_eye.position) && wouldSee && !ai.CanSeeTarget(player) && !ai.IsAlerted(),
                $"before the bash: player in the smoke {InSmoke(player, rules)}, Greydwarf's eye outside the cloud {!cloud.Contains(b.m_eye.position)}, it would see him "
                + $"without smoke {wouldSee}, it sees him {ai.CanSeeTarget(player)}, alerted {ai.IsAlerted()}");
            yield return Swing(rig, b, swing, 4f);
            if (c.Check(swing.Ok && b != null && !b.IsDead(), $"the shield bash from the smoke did not hit the Greydwarf, or killed it ({swing.Detail})"))
            {
                c.Check(AiMemory.IsRevealed(b.transform, player, SmokeRegistry.Now) && ai.CanSeeTarget(player),
                    "the Greydwarf bashed from inside the smoke does not see the player");
                // Its AI on from here: it comes back and fights.
                ai.enabled = true;
                var attacked = false;
                var watch = Time.time;
                while (Time.time - watch < 9f && !attacked && b != null)
                {
                    if (!InSmoke(player, rules))
                    {
                        MovePlayer(player, inside + Vector3.up * 0.05f);
                    }
                    yield return null;
                    if (b == null)
                    {
                        break;
                    }
                    attacked |= b.InAttack();
                }
                c.Check(b != null && ai.IsAlerted() && ReferenceEquals(ai.m_targetCreature, player), "the Greydwarf bashed from inside the smoke does not have the player as its target");
                c.Check(attacked, "the Greydwarf bashed from inside the smoke did not attack the player within 9 s");
            }
            NoProblems(c);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.x-tower-ng (X02, New Game+) ----------

    // World level 1 (the world modifier key, as Tower Shield Wall's own New Game+ test sets it; put back at the end):
    // creatures have armour that eats a bash's damage, so the game itself does not alert them; Tower Shield Wall does.
    private static IEnumerator RunTowerNg()
    {
        if (!Needs(TowerNgName, TowerGuid, "Tower Shield Wall", "X02 (New Game+)"))
        {
            yield break;
        }
        var c = new Checks(TowerNgName);
        var rig = Rig.Create(TowerNgName);
        if (rig == null)
        {
            yield break;
        }
        var zone = ZoneSystem.instance;
        var levelSet = false;
        var hadLevel = false;
        var levelBefore = 0f;
        try
        {
            var player = rig.Player;
            var rules = XpRules(c);
            ServerRules.TestRules = rules;
            SneakXp.TestShowMessage = true;
            Tap.Install();
            rig.SaveAllSkills();
            rig.SetSneak(20f);
            yield return Stand(player);
            yield return FreeHands(player);
            var place = new Spot();
            yield return FindOpenSpot(rig, 10f, false, null, place);
            var dir = place.Dir;
            var side = Vector3.Cross(Vector3.up, dir).normalized;
            var origin = player.transform.position;
            rig.TakeControls();
            var skill = player.GetSkills().GetSkill(Skills.SkillType.Sneak);

            hadLevel = zone.GetGlobalKey(GlobalKeys.WorldLevel, out levelBefore);
            levelSet = true;
            zone.SetGlobalKey(GlobalKeys.WorldLevel, 1f);
            yield return Frames(3);
            if (!c.Check(Game.m_worldLevel == 1, $"world level {Game.m_worldLevel} after setting the WorldLevel key to 1"))
            {
                c.Report();
                yield break;
            }
            var knife = rig.Give("KnifeFlint");
            var tower = rig.Give("ShieldWoodTower");
            EmptyHands(player);
            if (!c.Check(knife != null && tower != null && rig.Equip(knife), $"could not get a KnifeFlint and a ShieldWoodTower ({Hands(player)})"))
            {
                c.Report();
                yield break;
            }

            // Control: in this world a knife hit on an unaware Greydwarf from behind is a sneak attack that pays.
            var control = rig.Creature("Greydwarf", Ground(origin + dir * 4f), dir, false);
            if (!c.Check(control != null, "could not spawn a Greydwarf"))
            {
                c.Report();
                yield break;
            }
            yield return Frames(3);
            var behind = new Box();
            yield return GetBehind(rig, control, behind);
            Tap.Clear();
            var swing = new Box();
            yield return Swing(rig, control, swing);
            yield return Frames(2);
            c.Check(swing.Ok && Tap.Logged(XpLineMelee) == 1,
                $"control: at world level 1 a knife hit on an unaware Greydwarf did not pay as a sneak attack ({swing.Detail} '{Tap.FirstLogged("Sneak attack")}'): "
                + "the check after the bash would prove nothing");
            if (control != null)
            {
                rig.Remove(control.gameObject);
            }
            yield return WaitFor(() => !player.InAttack(), 3f);
            player.SetCrouch(false);
            yield return Stand(player);

            // The bash on another unaware Greydwarf, then the knife.
            EmptyHands(player);
            var b = rig.Creature("Greydwarf", Ground(origin + side * 5f + dir * 2f), dir, false);
            if (!c.Check(b != null && rig.Equip(tower), $"could not spawn the second Greydwarf or equip the tower shield ({Hands(player)})"))
            {
                c.Report();
                yield break;
            }
            yield return Frames(3);
            yield return StandBehind(rig, b);
            var ai = b.GetBaseAI();
            c.Check(!ai.IsAlerted(), "the second Greydwarf is alerted before the bash");
            var stab = b.m_backstabTime;
            var progress = skill.m_accumulator;
            var level = skill.m_level;
            Tap.Clear();
            yield return SwingTouch(rig, b, swing);
            if (c.Check(swing.Ok && b != null && !b.IsDead(), $"the shield bash did not reach the Greydwarf ({swing.Detail}; {Hands(player)})"))
            {
                c.Note($"after the bash at world level 1: alerted {ai.IsAlerted()}, staggering {b.IsStaggering()}, health {F(b.GetHealth())} of {F(b.GetMaxHealth())}");
                yield return WaitFor(() => !player.InAttack(), 4f);
                EmptyHands(player);
                yield return Frames(2);
                if (c.Check(rig.Equip(knife), $"could not take the knife after the bash ({Hands(player)}, {HandsText(player)})"))
                {
                    // The bash pushed it away: next to it again, then the knife.
                    var reach = player.GetRadius() + b.GetRadius() + 0.3f;
                    var toIt = Flat(b.transform.position - player.transform.position);
                    MovePlayer(player, b.transform.position - toIt * reach + Vector3.up * 0.05f);
                    Face(rig, toIt);
                    yield return new WaitForSeconds(0.4f);
                    yield return Swing(rig, b, swing);
                    yield return Frames(2);
                    if (c.Check(swing.Ok, $"the knife swing after the bash did not hit ({swing.Detail})"))
                    {
                        c.Check(b == null || b.m_backstabTime == stab, "the knife hit after the bash was a sneak attack");
                        c.Check(Tap.Logged("Sneak XP") == 0 && Tap.Messages("Sneak attack") == 0 && Near(skill.m_accumulator, progress) && Near(skill.m_level, level),
                            $"the bash or the knife hit after it paid Sneak XP or showed the message ('{Tap.FirstLogged("Sneak")}', '{Tap.MessageTexts("Sneak attack")}')");
                    }
                }
            }
            NoProblems(c);
            c.Report();
        }
        finally
        {
            try
            {
                if (levelSet && zone != null)
                {
                    if (hadLevel)
                    {
                        zone.SetGlobalKey(GlobalKeys.WorldLevel, levelBefore);
                    }
                    else
                    {
                        zone.RemoveGlobalKey(GlobalKeys.WorldLevel);
                    }
                }
            }
            catch (Exception e)
            {
                SelfTest.Fail(TowerNgName, $"could not put the world level back: {e.Message}");
            }
            rig.Restore();
        }
    }

    // ---------- sneak.x-dual (X03) ----------

    private static IEnumerator RunDual()
    {
        if (!Needs(DualName, DualGuid, "Dual Wielding", "X03"))
        {
            yield break;
        }
        var c = new Checks(DualName);
        var rig = Rig.Create(DualName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            var rules = XpRules(c);
            ServerRules.TestRules = rules;
            SneakXp.TestShowMessage = true;
            Tap.Install();
            rig.SaveAllSkills();
            rig.SetSneak(20f);
            yield return Stand(player);
            yield return FreeHands(player);
            var dir = ClearDirection(player, 8f, out _);
            var origin = player.transform.position;
            rig.TakeControls();
            var skill = player.GetSkills().GetSkill(Skills.SkillType.Sneak);
            var knife = rig.Give("KnifeCopper");
            var sword = rig.Give("SwordIron");
            if (!c.Check(knife != null && sword != null, "could not give a KnifeCopper and a SwordIron"))
            {
                c.Report();
                yield break;
            }

            bool Paired() => (ReferenceEquals(player.m_rightItem, knife) && ReferenceEquals(player.m_leftItem, sword))
                             || (ReferenceEquals(player.m_rightItem, sword) && ReferenceEquals(player.m_leftItem, knife));

            EmptyHands(player);
            yield return Frames(2);
            player.EquipItem(knife, false);
            player.EquipItem(sword, false);
            yield return new WaitForSeconds(0.3f);
            if (!c.Check(Paired(), $"KnifeCopper and SwordIron are not held as a pair ({Hands(player)})"))
            {
                c.Report();
                yield break;
            }

            // A Smoke Screen equipped while the pair is held.
            var smoke = player.GetInventory().AddItem(SmokeContent.ItemName, 1, 1, 0, 0L, "", false);
            var equipped = smoke != null && player.EquipItem(smoke, false);
            yield return Frames(3);
            c.Check(equipped && smoke != null && player.IsItemEquiped(smoke), $"the Smoke Screen could not be equipped while a pair is held ({Hands(player)})");
            c.Check(!ReferenceEquals(player.m_leftItem, smoke), $"the Smoke Screen went to the off hand ({Hands(player)})");
            c.Check(!player.IsItemEquiped(knife) && !player.IsItemEquiped(sword), $"equipping the Smoke Screen did not put the pair away ({Hands(player)})");

            // The pair again, then a real sneak attack with it.
            if (smoke != null && player.IsItemEquiped(smoke))
            {
                player.UnequipItem(smoke, false);
            }
            EmptyHands(player);
            yield return Frames(2);
            player.EquipItem(knife, false);
            player.EquipItem(sword, false);
            yield return new WaitForSeconds(0.3f);
            c.Check(Paired(), $"the pair could not be made again after the Smoke Screen ({Hands(player)})");
            var g = rig.Creature("Greydwarf", Ground(origin + dir * 4f), dir, false);
            if (!c.Check(g != null, "could not spawn a Greydwarf"))
            {
                c.Report();
                yield break;
            }
            yield return Frames(3);
            var behind = new Box();
            yield return GetBehind(rig, g, behind);
            c.Check(behind.Ok && Paired() && !g.GetBaseAI().IsAlerted(), $"not crouched behind an unaware Greydwarf with the pair in hand ({Hands(player)})");
            var progress = skill.m_accumulator;
            var level = skill.m_level;
            Tap.Clear();
            var swing = new Box();
            yield return Swing(rig, g, swing);
            if (c.Check(swing.Ok, $"the paired swing did not hit the Greydwarf ({swing.Detail})"))
            {
                // The whole swing over (the second hand strikes too), then counted.
                yield return WaitFor(() => !player.InAttack(), 3f);
                yield return new WaitForSeconds(0.3f);
                c.Check(Tap.Messages("Sneak attack", MessageHud.MessageType.TopLeft) == 1,
                    $"a paired sneak attack showed {Tap.Messages("Sneak attack", MessageHud.MessageType.TopLeft)} \"Sneak attack\" message(s), expected one: "
                    + Tap.MessageTexts("Sneak attack"));
                c.Check(Tap.Logged("to their game for Sneak XP") == 1 && Tap.Logged(XpLineMelee) == 1,
                    $"a paired sneak attack paid Sneak XP {Tap.Logged("Sneak attack: ")} time(s), expected once ('{Tap.FirstLogged("Sneak attack: ")}')");
                c.Check(skill.m_level > level || skill.m_accumulator > progress, "the Sneak skill did not gain from the paired sneak attack");
            }
            NoProblems(c);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.x-moveset (X04) ----------

    private static IEnumerator RunMoveset()
    {
        if (!Needs(MovesetName, MovesetGuid, "Weapon Moveset", "X04"))
        {
            yield break;
        }
        var c = new Checks(MovesetName);
        var rig = Rig.Create(MovesetName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            var rules = new AmbushRules();
            ServerRules.TestRules = rules;
            Tap.Install();
            yield return Stand(player);
            yield return FreeHands(player);
            var place = new Spot();
            yield return FindOpenSpot(rig, 16f, false, null, place);
            if (!place.Found)
            {
                c.Note("no open ground with 16 m free ahead found within 48 m: the throw may meet the scenery");
            }
            var dir = place.Dir;
            rig.TakeControls();
            var item = rig.GiveAndEquip(3);
            if (!c.Check(item != null && item.m_shared.m_attack != null, $"could not equip a Smoke Screen ({HandsText(player)})"))
            {
                c.Report();
                yield break;
            }
            var own = item.m_shared.m_attack.m_attackAnimation;
            foreach (var how in new[] { "jump", "roll" })
            {
                var ready = new Box();
                yield return WaitFor(() => player.IsOnGround() && !player.InAttack() && !player.InDodge(), 5f, ready);
                yield return FreeHands(player);
                if (!player.IsItemEquiped(item) && rig.GiveAndEquip(0) == null)
                {
                    c.Check(false, $"before the {how}: could not hold a Smoke Screen ({HandsText(player)})");
                    continue;
                }
                Face(rig, dir);
                rig.Aim(dir, 20f);
                yield return new WaitForSeconds(0.5f);
                var before = new HashSet<SmokeCloud>(SmokeRegistry.All);
                var stack = rig.SmokeCount();
                var did = new Box();
                if (how == "jump")
                {
                    player.Jump();
                    yield return WaitFor(() => !player.IsOnGround(), 1.5f, did);
                }
                else
                {
                    player.Dodge(dir);
                    yield return WaitFor(() => player.InDodge(), 2f, did);
                }
                if (!c.Check(ready.Ok && did.Ok, $"the player did not {how} (ready {ready.Ok}; {HandsText(player)})"))
                {
                    continue;
                }
                // The attack button from then on: the first attack the game lets start.
                var started = false;
                var midMove = false;
                var animation = "";
                var until = Time.time + 4f;
                while (Time.time < until && !started)
                {
                    midMove = how == "jump" ? !player.IsOnGround() : player.InDodge();
                    started = player.StartAttack(null, false);
                    if (!started)
                    {
                        yield return null;
                    }
                }
                if (!c.Check(started, $"after a {how}: no attack with the Smoke Screen started within 4 s ({HandsText(player)})"))
                {
                    continue;
                }
                animation = player.m_currentAttack != null ? player.m_currentAttack.m_attackAnimation : "";
                c.Check(animation == own, $"after a {how}: the attack that started is '{animation}', the Smoke Screen's own throw is '{own}'");
                var slot = new CloudSlot();
                yield return WaitForCloud(before, 6f, slot);
                c.Check(slot.Cloud != null && NewClouds(before).Count == 1, $"after a {how}: {NewClouds(before).Count} cloud(s) from the throw, expected one");
                c.Check(rig.SmokeCount() == stack - 1, $"after a {how}: stack {stack} -> {rig.SmokeCount()}, expected one Smoke Screen used");
                c.Note($"after a {how}: the throw started {(midMove ? "during" : "after")} the {how}");
                yield return WaitFor(() => !player.InAttack(), 3f);
            }
            NoProblems(c);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.x-encyclopedia (X06) ----------

    // Creature kinds for the "never met" check: the first one this character has not met (ground creatures; their
    // AI is off in the test). Names that are no prefab of this game are skipped.
    private static readonly string[] UnmetCandidates =
    {
        "Draugr_Elite", "Skeleton_Poison", "Goblin", "Fenring", "Wraith", "Ghost", "Surtling", "Blob", "Greyling", "Neck", "Boar",
    };

    private static bool EncyclopediaMet(Player player, string creatureName) =>
        player.m_customData.TryGetValue(EncyclopediaSeenKey, out var raw) && raw != null
        && raw.Split('\n').Any(line => line.TrimEnd('\r') == creatureName);

    private static IEnumerator RunEncyclopedia()
    {
        if (!Needs(EncyclopediaName, EncyclopediaGuid, "Encyclopedia", "X06"))
        {
            yield break;
        }
        var c = new Checks(EncyclopediaName);
        var rig = Rig.Create(EncyclopediaName);
        var hud = EnemyHud.instance;
        if (rig == null)
        {
            yield break;
        }
        if (hud == null)
        {
            SelfTest.Fail(EncyclopediaName, "no EnemyHud");
            yield break;
        }
        var hadRecord = false;
        string record = null;
        var recordRead = false;
        try
        {
            var player = rig.Player;
            var rules = new AmbushRules { ActivationDelay = 0f, CloudDuration = 40f };
            ServerRules.TestRules = rules;
            Tap.Install();
            yield return Stand(player);
            var place = new Spot();
            yield return FindOpenSpot(rig, 12f, false, null, place);
            var dir = place.Dir;
            var origin = player.transform.position;
            rig.TakeControls();
            rig.Aim(dir, 0f);

            // The character's record as it is (put back at the end: the test must not teach the character a creature).
            hadRecord = player.m_customData.TryGetValue(EncyclopediaSeenKey, out record);
            recordRead = true;
            string kind = null;
            string creatureName = null;
            foreach (var candidate in UnmetCandidates)
            {
                var prefab = ZNetScene.instance.GetPrefab(candidate);
                var character = prefab != null ? prefab.GetComponent<Character>() : null;
                if (character == null || character.IsBoss() || EncyclopediaMet(player, character.m_name))
                {
                    continue;
                }
                kind = candidate;
                creatureName = character.m_name;
                break;
            }
            if (!c.Check(kind != null, $"this character has met every creature the test knows ({string.Join(", ", UnmetCandidates)}): nothing to check"))
            {
                c.Report();
                yield break;
            }

            // Cloud first, 7 m in front; the creature appears inside it. The player looks at it from outside.
            var spot = Ground(origin + dir * 7f);
            var slot = new CloudSlot();
            yield return rig.PutCloud(slot, spot);
            var cloud = slot.Cloud;
            yield return WaitFor(() => cloud == null || cloud.IsActive(SmokeRegistry.Now, rules), 2f);
            var creature = rig.Creature(kind, spot, -dir, false);
            if (!c.Check(cloud != null && creature != null, $"could not make a cloud or spawn {kind}"))
            {
                c.Report();
                yield break;
            }
            var plated = false;
            var met = false;
            var hidden = true;
            var look = Time.time;
            while (Time.time - look < 2f && creature != null)
            {
                var to = creature.GetCenterPoint() - player.m_eye.position;
                rig.Aim(to, -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg);
                yield return null;
                if (creature == null)
                {
                    break;
                }
                // Crosshair on it for as long as a plate exists (what the game does for a hovered creature).
                plated |= AimAt(hud, creature) && PlateShown(hud, creature);
                met |= EncyclopediaMet(player, creatureName);
                hidden &= !hud.TestShow(creature, true);
            }
            var pending = ServerRules.TestPending;
            ServerRules.TestPending = true;
            var wouldShow = creature != null && hud.TestShow(creature, true);
            ServerRules.TestPending = pending;
            c.Check(creature != null && cloud.Contains(creature.GetCenterPoint()) && !InSmoke(player, rules),
                $"{kind} is not inside the active cloud with the player outside it");
            c.Check(wouldShow, $"without smoke the game would not show the bar of {kind} 7 m in front either: nothing proven");
            c.Check(hidden && !plated, $"looked at inside the smoke for 2 s: the bar of {kind} showed (hidden by the smoke rule {hidden})");
            c.Check(!met, $"looked at inside the smoke for 2 s with its bar hidden: Encyclopedia counts {creatureName} as met");

            // Smoke gone, the player looking 30 degrees past the creature (crosshair off it, creature still in the
            // picture): the game makes a bar for it again, but shows nothing (and nothing is met) until the crosshair
            // is on it.
            rig.Aim(Quaternion.Euler(0f, 30f, 0f) * dir, 0f);
            yield return Frames(2);
            if (cloud != null)
            {
                rig.Remove(cloud.gameObject);
            }
            slot.Cloud = null;
            var plate = new Box();
            if (creature != null)
            {
                yield return Plate(hud, creature, plate, 120);
            }
            yield return Frames(3);
            if (c.Check(creature != null && plate.Ok, $"no bar for {kind} after the smoke is gone"))
            {
                c.Check(!PlateShown(hud, creature) && !EncyclopediaMet(player, creatureName),
                    $"smoke gone, not aimed at yet: bar shown {PlateShown(hud, creature)}, met {EncyclopediaMet(player, creatureName)}");
                var at = creature.GetCenterPoint() - player.m_eye.position;
                rig.Aim(at, -Mathf.Atan2(at.y, new Vector2(at.x, at.z).magnitude) * Mathf.Rad2Deg);
                yield return Frames(2);
                c.Check(AimAt(hud, creature), "no bar to aim at");
                var seen = new Box();
                yield return WaitFor(() => creature == null || (PlateShown(hud, creature) && EncyclopediaMet(player, creatureName)), 2f, seen);
                c.Check(creature != null && PlateShown(hud, creature), $"aimed at after the smoke: the bar of {kind} does not show");
                c.Check(EncyclopediaMet(player, creatureName), $"its bar shows after the smoke: Encyclopedia still does not count {creatureName} as met");
            }
            NoProblems(c);
            c.Report();
        }
        finally
        {
            try
            {
                var p = rig.Player;
                if (recordRead && p != null && p.m_customData != null)
                {
                    if (hadRecord)
                    {
                        p.m_customData[EncyclopediaSeenKey] = record;
                    }
                    else
                    {
                        p.m_customData.Remove(EncyclopediaSeenKey);
                    }
                }
            }
            catch (Exception e)
            {
                SelfTest.Fail(EncyclopediaName, $"could not put the character's Encyclopedia record back: {e.Message}");
            }
            rig.Restore();
        }
    }

    // ---------- sneak.x-crossbow (X08) ----------

    private static IEnumerator RunCrossbow()
    {
        if (!Needs(CrossbowName, CrossbowGuid, "Crossbow Stays Loaded", "X08"))
        {
            yield break;
        }
        var c = new Checks(CrossbowName);
        var rig = Rig.Create(CrossbowName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            var rules = XpRules(c);
            ServerRules.TestRules = rules;
            SneakXp.TestShowMessage = true;
            Tap.Install();
            rig.SaveAllSkills();
            rig.SetSneak(20f);
            yield return Stand(player);
            yield return FreeHands(player);
            var place = new Spot();
            yield return FindOpenSpot(rig, 10f, false, (feet, d) => BareGround(Ground(feet + d * 7f), false), place);
            if (!place.Found)
            {
                c.Note("no open ground with 10 m free ahead found within 48 m: the scenery may be in the way of the bolt");
            }
            var dir = place.Dir;
            var origin = player.transform.position;
            rig.TakeControls();
            var crossbow = rig.Give("CrossbowArbalest");
            var bolts = rig.Give("BoltBone", 10);
            EmptyHands(player);
            yield return Frames(2);
            if (!c.Check(crossbow != null && bolts != null && rig.Equip(crossbow), $"could not get a CrossbowArbalest with BoltBone ({Hands(player)})"))
            {
                c.Report();
                yield break;
            }
            // A sure aim for the one shot (every skill is put back at the end): the game spreads bolts by the skill.
            player.GetSkills().GetSkill(Skills.SkillType.Crossbows).m_level = 100f;
            var loaded = new Box();
            yield return WaitFor(() => player.IsWeaponLoaded(), 15f, loaded);
            if (!c.Check(loaded.Ok, $"the crossbow did not load within 15 s ({HandsText(player)})"))
            {
                c.Report();
                yield break;
            }
            yield return FreeHands(player);

            // Crossbow Stays Loaded next to this mod: put away and taken out again, still loaded, no reload.
            player.UnequipItem(crossbow, false);
            yield return new WaitForSeconds(0.4f);
            c.Check(!player.IsItemEquiped(crossbow) && rig.Equip(crossbow), $"could not put the crossbow away and take it out again ({Hands(player)})");
            var kept = new Box();
            yield return WaitFor(() => player.IsWeaponLoaded(), 0.6f, kept);
            c.Check(kept.Ok && player.GetActionQueueCount() == 0,
                $"put away and taken out again, the crossbow is not still loaded (loaded {player.IsWeaponLoaded()}, queued actions {player.GetActionQueueCount()})");
            if (!kept.Ok)
            {
                yield return WaitFor(() => player.IsWeaponLoaded(), 15f, loaded);
            }

            // The real shot at an unaware Greydwarf 7 m in front that looks away.
            var g = rig.Creature("Greydwarf", Ground(origin + dir * 7f), dir, false);
            if (!c.Check(g != null, "could not spawn a Greydwarf"))
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(0.7f);
            var to = g.GetCenterPoint() - player.m_eye.position;
            Face(rig, to);
            rig.Aim(to, -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg);
            yield return new WaitForSeconds(0.4f);
            var blocker = ShotBlocker(player, g.GetCenterPoint(), g);
            c.Check(blocker == null, $"something stands between the crossbow and the Greydwarf: {blocker}");
            c.Check(!g.GetBaseAI().IsAlerted() && player.IsWeaponLoaded(), $"before the shot: Greydwarf alerted {g.GetBaseAI().IsAlerted()}, crossbow loaded {player.IsWeaponLoaded()}");
            var health = g.GetHealth();
            var boltName = bolts.m_shared.m_name;
            var boltsBefore = player.GetInventory().CountItems(boltName, -1, false);
            Tap.Clear();
            c.Check(player.StartAttack(null, false), $"StartAttack with the loaded crossbow refused ({HandsText(player)})");
            var hit = new Box();
            yield return WaitFor(() => g == null || g.IsDead() || g.GetHealth() < health - 0.001f, 3f, hit);
            if (c.Check(hit.Ok, "the bolt did not hit the Greydwarf 7 m in front within 3 s"))
            {
                yield return Frames(3);
                c.Check(player.GetInventory().CountItems(boltName, -1, false) == boltsBefore - 1,
                    $"the shot used {boltsBefore - player.GetInventory().CountItems(boltName, -1, false)} bolt(s), expected one");
                c.Check(Tap.Logged(XpLineRanged) == 1 && Tap.Logged("(ranged) to their game for Sneak XP") == 1,
                    $"the crossbow sneak attack did not log \"{XpLineRanged}\" once ('{Tap.FirstLogged("Sneak attack")}')");
                c.Check(Tap.Messages("Sneak attack", MessageHud.MessageType.TopLeft) == 1, $"expected one \"Sneak attack\" message, got: {Tap.MessageTexts("Sneak attack")}");
            }
            // As usual after a shot: empty, then loaded again by itself.
            c.Check(!player.IsWeaponLoaded(), "the crossbow still counts as loaded right after its shot");
            yield return WaitFor(() => player.IsWeaponLoaded(), 15f, loaded);
            c.Check(loaded.Ok && player.IsItemEquiped(crossbow), $"the crossbow did not reload by itself within 15 s of the shot ({HandsText(player)})");
            NoProblems(c);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.x-forge (X07) ----------

    // Both mods run side by side and the Smoke Screen can be crafted. What the Forge of Potential itself does is
    // checked by Forge Idol Upgrades' own tests (forge.*), which run in the same game with this mod loaded.
    private static IEnumerator RunForge()
    {
        if (!Needs(ForgeName, ForgeGuid, "Forge Idol Upgrades", "X07"))
        {
            yield break;
        }
        var c = new Checks(ForgeName);
        var rig = Rig.Create(ForgeName);
        if (rig == null)
        {
            yield break;
        }
        var knewRecipe = true;
        var addedRecipe = false;
        try
        {
            var player = rig.Player;
            ServerRules.TestRules = new AmbushRules();
            Tap.Install();
            yield return Stand(player);
            var forge = FeatureRegistry.Find(ForgeGuid);
            var own = FeatureRegistry.Find(ModInfo.Guid);
            c.Check(forge != null && forge.Value.IsActive && own != null && own.Value.IsActive,
                $"Forge Idol Upgrades ({(forge != null ? forge.Value.State : "missing")}) and {ModInfo.Name} ({(own != null ? own.Value.State : "missing")}) are not both active");
            c.Check(forge != null && !forge.Value.Status.StartsWith("Error", StringComparison.OrdinalIgnoreCase),
                $"Forge Idol Upgrades reports: {(forge != null ? forge.Value.Status : "")}");
            var benchGo = rig.Spawn(AmbushRules.DefaultRecipeStation, Ground(player.transform.position - Flat(player.transform.forward) * 3f), Quaternion.identity);
            var station = benchGo != null ? benchGo.GetComponent<CraftingStation>() : null;
            yield return Frames(3);
            var recipe = SmokeContent.CraftRecipe;
            if (c.Check(station != null && recipe != null, "could not spawn a workbench, or the Smoke Screen has no recipe"))
            {
                knewRecipe = player.m_knownRecipes.Contains(SmokeContent.DisplayName);
                player.m_knownRecipes.Add(SmokeContent.DisplayName);
                addedRecipe = true;
                c.Check(recipe.m_enabled && OfferedAt(player, station, recipe) && ObjectDB.instance.m_recipes.Count(r => r != null && r.name == SmokeContent.RecipeName) == 1,
                    $"with Forge Idol Upgrades running, the workbench does not offer the one Smoke Screen recipe ({RecipeText(recipe)})");
            }
            NoProblems(c);
            c.Report();
        }
        finally
        {
            if (addedRecipe && !knewRecipe && rig.Player != null)
            {
                rig.Player.m_knownRecipes.Remove(SmokeContent.DisplayName);
            }
            rig.Restore();
        }
    }
}
#endif
