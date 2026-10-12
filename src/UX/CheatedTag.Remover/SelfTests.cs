using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;
#endif

namespace MC.UX.CheatedTagRemoverMod;

// Me = self tests (Debug build only; Release build has empty methods and no test code). Run by
// tools/Test-InWorld.ps1 (single player world) and tools/Test-Multiplayer.ps1 (dedicated server + this game).
//   uncheat.blob      byte work alone: blobs written by the game's own writer, read back by the game's own reader
//   uncheat.world     single player: real objects tagged every way, the real command, what is left after
//   uncheat.scan      count only: nothing change
//   uncheat.blocked   copy that cannot run answer so and touch nothing; commands are no cheat commands
//   uncheat.mp.sweep  dedicated server: admin check, far objects of the server, own side of the player, and the
//                     server's write on an object a player without me own
//   uncheat.mp.contested  dedicated server: chest open on another player is left alone and named, object another
//                     player keep writing is tried three times and named
//   uncheat.mp.local  server without me: own items and loaded area only, said so
// Tests tag only what they make, and destroy it after. uncheat.world run the real command: it clean the whole
// throwaway world of the run (creatures the god mode probe player hit before carry the tag too).
internal static partial class SelfTests
{
    // BindConfig: test that must exist while me is inactive (server without me).
    [Conditional("DEBUG")]
    internal static void RegisterAlways()
    {
#if DEBUG
        SelfTest.RegisterMultiplayer(MpLocalName, VanillaServer, RunMpLocal);
#endif
    }

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        SelfTest.Register(BlobName, RunBlob);
        SelfTest.Register(WorldName, RunWorld);
        SelfTest.Register(ScanName, RunScan);
        SelfTest.Register(BlockedName, RunBlocked);
        SelfTest.RegisterMultiplayer(MpSweepName, SelfTest.Modded, RunMpSweep);
        SelfTest.RegisterMultiplayer(MpContestedName, SelfTest.Modded, RunMpContested);
        SelfTest.RegisterServerStep(ServerStepName, ServerStep);
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        SelfTest.Unregister(BlobName);
        SelfTest.Unregister(WorldName);
        SelfTest.Unregister(ScanName);
        SelfTest.Unregister(BlockedName);
        SelfTest.UnregisterMultiplayer(MpSweepName);
        SelfTest.UnregisterMultiplayer(MpContestedName);
        SelfTest.UnregisterServerStep(ServerStepName);
#endif
    }

