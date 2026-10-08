#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.UX.AutoPickupFilterMod;

// Debug build only. Harvest grace: what the player harvests or collects by hand is picked up whatever the filter
// says; kill, mining and production drops stay filtered. Helpers and Rig live in SelfTests.cs.
internal static partial class SelfTests
{
    private const string HarvestName = "lootfilter.harvest";
    private const string StationsName = "lootfilter.stations";
    private const string FilteredName = "lootfilter.filtered";
    private const string DoorName = "lootfilter.door";
    private const string KilnName = "lootfilter.kiln";
    private const string ScytheName = "lootfilter.scythe";
    private const string ScytheSwingName = "lootfilter.scythe-swing";
    private const string BugPickColumnName = "lootfilter.bug.pick-column";
    private const string CompatBatchName = "lootfilter.compat-batch";

    // Mode Only selected with only Coins selected: every other item is skipped by the lists.
    private static void OnlyCoins()
    {
        FilterState.SetMode(FilterMode.OnlySelected);
        if (!FilterState.Selected.Contains("Coins"))
        {
            FilterState.Toggle(FilterState.Selected, "Coins");
        }
        Player.m_enableAutoPickup = true;
    }

    // After a harvest action: wait its drops, check each one got the harvest grace (and that the lists alone would
    // skip it), shows no skip line, then that auto pickup collects them all (player walks over those out of reach).
    private static IEnumerator ExpectCollected(Rig rig, Checks c, string what, HashSet<ItemDrop> since, float appear)
    {
        var drops = new List<ItemDrop>();
        yield return WaitNewDrops(rig, since, appear, drops);
        if (!c.Check(drops.Count > 0, $"{what}: items dropped"))
        {
            yield break;
        }
        var inv = rig.Inv;
        var totals = drops.GroupBy(d => d.m_itemData.m_shared.m_name).ToDictionary(g => g.Key, g => g.Sum(d => d.m_itemData.m_stack));
        var before = totals.Keys.ToDictionary(n => n, n => inv.CountItems(n, -1, false));
        var tagged = drops.Count(HarvestGrace.IsTagged);
        c.Check(drops.All(FilterState.ListBlocks), $"{what}: the lists alone would skip these items ({string.Join(", ", totals.Keys.Select(L).ToArray())})");
        c.Check(tagged == drops.Count, $"{what}: every dropped item counts as harvested by hand ({tagged} of {drops.Count})");
        c.Check(!drops.Any(d => HoverOf(d).Contains(SkipLineAny)), $"{what}: none shows 'Auto pickup skips this'");
        yield return WaitGone(drops, 3f);
        if (AliveCount(drops) > 0)
        {
            yield return WalkOver(rig, drops, 6f);
        }
        c.Check(AliveCount(drops) == 0, $"{what}: all {drops.Count} item(s) picked up ({AliveCount(drops)} left)");
        c.Check(totals.All(p => inv.CountItems(p.Key, -1, false) == before[p.Key] + p.Value), $"{what}: they are in the inventory");
    }

    // After something that is no harvest: its drops get no grace, stay on the ground under the player, show the line.
    private static IEnumerator ExpectFiltered(Rig rig, Checks c, string what, List<ItemDrop> drops, string line, bool pickOneWithUse)
    {
        if (!c.Check(drops.Count > 0, $"{what}: items dropped"))
        {
            yield break;
        }
        var inv = rig.Inv;
        var names = drops.Select(d => d.m_itemData.m_shared.m_name).Distinct().ToList();
        var before = names.ToDictionary(n => n, n => inv.CountItems(n, -1, false));
        c.Check(!drops.Any(HarvestGrace.IsTagged), $"{what}: no harvest grace for these drops ({string.Join(", ", names.Select(L).ToArray())})");
        // Past the 0.5 s pickup delay, then stand right on them.
        yield return new WaitForSeconds(0.8f);
        yield return StandOn(rig, drops[0], 2f);
        c.Check(AliveCount(drops) == drops.Count && names.All(n => inv.CountItems(n, -1, false) == before[n]), $"{what}: none is auto-picked while the player stands on them");
        c.Check(drops.All(d => HoverOf(d).Contains(line)), $"{what}: each shows '{line}'");
        if (pickOneWithUse)
        {
            var first = drops[0];
            var name = first.m_itemData.m_shared.m_name;
            var stack = first.m_itemData.m_stack;
            yield return UsePickup(rig, first);
            c.Check(!Alive(first) && inv.CountItems(name, -1, false) == before[name] + stack, $"{what}: E picks one up");
        }
        rig.MoveTo(rig.Origin);
        yield return null;
    }

    private static void Kill(Character creature)
    {
        var hit = new HitData();
        hit.m_damage.m_damage = 100000f;
        hit.m_point = creature.GetCenterPoint();
        hit.m_dir = Vector3.down;
        creature.Damage(hit);
    }

    // Loot of a kill come when the corpse go (a few seconds). Still none after 12 s: corpse made to go now.
    private static IEnumerator WaitLoot(Rig rig, Checks c, HashSet<ItemDrop> since, Vector3 where, List<ItemDrop> into)
    {
        var end = Time.time + 12f;
        while (Time.time < end && rig.NewDrops(since).Count == 0)
        {
            yield return null;
        }
        if (rig.NewDrops(since).Count == 0)
        {
            var forced = 0;
            foreach (var ragdoll in Object.FindObjectsByType<Ragdoll>(FindObjectsSortMode.None))
            {
                if (ragdoll != null && (ragdoll.transform.position - where).sqrMagnitude < 15f * 15f)
                {
                    ragdoll.DestroyNow();
                    forced++;
                }
            }
            c.Note($"no loot 12 s after the kill: {forced} corpse(s) removed now");
            yield return Frames(3);
        }
        yield return Frames(2);
        into.Clear();
        into.AddRange(rig.NewDrops(since));
    }

