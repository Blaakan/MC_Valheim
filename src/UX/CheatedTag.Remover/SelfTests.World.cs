#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.UX.CheatedTagRemoverMod;

// Me = tests that need no second game: byte work, and the real command in a single player world.
internal static partial class SelfTests
{
    // ---------- uncheat.blob ----------

    // Every mix of optional fields the game can write, long strings and long custom data lists (two byte counts),
    // tagged or not. Blob written by Inventory.Save / ItemData.Save, cleaned by me, read back by ItemData.Load.
    private static IEnumerator RunBlob()
    {
        var c = new Checks(BlobName);
        yield return null;
        var wood = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab("Wood") : null;
        if (!c.Check(wood != null, "item prefab 'Wood' exists"))
        {
            c.Report();
            yield break;
        }

        var items = new List<ItemDrop.ItemData>();
        var expectTagged = 0;
        for (var mask = 0; mask < 64; mask++)
        {
            var item = new ItemDrop.ItemData
            {
                m_durability = 12.25f + mask,
                m_gridPos = new Vector2i(mask % 8, mask / 8),
                m_worldLevel = mask % 3,
                m_pickedUp = mask % 2 == 1,
                m_equipped = mask % 5 == 1,
                m_quality = (mask & 1) != 0 ? 3 : 1,
                m_stack = (mask & 2) != 0 ? 37 : 1,
                m_variant = (mask & 4) != 0 ? 2 : 0,
                m_dropPrefab = (mask & 16) != 0 ? wood : null,
                m_cheated = mask % 3 != 0,
            };
            if ((mask & 8) != 0)
            {
                item.m_crafterID = 123456789012L + mask;
                // 100 two-byte letters = 200 bytes: length needs two bytes in the string header.
                item.m_crafterName = mask % 2 == 0 ? "Odin" : new string('é', 100);
            }
            if ((mask & 32) != 0)
            {
                // 130 pairs: count needs two bytes. Else a few, one with a long value.
                var pairs = mask % 4 == 0 ? 130 : 1 + mask % 3;
                for (var k = 0; k < pairs; k++)
                {
                    item.m_customData["MC.SelfTest.Key" + k] = k == 0 ? new string('x', 300) : "v" + k;
                }
            }
            if (item.m_cheated)
            {
                expectTagged++;
            }
            items.Add(item);
        }

        var holder = new Inventory("MCProbe", null, 8, 8);
        holder.m_inventory.AddRange(items);
        var pkg = new ZPackage();
        holder.Save(pkg);
        var blob = pkg.GetArray();
        var original = (byte[])blob.Clone();

        var result = ItemBlobs.Inventory(blob, true, out var cleaned, out var tagged);
        c.Check(result == BlobResult.Tagged && tagged == expectTagged && cleaned != null && !ReferenceEquals(cleaned, blob),
            $"64 items written by the game: {tagged} tagged found (expected {expectTagged}), a new array comes back ({result})");
        c.Check(Differences(blob, original, out _) == 0, "the array the game holds is not written to");
        var diff = Differences(cleaned, blob, out var onlyBit);
        c.Check(diff == expectTagged && onlyBit, $"the copy differs in exactly one bit per tagged item ({diff} bytes)");
        var back = cleaned != null ? ReadInventory(cleaned) : null;
        if (c.Check(back != null && back.Count == items.Count, "the game reads the cleaned blob to its last byte"))
        {
            var same = 0;
            var stillTagged = 0;
            for (var i = 0; i < items.Count; i++)
            {
                var a = items[i];
                var b = back[i].Value;
                var hash = a.m_dropPrefab != null ? a.m_dropPrefab.name.GetStableHashCode() : 0;
                var equal = (int)(a.m_durability * 100f) == (int)Math.Round(b.m_durability * 100f) && a.m_gridPos == b.m_gridPos
                            && a.m_worldLevel == b.m_worldLevel && a.m_pickedUp == b.m_pickedUp && a.m_equipped == b.m_equipped
                            && a.m_quality == b.m_quality && a.m_stack == b.m_stack && a.m_variant == b.m_variant
                            && a.m_crafterID == b.m_crafterID && a.m_crafterName == b.m_crafterName && back[i].Key == hash
                            && SameData(a.m_customData, b.m_customData);
                if (equal)
                {
                    same++;
                }
                if (b.m_cheated)
                {
                    stillTagged++;
                }
            }
            c.Check(same == items.Count, $"every other field of every item reads back the same ({same} of {items.Count})");
            c.Check(stillTagged == 0, $"no item is tagged any more ({stillTagged})");
        }

        result = ItemBlobs.Inventory(blob, false, out var notPatched, out var counted);
        c.Check(result == BlobResult.Tagged && counted == expectTagged && notPatched == null, "count only: same number, no copy made");
        result = ItemBlobs.Inventory(cleaned, true, out var again, out counted);
        c.Check(result == BlobResult.Clean && counted == 0 && again == null, "a clean blob stays as it is");

        // Other bits of the flags byte belong to the game: only bit 0 goes.
        var bits = (byte[])blob.Clone();
        bits[bits.Length - 1] = 0xFF;
        result = ItemBlobs.Inventory(bits, true, out var bitsCleaned, out _);
        c.Check(result == BlobResult.Tagged && bitsCleaned != null && bitsCleaned[bitsCleaned.Length - 1] == 0xFE,
            "the other seven bits of an item's flags byte are kept");

        // Formats me must not touch.
        c.Check(Kind(WithVersion(blob, 110)) == BlobResult.Unknown, "container layout 110 (newer than this game): left alone");
        c.Check(Kind(WithVersion(blob, 107)) == BlobResult.Unknown, "container layout 107 (older, other shape): left alone");
        c.Check(Kind(WithVersion(blob, 108)) == BlobResult.Clean, "container layout 108 (no tag in it): nothing to do");
        c.Check(Kind(WithVersion(blob, 106)) == BlobResult.Clean, "container layout 106 (no tag in it): nothing to do");
        c.Check(Kind(WithVersion(blob, -5)) == BlobResult.Unknown, "container with a nonsense version: left alone");
        var cut = new byte[blob.Length - 1];
        Array.Copy(blob, cut, cut.Length);
        c.Check(Kind(cut) == BlobResult.Unknown, "container blob cut one byte short: left alone");
        var longer = new byte[blob.Length + 1];
        Array.Copy(blob, longer, blob.Length);
        longer[blob.Length] = 1;
        c.Check(Kind(longer) == BlobResult.Unknown, "container blob with one byte too many: left alone");
        var more = (byte[])blob.Clone();
        more[4]++;
        c.Check(Kind(more) == BlobResult.Unknown, "container blob that says one item more than it holds: left alone");
        c.Check(Kind(new byte[] { 109, 0, 0 }) == BlobResult.Unknown && Kind(new byte[0]) == BlobResult.Clean && Kind(null) == BlobResult.Clean,
            "three bytes: left alone; empty or missing: nothing to do");

        // One item blobs (ground item, stands).
        var singleOk = 0;
        var singleTagged = 0;
        foreach (var item in items)
        {
            var one = new ZPackage();
            one.Write((byte)ItemBlobs.Layout);
            item.Save(one);
            var bytes = one.GetArray();
            var keep = (byte[])bytes.Clone();
            var r = ItemBlobs.Single(bytes, true, out var fixedBytes);
            if (Differences(bytes, keep, out _) != 0)
            {
                continue;
            }
            if (!item.m_cheated)
            {
                if (r == BlobResult.Clean && fixedBytes == null)
                {
                    singleOk++;
                }
                continue;
            }
            singleTagged++;
            var read = fixedBytes != null ? ReadSingle(fixedBytes, out _) : null;
            if (r == BlobResult.Tagged && read != null && !read.m_cheated && read.m_stack == item.m_stack
                && read.m_crafterName == item.m_crafterName && SameData(read.m_customData, item.m_customData)
                && Differences(fixedBytes, bytes, out var bit) == 1 && bit)
            {
                singleOk++;
            }
        }
        c.Check(singleOk == items.Count && singleTagged == expectTagged,
            $"one-item blobs: {singleOk} of {items.Count} right ({singleTagged} tagged ones cleaned by one bit, the others not touched)");

        var tag = new ZPackage();
        tag.Write((byte)ItemBlobs.Layout);
        TestItem("Wood", 3, true).Save(tag);
        var tagBytes = tag.GetArray();
        var old = (byte[])tagBytes.Clone();
        old[0] = 108;
        c.Check(ItemBlobs.Single(old, true, out var oldOut) == BlobResult.Clean && oldOut == null,
            "one-item blob of layout 108 (no tag byte, its last byte is data): nothing to do");
        old[0] = 107;
        c.Check(ItemBlobs.Single(old, true, out oldOut) == BlobResult.Unknown && oldOut == null, "one-item blob of layout 107: left alone");
        old[0] = 110;
        c.Check(ItemBlobs.Single(old, true, out oldOut) == BlobResult.Unknown && oldOut == null, "one-item blob of layout 110: left alone");
        var tail = new byte[tagBytes.Length + 1];
        Array.Copy(tagBytes, tail, tagBytes.Length);
        tail[tagBytes.Length] = 1;
        c.Check(ItemBlobs.Single(tail, true, out oldOut) == BlobResult.Unknown && oldOut == null,
            "one-item blob with a byte after the item: left alone");
        c.Check(ItemBlobs.Single(new byte[] { 109, 1 }, true, out oldOut) == BlobResult.Clean, "two bytes (the game ignores it too): nothing to do");

        // The words.
        var counts = new SweepCounts { Pieces = 1, Creatures = 2, Queues = 1, StoredItems = 5, Storages = 1, SingleItems = 1, CarriedItems = 3 };
        var list = Report.List(counts);
        c.Check(list == "1 building piece, 2 creatures, 1 station queue, 5 items in 1 chest or other storage, "
                + "1 item on the ground or on a stand, 3 items you carry", $"report list: '{list}'");
        var before = new SweepCounts { Pieces = 4, Creatures = 1, StoredItems = 6, Storages = 2, StoredItemsInUse = 4, StoragesInUse = 1 };
        var after = new SweepCounts { Creatures = 2, StoredItemsInUse = 4, StoragesInUse = 1 };
        var removed = Report.Removed(before, after, 2);
        c.Check(removed.Pieces == 4 && removed.Creatures == 0 && removed.StoredItems == 6 && removed.Storages == 2
                && removed.CarriedItems == 2 && removed.StoredItemsInUse == 0,
            $"removed = before minus after, never below zero, open chests not in ({Show(removed)})");
        after.InUsePlaces.Add(new Vector3(120.4f, 30f, -340.6f));
        var text = Report.Clear(removed, after, 1, 2, true);
        c.Check(text.StartsWith("Cheated tags removed: 4 building pieces, 6 items in 2 chests or other storage, 2 items you carry.", StringComparison.Ordinal)
                && text.Contains("Not removed: 4 items in 1 chest that a player has open right now, or that a player who lost the "
                                 + "connection left open (at 120 -341). Open and close that chest once, then type cheated_clear again.")
                && text.Contains("Still tagged: 2 creatures.") && text.Contains("1 player online has this mod: their games were asked")
                && text.Contains("2 players online do not have this mod") && text.EndsWith("Saving the world.", StringComparison.Ordinal),
            $"clear report: '{text.Replace('\n', '/')}'");
        c.Check(Report.Clear(new SweepCounts(), new SweepCounts(), 0, 0, false) == Report.NothingFound, "nothing found = one line");
        var local = Report.Local(counts, true, false);
        var other = Report.Local(counts, true, true);
        c.Check(local.Contains("The server does not have " + ModInfo.Name + " (or has it turned off)") && local.Contains("install the mod on the server too")
                && other.Contains("The server has another version of " + ModInfo.Name) && other.Contains("use the same version on the server and on your game")
                && !other.Contains("does not have"),
            "a game alone says why: no copy on the server, or another version there");
        var checkedText = Report.LocalChecked(Report.Removed(before, after, 0), after, false);
        c.Check(checkedText.StartsWith("Cheated tags removed: 4 building pieces, 6 items in 2 chests or other storage.", StringComparison.Ordinal)
                && checkedText.Contains("Still tagged around you: 2 creatures. Another player's game controls these objects right now, or they were just loaded."),
            $"a game alone, after it looked again: '{checkedText.Replace('\n', '/')}'");
        c.Report();
    }

