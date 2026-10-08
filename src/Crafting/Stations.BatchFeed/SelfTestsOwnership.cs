#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Crafting.StationsBatchFeedMod;

// Single-player self tests of the non-owner paths (made-up owner), owner gone, live toggle, other mods and the log.
internal static partial class SelfTests
{
    // Station of "another game" in single player: a made-up owner uid on its ZDO (the game then sends the add rpcs
    // to nobody = an owner that never answers, the local copy stays stale) and BatchFeeder told that this uid is
    // still connected. Nobody get the station back while the test hooks are on (Hooks.KeepOwner say why): not the
    // world's 2 s hand-over, not another mod that claims a station before an add.
    private static void MakeOther(ZNetView view) => Hooks.KeepOwner(view);

    // Every add of a hold came one hold repeat or more after the one before (first = the batch press).
    private static bool Paced(List<float> times, float first, float gap)
    {
        var ok = true;
        var last = first;
        foreach (var time in times)
        {
            ok &= time - last >= gap;
            last = time;
        }
        return ok;
    }

    // ---------- batchfeed.nonowner-smelter (M02, M04, M11) ----------

    private static IEnumerator RunNonOwnerSmelter()
    {
        var c = new Checks(NonOwnerSmelterName);
        Rig rig = null;
        LogTap tap = null;
        try
        {
            rig = new Rig();
            tap = new LogTap();
            HudReady(c);
            BatchFeeder.TestConnectedOwner = MadeUpOwner;
            var oreToken = Token("CopperOre");
            var goA = rig.Spawn("smelter", -6f, 7f);
            var goB = rig.Spawn("smelter", 0f, 7f);
            var goC = rig.Spawn("smelter", 6f, 7f);
            var a = goA != null ? goA.GetComponentInChildren<Smelter>() : null;
            var b = goB != null ? goB.GetComponentInChildren<Smelter>() : null;
            var s = goC != null ? goC.GetComponentInChildren<Smelter>() : null;
            if (!c.Check(a != null && b != null && s != null && a.m_addOreSwitch != null && a.m_maxOre == 10 && oreToken != null, "spawn three vanilla smelters (10 ore)"))
            {
                c.Report();
                yield break;
            }
            yield return null;

            // M02: three batch presses before the owner answers: 5, 5, "It's full".
            var ore = a.m_addOreSwitch.gameObject;
            c.Check(rig.Give("CopperOre", 20), "give 20 CopperOre");
            MakeOther(a.m_nview);
            var mark = tap.Mark();
            var p1 = Press(ore, alt: true);
            var line1 = tap.Batch(mark);
            var p2 = Press(ore, alt: true);
            mark = tap.Mark();
            var p3 = Press(ore, alt: true);
            var line3 = tap.Batch(mark);
            var p4 = Press(ore, alt: false);
            c.Check(p1.Removed == 5 && p1.Messages == 1 && p1.Center == Loc($"$msg_added 5 {oreToken} (5/10)"), $"M02 first batch: 5, summary counts what was sent ({Show(p1)})");
            c.Check(p2.Removed == 5 && p2.Center == Loc($"$msg_added 5 {oreToken} (10/10)"), $"M02 second batch: 5 more, summary says 10/10 ({Show(p2)})");
            c.Check(p3.Removed == 0 && p3.Messages == 1 && p3.Center == Loc("$msg_itsfull"), $"M02 third batch: 'It's full', nothing removed ({Show(p3)})");
            c.Check(p4.Removed == 0 && p4.Messages == 1 && p4.Center == Loc("$msg_itsfull"), $"M11 a plain E right after is refused the same way ({Show(p4)})");
            c.Check(!a.m_nview.IsOwner() && a.m_nview.GetZDO().GetOwner() == MadeUpOwner,
                "the smelter is still the other game's after the presses (no mod took it over: the non-owner road was walked)");
            c.Check(rig.Count(oreToken) == 10 && a.GetQueueSize() == 0, $"M02 exactly 10 ore left the inventory; the mod wrote nothing into the station itself (ore left {rig.Count(oreToken)}, local queue {a.GetQueueSize()})");
            ExpectLog(c, "M02 first batch", line1, "smelter SmelterInput", "owner=other", "room=5", "added=5", "stop=amount");
            ExpectLog(c, "M02 third batch", line3, "owner=other", "room=0", "stop=pending");

            // M04: E, E, Shift+E = 7 sent, then only 3 more fit.
            ore = b.m_addOreSwitch.gameObject;
            rig.Empty();
            rig.Give("CopperOre", 20);
            MakeOther(b.m_nview);
            p1 = Press(ore, alt: false);
            p2 = Press(ore, alt: false);
            p3 = Press(ore, alt: true);
            var sent = FeedTarget.TryResolve(ore, out var target) ? PendingAdds.Effective(target, target.ReadValue()) : -1f;
            c.Check(p1.Removed == 1 && p2.Removed == 1 && p3.Removed == 5 && rig.Count(oreToken) == 13, $"M04 E, E, Shift+E: 1 + 1 + 5 ore gone ({p1.Removed}, {p2.Removed}, {p3.Removed})");
            c.Check(Near(sent, 7f) && b.GetQueueSize() == 0 && p3.Center == Loc($"$msg_added 5 {oreToken} (7/10)"),
                $"M04 the mod counts 7 sent to the other game, whose answer never came (counts {sent}, local queue {b.GetQueueSize()}, {Show(p3)})");
            p1 = Press(ore, alt: true);
            p2 = Press(ore, alt: false);
            c.Check(p1.Removed == 3 && p1.Center == Loc($"$msg_added 3 {oreToken} (10/10)") && p2.Removed == 0 && p2.Center == Loc("$msg_itsfull"),
                $"M04 then exactly 3 more fit, and the next press is refused ({Show(p1)} / {Show(p2)})");

            // M11: batch, then keep holding: one per repeat until the mod's count says 10, then "It's full".
            ore = s.m_addOreSwitch.gameObject;
            var repeat = s.m_addOreSwitch.m_holdRepeatInterval;
            rig.Empty();
            rig.Give("CopperOre", 20);
            MakeOther(s.m_nview);
            var t0 = Time.time;
            p1 = Press(ore, alt: true);
            var times = new List<float>();
            var lastText = "";
            var view = s.m_nview;
            yield return Hold(ore, true, 2.2f, times, () => MakeOther(view), p =>
            {
                if (p.Messages > 0)
                {
                    lastText = p.Center;
                }
            });
            var expected = repeat > 0f ? 10 : 5;
            c.Note($"M11 ore switch hold repeat {repeat} s: {times.Count} hold adds after the batch");
            c.Check(p1.Removed == 5 && 5 + times.Count == expected && rig.Count(oreToken) == 20 - expected && s.GetQueueSize() == 0,
                $"M11 hold after a batch on another game's smelter: +5, then one per repeat, never more than 10 in total (sent {5 + times.Count}, expected {expected}, ore left {rig.Count(oreToken)}, local queue {s.GetQueueSize()})");
            if (repeat > 0f)
            {
                c.Check(Paced(times, t0, Mathf.Max(0.19f, repeat - 0.01f)), $"M11 the hold adds one ore per hold repeat ({repeat} s), not faster");
                c.Check(lastText == Loc("$msg_itsfull"), $"M11 the repeats after the 10th are refused with 'It's full', last message '{lastText}'");
            }
            c.Report();
        }
        finally
        {
            tap?.Dispose();
            rig?.End();
        }
    }

