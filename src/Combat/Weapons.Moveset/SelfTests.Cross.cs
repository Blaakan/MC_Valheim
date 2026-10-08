#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Bootstrap;
using HarmonyLib;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Combat.WeaponsMovesetMod;

// Debug build only. In-world self tests of items the moves must leave alone and of the other mods (TESTING "other
// mods"). Each other-mod test is its own small test: it needs that mod installed and active.
//   moveset.exclusions-ranged  T11 / X06: crossbow, staff, harpoon and bow keep their own attack after a jump and a
//                              roll; buckler + empty right hand = fist moves
//   moveset.x.crossbow         X01 (Crossbow Stays Loaded): crossbow shot after a jump and a roll is vanilla and uses
//                              a bolt; still loaded after a sword roll attack
//   moveset.x.dualwield        X02 (Dual Wielding): pair moves, which hand strikes, durability, pair combo
//   moveset.x.dualwield-off    pair with the pair's roll attack Off: refused inside the roll, normal pair swing after
//   moveset.x.towershield      X03 (Tower Shield Wall): bash after a jump / a roll stays its bash, with its cooldown
//   moveset.x.sneak            X04 (Sneak Ambush): sneak roll attack backstabs, sneak XP once; a jump ends the crouch
//   moveset.x.gco              X07: Goo's Combat Overhaul stand-down (pretended), dash from a dash mod (pretended)
//   moveset.x.killcount        X08 (Creature Kill and Tame Counts): roll attack kill = melee kill, like a normal swing
internal static partial class SelfTests
{
    private const string RangedName = "moveset.exclusions-ranged";
    private const string CrossbowName = "moveset.x.crossbow";
    private const string DualName = "moveset.x.dualwield";
    private const string DualOffName = "moveset.x.dualwield-off";
    private const string TowerName = "moveset.x.towershield";
    private const string SneakName = "moveset.x.sneak";
    private const string GcoName = "moveset.x.gco";
    private const string KillName = "moveset.x.killcount";

    private const string CrossbowGuid = "MC.Combat.Crossbow.StaysLoaded";
    private const string DualGuid = "MC.Combat.Weapons.DualWield";
    private const string TowerGuid = "MC.Combat.Shields.TowerWall";
    private const string SneakGuid = "MC.Combat.Sneak.Ambush";
    private const string StatsGuid = "MC.Exploration.Stats.PerCreature";
    private const string DualLogName = "Dual Wielding"; // its name in the log

    private static void RegisterCross()
    {
        SelfTest.Register(RangedName, RunRanged);
        SelfTest.Register(CrossbowName, RunCrossbow);
        SelfTest.Register(DualName, RunDual);
        SelfTest.Register(DualOffName, RunDualOff);
        SelfTest.Register(TowerName, RunTower);
        SelfTest.Register(SneakName, RunSneak);
        SelfTest.Register(GcoName, RunGco);
        SelfTest.Register(KillName, RunKill);
    }

    private static void UnregisterCross()
    {
        SelfTest.Unregister(RangedName);
        SelfTest.Unregister(CrossbowName);
        SelfTest.Unregister(DualName);
        SelfTest.Unregister(DualOffName);
        SelfTest.Unregister(TowerName);
        SelfTest.Unregister(SneakName);
        SelfTest.Unregister(GcoName);
        SelfTest.Unregister(KillName);
    }

    private static bool OtherActive(string guid)
    {
        var view = FeatureRegistry.Find(guid);
        return view != null && view.Value.IsActive;
    }

    // A number setting of another plugin, read from its config (fallback when not there).
    private static float OtherSetting(string guid, string key, float fallback)
    {
        if (!Chainloader.PluginInfos.TryGetValue(guid, out var info) || info.Instance == null)
        {
            return fallback;
        }
        var config = info.Instance.Config;
        foreach (var def in config.Keys.ToArray())
        {
            if (def.Key == key && config[def].BoxedValue is float value)
            {
                return value;
            }
        }
        return fallback;
    }

    // ---------- moveset.exclusions-ranged ----------

    // Ready to fire without waiting: crossbow loaded (and no reload queued), staff has eitr. In memory only.
    private static void Ready(Player p, ItemDrop.ItemData item)
    {
        var attack = item.m_shared.m_attack;
        if (attack.m_requiresReload)
        {
            p.CancelReloadAction();
            p.SetWeaponLoaded(item);
        }
        if (attack.m_attackEitr > 0f)
        {
            // The game refuses a staff with no eitr food eaten (max eitr 0) and puts both back within a second.
            var need = attack.m_attackEitr * 3f;
            p.m_maxEitr = Mathf.Max(p.m_maxEitr, need);
            p.m_eitr = Mathf.Max(p.m_eitr, need);
        }
    }

