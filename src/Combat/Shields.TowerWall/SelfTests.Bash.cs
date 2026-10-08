#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MC.Shared;
using UnityEngine;
using ItemType = ItemDrop.ItemData.ItemType;
using Object = UnityEngine.Object;

namespace MC.Combat.ShieldsTowerWallMod;

// Debug build only. More in-world self tests around the bash, the settings and the switch (helpers: SelfTests.cs and
// SelfTests.Play.cs).
//   tower.train     a bash that hits train Blocking (creature, training dummy), one that misses do not and still
//                   cost its stamina; a press kept for the cooldown cost nothing until its bash starts
//   tower.guard     bash from the guard and guard back after it; hit time at the default speed, Kick slowed too, a
//                   sword swing play at its own speed after a slowed bash; block, dodge and weapon switch right after a
//                   bash whose Custom animation be refused
//   tower.follow    one bash staggers a Greydwarf; Troll in two or more at the 2 s cadence; a staggered Draugr takes
//                   double damage from the sword hit after a quick swap; NG+: bashed Draugr is alerted, so the sword hit
//                   is no sneak attack but still a critical hit
//   tower.cadence   default rules: bash after bash on one Draugr, no second stagger inside the 8 s lock; lock 0: every
//                   bash that find it out of its stagger stagger it again
//   tower.group     default arc and lock: two Draugr, three Greydwarf: one heavy stagger per bash, the faced one
//   tower.live      every rule setting reach the held tower in about a second through the own-settings path
//   tower.warn      exact Warning texts (Custom trigger refused, unknown BashAnimation, Towers problems, other shield
//                   mods), none for ShieldUp, none for the default Towers list
//   tower.creature  FallenWarrior with a tower shield keep a normal shield next to its sword and fights with the
//                   sword; a bash counts as melee on the creature it hits
//   tower.switch    tower leave the Towers list in the middle of a slowed bash; real off / on (OnDeactivated, patches
//                   off; patches on, OnActivated) while braced, and in the middle of a Kick bash
//   tower.press     set-up and control of the kept-press case: the press is still kept 1 s later, a fresh press
//                   start an attack once the tower left the hand
//   tower.bug.keptpress  REAL BUG (fail on this build): a press kept for the bash cooldown start a punch or a sword
//                   swing by itself once the tower left the hand (TESTING T37)
//   tower.mods.crossbow  Crossbow Stays Loaded: a loaded crossbow put away by the tower is still loaded
//   tower.log       no error from this mod and no Towers warning under the default list in the whole run (run last)
internal static partial class SelfTests
{
    private const string TrainName = "tower.train";
    private const string GuardName = "tower.guard";
    private const string FollowName = "tower.follow";
    private const string CadenceName = "tower.cadence";
    private const string GroupName = "tower.group";
    private const string LiveName = "tower.live";
    private const string WarnName = "tower.warn";
    private const string CreatureName = "tower.creature";
    private const string SwitchName = "tower.switch";
    private const string PressName = "tower.press";
    private const string PressBugName = "tower.bug.keptpress";
    private const string CrossbowName = "tower.mods.crossbow";
    private const string LogName = "tower.log";

    private const string CrossbowGuid = "MC.Combat.Crossbow.StaysLoaded";

    // ---------- helpers ----------

    // Stamina one bash cost this player now (vanilla attack rules: equipment, effects, -33% x Blocking, world rate).
    private static float BashCost(Player p)
    {
        var rules = TowerSync.Applied;
        var cost = (rules != null ? rules.BashStamina : TowerRules.DefaultBashStamina) * (1f + p.GetEquipmentAttackStaminaModifier());
        p.GetSEMan().ModifyAttackStaminaUsage(cost, ref cost);
        cost -= cost * 0.33f * p.GetSkillFactor(Skills.SkillType.Blocking);
        return cost * Game.m_staminaRate;
    }

    // A fallback an earlier test's bash made stay while the same rules stay applied: start from the chosen option.
    private static IEnumerator FreshBash(Player p, ItemDrop.ItemData tower)
    {
        BashAttack.Reset();
        if (!p.IsItemEquiped(tower))
        {
            p.EquipItem(tower);
        }
        yield return Fixed;
        BashAttack.Get(p);
    }

    // A bash with nothing in its lane: what it cost, whether its Hit event came.
    private static IEnumerator Miss(Player p, string test, Swing s)
    {
        yield return WaitIdle(p);
        yield return WaitCooldown(p);
        FaceClearLane(p, 3f, 35f, test, null);
        yield return Fixed;
        var events0 = BashWatch.DebugHitEvents;
        var stamina0 = p.GetStamina();
        var low = stamina0;
        var t0 = Time.time;
        s.Mode = BashAttack.Mode;
        s.Started = p.StartAttack(null, false);
        var attack = s.Started ? p.m_currentAttack : null;
        if (attack == null)
        {
            yield break;
        }
        s.Trigger = attack.m_attackAnimation;
        while (Time.time - t0 < 6f)
        {
            yield return null;
            low = Mathf.Min(low, p.GetStamina());
            if (s.HitAt < 0f && BashWatch.DebugHitEvents > events0)
            {
                s.HitAt = BashWatch.DebugFirstHitTime - t0;
            }
            if (!ReferenceEquals(p.m_currentAttack, attack) || attack.IsDone())
            {
                s.EndAt = Time.time - t0;
                break;
            }
        }
        yield return Fixed;
        yield return Fixed;
        s.Events = BashWatch.DebugHitEvents - events0;
        s.StaminaUsed = stamina0 - Mathf.Min(low, p.GetStamina());
        s.ModeAfter = BashAttack.Mode;
    }

    // Sword hit as a player's swing send it (20 slash, backstab x3 like a sword), built in code: exact numbers.
    private static HitData SwordHit(Player p, Character target, float slash)
    {
        var hit = BashHit(p, target, 0f, 1f);
        hit.m_damage.m_slash = slash;
        hit.m_skill = Skills.SkillType.Swords;
        hit.m_backstabBonus = 3f;
        return hit;
    }

    // Health a slash hit take from this creature after the multiplier (critical x2, backstab x3), vanilla order:
    // resistances, NG+ armor, player-count scale, world damage rate.
    private static float SlashLanded(Character c, float slash, float multiplier)
    {
        var hit = new HitData();
        hit.m_damage.m_slash = slash;
        hit.ApplyModifier(multiplier);
        hit.ApplyResistance(c.GetDamageModifiers(), out _);
        if (Game.m_worldLevel > 0)
        {
            hit.ApplyArmor(Game.m_worldLevel * Game.instance.m_worldLevelEnemyBaseAC);
        }
        hit.ApplyModifier(Game.instance.GetDifficultyDamageScaleEnemy(c.transform.position));
        hit.ApplyModifier(Game.m_playerDamageRate);
        return hit.GetTotalDamage();
    }

    // One swing of the weapon in the right hand: highest animator speed in its attack state, and whether the bash
    // slowdown touched it.
    private static IEnumerator WeaponSwing(Player p, Box<float> top, Box<bool> slowed)
    {
        top.Value = 0f;
        slowed.Value = false;
        yield return WaitIdle(p);
        var started = p.StartAttack(null, false);
        var t0 = Time.time;
        var seen = false;
        while (started && Time.time - t0 < 3f)
        {
            yield return null;
            if (p.InAttack())
            {
                seen = true;
                top.Value = Mathf.Max(top.Value, p.m_animator.speed);
                slowed.Value |= BashSpeed.Scaling;
            }
            else if (seen || Time.time - t0 > 0.5f)
            {
                break;
            }
        }
        yield return WaitIdle(p);
    }

    // ---------- tower.train ----------

