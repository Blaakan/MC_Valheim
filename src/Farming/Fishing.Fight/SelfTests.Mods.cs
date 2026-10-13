#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Farming.FishingFightMod;

// Debug build only. Checks with another MC mod loaded in the same game (same class as SelfTests.cs, rig of
// SelfTests.cs). Soft: the other mod is found by GUID in the shared registry, used through the game (a real harpoon
// throw, its console command, the character's saved data), never through its types. Not loaded and active = the test
// fails (nothing checked is not a pass).
//   fishing.harpoon-throw  X03: Harpoon Hooks Tames on: a REAL harpoon throw hooks a tame boar, then the rod is taken
//                          in hand and cast: the harpoon line lets go as in the normal game, a fight works after
//   fishing.filter         X05: Loot Pickup Filter on: fish caught with a full bag stays in the world; with room in
//                          the bag and Perch on the filter's Ignored list (its own console command) walking over the
//                          fish does not pick it up, back in Everything mode it does
// (X02 Trinkets on Demand is fishing.pad.trinket in SelfTests.Input.cs: it needs the gamepad.)
internal static partial class SelfTests
{
    private const string HarpoonThrowName = "fishing.harpoon-throw";
    private const string FilterName = "fishing.filter";

    // Vanilla names: Abyssal Harpoon (used by the passing tests of Harpoon Hooks Tames), Boar.
    private const string HarpoonItem = "SpearChitin";
    private const string BoarName = "Boar";

    // Loot Pickup Filter keeps mode and lists in the character's own data under these keys (its FilterState).
    private const string FilterModeKey = PickupFilterGuid + ".Mode";
    private const string FilterIgnoredKey = PickupFilterGuid + ".Ignored";
    private const string FilterSelectedKey = PickupFilterGuid + ".Selected";
    private const string FilterVersionKey = PickupFilterGuid + ".Version";
    private const int MaxThrows = 4;

    private static void RegisterMods()
    {
        SelfTest.Register(HarpoonThrowName, RunHarpoonThrow);
        SelfTest.Register(FilterName, RunFilter);
    }

    private static void UnregisterMods()
    {
        SelfTest.Unregister(HarpoonThrowName);
        SelfTest.Unregister(FilterName);
    }

    // ---------- fishing.harpoon-throw (X03) ----------

    private static IEnumerator RunHarpoonThrow() => RunLive(HarpoonThrowName, false, HarpoonThrowBody, HarpoonGuid);

    // Creature held on its spot (transform and body, else physics put the old place back).
    private static void HoldCreature(Character creature, Vector3 pos, Vector3 facing)
    {
        if (creature == null)
        {
            return;
        }
        var t = creature.transform;
        t.position = pos;
        facing = Flat(facing);
        if (facing.sqrMagnitude > 0.0001f)
        {
            t.rotation = Quaternion.LookRotation(facing.normalized);
        }
        var body = creature.m_body;
        if (body != null)
        {
            body.position = pos;
            body.rotation = t.rotation;
            body.linearVelocity = Vector3.zero;
        }
    }

    // Height above the player's feet where the throw lets the projectile go (Attack.GetProjectileSpawnPoint: the
    // attack's origin joint, or the feet, plus the attack height). Idle pose: a guess until a first throw showed it.
    private static float ThrowHeight(Player p, Attack attack)
    {
        var origin = p.transform;
        if (!string.IsNullOrEmpty(attack.m_attackOriginJoint) && p.GetVisual() != null)
        {
            var joint = Utils.FindChild(p.GetVisual().transform, attack.m_attackOriginJoint);
            if (joint != null)
            {
                origin = joint;
            }
        }
        return origin.position.y + attack.m_attackHeight - p.transform.position.y;
    }

    // Look so that a projectile leaving the launch point reaches the target (gravity drop added).
    private static void AimAt(Player p, Vector3 target, float height, float range, float velocity, float gravity)
    {
        var flat = Flat(target - p.transform.position);
        if (flat.sqrMagnitude < 0.01f)
        {
            return;
        }
        flat.Normalize();
        var launch = p.transform.position + Vector3.up * height + flat * range;
        var to = target - launch;
        var time = velocity > 0.1f ? to.magnitude / velocity : 0f;
        to.y += 0.5f * gravity * time * time;
        var dir = to.normalized;
        p.m_lookYaw = Quaternion.LookRotation(flat);
        // Positive pitch = down in the game.
        p.m_lookPitch = -Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
        p.transform.rotation = p.m_lookYaw;
        if (p.m_body != null)
        {
            p.m_body.rotation = p.m_lookYaw;
        }
        if (p.m_eye != null)
        {
            p.m_eye.rotation = p.m_lookYaw * Quaternion.Euler(p.m_lookPitch, 0f, 0f);
        }
    }

