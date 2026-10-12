using System;
using System.Collections.Generic;
using System.Diagnostics;
using MC.Shared;
using UnityEngine;

namespace MC.UX.CheatedTagRemoverMod;

// Which saved objects one pass look at.
internal enum SweepScope
{
    World,  // every object this game hold. Only right where the world run (server, host, single player).
    Owned,  // loaded objects this game own: its writes always win. Client helping the server.
    Loaded, // loaded objects around the player, owned or not. Client alone on a server without me.
}

// What one pass found (scan) or wrote (clear). Tagged chest a player has open go in the InUse pair, never written.
internal sealed class SweepCounts
{
    internal const int MaxPlaces = 5;

    internal int Pieces;
    internal int Creatures;
    internal int OtherObjects;
    internal int Queues;
    internal int StoredItems;
    internal int Storages;
    internal int SingleItems;
    internal int CarriedItems;
    internal int StoredItemsInUse;
    internal int StoragesInUse;
    internal int UnknownBlobs;
    // Tagged bytes left on a stand after its item was taken off (stand say "empty", bytes stay): cleaned, not counted.
    internal int LeftoverBlobs;
    // Writes on objects this game does not own (their owner's game can undo them): the caller check again later.
    internal int WrittenNotOwned;
    // Where the first few skipped open chests stand, for the answer.
    internal readonly List<Vector3> InUsePlaces = new List<Vector3>();
    internal long Milliseconds;

    internal int Objects => Pieces + Creatures + OtherObjects;

    // Tags in saved world data that a pass can write (open chests not in).
    internal int World => Objects + Queues + StoredItems + SingleItems;

    internal int Everything => World + StoredItemsInUse + CarriedItems;
}

// Me = the pass over saved object data (ZDO). Tag live in four places (design doc 1):
//   int "cheated"                 any object: piece, creature, station, rock...
//   int "cheatedQueued" (+ slot)  smelter, fermenter queue; cooking station one per slot
//   bytes "items"                 chest, cart, ship hold, tombstone: every item record has own bit
//   bytes "itemData" / "<n>_itemData"   item on ground, fish, item stand; armor stand slot
// Rules me never break (each one = lost data or tag that come back, see design doc 3):
//   - write with ZDO.Set only: Remove* is not saved, not sent, and other games put the key back;
//   - write only where a tag is: Set on a clean object add a key, dirty its save chunk, send it to everyone;
//   - bytes: new array each time (game hand out its own array; same array set again = nothing happen);
//   - never take ownership: chest open on old owner stay stuck "in use";
//   - chest open on another player: hands off, count it (their game write its own copy back at next item move);
//   - collect first, write after: no table change while me walk it. One frame, main thread, no save running.
internal static class WorldSweep
{
    // Cooking station slot n keep its queue tag at key "cheatedQueued" + n (number added to the hash). Vanilla has 5
    // at most; me look wide and ask for the slot's own "slotstatus<n>" key before me believe a key above slot 0.
    internal const int MaxSlots = 32;
    // Armor stand slot n keep its item at "<n>_itemData". Vanilla own save converter know 14.
    internal const int MaxStandSlots = 32;

    private const byte KindOther = 0;
    private const byte KindPiece = 1;
    private const byte KindCreature = 2;

    private struct IntHit
    {
        internal ZDOID Id;
        internal int Key;
        internal bool Queue;
    }

    private struct BlobHit
    {
        internal ZDOID Id;
        internal int Key;
        internal byte[] Old;
        internal byte[] New;
        internal int Items;
        internal bool Storage;
        internal bool Leftover;
    }

    private static readonly List<IntHit> IntHits = new List<IntHit>();
    private static readonly List<BlobHit> BlobHits = new List<BlobHit>();
    private static readonly Dictionary<int, byte> KindByPrefab = new Dictionary<int, byte>();
    private static int[] _slotStatusKeys;
    // Armor stand: "<n>_itemData" (bytes) -> "<n>_item" (int, prefab of the item on that slot, 0 = empty).
    private static Dictionary<int, int> _standKeys;
    private static int _unknown;

