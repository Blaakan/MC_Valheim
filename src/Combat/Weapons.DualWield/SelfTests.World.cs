#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.WeaponsDualWieldMod;

// Debug build only. More in-world self tests around the pair (same helpers as SelfTests.cs):
//   dual.places   sitting on a chair (weapons stay in hand), eating at feast pieces (FeastMeadows, FeastBlackforest:
//                 whether the game hides the main weapon there is NOTEd, same hands after either way; when hidden:
//                 attack refused, eat + dodge, two feasts in a row), main weapon moved into a chest (both ways the
//                 inventory screen does it), off-hand weapon dropped and picked up again, tombstone with and without
//                 the DeathKeepEquip world key (Player.CreateTombStone, everything put back)
//   dual.sheath   sheathed pairs the other tests leave out: MaceIron + SwordIron crossed, pair put away by a
//                 workbench (Player.SetCraftingStation), R mid-stride while jogging, lying down as in a bed with the
//                 CrossSheathedPair setting switched off and on, then standing up (back pair and knife pair)
//   dual.others   other MC mods, when they run: Crossbow Stays Loaded (crossbow still loaded after a pair), Tower
//                 Shield Wall (tower shield against a pair), Sneak Ambush (smoke screen never in the off hand)
//   dual.kills    Creature Kill and Tame Counts, when it runs: a Greyling killed by an off-hand hit is counted as a
//                 melee kill in the game's own kill statistics
//   dual.log      every warning and error line the mod logged while the tests ran: only the ones the tests provoke
internal static partial class SelfTests
{
    private const string PlacesName = "dual.places";
    private const string SheathName = "dual.sheath";
    private const string OthersName = "dual.others";
    private const string KillsName = "dual.kills";
    private const string LogName = "dual.log";

    // ---------- helpers ----------