    // ---------- batchfeed.nonowner-others (M03, M06, M07, M08, M09) ----------

    private static IEnumerator RunNonOwnerOthers()
    {
        var c = new Checks(NonOwnerOthersName);
        Rig rig = null;
        LogTap tap = null;
        try
        {
            rig = new Rig();
            tap = new LogTap();
            HudReady(c);
            rig.ParkSkill(Skills.SkillType.Cooking, ParkedSkillLevel);
            BatchFeeder.TestConnectedOwner = MadeUpOwner;

            // M03: fire 3 below its maximum, two batches.
            var pit = rig.Spawn("fire_pit", 0f, 7f);
            var fire = pit != null ? pit.GetComponent<Fireplace>() : null;
            if (c.Check(fire != null && FeedTarget.FireSkip(fire) == null && fire.m_maxFuel >= 4f, "spawn a campfire"))
            {
                var token = fire.m_fuelItem.m_itemData.m_shared.m_name;
                var max = fire.m_maxFuel;
                rig.Give(fire.m_fuelItem.name, 10);
                SetFuel(fire, max - 3f);
                MakeOther(fire.m_nview);
                var p1 = Press(pit, alt: true);
                var mark = tap.Mark();
                var p2 = Press(pit, alt: true);
                c.Check(p1.Removed == 3 && p1.Center == Loc(Words("$msg_fireadding", "3 " + token) + " (" + Mathf.Ceil(max) + "/" + (int)max + ")"),
                    $"M03 first batch on another game's fire at max - 3: exactly 3 ({Show(p1)})");
                c.Check(p2.Removed == 0 && p2.Messages == 1 && p2.Center == Loc(Words("$msg_cantaddmore", token)) && rig.Count(token) == 7,
                    $"M03 second batch: 'can't add more', nothing removed, exactly 3 wood gone ({Show(p2)}, wood left {rig.Count(token)})");
                ExpectLog(c, "M03 second batch", tap.Batch(mark), "Fire", "owner=other", "stop=pending");
            }
            yield return null;

            // M09 (fire part): a plain E on a fire that can be turned off still toggles after a non-owner batch.
            var pit2 = rig.Spawn("fire_pit", 6f, 7f);
            var fire2 = pit2 != null ? pit2.GetComponent<Fireplace>() : null;
            if (c.Check(fire2 != null && FeedTarget.FireSkip(fire2) == null && fire2.m_maxFuel >= 4f, "spawn a second campfire"))
            {
                fire2.m_canTurnOff = true;
                SetFuel(fire2, fire2.m_maxFuel - 3f);
                MakeOther(fire2.m_nview);
                Hooks.WatchRpcs(fire2.m_nview);
                var p1 = Press(pit2, alt: true);
                var adds = Hooks.Rpcs.Count;
                var p2 = Press(pit2, alt: false);
                var lastRpc = Hooks.Rpcs.Count > 0 ? Hooks.Rpcs[Hooks.Rpcs.Count - 1] : "";
                Hooks.Off();
                c.Check(p1.Removed == 3 && adds == 3, $"M09 fire: batch sends 3 adds ({p1.Removed} removed, {adds} rpcs)");
                c.Check(p2.Removed == 0 && p2.Messages == 0 && lastRpc == "RPC_ToggleOn",
                    $"M09 fire: the plain E after the batch is not blocked, it asks the owner to toggle (last rpc '{lastRpc}', {Show(p2)})");
            }

            // M06: iron cooking station, two batches: never more than the free slots.
            var meat = Token("RawMeat");
            var ironGo = rig.Spawn("piece_cookingstation_iron", -8f, 7f);
            var iron = ironGo != null ? ironGo.GetComponent<CookingStation>() : null;
            if (c.Check(iron != null && meat != null && CookConversion(iron, "RawMeat") != null, "spawn 'piece_cookingstation_iron'")
                && c.Check(PutFireUnder(rig, iron, out var how), $"iron station over a lit fire ({how})"))
            {
                rig.Empty();
                rig.Give("RawMeat", 10);
                var n = iron.m_slots.Length;
                MakeOther(iron.m_nview);
                var p1 = Press(ironGo, alt: true);
                var mark = tap.Mark();
                var p2 = Press(ironGo, alt: true);
                c.Check(p1.Removed == n && p1.Center == Loc($"$msg_added {n} {meat} ({n}/{n})"), $"M06 first batch: {n} pieces, one per free slot ({Show(p1)})");
                c.Check(p2.Removed == 0 && p2.Messages == 1 && p2.Center == Loc("$msg_nocookroom") && rig.Count(meat) == 10 - n,
                    $"M06 second batch: 'no room', nothing removed ({Show(p2)}, meat left {rig.Count(meat)})");
                ExpectLog(c, "M06 second batch", tap.Batch(mark), "CookFood", "owner=other", "stop=pending");
            }

            // M09 (cooking part): a cooked piece can be taken out right after a non-owner batch filled the station.
            var cookGo = rig.Spawn("piece_cookingstation", -16f, 7f);
            var cook = cookGo != null ? cookGo.GetComponent<CookingStation>() : null;
            var conversion = cook != null ? CookConversion(cook, "RawMeat") : null;
            if (c.Check(cook != null && conversion != null && cook.m_slots.Length == 2, "spawn 'piece_cookingstation'")
                && c.Check(PutFireUnder(rig, cook, out how), $"cooking station over a lit fire ({how})"))
            {
                rig.Empty();
                rig.Give("RawMeat", 5);
                SetSlot(cook, 0, "RawMeat", 0f, 0);
                MakeOther(cook.m_nview);
                var p1 = Press(cookGo, alt: true);
                // The first piece gets done (in single player the local copy can be written; in a real game the owner does it).
                SetSlot(cook, 0, conversion.m_to.name, conversion.m_cookTime + 1f, 1);
                Hooks.WatchRpcs(cook.m_nview);
                var p2 = Press(cookGo, alt: false);
                var p3 = Press(cookGo, alt: true);
                var takes = 0;
                foreach (var rpc in Hooks.Rpcs)
                {
                    takes += rpc == "RPC_RemoveDoneItem" ? 1 : 0;
                }
                Hooks.Off();
                c.Check(p1.Removed == 1 && p1.Center == Loc($"$msg_added 1 {meat} (2/2)"), $"M09 batch fills the other slot ({Show(p1)})");
                c.Check(p2.Removed == 0 && p3.Removed == 0 && takes == 2 && p2.Center != Loc("$msg_nocookroom") && p3.Center != Loc("$msg_nocookroom"),
                    $"M09 E and Shift+E with a cooked piece ask the owner to hand it out, never 'There is no room' ({takes} take-out rpcs, '{p2.Center}')");
            }

            // M07: ballista, wooden missiles in an earlier slot than black metal ones.
            var wood = Token("TurretBoltWood");
            var black = Token("TurretBolt");
            var turretGo = rig.Spawn("piece_turret", 12f, 7f);
            var turret = turretGo != null ? turretGo.GetComponent<Turret>() : null;
            if (c.Check(turret != null && wood != null && black != null, "spawn 'piece_turret'"))
            {
                rig.Empty();
                c.Check(rig.GiveAt("TurretBoltWood", 2, 0, 0) && rig.GiveAt("TurretBolt", 20, 1, 0), "give 2 TurretBoltWood (slot 1) and 20 TurretBolt (slot 2)");
                MakeOther(turret.m_nview);
                var mark = tap.Mark();
                var p1 = Press(turretGo, alt: true);
                var line1 = tap.Batch(mark);
                var p2 = Press(turretGo, alt: false);
                mark = tap.Mark();
                var p3 = Press(turretGo, alt: true);
                var other = Loc("$msg_turretotherammo") + Loc(wood);
                c.Check(p1.Removed == 2 && p1.Center == Loc($"$msg_added 2 {wood} (2/{turret.m_maxAmmo})"), $"M07 batch loads the 2 wooden missiles only ({Show(p1)})");
                c.Check(p2.Removed == 0 && p2.Messages == 1 && p2.Center == other, $"M07 E within 3 s: 'already loaded with Wooden Missile', nothing removed ({Show(p2)})");
                c.Check(p3.Removed == 0 && p3.Messages == 1 && p3.Center == other, $"M07 Shift+E within 3 s: same message, nothing removed ({Show(p3)})");
                c.Check(rig.Count(black) == 20 && rig.Count(wood) == 0, $"M07 20 black metal missiles kept (have {rig.Count(black)})");
                ExpectLog(c, "M07 batch", line1, "TurretAmmo", "owner=other", "added=2", "stop=otherammo");
                ExpectLog(c, "M07 second batch", tap.Batch(mark), "owner=other", "stop=otherammo (sent TurretBoltWood");
            }

            // M08: shield generator and oven fuel 2 below max, three batches each.
            var shieldGo = rig.Spawn("piece_shieldgenerator", 20f, 7f);
            var shield = shieldGo != null ? shieldGo.GetComponentInChildren<ShieldGenerator>() : null;
            if (c.Check(shield != null && shield.m_addFuelSwitch != null && shield.m_maxFuel >= 4, "spawn 'piece_shieldgenerator'"))
            {
                yield return ShieldReady(shield);
            }
            if (shield != null && shield.m_addFuelSwitch != null && shield.m_maxFuel >= 4
                && c.Check(shield.m_nview != null && shield.m_nview.IsValid(), "the shield generator is awake one frame after it is built"))
            {
                rig.Empty();
                rig.Give("BoneFragments", 10);
                shield.m_nview.GetZDO().Set(ZDOVars.s_fuel, (float)(shield.m_maxFuel - 2));
                MakeOther(shield.m_nview);
                var sw = shield.m_addFuelSwitch.gameObject;
                var p1 = Press(sw, alt: true);
                var p2 = Press(sw, alt: true);
                var p3 = Press(sw, alt: true);
                c.Check(p1.Removed == 2 && p2.Removed == 0 && p3.Removed == 0 && p2.Center == Loc("$msg_itsfull") && p3.Messages == 1,
                    $"M08 shield generator 2 below max: 2, then 'It's full' twice ({p1.Removed}, {p2.Removed}, {p3.Removed}, '{p2.Center}')");
            }
            var ovenGo = rig.Spawn("piece_oven", 28f, 7f);
            var oven = ovenGo != null ? ovenGo.GetComponent<CookingStation>() : null;
            if (c.Check(oven != null && oven.m_addFuelSwitch != null && oven.m_fuelItem != null && oven.m_maxFuel >= 4, "spawn 'piece_oven'"))
            {
                rig.Empty();
                rig.Give(oven.m_fuelItem.name, 10);
                oven.m_nview.GetZDO().Set(ZDOVars.s_fuel, (float)(oven.m_maxFuel - 2));
                MakeOther(oven.m_nview);
                var sw = oven.m_addFuelSwitch.gameObject;
                var p1 = Press(sw, alt: true);
                var p2 = Press(sw, alt: true);
                var p3 = Press(sw, alt: true);
                c.Check(p1.Removed == 2 && p2.Removed == 0 && p3.Removed == 0 && p2.Center == Loc("$msg_itsfull") && p3.Messages == 1,
                    $"M08 oven fuel 2 below max: 2, then 'It's full' twice ({p1.Removed}, {p2.Removed}, {p3.Removed}, '{p2.Center}')");
            }
            c.Report();
        }
        finally
        {
            tap?.Dispose();
            rig?.End();
        }
    }