    private static IEnumerator RunRanged()
    {
        var p = LocalPlayer(RangedName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(RangedName);
        var rig = new Rig(RangedName, p);
        var eitr = p.m_eitr;
        var maxEitr = p.m_maxEitr;
        try
        {
            ServerRules.TestRules = Loose();
            yield return RangedRow(rig, c, "CrossbowArbalest", "BoltBone");
            yield return RangedRow(rig, c, "StaffFireball", null);
            yield return RangedRow(rig, c, "SpearChitin", null);
            yield return BowRow(rig, c);
            yield return BucklerRow(rig, c);
            c.Report();
        }
        finally
        {
            if (p != null)
            {
                p.m_maxEitr = maxEitr;
                p.m_eitr = eitr;
            }
            rig.Restore();
        }
    }

    // Like ExclusionRow, for items that shoot or throw: jump then press, roll then press = the item's own attack, no
    // move. The item comes off right after the start, so nothing flies.
    private static IEnumerator RangedRow(Rig rig, Checks c, string name, string ammo)
    {
        var p = rig.P;
        if (Prefab(name) == null || (ammo != null && Prefab(ammo) == null))
        {
            c.Check(false, $"{name}{(ammo != null ? " and " + ammo : "")} exist in this game");
            yield break;
        }
        yield return WaitIdle(p, 4f);
        p.UnequipItem(p.m_rightItem, false);
        p.UnequipItem(p.m_leftItem, false);
        var item = rig.Give(name);
        var shots = ammo != null ? rig.Give(ammo, 5) : null;
        var box = new Box<Attack>();
        var edge = new Box<float>();
        for (var roll = 0; roll < 2; roll++)
        {
            var what = roll == 1 ? "after a roll" : "in a jump";
            yield return WaitIdle(p, 4f);
            if (item == null || (!p.IsItemEquiped(item) && !p.EquipItem(item, false)))
            {
                c.Check(false, $"{name}: could not equip");
                break;
            }
            Ready(p, item);
            yield return WaitIdle(p, 3f);
            Put(p, rig.Home);
            Face(p, rig.Forward);
            yield return Fixed;
            var shared = item.m_shared.m_attack;
            var mark = LogTap.Mark;
            var before = MoveTracker.LastMove.Clone;
            var last = p.m_currentAttack;
            if (roll == 0)
            {
                p.Jump();
                c.Check(MoveTracker.JumpLive, $"{name}: jump token live");
                yield return WaitTicks(0.1f);
                Ready(p, item);
                Press(p);
            }
            else
            {
                Ready(p, item);
                p.Dodge(rig.Forward);
                yield return WaitRollEdge(p, 4f, true, edge);
                c.Check(ReferenceEquals(p.m_currentAttack, last), $"{name} {what}: no attack inside the roll");
                Ready(p, item);
            }
            yield return WaitNewAttack(p, last, 0.6f, box);
            var a = box.Value;
            c.Check(a != null, $"{name} {what}: its attack started");
            if (a != null)
            {
                c.Check(ReferenceEquals(MoveTracker.LastMove.Clone, before) && a.m_attackAnimation == shared.m_attackAnimation
                        && a.m_attackType == shared.m_attackType && a.m_attackChainLevels == shared.m_attackChainLevels
                        && Near(a.m_damageMultiplier, shared.m_damageMultiplier),
                    $"{name} {what}: its own attack {shared.m_attackAnimation}, no move (got {a.m_attackAnimation})");
                c.Check(!MoveTracker.JumpLive && float.IsNegativeInfinity(MoveTracker.RollEndAt), $"{name} {what}: tokens used up");
                c.Check(LogTap.Count(mark, Dbg, " attack: ") == 0, $"{name} {what}: no move Debug line");
                p.UnequipItem(item, false);
                ResetFired(p, a);
            }
            yield return WaitLanded(p, 3f);
        }
        yield return WaitIdle(p, 4f);
        rig.TakeBack(item);
        rig.TakeBack(shots);
    }

    // Bow: the attack starts when the drawn string is let go. In a jump: draw and let go in the air. After a roll:
    // draw during the roll, let go as it ends (the roll attack's moment).
    private static IEnumerator BowRow(Rig rig, Checks c)
    {
        var p = rig.P;
        if (Prefab("Bow") == null || Prefab("ArrowWood") == null)
        {
            c.Check(false, "Bow and ArrowWood exist in this game");
            yield break;
        }
        yield return WaitIdle(p, 4f);
        p.UnequipItem(p.m_rightItem, false);
        p.UnequipItem(p.m_leftItem, false);
        var bow = rig.Give("Bow");
        var arrows = rig.Give("ArrowWood", 5);
        var box = new Box<Attack>();
        rig.TakeController();
        for (var roll = 0; roll < 2; roll++)
        {
            var what = roll == 1 ? "after a roll" : "in a jump";
            Release(p);
            yield return WaitIdle(p, 4f);
            if (bow == null || (!p.IsItemEquiped(bow) && !p.EquipItem(bow, false)))
            {
                c.Check(false, "Bow: could not equip");
                break;
            }
            yield return WaitIdle(p, 3f);
            Put(p, rig.Home);
            Face(p, rig.Forward);
            Release(p);
            yield return Fixed;
            yield return Fixed;
            var shared = bow.m_shared.m_attack;
            var mark = LogTap.Mark;
            var before = MoveTracker.LastMove.Clone;
            var last = p.m_currentAttack;
            var air = false;
            if (roll == 0)
            {
                p.Jump();
                c.Check(MoveTracker.JumpLive, "Bow: jump token live");
                var until = Time.fixedTime + 0.3f;
                while (Time.fixedTime < until)
                {
                    Hold(p, false);
                    yield return Fixed;
                }
                air = !p.IsOnGround();
                Release(p);
            }
            else
            {
                p.Dodge(rig.Forward);
                var seen = false;
                var t0 = Time.fixedTime;
                while (Time.fixedTime - t0 < 4f)
                {
                    yield return Fixed;
                    if (p.m_inDodge)
                    {
                        seen = true;
                        Hold(p, false);
                    }
                    else if (seen)
                    {
                        break;
                    }
                }
                c.Check(seen && ReferenceEquals(p.m_currentAttack, last), $"Bow {what}: no attack inside the roll");
                Release(p);
            }
            yield return WaitNewAttack(p, last, 0.5f, box);
            var a = box.Value;
            c.Check(a != null && (roll == 1 || air), $"Bow {what}: letting the string go started its attack{(roll == 0 ? " in the air" : "")}");
            if (a != null)
            {
                c.Check(ReferenceEquals(MoveTracker.LastMove.Clone, before) && a.m_attackAnimation == shared.m_attackAnimation
                        && a.m_bowDraw && Near(a.m_damageMultiplier, shared.m_damageMultiplier),
                    $"Bow {what}: its own attack {shared.m_attackAnimation}, no move (got {a.m_attackAnimation})");
                c.Check(!MoveTracker.JumpLive && float.IsNegativeInfinity(MoveTracker.RollEndAt), $"Bow {what}: tokens used up");
                c.Check(LogTap.Count(mark, Dbg, " attack: ") == 0, $"Bow {what}: no move Debug line");
                p.UnequipItem(bow, false);
                ResetFired(p, a);
            }
            yield return WaitLanded(p, 3f);
        }
        rig.GiveController();
        yield return WaitIdle(p, 4f);
        rig.TakeBack(bow);
        rig.TakeBack(arrows);
    }

    // Buckler in the left hand, nothing in the right: the attack is the bare hands', so jump and roll attacks with fists.
    private static IEnumerator BucklerRow(Rig rig, Checks c)
    {
        var p = rig.P;
        if (Prefab("ShieldBronzeBuckler") == null || p.m_unarmedWeapon == null)
        {
            c.Check(false, "ShieldBronzeBuckler exists and the player has bare hands");
            yield break;
        }
        yield return WaitIdle(p, 4f);
        p.UnequipItem(p.m_rightItem, false);
        p.UnequipItem(p.m_leftItem, false);
        var buckler = rig.Give("ShieldBronzeBuckler");
        var fists = p.m_unarmedWeapon.m_itemData;
        var equipped = buckler != null && p.EquipItem(buckler, false);
        yield return WaitIdle(p, 3f);
        c.Check(equipped && ReferenceEquals(p.m_leftItem, buckler) && p.m_rightItem == null && ReferenceEquals(p.GetCurrentWeapon(), fists),
            "buckler in the left hand, nothing in the right: the player attacks with bare hands");
        yield return Settle(rig);
        var jump = new JumpRun();
        yield return JumpPress(rig, jump);
        c.Check(jump.IsMove && jump.Move.Kind == MoveKind.Jump && jump.Move.Family == WeaponFamily.Fists
                && jump.Move.Trigger == "unarmed_attack1" && ReferenceEquals(jump.Move.Weapon, fists),
            $"buckler + bare hand: the jump attack is the fists' unarmed_attack1 (got {jump.Move.Kind} {jump.Move.Trigger})");
        yield return AttackOver(p, jump.Started, 3f);
        yield return Settle(rig);
        var roll = new RollRun();
        yield return Roll(rig, roll);
        p.m_queuedAttackTimer = 0f;
        c.Check(roll.Clone != null && roll.Move.Kind == MoveKind.Roll && roll.Move.Family == WeaponFamily.Fists
                && roll.Move.Trigger == "unarmed_attack1" && ReferenceEquals(roll.Move.Weapon, fists) && roll.Move.Cut,
            $"buckler + bare hand: the roll attack is the fists' unarmed_attack1 (got {roll.Move.Kind} {roll.Move.Trigger})");
        yield return AttackOver(p, roll.Started, 3f);
        yield return WaitIdle(p, 4f);
        rig.TakeBack(buckler);
    }

    // ---------- moveset.x.crossbow ----------

    private static IEnumerator RunCrossbow()
    {
        var p = LocalPlayer(CrossbowName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(CrossbowName);
        var rig = new Rig(CrossbowName, p);
        try
        {
            ServerRules.TestRules = Loose();
            c.Check(OtherActive(CrossbowGuid), "Crossbow Stays Loaded is installed and active");
            if (Prefab("CrossbowArbalest") == null || Prefab("BoltBone") == null)
            {
                c.Check(false, "CrossbowArbalest and BoltBone exist in this game");
                c.Report();
                yield break;
            }
            yield return WaitIdle(p, 4f);
            p.UnequipItem(p.m_rightItem, false);
            p.UnequipItem(p.m_leftItem, false);
            var crossbow = rig.Give("CrossbowArbalest");
            var bolts = rig.Give("BoltBone", 5);
            var sword = rig.Give("SwordIron");
            if (crossbow == null || bolts == null || sword == null || !p.EquipItem(crossbow, false))
            {
                c.Check(false, "crossbow, bolts and sword given, crossbow equipped");
                c.Report();
                yield break;
            }
            var shot = crossbow.m_shared.m_attack;
            Ready(p, crossbow); // loaded like after a reload (no wait)
            yield return WaitIdle(p, 3f);
            var loaded = new Box<bool>();
            var fired = new Box<bool>();
            for (var roll = 0; roll < 2; roll++)
            {
                var what = roll == 1 ? "after a roll" : "in a jump";
                if (roll == 1)
                {
                    // The game's own reload after the first shot.
                    yield return WaitFor(() => p.IsWeaponLoaded(), 10f, loaded);
                    c.Check(loaded.Value, "the crossbow reloads by itself after the shot");
                    if (!loaded.Value)
                    {
                        Ready(p, crossbow);
                    }
                }
                yield return Settle(rig);
                Look(p, 35f); // the bolt goes into the ground a few metres ahead
                yield return Fixed;
                c.Check(p.IsWeaponLoaded(), $"crossbow loaded before the shot {what}");
                var mark = LogTap.Mark;
                var stack = bolts.m_stack;
                var before = MoveTracker.LastMove.Clone;
                Attack started;
                if (roll == 0)
                {
                    var jump = new JumpRun();
                    yield return JumpPress(rig, jump, 0.1f, 1.5f);
                    started = jump.Started;
                }
                else
                {
                    var run = new RollRun();
                    yield return Roll(rig, run);
                    p.m_queuedAttackTimer = 0f;
                    started = run.Started;
                    c.Check(run.Started != null && !run.StartedInRoll, "crossbow: no shot inside the roll");
                }
                c.Check(started != null && ReferenceEquals(MoveTracker.LastMove.Clone, before) && started.m_attackAnimation == shot.m_attackAnimation
                        && started.m_requiresReload && Near(started.m_damageMultiplier, shot.m_damageMultiplier),
                    $"crossbow {what}: its own shot {shot.m_attackAnimation}, no move (got {Fired(started)})");
                yield return WaitFor(() => !p.IsWeaponLoaded(), 2.5f, fired);
                c.Check(fired.Value && bolts.m_stack == stack - 1, $"crossbow {what}: the shot is fired and one bolt is used ({stack} -> {bolts.m_stack})");
                c.Check(LogTap.Count(mark, Dbg, " attack: ") == 0, $"crossbow {what}: no move Debug line");
                yield return AttackOver(p, started, 3f);
                yield return WaitLanded(p, 3f);
                Look(p, 0f);
            }

            // Reload, put it away for the sword, roll attack, take it back: still loaded.
            yield return WaitFor(() => p.IsWeaponLoaded(), 10f, loaded);
            c.Check(loaded.Value, "the crossbow reloads again");
            yield return WaitIdle(p, 4f);
            var swordOn = p.EquipItem(sword, false);
            yield return WaitIdle(p, 3f);
            c.Check(swordOn && ReferenceEquals(p.GetCurrentWeapon(), sword) && !p.IsItemEquiped(crossbow), "switched to SwordIron");
            yield return Settle(rig);
            var attack = new RollRun();
            yield return Roll(rig, attack);
            p.m_queuedAttackTimer = 0f;
            c.Check(attack.Clone != null && attack.Move.Kind == MoveKind.Roll, "a sword roll attack plays");
            yield return AttackOver(p, attack.Started, 3f);
            yield return WaitIdle(p, 4f);
            var back = p.EquipItem(crossbow, false);
            yield return WaitFor(() => p.IsWeaponLoaded(), 1f, loaded);
            c.Check(back && loaded.Value && ReferenceEquals(p.GetCurrentWeapon(), crossbow),
                "switched back: the crossbow is still loaded (Crossbow Stays Loaded), no reload");
            yield return WaitIdle(p, 4f);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- moveset.x.dualwield ----------

    // Two of the same one-handed weapon equipped one after the other (the game's own equip calls, like Dual Wielding's
    // tests): with Dual Wielding the second goes to the off hand. main / off = null when the hands are not such a pair.
    private static IEnumerator EquipPair(Rig rig, string prefab, Box<ItemDrop.ItemData> main, Box<ItemDrop.ItemData> off)
    {
        var p = rig.P;
        main.Value = null;
        off.Value = null;
        yield return WaitIdle(p, 4f);
        p.UnequipItem(p.m_rightItem, false);
        p.UnequipItem(p.m_leftItem, false);
        var a = rig.Give(prefab);
        var b = rig.Give(prefab);
        if (a == null || b == null || ReferenceEquals(a, b))
        {
            yield break;
        }
        p.EquipItem(a, false);
        yield return WaitIdle(p, 3f);
        p.EquipItem(b, false);
        yield return WaitIdle(p, 3f);
        if (ReferenceEquals(p.m_rightItem, a) && ReferenceEquals(p.m_leftItem, b))
        {
            main.Value = a;
            off.Value = b;
        }
        else if (ReferenceEquals(p.m_rightItem, b) && ReferenceEquals(p.m_leftItem, a))
        {
            main.Value = b;
            off.Value = a;
        }
    }

    // Weapons of the hit events of one swing, as "main", "off" words.
    private static string HandsOf(Attack attack, ItemDrop.ItemData main, ItemDrop.ItemData off)
    {
        var words = new List<string>();
        foreach (var e in HitTap.Of(attack))
        {
            words.Add(ReferenceEquals(e.Weapon, main) ? "main" : ReferenceEquals(e.Weapon, off) ? "off" : "other");
        }
        return string.Join(", ", words.ToArray());
    }

    // Me press until the next attack after `after` starts (combo step when pressed in its window).
    private static IEnumerator NextAttack(Player p, Attack after, float seconds, Box<Attack> next)
    {
        next.Value = null;
        var t0 = Time.fixedTime;
        while (next.Value == null && Time.fixedTime - t0 < seconds)
        {
            Press(p);
            yield return Fixed;
            var current = p.m_currentAttack;
            if (current != null && !ReferenceEquals(current, after))
            {
                next.Value = current;
            }
        }
        p.m_queuedAttackTimer = 0f;
    }

    private static IEnumerator RunDual()
    {
        var p = LocalPlayer(DualName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(DualName);
        var rig = new Rig(DualName, p);
        try
        {
            var rules = Loose();
            ServerRules.TestRules = rules;
            if (!OtherActive(DualGuid))
            {
                c.Check(false, "Dual Wielding is installed and active");
                c.Report();
                yield break;
            }
            var alerts = LogTap.AlertMark;
            var theirs = LogTap.ForeignMark;
            HitTap.Start();
            var dummy = rig.SpawnDummy(10f);
            var main = new Box<ItemDrop.ItemData>();
            var off = new Box<ItemDrop.ItemData>();
            var next = new Box<Attack>();
            yield return EquipPair(rig, "AxeIron", main, off);
            c.Check(main.Value != null && dummy != null, "two AxeIron equipped as a Dual Wielding pair, training dummy spawned");
            if (main.Value == null || dummy == null)
            {
                c.Report();
                yield break;
            }
            var a1 = main.Value;
            var a2 = off.Value;
            var drain = a1.m_shared.m_useDurability ? a1.m_shared.m_useDurabilityDrain * Game.m_durabilityRate : 0f;

            // Jump attack: dualaxes3, main hand then off hand, each axe worn once. The dummy is put in front of every
            // hit event, so both land.
            yield return Settle(rig);
            var wear = new Vector2(a1.m_durability, a2.m_durability);
            HitTap.Present = dummy.Body;
            var jump = new JumpRun();
            yield return JumpPress(rig, jump, 0.1f, 1.2f);
            yield return AttackOver(p, jump.Started, 3f);
            yield return WaitLanded(p, 3f);
            HitTap.Present = null;
            Place(dummy.Body, ParkSpot(rig, 3));
            c.Check(jump.IsMove && jump.Move.Kind == MoveKind.Jump && jump.Move.Trigger == "dualaxes3" && jump.Move.Family == WeaponFamily.DualAxes
                    && jump.Move.BaseName == "dualaxes" && jump.Move.ChainLevels == 4,
                $"axe pair: the jump attack plays dualaxes3 of the pair's four-swing combo (got {jump.Move.Trigger}, base {jump.Move.BaseName}, {jump.Move.ChainLevels} steps)");
            c.Check(jump.Clone != null && HandsOf(jump.Clone, a1, a2) == "main, off",
                $"axe pair jump attack: hits with the main hand, then the off hand (got {(jump.Clone != null ? HandsOf(jump.Clone, a1, a2) : "no move")})");
            c.Check(Near(wear.x - a1.m_durability, drain) && Near(wear.y - a2.m_durability, drain),
                $"axe pair jump attack: each axe loses durability once ({F(wear.x - a1.m_durability)} and {F(wear.y - a2.m_durability)}, one hit is {F(drain)})");

            // Roll attack: dualaxes1 with the off hand, then the combo goes on: dualaxes2 and dualaxes3, main then off.
            yield return Settle(rig);
            HitTap.Present = dummy.Body;
            var roll = new RollRun();
            yield return Roll(rig, roll);
            var first = roll.Clone;
            c.Check(first != null && roll.Move.Kind == MoveKind.Roll && roll.Move.Trigger == "dualaxes1" && roll.Move.Step == 1,
                $"axe pair: the roll attack plays dualaxes1 (got {roll.Move.Trigger})");
            yield return NextAttack(p, roll.Started, 4f, next);
            var second = next.Value;
            yield return NextAttack(p, second, 4f, next);
            var third = next.Value;
            yield return AttackOver(p, third, 3f);
            HitTap.Present = null;
            Place(dummy.Body, ParkSpot(rig, 3));
            c.Check(first != null && HandsOf(first, a1, a2) == "off", $"axe pair roll attack: strikes with the off hand (got {(first != null ? HandsOf(first, a1, a2) : "no move")})");
            c.Check(second != null && second.m_attackAnimation == "dualaxes" && second.m_currentAttackCainLevel == 2 && HandsOf(second, a1, a2) == "main, off",
                $"then dualaxes2, main then off (got {Fired(second)}: {(second != null ? HandsOf(second, a1, a2) : "")})");
            c.Check(third != null && third.m_attackAnimation == "dualaxes" && third.m_currentAttackCainLevel == 3 && HandsOf(third, a1, a2) == "main, off",
                $"then dualaxes3, main then off (got {Fired(third)}: {(third != null ? HandsOf(third, a1, a2) : "")})");

            // Jump attack animations DualAxes = dualaxes2: followed by dualaxes3 (four-swing combo, not the axe's three).
            var custom = Loose();
            custom.JumpTriggers[Families.Index(WeaponFamily.DualAxes)] = "dualaxes2";
            ServerRules.TestRules = custom;
            MoveTriggers.Invalidate();
            yield return Settle(rig);
            jump = new JumpRun();
            yield return JumpPress(rig, jump, 0.1f, 1.2f);
            yield return NextAttack(p, jump.Started, 4f, next);
            c.Check(jump.IsMove && jump.Move.Trigger == "dualaxes2" && jump.Move.Step == 2
                    && next.Value != null && next.Value.m_attackAnimation == "dualaxes" && next.Value.m_currentAttackCainLevel == 3,
                $"DualAxes = dualaxes2: the jump attack plays dualaxes2 and the next attack is dualaxes3 (got {jump.Move.Trigger}, then {Fired(next.Value)})");
            yield return AttackOver(p, next.Value, 3f);
            yield return WaitLanded(p, 3f);
            ServerRules.TestRules = rules;
            MoveTriggers.Invalidate();

            // The normal pair combo on the ground: its four swings, its hands, no move.
            yield return Settle(rig);
            var moved = MoveTracker.LastMove.Clone;
            HitTap.Present = dummy.Body;
            var swings = new List<Attack>();
            var plain = true;
            var lastAttack = p.m_currentAttack;
            for (var step = 0; step < 4; step++)
            {
                yield return NextAttack(p, lastAttack, 4f, next);
                if (next.Value == null)
                {
                    plain = false;
                    break;
                }
                lastAttack = next.Value;
                swings.Add(lastAttack);
                plain &= lastAttack.m_attackAnimation == "dualaxes" && lastAttack.m_currentAttackCainLevel == step;
            }
            yield return AttackOver(p, lastAttack, 3f);
            HitTap.Present = null;
            Place(dummy.Body, ParkSpot(rig, 3));
            var combo = string.Join(" | ", swings.Select(x => HandsOf(x, a1, a2)).ToArray());
            c.Check(plain && ReferenceEquals(MoveTracker.LastMove.Clone, moved), "axe pair: the normal combo on the ground is dualaxes0 to dualaxes3, no move");
            c.Check(combo == "main | off | main, off | main, off",
                $"axe pair: the normal combo keeps Dual Wielding's hands (main, off, main then off, main then off; got {combo})");
            rig.TakeBack(a1);
            rig.TakeBack(a2);

            // Two knives: the dual knives animations; the roll attack strikes with the off hand.
            yield return EquipPair(rig, "KnifeCopper", main, off);
            c.Check(main.Value != null, "two KnifeCopper equipped as a Dual Wielding pair");
            if (main.Value != null)
            {
                var k1 = main.Value;
                var k2 = off.Value;
                yield return Settle(rig);
                jump = new JumpRun();
                yield return JumpPress(rig, jump, 0.1f, 1.2f);
                c.Check(jump.IsMove && jump.Move.Family == WeaponFamily.DualKnives && jump.Move.Trigger == "dual_knives2" && jump.Move.BaseName == "dual_knives",
                    $"knife pair: the jump attack plays dual_knives2 (got {jump.Move.Trigger})");
                yield return AttackOver(p, jump.Started, 3f);
                yield return Settle(rig);
                HitTap.Present = dummy.Body;
                roll = new RollRun();
                yield return Roll(rig, roll);
                p.m_queuedAttackTimer = 0f;
                yield return AttackOver(p, roll.Started, 3f);
                HitTap.Present = null;
                Place(dummy.Body, ParkSpot(rig, 3));
                c.Check(roll.Clone != null && roll.Move.Trigger == "dual_knives1" && HandsOf(roll.Clone, k1, k2) == "off",
                    $"knife pair: the roll attack plays dual_knives1 and strikes with the off hand (got {roll.Move.Trigger}: {(roll.Clone != null ? HandsOf(roll.Clone, k1, k2) : "")})");
            }
            var bad = LogTap.AlertsSince(alerts, Wrn | Err);
            c.Check(bad.Count == 0, $"no warning and no error from {ModInfo.Name} with pairs ({bad.Count}{(bad.Count > 0 ? ", first: " + bad[0].Text : "")})");
            var dual = LogTap.ForeignSince(theirs, DualLogName);
            c.Check(dual.Count == 0, $"no warning and no error from Dual Wielding meanwhile ({dual.Count}{(dual.Count > 0 ? ", first: " + dual[0] : "")})");
            yield return Settle(rig);
            c.Report();
        }
        finally
        {
            HitTap.Stop();
            rig.Restore();
        }
    }

    // ---------- moveset.x.dualwield-off ----------

    private static IEnumerator RunDualOff()
    {
        var p = LocalPlayer(DualOffName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(DualOffName);
        var rig = new Rig(DualOffName, p);
        try
        {
            var rules = Loose();
            rules.RollTriggers[Families.Index(WeaponFamily.DualAxes)] = MoveTriggers.Off;
            ServerRules.TestRules = rules;
            MoveTriggers.Invalidate();
            if (!OtherActive(DualGuid))
            {
                c.Check(false, "Dual Wielding is installed and active");
                c.Report();
                yield break;
            }
            var alerts = LogTap.AlertMark;
            var theirs = LogTap.ForeignMark;
            HitTap.Start();
            var dummy = rig.SpawnDummy(10f);
            var main = new Box<ItemDrop.ItemData>();
            var off = new Box<ItemDrop.ItemData>();
            yield return EquipPair(rig, "AxeIron", main, off);
            c.Check(main.Value != null && dummy != null, "two AxeIron equipped as a Dual Wielding pair, training dummy spawned");
            if (main.Value == null || dummy == null)
            {
                c.Report();
                yield break;
            }
            var dt = Time.fixedDeltaTime;
            yield return Settle(rig);
            var mark = LogTap.Mark;
            var moved = MoveTracker.LastMove.Clone;
            // Button held through the roll and the stand-up after it, let go once the swing is in its animation.
            // Held: the game start that swing again on every tick of the stand-up (a new Attack object each time);
            // run.Started = the one that play (first run looked at the first object, which never hit anything).
            var run = new RollRun { Held = true };
            HitTap.Present = dummy.Body;
            rig.TakeController();
            yield return Roll(rig, run, true, q => Hold(q, false));
            rig.GiveController();
            var next = new Box<Attack>();
            yield return NextAttack(p, run.Started, 4f, next);
            yield return AttackOver(p, next.Value, 3f);
            HitTap.Present = null;
            Place(dummy.Body, ParkSpot(rig, 3));
            SelfTest.Note(DualOffName, $"button held: the first swing was started {run.Restarts + 1} time(s) during the stand-up (the game does that with a held button, with or without the mods), "
                                       + $"in its animation {Span(run.StartAt, run.EnteredAt)} s after its first start");
            c.Check(run.Clone == null && ReferenceEquals(MoveTracker.LastMove.Clone, moved) && LogTap.Count(mark, Dbg, "Roll attack") == 0,
                "pair with Roll attack animations DualAxes = Off, attack held through a roll: no roll attack, no \"Roll attack\" Debug line");
            c.Check(run.Refusals >= 1 && run.Skips == 0,
                $"the attack is refused inside the roll, at the attack's own start (refused {run.Refusals} time(s); the roll is opened for a pair because its weapon type is only known there)");
            c.Check(run.RollStart >= 0f && run.RollEnd - run.RollStart >= 0.8f && !run.StartedInRoll,
                $"no attack starts inside the roll, and the roll plays its full length ({Span(run.RollStart, run.RollEnd)} s)");
            c.Check(run.Started != null && Mathf.Abs(run.StartAt - run.RollEnd - dt) < 0.5f * dt && run.GapTicks > 0 && run.EnteredAt >= 0f
                    && run.Started.m_attackAnimation == "dualaxes" && run.Started.m_currentAttackCainLevel == 0,
                $"the press is not lost: the pair's normal first swing starts on the tick after the roll and plays after the usual stand-up (got {Fired(run.Started)}, idle for {S(run.Gap)} s, in its animation {Span(run.StartAt, run.EnteredAt)} s after the start)");
            c.Check(run.Started != null && HandsOf(run.Started, main.Value, off.Value) == "main"
                    && next.Value != null && next.Value.m_attackAnimation == "dualaxes" && next.Value.m_currentAttackCainLevel == 1
                    && HandsOf(next.Value, main.Value, off.Value) == "off",
                $"Dual Wielding's hands are as usual: that swing with the main hand, the next one with the off hand (got {Fired(run.Started)}: {(run.Started != null ? HandsOf(run.Started, main.Value, off.Value) : "")} / {Fired(next.Value)}: {(next.Value != null ? HandsOf(next.Value, main.Value, off.Value) : "none")})");
            var bad = LogTap.AlertsSince(alerts, Wrn | Err);
            c.Check(bad.Count == 0, $"no warning and no error from {ModInfo.Name} ({bad.Count}{(bad.Count > 0 ? ", first: " + bad[0].Text : "")})");
            var dual = LogTap.ForeignSince(theirs, DualLogName);
            c.Check(dual.Count == 0, $"no warning and no error from Dual Wielding ({dual.Count}{(dual.Count > 0 ? ", first: " + dual[0] : "")})");
            yield return Settle(rig);
            c.Report();
        }
        finally
        {
            HitTap.Stop();
            MoveTriggers.Invalidate();
            rig.Restore();
        }
    }

    // ---------- moveset.x.towershield ----------

    private static IEnumerator RunTower()
    {
        var p = LocalPlayer(TowerName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(TowerName);
        var rig = new Rig(TowerName, p);
        try
        {
            ServerRules.TestRules = Loose();
            if (!OtherActive(TowerGuid) || Prefab("ShieldWoodTower") == null)
            {
                c.Check(false, "Tower Shield Wall is installed and active, ShieldWoodTower exists");
                c.Report();
                yield break;
            }
            yield return WaitIdle(p, 4f);
            p.UnequipItem(p.m_rightItem, false);
            p.UnequipItem(p.m_leftItem, false);
            var tower = rig.Give("ShieldWoodTower");
            var on = tower != null && p.EquipItem(tower, false);
            yield return WaitIdle(p, 3f);
            c.Check(on && ReferenceEquals(p.m_leftItem, tower) && p.m_rightItem == null, "tower shield alone, in the left hand");
            if (!on)
            {
                c.Report();
                yield break;
            }
            var cooldown = OtherSetting(TowerGuid, "BashCooldown", 2f);
            var dt = Time.fixedDeltaTime;
            var next = new Box<Attack>();

            // Jump, then bash: Tower Shield Wall's own bash, no move. A second bash only after its cooldown.
            yield return Settle(rig);
            var mark = LogTap.Mark;
            var jump = new JumpRun();
            yield return JumpPress(rig, jump, 0.1f, 1.2f);
            var bash = jump.Started;
            var own = tower.m_shared.m_attack;
            c.Check(bash != null && jump.Clone == null && ReferenceEquals(bash.m_weapon, tower) && tower.m_shared.m_skillType == Skills.SkillType.Blocking
                    && own != null && bash.m_attackAnimation == own.m_attackAnimation && Near(bash.m_damageMultiplier, own.m_damageMultiplier)
                    && Near(bash.m_staggerMultiplier, own.m_staggerMultiplier) && Near(bash.m_forceMultiplier, own.m_forceMultiplier),
                $"tower shield, attack in a jump: Tower Shield Wall's own bash with its own numbers, no move (got {Fired(bash)})");
            yield return NextAttack(p, bash, cooldown + 1.5f, next);
            var again = next.Value != null ? Time.fixedTime - jump.StartAt : -1f;
            c.Check(next.Value != null && again >= cooldown - 2f * dt && again <= cooldown + 0.6f,
                $"a second bash, asked all the time, only starts after Tower Shield Wall's {F(cooldown)} s cooldown ({S(again)} s after the first)");
            c.Check(!ReferenceEquals(MoveTracker.LastMove.Clone, next.Value) && LogTap.Count(mark, Dbg, " attack: ") == 0, "bash in a jump: no move Debug line");
            yield return AttackOver(p, next.Value, 3f);
            yield return WaitLanded(p, 3f);
            yield return WaitTicks(cooldown + 0.2f);

            // Roll, then bash.
            yield return Settle(rig);
            mark = LogTap.Mark;
            var roll = new RollRun();
            yield return Roll(rig, roll);
            p.m_queuedAttackTimer = 0f;
            c.Check(roll.Clone == null && roll.Started != null && !roll.StartedInRoll && ReferenceEquals(roll.Started.m_weapon, tower)
                    && roll.Started.m_attackAnimation == own.m_attackAnimation && Near(roll.Started.m_staggerMultiplier, own.m_staggerMultiplier),
                $"tower shield, attack pressed during a roll: the bash, once the roll is over, no move (got {Fired(roll.Started)})");
            c.Check(roll.Refusals == 0 && LogTap.Count(mark, Dbg, " attack: ") == 0, "bash after a roll: the roll is never opened for it, no move Debug line");
            yield return AttackOver(p, roll.Started, 3f);
            yield return WaitIdle(p, 4f);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- moveset.x.sneak ----------

    private static IEnumerator RunSneak()
    {
        var p = LocalPlayer(SneakName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(SneakName);
        var rig = new Rig(SneakName, p);
        var sneak = p.GetSkills().GetSkill(Skills.SkillType.Sneak);
        var sneakWas = new Vector2(sneak.m_level, sneak.m_accumulator);
        try
        {
            ServerRules.TestRules = Loose();
            c.Check(OtherActive(SneakGuid), "Sneak Ambush is installed and active");
            HitTap.Start();
            HitTap.WatchSkill = sneak;
            var held = new Box<ItemDrop.ItemData>();
            yield return Equip(rig, SwordRow, held);
            var grey = SpawnFoe(rig, "Greyling", 1, 2000f);
            if (held.Value == null || grey == null)
            {
                c.Check(false, "SwordIron equipped and a Greyling spawned (AI off: it never notices the player)");
                c.Report();
                yield break;
            }
            var crouching = new Box<bool>();

            // A jump ends crouching.
            yield return Settle(rig);
            p.SetCrouch(true);
            yield return WaitFor(() => p.IsCrouching(), 2f, crouching);
            p.Jump();
            c.Check(crouching.Value && p.m_jumpTimer == 0f && !p.m_crouchToggled, "a jump from a crouch ends the crouch");
            yield return WaitLanded(p, 3f);
            yield return WaitTicks(0.3f);
            c.Check(!p.IsCrouching(), "not crouching after the jump");

            // Sneak roll (crouch + jump on the keyboard = a roll) into a roll attack on the unaware Greyling: a
            // backstab, sneak XP once. Then again at once: no second backstab, no more XP.
            for (var pass = 0; pass < 2; pass++)
            {
                var what = pass == 0 ? "sneak roll attack on an unaware Greyling" : "a second sneak roll attack right after";
                yield return Settle(rig);
                p.SetCrouch(true);
                yield return WaitFor(() => p.IsCrouching(), 2f, crouching);
                var run = new RollRun();
                HitTap.Present = grey.Body;
                var from = HitTap.Hits.Count;
                yield return Roll(rig, run);
                p.m_queuedAttackTimer = 0f;
                var seen = new Box<bool>();
                yield return WaitFor(() => HitTap.OnTarget(grey.Body, from).Count > 0, 2f, seen);
                HitTap.Present = null;
                var hits = HitTap.OnTarget(grey.Body, from);
                var h = hits.Count > 0 ? hits[0] : null;
                // X04 asks: a roll attack out of the sneak roll, and what its hit does. HOW a roll that starts from a
                // crouch lets the attack flow (cut into it, or out of its last blend) is moveset.crouch-roll's job:
                // the first run asked "cut, no idle tick" here and failed both times with no numbers, hit and
                // backstab fine.
                var isRoll = run.Clone != null && ReferenceEquals(run.Started, run.Clone) && run.Move.Kind == MoveKind.Roll;
                var itsHit = h != null && isRoll && HitTap.Of(run.Clone).Any(e => Mathf.Abs(e.At - h.At) < 0.001f);
                c.Check(crouching.Value && isRoll && itsHit,
                    $"{what}: a roll attack starts out of the sneak roll and its swing hits the Greyling (crouching before the roll: {crouching.Value}; {RollWhat(run)}; "
                    + $"hit on the Greyling: {(h == null ? "none" : itsHit ? "by that swing" : "by another swing")})");
                SelfTest.Note(SneakName, $"{what}: {RollWhat(run)}");
                if (h != null && pass == 0)
                {
                    c.Check(h.Backstabbed && h.Backstab > 1f && h.Shown > h.WireDamage * 1.5f,
                        $"{what}: a backstab (bonus x{F(h.Backstab)}: {F(h.WireDamage)} damage became {F(h.Shown)})");
                    c.Check(h.SkillAfter > h.SkillBefore, $"{what}: sneak XP given with the hit (Sneak Ambush)");
                }
                else if (h != null)
                {
                    c.Check(!h.Backstabbed && Mathf.Abs(h.SkillAfter - h.SkillBefore) < 0.0001f, $"{what}: no second backstab, no more sneak XP");
                }
                yield return AttackOver(p, run.Started, 3f);
                Park(rig, grey, 1);
                p.SetCrouch(false);
            }
            yield return Settle(rig);
            c.Report();
        }
        finally
        {
            HitTap.Stop();
            sneak.m_level = sneakWas.x;
            sneak.m_accumulator = sneakWas.y;
            if (p != null)
            {
                p.SetCrouch(false);
            }
            rig.Restore();
        }
    }

    // ---------- moveset.x.gco ----------

    // Pretended dash mod: for this many ticks the player is "in a dodge" with no roll animation and the game's own
    // roll code is skipped (what instant dash mods do).
    private static int _dashTicks;

    private static bool DashPre(Player __instance)
    {
        try
        {
            if (_dashTicks > 0 && ReferenceEquals(__instance, Player.m_localPlayer))
            {
                _dashTicks--;
                __instance.m_inDodge = true;
                return false;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("SelfTests dash Player.UpdateDodge", e);
        }
        return true;
    }

    private static IEnumerator Dash(Player p, int ticks)
    {
        _dashTicks = ticks;
        var until = Time.fixedTime + 2f;
        while (_dashTicks > 0 && Time.fixedTime < until)
        {
            yield return Fixed;
        }
        until = Time.fixedTime + 0.2f;
        while (p.m_inDodge && Time.fixedTime < until)
        {
            yield return Fixed;
        }
    }

    private static IEnumerator RunGco()
    {
        var p = LocalPlayer(GcoName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(GcoName);
        var rig = new Rig(GcoName, p);
        var harmony = new Harmony(ModInfo.Guid + ".selftest.dash");
        try
        {
            var held = new Box<ItemDrop.ItemData>();
            yield return Equip(rig, SwordRow, held);
            var weapon = held.Value;
            if (weapon == null)
            {
                c.Check(false, "could not equip SwordIron");
                c.Report();
                yield break;
            }
            var shared = weapon.m_shared.m_attack;
            var box = new Box<Attack>();
            SelfTest.Note(GcoName, $"Goo's Combat Overhaul really installed: {Compat.GcoLoaded}; the stand-down below is pretended in memory");

            // Goo's Combat Overhaul "installed": Info line once, no jump attack, roll attack still.
            var rules = Loose();
            rules.Window = 2f;
            ServerRules.TestRules = rules;
            var mark = LogTap.Mark;
            Compat.TestGco = true;
            Compat.Reset();
            var found = Compat.GcoLoaded;
            _ = Compat.GcoLoaded;
            const string info = "Goo's Combat Overhaul is installed: it has its own jump attack, so Weapon Moveset's jump attack stays off (the roll attack still works).";
            c.Check(found && LogTap.Count(mark, Inf, info) == 1, $"Info \"{info}\" logged once");
            yield return Settle(rig);
            var jump = new JumpRun();
            yield return JumpPress(rig, jump);
            c.Check(jump.TokenAtJump && jump.Started != null && jump.Clone == null && Vanilla(jump.Started, shared),
                $"with Goo's Combat Overhaul: jump + attack is not this mod's jump attack (got {Fired(jump.Started)})");
            yield return AttackOver(p, jump.Started, 3f);
            yield return Settle(rig);
            var roll = new RollRun();
            yield return Roll(rig, roll);
            p.m_queuedAttackTimer = 0f;
            c.Check(roll.Clone != null && roll.Move.Kind == MoveKind.Roll, "with Goo's Combat Overhaul: roll attacks work");
            yield return AttackOver(p, roll.Started, 3f);

            // Window 2: roll, jump right after, attack in the air: no roll attack on top of GCO's jump attack.
            yield return Settle(rig);
            mark = LogTap.Mark;
            var moved = MoveTracker.LastMove.Clone;
            var edge = new Box<float>();
            p.Dodge(rig.Forward);
            yield return WaitRollEdge(p, 4f, false, edge);
            var jumped = false;
            var t0 = Time.fixedTime;
            while (!jumped && Time.fixedTime - t0 < 0.3f)
            {
                p.Jump();
                jumped = p.m_jumpTimer == 0f;
                if (!jumped)
                {
                    yield return Fixed;
                }
            }
            yield return WaitTicks(0.1f);
            var last = p.m_currentAttack;
            var air = !p.IsOnGround();
            Press(p);
            yield return WaitNewAttack(p, last, 0.4f, box);
            c.Check(jumped && air && Vanilla(box.Value, shared) && ReferenceEquals(MoveTracker.LastMove.Clone, moved)
                    && LogTap.Count(mark, Dbg, "Roll attack") == 0,
                $"with Goo's Combat Overhaul and Window 2: roll, jump, attack in the air gives no roll attack and no \"Roll attack\" Debug line (got {Fired(box.Value)})");
            yield return AttackOver(p, box.Value, 3f);
            Compat.TestGco = null;
            Compat.Reset();

            // Dash from a dash mod (pretended): dash, attack = roll attack; a second dash right after = normal swing.
            harmony.Patch(AccessTools.Method(typeof(Player), nameof(Player.UpdateDodge)),
                prefix: new HarmonyMethod(typeof(SelfTests), nameof(DashPre)));
            rules = MoveRules.Defaults(); // default 1 s cooldown and 0.4 s window
            ServerRules.TestRules = rules;
            MoveTracker.Reset();
            yield return Settle(rig);
            mark = LogTap.Mark;
            moved = MoveTracker.LastMove.Clone;
            yield return Dash(p, 6);
            last = p.m_currentAttack;
            Press(p);
            yield return WaitNewAttack(p, last, 0.4f, box);
            var dashMove = MoveTracker.LastMove;
            var isMove = box.Value != null && !ReferenceEquals(dashMove.Clone, moved) && ReferenceEquals(box.Value, dashMove.Clone);
            c.Check(isMove && dashMove.Kind == MoveKind.Roll && !dashMove.Cut && dashMove.Flow == FlowResult.None
                    && dashMove.RollAge >= 0f && dashMove.RollAge <= rules.Window,
                $"a dash (no roll animation) then an attack is a roll attack inside the window (got {Fired(box.Value)}, {S(dashMove.RollAge)} s after the dash)");
            var entered = new Box<bool>();
            yield return WaitFor(() => MoveTracker.LastEntryDelay >= 0f || !MoveTracker.Watching, 1f, entered);
            c.Check(isMove && MoveTracker.LastEntryDelay >= 0f && MoveTracker.LastEntryDelay <= MoveTracker.StartDeadline,
                $"the roll attack after a dash enters its animation by the game's own transition ({S(MoveTracker.LastEntryDelay)} s)");
            var entry = MoveTracker.LastMoveAt;
            yield return AttackOver(p, box.Value, 3f);
            yield return Dash(p, 3);
            last = p.m_currentAttack;
            var since = Time.fixedTime - entry;
            Press(p);
            yield return WaitNewAttack(p, last, 0.4f, box);
            c.Check(since < rules.Cooldown, $"the second dash came inside the cooldown ({S(since)} s after the first roll attack; test timing)");
            c.Check(Vanilla(box.Value, shared) && ReferenceEquals(MoveTracker.LastMove.Clone, dashMove.Clone)
                    && LogTap.Count(mark, Dbg, "Roll attack skipped: cooldown") == 1,
                $"a second dash right after gives a normal swing, Debug \"Roll attack skipped: cooldown\" (got {Fired(box.Value)})");
            yield return AttackOver(p, box.Value, 3f);
            yield return Settle(rig);
            c.Report();
        }
        finally
        {
            _dashTicks = 0;
            harmony.UnpatchSelf();
            Compat.TestGco = null;
            Compat.Reset();
            rig.Restore();
        }
    }

    // ---------- moveset.x.killcount ----------

    // Kills of one creature name in the player's lifetime statistics (what Creature Kill and Tame Counts shows):
    // [total, unarmed, magic, ranged, melee].
    private static float[] Kills(string name)
    {
        var result = new float[5];
        var stats = Game.instance.GetPlayerProfile().m_playerStats[0].m_enemyStats;
        for (var i = 0; i < result.Length && i < stats.Length; i++)
        {
            result[i] = stats[i] != null && stats[i].TryGetValue(name, out var n) ? n : 0f;
        }
        return result;
    }

    // Statistics of every difficulty slot for that name, to put back after (the kills of a test are not the player's).
    private static List<KeyValuePair<Dictionary<string, float>, float?>> KillSnapshot(string name)
    {
        var list = new List<KeyValuePair<Dictionary<string, float>, float?>>();
        foreach (var slot in Game.instance.GetPlayerProfile().m_playerStats)
        {
            if (slot == null || slot.m_enemyStats == null)
            {
                continue;
            }
            foreach (var bucket in slot.m_enemyStats)
            {
                if (bucket != null)
                {
                    list.Add(new KeyValuePair<Dictionary<string, float>, float?>(bucket, bucket.TryGetValue(name, out var n) ? n : (float?)null));
                }
            }
        }
        return list;
    }

    private static IEnumerator RunKill()
    {
        var p = LocalPlayer(KillName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(KillName);
        var rig = new Rig(KillName, p);
        List<KeyValuePair<Dictionary<string, float>, float?>> snapshot = null;
        string name = null;
        try
        {
            ServerRules.TestRules = Loose();
            SelfTest.Note(KillName, $"Creature Kill and Tame Counts {(OtherActive(StatsGuid) ? "is" : "is NOT")} installed and active; it shows the game's own lifetime kill statistics checked here");
            HitTap.Start();
            var held = new Box<ItemDrop.ItemData>();
            yield return Equip(rig, SwordRow, held);
            if (held.Value == null)
            {
                c.Check(false, "could not equip SwordIron");
                c.Report();
                yield break;
            }
            var melee = (int)KillModifiers.Melee;
            for (var pass = 0; pass < 2; pass++)
            {
                var what = pass == 0 ? "Boar killed by a roll attack" : "Boar killed by a normal swing";
                var boar = SpawnFoe(rig, "Boar", 1);
                if (boar == null)
                {
                    c.Check(false, "a Boar spawned (AI off)");
                    break;
                }
                // No loot on the ground after the test: the Boar drops nothing.
                var drops = boar.Go.GetComponent<CharacterDrop>();
                if (drops != null)
                {
                    Object.DestroyImmediate(drops);
                }
                name = boar.Body.m_name;
                if (snapshot == null)
                {
                    snapshot = KillSnapshot(name);
                }
                yield return Fixed;
                boar.Body.SetHealth(1f);
                var before = Kills(name);
                var hit = new Box<HitRec>();
                var run = new RollRun();
                if (pass == 0)
                {
                    yield return RollHit(rig, boar.Body, run, hit, false);
                }
                else
                {
                    yield return GroundHit(rig, boar.Body, hit, false);
                }
                var dead = new Box<bool>();
                yield return WaitFor(() => boar.Go == null || boar.Body == null || boar.Body.IsDead(), 2f, dead);
                yield return WaitTicks(0.2f);
                var after = Kills(name);
                c.Check(hit.Value != null && hit.Value.Skill == Skills.SkillType.Swords && dead.Value && (pass == 1 || (run.Clone != null && run.Move.Kind == MoveKind.Roll)),
                    $"{what}: the hit is a sword hit and the Boar dies");
                c.Check(Near(after[0], before[0] + 1f) && Near(after[melee], before[melee] + 1f)
                        && Near(after[1], before[1]) && Near(after[2], before[2]) && Near(after[3], before[3]),
                    $"{what}: counted as one more kill and one more melee kill of {name} (total {F(before[0])} -> {F(after[0])}, melee {F(before[melee])} -> {F(after[melee])})");
                // Its ragdoll goes too.
                foreach (var doll in Object.FindObjectsByType<Ragdoll>(FindObjectsSortMode.None))
                {
                    if (doll != null && Vector3.Distance(doll.transform.position, p.transform.position) < 25f)
                    {
                        rig.Track(doll.gameObject);
                    }
                }
            }
            yield return Settle(rig);
            c.Report();
        }
        finally
        {
            HitTap.Stop();
            if (snapshot != null && name != null)
            {
                foreach (var pair in snapshot)
                {
                    if (pair.Value.HasValue)
                    {
                        pair.Key[name] = pair.Value.Value;
                    }
                    else
                    {
                        pair.Key.Remove(name);
                    }
                }
            }
            rig.Restore();
        }
    }
}
#endif
