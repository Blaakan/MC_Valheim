#if DEBUG
using System;
using System.Collections;
using System.Linq;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.TrinketsOnDemandMod;

// Debug build only. In-world self tests of ranged pay with real shots (same class as SelfTests.cs). Player
// aims up and me takes each projectile in flight, then lands it on creature standing to side with
// game's own hit code (Projectile.OnHit): no aiming luck, and bar gain of hit is read at once.
//   trinkets.bow-hits        T17: Bow + ArrowWood at Bows 0 (full draw x2, hit pays 4, also 3 s after another shot;
//                            quick tap x1, 2), Bows 50 (x1.33) and Bows 100 (x1), Debug line of each shot; full bar
//                            held on arrow hit
//   trinkets.crossbow-shot   T18: CrossbowArbalest + BoltBone at Crossbows 0 (cycle 4 s, x2.67, 5.33) and 100 (2.25 s,
//                            x1.5, 3); X03: with Crossbow Stays Loaded, second loaded crossbow fired right after
//   trinkets.other-ranged    T19: spear throw, bomb throw, staff cast: no ranged line, projectile pays game's
//                            amount; X04: harpoon on tamed Boar with Harpoon Hooks Tames
internal static partial class SelfTests
{
    private const string BowHitsName = "trinkets.bow-hits";
    private const string CrossbowShotName = "trinkets.crossbow-shot";
    private const string OtherRangedName = "trinkets.other-ranged";
    private const string ShotLineStart = "Ranged shot (";

    // What one driven shot left behind.
    private sealed class ShotResult
    {
        internal bool Fired;          // projectile left weapon
        internal bool Counted;        // RangedBonus saw new bow or crossbow shot
        internal float FiredAt;
        internal RangedBonus.Shot Shot;
        internal Projectile Projectile;
        internal int Lines;           // "Ranged shot" Debug lines of this shot
        internal string Line;
    }

    private struct Landing
    {
        internal float Gained;
        internal float Expected;
        internal int Calls;
        internal float Amount;
    }

    private static void Collect(Rig rig, ItemDrop.ItemData weapon, int serial, int mark, ShotResult result)
    {
        var go = weapon.m_lastProjectile;
        rig.Track(go);
        result.Projectile = go != null ? go.GetComponent<Projectile>() : null;
        result.Fired = result.Projectile != null;
        result.Counted = RangedBonus.LastShot.Serial != serial;
        result.FiredAt = Time.time;
        result.Shot = RangedBonus.LastShot;
        var lines = LogLines(mark, ShotLineStart);
        result.Lines = lines.Count;
        result.Line = lines.Count > 0 ? lines[lines.Count - 1] : null;
    }

    private static void ClearShot(ShotResult result)
    {
        result.Fired = false;
        result.Counted = false;
        result.Projectile = null;
        result.Lines = 0;
        result.Line = null;
    }

    // Bow: attack button held until draw full (or for three physics ticks: quick tap), then let go.
    private static IEnumerator BowShot(Rig rig, ItemDrop.ItemData bow, bool fullDraw, ShotResult result)
    {
        var p = rig.P;
        ClearShot(result);
        rig.Drive(false);
        yield return Until(() => Calm(p) && p.GetAttackDrawPercentage() <= 0f, 4f);
        yield return FixedTicks(2);
        var serial = RangedBonus.LastShot.Serial;
        var mark = LogMark();
        bow.m_lastProjectile = null;
        var t = 0f;
        if (fullDraw)
        {
            while (p.GetAttackDrawPercentage() < 1f && t < 6f)
            {
                rig.Drive(true);
                t += Time.deltaTime;
                yield return null;
            }
        }
        else
        {
            rig.Drive(true);
            yield return FixedTicks(3);
        }
        t = 0f;
        while (RangedBonus.LastShot.Serial == serial && t < 4f)
        {
            rig.Drive(false);
            t += Time.deltaTime;
            yield return null;
        }
        Collect(rig, bow, serial, mark, result);
    }

