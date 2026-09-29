using System.Collections.Generic;
using MC.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace MC.Exploration.CompendiumEncyclopediaMod;

// Me = the side panel row with our button in it (design 3.6 step 5). Vanilla row (1.0.16, "Info"): 5 controls
// (Texts, Skills, Trophies, Achievements, PVP), 64 units, centres 100 apart, each with a 104-unit round "Background"
// child, inside the wood panel image "Bkg". One more control does not fit at step 100, so while the feature is on me lay
// the row out again: same sizes, our button right after the Texts button, 6 controls evenly spaced and centred where
// the vanilla row was, step shrunk so the whole row (backgrounds included) stay inside the panel background with a
// margin; controls scaled down only when even a tight step (control + 12 %) does not fit.
// Math only, in the row parent's local units (work in Awake and at any animation scale). Every run first put each
// control back on its baseline, then measure, then apply, all in the same frame (nothing drawn in between).
// Exact restore: me keep each vanilla control's own anchoredPosition, localScale, sizeDelta (baseline) and what me set
// (applied). Restore = baseline back, only on controls still where me put them. A control me do not hold now (row
// not laid out, did not fit, gave up) is never pulled back: at each run its current place become its baseline.
// Never fight: a control found neither where me put it nor on its baseline = another mod moved it: its new place
// become its baseline and me lay out again. Third time in one InventoryGui = me give up: our changes undone, the
// vanilla controls left to the other mod, SideButton go back to "one step after the last control".
// Gamepad: vanilla side controls use explicit navigation (Texts right -> Skills). When Texts and its right neighbour
// point at each other, me link ours between them (restored like the positions); else ours stay Automatic. Row layout
// dropped for any reason (anchor gone, no room, gave up, feature off) = links put back and ours Automatic again.
internal static class SideRow
{
    private const int MaxExternalChanges = 2;
    private const float TightGapRatio = 0.12f;
    private const float MinScale = 0.5f;

    private sealed class Slot
    {
        internal RectTransform Rt;
        internal Vector2 BasePos;
        internal Vector3 BaseScale;
        internal Vector2 BaseSize;
        internal Vector2 AppliedPos;
        internal Vector3 AppliedScale;
        internal bool Applied;
    }

    /// <summary>Baseline of one vanilla control (self tests compare after a live toggle).</summary>
    internal struct Baseline
    {
        internal RectTransform Rt;
        internal Vector2 Pos;
        internal Vector3 Scale;
        internal Vector2 Size;
    }

    private static readonly List<Slot> Slots = new List<Slot>();
    private static RectTransform _parent;
    private static RectTransform _background;
    private static int _external;
    private static bool _gaveUp;
    private static string _state = "not laid out";
    private static float _step;
    private static float _scale = 1f;

    private static Selectable _navPrev;
    private static Selectable _navNext;
    private static Selectable _navOurs;
    private static Navigation _navPrevBase;
    private static Navigation _navNextBase;
    private static Navigation _navPrevApplied;
    private static Navigation _navNextApplied;
    private static bool _navLinked;

    internal static bool GaveUp => _gaveUp;
    internal static string State => _state;
    internal static RectTransform Background => _background;
    internal static float Step => _step;
    internal static float Scale => _scale;
    internal static bool NavLinked => _navLinked;
    internal static Selectable NavPrev => _navPrev;
    internal static Selectable NavNext => _navNext;
    internal static Navigation NavPrevBase => _navPrevBase;
    internal static Navigation NavNextBase => _navNextBase;

    internal static List<Baseline> Baselines()
    {
        var list = new List<Baseline>();
        foreach (var s in Slots)
        {
            list.Add(new Baseline { Rt = s.Rt, Pos = s.BasePos, Scale = s.BaseScale, Size = s.BaseSize });
        }
        return list;
    }

    // ---------------------------------------------------------------- apply

