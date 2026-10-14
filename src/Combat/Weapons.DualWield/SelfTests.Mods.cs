#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx.Bootstrap;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.WeaponsDualWieldMod;

// Debug build only. Checks with another MC mod running in the same game (every MC mod is loaded in a test run). Same
// class and helper as SelfTests.cs. The other mod is found by its GUID in the shared registry; me never use its
// types: only what the game shows (hands, swing and hits of this mod's own records, health, skills, the creature's
// target, the Compendium text), and for Creature Morale its own Debug self-test overrides through reflection (boss
// rank and default rules, memory only). Other mod not loaded and active = FAIL, never a pass without a check.
//   dual.x.moveset        X02 Weapon Moveset: AxeIron + AxeJotunBane: jump attack = fourth dual axe swing, main then
//                         off hand; roll attack = second swing with the off hand, started inside the roll, then the
//                         combo go on (third and fourth swing, main then off); two knives: jump attack and roll
//                         attack are dual knife moves, the roll attack strike with the off hand
//   dual.x.ambush         X04 Sneak Ambush: crouched behind an unaware Greyling (AI on) with KnifeBlackMetal main +
//                         SwordIron off: the first hit is the knife's backstab (damage against a plain knife hit = the
//                         knife's bonus), the Sneak skill gains once; hands swapped, another Greyling: the sword's bonus
//   dual.x.ambush-morale  X04 with Creature Morale at boss rank 3: an afraid Greyling that have not seen the player
//                         still take the backstab; one that watch the player takes none (control at rank 0: it does)
//   dual.x.forge          X05 Forge Idol Upgrades: the Forge of Potential's own window: the off-hand weapon refined =
//                         unequipped, the main weapon stays; the main weapon refined = the off-hand weapon move to
//                         the main hand (pair held in hand at the open Forge, and pair put away by the Forge: then R
//                         after the Forge draw the off-hand weapon in the main hand)
//   dual.x.morale         X06 Creature Morale at boss rank 3: an afraid Greyling; first swing in the air, the off-hand
//                         swing hit it: the player become its target and stay it (it fights)
//   dual.x.kills-page     X09 Creature Kill and Tame Counts: a Greyling killed by an off-hand hit: its line on the
//                         Compendium's Player Statistics page count one more kill, under melee
internal static partial class SelfTests
{
    private const string XMovesetName = "dual.x.moveset";
    private const string XAmbushName = "dual.x.ambush";
    private const string XAmbushMoraleName = "dual.x.ambush-morale";
    private const string XForgeName = "dual.x.forge";
    private const string XMoraleName = "dual.x.morale";
    private const string XKillsPageName = "dual.x.kills-page";

    private const string MovesetGuid = "MC.Combat.Weapons.Moveset";
    private const string ForgeGuid = "MC.Crafting.Forge.IdolUpgrades";
    private const string MoraleGuid = "MC.Combat.Creatures.Morale";
    private const string MoraleNamespace = "MC.Combat.CreaturesMoraleMod";
    private const string GreylingName = "Greyling";

    // ---------- helpers ----------

    // Other MC mod loaded and active, or the test fail here (a skipped check must no read as proof).
    private static bool NeedsMod(string test, string guid, string modName, string item)
    {
        if (OtherModActive(guid))
        {
            return true;
        }
        var feature = FeatureRegistry.Find(guid);
        SelfTest.Fail(test, $"{modName} is not loaded and active ({(feature != null ? feature.Value.Status : "not installed")}): "
                            + $"nothing of TESTING.md {item} was checked");
        return false;
    }

    // Attack button held (the game's queued attack timer kept up) until 'count' pair swings more are recorded, or
    // 'seconds' passed; eachFrame run every frame meanwhile. Then the button is let go.
    private static IEnumerator PressUntilSwings(Player p, int count, float seconds, Action eachFrame = null)
    {
        var target = DualSwing.Swings.Count + count;
        var until = Time.time + seconds;
        while (DualSwing.Swings.Count < target && Time.time < until)
        {
            p.m_queuedAttackTimer = 0.5f;
            eachFrame?.Invoke();
            yield return null;
        }
        p.m_queuedAttackTimer = 0f;
    }

    // The running attack over (or 'seconds' passed), then on the ground.
    private static IEnumerator AttackAndLanding(Player p, float seconds, Action eachFrame = null)
    {
        var until = Time.time + seconds;
        while (Time.time < until && (p.InAttack() || (p.m_currentAttack != null && !p.m_currentAttack.IsDone())))
        {
            eachFrame?.Invoke();
            yield return null;
        }
        until = Time.time + 3f;
        while (Time.time < until && !p.IsOnGround())
        {
            eachFrame?.Invoke();
            yield return null;
        }
    }

    // Creature on a spot, facing a point, still.
    private static void PlaceCreature(Character creature, Vector3 position, Vector3 lookAt)
    {
        if (creature == null)
        {
            return;
        }
        var facing = lookAt - position;
        facing.y = 0f;
        var rotation = facing.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(facing.normalized) : creature.transform.rotation;
        creature.transform.SetPositionAndRotation(position, rotation);
        var body = creature.m_body;
        if (body != null)
        {
            body.position = position;
            body.rotation = rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }

    // Creature Morale's own self-test overrides, by reflection (its Debug build): boss rank of the local player and
    // built-in default rule with a chosen cornered range, both in memory only. Close put the real one back.
    private sealed class MoraleHook
    {
        private PropertyInfo _standing;
        private ConstructorInfo _rank;
        private PropertyInfo _rules;
        private ConstructorInfo _defaults;
        private MethodInfo _build;
        private FieldInfo _cornered;

        internal string Problem = "";

        internal bool Ready => _standing != null && _rank != null && _rules != null && _defaults != null && _build != null
                               && _cornered != null;

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
                hook._build = rules != null ? rules.GetMethod("Build", instances, null, Type.EmptyTypes, null) : null;
                hook._cornered = rules != null ? rules.GetField("CorneredRange", instances) : null;
                if (!hook.Ready)
                {
                    hook.Problem = "its self-test overrides (Standing.TestOverride, ServerRules.TestRules, MoraleRules) were not "
                                   + "found: a Release build of Creature Morale?";
                }
            }
            catch (Exception e)
            {
                hook.Problem = e.Message;
            }
            return hook;
        }

        // Built-in default rules (never the player's settings), with this cornered range (0 = a creature the player
        // stands next to never turn to fight for that alone).
        internal void DefaultRules(float corneredRange)
        {
            var rules = _defaults.Invoke(null);
            _cornered.SetValue(rules, corneredRange);
            _build.Invoke(rules, null);
            _rules.SetValue(null, rules, null);
        }

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

    // ---------- dual.x.moveset (X02) ----------

