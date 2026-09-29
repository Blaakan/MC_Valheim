using System.Text;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.CompendiumEncyclopediaMod;

// Me = small rect helpers the UI files share. World rect on overlay canvas = screen pixels.
internal static class UiUtil
{
    private static readonly Vector3[] Corners = new Vector3[4];

    /// <summary>
    /// UI trace line: Info in Debug builds (BepInEx's default log file drop Debug lines, and the in-world tests read
    /// the log file), Debug in Release builds.
    /// </summary>
    internal static void Trace(string message)
    {
#if DEBUG
        Log.Info(message);
#else
        Log.Debug(message);
#endif
    }

    /// <summary>Axis-aligned world rect of a RectTransform.</summary>
    internal static Rect WorldRect(RectTransform rt)
    {
        rt.GetWorldCorners(Corners);
        var min = Corners[0];
        var max = Corners[0];
        for (var i = 1; i < 4; i++)
        {
            min = Vector3.Min(min, Corners[i]);
            max = Vector3.Max(max, Corners[i]);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    /// <summary>
    /// Rect of <paramref name="rt"/> in <paramref name="space"/> local units (any pivot, anchor, scale). False when a
    /// transform on the way is at zero scale (show animation start): numbers would be NaN or infinite.
    /// </summary>
    internal static bool TryRectIn(RectTransform rt, Transform space, out Rect rect)
    {
        rect = default;
        if (rt == null || space == null)
        {
            return false;
        }
        rt.GetWorldCorners(Corners);
        var min = (Vector2)space.InverseTransformPoint(Corners[0]);
        var max = min;
        for (var i = 1; i < 4; i++)
        {
            var p = (Vector2)space.InverseTransformPoint(Corners[i]);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
        if (!Finite(min.x) || !Finite(min.y) || !Finite(max.x) || !Finite(max.y))
        {
            return false;
        }
        rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return true;
    }

    /// <summary>
    /// Rect of a direct child in its parent's local units, by math only (no world transform): work at zero ancestor
    /// scale and in Awake. Rotation ignored (UI buttons are not rotated).
    /// </summary>
    internal static Rect ParentSpaceRect(RectTransform child)
    {
        var r = child.rect;
        var s = child.localScale;
        var p = (Vector2)child.localPosition;
        var a = p + new Vector2(r.xMin * s.x, r.yMin * s.y);
        var b = p + new Vector2(r.xMax * s.x, r.yMax * s.y);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    /// <summary>
    /// Rect of a descendant in an ancestor's local units, by math only (localPosition and localScale up the chain;
    /// rotation ignored). Work while inactive and at any scale above the ancestor.
    /// </summary>
    internal static Rect RectInAncestor(RectTransform rt, Transform ancestor)
    {
        var r = rt.rect;
        var min = r.min;
        var max = r.max;
        for (Transform t = rt; t != null && t != ancestor; t = t.parent)
        {
            var s = t.localScale;
            var p = (Vector2)t.localPosition;
            var a = new Vector2(min.x * s.x, min.y * s.y) + p;
            var b = new Vector2(max.x * s.x, max.y * s.y) + p;
            min = Vector2.Min(a, b);
            max = Vector2.Max(a, b);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    /// <summary>
    /// How many <paramref name="ancestor"/> units one vertical unit of <paramref name="t"/> is (product of localScale.y
    /// from t up to, not including, the ancestor). Never 0.
    /// </summary>
    internal static float ScaleYTo(Transform t, Transform ancestor)
    {
        var k = 1f;
        for (var x = t; x != null && x != ancestor; x = x.parent)
        {
            k *= x.localScale.y;
        }
        return Mathf.Abs(k) < 1e-4f ? 1f : k;
    }

    /// <summary>Vertical length from one transform's units to another's.</summary>
    internal static float ConvertY(Transform from, Transform to, float dy)
    {
        if (from == null || to == null)
        {
            return dy;
        }
        var v = to.InverseTransformVector(from.TransformVector(new Vector3(0f, dy, 0f))).y;
        return Finite(v) ? v : dy;
    }

    /// <summary>
    /// Put a direct child of <paramref name="parent"/> on rect <paramref name="r"/> (parent local units). Anchors and
    /// pivot at the centre, so the rect stay put whatever the parent's pivot.
    /// </summary>
    internal static void PlaceIn(RectTransform child, RectTransform parent, Rect r)
    {
        var half = new Vector2(0.5f, 0.5f);
        child.anchorMin = half;
        child.anchorMax = half;
        child.pivot = half;
        child.localScale = Vector3.one;
        child.localRotation = Quaternion.identity;
        child.sizeDelta = r.size;
        child.anchoredPosition = r.center - parent.rect.center;
    }

    /// <summary>Stretch child over its whole parent.</summary>
    internal static void Fill(RectTransform child)
    {
        child.anchorMin = Vector2.zero;
        child.anchorMax = Vector2.one;
        child.pivot = new Vector2(0.5f, 0.5f);
        child.offsetMin = Vector2.zero;
        child.offsetMax = Vector2.zero;
        child.localScale = Vector3.one;
        child.localRotation = Quaternion.identity;
    }

    /// <summary>Top-stretch child: full width minus insets, <paramref name="height"/> high, top edge <paramref name="top"/> below parent top.</summary>
    internal static void TopStretch(RectTransform child, float left, float right, float top, float height)
    {
        child.anchorMin = new Vector2(0f, 1f);
        child.anchorMax = new Vector2(1f, 1f);
        child.pivot = new Vector2(0.5f, 1f);
        child.localScale = Vector3.one;
        child.localRotation = Quaternion.identity;
        child.offsetMin = new Vector2(left, -top - height);
        child.offsetMax = new Vector2(-right, -top);
    }

    internal static bool Overlaps(Rect a, Rect b, float tolerance) =>
        Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) > tolerance
        && Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) > tolerance;

    internal static bool Inside(Rect inner, Rect outer, float tolerance) =>
        inner.xMin >= outer.xMin - tolerance && inner.xMax <= outer.xMax + tolerance
        && inner.yMin >= outer.yMin - tolerance && inner.yMax <= outer.yMax + tolerance;

    internal static Rect Union(Rect a, Rect b) =>
        Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

    /// <summary>Transform at (near) zero scale: overlap and position tests lie.</summary>
    internal static bool Degenerate(Transform t)
    {
        var s = t.lossyScale;
        return Mathf.Abs(s.x) < 1e-3f || Mathf.Abs(s.y) < 1e-3f;
    }

    internal static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

    internal static string Fmt(Rect r) =>
        $"({r.xMin:F0},{r.yMin:F0})-({r.xMax:F0},{r.yMax:F0})";

    internal static string Path(Transform t)
    {
        if (t == null)
        {
            return "null";
        }
        var sb = new StringBuilder(t.name);
        for (var p = t.parent; p != null; p = p.parent)
        {
            sb.Insert(0, p.name + "/");
        }
        return sb.ToString();
    }
}
