using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MC.Combat.WeaponsDualWieldMod;

// Me = off-hand swing trails (design 2.7, E1). Vanilla VisEquipment.SetWeaponTrails only touch the right-hand
// weapon (unless m_useAllTrails). Me give the left one-handed weapon the same Emit value, for every player this game
// draw (visuals come from the synced left item hash, so any dual wielder, with or without the mod on their side).
// Cheap: players only, no graphics (dedicated server) = out, one-handed check cached per item hash, trail components
// cached per VisEquipment (entry replaced when its left instance change; dead entries pruned) so no
// GetComponentsInChildren alloc per swing like vanilla.
internal static class LeftTrails
{
    private const int PruneAbove = 32;

    private struct Entry
    {
        internal GameObject Instance;
        internal MeleeWeaponTrail[] Trails;
    }

    private static readonly Dictionary<VisEquipment, Entry> Cache = new Dictionary<VisEquipment, Entry>();
    private static readonly Dictionary<int, bool> OneHandedByHash = new Dictionary<int, bool>();
    private static readonly List<VisEquipment> Dead = new List<VisEquipment>();
    private static ObjectDB _db;
    private static int _noGraphics = -1;   // -1 = not checked yet

    // No graphics device (dedicated server): no visuals to touch. BackCross use me too.
    internal static bool NoGraphics
    {
        get
        {
            if (_noGraphics < 0)
            {
                _noGraphics = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ? 1 : 0;
            }
            return _noGraphics == 1;
        }
    }

    // SetWeaponTrails postfix. Turning off always pass (setting switched off mid-swing = no stuck trail).
    internal static void Apply(VisEquipment vis, bool enabled)
    {
        if (!vis.m_isPlayer || vis.m_useAllTrails || NoGraphics)
        {
            return;
        }
        if (enabled && (!TrailsWanted() || LocalSwingWithoutOffHand(vis)))
        {
            return;
        }
        var left = vis.m_leftItemInstance;
        if (left == null || !IsOneHanded(vis.m_currentLeftItemHash))
        {
            return;
        }
        var trails = TrailsOf(vis, left);
        for (var i = 0; i < trails.Length; i++)
        {
            if (trails[i] != null)
            {
                trails[i].Emit = enabled;
            }
        }
    }

    // Own player only (me know its swing): running attack that is not our converted pair swing (SecondaryMoves =
    // MainWeapon special, pair without valid moves) = off hand not strike, so no left trail. No running attack (direct
    // call) = pass. Other players: rule stay the left item hash (their swing unknown here). Two reference compares.
    private static bool LocalSwingWithoutOffHand(VisEquipment vis)
    {
        var player = Player.m_localPlayer;
        if (player == null || !ReferenceEquals(player.m_visEquipment, vis))
        {
            return false;
        }
        var attack = player.m_currentAttack;
        return attack != null && !attack.m_attackDone && !ReferenceEquals(attack, DualSwing.RecordedClone);
    }

    // LeftHandTrails setting (personal). Debug: self test force it in memory, never in the config file.
    private static bool TrailsWanted()
    {
#if DEBUG
        if (TestLeftHandTrails.HasValue)
        {
            return TestLeftHandTrails.Value;
        }
#endif
        return Plugin.LeftHandTrails != null && Plugin.LeftHandTrails.Value;
    }

#if DEBUG
    // Self test: LeftHandTrails forced (null = real setting).
    internal static bool? TestLeftHandTrails;

    // Self test: trail array cached for this player (null = none yet), to see the second call reuse it.
    internal static MeleeWeaponTrail[] CachedTrails(VisEquipment vis) =>
        vis != null && Cache.TryGetValue(vis, out var entry) ? entry.Trails : null;
#endif

    private static bool IsOneHanded(int hash)
    {
        var db = ObjectDB.instance;
        if (db == null)
        {
            return false;
        }
        if (!ReferenceEquals(db, _db))
        {
            _db = db;
            OneHandedByHash.Clear();
        }
        if (OneHandedByHash.TryGetValue(hash, out var known))
        {
            return known;
        }
        var prefab = db.GetItemPrefab(hash);
        var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
        var oneHanded = drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null
                        && drop.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon;
        OneHandedByHash[hash] = oneHanded;
        return oneHanded;
    }

    private static MeleeWeaponTrail[] TrailsOf(VisEquipment vis, GameObject left)
    {
        if (Cache.TryGetValue(vis, out var entry) && ReferenceEquals(entry.Instance, left))
        {
            return entry.Trails;
        }
        if (Cache.Count > PruneAbove)
        {
            Prune();
        }
        entry = new Entry { Instance = left, Trails = left.GetComponentsInChildren<MeleeWeaponTrail>() };
        Cache[vis] = entry;
        return entry.Trails;
    }

    // Players gone (VisEquipment destroyed): drop their entries.
    private static void Prune()
    {
        Dead.Clear();
        foreach (var pair in Cache)
        {
            if (pair.Key == null || pair.Value.Instance == null)
            {
                Dead.Add(pair.Key);
            }
        }
        foreach (var key in Dead)
        {
            Cache.Remove(key);
        }
        Dead.Clear();
    }

    // OnDeactivated: left trails still emitting turned off, caches forgotten.
    internal static void Clear()
    {
        foreach (var pair in Cache)
        {
            if (pair.Key == null || pair.Value.Instance == null || pair.Value.Trails == null)
            {
                continue;
            }
            foreach (var trail in pair.Value.Trails)
            {
                if (trail != null)
                {
                    trail.Emit = false;
                }
            }
        }
        Cache.Clear();
        OneHandedByHash.Clear();
        Dead.Clear();
        _db = null;
    }
}
