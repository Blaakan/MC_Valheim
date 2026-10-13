#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Bootstrap;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Combat.ShieldsTowerWallMod;

// Debug build only. Self tests that do the real thing where an older test took a shortcut (helpers: SelfTests.cs,
// SelfTests.Play.cs, SelfTests.Bash.cs). Rules forced in memory only, Rig put everything back.
//   tower.fire        TESTING T11: a real campfire (fire_pit) in front of the braced player: its own damage area hit,
//                     the hit is not made blockable, no "Blocked" text, no block stamina, the player burns
//   tower.foe         TESTING T21: a Draugr with its AI on, attack button held, default rules: first bash that lands
//                     stagger it, bashes inside the 8 s lock do not, and it attacks between the staggers
//   tower.arm         TESTING T12, T15: which limb go furthest forward in the swing of each option (hand bones of the
//                     game's own equipment code, feet of the animator): ShieldPunch = shield arm (left), OtherPunch =
//                     right arm, Kick = a foot; Custom throw_bomb, spear_poke, mace_secondary swung for real: hit, or
//                     fell back with its Warning
//   tower.input.bash  TESTING T12: a real press of the left mouse button (probe's input driver) start the bash
//   tower.ext.jotunn  TESTING T41: Jotunn and a mod built on it installed: no MISSING warning under the default list
//                     since the game started (log tap on from BindConfig), tower shields work in the world
// Staged and other-mod tests: SelfTests.Staged.cs, SelfTests.Mods.cs.
internal static partial class SelfTests
{
    private const string FireName = "tower.fire";
    private const string FoeName = "tower.foe";
    private const string ArmName = "tower.arm";
    private const string InputBashName = "tower.input.bash";
    private const string JotunnName = "tower.ext.jotunn";

    private const string CampfirePrefab = "fire_pit";
    private const string JotunnGuid = "com.jotunn.jotunn";
    private const string LeftMousePath = "<Mouse>/leftButton"; // ZInput.MouseButtonToPath(MouseButton.Left)

    // Every test of the three new files that run in the normal list, in run order.
    private static readonly KeyValuePair<string, Func<IEnumerator>>[] AutoTests =
    {
        new KeyValuePair<string, Func<IEnumerator>>(FireName, RunFire),
        new KeyValuePair<string, Func<IEnumerator>>(FoeName, RunFoe),
        new KeyValuePair<string, Func<IEnumerator>>(ArmName, RunArm),
        new KeyValuePair<string, Func<IEnumerator>>(InputBashName, RunInputBash),
        new KeyValuePair<string, Func<IEnumerator>>(JotunnName, RunJotunn),
        new KeyValuePair<string, Func<IEnumerator>>(DualName, RunDual),
        new KeyValuePair<string, Func<IEnumerator>>(MovesetName, RunMoveset),
        new KeyValuePair<string, Func<IEnumerator>>(SneakName, RunSneak),
        new KeyValuePair<string, Func<IEnumerator>>(KillsName, RunKills),
        new KeyValuePair<string, Func<IEnumerator>>(SpyglassName, RunSpyglass),
        new KeyValuePair<string, Func<IEnumerator>>(InstrumentName, RunInstrument),
        new KeyValuePair<string, Func<IEnumerator>>(InputSpyglassName, RunInputSpyglass),
        new KeyValuePair<string, Func<IEnumerator>>(InputInstrumentName, RunInputInstrument),
        new KeyValuePair<string, Func<IEnumerator>>(ForgeName, RunForge),
        new KeyValuePair<string, Func<IEnumerator>>(SortName, RunSort),
        new KeyValuePair<string, Func<IEnumerator>>(KindsName, RunKinds),
        new KeyValuePair<string, Func<IEnumerator>>(HarpoonName, RunHarpoon),
    };

    // OnActivated (after the older lists). tower.log registered again so it stay last and look back at these too.
    private static void RegisterAuto()
    {
        foreach (var test in AutoTests)
        {
            SelfTest.Register(test.Key, test.Value);
        }
        SelfTest.Register(LogName, RunLog);
        RegisterAutoMultiplayer();
    }

    private static void UnregisterAuto()
    {
        foreach (var test in AutoTests)
        {
            SelfTest.Unregister(test.Key);
        }
        UnregisterAutoMultiplayer();
    }