    private static BlobResult Kind(byte[] inventoryBlob)
    {
        var keep = inventoryBlob != null ? (byte[])inventoryBlob.Clone() : null;
        var result = ItemBlobs.Inventory(inventoryBlob, true, out var cleaned, out var tagged);
        // Not Tagged = no copy, no count; the source is never written.
        if (result != BlobResult.Tagged && (cleaned != null || tagged != 0))
        {
            return BlobResult.Tagged;
        }
        return inventoryBlob != null && Differences(inventoryBlob, keep, out _) != 0 ? BlobResult.Tagged : result;
    }

    private static byte[] WithVersion(byte[] inventoryBlob, int version)
    {
        var copy = (byte[])inventoryBlob.Clone();
        copy[0] = (byte)version;
        copy[1] = (byte)(version >> 8);
        copy[2] = (byte)(version >> 16);
        copy[3] = (byte)(version >> 24);
        return copy;
    }

    private static bool SameData(Dictionary<string, string> a, Dictionary<string, string> b)
    {
        if (a == null || b == null || a.Count != b.Count)
        {
            return false;
        }
        foreach (var pair in a)
        {
            if (!b.TryGetValue(pair.Key, out var value) || value != pair.Value)
            {
                return false;
            }
        }
        return true;
    }

    // ---------- uncheat.world ----------