    private static IEnumerator HarpoonThrowBody(Checks c, FishRig rig, Stage st, LogTap tap)
    {
        var p = rig.P;
        // No pass without a check: a skipped test must not read as proof.
        if (!c.Check(ModActive(HarpoonGuid),
                $"Harpoon Hooks Tames is loaded and active in this run ({ModStateText(HarpoonGuid)}): without it a harpoon flies through a tame"))
        {
            yield break;
        }
        // Harpoons lying in the world before the test (a thrown one that fell is taken away at the end).
        var lying = new HashSet<int>();
        foreach (var drop in ItemDrop.s_instances)
        {
            if (drop != null && Utils.GetPrefabName(drop.gameObject) == HarpoonItem)
            {
                lying.Add(drop.GetInstanceID());
            }
        }
        try
        {
            var weapon = rig.Give(HarpoonItem);
            var shared = weapon != null ? weapon.m_shared : null;
            Attack attack = null;
            var secondary = false;
            if (shared != null && shared.m_attack != null && shared.m_attack.m_attackProjectile != null)
            {
                attack = shared.m_attack;
            }
            else if (shared != null && shared.m_secondaryAttack != null && shared.m_secondaryAttack.m_attackProjectile != null)
            {
                attack = shared.m_secondaryAttack;
                secondary = true;
            }
            var flight = attack != null ? attack.m_attackProjectile.GetComponent<Projectile>() : null;
            var effect = shared != null ? shared.m_attackStatusEffect as SE_Harpooned : null;
            var boarPrefab = ZNetScene.instance.GetPrefab(BoarName);
            if (!c.Check(weapon != null && attack != null && flight != null && effect != null && boarPrefab != null,
                    $"{HarpoonItem} (Abyssal Harpoon) with an attack that throws it and the game's harpoon effect, and the {BoarName} prefab exist"))
            {
                yield break;
            }
            var hash = effect.NameHash();

            // A tame boar 5 m to the side (away from the fishing spot), held there: a wandering tame is not a miss.
            var side = Vector3.Cross(Vector3.up, st.WaterDir);
            var spot = st.Stand + side * 5f;
            spot.y = ZoneSystem.instance.GetGroundHeight(spot) + 0.05f;
            var boarGo = UnityEngine.Object.Instantiate(boarPrefab, spot, Quaternion.LookRotation(-side));
            rig.Track(boarGo);
            var boar = boarGo.GetComponent<Character>();
            for (var i = 0; i < 10; i++)
            {
                yield return new WaitForFixedUpdate();
            }
            if (!c.Check(boar != null && boar.m_nview != null && boar.m_nview.IsValid(), "a boar stands 5 m beside the player"))
            {
                yield break;
            }
            boar.SetTamed(true);

            // The harpoon in hand in place of the rod.
            p.UnequipItem(st.Rod, false);
            yield return null;
            var inHand = p.EquipItem(weapon, false);
            for (var i = 0; i < 5; i++)
            {
                HoldCreature(boar, spot, -side);
                yield return new WaitForFixedUpdate();
            }
            if (!c.Check(inHand && ReferenceEquals(p.GetCurrentWeapon(), weapon) && boar != null && boar.IsTamed() && !p.IsPVPEnabled(),
                    "the harpoon is in hand, the boar is tame, PvP is off (the normal game would let the harpoon fly through)"))
            {
                yield break;
            }
            rig.LookLevel();
            // "Unhurt" below = not less health than now.
            var healthBefore = boar.GetHealth();

            // The real thing: the attack the attack key starts (Humanoid.StartAttack), the throw animation, the game's
            // own projectile and its flight. Up to 4 throws: the throw has a random spread, and the first one tells
            // the real launch height for the aim of the next.
            var height = ThrowHeight(p, attack);
            var hooked = false;
            var throws = 0;
            var notes = "";
            for (var attempt = 1; attempt <= MaxThrows && !hooked && boar != null; attempt++)
            {
                throws = attempt;
                var t0 = Time.time;
                while (p.InAttack() && Time.time - t0 < 3f)
                {
                    HoldCreature(boar, spot, -side);
                    rig.Fill(p.GetMaxStamina());
                    yield return null;
                }
                t0 = Time.time;
                while (boar != null && Time.time - t0 < 0.3f)
                {
                    HoldCreature(boar, spot, -side);
                    AimAt(p, boar.GetCenterPoint(), height, attack.m_attackRange, attack.m_projectileVel, flight.m_gravity);
                    rig.Fill(p.GetMaxStamina());
                    yield return null;
                }
                if (boar == null)
                {
                    break;
                }
                weapon.m_lastProjectile = null;
                if (!p.StartAttack(null, secondary))
                {
                    notes += $" throw {attempt}: the attack did not start;";
                    yield return new WaitForSeconds(0.7f);
                    continue;
                }
                t0 = Time.time;
                while (boar != null && weapon.m_lastProjectile == null && Time.time - t0 < 4f)
                {
                    HoldCreature(boar, spot, -side);
                    AimAt(p, boar.GetCenterPoint(), height, attack.m_attackRange, attack.m_projectileVel, flight.m_gravity);
                    rig.Fill(p.GetMaxStamina());
                    yield return null;
                }
                var go = weapon.m_lastProjectile;
                if (go == null)
                {
                    notes += $" throw {attempt}: no projectile within 4 s of the attack start;";
                    continue;
                }
                rig.Track(go);
                var thrown = go.GetComponent<Projectile>();
                var aimed = height;
                if (thrown != null)
                {
                    // Where the game really let it go.
                    height = thrown.m_startPoint.y - p.transform.position.y;
                }
                t0 = Time.time;
                while (Time.time - t0 < 4f)
                {
                    HoldCreature(boar, spot, -side);
                    rig.Fill(p.GetMaxStamina());
                    yield return new WaitForFixedUpdate();
                    if (go == null || boar == null || (thrown != null && thrown.m_didHit) || boar.GetSEMan().HaveStatusEffect(hash))
                    {
                        break;
                    }
                }
                hooked = boar != null && boar.GetSEMan().HaveStatusEffect(hash);
                notes += $" throw {attempt}: aimed for a launch {F(aimed)} m up, launched {F(height)} m up, {(hooked ? "hooked" : "missed")} after {F(Time.time - t0)} s;";
                if (!hooked)
                {
                    Kill(go);
                }
            }
            var line = boar != null ? boar.GetSEMan().GetStatusEffect(hash) as SE_Harpooned : null;
            if (!c.Check(hooked && line != null && line.m_attacker == p && healthBefore > 0f && boar.GetHealth() >= healthBefore - 0.01f,
                    $"a real harpoon throw hooks the tame boar within {MaxThrows} throws, the line held by this player, the boar unhurt ({throws} throw(s):{notes}; health {(boar != null ? F(boar.GetHealth()) : "?")}, {F(healthBefore)} before)"))
            {
                yield break;
            }

            // Switch to the rod. The game lets a harpoon line go on an attack or a block only after 2 s.
            var hookedAt = Time.time;
            p.EquipItem(st.Rod, false);
            var top = new Box();
            yield return RodInHand(rig, st, top);
            while (Time.time - hookedAt < 2.4f)
            {
                HoldCreature(boar, spot, -side);
                rig.Fill(p.GetMaxStamina());
                yield return new WaitForFixedUpdate();
            }
            if (!c.Check(top.Ok && ReferenceEquals(p.GetCurrentWeapon(), st.Rod), "switched to the rod: it is in hand (its top shows)"))
            {
                yield break;
            }
            // Line must still hold here: else "gone after the cast" below say nothing about the cast (the normal game
            // keeps a harpoon line through a weapon switch: SE_Harpooned.IsDone asks only for an attack or a block).
            var stillOn = boar != null && boar.GetSEMan().HaveStatusEffect(hash);
            if (!c.Check(stillOn,
                    $"rod in hand {F(Time.time - hookedAt)} s after the hook: the boar is still on the harpoon line (the weapon switch does not let it go, as in the normal game)"))
            {
                yield break;
            }

            // Cast toward the fishing spot.
            var baitGiven = rig.Give("FishingBait", 3);
            c.Check(baitGiven != null, "FishingBait given");
            p.m_lookPitch = 5f;
            rig.Face(st.WaterDir);
            rig.Fill(p.GetMaxStamina());
            var cast = new CastResult();
            yield return Cast(rig, st, 0.8f, null, cast);
            var let = Time.time;
            while (boar != null && boar.GetSEMan().HaveStatusEffect(hash) && Time.time - let < 2f)
            {
                rig.Fill(p.GetMaxStamina());
                yield return new WaitForFixedUpdate();
            }
            c.Check(cast.Float != null, "cast with the rod: a float lands (" + cast.Detail + ")");
            c.Check(boar != null && !boar.GetSEMan().HaveStatusEffect(hash) && boar.GetHealth() >= healthBefore - 0.01f,
                "the cast lets the harpoon line go, as in the normal game, and the boar is unhurt");
            Kill(cast.Float != null ? cast.Float.gameObject : null);
            var swing = Time.time;
            while (p.InAttack() && Time.time - swing < 4f)
            {
                yield return new WaitForFixedUpdate();
            }
            yield return null;

            // The fishing fight works normally afterwards: catch bar, a fight, calm again.
            QuietCalm();
            var hook = new Hooked();
            yield return HookNew(rig, st, 1, hook);
            var fight = hook.Fight;
            if (!c.Check(fight != null, "after the harpoon: a hooked Perch starts the fight"))
            {
                yield break;
            }
            yield return null;
            yield return null;
            var calmBar = FightHud.BarShown;
            Fight.TestSide = FightLogic.Right;
            Fight.TestPhase = FightPhase.Struggle;
            yield return new WaitForSeconds(0.3f);
            yield return null;
            var fought = ReferenceEquals(Fight.Current, fight) && fight.Phase == FightPhase.Struggle && !FightHud.BarShown;
            Fight.TestPhase = FightPhase.Calm;
            yield return new WaitForSeconds(0.2f);
            yield return null;
            c.Check(calmBar && fought && ReferenceEquals(Fight.Current, fight) && fight.Phase == FightPhase.Calm && FightHud.BarShown,
                "after the harpoon: catch bar, fight and calm again work as usual");
        }
        finally
        {
            // A harpoon that fell to the ground during the test goes with its ZDO (no yield here).
            try
            {
                var fallen = new List<GameObject>();
                foreach (var drop in ItemDrop.s_instances)
                {
                    if (drop != null && Utils.GetPrefabName(drop.gameObject) == HarpoonItem && !lying.Contains(drop.GetInstanceID()))
                    {
                        fallen.Add(drop.gameObject);
                    }
                }
                foreach (var go in fallen)
                {
                    Kill(go);
                }
            }
            catch (Exception e)
            {
                SelfTest.Note(HarpoonThrowName, "clean-up of thrown harpoons failed: " + e.Message);
            }
        }
    }