    // ---------- batchfeed.ownergone (M10) ----------

    private static IEnumerator RunOwnerGone()
    {
        var c = new Checks(OwnerGoneName);
        Rig rig = null;
        LogTap tap = null;
        try
        {
            rig = new Rig();
            tap = new LogTap();
            HudReady(c);
            var oreToken = Token("CopperOre");
            var go = rig.Spawn("smelter", 0f, 7f);
            var smelter = go != null ? go.GetComponentInChildren<Smelter>() : null;
            if (!c.Check(smelter != null && smelter.m_addOreSwitch != null && oreToken != null, "spawn a smelter"))
            {
                c.Report();
                yield break;
            }
            yield return null;
            var ore = smelter.m_addOreSwitch.gameObject;
            var zdo = smelter.m_nview.GetZDO();
            rig.Give("CopperOre", 20);

            // Owner uid of no connected game (the owner just logged out): one vanilla press, not a batch.
            zdo.SetOwner(MadeUpOwner);
            var mark = tap.Mark();
            var p = Press(ore, alt: true);
            c.Check(p.Removed == 1 && p.Messages == 1 && p.Center == Loc("$msg_added " + oreToken),
                $"M10 owner gone: Shift+E is a single vanilla press, at most 1 ore leaves ({Show(p)})");
            ExpectLog(c, "M10 owner gone", tap.Batch(mark), "smelter SmelterInput", "owner=gone");

            // The world hands the station to this game (every 2 s): batch works again.
            yield return Until(() => zdo.IsOwner(), 8f);
            if (c.Check(zdo.IsOwner(), "M10 the station got a new owner (this game) within 8 s"))
            {
                var queue = smelter.GetQueueSize();
                mark = tap.Mark();
                p = Press(ore, alt: true);
                c.Check(p.Removed == 5 && smelter.GetQueueSize() == queue + 5, $"M10 a few seconds later Shift+E adds 5 again (queue {queue} -> {smelter.GetQueueSize()})");
                ExpectLog(c, "M10 after the hand-over", tap.Batch(mark), "owner=you", "added=5");
            }
            c.Report();
        }
        finally
        {
            tap?.Dispose();
            rig?.End();
        }
    }