    // write false = count only, nothing change. Counts add to c. seen (optional) get the id of every object with a
    // tag, open chests too: the caller mark them for saving later (MarkForSave).
    internal static void Run(SweepScope scope, bool write, SweepCounts c, HashSet<ZDOID> seen = null)
    {
        var man = ZDOMan.instance;
        if (man == null || c == null)
        {
            return;
        }
        var watch = Stopwatch.StartNew();
        EnsureKeys();
        IntHits.Clear();
        BlobHits.Clear();
        _unknown = 0;
        try
        {
            if (scope == SweepScope.World)
            {
                foreach (var pair in ZDOExtraData.s_ints)
                {
                    ScanInts(pair.Key, pair.Value);
                }
                foreach (var pair in ZDOExtraData.s_byteArrays)
                {
                    ScanBlobs(pair.Key, pair.Value, write);
                }
            }
            else
            {
                var scene = ZNetScene.instance;
                if (scene != null)
                {
                    foreach (var pair in scene.m_instances)
                    {
                        var zdo = pair.Key;
                        if (zdo == null || !zdo.IsValid() || (scope == SweepScope.Owned && !zdo.IsOwner()))
                        {
                            continue;
                        }
                        if (ZDOExtraData.s_ints.TryGetValue(zdo.m_uid, out var ints))
                        {
                            ScanInts(zdo.m_uid, ints);
                        }
                        if (ZDOExtraData.s_byteArrays.TryGetValue(zdo.m_uid, out var blobs))
                        {
                            ScanBlobs(zdo.m_uid, blobs, write);
                        }
                    }
                }
            }
            Apply(scope, write, c, man, seen);
        }
        finally
        {
            // Hits hold blob arrays: let them go.
            IntHits.Clear();
            BlobHits.Clear();
            c.UnknownBlobs += _unknown;
            c.Milliseconds += watch.ElapsedMilliseconds;
        }
    }

    // Game that hold the world: mark these objects for the next save. Needed for what a PLAYER's game cleaned: data
    // that come in from another game is taken into the tables but mark nothing (ZDOMan.RPC_ZDOData: no dirty chunk,
    // no portal flag), and a save only write marked chunks. Without this a cleaned object can be tagged again after
    // a restart. Marks only: no data change, nothing sent. Return how many marked; portal = portal file marked.
    internal static int MarkForSave(HashSet<ZDOID> ids, out bool portal)
    {
        portal = false;
        var man = ZDOMan.instance;
        if (man == null || ids == null)
        {
            return 0;
        }
        var marked = 0;
        foreach (var id in ids)
        {
            var zdo = man.GetZDO(id);
            if (zdo == null || !zdo.IsValid())
            {
                continue;
            }
            marked++;
            if (IsPortal(zdo))
            {
                portal = true;
            }
            else
            {
                man.SetDirtySector(zdo);
            }
        }
        if (portal)
        {
            man.SetDirtyPortals();
        }
        return marked;
    }

    private static void EnsureKeys()
    {
        if (_slotStatusKeys != null)
        {
            return;
        }
        var status = new int[MaxSlots];
        for (var i = 0; i < MaxSlots; i++)
        {
            status[i] = ("slotstatus" + i).GetStableHashCode();
        }
        var stands = new Dictionary<int, int>();
        for (var i = 0; i < MaxStandSlots; i++)
        {
            stands[(i + "_itemData").GetStableHashCode()] = (i + "_item").GetStableHashCode();
        }
        _standKeys = stands;
        _slotStatusKeys = status;
    }

