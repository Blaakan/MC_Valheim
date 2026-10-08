#if DEBUG
using System;
using System.Collections;
using System.Globalization;
using HarmonyLib;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Farming.HarpoonHooksTamesMod;

// Debug build only. Single-player self tests of the TESTING.md items T01-T24 and the single-player stand-ins of the
// multiplayer items. See SelfTests.cs for the list.
internal static partial class SelfTests
{
    // ---------- shared pieces ----------

    private static ItemDrop.ItemData ItemOf(string prefabName)
    {
        var prefab = ObjectDB.instance.GetItemPrefab(prefabName);
        var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
        if (drop == null)
        {
            return null;
        }
        var item = drop.m_itemData.Clone();
        item.m_dropPrefab = prefab;
        return item;
    }

    // Level and progress of a skill as text. Skill never used = level 0 (the game makes the entry at first use).
    private static string SkillState(Player p, Skills.SkillType type)
    {
        var level = 0f;
        var progress = 0f;
        if (p.GetSkills().m_skillData.TryGetValue(type, out var s))
        {
            level = s.m_level;
            progress = s.m_accumulator;
        }
        return $"level {F(level)} + {progress.ToString("0.####", CultureInfo.InvariantCulture)}";
    }

    // Deadly hit through Character.Damage, like the game send it: from a creature, from the player (with a weapon
    // skill), or from nobody (attacker null: drowning, fire, the killtame command).
    private static void Kill(Character victim, Character attacker, Skills.SkillType skill = Skills.SkillType.None)
    {
        if (victim == null)
        {
            return;
        }
        var hit = new HitData();
        hit.m_damage.m_damage = 1e6f;
        hit.m_point = victim.GetCenterPoint();
        hit.m_dir = Vector3.forward;
        hit.m_skill = skill;
        if (attacker != null)
        {
            hit.SetAttacker(attacker);
        }
        victim.Damage(hit);
    }

    // Creature get this target, like vanilla MonsterAI.SetTarget do (also when it already had one).
    private static void GiveTarget(MonsterAI ai, Character target)
    {
        ai.m_targetCreature = target;
        ai.m_targetStatic = null;
        ai.m_lastKnownTargetPos = target.transform.position;
        ai.m_beenAtLastPos = false;
    }

    // Fresh hook from the lane start on a creature put back `ahead` m in front. No checks: for the steps after.
    private static SE_Harpooned HookAt(Rig rig, Harpoon h, Character creature, float ahead)
    {
        Release(creature, h);
        rig.PlacePlayerAlong(0f);
        Place(creature, rig.At(ahead), -rig.Dir);
        DirectHit(rig, h, creature, false);
        return HookOn(creature, h);
    }

    // True when this mod has a patch on the game method right now.
    private static bool Patched(Type type, string method)
    {
        var original = AccessTools.Method(type, method);
        var info = original != null ? Harmony.GetPatchInfo(original) : null;
        return info != null && info.Owners.Contains(ModInfo.Guid);
    }

    private static bool AllPatched() =>
        Patched(typeof(Projectile), nameof(Projectile.IsValidTarget)) && Patched(typeof(Character), nameof(Character.Damage))
                                                                      && Patched(typeof(Character), nameof(Character.RPC_Damage));

    private static bool NonePatched() =>
        !Patched(typeof(Projectile), nameof(Projectile.IsValidTarget)) && !Patched(typeof(Character), nameof(Character.Damage))
                                                                       && !Patched(typeof(Character), nameof(Character.RPC_Damage));

    // Where a flying harpoon is aimed on a creature: its middle, but for a low creature (Neck) a bit higher, still
    // inside its body, so a small bump of the ground on the way does not stop the harpoon first.
    private static Vector3 AimPoint(Character c)
    {
        var point = c.GetCenterPoint();
        var collider = c.m_collider;
        if (collider != null)
        {
            point.y = Mathf.Max(point.y, Mathf.Min(collider.bounds.max.y - 0.12f, c.transform.position.y + 0.45f));
        }
        return point;
    }

    // Tame between the player and a wild creature, on one line. 1: the harpoon aimed at the wild one fly, the tame
    // catch it (hooked, no harm), the wild one get nothing. 2: control, thrower without the mod: same flight pass the
    // tame and hook the wild one (so the tame really stood in the way). 3 (angleShot): tame moved out of the way,
    // mod on: the wild one is hooked and hurt.
    private static IEnumerator LineOfFire(Rig rig, Checks c, Harpoon h, string tag, Character tame, Character wild,
        Vector3 tameSpot, Vector3 wildSpot, bool angleShot)
    {
        rig.PlacePlayerAlong(0f);
        var spot = tameSpot;
        Action pin = () =>
        {
            Place(tame, spot, rig.Dir);
            Place(wild, wildSpot, -rig.Dir);
        };
        yield return FixedFor(0.25f, pin);
        if (!c.Check(tame != null && wild != null, $"{tag}: a creature of the line-of-fire scene is gone"))
        {
            yield break;
        }
        // Launch at the height of the tame's middle, so the flight to the creature behind cross the tame's body.
        var height = Mathf.Clamp(tame.GetCenterPoint().y - tame.transform.position.y, 0.3f, 1.5f);
        var launch = Ground(rig.Origin + rig.Dir * 0.8f) + Vector3.up * height;

        var mark = Tap.Mark();
        var tameBefore = Snap.Take(tame);
        var wildBefore = Snap.Take(wild);
        ClearCenter();
        var flight = new Flight();
        yield return Fly(rig, h, launch, AimPoint(wild), pin, flight);
        CheckHarmlessHook(c, $"{tag} tame in the line of fire", rig, h, tame, tameBefore, flight.AsShot(HookOn(tame, h) != null));
        c.Check(HookOn(wild, h) == null && Near(wild.GetHealth(), wildBefore.Health) && ReferenceEquals(wild.m_lastHit, wildBefore.LastHit),
            $"{tag}: the creature behind the tame is not hooked and not hit ({Describe(wild, wildBefore)})");
        c.Check(Tap.CountSince(mark, "Harpoon passed tame") == 0, $"{tag}: no 'Harpoon passed tame' Debug line");
        // Its AI had time to think about the hit: still not after the player.
        yield return FixedFor(1.5f, pin);
        c.Check(tame != null && TargetOf(tame) != rig.P && ReferenceEquals(tame.m_lastHit, tameBefore.LastHit),
            $"{tag}: 1.5 s later the tame that caught the harpoon has taken no damaging hit and has not turned on the player (target {(tame != null && TargetOf(tame) != null ? TargetOf(tame).m_name : "none")})");
        Release(tame, h);
        yield return FixedFor(0.25f, pin);

        tameBefore = Snap.Take(tame);
        wildBefore = Snap.Take(wild);
        TestSwitches.ThrowerSideOff = true;
        try
        {
            yield return Fly(rig, h, launch, AimPoint(wild), pin, flight);
        }
        finally
        {
            TestSwitches.ThrowerSideOff = false;
        }
        c.Check(HookOn(tame, h) == null && Untouched(tame, tameBefore),
            $"{tag} control: thrown by a player without the mod, the same harpoon passes through the tame ({Describe(tame, tameBefore)})");
        c.Check(HookOn(wild, h) != null && wild.GetHealth() < wildBefore.Health - 0.01f,
            $"{tag} control: and hooks and damages the creature behind, so the tame stood in the line of fire ({Describe(wild, wildBefore)})");
        Release(wild, h);
        if (!angleShot)
        {
            yield break;
        }

        spot = Ground(tameSpot + rig.Right * 4f);
        yield return FixedFor(0.3f, pin);
        tameBefore = Snap.Take(tame);
        wildBefore = Snap.Take(wild);
        yield return Fly(rig, h, launch, AimPoint(wild), pin, flight);
        c.Check(HookOn(wild, h) != null && wild.GetHealth() < wildBefore.Health - 0.01f && flight.RaiseSkill > 0f,
            $"{tag}: with the tame out of the way the creature is hooked and damaged, vanilla ({Describe(wild, wildBefore)})");
        c.Check(HookOn(tame, h) == null && Untouched(tame, tameBefore), $"{tag}: the tame next to the line is left alone");
        Release(wild, h);
    }

    // Real swing of the held melee weapon at a creature held `reach` m ahead (Humanoid.StartAttack = attack key).
    private static IEnumerator Swing(Rig rig, Character target, Vector3 spot, Action each, Box box)
    {
        var p = rig.P;
        Action hold = () =>
        {
            Place(target, spot, -rig.Dir);
            each?.Invoke();
        };
        var t0 = Time.time;
        while (p.InAttack() && Time.time - t0 < 3f)
        {
            hold();
            yield return null;
        }
        rig.PlacePlayerAlong(0f);
        p.m_lookPitch = 25f; // look down a bit: a boar is low
        yield return Wait(0.3f, hold);
        var started = p.StartAttack(null, false);
        var seen = false;
        t0 = Time.time;
        while (Time.time - t0 < 3f)
        {
            hold();
            yield return null;
            if (p.InAttack())
            {
                seen = true;
            }
            else if (seen || Time.time - t0 > 1.5f)
            {
                break;
            }
        }
        box.Ok = started && seen;
        box.Seconds = Time.time - t0;
    }

    // ---------- harpoon.hook: T01, T07, T04 ----------