    // Crossbow: wait for game's own reload, then one attack.
    private static IEnumerator CrossbowShot(Rig rig, ItemDrop.ItemData crossbow, ShotResult result, float loadTimeout)
    {
        var p = rig.P;
        ClearShot(result);
        yield return Until(() => ReferenceEquals(p.m_weaponLoaded, crossbow) && Calm(p), loadTimeout);
        if (!ReferenceEquals(p.m_weaponLoaded, crossbow))
        {
            yield break;
        }
        var serial = RangedBonus.LastShot.Serial;
        var mark = LogMark();
        crossbow.m_lastProjectile = null;
        var started = false;
        var t = 0f;
        while (!started && t < 2f)
        {
            started = p.StartAttack(null, false);
            if (!started)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }
        if (!started)
        {
            yield break;
        }
        yield return Until(() => RangedBonus.LastShot.Serial != serial || crossbow.m_lastProjectile != null, 3f);
        Collect(rig, crossbow, serial, mark, result);
    }

    // Any other weapon that throws or casts: one attack, wait for its projectile. keep = called every frame until
    // projectile is out (staff: test character has no eitr of its own).
    private static IEnumerator ThrowShot(Rig rig, ItemDrop.ItemData weapon, bool secondary, ShotResult result, Action keep)
    {
        var p = rig.P;
        ClearShot(result);
        yield return Until(() => Calm(p), 4f);
        var serial = RangedBonus.LastShot.Serial;
        var mark = LogMark();
        weapon.m_lastProjectile = null;
        var started = false;
        var t = 0f;
        while (!started && t < 1.5f)
        {
            if (keep != null)
            {
                keep();
            }
            started = p.StartAttack(null, secondary);
            if (!started)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }
        t = 0f;
        while (started && weapon.m_lastProjectile == null && t < 4f)
        {
            if (keep != null)
            {
                keep();
            }
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        Collect(rig, weapon, serial, mark, result);
    }

    // Projectile lands on creature (game's hit code): what bar, emptied first, gained.
    private static Landing Land(Rig rig, Character foe, Projectile projectile)
    {
        var p = rig.P;
        foe.SetHealth(foe.GetMaxHealth());
        p.m_adrenaline = 0f;
        var landing = new Landing { Expected = Gain(p, projectile.m_adrenaline) };
        var calls = FullBar.Calls;
        FullBar.LastAmount = float.NaN;
        projectile.OnHit(ColliderOf(foe), foe.GetCenterPoint(), false, Vector3.zero);
        landing.Gained = p.m_adrenaline;
        landing.Calls = FullBar.Calls - calls;
        landing.Amount = FullBar.LastAmount;
        if (projectile != null)
        {
            DestroyObject(projectile.gameObject);
        }
        return landing;
    }

    // One bow or crossbow shot against numbers TESTING.md gives: cycle, factor, Debug line, what projectile
    // carries, what its hit adds to bar.
    private static void CheckShot(Checks c, Rig rig, TrinketRules rules, Attack attack, Character foe, ShotResult shot,
        float basePay, string what, float wantCycle, float wantFactor, bool firstShot)
    {
        if (!c.Check(shot.Fired && shot.Counted, $"{what}: no shot within the wait (projectile {shot.Fired}, counted {shot.Counted})"))
        {
            return;
        }
        var s = shot.Shot;
        if (wantCycle > 0f)
        {
            c.Check(Near(s.Nominal, wantCycle, 0.03f), $"{what}: cycle {F(s.Nominal)} s, expected {F(wantCycle)} s");
        }
        var formula = RangedBonus.FactorFor(s.Nominal, s.SinceLast, rules.RangedReferenceSeconds, rules.RangedMaxMultiplier,
            attack.m_projectiles, attack.m_projectileBursts);
        c.Check(Near(s.Factor, wantFactor, 0.01f) && Near(s.Factor, formula, 0.0005f),
            $"{what}: factor x{F(s.Factor)}, expected x{F(wantFactor)} (formula x{F(formula)})");
        c.Check(float.IsPositiveInfinity(s.SinceLast) == firstShot,
            $"{what}: seconds since the last shot {F(s.SinceLast)} (first shot expected: {firstShot})");
        c.Check(shot.Lines == 1 && shot.Line == ShotLine(s), $"{what}: Debug line '{shot.Line}' ({shot.Lines} line(s)), expected '{ShotLine(s)}'");
        var pay = shot.Projectile.m_adrenaline;
        c.Check(Near(pay, basePay * wantFactor, 0.02f), $"{what}: the projectile carries {F(pay)}, expected {F(basePay)} x {F(wantFactor)}");
        var landing = Land(rig, foe, shot.Projectile);
        c.Check(landing.Calls == 1 && Near(landing.Amount, pay) && Near(landing.Gained, landing.Expected, 0.001f),
            $"{what}: the hit made {landing.Calls} adrenaline call(s) of {F(landing.Amount)} and added {F(landing.Gained)} (expected one of {F(pay)}, adding {F(landing.Expected)})");
        c.Check(Near(landing.Gained, basePay * wantFactor, 0.02f),
            $"{what}: the hit raised the bar by {F(landing.Gained)}, expected {F(basePay * wantFactor)} (default world modifiers)");
    }

    private static Projectile PrefabProjectile(Attack attack)
    {
        return attack != null && attack.m_attackProjectile != null ? attack.m_attackProjectile.GetComponent<Projectile>() : null;
    }

    // View up: projectile in flight meets nothing before me takes it (Rig puts view back).
    private static void AimUp(Rig rig)
    {
        rig.Moves();
        rig.P.m_lookPitch = -30f;
        rig.P.SetMouseLook(Vector2.zero);
    }

    // ---------- trinkets.bow-hits (T17) ----------

    // Also run by multiplayer test with server's rules. Bow in hand, arrows in bag, foe to side.
    private static IEnumerator BowHitsCore(Checks c, Rig rig, TrinketRules rules, ItemDrop.ItemData bow, ItemDrop.ItemData arrows, Character foe)
    {
        var p = rig.P;
        var attack = bow.m_shared.m_attack;
        var arrow = PrefabProjectile(arrows.m_shared.m_attack);
        if (!c.Check(arrow != null, $"{ArrowName} has no projectile"))
        {
            yield break;
        }
        var basePay = arrow.m_adrenaline;
        var draw = attack.m_drawDurationMin;
        c.Check(Near(draw, 2.5f) && Near(basePay, 2f) && Near(rules.RangedReferenceSeconds, 1.5f) && Near(rules.RangedMaxMultiplier, 4f),
            $"{BowName} full draw {F(draw)} s at Bows 0, {ArrowName} pays {F(basePay)}, rules {F(rules.RangedReferenceSeconds)} s / x{F(rules.RangedMaxMultiplier)} "
            + "(the numbers of TESTING.md need 2.5 s, 2, 1.5 s and x4)");
        var shot = new ShotResult();

        // (a) Bows 0, full draw: cycle 3 s, x2, hit pays 4.
        rig.Skill(Skills.SkillType.Bows, 0f);
        RangedBonus.Reset();
        yield return BowShot(rig, bow, true, shot);
        CheckShot(c, rig, rules, attack, foe, shot, basePay, "Bows 0, full draw", 3f, 2f, true);
        var firstAt = shot.FiredAt;

        // (a) Again, 3 s or more after that shot: line gives seconds, still x2.
        rig.Skill(Skills.SkillType.Bows, 0f);
        yield return Until(() => Time.time - firstAt >= 1f, 2f);
        yield return BowShot(rig, bow, true, shot);
        c.Check(shot.Counted && shot.Shot.SinceLast >= 3f && shot.Line != null && shot.Line.Contains(" s since the last shot"),
            $"second full draw: {F(shot.Shot.SinceLast)} s since the last shot, line '{shot.Line}'");
        CheckShot(c, rig, rules, attack, foe, shot, basePay, "Bows 0, full draw, 3 s or more after a shot", 3f, 2f, false);

        // (b) Quick tap: x1, hit pays 2.
        rig.Skill(Skills.SkillType.Bows, 0f);
        yield return BowShot(rig, bow, false, shot);
        c.Check(shot.Counted && shot.Shot.DrawPercentage < 0.3f, $"quick tap: the bow was drawn to {F(shot.Shot.DrawPercentage)}");
        CheckShot(c, rig, rules, attack, foe, shot, basePay, "Bows 0, quick tap", -1f, 1f, false);

        // (c) Bows 50: cycle 2 s, x1.33 (2.67 per hit). Bows 100: cycle 1 s, x1 (2 per hit).
        rig.Skill(Skills.SkillType.Bows, 50f);
        RangedBonus.Reset();
        yield return BowShot(rig, bow, true, shot);
        CheckShot(c, rig, rules, attack, foe, shot, basePay, "Bows 50, full draw", 2f, 4f / 3f, true);
        rig.Skill(Skills.SkillType.Bows, 100f);
        RangedBonus.Reset();
        yield return BowShot(rig, bow, true, shot);
        CheckShot(c, rig, rules, attack, foe, shot, basePay, "Bows 100, full draw", 1f, 1f, true);
        c.Check(Near(arrow.m_adrenaline, basePay), $"the {ArrowName} prefab now pays {F(arrow.m_adrenaline)} (it must stay {F(basePay)})");
    }

    private static IEnumerator RunBowHits()
    {
        var rig = Rig.Create(BowHitsName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(BowHitsName);
        var mark = LogMark();
        try
        {
            var p = rig.P;
            var rules = new TrinketRules();
            ServerRules.TestRules = rules;
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            var foe = rig.Tough(FoeName, Flat(p.transform.right) * 5f);
            var bow = rig.Give(BowName);
            var arrows = rig.Give(ArrowName, 20);
            if (!c.Check(item != null && foe != null && bow != null && arrows != null,
                    $"could not equip a trinket, give {BowName} and {ArrowName} or spawn {FoeName}"))
            {
                c.Report();
                yield break;
            }
            var hash = item.m_shared.m_fullAdrenalineSE.NameHash();
            rig.Effect(hash);
            rig.EmptyHands();
            c.Check(p.EquipItem(bow, false), $"could not equip {BowName}");
            rig.TakeControls();
            AimUp(rig);
            yield return new WaitForSeconds(0.5f);
            CombatState.Reset();
            var begun = Time.time;

            yield return BowHitsCore(c, rig, rules, bow, arrows, foe);
            c.Check(CombatState.LastExchange >= begun, "arrow hits on a creature did not start a fight");

            // T08: arrow hit on full bar held.
            var max = p.GetMaxAdrenaline();
            var shot = new ShotResult();
            rig.Skill(Skills.SkillType.Bows, 0f);
            yield return BowShot(rig, bow, true, shot);
            if (c.Check(shot.Fired, "no arrow for the full-bar check"))
            {
                rig.Effect(hash);
                p.m_adrenaline = max;
                var calls = FullBar.Calls;
                shot.Projectile.OnHit(ColliderOf(foe), foe.GetCenterPoint(), false, Vector3.zero);
                c.Check(FullBar.Calls > calls && Near(p.m_adrenaline, max) && !rig.Has(hash),
                    $"full bar + an arrow hit: bar {F(p.m_adrenaline)} of {F(max)}, effect {rig.Has(hash)}");
                if (shot.Projectile != null)
                {
                    DestroyObject(shot.Projectile.gameObject);
                }
            }
            CheckCleanLog(c, mark);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.crossbow-shot (T18, X03) ----------

    private static IEnumerator RunCrossbowShot()
    {
        var rig = Rig.Create(CrossbowShotName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(CrossbowShotName);
        var mark = LogMark();
        try
        {
            var p = rig.P;
            var rules = new TrinketRules();
            ServerRules.TestRules = rules;
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            var foe = rig.Tough(FoeName, Flat(p.transform.right) * 5f);
            rig.Skill(Skills.SkillType.Crossbows, 0f);
            var crossbow = rig.Give(CrossbowName);
            var second = rig.Give(CrossbowName);
            var bolts = rig.Give(BoltName, 20);
            if (!c.Check(item != null && foe != null && crossbow != null && second != null && bolts != null,
                    $"could not equip a trinket, give two {CrossbowName} and {BoltName} or spawn {FoeName}"))
            {
                c.Report();
                yield break;
            }
            rig.Effect(item.m_shared.m_fullAdrenalineSE.NameHash());
            var attack = crossbow.m_shared.m_attack;
            var bolt = PrefabProjectile(bolts.m_shared.m_attack);
            if (!c.Check(bolt != null, $"{BoltName} has no projectile"))
            {
                c.Report();
                yield break;
            }
            var basePay = bolt.m_adrenaline;
            c.Check(Near(attack.m_reloadTime, 3.5f) && Near(basePay, 2f) && attack.m_requiresReload,
                $"{CrossbowName} reload {F(attack.m_reloadTime)} s at Crossbows 0, {BoltName} pays {F(basePay)} (the numbers of TESTING.md need 3.5 s and 2)");
            rig.EmptyHands();
            c.Check(p.EquipItem(crossbow, false), $"could not equip {CrossbowName}");
            rig.TakeControls();
            AimUp(rig);
            var shot = new ShotResult();

            // Crossbows 0: cycle 4 s, x2.67, hit pays about 5.3.
            yield return Until(() => ReferenceEquals(p.m_weaponLoaded, crossbow), 8f);
            RangedBonus.Reset();
            rig.Skill(Skills.SkillType.Crossbows, 0f);
            yield return CrossbowShot(rig, crossbow, shot, 2f);
            CheckShot(c, rig, rules, attack, foe, shot, basePay, "Crossbows 0", 4f, 8f / 3f, true);

            // Crossbows 100: cycle 2.25 s, x1.5, hit pays 3.
            rig.Skill(Skills.SkillType.Crossbows, 100f);
            yield return Until(() => ReferenceEquals(p.m_weaponLoaded, crossbow), 8f);
            RangedBonus.Reset();
            rig.Skill(Skills.SkillType.Crossbows, 100f);
            yield return CrossbowShot(rig, crossbow, shot, 2f);
            CheckShot(c, rig, rules, attack, foe, shot, basePay, "Crossbows 100", 2.25f, 1.5f, true);
            c.Check(Near(bolt.m_adrenaline, basePay), $"the {BoltName} prefab now pays {F(bolt.m_adrenaline)} (it must stay {F(basePay)})");

            // X03 (Crossbow Stays Loaded): two loaded crossbows, fire one, swap, fire other at once: second
            // shot's cycle is time since first, so about x1, not x2.67.
            if (c.Check(ModActive(StaysLoadedGuid), "Crossbow Stays Loaded is not loaded and active: the two-crossbow check was not run"))
            {
                rig.Skill(Skills.SkillType.Crossbows, 0f);
                yield return Until(() => Calm(p), 3f);
                p.UnequipItem(crossbow, false);
                c.Check(p.EquipItem(second, false), "could not equip the second crossbow");
                yield return Until(() => ReferenceEquals(p.m_weaponLoaded, second) && Calm(p), 8f);
                var secondLoaded = ReferenceEquals(p.m_weaponLoaded, second);
                p.UnequipItem(second, false);
                p.EquipItem(crossbow, false);
                yield return Until(() => ReferenceEquals(p.m_weaponLoaded, crossbow) && Calm(p), 8f);
                c.Check(secondLoaded && ReferenceEquals(p.m_weaponLoaded, crossbow), "could not load both crossbows");
                RangedBonus.Reset();
                rig.Skill(Skills.SkillType.Crossbows, 0f);
                yield return CrossbowShot(rig, crossbow, shot, 2f);
                var first = shot.Shot;
                var firstCounted = shot.Counted;
                if (shot.Projectile != null)
                {
                    DestroyObject(shot.Projectile.gameObject);
                }
                // Weapon cannot be changed inside shot's animation; right after it, swap (not "calm":
                // game starts reloading fired crossbow at once, swap drops that reload).
                yield return Until(() => !p.InAttack(), 3f);
                p.UnequipItem(crossbow, false);
                var swapped = p.EquipItem(second, false);
                yield return CrossbowShot(rig, second, shot, 3f);
                c.Check(firstCounted && Near(first.Factor, 8f / 3f, 0.01f), $"first crossbow: factor x{F(first.Factor)}, expected x2.67");
                if (c.Check(swapped && shot.Counted && shot.Fired, "the second crossbow did not fire within 3 s of the swap (it should still be loaded)"))
                {
                    var s = shot.Shot;
                    var formula = RangedBonus.FactorFor(s.Nominal, s.SinceLast, rules.RangedReferenceSeconds, rules.RangedMaxMultiplier,
                        attack.m_projectiles, attack.m_projectileBursts);
                    c.Check(s.SinceLast < 2f && Near(s.Factor, formula, 0.0005f) && s.Factor <= 1.34f,
                        $"second crossbow: {F(s.SinceLast)} s since the last shot, factor x{F(s.Factor)} (expected under 2 s and x1 or close, formula x{F(formula)})");
                    c.Check(shot.Lines == 1 && shot.Line == ShotLine(s) && shot.Line.Contains(" s since the last shot"),
                        $"second crossbow: Debug line '{shot.Line}', expected '{ShotLine(s)}'");
                    c.Check(Near(shot.Projectile.m_adrenaline, basePay * s.Factor, 0.001f),
                        $"second crossbow: the bolt carries {F(shot.Projectile.m_adrenaline)}, expected {F(basePay * s.Factor)}");
                    DestroyObject(shot.Projectile.gameObject);
                }
            }
            CheckCleanLog(c, mark);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.other-ranged (T19, X04) ----------

    // One throw or cast of weapon that no bow or crossbow: no ranged line, no shot counted, no context left
    // open, and projectile carries what its prefab carries.
    private static bool CheckPlainShot(Checks c, ShotResult shot, Projectile prefab, string what)
    {
        if (!c.Check(shot.Fired, $"{what}: no projectile within the wait"))
        {
            return false;
        }
        c.Check(shot.Lines == 0 && !shot.Counted && !RangedBonus.Open,
            $"{what}: {shot.Lines} 'Ranged shot' line(s), counted as a bow or crossbow shot {shot.Counted}");
        c.Check(prefab != null && Near(shot.Projectile.m_adrenaline, prefab.m_adrenaline),
            $"{what}: the projectile carries {F(shot.Projectile.m_adrenaline)}, its prefab {(prefab != null ? F(prefab.m_adrenaline) : "none")}");
        return true;
    }

    private static IEnumerator RunOtherRanged()
    {
        var rig = Rig.Create(OtherRangedName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(OtherRangedName);
        var mark = LogMark();
        try
        {
            var p = rig.P;
            ServerRules.TestRules = new TrinketRules();
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            var foe = rig.Tough(FoeName, Flat(p.transform.right) * 5f);
            var spear = rig.Give(SpearName);
            var bomb = rig.Give(BombName, 3);
            var staff = rig.Give(StaffName);
            if (!c.Check(item != null && foe != null && spear != null && bomb != null && staff != null,
                    $"could not equip a trinket, give {SpearName}, {BombName} and {StaffName} or spawn {FoeName}"))
            {
                c.Report();
                yield break;
            }
            rig.Effect(item.m_shared.m_fullAdrenalineSE.NameHash());
            rig.WatchDrops();
            rig.TakeControls();
            AimUp(rig);
            var shot = new ShotResult();

            // Staff cast (cast itself pays staff's own amount, as in normal game).
            var cast = staff.m_shared.m_attack;
            rig.EmptyHands();
            c.Check(p.EquipItem(staff, false), $"could not equip {StaffName}");
            yield return FixedTicks(3);
            CombatState.Reset();
            p.m_adrenaline = 0f;
            var wantCast = Gain(p, cast.m_attackUseAdrenaline);
            yield return ThrowShot(rig, staff, false, shot, () =>
            {
                p.m_maxEitr = 500f; // Rig puts both back
                p.m_eitr = 500f;
            });
            if (CheckPlainShot(c, shot, PrefabProjectile(cast), "staff cast"))
            {
                DestroyObject(shot.Projectile.gameObject); // before it lands: no fire in world
                c.Check(Near(p.m_adrenaline, wantCast), $"staff cast: the bar went to {F(p.m_adrenaline)}, the staff's own pay for a cast is {F(wantCast)}");
            }

            // Bomb throw.
            yield return Until(() => Calm(p), 4f);
            rig.EmptyHands();
            c.Check(p.EquipItem(bomb, false), $"could not equip {BombName}");
            yield return FixedTicks(3);
            CombatState.Reset();
            p.m_adrenaline = 0f;
            yield return ThrowShot(rig, bomb, false, shot, null);
            if (CheckPlainShot(c, shot, PrefabProjectile(bomb.m_shared.m_attack), "bomb throw"))
            {
                DestroyObject(shot.Projectile.gameObject); // before it lands: no ooze in world
                c.Check(Near(p.m_adrenaline, Gain(p, bomb.m_shared.m_attack.m_attackUseAdrenaline)),
                    $"bomb throw: the bar went to {F(p.m_adrenaline)} (the throw itself pays {F(bomb.m_shared.m_attack.m_attackUseAdrenaline)})");
            }

            // Spear throw, and its hit: game's amount.
            yield return Until(() => Calm(p), 4f);
            var throwAttack = spear.m_shared.m_secondaryAttack;
            var throwIsSecondary = throwAttack != null && throwAttack.m_attackProjectile != null;
            if (!throwIsSecondary)
            {
                throwAttack = spear.m_shared.m_attack;
            }
            rig.EmptyHands();
            c.Check(p.EquipItem(spear, false), $"could not equip {SpearName}");
            yield return FixedTicks(3);
            CombatState.Reset();
            yield return ThrowShot(rig, spear, throwIsSecondary, shot, null);
            var spearPrefab = PrefabProjectile(throwAttack);
            if (CheckPlainShot(c, shot, spearPrefab, "spear throw"))
            {
                var landing = Land(rig, foe, shot.Projectile);
                c.Check(landing.Calls == 1 && Near(landing.Amount, spearPrefab.m_adrenaline) && Near(landing.Gained, landing.Expected, 0.001f),
                    $"spear hit: {landing.Calls} adrenaline call(s) of {F(landing.Amount)}, bar +{F(landing.Gained)} (the game's amount is {F(spearPrefab.m_adrenaline)})");
            }

            // X04 (Harpoon Hooks Tames): hooking tamed Boar moves no bar, starts no fight, logs no ranged line.
            if (c.Check(ModActive(HarpoonGuid), "Harpoon Hooks Tames is not loaded and active: the harpoon check was not run"))
            {
                var harpoon = rig.Give(HarpoonName);
                var boar = rig.Tough(BoarName, -Flat(p.transform.right) * 5f);
                yield return Until(() => Calm(p), 4f);
                rig.EmptyHands();
                if (c.Check(harpoon != null && boar != null && p.EquipItem(harpoon, false), $"could not equip {HarpoonName} or spawn {BoarName}"))
                {
                    boar.SetTamed(true);
                    var hookAttack = harpoon.m_shared.m_attack;
                    var hookIsSecondary = hookAttack == null || hookAttack.m_attackProjectile == null;
                    yield return FixedTicks(3);
                    CombatState.Reset();
                    yield return new WaitForSeconds(1.2f);
                    var fightMark = LogMark();
                    yield return ThrowShot(rig, harpoon, hookIsSecondary, shot, null);
                    if (c.Check(shot.Fired, "harpoon: no projectile within the wait"))
                    {
                        c.Check(shot.Lines == 0 && !shot.Counted, $"harpoon: {shot.Lines} 'Ranged shot' line(s)");
                        p.m_adrenaline = 10f;
                        shot.Projectile.OnHit(ColliderOf(boar), boar.GetCenterPoint(), false, Vector3.zero);
                        var hooked = boar.GetSEMan().GetStatusEffects().Any(se => se is SE_Harpooned);
                        c.Check(boar.IsTamed() && hooked, "harpoon: the tamed Boar was not hooked (Harpoon Hooks Tames is active)");
                        c.Check(Near(p.m_adrenaline, 10f) && float.IsNegativeInfinity(CombatState.LastExchange),
                            $"harpoon on a tamed Boar: bar 10 -> {F(p.m_adrenaline)}, fight started {!float.IsNegativeInfinity(CombatState.LastExchange)}");
                        yield return new WaitForSeconds(1.3f);
                        c.Check(Near(p.m_adrenaline, 10f) && LogCount(fightMark, FightOnLine) == 0,
                            $"harpoon on a tamed Boar: bar {F(p.m_adrenaline)} a second later (no income)");
                        boar.GetSEMan().RemoveAllStatusEffects(true);
                        if (shot.Projectile != null)
                        {
                            DestroyObject(shot.Projectile.gameObject);
                        }
                    }
                }
            }
            CheckCleanLog(c, mark);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }
}
#endif
