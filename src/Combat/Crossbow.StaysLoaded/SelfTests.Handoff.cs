#if DEBUG
using System.Collections;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.CrossbowStaysLoadedMod;

// Debug build only. TESTING.md multiplayer items M02 to M04: the half that one game can check. A second player's
// game is never here, so every one of them stay partly by hand.
//   crossbow.handoff           M02: the Loaded mark is in the networked data of a dropped crossbow and of a chest (what
//                              a friend's game receive), and picked up / taken out again it is loaded at once
//   crossbow.relog.chest       M02: chest with the loaded crossbow, real log out, world loaded from the save (all
//                              objects new, like in another game): crossbow from the chest is loaded at once
//   crossbow.disabled.stamped  M03: game started with every MC mod off: marked crossbow of the save reload like any
//                              other (that is what a friend without the mod get)
//   crossbow.vanilla-shot      M04: my patches off (the game code a friend without the mod run), real shot, crossbow
//                              dropped, patches on, picked up: must reload; same with my own repair before equipping
internal static partial class SelfTests
{
    // ---------- M02: on the ground and in a chest ----------

    private static IEnumerator HandoffBody(Rig rig, Checks c)
    {
        var p = rig.P;
        rig.UseSettings(false).Defaults(); // Arbalest on the list whatever the player's cfg says
        var bow = rig.Give(BowName);
        var crossbow = rig.Give(ArbalestName);
        if (!c.Check(bow != null && crossbow != null, $"could not give {BowName} ({bow != null}) and {ArbalestName} ({crossbow != null})"))
        {
            yield break;
        }
        rig.EmptyHands();
        var s = new Swap();
        var loadTime = crossbow.GetWeaponLoadingTime();
        yield return Take(rig, crossbow, s, loadTime + 4f);
        if (!c.Check(s.InHand && s.Loaded && LoadedState.IsLoaded(crossbow), $"set-up: {ArbalestName} did not load ({s}, mark '{MarkOf(crossbow)}')"))
        {
            yield break;
        }
        var mark = MarkOf(crossbow);

        // A. Dropped on the ground. Bow in hand first: with a free hand the game would equip what is picked up.
        yield return Take(rig, bow, s, 0f);
        var drop = Drop(rig, crossbow);
        if (!c.Check(s.InHand && drop != null, $"could not drop the loaded {ArbalestName} ({BowName} in hand {s.InHand}, dropped {drop != null})"))
        {
            yield break;
        }
        yield return new WaitForSeconds(0.3f);
        var onGround = FromGroundData(drop);
        c.Check(onGround != null && MarkOf(onGround) == mark && LoadedState.IsLoaded(onGround),
            $"the networked data of the dropped {ArbalestName} does not carry a valid Loaded mark (mark '{MarkOf(onGround)}', it was '{mark}')");
        var picked = drop.m_itemData;
        var ok = new bool[1];
        yield return PickUp(rig, drop, ok);
        if (!c.Check(ok[0], $"could not pick the dropped {ArbalestName} up again"))
        {
            yield break;
        }
        c.Check(!ReferenceEquals(picked, crossbow) && LoadedState.IsLoaded(picked),
            $"the picked up {ArbalestName} does not count as loaded (mark '{MarkOf(picked)}', durability {F(picked.m_durability)})");
        yield return Take(rig, picked, s, 1f, 0.3f);
        c.Check(s.InHand && s.LoadedAtOnce && !s.ReloadQueued, $"the picked up {ArbalestName} was not loaded at once when equipped ({s})");

        // B. Into a chest and out again.
        var chestObject = SpawnChest(rig, false);
        if (!c.Check(chestObject != null, $"no {ChestName} prefab with a container"))
        {
            yield break;
        }
        yield return new WaitForSeconds(0.5f);
        var chest = chestObject.GetComponent<Container>();
        if (!c.Check(chest != null && chest.GetInventory() != null, $"the spawned {ChestName} has no inventory"))
        {
            yield break;
        }
        IntoChest(p, chest, picked);
        yield return null;
        if (!c.Check(chest.GetInventory().ContainsItem(picked) && !rig.Inv.ContainsItem(picked), $"could not move the loaded {ArbalestName} into the chest"))
        {
            yield break;
        }
        var inChest = FromChestData(chest, ArbalestName);
        c.Check(inChest != null && !ReferenceEquals(inChest, picked) && LoadedState.IsLoaded(inChest),
            $"the networked data of the chest does not hold a {ArbalestName} with a valid Loaded mark (found {inChest != null}, mark '{MarkOf(inChest)}')");
        OutOfChest(p, chest, picked);
        if (!c.Check(rig.Inv.ContainsItem(picked), $"could not take the {ArbalestName} out of the chest"))
        {
            yield break;
        }
        yield return Take(rig, picked, s, 1f, 0.3f);
        c.Check(s.InHand && s.LoadedAtOnce && !s.ReloadQueued, $"the {ArbalestName} taken out of the chest was not loaded at once when equipped ({s})");
        c.Summary = $"the Loaded mark ('{mark}') is in the networked data of the dropped {ArbalestName} and of the chest; picked up and taken out again it was loaded at once";
    }