    private static IEnumerator RunXMoveset()
    {
        if (!NeedsMod(XMovesetName, MovesetGuid, "Weapon Moveset", "X02"))
        {
            yield break;
        }
        var c = new Checks(XMovesetName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(XMovesetName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        SkillSave skills = null;
        var spot = player.transform.position;
        var facing = player.transform.rotation;
        var warnings = LogWatch.WarningLines().Length;
        var errors = LogWatch.ErrorLines().Length;
        try
        {
            bench = new Bench(player);
            skills = new SkillSave(player, Skills.SkillType.Axes, Skills.SkillType.Knives, Skills.SkillType.Jump);
            var axe = bench.Give(Axe);
            var bane = bench.Give(JotunBane);
            var knife = bench.Give(KnifeBlack);
            var knife2 = bench.Give(KnifeBlack);
            if (axe == null || bane == null || knife == null || knife2 == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            SelfTest.Note(XMovesetName, "Weapon Moveset runs with this game's own settings (defaults: jump attack and roll attack on, "
                                        + "cooldown 1 s, pairs jump with dualaxes3 / dual_knives2 and roll with dualaxes1 / dual_knives1)");
            DualSwing.ResetRecords();
            DualSwing.Recording = true;

            // AxeIron (main) + AxeJotunBane (off): the jump attack play the fourth dual axe swing, main then off hand.
            bench.Pair(axe, bane);
            yield return WaitIdle(player);
            yield return WaitMinor(player);
            yield return WaitCanAttack(player);
            c.Check(Holds(player, axe, bane) && _attackGate == null,
                $"axe pair in hand and the player can attack ({HandsText(player)}; {_attackGate ?? "nothing in the way"})");
            var from = DualSwing.Swings.Count;
            player.Jump();
            yield return WaitFor(() => !player.IsOnGround(), 1.5f);
            var jumped = !player.IsOnGround();
            var inAir = false;
            yield return PressUntilSwings(player, 1, 2.5f, () => inAir = !player.IsOnGround());
            yield return AttackAndLanding(player, 4f);
            c.Check(jumped && inAir, $"the attack button was pressed in the air after a jump (left the ground {jumped}, in the air at the press {inAir})");
            CheckSwings(c, from, "axe pair, jump attack: the fourth dual axe swing, main hand then off hand", "dualaxes3:0M,1O");
            CheckWeapons(c, from, axe, bane, "axe pair, jump attack");
            Invariant(c, player, "jump attack");
            PutBack(player, spot, facing);
            yield return new WaitForSeconds(1.5f);

            // The roll attack play the second swing with the off hand, straight out of the roll; the next swings go on
            // with the combo (third and fourth swing: main, then off).
            yield return WaitIdle(player);
            yield return WaitCanAttack(player);
            from = DualSwing.Swings.Count;
            player.Dodge(Forward(player));
            yield return WaitFor(() => player.InDodge(), 2f);
            var rolled = player.InDodge();
            var rollAt = Time.time;
            var stoodUp = false;
            var first = from;
            yield return PressUntilSwings(player, 3, 8f, () =>
            {
                // Before the roll attack starts: the roll must no be over (a stand-up in between).
                if (DualSwing.Swings.Count == first && !player.InDodge())
                {
                    stoodUp = true;
                }
            });
            yield return AttackAndLanding(player, 4f);
            CheckSwings(c, from, "axe pair, roll attack then two more presses: second swing with the off hand, then the third and "
                                 + "fourth swing, main then off", "dualaxes1:0O", "dualaxes2:0M,1O", "dualaxes3:0M,1O");
            CheckWeapons(c, from, axe, bane, "axe pair, roll attack and combo");
            var rollSwingAfter = DualSwing.Swings.Count > from ? DualSwing.Swings[from].Time - rollAt : -1f;
            c.Check(rolled && !stoodUp && rollSwingAfter >= 0f && rollSwingAfter < 1.3f,
                $"the roll attack starts inside the roll, no stand-up in between (rolled {rolled}, roll over before the swing "
                + $"{stoodUp}, swing {F2(rollSwingAfter)} s after the roll began)");
            Invariant(c, player, "roll attack");
            PutBack(player, spot, facing);
            yield return new WaitForSeconds(1.5f);

            // Two knives: the dual knife moves. Jump attack, then (after the cooldown) the roll attack with the off hand.
            bench.Pair(knife, knife2);
            yield return WaitIdle(player);
            yield return WaitMinor(player);
            yield return WaitCanAttack(player);
            c.Check(Holds(player, knife, knife2), $"knife pair in hand ({HandsText(player)})");
            from = DualSwing.Swings.Count;
            player.Jump();
            yield return WaitFor(() => !player.IsOnGround(), 1.5f);
            yield return PressUntilSwings(player, 1, 2.5f);
            yield return AttackAndLanding(player, 4f);
            CheckSwings(c, from, "knife pair, jump attack: a dual knife move (the last stab, both knives)", "dual_knives2:0M,0O");
            CheckWeapons(c, from, knife, knife2, "knife pair, jump attack");
            PutBack(player, spot, facing);
            yield return new WaitForSeconds(1.5f);
            yield return WaitIdle(player);
            yield return WaitCanAttack(player);
            from = DualSwing.Swings.Count;
            player.Dodge(Forward(player));
            yield return WaitFor(() => player.InDodge(), 2f);
            yield return PressUntilSwings(player, 1, 5f);
            yield return AttackAndLanding(player, 4f);
            CheckSwings(c, from, "knife pair, roll attack: the second stab, with the off hand", "dual_knives1:0O");
            CheckWeapons(c, from, knife, knife2, "knife pair, roll attack");
            Invariant(c, player, "knife pair moves");
            var problems = LogProblemsSince(warnings, errors);
            c.Check(problems.Length == 0, $"no warning or error from {ModInfo.Name} meanwhile ({problems.Length}: {string.Join(" | ", problems)})");
            yield return WaitIdle(player);
            c.Report();
        }
        finally
        {
            player.m_queuedAttackTimer = 0f;
            DualSwing.Recording = false;
            DualSwing.ResetRecords();
            if (bench != null)
            {
                bench.TakeBack();
            }
            if (skills != null)
            {
                skills.Restore();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
            PutBack(player, spot, facing);
        }
    }

    // ---------- dual.x.ambush (X04), dual.x.ambush-morale ----------

    // What one ambush gave (iterators have no out parameters).
    private static bool _ambushUnaware;
    private static bool _ambushBackstab;
    private static DualSwing.DamageRecord _ambushHit;
    private static float _ambushLost;
    private static string _ambushWhy = "";

    // A Greyling 1.1 m in front of the player, tough (it must live through the hits). facingAway = it back to the
    // player; aiOn = its own AI runs (it can notice the player). Null = no such prefab.
    private static Character SpawnGreyling(Bench bench, Player p, Vector3 spot, Vector3 ahead, bool facingAway, bool aiOn)
    {
        var at = Ground(spot + ahead * 1.1f);
        var go = bench.SpawnAt(GreylingName, at, Quaternion.LookRotation(facingAway ? ahead : -ahead));
        var creature = go != null ? go.GetComponent<Character>() : null;
        if (creature == null)
        {
            return null;
        }
        creature.SetMaxHealth(100000f);
        creature.SetHealth(100000f);
        if (!aiOn)
        {
            foreach (var ai in go.GetComponents<BaseAI>())
            {
                ai.enabled = false;
            }
        }
        return creature;
    }

    // One pair swing at the creature in front: the first hit of the first swing (main hand). Fills the fields above:
    // was the creature unaware right before (not alerted; 'mustSee' false = it also could not see the player), did the
    // game apply its backstab (Character.m_backstabTime moved), the hit as dealt, the health it took.
    private static IEnumerator AmbushSwing(Player p, Character creature, Vector3 ahead, bool mustSee)
    {
        _ambushUnaware = false;
        _ambushBackstab = false;
        _ambushHit = null;
        _ambushLost = 0f;
        _ambushWhy = "";
        var ai = creature.GetBaseAI();
        Face(p, ahead);
        yield return null;
        var sees = ai != null && ai.CanSeeTarget(p);
        var alerted = ai != null && ai.IsAlerted();
        _ambushUnaware = ai != null && !alerted && sees == mustSee;
        _ambushWhy = $"alerted {alerted}, sees the player {sees}";
        var stab = creature.m_backstabTime;
        var health = creature.GetHealth();
        var damages = DualSwing.Damages.Count;
        yield return PressUntilSwings(p, 1, 3f);
        var until = Time.time + 2.5f;
        while (Time.time < until && DualSwing.Damages.Count == damages)
        {
            yield return null;
        }
        if (DualSwing.Damages.Count > damages)
        {
            _ambushHit = DualSwing.Damages[damages];
            _ambushLost = health - creature.GetHealth();
            _ambushBackstab = !Mathf.Approximately(creature.m_backstabTime, stab);
        }
        yield return AttackAndLanding(p, 4f);
    }

    private static IEnumerator RunXAmbush()
    {
        if (!NeedsMod(XAmbushName, SneakGuid, "Sneak Ambush", "X04"))
        {
            yield break;
        }
        var c = new Checks(XAmbushName);
        var player = Player.m_localPlayer;
        if (player == null || ZoneSystem.instance == null)
        {
            SelfTest.Fail(XAmbushName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        SkillSave skills = null;
        var spot = player.transform.position;
        var facing = player.transform.rotation;
        var crouched = player.IsCrouching();
        var sneakTick = player.m_sneakSkillImproveTimer;
        try
        {
            bench = new Bench(player);
            skills = new SkillSave(player, Skills.SkillType.Sneak, Skills.SkillType.Knives, Skills.SkillType.Swords);
            var knife = bench.Give(KnifeBlack);
            var sword = bench.Give(Sword);
            if (knife == null || sword == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            var knifeBonus = knife.m_shared.m_backstabBonus;
            var swordBonus = sword.m_shared.m_backstabBonus;
            c.Check(knifeBonus > swordBonus && swordBonus > 1f,
                $"game data: the knife's backstab bonus ({F2(knifeBonus)}) is larger than the sword's ({F2(swordBonus)})");
            // Sneak at level 50, no progress: a level takes about 180 point there, so the gain can be read as points.
            var sneak = player.GetSkills().GetSkill(Skills.SkillType.Sneak);
            sneak.m_level = 50f;
            sneak.m_accumulator = 0f;
            // The game raise Sneak by itself too: one tick for every second of crouched moving, a full point next to
            // a creature (Player.OnSneaking). Its timer is held far back for the whole test (put back in finally), so
            // every Sneak point counted below come from the ambush, never from that tick: a tick alone must no read
            // as "Sneak Ambush paid".
            player.m_sneakSkillImproveTimer = -1000f;
            var ahead = Forward(player);
            DualSwing.ResetRecords();
            DualSwing.Recording = true;

            foreach (var swordMain in new[] { false, true })
            {
                var main = swordMain ? sword : knife;
                var off = swordMain ? knife : sword;
                var bonus = swordMain ? swordBonus : knifeBonus;
                var what = swordMain ? "hands swapped (sword main, knife off)" : "knife main, sword off";
                if (!swordMain)
                {
                    bench.Pair(knife, sword);
                    yield return WaitIdle(player);
                }
                else
                {
                    // X04: swap hands (the real swap, as the key queue it).
                    yield return WaitIdle(player);
                    yield return new WaitForSeconds(0.5f);
                    yield return SwapHands(player);
                    yield return WaitSwap(player);
                }
                yield return WaitMinor(player);
                c.Check(Holds(player, main, off), $"{what}: the pair in hand ({HandsText(player)})");
                PutBack(player, spot, facing);
                player.m_noiseRange = 0f;
                player.SetCrouch(true);
                yield return new WaitForSeconds(0.3f);

                // An unaware Greyling with its AI on, it back to the crouched, silent player right behind it.
                var greyling = SpawnGreyling(bench, player, spot, ahead, true, true);
                c.Check(greyling != null, $"{what}: a Greyling spawned in front of the player");
                if (greyling == null)
                {
                    continue;
                }
                yield return new WaitForSeconds(0.4f);
                yield return WaitCanAttack(player);
                var before = sneak.m_level * 1000f + sneak.m_accumulator;
                yield return AmbushSwing(player, greyling, ahead, false);
                yield return new WaitForSeconds(0.5f);
                var gained = sneak.m_level * 1000f + sneak.m_accumulator - before;
                var hit = _ambushHit;
                var lost = _ambushLost;
                c.Check(_ambushUnaware,
                    $"{what}: the Greyling was unaware of the crouched player behind it right before the swing ({_ambushWhy})");
                c.Check(hit != null && hit.Hand == Hand.Main && hit.Trigger == "dualaxes0" && Approx(hit.Backstab, bonus),
                    $"{what}: the first hit is the main-hand weapon's and carries its backstab bonus {F2(bonus)} (hit: "
                    + $"{(hit != null ? $"{hit.Trigger} {hit.Hand}, bonus {F2(hit.Backstab)}" : "none reached the Greyling")})");
                c.Check(_ambushBackstab, $"{what}: the game applied its backstab to that hit (the creature's backstab time moved)");
                c.Check(gained > 0.5f, $"{what}: the ambush raised the Sneak skill ({F2(gained)} points; Sneak Ambush's XP for a sneak "
                                       + "attack; the game's own Sneak tick for crouched moving is held off in this test)");

                // The same creature held still, AI off: another main-hand hit, no backstab now (the game give one per
                // creature every 5 minutes). Its damage against the ambush hit's = the bonus the game applied.
                var held = new Dummy(greyling.gameObject, Ground(spot + ahead * 1.1f), Quaternion.LookRotation(ahead),
                    greyling.GetRadius(), 1.1f, 0f, null);
                player.SetCrouch(false);
                yield return new WaitForSeconds(0.6f);
                Face(player, ahead);
                var plainFrom = DualSwing.Damages.Count;
                var stab = greyling.m_backstabTime;
                var health = greyling.GetHealth();
                var sneakBefore = sneak.m_level * 1000f + sneak.m_accumulator;
                yield return Swing(player, held, 1, false);
                var plain = DualSwing.Damages.Count > plainFrom ? DualSwing.Damages[plainFrom] : null;
                var plainLost = health - greyling.GetHealth();
                yield return new WaitForSeconds(0.5f);
                var gainedAgain = sneak.m_level * 1000f + sneak.m_accumulator - sneakBefore;
                var measured = hit != null && plain != null && hit.Total > 0f && plain.Total > 0f && plainLost > 0f
                    ? lost / hit.Total / (plainLost / plain.Total)
                    : -1f;
                c.Check(plain != null && plain.Hand == Hand.Main && Mathf.Approximately(greyling.m_backstabTime, stab),
                    $"{what}: a second main-hand hit on the same Greyling is a plain hit, no backstab (hit: "
                    + $"{(plain != null ? $"{plain.Trigger} {plain.Hand}" : "none reached the Greyling")})");
                c.Check(measured > 0f && Mathf.Abs(measured - bonus) <= bonus * 0.1f,
                    $"{what}: the ambush hit took {F2(measured)} times the health of a plain hit of the same weapon, the weapon's "
                    + $"backstab bonus is {F2(bonus)} (ambush: {F2(lost)} health for {F2(hit != null ? hit.Total : 0f)} damage; plain: "
                    + $"{F2(plainLost)} for {F2(plain != null ? plain.Total : 0f)})");
                c.Check(gainedAgain < Mathf.Max(0.5f, gained * 0.25f),
                    $"{what}: the Sneak skill gained once for the ambush, nothing more for the plain hit ({F2(gained)} points, then {F2(gainedAgain)})");
                Invariant(c, player, what);
                DestroyWorldObject(greyling.gameObject);
                yield return WaitIdle(player);
                yield return null;
            }
            SelfTest.Note(XAmbushName, $"backstab bonus: KnifeBlackMetal {F2(knifeBonus)}, SwordIron {F2(swordBonus)}; the Smoke Screen "
                                       + "while paired is dual.others");
            yield return NoteIfStuck(XAmbushName, player, "the two ambushes");
            c.Report();
        }
        finally
        {
            player.m_queuedAttackTimer = 0f;
            player.m_sneakSkillImproveTimer = sneakTick;
            DualSwing.Recording = false;
            DualSwing.ResetRecords();
            player.SetCrouch(crouched);
            if (bench != null)
            {
                bench.TakeBack();
            }
            if (skills != null)
            {
                skills.Restore();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
            PutBack(player, spot, facing);
        }
    }

    private static IEnumerator RunXAmbushMorale()
    {
        if (!NeedsMod(XAmbushMoraleName, SneakGuid, "Sneak Ambush", "X04")
            || !NeedsMod(XAmbushMoraleName, MoraleGuid, "Creature Morale", "X04 (the Creature Morale case)"))
        {
            yield break;
        }
        var c = new Checks(XAmbushMoraleName);
        var player = Player.m_localPlayer;
        if (player == null || ZoneSystem.instance == null)
        {
            SelfTest.Fail(XAmbushMoraleName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        SkillSave skills = null;
        var morale = MoraleHook.Open();
        var spot = player.transform.position;
        var facing = player.transform.rotation;
        var crouched = player.IsCrouching();
        try
        {
            c.Check(morale.Ready, $"Creature Morale can be given a boss rank for the test ({morale.Problem})");
            if (!morale.Ready)
            {
                c.Report();
                yield break;
            }
            bench = new Bench(player);
            skills = new SkillSave(player, Skills.SkillType.Sneak, Skills.SkillType.Knives, Skills.SkillType.Swords);
            var knife = bench.Give(KnifeBlack);
            var sword = bench.Give(Sword);
            if (knife == null || sword == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            c.Check(player.GetCurrentBiome() == Heightmap.Biome.Meadows,
                $"the test stands in the Meadows (it is {player.GetCurrentBiome()}): TESTING.md X04 is about a Greyling there");
            var knifeBonus = knife.m_shared.m_backstabBonus;
            var ahead = Forward(player);
            morale.DefaultRules(0f);
            DualSwing.ResetRecords();
            DualSwing.Recording = true;
            bench.Pair(knife, sword);
            yield return WaitIdle(player);
            yield return WaitMinor(player);
            c.Check(Holds(player, knife, sword), $"knife main, sword off ({HandsText(player)})");

            // Boss rank 3: a Greyling is afraid of the player. One that have not seen him (its back to the crouched,
            // silent player, its AI on) still take the backstab.
            morale.Rank(3);
            yield return SkipFrames(2);
            player.m_noiseRange = 0f;
            player.SetCrouch(true);
            yield return new WaitForSeconds(0.3f);
            var unseen = SpawnGreyling(bench, player, spot, ahead, true, true);
            c.Check(unseen != null, "a Greyling spawned with its back to the player");
            if (unseen != null)
            {
                yield return new WaitForSeconds(0.4f);
                yield return WaitCanAttack(player);
                yield return AmbushSwing(player, unseen, ahead, false);
                c.Check(_ambushUnaware, $"boss rank 3: the Greyling had not seen the crouched player behind it ({_ambushWhy})");
                c.Check(_ambushHit != null && _ambushHit.Hand == Hand.Main && Approx(_ambushHit.Backstab, knifeBonus) && _ambushBackstab,
                    $"boss rank 3: an afraid Greyling that has not seen the player still takes the knife's backstab (hit reached it "
                    + $"{_ambushHit != null}, backstab applied {_ambushBackstab})");
                DestroyWorldObject(unseen.gameObject);
            }
            player.SetCrouch(false);
            yield return WaitIdle(player);
            yield return new WaitForSeconds(0.6f);
            PutBack(player, spot, facing);

            // Boss rank 3: one that watch the player (facing him, not alerted: its AI off so it stay so) takes none.
            var watcher = SpawnGreyling(bench, player, spot, ahead, false, false);
            c.Check(watcher != null, "a Greyling spawned facing the player");
            if (watcher != null)
            {
                yield return new WaitForSeconds(0.6f);
                yield return WaitCanAttack(player);
                yield return AmbushSwing(player, watcher, ahead, true);
                c.Check(_ambushUnaware, $"boss rank 3: the second Greyling watches the player and is not alerted ({_ambushWhy})");
                c.Check(_ambushHit != null && _ambushHit.Hand == Hand.Main && !_ambushBackstab,
                    $"boss rank 3: an afraid Greyling that watches the player takes no backstab (hit reached it {_ambushHit != null}, "
                    + $"backstab applied {_ambushBackstab})");
                DestroyWorldObject(watcher.gameObject);
            }
            yield return WaitIdle(player);
            yield return new WaitForSeconds(0.6f);
            PutBack(player, spot, facing);

            // Control, boss rank 0 (every creature hostile, as in the normal game): the same watcher take the game's
            // backstab, so the "none" above is the afraid creature's rule.
            morale.Rank(0);
            yield return SkipFrames(2);
            var control = SpawnGreyling(bench, player, spot, ahead, false, false);
            c.Check(control != null, "a control Greyling spawned facing the player");
            if (control != null)
            {
                yield return new WaitForSeconds(0.6f);
                yield return WaitCanAttack(player);
                yield return AmbushSwing(player, control, ahead, true);
                c.Check(_ambushUnaware && _ambushHit != null && _ambushBackstab,
                    $"control, boss rank 0: a Greyling that faces the player and is not alerted takes the game's backstab ({_ambushWhy}; "
                    + $"hit reached it {_ambushHit != null}, backstab applied {_ambushBackstab})");
                DestroyWorldObject(control.gameObject);
            }
            Invariant(c, player, "ambushes with Creature Morale");
            yield return NoteIfStuck(XAmbushMoraleName, player, "the ambushes");
            c.Report();
        }
        finally
        {
            morale.Close(XAmbushMoraleName);
            player.m_queuedAttackTimer = 0f;
            DualSwing.Recording = false;
            DualSwing.ResetRecords();
            player.SetCrouch(crouched);
            if (bench != null)
            {
                bench.TakeBack();
            }
            if (skills != null)
            {
                skills.Restore();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
            PutBack(player, spot, facing);
        }
    }

    // ---------- dual.x.forge (X05) ----------

    // The idol requirement of a weapon's recipe at the level after 'item' (null = none), as the Forge of Potential asks.
    private static Piece.Requirement IdolRequirement(Recipe recipe, ItemDrop.ItemData item)
    {
        if (recipe == null || recipe.m_resources == null)
        {
            return null;
        }
        foreach (var requirement in recipe.m_resources)
        {
            if (requirement != null && requirement.m_upgraderResource && requirement.m_resItem != null
                && requirement.GetAmount(item.m_quality + 1) > 0)
            {
                return requirement;
            }
        }
        return null;
    }

    // Result of the last Refine: the weapon's row was listed, the press started the Forge's timer, the level after.
    private static bool _refineListed;
    private static bool _refineStarted;

    // The Forge window's own flow: the weapon's row picked in the list, the craft button's handler, the 8-12 s timer
    // skipped (the next panel update run the refinement).
    private static IEnumerator Refine(InventoryGui gui, ItemDrop.ItemData item)
    {
        _refineListed = false;
        _refineStarted = false;
        gui.UpdateCraftingPanel();
        yield return null;
        var row = -1;
        for (var i = 0; i < gui.m_availableRecipes.Count; i++)
        {
            if (ReferenceEquals(gui.m_availableRecipes[i].ItemData, item))
            {
                row = i;
                break;
            }
        }
        if (row < 0)
        {
            yield break;
        }
        _refineListed = true;
        gui.SetRecipe(row, false);
        yield return null;
        yield return null;
        gui.OnCraftPressed();
        yield return null;
        _refineStarted = gui.m_craftTimer >= 0f;
        if (_refineStarted)
        {
            gui.m_craftTimer = 1000f;
        }
        yield return null;
        yield return null;
        yield return null;
    }

    private static IEnumerator RunXForge()
    {
        if (!NeedsMod(XForgeName, ForgeGuid, "Forge Idol Upgrades", "X05"))
        {
            yield break;
        }
        var c = new Checks(XForgeName);
        var player = Player.m_localPlayer;
        var gui = InventoryGui.instance;
        if (player == null || gui == null || ZNetScene.instance == null || ObjectDB.instance == null || ZoneSystem.instance == null)
        {
            SelfTest.Fail(XForgeName, "no local player, no world or no inventory window");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        SkillSave skills = null;
        var spot = player.transform.position;
        var facing = player.transform.rotation;
        var taught = new List<string>();
        var learned = new List<string>();
        string stationName = null;
        var stationKnown = false;
        var stationLevel = 0;
        try
        {
            bench = new Bench(player);
            skills = new SkillSave(player, Skills.SkillType.Crafting);
            var sword = bench.Inventory.AddItem(Sword, 1, 2, 0, 0L, "", false);
            var axe = bench.Inventory.AddItem(Axe, 1, 2, 0, 0L, "", false);
            if (sword == null || axe == null)
            {
                c.Check(false, "could not give the test items (level 2)");
                c.Report();
                yield break;
            }
            var swordRecipe = ObjectDB.instance.GetRecipe(sword);
            var axeRecipe = ObjectDB.instance.GetRecipe(axe);
            var swordIdol = IdolRequirement(swordRecipe, sword);
            var axeIdol = IdolRequirement(axeRecipe, axe);
            c.Check(swordIdol != null && axeIdol != null,
                $"game data: the recipes of {Sword} and {Axe} ask for an idol at the Forge of Potential (sword {swordIdol != null}, axe {axeIdol != null})");
            // Game name of the Forge of Potential is UpgradeStation (first run: search for an upgrader station that is
            // also a Piece found none). Search by the upgrader flag only when that name is gone.
            var forgePrefab = ZNetScene.instance.GetPrefab("UpgradeStation");
            if (forgePrefab == null)
            {
                forgePrefab = ZNetScene.instance.m_prefabs.FirstOrDefault(p =>
                {
                    var station = p != null ? p.GetComponentInChildren<CraftingStation>(true) : null;
                    return station != null && station.m_upgrader;
                });
            }
            c.Check(forgePrefab != null, "the Forge of Potential (an upgrader crafting station piece) exists in the game");
            if (swordIdol == null || axeIdol == null || forgePrefab == null)
            {
                c.Report();
                yield break;
            }
            // The character know both recipes (rows of the Forge list) for the test; unknown ones are forgotten after.
            foreach (var name in new[] { sword.m_shared.m_name, axe.m_shared.m_name })
            {
                if (player.m_knownRecipes.Add(name))
                {
                    taught.Add(name);
                }
            }
            foreach (var idol in new[] { swordIdol, axeIdol })
            {
                var name = idol.m_resItem.m_itemData.m_shared.m_name;
                if (!player.m_knownMaterial.Contains(name) && !learned.Contains(name))
                {
                    learned.Add(name);
                }
            }

            // The Forge 3 m ahead (it is big: closer, its collider push the player). This test copy's use range is
            // made large, so the game no close the window for the distance.
            var forgeGo = bench.SpawnAt(forgePrefab.name, Ground(spot + Forward(player) * 3f), Quaternion.LookRotation(-Forward(player)));
            yield return new WaitForSeconds(0.5f);
            var forge = forgeGo != null ? forgeGo.GetComponentInChildren<CraftingStation>(true) : null;
            c.Check(forge != null, $"the Forge spawned ({forgePrefab.name})");
            if (forge == null)
            {
                c.Report();
                yield break;
            }
            forge.m_useDistance = 50f;
            stationName = forge.m_name;
            stationKnown = player.m_knownStations.TryGetValue(stationName, out stationLevel);

            // X05, first half: the off-hand weapon refined. The station put the pair away as the game does.
            bench.Pair(sword, axe);
            yield return WaitIdle(player);
            yield return WaitMinor(player);
            c.Check(Holds(player, sword, axe), $"pair in hand before the Forge ({HandsText(player)})");
            bench.GiveStack(axeIdol.m_resItem.name, axeIdol.GetAmount(axe.m_quality + 1));
            player.SetCraftingStation(forge);
            gui.Show(null, 3);
            yield return new WaitForSecondsRealtime(1f);
            c.Check(InventoryGui.IsVisible() && ReferenceEquals(player.GetCurrentCraftingStation(), forge),
                "the Forge window is open at the Forge");
            c.Check(ReferenceEquals(player.m_hiddenRightItem, sword) && ReferenceEquals(player.m_hiddenLeftItem, axe),
                $"using the Forge puts both weapons away ({HandsText(player)})");
            var level = axe.m_quality;
            yield return Refine(gui, axe);
            var axeGone = !bench.Inventory.ContainsItem(axe);
            c.Check(_refineListed && _refineStarted && !axeGone && axe.m_quality != level,
                $"the off-hand axe was refined at the Forge (listed {_refineListed}, timer started {_refineStarted}, still in the bag "
                + $"{!axeGone}, level {level} -> {axe.m_quality}; a weapon destroyed here means OnFailure = Destroy in Forge Idol "
                + "Upgrades' settings, and no change means the Forge found no idol to spend)");
            c.Check(!axeGone && !player.IsItemEquiped(axe) && !axe.m_equipped && !ReferenceEquals(player.m_hiddenLeftItem, axe)
                    && !ReferenceEquals(player.m_hiddenRightItem, axe),
                $"refined, the off-hand weapon comes back unequipped: not in hand, not put away ({HandsText(player)})");
            c.Check(ReferenceEquals(player.m_hiddenRightItem, sword) && player.m_hiddenLeftItem == null,
                $"the main weapon stays (still the put-away main weapon at the Forge; {HandsText(player)})");
            gui.Hide();
            player.SetCraftingStation(null);
            yield return new WaitForSecondsRealtime(0.4f);
            player.ShowHandItems();
            yield return null;
            yield return null;
            c.Check(Holds(player, sword, null) && !axe.m_equipped,
                $"after the Forge, R draws the main weapon alone ({HandsText(player)})");
            Invariant(c, player, "off-hand weapon refined");

            // X05, second half: the main weapon refined while the pair is in hand (equipped again with the window open,
            // as a click in the inventory does): the off-hand weapon move to the main hand.
            if (axeGone)
            {
                axe = bench.Inventory.AddItem(Axe, 1, 2, 0, 0L, "", false);
            }
            bench.GiveStack(swordIdol.m_resItem.name, swordIdol.GetAmount(sword.m_quality + 1));
            player.SetCraftingStation(forge);
            gui.Show(null, 3);
            yield return new WaitForSecondsRealtime(1f);
            bench.Empty();
            Hands.SetMarked(sword, false);
            Hands.SetMarked(axe, false);
            player.EquipItem(sword, false);
            player.EquipItem(axe, false);
            yield return null;
            yield return null;
            c.Check(InventoryGui.IsVisible() && ReferenceEquals(player.GetCurrentCraftingStation(), forge) && Holds(player, sword, axe),
                $"at the Forge with the pair equipped again in hand ({HandsText(player)})");
            level = sword.m_quality;
            yield return Refine(gui, sword);
            var swordGone = !bench.Inventory.ContainsItem(sword);
            c.Check(_refineListed && _refineStarted && !swordGone && sword.m_quality != level,
                $"the main-hand sword was refined at the Forge (listed {_refineListed}, timer started {_refineStarted}, still in the "
                + $"bag {!swordGone}, level {level} -> {sword.m_quality})");
            c.Check(!player.IsItemEquiped(sword) && !sword.m_equipped,
                $"refined, the main weapon comes back unequipped ({HandsText(player)})");
            c.Check(Holds(player, axe, null) && !Hands.IsMarked(axe),
                $"the off-hand weapon moves to the main hand, its marker cleared ({HandsText(player)})");
            Invariant(c, player, "main weapon refined");
            gui.Hide();
            player.SetCraftingStation(null);
            yield return new WaitForSecondsRealtime(0.4f);
            c.Check(Holds(player, axe, null) || ReferenceEquals(player.m_hiddenRightItem, axe),
                $"after the Forge the axe is still the player's main weapon ({HandsText(player)})");

            // X05, the main weapon once more, the way a player meet it at the Forge: the station put the pair away
            // (as in the first half), the put-away main weapon is refined, and R after the Forge draw the off-hand
            // weapon, in the main hand. A second SwordIron at level 2: the same refinement as the first one's.
            var sword2 = bench.Inventory.AddItem(Sword, 1, 2, 0, 0L, "", false);
            c.Check(sword2 != null, "a second SwordIron (level 2) given");
            if (sword2 == null)
            {
                c.Report();
                yield break;
            }
            bench.GiveStack(swordIdol.m_resItem.name, swordIdol.GetAmount(sword2.m_quality + 1));
            bench.Pair(sword2, axe);
            yield return WaitIdle(player);
            yield return WaitMinor(player);
            c.Check(Holds(player, sword2, axe), $"pair in hand again before the Forge ({HandsText(player)})");
            player.SetCraftingStation(forge);
            gui.Show(null, 3);
            yield return new WaitForSecondsRealtime(1f);
            c.Check(InventoryGui.IsVisible() && ReferenceEquals(player.GetCurrentCraftingStation(), forge)
                    && ReferenceEquals(player.m_hiddenRightItem, sword2) && ReferenceEquals(player.m_hiddenLeftItem, axe),
                $"at the Forge again, both weapons put away by it ({HandsText(player)})");
            level = sword2.m_quality;
            yield return Refine(gui, sword2);
            var sword2Gone = !bench.Inventory.ContainsItem(sword2);
            c.Check(_refineListed && _refineStarted && !sword2Gone && sword2.m_quality != level,
                $"the put-away main-hand sword was refined at the Forge (listed {_refineListed}, timer started {_refineStarted}, "
                + $"still in the bag {!sword2Gone}, level {level} -> {sword2.m_quality})");
            c.Check(!player.IsItemEquiped(sword2) && !sword2.m_equipped && !ReferenceEquals(player.m_hiddenRightItem, sword2)
                    && player.m_rightItem == null && player.m_leftItem == null && ReferenceEquals(player.m_hiddenLeftItem, axe),
                $"refined while put away, the main weapon comes back unequipped (no longer the put-away main weapon), the "
                + $"off-hand weapon is still put away ({HandsText(player)})");
            gui.Hide();
            player.SetCraftingStation(null);
            yield return new WaitForSecondsRealtime(0.4f);
            player.ShowHandItems();
            yield return null;
            yield return null;
            c.Check(Holds(player, axe, null) && !Hands.IsMarked(axe) && !sword2.m_equipped,
                $"after the Forge, R draws the former off-hand weapon in the main hand, its marker cleared ({HandsText(player)})");
            Invariant(c, player, "put-away main weapon refined");
            yield return NoteIfStuck(XForgeName, player, "the Forge");
            c.Report();
        }
        finally
        {
            try
            {
                if (InventoryGui.IsVisible())
                {
                    gui.Hide();
                }
                if (player.GetCurrentCraftingStation() != null)
                {
                    player.SetCraftingStation(null);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"Self test clean-up (Forge window) failed: {e}");
            }
            // Forge gone first, then the character forget it again (a station in range is discovered once a second).
            if (bench != null)
            {
                bench.TakeBack();
            }
            foreach (var name in taught)
            {
                player.m_knownRecipes.Remove(name);
            }
            foreach (var name in learned)
            {
                player.m_knownMaterial.Remove(name);
            }
            if (stationName != null)
            {
                if (stationKnown)
                {
                    player.m_knownStations[stationName] = stationLevel;
                }
                else
                {
                    player.m_knownStations.Remove(stationName);
                }
            }
            if (skills != null)
            {
                skills.Restore();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
            PutBack(player, spot, facing);
        }
    }

    // ---------- dual.x.morale (X06) ----------

    private static IEnumerator RunXMorale()
    {
        if (!NeedsMod(XMoraleName, MoraleGuid, "Creature Morale", "X06"))
        {
            yield break;
        }
        var c = new Checks(XMoraleName);
        var player = Player.m_localPlayer;
        if (player == null || ZoneSystem.instance == null)
        {
            SelfTest.Fail(XMoraleName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        SkillSave skills = null;
        var morale = MoraleHook.Open();
        var spot = player.transform.position;
        var facing = player.transform.rotation;
        try
        {
            c.Check(morale.Ready, $"Creature Morale can be given a boss rank for the test ({morale.Problem})");
            if (!morale.Ready)
            {
                c.Report();
                yield break;
            }
            bench = new Bench(player);
            skills = new SkillSave(player, Skills.SkillType.Swords, Skills.SkillType.Axes);
            var sword = bench.Give(Sword);
            var axe = bench.Give(Axe);
            if (sword == null || axe == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            c.Check(player.GetCurrentBiome() == Heightmap.Biome.Meadows,
                $"the test stands in the Meadows (it is {player.GetCurrentBiome()}): TESTING.md X06 is about a Greyling there");
            // Built-in default rules, but no "cornered" rule: next to the player the creature must no turn to fight
            // for that alone. Boss rank 3: a Greyling is afraid of the player.
            morale.DefaultRules(0f);
            morale.Rank(3);
            bench.Pair(sword, axe);
            yield return WaitIdle(player);
            yield return WaitMinor(player);
            yield return WaitCanAttack(player);
            c.Check(Holds(player, sword, axe) && _attackGate == null,
                $"sword main, axe off, and the player can attack ({HandsText(player)}; {_attackGate ?? "nothing in the way"})");
            var ahead = Forward(player);
            var farSpot = Ground(spot + RightOf(player) * 6f);
            var nearSpot = Ground(spot + ahead * 1.2f);
            var go = bench.SpawnAt(GreylingName, farSpot, Quaternion.LookRotation(spot - farSpot));
            var greyling = go != null ? go.GetComponent<Character>() : null;
            var ai = go != null ? go.GetComponent<MonsterAI>() : null;
            c.Check(greyling != null && ai != null, "a Greyling spawned 6 m to the side, its AI on");
            if (greyling == null || ai == null)
            {
                c.Report();
                yield break;
            }
            greyling.SetMaxHealth(100000f);
            greyling.SetHealth(100000f);
            var near = false;
            Action hold = () =>
            {
                // The player is heard, the creature is kept on its spot (it run in place).
                player.AddNoise(40f);
                PlaceCreature(greyling, near ? nearSpot : farSpot, player.transform.position);
            };

            // Afraid: it sense the noisy player 6 m away and runs (alerted), and never target him. Watched for 4 s: a
            // hostile Greyling pick the player as its target within a second or two.
            var targeted = false;
            var ran = false;
            var away = false;
            var until = Time.time + 4f;
            while (Time.time < until)
            {
                hold();
                yield return null;
                targeted |= ReferenceEquals(ai.m_targetCreature, player);
                ran |= ai.IsAlerted() && !ReferenceEquals(ai.m_targetCreature, player);
                var toPlayer = player.transform.position - greyling.transform.position;
                toPlayer.y = 0f;
                away |= ai.IsAlerted() && Vector3.Dot(greyling.m_moveDir, toPlayer.normalized) < -0.2f;
            }
            c.Check(ran && !targeted,
                $"boss rank 3: the Greyling 6 m away is afraid of the player: alerted (it runs), never targeting him in 4 s (alerted "
                + $"without the player as target {ran}, targeted the player {targeted})");
            SelfTest.Note(XMoraleName, $"while afraid its move direction pointed away from the player at some moment = {away} (it is kept on its spot)");

            // First swing in the air (the creature stays 6 m away), the second swing of the combo (off hand) hit it.
            DualSwing.ResetRecords();
            DualSwing.Recording = true;
            greyling.m_localPlayerHasHit = false;
            Face(player, ahead);
            var firstTouched = false;
            var targetedNear = false;
            DualSwing.DamageRecord offHit = null;
            until = Time.time + 10f;
            while (Time.time < until && offHit == null)
            {
                player.m_queuedAttackTimer = 0.5f;
                if (!near && DualSwing.Swings.Count >= 2)
                {
                    firstTouched = greyling.m_localPlayerHasHit || DualSwing.Damages.Count > 0;
                    near = true;
                }
                hold();
                yield return null;
                offHit = DualSwing.Damages.FirstOrDefault(d => d.Swing == 1);
                var targets = ReferenceEquals(ai.m_targetCreature, player);
                targeted |= !near && targets;
                // Moved next to the player, not hit yet: only the hit may make him its target.
                targetedNear |= near && offHit == null && targets;
            }
            player.m_queuedAttackTimer = 0f;
            c.Check(DualSwing.Swings.Count >= 2 && DualSwing.Swings[0].Trigger == "dualaxes0" && DualSwing.Swings[1].Trigger == "dualaxes1"
                    && !firstTouched && !targeted,
                $"the first swing of the pair hits nothing and the next swing is the second of the combo ({DualSwing.Swings.Count} "
                + $"swing(s): {string.Join(", ", DualSwing.Swings.Select(s => s.Trigger).ToArray())}; the first touched the Greyling "
                + $"{firstTouched}; it targeted the player before the hit {targeted})");
            c.Check(!targetedNear,
                "next to the player for the second swing, the afraid Greyling does not make him its target before the hit lands "
                + $"(targeted him before the hit {targetedNear})");
            c.Check(offHit != null && offHit.Hand == Hand.Off && offHit.Skill == axe.m_shared.m_skillType
                    && offHit.Target.StartsWith(GreylingName, StringComparison.Ordinal),
                $"that second swing hits the Greyling with the off-hand weapon (hit: "
                + $"{(offHit != null ? $"{offHit.Trigger} {offHit.Hand}, skill {offHit.Skill}, on {offHit.Target}" : "none")})");
            if (offHit != null)
            {
                // X06: it stops running and fight the player, as after a main-hand hit. The game itself make the
                // attacker the target at every hit that hurt (MonsterAI.OnDamaged), and Creature Morale take the
                // target away again from a creature that is still afraid: one frame with the player as target prove
                // nothing. "Fights" = he stays its target, watched for 1.5 s (afraid, it never had him as target in
                // the 4 s above).
                var frames = 0;
                var onPlayer = 0;
                until = Time.time + 1.5f;
                while (Time.time < until)
                {
                    hold();
                    yield return null;
                    frames++;
                    if (ReferenceEquals(ai.m_targetCreature, player))
                    {
                        onPlayer++;
                    }
                }
                var stays = ReferenceEquals(ai.m_targetCreature, player) && ai.IsAlerted();
                c.Check(stays && frames > 0 && onPlayer >= frames * 0.8f,
                    $"the off-hand hit makes the afraid Greyling fight the player: he is its target and stays it for 1.5 s (target "
                    + $"at the end {(ai.m_targetCreature != null ? ai.m_targetCreature.name : "none")}, alerted {ai.IsAlerted()}, the "
                    + $"player was its target in {onPlayer} of {frames} frames after the hit)");
            }
            yield return AttackAndLanding(player, 4f, hold);
            Invariant(c, player, "off-hand hit on an afraid creature");
            DestroyWorldObject(go);
            yield return NoteIfStuck(XMoraleName, player, "the fight");
            c.Report();
        }
        finally
        {
            morale.Close(XMoraleName);
            player.m_queuedAttackTimer = 0f;
            DualSwing.Recording = false;
            DualSwing.ResetRecords();
            if (bench != null)
            {
                bench.TakeBack();
            }
            if (skills != null)
            {
                skills.Restore();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
            PutBack(player, spot, facing);
        }
    }

    // ---------- dual.x.kills-page (X09) ----------

    // Kill counts of one creature's line on the Player Statistics page (-1 = not there, or no such part).
    private static int _pageKilled;
    private static int _pageMelee;
    private static string _pageLine = "";
    private static string _pageProblem;

    // The page opened like the raven button do (inventory shown, TextsDialog.Setup: the game's list build with every
    // mod's patch on it), the Player Statistics entry shown, the creature's line read, everything closed again.
    private static IEnumerator ReadKillsLine(Player player, string creature)
    {
        _pageKilled = -1;
        _pageMelee = -1;
        _pageLine = "";
        _pageProblem = null;
        var gui = InventoryGui.instance;
        if (gui == null || gui.m_textsDialog == null || Localization.instance == null)
        {
            _pageProblem = "no inventory window or no Compendium dialog";
            yield break;
        }
        var dialog = gui.m_textsDialog;
        var opened = false;
        if (!gui.m_animator.GetBool("visible"))
        {
            gui.Show(null);
            opened = true;
        }
        try
        {
            var end = Time.realtimeSinceStartup + 2f;
            while (dialog.transform.parent != null && !dialog.transform.parent.gameObject.activeInHierarchy
                   && Time.realtimeSinceStartup < end)
            {
                yield return null;
            }
            yield return null;
            if (dialog.transform.parent != null && !dialog.transform.parent.gameObject.activeInHierarchy)
            {
                _pageProblem = "the inventory did not show within 2 s";
                yield break;
            }
            dialog.Setup(player);
            var topic = Localization.instance.Localize("$inventory_stats");
            var texts = dialog.m_texts;
            var index = -1;
            for (var i = 0; i < texts.Count; i++)
            {
                if (texts[i] != null && texts[i].m_topic == topic)
                {
                    index = i;
                }
            }
            if (index < 0)
            {
                _pageProblem = $"no '{topic}' entry among the Compendium's {texts.Count} entries";
                yield break;
            }
            dialog.ShowText(index);
            yield return null;
            yield return null;
            var shown = dialog.m_textArea != null ? dialog.m_textArea.text : null;
            if (string.IsNullOrEmpty(shown))
            {
                _pageProblem = "the Player Statistics page shows no text";
                yield break;
            }
            _pageKilled = 0;
            _pageMelee = 0;
            foreach (var raw in shown.Split('\n'))
            {
                var line = raw.Trim();
                if (!line.StartsWith(creature + ": ", StringComparison.Ordinal))
                {
                    continue;
                }
                _pageLine = line;
                var killed = Regex.Match(line, @"(\d+) killed");
                var melee = Regex.Match(line, @"melee (\d+)");
                _pageKilled = killed.Success ? int.Parse(killed.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
                _pageMelee = melee.Success ? int.Parse(melee.Groups[1].Value, CultureInfo.InvariantCulture) : -1;
                break;
            }
        }
        finally
        {
            if (dialog.gameObject.activeSelf)
            {
                dialog.OnClose();
            }
            if (opened)
            {
                gui.Hide();
            }
        }
        yield return null;
    }

    private static IEnumerator RunXKillsPage()
    {
        if (!NeedsMod(XKillsPageName, StatsGuid, "Creature Kill and Tame Counts", "X09"))
        {
            yield break;
        }
        var c = new Checks(XKillsPageName);
        var player = Player.m_localPlayer;
        if (player == null || Localization.instance == null)
        {
            SelfTest.Fail(XKillsPageName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        SkillSave skills = null;
        try
        {
            bench = new Bench(player);
            skills = new SkillSave(player, Skills.SkillType.Swords, Skills.SkillType.Axes);
            var sword = bench.Give(Sword);
            var axe = bench.Give(Axe);
            var greyling = sword != null && axe != null ? bench.Spawn(GreylingName, DummyGap) : null;
            if (sword == null || axe == null || greyling == null || greyling.Character == null)
            {
                c.Check(false, "could not give the test items or spawn a Greyling");
                c.Report();
                yield break;
            }
            NoteDummy(XKillsPageName, greyling);
            // No loot and no ragdoll left behind; a creature nobody hit yet (see dual.kills).
            var loot = greyling.Go.GetComponent<CharacterDrop>();
            if (loot != null)
            {
                UnityEngine.Object.Destroy(loot);
            }
            greyling.Character.m_deathEffects = new EffectList();
            greyling.Refill = false;
            var zdo = greyling.Character.m_nview != null ? greyling.Character.m_nview.GetZDO() : null;
            if (zdo != null)
            {
                zdo.Set(ZDOVars.s_modifiers, (int)KillModifiers.CountNone);
            }
            var creature = Localization.instance.Localize(greyling.Character.m_name);

            // The page before the kill.
            yield return ReadKillsLine(player, creature);
            c.Check(_pageProblem == null, $"the Compendium's Player Statistics page can be read ({_pageProblem})");
            if (_pageProblem != null)
            {
                c.Report();
                yield break;
            }
            var killedBefore = _pageKilled;
            var meleeBefore = _pageMelee;
            var lineBefore = _pageLine;

            // The kill: after the first swing's (main hand) hit one hit point is left, the off-hand hit of the second
            // swing kills.
            bench.Pair(sword, axe);
            yield return new WaitForSeconds(0.5f);
            yield return WaitCanAttack(player);
            c.Check(Holds(player, sword, axe) && _attackGate == null,
                $"pair in hand and the player can attack before the kill ({HandsText(player)}; {_attackGate ?? "nothing in the way"})");
            DualSwing.ResetRecords();
            DualSwing.Recording = true;
            Action<DualSwing.HitRecord> weaken = hit =>
            {
                if (hit.Trigger == "dualaxes0" && greyling.Go != null)
                {
                    greyling.Character.SetHealth(1f);
                }
            };
            yield return Swing(player, greyling, 2, false, onHit: weaken);
            var last = DualSwing.Damages.LastOrDefault();
            var dead = greyling.Go == null || greyling.Character == null || greyling.Character.IsDead();
            c.Check(dead && last != null && last.Hand == Hand.Off && last.Trigger == "dualaxes1",
                $"the Greyling died from the off-hand hit of the second swing (dead {dead}, last hit "
                + $"{(last != null ? last.Trigger + " " + last.Hand : "none")})");
            DualSwing.Recording = false;
            yield return WaitIdle(player);
            yield return null;

            // X09: the kill counts under melee in the creature's line of the page.
            yield return ReadKillsLine(player, creature);
            c.Check(_pageProblem == null && _pageLine.Length > 0,
                $"after the kill the page has a line for {creature} ({_pageProblem ?? (_pageLine.Length > 0 ? _pageLine : "no such line")})");
            c.Check(_pageKilled == Mathf.Max(0, killedBefore) + 1,
                $"the {creature} line counts one more kill (before: '{(lineBefore.Length > 0 ? lineBefore : "no line")}', after: '{_pageLine}')");
            c.Check(_pageMelee >= 0 && _pageMelee == Mathf.Max(0, meleeBefore) + 1,
                $"the kill counts under melee in that line (melee {Mathf.Max(0, meleeBefore)} -> {_pageMelee}; -1 = the line shows no "
                + "melee part: Creature Kill and Tame Counts' weapon types are turned off in its settings)");
            c.Report();
        }
        finally
        {
            DualSwing.Recording = false;
            DualSwing.ResetRecords();
            if (bench != null)
            {
                bench.TakeBack();
            }
            if (skills != null)
            {
                skills.Restore();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
        }
    }
}
#endif
