#if DEBUG
using System.Collections;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Crafting.StationsBatchFeedMod;

// Single-player self tests of cooking stations, oven, foundry, shield generator, ballista and the cooking skill line.
internal static partial class SelfTests
{
    // Cooking skill parked here while a test add food: a level-up message would join the summary line.
    private const float ParkedSkillLevel = 50f;

    // ---------- batchfeed.cooking (T09) ----------

    private static IEnumerator RunCooking()
    {
        var c = new Checks(CookingName);
        Rig rig = null;
        try
        {
            rig = new Rig();
            HudReady(c);
            rig.ParkSkill(Skills.SkillType.Cooking, ParkedSkillLevel);
            var meat = Token("RawMeat");

            // Cooking Station, 2 slots, over a lit campfire.
            var go = rig.Spawn("piece_cookingstation", 0f, 6f);
            var station = go != null ? go.GetComponent<CookingStation>() : null;
            var conversion = station != null ? CookConversion(station, "RawMeat") : null;
            if (!c.Check(station != null && meat != null && conversion != null && station.m_addFoodSwitch == null,
                    "spawn 'piece_cookingstation' (no food switch) that cooks RawMeat"))
            {
                c.Report();
                yield break;
            }
            var lit = PutFireUnder(rig, station, out var how);
            c.Note($"piece_cookingstation: {station.m_slots.Length} slots, needs fire {station.m_requireFire}, fire: {how}");
            if (!c.Check(lit, $"the cooking station counts as over a lit fire ({how})"))
            {
                c.Report();
                yield break;
            }
            yield return null;
            var slots = station.m_slots.Length;
            c.Check(slots == 2, $"Cooking Station has 2 slots, has {slots}");

            // Nothing to cook: vanilla's reason once.
            var p = Press(go, alt: true);
            c.Check(p.Removed == 0 && p.Messages == 1 && p.Center == Loc(station.m_noCookableItemsMessage),
                $"batch with nothing to cook: vanilla's own message once ({Show(p)})");

            c.Check(rig.Give("RawMeat", 10), "give 10 RawMeat");
            p = Press(go, alt: true);
            c.Check(p.Removed == slots && UsedSlots(station) == slots && Slot(station, 0) == "RawMeat" && Slot(station, slots - 1) == "RawMeat",
                $"T09 batch fills both slots (used {UsedSlots(station)}, removed {p.Removed})");
            c.Check(p.Messages == 1 && p.Center == Loc($"$msg_added {slots} {meat} ({slots}/{slots})"), $"T09 one summary 'Added 2 Boar Meat (2/2)' ({Show(p)})");
            p = Press(go, alt: true);
            c.Check(p.Removed == 0 && p.Messages == 1 && p.Center == Loc("$msg_nocookroom"), $"batch on a full station: 'no room', nothing removed ({Show(p)})");

            // One piece cooked (owner write, like the owner tick does after the cook time): hint goes, Shift+E takes it.
            ExpectHint(c, "T09 station with raw meat only", station.GetHoverText());
            SetSlot(station, 0, conversion.m_to.name, conversion.m_cookTime + 1f, 1);
            yield return new WaitForSeconds(0.35f);
            ExpectNoHint(c, "T09 station holding a cooked piece", station.GetHoverText());
            var before = rig.Count(meat);
            p = Press(go, alt: true);
            var cooked = rig.NewDropCount(conversion.m_to.name);
            c.Check(p.Removed == 0 && rig.Count(meat) == before && Slot(station, 0) == "" && UsedSlots(station) == slots - 1,
                $"T09 Shift+E with a cooked piece takes exactly one piece and adds no raw meat (used {UsedSlots(station)}, removed {p.Removed})");
            c.Check(cooked >= 1, $"T09 the cooked piece came out as an item ({cooked} {conversion.m_to.name} on the ground)");
            yield return new WaitForSeconds(0.35f);
            ExpectHint(c, "T09 station after the cooked piece was taken", station.GetHoverText());

            // Plain E: exactly one.
            p = Press(go, alt: false);
            c.Check(p.Removed == 1 && UsedSlots(station) == slots, $"plain E adds exactly 1 ({Show(p)})");
            rig.Destroy(go);
            yield return null;

            // Iron Cooking Station, 5 slots.
            var iron = rig.Spawn("piece_cookingstation_iron", 6f, 6f);
            var ironStation = iron != null ? iron.GetComponent<CookingStation>() : null;
            if (c.Check(ironStation != null && CookConversion(ironStation, "RawMeat") != null, "spawn 'piece_cookingstation_iron' that cooks RawMeat"))
            {
                lit = PutFireUnder(rig, ironStation, out how);
                c.Note($"piece_cookingstation_iron: {ironStation.m_slots.Length} slots, fire: {how}");
                if (c.Check(lit, $"the iron cooking station counts as over a lit fire ({how})"))
                {
                    rig.Empty();
                    rig.Give("RawMeat", 10);
                    var n = ironStation.m_slots.Length;
                    c.Check(n == 5, $"Iron Cooking Station has 5 slots, has {n}");
                    p = Press(iron, alt: true);
                    c.Check(p.Removed == n && UsedSlots(ironStation) == n && p.Messages == 1 && p.Center == Loc($"$msg_added {n} {meat} ({n}/{n})"),
                        $"T09 iron station: batch adds 5 with 'Added 5 Boar Meat (5/5)' (used {UsedSlots(ironStation)}, {Show(p)})");
                }
                rig.Destroy(iron);
                yield return null;
            }

            // No fire below.
            var cold = rig.Spawn("piece_cookingstation", -14f, 6f);
            var coldStation = cold != null ? cold.GetComponent<CookingStation>() : null;
            if (c.Check(coldStation != null && !coldStation.IsFireLit(), "a cooking station 14 m from any fire has no fire below"))
            {
                rig.Empty();
                rig.Give("RawMeat", 10);
                p = Press(cold, alt: true);
                c.Check(p.Removed == 0 && UsedSlots(coldStation) == 0 && p.Messages == 1 && p.Center == Loc("$msg_needfire"),
                    $"T09 no fire below: one 'need a lit fire' message, nothing removed ({Show(p)})");
            }
            c.Report();
        }
        finally
        {
            rig?.End();
        }
    }