    // ---------- batchfeed.toggle (T21, framework toggle played by hand) ----------

    // Same patching as ModPlugin.ApplyPatches (every patch class of the mod, same Harmony id).
    private static void Repatch(Harmony harmony)
    {
        foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
        {
            if (!type.IsDefined(typeof(AlwaysOnPatchAttribute), false))
            {
                harmony.CreateClassProcessor(type).Patch();
            }
        }
    }

    private static IEnumerator RunToggle()
    {
        var c = new Checks(ToggleName);
        Rig rig = null;
        Harmony harmony = null;
        var off = false;
        try
        {
            rig = new Rig();
            HudReady(c);
            var oreToken = Token("CopperOre");
            var goA = rig.Spawn("smelter", -4f, 7f);
            var goB = rig.Spawn("smelter", 4f, 7f);
            var a = goA != null ? goA.GetComponentInChildren<Smelter>() : null;
            var b = goB != null ? goB.GetComponentInChildren<Smelter>() : null;
            if (!c.Check(ModActive(ModInfo.Guid) && a != null && b != null && a.m_addOreSwitch != null && oreToken != null, "the feature is active; spawn two smelters"))
            {
                c.Report();
                yield break;
            }
            yield return null;
            rig.Give("CopperOre", 20);
            ExpectHint(c, "feature on", a.m_addOreSwitch.GetHoverText());

            // Off like the framework does it (single player must not write Enabled to the cfg): the feature's own
            // clean-up, then every patch of this mod gone.
            harmony = new Harmony(ModInfo.Guid);
            BatchFeeder.Reset();
            PendingAdds.Clear();
            HoverHint.Clear();
            harmony.UnpatchSelf();
            off = true;
            yield return null;
            ExpectNoHint(c, "T21 feature off", a.m_addOreSwitch.GetHoverText());
            var p = Press(a.m_addOreSwitch.gameObject, alt: true);
            c.Check(p.Removed == 1 && a.GetQueueSize() == 1 && p.Messages == 1 && p.Center == Loc("$msg_added " + oreToken),
                $"T21 feature off: Shift+E adds 1, vanilla's own message ({Show(p)})");

            Repatch(harmony);
            off = false;
            ForceDefaults();
            yield return null;
            ExpectHint(c, "T21 feature on again", b.m_addOreSwitch.GetHoverText());
            p = Press(b.m_addOreSwitch.gameObject, alt: true);
            c.Check(p.Removed == 5 && b.GetQueueSize() == 5, $"T21 feature on again: Shift+E adds 5, no restart ({Show(p)})");
            c.Report();
        }
        finally
        {
            if (off && harmony != null)
            {
                Repatch(harmony);
            }
            rig?.End();
        }
    }

