using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MC.Exploration.CompendiumEncyclopediaMod;

// Me find the parts of a Valheim Compendium dialog by structure (design 1.4, 3.7): the vanilla dialog itself (top tabs
// on it, TopTabs) and our clone of it (window build) use the same rules, so both land on the same rects. Read only:
// me never change the dialog here. Math only (RectInAncestor), so it work while inactive and at any animation scale.
internal static class DialogParts
{
    internal const string TitleToken = "$inventory_texts";

    /// <summary>Buttons whose prefab-saved onClick call <paramref name="method"/> (vanilla close buttons: "OnClose").</summary>
    internal static List<Button> FindPersistent(GameObject go, string method)
    {
        var list = new List<Button>();
        foreach (var b in go.GetComponentsInChildren<Button>(true))
        {
            var ev = b.onClick;
            for (var i = 0; i < ev.GetPersistentEventCount(); i++)
            {
                if (ev.GetPersistentMethodName(i) == method)
                {
                    list.Add(b);
                    break;
                }
            }
        }
        return list;
    }

    // Title = the text outside list, text area and close buttons that reads "$inventory_texts" (raw or localized);
    // else the top-most such text above both panes (vanilla: "topic"). Our own top tabs never count.
    internal static TMP_Text FindTitle(GameObject go, TMP_Text topic, TMP_Text textArea, ScrollRect left, ScrollRect right, List<Button> closes)
    {
        var localized = Localization.instance != null ? Localization.instance.Localize(TitleToken) : TitleToken;
        var root = go.transform;
        var panesTop = Mathf.Max(UiUtil.RectInAncestor((RectTransform)left.transform, root).yMax,
            UiUtil.RectInAncestor((RectTransform)right.transform, root).yMax);
        TMP_Text top = null;
        var topY = float.MinValue;
        foreach (var t in go.GetComponentsInChildren<TMP_Text>(true))
        {
            if (t == null || t == topic || t == textArea || t.transform.IsChildOf(left.transform) || t.transform.IsChildOf(right.transform)
                || InsideAny(t.transform, closes) || UnderNamed(t.transform, root, TopTabs.StripName))
            {
                continue;
            }
            var s = (t.text ?? "").Trim();
            if (s.Length > 0 && (s == TitleToken || string.Equals(s, localized, StringComparison.OrdinalIgnoreCase)))
            {
                return t;
            }
            var r = UiUtil.RectInAncestor(t.rectTransform, root);
            if (r.yMin >= panesTop - 1f && r.yMax > topY)
            {
                top = t;
                topY = r.yMax;
            }
        }
        return top;
    }

    private static bool InsideAny(Transform t, List<Button> buttons)
    {
        foreach (var b in buttons)
        {
            if (b != null && t.IsChildOf(b.transform))
            {
                return true;
            }
        }
        return false;
    }

    private static bool UnderNamed(Transform t, Transform root, string name)
    {
        for (var x = t; x != null && x != root; x = x.parent)
        {
            if (x.name == name)
            {
                return true;
            }
        }
        return false;
    }

    internal static Transform CommonAncestor(Transform a, Transform b)
    {
        for (var t = a.parent; t != null; t = t.parent)
        {
            if (b.IsChildOf(t))
            {
                return t;
            }
        }
        return null;
    }

    /// <summary>Ancestor of rt (or rt) whose parent is frame. Frame null or not above: rt itself.</summary>
    internal static RectTransform ChildUnder(RectTransform rt, Transform frame)
    {
        if (frame == null)
        {
            return rt;
        }
        Transform t = rt;
        while (t.parent != null && t.parent != frame)
        {
            t = t.parent;
        }
        return t.parent == frame && t is RectTransform r ? r : rt;
    }

    /// <summary>
    /// Pane boxes (the scroll views' ancestors right under the object holding both: vanilla TextList, TextArea) and
    /// the union of both boxes and their bars, in <paramref name="root"/> units.
    /// </summary>
    internal static Rect PaneArea(Transform root, ScrollRect left, ScrollRect right, Scrollbar leftSb, Scrollbar rightSb,
        out RectTransform leftPane, out RectTransform rightPane)
    {
        var leftRt = (RectTransform)left.transform;
        var rightRt = (RectTransform)right.transform;
        var frame = CommonAncestor(leftRt, rightRt);
        leftPane = ChildUnder(leftRt, frame);
        rightPane = ChildUnder(rightRt, frame);
        var l = UiUtil.RectInAncestor(leftPane, root);
        if (leftSb != null && leftSb.transform is RectTransform lsb)
        {
            l = UiUtil.Union(l, UiUtil.RectInAncestor(lsb, root));
        }
        var r = UiUtil.RectInAncestor(rightPane, root);
        if (rightSb != null && rightSb.transform is RectTransform rsb)
        {
            r = UiUtil.Union(r, UiUtil.RectInAncestor(rsb, root));
        }
        return UiUtil.Union(l, r);
    }

    /// <summary>List scroll view = the ScrollRect above m_listRoot (not m_leftScrollRect: 1.0.16 trap, design 1.4).</summary>
    internal static ScrollRect ListScroll(RectTransform listRoot) => listRoot != null ? listRoot.GetComponentInParent<ScrollRect>(true) : null;

    /// <summary>Text scroll view = the ScrollRect above m_textArea.</summary>
    internal static ScrollRect TextScroll(TMP_Text textArea) => textArea != null ? textArea.GetComponentInParent<ScrollRect>(true) : null;
}