    // ---------- batchfeed.oven-foundry (T10) ----------

    private static IEnumerator RunOvenFoundry()
    {
        var c = new Checks(OvenName);
        Rig rig = null;
        try
        {
            rig = new Rig();
            HudReady(c);
            rig.ParkSkill(Skills.SkillType.Cooking, ParkedSkillLevel);

            // Stone Oven: fuel switch, food switch (4 slots), no fire needed.
            var go = rig.Spawn("piece_oven", 0f, 7f);
            var oven = go != null ? go.GetComponent<CookingStation>() : null;
            if (c.Check(oven != null && oven.m_addFuelSwitch != null && oven.m_addFoodSwitch != null && oven.m_fuelItem != null,
                    "spawn 'piece_oven' with food and fuel switches"))
            {
                var fuelName = oven.m_fuelItem.name;
                var fuelToken = oven.m_fuelItem.m_itemData.m_shared.m_name;
                var maxFuel = oven.m_maxFuel;
                var slots = oven.m_slots.Length;
                var inputs = new List<string>();
                foreach (var conversion in oven.m_conversion)
                {
                    if (conversion != null && conversion.m_from != null)
                    {
                        inputs.Add(conversion.m_from.name);
                    }
                }
                c.Note($"piece_oven: {slots} slots, maxFuel {maxFuel}, fuel {fuelName}, needs fire {oven.m_requireFire}, bakes {string.Join(", ", inputs.ToArray())}");
                c.Check(fuelName == "Wood" && maxFuel == 10 && slots == 4, $"T10 oven: 4 slots and 10 Wood, got {slots} slots, {maxFuel} {fuelName}");
                if (oven.m_requireFire)
                {
                    c.Check(PutFireUnder(rig, oven, out var how), $"oven needs a fire in this game: {how}");
                }

                c.Check(rig.Give(fuelName, 20), $"give 20 {fuelName}");
                var p = Press(oven.m_addFuelSwitch.gameObject, alt: true);
                c.Check(p.Removed == 5 && Near(oven.GetFuel(), 5f) && p.Messages == 1 && p.Center == Loc($"$msg_added 5 {fuelToken} (5/{maxFuel})"),
                    $"T10 oven fuel: batch adds 5 with 'Added 5 Wood (5/10)' (fuel {oven.GetFuel()}, {Show(p)})");
                p = Press(oven.m_addFuelSwitch.gameObject, alt: false);
                c.Check(p.Removed == 1 && Near(oven.GetFuel(), 6f), $"oven fuel: plain E adds exactly 1 ({Show(p)})");

                var dough = inputs.Contains("BreadDough") ? "BreadDough" : (inputs.Count > 0 ? inputs[0] : null);
                c.Check(dough == "BreadDough", $"T10 the oven bakes BreadDough (first input here: {dough})");
                var doughToken = dough != null ? Token(dough) : null;
                if (doughToken != null && c.Check(rig.Give(dough, 6), $"give 6 {dough}"))
                {
                    var n = Mathf.Min(5, slots);
                    p = Press(oven.m_addFoodSwitch.gameObject, alt: true);
                    c.Check(p.Removed == n && UsedSlots(oven) == n && rig.Count(doughToken) == 6 - n,
                        $"T10 oven food: batch fills {n} slots (used {UsedSlots(oven)}, removed {p.Removed})");
                    c.Check(p.Messages == 1 && p.Center == Loc($"$msg_added {n} {doughToken} ({n}/{slots})"), $"T10 one summary 'Added 4 Bread Dough (4/4)' ({Show(p)})");
                }
                rig.Destroy(go);
                yield return null;
            }

            // Frost Foundry: Liquid Frost fuel batched, cast slot (1 slot) stays vanilla.
            rig.Empty();
            go = rig.Spawn("piece_FrostFoundry", 0f, 7f);
            var foundry = go != null ? go.GetComponent<CookingStation>() : null;
            if (c.Check(foundry != null && foundry.m_addFuelSwitch != null && foundry.m_addFoodSwitch != null && foundry.m_fuelItem != null,
                    "spawn 'piece_FrostFoundry' with cast and fuel switches"))
            {
                var fuelName = foundry.m_fuelItem.name;
                var fuelToken = foundry.m_fuelItem.m_itemData.m_shared.m_name;
                var maxFuel = foundry.m_maxFuel;
                var casts = new List<string>();
                foreach (var conversion in foundry.m_conversion)
                {
                    if (conversion != null && conversion.m_from != null)
                    {
                        casts.Add(conversion.m_from.name);
                    }
                }
                c.Note($"piece_FrostFoundry: {foundry.m_slots.Length} slot(s), maxFuel {maxFuel}, fuel {fuelName}, needs fire {foundry.m_requireFire}, "
                       + $"{casts.Count} casts: {string.Join(", ", casts.GetRange(0, Mathf.Min(8, casts.Count)).ToArray())}{(casts.Count > 8 ? ", ..." : "")}");
                c.Check(fuelName == "FrozenFuel" && maxFuel == 20 && foundry.m_slots.Length == 1,
                    $"T10 foundry: 1 cast slot and 20 Liquid Frost (FrozenFuel), got {foundry.m_slots.Length} slot(s), {maxFuel} {fuelName}");
                if (foundry.m_requireFire)
                {
                    c.Check(PutFireUnder(rig, foundry, out var how), $"foundry needs a fire in this game: {how}");
                }

                c.Check(rig.Give(fuelName, 25), $"give 25 {fuelName}");
                var p = Press(foundry.m_addFuelSwitch.gameObject, alt: true);
                c.Check(p.Removed == 5 && Near(foundry.GetFuel(), 5f) && p.Messages == 1 && p.Center == Loc($"$msg_added 5 {fuelToken} (5/{maxFuel})"),
                    $"T10 foundry fuel: batch adds 5 with 'Added 5 Liquid Frost (5/20)' (fuel {foundry.GetFuel()}, {Show(p)})");
                foundry.m_nview.GetZDO().Set(ZDOVars.s_fuel, maxFuel - 3f);
                p = Press(foundry.m_addFuelSwitch.gameObject, alt: true);
                c.Check(p.Removed == 3 && Near(foundry.GetFuel(), maxFuel) && p.Center == Loc($"$msg_added 3 {fuelToken} ({maxFuel}/{maxFuel})"),
                    $"T10 foundry fuel near the maximum: only what fits (fuel {foundry.GetFuel()}, {Show(p)})");

                var castSwitch = foundry.m_addFoodSwitch.gameObject;
                c.Check(!FeedTarget.TryResolve(castSwitch, out _), "T10 the cast slot is not a batch target (it holds 1)");
                ExpectNoHint(c, "T10 foundry cast slot", foundry.m_addFoodSwitch.GetHoverText());
                var cast = casts.Contains("AtgeirGoldUncooked") ? "AtgeirGoldUncooked" : (casts.Count > 0 ? casts[0] : null);
                c.Note($"cast used: {cast} (AtgeirGoldUncooked accepted: {casts.Contains("AtgeirGoldUncooked")})");
                rig.Empty();
                if (cast != null && c.Check(rig.Give(cast, 1) && rig.Give(cast, 1) && rig.Give(cast, 1), $"give 3 {cast}"))
                {
                    var total = rig.Inv.CountItems(null, -1, matchWorldLevel: false);
                    p = Press(castSwitch, alt: true);
                    c.Check(p.Removed == 1 && Slot(foundry, 0) == cast && rig.Inv.CountItems(null, -1, matchWorldLevel: false) == total - 1,
                        $"T10 Shift+E on the cast slot places exactly 1 cast, like vanilla (slot '{Slot(foundry, 0)}', removed {p.Removed})");
                }
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

    // ---------- batchfeed.shield-turret (T11) ----------

    private static IEnumerator RunShieldTurret()
    {
        var c = new Checks(ShieldTurretName);
        Rig rig = null;
        LogTap tap = null;
        try
        {
            rig = new Rig();
            tap = new LogTap();
            HudReady(c);

            var go = rig.Spawn("piece_shieldgenerator", 0f, 7f);
            var shield = go != null ? go.GetComponentInChildren<ShieldGenerator>() : null;
            if (c.Check(shield != null && shield.m_addFuelSwitch != null && shield.m_fuelItems != null && shield.m_fuelItems.Count > 0,
                    "spawn 'piece_shieldgenerator' with a fuel switch"))
            {
                yield return ShieldReady(shield);
            }
            if (shield != null && shield.m_addFuelSwitch != null
                && c.Check(shield.m_nview != null && shield.m_nview.IsValid() && shield.m_addFuelSwitch.m_onUse != null,
                    "the shield generator is awake one frame after it is built (its fuel switch works)"))
            {
                var bones = Token("BoneFragments");
                var takesBones = false;
                var names = new List<string>();
                foreach (var item in shield.m_fuelItems)
                {
                    names.Add(item != null ? item.name : "(none)");
                    takesBones |= item != null && item.name == "BoneFragments";
                }
                var max = shield.m_maxFuel;
                var start = shield.GetFuel();
                c.Note($"piece_shieldgenerator: maxFuel {max}, start fuel {start}, fuel items {string.Join(", ", names.ToArray())}");
                c.Check(takesBones && bones != null, "T11 the shield generator takes BoneFragments");
                c.Check(rig.Give("BoneFragments", 20), "give 20 BoneFragments");
                var n = FuelRoom(start, max, 5);
                var p = Press(shield.m_addFuelSwitch.gameObject, alt: true);
                c.Check(n > 0 && p.Removed == n && Near(shield.GetFuel(), start + n) && rig.Count(bones) == 20 - n,
                    $"T11 shield generator: batch adds {n} bones (fuel {start} -> {shield.GetFuel()}, removed {p.Removed})");
                c.Check(p.Messages == 1 && p.Center == Loc($"$msg_added {n} {bones} ({Mathf.Ceil(start + n)}/{max})"), $"T11 one summary 'Added {n} Bone Fragments (x/max)' ({Show(p)})");
                if (FuelRoom(shield.GetFuel(), max, 1) == 1)
                {
                    var fuel = shield.GetFuel();
                    p = Press(shield.m_addFuelSwitch.gameObject, alt: false);
                    c.Check(p.Removed == 1 && Near(shield.GetFuel(), fuel + 1f), $"shield generator: plain E adds exactly 1 ({Show(p)})");
                }
                rig.Destroy(go);
                yield return null;
            }

            // Ballista. Every step of one case in the same frame: a loaded ballista shoots at what it sees.
            var wood = Token("TurretBoltWood");
            var black = Token("TurretBolt");
            go = rig.Spawn("piece_turret", 0f, 7f);
            var turret = go != null ? go.GetComponent<Turret>() : null;
            if (c.Check(turret != null && wood != null && black != null, "spawn 'piece_turret'; TurretBoltWood and TurretBolt exist"))
            {
                var max = turret.m_maxAmmo;
                c.Note($"piece_turret: maxAmmo {max}, hold repeat {turret.m_holdRepeatInterval} s, returns ammo when destroyed {turret.m_returnAmmoOnDestroy}, "
                       + $"targets players {turret.m_targetPlayers}");
                rig.Empty();
                c.Check(rig.Give("TurretBoltWood", 20), "give 20 TurretBoltWood");
                var n = Mathf.Min(5, max);
                var p = Press(go, alt: true);
                c.Check(p.Removed == n && turret.GetAmmo() == n && turret.GetAmmoType() == "TurretBoltWood" && rig.Count(wood) == 20 - n,
                    $"T11 ballista: batch loads {n} wooden missiles (ammo {turret.GetAmmo()} {turret.GetAmmoType()}, removed {p.Removed})");
                c.Check(p.Messages == 1 && p.Center == Loc($"$msg_added {n} {wood} ({n}/{max})"), $"T11 one summary 'Added 5 Wooden Missile (5/max)' ({Show(p)})");
                p = Press(go, alt: false);
                c.Check(p.Removed == 1 && turret.GetAmmo() == n + 1, $"ballista: plain E loads exactly 1 ({Show(p)})");
                // Only the other missile kind in the inventory.
                rig.Empty();
                rig.Give("TurretBolt", 20);
                var loaded = turret.GetAmmo();
                p = Press(go, alt: true);
                c.Check(p.Removed == 0 && turret.GetAmmo() == loaded && rig.Count(black) == 20 && p.Messages == 1
                        && p.Center == Loc("$msg_turretotherammo") + Loc(wood),
                    $"T11 other missile kind: one 'already loaded with Wooden Missile', nothing removed ({Show(p)})");
                rig.Destroy(go);
                yield return null;
            }

            go = rig.Spawn("piece_turret", 0f, 7f);
            turret = go != null ? go.GetComponent<Turret>() : null;
            if (c.Check(turret != null && wood != null && black != null, "spawn a second 'piece_turret'"))
            {
                // 2 wooden in the first inventory slot, 20 black metal in the second.
                rig.Empty();
                c.Check(rig.GiveAt("TurretBoltWood", 2, 0, 0) && rig.GiveAt("TurretBolt", 20, 1, 0), "give 2 TurretBoltWood (slot 1) and 20 TurretBolt (slot 2)");
                var mark = tap.Mark();
                var p = Press(go, alt: true);
                c.Check(p.Removed == 2 && turret.GetAmmo() == 2 && turret.GetAmmoType() == "TurretBoltWood" && rig.Count(black) == 20 && rig.Count(wood) == 0,
                    $"T11 mixed missiles: only the 2 wooden ones are loaded, 20 black metal kept (ammo {turret.GetAmmo()} {turret.GetAmmoType()}, black metal {rig.Count(black)})");
                c.Check(p.Messages == 1 && p.Center == Loc($"$msg_added 2 {wood} (2/{turret.m_maxAmmo})"), $"T11 one summary 'Added 2 Wooden Missile (2/max)' ({Show(p)})");
                ExpectLog(c, "T11 mixed missiles", tap.Batch(mark), "TurretAmmo", "owner=you", "added=2", "presses=2", "stop=otherammo");
                rig.Destroy(go);
                yield return null;
            }
            c.Report();
        }
        finally
        {
            tap?.Dispose();
            rig?.End();
        }
    }

    // ---------- batchfeed.skill (T25) ----------

    private static IEnumerator RunSkill()
    {
        var c = new Checks(SkillName);
        Rig rig = null;
        try
        {
            rig = new Rig();
            HudReady(c);
            var skill = rig.ParkSkill(Skills.SkillType.Cooking, 0f);
            var meat = Token("RawMeat");
            var go = rig.Spawn("piece_cookingstation_iron", 0f, 6f);
            var station = go != null ? go.GetComponent<CookingStation>() : null;
            if (!c.Check(station != null && meat != null && CookConversion(station, "RawMeat") != null && station.m_slots.Length == 5,
                    "spawn 'piece_cookingstation_iron' with 5 slots that cooks RawMeat"))
            {
                c.Report();
                yield break;
            }
            if (!c.Check(PutFireUnder(rig, station, out var how), $"the station counts as over a lit fire ({how})"))
            {
                c.Report();
                yield break;
            }
            yield return null;

            // What five adds of 0.4 do to a skill at 0, with the game's own numbers.
            var multiplier = 1f;
            rig.P.GetSEMan().ModifyRaiseSkill(Skills.SkillType.Cooking, ref multiplier);
            var perItem = skill.m_info.m_increseStep * 0.4f * multiplier * Game.m_skillGainRate;
            // Level 0 need a full bar of 1 (game formula). Game data: Cooking gain step is 0.25, so one item give 0.1
            // and five items alone never fill it (a new character need ten). Then me start the bar 2.5 items below
            // level 1: the 3rd item of the batch reach it, like the test list expect. Bar is in memory, put back after.
            var need = Mathf.Pow(1f, 1.5f) * 0.5f + 0.5f;
            var startBar = perItem * 5f >= need ? 0f : Mathf.Max(0f, need - perItem * 2.5f);
            skill.m_level = 0f;
            skill.m_accumulator = startBar;
            c.Check(perItem > 0f, $"cooking an item raises the Cooking skill ({perItem} per item)");
            var level = 0f;
            var accumulator = startBar;
            var ups = 0;
            for (var i = 0; i < 5; i++)
            {
                accumulator += perItem;
                if (accumulator >= Mathf.Pow(Mathf.Floor(level + 1f), 1.5f) * 0.5f + 0.5f)
                {
                    level += 1f;
                    accumulator = 0f;
                    ups++;
                }
            }
            c.Note($"Cooking: gain step {skill.m_info.m_increseStep}, skill gain rate {Game.m_skillGainRate}, status effect multiplier {multiplier}: "
                   + $"{perItem} per item, level 0 needs {need}, bar started at {startBar}, 5 items = level {level}");
            c.Check(ups >= 1, $"T25 the five items take Cooking from 0 to 1 or more (they give {perItem} each, bar started at {startBar} of {need})");

            var hud = MessageHud.instance;
            c.Check(rig.Give("RawMeat", 5), "give 5 RawMeat");
            var topLeft = hud.m_msgQeue.Count;
            var p = Press(go, alt: true);
            c.Check(p.Removed == 5 && UsedSlots(station) == 5, $"T25 batch adds 5 (used {UsedSlots(station)}, removed {p.Removed})");
            c.Check(p.Messages == 1 && p.Center == Loc($"$msg_added 5 {meat} (5/5)\n$msg_skillup $skill_cooking: 1"),
                $"T25 one center message with two lines: the summary and 'Skill improved Cooking: 1' ({Show(p)})");
            c.Check(Near(skill.m_level, level) && hud.m_msgQeue.Count - topLeft == ups - 1 && BatchFeeder.HeldSkillUp == null,
                $"T25 skill at {skill.m_level} (expected {level}), {hud.m_msgQeue.Count - topLeft} top-left message(s) (expected {ups - 1})");

            // Later level-ups: top-left, as in vanilla. Station emptied by its owner, skill one add from level 2.
            for (var i = 0; i < station.m_slots.Length; i++)
            {
                SetSlot(station, i, "", 0f, 0);
            }
            skill.m_level = 1f;
            skill.m_accumulator = Mathf.Pow(2f, 1.5f) * 0.5f + 0.5f - perItem * 0.5f;
            c.Check(rig.Give("RawMeat", 5), "give 5 RawMeat again");
            topLeft = hud.m_msgQeue.Count;
            p = Press(go, alt: true);
            var queue = hud.m_msgQeue.ToArray();
            var last = queue.Length > 0 ? queue[queue.Length - 1].m_text : "";
            c.Check(p.Removed == 5 && p.Messages == 1 && p.Center == Loc($"$msg_added 5 {meat} (5/5)"),
                $"T25 a later level-up does not join the summary ({Show(p)})");
            c.Check(skill.m_level >= 2f && hud.m_msgQeue.Count - topLeft >= 1 && last == Loc("$msg_skillup $skill_cooking: " + (int)skill.m_level),
                $"T25 the later level-up went to the top-left corner (level {skill.m_level}, {hud.m_msgQeue.Count - topLeft} top-left message(s), last '{last}')");
            c.Report();
        }
        finally
        {
            rig?.End();
        }
    }
}
#endif