    // ---------- M02: chest through a real log out ----------

    // World load give every object a new id (ZDO.Load: m_uid.SetID), so an id kept from before the log out find
    // nothing after it, or a wrong object. Me keep the spot of the chest and look for a wood chest there.
    private static bool _relogLeft;
    private static Vector3 _relogChestPos;
    private static ItemDrop.ItemData _relogItem;
    private static string _relogMark;

    private static IEnumerator RelogBefore()
    {
        _relogLeft = false;
        _relogItem = null;
        _relogMark = null;
        var rig = Rig.Create(RelogChestName, 1);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(RelogChestName);
        var chest = new GameObject[1];
        try
        {
            yield return RelogBeforeBody(rig, c, chest);
        }
        finally
        {
            if (_relogLeft && chest[0] != null)
            {
                rig.Untrack(chest[0]); // chest stay in the world: the part after the log out remove it
            }
            rig.Restore();
        }
        c.Report();
    }

    private static IEnumerator RelogBeforeBody(Rig rig, Checks c, GameObject[] chestOut)
    {
        var p = rig.P;
        rig.UseSettings(false).Defaults();
        var crossbow = rig.Give(ArbalestName);
        if (!c.Check(crossbow != null, $"could not give {ArbalestName}"))
        {
            yield break;
        }
        rig.EmptyHands();
        var s = new Swap();
        yield return Take(rig, crossbow, s, crossbow.GetWeaponLoadingTime() + 4f);
        if (!c.Check(s.InHand && s.Loaded && LoadedState.IsLoaded(crossbow), $"set-up: {ArbalestName} did not load ({s})"))
        {
            yield break;
        }
        chestOut[0] = SpawnChest(rig, true);
        if (!c.Check(chestOut[0] != null, $"no {ChestName} prefab with a container"))
        {
            yield break;
        }
        yield return new WaitForSeconds(0.5f);
        var chest = chestOut[0].GetComponent<Container>();
        var view = chestOut[0].GetComponent<ZNetView>();
        if (!c.Check(chest != null && chest.GetInventory() != null && view != null && view.IsValid(), $"the spawned {ChestName} has no inventory"))
        {
            yield break;
        }
        IntoChest(p, chest, crossbow);
        yield return null;
        var saved = FromChestData(chest, ArbalestName);
        if (!c.Check(chest.GetInventory().ContainsItem(crossbow) && !rig.Inv.ContainsItem(crossbow) && saved != null && LoadedState.IsLoaded(saved),
                $"the loaded {ArbalestName} is not in the chest's data with a valid mark before the log out (in chest {chest.GetInventory().ContainsItem(crossbow)}, mark in data '{MarkOf(saved)}')"))
        {
            yield break;
        }
        _relogMark = MarkOf(crossbow);
        _relogItem = crossbow;
        _relogChestPos = chestOut[0].transform.position;
        _relogLeft = true;
        c.Summary = $"before the log out: a loaded {ArbalestName} (mark '{_relogMark}') lies in a chest";
    }