    private static IEnumerator RunWorld()
    {
        var c = new Checks(WorldName);
        var player = Player.m_localPlayer;
        var net = ZNet.instance;
        var console = global::Console.instance;
        if (!c.Check(player != null && net != null && net.IsServer() && console != null && Game.instance != null,
                "a local player in a world this game holds, with a console"))
        {
            c.Report();
            yield break;
        }
        var rig = new Rig(player);
        var lines = new List<string>();
        var profile = Game.instance.GetPlayerProfile();
        var usedCheats = profile.m_usedCheats;
        var cheatCount = profile.m_playerStats[0].m_stats[PlayerStatType.Cheats];
        ItemDrop.ItemData carried = null;
        Container open = null;
        try
        {
            yield return Until(() => !net.IsSaving() && !SweepJob.Running, 60f);

            var chestGo = rig.Spawn(ChestPrefab);
            var openGo = rig.Spawn(ChestPrefab);
            var floorGo = rig.Spawn(FloorPrefab);
            var cleanGo = rig.Spawn(FloorPrefab);
            var zeroGo = rig.Spawn(FloorPrefab);
            var stationGo = rig.Spawn(CookingPrefab);
            var boarGo = rig.Spawn(CreaturePrefab);
            var portalHashes = Game.instance.PortalPrefabHash;
            var portalGo = portalHashes.Count > 0 ? rig.Spawn(ZNetScene.instance.GetPrefab(portalHashes[0])) : null;
            var groundGo = rig.Spawn("Wood");
            var chest = chestGo != null ? chestGo.GetComponent<Container>() : null;
            open = openGo != null ? openGo.GetComponent<Container>() : null;
            var station = stationGo != null ? stationGo.GetComponent<CookingStation>() : null;
            var drop = groundGo != null ? groundGo.GetComponent<ItemDrop>() : null;
            if (!c.Check(chest != null && open != null && floorGo != null && cleanGo != null && zeroGo != null && station != null
                         && boarGo != null && boarGo.GetComponent<Character>() != null && portalGo != null && drop != null,
                    $"spawn '{ChestPrefab}' x2, '{FloorPrefab}' x3, '{CookingPrefab}', '{CreaturePrefab}', a portal and a Wood item"))
            {
                c.Report();
                yield break;
            }
            // Awake and Start of everything ran (a ground item saves itself in Start).
            yield return null;
            yield return null;
            var chestZdo = Zdo(chestGo);
            var openZdo = Zdo(openGo);
            var floorZdo = Zdo(floorGo);
            var cleanZdo = Zdo(cleanGo);
            var zeroZdo = Zdo(zeroGo);
            var stationZdo = Zdo(stationGo);
            var boarZdo = Zdo(boarGo);
            var portalZdo = Zdo(portalGo);
            var groundZdo = Zdo(groundGo);
            if (!c.Check(chestZdo != null && openZdo != null && floorZdo != null && cleanZdo != null && zeroZdo != null
                         && stationZdo != null && boarZdo != null && portalZdo != null && groundZdo != null, "every object has its saved data"))
            {
                c.Report();
                yield break;
            }

            // Closed chest: two tagged stacks (one with another mod's data, one crafted), one clean. Chest itself tagged.
            var inv = chest.GetInventory();
            var wood = inv.AddItem("Wood", 20, 1, 0, 0L, "", true);
            var stone = inv.AddItem("Stone", 5, 1, 0, 0L, "", false);
            var club = inv.AddItem("Club", 1, 3, 0, 4242L, "MCProbe crafter", true);
            // Chest open on this player (owner): its window holds the items, the game does not reload them.
            var openInv = open.GetInventory();
            var openWood = openInv.AddItem("Wood", 7, 1, 0, 0L, "", true);
            // Club never stacks: the item that comes back is the one in the inventory, and no stack of the player changes.
            carried = player.GetInventory().AddItem("Club", 1, 1, 0, 0L, "", true);
            if (!c.Check(wood != null && stone != null && club != null && openWood != null && carried != null,
                    "items go into both chests and into the player's inventory"))
            {
                c.Report();
                yield break;
            }
            wood.m_customData["MC.SelfTest.Mark"] = "kept";
            inv.Changed();
            open.SetInUse(true);
            chestZdo.Set(ZDOVars.s_cheated, true);

            // Piece, creature, portal: the object tag. Floor also carries an item like an item stand ("itemData") and
            // like an armor stand slot ("3_itemData").
            floorZdo.Set(ZDOVars.s_cheated, true);
            boarZdo.Set(ZDOVars.s_cheated, true);
            portalZdo.Set(ZDOVars.s_cheated, true);
            var standItem = TestItem("Wood", 3, true);
            standItem.m_customData["MC.SelfTest.Mark"] = "stand";
            ItemDrop.SaveToZDO(standItem, floorZdo);
            ItemDrop.SaveToZDO(standItem, floorZdo, 3);
            var slotKey = (3 + "_itemData").GetStableHashCode();

            // Cooking station: the game's own writer puts the slot tags.
            station.SetSlot(0, "RawMeat", 0f, CookingStation.Status.NotDone, true);
            station.SetSlot(1, "RawMeat", 0f, CookingStation.Status.NotDone, true);
            var queue = ZDOVars.s_cheatedQueued;
            c.Check(stationZdo.GetBool(queue) && stationZdo.GetBool(queue + 1),
                "the game keeps the tag of cooking slot n at key cheatedQueued + n");

            // Ground item like the console 'spawn' leaves it: tag in the item and on the object.
            drop.m_itemData.m_cheated = true;
            drop.m_itemData.m_stack = 4;
            drop.Save();
            groundZdo.Set(ZDOVars.s_cheated, true);

            // Things that must stay exactly as they are.
            cleanZdo.Set(queue + 2, true); // a number that looks like cooking slot 2, on an object with no slot 2
            var otherKey = "MC.SelfTest.Blob".GetStableHashCode();
            var otherBlob = (byte[])floorZdo.GetByteArray(ZDOVars.s_itemData).Clone();
            cleanZdo.Set(otherKey, otherBlob); // tagged item bytes under a key that is not an item key of the game
            var future = (byte[])chestZdo.GetByteArray(ZDOVars.s_items).Clone();
            future[0] = 110;
            cleanZdo.Set(ZDOVars.s_items, future); // container layout from a newer game
            zeroZdo.Set(ZDOVars.s_cheated, false); // key there, value 0: the normal vanilla leftover
            // Stands whose item was taken off: the bytes stay, the stand's item key says 0. Cleaned, never counted.
            ItemDrop.SaveToZDO(standItem, stationZdo);
            stationZdo.Set(ZDOVars.s_item, 0);
            ItemDrop.SaveToZDO(standItem, floorZdo, 5);
            floorZdo.Set(5 + "_item", 0);
            var leftSlotKey = (5 + "_itemData").GetStableHashCode();
            var cleanRevision = cleanZdo.DataRevision;
            var zeroRevision = zeroZdo.DataRevision;

            var chestBlob = chestZdo.GetByteArray(ZDOVars.s_items);
            var chestBefore = (byte[])chestBlob.Clone();
            c.Check(Tagged(chestBlob) == 2 && Tagged(openZdo.GetByteArray(ZDOVars.s_items)) == 1
                    && (floorZdo.GetByteArray(ZDOVars.s_itemData)[floorZdo.GetByteArray(ZDOVars.s_itemData).Length - 1] & 1) == 1
                    && (groundZdo.GetByteArray(ZDOVars.s_itemData)[groundZdo.GetByteArray(ZDOVars.s_itemData).Length - 1] & 1) == 1,
                "setup: the saved data holds the tags (2 in the closed chest, 1 in the open one, stand and ground item)");
            var mark = Localization.instance.Localize("$achievements_cheated_item_inventory");
            var bypass = PlayerProfile.s_bypassCheatChecks;
            if (bypass)
            {
                c.Note("cheat checks are bypassed on this profile: tooltips carry no cheated line");
            }
            else
            {
                c.Check(wood.GetTooltip().IndexOf(mark, StringComparison.Ordinal) >= 0 && carried.GetTooltip().IndexOf(mark, StringComparison.Ordinal) >= 0,
                    $"setup: tagged items show the cheated line ('{mark}')");
            }

            // ----- the command -----
            Output.TestLines = lines;
            var finished = SweepJob.Finished;
            // Spawning the portal marked the portal file already: take the mark off so the check below is about me.
            ZDOMan.instance.DirtyPortalObjects = false;
            console.TryRunCommand(ClearCommand.ClearName);
            var atOnce = SweepJob.Finished == finished + 1;
            c.Note($"single player: the whole clean-up ran inside the command call: {atOnce}");
            if (atOnce)
            {
                c.Check(ZDOMan.instance.DirtyPortalObjects, "the portal file is marked for the next save");
                c.Check(SweepJob.LastMarkedPortal && SweepJob.LastMarked >= 7,
                    $"every object that had a tag (7 made here) is marked for the save, the portal too ({SweepJob.LastMarked})");
                c.Check(stationZdo.GetString("slot1") == "RawMeat" && stationZdo.GetInt("slotstatus1") == 0,
                    "the cooking slot's own data is unchanged");
                // The marking alone, on objects nobody here wrote (what a player's game cleaned arrives like that).
                ZDOMan.instance.DirtyPortalObjects = false;
                var markedNow = WorldSweep.MarkForSave(new HashSet<ZDOID> { portalZdo.m_uid, chestZdo.m_uid }, out var portalMarked);
                c.Check(markedNow == 2 && portalMarked && ZDOMan.instance.DirtyPortalObjects,
                    "objects cleaned by another game can be marked for the save, the portal file included");
            }
            else
            {
                yield return Until(() => SweepJob.Finished > finished, 90f);
            }
            var removed = SweepJob.LastRemoved;
            var after = SweepJob.LastAfter;
            if (!c.Check(SweepJob.Finished > finished && removed != null && after != null, "the clean-up finished"))
            {
                c.Report();
                yield break;
            }
            c.Note($"removed: {Show(removed)}; left: {Show(after)}; said: {Show(lines)}");

            c.Check(!chestZdo.GetBool(ZDOVars.s_cheated) && !floorZdo.GetBool(ZDOVars.s_cheated) && !boarZdo.GetBool(ZDOVars.s_cheated)
                    && !portalZdo.GetBool(ZDOVars.s_cheated) && !groundZdo.GetBool(ZDOVars.s_cheated),
                "object tag gone: chest, floor piece, creature, portal, ground item");
            c.Check(!stationZdo.GetBool(queue) && !stationZdo.GetBool(queue + 1), "cooking slot tags gone (slot 0 and slot 1)");

            var chestAfter = chestZdo.GetByteArray(ZDOVars.s_items);
            var diff = Differences(chestAfter, chestBefore, out var onlyBit);
            c.Check(!ReferenceEquals(chestAfter, chestBlob) && diff == 2 && onlyBit,
                $"closed chest: a new saved array that differs from the old one in one bit per tagged stack ({diff} bytes)");
            var read = ReadInventory(chestAfter);
            c.Check(read != null && read.Count == 3 && Tagged(chestAfter) == 0, "closed chest: the game reads 3 stacks, none tagged");
            var openAfter = openZdo.GetByteArray(ZDOVars.s_items);
            c.Check(Tagged(openAfter) == 0 && !openWood.m_cheated, "chest open on its owner: saved data and the open window's items are clean");
            // The player moves an item in the open chest: the game writes the window's items back.
            openInv.Changed();
            c.Check(Tagged(openZdo.GetByteArray(ZDOVars.s_items)) == 0 && openInv.NrOfItems() == 1 && openWood.m_stack == 7,
                "open chest after an item move: still clean, stack unchanged");
            open.SetInUse(false);

            var standAfter = ReadSingle(floorZdo.GetByteArray(ZDOVars.s_itemData), out _);
            var slotAfter = ReadSingle(floorZdo.GetByteArray(slotKey), out _);
            c.Check(standAfter != null && !standAfter.m_cheated && standAfter.m_stack == 3
                    && standAfter.m_customData.TryGetValue("MC.SelfTest.Mark", out var standMark) && standMark == "stand",
                "item kept like on an item stand: clean, stack and other mod's data kept");
            c.Check(slotAfter != null && !slotAfter.m_cheated && slotAfter.m_stack == 3, "item kept like in armor stand slot 3: clean");
            var leftStand = ReadSingle(stationZdo.GetByteArray(ZDOVars.s_itemData), out _);
            var leftSlot = ReadSingle(floorZdo.GetByteArray(leftSlotKey), out _);
            c.Check(leftStand != null && !leftStand.m_cheated && leftSlot != null && !leftSlot.m_cheated
                    && SweepJob.LastBefore != null && SweepJob.LastBefore.LeftoverBlobs >= 2,
                $"bytes left on emptied stands are cleaned too, and counted apart, not as items ({SweepJob.LastBefore?.LeftoverBlobs})");
            var groundAfter = ReadSingle(groundZdo.GetByteArray(ZDOVars.s_itemData), out _);
            c.Check(groundAfter != null && !groundAfter.m_cheated && groundAfter.m_stack == 4 && !drop.m_itemData.m_cheated,
                "ground item: saved data and loaded object clean, stack 4");
            c.Check(!carried.m_cheated && player.GetInventory().ContainsItem(carried), "carried item: clean, still in the inventory");

            c.Check(cleanZdo.GetBool(queue + 2), "a number key that only looks like a cooking slot tag is not touched");
            c.Check(ReferenceEquals(cleanZdo.GetByteArray(otherKey), otherBlob) && (otherBlob[otherBlob.Length - 1] & 1) == 1,
                "item bytes under a key the game does not use for items are not touched");
            c.Check(ReferenceEquals(cleanZdo.GetByteArray(ZDOVars.s_items), future) && future[0] == 110 && after.UnknownBlobs >= 1,
                $"a container blob of a newer layout is not touched, and counted ({after.UnknownBlobs})");
            c.Check(!cleanZdo.GetBool(ZDOVars.s_cheated, out _) && cleanZdo.DataRevision == cleanRevision,
                "an object without the tag gets no key and is not marked changed");
            c.Check(zeroZdo.DataRevision == zeroRevision, "an object whose tag key is already 0 is not marked changed");

            c.Check(removed.Pieces >= 2 && removed.Creatures >= 1 && removed.Queues >= 2 && removed.StoredItems >= 3 && removed.Storages >= 2
                    && removed.SingleItems >= 3 && removed.CarriedItems >= 1,
                "the count covers at least what the test tagged (2 pieces, 1 creature, 2 queues, 3 items in 2 chests, 3 single items, 1 carried)");
            c.Check(after.World == 0 && after.StoragesInUse == 0 && SweepJob.LastRounds == 1,
                $"checked again: nothing left, one pass ({SweepJob.LastRounds})");
            c.Check(Any(lines, "Cheated tags removed: ") && lines.Contains("Checked again: no cheated tag is left in the world.")
                    && lines.Contains("Saving the world."), "the console says what was removed, that nothing is left and that it saves");
            c.Check(profile.m_usedCheats == usedCheats && profile.m_playerStats[0].m_stats[PlayerStatType.Cheats] == cheatCount,
                "the command did not mark the character as a cheater");
            if (!bypass)
            {
                c.Check(wood.GetTooltip().IndexOf(mark, StringComparison.Ordinal) < 0 && carried.GetTooltip().IndexOf(mark, StringComparison.Ordinal) < 0,
                    "the tooltips have no cheated line any more");
            }

            // Closed chest reloads from its saved data within a second.
            yield return new WaitForSeconds(1.6f);
            var reloaded = inv.GetAllItems();
            var kept = false;
            var crafted = false;
            var anyTag = false;
            foreach (var item in reloaded)
            {
                anyTag |= item.m_cheated;
                kept |= item.m_stack == 20 && item.m_customData.TryGetValue("MC.SelfTest.Mark", out var m) && m == "kept";
                crafted |= item.m_quality == 3 && item.m_crafterID == 4242L && item.m_crafterName == "MCProbe crafter";
            }
            c.Check(reloaded.Count == 3 && !anyTag && kept && crafted,
                "closed chest after its reload: 3 stacks, none tagged, the other mod's data and the crafter are kept");

            // Run it again: nothing to do. (Waits for the save the first run started.)
            lines.Clear();
            finished = SweepJob.Finished;
            console.TryRunCommand(ClearCommand.ClearName);
            yield return Until(() => SweepJob.Finished > finished, 90f);
            c.Check(SweepJob.Finished > finished && SweepJob.LastRemoved != null && SweepJob.LastRemoved.Everything == 0
                    && lines.Contains(Report.NothingFound), $"a second run finds nothing ({Show(SweepJob.LastRemoved)}; said: {Show(lines)})");
        }
        finally
        {
            Output.TestLines = null;
            if (open != null)
            {
                open.SetInUse(false);
            }
            if (carried != null && player != null)
            {
                player.GetInventory().RemoveItem(carried);
            }
            rig.DestroyAll();
        }
        c.Report();
    }