    // ---------- fishing.filter (X05) ----------

    private static IEnumerator RunFilter() => RunLive(FilterName, false, FilterBody, PickupFilterGuid);

    private static string Data(Player p, string key) =>
        p.m_customData != null && p.m_customData.TryGetValue(key, out var value) ? value : null;

    // Loot Pickup Filter's own chat / console command, as a player types it (F5 console).
    private static bool FilterCommand(string text)
    {
        var console = Console.instance;
        if (console == null)
        {
            return false;
        }
        // Third argument: no "is this command allowed in this window" question (that gate is about the chat window).
        console.TryRunCommand(text, false, true);
        return true;
    }

    private static bool Listed(string list, string name)
    {
        if (string.IsNullOrEmpty(list))
        {
            return false;
        }
        foreach (var part in list.Split(','))
        {
            if (part.Trim() == name)
            {
                return true;
            }
        }
        return false;
    }

    // Walk to the fish like a player (look at it, forward held) until it is at arm's length, then stand there for
    // 'stand' seconds with auto pickup on. Ends early when the fish is gone (picked up).
    private static IEnumerator WalkOver(FishRig rig, Fish fish, float stand, Box result)
    {
        var p = rig.P;
        var start = Time.time;
        var from = p.transform.position;
        while (fish != null && Time.time - start < 6f)
        {
            var to = Flat(fish.transform.position - p.transform.position);
            if (to.magnitude < 0.8f)
            {
                break;
            }
            rig.Face(to);
            p.SetControls(new Vector3(0f, 0f, 1f), false, false, false, false, false, false, false, false, false, false);
            yield return new WaitForFixedUpdate();
        }
        rig.Reel(false);
        var walked = Vector3.Distance(Flat(from), Flat(p.transform.position));
        var near = float.MaxValue;
        var inReach = 0f;
        var stood = Time.time;
        while (fish != null && Time.time - stood < stand)
        {
            // 1.5 m on the ground = inside the game's pickup ball (2 m around a point 1 m above the feet).
            var gap = Vector3.Distance(Flat(fish.transform.position), Flat(p.transform.position));
            near = Mathf.Min(near, gap);
            if (gap < 1.5f)
            {
                inReach += Time.fixedDeltaTime;
            }
            yield return new WaitForFixedUpdate();
        }
        result.Ok = inReach >= 0.5f || fish == null;
        result.Detail = fish != null
            ? $"walked {F(walked)} m, stood {F(Time.time - stood)} s, the fish within reach of auto pickup for {F(inReach)} s (as near as {F(near)} m; auto pickup reaches {F(p.m_autoPickupRange)} m)"
            : $"walked {F(walked)} m, the fish was gone after {F(Time.time - stood)} s there";
    }