    // ---------- batchfeed.othermod (C03) ----------

    private static IEnumerator RunOtherMod()
    {
        var c = new Checks(OtherModName);
        Rig rig = null;
        LogTap tap = null;
        try
        {
            rig = new Rig();
            tap = new LogTap();
            HudReady(c);
            var oreToken = Token("CopperOre");
            var goA = rig.Spawn("smelter", -4f, 7f);
            var goB = rig.Spawn("smelter", 4f, 7f);
            var a = goA != null ? goA.GetComponentInChildren<Smelter>() : null;
            var b = goB != null ? goB.GetComponentInChildren<Smelter>() : null;
            if (!c.Check(a != null && b != null && a.m_addOreSwitch != null && a.m_maxOre == 10 && oreToken != null, "spawn two vanilla smelters"))
            {
                c.Report();
                yield break;
            }
            yield return null;
            rig.Give("CopperOre", 20);

            // Stand-in for BulkSmelt and friends: inside vanilla's add it adds two more ore itself.
            Hooks.PlayFillAllMod();
            var mark = tap.Mark();
            var p = Press(a.m_addOreSwitch.gameObject, alt: true);
            c.Check(p.Removed == 3 && a.GetQueueSize() == 3 && rig.Count(oreToken) == 17,
                $"C03 station you own: one press holds what the other mod filled (3) and no more (queue {a.GetQueueSize()}, removed {p.Removed})");
            c.Check(p.Messages == 1 && p.Center == Loc($"$msg_added 3 {oreToken} (3/10)"), $"C03 one summary for what really went in ({Show(p)})");
            ExpectLog(c, "C03 owner", tap.Batch(mark), "owner=you", "added=3", "presses=1", "stop=othermod");

            // Same on a smelter of another game (made-up owner): stop after the first press, count what was sent.
            BatchFeeder.TestConnectedOwner = MadeUpOwner;
            var ore = b.m_addOreSwitch.gameObject;
            MakeOther(b.m_nview);
            mark = tap.Mark();
            p = Press(ore, alt: true);
            var sent = FeedTarget.TryResolve(ore, out var target) ? PendingAdds.Effective(target, target.ReadValue()) : -1f;
            c.Check(p.Removed == 3 && Near(sent, 3f) && rig.Count(oreToken) == 14, $"C03 station of another game: the loop stops after the first press, 3 counted as sent (removed {p.Removed}, counted {sent})");
            ExpectLog(c, "C03 non-owner", tap.Batch(mark), "owner=other", "added=3", "presses=1", "stop=othermod");
            Hooks.Off();
            c.Report();
        }
        finally
        {
            tap?.Dispose();
            rig?.End();
        }
    }

    // ---------- batchfeed.compat-repair (C01) ----------

    private static IEnumerator RunCompatRepair()
    {
        var c = new Checks(RepairName);
        Rig rig = null;
        try
        {
            rig = new Rig();
            HudReady(c);
            var oreToken = Token("CopperOre");
            var go = rig.Spawn("smelter", 0f, 7f);
            var smelter = go != null ? go.GetComponentInChildren<Smelter>() : null;
            // Without the other mod there is nothing to check: fail, never a silent pass.
            if (!c.Check(ModActive(RepairGuid), "One Click Repair All (MC) is loaded and active (this check needs both mods)")
                || !c.Check(smelter != null && smelter.m_addOreSwitch != null && oreToken != null, "spawn a smelter"))
            {
                c.Report();
                yield break;
            }
            yield return null;
            rig.Give("CopperOre", 10);
            var hud = MessageHud.instance;

            var p = Press(smelter.m_addOreSwitch.gameObject, alt: true);
            c.Check(p.Removed == 5 && p.Messages == 1 && p.Center == Loc($"$msg_added 5 {oreToken} (5/{smelter.m_maxOre})"),
                $"C01 the batch summary shows with the repair mod's message hook installed ({Show(p)})");
            c.Check(!BatchFeeder.Muting && BatchFeeder.LastMuted == null, "C01 nothing stays muted after the batch");

            // Any center message right after the batch (a repair summary is one) reaches the screen.
            const string probe = "Batch Station Feeding self test";
            var fades = hud._crossFadeTextBuffer.Count;
            hud.ShowMessage(MessageHud.MessageType.Center, probe);
            c.Check(hud.m_messageCenterText.text == probe && hud._crossFadeTextBuffer.Count - fades == 2,
                $"C01 a center message right after a batch is not swallowed (text '{hud.m_messageCenterText.text}')");

            // Only inside the loop: center messages are held back, top-left ones pass.
            var topLeft = hud.m_msgQeue.Count;
            try
            {
                BatchFeeder.Muting = true;
                hud.ShowMessage(MessageHud.MessageType.Center, "muted line");
                hud.ShowMessage(MessageHud.MessageType.TopLeft, probe);
                c.Check(hud.m_messageCenterText.text == probe && BatchFeeder.LastMuted == "muted line" && hud.m_msgQeue.Count == topLeft + 1,
                    "inside the loop a center message is held back (kept as the refusal reason) and a top-left one passes");
            }
            finally
            {
                BatchFeeder.Muting = false;
                BatchFeeder.LastMuted = null;
            }
            c.Report();
        }
        finally
        {
            rig?.End();
        }
    }

