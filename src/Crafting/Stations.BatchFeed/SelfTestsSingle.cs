#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using MC.Shared;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MC.Crafting.StationsBatchFeedMod;

// Single-player self tests (list and what each one check: SelfTests.cs). Every test: own bench, same-frame checks
// where the owner tick could change a value (fires burn, smelters process), all put back in finally.
internal static partial class SelfTests
{
    private const string SmelterName = "batchfeed.smelter";
    private const string FamilyName = "batchfeed.family";
    private const string FiresName = "batchfeed.fires";
    private const string CookingName = "batchfeed.cooking";
    private const string OvenName = "batchfeed.oven-foundry";
    private const string ShieldTurretName = "batchfeed.shield-turret";
    private const string HintName = "batchfeed.hint";
    private const string SettingsName = "batchfeed.settings";
    private const string RebindName = "batchfeed.rebind";
    private const string RebindKeysName = "batchfeed.rebind-keys";
    private const string RealKeyName = "batchfeed.realkey";
    private const string HoldName = "batchfeed.hold";
    private const string VanillaName = "batchfeed.vanilla";
    private const string SkillName = "batchfeed.skill";
    private const string CoverageName = "batchfeed.coverage";
    private const string NonOwnerSmelterName = "batchfeed.nonowner-smelter";
    private const string NonOwnerOthersName = "batchfeed.nonowner-others";
    private const string OwnerGoneName = "batchfeed.ownergone";
    private const string ToggleName = "batchfeed.toggle";
    private const string OtherModName = "batchfeed.othermod";
    private const string RepairName = "batchfeed.compat-repair";
    private const string TamesName = "batchfeed.compat-tames";
    private const string LightsName = "batchfeed.compat-lights";
    private const string HintFollowsName = "batchfeed.hint-follows";
    private const string HintStaleName = "batchfeed.bug.hint-stale";
    private const string ClaimName = "batchfeed.compat-claim";
    private const string LogName = "batchfeed.log";

    // Log test last: it look back at everything the others did.
    private static KeyValuePair<string, Func<IEnumerator>>[] SingleTests() => new[]
    {
        Test(SmelterName, RunSmelter),
        Test(FamilyName, RunFamily),
        Test(FiresName, RunFires),
        Test(CookingName, RunCooking),
        Test(OvenName, RunOvenFoundry),
        Test(ShieldTurretName, RunShieldTurret),
        Test(HintName, RunHint),
        Test(SettingsName, RunSettings),
        Test(RebindName, RunRebind),
        Test(RebindKeysName, RunRebindKeys),
        Test(RealKeyName, RunRealKey),
        Test(HoldName, RunHold),
        Test(VanillaName, RunVanilla),
        Test(SkillName, RunSkill),
        Test(CoverageName, RunCoverage),
        Test(NonOwnerSmelterName, RunNonOwnerSmelter),
        Test(NonOwnerOthersName, RunNonOwnerOthers),
        Test(OwnerGoneName, RunOwnerGone),
        Test(ToggleName, RunToggle),
        Test(OtherModName, RunOtherMod),
        Test(RepairName, RunCompatRepair),
        Test(TamesName, RunCompatTames),
        Test(LightsName, RunCompatLights),
        Test(HintFollowsName, RunHintFollows),
        Test(HintStaleName, RunHintStale),
        Test(ClaimName, RunCompatClaim),
        Test(LogName, () => RunLog(LogName)),
    };

    // ---------- batchfeed.smelter (T01-T05, T18, T19) ----------