    // Keys of one object sit sorted in two arrays: one binary search each, no garbage.
    private static void ScanInts(ZDOID id, BinarySearchDictionary<int, int> ints)
    {
        if (ints == null)
        {
            return;
        }
        int n = ints.m_length;
        var keys = ints.m_keys;
        var values = ints.m_values;
        if (n == 0 || keys == null || values == null)
        {
            return;
        }
        var cheated = ZDOVars.s_cheated;
        var i = LowerBound(keys, n, cheated);
        if (i < n && keys[i] == cheated && values[i] != 0)
        {
            IntHits.Add(new IntHit { Id = id, Key = cheated, Queue = false });
        }
        var queue = ZDOVars.s_cheatedQueued;
        for (i = LowerBound(keys, n, queue); i < n; i++)
        {
            var slot = (long)keys[i] - queue;
            if (slot >= MaxSlots)
            {
                break;
            }
            if (values[i] == 0)
            {
                continue;
            }
            // Slot 0 = the key smelter and fermenter use too. Higher = cooking slot only when that slot exist here.
            if (slot == 0 || HasKey(keys, n, _slotStatusKeys[slot]))
            {
                IntHits.Add(new IntHit { Id = id, Key = keys[i], Queue = true });
            }
        }
    }

    private static void ScanBlobs(ZDOID id, BinarySearchDictionary<int, byte[]> blobs, bool write)
    {
        if (blobs == null)
        {
            return;
        }
        int n = blobs.m_length;
        var keys = blobs.m_keys;
        var values = blobs.m_values;
        if (n == 0 || keys == null || values == null)
        {
            return;
        }
        for (var i = 0; i < n; i++)
        {
            var key = keys[i];
            var data = values[i];
            if (key == ZDOVars.s_items)
            {
                var result = ItemBlobs.Inventory(data, write, out var cleaned, out var tagged);
                if (result == BlobResult.Tagged)
                {
                    BlobHits.Add(new BlobHit { Id = id, Key = key, Old = data, New = cleaned, Items = tagged, Storage = true });
                }
                else if (result == BlobResult.Unknown)
                {
                    _unknown++;
                }
                continue;
            }
            var itemKey = 0;
            if (key == ZDOVars.s_itemData)
            {
                itemKey = ZDOVars.s_item;
            }
            else if (!_standKeys.TryGetValue(key, out itemKey))
            {
                continue;
            }
            var single = ItemBlobs.Single(data, write, out var fixedBytes);
            if (single == BlobResult.Tagged)
            {
                BlobHits.Add(new BlobHit
                {
                    Id = id, Key = key, Old = data, New = fixedBytes, Items = 1, Storage = false,
                    Leftover = EmptyStand(id, itemKey),
                });
            }
            else if (single == BlobResult.Unknown)
            {
                _unknown++;
            }
        }
    }

    // Stand whose item was taken off keep the item's bytes and write 0 in its item key (ItemStand.DropItem,
    // ArmorStand.DropItem): that item now lie on the ground with its own bytes. Key there and 0 = leftover. No such
    // key at all = ground item or fish (they never have it).
    private static bool EmptyStand(ZDOID id, int itemKey)
    {
        return ZDOExtraData.s_ints.TryGetValue(id, out var ints) && ints != null
               && ints.TryGetValue(itemKey, out var item) && item == 0;
    }