    // ---------- batchfeed.compat-tames (C02) ----------

    private const string TamesKey = StatsGuid + ".Tames";

    // Creature Kill and Tame Counts keeps its tame counts in Player.m_customData under a documented key and format:
    // first line = format version, then "<count><TAB><creature name>" lines.
    private static int StoredTames(Player player, string creature)
    {
        if (player.m_customData == null || !player.m_customData.TryGetValue(TamesKey, out var raw) || string.IsNullOrEmpty(raw))
        {
            return 0;
        }
        var lines = raw.Split('\n');
        for (var i = 1; i < lines.Length; i++)
        {
            var tab = lines[i].IndexOf('\t');
            if (tab > 0 && lines[i].Substring(tab + 1) == creature && int.TryParse(lines[i].Substring(0, tab), out var count))
            {
                return count;
            }
        }
        return 0;
    }

    private static IEnumerator RunCompatTames()
    {
        var c = new Checks(TamesName);
        Rig rig = null;
        var player = Player.m_localPlayer;
        var hadKey = false;
        string oldRaw = null;
        var touched = false;
        try
        {
            rig = new Rig();
            HudReady(c);
            var go = rig.Spawn("smelter", 0f, 7f);
            var smelter = go != null ? go.GetComponentInChildren<Smelter>() : null;
            if (!c.Check(ModActive(StatsGuid), "Creature Kill and Tame Counts (MC) is loaded and active (this check needs both mods)")
                || !c.Check(smelter != null && smelter.m_addOreSwitch != null && player.m_customData != null, "spawn a smelter"))
            {
                c.Report();
                yield break;
            }
            yield return null;
            hadKey = player.m_customData.TryGetValue(TamesKey, out oldRaw);
            touched = true;
            rig.Give("CopperOre", 10);
            var hud = MessageHud.instance;

            // Inside the loop a tame message always passes.
            const string fake = "Batch Station Feeding self test $hud_tamedone";
            try
            {
                BatchFeeder.Muting = true;
                hud.ShowMessage(MessageHud.MessageType.Center, fake);
                c.Check(hud.m_messageCenterText.text == Loc(fake) && BatchFeeder.LastMuted == null, "a tame message shown while the loop runs is never held back");
            }
            finally
            {
                BatchFeeder.Muting = false;
                BatchFeeder.LastMuted = null;
            }

            // Batch, then right away a tame: message shows, the other mod counts it.
            var p = Press(smelter.m_addOreSwitch.gameObject, alt: true);
            c.Check(p.Removed == 5, $"batch on the smelter adds 5 ({Show(p)})");
            var boarGo = rig.SpawnAt("Boar", rig.At(-3f, 3f) + Vector3.up * 0.5f);
            var tame = boarGo != null ? boarGo.GetComponent<Tameable>() : null;
            var boar = boarGo != null ? boarGo.GetComponent<Character>() : null;
            if (c.Check(tame != null && boar != null, "spawn a 'Boar'"))
            {
                var before = StoredTames(player, boar.m_name);
                var fades = hud._crossFadeTextBuffer.Count;
                tame.Tame();
                c.Check(boar.IsTamed() && hud.m_messageCenterText.text == Loc(boar.m_name + " $hud_tamedone") && hud._crossFadeTextBuffer.Count - fades == 2,
                    $"C02 'Boar has been tamed' shows right after a batch (text '{hud.m_messageCenterText.text}')");
                yield return null;
                var after = StoredTames(player, boar.m_name);
                c.Check(after == before + 1, $"C02 the Boar tame count of Creature Kill and Tame Counts went up by 1 ({before} -> {after})");
            }
            c.Report();
        }
        finally
        {
            // Test character's tame counts as before.
            if (touched && player != null && player.m_customData != null)
            {
                if (hadKey)
                {
                    player.m_customData[TamesKey] = oldRaw;
                }
                else
                {
                    player.m_customData.Remove(TamesKey);
                }
            }
            rig?.End();
        }
    }

    // ---------- batchfeed.compat-lights (C04) ----------