    private static IEnumerator RunSmelter()
    {
        var c = new Checks(SmelterName);
        Rig rig = null;
        LogTap tap = null;
        try
        {
            rig = new Rig();
            tap = new LogTap();
            HudReady(c);

            var go = rig.Spawn("smelter", 0f, 6f);
            var smelter = go != null ? go.GetComponentInChildren<Smelter>() : null;
            if (smelter == null || smelter.m_addOreSwitch == null || smelter.m_addWoodSwitch == null || smelter.m_fuelItem == null)
            {
                c.Check(false, "could not spawn a 'smelter' with an ore switch, a coal switch and a fuel item");
                c.Report();
                yield break;
            }
            yield return null;
            var max = smelter.m_maxOre;
            var maxFuel = smelter.m_maxFuel;
            var oreToken = Token("CopperOre");
            var tinToken = Token("TinOre");
            var fuelName = smelter.m_fuelItem.name;
            var coalToken = smelter.m_fuelItem.m_itemData.m_shared.m_name;
            c.Note($"smelter: maxOre {max}, maxFuel {maxFuel}, fuel item {fuelName}, ore switch hold repeat {smelter.m_addOreSwitch.m_holdRepeatInterval} s");
            c.Check(max == 10 && maxFuel == 20 && fuelName == "Coal", $"smelter is the vanilla one (10 ore, 20 Coal), got {max} / {maxFuel} {fuelName}");
            c.Check(oreToken != null && tinToken != null && ConversionIndex(smelter, "CopperOre") >= 0 && ConversionIndex(smelter, "TinOre") >= 0,
                "the smelter takes CopperOre and TinOre");
            if (max != 10 || maxFuel != 20 || oreToken == null || tinToken == null)
            {
                c.Report();
                yield break;
            }
            var ore = smelter.m_addOreSwitch.gameObject;
            var coal = smelter.m_addWoodSwitch.gameObject;

            // T19: nothing the slot takes = vanilla's reason once, nothing removed.
            var mark = tap.Mark();
            var p = Press(ore, alt: true);
            c.Check(p.Removed == 0 && p.Messages == 1 && p.Center == Loc("$msg_noprocessableitems"),
                $"T19 ore slot without ore: one 'no processable items' message, nothing removed ({Show(p)})");
            ExpectLog(c, "T19 ore slot without ore", tap.Batch(mark), "owner=you", "added=0", "stop=refused");
            p = Press(coal, alt: true);
            c.Check(p.Removed == 0 && p.Messages == 1 && p.Center == Loc("$msg_donthaveany " + coalToken),
                $"T19 coal slot without coal: one 'don't have any Coal' message, nothing removed ({Show(p)})");

            // T01: 20 ore, one batch press = 5.
            c.Check(rig.Give("CopperOre", 20), "give 20 CopperOre");
            var effects = smelter.m_oreAddedEffects;
            Hooks.WatchEffects(effects);
            mark = tap.Mark();
            p = Press(ore, alt: true);
            var sounds = Hooks.EffectCount;
            Hooks.Off();
            c.Check(p.Removed == 5 && smelter.GetQueueSize() == 5 && rig.Count(oreToken) == 15,
                $"T01 batch on the ore slot adds 5: queue {smelter.GetQueueSize()}, ore left {rig.Count(oreToken)}, removed {p.Removed}");
            c.Check(p.Messages == 1 && p.Center == Loc($"$msg_added 5 {oreToken} (5/{max})"), $"T01 one summary 'Added 5 Copper Ore (5/10)' ({Show(p)})");
            c.Check(sounds == 1, $"T01 one add sound: the ore-added effect played {sounds} time(s)");
            c.Check(ReferenceEquals(smelter.m_oreAddedEffects, effects), "T01 the smelter's add effect list is the original one again after the press");
            c.Check(smelter.m_addOreSwitch.GetHoverText().Contains($"(5/{max})"), $"T01 hover shows 5/{max}: '{smelter.m_addOreSwitch.GetHoverText()}'");
            ExpectLog(c, "T01", tap.Batch(mark), "smelter SmelterInput", "owner=you", "room=5", "asked=5", "added=5", "presses=5", "stop=amount");

            // T02: plain E to 8/10 (each adds exactly one), batch adds 2, then "It's full".
            for (var i = 0; i < 3; i++)
            {
                p = Press(ore, alt: false);
                c.Check(p.Removed == 1 && p.Messages == 1 && p.Center == Loc("$msg_added " + oreToken), $"T02 plain E adds exactly 1 ({Show(p)})");
            }
            c.Check(smelter.GetQueueSize() == 8, $"T02 queue at 8 after three plain presses, is {smelter.GetQueueSize()}");
            mark = tap.Mark();
            p = Press(ore, alt: true);
            c.Check(p.Removed == 2 && smelter.GetQueueSize() == max && p.Messages == 1 && p.Center == Loc($"$msg_added 2 {oreToken} ({max}/{max})"),
                $"T02 batch at 8/10 adds exactly 2 with 'Added 2 Copper Ore (10/10)' (queue {smelter.GetQueueSize()}, {Show(p)})");
            ExpectLog(c, "T02", tap.Batch(mark), "room=2", "added=2", "stop=room");
            p = Press(ore, alt: true);
            c.Check(p.Removed == 0 && smelter.GetQueueSize() == max && p.Messages == 1 && p.Center == Loc("$msg_itsfull"),
                $"T02 batch on a full smelter: one 'It's full', nothing removed ({Show(p)})");
            c.Check(rig.Count(oreToken) == 10, $"T02 10 ore left after 5 + 3 + 2, have {rig.Count(oreToken)}");

            // T03: exactly 3 ore.
            rig.Destroy(go);
            yield return null;
            go = rig.Spawn("smelter", 0f, 6f);
            smelter = go.GetComponentInChildren<Smelter>();
            ore = smelter.m_addOreSwitch.gameObject;
            rig.Empty();
            c.Check(rig.Give("CopperOre", 3), "give 3 CopperOre");
            mark = tap.Mark();
            p = Press(ore, alt: true);
            c.Check(p.Removed == 3 && smelter.GetQueueSize() == 3 && rig.Count(oreToken) == 0,
                $"T03 with 3 ore the batch adds 3: queue {smelter.GetQueueSize()}, left {rig.Count(oreToken)}");
            c.Check(p.Messages == 1 && p.Center == Loc($"$msg_added 3 {oreToken} (3/{max})"), $"T03 one summary 'Added 3 Copper Ore (3/10)' ({Show(p)})");
            ExpectLog(c, "T03", tap.Batch(mark), "added=3", "presses=3", "stop=refused");

            // T04: 3 copper + 3 tin = 5 in total, the smelter's own list order decides which kind goes first.
            rig.Destroy(go);
            yield return null;
            go = rig.Spawn("smelter", 0f, 6f);
            smelter = go.GetComponentInChildren<Smelter>();
            ore = smelter.m_addOreSwitch.gameObject;
            rig.Empty();
            c.Check(rig.Give("CopperOre", 3) && rig.Give("TinOre", 3), "give 3 CopperOre and 3 TinOre");
            var copperFirst = ConversionIndex(smelter, "CopperOre") < ConversionIndex(smelter, "TinOre");
            var copperGone = copperFirst ? 3 : 2;
            var tinGone = 5 - copperGone;
            p = Press(ore, alt: true);
            c.Check(p.Removed == 5 && smelter.GetQueueSize() == 5 && rig.Count(oreToken) == 3 - copperGone && rig.Count(tinToken) == 3 - tinGone,
                $"T04 mixed ore: 5 added, {copperGone} copper and {tinGone} tin (queue {smelter.GetQueueSize()}, copper left {rig.Count(oreToken)}, tin left {rig.Count(tinToken)})");
            var zdo = smelter.m_nview.GetZDO();
            var order = true;
            var queued = "";
            for (var i = 0; i < 5; i++)
            {
                var name = zdo.GetString("item" + i);
                queued += (i > 0 ? "," : "") + name;
                order &= name == ((i < 3) == copperFirst ? "CopperOre" : "TinOre");
            }
            c.Check(order, $"T04 queue holds the first kind of the smelter's list three times, then the other twice: {queued}");
            c.Check(p.Messages == 1 && p.Center == Loc($"$msg_added {copperGone} {oreToken}, {tinGone} {tinToken} (5/{max})"),
                $"T04 one summary lists both kinds in inventory order ({Show(p)})");

            // T05: coal slot. No ore in this smelter, so nothing burns.
            rig.Destroy(go);
            yield return null;
            go = rig.Spawn("smelter", 0f, 6f);
            smelter = go.GetComponentInChildren<Smelter>();
            coal = smelter.m_addWoodSwitch.gameObject;
            zdo = smelter.m_nview.GetZDO();
            rig.Empty();
            c.Check(rig.Give(fuelName, 30), "give 30 Coal");
            p = Press(coal, alt: true);
            c.Check(p.Removed == 5 && Near(smelter.GetFuel(), 5f) && p.Messages == 1 && p.Center == Loc($"$msg_added 5 {coalToken} (5/{maxFuel})"),
                $"T05 batch on the coal slot adds 5 with 'Added 5 Coal (5/20)' (fuel {smelter.GetFuel()}, {Show(p)})");
            p = Press(coal, alt: true);
            var p2 = Press(coal, alt: true);
            c.Check(p.Removed == 5 && p2.Removed == 5 && Near(smelter.GetFuel(), 15f), $"T05 two more batches: 15/20 (fuel {smelter.GetFuel()})");
            p = Press(coal, alt: false);
            p2 = Press(coal, alt: false);
            c.Check(p.Removed == 1 && p2.Removed == 1 && Near(smelter.GetFuel(), 17f), $"T05 plain E twice: 17/20 (fuel {smelter.GetFuel()})");
            mark = tap.Mark();
            p = Press(coal, alt: true);
            c.Check(p.Removed == 3 && Near(smelter.GetFuel(), 20f) && p.Messages == 1 && p.Center == Loc($"$msg_added 3 {coalToken} ({maxFuel}/{maxFuel})"),
                $"T05 batch at 17/20 adds exactly 3 (fuel {smelter.GetFuel()}, {Show(p)})");
            ExpectLog(c, "T05", tap.Batch(mark), "SmelterFuel", "room=3", "added=3", "stop=room");
            p = Press(coal, alt: true);
            c.Check(p.Removed == 0 && p.Messages == 1 && p.Center == Loc("$msg_itsfull"), $"T05 batch on a full coal slot: 'It's full', nothing removed ({Show(p)})");
            // Burning ore leaves a fraction: vanilla takes coal while the value is at most max - 1.
            zdo.Set(ZDOVars.s_fuel, maxFuel - 3.4f);
            p = Press(coal, alt: true);
            c.Check(p.Removed == 3 && smelter.GetFuel() > maxFuel - 1f && smelter.GetFuel() <= maxFuel
                    && p.Center == Loc($"$msg_added 3 {coalToken} ({maxFuel}/{maxFuel})"),
                $"T05 at 16.6 fuel the batch adds 3, like three vanilla presses (fuel {smelter.GetFuel()}, {Show(p)})");
            c.Check(rig.Count(coalToken) == 30 - 23, $"T05 coal left 7 after 23 adds, have {rig.Count(coalToken)}");

            // T18: hotbar key 1 with Coal in the first slot while the coal slot is hovered = vanilla, one coal.
            rig.Empty();
            c.Check(rig.GiveAt(fuelName, 5, 0, 0), "give 5 Coal into the first hotbar slot");
            zdo.Set(ZDOVars.s_fuel, 3f);
            var hud = MessageHud.instance;
            var fades = hud._crossFadeTextBuffer.Count;
            var hovering = rig.P.m_hovering;
            rig.P.m_hovering = coal;
            rig.P.UseHotbarItem(1);
            rig.P.m_hovering = hovering;
            c.Check(Near(smelter.GetFuel(), 4f) && rig.Count(coalToken) == 4, $"T18 hotbar key on the coal slot adds exactly 1 coal (fuel {smelter.GetFuel()}, coal left {rig.Count(coalToken)})");
            c.Check((hud._crossFadeTextBuffer.Count - fades) / 2 == 1 && hud.m_messageCenterText.text == Loc("$msg_added " + coalToken),
                $"T18 vanilla's own 'Added Coal' message once, is '{hud.m_messageCenterText.text}'");

            c.Report();
        }
        finally
        {
            tap?.Dispose();
            rig?.End();
        }
    }