    private static IEnumerator RunHook()
    {
        var c = new Checks(HookName);
        var rig = Rig.Create(HookName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        try
        {
            var h = Harpoon.Resolve(c);
            if (h == null)
            {
                c.Report();
                yield break;
            }
            h.NoteFacts(c);
            var p = rig.P;
            yield return rig.Stage(14f, 4f, c);

            // T01: tamed Boar 12 m away, PvP off.
            var boar = rig.Spawn("Boar", rig.At(12f), true);
            if (!c.Check(boar != null, "could not spawn a Boar"))
            {
                c.Report();
                yield break;
            }
            yield return FixedFor(0.6f);
            c.Check(TameRules.IsTame(boar) && !p.IsPVPEnabled(), $"T01 setup: the Boar is tamed ({boar.IsTamed()}), PvP is off ({!p.IsPVPEnabled()})");
            TestSwitches.ThrowerSideOff = true;
            var probe = Probe(rig, h, p);
            var vanilla = probe.IsValidTarget(boar);
            TestSwitches.ThrowerSideOff = false;
            rig.Destroy(probe);
            c.Check(!vanilla, "T01 baseline: without the mod the harpoon flies through this tame (PvP off)");

            var hits = Stat(PlayerStatType.EnemyHits);
            var before = Snap.Take(boar);
            var distance = Vector3.Distance(p.transform.position, boar.transform.position);
            var mark = Tap.Mark();
            ClearCenter();
            var shot = DirectHit(rig, h, boar);
            CheckHarmlessHook(c, "T01", rig, h, boar, before, shot);
            c.Check(Tap.CountSince(mark, $"Hooked tame {boar.m_name} (simulated by this game)") == 1,
                "T01: one Debug line 'Hooked tame ... (simulated by this game)'");
            c.Check(Near(Stat(PlayerStatType.EnemyHits), hits + 1f),
                $"T01: the game's Enemy Hits statistic +1, as the README says ({F(hits)} -> {F(Stat(PlayerStatType.EnemyHits))})");
            var se = HookOn(boar, h);
            c.Check(se != null && Near(se.m_baseDistance, distance, 0.3f),
                $"T01: the line length is the distance at the hit ({F(distance)} m, line {(se != null ? F(se.m_baseDistance) : "none")})");
            yield return CheckCalm(c, "T01", rig, boar, before, 3f);
            c.Check(HookOn(boar, h) != null, "T01: still hooked 3 s later");
            Release(boar, h);

            // T07: PvP on. Vanilla accepts the tame; the mod still takes the harm out.
            p.SetPVP(true);
            yield return null;
            before = Snap.Take(boar);
            ClearCenter();
            shot = DirectHit(rig, h, boar);
            CheckHarmlessHook(c, "T07 PvP on", rig, h, boar, before, shot);
            Release(boar, h);
            var control = rig.Spawn("Boar", rig.At(10f, 3f), true);
            if (c.Check(control != null, "T07 control: could not spawn a second Boar"))
            {
                Tough(control);
                yield return FixedFor(0.4f);
                var controlBefore = Snap.Take(control);
                TestSwitches.ThrowerSideOff = true;
                var controlShot = DirectHit(rig, h, control);
                TestSwitches.ThrowerSideOff = false;
                c.Check(controlShot.Valid && HookOn(control, h) != null && control.GetHealth() < controlBefore.Health - 0.01f,
                    $"T07 control: the same PvP throw by a player without the mod hooks and damages the tame ({Describe(control, controlBefore)})");
                rig.Destroy(control);
            }
            p.SetPVP(false);

            // T04: further than the line reaches.
            var reach = h.Effect.m_maxDistance;
            var far = rig.Spawn("Boar", rig.At(reach + 5f), true);
            if (c.Check(far != null, "T04: could not spawn the far Boar"))
            {
                yield return FixedFor(0.5f);
                distance = Vector3.Distance(p.transform.position, far.transform.position);
                c.Check(distance > reach, $"T04 setup: the tame is {F(distance)} m away, the line reaches {F(reach)} m");
                var zdo = far.m_nview.GetZDO();
                var key = AttackerKey(p);
                before = Snap.Take(far);
                ClearCenter();
                shot = DirectHit(rig, h, far);
                var want = Loc("$msg_harpoon_targettoofar");
                c.Check(shot.Valid, "T04: the harpoon is allowed to hit the far tame");
                c.Check(Center() == want, $"T04: message '{want}' on screen (got '{Center()}')");
                yield return FixedFor(0.2f);
                c.Check(HookOn(far, h) == null, "T04: not hooked (the hook effect is gone at once)");
                c.Check(Untouched(far, before) && Near(far.GetHealth(), far.GetMaxHealth()), $"T04: no damage ({Describe(far, before)})");
                c.Check(!zdo.GetBool(key) && zdo.GetInt(ZDOVars.s_attackers) == 0, $"T04: no kill-credit mark left on it ({Marks(zdo, key)})");
            }
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- harpoon.throw: T01 through the real attack ----------

    private static IEnumerator RunThrow()
    {
        var c = new Checks(ThrowName);
        var rig = Rig.Create(ThrowName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        try
        {
            var h = Harpoon.Resolve(c);
            if (h == null)
            {
                c.Report();
                yield break;
            }
            var p = rig.P;
            yield return rig.Stage(11f, 3f, c);
            var weapon = rig.Give(Harpoon.Prefab);
            var spot = rig.At(7f);
            var boar = rig.Spawn("Boar", spot, true);
            if (!c.Check(weapon != null && boar != null, "could not give the harpoon or spawn a Boar"))
            {
                c.Report();
                yield break;
            }
            rig.KeepSkill(Skills.SkillType.Spears);
            rig.EmptyHands();
            if (!c.Check(p.EquipItem(weapon, false), "could not equip the harpoon"))
            {
                c.Report();
                yield break;
            }
            rig.TakeControls();
            yield return FixedFor(0.8f, () => Place(boar, spot, -rig.Dir));
            c.Check(TameRules.IsTame(boar) && !p.IsPVPEnabled(), "setup: tamed Boar 7 m ahead, PvP off");

            var before = Snap.Take(boar);
            var skill = SkillState(p, Skills.SkillType.Spears);
            var hits = Stat(PlayerStatType.EnemyHits);
            var mark = Tap.Mark();
            ClearCenter();
            var r = new ThrowResult();
            yield return RealThrow(rig, h, weapon, boar, spot, r);
            c.Note($"real throw: {r.Attempts} throw(s), attack started {r.Started}, projectile launched {r.Launched}, hooked {r.Hooked}; {r.Detail}");
            if (c.Check(r.Hooked, $"T01: a real throw (attack, animation, Attack.FireProjectileBurst, flight) hooks the tame 7 m away within 5 throws ({r.Detail})"))
            {
                var shot = new Shot { Valid = true, Stopped = true, RaiseSkill = r.RaiseSkill, Adrenaline = r.Adrenaline, Push = r.Push };
                CheckHarmlessHook(c, "T01 real throw", rig, h, boar, before, shot);
                c.Check(SkillState(p, Skills.SkillType.Spears) == skill,
                    $"T01 real throw: the Spears skill did not move ({skill} -> {SkillState(p, Skills.SkillType.Spears)})");
                c.Check(Tap.CountSince(mark, $"Hooked tame {boar.m_name} (simulated by this game)") == 1, "T01 real throw: one Debug line 'Hooked tame ...'");
                c.Check(Near(Stat(PlayerStatType.EnemyHits), hits + 1f), "T01 real throw: Enemy Hits +1");
                yield return CheckCalm(c, "T01 real throw", rig, boar, before, 2f, () => Place(boar, spot, -rig.Dir));
            }
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- harpoon.rehook: T24 ----------

    private static IEnumerator RunRehook()
    {
        var c = new Checks(RehookName);
        var rig = Rig.Create(RehookName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        try
        {
            var h = Harpoon.Resolve(c);
            if (h == null)
            {
                c.Report();
                yield break;
            }
            var p = rig.P;
            yield return rig.Stage(11f, 3f, c);
            var boar = rig.Spawn("Boar", rig.At(10f), true);
            if (!c.Check(boar != null, "could not spawn a Boar"))
            {
                c.Report();
                yield break;
            }
            yield return FixedFor(0.6f);
            var before = Snap.Take(boar);
            var firstMark = Tap.Mark();
            ClearCenter();
            var shot = DirectHit(rig, h, boar);
            CheckHarmlessHook(c, "first hook", rig, h, boar, before, shot);
            c.Check(Tap.CountSince(firstMark, "Cleared the attacker mark a harpoon hook left on " + boar.m_name) == 1,
                "T24: the first throw's mark was cleared (one Debug line)");
            var first = HookOn(boar, h);
            if (!c.Check(first != null, "setup: the Boar is hooked"))
            {
                c.Report();
                yield break;
            }
            var firstLength = first.m_baseDistance;

            // 4 m closer (the line hangs loose), second throw while it is still hooked.
            rig.PlacePlayerAlong(4f);
            yield return FixedFor(0.5f);
            c.Check(ReferenceEquals(HookOn(boar, h), first), "setup: still hooked after stepping closer");
            var zdo = boar.m_nview.GetZDO();
            var key = AttackerKey(p);
            before = Snap.Take(boar);
            var distance = Vector3.Distance(p.transform.position, boar.transform.position);
            var mark = Tap.Mark();
            ClearCenter();
            shot = DirectHit(rig, h, boar);
            CheckHarmlessHook(c, "T24 second throw", rig, h, boar, before, shot);
            var second = HookOn(boar, h);
            c.Check(ReferenceEquals(second, first), "T24: it keeps the one hook effect (no second one)");
            c.Check(second != null && Near(second.m_baseDistance, distance, 0.3f) && second.m_baseDistance < firstLength - 1f,
                $"T24: the line length is taken again from where the player stands ({F(firstLength)} m -> {(second != null ? F(second.m_baseDistance) : "none")} m, distance {F(distance)} m)");
            c.Check(!zdo.GetBool(key) && zdo.GetInt(ZDOVars.s_attackers) == 0, $"T24: no kill-credit mark after both throws ({Marks(zdo, key)})");
            c.Check(Tap.CountSince(mark, "Cleared the attacker mark a harpoon hook left on " + boar.m_name) == 1,
                "T24: the second throw's mark was cleared too (one Debug line)");
            yield return CheckCalm(c, "T24", rig, boar, before, 2f);
            c.Check(HookOn(boar, h) != null, "T24: still hooked 2 s later");
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- harpoon.wild: T05, T16 ----------

    private static IEnumerator RunWild()
    {
        var c = new Checks(WildName);
        var rig = Rig.Create(WildName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        try
        {
            var h = Harpoon.Resolve(c);
            if (h == null)
            {
                c.Report();
                yield break;
            }
            var p = rig.P;
            rig.KeepSkill(Skills.SkillType.Spears);
            yield return rig.Stage(10f, 3f, c);

            // T05: wild boar.
            var wild = rig.Spawn("Boar", rig.At(9f), false);
            if (!c.Check(wild != null, "could not spawn a Boar"))
            {
                c.Report();
                yield break;
            }
            Tough(wild);
            yield return FixedFor(0.4f);
            c.Check(!wild.IsTamed() && !TameRules.IsTame(wild), "T05 setup: the Boar is wild");
            var before = Snap.Take(wild);
            var skill = SkillState(p, Skills.SkillType.Spears);
            var mark = Tap.Mark();
            var shot = DirectHit(rig, h, wild);
            CheckVanillaHit(c, "T05 wild boar", rig, h, wild, before, shot);
            c.Check(SkillState(p, Skills.SkillType.Spears) != skill, $"T05: the hit raises the Spears skill ({skill} -> {SkillState(p, Skills.SkillType.Spears)})");
            c.Check(Tap.CountSince(mark, "Hooked tame") == 0, "T05: the mod did not treat it as a tame (no 'Hooked tame' Debug line)");
            rig.Destroy(wild);

            // T16: fed once, half tamed.
            var fed = rig.Spawn("Boar", rig.At(9f, 3f), false);
            var tameable = fed != null ? fed.GetComponent<Tameable>() : null;
            if (c.Check(fed != null && tameable != null, "T16: could not spawn a tameable Boar"))
            {
                Tough(fed);
                var zdo = fed.m_nview.GetZDO();
                zdo.Set(ZDOVars.s_tameLastFeeding, ZNet.instance.GetTime().Ticks);
                zdo.Set(ZDOVars.s_tameTimeLeft, tameable.m_tamingTime * 0.5f);
                yield return FixedFor(0.4f);
                c.Check(!fed.IsTamed() && !TameRules.IsTame(fed) && tameable.GetTameness() > 0 && !tameable.IsHungry(),
                    $"T16 setup: fed, {tameable.GetTameness()} % tame, not tamed yet (tamed {fed.IsTamed()}, hungry {tameable.IsHungry()})");
                before = Snap.Take(fed);
                mark = Tap.Mark();
                shot = DirectHit(rig, h, fed);
                CheckVanillaHit(c, "T16 creature being tamed", rig, h, fed, before, shot);
                c.Check(Tap.CountSince(mark, "Hooked tame") == 0, "T16: the mod did not treat it as a tame");
            }
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- harpoon.drag: T02 ----------

    private static IEnumerator RunDrag()
    {
        var c = new Checks(DragName);
        var rig = Rig.Create(DragName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        try
        {
            var h = Harpoon.Resolve(c);
            if (h == null)
            {
                c.Report();
                yield break;
            }
            var p = rig.P;
            yield return rig.Stage(10f, 18f, c);
            var boar = rig.Spawn("Boar", rig.At(8f), true);
            if (!c.Check(boar != null, "could not spawn a Boar"))
            {
                c.Report();
                yield break;
            }
            yield return FixedFor(0.6f);
            var box = new Box();
            var pull = new PullResult();

            // Dragged: the player walks 16 m away in 4 s. World stamina rate still 0 here: the line cannot be lost
            // to stamina.
            var se = HookAt(rig, h, boar, 8f);
            if (!c.Check(se != null, "setup: the tamed Boar is hooked"))
            {
                c.Report();
                yield break;
            }
            var length = se.m_baseDistance;
            ClearCenter();
            yield return Walk(rig, h, boar, WalkSpeed, 4f, pull);
            c.Check(!pull.Ended && pull.Moved >= 3f,
                $"T02: walking away drags the boar along: the player went {F(pull.Walked)} m in {F(pull.Seconds)} s, the boar came {F(pull.Moved)} m "
                + $"(line stretched at most {F(pull.Stretch)} m, it breaks at {F(h.Effect.m_breakDistance)} m; hook lost {pull.Ended}, message '{Center()}')");
            c.Check(pull.Gap > length - 2f,
                $"T02: the boar stays behind the player on its line, it is not thrown past him (it ends {F(pull.Gap)} m behind, line length {F(length)} m)");
            c.Check(boar != null && Near(boar.GetHealth(), boar.GetMaxHealth()), "T02: being dragged does not hurt it");

            // Stamina drains while it pulls, and an empty bar releases.
            yield return rig.StaminaOn(c);
            se = HookAt(rig, h, boar, 8f);
            p.m_stamina = p.GetMaxStamina();
            yield return Walk(rig, h, boar, WalkSpeed, 3.5f, pull);
            var used = pull.Drop;
            c.Check(!pull.Ended && used > 0.01f,
                $"T02: stamina drains while the boar is pulled ({F(used)} used in {F(pull.Seconds)} s of walking away, boar dragged {F(pull.Moved)} m, hook lost {pull.Ended})");
            if (used > 0.01f)
            {
                se = HookAt(rig, h, boar, 8f);
                ClearCenter();
                // Less left in the bar than the same walk just used: the pull must empty it on the way.
                p.m_stamina = Mathf.Clamp(used * 0.4f, 0.002f, p.GetMaxStamina());
                p.m_staminaRegenTimer = 10f; // no regen meanwhile: only the pull move the bar
                var left = p.GetStamina();
                yield return Walk(rig, h, boar, WalkSpeed, 4.2f, pull);
                yield return FixedFor(0.1f);
                c.Check(pull.Ended && HookOn(boar, h) == null && p.GetStamina() <= 0.001f,
                    $"T02: when the stamina runs out the boar is let go ({F(left)} left at the start, hook gone {HookOn(boar, h) == null} after {F(pull.Seconds)} s, stamina {F(p.GetStamina())})");
                c.Check(Center() == ReleasedText(boar), $"T02: message '{ReleasedText(boar)}' when the stamina ran out (got '{Center()}')");
            }
            p.m_stamina = p.GetMaxStamina();
            yield return rig.StaminaOff();

            // Block: nothing in the first 2 s, release after.
            se = HookAt(rig, h, boar, 8f);
            ClearCenter();
            yield return BlockRelease(rig, h, boar, 0.6f, box);
            c.Check(HookOn(boar, h) != null, "T02: blocking in the first 2 s does not release it");
            yield return Until(() => HookOn(boar, h) == null || HookOn(boar, h).m_time > 2.1f, 4f, box);
            ClearCenter();
            yield return BlockRelease(rig, h, boar, 1.5f, box);
            c.Check(box.Ok && HookOn(boar, h) == null, $"T02: holding block after 2 s releases the boar (blocking seen and hook gone: {box.Ok})");
            c.Check(Center() == ReleasedText(boar), $"T02: message '{ReleasedText(boar)}' after the block (got '{Center()}')");

            // Too far: the line breaks.
            se = HookAt(rig, h, boar, 8f);
            if (c.Check(se != null, "setup: hooked again for the line break"))
            {
                ClearCenter();
                rig.PlacePlayerAlong(rig.Along(boar.transform.position) - (se.m_baseDistance + se.m_breakDistance + 2f));
                yield return FixedFor(0.4f);
                var broke = Loc("$msg_harpoon_linebroke");
                c.Check(HookOn(boar, h) == null && Center() == broke,
                    $"T02: {F(se.m_breakDistance + 2f)} m beyond the line length the line breaks with '{broke}' (hook gone {HookOn(boar, h) == null}, got '{Center()}')");
            }
            c.Check(boar != null && Near(boar.GetHealth(), boar.GetMaxHealth()) && boar.m_lastHit == null, "T02: the boar took no damage in all of this");
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- harpoon.heavy: T03 ----------

    private static IEnumerator RunHeavy()
    {
        var c = new Checks(HeavyName);
        var rig = Rig.Create(HeavyName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        try
        {
            var h = Harpoon.Resolve(c);
            if (h == null)
            {
                c.Report();
                yield break;
            }
            var p = rig.P;
            yield return rig.Stage(10f, 18f, c);
            // Line held this far beyond its length for the stamina comparison: same stretch = same pull strength
            // for both animals, so only their weight differs (vanilla drain = drain value x pull strength x mass).
            const float stretch = 0.5f;
            string[] names = { "Boar", "Lox" };
            var drain = new float[2];
            var moved = new float[2];
            var walked = new float[2];
            var broke = new bool[2];
            for (var i = 0; i < names.Length; i++)
            {
                rig.PlacePlayerAlong(0f);
                var animal = rig.Spawn(names[i], rig.At(9f), true);
                if (!c.Check(animal != null, $"could not spawn a {names[i]}"))
                {
                    continue;
                }
                yield return FixedFor(0.8f);
                var before = Snap.Take(animal);
                ClearCenter();
                var shot = DirectHit(rig, h, animal);
                CheckHarmlessHook(c, $"T03 tamed {names[i]}", rig, h, animal, before, shot);

                // How far it comes while the player walks 16 m away in 4 s (stamina rate 0: nothing lost to
                // stamina). An animal that does not come loses the line by distance ("Line broke").
                var walk = new PullResult();
                ClearCenter();
                yield return Walk(rig, h, animal, WalkSpeed, 4f, walk);
                yield return FixedFor(0.1f);
                moved[i] = walk.Moved;
                walked[i] = walk.Walked;
                broke[i] = HookOn(animal, h) == null && Center() == Loc("$msg_harpoon_linebroke");

                // Stamina per second with the line held `stretch` m beyond its length, from the normal full bar
                // (a bar set above its maximum is cut back by the game's food update and reads as stamina used).
                yield return rig.StaminaOn(c);
                var se = HookAt(rig, h, animal, 9f);
                c.Check(se != null, $"setup: the {names[i]} is hooked again for the stamina part");
                p.m_stamina = p.GetMaxStamina();
                var hold = new PullResult();
                yield return Hold(rig, h, animal, stretch, 1.5f, hold);
                drain[i] = hold.Drop / Mathf.Max(0.1f, hold.Seconds);
                p.m_stamina = p.GetMaxStamina();
                yield return rig.StaminaOff();
                c.Check(animal != null && Near(animal.GetHealth(), animal.GetMaxHealth()) && animal.m_lastHit == null, $"T03 {names[i]}: no damage from hook and pull");
                c.Note($"{names[i]} (mass {F(animal.GetMass())}): the player walked {F(walked[i])} m away in {F(walk.Seconds)} s, it came {F(moved[i])} m "
                       + $"(line stretched at most {F(walk.Stretch)} m of the {F(h.Effect.m_breakDistance)} m that break it; {(broke[i] ? "the line broke" : walk.Ended ? "the hook was lost" : "still hooked")}); "
                       + $"stamina {F(drain[i])} per second with the line held {F(stretch)} m beyond its length ({F(hold.Drop)} in {F(hold.Seconds)} s, stretch seen {F(hold.Stretch)} m{(hold.Ended ? ", hook lost" : "")})");
                rig.Destroy(animal);
                yield return FixedFor(0.2f);
            }
            c.Check(drain[0] > 0f && drain[1] > drain[0] * 3f,
                $"T03: with the same stretch of the line the lox drains stamina much faster than the boar (lox {F(drain[1])} per second, boar {F(drain[0])} per second)");
            c.Note($"for the README line 'Dragging a lox': while the player walked {F(walked[1])} m away the lox came {F(moved[1])} m"
                   + $"{(broke[1] ? " and the line broke" : "")} (the boar came {F(moved[0])} m of {F(walked[0])} m{(broke[0] ? " and its line broke" : "")})");
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- harpoon.others: T06 ----------

    private static IEnumerator RunOthers()
    {
        var c = new Checks(OthersName);
        var rig = Rig.Create(OthersName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        try
        {
            var h = Harpoon.Resolve(c);
            if (h == null)
            {
                c.Report();
                yield break;
            }
            var p = rig.P;
            rig.KeepSkill(Skills.SkillType.Bows);
            rig.KeepSkill(Skills.SkillType.Spears);
            rig.KeepSkill(Skills.SkillType.Clubs);
            yield return rig.Stage(8f, 3f, c);
            var tameSpot = rig.At(6f);
            var wildSpot = rig.At(6f, 4f);
            var tame = rig.Spawn("Boar", tameSpot, true);
            var wild = rig.Spawn("Boar", wildSpot, false);
            if (!c.Check(tame != null && wild != null, "could not spawn the Boars"))
            {
                c.Report();
                yield break;
            }
            Tough(wild);
            if (wild.GetBaseAI() != null)
            {
                wild.GetBaseAI().enabled = false; // control dummy: it must not fight the tame
            }
            yield return FixedFor(0.5f);
            c.Check(!p.IsPVPEnabled() && TameRules.IsTame(tame) && !wild.IsTamed(), "setup: PvP off, one tamed Boar, one wild Boar");

            // Arrow of the Crude Bow, thrown Flint Spear: their projectiles ask the same vanilla question.
            var bow = ItemOf("Bow");
            var arrow = ItemOf("ArrowFlint");
            var spear = ItemOf("SpearFlint");
            if (c.Check(bow != null && arrow != null && spear != null, "items Bow, ArrowFlint or SpearFlint not found"))
            {
                var spearThrow = spear.m_shared.m_attack != null && spear.m_shared.m_attack.m_attackProjectile != null
                    ? spear.m_shared.m_attack
                    : spear.m_shared.m_secondaryAttack;
                var arrowProjectile = arrow.m_shared.m_attack != null ? arrow.m_shared.m_attack.m_attackProjectile : null;
                var spearProjectile = spearThrow != null ? spearThrow.m_attackProjectile : null;
                if (c.Check(arrowProjectile != null && spearProjectile != null, "the arrow or the flint spear has no projectile"))
                {
                    for (var i = 0; i < 2; i++)
                    {
                        var name = i == 0 ? "Bow with ArrowFlint" : "thrown SpearFlint";
                        var prefab = i == 0 ? arrowProjectile : spearProjectile;
                        var weapon = i == 0 ? bow : spear;
                        var attack = i == 0 ? bow.m_shared.m_attack : spearThrow;
                        var ammo = i == 0 ? arrow : null;
                        var before = Snap.Take(tame);
                        var shot = ProjectileHit(rig, prefab, p, tame, BuildHit(p, weapon, attack, ammo), weapon, ammo, attack.m_projectileVel, attack.m_attackHitNoise, true);
                        c.Check(!shot.Valid && !shot.Stopped && HookOn(tame, h) == null && Untouched(tame, before),
                            $"T06 {name}: flies through the tame, no hit, no damage (allowed {shot.Valid}, stopped {shot.Stopped}, {Describe(tame, before)})");
                        var wildBefore = Snap.Take(wild);
                        var wildShot = ProjectileHit(rig, prefab, p, wild, BuildHit(p, weapon, attack, ammo), weapon, ammo, attack.m_projectileVel, attack.m_attackHitNoise, true);
                        c.Check(wildShot.Valid && wild.GetHealth() < wildBefore.Health - 0.01f,
                            $"T06 {name} control: the same projectile does hit a wild boar ({Describe(wild, wildBefore)})");
                    }
                }
            }

            // The mod's damage patch only touches harpoon hits: any other hit sent at a tame keeps its damage.
            var other = rig.Spawn("Boar", rig.At(6f, -4f), true);
            if (c.Check(other != null, "could not spawn the third Boar"))
            {
                Tough(other);
                yield return FixedFor(0.3f);
                var otherBefore = Snap.Take(other);
                var plain = new HitData();
                plain.m_damage.m_slash = 5f;
                plain.m_point = other.GetCenterPoint();
                plain.m_dir = rig.Dir;
                plain.m_skill = Skills.SkillType.Swords;
                plain.SetAttacker(p);
                other.Damage(plain);
                c.Check(other.GetHealth() < otherBefore.Health - 0.01f,
                    $"a hit that is not a harpoon hit reaches a tame with its damage (as the Butcher Knife or PvP melee do) ({Describe(other, otherBefore)})");
                rig.Destroy(other);
            }

            // Real melee swing (Club): the tame right ahead is not hit, a wild boar on the same spot is.
            var club = rig.Give("Club");
            if (c.Check(club != null, "could not give a Club"))
            {
                rig.EmptyHands();
                c.Check(p.EquipItem(club, false), "could not equip the Club");
                rig.TakeControls();
                var ahead = rig.At(1.2f);
                var box = new Box();
                var before = Snap.Take(tame);
                var hits = Stat(PlayerStatType.EnemyHits);
                yield return Swing(rig, tame, ahead, () => Place(wild, wildSpot, -rig.Dir), box);
                c.Check(box.Ok, "T06 melee: the Club swing ran");
                c.Check(Untouched(tame, before) && Near(Stat(PlayerStatType.EnemyHits), hits),
                    $"T06 melee: the swing does not hit the tame right ahead ({Describe(tame, before)}, Enemy Hits {F(hits)} -> {F(Stat(PlayerStatType.EnemyHits))})");
                Place(tame, tameSpot, -rig.Dir);
                var wildBefore = Snap.Take(wild);
                yield return Swing(rig, wild, ahead, () => Place(tame, tameSpot, -rig.Dir), box);
                c.Check(box.Ok && wild.GetHealth() < wildBefore.Health - 0.01f,
                    $"T06 melee control: the same swing hits a wild boar on that spot ({Describe(wild, wildBefore)})");
            }
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- harpoon.rider: M03 (the local player is the rider), T08 ----------

    private static IEnumerator RunRider()
    {
        var c = new Checks(RiderName);
        var rig = Rig.Create(RiderName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        try
        {
            var h = Harpoon.Resolve(c);
            if (h == null)
            {
                c.Report();
                yield break;
            }
            var p = rig.P;
            yield return rig.Stage(10f, 3f, c);
            var lox = rig.Spawn("Lox", rig.At(8f), true);
            var tameable = lox != null ? lox.GetComponent<Tameable>() : null;
            if (!c.Check(lox != null && tameable != null && tameable.m_saddle != null && tameable.m_saddleItem != null,
                    "setup: no Lox, or the Lox takes no saddle"))
            {
                c.Report();
                yield break;
            }
            yield return FixedFor(0.8f);
            var zdo = lox.m_nview.GetZDO();
            var saddleName = tameable.m_saddleItem.gameObject.name;
            var saddleItem = rig.Give(saddleName);
            c.Check(saddleItem != null && tameable.UseItem(p, saddleItem), $"setup: {saddleName} put on the lox");
            yield return FixedFor(0.3f);
            c.Check(zdo.GetBool(ZDOVars.s_haveSaddleHash) && tameable.m_saddle.gameObject.activeInHierarchy, "setup: the lox wears the saddle");

            // Ride it (Sadle.Interact = Use on the saddle).
            var box = new Box();
            Place(p, Ground(lox.transform.position - rig.Dir * 5f), rig.Dir);
            yield return FixedFor(0.3f);
            tameable.m_saddle.Interact(p, false, false);
            yield return Until(() => tameable.HaveRider(), 3f, box);
            if (c.Check(box.Ok && p.GetDoodadController() != null, "setup: the player rides the lox"))
            {
                // M03, PvP off: flies past, with the Debug line once per projectile and tame.
                var mark = Tap.Mark();
                var probe = Probe(rig, h, p);
                var first = probe.IsValidTarget(lox);
                var again = probe.IsValidTarget(lox);
                rig.Destroy(probe);
                c.Check(!first && !again, "M03: PvP off, a tame somebody rides is not a target for the harpoon");
                c.Check(Tap.CountSince(mark, $"Harpoon passed tame {lox.m_name}: a player is riding it.") == 1,
                    "M03: one Debug line 'Harpoon passed tame ...: a player is riding it.' for that harpoon");
                var before = Snap.Take(lox);
                var shot = DirectHit(rig, h, lox);
                c.Check(!shot.Valid && !shot.Stopped && HookOn(lox, h) == null && Untouched(lox, before),
                    $"M03: PvP off, the harpoon flies past the ridden lox: not hooked, untouched ({Describe(lox, before)})");

                // M03, PvP on: vanilla accepts it, hooked without harm.
                p.SetPVP(true);
                yield return null;
                before = Snap.Take(lox);
                ClearCenter();
                shot = DirectHit(rig, h, lox);
                CheckHarmlessHook(c, "M03 PvP on, ridden lox", rig, h, lox, before, shot);
                Release(lox, h);
                p.SetPVP(false);

                p.StopDoodadControl();
                yield return Until(() => !tameable.HaveRider() && !p.IsAttached(), 3f, box);
                c.Check(box.Ok, "setup: the player got off the lox");
            }

            // T08: saddled, nobody on it, from a few steps back.
            rig.PlacePlayerAlong(0f);
            yield return FixedFor(0.5f);
            c.Check(!tameable.HaveRider() && zdo.GetBool(ZDOVars.s_haveSaddleHash), "T08 setup: saddled lox, nobody rides it");
            var before8 = Snap.Take(lox);
            ClearCenter();
            var shot8 = DirectHit(rig, h, lox);
            CheckHarmlessHook(c, "T08 saddled lox, not ridden", rig, h, lox, before8, shot8);
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- harpoon.summon: T09, T13 ----------

    private static IEnumerator RunSummon()
    {
        var c = new Checks(SummonName);
        var rig = Rig.Create(SummonName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        try
        {
            var h = Harpoon.Resolve(c);
            if (h == null)
            {
                c.Report();
                yield break;
            }
            rig.KeepSkill(Skills.SkillType.Spears);
            yield return rig.Stage(13f, 3f, c);
            var skeletonSpot = rig.At(6f);
            var neckSpot = rig.At(11f);
            var skeleton = rig.Spawn("Skeleton_Friendly", skeletonSpot, false);
            if (!c.Check(skeleton != null, "could not spawn Skeleton_Friendly"))
            {
                c.Report();
                yield break;
            }
            yield return FixedFor(0.6f);
            var tameable = skeleton.GetComponent<Tameable>();
            c.Note($"Skeleton_Friendly spawns tamed: {skeleton.IsTamed()} (its 'starts tamed' flag: {(tameable != null ? tameable.m_startsTamed.ToString() : "no Tameable")}), name {skeleton.m_name}");
            var ai = skeleton.GetBaseAI() as MonsterAI;
            if (!skeleton.IsTamed() && ai != null)
            {
                ai.MakeTame();
                yield return FixedFor(0.2f);
            }
            c.Check(TameRules.IsTame(skeleton), "setup: the skeleton counts as a tame");

            // T09: hooked, harmless, does not attack the player.
            var before = Snap.Take(skeleton);
            ClearCenter();
            var shot = DirectHit(rig, h, skeleton);
            CheckHarmlessHook(c, "T09 summon", rig, h, skeleton, before, shot);
            yield return CheckCalm(c, "T09 summon", rig, skeleton, before, 3f);
            Release(skeleton, h);

            // T13: while it fights a Neck.
            var neck = rig.Spawn("Neck", neckSpot, false);
            if (!c.Check(neck != null && ai != null, "T13: could not spawn a Neck, or the skeleton has no MonsterAI"))
            {
                c.Report();
                yield break;
            }
            Tough(neck);
            Action pin = () =>
            {
                Place(skeleton, skeletonSpot, rig.Dir);
                Place(neck, neckSpot, -rig.Dir);
            };
            if (tameable != null && tameable.m_commandable && ai.GetFollowTarget() == null)
            {
                tameable.Command(rig.P, false); // a skeleton raised by the staff follows its player
            }
            GiveTarget(ai, neck);
            yield return FixedFor(0.5f, pin);
            if (ai.GetTargetCreature() != neck)
            {
                GiveTarget(ai, neck);
            }
            c.Check(ai.GetTargetCreature() == neck, "T13 setup: the skeleton has the Neck as its target (it fights)");
            before = Snap.Take(skeleton);
            ClearCenter();
            shot = DirectHit(rig, h, skeleton);
            CheckHarmlessHook(c, "T13 fighting summon", rig, h, skeleton, before, shot);
            // Its AI had time to think about the hit: still not after the player.
            yield return FixedFor(1.5f, pin);
            c.Check(skeleton != null && TargetOf(skeleton) != rig.P,
                $"T13: 1.5 s later the hooked skeleton has not turned on the player (target {(skeleton != null && TargetOf(skeleton) != null ? TargetOf(skeleton).m_name : "none")})");
            Release(skeleton, h);
            yield return LineOfFire(rig, c, h, "T13", skeleton, neck, skeletonSpot, neckSpot, false);
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- harpoon.stay: T10 ----------

    private static IEnumerator RunStay()
    {
        var c = new Checks(StayName);
        var rig = Rig.Create(StayName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        try
        {
            var h = Harpoon.Resolve(c);
            if (h == null)
            {
                c.Report();
                yield break;
            }
            var p = rig.P;
            yield return rig.Stage(10f, 18f, c);
            var wolf = rig.Spawn("Wolf", rig.At(8f), true);
            var tameable = wolf != null ? wolf.GetComponent<Tameable>() : null;
            var ai = wolf != null ? wolf.GetBaseAI() as MonsterAI : null;
            if (!c.Check(wolf != null && tameable != null && ai != null, "could not spawn a tameable Wolf"))
            {
                c.Report();
                yield break;
            }
            yield return FixedFor(0.6f);
            c.Check(tameable.m_commandable, "setup: a tamed Wolf can be told to follow and to stay (a Boar cannot)");
            var zdo = wolf.m_nview.GetZDO();

            // Use on it twice: follow, then stay here.
            tameable.Command(p, false);
            yield return FixedFor(0.2f);
            c.Check(ai.GetFollowTarget() == p.gameObject, "setup: told to follow");
            tameable.Command(p, false);
            yield return FixedFor(0.2f);
            var stay = zdo.GetVec3(ZDOVars.s_patrolPoint, Vector3.zero);
            c.Check(ai.GetFollowTarget() == null && zdo.GetBool(ZDOVars.s_patrol) && HDist(stay, wolf.transform.position) < 1.5f,
                $"setup: told to stay, stay spot where it stands ({F(stay)})");
            var roam = ai.m_randomMoveRange;
            ai.m_randomMoveRange = Mathf.Min(roam, 3f); // short roam: "far from the stay spot" start at 6 m, test stay short
            c.Note($"Wolf roam range {F(roam)} m (vanilla walks back when further than twice that); shortened to {F(ai.m_randomMoveRange)} m for this wolf");
            var need = ai.m_randomMoveRange * 2f + 3f;

            // The hook leaves the stay state alone.
            var follow = zdo.GetString(ZDOVars.s_follow);
            var before = Snap.Take(wolf);
            ClearCenter();
            var shot = DirectHit(rig, h, wolf);
            CheckHarmlessHook(c, "T10 staying wolf", rig, h, wolf, before, shot);
            c.Check(zdo.GetBool(ZDOVars.s_patrol) && zdo.GetVec3(ZDOVars.s_patrolPoint, Vector3.zero) == stay && zdo.GetString(ZDOVars.s_follow) == follow
                    && ai.GetFollowTarget() == null, "T10: the hook leaves its stay spot and follow state as they were");

            // Drag it away, release with block.
            var pull = new PullResult();
            yield return Walk(rig, h, wolf, WalkSpeed, 4.2f, pull);
            c.Check(pull.Moved >= 3f && !pull.Ended,
                $"T10: the staying wolf is dragged along (the player went {F(pull.Walked)} m in {F(pull.Seconds)} s, the wolf came {F(pull.Moved)} m, hook lost {pull.Ended})");
            var box = new Box();
            ClearCenter();
            yield return BlockRelease(rig, h, wolf, 2f, box);
            c.Check(box.Ok && HookOn(wolf, h) == null && Center() == ReleasedText(wolf), $"T10: block releases it ('{Center()}')");
            c.Check(zdo.GetBool(ZDOVars.s_patrol) && zdo.GetVec3(ZDOVars.s_patrolPoint, Vector3.zero) == stay, "T10: drag and release did not move its stay spot");
            if (HDist(wolf.transform.position, stay) < need)
            {
                var from = HDist(wolf.transform.position, stay);
                Place(wolf, Ground(stay - rig.Dir * need), rig.Dir);
                c.Note($"the line dragged it {F(from)} m from its stay spot; set down {F(need)} m away so the walk back starts (more than twice the roam range)");
            }

            // It walks back toward the stay spot (vanilla idle movement; me only skip its idle wait).
            yield return FixedFor(0.3f);
            var start = HDist(wolf.transform.position, stay);
            ai.m_randomMoveUpdateTimer = 0f;
            yield return Until(() => wolf == null || HDist(wolf.transform.position, stay) < start - 2f, 20f, box);
            c.Check(box.Ok && wolf != null,
                $"T10: released {F(start)} m from its stay spot, it walks back towards it ({(wolf != null ? F(HDist(wolf.transform.position, stay)) : "?")} m after {F(box.Seconds)} s)");
            if (wolf == null)
            {
                c.Report();
                yield break;
            }

            // Follow, then stay at the new spot.
            tameable.Command(p, false);
            yield return FixedFor(0.2f);
            c.Check(ai.GetFollowTarget() == p.gameObject && !zdo.GetBool(ZDOVars.s_patrol) && zdo.GetString(ZDOVars.s_follow) == p.GetPlayerName(),
                "T10: told to follow again");
            var spot = rig.At(3f, 4f);
            Place(wolf, spot, rig.Dir); // led to a new spot
            yield return FixedFor(0.3f);
            tameable.Command(p, false);
            yield return FixedFor(0.2f);
            var newStay = zdo.GetVec3(ZDOVars.s_patrolPoint, Vector3.zero);
            c.Check(ai.GetFollowTarget() == null && zdo.GetBool(ZDOVars.s_patrol) && HDist(newStay, wolf.transform.position) < 1.5f && HDist(newStay, stay) > 2f,
                $"T10: told to stay at the new spot: the stay spot moved there ({F(stay)} -> {F(newStay)})");
            yield return Wait(4f);
            c.Check(wolf != null && HDist(wolf.transform.position, newStay) <= ai.m_randomMoveRange * 2f + 1.5f,
                $"T10: 4 s later it is still around the new spot ({(wolf != null ? F(HDist(wolf.transform.position, newStay)) : "?")} m from it)");
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- harpoon.xp: T11 ----------

    private static string AdrenalineTrinket()
    {
        const string preferred = "TrinketBronzeStamina";
        var item = ItemOf(preferred);
        if (item != null && item.m_shared.m_maxAdrenaline > 0f)
        {
            return preferred;
        }
        foreach (var go in ObjectDB.instance.m_items)
        {
            var drop = go != null ? go.GetComponent<ItemDrop>() : null;
            if (drop != null && drop.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trinket && drop.m_itemData.m_shared.m_maxAdrenaline > 0f)
            {
                return go.name;
            }
        }
        return null;
    }

    private static IEnumerator RunXp()
    {
        var c = new Checks(XpName);
        var rig = Rig.Create(XpName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        try
        {
            var h = Harpoon.Resolve(c);
            if (h == null)
            {
                c.Report();
                yield break;
            }
            var p = rig.P;
            rig.KeepSkill(Skills.SkillType.Spears);
            yield return rig.Stage(10f, 3f, c);

            // Bronze Pendant (or the first trinket with an adrenaline bar): without one the bar has no room.
            var trinketName = AdrenalineTrinket();
            var trinket = trinketName != null ? rig.Give(trinketName) : null;
            if (trinket != null)
            {
                if (p.m_trinketItem != null)
                {
                    p.UnequipItem(p.m_trinketItem, false);
                }
                p.EquipItem(trinket, false);
                p.UpdateModifiers();
            }
            var max = p.GetMaxAdrenaline();
            c.Check(trinket != null && max > 0f, $"setup: a trinket that gives an adrenaline bar is worn ({trinketName ?? "none found"}, bar {F(max)})");
            p.m_adrenaline = 0f;
            c.Note($"trinket {trinketName}: adrenaline bar {F(max)} (without it {F(max - (trinket != null ? trinket.m_shared.m_maxAdrenaline : 0f))}); the throw itself gives "
                   + $"{F(h.Throw.m_attackUseAdrenaline)} adrenaline, a harpoon hit {F(h.ProjectileData.m_adrenaline)}; adrenaline rate {F(Game.m_adrenalineRate)}");

            var tame = rig.Spawn("Boar", rig.At(8f), true);
            var wild = rig.Spawn("Boar", rig.At(8f, 4f), false);
            if (!c.Check(tame != null && wild != null, "could not spawn the Boars"))
            {
                c.Report();
                yield break;
            }
            Tough(wild);
            if (wild.GetBaseAI() != null)
            {
                wild.GetBaseAI().enabled = false;
            }
            yield return FixedFor(0.5f);

            // Three hooks of the tame: the hit itself pays nothing (measured around the hit, same frame).
            for (var i = 1; i <= 3; i++)
            {
                var skill = SkillState(p, Skills.SkillType.Spears);
                var adrenaline = p.GetAdrenaline();
                var before = Snap.Take(tame);
                var shot = DirectHit(rig, h, tame);
                c.Check(HookOn(tame, h) != null && Untouched(tame, before), $"T11 hook {i}: the tame is hooked, unharmed");
                c.Check(SkillState(p, Skills.SkillType.Spears) == skill, $"T11 hook {i}: the Spears skill does not move ({skill} -> {SkillState(p, Skills.SkillType.Spears)})");
                c.Check(Near(p.GetAdrenaline(), adrenaline, 0.0001f), $"T11 hook {i}: no adrenaline from the hit ({F(adrenaline)} -> {F(p.GetAdrenaline())})");
                c.Check(Near(shot.RaiseSkill, 0f) && Near(shot.Adrenaline, 0f), $"T11 hook {i}: the projectile's skill and adrenaline pay are zero");
                Release(tame, h);
                yield return FixedFor(0.2f);
            }

            // Wild boar: both grow (vanilla).
            var skillBefore = SkillState(p, Skills.SkillType.Spears);
            var adrenalineBefore = p.GetAdrenaline();
            var wildBefore = Snap.Take(wild);
            var wildShot = DirectHit(rig, h, wild);
            c.Check(HookOn(wild, h) != null && wild.GetHealth() < wildBefore.Health - 0.01f, $"T11 control: the wild boar is hooked and hurt ({Describe(wild, wildBefore)})");
            c.Check(SkillState(p, Skills.SkillType.Spears) != skillBefore,
                $"T11 control: a hit on a wild boar raises the Spears skill ({skillBefore} -> {SkillState(p, Skills.SkillType.Spears)})");
            if (max > 0f && wildShot.Adrenaline > 0f && Game.m_adrenalineRate > 0f)
            {
                c.Check(p.GetAdrenaline() > adrenalineBefore + 0.0001f,
                    $"T11 control: a hit on a wild boar gives adrenaline ({F(adrenalineBefore)} -> {F(p.GetAdrenaline())})");
            }
            else
            {
                c.Check(false, $"T11 control: adrenaline from a wild hit could not be checked (bar {F(max)}, projectile pays {F(wildShot.Adrenaline)}, rate {F(Game.m_adrenalineRate)})");
            }
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- harpoon.line: T12 ----------

    private static IEnumerator RunLine()
    {
        var c = new Checks(LineName);
        var rig = Rig.Create(LineName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        try
        {
            var h = Harpoon.Resolve(c);
            if (h == null)
            {
                c.Report();
                yield break;
            }
            var p = rig.P;
            rig.KeepSkill(Skills.SkillType.Spears);
            yield return rig.Stage(13f, 3f, c);
            var wolfSpot = rig.At(6f);
            var greylingSpot = rig.At(11f);
            var wolf = rig.Spawn("Wolf", wolfSpot, true);
            var greyling = rig.Spawn("Greyling", greylingSpot, false);
            var ai = wolf != null ? wolf.GetBaseAI() as MonsterAI : null;
            var tameable = wolf != null ? wolf.GetComponent<Tameable>() : null;
            if (!c.Check(wolf != null && greyling != null && ai != null && tameable != null, "could not spawn the Wolf or the Greyling"))
            {
                c.Report();
                yield break;
            }
            Tough(greyling);
            Action pin = () =>
            {
                Place(wolf, wolfSpot, rig.Dir);
                Place(greyling, greylingSpot, -rig.Dir);
            };
            yield return FixedFor(0.4f, pin);
            tameable.Command(p, false); // Use on it: follow
            GiveTarget(ai, greyling);
            yield return FixedFor(0.5f, pin);
            if (ai.GetTargetCreature() != greyling)
            {
                GiveTarget(ai, greyling);
            }
            c.Check(TameRules.IsTame(wolf) && ai.GetFollowTarget() == p.gameObject && ai.GetTargetCreature() == greyling,
                $"T12 setup: tamed wolf following the player, its target is the greyling (follow {ai.GetFollowTarget() == p.gameObject}, target {(TargetOf(wolf) != null ? TargetOf(wolf).m_name : "none")})");
            yield return LineOfFire(rig, c, h, "T12", wolf, greyling, wolfSpot, greylingSpot, true);
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- harpoon.busy: T21 ----------

    private static IEnumerator RunBusy()
    {
        var c = new Checks(BusyName);
        var rig = Rig.Create(BusyName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        try
        {
            var h = Harpoon.Resolve(c);
            if (h == null)
            {
                c.Report();
                yield break;
            }
            var p = rig.P;
            yield return rig.Stage(10f, 8f, c);
            var mark = Tap.Mark();

            // T21: tame that keeps a wild creature it cannot reach as its target.
            var boarSpot = rig.At(8f);
            var deerSpot = rig.At(8f, 15f);
            var boar = rig.Spawn("Boar", boarSpot, true);
            var deer = rig.Spawn("Deer", deerSpot, false);
            var boarAi = boar != null ? boar.GetBaseAI() as MonsterAI : null;
            if (!c.Check(boar != null && deer != null && boarAi != null, "could not spawn the Boar or the Deer"))
            {
                c.Report();
                yield break;
            }
            Tough(deer);
            Action pin = () =>
            {
                Place(boar, boarSpot, rig.Right);
                Place(deer, deerSpot, -rig.Right);
            };
            for (var i = 1; i <= 3; i++)
            {
                GiveTarget(boarAi, deer);
                yield return FixedFor(i == 1 ? 0.6f : 6f, pin); // three throws spread over some time
                if (boarAi.GetTargetCreature() != deer)
                {
                    c.Note($"T21 throw {i}: the boar had dropped the deer as target; given back");
                    GiveTarget(boarAi, deer);
                    yield return FixedFor(0.3f, pin);
                }
                c.Check(boarAi.GetTargetCreature() == deer, $"T21 throw {i} setup: the boar's target is the deer 15 m away");
                var before = Snap.Take(boar);
                ClearCenter();
                var shot = DirectHit(rig, h, boar);
                CheckHarmlessHook(c, $"T21 throw {i}", rig, h, boar, before, shot);
                yield return FixedFor(0.3f, pin);
                Release(boar, h);
            }
            c.Check(Tap.CountSince(mark, "Harpoon passed tame") == 0, "T21: no 'Harpoon passed tame' Debug line");
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- harpoon.chase: T22 ----------

    private static IEnumerator RunChase()
    {
        var c = new Checks(ChaseName);
        var rig = Rig.Create(ChaseName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        try
        {
            var h = Harpoon.Resolve(c);
            if (h == null)
            {
                c.Report();
                yield break;
            }
            var p = rig.P;
            yield return rig.Stage(10f, 8f, c);

            // T22: stray wolf running after a deer 20 m further down the lane.
            var deerSpot = rig.At(24f);
            var deer = rig.Spawn("Deer", deerSpot, false);
            var wolf = rig.Spawn("Wolf", rig.At(4f, -2f), true);
            var wolfAi = wolf != null ? wolf.GetBaseAI() as MonsterAI : null;
            if (!c.Check(deer != null && wolf != null && wolfAi != null, "T22: could not spawn the Wolf or the Deer"))
            {
                c.Report();
                yield break;
            }
            Tough(deer);
            Action pin = () => Place(deer, deerSpot, -rig.Dir);
            yield return FixedFor(0.5f, pin);
            var from = wolf.transform.position;
            GiveTarget(wolfAi, deer);
            wolfAi.SetAlerted(true);
            yield return FixedFor(1.2f, () =>
            {
                pin();
                if (wolfAi.GetTargetCreature() == null)
                {
                    GiveTarget(wolfAi, deer);
                }
            });
            var ran = HDist(from, wolf.transform.position);
            c.Check(wolfAi.GetTargetCreature() == deer && ran > 0.5f, $"T22 setup: the wolf runs after the deer (moved {F(ran)} m in 1.2 s, target {(TargetOf(wolf) != null ? TargetOf(wolf).m_name : "none")})");
            var wolfBefore = Snap.Take(wolf);
            var mark = Tap.Mark();
            ClearCenter();
            var wolfShot = DirectHit(rig, h, wolf);
            CheckHarmlessHook(c, "T22 chasing wolf", rig, h, wolf, wolfBefore, wolfShot);
            var se = HookOn(wolf, h);
            var length = se != null ? se.m_baseDistance : 0f;
            var longest = 0f;
            yield return FixedFor(3f, () =>
            {
                pin();
                if (wolf != null && HookOn(wolf, h) != null)
                {
                    longest = Mathf.Max(longest, Vector3.Distance(p.transform.position, wolf.transform.position) - length);
                }
            });
            var held = wolf != null && HookOn(wolf, h) != null;
            var broke = Center() == Loc("$msg_harpoon_linebroke");
            c.Note($"T22: after 3 s with the player standing still the chasing wolf is {(held ? "held on the line" : broke ? "free: the line broke (vanilla distance rule)" : "free")}; "
                   + $"it got at most {F(longest)} m beyond the line length ({F(length)} m, the line breaks at +{F(h.Effect.m_breakDistance)} m)");
            c.Check(held, $"T22: 3 s later the chasing wolf is still held on the line (hooked {held}, message '{Center()}')");
            c.Check(wolf != null && Near(wolf.GetHealth(), wolfBefore.Health) && TargetOf(wolf) != p, "T22: still unharmed and not after the player");
            if (held)
            {
                // Walk away: pulled toward the player like any hooked creature, although it wants to run the other way.
                var pull = new PullResult();
                var at = rig.Along(wolf.transform.position);
                yield return Walk(rig, h, wolf, WalkSpeed, 1.9f, pull);
                c.Check(!pull.Ended && pull.Moved >= 2f,
                    $"T22: walking away pulls the held wolf toward the player: the player went {F(pull.Walked)} m in {F(pull.Seconds)} s, the wolf came {F(pull.Moved)} m "
                    + $"(started {F(at)} m down the lane, line stretched at most {F(pull.Stretch)} m, hook lost {pull.Ended}, message '{Center()}')");
                c.Check(wolf != null && Near(wolf.GetHealth(), wolfBefore.Health) && ReferenceEquals(wolf.m_lastHit, wolfBefore.LastHit) && TargetOf(wolf) != p,
                    "T22: after the pull still unharmed and not after the player");
            }
            c.Check(Tap.CountSince(mark, "Harpoon passed tame") == 0, "T22: no 'Harpoon passed tame' Debug line");
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- harpoon.credit: T14 ----------

    private static IEnumerator RunCredit()
    {
        var c = new Checks(CreditName);
        var rig = Rig.Create(CreditName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        try
        {
            var h = Harpoon.Resolve(c);
            if (h == null)
            {
                c.Report();
                yield break;
            }
            var p = rig.P;
            yield return rig.Stage(10f, 3f, c);
            var key = AttackerKey(p);
            var listed = false;
            foreach (var info in ZNet.instance.GetPlayerList())
            {
                listed |= info.m_name == p.GetPlayerName();
            }
            c.Check(listed, $"setup: the player '{p.GetPlayerName()}' is in the game's player list (kill credit is given to listed players)");

            // Fresh tame, hooked, then killed by a wild wolf: not the player's kill.
            var boar = rig.Spawn("Boar", rig.At(8f), true);
            if (!c.Check(boar != null, "could not spawn a Boar"))
            {
                c.Report();
                yield break;
            }
            yield return FixedFor(0.6f);
            var name = boar.m_name;
            var zdo = boar.m_nview.GetZDO();
            var marksBefore = Marks(zdo, key);
            c.Check(!zdo.GetBool(key) && zdo.GetInt(ZDOVars.s_attackers) == 0, $"setup: fresh tame, never hit ({marksBefore})");
            var before = Snap.Take(boar);
            var mark = Tap.Mark();
            ClearCenter();
            var shot = DirectHit(rig, h, boar);
            CheckHarmlessHook(c, "T14 fresh tame", rig, h, boar, before, shot);
            c.Check(Marks(zdo, key) == marksBefore, $"T14: after the hook the tame carries no attacker mark of the player ({marksBefore} -> {Marks(zdo, key)})");
            c.Check(Tap.CountSince(mark, "Cleared the attacker mark a harpoon hook left on " + name) == 1, "T14: one Debug line 'Cleared the attacker mark ...'");
            Release(boar, h);
            var kills = Stat(PlayerStatType.EnemyKills);
            var boarKills = EnemyStat(0, name);
            var lastHits = Stat(PlayerStatType.EnemyKillsLastHits);
            var wolf = rig.Spawn("Wolf", rig.At(8f, 2.5f), false);
            c.Check(wolf != null, "could not spawn the wild Wolf");
            yield return FixedFor(0.3f);
            Kill(boar, wolf); // the wolf's deadly bite (if it did not bite first by itself)
            var box = new Box();
            yield return Until(() => boar == null, 4f, box);
            yield return FixedFor(0.3f);
            c.Check(box.Ok, "T14: the hooked tame died from the wild wolf");
            c.Check(Near(Stat(PlayerStatType.EnemyKills), kills) && Near(EnemyStat(0, name), boarKills) && Near(Stat(PlayerStatType.EnemyKillsLastHits), lastHits),
                $"T14: Enemy Kills unchanged ({F(kills)} -> {F(Stat(PlayerStatType.EnemyKills))}, {name} {F(boarKills)} -> {F(EnemyStat(0, name))})");
            rig.Destroy(wolf);

            // Control: hooked, then killed by the player's knife hit: +1, once.
            var second = rig.Spawn("Boar", rig.At(8f), true);
            if (c.Check(second != null, "could not spawn the second Boar"))
            {
                yield return FixedFor(0.6f);
                DirectHit(rig, h, second);
                c.Check(HookOn(second, h) != null, "T14 control: the second tame is hooked");
                Release(second, h);
                kills = Stat(PlayerStatType.EnemyKills);
                boarKills = EnemyStat(0, name);
                Kill(second, p, Skills.SkillType.Knives);
                yield return Until(() => second == null, 4f, box);
                yield return FixedFor(0.3f);
                c.Check(box.Ok, "T14 control: the hooked tame died from the player's knife hit");
                c.Check(Near(Stat(PlayerStatType.EnemyKills), kills + 1f) && Near(EnemyStat(0, name), boarKills + 1f),
                    $"T14 control: Enemy Kills +1, exactly once ({F(kills)} -> {F(Stat(PlayerStatType.EnemyKills))}, {name} {F(boarKills)} -> {F(EnemyStat(0, name))})");
            }
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- harpoon.credit-kept: T23 ----------

    private static IEnumerator RunCreditKept()
    {
        var c = new Checks(CreditKeptName);
        var rig = Rig.Create(CreditKeptName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        try
        {
            var h = Harpoon.Resolve(c);
            if (h == null)
            {
                c.Report();
                yield break;
            }
            var p = rig.P;
            yield return rig.Stage(10f, 3f, c);
            var boar = rig.Spawn("Boar", rig.At(8f), true);
            if (!c.Check(boar != null, "could not spawn a Boar"))
            {
                c.Report();
                yield break;
            }
            yield return FixedFor(0.6f);
            var name = boar.m_name;
            var zdo = boar.m_nview.GetZDO();
            var key = AttackerKey(p);

            // A real, damaging arrow-like hit first (in game: PvP on and the Bow). The tame must survive it.
            var arrow = new HitData();
            arrow.m_damage.m_pierce = 3f;
            arrow.m_skill = Skills.SkillType.Bows;
            arrow.m_ranged = true;
            arrow.m_point = boar.GetCenterPoint();
            arrow.m_dir = rig.Dir;
            arrow.SetAttacker(p);
            boar.Damage(arrow);
            yield return FixedFor(0.2f);
            if (!c.Check(boar != null && boar.GetHealth() > 0f && boar.GetHealth() < boar.GetMaxHealth() - 0.01f,
                    $"setup: the arrow hit hurt the tame without killing it ({(boar != null ? F(boar.GetHealth()) : "dead")} of {(boar != null ? F(boar.GetMaxHealth()) : "?")})"))
            {
                c.Report();
                yield break;
            }
            var marks = Marks(zdo, key);
            c.Check(zdo.GetBool(key) && zdo.GetInt(ZDOVars.s_attackers) == 1 && zdo.GetInt(ZDOVars.s_modifiers, (int)KillModifiers.CountNone) == (int)KillModifiers.Ranged,
                $"setup: vanilla marked the player as attacker, kill type Ranged ({marks})");

            // Hook right after (vanilla would write the kill type as mixed during the hook).
            var before = Snap.Take(boar);
            var mark = Tap.Mark();
            ClearCenter();
            var shot = DirectHit(rig, h, boar);
            CheckHarmlessHook(c, "T23 hook after a real hit", rig, h, boar, before, shot, false);
            c.Check(Marks(zdo, key) == marks, $"T23: the earlier hit keeps its mark and its Ranged kill type ({marks} -> {Marks(zdo, key)})");
            c.Check(Tap.CountSince(mark, "Cleared the attacker mark a harpoon hook left on " + name) == 1,
                "T23: one Debug line 'Cleared the attacker mark ...' (the hook's own trace, the kill type, was put back)");
            Release(boar, h);

            // It dies from something that is not the player: the arrow's credit counts, as a ranged kill.
            var kills = Stat(PlayerStatType.EnemyKills);
            var all = EnemyStat(0, name);
            var ranged = EnemyStat((int)KillModifiers.Ranged, name);
            var melee = EnemyStat((int)KillModifiers.Melee, name);
            Kill(boar, null);
            var box = new Box();
            yield return Until(() => boar == null, 4f, box);
            yield return FixedFor(0.3f);
            c.Check(box.Ok, "T23: the tame died from a hit without attacker");
            c.Check(Near(Stat(PlayerStatType.EnemyKills), kills + 1f) && Near(EnemyStat(0, name), all + 1f),
                $"T23: Enemy Kills +1 from the earlier arrow hit ({F(kills)} -> {F(Stat(PlayerStatType.EnemyKills))})");
            c.Check(Near(EnemyStat((int)KillModifiers.Ranged, name), ranged + 1f) && Near(EnemyStat((int)KillModifiers.Melee, name), melee),
                $"T23: counted as a ranged kill, not as a melee one (ranged {F(ranged)} -> {F(EnemyStat((int)KillModifiers.Ranged, name))}, melee {F(melee)} -> {F(EnemyStat((int)KillModifiers.Melee, name))})");
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- harpoon.shield: T15 ----------

    private static IEnumerator RunShield()
    {
        var c = new Checks(ShieldName);
        var rig = Rig.Create(ShieldName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        try
        {
            var h = Harpoon.Resolve(c);
            if (h == null)
            {
                c.Report();
                yield break;
            }
            var p = rig.P;
            rig.KeepSkill(Skills.SkillType.Spears);
            yield return rig.Stage(10f, 3f, c);

            // The bubble: the game's protection effect (the one the Staff of Protection's area effect names, when
            // the staff data say so; the real cast is not played here).
            SE_Shield asset = null;
            var staff = ItemOf("StaffShield");
            var staffAttack = staff != null ? staff.m_shared.m_attack : null;
            var staffArea = staffAttack != null && staffAttack.m_attackProjectile != null ? staffAttack.m_attackProjectile.GetComponent<Aoe>() : null;
            if (staffArea == null && staffAttack != null && staffAttack.m_spawnOnTrigger != null)
            {
                staffArea = staffAttack.m_spawnOnTrigger.GetComponent<Aoe>();
            }
            if (staffArea != null && !string.IsNullOrEmpty(staffArea.m_statusEffect))
            {
                asset = ObjectDB.instance.GetStatusEffect(staffArea.m_statusEffect.GetStableHashCode()) as SE_Shield;
                c.Note($"Staff of Protection data: area effect {staffArea.name} gives '{staffArea.m_statusEffect}' (a protection bubble: {asset != null}), "
                       + $"hits friendly {staffArea.m_hitFriendly}, hits enemies {staffArea.m_hitEnemy}, hits characters {staffArea.m_hitCharacters}: "
                       + $"by its data the bubble {(staffArea.m_hitFriendly && staffArea.m_hitCharacters ? "reaches" : "does not reach")} tames (the real cast was not played)");
            }
            else
            {
                c.Note($"Staff of Protection data: no area effect with a status effect found on StaffShield (item found {staff != null}); whether its bubble reaches tames is not known from here");
            }
            if (asset == null)
            {
                foreach (var effect in ObjectDB.instance.m_StatusEffects)
                {
                    if (effect is SE_Shield found)
                    {
                        asset = found;
                        break;
                    }
                }
            }
            if (!c.Check(asset != null, "no protection bubble effect (SE_Shield) in the game data"))
            {
                c.Report();
                yield break;
            }
            var tame = rig.Spawn("Boar", rig.At(8f), true);
            if (!c.Check(tame != null, "could not spawn a Boar"))
            {
                c.Report();
                yield break;
            }
            yield return FixedFor(0.6f);
            var bubble = tame.GetSEMan().AddStatusEffect(asset, false, 1, 0f) as SE_Shield;
            if (!c.Check(bubble != null && bubble.m_totalAbsorbDamage > 0f, $"setup: the tame carries the bubble '{asset.name}'"))
            {
                c.Report();
                yield break;
            }
            yield return FixedFor(0.2f);

            var absorbed = bubble.m_damage;
            var before = Snap.Take(tame);
            ClearCenter();
            var shot = DirectHit(rig, h, tame);
            CheckHarmlessHook(c, "T15 tame under a bubble", rig, h, tame, before, shot);
            c.Check(ReferenceEquals(tame.GetSEMan().GetStatusEffect(asset.NameHash()), bubble) && Near(bubble.m_damage, absorbed, 0.0001f),
                $"T15: the bubble is still there and not weakened (absorbed {F(absorbed)} -> {F(bubble.m_damage)} of {F(bubble.m_totalAbsorbDamage)})");
            Release(tame, h);

            // Control: the same throw without the mod (PvP on) does weaken the bubble.
            p.SetPVP(true);
            yield return null;
            TestSwitches.ThrowerSideOff = true;
            DirectHit(rig, h, tame);
            TestSwitches.ThrowerSideOff = false;
            p.SetPVP(false);
            c.Check(bubble.m_damage > absorbed + 0.01f, $"T15 control: thrown without the mod (PvP on) the harpoon weakens the bubble (absorbed {F(absorbed)} -> {F(bubble.m_damage)})");
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- harpoon.ship: T17 ----------

    private static IEnumerator RunShip()
    {
        var c = new Checks(ShipName);
        var rig = Rig.Create(ShipName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        try
        {
            var h = Harpoon.Resolve(c);
            if (h == null)
            {
                c.Report();
                yield break;
            }
            var p = rig.P;
            yield return rig.Stage(12f, 8f, c);
            var prefab = ZNetScene.instance.GetPrefab("Karve");
            if (!c.Check(prefab != null && prefab.GetComponent<Ship>() != null, "prefab Karve not found"))
            {
                c.Report();
                yield break;
            }
            // The Karve sits on the ground of the lane (no water needed: the game only asks "standing on a ship").
            var shipObject = Object.Instantiate(prefab, rig.At(9f) + Vector3.up * 0.6f, Quaternion.LookRotation(rig.Right));
            rig.Track(shipObject);
            var ship = shipObject.GetComponent<Ship>();
            yield return FixedFor(2.5f);
            var boar = rig.Spawn("Boar", shipObject.transform.position + Vector3.up * 1.6f, true);
            if (!c.Check(boar != null, "could not spawn a Boar"))
            {
                c.Report();
                yield break;
            }
            if (boar.GetBaseAI() != null)
            {
                boar.GetBaseAI().enabled = false; // it must not walk off the deck
            }
            var box = new Box();
            Vector3[] drops = { new Vector3(0f, 1.6f, 0f), new Vector3(0f, 1.6f, -1f), new Vector3(0f, 1.6f, 1f), new Vector3(0f, 2.4f, 0f) };
            foreach (var drop in drops)
            {
                Place(boar, shipObject.transform.TransformPoint(drop), rig.Dir);
                yield return Until(() => boar.GetStandingOnShip() == ship, 2.5f, box);
                if (box.Ok)
                {
                    break;
                }
            }
            if (!c.Check(box.Ok, "setup: the tame never stood on the Karve's deck (Character.GetStandingOnShip); the ship part could not be played"))
            {
                c.Report();
                yield break;
            }
            yield return FixedFor(0.5f);

            // Hooked from the ground beside the ship, no harm.
            rig.PlacePlayerAlong(0f);
            var before = Snap.Take(boar);
            ClearCenter();
            var shot = DirectHit(rig, h, boar);
            CheckHarmlessHook(c, "T17 tame on a Karve deck", rig, h, boar, before, shot);
            var se = HookOn(boar, h);
            if (!c.Check(se != null && boar.GetStandingOnShip() == ship, "setup: hooked while standing on the deck"))
            {
                c.Report();
                yield break;
            }

            // Not pulled while on the deck: 2.5 m beyond the line length for 2 s, stamina in use.
            yield return rig.StaminaOn(c);
            p.m_stamina = p.GetMaxStamina();
            var local = shipObject.transform.InverseTransformPoint(boar.transform.position);
            var stamina = p.GetStamina();
            var hold = rig.Along(boar.transform.position) - (se.m_baseDistance + 2.5f);
            var onDeck = true;
            yield return FixedFor(2f, () =>
            {
                rig.PlacePlayerAlong(hold);
                onDeck &= boar != null && boar.GetStandingOnShip() == ship;
            });
            var drift = (shipObject.transform.InverseTransformPoint(boar.transform.position) - local).magnitude;
            c.Check(onDeck && HookOn(boar, h) != null, $"T17: it stayed on the deck and hooked for the 2 s (on deck {onDeck}, hooked {HookOn(boar, h) != null})");
            c.Check(drift < 0.5f && Near(p.GetStamina(), stamina, 0.01f),
                $"T17: not pulled while it stands on the deck: moved {F(drift)} m on the deck, stamina {F(stamina)} -> {F(p.GetStamina())}");

            // Control: off the deck the same stance does pull.
            Release(boar, h);
            Place(boar, rig.At(4.5f), rig.Dir);
            yield return Until(() => boar.GetStandingOnShip() == null, 2f, box);
            rig.PlacePlayerAlong(0f);
            DirectHit(rig, h, boar, false);
            se = HookOn(boar, h);
            if (c.Check(se != null && box.Ok, "T17 control: hooked again on the ground beside the ship"))
            {
                p.m_stamina = p.GetMaxStamina();
                stamina = p.GetStamina();
                var from = boar.transform.position;
                hold = rig.Along(boar.transform.position) - (se.m_baseDistance + 2.5f);
                var low = stamina;
                yield return FixedFor(2f, () =>
                {
                    rig.PlacePlayerAlong(hold);
                    low = Mathf.Min(low, p.GetStamina());
                });
                c.Check(HDist(from, boar.transform.position) > 0.5f || low < stamina - 0.01f,
                    $"T17 control: on the ground the same stance pulls it (moved {F(HDist(from, boar.transform.position))} m, stamina {F(stamina)} -> {F(low)})");
            }
            p.m_stamina = p.GetMaxStamina();
            yield return rig.StaminaOff();
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- harpoon.toggle: T18 (T19 without the restart) ----------

    // Harpoon against a tame while the mod is off: vanilla, it flies past (PvP off).
    private static void CheckPassesThrough(Checks c, string tag, Rig rig, Harpoon h, Character tame)
    {
        var hits = Stat(PlayerStatType.EnemyHits);
        var before = Snap.Take(tame);
        ClearCenter();
        var shot = DirectHit(rig, h, tame);
        c.Check(!shot.Valid && !shot.Stopped, $"{tag}: the harpoon flies through the tame (allowed {shot.Valid}, stopped {shot.Stopped})");
        c.Check(HookOn(tame, h) == null && Untouched(tame, before) && Center() == "" && Near(Stat(PlayerStatType.EnemyHits), hits),
            $"{tag}: not hooked, no message, nothing reached it ({Describe(tame, before)}, message '{Center()}')");
    }

    private static IEnumerator RunToggle()
    {
        var c = new Checks(ToggleName);
        var rig = Rig.Create(ToggleName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        try
        {
            var h = Harpoon.Resolve(c);
            var found = FeatureRegistry.Find(ModInfo.Guid);
            if (h == null || !c.Check(found != null, "the mod is not in the MC Mods list"))
            {
                c.Report();
                yield break;
            }
            var view = found.Value;
            yield return rig.Stage(10f, 3f, c);
            var tame = rig.Spawn("Boar", rig.At(8f), true);
            var held = rig.Spawn("Boar", rig.At(8f, 4f), true);
            if (!c.Check(tame != null && held != null, "could not spawn the Boars"))
            {
                c.Report();
                yield break;
            }
            yield return FixedFor(0.6f);
            c.Check(view.IsActive && AllPatched(), $"on: the mod is active and its three game patches are in place ({view.State})");
            c.Check(!rig.P.IsPVPEnabled(), "setup: PvP off");
            DirectHit(rig, h, held);
            c.Check(HookOn(held, h) != null, "setup: a second tame is hooked before the mod goes off");

            // Off, live. Same framework refresh as unticking the mod in MC Mods (the test uses the mod's in-memory
            // blocker instead of the Enabled setting, which would be written to the player's config file).
            TestSwitches.Blocker = "Inactive: turned off by a self test.";
            FeatureRegistry.RefreshAll();
            yield return null;
            c.Check(!view.IsActive && NonePatched(), $"off: the mod is inactive and none of its patches is left ({view.State}: {view.Status})");
            CheckPassesThrough(c, "T18 off", rig, h, tame);
            yield return FixedFor(0.5f);
            c.Check(HookOn(held, h) != null, "off: the tame hooked before keeps its line (README: until it breaks or is released)");
            Release(held, h);

            // On again, no restart.
            TestSwitches.Blocker = null;
            FeatureRegistry.RefreshAll();
            yield return null;
            c.Check(view.IsActive && AllPatched(), $"on again: active, patches back ({view.State}: {view.Status})");
            var before = Snap.Take(tame);
            ClearCenter();
            var shot = DirectHit(rig, h, tame);
            CheckHarmlessHook(c, "T18 on again", rig, h, tame, before, shot);
            c.Check(!HitScope.DebugIsOpen, "on again: no protection scope left open");
            c.Report();
        }
        finally
        {
            if (TestSwitches.Blocker != null)
            {
                TestSwitches.Blocker = null;
                FeatureRegistry.RefreshAll();
            }
            rig.Done();
        }
    }

    // ---------- harpoon.handoff: single-player stand-ins for M01, M02, M05, M06 ----------

    private static IEnumerator RunHandoff()
    {
        var c = new Checks(HandoffName);
        var rig = Rig.Create(HandoffName, c);
        if (rig == null)
        {
            c.Report();
            yield break;
        }
        try
        {
            var h = Harpoon.Resolve(c);
            if (h == null)
            {
                c.Report();
                yield break;
            }
            var p = rig.P;
            rig.KeepSkill(Skills.SkillType.Spears);
            yield return rig.Stage(10f, 12f, c);
            var key = AttackerKey(p);
            var box = new Box();

            // M01: the game that runs the tame has no mod (owner-side patches off). The thrower's side alone makes
            // the hit harmless; vanilla on the owner hooks, pulls, and keeps its attacker mark.
            var first = rig.Spawn("Boar", rig.At(8f), true);
            if (!c.Check(first != null, "could not spawn a Boar"))
            {
                c.Report();
                yield break;
            }
            yield return FixedFor(0.6f);
            var name = first.m_name;
            var zdo = first.m_nview.GetZDO();
            TestSwitches.OwnerSideOff = true;
            var before = Snap.Take(first);
            var mark = Tap.Mark();
            ClearCenter();
            var shot = DirectHit(rig, h, first);
            CheckHarmlessHook(c, "M01 tame run by a game without the mod", rig, h, first, before, shot);
            c.Check(zdo.GetBool(key) && zdo.GetInt(ZDOVars.s_attackers) == 1,
                $"M01/M05: that game keeps the vanilla attacker mark (documented limit) ({Marks(zdo, key)})");
            c.Check(Tap.CountSince(mark, "Cleared the attacker mark") == 0, "M01: nothing cleared the mark there");
            var pull = new PullResult();
            yield return Walk(rig, h, first, WalkSpeed, 2.8f, pull);
            c.Check(pull.Moved >= 2f && !pull.Ended && first != null && Near(first.GetHealth(), first.GetMaxHealth()),
                $"M01: hooked that way it is dragged like any creature, unharmed (the player went {F(pull.Walked)} m in {F(pull.Seconds)} s, it came {F(pull.Moved)} m, hook lost {pull.Ended})");
            Release(first, h);

            // M05, owner without the mod: the tame dies later from something else: the thrower is credited.
            var kills = Stat(PlayerStatType.EnemyKills);
            Kill(first, null);
            yield return Until(() => first == null, 4f, box);
            yield return FixedFor(0.3f);
            c.Check(box.Ok && Near(Stat(PlayerStatType.EnemyKills), kills + 1f),
                $"M05: owner without the mod (or with it turned off): its later death counts as the thrower's kill, +1 ({F(kills)} -> {F(Stat(PlayerStatType.EnemyKills))})");
            TestSwitches.OwnerSideOff = false;

            // M05, owner with the mod on: no +1.
            var second = rig.Spawn("Boar", rig.At(8f), true);
            if (c.Check(second != null, "could not spawn the second Boar"))
            {
                rig.PlacePlayerAlong(0f);
                yield return FixedFor(0.6f);
                DirectHit(rig, h, second);
                c.Check(HookOn(second, h) != null && !second.m_nview.GetZDO().GetBool(key), "M05: owner with the mod on: hooked, no attacker mark");
                Release(second, h);
                kills = Stat(PlayerStatType.EnemyKills);
                Kill(second, null);
                yield return Until(() => second == null, 4f, box);
                yield return FixedFor(0.3f);
                c.Check(box.Ok && Near(Stat(PlayerStatType.EnemyKills), kills),
                    $"M05: owner with the mod on: its later death is not the thrower's kill ({F(kills)} -> {F(Stat(PlayerStatType.EnemyKills))})");
            }

            // M06: the thrower has the mod turned off, PvP on; the tame's game runs the mod: plain vanilla hit.
            var third = rig.Spawn("Boar", rig.At(8f), true);
            if (c.Check(third != null, "could not spawn the third Boar"))
            {
                Tough(third);
                yield return FixedFor(0.6f);
                var thirdZdo = third.m_nview.GetZDO();
                p.SetPVP(true);
                yield return null;
                before = Snap.Take(third);
                mark = Tap.Mark();
                TestSwitches.ThrowerSideOff = true;
                shot = DirectHit(rig, h, third);
                TestSwitches.ThrowerSideOff = false;
                p.SetPVP(false);
                c.Check(shot.Valid && HookOn(third, h) != null && third.GetHealth() < before.Health - 0.01f && !ReferenceEquals(third.m_lastHit, before.LastHit),
                    $"M06: thrower's mod off, PvP on: the tame is hooked and damaged as in vanilla ({Describe(third, before)})");
                c.Check(thirdZdo.GetBool(key), $"M06: the damaging hit keeps its vanilla kill credit; the owner's mod leaves it ({Marks(thirdZdo, key)})");
                c.Check(Tap.CountSince(mark, "Cleared the attacker mark") == 0 && Tap.CountSince(mark, "Hooked tame") == 0 && !HitScope.DebugIsOpen,
                    "M06: the mod on the owner's side did nothing to that hit");
                Release(third, h);

                // M02: thrower without the mod, PvP off: flies past, whatever the tame's game runs.
                yield return null;
                TestSwitches.ThrowerSideOff = true;
                before = Snap.Take(third);
                shot = DirectHit(rig, h, third);
                TestSwitches.ThrowerSideOff = false;
                c.Check(!shot.Valid && !shot.Stopped && HookOn(third, h) == null && Untouched(third, before),
                    "M02: thrower without the mod, PvP off: the harpoon flies through the tame although the tame's game runs the mod");
            }

            // M02: a harpoon that is not the local player's is never made to hit by this mod.
            var skeleton = rig.Spawn("Skeleton_Friendly", rig.At(5f, -4f), true);
            if (c.Check(skeleton != null && third != null, "could not spawn the skeleton that owns the foreign harpoon"))
            {
                yield return FixedFor(0.4f);
                var foreign = Probe(rig, h, skeleton);
                var valid = foreign.IsValidTarget(third);
                rig.Destroy(foreign);
                c.Check(!valid, "M02: a harpoon thrown by somebody else is left to vanilla (it does not hit the tame)");
            }
            c.Check(!TameRules.IsTame(p), "players are never treated as tames");
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- harpoon.log: T20 ----------

    private static IEnumerator RunLog()
    {
        var errors = Tap.Errors(out var first, out var lines);
        if (errors == 0 && lines == 0)
        {
            // Listener heard nothing of this mod at all (test run alone, or listener lost): nothing was looked at.
            SelfTest.Fail(LogName, "the log listener saw no line of this mod since it started, so nothing was checked (run it after the other harpoon tests)");
        }
        else if (errors == 0)
        {
            SelfTest.Pass(LogName, $"no error or exception line of this mod (or naming it) in the log since it started; {lines} lines of this mod read");
        }
        else
        {
            SelfTest.Fail(LogName, $"{errors} error line(s) of this mod (or naming it) since it started, first: {first}");
        }
        yield break;
    }
}
#endif
