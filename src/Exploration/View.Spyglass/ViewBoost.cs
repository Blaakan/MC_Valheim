using System;
using UnityEngine;

namespace MC.Exploration.ViewSpyglassMod;

// Me = message to the Distant Horizons mod (MC.Exploration.View.DistantHorizons) while the spyglass is up: "zoom x,
// looking that way, this wide". Distant Horizons then draws more land detail and objects farther in that direction.
// No reference either way: a double[] in an AppDomain slot (BCL type only, like FeatureRegistry), written every
// frame while the spyglass is up. Distant Horizons ignores it once it is a few frames old, so a removed or crashed
// spyglass never leaves a boost on. Layout never changes for "v1" (new layout = new slot name):
//   [0] layout (1)   [1] Time.frameCount   [2] zoom (>= 1)   [3] weight 0..1 (raise transition)
//   [4..6] camera forward (world)   [7] half field of view, horizontal (degrees)   [8] half, vertical (degrees)
internal static class ViewBoost
{
    internal const string SlotName = "MC.ViewBoost.v1";
    private const int Length = 9;

    private static double[] _slot;

    internal static void Write(float zoom, float weight, Vector3 forward, float verticalFov, float aspect)
    {
        var s = Slot();
        var halfV = verticalFov * 0.5f;
        var halfH = Mathf.Atan(Mathf.Tan(halfV * Mathf.Deg2Rad) * Mathf.Max(0.1f, aspect)) * Mathf.Rad2Deg;
        s[0] = 1;
        s[1] = Time.frameCount;
        s[2] = Mathf.Max(1f, zoom);
        s[3] = Mathf.Clamp01(weight);
        s[4] = forward.x;
        s[5] = forward.y;
        s[6] = forward.z;
        s[7] = halfH;
        s[8] = halfV;
    }

    // Spyglass down: weight 0 at once (Distant Horizons drops the boost now, not only once the frame is old).
    internal static void Clear()
    {
        if (_slot == null)
        {
            return;
        }
        _slot[1] = Time.frameCount;
        _slot[2] = 1;
        _slot[3] = 0;
    }

    private static double[] Slot()
    {
        if (_slot != null)
        {
            return _slot;
        }
        var existing = AppDomain.CurrentDomain.GetData(SlotName) as double[];
        if (existing != null && existing.Length >= Length)
        {
            _slot = existing;
        }
        else
        {
            _slot = new double[Length];
            AppDomain.CurrentDomain.SetData(SlotName, _slot);
        }
        return _slot;
    }

#if DEBUG
    internal static double[] Peek() => AppDomain.CurrentDomain.GetData(SlotName) as double[];
#endif
}