    private static IEnumerator RunCompatLights()
    {
        var c = new Checks(LightsName);
        Rig rig = null;
        try
        {
            rig = new Rig();
            HudReady(c);
            if (!c.Check(ModActive(LightsGuid), "Switchable Lights (MC) is loaded and active (this check needs both mods)"))
            {
                c.Report();
                yield break;
            }

            // Sconce and standing torch: switches now. No hint, Shift+E toggles once like E, no fuel taken.
            foreach (var prefab in new[] { "piece_walltorch", "piece_groundtorch_wood" })
            {
                var go = rig.Spawn(prefab, 0f, 6f);
                var fire = go != null ? go.GetComponent<Fireplace>() : null;
                if (!c.Check(fire != null && fire.m_fuelItem != null, $"spawn '{prefab}'"))
                {
                    continue;
                }
                var what = "C04 " + prefab;
                // Its first owner tick lights it (fuel topped up to the maximum).
                yield return Until(() => Near(Fuel(fire), fire.m_maxFuel), 4f);
                rig.Empty();
                var resin = fire.m_fuelItem.m_itemData.m_shared.m_name;
                rig.Give(fire.m_fuelItem.name, 10);
                c.Check(!fire.m_canRefill && !FeedTarget.TryResolve(go, out _), $"{what}: a managed light is not a batch target");
                ExpectNoHint(c, what, fire.GetHoverText());
                var lit = Near(Fuel(fire), fire.m_maxFuel);
                var p = Press(go, alt: true);
                var offNow = Near(Fuel(fire), 0f);
                c.Check(lit && offNow && p.Removed == 0 && rig.Count(resin) == 10,
                    $"{what}: Shift+E switches the light exactly once (lit before {lit}, off after {offNow}) and takes no {fire.m_fuelItem.name} ({rig.Count(resin)} left)");
                yield return null;
                p = Press(go, alt: false);
                c.Check(Near(Fuel(fire), fire.m_maxFuel) && p.Removed == 0, $"{what}: plain E switches it back on, like Shift+E did off (fuel {Fuel(fire)})");
                rig.Destroy(go);
                yield return null;
            }

            // Real fires still batch.
            foreach (var prefab in new[] { "fire_pit", "hearth", "bonfire", "piece_brazierfloor01" })
            {
                var go = rig.Spawn(prefab, 0f, 7f);
                var fire = go != null ? go.GetComponent<Fireplace>() : null;
                if (!c.Check(fire != null && FeedTarget.FireSkip(fire) == null, $"C04 '{prefab}' is still a batch target"))
                {
                    rig.Destroy(go);
                    continue;
                }
                rig.Empty();
                rig.Give(fire.m_fuelItem.name, 10);
                SetFuel(fire, 0f);
                var n = Mathf.Min(5, Mathf.RoundToInt(fire.m_maxFuel));
                var p = Press(go, alt: true);
                c.Check(p.Removed == n && Near(Fuel(fire), n), $"C04 {prefab}: Shift+E still adds {n} fuel (fuel {Fuel(fire)}, removed {p.Removed})");
                ExpectHint(c, "C04 " + prefab, fire.GetHoverText());
                rig.Destroy(go);
                yield return null;
            }
            c.Report();
        }
        finally
        {
            rig?.End();
        }
    }

    // ---------- batchfeed.hint-follows, batchfeed.bug.hint-stale (C05) ----------

    // What Switchable Lights (MC) does to a light when it is turned on: the instance stops taking fuel
    // (m_canRefill = false) and its hover keeps a Use line. Turned off: the flag goes back.
    private static void MakeSwitch(Fireplace fire, bool on)
    {
        fire.m_canRefill = !on;
        fire.m_canTurnOff = on;
    }

    // The crosshair stays on this spot: the game asks its hover text every frame.
    private static IEnumerator KeepLooking(Fireplace fire, float seconds)
    {
        var end = Time.time + seconds;
        while (Time.time < end)
        {
            yield return null;
            if (fire == null)
            {
                yield break;
            }
            fire.GetHoverText();
        }
    }

    // What already works: the press follows the flag at once, and the hint is right after a look at another covered spot.
    private static IEnumerator RunHintFollows()
    {
        var c = new Checks(HintFollowsName);
        Rig rig = null;
        try
        {
            rig = new Rig();
            var goA = rig.Spawn("fire_pit", -3f, 7f);
            var goB = rig.Spawn("fire_pit", 3f, 7f);
            var a = goA != null ? goA.GetComponent<Fireplace>() : null;
            var b = goB != null ? goB.GetComponent<Fireplace>() : null;
            if (!c.Check(a != null && b != null && FeedTarget.FireSkip(a) == null && FeedTarget.FireSkip(b) == null, "spawn two campfires"))
            {
                c.Report();
                yield break;
            }
            SetFuel(a, 3f);
            SetFuel(b, 3f);
            rig.Give(a.m_fuelItem.name, 20);
            var wood = a.m_fuelItem.m_itemData.m_shared.m_name;

            // A turns into a switch. Same frame: the press already knows (it resolves the spot every time).
            ExpectHint(c, "a campfire first", a.GetHoverText());
            MakeSwitch(a, true);
            var fuel = Fuel(a);
            var p = Press(goA, alt: true);
            c.Check(p.Removed == 0 && rig.Count(wood) == 20 && Near(Fuel(a), fuel), $"C05 Shift+E on a fire that stopped taking fuel adds nothing (removed {p.Removed}, fuel {fuel} -> {Fuel(a)})");
            // Look at another covered spot and back: the hint is right.
            b.GetHoverText();
            var hover = a.GetHoverText();
            c.Check(hover.IndexOf(Anchor, StringComparison.Ordinal) >= 0, $"the fire still shows a Use line: '{hover}'");
            ExpectNoHint(c, "C05 a fire that stopped taking fuel, after a look at another fire", hover);

            // A takes fuel again.
            MakeSwitch(a, false);
            SetFuel(a, 3f);
            p = Press(goA, alt: true);
            c.Check(p.Removed == 5 && Near(Fuel(a), 8f), $"C05 Shift+E on a fire that takes fuel again adds 5 at once (removed {p.Removed}, fuel {Fuel(a)})");
            b.GetHoverText();
            ExpectHint(c, "C05 a fire that takes fuel again, after a look at another fire", a.GetHoverText());
            c.Report();
        }
        finally
        {
            rig?.End();
        }
    }