    /// <summary>
    /// Lay the row out with <paramref name="ours"/> right after <paramref name="anchor"/>. False = not done (gave up,
    /// no other control, would need a scale under 50 %): caller place ours the old way.
    /// </summary>
    internal static bool Apply(RectTransform parent, RectTransform anchor, RectTransform ours, Selectable oursSel)
    {
        if (_gaveUp)
        {
            return false;
        }
        if (!ReferenceEquals(_parent, parent))
        {
            Restore();
            _parent = parent;
        }
        var controls = Controls(parent, ours);
        if (!Sync(controls))
        {
            return false;
        }
        if (controls.Count == 0)
        {
            LetGo("no other control in the row");
            return false;
        }
        Slot anchorSlot = null;
        foreach (var s in Slots)
        {
            // Baseline first: measure the row as vanilla (or the other mod) left it. On the baseline = not ours any
            // more (a failure below leave it there; success mark it applied again).
            s.Rt.localScale = s.BaseScale;
            s.Rt.anchoredPosition = s.BasePos;
            s.Applied = false;
            if (s.Rt == anchor)
            {
                anchorSlot = s;
            }
        }
        if (anchorSlot == null)
        {
            LetGo("the Valheim Compendium button is not in the row");
            return false;
        }
        ours.localScale = anchorSlot.BaseScale;
        ours.anchoredPosition = anchorSlot.BasePos;

        // Line: axis with the larger spread of the centres. Order: left to right, or top to bottom.
        var centers = new List<Vector2>();
        foreach (var c in controls)
        {
            centers.Add(UiUtil.ParentSpaceRect(c).center);
        }
        var horizontal = Spread(centers, true) >= Spread(centers, false);
        controls.Sort((a, b) => Along(UiUtil.ParentSpaceRect(a).center, horizontal).CompareTo(Along(UiUtil.ParentSpaceRect(b).center, horizontal)));

        // Sizes along the line: control and "visual" (control + its child graphics, the round backgrounds).
        float size = 0f, visual = 0f, visMin = float.MaxValue, visMax = float.MinValue;
        var along = new List<float>();
        foreach (var c in controls)
        {
            var r = UiUtil.ParentSpaceRect(c);
            var v = VisualRect(c, parent);
            size = Mathf.Max(size, Len(r, horizontal));
            visual = Mathf.Max(visual, Len(v, horizontal));
            visMin = Mathf.Min(visMin, Min(v, horizontal));
            visMax = Mathf.Max(visMax, Max(v, horizontal));
            along.Add(Along(r.center, horizontal));
        }
        var n = controls.Count + 1;
        var baseStep = along.Count >= 2 ? MedianGap(along) : size + 4f;

        _background = FindBackground(parent, ours, controls);
        var region = _background != null ? UiUtil.ParentSpaceRect(_background) : parent.rect;
        var regMin = Min(region, horizontal);
        var regMax = Max(region, horizontal);
        var regLen = regMax - regMin;
        var margin = Mathf.Max(0f, Mathf.Min(visMin - regMin, regMax - visMax));
        var inset = Mathf.Min(margin, Mathf.Max(4f, 0.04f * regLen));
        var avail = regLen - 2f * inset;

        var step = baseStep;
        if ((n - 1) * step + visual > avail)
        {
            step = (avail - visual) / (n - 1);
        }
        var tight = size * (1f + TightGapRatio);
        var k = 1f;
        if (step < tight)
        {
            k = avail / ((n - 1) * tight + visual);
            step = tight * k;
        }
        if (k < MinScale || step <= 0f)
        {
            LetGo($"{n} controls do not fit (scale {k:F2} needed)");
            return false;
        }
        var half = ((n - 1) * step + visual * k) * 0.5f;
        var mid = (along[0] + along[along.Count - 1]) * 0.5f;
        var lo = regMin + inset + half;
        var hi = regMax - inset - half;
        mid = lo <= hi ? Mathf.Clamp(mid, lo, hi) : (regMin + regMax) * 0.5f;

        var order = new List<RectTransform>(controls);
        var at = order.IndexOf(anchor);
        order.Insert(at + 1, ours);
        // Along units: x, or -y (top to bottom).
        var first = mid - (n - 1) * step * 0.5f;
        for (var i = 0; i < order.Count; i++)
        {
            var rt = order[i];
            var slot = rt == ours ? null : Find(rt);
            rt.localScale = (slot != null ? slot.BaseScale : anchorSlot.BaseScale) * k;
            var d = first + i * step - Along(UiUtil.ParentSpaceRect(rt).center, horizontal);
            rt.anchoredPosition += horizontal ? new Vector2(d, 0f) : new Vector2(0f, -d);
            if (slot != null)
            {
                slot.AppliedPos = rt.anchoredPosition;
                slot.AppliedScale = rt.localScale;
                slot.Applied = true;
            }
        }
        _step = step;
        _scale = k;
        var next = at + 2 < order.Count ? order[at + 2] : null;
        LinkNavigation(anchor, next, oursSel, horizontal);
        _state = $"{n} controls, step {step:F1} (vanilla {baseStep:F1}), scale {k:F2}, inset {inset:F1}, background "
                 + $"'{(_background != null ? _background.name : parent.name)}', {(horizontal ? "horizontal" : "vertical")}, "
                 + $"navigation {(_navLinked ? "linked" : "automatic")}, external changes {_external}";
        return true;
    }