    // Spot at ground height.
    private static Vector3 Ground(Vector3 position)
    {
        if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(position, out var height))
        {
            position.y = height;
        }
        return position;
    }

    private static Vector3 RightOf(Player p) => Quaternion.Euler(0f, 90f, 0f) * Forward(p);

    // Object the game would call hovered when the crosshair is on this object (Player.FindHoverObject: the collider's
    // own object when it is Hoverable, else its rigidbody's, else the collider's).
    private static GameObject HoverOf(GameObject go)
    {
        foreach (var collider in go.GetComponentsInChildren<Collider>())
        {
            if (collider == null || !collider.enabled || collider.isTrigger)
            {
                continue;
            }
            if (collider.GetComponent<Hoverable>() != null)
            {
                return collider.gameObject;
            }
            return collider.attachedRigidbody ? collider.attachedRigidbody.gameObject : collider.gameObject;
        }
        return go;
    }

    // Player back on its spot and facing (a test that sat, lay down or was turned by an interaction).
    private static void PutBack(Player p, Vector3 spot, Quaternion facing)
    {
        p.transform.position = spot;
        p.transform.rotation = facing;
        if (p.m_body != null)
        {
            p.m_body.position = spot;
            p.m_body.rotation = facing;
            p.m_body.linearVelocity = Vector3.zero;
        }
        p.m_currentVel = Vector3.zero;
    }

    // Jog in place for 'seconds' (controller already off): forward controls every frame, player pinned on its spot.
    private static IEnumerator Jog(Player p, Vector3 spot, float seconds)
    {
        var until = Time.time + seconds;
        while (Time.time < until)
        {
            p.SetControls(Vector3.forward, false, false, false, false, false, false, false, false, false, false);
            yield return null;
            Pin(p, spot);
        }
    }

    // ---------- dual.places ----------

    private static IEnumerator RunPlaces()
    {
        var c = new Checks(PlacesName);
        var player = Player.m_localPlayer;
        if (player == null || ZoneSystem.instance == null)
        {
            SelfTest.Fail(PlacesName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        List<Player.Food> foods = null;
        var healthBefore = player.GetHealth();
        var spot = player.transform.position;
        var facing = player.transform.rotation;
        var keepEquip = false;
        Inventory grave = null;
        Inventory inventory = null;
        try
        {
            bench = new Bench(player);
            foods = new List<Player.Food>(player.m_foods);
            inventory = bench.Inventory;
            var sword = bench.Give(Sword);
            var axe = bench.Give(Axe);
            if (sword == null || axe == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            var ahead = Forward(player);
            var side = RightOf(player);

            // T18: sitting on a chair keeps the weapons in hand (Chair.Interact: AttachStart without hiding them).
            var scene = ZNetScene.instance;
            var chairPrefab = scene.m_prefabs.FirstOrDefault(p => p != null && p.GetComponent<Piece>() != null
                                                                  && p.GetComponentInChildren<Chair>(true) != null
                                                                  && !p.GetComponentInChildren<Chair>(true).m_inShip
                                                                  && p.GetComponent<Ship>() == null);
            c.Check(chairPrefab != null, "a chair piece exists in the game");
            if (chairPrefab != null)
            {
                var chairGo = bench.SpawnAt(chairPrefab.name, Ground(spot + side * 1.2f), facing);
                yield return null;
                yield return null;
                var chair = chairGo != null ? chairGo.GetComponentInChildren<Chair>(true) : null;
                bench.Pair(sword, axe);
                yield return null;
                if (chair != null)
                {
                    chair.Interact(player, false, false);
                }
                yield return new WaitForSeconds(0.5f);
                var sat = player.IsAttached();
                c.Check(sat && Holds(player, sword, axe),
                    $"sitting on a chair ({chairPrefab.name}): seated {sat}, the weapons stay in hand ({HandsText(player)})");
                player.AttachStop();
                yield return null;
                PutBack(player, spot, facing);
                yield return null;
                c.Check(Holds(player, sword, axe), $"after the chair: same hands ({HandsText(player)})");
                Invariant(c, player, "chair");
            }

            // T17: eating at a feast piece. The game hides the main weapon only when the object the "use" lands on
            // carries a Consumable ItemDrop that is a piece (Humanoid.DoInteractAnimation); either way: same hands after.
            var feasts = new List<GameObject>();
            var offsets = new[] { ahead * 1.1f - side * 0.5f, ahead * 1.1f + side * 0.5f };
            // The two feasts TESTING.md names; should a game update rename them, any two feast pieces.
            var feastNames = new List<string>();
            foreach (var wanted in new[] { "FeastMeadows", "FeastBlackforest" })
            {
                var prefab = scene.GetPrefab(wanted);
                if (prefab != null && prefab.GetComponent<Feast>() != null)
                {
                    feastNames.Add(wanted);
                }
            }
            foreach (var prefab in scene.m_prefabs)
            {
                if (feastNames.Count >= 2)
                {
                    break;
                }
                if (prefab != null && prefab.GetComponent<Feast>() != null && !feastNames.Contains(prefab.name))
                {
                    feastNames.Add(prefab.name);
                }
            }
            for (var i = 0; i < feastNames.Count && i < offsets.Length; i++)
            {
                var feastGo = bench.SpawnAt(feastNames[i], Ground(spot + offsets[i]), Quaternion.identity);
                if (feastGo == null)
                {
                    continue;
                }
                var drop = feastGo.GetComponent<ItemDrop>();
                if (drop != null)
                {
                    drop.m_autoPickup = false;
                    if (feastGo.GetComponent<Piece>() != null)
                    {
                        // As when a player places it: a piece, no longer an item lying around.
                        drop.MakePiece();
                    }
                }
                feasts.Add(feastGo);
            }
            c.Check(feasts.Count == 2, $"two feast pieces spawned ({string.Join(", ", feastNames.ToArray())}; {feasts.Count} of 2)");
            yield return new WaitForSeconds(0.3f);
            if (feasts.Count == 2)
            {
                var hovers = feasts.Select(HoverOf).ToList();
                bench.Pair(sword, axe);
                yield return WaitIdle(player);
                yield return WaitMinor(player);
                player.m_foods.Clear();
                player.Interact(hovers[0], false, false);
                yield return null;
                var ate = player.m_foods.Count > 0;
                var hidden = player.m_useItemTime > 0f && player.m_rightItem == null;
                var target = hovers[0].GetComponentInParent<Interactable>() as MonoBehaviour;
                SelfTest.Note(PlacesName, $"{feastNames[0]}: use lands on '{(target != null ? target.gameObject.name : "nothing")}' "
                                          + $"({(target != null ? target.GetType().Name : "none")}), food eaten {ate}, main weapon "
                                          + $"hidden for the eat animation {hidden} (eat timer {F2(player.m_useItemTime)} s)");
                c.Check(ate, $"eating at the feast worked (foods now {player.m_foods.Count})");
                if (hidden)
                {
                    c.Check(ReferenceEquals(player.m_hiddenRightItem, sword) && Holds(player, null, axe) && Hands.IsEatWindow(player),
                        $"feast: main weapon hidden, off-hand weapon stays ({HandsText(player)})");
                    var started = player.StartAttack(null, false);
                    c.Check(!started && !player.InAttack(), "feast: an attack while the main weapon is hidden is refused");
                    yield return WaitEat(player);
                    c.Check(Holds(player, sword, axe), $"after the feast: same hands ({HandsText(player)})");
                    Invariant(c, player, "feast");

                    // Eat and roll at once.
                    yield return WaitMinor(player);
                    player.m_foods.Clear();
                    player.Interact(hovers[0], false, false);
                    yield return null;
                    var until = Time.time + 3f;
                    while (Time.time < until && player.m_useItemTime > 0.5f)
                    {
                        yield return null;
                    }
                    player.Dodge(-Forward(player));
                    until = Time.time + 4f;
                    while (Time.time < until && (player.m_useItemTime > 0f || player.InDodge() || Hands.PendingEatReturn))
                    {
                        yield return null;
                    }
                    yield return null;
                    yield return null;
                    c.Check(Holds(player, sword, axe), $"feast + roll: same hands after the roll ({HandsText(player)})");
                    Invariant(c, player, "feast and roll");
                    yield return WaitIdle(player);
                    PutBack(player, spot, facing);
                    yield return null;

                    // Two feasts within the second.
                    yield return WaitMinor(player);
                    player.m_foods.Clear();
                    player.Interact(hovers[0], false, false);
                    yield return null;
                    var first = player.m_useItemTime > 0f;
                    player.Interact(hovers[1], false, false);
                    yield return null;
                    c.Check(first && player.m_foods.Count == 2 && player.m_rightItem == null
                            && ReferenceEquals(player.m_hiddenRightItem, sword) && Holds(player, null, axe),
                        $"second feast eaten while the main weapon is hidden: it stays remembered (foods {player.m_foods.Count}; "
                        + $"{HandsText(player)})");
                    yield return WaitEat(player);
                    c.Check(Holds(player, sword, axe), $"two feasts in a row: same hands after ({HandsText(player)})");
                    Invariant(c, player, "two feasts");
                }
                else
                {
                    yield return new WaitForSeconds(1.5f);
                    c.Check(Holds(player, sword, axe), $"feast (nothing hidden): both weapons stay in hand ({HandsText(player)})");
                    yield return WaitMinor(player);
                    player.m_foods.Clear();
                    player.Interact(hovers[0], false, false);
                    yield return null;
                    player.Interact(hovers[1], false, false);
                    yield return new WaitForSeconds(1.5f);
                    c.Check(Holds(player, sword, axe), $"two feasts in a row (nothing hidden): same hands ({HandsText(player)})");
                    Invariant(c, player, "two feasts");
                }
                player.m_foods.Clear();
                PutBack(player, spot, facing);
                yield return null;
            }

            // T21: the main weapon goes into a chest: the axe becomes the main weapon. Both ways of the inventory
            // screen (InventoryGui.OnSelectedItem): the quick move (Ctrl + click), and a drag onto a chest slot.
            var chestGo = bench.SpawnAt("piece_chest_wood", Ground(spot - ahead * 2f), facing);
            var chest = chestGo != null ? chestGo.GetComponent<Container>() : null;
            var chestItems = chest != null ? chest.GetInventory() : null;
            c.Check(chestItems != null, "a wooden chest spawned");
            if (chestItems != null)
            {
                bench.Pair(sword, axe);
                yield return null;
                player.RemoveEquipAction(sword);
                player.UnequipItem(sword);
                chestItems.MoveItemToThis(inventory, sword);
                yield return null;
                c.Check(chestItems.ContainsItem(sword) && !inventory.ContainsItem(sword) && Holds(player, axe, null)
                        && !Hands.IsMarked(axe),
                    $"main weapon moved into a chest: the axe moves to the main hand ({HandsText(player)})");
                Invariant(c, player, "main weapon into a chest");
                inventory.MoveItemToThis(chestItems, sword);
                yield return null;

                bench.Pair(sword, axe);
                yield return null;
                player.RemoveEquipAction(sword);
                player.UnequipItem(sword, false);
                chestItems.MoveItemToThis(inventory, sword, sword.m_stack, 0, 0);
                var inChest = chestItems.GetItemAt(0, 0);
                if (inChest != null)
                {
                    // The screen then tries to equip the item now in that slot; the game refuses (not in the player's inventory).
                    player.EquipItem(inChest, false);
                }
                yield return null;
                c.Check(inChest != null && !inventory.ContainsItem(sword) && Holds(player, axe, null) && !Hands.IsMarked(axe),
                    $"main weapon dragged onto a chest slot: the axe moves to the main hand ({HandsText(player)})");
                Invariant(c, player, "main weapon dragged into a chest");
                if (inChest != null)
                {
                    inventory.MoveItemToThis(chestItems, inChest);
                    sword = inChest;
                }
                yield return null;
            }

            // T21: the off-hand weapon dropped on the ground: the sword stays. M11 (items): picked up again, it pairs
            // like any weapon.
            bench.Pair(sword, axe);
            yield return null;
            var dropsBefore = new HashSet<ItemDrop>(ItemDrop.s_instances);
            var dropped = player.DropItem(inventory, axe, 1);
            yield return null;
            var onGround = ItemDrop.s_instances.FirstOrDefault(d => d != null && !dropsBefore.Contains(d) && d.m_itemData != null
                                                                    && ReferenceEquals(d.m_itemData.m_shared, axe.m_shared));
            if (onGround != null)
            {
                bench.Track(onGround.gameObject);
            }
            c.Check(dropped && onGround != null && !inventory.ContainsItem(axe) && Holds(player, sword, null),
                $"off-hand weapon dropped: it lies on the ground, the sword stays in the main hand ({HandsText(player)})");
            Invariant(c, player, "off-hand weapon dropped");
            if (onGround != null)
            {
                var back = onGround.m_itemData;
                SelfTest.Note(PlacesName, $"the dropped former off-hand axe still carries the off-hand marker = {Hands.IsMarked(back)} "
                                          + "(custom data travels with the item; the game ignores it)");
                var picked = player.Pickup(onGround.gameObject, false, false);
                yield return null;
                var pairedAgain = picked && inventory.ContainsItem(back) && player.EquipItem(back);
                yield return null;
                c.Check(pairedAgain && Holds(player, sword, back) && Hands.IsMarked(back) && !Hands.IsMarked(sword),
                    $"the axe picked up again pairs normally with the sword ({HandsText(player)})");
                Invariant(c, player, "picked up again");
                axe = back;
            }

            // T20: death puts both weapons in the tombstone, nothing equipped (Player.CreateTombStone, what OnDeath
            // calls). Everything goes back at once: an emptied tombstone would vanish and give its boost.
            bench.Pair(sword, axe);
            yield return null;
            var worn = inventory.GetAllItems()
                .Where(i => player.IsItemEquiped(i) && !ReferenceEquals(i, sword) && !ReferenceEquals(i, axe)).ToList();
            var tombs = new HashSet<TombStone>(UnityEngine.Object.FindObjectsByType<TombStone>(FindObjectsSortMode.None));
            player.CreateTombStone();
            var tomb = UnityEngine.Object.FindObjectsByType<TombStone>(FindObjectsSortMode.None).FirstOrDefault(t => !tombs.Contains(t));
            grave = tomb != null && tomb.GetComponent<Container>() != null ? tomb.GetComponent<Container>().GetInventory() : null;
            c.Check(grave != null && grave.ContainsItem(sword) && grave.ContainsItem(axe) && !inventory.ContainsItem(sword)
                    && !inventory.ContainsItem(axe) && player.m_rightItem == null && player.m_leftItem == null
                    && !sword.m_equipped && !axe.m_equipped,
                $"death in a normal world: both weapons in the tombstone, nothing in the hands ({HandsText(player)})");
            EmptyGrave(inventory, grave);
            grave = null;
            if (tomb != null)
            {
                scene.Destroy(tomb.gameObject);
            }
            foreach (var item in worn)
            {
                player.EquipItem(item, false);
            }
            yield return null;
            c.Check(inventory.ContainsItem(sword) && inventory.ContainsItem(axe), "test clean-up: the tombstone's items are back in the inventory");

            // T20 with the keep-equipment world key: the pair stays equipped (and is saved so: the respawn loads it,
            // dual.pair and dual.keep check that load).
            ZoneSystem.instance.SetGlobalKey(GlobalKeys.DeathKeepEquip);
            keepEquip = true;
            var keyUntil = Time.time + 3f;
            while (Time.time < keyUntil && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.DeathKeepEquip))
            {
                yield return null;
            }
            c.Check(ZoneSystem.instance.GetGlobalKey(GlobalKeys.DeathKeepEquip), "world key DeathKeepEquip set for the test");
            bench.Pair(sword, axe);
            yield return null;
            tombs = new HashSet<TombStone>(UnityEngine.Object.FindObjectsByType<TombStone>(FindObjectsSortMode.None));
            player.CreateTombStone();
            tomb = UnityEngine.Object.FindObjectsByType<TombStone>(FindObjectsSortMode.None).FirstOrDefault(t => !tombs.Contains(t));
            grave = tomb != null && tomb.GetComponent<Container>() != null ? tomb.GetComponent<Container>().GetInventory() : null;
            c.Check(Holds(player, sword, axe) && sword.m_equipped && axe.m_equipped && Hands.IsMarked(axe)
                    && inventory.ContainsItem(sword) && inventory.ContainsItem(axe)
                    && (grave == null || (!grave.ContainsItem(sword) && !grave.ContainsItem(axe))),
                $"death with DeathKeepEquip: the pair stays equipped, sword main and axe off ({HandsText(player)})");
            EmptyGrave(inventory, grave);
            grave = null;
            if (tomb != null)
            {
                scene.Destroy(tomb.gameObject);
            }
            ZoneSystem.instance.RemoveGlobalKey(GlobalKeys.DeathKeepEquip);
            keepEquip = false;
            yield return null;
            Invariant(c, player, "tombstone with DeathKeepEquip");
            yield return NoteIfStuck(PlacesName, player, "the chair, feast, chest and tombstone steps");
            c.Report();
        }
        finally
        {
            if (grave != null && inventory != null)
            {
                EmptyGrave(inventory, grave);
            }
            if (keepEquip && ZoneSystem.instance != null)
            {
                ZoneSystem.instance.RemoveGlobalKey(GlobalKeys.DeathKeepEquip);
            }
            if (player.IsAttached())
            {
                player.AttachStop();
            }
            if (foods != null)
            {
                PutFoodsBack(player, foods, healthBefore);
            }
            if (bench != null)
            {
                bench.TakeBack();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
            PutBack(player, spot, facing);
        }
    }

    // Everything a tombstone took goes back into the inventory it came from (same item objects, same slots).
    private static void EmptyGrave(Inventory inventory, Inventory grave)
    {
        if (grave == null)
        {
            return;
        }
        foreach (var item in grave.m_inventory.ToList())
        {
            if (!inventory.m_inventory.Contains(item))
            {
                inventory.m_inventory.Add(item);
            }
        }
        grave.m_inventory.Clear();
        inventory.Changed();
    }

    // ---------- dual.sheath ----------

    private static IEnumerator RunSheath()
    {
        var c = new Checks(SheathName);
        var player = Player.m_localPlayer;
        if (player == null || player.m_visEquipment == null)
        {
            SelfTest.Fail(SheathName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        var vis = player.m_visEquipment;
        var spot = player.transform.position;
        var facing = player.transform.rotation;
        var controller = player.GetComponent<PlayerController>();
        var controllerOn = controller != null && controller.enabled;
        var walkBefore = player.GetWalk();
        var skills = new SkillSave(player, Skills.SkillType.Run);
        string stationName = null;
        var stationKnown = false;
        var stationLevel = 0;
        try
        {
            _visName = SheathName;
            bench = new Bench(player);
            var sword = bench.Give(Sword);
            var sword2 = bench.Give(Sword);
            var mace = bench.Give(Mace);
            var knife = bench.Give(KnifeBlack);
            var knife2 = bench.Give(KnifeBlack);
            if (sword == null || sword2 == null || mace == null || knife == null || knife2 == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            var side = RightOf(player);
            BackCross.TestEnabled = true;

            // T33: MaceIron + SwordIron crossed on the back.
            bench.Pair(mace, sword);
            yield return new WaitForSeconds(0.3f);
            yield return Sheathe(c, player, vis, "MaceIron + SwordIron", vis.m_backMelee, "sheathed-mace-sword", false);
            player.ShowHandItems();
            yield return null;
            c.Check(Holds(player, mace, sword), $"mace + sword drawn again ({HandsText(player)})");

            // T18, T33: a workbench puts the pair away (Player.SetCraftingStation), placed the same way; R draws the
            // same hands. The station counts as known for the test, so nothing is unlocked for the character.
            var benchPrefab = ZNetScene.instance.GetPrefab("piece_workbench");
            var prefabStation = benchPrefab != null ? benchPrefab.GetComponentInChildren<CraftingStation>(true) : null;
            c.Check(prefabStation != null, "the workbench piece exists");
            if (prefabStation != null)
            {
                stationName = prefabStation.m_name;
                stationKnown = player.m_knownStations.TryGetValue(stationName, out stationLevel);
                player.m_knownStations[stationName] = 99;
                var benchGo = bench.SpawnAt("piece_workbench", Ground(spot + side * 1.6f), facing);
                yield return null;
                yield return null;
                var station = benchGo != null ? benchGo.GetComponentInChildren<CraftingStation>(true) : null;
                bench.Pair(sword, sword2);
                yield return new WaitForSeconds(0.3f);
                BackCross.ResetRecord();
                if (station != null)
                {
                    player.SetCraftingStation(station);
                }
                yield return null;
                yield return null;
                c.Check(station != null && player.m_rightItem == null && player.m_leftItem == null
                        && ReferenceEquals(player.m_hiddenRightItem, sword) && ReferenceEquals(player.m_hiddenLeftItem, sword2),
                    $"using a workbench puts both weapons away ({HandsText(player)})");
                CheckCrossed(c, vis, vis.m_backMelee, "pair put away by a workbench", true);
                yield return new WaitForSeconds(0.3f);
                player.SetCraftingStation(null);
                player.ShowHandItems();
                yield return null;
                c.Check(Holds(player, sword, sword2), $"after the workbench, R draws the same hands ({HandsText(player)})");
                Invariant(c, player, "workbench");
            }

            // T33: R pressed mid-stride while jogging: the X is level right after each put-away.
            bench.Pair(sword, sword2);
            yield return new WaitForSeconds(0.3f);
            player.HideHandItems();
            yield return new WaitForSeconds(0.3f);
            if (controller != null)
            {
                controller.enabled = false;
            }
            player.SetWalk(false);
            for (var round = 1; round <= 3; round++)
            {
                yield return Jog(player, spot, 0.45f);
                player.ShowHandItems();
                yield return Jog(player, spot, 0.3f);
                var drawn = Holds(player, sword, sword2);
                player.HideHandItems();
                yield return Jog(player, spot, 0.05f);
                yield return Jog(player, spot, 0.05f);
                c.Check(drawn, $"R mid-stride {round}: the pair drawn while jogging ({HandsText(player)})");
                CheckSymmetric(c, vis, $"R mid-stride {round}, right after the put-away");
                yield return Jog(player, spot, 0.3f);
                CheckSymmetric(c, vis, $"R mid-stride {round}, 0.3 s later, still jogging");
            }
            player.SetControls(Vector3.zero, false, false, false, false, false, false, false, false, false, false);
            if (controller != null)
            {
                controller.enabled = controllerOn;
            }
            yield return new WaitForSeconds(0.3f);
            PutBack(player, spot, facing);
            player.ShowHandItems();
            yield return null;
            c.Check(Holds(player, sword, sword2), $"after jogging: same hands ({HandsText(player)})");

            // T33, T18: lying down as in a bed (Bed.Interact: AttachStart on the bed's spot, weapons hidden), setting
            // switched off and on while lying, then up: the back pair is crossed again within a moment, the knives are
            // one per hip. Without the "in bed" flag: the game must not start a night's sleep in the test.
            var bedGo = bench.SpawnAt("bed", Ground(spot - side * 2.5f), facing);
            yield return null;
            yield return null;
            var bed = bedGo != null ? bedGo.GetComponentInChildren<Bed>(true) : null;
            c.Check(bed != null && bed.m_spawnPoint != null, "a bed spawned");
            if (bed != null && bed.m_spawnPoint != null)
            {
                foreach (var knives in new[] { false, true })
                {
                    var what = knives ? "knife pair" : "sword pair";
                    var main = knives ? knife : sword;
                    var off = knives ? knife2 : sword2;
                    bench.Pair(main, off);
                    yield return new WaitForSeconds(0.3f);
                    player.AttachStart(bed.m_spawnPoint, bedGo, true, false, false, "attach_bed", new Vector3(0f, 0.5f, 0f));
                    yield return new WaitForSeconds(0.9f);
                    c.Check(player.IsAttached() && ReferenceEquals(player.m_hiddenRightItem, main) && ReferenceEquals(player.m_hiddenLeftItem, off),
                        $"{what}: lying down puts both weapons away ({HandsText(player)})");
                    BackCross.TestEnabled = false;
                    BackCross.RebuildAll();
                    yield return null;
                    yield return null;
                    yield return null;
                    BackCross.TestEnabled = true;
                    BackCross.ResetRecord();
                    BackCross.RebuildAll();
                    yield return null;
                    yield return null;
                    yield return null;
                    SelfTest.Note(SheathName, $"{what}, setting off and on while lying: layout {BackCross.LastLayout} "
                                              + $"({BackCross.LastRecord ?? "nothing placed yet"})");
                    player.AttachStop();
                    var stood = Time.time;
                    var wanted = knives ? BackCross.Layout.Hips : BackCross.Layout.Crossed;
                    while (Time.time - stood < 4f && BackCross.LastLayout != wanted)
                    {
                        yield return null;
                    }
                    var after = Time.time - stood;
                    c.Check(BackCross.LastLayout == wanted,
                        $"{what}: placed again by kind after getting up (layout {BackCross.LastLayout} after {F2(after)} s, {wanted} expected)");
                    // Away from the bed as a player leaves it: a movement key (a few steps on the spot), then still.
                    if (controller != null)
                    {
                        controller.enabled = false;
                    }
                    yield return Jog(player, spot, 0.4f);
                    player.SetControls(Vector3.zero, false, false, false, false, false, false, false, false, false, false);
                    if (controller != null)
                    {
                        controller.enabled = controllerOn;
                    }
                    yield return new WaitForSeconds(1.2f);
                    PutBack(player, spot, facing);
                    yield return new WaitForSeconds(0.3f);
                    SelfTest.Note(SheathName, $"{what}, 1.5 s after getting up: attached {player.IsAttached()}, can move "
                                              + $"{player.CanMove()}, on the ground {player.IsOnGround()}, clips {ClipsNow(player)}");
                    if (knives)
                    {
                        CheckHips(c, vis, "knife pair after getting up", true);
                    }
                    else
                    {
                        CheckCrossed(c, vis, vis.m_backMelee, "sword pair after getting up", true);
                    }
                    player.ShowHandItems();
                    yield return null;
                    c.Check(Holds(player, main, off), $"{what}: after the bed, R draws the same hands ({HandsText(player)})");
                    Invariant(c, player, $"{what} and bed");
                }
            }
            BackCross.TestEnabled = null;
            yield return NoteIfStuck(SheathName, player, "the workbench, jogging and bed steps");
            c.Report();
        }
        finally
        {
            _visName = VisualsName;
            if (player.IsAttached())
            {
                player.AttachStop();
            }
            player.SetControls(Vector3.zero, false, false, false, false, false, false, false, false, false, false);
            player.SetWalk(walkBefore);
            if (controller != null)
            {
                controller.enabled = controllerOn;
            }
            if (player.m_currentStation != null)
            {
                player.SetCraftingStation(null);
            }
            // Workbench gone first, then the character forgets it again (a station in range is discovered once a second).
            if (bench != null)
            {
                bench.TakeBack();
            }
            if (stationName != null)
            {
                if (stationKnown)
                {
                    player.m_knownStations[stationName] = stationLevel;
                }
                else
                {
                    player.m_knownStations.Remove(stationName);
                }
            }
            skills.Restore();
            Bench.ClearOverrides();
            Hands.ResetState();
            PutBack(player, spot, facing);
        }
    }

    // ---------- dual.others ----------

    private const string CrossbowGuid = "MC.Combat.Crossbow.StaysLoaded";
    private const string TowerGuid = "MC.Combat.Shields.TowerWall";
    private const string SneakGuid = "MC.Combat.Sneak.Ambush";
    private const string StatsGuid = "MC.Exploration.Stats.PerCreature";

    private static bool OtherModActive(string guid)
    {
        var feature = FeatureRegistry.Find(guid);
        return feature != null && feature.Value.IsActive;
    }

    // The game's own lifetime kill count of a creature (what Creature Kill and Tame Counts shows).
    private static int Kills(string creature, KillModifiers bucket)
    {
        var profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
        var stats = profile != null && profile.m_playerStats.Length > 0 && profile.m_playerStats[0] != null
            ? profile.m_playerStats[0].m_enemyStats
            : null;
        if (stats == null || (int)bucket >= stats.Length || stats[(int)bucket] == null)
        {
            return -1;
        }
        return stats[(int)bucket].TryGetValue(creature, out var count) ? Mathf.RoundToInt(count) : 0;
    }

    private static IEnumerator RunOthers()
    {
        var c = new Checks(OthersName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(OthersName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        SkillSave skills = null;
        try
        {
            bench = new Bench(player);
            skills = new SkillSave(player, Skills.SkillType.Swords, Skills.SkillType.Axes, Skills.SkillType.Crossbows,
                Skills.SkillType.Blocking);
            var sword = bench.Give(Sword);
            var axe = bench.Give(Axe);
            if (sword == null || axe == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            var mods = new[] { CrossbowGuid, TowerGuid, SneakGuid, StatsGuid };
            SelfTest.Note(OthersName, "other MC mods running: " + string.Join(", ", mods.Select(g => $"{g} {OtherModActive(g)}").ToArray()));

            // X01: Crossbow Stays Loaded: a loaded crossbow, a pair, the crossbow again: still loaded.
            c.Check(OtherModActive(CrossbowGuid), "Crossbow Stays Loaded runs in this game (its check cannot run without it)");
            if (OtherModActive(CrossbowGuid))
            {
                var crossbow = bench.Give("CrossbowArbalest");
                var bolts = bench.GiveStack("BoltBone", 5);
                c.Check(crossbow != null && bolts != null, "CrossbowArbalest and BoltBone given");
                if (crossbow != null)
                {
                    // Empty hands first, as when this step ran first.
                    bench.Empty();
                    yield return null;
                    player.EquipItem(crossbow);
                    var until = Time.time + 12f;
                    while (Time.time < until && !player.IsWeaponLoaded())
                    {
                        yield return null;
                    }
                    var loaded = player.IsWeaponLoaded();
                    yield return WaitIdle(player);
                    c.Check(loaded && player.IsItemEquiped(crossbow), $"the crossbow is loaded ({HandsText(player)})");
                    player.EquipItem(sword);
                    player.EquipItem(axe);
                    yield return null;
                    c.Check(Holds(player, sword, axe) && !crossbow.m_equipped,
                        $"switching to a pair puts the crossbow away ({HandsText(player)})");
                    Invariant(c, player, "pair after a crossbow");
                    yield return new WaitForSeconds(0.3f);
                    player.EquipItem(crossbow);
                    var reloadFree = Time.time + 0.6f;
                    while (Time.time < reloadFree && !player.IsWeaponLoaded())
                    {
                        yield return null;
                    }
                    c.Check(player.IsItemEquiped(crossbow) && !sword.m_equipped && !axe.m_equipped,
                        $"the crossbow puts the pair away ({HandsText(player)})");
                    c.Check(player.IsWeaponLoaded() && player.GetActionQueueCount() == 0,
                        $"back on the crossbow it is still loaded, no reload (loaded {player.IsWeaponLoaded()}, queued actions {player.GetActionQueueCount()})");
                    yield return WaitIdle(player);
                    player.EquipItem(sword);
                    player.EquipItem(axe);
                    yield return null;
                    c.Check(Holds(player, sword, axe) && Hands.IsMarked(axe), $"the pair can be made again ({HandsText(player)})");
                    Invariant(c, player, "pair again after the crossbow");
                }
            }

            // X03: Tower Shield Wall: a tower shield puts both weapons away; the sword puts the tower shield away.
            c.Check(OtherModActive(TowerGuid), "Tower Shield Wall runs in this game (its check cannot run without it)");
            if (OtherModActive(TowerGuid))
            {
                var tower = bench.Give("ShieldIronTower");
                c.Check(tower != null, "ShieldIronTower given");
                if (tower != null)
                {
                    bench.Pair(sword, axe);
                    yield return null;
                    player.EquipItem(tower);
                    yield return null;
                    yield return null;
                    c.Check(ReferenceEquals(player.m_leftItem, tower) && player.m_rightItem == null && !sword.m_equipped && !axe.m_equipped,
                        $"tower shield while paired: both weapons put away ({HandsText(player)})");
                    Invariant(c, player, "tower shield");
                    player.EquipItem(sword);
                    yield return null;
                    yield return null;
                    c.Check(Holds(player, sword, null) && !tower.m_equipped, $"the sword puts the tower shield away ({HandsText(player)})");
                    Invariant(c, player, "sword after the tower shield");
                }
            }

            // X04 (smoke screen): Sneak Ambush's item never goes to the off hand: both weapons put away.
            c.Check(OtherModActive(SneakGuid), "Sneak Ambush runs in this game (its check cannot run without it)");
            if (OtherModActive(SneakGuid))
            {
                var smoke = PrefabItem("MC_SmokeScreen") != null ? bench.Give("MC_SmokeScreen") : null;
                c.Check(smoke != null, "MC_SmokeScreen given");
                if (smoke != null)
                {
                    bench.Pair(sword, axe);
                    yield return null;
                    var equipped = player.EquipItem(smoke);
                    yield return null;
                    yield return null;
                    c.Check(equipped && player.IsItemEquiped(smoke) && !ReferenceEquals(player.m_leftItem, smoke) && !sword.m_equipped
                            && !axe.m_equipped,
                        $"MC_SmokeScreen while paired: both weapons put away, never in the off hand ({HandsText(player)})");
                    Invariant(c, player, "smoke screen");
                }
            }

            yield return NoteIfStuck(OthersName, player, "the crossbow, tower shield and smoke screen steps");
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

    // ---------- dual.kills ----------

    // Own test (was the last step of dual.others): the kill needs a player who can swing, and must not hold back the
    // equip checks of the other mods when it fails.
    private static IEnumerator RunKills()
    {
        var c = new Checks(KillsName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(KillsName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        SkillSave skills = null;
        try
        {
            bench = new Bench(player);
            skills = new SkillSave(player, Skills.SkillType.Swords, Skills.SkillType.Axes);
            var sword = bench.Give(Sword);
            var axe = bench.Give(Axe);
            if (sword == null || axe == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            // X09: Creature Kill and Tame Counts reads the game's own kill statistics: a Greyling killed by an off-hand
            // hit counts as a melee kill there. God mode stays on (the kill is flagged as cheated: no achievement).
            c.Check(OtherModActive(StatsGuid), "Creature Kill and Tame Counts runs in this game (its check cannot run without it)");
            if (OtherModActive(StatsGuid))
            {
                var greyling = bench.Spawn("Greyling", DummyGap);
                c.Check(greyling != null && greyling.Character != null, "Greyling spawned in front of the player");
                if (greyling != null && greyling.Character != null)
                {
                    NoteDummy(KillsName, greyling);
                    // No loot and no ragdoll left behind.
                    var loot = greyling.Go.GetComponent<CharacterDrop>();
                    if (loot != null)
                    {
                        UnityEngine.Object.Destroy(loot);
                    }
                    greyling.Character.m_deathEffects = new EffectList();
                    // Like a creature nobody hit yet: the game resets a creature's "killed with" note to "mixed" when
                    // its health is set to the maximum (Character.SetHealth), which the dummy's set-up just did. No
                    // top-up from here on (it would reset it again).
                    greyling.Refill = false;
                    var greylingZdo = greyling.Character.m_nview != null ? greyling.Character.m_nview.GetZDO() : null;
                    if (greylingZdo != null)
                    {
                        greylingZdo.Set(ZDOVars.s_modifiers, (int)KillModifiers.CountNone);
                    }
                    var creature = greyling.Character.m_name;
                    bench.Pair(sword, axe);
                    yield return new WaitForSeconds(0.5f);
                    // Ready to swing: a test before this one may have left the player sitting, lying or frozen.
                    yield return WaitCanAttack(player);
                    var ready = _attackGate;
                    c.Check(Holds(player, sword, axe) && ready == null,
                        $"pair in hand and the player can attack before the kill ({HandsText(player)}; "
                        + $"{ready ?? "nothing in the way"})");
                    var melee = Kills(creature, KillModifiers.Melee);
                    var total = Kills(creature, KillModifiers.MixedAndTotal);
                    DualSwing.ResetRecords();
                    DualSwing.Recording = true;
                    // After the first swing's (main hand) hit: one hit point left, so the off-hand hit of the second
                    // swing kills.
                    Action<DualSwing.HitRecord> weaken = hit =>
                    {
                        if (hit.Trigger == "dualaxes0" && greyling.Go != null)
                        {
                            greyling.Refill = false;
                            greyling.Character.SetHealth(1f);
                        }
                    };
                    yield return Swing(player, greyling, 2, false, onHit: weaken);
                    var last = DualSwing.Damages.LastOrDefault();
                    var dead = greyling.Go == null || greyling.Character == null || greyling.Character.IsDead();
                    var refused = DualSwing.Swings.Count == 0 ? AttackGate(player) ?? "no gate of the game closed now" : null;
                    c.Check(dead && last != null && last.Hand == Hand.Off && last.Trigger == "dualaxes1",
                        $"the Greyling died from the off-hand hit of the second swing (dead {dead}, last hit "
                        + $"{(last != null ? last.Trigger + " " + last.Hand : "none")}; {DualSwing.Swings.Count} pair swing(s) "
                        + $"started, {DualSwing.Hits.Count} hit event(s), {DualSwing.Damages.Count} hit(s) reached a creature"
                        + $"{(refused != null ? "; no swing started: " + refused : "")})");
                    yield return null;
                    c.Check(melee >= 0 && Kills(creature, KillModifiers.Melee) == melee + 1
                            && Kills(creature, KillModifiers.MixedAndTotal) == total + 1,
                        $"the kill counts as a melee kill of {creature} in the game's statistics (melee {melee} -> "
                        + $"{Kills(creature, KillModifiers.Melee)}, total {total} -> {Kills(creature, KillModifiers.MixedAndTotal)})");
                    DualSwing.Recording = false;
                    DualSwing.ResetRecords();
                    yield return WaitIdle(player);
                }
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
        }
    }

    // ---------- dual.log ----------

    // Warning and error lines this mod logged since its tests were first registered (Debug builds only; one listener
    // for the session).
    private sealed class LogWatch : ILogListener
    {
        private static LogWatch _installed;
        private static readonly List<string> Warnings = new List<string>();
        private static readonly List<string> Errors = new List<string>();
        // Last lines of any level (a test looking for a line the mod just logged).
        private static readonly List<string> Recent = new List<string>();
        private const int RecentMax = 400;

        internal static void Install()
        {
            if (_installed != null)
            {
                return;
            }
            _installed = new LogWatch();
            BepInEx.Logging.Logger.Listeners.Add(_installed);
        }

        internal static string[] WarningLines()
        {
            lock (Warnings)
            {
                return Warnings.ToArray();
            }
        }

        internal static string[] ErrorLines()
        {
            lock (Warnings)
            {
                return Errors.ToArray();
            }
        }

        // One of the mod's last log lines holds this text.
        internal static bool Saw(string part)
        {
            lock (Warnings)
            {
                return Recent.Any(line => line.Contains(part));
            }
        }

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            if (eventArgs == null || eventArgs.Source == null || eventArgs.Source.SourceName != ModInfo.Name)
            {
                return;
            }
            var level = eventArgs.Level;
            var text = eventArgs.Data != null ? eventArgs.Data.ToString() : "";
            lock (Warnings)
            {
                Recent.Add(text);
                if (Recent.Count > RecentMax)
                {
                    Recent.RemoveRange(0, Recent.Count - RecentMax);
                }
                if ((level & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) == 0)
                {
                    return;
                }
                if ((level & LogLevel.Warning) != 0)
                {
                    Warnings.Add(text);
                }
                else
                {
                    Errors.Add(text);
                }
            }
        }

        public void Dispose()
        {
        }
    }

    // Warnings the tests provoke on purpose (TESTING.md L03): an unknown ExcludedWeapons name, a moves item that
    // cannot be used, a key that cannot be bound.
    private static readonly string[] ProvokedWarnings =
    {
        "ExcludedWeapons: no item named ", "Moves.PairMoves = ", "Moves.KnifePairMoves = ", "Controls.SwapHandsKey = ",
        "Controls.MainHandKey = ",
    };

    private static IEnumerator RunLog()
    {
        var c = new Checks(LogName);
        yield return null;
        var warnings = LogWatch.WarningLines();
        var errors = LogWatch.ErrorLines().Where(e => !e.Contains(SelfTest.Prefix + " FAIL")).ToArray();
        var odd = warnings.Where(w => !ProvokedWarnings.Any(p => w.StartsWith(p, StringComparison.Ordinal))).ToArray();
        SelfTest.Note(LogName, $"{warnings.Length} warning line(s) from {ModInfo.Name} since its tests were registered, "
                               + $"{warnings.Length - odd.Length} of them provoked by the tests on purpose");
        c.Check(odd.Length == 0, $"no warning from {ModInfo.Name} but the provoked ones ({odd.Length}: {string.Join(" | ", odd)})");
        c.Check(errors.Length == 0, $"no error from {ModInfo.Name} ({errors.Length}: {string.Join(" | ", errors)})");
        c.Report();
    }
}
#endif