    private static void Apply(SweepScope scope, bool write, SweepCounts c, ZDOMan man, HashSet<ZDOID> seen)
    {
        var net = ZNet.instance;
        var dirtyPortal = false;
        foreach (var hit in IntHits)
        {
            var zdo = man.GetZDO(hit.Id);
            if (zdo == null || !zdo.IsValid() || zdo.GetInt(hit.Key) == 0)
            {
                continue;
            }
            seen?.Add(hit.Id);
            if (hit.Queue)
            {
                c.Queues++;
            }
            else
            {
                switch (Kind(zdo))
                {
                    case KindPiece:
                        c.Pieces++;
                        break;
                    case KindCreature:
                        c.Creatures++;
                        break;
                    default:
                        c.OtherObjects++;
                        break;
                }
            }
            if (!write)
            {
                continue;
            }
            zdo.Set(hit.Key, false);
            dirtyPortal |= IsPortal(zdo);
            if (!zdo.IsOwner())
            {
                c.WrittenNotOwned++;
            }
        }
        foreach (var hit in BlobHits)
        {
            var zdo = man.GetZDO(hit.Id);
            // Same array still there = nothing moved since me read it (always, same frame; cheap to be sure).
            if (zdo == null || !zdo.IsValid() || !ReferenceEquals(zdo.GetByteArray(hit.Key), hit.Old))
            {
                continue;
            }
            seen?.Add(hit.Id);
            if (hit.Storage && OpenOnOtherPlayer(scope, zdo, net))
            {
                c.StoragesInUse++;
                c.StoredItemsInUse += hit.Items;
                if (c.InUsePlaces.Count < SweepCounts.MaxPlaces)
                {
                    c.InUsePlaces.Add(zdo.GetPosition());
                }
                continue;
            }
            if (hit.Storage)
            {
                c.Storages++;
                c.StoredItems += hit.Items;
            }
            else if (hit.Leftover)
            {
                c.LeftoverBlobs++;
            }
            else
            {
                c.SingleItems++;
            }
            if (!write || hit.New == null)
            {
                continue;
            }
            zdo.Set(hit.Key, hit.New);
            dirtyPortal |= IsPortal(zdo);
            // Leftover bytes are no item: cleaning them on another game's stand need no second look.
            if (!hit.Leftover && !zdo.IsOwner())
            {
                c.WrittenNotOwned++;
            }
        }
        if (dirtyPortal)
        {
            // Portals save in own chunk, and ZDO.Set no mark that one dirty.
            man.SetDirtyPortals();
        }
    }

    // Chest window open on some other game: that game hold the items in memory and write them back at next move.
    // "InUse" is written by the chest's owner only, and nobody take it back when that player's game dies with the
    // window open. On the server: owner gone and nobody near = no connected owner, me write. Owner gone and another
    // player's game got the chest = still 1 with a connected owner: me cannot tell it from a real open window, so me
    // skip it too and the answer say so (open and close it once).
    private static bool OpenOnOtherPlayer(SweepScope scope, ZDO zdo, ZNet net)
    {
        if (zdo.GetInt(ZDOVars.s_inUse) != 1)
        {
            return false;
        }
        if (scope != SweepScope.World)
        {
            return !zdo.IsOwner();
        }
        var owner = zdo.GetOwner();
        return owner != 0L && owner != ZDOMan.GetSessionID() && net != null && net.GetPeer(owner) != null;
    }

    private static bool IsPortal(ZDO zdo)
    {
        var game = Game.instance;
        return game != null && game.PortalPrefabHash.Contains(zdo.GetPrefab());
    }

    // For the report only. Prefab unknown here (mod gone) = other.
    private static byte Kind(ZDO zdo)
    {
        var hash = zdo.GetPrefab();
        if (KindByPrefab.TryGetValue(hash, out var kind))
        {
            return kind;
        }
        var scene = ZNetScene.instance;
        if (scene == null)
        {
            return KindOther;
        }
        kind = KindOther;
        try
        {
            var prefab = scene.GetPrefab(hash);
            if (prefab != null)
            {
                if (prefab.GetComponent<Character>() != null)
                {
                    kind = KindCreature;
                }
                else if (prefab.GetComponent<Piece>() != null)
                {
                    kind = KindPiece;
                }
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("WorldSweep.Kind", e);
        }
        KindByPrefab[hash] = kind;
        return kind;
    }

    private static int LowerBound(int[] keys, int n, int value)
    {
        var lo = 0;
        var hi = n;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (keys[mid] < value)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }
        return lo;
    }

    private static bool HasKey(int[] keys, int n, int key)
    {
        var i = LowerBound(keys, n, key);
        return i < n && keys[i] == key;
    }
}