    // REAL BUG, expected to fail until the mod is fixed: the hint keeps the covered / not covered answer of the
    // spot while the crosshair stays on it (HoverHint.Append resolves the spot only when the hovered component
    // changes). Only these two checks, so nothing else is blocked by it.
    private static IEnumerator RunHintStale()
    {
        var c = new Checks(HintStaleName);
        Rig rig = null;
        try
        {
            rig = new Rig();
            var goA = rig.Spawn("fire_pit", -3f, 7f);
            var goB = rig.Spawn("fire_pit", 3f, 7f);
            var a = goA != null ? goA.GetComponent<Fireplace>() : null;
            var b = goB != null ? goB.GetComponent<Fireplace>() : null;
            if (!c.Check(a != null && b != null && FeedTarget.FireSkip(a) == null && FeedTarget.FireSkip(b) == null, "spawn two campfires"))
            {
                c.Report();
                yield break;
            }
            // Fuel in both: a fire that can be turned off keeps its Use line only while it has fuel.
            SetFuel(a, 5f);
            SetFuel(b, 5f);
            if (!c.Check(HintLine(a.GetHoverText()) != null, "a campfire shows the hint first"))
            {
                c.Report();
                yield break;
            }

            // Covered fire becomes a switch while the crosshair stays on it.
            MakeSwitch(a, true);
            yield return KeepLooking(a, 0.7f);
            var hover = a.GetHoverText();
            c.Check(hover.IndexOf(Anchor, StringComparison.Ordinal) >= 0 && HintLine(hover) == null,
                $"C05 the hint goes away within about half a second when the fire you look at stops taking fuel, still shows '{HintLine(hover)}'");

            // The other way round: a switch becomes a refillable fire again while the crosshair stays on it.
            MakeSwitch(b, true);
            var none = HintLine(b.GetHoverText()) == null;
            MakeSwitch(b, false);
            yield return KeepLooking(b, 0.7f);
            var back = HintOk(b.GetHoverText(), AltLabel(), 5, out var detail);
            c.Check(none && back,
                $"C05 the hint comes back within about half a second when the fire you look at takes fuel again (no hint while it took none: {none}; {detail})");
            c.Report();
        }
        finally
        {
            rig?.End();
        }
    }

    // ---------- batchfeed.compat-claim (C06) ----------

    // A mod that makes this game the owner of a station right before vanilla's add (ValheimCommunityPatch, "Fix Fuel
    // And Ore Loss"). Stand-in: a prefix of Smelter.OnAddOre with the same ClaimOwnership call, so the test says the
    // same thing with or without that mod loaded.
    private static IEnumerator RunCompatClaim()
    {
        var c = new Checks(ClaimName);
        Rig rig = null;
        LogTap tap = null;
        try
        {
            rig = new Rig();
            tap = new LogTap();
            HudReady(c);
            BatchFeeder.TestConnectedOwner = MadeUpOwner;
            var oreToken = Token("CopperOre");
            var go = rig.Spawn("smelter", 0f, 7f);
            var smelter = go != null ? go.GetComponentInChildren<Smelter>() : null;
            if (!c.Check(smelter != null && smelter.m_addOreSwitch != null && smelter.m_maxOre == 10 && oreToken != null, "spawn a vanilla smelter (10 ore)"))
            {
                c.Report();
                yield break;
            }
            yield return null;
            var real = false;
            foreach (var info in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
            {
                real |= info != null && info.Metadata != null && info.Metadata.Name != null
                        && info.Metadata.Name.IndexOf("CommunityPatch", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            c.Note($"ValheimCommunityPatch loaded in this game: {real} (the stand-in prefix is used either way)");
            var ore = smelter.m_addOreSwitch.gameObject;
            c.Check(rig.Give("CopperOre", 20), "give 20 CopperOre");

            // The smelter is another game's when the press starts (plain made-up owner: it can be taken over).
            Hooks.PlayClaimMod();
            Hooks.WatchEffects(smelter.m_oreAddedEffects);
            smelter.m_nview.GetZDO().SetOwner(MadeUpOwner);
            var mark = tap.Mark();
            var p1 = Press(ore, alt: true);
            var line1 = tap.Batch(mark);
            var mine = smelter.m_nview.IsOwner();
            var queue1 = smelter.GetQueueSize();
            // Not a check: the mod mutes the extra add sounds only on a station that was its own when the press began.
            c.Note($"the add effect (sound) played {Hooks.EffectCount} time(s) during the first press, the one that made the station yours");
            mark = tap.Mark();
            var p2 = Press(ore, alt: true);
            var line2 = tap.Batch(mark);
            var p3 = Press(ore, alt: true);
            Hooks.Off();
            c.Check(mine, "C06 the first add made this game the owner of the station (the other mod's doing)");
            c.Check(p1.Removed == 5 && queue1 == 5 && p1.Messages == 1 && p1.Center == Loc($"$msg_added 5 {oreToken} (5/10)"),
                $"C06 first batch: 5 added, in the smelter at once, one summary (queue {queue1}, {Show(p1)})");
            c.Check(p2.Removed == 5 && p2.Messages == 1 && p2.Center == Loc($"$msg_added 5 {oreToken} (10/10)"), $"C06 second batch: 5 more ({Show(p2)})");
            c.Check(p3.Removed == 0 && p3.Messages == 1 && p3.Center == Loc("$msg_itsfull"), $"C06 third batch: 'It's full', nothing removed ({Show(p3)})");
            c.Check(rig.Count(oreToken) == 10 && smelter.GetQueueSize() == 10, $"C06 exactly 10 ore gone, 10 in the smelter, never above (ore left {rig.Count(oreToken)}, queue {smelter.GetQueueSize()})");
            c.Check(FeedTarget.TryResolve(ore, out var target) && Near(PendingAdds.Effective(target, target.ReadValue()), 10f),
                "C06 the mod keeps no count of 'sent' ore for a station that became its own");
            ExpectLog(c, "C06 first batch", line1, "owner=other", "room=5", "added=5", "presses=5", "stop=amount");
            ExpectLog(c, "C06 second batch", line2, "owner=you", "added=5");
            c.Report();
        }
        finally
        {
            tap?.Dispose();
            rig?.End();
        }
    }

    // ---------- batchfeed.log / batchfeed.mp.log (T23) ----------

    private static IEnumerator RunLog(string name)
    {
        var c = new Checks(name);
        yield return null;
        if (c.Check(SessionLog.Read(out var errors, out var first), "the session log listener is on"))
        {
            c.Check(errors == 0, $"T23 no error or exception line of this mod since it started, got {errors}, first: {first}");
        }
        c.Report();
    }
}
#endif