    // Active direct children holding an active Selectable, ours excluded.
    private static List<RectTransform> Controls(RectTransform parent, RectTransform ours)
    {
        var list = new List<RectTransform>();
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i) as RectTransform;
            if (child == null || child == ours || !child.gameObject.activeSelf || child.GetComponentInChildren<Selectable>() == null)
            {
                continue;
            }
            list.Add(child);
        }
        return list;
    }

    // Slots follow the controls. False = gave up (another mod keeps moving them).
    private static bool Sync(List<RectTransform> controls)
    {
        for (var i = Slots.Count - 1; i >= 0; i--)
        {
            var s = Slots[i];
            if (s.Rt == null || !controls.Contains(s.Rt))
            {
                if (s.Rt != null && StillApplied(s))
                {
                    PutBase(s);
                }
                Slots.RemoveAt(i);
            }
        }
        var changed = false;
        foreach (var c in controls)
        {
            var s = Find(c);
            if (s == null)
            {
                Slots.Add(new Slot { Rt = c, BasePos = c.anchoredPosition, BaseScale = c.localScale, BaseSize = c.sizeDelta });
                continue;
            }
            if (!s.Applied)
            {
                // Not ours (row not laid out, or gave up): wherever it is now is its place. Never pull it back.
                s.BasePos = c.anchoredPosition;
                s.BaseScale = c.localScale;
                s.BaseSize = c.sizeDelta;
                continue;
            }
            if (StillApplied(s))
            {
                continue;
            }
            changed = true;
            if (c.anchoredPosition != s.BasePos || c.localScale != s.BaseScale)
            {
                // Moved somewhere new by someone else: that is its place now.
                s.BasePos = c.anchoredPosition;
                s.BaseScale = c.localScale;
                s.BaseSize = c.sizeDelta;
            }
            s.Applied = false;
        }
        if (!changed)
        {
            return true;
        }
        _external++;
        if (_external <= MaxExternalChanges)
        {
            UiUtil.Trace($"Encyclopedia side row: a side control was moved by something else ({_external} time(s)): row laid out again.");
            return true;
        }
        GiveUp();
        return false;
    }

    // Row layout dropped (anchor gone, no room, no control): our changes undone, positions and the two navigation links
    // alike, so a failed Apply never leave the D-pad walking to our button parked after the last control.
    private static void LetGo(string state)
    {
        RestoreSlots();
        RestoreNavigation();
        _state = state;
    }

    private static void GiveUp()
    {
        _gaveUp = true;
        LetGo("gave up: another mod keeps moving the side controls");
        Log.Warning("Another mod keeps moving the inventory side panel buttons: the Encyclopedia leaves them alone and puts its "
                    + "button after the last one.");
    }

    private static Slot Find(RectTransform rt)
    {
        foreach (var s in Slots)
        {
            if (s.Rt == rt)
            {
                return s;
            }
        }
        return null;
    }

    private static bool StillApplied(Slot s) => s.Applied && s.Rt.anchoredPosition == s.AppliedPos && s.Rt.localScale == s.AppliedScale;

    private static void PutBase(Slot s)
    {
        s.Rt.localScale = s.BaseScale;
        s.Rt.anchoredPosition = s.BasePos;
        s.Rt.sizeDelta = s.BaseSize;
        s.Applied = false;
    }

    // ---------------------------------------------------------------- background

    /// <summary>
    /// Panel background = the smallest active direct child of the row parent with an enabled Image (no Selectable in
    /// it, not ours) whose rect holds every control. None = null (the parent's rect is used).
    /// </summary>
    internal static RectTransform FindBackground(RectTransform parent, RectTransform ours, List<RectTransform> controls)
    {
        RectTransform best = null;
        var bestArea = float.MaxValue;
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i) as RectTransform;
            if (child == null || child == ours || !child.gameObject.activeSelf || controls.Contains(child)
                || child.GetComponentInChildren<Selectable>(true) != null)
            {
                continue;
            }
            var img = child.GetComponent<Image>();
            if (img == null || !img.enabled)
            {
                continue;
            }
            var r = UiUtil.ParentSpaceRect(child);
            var holds = true;
            foreach (var c in controls)
            {
                if (!UiUtil.Inside(UiUtil.ParentSpaceRect(c), r, 1f))
                {
                    holds = false;
                    break;
                }
            }
            var area = r.width * r.height;
            if (holds && area < bestArea)
            {
                best = child;
                bestArea = area;
            }
        }
        return best;
    }

    /// <summary>Background for a row me did not lay out (layout group, fallback placement): same rule.</summary>
    internal static RectTransform FindBackground(RectTransform parent, RectTransform ours) =>
        parent != null ? FindBackground(parent, ours, Controls(parent, ours)) : null;

    private static Rect VisualRect(RectTransform control, RectTransform parent)
    {
        var r = UiUtil.ParentSpaceRect(control);
        foreach (var g in control.GetComponentsInChildren<Graphic>())
        {
            if (g != null && g.enabled)
            {
                r = UiUtil.Union(r, UiUtil.RectInAncestor(g.rectTransform, parent));
            }
        }
        return r;
    }

    // ---------------------------------------------------------------- navigation

    private static void LinkNavigation(RectTransform anchorRt, RectTransform nextRt, Selectable ours, bool horizontal)
    {
        var prev = anchorRt.GetComponent<Selectable>();
        var next = nextRt != null ? nextRt.GetComponent<Selectable>() : null;
        if (_navLinked && (prev == null || next == null || prev != _navPrev || next != _navNext
                           || !prev.navigation.Equals(_navPrevApplied) || !next.navigation.Equals(_navNextApplied)))
        {
            RestoreNavigation(); // row changed, or someone else rewired them: start again from what is there now
        }
        if (_navLinked || prev == null || next == null || ours == null || !horizontal)
        {
            return;
        }
        var pn = prev.navigation;
        var nn = next.navigation;
        if (pn.mode != Navigation.Mode.Explicit || nn.mode != Navigation.Mode.Explicit || pn.selectOnRight != next || nn.selectOnLeft != prev)
        {
            return;
        }
        _navPrev = prev;
        _navNext = next;
        _navPrevBase = pn;
        _navNextBase = nn;
        var p2 = pn;
        p2.selectOnRight = ours;
        var n2 = nn;
        n2.selectOnLeft = ours;
        prev.navigation = p2;
        next.navigation = n2;
        _navPrevApplied = p2;
        _navNextApplied = n2;
        _navOurs = ours;
        ours.navigation = new Navigation
        {
            mode = Navigation.Mode.Explicit,
            selectOnLeft = prev,
            selectOnRight = next,
            selectOnUp = pn.selectOnUp,
            selectOnDown = pn.selectOnDown,
        };
        _navLinked = true;
    }

    private static void RestoreNavigation()
    {
        if (_navLinked)
        {
            if (_navPrev != null && _navPrev.navigation.Equals(_navPrevApplied))
            {
                _navPrev.navigation = _navPrevBase;
            }
            if (_navNext != null && _navNext.navigation.Equals(_navNextApplied))
            {
                _navNext.navigation = _navNextBase;
            }
            if (_navOurs != null)
            {
                // Ours back to what CloneUtil gave it: Automatic, no explicit link to a vanilla control.
                _navOurs.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            }
        }
        _navLinked = false;
        _navPrev = null;
        _navNext = null;
        _navOurs = null;
    }

    // ---------------------------------------------------------------- restore / forget

    // Only controls still where me put them go back; the others belong to vanilla or another mod as they are.
    private static void RestoreSlots()
    {
        foreach (var s in Slots)
        {
            if (s.Rt != null && StillApplied(s))
            {
                PutBase(s);
            }
        }
    }

    /// <summary>Feature off, button hidden: every vanilla control back exactly (those still where me put them).</summary>
    internal static void Restore()
    {
        RestoreSlots();
        RestoreNavigation();
        Clear();
    }

    /// <summary>New InventoryGui: the old controls died with the old one. Nothing to put back.</summary>
    internal static void Forget()
    {
        _navLinked = false;
        _navPrev = null;
        _navNext = null;
        _navOurs = null;
        Clear();
    }

    private static void Clear()
    {
        Slots.Clear();
        _parent = null;
        _background = null;
        _external = 0;
        _gaveUp = false;
        _state = "not laid out";
        _step = 0f;
        _scale = 1f;
    }

    // ---------------------------------------------------------------- math

    private static float Along(Vector2 c, bool horizontal) => horizontal ? c.x : -c.y;

    private static float Len(Rect r, bool horizontal) => horizontal ? r.width : r.height;

    private static float Min(Rect r, bool horizontal) => horizontal ? r.xMin : -r.yMax;

    private static float Max(Rect r, bool horizontal) => horizontal ? r.xMax : -r.yMin;

    private static float Spread(List<Vector2> c, bool x)
    {
        if (c.Count < 2)
        {
            return x ? 1f : 0f;
        }
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var v in c)
        {
            var a = x ? v.x : v.y;
            lo = Mathf.Min(lo, a);
            hi = Mathf.Max(hi, a);
        }
        return hi - lo;
    }

    private static float MedianGap(List<float> sorted)
    {
        var gaps = new List<float>();
        for (var i = 1; i < sorted.Count; i++)
        {
            var g = sorted[i] - sorted[i - 1];
            if (g >= 1f)
            {
                gaps.Add(g);
            }
        }
        if (gaps.Count == 0)
        {
            return 0f;
        }
        gaps.Sort();
        var n = gaps.Count;
        return n % 2 == 1 ? gaps[n / 2] : (gaps[n / 2 - 1] + gaps[n / 2]) * 0.5f;
    }
}