    private static IEnumerator RelogAfter()
    {
        if (!_relogLeft)
        {
            // The part before the log out failed and said why; it left nothing in the world.
            SelfTest.Fail(RelogChestName, "nothing to check after the log out: the part before it left no chest with a loaded crossbow");
            yield break;
        }
        var c = new Checks(RelogChestName);
        var chest = new GameObject[1];
        Rig rig = null;
        try
        {
            rig = Rig.Create(RelogChestName, 1);
            if (rig != null)
            {
                yield return RelogAfterBody(rig, c, chest);
            }
        }
        finally
        {
            if (rig != null)
            {
                rig.Restore();
            }
            Safe("chest", () => RemoveRelogChest(chest[0]));
            _relogLeft = false;
            _relogItem = null;
        }
        if (rig != null)
        {
            c.Report();
        }
    }

    // Wood chest that stand where the part before the log out left one (a piece never move). Null = none there yet.
    private static GameObject FindRelogChest()
    {
        GameObject best = null;
        var bestDistance = 1f;
        foreach (var box in Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
        {
            if (box == null || !box.gameObject.name.StartsWith(ChestName, System.StringComparison.Ordinal))
            {
                continue;
            }
            var distance = Vector3.Distance(box.transform.position, _relogChestPos);
            if (distance < bestDistance)
            {
                best = box.gameObject;
                bestDistance = distance;
            }
        }
        return best;
    }

    private static IEnumerator RelogAfterBody(Rig rig, Checks c, GameObject[] chestOut)
    {
        var p = rig.P;
        rig.UseSettings(false).Defaults();
        yield return Until(() => (chestOut[0] = FindRelogChest()) != null, 30f);
        if (!c.Check(chestOut[0] != null, $"no {ChestName} stands where the part before the log out left one 30 s after logging back in"))
        {
            yield break;
        }
        var chest = chestOut[0].GetComponent<Container>();
        var view = chestOut[0].GetComponent<ZNetView>();
        if (!c.Check(chest != null && view != null, $"the object found again is no {ChestName}"))
        {
            yield break;
        }
        // Chest read its saved data within a second; the game hand objects of the loaded area to this player within a
        // few seconds (only the owner write a chest).
        yield return Until(() => view.IsValid() && view.IsOwner() && chest.GetInventory() != null && FindIn(chest.GetInventory(), ArbalestName) != null, 20f);
        var inChest = chest.GetInventory() != null ? FindIn(chest.GetInventory(), ArbalestName) : null;
        if (!c.Check(inChest != null, $"the chest loaded from the save holds no {ArbalestName}"))
        {
            yield break;
        }
        c.Check(!ReferenceEquals(inChest, _relogItem), "the crossbow in the chest is the same object as before the log out (the world was not loaded again)");
        c.Check(MarkOf(inChest) == _relogMark && LoadedState.IsLoaded(inChest),
            $"the {ArbalestName} read from the save does not count as loaded (mark '{MarkOf(inChest)}', before the log out '{_relogMark}', durability {F(inChest.m_durability)})");
        OutOfChest(p, chest, inChest);
        if (!c.Check(rig.Inv.ContainsItem(inChest), $"could not take the {ArbalestName} out of the chest"))
        {
            yield break;
        }
        rig.Own(inChest);
        rig.EmptyHands();
        var s = new Swap();
        yield return Take(rig, inChest, s, 1f, 0.3f);
        c.Check(s.InHand && s.LoadedAtOnce && !s.ReloadQueued, $"the {ArbalestName} from the chest of the saved world was not loaded at once when equipped ({s})");
        c.Summary = $"after a real log out and load of the world the {ArbalestName} taken from the chest was loaded at once";
    }

    private static void RemoveRelogChest(GameObject chest)
    {
        if (chest == null)
        {
            chest = FindRelogChest();
        }
        if (chest == null)
        {
            return;
        }
        // Only the owner's destroy remove the object for good. Single-player world: this game is the server.
        var view = chest.GetComponent<ZNetView>();
        if (view != null && view.IsValid() && !view.IsOwner())
        {
            view.ClaimOwnership();
        }
        DestroyObject(chest);
    }

    // ---------- M03: game started with every MC mod off ----------

    private const string DisabledMarkKey = DisabledName + ".mark";

    // End of the first game (me on): a really loaded crossbow stay in the inventory.
    private static IEnumerator DisabledLeave()
    {
        var rig = Rig.Create(DisabledName, 1);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(DisabledName);
        try
        {
            yield return DisabledLeaveBody(rig, c);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    private static IEnumerator DisabledLeaveBody(Rig rig, Checks c)
    {
        var view = FeatureRegistry.Find(ModInfo.Guid);
        if (!c.Check(view != null && view.Value.IsActive, "the mod is not active in the first game: it cannot leave a loaded crossbow for the second one"))
        {
            yield break;
        }
        rig.UseSettings(false).Defaults();
        var crossbow = rig.Give(ArbalestName);
        if (!c.Check(crossbow != null, $"could not give {ArbalestName}"))
        {
            yield break;
        }
        rig.EmptyHands();
        var s = new Swap();
        yield return Take(rig, crossbow, s, crossbow.GetWeaponLoadingTime() + 4f);
        if (!c.Check(s.InHand && s.Loaded, $"set-up: {ArbalestName} did not load ({s})"))
        {
            yield break;
        }
        yield return PutAway(rig, crossbow);
        if (!c.Check(!rig.P.IsItemEquiped(crossbow) && LoadedState.IsLoaded(crossbow),
                $"the put away {ArbalestName} carries no valid Loaded mark (still equipped {rig.P.IsItemEquiped(crossbow)}, mark '{MarkOf(crossbow)}')"))
        {
            yield break;
        }
        if (!c.Check(SelfTest.Carry(DisabledMarkKey, MarkOf(crossbow)), "the mark could not be kept for the second game (no run folder of the probe)"))
        {
            yield break;
        }
        rig.Keep(crossbow);
        c.Summary = $"first game: a loaded {ArbalestName} (mark '{MarkOf(crossbow)}') stays in the inventory for the game with the mods off";
    }

    // Second game (every MC mod off from the start): the marked crossbow is still marked, and the game reload it.
    private static IEnumerator DisabledCheck()
    {
        var rig = Rig.Create(DisabledName, 0);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(DisabledName);
        try
        {
            yield return DisabledCheckBody(rig, c);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    private static IEnumerator DisabledCheckBody(Rig rig, Checks c)
    {
        var p = rig.P;
        ItemDrop.ItemData crossbow = null;
        foreach (var item in rig.Inv.GetAllItems())
        {
            if (PrefabOf(item) == ArbalestName && LoadedState.HasStamp(item))
            {
                crossbow = item;
                break;
            }
        }
        if (!c.Check(crossbow != null, $"no {ArbalestName} with a Loaded mark is in the inventory: the first game left none, or the game without the mod lost the mark"))
        {
            yield break;
        }
        rig.Own(crossbow); // out of the inventory at the end
        var view = FeatureRegistry.Find(ModInfo.Guid);
        c.Check(view != null && !view.Value.IsActive, $"the mod is not off in this game (state {(view != null ? view.Value.State : "not registered")})");
        var mark = MarkOf(crossbow);
        var carried = SelfTest.Carried(DisabledMarkKey);
        c.Check(carried != null && mark == carried && LoadedState.IsLoaded(crossbow),
            $"the game without the mod did not keep the mark as it was (mark '{mark}', the first game left '{carried}', durability {F(crossbow.m_durability)})");

        if (p.IsItemEquiped(crossbow))
        {
            yield return PutAway(rig, crossbow);
        }
        rig.EmptyHands();
        var s = new Swap();
        var loadTime = crossbow.GetWeaponLoadingTime();
        yield return Take(rig, crossbow, s, loadTime + 4f);
        if (!c.Check(s.InHand, $"could not equip the marked {ArbalestName}"))
        {
            yield break;
        }
        c.Check(!s.LoadedAtOnce && s.ReloadQueued, $"with the mod off the marked {ArbalestName} did not start a reload when equipped ({s})");
        c.Check(s.Loaded && s.Seconds >= 0.8f * loadTime,
            $"with the mod off the reload of the marked {ArbalestName} took {F(s.Seconds)} s (loaded {s.Loaded}), a full reload takes {F(loadTime)} s");
        c.Check(MarkOf(crossbow) == mark, $"the game without the mod changed the mark ('{MarkOf(crossbow)}', it was '{mark}')");
        c.Summary = $"game started with the mod off: the {ArbalestName} that carries the Loaded mark '{mark}' did a full reload ({F(s.Seconds)} s) when equipped";
    }

    // ---------- M04: fired by game code without me, then back to me ----------

    private static IEnumerator VanillaShotBody(Rig rig, Checks c)
    {
        var plugin = Self();
        if (!c.Check(plugin != null && plugin.IsActive && !plugin.TestOff, "the mod is not active"))
        {
            yield break;
        }
        rig.UseSettings(false).Defaults();
        var bow = rig.Give(BowName);
        var crossbow = rig.Give(ArbalestName);
        if (!c.Check(bow != null && crossbow != null && rig.GiveAmmo(BoltName, 4),
                $"could not give {BowName} ({bow != null}), {ArbalestName} ({crossbow != null}) and 4 {BoltName}"))
        {
            yield break;
        }
        var drain = crossbow.m_shared.m_useDurabilityDrain * Game.m_durabilityRate;
        if (!c.Check(crossbow.m_shared.m_useDurability && drain > 0f,
                $"a shot costs no durability in this world (drain {F(drain)}): the mod cannot tell a shot fired without it (limit named in the README)"))
        {
            yield break;
        }
        rig.EmptyHands();
        var s = new Swap();
        yield return Take(rig, crossbow, s, crossbow.GetWeaponLoadingTime() + 4f);
        if (!c.Check(s.InHand && s.Loaded, $"set-up: {ArbalestName} did not load ({s})"))
        {
            yield break;
        }

        // Repair case first: crossbow is new, so its mark say "full durability"; after the foreign shot my repair put
        // it back to exactly that number.
        var held = new[] { crossbow };
        var done = new bool[1];
        yield return FiredElsewhere(rig, c, plugin, bow, held, true, "fired without the mod, then repaired by me", done);
        if (!done[0])
        {
            yield break;
        }
        yield return FiredElsewhere(rig, c, plugin, bow, held, false, "fired without the mod", done);
        if (done[0])
        {
            c.Summary = $"a {ArbalestName} fired by the game without my patches and picked up again did a full reload when equipped, also after my own repair";
        }
    }

    // held[0]: crossbow loaded in hand with a valid mark. Patches off (vanilla code, like in the game of a friend
    // without the mod), real shot, on the ground, patches on, picked up, (repair), equipped: must reload. At the end
    // held[0] = the same crossbow (new item object after the pick up), loaded in hand again.
    private static IEnumerator FiredElsewhere(Rig rig, Checks c, Plugin plugin, ItemDrop.ItemData bow, ItemDrop.ItemData[] held, bool repair, string label, bool[] done)
    {
        done[0] = false;
        var p = rig.P;
        var crossbow = held[0];
        if (!c.Check(ReferenceEquals(p.m_weaponLoaded, crossbow) && LoadedState.IsLoaded(crossbow),
                $"{label}: set-up: the {ArbalestName} is not loaded in hand with a valid mark (mark '{MarkOf(crossbow)}', durability {F(crossbow.m_durability)})"))
        {
            yield break;
        }
        var mark = MarkOf(crossbow);
        var durability = crossbow.m_durability;
        var drain = crossbow.m_shared.m_useDurabilityDrain * Game.m_durabilityRate;

        plugin.TestTurnOff();
        if (!c.Check(plugin.TestOff, $"{label}: could not take the mod's patches off"))
        {
            yield break;
        }
        var shot = new Shot();
        yield return Fire(rig, crossbow, shot);
        if (!c.Check(shot.Started && shot.Left, $"{label}: the shot without the mod did not go off (attack started {shot.Started}, load gone {shot.Left})"))
        {
            yield break;
        }
        c.Check(crossbow.m_durability <= durability - 0.5f * drain,
            $"{label}: the shot cost {F(durability - crossbow.m_durability)} durability, expected {F(drain)}");
        c.Check(MarkOf(crossbow) == mark && !LoadedState.IsLoaded(crossbow),
            $"{label}: after the shot the old mark must still be on the item and no longer count (mark '{MarkOf(crossbow)}', it was '{mark}', counts as loaded {LoadedState.IsLoaded(crossbow)})");

        // Still without the mod: other weapon in hand, crossbow on the ground.
        var s = new Swap();
        yield return Take(rig, bow, s, 0f);
        var drop = s.InHand ? Drop(rig, crossbow) : null;
        if (!c.Check(drop != null, $"{label}: could not drop the fired {ArbalestName} ({BowName} in hand {s.InHand})"))
        {
            yield break;
        }
        var picked = drop.m_itemData;

        plugin.TestTurnOn();
        if (!c.Check(!plugin.TestOff && plugin.IsActive, $"{label}: could not put the mod's patches back on"))
        {
            yield break;
        }
        var ok = new bool[1];
        yield return PickUp(rig, drop, ok);
        if (!c.Check(ok[0], $"{label}: could not pick the fired {ArbalestName} up again"))
        {
            yield break;
        }
        held[0] = picked;
        c.Check(LoadedState.HasStamp(picked) && !LoadedState.IsLoaded(picked),
            $"{label}: the {ArbalestName} must come back with its old mark, which must not count (has a mark {LoadedState.HasStamp(picked)}, counts as loaded {LoadedState.IsLoaded(picked)})");

        if (repair)
        {
            var repaired = Repair(rig, picked);
            c.Check(repaired, $"{label}: the game's repair did not bring the {ArbalestName} to full durability ({F(picked.m_durability)} of {F(picked.GetMaxDurability())})");
            c.Check(!LoadedState.IsLoaded(picked),
                $"{label}: after my own repair the fired {ArbalestName} counts as loaded (mark '{MarkOf(picked)}', durability {F(picked.m_durability)})");
        }

        var loadTime = picked.GetWeaponLoadingTime();
        yield return Take(rig, picked, s, loadTime + 4f);
        if (!c.Check(s.InHand, $"{label}: could not equip the {ArbalestName} that came back"))
        {
            yield break;
        }
        c.Check(!s.LoadedAtOnce && s.ReloadQueued, $"{label}: the {ArbalestName} that came back did not start a reload when equipped ({s})");
        c.Check(s.Loaded && s.Seconds >= 0.8f * loadTime,
            $"{label}: the reload of the {ArbalestName} that came back took {F(s.Seconds)} s (loaded {s.Loaded}), a full reload takes {F(loadTime)} s");
        c.Note($"{label}: equipped again: {s}");
        done[0] = s.Loaded;
    }
}
#endif