    // ---------- tower.fire ----------

    private static IEnumerator RunFire()
    {
        var c = new Checks(FireName);
        var rig = new Rig(FireName);
        try
        {
            var p = rig.P;
            yield return UseRules(TowerRules.Defaults());
            var tower = rig.Give(IronTower);
            var prefab = ZNetScene.instance.GetPrefab(CampfirePrefab);
            if (tower == null || prefab == null || prefab.GetComponent<Fireplace>() == null)
            {
                c.Check(false, $"could not add ShieldIronTower or find the campfire prefab ({CampfirePrefab})");
                c.Report();
                yield break;
            }
            p.EquipItem(tower);
            yield return Fixed;
            rig.TakeStaminaRate(); // a block would cost stamina now
            yield return null;
            // Rate still 0 = a block cost nothing and "no block stamina spent" below would say nothing.
            c.Check(Game.m_staminaRate > 0f, $"stamina use is on, so a block would cost stamina (world stamina rate {F(Game.m_staminaRate)})");
            var front = Flat(p.transform.forward);
            Face(p, front);
            yield return Guard(p, true);
            c.Check(p.IsBlocking() && Brace.Clone != null && !Brace.Clone.m_hidden, "braced with the tower shield before the campfire is there");

            // Campfire in front, nearer at each try, until its own damage area reach the player. In front: the hit come
            // from the front, the side a braced tower blocks enemy attacks from.
            var got = false;
            var record = default(Brace.DebugHit);
            var blockedText = false;
            var spent = 0f;
            var burning = false;
            var facing = 0f;
            var at = 0f;
            var lit = false;
            foreach (var distance in new[] { 0.9f, 0.6f, 0.35f })
            {
                var pos = p.transform.position + front * distance;
                pos.y = ZoneSystem.instance.GetGroundHeight(pos);
                var fire = Object.Instantiate(prefab, pos, Quaternion.identity);
                rig.Track(fire);
                var fireplace = fire.GetComponent<Fireplace>();
                var zdo = fireplace != null && fireplace.m_nview != null ? fireplace.m_nview.GetZDO() : null;
                if (zdo != null && zdo.GetFloat(ZDOVars.s_fuel) < 2f)
                {
                    zdo.Set(ZDOVars.s_fuel, 2f); // wood on it, as a player feeds it
                }
                yield return Ready(p, p.GetMaxStamina());
                var stamina = p.GetStamina();
                var texts = BlockedTexts();
                var count = Brace.DebugLast.Count;
                var until = Time.time + 6f;
                while (Time.time < until && !got)
                {
                    yield return null;
                    lit |= fireplace != null && fireplace.IsBurning();
                    if (Brace.DebugLast.Count == count)
                    {
                        continue;
                    }
                    count = Brace.DebugLast.Count;
                    if (Brace.DebugLast.Attacker != null)
                    {
                        continue; // a creature that wandered in: not the fire
                    }
                    record = Brace.DebugLast;
                    facing = Vector3.Dot(Flat(p.transform.forward), front);
                    yield return null;
                    yield return null;
                    got = true;
                    at = distance;
                    spent = stamina - p.GetStamina();
                    burning = p.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectBurning);
                    blockedText = BlockedTexts().Any(t => !texts.Contains(t));
                }
                ZNetScene.instance.Destroy(fire);
                yield return null;
                if (got)
                {
                    break;
                }
            }
            c.Check(got, $"the campfire's own damage reached the braced player (tried 0.9, 0.6 and 0.35 m in front of it; campfire burning {lit})");
            if (got)
            {
                SelfTest.Note(FireName, $"campfire {F(at)} m in front: hit type {record.Type}, blockable {record.WasBlockable}, braced {record.Braced}, block holds {record.Holds}");
                c.Check(record.Braced && facing > 0.9f, $"the player was braced and faced the campfire when its hit came (braced {record.Braced})");
                c.Check(!record.WasBlockable && !record.MadeBlockable, $"the campfire's hit is not blockable and stays so (made blockable {record.MadeBlockable})");
                c.Check(!blockedText, "no 'Blocked' text came up");
                c.Check(spent <= 0.01f, $"no block stamina was spent ({F(spent)})");
                c.Check(burning, "the player burns");
            }
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- tower.foe ----------

    // A Draugr that fights (AI on) with a melee weapon, 4 m ahead. The prefab pick its weapons at random: one that
    // carry something to shoot with is sent away and another one made.
    private static IEnumerator LiveDraugr(Rig rig, Box<Humanoid> result)
    {
        result.Value = null;
        var prefab = ZNetScene.instance.GetPrefab("Draugr");
        for (var attempt = 0; attempt < 6 && result.Value == null && prefab != null; attempt++)
        {
            var p = rig.P;
            var fwd = Flat(p.transform.forward);
            var obj = Object.Instantiate(prefab, p.transform.position + fwd * 4f, Quaternion.LookRotation(-fwd));
            rig.Track(obj);
            yield return Seconds(0.4f); // its own items come in its first frames
            var h = obj != null ? obj.GetComponent<Humanoid>() : null;
            if (h == null)
            {
                continue;
            }
            var shoots = h.GetInventory().GetAllItems().Any(i => i != null && i.m_shared.m_attack != null
                && (i.m_shared.m_attack.m_attackType == Attack.AttackType.Projectile || i.m_shared.m_attack.m_bowDraw));
            if (shoots)
            {
                ZNetScene.instance.Destroy(obj);
                yield return null;
                continue;
            }
            result.Value = h;
        }
    }

    private static IEnumerator RunFoe()
    {
        var c = new Checks(FoeName);
        var rig = new Rig(FoeName);
        try
        {
            var p = rig.P;
            var rules = TowerRules.Defaults();
            yield return UseRules(rules);
            var tower = rig.Give(IronTower);
            if (tower == null)
            {
                c.Check(false, "could not add ShieldIronTower");
                c.Report();
                yield break;
            }
            SetSkill(p, Skills.SkillType.Blocking, 0f);
            yield return FreshBash(p, tower);
            yield return WaitIdle(p);
            yield return WaitCooldown(p);
            FaceClearLane(p, 5f, LaneFan, FoeName, null);
            var box = new Box<Humanoid>();
            yield return LiveDraugr(rig, box);
            var draugr = box.Value;
            if (draugr == null)
            {
                c.Check(false, "could not spawn a Draugr with a melee weapon (six tries)");
                c.Report();
                yield break;
            }

            // Attack button held: a bash every BashCooldown s. The player look at the Draugr (vanilla turn the body)
            // and step up to it when it stand too far for the bash.
            var staggers = new List<float>();     // starts of its stagger
            var staggerEnds = new List<float>();
            var landed = new List<float>();       // bashes that took health from it
            var attacks = new List<float>();      // starts of its own attacks (not while staggering)
            var struck = new List<float>();       // its hits on the player
            var wasStaggering = false;
            var wasAttacking = false;
            var health = draugr.GetHealth();
            var hits = Brace.DebugLast.Count;
            var lockSeconds = rules.BashStaggerLock;
            var watch = lockSeconds + 2f * rules.BashCooldown + 2f;
            var t0 = Time.time;
            SelfTestHooks.HoldAttack = true;
            while (Time.time - t0 < 34f)
            {
                yield return null;
                if (draugr == null || draugr.IsDead())
                {
                    break;
                }
                var now = Time.time - t0;
                var to = draugr.transform.position - p.transform.position;
                var dir = Flat(to);
                p.m_lookYaw = Quaternion.LookRotation(dir);
                p.SetLookDir(dir);
                to.y = 0f;
                var far = to.magnitude > Radius(p) + Radius(draugr) + 0.9f;
                SelfTestHooks.Moving = far;
                SelfTestHooks.Move = far ? dir : Vector3.zero;
                SelfTestHooks.Run = false;
                var staggering = draugr.IsStaggering();
                if (staggering && !wasStaggering)
                {
                    staggers.Add(now);
                }
                if (!staggering && wasStaggering)
                {
                    staggerEnds.Add(now);
                }
                wasStaggering = staggering;
                var attacking = draugr.InAttack();
                if (attacking && !wasAttacking && !staggering)
                {
                    attacks.Add(now);
                }
                wasAttacking = attacking;
                var hp = draugr.GetHealth();
                if (hp < health - 0.01f)
                {
                    landed.Add(now);
                }
                health = hp;
                if (Brace.DebugLast.Count != hits)
                {
                    hits = Brace.DebugLast.Count;
                    if (ReferenceEquals(Brace.DebugLast.Attacker, draugr))
                    {
                        struck.Add(now);
                    }
                }
                if (staggers.Count > 0 && now - staggers[0] > watch)
                {
                    break;
                }
            }
            SelfTestHooks.HoldAttack = false;
            SelfTestHooks.Moving = false;
            SelfTestHooks.Move = Vector3.zero;

            Func<List<float>, string> list = times => times.Count == 0 ? "none" : string.Join(", ", times.Select(F).ToArray());
            SelfTest.Note(FoeName, $"seconds after the button went down: bashes landed at {list(landed)}; Draugr staggered at {list(staggers)} (until {list(staggerEnds)}); "
                                   + $"its attacks began at {list(attacks)}; its hits on the player at {list(struck)}");
            var first = staggers.Count > 0 ? staggers[0] : -1f;
            c.Check(landed.Count > 0 && first >= 0f && first >= landed[0] - 0.1f && first - landed[0] < 0.7f,
                $"the first bash that lands staggers the Draugr (landed {(landed.Count > 0 ? F(landed[0]) : "never")}, staggered {(first >= 0f ? F(first) : "never")})");
            if (first >= 0f)
            {
                var inLock = landed.Count(t => t > first + 0.5f && t < first + lockSeconds - 0.5f);
                var again = staggers.Count > 1 ? staggers[1] : -1f;
                c.Check(inLock >= 1, $"bashes kept landing inside the {F(lockSeconds)} s after that stagger ({inLock})");
                c.Check(again < 0f || again - first >= lockSeconds - 0.4f,
                    $"no second stagger within {F(lockSeconds)} s of the first ({(again < 0f ? "none in the time watched" : F(again - first) + " s later")})");
                var firstEnd = staggerEnds.Count > 0 ? staggerEnds[0] : first + 3f;
                var window = again >= 0f ? again : first + lockSeconds;
                var fought = attacks.Count(t => t >= firstEnd && t <= window) + struck.Count(t => t >= firstEnd && t <= window);
                c.Check(fought >= 1,
                    $"the Draugr attacks the player between the staggers ({attacks.Count(t => t >= firstEnd && t <= window)} attack(s) begun, {struck.Count(t => t >= firstEnd && t <= window)} hit(s) on the player, {F(firstEnd)} to {F(window)} s)");
                var after = landed.Where(t => t > first + lockSeconds + 0.2f).ToList();
                if (after.Count > 0)
                {
                    c.Check(again >= 0f && again >= after[0] - 0.1f && again - after[0] < 0.7f,
                        $"the first bash that lands after the {F(lockSeconds)} s staggers it again (landed {F(after[0])}, staggered {(again >= 0f ? F(again) : "never")})");
                }
                else
                {
                    SelfTest.Note(FoeName, "no bash landed after the lock in the time watched: the second stagger is not judged here (tower.cadence does, with a Draugr that stands still)");
                }
                if (staggerEnds.Count > 0)
                {
                    SelfTest.Note(FoeName, $"the first stagger lasted {F(staggerEnds[0] - first)} s");
                }
            }
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- tower.arm ----------

    // How far each limb went forward of the body in one swing (m, against where it was at the press).
    private sealed class Limbs
    {
        internal float Left;
        internal float Right;
        internal float Foot;
        internal bool Feet;

        internal string Text => $"left hand +{F(Left)} m, right hand +{F(Right)} m, " + (Feet ? $"furthest foot +{F(Foot)} m" : "feet not found");
    }

    private static float Ahead(Player p, Transform bone) =>
        bone != null ? Vector3.Dot(bone.position - p.transform.position, Flat(p.transform.forward)) : 0f;

    // Foot bone: the animator's own when its rig is a humanoid one, else by name, the way the game find the head
    // (Utils.GetBoneTransform look for a child named like the bone; Character.Awake use it). Null = not found.
    private static Transform FootBone(Animator anim, HumanBodyBones bone)
    {
        if (anim == null)
        {
            return null;
        }
        var found = anim.isHuman ? anim.GetBoneTransform(bone) : null;
        return found != null ? found : Utils.GetBoneTransform(anim, bone);
    }

    // One bash on the target (Press), the limbs watched every frame from the press to the end of the swing.
    private static IEnumerator WatchedBash(Player p, Character target, Swing s, Limbs limbs, string test)
    {
        var vis = p.m_visEquipment;
        var left = vis != null ? vis.m_leftHand : null;
        var right = vis != null ? vis.m_rightHand : null;
        var anim = p.m_animator;
        var leftFoot = FootBone(anim, HumanBodyBones.LeftFoot);
        var rightFoot = FootBone(anim, HumanBodyBones.RightFoot);
        limbs.Feet = leftFoot != null && rightFoot != null;
        limbs.Left = 0f;
        limbs.Right = 0f;
        limbs.Foot = 0f;
        var taken = false;
        float l0 = 0f, r0 = 0f, lf0 = 0f, rf0 = 0f;
        var press = Press(p, target, s, test, null);
        while (press.MoveNext())
        {
            if (s.Started)
            {
                var l = Ahead(p, left);
                var r = Ahead(p, right);
                var lf = Ahead(p, leftFoot);
                var rf = Ahead(p, rightFoot);
                if (!taken)
                {
                    taken = true;
                    l0 = l;
                    r0 = r;
                    lf0 = lf;
                    rf0 = rf;
                }
                limbs.Left = Mathf.Max(limbs.Left, l - l0);
                limbs.Right = Mathf.Max(limbs.Right, r - r0);
                limbs.Foot = Mathf.Max(limbs.Foot, Mathf.Max(lf - lf0, rf - rf0));
            }
            yield return press.Current;
        }
    }

    private static TowerRules ArmRules(BashAnimationKind kind, string custom) =>
        TowerRules.Defaults().With(r =>
        {
            r.BashAnimation = kind;
            r.BashCustomTrigger = custom;
            r.BashCooldown = 0f;
        });

    private static IEnumerator RunArm()
    {
        var c = new Checks(ArmName);
        var rig = new Rig(ArmName);
        try
        {
            var p = rig.P;
            yield return UseRules(ArmRules(BashAnimationKind.ShieldPunch, ""));
            var tower = rig.Give(IronTower);
            var troll = rig.Creature("Troll");
            if (tower == null || troll == null)
            {
                c.Check(false, "could not add ShieldIronTower or spawn a Troll");
                c.Report();
                yield break;
            }
            p.EquipItem(tower);
            yield return Fixed;
            c.Check(ReferenceEquals(p.m_leftItem, tower) && p.m_visEquipment != null && p.m_visEquipment.m_leftHand != null && p.m_visEquipment.m_rightHand != null,
                "the tower shield is on the left arm and the game knows both hand bones");

            foreach (var kind in new[] { BashAnimationKind.ShieldPunch, BashAnimationKind.OtherPunch, BashAnimationKind.Kick })
            {
                yield return UseRules(ArmRules(kind, ""));
                yield return FreshBash(p, tower);
                var s = new Swing();
                var limbs = new Limbs();
                yield return WatchedBash(p, troll, s, limbs, ArmName);
                SelfTest.Note(ArmName, $"{kind}: {limbs.Text}. {Describe(kind.ToString(), s)}");
                var played = s.Started && s.Hit && s.Mode == kind && s.ModeAfter == kind;
                c.Check(played, $"{kind}: the bash plays this option to its hit, no fallback (option {s.Mode}, after {s.ModeAfter}, hit {s.Hit})");
                if (!played)
                {
                    continue;
                }
                switch (kind)
                {
                    case BashAnimationKind.ShieldPunch:
                        c.Check(limbs.Left >= 0.15f && limbs.Left > limbs.Right + 0.08f, $"ShieldPunch: the shield arm (left) is the one that strikes ({limbs.Text})");
                        break;
                    case BashAnimationKind.OtherPunch:
                        c.Check(limbs.Right >= 0.15f && limbs.Right > limbs.Left + 0.08f, $"OtherPunch: the other arm (right) is the one that strikes ({limbs.Text})");
                        break;
                    default:
                        // No foot bones = which limb kicks was not measured: a failed check, never a silent skip.
                        c.Check(limbs.Feet, "Kick: the player's foot bones (LeftFoot, RightFoot) were found, so the limb that strikes can be measured");
                        if (limbs.Feet)
                        {
                            c.Check(limbs.Foot >= 0.2f && limbs.Foot > limbs.Left && limbs.Foot > limbs.Right, $"Kick: a foot goes furthest forward ({limbs.Text})");
                        }
                        break;
                }
            }

            // TESTING T15: the Custom examples swung for real. Each one either plays to its hit, or the mod fell back
            // and said so in the log; the swing ends and the player is free after it.
            foreach (var trigger in new[] { "throw_bomb", "spear_poke", "mace_secondary" })
            {
                var mark = Tap.WarningMark();
                yield return UseRules(ArmRules(BashAnimationKind.Custom, trigger));
                yield return FreshBash(p, tower);
                var s = new Swing();
                var limbs = new Limbs();
                yield return WatchedBash(p, troll, s, limbs, ArmName);
                yield return WaitIdle(p);
                var warnings = Tap.WarningsSince(mark).Where(w => w.Contains(trigger) || w.Contains("Custom")).ToList();
                var fellBack = s.Mode != BashAnimationKind.Custom || s.ModeAfter != BashAnimationKind.Custom;
                SelfTest.Note(ArmName, $"Custom '{trigger}': {limbs.Text}; {(fellBack ? "fell back: " + (warnings.Count > 0 ? warnings[0] : "no Warning") : "no fallback")}. {Describe(trigger, s)}");
                c.Check(s.Started && s.EndAt >= 0f && !p.InAttack(), $"Custom '{trigger}': the bash starts, its swing ends and the player is free again (started {s.Started}, over at {T(s.EndAt)})");
                c.Check(fellBack ? warnings.Count > 0 : s.Hit,
                    fellBack ? $"Custom '{trigger}': the mod fell back to {s.ModeAfter} and the log has its Warning" : $"Custom '{trigger}': the swing brings its hit");
            }
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- tower.input.bash ----------

    // The attack button as this game has it bound: only the left mouse button can be pressed by the test.
    private static bool AttackOnLeftMouse(out string path)
    {
        var def = ZInput.instance != null ? ZInput.instance.GetButtonDef("Attack") : null;
        path = def != null ? def.GetActionPath() : null;
        return path == LeftMousePath;
    }

    private static IEnumerator RunInputBash()
    {
        var c = new Checks(InputBashName);
        var rig = new Rig(InputBashName);
        try
        {
            var p = rig.P;
            var rules = TowerRules.Defaults();
            yield return UseRules(rules);
            var tower = rig.Give(IronTower);
            var troll = rig.Creature("Troll");
            if (tower == null || troll == null)
            {
                c.Check(false, "could not add ShieldIronTower or spawn a Troll");
                c.Report();
                yield break;
            }
            if (!AttackOnLeftMouse(out var path))
            {
                c.Check(false, $"the attack button is bound to '{path}', not to the left mouse button: the test can only press that one");
                c.Report();
                yield break;
            }
            SetSkill(p, Skills.SkillType.Blocking, 0f);
            yield return FreshBash(p, tower);
            yield return WaitIdle(p);
            yield return WaitCooldown(p);
            FaceClearLane(p, Reach(p, troll), LaneFan, InputBashName, troll);
            PlaceInFront(p, troll);
            troll.m_staggerDamage = 0f;
            rig.TakeStaminaRate();
            yield return Fixed;
            p.m_stamina = p.GetMaxStamina();
            var cost = BashCost(p);
            var stamina = p.GetStamina();
            var low = stamina;
            var starts = BashWatch.DebugStarts.Count;
            var events = BashWatch.DebugHitEvents;
            var before = p.m_currentAttack;
            var health = troll.GetHealth();
            if (!SelfTest.Key(KeyCode.Mouse0, true))
            {
                c.Check(false, "no input driver: the left mouse button could not be pressed (not a probe run?)");
                c.Report();
                yield break;
            }
            yield return new WaitForSecondsRealtime(0.15f);
            SelfTest.Key(KeyCode.Mouse0, false);
            Attack bash = null;
            var t0 = Time.time;
            // Until the swing the click started is over (or 1 s with no swing at all).
            while (Time.time - t0 < 6f)
            {
                yield return null;
                low = Mathf.Min(low, p.GetStamina());
                var now = p.m_currentAttack;
                if (bash == null && now != null && !ReferenceEquals(now, before))
                {
                    bash = now;
                }
                if (bash == null ? Time.time - t0 > 1f : bash.IsDone() || !ReferenceEquals(p.m_currentAttack, bash))
                {
                    break;
                }
            }
            yield return WaitIdle(p);
            c.Check(bash != null && BashWatch.DebugStarts.Count == starts + 1,
                $"one press of the left mouse button starts one bash ({BashWatch.DebugStarts.Count - starts} start(s))");
            if (bash != null)
            {
                c.Check(ReferenceEquals(bash.GetWeapon(), tower) && bash.m_attackAnimation == BashAttack.ShieldArmTrigger,
                    $"it is the tower shield's bash with the shield-arm strike ('{bash.m_attackAnimation}', weapon {Name(bash.GetWeapon())})");
                // Rate 0 = cost 0 and nothing spent: the two would match and say nothing.
                c.Check(Game.m_staminaRate > 0f && Near(cost, rules.BashStamina, 0.5f) && Near(stamina - low, cost, 0.6f),
                    $"it costs about {F(rules.BashStamina)} stamina ({F(stamina - low)}, {F(cost)} expected at Blocking 0; world stamina rate {F(Game.m_staminaRate)})");
                c.Check(BashWatch.DebugHitEvents == events + 1 && troll.GetHealth() < health,
                    $"its hit lands on the creature in front ({BashWatch.DebugHitEvents - events} hit event(s), health -{F(health - troll.GetHealth())})");
            }
            c.Report();
        }
        finally
        {
            SelfTest.ReleaseInput();
            rig.Done();
        }
    }

    // ---------- tower.ext.jotunn ----------

    private static IEnumerator RunJotunn()
    {
        var c = new Checks(JotunnName);
        if (!Chainloader.PluginInfos.ContainsKey(JotunnGuid))
        {
            SelfTest.Fail(JotunnName, $"Jotunn ({JotunnGuid}) is not installed: TESTING T41 was not checked");
            yield break;
        }
        var rig = new Rig(JotunnName);
        try
        {
            var p = rig.P;
            var users = Chainloader.PluginInfos.Values
                .Where(i => i != null && i.Metadata != null && i.Dependencies != null && i.Dependencies.Any(d => d != null && d.DependencyGUID == JotunnGuid))
                .Select(i => i.Metadata.Name).ToList();
            c.Check(users.Count > 0, $"a mod built on Jotunn is installed ({(users.Count > 0 ? string.Join(", ", users.ToArray()) : "none")})");
            c.Check(_tapFromStart, "this mod's log lines were watched from the game's start (before the main menu)");
            c.Check(TowerRules.Own().Key == TowerRules.Defaults().Key, "this game's own tower settings are the default ones (the item's premise)");
            var missing = Tap.Bad().Where(b => b.StartsWith("Towers warning with the default settings", StringComparison.Ordinal) && b.Contains("(MISSING)")).ToList();
            c.Check(missing.Count == 0,
                missing.Count == 0
                    ? "no '(MISSING)' Towers warning under the default list since the game started: main menu, world load and every test so far"
                    : $"{missing.Count} '(MISSING)' Towers warning(s) under the default list: {missing[0]}");

            // In the world the tower shields work: two-handed, and the attack button bashes.
            yield return UseRules(TowerRules.Defaults());
            var tower = rig.Give(IronTower);
            var sword = rig.Give("SwordIron");
            if (tower == null || sword == null)
            {
                c.Check(false, "could not add ShieldIronTower and SwordIron");
                c.Report();
                yield break;
            }
            p.EquipItem(sword);
            p.EquipItem(tower);
            yield return Fixed;
            c.Check(TowerCatalog.Count == TowerRules.Defaults().Entries.Count && Only(p, tower, left: true) && !sword.m_equipped,
                $"in the world every default tower shield is known ({TowerCatalog.Count}) and the Iron one takes both hands ({Hands(p)})");
            yield return FreshBash(p, tower);
            var s = new Swing();
            yield return Miss(p, JotunnName, s);
            c.Check(s.Started && s.Trigger == BashAttack.ShieldArmTrigger, $"the attack button bashes (started {s.Started}, '{s.Trigger}')");
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }
}
#endif