    // ---------------------------------------------------------------- lootfilter.harvest (T24)

    private static IEnumerator RunHarvest()
    {
        if (!Ready(HarvestName, out var player, out _))
        {
            yield break;
        }
        var c = new Checks(HarvestName);
        var rig = new Rig(player, HarvestName);
        try
        {
            NoteRoom(c, rig);
            OnlyCoins();

            // Use key (Player.Interact, like E) on pickables.
            foreach (var name in new[] { "RaspberryBush", "Pickable_Flint", "Pickable_Mushroom" })
            {
                var go = rig.Spawn(name, rig.Spot(1.3f), Quaternion.identity);
                var pickable = go != null ? go.GetComponent<Pickable>() : null;
                if (!c.Check(pickable != null, $"spawned {name}"))
                {
                    continue;
                }
                yield return Settle();
                c.Check(pickable.CanBePicked(), $"a spawned {name} can be picked at once");
                var since = rig.DropsNow();
                player.Interact(go, false, false);
                yield return ExpectCollected(rig, c, "E on " + name, since, 2f);
                rig.Destroy(go);
                yield return null;
            }

            // Cooked food taken off a cooking station.
            var station = rig.Spawn("piece_cookingstation", rig.Spot(1.4f), Quaternion.LookRotation(-rig.Forward));
            var cooking = station != null ? station.GetComponent<CookingStation>() : null;
            if (c.Check(cooking != null && cooking.m_conversion.Count > 0, "spawned a cooking station (piece_cookingstation)"))
            {
                yield return Settle();
                var conversion = cooking.m_conversion.FirstOrDefault(x => x.m_from != null && x.m_from.name == "RawMeat") ?? cooking.m_conversion[0];
                cooking.SetSlot(0, conversion.m_to.name, 0f, CookingStation.Status.Done, false);
                c.Check(cooking.HaveDoneItem(), $"cooked {conversion.m_to.name} on the station ({conversion.m_from.name} cooks in {F(conversion.m_cookTime)} s)");
                var since = rig.DropsNow();
                player.Interact(station, false, false);
                yield return ExpectCollected(rig, c, "E on a cooking station with cooked food", since, 2f);
                rig.Destroy(station);
                yield return null;
            }

            // Trophy hung on an item stand and taken back (the game takes it back on a held Use).
            var standGo = rig.Spawn("itemstand", rig.Spot(1.3f), Quaternion.LookRotation(-rig.Forward));
            var stand = standGo != null ? standGo.GetComponentInChildren<ItemStand>() : null;
            var trophy = rig.Give("TrophyBoar");
            if (c.Check(stand != null && trophy != null, "spawned an item stand (itemstand), TrophyBoar given"))
            {
                yield return Settle();
                c.Check(stand.CanAttach(trophy), "the item stand takes a TrophyBoar");
                stand.UseItem(player, trophy);
                var until = Time.time + 2f;
                while (Time.time < until && !stand.HaveAttachment())
                {
                    yield return null;
                }
                if (c.Check(stand.HaveAttachment(), "trophy hangs on the stand"))
                {
                    var since = rig.DropsNow();
                    player.m_lastHoverInteractTime = Time.time - 1f;
                    player.Interact(stand.gameObject, true, false);
                    yield return ExpectCollected(rig, c, "E on an item stand with a trophy", since, 2f);
                }
            }
            c.Check(rig.NewGuardReports == 0 && rig.Logs.Count(LogLevel.Error | LogLevel.Fatal) == 0, $"no error from the mod ({rig.Logs.First(LogLevel.Error | LogLevel.Fatal)})");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.stations (T24 optional part, T37)

    private static IEnumerator RunStations()
    {
        if (!Ready(StationsName, out var player, out _))
        {
            yield break;
        }
        var c = new Checks(StationsName);
        var rig = new Rig(player, StationsName);
        try
        {
            NoteRoom(c, rig);
            OnlyCoins();
            var facing = Quaternion.LookRotation(-rig.Forward);

            // Fermenter: tap = items come out 1.5 s after the press (inside the 3 s window).
            var go = rig.Spawn("fermenter", rig.Spot(1.8f), facing);
            var fermenter = go != null ? go.GetComponent<Fermenter>() : null;
            if (c.Check(fermenter != null && fermenter.m_conversion.Count > 0, "spawned a fermenter"))
            {
                yield return Settle();
                var conversion = fermenter.m_conversion[0];
                var zdo = go.GetComponent<ZNetView>().GetZDO();
                fermenter.m_fermentationDuration = 1f;
                zdo.Set(ZDOVars.s_content, conversion.m_from.gameObject.name.GetStableHashCode());
                zdo.Set(ZDOVars.s_startTime, ZNet.instance.GetTime().Ticks - TimeSpan.FromSeconds(20.0).Ticks);
                yield return null;
                if (c.Check(fermenter.GetStatus() == Fermenter.Status.Ready, $"fermenter is ready ({conversion.m_from.name} to {conversion.m_producedItems} {conversion.m_to.name}, tap delay {F(fermenter.m_tapDelay)} s)"))
                {
                    var since = rig.DropsNow();
                    player.Interact(go, false, false);
                    yield return ExpectCollected(rig, c, "E on a finished fermenter", since, fermenter.m_tapDelay + 3f);
                }
                rig.Destroy(go);
                yield return null;
            }

            // Beehive with honey.
            go = rig.Spawn("piece_beehive", rig.Spot(1.8f), facing);
            var hive = go != null ? go.GetComponent<Beehive>() : null;
            if (c.Check(hive != null, "spawned a beehive (piece_beehive)"))
            {
                yield return Settle();
                go.GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_level, 2);
                yield return null;
                if (c.Check(hive.GetHoneyLevel() == 2, "beehive holds 2 honey"))
                {
                    var since = rig.DropsNow();
                    player.Interact(go, false, false);
                    yield return ExpectCollected(rig, c, "E on a beehive with honey", since, 2f);
                }
                rig.Destroy(go);
                yield return null;
            }

            // Sap collector with sap.
            go = rig.Spawn("piece_sapcollector", rig.Spot(1.8f), facing);
            var sap = go != null ? go.GetComponent<SapCollector>() : null;
            if (c.Check(sap != null, "spawned a sap collector (piece_sapcollector)"))
            {
                yield return Settle();
                go.GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_level, 2);
                yield return null;
                if (c.Check(sap.GetLevel() == 2, "sap collector holds 2 sap"))
                {
                    var since = rig.DropsNow();
                    player.Interact(go, false, false);
                    yield return ExpectCollected(rig, c, "E on a sap collector with sap", since, 2f);
                }
                rig.Destroy(go);
                yield return null;
            }

            // T37: stone oven, bread taken out with E on its food switch.
            go = rig.Spawn("piece_oven", rig.Spot(2.6f), facing);
            var oven = go != null ? go.GetComponentInChildren<CookingStation>() : null;
            if (c.Check(oven != null && oven.m_addFoodSwitch != null, "spawned a stone oven (piece_oven) with a food switch"))
            {
                yield return Settle();
                var conversion = oven.m_conversion.FirstOrDefault(x => x.m_from != null && x.m_from.name == "BreadDough");
                if (c.Check(conversion != null && conversion.m_to != null, "the stone oven bakes BreadDough"))
                {
                    c.Note($"piece_oven: BreadDough becomes {conversion.m_to.name} in {F(conversion.m_cookTime)} s; fuel {(oven.m_fuelItem != null ? oven.m_fuelItem.name : "none")}");
                    oven.SetSlot(0, conversion.m_to.name, 0f, CookingStation.Status.Done, false);
                    c.Check(oven.HaveDoneItem(), "baked bread in the oven");
                    var since = rig.DropsNow();
                    player.Interact(oven.m_addFoodSwitch.gameObject, false, false);
                    yield return ExpectCollected(rig, c, "E on the stone oven's food switch with baked bread", since, 2f);
                }
                rig.Destroy(go);
                yield return null;
            }
            c.Check(rig.NewGuardReports == 0 && rig.Logs.Count(LogLevel.Error | LogLevel.Fatal) == 0, $"no error from the mod ({rig.Logs.First(LogLevel.Error | LogLevel.Fatal)})");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.filtered (T25)

    // Small rock that breaks into Stone items when mined (no ore vein, no tree).
    private static GameObject FindRockPrefab()
    {
        var scene = ZNetScene.instance;
        foreach (var name in new[] { "Rock_3", "Rock_4", "Rock_7", "Rock_4_plains" })
        {
            var prefab = scene.GetPrefab(name);
            if (IsStoneRock(prefab))
            {
                return prefab;
            }
        }
        return scene.m_prefabs.FirstOrDefault(IsStoneRock);
    }

    private static bool IsStoneRock(GameObject prefab)
    {
        if (prefab == null || prefab.GetComponent<ZNetView>() == null || prefab.GetComponent<MineRock>() != null || prefab.GetComponent<MineRock5>() != null
            || prefab.GetComponent<TreeBase>() != null || prefab.GetComponent<Character>() != null)
        {
            return false;
        }
        var destructible = prefab.GetComponent<Destructible>();
        var drop = prefab.GetComponent<DropOnDestroyed>();
        return destructible != null && drop != null && destructible.m_spawnWhenDestroyed == null
               && drop.m_dropWhenDestroyed.m_drops.Any(d => d.m_item != null && d.m_item.name == "Stone");
    }

    private static IEnumerator RunFiltered()
    {
        if (!Ready(FilteredName, out var player, out _))
        {
            yield break;
        }
        var c = new Checks(FilteredName);
        var rig = new Rig(player, FilteredName);
        try
        {
            NoteRoom(c, rig);
            OnlyCoins();
            var loot = new List<ItemDrop>();

            // Kill: its drops are filtered.
            for (var attempt = 0; attempt < 2 && loot.Count == 0; attempt++)
            {
                var boar = rig.Spawn("Boar", rig.Spot(2.5f), Quaternion.identity);
                var creature = boar != null ? boar.GetComponent<Character>() : null;
                if (!c.Check(creature != null, "spawned a Boar"))
                {
                    break;
                }
                yield return Frames(3);
                var since = rig.DropsNow();
                var where = boar.transform.position;
                Kill(creature);
                yield return WaitLoot(rig, c, since, where, loot);
            }
            yield return ExpectFiltered(rig, c, "killed Boar", loot, SkipLineNotSelected, pickOneWithUse: true);

            // Mined rock: its stones are filtered.
            var rockPrefab = FindRockPrefab();
            if (c.Check(rockPrefab != null, "found a small rock that drops Stone when mined"))
            {
                var rock = Object.Instantiate(rockPrefab, rig.Spot(2.5f), Quaternion.identity);
                rig.Track(rock);
                yield return Frames(4);
                var since = rig.DropsNow();
                var hit = new HitData();
                hit.m_damage.m_pickaxe = 100000f;
                hit.m_damage.m_blunt = 100000f;
                hit.m_toolTier = 10;
                hit.m_point = rock.transform.position;
                hit.m_dir = Vector3.down;
                rock.GetComponent<Destructible>().Damage(hit);
                yield return WaitNewDrops(rig, since, 3f, loot);
                c.Note($"mined rock: {rockPrefab.name}");
                yield return ExpectFiltered(rig, c, "mined rock (" + rockPrefab.name + ")", loot, SkipLineNotSelected, pickOneWithUse: true);
            }

            // ExemptHarvest off: harvest drops are filtered like the rest.
            TestHooks.ExemptHarvest = false;
            c.Check(RaiseChanged(Plugin.ExemptHarvest), "ExemptHarvest change handler ran");
            var bush = rig.Spawn("RaspberryBush", rig.Spot(1.3f), Quaternion.identity);
            if (c.Check(bush != null, "spawned a RaspberryBush"))
            {
                yield return Settle();
                var since = rig.DropsNow();
                var berries = Count(rig.Inv, "Raspberry");
                player.Interact(bush, false, false);
                yield return WaitNewDrops(rig, since, 2f, loot);
                if (c.Check(loot.Count > 0, "ExemptHarvest off: berries dropped"))
                {
                    yield return new WaitForSeconds(2.5f);
                    c.Check(AliveCount(loot) == loot.Count && Count(rig.Inv, "Raspberry") == berries, "ExemptHarvest off: the berries stay on the ground");
                    c.Check(loot.All(d => HoverOf(d).Contains(SkipLineNotSelected)), "ExemptHarvest off: they show 'Auto pickup skips this (not selected)'");
                    // Back on: the same berries are collected (grace was decided when they dropped).
                    TestHooks.ExemptHarvest = true;
                    RaiseChanged(Plugin.ExemptHarvest);
                    c.Check(!loot.Any(d => HoverOf(d).Contains(SkipLineAny)), "ExemptHarvest on again: the line is gone");
                    yield return WaitGone(loot, 3f);
                    if (AliveCount(loot) > 0)
                    {
                        yield return WalkOver(rig, loot, 4f);
                    }
                    c.Check(AliveCount(loot) == 0, "ExemptHarvest on again: the berries are collected");
                }
                rig.Destroy(bush);
                yield return null;
            }
            TestHooks.ExemptHarvest = true;

            // Auto pickup off (V): berries stay, like the normal game.
            Player.m_enableAutoPickup = false;
            bush = rig.Spawn("RaspberryBush", rig.Spot(1.3f), Quaternion.identity);
            if (c.Check(bush != null, "spawned another RaspberryBush"))
            {
                yield return Settle();
                var since = rig.DropsNow();
                var berries = Count(rig.Inv, "Raspberry");
                player.Interact(bush, false, false);
                yield return WaitNewDrops(rig, since, 2f, loot);
                if (c.Check(loot.Count > 0, "auto pickup off: berries dropped"))
                {
                    yield return new WaitForSeconds(2.5f);
                    c.Check(AliveCount(loot) == loot.Count && Count(rig.Inv, "Raspberry") == berries, "auto pickup off: the berries stay on the ground");
                }
            }
            c.Check(rig.NewGuardReports == 0 && rig.Logs.Count(LogLevel.Error | LogLevel.Fatal) == 0, $"no error from the mod ({rig.Logs.First(LogLevel.Error | LogLevel.Fatal)})");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.door (T32)

    private static IEnumerator RunDoor()
    {
        if (!Ready(DoorName, out var player, out _))
        {
            yield break;
        }
        var c = new Checks(DoorName);
        var rig = new Rig(player, DoorName);
        try
        {
            NoteRoom(c, rig);
            FilterState.SetMode(FilterMode.SkipIgnored);
            // Everything a Greydwarf can drop is ignored (Resin and eyes among them): whatever the dice give must stay.
            var prefab = ZNetScene.instance.GetPrefab("Greydwarf");
            var table = prefab != null ? prefab.GetComponent<CharacterDrop>() : null;
            var ignored = new List<string> { "Resin", "GreydwarfEye" };
            if (table != null)
            {
                ignored.AddRange(table.m_drops.Where(d => d.m_prefab != null).Select(d => d.m_prefab.name));
            }
            foreach (var name in ignored.Distinct())
            {
                FilterState.Toggle(FilterState.Ignored, name);
            }
            c.Note("ignored: " + FilterState.Join(FilterState.Ignored));

            var doorGo = rig.Spawn("wood_door", rig.Spot(1.5f), Quaternion.LookRotation(rig.Right));
            var door = doorGo != null ? doorGo.GetComponentInChildren<Door>() : null;
            if (!c.Check(door != null && prefab != null, "spawned a wood door (wood_door); Greydwarf prefab found"))
            {
                c.Report();
                yield break;
            }
            yield return Settle();
            var loot = new List<ItemDrop>();
            for (var attempt = 0; attempt < 2 && loot.Count == 0; attempt++)
            {
                var greydwarf = rig.Spawn("Greydwarf", rig.Spot(2.2f, 1f), Quaternion.identity);
                var creature = greydwarf != null ? greydwarf.GetComponent<Character>() : null;
                if (!c.Check(creature != null, "spawned a Greydwarf next to the door"))
                {
                    break;
                }
                yield return Frames(3);
                // Drops another mod gave this very creature are ignored too.
                var own = greydwarf.GetComponent<CharacterDrop>();
                if (own != null)
                {
                    foreach (var extra in own.m_drops.Where(d => d.m_prefab != null).Select(d => d.m_prefab.name).Distinct())
                    {
                        if (!FilterState.Ignored.Contains(extra))
                        {
                            FilterState.Toggle(FilterState.Ignored, extra);
                        }
                    }
                }
                // E on the door, then the kill at once.
                player.Interact(door.gameObject, false, false);
                c.Check(!HarvestGrace.CoversSpawn(doorGo.transform.position + Vector3.up, Time.time) && !HarvestGrace.CoversSpawn(creature.GetCenterPoint(), Time.time),
                    "E on a door leaves no harvest spot");
                var since = rig.DropsNow();
                var where = greydwarf.transform.position;
                Kill(creature);
                yield return WaitLoot(rig, c, since, where, loot);
                // Only what is in the Ignored list matters here.
                loot.RemoveAll(d => d.m_itemData.m_dropPrefab == null || !FilterState.Ignored.Contains(d.m_itemData.m_dropPrefab.name));
            }
            c.Note("loot of the kill: " + string.Join(", ", loot.Select(d => d.m_itemData.m_stack + " " + L(d.m_itemData.m_shared.m_name)).ToArray()));
            yield return ExpectFiltered(rig, c, "Greydwarf killed right after E on the door", loot, SkipLineIgnored, pickOneWithUse: false);
            c.Check(rig.NewGuardReports == 0, "no error inside the mod");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.kiln (T33)

    private static IEnumerator RunKiln()
    {
        if (!Ready(KilnName, out var player, out _))
        {
            yield break;
        }
        var c = new Checks(KilnName);
        var rig = new Rig(player, KilnName);
        try
        {
            NoteRoom(c, rig);
            OnlyCoins(); // Coal is not selected
            var prefab = ZNetScene.instance.GetPrefab("charcoal_kiln");
            var model = prefab != null ? prefab.GetComponent<Smelter>() : null;
            if (!c.Check(model != null && model.m_outputPoint != null && model.m_addOreSwitch != null, "charcoal kiln prefab (charcoal_kiln) with an output point and an add switch"))
            {
                c.Report();
                yield break;
            }
            // Kiln placed so that its output point is 1.3 m in front of the player, its body behind that point.
            var local = prefab.transform.InverseTransformPoint(model.m_outputPoint.position);
            var flat = new Vector3(local.x, 0f, local.z);
            var yaw = flat.sqrMagnitude > 0.01f ? Vector3.SignedAngle(flat, -rig.Forward, Vector3.up) : 180f;
            var rot = Quaternion.AngleAxis(yaw, Vector3.up) * (flat.sqrMagnitude > 0.01f ? Quaternion.identity : Quaternion.LookRotation(rig.Forward));
            var target = rig.Spot(1.3f);
            var pos = target - rot * flat;
            pos.y = rig.GroundAt(pos);
            var kilnGo = rig.Spawn("charcoal_kiln", pos, rot);
            var kiln = kilnGo != null ? kilnGo.GetComponent<Smelter>() : null;
            if (!c.Check(kiln != null, "spawned a charcoal kiln"))
            {
                c.Report();
                yield break;
            }
            yield return Settle();
            var output = kiln.m_outputPoint.position;
            c.Note($"kiln output point {F(Vector3.Distance(output, player.transform.position))} m from the player; one coal every {F(kiln.m_secPerProduct)} s in the game");
            var wood = rig.Give("Wood", 5);
            var queue = kiln.GetQueueSize();
            var woods = Count(rig.Inv, "Wood");
            for (var i = 0; i < 3; i++)
            {
                player.Interact(kiln.m_addOreSwitch.gameObject, false, false);
                yield return new WaitForSeconds(0.3f);
            }
            c.Check(wood != null && kiln.GetQueueSize() == queue + 3 && Count(rig.Inv, "Wood") == woods - 3, $"three presses of E fed three Wood (queue {queue} to {kiln.GetQueueSize()})");
            c.Check(!HarvestGrace.CoversSpawn(output, Time.time) && !HarvestGrace.CoversSpawn(kiln.m_addOreSwitch.transform.position, Time.time), "feeding the kiln leaves no harvest spot");
            // Coal fast for the test (this kiln only).
            var since = rig.DropsNow();
            kiln.m_secPerProduct = 1f;
            var coal = new List<ItemDrop>();
            yield return WaitNewDrops(rig, since, 8f, coal);
            coal = coal.Where(d => d.m_itemData.m_shared.m_name == Shared("Coal")).ToList();
            yield return ExpectFiltered(rig, c, "coal out of the kiln the player just fed", coal, SkipLineNotSelected, pickOneWithUse: false);
            c.Check(rig.NewGuardReports == 0, "no error inside the mod");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.scythe (T30, T31)

    private static IEnumerator RunScythe()
    {
        if (!Ready(ScytheName, out var player, out _))
        {
            yield break;
        }
        var c = new Checks(ScytheName);
        var rig = new Rig(player, ScytheName);
        try
        {
            var inv = rig.Inv;
            NoteRoom(c, rig);
            OnlyCoins();
            // Field of 40 ripe barley, 3 to 7 m ahead: none of its drops is in reach from where the player stands.
            var plants = new List<Pickable>();
            for (var row = 0; row < 5; row++)
            {
                for (var col = 0; col < 8; col++)
                {
                    var go = rig.Spawn("Pickable_Barley", rig.Spot(3f + row, col - 3.5f), Quaternion.identity);
                    var pickable = go != null ? go.GetComponent<Pickable>() : null;
                    if (pickable != null)
                    {
                        plants.Add(pickable);
                    }
                }
            }
            if (!c.Check(plants.Count == 40, $"spawned 40 Pickable_Barley ({plants.Count})"))
            {
                c.Report();
                yield break;
            }
            yield return Settle();
            c.Check(plants.All(p => p.m_harvestable && p.CanBePicked()), "spawned crops are ripe and can be scythed at once");
            // Four swings of ten plants: for each plant in reach a scythe swing calls Pickable.Interact(local player).
            var since = rig.DropsNow();
            for (var swing = 0; swing < 4; swing++)
            {
                for (var i = 0; i < 10; i++)
                {
                    var plant = plants[swing * 10 + i];
                    if (plant != null)
                    {
                        plant.Interact(player, false, false);
                    }
                }
                yield return new WaitForSeconds(0.6f);
            }
            yield return Frames(3);
            var drops = rig.NewDrops(since);
            var totals = drops.GroupBy(d => d.m_itemData.m_shared.m_name).ToDictionary(g => g.Key, g => g.Sum(d => d.m_itemData.m_stack));
            var before = totals.Keys.ToDictionary(n => n, n => inv.CountItems(n, -1, false));
            c.Note($"{drops.Count} drops from 40 plants: " + string.Join(", ", totals.Select(p => p.Value + " " + L(p.Key)).ToArray()));
            c.Check(drops.Count >= 40, $"at least one drop per plant ({drops.Count})");
            c.Check(drops.All(FilterState.ListBlocks), "the lists alone would skip all of them");
            c.Check(drops.All(HarvestGrace.IsTagged), $"every drop of the four swings counts as harvested by hand ({drops.Count(HarvestGrace.IsTagged)} of {drops.Count})");
            c.Check(!drops.Any(d => HoverOf(d).Contains(SkipLineAny)), "none shows 'Auto pickup skips this'");
            yield return new WaitForSeconds(1f);
            c.Check(AliveCount(drops) == drops.Count, "nothing was picked up from where the player stands (drops out of reach)");

            // T31: filter changed between the harvest and the walk.
            FilterUi.CycleModeWithMessage();
            FilterUi.CycleModeWithMessage();
            FilterUi.CycleModeWithMessage();
            var lines = Run(Chat.instance, "lootfilter_select Stone");
            c.Check(FilterState.Mode == FilterMode.OnlySelected && FilterState.Selected.Contains("Stone"), $"mode cycled once around, Stone selected by command ({Lines(lines)})");
            c.Check(drops.All(HarvestGrace.IsTagged) && !drops.Any(d => HoverOf(d).Contains(SkipLineAny)), "after the mode cycle and the list change: the harvested drops keep their grace");

            // Walk back over every drop.
            yield return WalkOver(rig, drops, 30f);
            c.Check(AliveCount(drops) == 0, $"walking over them: all picked up ({AliveCount(drops)} left)");
            c.Check(totals.All(p => inv.CountItems(p.Key, -1, false) == before[p.Key] + p.Value), "all of it is in the inventory");
            c.Check(rig.NewGuardReports == 0 && rig.Logs.Count(LogLevel.Error | LogLevel.Fatal) == 0, $"no error from the mod ({rig.Logs.First(LogLevel.Error | LogLevel.Fatal)})");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.scythe-swing (T30, real swing)

    private static IEnumerator RunScytheSwing()
    {
        if (!Ready(ScytheSwingName, out var player, out _))
        {
            yield break;
        }
        var c = new Checks(ScytheSwingName);
        var rig = new Rig(player, ScytheSwingName);
        try
        {
            NoteRoom(c, rig);
            OnlyCoins();
            var scythe = rig.Give("Scythe");
            if (!c.Check(scythe != null && scythe.m_shared.m_attack != null && scythe.m_shared.m_attack.m_harvest, "Scythe given; its attack harvests"))
            {
                c.Report();
                yield break;
            }
            var attack = scythe.m_shared.m_attack;
            c.Note($"Scythe: harvest radius {F(attack.m_harvestRadius)} m (max skill {F(attack.m_harvestRadiusMaxLevel)} m), attack range {F(attack.m_attackRange)} m");
            var plants = new List<Pickable>();
            for (var row = 0; row < 3; row++)
            {
                for (var col = 0; col < 3; col++)
                {
                    var go = rig.Spawn("Pickable_Barley", rig.Spot(1.2f + row * 0.6f, (col - 1) * 0.6f), Quaternion.identity);
                    var pickable = go != null ? go.GetComponent<Pickable>() : null;
                    if (pickable != null)
                    {
                        plants.Add(pickable);
                    }
                }
            }
            if (!c.Check(plants.Count == 9, "spawned 9 Pickable_Barley in front of the player"))
            {
                c.Report();
                yield break;
            }
            yield return Settle();
            player.EquipItem(scythe, false);
            // The swing goes where the player LOOKS (Attack.Start turns the body to the look direction), and the field
            // lies along the body's facing of the test start. Round 1: three swings started, none cut a plant, the
            // screenshots show the body facing the camera. Body and look along the field, a little down.
            rig.Face(rig.Forward, 20f);
            yield return Frames(5);
            var since = rig.DropsNow();
            var cut = 0;
            var swing = new bool[1];
            for (var attempt = 0; attempt < 3 && cut == 0; attempt++)
            {
                yield return StartAttackSoon(player, false, 3f, swing);
                var end = Time.time + 2.5f;
                while (Time.time < end && cut == 0)
                {
                    yield return null;
                    cut = plants.Count(p => p == null || !p.CanBePicked());
                }
                c.Note($"swing {attempt + 1}: attack started {swing[0]}, plants cut {cut}");
            }
            if (c.Check(cut > 0, $"a real scythe swing cut {cut} of 9 plants"))
            {
                yield return ExpectCollected(rig, c, "real scythe swing", since, 2f);
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.bug.pick-column (T38)

    // One pick that gives many items: Pickable.Drop stacks them 0.5 m apart upwards. Every one of them is that
    // harvest. REAL BUG of 0.1.0 (round 1: 7 of 9 seeds tagged, 2 left on the ground): FAIL until the mod covers the
    // whole drop column (today: 4 m around the plant only, HarvestGrace.RadiusSqr).
    private static IEnumerator RunBugPickColumn()
    {
        if (!Ready(BugPickColumnName, out var player, out _))
        {
            yield break;
        }
        var c = new Checks(BugPickColumnName);
        var rig = new Rig(player, BugPickColumnName);
        try
        {
            NoteRoom(c, rig);
            OnlyCoins();
            // World modifier Resources x3.
            Game.m_resourceRate = 3f;
            // Which vanilla pickables give 8 items or more in one pick then? (0.5 m spawn offset + 0.5 m per item.)
            var tall = new List<string>();
            GameObject best = null; // seed crop (Pickable_Seed*) that gives most
            var bestCount = 0;
            foreach (var prefab in ZNetScene.instance.m_prefabs)
            {
                var p = prefab != null ? prefab.GetComponent<Pickable>() : null;
                if (p == null || p.m_itemPrefab == null || p.m_itemPrefab.GetComponent<ItemDrop>() == null)
                {
                    continue;
                }
                var n = p.m_dontScale ? p.m_amount : Mathf.Max(p.m_minAmountScaled, Game.instance.ScaleDrops(p.m_itemPrefab, p.m_amount));
                var extra = p.m_extraDrops != null && p.m_extraDrops.m_drops != null && p.m_extraDrops.m_drops.Count > 0 ? p.m_extraDrops.m_dropMax : 0;
                if (n + extra >= 8)
                {
                    tall.Add($"{prefab.name} {n}+{extra}");
                }
                if (n > bestCount && prefab.name.StartsWith("Pickable_Seed", StringComparison.Ordinal))
                {
                    best = prefab;
                    bestCount = n;
                }
            }
            c.Note($"Resources x3: pickables that give 8 or more items in one pick: {(tall.Count == 0 ? "none" : string.Join(", ", tall.Take(25).ToArray()))}"
                   + (tall.Count > 25 ? $" (+{tall.Count - 25} more)" : ""));
            var useName = best != null && bestCount >= 9 ? best.name : "RaspberryBush";
            var go = rig.Spawn(useName, rig.Spot(1.3f), Quaternion.identity);
            var pickable = go != null ? go.GetComponent<Pickable>() : null;
            if (!c.Check(pickable != null, $"spawned {useName}"))
            {
                c.Report();
                yield break;
            }
            yield return Settle();
            var gives = pickable.m_dontScale ? pickable.m_amount : Mathf.Max(pickable.m_minAmountScaled, Game.instance.ScaleDrops(pickable.m_itemPrefab, pickable.m_amount));
            if (gives < 9)
            {
                // No vanilla plant reaches it here: this one plant made generous (like a high-yield modded plant).
                pickable.m_dontScale = true;
                pickable.m_amount = 10;
                gives = 10;
                c.Note($"{useName}: amount raised to 10 on this one plant");
            }
            c.Check(pickable.CanBePicked(), $"{useName} can be picked ({gives} items expected, spawn offset {F(pickable.m_spawnOffset)} m)");
            var since = rig.DropsNow();
            var root = go.transform.position;
            player.Interact(go, false, false);
            var drops = new List<ItemDrop>();
            yield return WaitNewDrops(rig, since, 2f, drops);
            if (!c.Check(drops.Count >= 9, $"one pick gave {drops.Count} items (9 or more needed for the tall column)"))
            {
                c.Report();
                yield break;
            }
            var inv = rig.Inv;
            var name = drops[0].m_itemData.m_shared.m_name;
            var total = drops.Sum(d => d.m_itemData.m_stack);
            var before = inv.CountItems(name, -1, false);
            var tagged = drops.Count(HarvestGrace.IsTagged);
            c.Check(tagged == drops.Count, $"every item of one pick counts as harvested by hand ({tagged} of {drops.Count})");
            c.Check(!drops.Any(d => HoverOf(d).Contains(SkipLineAny)), $"none shows 'Auto pickup skips this' ({drops.Count(d => HoverOf(d).Contains(SkipLineAny))} do)");
            yield return WaitGone(drops, 4f);
            if (AliveCount(drops) > 0)
            {
                yield return WalkOver(rig, drops, 5f);
            }
            c.Check(AliveCount(drops) == 0 && inv.CountItems(name, -1, false) == before + total, $"all {drops.Count} items are picked up ({AliveCount(drops)} left on the ground)");
            // The rule itself: an item born high in the drop column of a plant just harvested is that harvest.
            HarvestGrace.Clear();
            HarvestGrace.Record(root);
            c.Check(HarvestGrace.CoversSpawn(root + Vector3.up * 5f, Time.time), "an item born 5 m straight above a plant just harvested counts as that harvest");
            c.Check(!HarvestGrace.CoversSpawn(root + rig.Forward * 6f, Time.time), "an item born 6 m to the side does not");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------------------------------------------------------------- lootfilter.compat-batch (C02)

    private static IEnumerator RunCompatBatch()
    {
        if (!Ready(CompatBatchName, out var player, out _))
        {
            yield break;
        }
        if (!OtherModActive("MC.Crafting.Stations.BatchFeed"))
        {
            SelfTest.Fail(CompatBatchName, "Batch Station Feeding (MC.Crafting.Stations.BatchFeed) is not active in this run: nothing checked");
            yield break;
        }
        var c = new Checks(CompatBatchName);
        var rig = new Rig(player, CompatBatchName);
        try
        {
            NoteRoom(c, rig);
            OnlyCoins();
            // Both mods hook Player.Interact. Cooked meat taken with a plain press, then with the alt press (the
            // press Batch Station Feeding looks at: with a finished item it hands the press back to the game).
            var station = rig.Spawn("piece_cookingstation", rig.Spot(1.4f), Quaternion.LookRotation(-rig.Forward));
            var cooking = station != null ? station.GetComponent<CookingStation>() : null;
            if (c.Check(cooking != null && cooking.m_conversion.Count > 0 && cooking.m_slots.Length >= 2, "spawned a cooking station with two slots"))
            {
                yield return Settle();
                var conversion = cooking.m_conversion.FirstOrDefault(x => x.m_from != null && x.m_from.name == "RawMeat") ?? cooking.m_conversion[0];
                cooking.SetSlot(0, conversion.m_to.name, 0f, CookingStation.Status.Done, false);
                cooking.SetSlot(1, conversion.m_to.name, 0f, CookingStation.Status.Done, false);
                var since = rig.DropsNow();
                player.Interact(station, false, false);
                yield return ExpectCollected(rig, c, "cooked meat taken with E", since, 2f);
                since = rig.DropsNow();
                player.Interact(station, false, true);
                yield return ExpectCollected(rig, c, "cooked meat taken with the alt press (Shift + E)", since, 2f);
                rig.Destroy(station);
                yield return null;
            }
            var bush = rig.Spawn("RaspberryBush", rig.Spot(1.3f), Quaternion.identity);
            if (c.Check(bush != null, "spawned a RaspberryBush"))
            {
                yield return Settle();
                var since = rig.DropsNow();
                player.Interact(bush, false, false);
                yield return ExpectCollected(rig, c, "E on a raspberry bush", since, 2f);
                rig.Destroy(bush);
                yield return null;
            }
            // Plain E on a kiln still adds exactly one, and is no harvest.
            var kilnGo = rig.Spawn("charcoal_kiln", rig.Spot(3.5f), Quaternion.LookRotation(-rig.Forward));
            var kiln = kilnGo != null ? kilnGo.GetComponent<Smelter>() : null;
            if (c.Check(kiln != null && kiln.m_addOreSwitch != null, "spawned a charcoal kiln"))
            {
                yield return Settle();
                rig.Give("Wood", 12);
                var woods = Count(rig.Inv, "Wood");
                var queue = kiln.GetQueueSize();
                // Spots of the meat and the berries taken a second ago still live (3 s, 4 m: the kiln stands inside)
                // and are no business of this press: forgotten, so a spot found after it is the press's own.
                HarvestGrace.Clear();
                player.Interact(kiln.m_addOreSwitch.gameObject, false, false);
                yield return new WaitForSeconds(0.3f);
                c.Check(kiln.GetQueueSize() == queue + 1 && Count(rig.Inv, "Wood") == woods - 1, $"plain E on the kiln adds one Wood (queue {queue} to {kiln.GetQueueSize()})");
                c.Check(!HarvestGrace.CoversSpawn(kiln.m_addOreSwitch.transform.position, Time.time), "feeding leaves no harvest spot");
            }
            c.Check(rig.NewGuardReports == 0 && rig.Logs.Count(LogLevel.Error | LogLevel.Fatal) == 0, $"no error from the mod ({rig.Logs.First(LogLevel.Error | LogLevel.Fatal)})");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }
}
#endif
