using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.ViewDistantHorizonsMod;

internal sealed class ZoneObjects
{
    public readonly List<ObjectInstance> Items = new List<ObjectInstance>();
    // A _ZoneCtrl object exist = server generated this zone at some point.
    public bool Generated;
    public int ContentHash;
    public float ScannedAt;
    // Length of live sector list at scan time; cheap way to notice objects created since.
    public int LiveCount;
}

// Me read objects of a zone from object store (ZDOs) this client hold. On host or in single player = whole
// generated world; on dedicated-server client = every area player been near this session. Main thread only;
// ZDOs pooled, so me snapshot everything.
internal sealed class ZoneObjectSource
{
    private const int SectorWidth = 512;
    private const int SectorHalf = 256;

    private static readonly List<ZDO> s_empty = new List<ZDO>();

    private readonly Dictionary<int, ZoneObjects> _zones = new Dictionary<int, ZoneObjects>();
    private readonly List<int> _keys = new List<int>();
    private readonly HashSet<int> _dirty = new HashSet<int>();
    private Action<ZDO> _destroyHandler;
    private ZDOMan _hooked;

    public int CachedZones => _zones.Count;
    public int KeyCount => _keys.Count;
    public int KeyAt(int i) => _keys[i];
    public int Scans { get; private set; }

    public static int ZoneKey(int x, int y) => (y + 512) * 1024 + (x + 512);

    public static void Decode(int key, out int x, out int y)
    {
        x = key % 1024 - 512;
        y = key / 1024 - 512;
    }

    public void Hook()
    {
        ZDOMan man = ZDOMan.instance;
        if (man == null || ReferenceEquals(_hooked, man)) return;
        Unhook();
        _destroyHandler = OnDestroyed;
        man.m_onZDODestroyed += _destroyHandler;
        _hooked = man;
    }

    public void Unhook()
    {
        if (_hooked != null && _destroyHandler != null)
        {
            try
            {
                _hooked.m_onZDODestroyed -= _destroyHandler;
            }
            catch (Exception)
            {
                // ZDOMan maybe gone already
            }
        }
        _hooked = null;
        _destroyHandler = null;
    }

    private void OnDestroyed(ZDO zdo)
    {
        try
        {
            Vector2s s = zdo.GetSector();
            _dirty.Add(ZoneKey(s.x, s.y));
        }
        catch (Exception e)
        {
            // never let mod handler break game destroy path (it run before sector removal)
            PatchGuard.Report("ZoneObjectSource.OnDestroyed", e);
        }
    }

    public void MarkDirty(int zx, int zy) => _dirty.Add(ZoneKey(zx, zy));

    // Me move zones marked dirty by destroyed objects into the list into.
    public void TakeDirty(List<int> into)
    {
        if (_dirty.Count == 0) return;
        into.AddRange(_dirty);
        _dirty.Clear();
    }

    public void Clear()
    {
        _zones.Clear();
        _keys.Clear();
        _dirty.Clear();
    }

    public bool TryGetCached(int zx, int zy, out ZoneObjects zone)
    {
        return _zones.TryGetValue(ZoneKey(zx, zy), out zone);
    }

    // Live ZDO list of a sector (no keep references: ZDOs pooled).
    public IReadOnlyList<ZDO> EnumerateSector(int zx, int zy)
    {
        ZDOMan man = ZDOMan.instance;
        if (man == null) return s_empty;
        List<ZDO>[] sectors = man.m_objectsBySector;
        if (sectors == null) return s_empty;
        if (zx < -SectorHalf || zx >= SectorHalf || zy < -SectorHalf || zy >= SectorHalf) return s_empty;
        int index = (zy + SectorHalf) * SectorWidth + (zx + SectorHalf);
        if (index <= 0 || index >= sectors.Length) return s_empty;
        return sectors[index] ?? s_empty;
    }

    // Expiry jittered per zone, so zones scanned together never all expire together.
    public bool IsStale(int key, float now, float maxAge)
    {
        if (_dirty.Contains(key)) return true;
        if (!_zones.TryGetValue(key, out ZoneObjects z)) return true;
        float jitter = 0.75f + 0.5f * (((uint)key * 2654435761u) >> 16 & 0xFFFF) / 65536f;
        return now - z.ScannedAt >= maxAge * jitter;
    }

    // Me give zone objects; zone unknown = me scan store first.
    public ZoneObjects Get(int zx, int zy, float now)
    {
        int key = ZoneKey(zx, zy);
        if (_zones.TryGetValue(key, out ZoneObjects z)) return z;
        z = new ZoneObjects();
        _zones[key] = z;
        _keys.Add(key);
        Scan(zx, zy, z, now);
        _dirty.Remove(key);
        return z;
    }

    // Me re-read a cached zone. Return true when its content changed.
    public bool Refresh(int key, float now)
    {
        if (!_zones.TryGetValue(key, out ZoneObjects z))
        {
            _dirty.Remove(key);
            return false;
        }
        int before = z.ContentHash;
        Decode(key, out int zx, out int zy);
        Scan(zx, zy, z, now);
        _dirty.Remove(key);
        return z.ContentHash != before;
    }

    private void Scan(int zx, int zy, ZoneObjects z, float now)
    {
        z.Items.Clear();
        z.Generated = false;
        z.ScannedAt = now;
        z.ContentHash = 0;
        Scans++;

        IReadOnlyList<ZDO> list = EnumerateSector(zx, zy);
        z.LiveCount = list.Count;
        int hash = 17;
        for (int i = 0; i < list.Count; i++)
        {
            ZDO zdo = list[i];
            if (zdo == null || !zdo.IsValid()) continue;
            int prefab = zdo.GetPrefab();
            if (prefab == PrefabCatalog.ZoneCtrlHash)
            {
                z.Generated = true;
                continue;
            }
            if (!zdo.Persistent) continue;
            PrefabInfo info = PrefabCatalog.Get(prefab);
            if (info.Kind == ObjectKind.Skip) continue;

            Vector3 pos = zdo.GetPosition();
            float scale = info.PrefabScale.x;
            if (info.SyncScale)
            {
                Vector3 v = zdo.GetVec3(ZDOVars.s_scaleHash, Vector3.zero);
                scale = v != Vector3.zero ? v.x : zdo.GetFloat(ZDOVars.s_scaleScalarHash, scale);
            }
            z.Items.Add(new ObjectInstance
            {
                PrefabHash = prefab,
                Position = pos,
                Rotation = zdo.GetRotation(),
                Scale = scale,
            });
            unchecked
            {
                hash = hash * 31 + prefab;
                hash = hash * 31 + Mathf.RoundToInt(pos.x * 4f);
                hash = hash * 31 + Mathf.RoundToInt(pos.z * 4f);
            }
        }
        z.ContentHash = unchecked(hash * 31 + z.Items.Count);
    }
}