    private static IEnumerator FilterBody(Checks c, FishRig rig, Stage st, LogTap tap)
    {
        var p = rig.P;
        // No pass without a check: a skipped test must not read as proof.
        if (!c.Check(ModActive(PickupFilterGuid),
                $"Loot Pickup Filter is loaded and active in this run ({ModStateText(PickupFilterGuid)}): without it nothing of this item is checked"))
        {
            yield break;
        }
        // The filter of this character before the test (put back at the end).
        var hadKeys = Data(p, FilterModeKey) != null;
        var modeBefore = Data(p, FilterModeKey) ?? "Everything";
        var ignoredBefore = Data(p, FilterIgnoredKey);
        var selectedBefore = Data(p, FilterSelectedKey);
        var versionBefore = Data(p, FilterVersionKey);
        var fishKey = st.FishPrefab.name;
        // On the filter's Ignored list before the test (its own memory: a character that never used the filter has
        // the config's default list there and nothing saved).
        var wasIgnored = false;
        var touched = false;
        try
        {
            FightHud.TestDisplay = DefaultDisplay;
            // --- Full bag: the catch happens, the fish stays where it was pulled in.
            var fillers = new List<ItemDrop.ItemData>();
            for (var i = 0; i < 64 && rig.Inv.HaveEmptySlot(); i++)
            {
                var filler = rig.Give("FishingRod");
                if (filler == null)
                {
                    break;
                }
                fillers.Add(filler);
            }
            ServerRules.TestRules = new FightRules { ReelSpeed = FightRules.ReelSpeedMax };
            Fight.TestPhase = FightPhase.Calm;
            Fight.TestPin = FishPin.InZone;
            var mark = tap.Mark();
            var hook = new Hooked();
            yield return HookNew(rig, st, 1, hook);
            if (!c.Check(hook.Fight != null, "full bag: fight starts (float and Perch on dry land in front of the player)"))
            {
                yield break;
            }
            var ff = hook.Float;
            var fish = hook.Fish;
            var full = !rig.Inv.HaveEmptySlot() && fish.m_itemDrop != null && !rig.Inv.CanAddItem(fish.m_itemDrop.m_itemData);
            var count0 = rig.Inv.CountItems(st.FishName);
            var start = Time.time;
            while (ff != null && Time.time - start < 30f)
            {
                rig.Fill(p.GetMaxStamina());
                yield return new WaitForFixedUpdate();
            }
            yield return null;
            c.Check(full, $"the inventory is full before the catch ({fillers.Count} slots filled)");
            if (!c.Check(ff == null && Fight.Current == null && tap.Has("Fight ended: caught.", mark) && fish != null && IsFree(fish)
                         && rig.Inv.CountItems(st.FishName) == count0,
                    $"full bag: the reel ends like a catch, nothing goes into the inventory and the fish stays in the world, off the hook ({F(Time.time - start)} s)"))
            {
                yield break;
            }
            var away = Vector3.Distance(Flat(fish.transform.position), Flat(p.transform.position));
            c.Check(away < 6f, $"full bag: the fish lies where it was pulled in ({F(away)} m from the fisher)");

            // --- Room in the bag again, the filter told to skip Perch (its own console command).
            foreach (var filler in fillers)
            {
                if (rig.Inv.ContainsItem(filler))
                {
                    rig.Inv.RemoveItem(filler);
                }
            }
            touched = true;
            var ran = FilterCommand("lootfilter mode skip");
            ran &= FilterCommand("lootfilter_ignore " + fishKey);
            if (!Listed(Data(p, FilterIgnoredKey), fishKey))
            {
                // It was on the list already and the command took it off: once more.
                wasIgnored = true;
                ran &= FilterCommand("lootfilter_ignore " + fishKey);
            }
            if (!c.Check(ran && Data(p, FilterModeKey) == "SkipIgnored" && Listed(Data(p, FilterIgnoredKey), fishKey),
                    $"the filter is set with its console command: mode Skip ignored, {fishKey} ignored (mode '{Data(p, FilterModeKey)}', ignored '{Data(p, FilterIgnoredKey)}')"))
            {
                yield break;
            }
            var room = fish.m_itemDrop != null && rig.Inv.CanAddItem(fish.m_itemDrop.m_itemData);
            var auto = fish.m_itemDrop != null && fish.m_itemDrop.m_autoPickup;
            Player.m_enableAutoPickup = true;
            var walk = new Box();
            yield return WalkOver(rig, fish, 2.5f, walk);
            c.Check(room && auto && walk.Ok,
                $"the player walks over the fish with auto pickup on and room in the bag (room {room}, a Perch is an item the game picks up by itself {auto}; {walk.Detail})");
            c.Check(fish != null && IsFree(fish) && rig.Inv.CountItems(st.FishName) == count0,
                $"Perch ignored by the filter: walking over the fish does not pick it up ({rig.Inv.CountItems(st.FishName)} in the bag, {count0} before)");
            if (fish == null)
            {
                yield break;
            }

            // --- Filter back to Everything: the same walk picks it up (so the filter alone held it back).
            ran = FilterCommand("lootfilter mode everything");
            c.Check(ran && Data(p, FilterModeKey) == "Everything", $"the filter is set back to Everything (mode '{Data(p, FilterModeKey)}')");
            walk = new Box();
            yield return WalkOver(rig, fish, 4f, walk);
            yield return null;
            c.Check(fish == null && rig.Inv.CountItems(st.FishName) == count0 + 1,
                $"filter mode Everything: walking over the fish picks it up as in the normal game ({rig.Inv.CountItems(st.FishName)} in the bag, {count0} before; {walk.Detail})");
        }
        finally
        {
            // The filter as it was (no yield here). Its own commands change what it holds in memory; a character
            // that never used it gets its saved keys taken away again.
            try
            {
                if (touched && p != null && p.m_customData != null)
                {
                    if (!wasIgnored && Listed(Data(p, FilterIgnoredKey), fishKey))
                    {
                        // The command switches an item on and off: off again what the test put on.
                        FilterCommand("lootfilter_ignore " + fishKey);
                    }
                    FilterCommand("lootfilter mode " + (modeBefore == "SkipIgnored" ? "skip" : modeBefore == "OnlySelected" ? "only" : "everything"));
                    if (!hadKeys)
                    {
                        p.m_customData.Remove(FilterModeKey);
                        p.m_customData.Remove(FilterIgnoredKey);
                        p.m_customData.Remove(FilterSelectedKey);
                        p.m_customData.Remove(FilterVersionKey);
                    }
                    else if (Data(p, FilterModeKey) != modeBefore || Data(p, FilterIgnoredKey) != ignoredBefore
                             || Data(p, FilterSelectedKey) != selectedBefore || Data(p, FilterVersionKey) != versionBefore)
                    {
                        SelfTest.Note(FilterName, $"the filter of this character was not put back exactly: mode '{Data(p, FilterModeKey)}' (was '{modeBefore}'), "
                                                  + $"ignored '{Data(p, FilterIgnoredKey)}' (was '{ignoredBefore}')");
                    }
                }
            }
            catch (Exception e)
            {
                SelfTest.Note(FilterName, "putting the filter back failed: " + e.Message);
            }
        }
    }
}
#endif
