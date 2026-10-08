#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.WeaponsDualWieldMod;

// Debug build only. More in-world self tests of the pair's moves and hits (same helpers as SelfTests.cs):
//   dual.moves     axe moves of MaceIron + SwordIron and KnifeBlackMetal + SwordIron (four swings, six hits, cleave);
//                  the game's own AxeBerzerkr and KnifeSkollAndHati swung next to them (same triggers, same stance);
//                  swing trails of both blades sampled during real swings, LeftHandTrails off, the main weapon's own
//                  special
//   dual.elements  SwordMistwalker (frost) + AxeJotunBane (poison) on a Troll: damage types per hit and hand for the
//                  combo, the cleave, HitPattern BothHands, PairMoves = SwordIron and the sword's own special
//                  (SecondaryMoves = MainWeapon); wear and both skills; stance follows a live PairMoves change; the
//                  fallback warning text; the moves Info line; backstab bonus of the striking weapon per hand
//   dual.stamina   stamina really taken by pair swings against one swing of each weapon alone (world stamina
//                  modifier back to normal for the test), cleave x2, knife leap x3
//   dual.trees     a Beech: two axes (every swing the first of the combo, main axe chops, Wood Cutting), sword main
//                  (nothing chopped until swapped), two knives (combo goes on)
//   dual.parry     blocks and parries with a creature as the attacker (it staggers on a parry), the off-hand axe's
//                  block power, the "Blocked" number, knife pairs at about 265 max health and Blocking 0
//   dual.wounded   low-health bonus of SwordNiedhoggBlood in either hand, lightning of SwordNiedhoggLightning only
//                  on off-hand hits (its chance forced to 1 on the test's own copy)
internal static partial class SelfTests
{
    private const string MovesName = "dual.moves";
    private const string ElementsName = "dual.elements";
    private const string StaminaName = "dual.stamina";
    private const string TreesName = "dual.trees";
    private const string ParryName = "dual.parry";
    private const string WoundedName = "dual.wounded";

    private const string Mistwalker = "SwordMistwalker";
    private const string JotunBane = "AxeJotunBane";

    // ---------- helpers ----------

    // Trigger an attack fired: animation + chain level for a chain move, else the animation (as DualSwing record it).
    private static string TriggerOf(Attack a) =>
        a.m_attackChainLevels > 1 ? a.m_attackAnimation + a.m_currentAttackCainLevel : a.m_attackAnimation;

    // Attacks VanillaSwing saw start, in order: their triggers, and the last attack object.
    private static readonly List<string> VanillaTriggers = new List<string>();
    private static Attack _vanillaAttack;

    // Attack button like Swing, for attacks that are no converted pair swing (a weapon alone, the main weapon's own
    // special): held until 'count' attacks started, then the last one runs to its end.
    private static IEnumerator VanillaSwing(Player p, Dummy dummy, int count, bool secondary, Action onFrame = null)
    {
        VanillaTriggers.Clear();
        _vanillaAttack = null;
        yield return WaitIdle(p, dummy);
        var until = Time.time + 3f;
        while (Time.time < until && p.InMinorAction())
        {
            Hold(dummy);
            yield return null;
        }
        var last = p.m_currentAttack;
        until = Time.time + 2.5f * count + 2f;
        while (Time.time < until)
        {
            var now = p.m_currentAttack;
            if (now != null && !ReferenceEquals(now, last))
            {
                last = now;
                _vanillaAttack = now;
                VanillaTriggers.Add(TriggerOf(now));
            }
            if (VanillaTriggers.Count >= count)
            {
                break;
            }
            if (secondary)
            {
                p.m_queuedSecondAttackTimer = 0.5f;
            }
            else
            {
                p.m_queuedAttackTimer = 0.5f;
            }
            Hold(dummy);
            onFrame?.Invoke();
            yield return null;
        }
        p.m_queuedAttackTimer = 0f;
        p.m_queuedSecondAttackTimer = 0f;
        until = Time.time + 4f;
        while (Time.time < until && (p.InAttack() || (p.m_currentAttack != null && !p.m_currentAttack.IsDone())))
        {
            Hold(dummy);
            onFrame?.Invoke();
            yield return null;
        }
        for (var i = 0; i < 15; i++)
        {
            Hold(dummy);
            onFrame?.Invoke();
            yield return null;
        }
    }

    // Frost and poison of a recorded hit are the weapon's (the two test weapons differ in them).
    private static bool ElementsMatch(DualSwing.DamageRecord d, ItemDrop.ItemData weapon)
    {
        var own = weapon.GetDamage();
        return d != null && (d.Frost > 0f) == (own.m_frost > 0f) && (d.Poison > 0f) == (own.m_poison > 0f);
    }

    // Every hit event of the swings from 'fromSwing' reached a creature, and carried the damage types of the weapon
    // of the hand that struck it.
    private static void CheckElements(Checks c, int fromSwing, ItemDrop.ItemData main, ItemDrop.ItemData off, string what)
    {
        var missed = new List<string>();
        var wrong = new List<string>();
        var hits = 0;
        foreach (var hit in DualSwing.Hits)
        {
            if (hit.Swing < fromSwing)
            {
                continue;
            }
            hits++;
            var damage = DamageOf(hit.Swing, hit.Event, hit.Hand);
            if (damage == null)
            {
                missed.Add($"{hit.Trigger} event {hit.Event} {hit.Hand}");
            }
            else if (!ElementsMatch(damage, hit.Hand == Hand.Off ? off : main))
            {
                wrong.Add($"{hit.Trigger} event {hit.Event} {hit.Hand}: frost {F2(damage.Frost)}, poison {F2(damage.Poison)}");
            }
        }
        c.Check(hits > 0 && missed.Count == 0,
            $"{what}: every hit reached the target ({hits} hit event(s); missed: {string.Join("; ", missed.ToArray())})");
        c.Check(hits > missed.Count && wrong.Count == 0,
            $"{what}: each hit carries the damage types of its hand's weapon ({Name(main)} main, {Name(off)} off; wrong: "
            + $"{string.Join("; ", wrong.ToArray())})");
    }

    // About 265 max health as TESTING.md T34 asks (three foods of 80), without eating: no food statistics. The test
    // puts the player's own foods back.
    private static void ThreeFoods(Player p)
    {
        p.m_foods.Clear();
        foreach (var name in new[] { "MeatPlatter", "SerpentStew", "HoneyGlazedChicken" })
        {
            var item = PrefabItem(name);
            if (item == null)
            {
                continue;
            }
            p.m_foods.Add(new Player.Food
            {
                m_name = name,
                m_item = item,
                m_time = item.m_shared.m_foodBurnTime,
                m_health = item.m_shared.m_food,
                m_stamina = item.m_shared.m_foodStamina,
                m_eitr = item.m_shared.m_foodEitr,
            });
        }
        p.UpdateFood(0f, true);
    }

    private static void PutFoodsBack(Player p, List<Player.Food> foods, float health)
    {
        if (foods == null)
        {
            return;
        }
        p.m_foods.Clear();
        p.m_foods.AddRange(foods);
        p.UpdateFood(0f, true);
        p.SetHealth(Mathf.Min(health, p.GetMaxHealth()));
    }

    // ---------- dual.moves ----------