#if DEBUG
    private const string BlobName = "uncheat.blob";
    private const string WorldName = "uncheat.world";
    private const string ScanName = "uncheat.scan";
    private const string BlockedName = "uncheat.blocked";
    private const string MpSweepName = "uncheat.mp.sweep";
    private const string MpContestedName = "uncheat.mp.contested";
    private const string MpLocalName = "uncheat.mp.local";
    private const string ServerStepName = "uncheat.mp.server";
    private const string VanillaServer = "vanilla-server";

    // Names used by other MC self tests on this game version (1.0.17).
    private const string ChestPrefab = "piece_chest_wood";
    private const string FloorPrefab = "wood_floor";
    private const string CookingPrefab = "piece_cookingstation";
    private const string CreaturePrefab = "Boar";

    private sealed class Checks
    {
        private readonly string _name;
        private readonly List<string> _failures = new List<string>();
        private int _count;

        internal Checks(string name) => _name = name;

        internal bool Check(bool ok, string what)
        {
            _count++;
            if (!ok)
            {
                _failures.Add(what);
            }
            return ok;
        }

        internal void Note(string detail) => SelfTest.Note(_name, detail);

        internal void Report()
        {
            if (_failures.Count == 0)
            {
                SelfTest.Pass(_name, $"{_count} checks OK");
            }
            else
            {
                SelfTest.Fail(_name, $"{_failures.Count} of {_count} checks failed: {string.Join("; ", _failures.ToArray())}");
            }
        }
    }

    // What a test put in the world, in a row beside the player. All gone at DestroyAll.
    private sealed class Rig
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly Vector3 _origin;
        private readonly Vector3 _right;
        private readonly Vector3 _fwd;
        private int _next;

        internal Rig(Player player)
        {
            _origin = player.transform.position;
            _right = player.transform.right;
            _fwd = player.transform.forward;
        }

        // Networked prefab by name (piece, creature) or item prefab.
        internal GameObject Spawn(string prefabName)
        {
            var scene = ZNetScene.instance;
            return Spawn(scene != null ? scene.GetPrefab(prefabName) : null);
        }

        internal GameObject Spawn(GameObject prefab)
        {
            if (prefab == null)
            {
                return null;
            }
            var pos = _origin + _fwd * 5f + _right * (2.5f * (_next - 3));
            _next++;
            pos.y = ZoneSystem.instance.GetGroundHeight(pos) + 0.1f;
            var go = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);
            _spawned.Add(go);
            // Support off: piece never break and spill. Creature stand still.
            foreach (var wnt in go.GetComponentsInChildren<WearNTear>(true))
            {
                wnt.enabled = false;
            }
            foreach (var ai in go.GetComponentsInChildren<BaseAI>(true))
            {
                ai.enabled = false;
            }
            return go;
        }

        internal void DestroyAll()
        {
            var scene = ZNetScene.instance;
            foreach (var go in _spawned)
            {
                if (go == null)
                {
                    continue;
                }
                var nview = go.GetComponent<ZNetView>();
                if (scene != null && nview != null && nview.IsValid())
                {
                    nview.ClaimOwnership();
                    scene.Destroy(go);
                }
                else
                {
                    UnityEngine.Object.Destroy(go);
                }
            }
            _spawned.Clear();
        }
    }

    private static ZDO Zdo(GameObject go)
    {
        var nview = go != null ? go.GetComponent<ZNetView>() : null;
        return nview != null && nview.IsValid() ? nview.GetZDO() : null;
    }

    private static IEnumerator Until(Func<bool> done, float seconds)
    {
        var end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end && !done())
        {
            yield return null;
        }
    }

    // Item like the game make it, never in any inventory.
    private static ItemDrop.ItemData TestItem(string prefabName, int stack, bool cheated)
    {
        var db = ObjectDB.instance;
        return new ItemDrop.ItemData
        {
            m_dropPrefab = db != null ? db.GetItemPrefab(prefabName) : null,
            m_stack = stack,
            m_durability = 100f,
            m_cheated = cheated,
        };
    }

    // Items of a saved container blob, read by the game's own reader. Null = the game cannot read it to the end.
    private static List<KeyValuePair<int, ItemDrop.ItemData>> ReadInventory(byte[] blob)
    {
        try
        {
            var pkg = new ZPackage(blob);
            var version = (global::Version.Item)pkg.ReadInt();
            var count = pkg.ReadUShort();
            var items = new List<KeyValuePair<int, ItemDrop.ItemData>>();
            for (var i = 0; i < count; i++)
            {
                var (hash, item) = ItemDrop.ItemData.Load(pkg, version);
                items.Add(new KeyValuePair<int, ItemDrop.ItemData>(hash, item));
            }
            return pkg.GetPos() == blob.Length ? items : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // One item blob (ground item, stand), read by the game's own reader.
    private static ItemDrop.ItemData ReadSingle(byte[] blob, out int prefabHash)
    {
        prefabHash = 0;
        try
        {
            var pkg = new ZPackage(blob);
            var version = (global::Version.Item)pkg.ReadByte();
            var item = new ItemDrop.ItemData();
            prefabHash = ItemDrop.ItemData.Load(pkg, item, version);
            return pkg.GetPos() == blob.Length ? item : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static int Tagged(byte[] inventoryBlob)
    {
        var items = ReadInventory(inventoryBlob);
        if (items == null)
        {
            return -1;
        }
        var n = 0;
        foreach (var pair in items)
        {
            if (pair.Value.m_cheated)
            {
                n++;
            }
        }
        return n;
    }

    // Tagged items a loaded chest holds in memory right now. Never keep an item object over frames for this: a chest
    // reloads its items (new objects) whenever its saved data moved.
    private static int LoadedTags(Container container)
    {
        var n = 0;
        foreach (var item in container.GetInventory().GetAllItems())
        {
            if (item.m_cheated)
            {
                n++;
            }
        }
        return n;
    }

    // Bytes that differ, and whether every difference is bit 0 only.
    private static int Differences(byte[] a, byte[] b, out bool onlyTagBit)
    {
        onlyTagBit = true;
        if (a == null || b == null || a.Length != b.Length)
        {
            onlyTagBit = false;
            return -1;
        }
        var n = 0;
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] == b[i])
            {
                continue;
            }
            n++;
            if ((a[i] ^ b[i]) != 1)
            {
                onlyTagBit = false;
            }
        }
        return n;
    }

    private static bool Any(List<string> lines, string start)
    {
        foreach (var line in lines)
        {
            if (line.StartsWith(start, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static bool AnyHolds(List<string> lines, string part)
    {
        foreach (var line in lines)
        {
            if (line.IndexOf(part, StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    private static string Show(List<string> lines) => lines.Count == 0 ? "(nothing)" : string.Join(" / ", lines.ToArray());

    private static string Show(SweepCounts c)
    {
        return c == null
            ? "(none)"
            : $"pieces {c.Pieces}, creatures {c.Creatures}, other {c.OtherObjects}, queues {c.Queues}, stored {c.StoredItems} in "
              + $"{c.Storages}, single {c.SingleItems}, carried {c.CarriedItems}, open {c.StoredItemsInUse} in {c.StoragesInUse}, "
              + $"unknown {c.UnknownBlobs}";
    }
#endif
}