    // ---------- uncheat.scan ----------

    private static IEnumerator RunScan()
    {
        var c = new Checks(ScanName);
        var player = Player.m_localPlayer;
        var net = ZNet.instance;
        var console = global::Console.instance;
        if (!c.Check(player != null && net != null && net.IsServer() && console != null, "a local player in a world this game holds, with a console"))
        {
            c.Report();
            yield break;
        }
        var rig = new Rig(player);
        var lines = new List<string>();
        try
        {
            yield return Until(() => !net.IsSaving() && !SweepJob.Running, 60f);
            var chestGo = rig.Spawn(ChestPrefab);
            var floorGo = rig.Spawn(FloorPrefab);
            var chest = chestGo != null ? chestGo.GetComponent<Container>() : null;
            if (!c.Check(chest != null && floorGo != null, $"spawn '{ChestPrefab}' and '{FloorPrefab}'"))
            {
                c.Report();
                yield break;
            }
            yield return null;
            var chestZdo = Zdo(chestGo);
            var floorZdo = Zdo(floorGo);
            var wood = chest.GetInventory().AddItem("Wood", 9, 1, 0, 0L, "", true);
            if (!c.Check(chestZdo != null && floorZdo != null && wood != null, "tagged stack in the chest"))
            {
                c.Report();
                yield break;
            }
            floorZdo.Set(ZDOVars.s_cheated, true);
            var blob = chestZdo.GetByteArray(ZDOVars.s_items);
            var chestRevision = chestZdo.DataRevision;
            var floorRevision = floorZdo.DataRevision;

            Output.TestLines = lines;
            var finished = SweepJob.Finished;
            console.TryRunCommand(ClearCommand.ScanName);
            yield return Until(() => SweepJob.Finished > finished, 90f);
            var found = SweepJob.LastScan;
            if (!c.Check(SweepJob.Finished > finished && found != null, "the count finished"))
            {
                c.Report();
                yield break;
            }
            c.Note($"found: {Show(found)}; said: {Show(lines)}");
            c.Check(found.Pieces >= 1 && found.StoredItems >= 1 && found.Storages >= 1, "the count has the tagged piece and the tagged stack");
            c.Check(floorZdo.GetBool(ZDOVars.s_cheated) && wood.m_cheated && ReferenceEquals(chestZdo.GetByteArray(ZDOVars.s_items), blob)
                    && Tagged(blob) == 1, "nothing was changed: piece, loaded item and saved chest data still tagged");
            if (!PlayerProfile.s_bypassCheatChecks)
            {
                var mark = Localization.instance.Localize("$achievements_cheated_item_inventory");
                c.Check(wood.GetTooltip().IndexOf(mark, StringComparison.Ordinal) >= 0, "the stack's tooltip still has the cheated line");
            }
            c.Check(chestZdo.DataRevision == chestRevision && floorZdo.DataRevision == floorRevision, "no object was marked changed");
            c.Check(Any(lines, "Cheated tags found (nothing was changed): ") && lines.Contains("Type " + ClearCommand.ClearName + " to remove them."),
                "the console lists what it found and names the command that removes it");
        }
        finally
        {
            Output.TestLines = null;
            rig.DestroyAll();
        }
        c.Report();
    }