    private static IEnumerator RunTrain()
    {
        var c = new Checks(TrainName);
        var rig = new Rig(TrainName);
        try
        {
            var p = rig.P;
            var rules = TowerRules.Defaults();
            yield return UseRules(rules);
            var tower = rig.Give(IronTower);
            var draugr = rig.Creature("Draugr");
            if (tower == null || draugr == null)
            {
                c.Check(false, "could not add a ShieldIronTower or spawn a Draugr");
                c.Report();
                yield break;
            }
            yield return FreshBash(p, tower);
            var taken = BluntTaken(draugr);

            // TESTING T12: the bash is the shield-arm punch, low damage, and a hit train Blocking.
            SetSkill(p, Skills.SkillType.Blocking, 0f);
            var before = Trained(p, Skills.SkillType.Blocking);
            var s = new Swing();
            yield return Press(p, draugr, s, TrainName, null);
            SelfTest.Note(TrainName, Describe("bash on a Draugr", s));
            c.Check(s.Started && s.Mode == BashAnimationKind.ShieldPunch && s.Trigger == BashAttack.ShieldArmTrigger,
                $"pressing attack with the tower plays the shield-arm punch (ShieldPunch, '{s.Trigger}')");
            c.Check(s.Landed && s.Lost <= 12f * 0.55f * taken + 0.2f, $"the bash lands with low damage (health -{F(s.Lost)}, at most {F(12f * 0.55f * taken)} at Blocking 0)");
            c.Check(s.Landed && Trained(p, Skills.SkillType.Blocking) > before, "bashing a creature raises the Blocking skill");

            // TESTING T35 (first half): a bash that hits nothing still cost its stamina, about 20, and train nothing.
            Park(p, draugr);
            rig.TakeStaminaRate();
            p.m_stamina = p.GetMaxStamina();
            yield return Fixed;
            SetSkill(p, Skills.SkillType.Blocking, 0f);
            before = Trained(p, Skills.SkillType.Blocking);
            var cost = BashCost(p);
            var miss = new Swing();
            yield return Miss(p, TrainName, miss);
            SelfTest.Note(TrainName, $"bash at nothing: started {miss.Started}, Hit events {miss.Events}, stamina -{F(miss.StaminaUsed)} (expected {F(cost)}; world stamina rate {F(Game.m_staminaRate)})");
            c.Check(Game.m_staminaRate > 0f && miss.Started && Near(miss.StaminaUsed, cost, 0.3f), $"a bash that misses costs its stamina too ({F(miss.StaminaUsed)}, {F(cost)} expected)");
            c.Check(Near(cost, 20f, 0.5f), $"one bash costs about 20 stamina at Blocking 0 ({F(cost)})");
            c.Check(miss.Events == 1 && Trained(p, Skills.SkillType.Blocking) <= before, "a bash that hits nothing trains no Blocking");

            // TESTING T33: one press after the swing wait for the cooldown and cost nothing until its bash starts.
            p.m_stamina = p.GetMaxStamina();
            var first = new Swing();
            yield return Press(p, draugr, first, TrainName, null);
            SetSkill(p, Skills.SkillType.Blocking, 0f);
            cost = BashCost(p);
            var starts = BashWatch.DebugStarts.Count;
            var left = BashWatch.CooldownLeft(p);
            var stamina = p.GetStamina();
            var lowest = stamina;
            var atStart = stamina; // last value read while the press still waited
            p.m_queuedAttackTimer = BashWatch.QueuedPress; // what one press of the attack button does
            var until = Time.time + left + 1f;
            while (Time.time < until && BashWatch.DebugStarts.Count == starts)
            {
                yield return null;
                if (BashWatch.DebugStarts.Count == starts)
                {
                    atStart = p.GetStamina();
                    lowest = Mathf.Min(lowest, atStart);
                }
            }
            var again = BashWatch.DebugStarts.Count > starts;
            yield return Until(() => p.InAttack(), 0.6f);
            yield return FixedSteps(3);
            var spent = atStart - p.GetStamina();
            c.Check(first.Started && left > BashWatch.QueuedPress && again && lowest >= stamina - 0.01f,
                $"a press {F(left)} s before the cooldown ends is kept and costs no stamina while it waits (lowest {F(lowest)}, {F(stamina)} at the press; bash started {again})");
            c.Check(again && Near(spent, cost, 1f), $"the stamina is spent when that bash starts ({F(spent)}, {F(cost)} expected)");
            yield return WaitIdle(p);

            // TESTING T32: bashing the training dummy train Blocking.
            var dummy = rig.Creature("piece_TrainingDummy");
            if (dummy == null)
            {
                c.Check(false, "could not spawn piece_TrainingDummy");
                c.Report();
                yield break;
            }
            var wnt = dummy.GetComponent<WearNTear>();
            if (wnt != null)
            {
                wnt.enabled = false; // no support check: it never falls apart during the test
            }
            Park(p, draugr);
            yield return Frames(3);
            p.m_stamina = p.GetMaxStamina();
            SetSkill(p, Skills.SkillType.Blocking, 0f);
            before = Trained(p, Skills.SkillType.Blocking);
            var d = new Swing();
            yield return Press(p, dummy, d, TrainName, "dummy");
            SelfTest.Note(TrainName, Describe("bash on the training dummy", d));
            c.Check(d.Hit && d.Lost > 0.001f, $"the bash hits the training dummy (health -{F(d.Lost)})");
            c.Check(Trained(p, Skills.SkillType.Blocking) > before, "bashing the training dummy raises the Blocking skill");
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- tower.guard ----------

    private static IEnumerator RunGuard()
    {
        var c = new Checks(GuardName);
        var rig = new Rig(GuardName);
        try
        {
            var p = rig.P;
            var rules = TowerRules.Defaults();
            yield return UseRules(rules);
            var tower = rig.Give(IronTower);
            var sword = rig.Give("SwordIron");
            var troll = rig.Creature("Troll");
            if (tower == null || sword == null || troll == null)
            {
                c.Check(false, "could not add ShieldIronTower and SwordIron or spawn a Troll");
                c.Report();
                yield break;
            }
            // Control before any bash: a sword swing's own top speed.
            var top = new Box<float>();
            var slowed = new Box<bool>();
            p.EquipItem(sword);
            yield return Fixed;
            yield return WeaponSwing(p, top, slowed);
            var swordSpeed = top.Value;
            yield return FreshBash(p, tower);

            // TESTING T14: hold block, press attack: the bash plays, the guard is back right after it.
            yield return WaitIdle(p);
            yield return WaitCooldown(p);
            FaceClearLane(p, Reach(p, troll), LaneFan, GuardName, troll);
            PlaceInFront(p, troll);
            troll.SetHealth(troll.GetMaxHealth());
            yield return Guard(p, true);
            c.Check(p.IsBlocking() && Brace.Clone != null && !Brace.Clone.m_hidden, "block held with the tower: braced");
            var health = troll.GetHealth();
            var events = BashWatch.DebugHitEvents;
            var started = p.StartAttack(null, false);
            var attack = started ? p.m_currentAttack : null;
            var inSwing = false;
            var blockedInSwing = false;
            var t0 = Time.time;
            var end = -1f;
            while (attack != null && Time.time - t0 < 5f)
            {
                yield return null;
                if (p.InAttack())
                {
                    inSwing = true;
                    blockedInSwing |= p.IsBlocking();
                }
                if (!ReferenceEquals(p.m_currentAttack, attack) || attack.IsDone())
                {
                    end = Time.time;
                    break;
                }
            }
            yield return Until(() => p.IsBlocking() && Brace.Clone != null && !Brace.Clone.m_hidden, 1f);
            var back = end >= 0f ? Time.time - end : -1f;
            c.Check(started && inSwing && !blockedInSwing && BashWatch.DebugHitEvents > events && troll.GetHealth() < health - 0.001f,
                $"attack pressed while blocking: the bash plays and lands (started {started}, Troll health {F(health)} -> {F(troll.GetHealth())})");
            c.Check(p.IsBlocking() && back >= 0f && back <= 0.4f, $"with block still held the guard is back {T(back)} after the swing");
            yield return Guard(p, false);

            // TESTING T34: the default swing's hit come about 0.8 s after the press; after it other attacks play at
            // their own speed.
            var slow = new Swing();
            yield return Press(p, troll, slow, GuardName, null);
            SelfTest.Note(GuardName, Describe("default swing", slow));
            c.Check(slow.Landed && slow.Mode == BashAnimationKind.ShieldPunch && slow.HitAt >= 0.6f && slow.HitAt <= 1.0f,
                $"default speed 0.6: the hit comes {T(slow.HitAt)} after the press (about 0.8 s)");
            CheckSpeed(c, "default swing", slow);
            yield return WaitIdle(p);
            p.EquipItem(sword);
            yield return Fixed;
            yield return WeaponSwing(p, top, slowed);
            c.Check(swordSpeed > 0f && Near(top.Value, swordSpeed, 0.05f) && !slowed.Value,
                $"after a slowed bash a sword swing plays at its own speed (top animator speed {F(top.Value)}, {F(swordSpeed)} before any bash)");
            p.EquipItem(tower);
            yield return Fixed;
            // Kick slowed too.
            var kick = WithAnimation(BashAnimationKind.Kick);
            yield return UseRules(kick);
            var k6 = new Swing();
            yield return Press(p, troll, k6, GuardName, null);
            yield return UseRules(kick.With(r => r.BashAnimationSpeed = 1f));
            var k1 = new Swing();
            yield return Press(p, troll, k1, GuardName, null);
            SelfTest.Note(GuardName, Describe("Kick at 0.6", k6) + " || " + Describe("Kick at 1", k1));
            c.Check(k6.Mode == BashAnimationKind.Kick && k1.Mode == BashAnimationKind.Kick && k6.HitAt > 0f && k1.HitAt > 0f && k6.HitAt / k1.HitAt >= 1.3f,
                $"the Kick is slowed too: hit {T(k6.HitAt)} at 0.6, {T(k1.HitAt)} at speed 1");
            CheckSpeed(c, "Kick at 0.6", k6);

            // TESTING T15 (end): Custom staff_rapidfire is refused, ShieldPunch plays, and player can block, dodge
            // and switch weapons right after that bash.
            yield return UseRules(WithAnimation(BashAnimationKind.Custom, "staff_rapidfire"));
            var punch = new Swing();
            yield return Press(p, troll, punch, GuardName, null);
            SelfTest.Note(GuardName, Describe("Custom staff_rapidfire", punch));
            c.Check(punch.Started && punch.Mode == BashAnimationKind.ShieldPunch && punch.Trigger == BashAttack.ShieldArmTrigger && punch.Hit && punch.EndAt > 0f,
                $"Custom staff_rapidfire: the ShieldPunch animation plays, hits and ends ('{punch.Trigger}', swing over at {T(punch.EndAt)})");
            SelfTestHooks.HoldBlock = true;
            yield return Until(() => p.IsBlocking(), 0.6f);
            var blocks = p.IsBlocking();
            SelfTestHooks.HoldBlock = false;
            yield return Until(() => !p.m_internalBlockingState, 1f);
            p.Dodge(-Flat(p.transform.forward));
            yield return Until(() => p.InDodge(), 1f);
            var dodges = p.InDodge();
            yield return Until(() => !p.InDodge(), 3f);
            yield return Fixed;
            var switches = p.EquipItem(sword);
            c.Check(blocks && dodges && switches && Only(p, sword, left: false),
                $"right after that bash: block {blocks}, dodge {dodges}, switch to the sword {switches} ({Hands(p)})");
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- tower.follow ----------

    private static void Deafen(Character c)
    {
        var ai = c != null ? c.GetComponent<MonsterAI>() : null;
        if (ai != null)
        {
            ai.m_viewRange = 0f;
            ai.m_hearRange = 0f;
        }
    }

    private static IEnumerator RunFollow()
    {
        var c = new Checks(FollowName);
        var rig = new Rig(FollowName);
        try
        {
            var p = rig.P;
            var rules = TowerRules.Defaults();
            yield return UseRules(rules);
            var tower = rig.Give(IronTower);
            var sword = rig.Give("SwordIron");
            var grey = rig.Creature("Greydwarf");
            var draugr = rig.Creature("Draugr");
            var troll = rig.Creature("Troll");
            if (tower == null || sword == null || grey == null || draugr == null || troll == null)
            {
                c.Check(false, "could not add ShieldIronTower and SwordIron or spawn a Greydwarf, a Draugr and a Troll");
                c.Report();
                yield break;
            }
            Park(p, draugr, 15f);
            Park(p, troll, -25f);
            yield return FreshBash(p, tower);

            // TESTING T13: Greydwarf in one bash.
            SetSkill(p, Skills.SkillType.Blocking, 0f);
            var s = new Swing();
            yield return Press(p, grey, s, FollowName, null);
            SelfTest.Note(FollowName, Describe("Greydwarf", s) + $" (threshold {F(grey.GetStaggerTreshold())})");
            c.Check(s.Landed && s.Staggered, "one Iron tower bash staggers a Greydwarf");
            ZNetScene.instance.Destroy(grey.gameObject);

            // Draugr in one bash, then the quick swap: the sword hit on the staggered Draugr is a critical hit (x2).
            SetSkill(p, Skills.SkillType.Blocking, 0f);
            s = new Swing();
            yield return Press(p, draugr, s, FollowName, null);
            c.Check(s.Landed && s.Staggered, "one Iron tower bash staggers a Draugr");
            yield return Until(() => !p.InAttack(), 2f);
            var swapped = p.EquipItem(sword);
            var since = Time.time - s.HitTime;
            var still = draugr.IsStaggering();
            c.Check(swapped && Only(p, sword, left: false) && still, $"quick swap: the sword is in hand {F(since)} s after the bash hit and the Draugr still staggers ({still})");
            draugr.SetHealth(draugr.GetMaxHealth());
            var health = draugr.GetHealth();
            var staggering = draugr.IsStaggering();
            draugr.RPC_Damage(0L, SwordHit(p, draugr, 20f));
            var crit = health - draugr.GetHealth();
            var twice = SlashLanded(draugr, 20f, 2f);
            c.Check(staggering && Near(crit, twice, 0.3f),
                $"a sword hit on the staggered Draugr deals double damage ({F(crit)}, {F(twice)} expected for 20 slash x2; x1 would be {F(SlashLanded(draugr, 20f, 1f))}, a sneak attack x6 {F(SlashLanded(draugr, 20f, 6f))})");
            // A real swing right after the swap lands while it still staggers.
            PlaceInFront(p, draugr);
            draugr.SetHealth(draugr.GetMaxHealth());
            yield return Fixed;
            health = draugr.GetHealth();
            var swung = p.StartAttack(null, false);
            var landed = false;
            var whileStaggering = false;
            var t0 = Time.time;
            while (swung && Time.time - t0 < 2.5f)
            {
                yield return null;
                if (draugr.GetHealth() < health - 0.01f)
                {
                    landed = true;
                    whileStaggering = draugr.IsStaggering();
                    break;
                }
            }
            c.Check(swung && landed && whileStaggering,
                $"a real sword swing right after the swap lands {F(Time.time - s.HitTime)} s after the bash hit, while the Draugr still staggers (landed {landed}, staggering {whileStaggering})");
            // Control: the same hit once the stagger is over is a normal hit.
            yield return WaitIdle(p);
            yield return Until(() => !draugr.IsStaggering(), 6f);
            yield return Fixed;
            draugr.SetHealth(draugr.GetMaxHealth());
            health = draugr.GetHealth();
            draugr.RPC_Damage(0L, SwordHit(p, draugr, 20f));
            var plain = health - draugr.GetHealth();
            c.Check(Near(plain, SlashLanded(draugr, 20f, 1f), 0.3f), $"control: the same sword hit after the stagger deals normal damage ({F(plain)})");
            Park(p, draugr, 15f);

            // Troll: two or more bashes at the 2 s cadence (TESTING T13 say two to four; the skill roll is random).
            yield return WaitIdle(p);
            p.EquipItem(tower);
            yield return Fixed;
            troll.m_staggerDamage = 0f;
            var bashes = 0;
            var staggered = false;
            var gains = new List<string>();
            for (var press = 1; press <= 6 && !staggered; press++)
            {
                SetSkill(p, Skills.SkillType.Blocking, 0f);
                var t = new Swing();
                yield return Press(p, troll, t, FollowName, null, resetBar: false);
                if (t.Landed)
                {
                    bashes++;
                    staggered = t.Staggered;
                    gains.Add(F(t.Gained));
                }
            }
            SelfTest.Note(FollowName, $"Troll (threshold {F(troll.GetStaggerTreshold())}): staggered {staggered} after {bashes} bash(es) at the cooldown's cadence, stagger per bash {string.Join(", ", gains.ToArray())}");
            c.Check(staggered && bashes >= 2 && bashes <= 6, $"a Troll staggers after two or more bashes 2 s apart, never one ({bashes}; TESTING T13: two to four)");
            Park(p, troll, -25f);

            // TESTING T23 (end): NG+. An unaware Draugr take a sneak attack; one that be just bashed is alerted, so the
            // sword hit right after is no sneak attack, but still a critical hit (it staggers).
            rig.SetWorldLevel(1);
            yield return null;
            c.Check(Game.m_worldLevel == 1, $"world level {Game.m_worldLevel} for the New Game+ part");
            var unaware = rig.Creature("Draugr");
            var bashed = rig.Creature("Draugr");
            if (unaware == null || bashed == null)
            {
                c.Check(false, "could not spawn the two New Game+ Draugr");
                c.Report();
                yield break;
            }
            Deafen(unaware);
            Deafen(bashed);
            Park(p, unaware, 40f);
            yield return Fixed;
            var ai = bashed.GetComponent<MonsterAI>();
            unaware.SetHealth(unaware.GetMaxHealth());
            health = unaware.GetHealth();
            unaware.RPC_Damage(0L, SwordHit(p, unaware, 60f));
            var sneak = health - unaware.GetHealth();
            c.Check(sneak >= SlashLanded(unaware, 60f, 1f) * 1.5f,
                $"control: a sword hit on an unaware Draugr is a sneak attack ({F(sneak)} damage, a normal hit {F(SlashLanded(unaware, 60f, 1f))})");
            c.Check(ai != null && !ai.IsAlerted(), "the other Draugr is unaware before the bash");
            SetSkill(p, Skills.SkillType.Blocking, 0f);
            s = new Swing();
            yield return Press(p, bashed, s, FollowName, null);
            SelfTest.Note(FollowName, Describe("New Game+ bash", s));
            c.Check(s.Landed && s.Staggered && s.Lost <= 1f, $"New Game+: one bash still staggers a Draugr and shows almost no damage (health -{F(s.Lost)})");
            c.Check(ai != null && ai.IsAlerted() && ReferenceEquals(ai.GetTargetCreature(), p), "the bashed Draugr turns to fight: alerted, targeting the player");
            bashed.SetHealth(bashed.GetMaxHealth());
            health = bashed.GetHealth();
            staggering = bashed.IsStaggering();
            bashed.RPC_Damage(0L, SwordHit(p, bashed, 60f));
            var after = health - bashed.GetHealth();
            twice = SlashLanded(bashed, 60f, 2f);
            c.Check(staggering && Near(after, twice, 0.5f),
                $"the sword hit right after the bash is no sneak attack but a critical hit: {F(after)} damage ({F(twice)} expected for x2; a sneak attack on top would be {F(SlashLanded(bashed, 60f, 6f))})");
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- tower.cadence ----------

    private static IEnumerator RunCadence()
    {
        var c = new Checks(CadenceName);
        var rig = new Rig(CadenceName);
        try
        {
            var p = rig.P;
            var rules = TowerRules.Defaults();
            yield return UseRules(rules);
            var tower = rig.Give(IronTower);
            var draugr = rig.Creature("Draugr");
            if (tower == null || draugr == null)
            {
                c.Check(false, "could not add a ShieldIronTower or spawn a Draugr");
                c.Report();
                yield break;
            }
            yield return FreshBash(p, tower);
            c.Check(Near(rules.BashCooldown, 2f) && Near(rules.BashStaggerLock, 8f) && BashAttack.Mode == BashAnimationKind.ShieldPunch,
                "default rules: ShieldPunch, cooldown 2 s, stagger lock 8 s");

            // TESTING T21: bash after bash; the first staggers, the next ones inside 8 s do not, the first after does.
            SetSkill(p, Skills.SkillType.Blocking, 0f);
            var first = new Swing();
            yield return Press(p, draugr, first, CadenceName, null);
            c.Check(first.Landed && first.Staggered, $"the first bash staggers the Draugr (threshold {F(draugr.GetStaggerTreshold())})");
            if (!first.Landed)
            {
                c.Report();
                yield break;
            }
            var t1 = first.HitTime;
            var inside = 0;
            var afterLock = false;
            var times = new List<string>();
            for (var press = 2; press <= 8 && !afterLock; press++)
            {
                SetSkill(p, Skills.SkillType.Blocking, 0f);
                var s = new Swing();
                yield return Press(p, draugr, s, CadenceName, null, resetBar: false);
                if (!s.Landed)
                {
                    SelfTest.Note(CadenceName, Describe($"press {press} (no hit)", s));
                    continue;
                }
                var at = s.HitTime - t1;
                times.Add(F(at));
                if (at < rules.BashStaggerLock)
                {
                    inside++;
                    c.Check(s.Gained <= s.Lost + 1f, $"bash {F(at)} s after the first stagger: no heavy stagger (bar +{F(s.Gained)}, landed {F(s.Lost)})");
                    if (at > 4f)
                    {
                        c.Check(!s.StaggeringBefore && !draugr.IsStaggering(), $"bash {F(at)} s after the first stagger: the Draugr is out of its stagger and does not stagger again");
                    }
                }
                else
                {
                    afterLock = true;
                    c.Check(s.Staggered && !s.StaggeringBefore, $"the first bash after the lock ({F(at)} s) staggers it again");
                }
            }
            SelfTest.Note(CadenceName, $"default cadence: bashes landed {string.Join(", ", times.ToArray())} s after the first stagger");
            c.Check(inside >= 3 && afterLock, $"{inside} bashes landed inside the 8 s lock, then one after it ({afterLock})");
            yield return Until(() => draugr.IsStaggering(), 0.5f);
            var from = Time.time;
            yield return Until(() => !draugr.IsStaggering(), 5f);
            SelfTest.Note(CadenceName, $"a Draugr stagger lasts about {F(Time.time - from + 0.5f)} s (measured from the end of the bash)");

            // BashStaggerLock 0: every bash that lands once the last stagger is over stagger it again.
            yield return UseRules(TowerRules.Defaults().With(r => r.BashStaggerLock = 0f));
            draugr.m_staggerDamage = 0f;
            var staggers = 0;
            var free = 0;
            var pattern = new List<string>();
            for (var press = 1; press <= 6; press++)
            {
                SetSkill(p, Skills.SkillType.Blocking, 0f);
                var s = new Swing();
                yield return Press(p, draugr, s, CadenceName, null, resetBar: false);
                if (!s.Landed)
                {
                    pattern.Add("miss");
                    continue;
                }
                if (s.StaggeringBefore)
                {
                    pattern.Add("still staggering");
                    continue;
                }
                free++;
                if (s.Staggered)
                {
                    staggers++;
                }
                pattern.Add(s.Staggered ? "staggers" : "NO stagger");
                c.Check(s.Staggered, $"lock 0, bash {press}: the Draugr was out of its stagger, so this bash staggers it again (bar +{F(s.Gained)})");
            }
            SelfTest.Note(CadenceName, $"lock 0, six bashes 2 s apart: {string.Join(", ", pattern.ToArray())}");
            c.Check(staggers >= 2 && staggers == free, $"lock 0: {staggers} staggers from the {free} bashes that found it out of its stagger");
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- tower.group ----------

    private sealed class GroupHit
    {
        internal bool Started;
        internal bool Hit;
        internal float HitTime;
        internal Character Pick;
        internal float[] Gain;
        internal float[] Lost;
        internal float[] Push;
        internal float[] Moved;
        internal bool[] Staggering;
    }

    private static readonly WaitForEndOfFrame FrameEnd = new WaitForEndOfFrame();

    // One bash at creatures already placed (first = the faced one). Bars emptied first; gains read right after the Hit
    // event (drain since put back); the push each one got = highest push force seen after every physics step and at the
    // end of every frame of the swing (vanilla let it fade 100 per second: read later it may be gone); where they
    // stand and the stagger animation 0.35 s after the hit.
    private static IEnumerator GroupBash(Player p, Character[] group, GroupHit g)
    {
        var n = group.Length;
        g.Gain = new float[n];
        g.Lost = new float[n];
        g.Push = new float[n];
        g.Moved = new float[n];
        g.Staggering = new bool[n];
        var health = new float[n];
        var place = new Vector3[n];
        foreach (var c in group)
        {
            c.m_staggerDamage = 0f;
            c.SetHealth(c.GetMaxHealth());
        }
        yield return Fixed;
        for (var i = 0; i < n; i++)
        {
            group[i].m_staggerDamage = 0f;
            group[i].m_pushForce = Vector3.zero;
            health[i] = group[i].GetHealth();
            place[i] = group[i].transform.position;
        }
        var events = BashWatch.DebugHitEvents;
        BashTarget.DebugLastPick = null;
        g.Started = p.StartAttack(null, false);
        var until = Time.time + 4f;
        var extra = 2;
        var read = false;
        while (g.Started && Time.time < until && extra > 0)
        {
            yield return Fixed;
            for (var i = 0; i < n; i++)
            {
                g.Push[i] = Mathf.Max(g.Push[i], group[i].m_pushForce.magnitude);
            }
            yield return FrameEnd;
            for (var i = 0; i < n; i++)
            {
                g.Push[i] = Mathf.Max(g.Push[i], group[i].m_pushForce.magnitude);
            }
            if (BashWatch.DebugHitEvents > events)
            {
                if (!read)
                {
                    read = true;
                    g.Hit = true;
                    g.HitTime = BashWatch.DebugFirstHitTime;
                    g.Pick = BashTarget.DebugLastPick;
                    var since = Time.time - g.HitTime;
                    for (var i = 0; i < n; i++)
                    {
                        g.Gain[i] = group[i].m_staggerDamage + Drain(group[i]) * since;
                        g.Lost[i] = health[i] - group[i].GetHealth();
                    }
                }
                extra--;
            }
        }
        if (!g.Hit)
        {
            g.HitTime = -1f;
            yield break;
        }
        yield return UntilTime(g.HitTime + 0.35f);
        for (var i = 0; i < n; i++)
        {
            g.Moved[i] = Vector3.Dot(group[i].transform.position - place[i], Flat(place[i] - p.transform.position));
            g.Staggering[i] = group[i].IsStaggering();
        }
    }

    // Faced creature 1.45 m ahead (centre to centre, inside the bash range), the others beside it at the smallest
    // angle that keep the capsules apart: the outer rays of the default 60 degree arc reach them before the faced
    // one. Nothing else in the lane. Angles of the others in degrees.
    private static float[] PlaceGroup(Player p, Character[] group, string test)
    {
        for (var i = 0; i < group.Length; i++)
        {
            Park(p, group[i], 25f * i);
        }
        FaceClearLane(p, 1.45f + Radius(group[0]) + 0.15f, 55f, test, null);
        var gap = Mathf.Max(0.3f, 1.45f - Radius(p) - Radius(group[0]));
        PlaceInFront(p, group[0], gap);
        var angles = new float[group.Length];
        for (var i = 1; i < group.Length; i++)
        {
            angles[i] = PlaceBeside(p, group[i], group[0], i % 2 == 1 ? 1f : -1f);
        }
        return angles;
    }

    private static IEnumerator RunGroup()
    {
        var c = new Checks(GroupName);
        var rig = new Rig(GroupName);
        try
        {
            var p = rig.P;
            var rules = TowerRules.Defaults();
            yield return UseRules(rules);
            var tower = rig.Give(IronTower);
            var a = rig.Creature("Draugr");
            var b = rig.Creature("Draugr");
            if (tower == null || a == null || b == null)
            {
                c.Check(false, "could not add a ShieldIronTower or spawn two Draugr");
                c.Report();
                yield break;
            }
            yield return FreshBash(p, tower);
            c.Check(Near(rules.BashAngle, 60f) && Near(rules.BashStaggerLock, 8f) && Near(rules.BashRange, 1.8f), "default rules: arc 60 degrees, range 1.8 m, stagger lock 8 s");
            var g = new GroupHit();

            // TESTING T36. Bash 1: A faced, B beside it. Both hit and pushed, only A staggers.
            SetSkill(p, Skills.SkillType.Blocking, 0f);
            yield return WaitIdle(p);
            yield return WaitCooldown(p);
            var angles = PlaceGroup(p, new[] { a, b }, GroupName);
            yield return GroupBash(p, new[] { a, b }, g);
            var firstHit = g.HitTime;
            SelfTest.Note(GroupName, $"bash 1, Draugr B {F(angles[1])} degrees beside the faced Draugr A: started {g.Started}, hit {g.Hit}, picked "
                                     + $"{(ReferenceEquals(g.Pick, a) ? "A" : ReferenceEquals(g.Pick, b) ? "B" : "none")}; A bar +{F(g.Gain[0])} of {F(a.GetStaggerTreshold())}, health -{F(g.Lost[0])}, "
                                     + $"push {F(g.Push[0])}, moved {F(g.Moved[0])} m; B bar +{F(g.Gain[1])}, health -{F(g.Lost[1])}, push {F(g.Push[1])}, moved {F(g.Moved[1])} m");
            c.Check(g.Started && g.Hit && ReferenceEquals(g.Pick, a) && g.Gain[0] >= a.GetStaggerTreshold() - 0.05f && g.Staggering[0],
                $"bash 1: the Draugr you face staggers (bar +{F(g.Gain[0])})");
            c.Check(g.Lost[0] > 0.001f && g.Lost[1] > 0.001f, $"bash 1: both take the bash's damage (health -{F(g.Lost[0])} and -{F(g.Lost[1])})");
            c.Check(g.Push[0] > 0.05f && g.Push[1] > 0.05f, $"bash 1: both are pushed back (push {F(g.Push[0])} and {F(g.Push[1])}; moved {F(g.Moved[0])} m and {F(g.Moved[1])} m in 0.35 s)");
            c.Check(g.Gain[1] <= g.Lost[1] + 1f && !g.Staggering[1], $"bash 1: the other Draugr does not stagger (bar +{F(g.Gain[1])}, landed {F(g.Lost[1])})");

            // Bash 2, 2 s later at the same one (inside its lock): neither staggers.
            SetSkill(p, Skills.SkillType.Blocking, 0f);
            yield return WaitIdle(p);
            yield return WaitCooldown(p);
            PlaceGroup(p, new[] { a, b }, GroupName);
            yield return GroupBash(p, new[] { a, b }, g);
            var gapSeconds = g.Hit ? g.HitTime - firstHit : -1f;
            c.Check(g.Hit && gapSeconds > 0f && gapSeconds < rules.BashStaggerLock && g.Gain[0] <= g.Lost[0] + 1f && g.Gain[1] <= g.Lost[1] + 1f && !g.Staggering[1],
                $"bash 2, {F(gapSeconds)} s later at the same Draugr: neither gets a heavy stagger (A bar +{F(g.Gain[0])}, landed {F(g.Lost[0])}; B bar +{F(g.Gain[1])}, landed {F(g.Lost[1])})");

            // Bash 3: turn to the other one. It staggers.
            SetSkill(p, Skills.SkillType.Blocking, 0f);
            yield return WaitIdle(p);
            yield return WaitCooldown(p);
            PlaceGroup(p, new[] { b, a }, GroupName);
            yield return GroupBash(p, new[] { b, a }, g);
            c.Check(g.Hit && ReferenceEquals(g.Pick, b) && g.Gain[0] >= b.GetStaggerTreshold() - 0.05f && g.Staggering[0],
                $"bash 3, facing the other Draugr: it staggers (bar +{F(g.Gain[0])} of {F(b.GetStaggerTreshold())})");
            ZNetScene.instance.Destroy(a.gameObject);
            ZNetScene.instance.Destroy(b.gameObject);

            // Three Greydwarf at once: one bash staggers one of them, the faced one.
            var greys = new[] { rig.Creature("Greydwarf"), rig.Creature("Greydwarf"), rig.Creature("Greydwarf") };
            if (greys.Any(x => x == null))
            {
                c.Check(false, "could not spawn three Greydwarf");
                c.Report();
                yield break;
            }
            yield return Fixed;
            SetSkill(p, Skills.SkillType.Blocking, 0f);
            yield return WaitIdle(p);
            yield return WaitCooldown(p);
            angles = PlaceGroup(p, greys, GroupName);
            yield return GroupBash(p, greys, g);
            var heavy = 0;
            var reached = 0;
            for (var i = 0; i < greys.Length; i++)
            {
                if (g.Gain[i] >= greys[i].GetStaggerTreshold() - 0.05f)
                {
                    heavy++;
                }
                if (g.Lost[i] > 0.001f)
                {
                    reached++;
                }
            }
            SelfTest.Note(GroupName, $"three Greydwarf (beside at {F(angles[1])} and {F(angles[2])} degrees): hit {g.Hit}, {reached} took damage, bars +{F(g.Gain[0])}, +{F(g.Gain[1])}, +{F(g.Gain[2])} "
                                     + $"of {F(greys[0].GetStaggerTreshold())}, staggering {g.Staggering.Count(x => x)}");
            c.Check(g.Hit && reached >= 2, $"three Greydwarf: the bash hits more than one of them ({reached})");
            c.Check(heavy == 1 && ReferenceEquals(g.Pick, greys[0]) && g.Gain[0] >= greys[0].GetStaggerTreshold() - 0.05f && g.Staggering.Count(x => x) == 1,
                $"three Greydwarf: one bash staggers exactly one of them, the faced one ({heavy} heavy staggers, {g.Staggering.Count(x => x)} staggering)");
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- tower.live ----------

    private static IEnumerator RunLive()
    {
        var c = new Checks(LiveName);
        var rig = new Rig(LiveName);
        try
        {
            var p = rig.P;
            p.SetGodMode(true);
            var defaults = TowerRules.Defaults();
            var took = new Box<float>();
            // The step the in-memory settings below skip (single player tests never write a setting): the settings
            // file's values -> rules. Read only: every setting TESTING T16 names lands in its own rule.
            TowerRules.DebugOwn = null;
            var cfg = TowerRules.Own();
            c.Check(Near(cfg.BlockArmorMultiplier, Plugin.BlockArmorMultiplier.Value) && cfg.BlockForcePercent == Plugin.BlockForcePercent.Value
                    && cfg.CarrySlowPercent == Plugin.CarrySlowPercent.Value && cfg.BraceSlowPercent == Plugin.BraceSlowPercent.Value
                    && cfg.BraceKnockbackResistPercent == Plugin.BraceKnockbackResistPercent.Value && Near(cfg.BashStagger, Plugin.BashStagger.Value)
                    && Near(cfg.BashStaggerLock, Plugin.BashStaggerLock.Value) && Near(cfg.BashCooldown, Plugin.BashCooldown.Value)
                    && Near(cfg.BashAnimationSpeed, Plugin.BashAnimationSpeed.Value) && cfg.BashAnimation == TowerRules.ParseAnimation(Plugin.BashAnimation.Value),
                $"the rules read from the settings carry each setting's own value (settings now: {cfg.Describe()})");
            yield return UseOwn(defaults);
            c.Check(TowerSync.AppliedKey == defaults.Key && ReferenceEquals(TowerRules.InForce, ServerRules.Own),
                "the own-settings path is in use (no test override of the rules in force): default settings");
            var tower = rig.Give(IronTower);
            var draugr = rig.Creature("Draugr");
            var snap = SnapshotOf(IronTower);
            if (tower == null || draugr == null || snap == null)
            {
                c.Check(false, "could not add a ShieldIronTower (or no snapshot) or spawn a Draugr");
                c.Report();
                yield break;
            }
            p.UpdateModifiers();
            var jog0 = p.GetJogSpeedFactor();
            yield return FreshBash(p, tower);
            p.UpdateModifiers();
            c.Check(Near(p.GetJogSpeedFactor(), jog0 - 0.30f, 0.001f) && tower.GetTooltip().Contains("$item_blockarmor: <color=orange>130</color>"),
                "start: the held tower has the default values");

            // TESTING T16, first half: tower shield and bracing settings, changed like a settings edit.
            var a = defaults.With(r =>
            {
                r.BlockArmorMultiplier = 4f;
                r.BlockForcePercent = 50;
                r.CarrySlowPercent = 20;
                r.BraceSlowPercent = 50;
                r.BraceKnockbackResistPercent = 0;
            });
            yield return UseOwn(a, took);
            c.Check(took.Value >= 0.45f && took.Value <= 1.5f, $"the changed settings reach the items {T(took.Value)} after the change (about half a second)");
            c.Check(ReferenceEquals(p.m_leftItem, tower) && tower.m_equipped, "the tower was never re-equipped");
            var tip = tower.GetTooltip();
            p.UpdateModifiers();
            c.Check(tip.Contains($"$item_blockarmor: <color=orange>{snap.BlockPower * 4f}</color>") && Near(tower.m_shared.m_blockPower, snap.BlockPower * 4f),
                $"BlockArmorMultiplier 4: block armor {F(tower.m_shared.m_blockPower)} on the held tower and its tooltip");
            c.Check(Near(tower.m_shared.m_deflectionForce, snap.DeflectionForce * 0.5f) && Near(tower.GetDeflectionForce(), snap.DeflectionForce * 0.5f),
                $"BlockForcePercent 50: block force {F(snap.DeflectionForce)} -> {F(tower.m_shared.m_deflectionForce)}");
            c.Check(Near(p.GetJogSpeedFactor(), jog0 - 0.20f, 0.001f) && tip.Contains(Player.s_equipmentModifierTooltips[0] + ": <color=orange>-20%</color>"),
                $"CarrySlowPercent 20: jog factor {F(p.GetJogSpeedFactor())}, tooltip movement -20%");
            yield return Guard(p, true);
            var se = Brace.Clone;
            c.Check(se != null && Near(se.m_speedModifier, -0.50f) && tip.Contains("movement -50%"), $"BraceSlowPercent 50: braced speed modifier {(se != null ? F(se.m_speedModifier) : "none")}, tooltip 'movement -50%'");
            yield return Ready(p, p.GetMaxStamina());
            p.ApplyPushback(-Flat(p.transform.forward), 50f);
            var pushed = p.m_pushForce.magnitude;
            p.m_pushForce = Vector3.zero;
            c.Check(pushed > 0.1f && !tip.Contains("knockback from the front"),
                $"BraceKnockbackResistPercent 0: a push from the front moves the braced player (push {F(pushed)}), no knockback part in the tooltip's Braced line");
            yield return Guard(p, false);

            // Second half: the bash settings. BashStagger 5, not 20: at Blocking 0 one bash then add 15 to 33 to the bar,
            // below the Draugr's threshold of 50 (the bar never go above it: first run read +50 for x20, whose 60 to
            // 132 no Draugr can show) and far from the default x25 (75 to 165): only a bash that really use 5 fit.
            var b = defaults.With(r =>
            {
                r.BashStagger = 5f;
                r.BashStaggerLock = 3f;
                r.BashCooldown = 4f;
                r.BashAnimationSpeed = 1f;
                r.BashAnimation = BashAnimationKind.Kick;
            });
            yield return UseOwn(b, took);
            c.Check(took.Value >= 0.45f && took.Value <= 1.5f, $"the bash settings reach the items {T(took.Value)} after the change");
            c.Check(ReferenceEquals(p.m_leftItem, tower) && tower.m_equipped, "the tower was still never re-equipped");
            tip = tower.GetTooltip();
            var bash = BashAttack.Current;
            c.Check(tip.Contains("Bash stagger: <color=orange>×5</color>") && tip.Contains("Bash cooldown: <color=orange>4 s</color>")
                    && tip.Contains("$item_blockarmor: <color=orange>130</color>"),
                $"BashStagger 5 and BashCooldown 4 on the tooltip, block armor back to 130: '{OneLine(tip)}'");
            c.Check(bash != null && ReferenceEquals(tower.m_shared.m_attack, bash) && Near(bash.m_staggerMultiplier, 5f) && bash.m_attackAnimation == BashAttack.KickTrigger
                    && BashAttack.Mode == BashAnimationKind.Kick,
                $"the held tower carries the new bash: stagger x{(bash != null ? F(bash.m_staggerMultiplier) : "?")}, animation '{(bash != null ? bash.m_attackAnimation : "")}' (Kick)");
            c.Check(TowerSync.Applied != null && Near(TowerSync.Applied.BashStaggerLock, 3f), "BashStaggerLock 3 is the lock in force");
            SetSkill(p, Skills.SkillType.Blocking, 0f);
            var s = new Swing();
            yield return Press(p, draugr, s, LiveName, null);
            SelfTest.Note(LiveName, Describe("bash with the changed settings", s));
            c.Check(s.Started && s.Trigger == BashAttack.KickTrigger && Near(s.Factor, 1f) && s.AnimSpeed >= 0.99f,
                $"BashAnimation Kick at BashAnimationSpeed 1: the kick plays unslowed, never below its own speed ('{s.Trigger}', animator speed {F(s.AnimSpeed)} at 0.2 s)");
            if (s.Landed)
            {
                CheckHit(c, "BashStagger 5", s, 12f, 5f, 1f, -1f);
            }
            c.Check(s.Landed && !s.Staggered,
                $"the bash with the changed settings lands and, at x5, does not stagger a Draugr in one hit as the default x25 does (threshold {F(draugr.GetStaggerTreshold())}, staggered {s.Staggered})");
            var left = BashWatch.CooldownLeft(p);
            c.Check(s.EndAt > 0f && left > 4f - s.EndAt - 0.3f && left <= 4f, $"BashCooldown 4: {F(left)} s of cooldown left when the swing is over ({T(s.EndAt)} after the press)");
            yield return UseOwn(defaults, took);
            c.Check(tower.GetTooltip().Contains("Bash stagger: <color=orange>×25</color>") && ReferenceEquals(p.m_leftItem, tower), "settings back to the defaults: the held tower follows again");
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- tower.warn ----------

    private static void NoPrefix()
    {
    }

    private static IEnumerator RunWarn()
    {
        var c = new Checks(WarnName);
        var rig = new Rig(WarnName);
        Harmony foreign = null;
        try
        {
            var tap = Tap;
            var defaults = TowerRules.Defaults();
            yield return UseRules(defaults);

            // TESTING T15: Custom triggers that cannot be a bash: one Warning each, ShieldPunch used.
            var refused = new[]
            {
                new KeyValuePair<string, string>("emote_wave", "BashCustomTrigger 'emote_wave' is not a player attack animation; using ShieldPunch."),
                new KeyValuePair<string, string>("mc_no_such_trigger", "BashCustomTrigger 'mc_no_such_trigger' is not a player attack animation; using ShieldPunch."),
                new KeyValuePair<string, string>("staff_rapidfire",
                    "BashCustomTrigger 'staff_rapidfire' is the animation of an attack that is held, aimed or reloaded, which a bash cannot play; using ShieldPunch."),
            };
            foreach (var pair in refused)
            {
                var mark = tap.WarningMark();
                yield return UseRules(WithAnimation(BashAnimationKind.Custom, pair.Key));
                var lines = tap.WarningsSince(mark);
                c.Check(lines.Count(l => l == pair.Value) == 1 && BashAttack.Mode == BashAnimationKind.ShieldPunch,
                    $"Custom '{pair.Key}': the Warning '{pair.Value}' once, ShieldPunch in use (warnings: {string.Join(" / ", lines.ToArray())})");
            }
            var okMark = tap.WarningMark();
            yield return UseRules(WithAnimation(BashAnimationKind.Custom, "throw_bomb"));
            c.Check(tap.WarningsSince(okMark).Count == 0 && BashAttack.Mode == BashAnimationKind.Custom, "Custom 'throw_bomb' (a player attack animation): no Warning, Custom in use");

            // The BashAnimation setting's own reading of a file value (its clamp; nothing is written).
            var names = Plugin.BashAnimation.Description.AcceptableValues as AnimationNameList;
            if (names == null)
            {
                c.Check(false, "the BashAnimation setting has no name list");
            }
            else
            {
                var mark = tap.WarningMark();
                var read = names.Clamp("Kicks") as string;
                var lines = tap.WarningsSince(mark);
                const string kicks = "BashAnimation 'Kicks' is not one of ShieldPunch, OtherPunch, Kick, Custom; ShieldPunch is used.";
                c.Check(read == "ShieldPunch" && lines.Count == 1 && lines[0] == kicks, $"BashAnimation = Kicks: read as {read}, Warning '{(lines.Count > 0 ? lines[0] : "none")}'");
                mark = tap.WarningMark();
                var old = names.Clamp("ShieldUp") as string;
                var lower = names.Clamp("kick") as string;
                c.Check(old == "ShieldPunch" && lower == "Kick" && tap.WarningsSince(mark).Count == 0,
                    $"BashAnimation = ShieldUp is read as {old} and 'kick' as {lower}, both without a Warning");
            }

            // Towers problems are reported; the default list reports nothing (TESTING T20).
            var bad = defaults.With(r => r.Towers = "ShieldIronTower:12, SwordIron:5, MC_TowerTestMissing, ShieldIronTower:3, ShieldBanded:7");
            var badMark = tap.WarningMark();
            yield return UseRules(bad);
            var badLines = tap.WarningsSince(badMark);
            c.Check(badLines.Count(l => l.StartsWith("The Towers setting has problems: ", StringComparison.Ordinal) && l.Contains("ShieldIronTower is listed more than once")) == 1
                    && badLines.Count(l => l == "The Towers setting names items this game does not have (MISSING), ignored: MC_TowerTestMissing.") == 1
                    && badLines.Count(l => l.StartsWith("The Towers setting names items that are not shields, ignored: SwordIron.", StringComparison.Ordinal)) == 1
                    && badLines.Count(l => l.StartsWith("The Towers setting names shields that can parry: ShieldBanded. As tower shields they can no longer parry.", StringComparison.Ordinal)) == 1,
                $"a Towers list with a duplicate, a sword, an unknown name and a parrying shield: one Warning for each ({string.Join(" / ", badLines.ToArray())})");
            var cleanMark = tap.WarningMark();
            yield return UseRules(defaults);
            var cleanLines = tap.WarningsSince(cleanMark);
            c.Check(TowerSync.AppliedKey == defaults.Key && !cleanLines.Any(l => l.Contains("Towers")),
                $"the default settings applied again: no Towers warning ({cleanLines.Count} warnings: {string.Join(" / ", cleanLines.ToArray())})");

            // TESTING C07: another mod with a prefix on Humanoid.BlockAttack is named in one Warning.
            var block = AccessTools.DeclaredMethod(typeof(Humanoid), nameof(Humanoid.BlockAttack), new[] { typeof(HitData), typeof(Character) });
            const string id = "Test.ShieldBash.SelfTest";
            if (block == null)
            {
                c.Check(false, "Humanoid.BlockAttack not found");
            }
            else
            {
                foreign = new Harmony(id);
                foreign.Patch(block, prefix: new HarmonyMethod(typeof(SelfTests), nameof(NoPrefix)));
                var mark = tap.WarningMark();
                TowerGuard.Reset();
                TowerGuard.WarnOnce();
                TowerGuard.WarnOnce();
                var lines = tap.WarningsSince(mark).Where(l => l.StartsWith("Other mods that change shields, blocking or attacks are installed: ", StringComparison.Ordinal)).ToList();
                foreign.UnpatchSelf();
                foreign = null;
                c.Check(lines.Count == 1 && lines[0].Contains(id + " (changes blocking)") && lines[0].Contains("README"),
                    $"a foreign patch on blocking: one Warning that names it ({lines.Count}: {(lines.Count > 0 ? lines[0] : "none")})");
            }
            c.Report();
        }
        finally
        {
            if (foreign != null)
            {
                foreign.UnpatchSelf();
            }
            rig.Done();
        }
    }

    // ---------- tower.creature ----------

    private static IEnumerator RunCreature()
    {
        var c = new Checks(CreatureName);
        var rig = new Rig(CreatureName);
        BaseAI live = null;
        try
        {
            var p = rig.P;
            p.SetGodMode(true);
            var rules = TowerRules.Defaults();
            yield return UseRules(rules);

            // TESTING T17: a FallenWarrior whose equipment set has a tower shield. Its sets are picked from a seed: me
            // try seeds until one hold the tower shield.
            var prefab = ZNetScene.instance.GetPrefab("FallenWarrior");
            var template = prefab != null ? prefab.GetComponent<Humanoid>() : null;
            if (template == null)
            {
                c.Check(false, "no FallenWarrior prefab");
                c.Report();
                yield break;
            }
            SelfTest.Note(CreatureName, $"FallenWarrior equipment sets: {string.Join(" | ", template.m_randomSets.Select(set => string.Join("+", set.m_items.Select(i => i != null ? i.name : "?").ToArray())).ToArray())}");
            Humanoid warrior = null;
            ItemDrop.ItemData shield = null;
            var fwd = Flat(p.transform.forward);
            for (var seed = 1; seed <= 40 && warrior == null; seed++)
            {
                var obj = Object.Instantiate(prefab, p.transform.position + fwd * 6f, Quaternion.LookRotation(-fwd));
                rig.Track(obj);
                var ai = obj.GetComponent<BaseAI>();
                if (ai != null)
                {
                    ai.enabled = false;
                }
                var h = obj.GetComponent<Humanoid>();
                h.m_seed = seed; // Humanoid.Start (next frame) draws its set from it
                yield return Frames(3);
                var left = h.m_leftItem;
                if (left != null && left.m_dropPrefab != null && left.m_dropPrefab.name.Contains("Tower"))
                {
                    warrior = h;
                    shield = left;
                    live = ai;
                }
                else
                {
                    ZNetScene.instance.Destroy(obj);
                }
            }
            c.Check(warrior != null, "a FallenWarrior with a tower shield was found (seeds 1 to 40)");
            if (warrior != null)
            {
                var name = shield.m_dropPrefab.name;
                var own = SharedOf(name);
                c.Check(shield.m_shared.m_itemType == ItemType.Shield && !shield.IsWeapon() && TowerCatalog.SnapshotOf(shield) == null && TowerCatalog.TowerOf(shield) == null
                        && own != null && Near(shield.m_shared.m_blockPower, own.m_blockPower) && own.m_itemType == ItemType.Shield,
                    $"its {name} is a normal one-handed shield, never touched by the mod (type {shield.m_shared.m_itemType}, block armor {F(shield.m_shared.m_blockPower)})");
                warrior.EquipBestWeapon(p, null, null, null);
                var weapon = warrior.m_rightItem;
                c.Check(weapon != null && weapon.IsWeapon() && ReferenceEquals(warrior.m_leftItem, shield),
                    $"it holds its weapon and its shield together (right {Name(weapon)}, left {Name(warrior.m_leftItem)})");
                // It fights: AI on, next to the player, until its first attack.
                PlaceInFront(p, warrior, 1f);
                if (live != null)
                {
                    live.enabled = true;
                }
                Attack seen = null;
                var until = Time.time + 14f;
                while (Time.time < until && seen == null)
                {
                    yield return null;
                    if (warrior == null)
                    {
                        break;
                    }
                    seen = warrior.m_currentAttack;
                }
                var attackWeapon = seen != null ? seen.GetWeapon() : null;
                var shieldHeld = warrior != null && ReferenceEquals(warrior.m_leftItem, shield);
                if (live != null)
                {
                    live.enabled = false;
                }
                c.Check(seen != null && attackWeapon != null && !ReferenceEquals(attackWeapon, shield) && attackWeapon.IsWeapon() && shieldHeld,
                    $"it fights as in the normal game: it attacks with its {Name(attackWeapon)} ('{(seen != null ? seen.m_attackAnimation : "no attack in 14 s")}'), the shield stays in its other hand ({shieldHeld})");
                if (warrior != null)
                {
                    ZNetScene.instance.Destroy(warrior.gameObject);
                }
                live = null;
            }

            // TESTING C10 (this mod's side): a bash is a melee hit for the kill statistics, and bashes kill.
            var tower = rig.Give(IronTower);
            var greyling = rig.Creature("Greyling");
            if (tower == null || greyling == null)
            {
                c.Check(false, "could not add a ShieldIronTower or spawn a Greyling");
                c.Report();
                yield break;
            }
            yield return FreshBash(p, tower);
            // A Greyling nobody hit yet: its "killed with" note is not set. Press must not refill its health here:
            // Character.SetHealth(max) write the note to "mixed" (first run read MixedAndTotal after one bash), and
            // vanilla RPC_Damage then never change it.
            var zdo = greyling.m_nview != null ? greyling.m_nview.GetZDO() : null;
            var kindBefore = zdo != null ? (KillModifiers)zdo.GetInt(ZDOVars.s_modifiers, (int)KillModifiers.CountNone) : KillModifiers.MixedAndTotal;
            c.Check(kindBefore == KillModifiers.CountNone, $"a newly spawned Greyling has no kill kind noted yet ({kindBefore})");
            var s = new Swing();
            yield return Press(p, greyling, s, CreatureName, null, refill: false);
            zdo = greyling != null && greyling.m_nview != null ? greyling.m_nview.GetZDO() : null;
            var kind = zdo != null ? (KillModifiers)zdo.GetInt(ZDOVars.s_modifiers, (int)KillModifiers.CountNone) : KillModifiers.CountNone;
            c.Check(s.Landed && kind == KillModifiers.Melee, $"a bash on a Greyling marks it for a melee kill (hit landed {s.Landed}, kill kind {kind})");
            // The killing bash.
            yield return WaitIdle(p);
            yield return WaitCooldown(p);
            if (greyling != null)
            {
                FaceClearLane(p, Reach(p, greyling), LaneFan, CreatureName, greyling);
                PlaceInFront(p, greyling);
                greyling.SetHealth(1f);
                yield return Fixed;
                var events = BashWatch.DebugHitEvents;
                p.StartAttack(null, false);
                yield return Until(() => BashWatch.DebugHitEvents > events, 3f);
                yield return Frames(3);
                c.Check(greyling == null || greyling.IsDead() || greyling.GetHealth() <= 0f, "a bash kills a Greyling at 1 health (bashes alone can kill)");
            }
            yield return WaitIdle(p);
            c.Report();
        }
        finally
        {
            if (live != null)
            {
                live.enabled = false;
            }
            rig.Done();
        }
    }

    // ---------- tower.switch ----------

    private static bool Patched(System.Reflection.MethodBase method)
    {
        var info = method != null ? Harmony.GetPatchInfo(method) : null;
        return info != null && info.Owners.Contains(ModInfo.Guid);
    }

    private static IEnumerator RunSwitch()
    {
        var c = new Checks(SwitchName);
        var rig = new Rig(SwitchName);
        try
        {
            var p = rig.P;
            p.SetGodMode(true);
            var tap = Tap;
            var defaults = TowerRules.Defaults();
            var tower = rig.Give(IronTower);
            var sword = rig.Give("SwordIron");
            var troll = rig.Creature("Troll");
            var snap = SnapshotOf(IronTower);
            var prefab = SharedOf(IronTower);
            if (tower == null || sword == null || troll == null)
            {
                c.Check(false, "could not add ShieldIronTower and SwordIron or spawn a Troll");
                c.Report();
                yield break;
            }

            // (1) The held tower leave the Towers list in the middle of a slowed bash (a settings edit or a server
            // push): the bash is cancelled before its hit, the tower is a normal shield in place.
            var slow = defaults.With(r => r.BashAnimationSpeed = 0.3f);
            var dropped = slow.With(r => r.Towers = "");
            yield return UseRules(slow);
            snap = SnapshotOf(IronTower);
            yield return FreshBash(p, tower);
            yield return WaitIdle(p);
            yield return WaitCooldown(p);
            FaceClearLane(p, Reach(p, troll), LaneFan, SwitchName, troll);
            PlaceInFront(p, troll);
            troll.SetHealth(troll.GetMaxHealth());
            yield return Fixed;
            var health = troll.GetHealth();
            var events = BashWatch.DebugHitEvents;
            var mark = tap.WarningMark();
            var t0 = Time.time;
            var started = p.StartAttack(null, false);
            yield return Until(() => p.InAttack() && Time.time - t0 >= 0.15f, 0.6f);
            var slowedSpeed = p.m_animator.speed;
            var raw = BashSpeed.DebugRaw;
            ServerRules.TestRules = dropped;
            yield return Until(() => TowerSync.AppliedKey == dropped.Key, 2f);
            var applied = TowerSync.AppliedKey == dropped.Key;
            var beforeHit = BashWatch.DebugHitEvents == events;
            var stillInClip = p.InAttack();
            var speedNow = p.m_animator.speed;
            c.Check(started && applied && beforeHit, $"setup: the Towers list lost the tower {F(Time.time - t0)} s after the press, before the hit ({beforeHit})");
            c.Check(p.m_currentAttack == null && BashWatch.Running == null, "the running bash is cancelled: no current attack");
            var droppedTip = tower.GetTooltip();
            c.Check(snap != null && tower.m_shared.m_itemType == ItemType.Shield && Near(tower.m_shared.m_blockPower, snap.BlockPower) && ReferenceEquals(p.m_leftItem, tower)
                    && !droppedTip.Contains("$item_twohanded") && droppedTip.Contains($"$item_blockarmor: <color=orange>{snap.BlockPower}</color>"),
                $"the held tower is a normal one-handed shield in place (type {tower.m_shared.m_itemType}, block armor {F(tower.m_shared.m_blockPower)}; no Two-handed line on its tooltip)");
            c.Check(!p.GetSEMan().HaveStatusEffect(Brace.Hash) && Brace.Clone == null, "the Braced effect is gone");
            c.Check(Near(slowedSpeed, raw * 0.3f, 0.02f) && (!stillInClip || Near(speedNow, raw, 0.02f)),
                $"the swing's speed is back to the clip's own at once ({F(slowedSpeed)} slowed, {F(speedNow)} now, clip's own {F(raw)}, still in the clip {stillInClip})");
            yield return Seconds(2f);
            var warnings = tap.WarningsSince(mark);
            c.Check(BashWatch.DebugHitEvents == events && Near(troll.GetHealth(), health) && Near(p.m_animator.speed, 1f, 0.01f),
                $"2 s later: no bash hit (Troll health {F(health)} -> {F(troll.GetHealth())}), animator speed {F(p.m_animator.speed)}");
            c.Check(!warnings.Any(l => l.Contains("; using ")), $"no fallback Warning from the cancelled bash ({string.Join(" / ", warnings.ToArray())})");
            yield return UseRules(defaults);
            yield return WaitIdle(p);

            // (2) Real off and on, braced (TESTING T18): what the framework do when the mod is unticked.
            var chest = rig.Chest(out var chestName);
            var inChest = chest != null ? chest.GetInventory().AddItem(IronTower, 1, 1, 0, 0L, "", false) : null;
            var ground = rig.Ground(IronTower);
            var startAttack = AccessTools.DeclaredMethod(typeof(Humanoid), nameof(Humanoid.StartAttack), new[] { typeof(Character), typeof(bool) });
            var rpcDamage = AccessTools.DeclaredMethod(typeof(Character), nameof(Character.RPC_Damage));
            rig.EmptyHands();
            p.UpdateModifiers();
            var jog0 = p.GetJogSpeedFactor();
            var speed0 = SpeedMods(p);
            p.EquipItem(tower);
            yield return Guard(p, true);
            c.Check(inChest != null && ground != null && IsTower(inChest.m_shared, snap, defaults) && IsTower(ground.m_itemData.m_shared, snap, defaults)
                    && Brace.Clone != null && !Brace.Clone.m_hidden && Patched(startAttack) && Patched(rpcDamage),
                $"on: braced with the tower, copies in a chest ({chestName}) and on the ground are towers, the mod's patches are applied");
            var off = Plugin.DebugSwitch(false);
            c.Check(off && Plugin.DebugOff, "the mod is switched off (OnDeactivated, then its patches removed)");
            c.Check(!Patched(startAttack) && !Patched(rpcDamage), "off: the mod's patches are really gone (Humanoid.StartAttack, Character.RPC_Damage)");
            var tip = tower.GetTooltip();
            c.Check(ReferenceEquals(p.m_leftItem, tower) && IsVanilla(tower.m_shared, snap) && tip.Contains("\n$item_onehanded") && !tip.Contains("$item_twohanded")
                    && !tip.Contains("Cannot parry") && tip.Contains($"$item_blockarmor: <color=orange>{snap.BlockPower}</color>"),
                $"off: the held tower is a one-handed shield in place, normal block armor on its tooltip ('{OneLine(tip)}')");
            p.UpdateModifiers();
            c.Check(!p.GetSEMan().HaveStatusEffect(Brace.Hash) && Near(SpeedMods(p), speed0, 0.001f) && Near(p.GetJogSpeedFactor(), jog0 + snap.MovementModifier, 0.001f),
                $"off: the Braced effect and its slowdown are gone (jog factor {F(p.GetJogSpeedFactor())}: the normal shield's own {F(snap.MovementModifier)})");
            c.Check(IsVanilla(prefab, snap) && IsVanilla(inChest.m_shared, snap) && IsVanilla(ground.m_itemData.m_shared, snap), "off: the prefab and the copies in the chest and on the ground are normal too");
            yield return Until(() => !p.m_internalBlockingState, 2f);
            yield return WaitIdle(p);
            var punched = p.StartAttack(null, false);
            var punch = punched ? p.m_currentAttack : null;
            c.Check(punched && punch != null && p.m_unarmedWeapon != null && ReferenceEquals(punch.GetWeapon(), p.m_unarmedWeapon.m_itemData),
                $"off: attack with only the tower shield held punches (weapon {(punch != null ? Name(punch.GetWeapon()) : "none")})");
            yield return WaitIdle(p);
            var added = p.EquipItem(sword);
            c.Check(added && ReferenceEquals(p.m_rightItem, sword) && ReferenceEquals(p.m_leftItem, tower), $"off: a sword can be added next to it ({Hands(p)})");
            // On again while holding both. Own settings = the defaults for this, whatever the config file says.
            TowerRules.DebugOwn = defaults;
            ServerRules.OwnChanged();
            var on = Plugin.DebugSwitch(true);
            yield return Fixed;
            yield return Fixed;
            c.Check(on && !Plugin.DebugOff && Patched(startAttack) && Patched(rpcDamage), "the mod is switched on again (patches applied, OnActivated)");
            c.Check(p.m_rightItem == null && !sword.m_equipped && ReferenceEquals(p.m_leftItem, tower) && IsTower(tower.m_shared, snap, defaults) && tower.IsTwoHanded(),
                $"on while holding a sword and a tower: the sword is put away, the tower is two-handed again ({Hands(p)})");
            c.Check(IsTower(prefab, snap, defaults) && IsTower(inChest.m_shared, snap, defaults) && IsTower(ground.m_itemData.m_shared, snap, defaults)
                    && p.GetSEMan().HaveStatusEffect(Brace.Hash),
                "on: prefab, chest and ground copies are towers again, the Braced effect is back on the bearer");

            // (3) Off in the middle of a Kick bash, before its hit (TESTING T18b). First a plain punch with empty hands:
            // its own top speed, to compare the punch after the cancelled kick with.
            var top = new Box<float>();
            var slowed = new Box<bool>();
            yield return WaitIdle(p);
            rig.EmptyHands();
            yield return Fixed;
            yield return WeaponSwing(p, top, slowed);
            var punchSpeed = top.Value;
            yield return UseRules(WithAnimation(BashAnimationKind.Kick));
            yield return FreshBash(p, tower);
            yield return WaitIdle(p);
            yield return WaitCooldown(p);
            FaceClearLane(p, Reach(p, troll), LaneFan, SwitchName, troll);
            PlaceInFront(p, troll);
            troll.SetHealth(troll.GetMaxHealth());
            yield return Fixed;
            health = troll.GetHealth();
            events = BashWatch.DebugHitEvents;
            t0 = Time.time;
            started = p.StartAttack(null, false);
            var trigger = started && p.m_currentAttack != null ? p.m_currentAttack.m_attackAnimation : "";
            yield return Until(() => p.InAttack() && Time.time - t0 >= 0.2f, 0.7f);
            slowedSpeed = p.m_animator.speed;
            raw = BashSpeed.DebugRaw;
            beforeHit = BashWatch.DebugHitEvents == events;
            off = Plugin.DebugSwitch(false);
            speedNow = p.m_animator.speed;
            c.Check(started && trigger == BashAttack.KickTrigger && beforeHit && off, $"setup: switched off {F(Time.time - t0)} s into a Kick bash, before its hit ({beforeHit})");
            c.Check(p.m_currentAttack == null && Near(slowedSpeed, raw * 0.6f, 0.02f) && Near(speedNow, raw, 0.02f),
                $"the kick is cancelled and the rest of it plays at its own speed at once ({F(slowedSpeed)} slowed -> {F(speedNow)}, clip's own {F(raw)})");
            yield return Seconds(2f);
            c.Check(BashWatch.DebugHitEvents == events && Near(troll.GetHealth(), health) && Near(p.m_animator.speed, 1f, 0.01f),
                $"2 s later: no hit, no damage (Troll health {F(health)} -> {F(troll.GetHealth())}), animator speed {F(p.m_animator.speed)}");
            // The next attack (a punch: the mod is off) play at its own speed.
            yield return WaitIdle(p);
            yield return WeaponSwing(p, top, slowed);
            c.Check(punchSpeed > 0f && Near(top.Value, punchSpeed, 0.05f) && !slowed.Value,
                $"the next attack (a punch) plays at its normal speed (top animator speed {F(top.Value)}, {F(punchSpeed)} for a punch before the bash)");
            TowerRules.DebugOwn = defaults;
            ServerRules.OwnChanged();
            on = Plugin.DebugSwitch(true);
            yield return Fixed;
            c.Check(on && IsTower(tower.m_shared, snap, defaults), "switched on again: the tower is a tower again");
            c.Report();
        }
        finally
        {
            if (Plugin.DebugOff)
            {
                Plugin.DebugSwitch(true);
            }
            rig.Done();
        }
    }

    // ---------- tower.press, tower.bug.keptpress ----------

    // One round of TESTING T37: bash, one press of the attack button once the swing is over, one second of waiting,
    // then the tower leave the hand (unequipped, or a sword equipped). What came of the kept press.
    private sealed class KeptPress
    {
        internal bool Started;        // the first bash started
        internal bool Kept;           // 1 s after the press: cooldown still run, press timer still above 0, no new bash
        internal float Left;          // cooldown left then
        internal float Timer;         // press timer then
        internal bool Leaked;         // an attack started by itself within 0.7 s of the tower leaving the hand
        internal string LeakedWith = "";
        internal bool Control;        // a fresh press after that do start an attack
    }

    private static IEnumerator KeptPressRound(Rig rig, ItemDrop.ItemData tower, ItemDrop.ItemData sword, Character draugr, bool swap, string test, KeptPress k)
    {
        var p = rig.P;
        yield return WaitIdle(p);
        if (!p.IsItemEquiped(tower))
        {
            p.EquipItem(tower);
            yield return Fixed;
        }
        var s = new Swing();
        yield return Press(p, draugr, s, test, null);
        k.Started = s.Started;
        var starts = BashWatch.DebugStarts.Count;
        p.m_queuedAttackTimer = BashWatch.QueuedPress; // one press of the attack button, the swing is over
        yield return Seconds(1f);
        k.Left = BashWatch.CooldownLeft(p);
        k.Timer = p.m_queuedAttackTimer;
        k.Kept = s.Started && k.Left > 1f && k.Timer > 0.1f && BashWatch.DebugStarts.Count == starts && !p.InAttack();
        if (swap)
        {
            p.EquipItem(sword);
        }
        else
        {
            p.UnequipItem(tower);
        }
        Attack leaked = null;
        var until = Time.time + 0.7f;
        while (Time.time < until && leaked == null)
        {
            yield return null;
            leaked = p.m_currentAttack;
        }
        k.Leaked = leaked != null;
        k.LeakedWith = leaked == null ? "none started" : $"an attack with {Name(leaked.GetWeapon())} ('{leaked.m_attackAnimation}') started by itself";
        // Control: a fresh press do start an attack now (so "none started" mean something).
        yield return WaitIdle(p);
        var last = p.m_currentAttack;
        p.m_queuedAttackTimer = BashWatch.QueuedPress;
        yield return Until(() => p.m_currentAttack != null && !ReferenceEquals(p.m_currentAttack, last), 0.6f);
        k.Control = p.m_currentAttack != null && !ReferenceEquals(p.m_currentAttack, last);
        yield return WaitIdle(p);
        p.m_queuedAttackTimer = 0f;
        rig.EmptyHands();
        yield return WaitCooldown(p);
    }

    // The set-up and the control of TESTING T37 (what makes the bug test below mean something): with BashCooldown 6
    // the press is still kept a second later, and a fresh press starts an attack once the tower left the hand. The
    // expectation itself (nothing swings by itself) is tower.bug.keptpress: it fails on this build, a real bug.
    private static IEnumerator RunPress() => RunKeptPress(PressName, bug: false);

    private static IEnumerator RunPressBug() => RunKeptPress(PressBugName, bug: true);

    private static IEnumerator RunKeptPress(string test, bool bug)
    {
        var c = new Checks(test);
        var rig = new Rig(test);
        try
        {
            var p = rig.P;
            // Long cooldown: the kept press is clearly older than vanilla's 0.5 s when the tower leave the hand.
            var rules = TowerRules.Defaults().With(r => r.BashCooldown = 6f);
            yield return UseRules(rules);
            var tower = rig.Give(IronTower);
            var sword = rig.Give("SwordIron");
            var draugr = rig.Creature("Draugr");
            if (tower == null || sword == null || draugr == null)
            {
                c.Check(false, "could not add ShieldIronTower and SwordIron or spawn a Draugr");
                c.Report();
                yield break;
            }
            yield return FreshBash(p, tower);
            foreach (var swap in new[] { false, true })
            {
                var what = swap ? "a sword is equipped" : "the tower is unequipped";
                var k = new KeptPress();
                yield return KeptPressRound(rig, tower, sword, draugr, swap, test, k);
                if (bug)
                {
                    c.Check(k.Kept && !k.Leaked,
                        $"a press made 1 s ago for the bash must not start another attack once {what}: {k.LeakedWith}"
                        + (k.Kept ? "" : $" (set-up failed: the press was not kept: {F(k.Left)} s of cooldown left, press timer {F(k.Timer)})"));
                }
                else
                {
                    c.Check(k.Kept,
                        $"setup ({what}): 1 s after the press the bash still waits for its cooldown and the press is still kept for it ({F(k.Left)} s left, press timer {F(k.Timer)})");
                    c.Check(k.Control, $"control ({what}): a new press of the attack button starts an attack");
                }
            }
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- tower.mods.crossbow ----------

    private static IEnumerator RunCrossbow()
    {
        var c = new Checks(CrossbowName);
        var rig = new Rig(CrossbowName);
        ItemDrop.ItemData crossbow = null;
        try
        {
            var p = rig.P;
            var other = FeatureRegistry.Find(CrossbowGuid);
            if (other == null || !other.Value.IsActive)
            {
                c.Check(false, $"Crossbow Stays Loaded ({CrossbowGuid}) is not active in this game ({(other == null ? "not installed" : other.Value.State)}): TESTING C02 could not be checked");
                c.Report();
                yield break;
            }
            yield return UseRules(TowerRules.Defaults());
            crossbow = rig.Give("CrossbowArbalest");
            var tower = rig.Give(IronTower);
            if (crossbow == null || tower == null)
            {
                c.Check(false, "could not add CrossbowArbalest and ShieldIronTower");
                c.Report();
                yield break;
            }
            var held = p.EquipItem(crossbow);
            yield return Fixed;
            p.SetWeaponLoaded(crossbow); // the end of a reload
            c.Check(held && p.IsWeaponLoaded() && ReferenceEquals(p.m_weaponLoaded, crossbow), "the crossbow is in hand and loaded");
            var equipped = p.EquipItem(tower);
            c.Check(equipped && Only(p, tower, left: true) && !crossbow.m_equipped && !p.IsWeaponLoaded(), $"equipping the tower puts the crossbow away ({Hands(p)})");
            var again = p.EquipItem(crossbow);
            yield return Until(() => p.IsWeaponLoaded(), 0.5f);
            c.Check(again && !tower.m_equipped && p.IsWeaponLoaded() && ReferenceEquals(p.m_weaponLoaded, crossbow),
                $"the crossbow equipped again is still loaded, at once (loaded {p.IsWeaponLoaded()})");
            c.Report();
        }
        finally
        {
            if (rig.P != null && crossbow != null && ReferenceEquals(rig.P.m_weaponLoaded, crossbow))
            {
                rig.P.SetWeaponLoaded(null);
            }
            rig.Done();
        }
    }

    // ---------- tower.log ----------

    // TESTING T20: look back at everything this mod logged since it be turned on (all the tests before, and the
    // play around them): no error line, and no Towers warning while the default list be in force.
    private static IEnumerator RunLog()
    {
        yield return null;
        var bad = Tap.Bad();
        if (bad.Count == 0)
        {
            SelfTest.Pass(LogName, "no error from Tower Shield Wall and no Towers warning with the default settings since the mod was turned on");
        }
        else
        {
            SelfTest.Fail(LogName, $"{bad.Count} bad log line(s) from Tower Shield Wall: {string.Join(" || ", bad.Take(8).ToArray())}");
        }
    }
}
#endif