    private static IEnumerator RunMoves()
    {
        var c = new Checks(MovesName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(MovesName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        SkillSave skills = null;
        var spot = player.transform.position;
        var facing = player.transform.rotation;
        try
        {
            bench = new Bench(player);
            skills = new SkillSave(player, Skills.SkillType.Swords, Skills.SkillType.Axes, Skills.SkillType.Clubs,
                Skills.SkillType.Knives);
            if (bench.FreeSlots < 8)
            {
                c.Check(false, $"needs 8 free inventory slots, has {bench.FreeSlots}");
                c.Report();
                yield break;
            }
            var sword = bench.Give(Sword);
            var sword2 = bench.Give(Sword);
            var club = bench.Give(ClubName);
            var mace = bench.Give(Mace);
            var knifeBlack = bench.Give(KnifeBlack);
            var knifeFlint = bench.Give(KnifeFlintName);
            var berzerkr = bench.Give(DualRules.DefaultPairMoves);
            var skoll = bench.Give(DualRules.DefaultKnifePairMoves);
            if (sword == null || sword2 == null || club == null || mace == null || knifeBlack == null || knifeFlint == null
                || berzerkr == null || skoll == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            DualSwing.ResetRecords();
            DualSwing.Recording = true;

            // T08: every pair that is not two knives swings the four axe swings (six hits: one, one, two, two) and
            // the cleave.
            var axeMoves = new[] { "dualaxes0:0M", "dualaxes1:0O", "dualaxes2:0M,1O", "dualaxes3:0M,1O" };
            foreach (var pair in new[] { new[] { mace, sword }, new[] { knifeBlack, sword } })
            {
                var what = $"{Name(pair[0])} + {Name(pair[1])}";
                bench.Pair(pair[0], pair[1]);
                yield return new WaitForSeconds(0.4f);
                c.Check(Holds(player, pair[0], pair[1]) && StateI(player) == 15,
                    $"{what}: paired, dual axe stance (statei {StateI(player)}; {HandsText(player)})");
                var from = DualSwing.Swings.Count;
                yield return Swing(player, null, 4, false);
                CheckSwings(c, from, $"{what}: four axe swings, six hits", axeMoves);
                CheckWeapons(c, from, pair[0], pair[1], what);
                from = DualSwing.Swings.Count;
                yield return Swing(player, null, 1, true);
                CheckSwings(c, from, $"{what}: the cleave", "dualaxes_secondary:0M,0O");
                CheckWeapons(c, from, pair[0], pair[1], $"{what}, cleave");
                yield return new WaitForSeconds(0.3f);
            }

            // T13: Club (no special of its own) main + SwordIron off: no special. Swapped: the cleave.
            bench.Pair(club, sword);
            yield return WaitIdle(player);
            yield return WaitMinor(player);
            c.Check(Holds(player, club, sword), $"Club + SwordIron paired ({HandsText(player)})");
            var beforeSpecial = DualSwing.Swings.Count;
            var clubSpecial = player.StartAttack(null, true);
            yield return null;
            c.Check(!clubSpecial && DualSwing.Swings.Count == beforeSpecial && !player.InAttack(),
                "Club main + SwordIron off: the special attack does nothing");
            yield return SwapHands(player);
            c.Check(_swapQueued && _swapDone && Holds(player, sword, club), $"swapped: sword main, club off ({HandsText(player)})");
            beforeSpecial = DualSwing.Swings.Count;
            yield return Swing(player, null, 1, true);
            CheckSwings(c, beforeSpecial, "club in the off hand: the cleave works", "dualaxes_secondary:0M,0O");
            CheckWeapons(c, beforeSpecial, sword, club, "cleave with the club in the off hand");
            yield return new WaitForSeconds(0.3f);

            // T08: the game's own Berserkir axes, swung the same way: same triggers, same stance.
            bench.Empty();
            var berzerkrEquipped = player.EquipItem(berzerkr);
            yield return new WaitForSeconds(0.4f);
            var berzerkrStance = StateI(player);
            yield return VanillaSwing(player, null, 4, false);
            var berzerkrCombo = string.Join(", ", VanillaTriggers.ToArray());
            yield return VanillaSwing(player, null, 1, true);
            var berzerkrSpecial = string.Join(", ", VanillaTriggers.ToArray());
            c.Check(berzerkrEquipped && berzerkrStance == 15 && berzerkrCombo == "dualaxes0, dualaxes1, dualaxes2, dualaxes3"
                    && berzerkrSpecial == "dualaxes_secondary",
                "the game's AxeBerzerkr swings the same moves in the same stance as the pairs above (stance statei "
                + $"{berzerkrStance}; combo {berzerkrCombo}; special {berzerkrSpecial})");

            // T09: two knives against the game's own Skoll and Hati.
            bench.Pair(knifeFlint, knifeBlack);
            yield return new WaitForSeconds(0.4f);
            c.Check(Holds(player, knifeFlint, knifeBlack) && StateI(player) == 11,
                $"KnifeFlint + KnifeBlackMetal: knife stance (statei {StateI(player)}; {HandsText(player)})");
            var knives = DualSwing.Swings.Count;
            yield return Swing(player, null, 3, false);
            CheckSwings(c, knives, "knife pair: three stabs", "dual_knives0:0M", "dual_knives1:0O", "dual_knives2:0M,0O");
            CheckWeapons(c, knives, knifeFlint, knifeBlack, "knife pair");
            knives = DualSwing.Swings.Count;
            yield return Swing(player, null, 1, true);
            CheckSwings(c, knives, "knife pair: the leap", "dual_knives_secondary:0M,0O");
            yield return WaitIdle(player);
            Pin(player, spot);
            bench.Empty();
            var skollEquipped = player.EquipItem(skoll);
            yield return new WaitForSeconds(0.4f);
            var skollStance = StateI(player);
            yield return VanillaSwing(player, null, 3, false);
            var skollCombo = string.Join(", ", VanillaTriggers.ToArray());
            yield return VanillaSwing(player, null, 1, true);
            var skollSpecial = string.Join(", ", VanillaTriggers.ToArray());
            c.Check(skollEquipped && skollStance == 11 && skollCombo == "dual_knives0, dual_knives1, dual_knives2"
                    && skollSpecial == "dual_knives_secondary",
                "the game's KnifeSkollAndHati swings the same moves in the same stance as the knife pair (stance statei "
                + $"{skollStance}; combo {skollCombo}; special {skollSpecial})");
            yield return WaitIdle(player);
            Pin(player, spot);

            // T24: swing trails during real swings (the clips switch them through the game's TrailOn / TrailOff).
            bench.Pair(sword, sword2);
            yield return new WaitForSeconds(0.5f);
            var vis = player.m_visEquipment;
            var leftGo = vis != null ? vis.m_leftItemInstance : null;
            var rightGo = vis != null ? vis.m_rightItemInstance : null;
            var leftTrails = leftGo != null ? leftGo.GetComponentsInChildren<MeleeWeaponTrail>(true) : new MeleeWeaponTrail[0];
            var rightTrails = rightGo != null ? rightGo.GetComponentsInChildren<MeleeWeaponTrail>(true) : new MeleeWeaponTrail[0];
            c.Check(vis != null && !vis.m_useAllTrails && leftTrails.Length > 0 && rightTrails.Length > 0,
                $"two SwordIron in hand, each with a swing trail to watch (main {rightTrails.Length}, off hand {leftTrails.Length})");
            if (vis != null && !vis.m_useAllTrails && leftTrails.Length > 0 && rightTrails.Length > 0)
            {
                var leftOn = false;
                var rightOn = false;
                // Mesh of the trail (what is drawn): the game builds it while the trail emits.
                var leftMesh = false;
                var rightMesh = false;
                Action sample = () =>
                {
                    leftOn |= leftTrails.Any(t => t != null && t._emit);
                    rightOn |= rightTrails.Any(t => t != null && t._emit);
                    leftMesh |= leftTrails.Any(t => t != null && t.m_trailMesh != null && t.m_trailMesh.vertexCount > 0);
                    rightMesh |= rightTrails.Any(t => t != null && t.m_trailMesh != null && t.m_trailMesh.vertexCount > 0);
                };
                LeftTrails.TestLeftHandTrails = true;
                // Known start: both trails off through the game's own call (a weapon just put in a hand may still carry
                // its prefab's emit flag), so a trail seen during the combo was switched on by the swing.
                vis.SetWeaponTrails(false);
                yield return null;
                c.Check(TrailsAre(leftTrails, false) && TrailsAre(rightTrails, false), "both trails are off before the combo");
                // Old trail points gone first (the game keeps one up to a second): a mesh seen in the combo is the combo's.
                yield return new WaitForSeconds(1.2f);
                yield return Swing(player, null, 4, false, onFrame: sample);
                c.Check(rightOn && leftOn, $"both blades trail during the combo (main-hand trail seen {rightOn}, off-hand trail seen {leftOn})");
                SelfTest.Note(MovesName, $"trail mesh drawn during the combo: main-hand blade {rightMesh}, off-hand blade {leftMesh}");
                c.Check(leftMesh || !rightMesh,
                    $"the off-hand blade's trail is drawn like the main-hand one (mesh with points: main hand {rightMesh}, off hand {leftMesh})");
                c.Check(TrailsAre(leftTrails, false) && TrailsAre(rightTrails, false), "both trails are off again after the combo");
                leftOn = false;
                rightOn = false;
                LeftTrails.TestLeftHandTrails = false;
                yield return new WaitForSeconds(0.3f);
                yield return Swing(player, null, 2, false, onFrame: sample);
                c.Check(rightOn && !leftOn,
                    $"LeftHandTrails off: only the main-hand blade trails (main-hand trail seen {rightOn}, off-hand trail seen {leftOn})");
                leftOn = false;
                rightOn = false;
                LeftTrails.TestLeftHandTrails = true;
                ServerRules.TestRules = Rules(secondary: SecondaryMovesMode.MainWeapon);
                yield return new WaitForSeconds(0.3f);
                var converted = DualSwing.Swings.Count;
                yield return VanillaSwing(player, null, 1, true, sample);
                c.Check(VanillaTriggers.Count == 1 && DualSwing.Swings.Count == converted && _vanillaAttack != null
                        && ReferenceEquals(_vanillaAttack.m_weapon, sword),
                    "SecondaryMoves = MainWeapon: the special is the main sword's own attack, not a pair swing "
                    + $"({string.Join(", ", VanillaTriggers.ToArray())})");
                c.Check(!leftOn && rightOn,
                    $"SecondaryMoves = MainWeapon: only the main-hand sword trails during the special (main-hand trail seen {rightOn}, "
                    + $"off-hand trail seen {leftOn})");
                ServerRules.TestRules = DualRules.Defaults;
                LeftTrails.TestLeftHandTrails = null;
            }
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
            Pin(player, spot);
            player.transform.rotation = facing;
            if (player.m_body != null)
            {
                player.m_body.rotation = facing;
            }
        }
    }

    // ---------- dual.elements ----------

    private static IEnumerator RunElements()
    {
        var c = new Checks(ElementsName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(ElementsName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        SkillSave skills = null;
        try
        {
            bench = new Bench(player);
            skills = new SkillSave(player, Skills.SkillType.Swords, Skills.SkillType.Axes, Skills.SkillType.Knives);
            var mist = bench.Give(Mistwalker);
            var jotun = bench.Give(JotunBane);
            var knife = bench.Give(KnifeBlack);
            var sword = bench.Give(Sword);
            if (mist == null || jotun == null || knife == null || sword == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            var mistDamage = mist.GetDamage();
            var jotunDamage = jotun.GetDamage();
            c.Check(mistDamage.m_frost > 0f && mistDamage.m_poison <= 0f && jotunDamage.m_poison > 0f && jotunDamage.m_frost <= 0f,
                $"SwordMistwalker deals frost and no poison, AxeJotunBane poison and no frost (frost {F2(mistDamage.m_frost)} / "
                + $"{F2(jotunDamage.m_frost)}, poison {F2(mistDamage.m_poison)} / {F2(jotunDamage.m_poison)})");
            var dummy = bench.Spawn(TrollName, DummyGap);
            c.Check(dummy != null && dummy.Character != null, "Troll spawned in front of the player");
            NoteDummy(ElementsName, dummy);
            yield return new WaitForSeconds(0.5f);
            Hold(dummy);
            bench.Pair(mist, jotun);
            yield return new WaitForSeconds(0.4f);
            c.Check(Holds(player, mist, jotun) && StateI(player) == 15, $"SwordMistwalker + AxeJotunBane paired ({HandsText(player)})");
            // T25: the Info line last logged is the default moves' line.
            MoveTemplates.For(mist, jotun, ServerRules.Current, player);
            c.Check(MoveTemplates.LastInfo == DefaultMovesInfo && MoveTemplates.InfoCount > 0,
                $"moves Info line of the default settings: '{DefaultMovesInfo}' (logged last: '{MoveTemplates.LastInfo}', "
                + $"{MoveTemplates.InfoCount} Info line(s) this session)");
            DualSwing.ResetRecords();
            DualSwing.Recording = true;

            // T10: frost on main-hand hits, poison on off-hand hits, both on the cleave; wear and both skills.
            var mistWear = mist.m_durability;
            var jotunWear = jotun.m_durability;
            var swords = SkillSave.Value(player, Skills.SkillType.Swords);
            var axes = SkillSave.Value(player, Skills.SkillType.Axes);
            var from = DualSwing.Swings.Count;
            yield return Swing(player, dummy, 4, false);
            CheckSwings(c, from, "combo", "dualaxes0:0M", "dualaxes1:0O", "dualaxes2:0M,1O", "dualaxes3:0M,1O");
            CheckElements(c, from, mist, jotun, "combo");
            c.Check(mist.m_durability < mistWear && jotun.m_durability < jotunWear,
                $"both weapons wear (sword {F2(mistWear)} -> {F2(mist.m_durability)}, axe {F2(jotunWear)} -> {F2(jotun.m_durability)})");
            c.Check(SkillSave.Value(player, Skills.SkillType.Swords) > swords && SkillSave.Value(player, Skills.SkillType.Axes) > axes,
                "Swords and Axes both rise");
            from = DualSwing.Swings.Count;
            yield return Swing(player, dummy, 1, true);
            CheckSwings(c, from, "cleave", "dualaxes_secondary:0M,0O");
            CheckElements(c, from, mist, jotun, "cleave (frost and poison)");

            // T27: HitPattern BothHands: every hit event strikes twice, frost and poison each time.
            ServerRules.TestRules = Rules(pattern: HitPatternMode.BothHands);
            yield return new WaitForSeconds(0.5f);
            from = DualSwing.Swings.Count;
            yield return Swing(player, dummy, 2, false);
            CheckSwings(c, from, "BothHands", "dualaxes0:0M,0O", "dualaxes1:0M,0O");
            CheckElements(c, from, mist, jotun, "BothHands (two numbers per hit, frost and poison)");

            // T25: PairMoves = SwordIron: the sword's stance and three-swing combo (frost, poison, frost), its special
            // with both.
            ServerRules.TestRules = Rules(pair: Sword);
            yield return new WaitForSeconds(0.5f);
            var swordStance = PrefabItem(Sword) != null ? (int)PrefabItem(Sword).m_shared.m_animationState : -1;
            c.Check(StateI(player) == swordStance,
                $"PairMoves = SwordIron: the one-handed sword stance at once (statei {StateI(player)}, {swordStance} expected)");
            from = DualSwing.Swings.Count;
            yield return Swing(player, dummy, 3, false);
            CheckSwings(c, from, "PairMoves = SwordIron: combo main, off, main", "swing_longsword0:0M", "swing_longsword1:0O",
                "swing_longsword2:0M");
            CheckElements(c, from, mist, jotun, "PairMoves = SwordIron combo (frost, poison, frost)");
            from = DualSwing.Swings.Count;
            yield return Swing(player, dummy, 1, true);
            CheckSwings(c, from, "PairMoves = SwordIron: special with both", "sword_secondary:0M,0O");
            CheckElements(c, from, mist, jotun, "PairMoves = SwordIron special (frost and poison)");

            // T25: SecondaryMoves = MainWeapon: the special is the main sword's own, struck by the sword only.
            ServerRules.TestRules = Rules(secondary: SecondaryMovesMode.MainWeapon);
            yield return new WaitForSeconds(0.5f);
            var converted = DualSwing.Swings.Count;
            var damages = DualSwing.Damages.Count;
            yield return VanillaSwing(player, dummy, 1, true);
            var own = DualSwing.Damages.Skip(damages).ToList();
            c.Check(VanillaTriggers.Count == 1 && VanillaTriggers[0] == mist.m_shared.m_secondaryAttack.m_attackAnimation
                    && DualSwing.Swings.Count == converted && _vanillaAttack != null && ReferenceEquals(_vanillaAttack.m_weapon, mist),
                "SecondaryMoves = MainWeapon: the special is the main sword's own attack "
                + $"({string.Join(", ", VanillaTriggers.ToArray())}, {mist.m_shared.m_secondaryAttack.m_attackAnimation} expected)");
            c.Check(own.Count > 0 && own.All(d => ElementsMatch(d, mist)),
                $"SecondaryMoves = MainWeapon: struck by the sword only ({own.Count} hit(s), every one frost and no poison: "
                + $"{string.Join("; ", own.Select(d => $"frost {F2(d.Frost)} poison {F2(d.Poison)}").ToArray())})");

            // T25: a live PairMoves change switches the stance at once; a bad name warns and falls back.
            ServerRules.TestRules = Rules(pair: DualRules.DefaultKnifePairMoves);
            yield return null;
            yield return null;
            yield return null;
            var knifeTemplate = MoveTemplates.For(mist, jotun, ServerRules.Current, player);
            c.Check(StateI(player) == 11 && knifeTemplate != null && knifeTemplate.PrefabName == DualRules.DefaultKnifePairMoves,
                $"PairMoves = KnifeSkollAndHati while paired: knife stance and moves at once (statei {StateI(player)})");
            yield return new WaitForSeconds(0.4f);
            from = DualSwing.Swings.Count;
            yield return Swing(player, dummy, 1, false);
            CheckSwings(c, from, "PairMoves = KnifeSkollAndHati: the sword + axe pair stabs", "dual_knives0:0M");
            MoveTemplates.LastWarning = null;
            ServerRules.TestRules = Rules(pair: "Nothing");
            var fallback = MoveTemplates.For(mist, jotun, ServerRules.Current, player);
            yield return null;
            yield return null;
            yield return null;
            const string NothingWarning = "Moves.PairMoves = Nothing: no item with this name in the game. The "
                                          + "default AxeBerzerkr is used instead.";
            var nothingLogged = LogWatch.WarningLines().Contains(NothingWarning);
            c.Check(fallback != null && fallback.PrefabName == DualRules.DefaultPairMoves && StateI(player) == 15
                    && MoveTemplates.LastWarning == NothingWarning && nothingLogged,
                $"PairMoves = Nothing: the warning, and the axe moves and stance (statei {StateI(player)}, warning "
                + $"'{MoveTemplates.LastWarning}', in the log {nothingLogged})");
            ServerRules.TestRules = DualRules.Defaults;
            yield return new WaitForSeconds(0.4f);

            // X04 (hits): each hit carries the backstab bonus of the weapon that strikes it.
            bench.Pair(knife, sword);
            yield return new WaitForSeconds(0.4f);
            var knifeBonus = knife.m_shared.m_backstabBonus;
            var swordBonus = sword.m_shared.m_backstabBonus;
            from = DualSwing.Swings.Count;
            yield return Swing(player, dummy, 2, false);
            var mainHit = DamageOf(SwingIndex(from, "dualaxes0"), 0, Hand.Main);
            var offHit = DamageOf(SwingIndex(from, "dualaxes1"), 0, Hand.Off);
            c.Check(mainHit != null && offHit != null && Approx(mainHit.Backstab, knifeBonus) && Approx(offHit.Backstab, swordBonus),
                $"knife main + sword off: the first hit carries the knife's backstab bonus ({F2(knifeBonus)}), the off-hand "
                + $"hit the sword's ({F2(swordBonus)}); got {(mainHit != null ? F2(mainHit.Backstab) : "no hit")} and "
                + $"{(offHit != null ? F2(offHit.Backstab) : "no hit")}");
            yield return SwapHands(player, dummy);
            yield return new WaitForSeconds(0.3f);
            from = DualSwing.Swings.Count;
            yield return Swing(player, dummy, 1, false);
            mainHit = DamageOf(SwingIndex(from, "dualaxes0"), 0, Hand.Main);
            c.Check(_swapQueued && _swapDone && mainHit != null && Approx(mainHit.Backstab, swordBonus),
                $"after the swap the first hit carries the sword's backstab bonus ({F2(swordBonus)}); got "
                + $"{(mainHit != null ? F2(mainHit.Backstab) : "no hit")}");
            SelfTest.Note(ElementsName, $"backstab bonus: KnifeBlackMetal {F2(knifeBonus)}, SwordIron {F2(swordBonus)}");
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

    // ---------- dual.stamina ----------

    // Stamina MeasureStamina saw one attack take (-1 = the attack did not start).
    private static float _staminaUsed;

    // One attack with a full bar: stamina read before the press, and again when vanilla has taken the cost (Attack.Update
    // on the first frame in the attack state; nothing regenerates for a second after). Then the attack runs out and the
    // combo window passes, so the next measure starts a combo again.
    private static IEnumerator MeasureStamina(Player p, bool secondary)
    {
        _staminaUsed = -1f;
        yield return WaitIdle(p);
        yield return WaitMinor(p);
        yield return new WaitForSeconds(0.35f);
        p.AddStamina(p.GetMaxStamina());
        yield return null;
        var before = p.GetStamina();
        var old = p.m_currentAttack;
        var until = Time.time + 2f;
        while (Time.time < until && (p.m_currentAttack == null || ReferenceEquals(p.m_currentAttack, old)))
        {
            if (secondary)
            {
                p.m_queuedSecondAttackTimer = 0.5f;
            }
            else
            {
                p.m_queuedAttackTimer = 0.5f;
            }
            yield return null;
        }
        p.m_queuedAttackTimer = 0f;
        p.m_queuedSecondAttackTimer = 0f;
        var attack = p.m_currentAttack;
        if (attack == null || ReferenceEquals(attack, old))
        {
            yield break;
        }
        until = Time.time + 2f;
        while (Time.time < until && !attack.m_wasInAttack && !attack.m_attackDone)
        {
            yield return null;
        }
        _staminaUsed = before - p.GetStamina();
        yield return WaitIdle(p);
    }

    private static IEnumerator RunStamina()
    {
        var c = new Checks(StaminaName);
        var player = Player.m_localPlayer;
        if (player == null || ZoneSystem.instance == null)
        {
            SelfTest.Fail(StaminaName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        SkillSave skills = null;
        var spot = player.transform.position;
        var facing = player.transform.rotation;
        var hadKey = ZoneSystem.instance.GetGlobalKey(GlobalKeys.StaminaRate, out float rateBefore);
        var keyRemoved = false;
        try
        {
            bench = new Bench(player);
            // Same skill for every weapon: the skill of the main weapon lowers the cost of a swing.
            skills = new SkillSave(player, Skills.SkillType.Swords, Skills.SkillType.Knives, Skills.SkillType.Axes);
            SkillSave.SetLevel(player, Skills.SkillType.Swords, 0f);
            SkillSave.SetLevel(player, Skills.SkillType.Knives, 0f);
            SkillSave.SetLevel(player, Skills.SkillType.Axes, 0f);
            var sword = bench.Give(Sword);
            var knifeFlint = bench.Give(KnifeFlintName);
            var knifeBlack = bench.Give(KnifeBlack);
            if (sword == null || knifeFlint == null || knifeBlack == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            // The probe world has the stamina modifier at 0 (no stamina use): back to the default for this test.
            if (hadKey)
            {
                ZoneSystem.instance.RemoveGlobalKey(GlobalKeys.StaminaRate);
                keyRemoved = true;
            }
            var until = Time.time + 3f;
            while (Time.time < until && !Approx(Game.m_staminaRate, 1f))
            {
                yield return null;
            }
            c.Check(Approx(Game.m_staminaRate, 1f), $"world stamina modifier at its default for the test (rate {F2(Game.m_staminaRate)})");

            // One swing of each weapon alone.
            var alone = new Dictionary<ItemDrop.ItemData, float>();
            foreach (var weapon in new[] { sword, knifeFlint, knifeBlack })
            {
                bench.Empty();
                player.EquipItem(weapon);
                yield return MeasureStamina(player, false);
                alone[weapon] = _staminaUsed;
            }
            SelfTest.Note(StaminaName, $"one swing alone: SwordIron {F2(alone[sword])}, KnifeFlint {F2(alone[knifeFlint])}, "
                                       + $"KnifeBlackMetal {F2(alone[knifeBlack])} stamina (skills at 0, max stamina "
                                       + $"{F2(player.GetMaxStamina())})");
            c.Check(alone[sword] > 0f && alone[knifeFlint] > 0f && alone[knifeBlack] > 0f
                    && alone[sword] > alone[knifeFlint] && alone[knifeBlack] > alone[sword],
                "a swing of each weapon alone takes stamina, KnifeFlint the least and KnifeBlackMetal the most "
                + $"({F2(alone[knifeFlint])}, {F2(alone[sword])}, {F2(alone[knifeBlack])})");

            // KnifeFlint + SwordIron, either hand: the sword's cost. Cleave: twice.
            bench.Pair(knifeFlint, sword);
            yield return MeasureStamina(player, false);
            var flintMain = _staminaUsed;
            yield return MeasureStamina(player, true);
            var cleave = _staminaUsed;
            bench.Pair(sword, knifeFlint);
            yield return MeasureStamina(player, false);
            var flintOff = _staminaUsed;
            c.Check(Mathf.Abs(flintMain - alone[sword]) < 0.05f && Mathf.Abs(flintOff - alone[sword]) < 0.05f,
                $"KnifeFlint + SwordIron, either hand: a swing costs the sword's swing ({F2(alone[sword])}); got "
                + $"{F2(flintMain)} (knife main) and {F2(flintOff)} (sword main)");
            c.Check(flintMain > 0f && Mathf.Abs(cleave - 2f * flintMain) < 0.1f,
                $"the cleave costs twice a pair swing ({F2(2f * flintMain)}); got {F2(cleave)}");
            // KnifeBlackMetal + SwordIron: the knife's cost.
            bench.Pair(knifeBlack, sword);
            yield return MeasureStamina(player, false);
            var blackMain = _staminaUsed;
            c.Check(Mathf.Abs(blackMain - alone[knifeBlack]) < 0.05f,
                $"KnifeBlackMetal + SwordIron: a swing costs the black metal knife's swing ({F2(alone[knifeBlack])}); got {F2(blackMain)}");
            // Two knives: the dearer knife's cost; the leap three times.
            bench.Pair(knifeFlint, knifeBlack);
            yield return MeasureStamina(player, false);
            var stab = _staminaUsed;
            yield return MeasureStamina(player, true);
            var leap = _staminaUsed;
            c.Check(Mathf.Abs(stab - alone[knifeBlack]) < 0.05f && stab > 0f && Mathf.Abs(leap - 3f * stab) < 0.15f,
                $"two knives: a stab costs the dearer knife's swing ({F2(alone[knifeBlack])}), the leap three times a stab "
                + $"({F2(3f * stab)}); got {F2(stab)} and {F2(leap)}");
            c.Report();
        }
        finally
        {
            if (keyRemoved && ZoneSystem.instance != null)
            {
                ZoneSystem.instance.SetGlobalKey(GlobalKeys.StaminaRate, rateBefore);
            }
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
            player.AddStamina(player.GetMaxStamina());
            Pin(player, spot);
            player.transform.rotation = facing;
            if (player.m_body != null)
            {
                player.m_body.rotation = facing;
            }
        }
    }

    // ---------- dual.trees ----------

    private static IEnumerator RunTrees()
    {
        var c = new Checks(TreesName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(TreesName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        SkillSave skills = null;
        try
        {
            bench = new Bench(player);
            skills = new SkillSave(player, Skills.SkillType.WoodCutting, Skills.SkillType.Axes, Skills.SkillType.Swords,
                Skills.SkillType.Knives);
            var axe = bench.Give(Axe);
            var axe2 = bench.Give(Axe);
            var sword = bench.Give(Sword);
            var knifeFlint = bench.Give(KnifeFlintName);
            var knifeBlack = bench.Give(KnifeBlack);
            if (axe == null || axe2 == null || sword == null || knifeFlint == null || knifeBlack == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            // A Beech in front (a tree, not a creature: the dummy only holds its place). Health so high it never falls.
            var tree = bench.Spawn("Beech1", DummyGap);
            var treeBase = tree != null ? tree.Go.GetComponent<TreeBase>() : null;
            var nview = tree != null ? tree.Go.GetComponent<ZNetView>() : null;
            if (treeBase == null || nview == null || !nview.IsValid())
            {
                c.Check(false, "a Beech1 tree spawned in front of the player");
                c.Report();
                yield break;
            }
            NoteDummy(TreesName, tree);
            var zdo = nview.GetZDO();
            zdo.Set(ZDOVars.s_health, 100000f);
            Func<float> health = () => zdo.GetFloat(ZDOVars.s_health, treeBase.m_health);
            yield return new WaitForSeconds(0.5f);
            DualSwing.ResetRecords();
            DualSwing.Recording = true;

            // Two axes: the tree resets the combo on every hit, the main axe chops, Wood Cutting rises.
            bench.Pair(axe, axe2);
            yield return new WaitForSeconds(0.4f);
            var before = health();
            var cutting = SkillSave.Value(player, Skills.SkillType.WoodCutting);
            var from = DualSwing.Swings.Count;
            yield return Swing(player, tree, 3, false);
            CheckSwings(c, from, "two AxeIron on a tree: every swing is the first of the combo, struck by the main axe",
                "dualaxes0:0M", "dualaxes0:0M", "dualaxes0:0M");
            CheckWeapons(c, from, axe, axe2, "two axes on a tree");
            c.Check(health() < before - 1f, $"the main axe chops the tree (tree health {F2(before)} -> {F2(health())})");
            c.Check(SkillSave.Value(player, Skills.SkillType.WoodCutting) > cutting, "Wood Cutting rises");

            // Sword main + axe off: the sword chops nothing (and the combo never reaches the off-hand axe); swapped,
            // the axe chops.
            bench.Pair(sword, axe);
            yield return new WaitForSeconds(0.4f);
            before = health();
            cutting = SkillSave.Value(player, Skills.SkillType.WoodCutting);
            from = DualSwing.Swings.Count;
            yield return Swing(player, tree, 2, false);
            CheckSwings(c, from, "sword main + axe off on a tree: the sword strikes every swing", "dualaxes0:0M", "dualaxes0:0M");
            CheckWeapons(c, from, sword, axe, "sword main on a tree");
            c.Check(Approx(health(), before) && Approx(SkillSave.Value(player, Skills.SkillType.WoodCutting), cutting),
                $"the sword chops nothing (tree health {F2(before)} -> {F2(health())}, Wood Cutting unchanged "
                + $"{Approx(SkillSave.Value(player, Skills.SkillType.WoodCutting), cutting)})");
            yield return SwapHands(player, tree);
            yield return new WaitForSeconds(0.3f);
            before = health();
            from = DualSwing.Swings.Count;
            yield return Swing(player, tree, 1, false);
            CheckSwings(c, from, "after the swap the axe strikes first", "dualaxes0:0M");
            c.Check(_swapQueued && _swapDone && Holds(player, axe, sword) && health() < before - 1f
                    && SkillSave.Value(player, Skills.SkillType.WoodCutting) > cutting,
                $"after the swap the axe chops (tree health {F2(before)} -> {F2(health())}; {HandsText(player)})");

            // Two knives: no chain reset in the knife moves, the combo goes on; nothing chopped.
            bench.Pair(knifeFlint, knifeBlack);
            yield return new WaitForSeconds(0.4f);
            before = health();
            cutting = SkillSave.Value(player, Skills.SkillType.WoodCutting);
            from = DualSwing.Swings.Count;
            yield return Swing(player, tree, 3, false);
            CheckSwings(c, from, "two knives on a tree: the combo goes on", "dual_knives0:0M", "dual_knives1:0O", "dual_knives2:0M,0O");
            c.Check(Approx(SkillSave.Value(player, Skills.SkillType.WoodCutting), cutting) && Approx(health(), before),
                $"two knives chop nothing (tree health {F2(before)} -> {F2(health())}, Wood Cutting unchanged "
                + $"{Approx(SkillSave.Value(player, Skills.SkillType.WoodCutting), cutting)})");
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

    // ---------- dual.parry ----------

    // Result of BlockFrom: blunt damage left on the hit, what the blocker lost, base power factor of the Blocking
    // skill at the call, world texts shown before the call.
    private static float _blockLeft;
    private static float _blockWear;
    private static float _blockSkill;
    private static int _blockTexts;

    // One real BlockAttack call with a creature as the attacker: 'blunt' damage from straight ahead, block timer 1 s
    // (block) or 0.1 s (parry, inside the game's 0.25 s window). Full stamina, empty stagger bar.
    private static void BlockFrom(Player p, Character attacker, float blunt, bool parry)
    {
        var blocker = p.GetCurrentBlocker();
        var hit = new HitData();
        hit.m_damage.m_blunt = blunt;
        hit.m_dir = -p.transform.forward;
        hit.m_point = p.GetCenterPoint();
        if (attacker != null)
        {
            hit.SetAttacker(attacker);
        }
        p.AddStamina(p.GetMaxStamina());
        p.m_staggerDamage = 0f;
        _blockSkill = 1f + p.GetSkillFactor(Skills.SkillType.Blocking) * 0.5f;
        _blockTexts = global::DamageText.instance != null ? global::DamageText.instance.m_worldTexts.Count : 0;
        var wear = blocker != null ? blocker.m_durability : 0f;
        var timer = p.m_blockTimer;
        p.m_blockTimer = parry ? 0.1f : 1f;
        try
        {
            p.BlockAttack(hit, attacker);
        }
        finally
        {
            p.m_blockTimer = timer;
        }
        _blockLeft = hit.m_damage.m_blunt;
        _blockWear = blocker != null ? wear - blocker.m_durability : 0f;
    }

    // "Blocked" number the game showed for the last BlockFrom: the newest world text, when one came with the call
    // (read right after it, in the same frame). Null = none.
    private static string BlockedText()
    {
        var texts = global::DamageText.instance != null ? global::DamageText.instance.m_worldTexts : null;
        if (texts == null || texts.Count <= _blockTexts || texts.Count == 0)
        {
            return null;
        }
        var field = texts[texts.Count - 1].m_textField;
        return field != null ? field.text : null;
    }

    private static string Number(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    // Me wait: creature staggering (or 'seconds' gone). Result = it was seen staggering.
    private static bool _staggered;

    private static IEnumerator WaitStagger(Character creature, float seconds)
    {
        _staggered = false;
        var until = Time.time + seconds;
        while (Time.time < until && creature != null && !creature.IsDead())
        {
            if (creature.IsStaggering())
            {
                _staggered = true;
                yield break;
            }
            yield return null;
        }
    }

    private static IEnumerator RunParry()
    {
        var c = new Checks(ParryName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(ParryName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        SkillSave skills = null;
        List<Player.Food> foods = null;
        var healthBefore = player.GetHealth();
        var staggerBefore = player.m_staggerDamage;
        try
        {
            bench = new Bench(player);
            skills = new SkillSave(player, Skills.SkillType.Blocking);
            foods = new List<Player.Food>(player.m_foods);
            var template = PrefabItem(DualRules.DefaultKnifePairMoves);
            var sword = bench.Give(Sword);
            var axe = bench.Give(Axe);
            var black = bench.Give(KnifeBlack);
            var black2 = bench.Give(KnifeBlack);
            if (template == null || sword == null || axe == null || black == null || black2 == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            // TESTING.md T34 setup: Blocking 0 and about 265 max health (a block fails when what gets through fills the
            // stagger bar, 40% of the max health).
            SkillSave.SetLevel(player, Skills.SkillType.Blocking, 0f);
            ThreeFoods(player);
            player.SetHealth(player.GetMaxHealth());
            SelfTest.Note(ParryName, $"max health {F2(player.GetMaxHealth())} with three foods of 80, stagger bar "
                                     + $"{F2(player.GetMaxHealth() * player.m_staggerDamageFactor)}");
            c.Check(player.GetMaxHealth() > 200f, $"about 265 max health for the block checks (got {F2(player.GetMaxHealth())})");

            // T14: sword + axe: the off-hand axe blocks with its own block power; a timed block parries with its bonus.
            bench.Pair(sword, axe);
            yield return null;
            c.Check(Holds(player, sword, axe) && ReferenceEquals(player.GetCurrentBlocker(), axe),
                $"sword + axe: the off-hand axe is the blocker ({HandsText(player)})");
            CheckBlock(c, player, axe.GetBaseBlockPower(), axe.m_shared.m_timedBlockBonus, false, "sword + axe, block with the axe's power");
            CheckBlock(c, player, axe.GetBaseBlockPower(), axe.m_shared.m_timedBlockBonus, true, "sword + axe, parry with the axe's bonus");
            c.Check(black.GetBaseBlockPower() < sword.GetBaseBlockPower() * 0.5f,
                $"a knife blocks far worse than a sword (block power {F2(black.GetBaseBlockPower())} against {F2(sword.GetBaseBlockPower())})");

            // T14: a creature as the attacker: a plain block does not stagger it, a timed block does.
            var troll = bench.Spawn(TrollName, 2f);
            c.Check(troll != null && troll.Character != null, "Troll spawned in front of the player");
            if (troll != null && troll.Character != null)
            {
                yield return new WaitForSeconds(0.5f);
                var axePower = axe.GetBaseBlockPower();
                BlockFrom(player, troll.Character, 30f, false);
                var plainLeft = HitData.DamageTypes.ApplyArmor(30f, axePower * _blockSkill);
                c.Check(Mathf.Abs(_blockLeft - plainLeft) < 0.01f,
                    $"block of a Troll's 30 blunt with the axe: {F2(plainLeft)} gets through, got {F2(_blockLeft)}");
                yield return WaitStagger(troll.Character, 0.6f);
                c.Check(!_staggered, "a plain block does not stagger the Troll");
                var adrenaline = player.GetAdrenaline();
                BlockFrom(player, troll.Character, 30f, true);
                var parryLeft = HitData.DamageTypes.ApplyArmor(30f, axePower * _blockSkill * axe.m_shared.m_timedBlockBonus);
                c.Check(Mathf.Abs(_blockLeft - parryLeft) < 0.01f,
                    $"parry of a Troll's 30 blunt with the axe: {F2(parryLeft)} gets through, got {F2(_blockLeft)}");
                yield return WaitStagger(troll.Character, 1.5f);
                SelfTest.Note(ParryName, $"Troll staggers when parried (m_staggerWhenBlocked) = {troll.Character.m_staggerWhenBlocked}; "
                                         + $"adrenaline {F2(adrenaline)} -> {F2(player.GetAdrenaline())} (max {F2(player.GetMaxAdrenaline())}, "
                                         + $"parry gives {F2(axe.m_shared.m_perfectBlockAdrenaline)})");
                c.Check(_staggered && troll.Character.m_staggerWhenBlocked,
                    $"a timed block (parry) with the off-hand axe staggers the Troll (seen staggering {_staggered}; the "
                    + $"creature staggers when parried {troll.Character.m_staggerWhenBlocked})");
            }

            // T34: two KnifeBlackMetal against a creature's 70 blunt: "Blocked" shows the pair's block power (about
            // 18), one knife (alone, or in the off hand of a sword) 2; a parry blocks most of it and staggers the
            // creature, one knife parries 8; the pair's knife wears far slower than a lone knife.
            var greyling = bench.Spawn("Greyling", 1.5f);
            c.Check(greyling != null && greyling.Character != null, "Greyling spawned in front of the player");
            var attacker = greyling != null ? greyling.Character : null;
            var shared = template.m_shared;
            var pairPower = shared.m_blockPower * KnifeBlock.PhysicalDamage(black.m_shared) / KnifeBlock.PhysicalDamage(shared);
            var knifePower = black.GetBaseBlockPower();
            var knifeBonus = black.m_shared.m_timedBlockBonus;
            const float Blunt = 70f;
            bench.Pair(black, black2);
            yield return null;
            c.Check(Holds(player, black, black2), $"two Black Metal knives paired ({HandsText(player)})");
            BlockFrom(player, attacker, Blunt, false);
            var pairWear = _blockWear;
            var pairBlocked = Blunt - _blockLeft;
            var pairText = BlockedText();
            c.Check(Mathf.Abs(pairBlocked - pairPower * _blockSkill) < 0.05f && Mathf.Abs(pairPower - 24f * 68f / 90f) < 0.01f,
                $"two knives block {F2(pairPower)} of a 70 blunt hit (about 18 in the 1.0.16 data), got {F2(pairBlocked)}");
            c.Check(pairText != null && pairText.EndsWith(": " + Number(pairBlocked), StringComparison.Ordinal),
                $"the game shows the \"Blocked\" number {Number(pairBlocked)} (text '{pairText}')");
            c.Check(!player.IsStaggering(), "the player is not staggered by the blocked hit at 265 max health");
            BlockFrom(player, attacker, Blunt, true);
            var parryBlocked = Blunt - _blockLeft;
            var parryExpected = Blunt - HitData.DamageTypes.ApplyArmor(Blunt, pairPower * _blockSkill * shared.m_timedBlockBonus);
            c.Check(Mathf.Abs(parryBlocked - parryExpected) < 0.05f && parryBlocked > Blunt * 0.5f,
                $"two knives parry most of a 70 blunt hit ({F2(parryExpected)} blocked expected), got {F2(parryBlocked)}");
            if (attacker != null)
            {
                yield return WaitStagger(attacker, 1.5f);
                c.Check(_staggered && attacker.m_staggerWhenBlocked,
                    $"a parry with two knives staggers the attacker (seen staggering {_staggered}; the creature staggers "
                    + $"when parried {attacker.m_staggerWhenBlocked})");
            }
            bench.Empty();
            player.EquipItem(black);
            yield return null;
            BlockFrom(player, attacker, Blunt, false);
            var loneWear = _blockWear;
            var loneBlocked = Blunt - _blockLeft;
            var loneText = BlockedText();
            c.Check(Mathf.Abs(loneBlocked - knifePower * _blockSkill) < 0.05f && Approx(knifePower, 2f),
                $"one Black Metal knife alone blocks {F2(knifePower)} (2 in the 1.0.16 data), got {F2(loneBlocked)}");
            c.Check(loneText != null && loneText.EndsWith(": " + Number(loneBlocked), StringComparison.Ordinal),
                $"the game shows the \"Blocked\" number {Number(loneBlocked)} for one knife (text '{loneText}')");
            BlockFrom(player, attacker, Blunt, true);
            var loneParry = Blunt - _blockLeft;
            c.Check(Mathf.Abs(loneParry - knifePower * _blockSkill * knifeBonus) < 0.05f && Approx(knifePower * knifeBonus, 8f),
                $"one knife alone parries {F2(knifePower * knifeBonus)} (8 in the 1.0.16 data), got {F2(loneParry)}");
            c.Check(pairWear > 0f && loneWear > pairWear * 4f,
                $"the pair's off-hand knife wears far slower than a lone knife per blocked hit ({F2(pairWear)} against {F2(loneWear)})");
            bench.Pair(sword, black);
            yield return null;
            BlockFrom(player, attacker, Blunt, false);
            c.Check(Holds(player, sword, black) && Mathf.Abs(Blunt - _blockLeft - knifePower * _blockSkill) < 0.05f,
                $"a knife in the off hand of a sword blocks like one knife ({F2(knifePower)}), got {F2(Blunt - _blockLeft)}");
            var proto = PrefabItem(KnifeBlack);
            c.Check(proto != null && new[] { black, black2 }.All(k => Approx(k.m_shared.m_blockPower, proto.m_shared.m_blockPower)
                                                                      && Approx(k.m_shared.m_blockPowerPerLevel, proto.m_shared.m_blockPowerPerLevel)
                                                                      && Approx(k.m_shared.m_timedBlockBonus, proto.m_shared.m_timedBlockBonus)),
                "both knives have the game's own block values again after the calls");
            c.Report();
        }
        finally
        {
            player.m_staggerDamage = staggerBefore;
            PutFoodsBack(player, foods, healthBefore);
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

    // ---------- dual.wounded ----------

    // What vanilla Attack.ModifyDamage multiplies a hit by for the attacker's missing health, with this weapon's
    // combo attack.
    private static float WoundedFactor(Player p, ItemDrop.ItemData weapon)
    {
        var attack = weapon.m_shared.m_attack;
        var factor = 1f;
        if (attack.m_damageMultiplierPerMissingHP > 0f)
        {
            factor *= 1f + (p.GetMaxHealth() - p.GetHealth()) * attack.m_damageMultiplierPerMissingHP;
        }
        if (attack.m_damageMultiplierByTotalHealthMissing > 0f)
        {
            factor *= 1f + (1f - p.GetHealthPercentage()) * attack.m_damageMultiplierByTotalHealthMissing;
        }
        return factor;
    }

    // Off-hand hit / main-hand hit of the third swing (dualaxes2) of the combo from 'from', without the roll and the
    // split. -1 = a hit missing.
    private static float ThirdSwingRatio(int from)
    {
        var swing = SwingIndex(from, "dualaxes2");
        return BaseRatio(DamageOf(swing, 1, Hand.Off), DamageOf(swing, 0, Hand.Main));
    }

    private static IEnumerator RunWounded()
    {
        var c = new Checks(WoundedName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(WoundedName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        SkillSave skills = null;
        List<Player.Food> foods = null;
        var healthBefore = player.GetHealth();
        try
        {
            bench = new Bench(player);
            skills = new SkillSave(player, Skills.SkillType.Swords);
            foods = new List<Player.Food>(player.m_foods);
            var plain = bench.Give(Niedhogg);
            var blood = bench.Give(NiedhoggBlood);
            var lightning = bench.Give(NiedhoggLightning);
            if (plain == null || blood == null || lightning == null)
            {
                c.Check(false, "could not give the Niedhogg swords");
                c.Report();
                yield break;
            }
            var dummy = bench.Spawn(TrollName, DummyGap);
            c.Check(dummy != null && dummy.Character != null, "Troll spawned in front of the player");
            NoteDummy(WoundedName, dummy);
            // Skill 100: the roll of each hit is recorded and taken out. Three foods: a health bar with room to miss.
            SkillSave.SetLevel(player, Skills.SkillType.Swords, 100f);
            ThreeFoods(player);
            var max = player.GetMaxHealth();
            var low = max * 0.2f;
            Action<DualSwing.HitRecord> stayLow = _ => player.SetHealth(low);
            bench.Pair(plain, blood);
            yield return new WaitForSeconds(0.5f);
            c.Check(Holds(player, plain, blood), $"SwordNiedhogg + SwordNiedhoggBlood paired ({HandsText(player)})");
            DualSwing.ResetRecords();
            DualSwing.Recording = true;

            // Full health: the reference ratio of the two swords.
            player.SetHealth(max);
            var from = DualSwing.Swings.Count;
            yield return Swing(player, dummy, 4, false, onHit: _ => player.SetHealth(max));
            var full = ThirdSwingRatio(from);
            // A fifth of the health: the Blood sword in the off hand hits harder.
            player.SetHealth(low);
            var bonusBlood = WoundedFactor(player, blood);
            var bonusPlain = WoundedFactor(player, plain);
            yield return new WaitForSeconds(0.3f);
            player.SetHealth(low);
            from = DualSwing.Swings.Count;
            yield return Swing(player, dummy, 4, false, onHit: stayLow);
            var wounded = ThirdSwingRatio(from);
            SelfTest.Note(WoundedName, $"max health {F2(max)}, wounded at {F2(low)}: low-health factor SwordNiedhoggBlood "
                                       + $"{F2(bonusBlood)}, SwordNiedhogg {F2(bonusPlain)}; off-hand / main-hand hit of the "
                                       + $"third swing {F2(full)} at full health, {F2(wounded)} wounded");
            c.Check(full > 0f && wounded > 0f && bonusBlood > bonusPlain * 1.02f,
                "both hits of the third swing recorded at full and at low health, and the Blood sword has a low-health "
                + $"bonus the plain one lacks (ratios {F2(full)} and {F2(wounded)}, factors {F2(bonusBlood)} / {F2(bonusPlain)})");
            if (full > 0f && wounded > 0f)
            {
                var expected = full * bonusBlood / bonusPlain;
                c.Check(Mathf.Abs(wounded / expected - 1f) < 0.05f && wounded > 1f,
                    "wounded, Blood sword in the off hand: its hit is larger than the main-hand hit of the same swing, by "
                    + $"its low-health bonus ({F2(expected)} expected, got {F2(wounded)})");
            }
            // Swapped: now the main-hand hits are the larger ones.
            player.SetHealth(max);
            yield return SwapHands(player, dummy);
            player.SetHealth(low);
            yield return new WaitForSeconds(0.3f);
            player.SetHealth(low);
            from = DualSwing.Swings.Count;
            yield return Swing(player, dummy, 4, false, onHit: stayLow);
            var swapped = ThirdSwingRatio(from);
            c.Check(_swapQueued && _swapDone && Holds(player, blood, plain), $"hands swapped ({HandsText(player)})");
            if (full > 0f && swapped > 0f)
            {
                var expected = bonusPlain / (full * bonusBlood);
                c.Check(Mathf.Abs(swapped / expected - 1f) < 0.05f && swapped < 1f,
                    "wounded, Blood sword in the main hand: now the main-hand hit is the larger one "
                    + $"({F2(expected)} expected for off-hand / main-hand, got {F2(swapped)})");
            }
            else
            {
                c.Check(false, $"both hits of the third swing recorded after the swap (ratio {F2(swapped)})");
            }
            player.SetHealth(max);

            // Lightning sword in the off hand: its strike comes on off-hand hits only. The chance is the weapon's own
            // random roll: forced to 1 on this copy, so every off-hand hit must strike and no main-hand hit may.
            // The strike belongs to the combo attack, or (when the combo has none) to the special: there both hands
            // strike in one event, and the plain sword's half spawns nothing, so every strike is the off hand's.
            var comboAttack = lightning.m_shared.m_attack;
            var specialAttack = lightning.m_shared.m_secondaryAttack;
            var bySpecial = comboAttack.m_spawnOnHit == null && specialAttack != null && specialAttack.m_spawnOnHit != null;
            var source = bySpecial ? specialAttack : comboAttack;
            var plainSource = bySpecial ? plain.m_shared.m_secondaryAttack : plain.m_shared.m_attack;
            var strike = source.m_spawnOnHit;
            var countable = strike != null && (plainSource == null || plainSource.m_spawnOnHit == null);
            c.Check(countable, "SwordNiedhoggLightning spawns a strike on hit and SwordNiedhogg does not (lightning sword: "
                               + $"{(strike != null ? strike.name : "nothing")}, plain sword: "
                               + $"{(plainSource != null && plainSource.m_spawnOnHit != null ? plainSource.m_spawnOnHit.name : "nothing")})");
            if (countable)
            {
                var chance = source.m_spawnOnHitChance;
                source.m_spawnOnHitChance = 1f;
                bench.Pair(plain, lightning);
                yield return new WaitForSeconds(0.5f);
                var seen = new HashSet<int>();
                CountNew(strike.name, seen);
                var onMain = 0;
                var onOff = 0;
                var offHits = 0;
                Action<DualSwing.HitRecord> count = hit =>
                {
                    if (hit.Hand == Hand.Off)
                    {
                        onOff += CountNew(strike.name, seen);
                        offHits += DamageOf(hit.Swing, hit.Event, Hand.Off) != null ? 1 : 0;
                    }
                    else if (!bySpecial)
                    {
                        // Combo: the main-hand hit is a swing of its own, seen before the off-hand one.
                        onMain += CountNew(strike.name, seen);
                    }
                };
                from = DualSwing.Swings.Count;
                yield return Swing(player, dummy, bySpecial ? 1 : 2, bySpecial, onHit: count);
                source.m_spawnOnHitChance = chance;
                if (bySpecial)
                {
                    CheckSwings(c, from, "SwordNiedhogg + SwordNiedhoggLightning, special", "dualaxes_secondary:0M,0O");
                }
                else
                {
                    CheckSwings(c, from, "SwordNiedhogg + SwordNiedhoggLightning", "dualaxes0:0M", "dualaxes1:0O");
                }
                SelfTest.Note(WoundedName, $"{strike.name} ({(bySpecial ? "special" : "combo")} attack, chance {F2(chance)} on the "
                                           + $"weapon, 1 for the test): {onMain} after main-hand hits, {onOff} after {offHits} "
                                           + "off-hand hit(s) that reached the Troll");
                c.Check(onMain == 0 && offHits > 0 && onOff >= offHits,
                    $"lightning strikes with off-hand hits only ({onOff} strike(s) for {offHits} off-hand hit(s), {onMain} with main-hand hits)");
                DestroyNamed(strike.name);
            }
            yield return NoteIfStuck(WoundedName, player, "the low-health and lightning steps");
            c.Report();
        }
        finally
        {
            DualSwing.Recording = false;
            DualSwing.ResetRecords();
            PutFoodsBack(player, foods, healthBefore);
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

    // Objects made from prefab 'name' not seen before ('seen' remembers them). A spawned prefab is a root object of
    // the scene (Instantiate without a parent), networked or not.
    private static int CountNew(string name, HashSet<int> seen)
    {
        var count = 0;
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root != null && root.name.StartsWith(name, StringComparison.Ordinal) && seen.Add(root.GetInstanceID()))
            {
                count++;
            }
        }
        return count;
    }

    // Leftover objects of a test (lightning strikes): gone.
    private static void DestroyNamed(string name)
    {
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root == null || !root.name.StartsWith(name, StringComparison.Ordinal))
            {
                continue;
            }
            var view = root.GetComponent<ZNetView>();
            if (view != null && view.IsValid() && ZNetScene.instance != null)
            {
                ZNetScene.instance.Destroy(root);
            }
            else
            {
                UnityEngine.Object.Destroy(root);
            }
        }
    }
}
#endif