    // ---------- uncheat.blocked ----------

    private static IEnumerator RunBlocked()
    {
        var c = new Checks(BlockedName);
        var player = Player.m_localPlayer;
        var console = global::Console.instance;
        var plugin = Plugin.Instance;
        if (!c.Check(player != null && console != null && plugin != null && ZNet.instance != null, "a local player, a console and the plugin"))
        {
            c.Report();
            yield break;
        }
        var rig = new Rig(player);
        var lines = new List<string>();
        try
        {
            c.Check(Terminal.commands.TryGetValue(ClearCommand.ClearName, out var clear) && !clear.IsCheat
                    && Terminal.commands.TryGetValue(ClearCommand.ScanName, out var scan) && !scan.IsCheat,
                "both commands exist and are not cheat commands");
            var floorGo = rig.Spawn(FloorPrefab);
            yield return null;
            var floorZdo = Zdo(floorGo);
            if (!c.Check(floorZdo != null, $"spawn '{FloorPrefab}'"))
            {
                c.Report();
                yield break;
            }
            floorZdo.Set(ZDOVars.s_cheated, true);

            // Copy that cannot run (same state as a mod that failed to start): command stays, does nothing, says why.
            Plugin.TestBlocked = "Inactive: blocked by the self test.";
            FeatureRegistry.RefreshAll();
            Output.TestLines = lines;
            var finished = SweepJob.Finished;
            console.TryRunCommand(ClearCommand.ClearName);
            yield return null;
            c.Check(!plugin.IsActive && Any(lines, ModInfo.Name + " is not running. Inactive: blocked by the self test."),
                $"blocked copy answers that it is not running ({Show(lines)})");
            c.Check(floorZdo.GetBool(ZDOVars.s_cheated) && SweepJob.Finished == finished && !SweepJob.Running, "and changes nothing");

            Plugin.TestBlocked = null;
            FeatureRegistry.RefreshAll();
            c.Check(plugin.IsActive && Terminal.commands.ContainsKey(ClearCommand.ClearName), "unblocked: active again, command still there");
        }
        finally
        {
            Output.TestLines = null;
            Plugin.TestBlocked = null;
            FeatureRegistry.RefreshAll();
            rig.DestroyAll();
        }
        c.Report();
    }
}
#endif