    // ---------- batchfeed.family (T06) ----------

    private sealed class FamilyPiece
    {
        internal string Prefab;
        internal string Input;   // item the test list name, null = no input slot expected
        internal bool HasFuel;
        internal string Fuel;    // expected fuel item, null = only noted
        internal int MaxFuel;    // expected, 0 = not stated
    }

    private static IEnumerator RunFamily()
    {
        var c = new Checks(FamilyName);
        Rig rig = null;
        try
        {
            rig = new Rig();
            HudReady(c);
            var pieces = new[]
            {
                new FamilyPiece { Prefab = "blastfurnace", Input = "BlackMetalScrap", HasFuel = true, Fuel = "Coal" },
                new FamilyPiece { Prefab = "charcoal_kiln", Input = "Wood" },
                new FamilyPiece { Prefab = "windmill", Input = "Barley" },
                new FamilyPiece { Prefab = "piece_spinningwheel", Input = "Flax" },
                // Game data (run of 1.0.17): the refinery's queue slot takes Soft Tissue, its fuel slot takes Sap.
                new FamilyPiece { Prefab = "eitrrefinery", Input = "Softtissue", HasFuel = true, Fuel = "Sap" },
                new FamilyPiece { Prefab = "piece_FrostKiln", HasFuel = true, MaxFuel = 25 },
                new FamilyPiece { Prefab = "piece_bathtub", HasFuel = true, Fuel = "Wood", MaxFuel = 10 },
            };
            foreach (var piece in pieces)
            {
                var go = rig.Spawn(piece.Prefab, 0f, 7f);
                var smelter = go != null ? go.GetComponentInChildren<Smelter>() : null;
                if (!c.Check(smelter != null, $"spawn '{piece.Prefab}' with a Smelter part"))
                {
                    continue;
                }
                var what = "T06 " + piece.Prefab;
                rig.Empty();

                if (piece.Input != null)
                {
                    var token = Token(piece.Input);
                    var covered = smelter.m_addOreSwitch != null && FeedTarget.SmelterInputSkip(smelter) == null;
                    c.Check(covered && token != null && ConversionIndex(smelter, piece.Input) >= 0,
                        $"{what}: input slot covered and it takes {piece.Input} (it takes: {Inputs(smelter)})");
                    if (covered && token != null && rig.Give(piece.Input, 20))
                    {
                        var n = Mathf.Min(5, smelter.m_maxOre);
                        var p = Press(smelter.m_addOreSwitch.gameObject, alt: true);
                        c.Check(p.Removed == n && smelter.GetQueueSize() == n && rig.Count(token) == 20 - n,
                            $"{what}: batch adds {n} {piece.Input} (queue {smelter.GetQueueSize()}, removed {p.Removed})");
                        c.Check(p.Messages == 1 && p.Center == Loc($"$msg_added {n} {token} ({n}/{smelter.m_maxOre})"), $"{what}: one summary for the input ({Show(p)})");
                    }
                }
                else
                {
                    c.Check(FeedTarget.SmelterInputSkip(smelter) == "no input switch", $"{what}: no input slot (its only slot is fuel), got '{FeedTarget.SmelterInputSkip(smelter)}'");
                }

                if (piece.HasFuel)
                {
                    var covered = smelter.m_addWoodSwitch != null && smelter.m_fuelItem != null && FeedTarget.SmelterFuelSkip(smelter) == null;
                    c.Check(covered, $"{what}: fuel slot covered, got '{FeedTarget.SmelterFuelSkip(smelter)}'");
                    if (covered)
                    {
                        var fuelName = smelter.m_fuelItem.name;
                        var token = smelter.m_fuelItem.m_itemData.m_shared.m_name;
                        c.Note($"{piece.Prefab}: fuel item {fuelName}, maxFuel {smelter.m_maxFuel}, maxOre {smelter.m_maxOre}");
                        if (piece.Fuel != null)
                        {
                            c.Check(fuelName == piece.Fuel, $"{what}: fuel is {piece.Fuel}, got {fuelName}");
                        }
                        if (piece.MaxFuel > 0)
                        {
                            c.Check(smelter.m_maxFuel == piece.MaxFuel, $"{what}: holds {piece.MaxFuel} fuel, got {smelter.m_maxFuel}");
                        }
                        if (c.Check(rig.Give(fuelName, 20), $"{what}: give 20 {fuelName}"))
                        {
                            var n = Mathf.Min(5, smelter.m_maxFuel);
                            var p = Press(smelter.m_addWoodSwitch.gameObject, alt: true);
                            c.Check(p.Removed == n && Near(smelter.GetFuel(), n) && rig.Count(token) == 20 - n,
                                $"{what}: batch adds {n} {fuelName} (fuel {smelter.GetFuel()}, removed {p.Removed})");
                            c.Check(p.Messages == 1 && p.Center == Loc($"$msg_added {n} {token} ({n}/{smelter.m_maxFuel})"), $"{what}: one summary for the fuel ({Show(p)})");
                        }
                    }
                }
                else
                {
                    c.Check(FeedTarget.SmelterFuelSkip(smelter) != null, $"{what}: no fuel slot");
                }

                // Gone before its owner tick starts to process what me put in.
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

    // ---------- batchfeed.fires (T07, T08, T19) ----------

    private static IEnumerator RunFires()
    {
        var c = new Checks(FiresName);
        Rig rig = null;
        try
        {
            rig = new Rig();
            HudReady(c);
            var outOfFuelDone = false;
            foreach (var prefab in new[] { "fire_pit", "hearth", "bonfire", "piece_brazierfloor01", "piece_walltorch" })
            {
                var go = rig.Spawn(prefab, 0f, 7f);
                var fire = go != null ? go.GetComponent<Fireplace>() : null;
                if (!c.Check(fire != null && fire.m_nview != null && fire.m_nview.IsValid(), $"spawn '{prefab}' with a Fireplace"))
                {
                    continue;
                }
                var what = "T07 " + prefab;
                var skip = FeedTarget.FireSkip(fire);
                if (skip != null)
                {
                    if (prefab == "piece_walltorch" && ModActive(LightsGuid) && !fire.m_canRefill)
                    {
                        // Switchable Lights (MC) made the Sconce a switch: E toggles it, it takes no fuel (test batchfeed.compat-lights).
                        c.Note("piece_walltorch is a managed light of Switchable Lights here: not a batch target (see C04)");
                    }
                    else
                    {
                        c.Check(false, $"{what}: fire is not covered ({skip})");
                    }
                    rig.Destroy(go);
                    yield return null;
                    continue;
                }
                var fuelName = fire.m_fuelItem.name;
                var token = fire.m_fuelItem.m_itemData.m_shared.m_name;
                var max = fire.m_maxFuel;
                c.Note($"{prefab}: maxFuel {max}, fuel item {fuelName}, canTurnOff {fire.m_canTurnOff}, hold repeat {fire.m_holdRepeatInterval} s");
                rig.Empty();

                if (!outOfFuelDone)
                {
                    // T19: no fuel item on the player.
                    outOfFuelDone = true;
                    SetFuel(fire, Mathf.Min(2f, max - 1f));
                    var none = Press(go, alt: true);
                    c.Check(none.Removed == 0 && none.Messages == 1 && none.Center == Loc("$msg_outof " + token),
                        $"T19 {prefab} without fuel item: one 'You are out of Wood' message, nothing removed ({Show(none)})");
                }

                c.Check(rig.Give(fuelName, 15), $"{what}: give 15 {fuelName}");
                var start = Mathf.Max(0f, max - 3f);
                var expect = Mathf.Min(5, Mathf.RoundToInt(max - start));
                SetFuel(fire, start);
                var p = Press(go, alt: true);
                var after = Fuel(fire);
                c.Check(p.Removed == expect && Near(after, start + expect) && rig.Count(token) == 15 - expect,
                    $"{what}: a fire {expect} below its maximum gets exactly {expect}, inventory drops by the same (fuel {start} -> {after}, removed {p.Removed})");
                c.Check(p.Messages == 1 && p.Center == Loc(Words("$msg_fireadding", expect + " " + token) + " (" + Mathf.Ceil(after) + "/" + (int)max + ")"),
                    $"{what}: one summary 'Adding {expect} ... to the fire ({Mathf.Ceil(after)}/{(int)max})' ({Show(p)})");
                p = Press(go, alt: true);
                c.Check(p.Removed == 0 && Near(Fuel(fire), after) && p.Messages == 1 && p.Center == Loc(Words("$msg_cantaddmore", token)),
                    $"{what}: batch on a full fire: one 'can't add more', nothing removed ({Show(p)})");

                SetFuel(fire, 0f);
                var left = rig.Count(token);
                expect = Mathf.Min(5, Mathf.RoundToInt(max));
                p = Press(go, alt: true);
                after = Fuel(fire);
                c.Check(p.Removed == expect && Near(after, expect) && rig.Count(token) == left - expect,
                    $"{what}: an empty fire gets {expect} (fuel {after}, removed {p.Removed})");
                c.Check(p.Messages == 1 && p.Center == Loc(Words("$msg_fireadding", expect + " " + token) + " (" + Mathf.Ceil(after) + "/" + (int)max + ")"),
                    $"{what}: one summary for {expect} ({Show(p)})");
                if (after + 1f <= max)
                {
                    p = Press(go, alt: false);
                    c.Check(p.Removed == 1 && Near(Fuel(fire), after + 1f) && p.Center == Loc(Words("$msg_fireadding", token)),
                        $"{what}: plain E adds exactly 1 fuel ({Show(p)}, fuel {Fuel(fire)})");
                }
                rig.Destroy(go);
                yield return null;
            }

            // T08: no vanilla fire takes fuel AND can be turned off, so me flag one campfire instance (prefab untouched).
            var both = new List<string>();
            foreach (var prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab == null)
                {
                    continue;
                }
                foreach (var f in prefab.GetComponentsInChildren<Fireplace>(true))
                {
                    if (f.m_canRefill && f.m_canTurnOff && !f.m_infiniteFuel)
                    {
                        both.Add(prefab.name);
                    }
                }
            }
            c.Note($"T08 fires in the game data that take fuel and can be turned off: {(both.Count == 0 ? "none" : string.Join(", ", both.ToArray()))}");
            var pit = rig.Spawn("fire_pit", 0f, 7f);
            var toggle = pit != null ? pit.GetComponent<Fireplace>() : null;
            if (c.Check(toggle != null && FeedTarget.FireSkip(toggle) == null, "T08 spawn a campfire"))
            {
                rig.Empty();
                var token = toggle.m_fuelItem.m_itemData.m_shared.m_name;
                c.Check(rig.Give(toggle.m_fuelItem.name, 10), "T08 give 10 fuel items");
                toggle.m_canTurnOff = true;
                SetFuel(toggle, 2f);
                // Same frame: the owner tick would turn a wet fire off by itself.
                var p = Press(pit, alt: false);
                c.Check(p.Removed == 0 && State(toggle) == 2 && Near(Fuel(toggle), 2f), $"T08 plain E turns the fire off like vanilla, adds nothing (state {State(toggle)}, fuel {Fuel(toggle)}, removed {p.Removed})");
                p = Press(pit, alt: false);
                c.Check(p.Removed == 0 && State(toggle) == 1, $"T08 plain E again turns it on (state {State(toggle)})");
                p = Press(pit, alt: true);
                c.Check(p.Removed == 5 && Near(Fuel(toggle), 7f) && State(toggle) == 1,
                    $"T08 batch adds 5 fuel and does not toggle (state {State(toggle)}, fuel {Fuel(toggle)}, removed {p.Removed})");
                c.Check(p.Messages == 1 && p.Center == Loc(Words("$msg_fireadding", "5 " + token) + " (7/" + (int)toggle.m_maxFuel + ")"), $"T08 one summary ({Show(p)})");
                rig.Destroy(pit);
            }
            c.Report();
        }
        finally
        {
            rig?.End();
        }
    }
}
#endif
