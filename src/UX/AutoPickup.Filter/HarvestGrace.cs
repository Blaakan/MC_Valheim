using System.Collections.Generic;
using UnityEngine;

namespace MC.UX.AutoPickupFilterMod;

// Me remember where local player just harvested or collected by hand (Use on bush, station, stand; scythe swing).
// Those drops only reach inventory through auto pickup, but they are "manual pickup" for the player: filter must
// let them in. Me decide ONCE, when drop is born (ItemDrop.Awake postfix): born near such a spot, a little after it
// = tagged, and tag stay until drop object die (picked up, despawn, unload). No later guess from position or time,
// so drop far away that player walk to much later still count, and mode/list change never take tag away.
// Ring of 64 spots, no allocation. One spot per frame: whole scythe swing (every plant, same frame) = one box.
internal static class HarvestGrace
{
    // Only spots of last 3 s matter (spawn window). Held E make at most ~5 new spots per second, one swing = one.
    private const int Capacity = 64;

    // Drop may spawn up to this long after last press (network delay when other client own the bush).
    private const float WindowSeconds = 3f;

    // Drop spawn time may be a hair before press time (same frame order).
    private const float EarlySeconds = 0.05f;

    // 4 m around the box: spawn points of harvest drops sit within ~2 m of the thing; bushes throw drops up.
    private const float RadiusSqr = 16f;

    // Same frame, within 8 m of the box = same interaction (scythe swing loop, or Player.Interact + Pickable.Interact
    // of one E press): box grow.
    private const float FrameMergeRadiusSqr = 64f;

    // Holding Use call Interact again and again: same spot (0.5 m) within 0.5 s = same entry, only Last move.
    private const float MergeRadiusSqr = 0.25f;
    private const float MergeSeconds = 0.5f;

    private struct Entry
    {
        public float Start;
        public float Last;
        public int Frame;
        public Vector3 Min;
        public Vector3 Max;
    }

    private static readonly Entry[] Ring = new Entry[Capacity];
    private static int _count;
    private static int _head;
    private static float _newestLast = float.NegativeInfinity;

    // Instance ids of live drops born from own harvest. Removed when drop die: size = live tagged drops.
    private static readonly HashSet<int> Tagged = new HashSet<int>();

    internal static void Record(Vector3 pos)
    {
        var t = Time.time;
        var f = Time.frameCount;
        if (_count > 0)
        {
            ref var newest = ref Ring[(_head + Capacity - 1) % Capacity];
            var d = (pos - ClosestInBox(pos, in newest)).sqrMagnitude;
            if ((newest.Frame == f && d <= FrameMergeRadiusSqr) || (t - newest.Last < MergeSeconds && d <= MergeRadiusSqr))
            {
                newest.Min = Vector3.Min(newest.Min, pos);
                newest.Max = Vector3.Max(newest.Max, pos);
                newest.Last = t;
                newest.Frame = f;
                _newestLast = t;
                return;
            }
        }
        Ring[_head] = new Entry { Start = t, Last = t, Frame = f, Min = pos, Max = pos };
        _head = (_head + 1) % Capacity;
        if (_count < Capacity)
        {
            _count++;
        }
        _newestLast = t;
    }

    // Drop born at pos, now, came from recent hand harvest of local player? Called from ItemDrop.Awake: no recent
    // spot = one float compare.
    internal static bool CoversSpawn(Vector3 pos, float time)
    {
        if (_count == 0 || time > _newestLast + WindowSeconds)
        {
            return false;
        }
        // Ring fill from 0, so while not full the live entries are 0.._count-1; full = all.
        for (var i = 0; i < _count; i++)
        {
            ref var e = ref Ring[i];
            if (time < e.Start - EarlySeconds || time > e.Last + WindowSeconds)
            {
                continue;
            }
            if ((pos - ClosestInBox(pos, in e)).sqrMagnitude <= RadiusSqr)
            {
                return true;
            }
        }
        return false;
    }

    private static Vector3 ClosestInBox(Vector3 p, in Entry e) =>
        new Vector3(Mathf.Clamp(p.x, e.Min.x, e.Max.x), Mathf.Clamp(p.y, e.Min.y, e.Max.y), Mathf.Clamp(p.z, e.Min.z, e.Max.z));

    internal static void Tag(ItemDrop drop) => Tagged.Add(drop.GetInstanceID());

    internal static bool IsTagged(ItemDrop drop) => Tagged.Count != 0 && Tagged.Contains(drop.GetInstanceID());

    internal static void Untag(ItemDrop drop)
    {
        if (Tagged.Count != 0)
        {
            Tagged.Remove(drop.GetInstanceID());
        }
    }

    // Character change, activate, deactivate.
    internal static void Clear()
    {
        _count = 0;
        _head = 0;
        _newestLast = float.NegativeInfinity;
        Tagged.Clear();
    }
}
