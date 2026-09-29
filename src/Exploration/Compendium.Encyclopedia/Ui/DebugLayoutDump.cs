using System.Diagnostics;
#if DEBUG
using System;
using System.Collections.Generic;
using System.Text;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
#endif

namespace MC.Exploration.CompendiumEncyclopediaMod;

// Debug build only (calls vanish in Release): log where things sit, so the side panel layout, the button placement,
// the texts dialog structure, the canvases and the focus groups can be checked from the log alone (design 3.15,
// T01-T02). Info level: BepInEx default log file drop Debug lines, and these dumps exist only in Debug builds anyway.
//   SidePanel      = once per InventoryGui after the panel settled, opt-in side button only (see SideButton);
//   TextsSource    = once per InventoryGui when the window build start (before any check, so a refused build still dump);
//   VanillaTopTabs = once per InventoryGui, first time our top tabs sit on the Valheim Compendium (pads, overlaps);
//   Window         = once per window, first frame open.
internal static class DebugLayoutDump
{
#if DEBUG
    private const int MaxLines = 400;
    private static InventoryGui _sideFor;
    private static InventoryGui _textsFor;
    private static InventoryGui _windowFor;
#endif

    [Conditional("DEBUG")]
    internal static void SidePanel(InventoryGui gui, bool again = false)
    {
#if DEBUG
        if (gui == null || (!again && ReferenceEquals(_sideFor, gui)))
        {
            return;
        }
        _sideFor = gui;
        try
        {
            var sb = new StringBuilder();
            sb.Append("Encyclopedia side panel layout (world rects = screen pixels on an overlay canvas), screen ")
                .Append(Screen.width).Append('x').Append(Screen.height).Append(':');
            var anchor = SideButton.AnchorButton;
            var parent = anchor != null ? anchor.transform.parent : null;
            sb.Append("\n anchor: ").Append(anchor != null ? UiUtil.Path(anchor.transform) : "none")
                .Append(" (").Append(SideButton.AnchorMethod).Append(')');
            DescribeParent(sb, "anchor parent", parent, gui);
            var pvpParent = gui.m_pvp != null ? gui.m_pvp.transform.parent : null;
            if (pvpParent != null && pvpParent != parent)
            {
                DescribeParent(sb, "m_pvp parent", pvpParent, gui);
            }
            else
            {
                sb.Append("\n m_pvp: ").Append(gui.m_pvp != null ? "same parent as the anchor" : "none");
            }
            DescribeControls(sb, parent);
            if (pvpParent != null && pvpParent != parent)
            {
                DescribeControls(sb, pvpParent);
            }
            // Ancestors of the row up to the inventory root: which one is the panel background.
            sb.Append("\n row ancestors (world rects):");
            for (var t = parent; t != null; t = t.parent)
            {
                sb.Append("\n  '").Append(t.name).Append("' [").Append(Components(t)).Append(']');
                if (t is RectTransform art)
                {
                    sb.Append(" world=").Append(UiUtil.Fmt(UiUtil.WorldRect(art)));
                }
                AppendImage(sb, t.GetComponent<Image>());
                if (t == gui.transform)
                {
                    break;
                }
            }
            sb.Append("\n row parent tree (world rects):");
            var lines = 0;
            if (parent != null)
            {
                WalkDetailed(sb, parent, null, 0, 3, null, ref lines);
            }
            sb.Append("\n row layout: ").Append(SideButton.RowLayoutState);

            var rt = SideButton.Rect;
            sb.Append("\n our button: ");
            if (rt != null)
            {
                sb.Append(UiUtil.Path(rt)).Append(" world=").Append(UiUtil.Fmt(UiUtil.WorldRect(rt)))
                    .Append(" pos=").Append(rt.anchoredPosition).Append(" sibling=").Append(rt.GetSiblingIndex())
                    .Append(" placement=").Append(SideButton.CurrentPlacement).Append(" check=").Append(SideButton.LastCheck)
                    .Append(" icon=").Append(SideButton.Icon != null ? SideButton.Icon.name : "none")
                    .Append(" tooltip=").Append(SideButton.Tooltip != null ? SideButton.Tooltip.m_topic : "none");
                var pad = SideButton.Pad;
                sb.Append(" pad=").Append(pad != null ? pad.m_zinputKey : "none")
                    .Append(" padGroup=").Append(pad != null && pad.m_group != null ? pad.m_group.name : "none");
            }
            else
            {
                sb.Append("none");
            }

            if (!again)
            {
                DescribeGroups(sb, gui);
                DescribeCanvases(sb, gui);
                var secret = gui.m_secretAchievementIcon;
                sb.Append("\n m_secretAchievementIcon: ").Append(secret != null ? $"{secret.name} {secret.rect.width:F0}x{secret.rect.height:F0}" : "none");
                DescribeTree(sb, "m_tabCraft", gui.m_tabCraft != null ? gui.m_tabCraft.transform : null, 2);
                DescribeTree(sb, "m_recipeElementPrefab", gui.m_recipeElementPrefab != null ? gui.m_recipeElementPrefab.transform : null, 2);
                foreach (var line in CloneUtil.StripLog)
                {
                    sb.Append("\n ").Append(line);
                }
            }
            Log.Info(sb.ToString());
        }
        catch (Exception e)
        {
            Log.Info($"Encyclopedia side panel dump failed: {e}");
        }
#endif
    }

    /// <summary>The vanilla texts dialog as it is (the window clone source), with the TextsDialog references marked.</summary>
    [Conditional("DEBUG")]
    internal static void TextsSource(InventoryGui gui)
    {
#if DEBUG
        if (gui == null || ReferenceEquals(_textsFor, gui))
        {
            return;
        }
        _textsFor = gui;
        try
        {
            var dialog = gui.m_textsDialog;
            var sb = new StringBuilder();
            sb.Append("Encyclopedia texts dialog source (rects in the dialog's space):");
            if (dialog == null)
            {
                Log.Info(sb.Append(" none").ToString());
                return;
            }
            var refs = new Dictionary<Object, string>();
            void Ref(Object o, string field)
            {
                if (o == null)
                {
                    sb.Append("\n ").Append(field).Append(": null");
                    return;
                }
                var tr = o is Component c ? c.transform : o is GameObject g ? g.transform : null;
                sb.Append("\n ").Append(field).Append(": ").Append(o.GetType().Name).Append(' ')
                    .Append(tr != null ? (tr.IsChildOf(dialog.transform) ? UiUtil.Path(tr) : "OUTSIDE the dialog: " + UiUtil.Path(tr)) : o.name);
                if (tr != null)
                {
                    refs[tr.gameObject] = refs.TryGetValue(tr.gameObject, out var old) ? old + "," + field : field;
                }
            }
            Ref(dialog.m_listRoot, "m_listRoot");
            Ref(dialog.m_leftScrollRect, "m_leftScrollRect");
            Ref(dialog.m_leftScrollbar, "m_leftScrollbar");
            Ref(dialog.m_rightScrollbar, "m_rightScrollbar");
            Ref(dialog.m_elementPrefab, "m_elementPrefab");
            Ref(dialog.m_totalSkillText, "m_totalSkillText");
            Ref(dialog.m_textAreaTopic, "m_textAreaTopic");
            Ref(dialog.m_textArea, "m_textArea");
            Ref(dialog.m_recipeEnsureVisible, "m_recipeEnsureVisible");
            sb.Append("\n m_spacing: ").Append(dialog.m_spacing);
            var textArea = dialog.m_textArea;
            if (textArea != null)
            {
                sb.Append("\n ScrollRects above m_textArea:");
                for (var t = textArea.transform; t != null && t != dialog.transform.parent; t = t.parent)
                {
                    var sr = t.GetComponent<ScrollRect>();
                    if (sr != null)
                    {
                        sb.Append(" '").Append(t.name).Append('\'');
                    }
                }
            }
            sb.Append("\n dialog world=").Append(UiUtil.Fmt(UiUtil.WorldRect((RectTransform)dialog.transform)))
                .Append(" parent=").Append(UiUtil.Path(dialog.transform.parent));
            sb.Append("\n tree:");
            var lines = 0;
            WalkDetailed(sb, dialog.transform, dialog.transform, 0, 9, refs, ref lines);
            if (dialog.m_elementPrefab != null && !dialog.m_elementPrefab.transform.IsChildOf(dialog.transform))
            {
                sb.Append("\n m_elementPrefab tree:");
                WalkDetailed(sb, dialog.m_elementPrefab.transform, dialog.m_elementPrefab.transform, 0, 3, null, ref lines);
            }
            Log.Info(sb.ToString());
        }
        catch (Exception e)
        {
            Log.Info($"Encyclopedia texts dialog dump failed: {e}");
        }
#endif
    }

    /// <summary>
    /// The vanilla dialog with our top tabs on it, once per InventoryGui: tabs vs title, close button, panes and the
    /// wood frame (OVERLAP / OUTSIDE lines), and every UIGamePad key in the dialog (none may be on LT / RT).
    /// </summary>
    [Conditional("DEBUG")]
    internal static void VanillaTopTabs(InventoryGui gui, TextsDialog dialog)
    {
#if DEBUG
        try
        {
            var root = dialog != null ? dialog.transform as RectTransform : null;
            var strip = TopTabs.VanillaStrip;
            if (root == null || strip == null)
            {
                return;
            }
            var sb = new StringBuilder();
            sb.Append("Encyclopedia top tabs on the Valheim Compendium, screen ").Append(Screen.width).Append('x').Append(Screen.height)
                .Append(" (world rects):");
            var tabs = new[] { TopTabs.VanillaTexts, TopTabs.VanillaEncyclopedia };
            foreach (var b in tabs)
            {
                DescribeRect(b != null ? b.name : "tab", b != null ? b.transform as RectTransform : null, sb);
                if (b != null)
                {
                    sb.Append(" interactable=").Append(b.interactable);
                }
            }
            var others = new List<RectTransform>();
            var closes = DialogParts.FindPersistent(dialog.gameObject, "OnClose");
            foreach (var c in closes)
            {
                var rt = (RectTransform)c.transform;
                // The full-screen click-outside button covers everything by design.
                if (!(rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one))
                {
                    others.Add(rt);
                }
            }
            if (dialog.m_textAreaTopic != null)
            {
                others.Add(dialog.m_textAreaTopic.rectTransform);
            }
            var left = DialogParts.ListScroll(dialog.m_listRoot);
            var right = DialogParts.TextScroll(dialog.m_textArea);
            if (left != null && right != null)
            {
                var title = DialogParts.FindTitle(dialog.gameObject, dialog.m_textAreaTopic, dialog.m_textArea, left, right, closes);
                if (title != null)
                {
                    others.Add(title.rectTransform);
                    DescribeRect("title", title.rectTransform, sb);
                }
                DialogParts.PaneArea(root, left, right, left.verticalScrollbar, right.verticalScrollbar, out var lp, out var rp);
                others.Add(lp);
                others.Add(rp);
                DescribeRect("left pane", lp, sb);
                DescribeRect("right pane", rp, sb);
            }
            RectTransform frameBkg = null;
            foreach (var img in dialog.GetComponentsInChildren<Image>(true))
            {
                if (img != null && img.sprite != null && img.sprite.name.IndexOf("woodpanel", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    frameBkg = img.rectTransform;
                    break;
                }
            }
            DescribeRect("frame", frameBkg, sb);
            foreach (var b in tabs)
            {
                if (b == null)
                {
                    continue;
                }
                var w = UiUtil.WorldRect((RectTransform)b.transform);
                foreach (var o in others)
                {
                    if (o != null && UiUtil.Overlaps(w, UiUtil.WorldRect(o), 1f))
                    {
                        sb.Append("\n OVERLAP: ").Append(b.name).Append(" and '").Append(o.name).Append('\'');
                    }
                }
                if (frameBkg != null && !UiUtil.Inside(w, UiUtil.WorldRect(frameBkg), 1f))
                {
                    sb.Append("\n OUTSIDE: ").Append(b.name).Append(" leaves the frame");
                }
            }
            sb.Append("\n controller pads in the dialog:");
            foreach (var pad in dialog.GetComponentsInChildren<UIGamePad>(true))
            {
                sb.Append("\n  '").Append(UiUtil.Path(pad.transform)).Append("' key=")
                    .Append(string.IsNullOrEmpty(pad.m_zinputKey) ? pad.m_keyCode.ToString() : pad.m_zinputKey);
            }
            sb.Append("\n dialog tree (rects in the dialog's space):");
            var lines = 0;
            WalkDetailed(sb, root, root, 0, 4, null, ref lines);
            Log.Info(sb.ToString());
        }
        catch (Exception e)
        {
            Log.Info($"Encyclopedia top tabs dump failed: {e}");
        }
#endif
    }

    [Conditional("DEBUG")]
    internal static void Window(InventoryGui gui)
    {
#if DEBUG
        if (gui == null || ReferenceEquals(_windowFor, gui) || CompendiumWindow.Root == null)
        {
            return;
        }
        _windowFor = gui;
        try
        {
            var sb = new StringBuilder();
            sb.Append("Encyclopedia window layout, screen ").Append(Screen.width).Append('x').Append(Screen.height).Append(':');
            var root = CompendiumWindow.Root.transform as RectTransform;
            sb.Append("\n window: ").Append(UiUtil.Path(root)).Append(" world=").Append(UiUtil.Fmt(UiUtil.WorldRect(root)));
            var canvas = CompendiumWindow.WindowCanvas;
            var rootCanvas = CompendiumWindow.RootCanvas;
            sb.Append("\n window canvas: override=").Append(canvas != null && canvas.overrideSorting)
                .Append(" order=").Append(canvas != null ? canvas.sortingOrder : 0)
                .Append(" root canvas order=").Append(rootCanvas != null ? rootCanvas.sortingOrder : 0);
            var group = CompendiumWindow.Group;
            sb.Append("\n window group: priority=").Append(group != null ? group.m_groupPriority : 0)
                .Append(" active=").Append(group != null && group.IsActive);
            DescribeRect("title", CompendiumWindow.TitleText != null ? CompendiumWindow.TitleText.rectTransform : null, sb);
            DescribeRect("counter", CompendiumWindow.CounterText != null ? CompendiumWindow.CounterText.rectTransform : null, sb);
            DescribeRect("topic", CompendiumWindow.TopicText != null ? CompendiumWindow.TopicText.rectTransform : null, sb);
            DescribeRect("close", CompendiumWindow.CloseButton != null ? CompendiumWindow.CloseButton.transform as RectTransform : null, sb);
            DescribeRect("search", CompendiumWindow.Search != null ? CompendiumWindow.Search.transform as RectTransform : null, sb);
            DescribeRect("blocker", CompendiumWindow.Blocker, sb);
            DescribeRect("list viewport", CompendiumWindow.ListViewport, sb);
            DescribeRect("detail viewport", CompendiumWindow.DetailViewport, sb);
            var tabs = CompendiumWindow.TabButtonList;
            for (var i = 0; i < tabs.Count; i++)
            {
                DescribeRect("tab " + i, tabs[i] != null ? tabs[i].transform as RectTransform : null, sb);
            }
            var pool = CompendiumWindow.ListPool;
            sb.Append("\n list rows pooled: ").Append(pool.Count).Append(", tab rows: ").Append(CompendiumWindow.TabRowCount);
            if (pool.Count > 0)
            {
                DescribeRect("list row 0", pool[0].Rt, sb);
            }
            DescribeRect("top tab texts", CompendiumWindow.TopTextsButton != null ? CompendiumWindow.TopTextsButton.transform as RectTransform : null, sb);
            DescribeRect("top tab encyclopedia", CompendiumWindow.TopEncyclopediaButton != null
                ? CompendiumWindow.TopEncyclopediaButton.transform as RectTransform : null, sb);
            // Our additions must not cover vanilla title / close button, nor each other (top tabs vs category tabs,
            // counter, search).
            var close = CompendiumWindow.CloseButton != null ? CompendiumWindow.CloseButton.transform as RectTransform : null;
            var title = CompendiumWindow.TitleText != null ? CompendiumWindow.TitleText.rectTransform : null;
            var ours = new List<RectTransform>();
            foreach (var t in tabs)
            {
                ours.Add(t != null ? t.transform as RectTransform : null);
            }
            ours.Add(CompendiumWindow.TopTextsButton != null ? CompendiumWindow.TopTextsButton.transform as RectTransform : null);
            ours.Add(CompendiumWindow.TopEncyclopediaButton != null ? CompendiumWindow.TopEncyclopediaButton.transform as RectTransform : null);
            ours.Add(CompendiumWindow.CounterText != null ? CompendiumWindow.CounterText.rectTransform : null);
            ours.Add(CompendiumWindow.Search != null ? CompendiumWindow.Search.transform as RectTransform : null);
            for (var i = 0; i < ours.Count; i++)
            {
                var r = ours[i];
                if (r == null)
                {
                    continue;
                }
                var w = UiUtil.WorldRect(r);
                if (close != null && UiUtil.Overlaps(w, UiUtil.WorldRect(close), 1f))
                {
                    sb.Append("\n OVERLAP: ").Append(r.name).Append(" covers the close button");
                }
                if (title != null && UiUtil.Overlaps(w, UiUtil.WorldRect(title), 1f))
                {
                    sb.Append("\n OVERLAP: ").Append(r.name).Append(" covers the title");
                }
                for (var j = i + 1; j < ours.Count; j++)
                {
                    if (ours[j] != null && UiUtil.Overlaps(w, UiUtil.WorldRect(ours[j]), 1f))
                    {
                        sb.Append("\n OVERLAP: ").Append(r.name).Append(" and ").Append(ours[j].name);
                    }
                }
            }
            sb.Append("\n window tree (rects in the window's space; rows not expanded):");
            var lines = 0;
            WalkDetailed(sb, root, root, 0, 5, null, ref lines);
            DescribeCanvases(sb, gui);
            foreach (var line in CloneUtil.StripLog)
            {
                sb.Append("\n ").Append(line);
            }
            Log.Info(sb.ToString());
        }
        catch (Exception e)
        {
            Log.Info($"Encyclopedia window dump failed: {e}");
        }
#endif
    }

#if DEBUG
    private static void DescribeRect(string label, RectTransform rt, StringBuilder sb)
    {
        sb.Append("\n ").Append(label).Append(": ");
        if (rt == null)
        {
            sb.Append("none");
            return;
        }
        sb.Append('\'').Append(rt.name).Append("' active=").Append(rt.gameObject.activeInHierarchy)
            .Append(" world=").Append(UiUtil.Fmt(UiUtil.WorldRect(rt)));
    }

    private static void DescribeParent(StringBuilder sb, string label, Transform t, InventoryGui gui)
    {
        sb.Append("\n ").Append(label).Append(": ");
        if (t == null)
        {
            sb.Append("none");
            return;
        }
        sb.Append(UiUtil.Path(t)).Append(" components=[").Append(Components(t)).Append(']');
        var layout = t.GetComponent<LayoutGroup>();
        if (layout != null)
        {
            sb.Append(" layout=").Append(layout.GetType().Name).Append(" padding=").Append(layout.padding)
                .Append(" align=").Append(layout.childAlignment);
            if (layout is HorizontalOrVerticalLayoutGroup hv)
            {
                sb.Append(" spacing=").Append(hv.spacing);
            }
        }
        var group = t.GetComponentInParent<UIGroupHandler>();
        if (group != null)
        {
            sb.Append(" group=").Append(group.name).Append(" priority=").Append(group.m_groupPriority)
                .Append(" index=").Append(gui.m_uiGroups != null ? Array.IndexOf(gui.m_uiGroups, group) : -1);
        }
        var cg = t.GetComponentInParent<CanvasGroup>();
        sb.Append(" canvasGroup=").Append(cg != null ? cg.name : "none");
        if (t is RectTransform rt)
        {
            sb.Append(" world=").Append(UiUtil.Fmt(UiUtil.WorldRect(rt)));
        }
    }

    private static void DescribeControls(StringBuilder sb, Transform parent)
    {
        if (parent == null)
        {
            return;
        }
        foreach (var s in parent.GetComponentsInChildren<Selectable>(true))
        {
            if (s == null || s.transform.parent == null)
            {
                continue;
            }
            sb.Append("\n  control '").Append(s.name).Append("' ").Append(s.GetType().Name)
                .Append(" active=").Append(s.gameObject.activeInHierarchy)
                .Append(" world=").Append(UiUtil.Fmt(UiUtil.WorldRect((RectTransform)s.transform)))
                .Append(" nav=").Append(s.navigation.mode);
            if (s.navigation.mode == Navigation.Mode.Explicit)
            {
                var nav = s.navigation;
                sb.Append("{L=").Append(nav.selectOnLeft != null ? nav.selectOnLeft.name : "-")
                    .Append(" R=").Append(nav.selectOnRight != null ? nav.selectOnRight.name : "-")
                    .Append(" U=").Append(nav.selectOnUp != null ? nav.selectOnUp.name : "-")
                    .Append(" D=").Append(nav.selectOnDown != null ? nav.selectOnDown.name : "-").Append('}');
            }
            sb.Append(" transition=").Append(s.transition);
            if (s.transition == Selectable.Transition.ColorTint)
            {
                sb.Append("{target=").Append(s.targetGraphic != null ? s.targetGraphic.name : "-")
                    .Append(" normal=").Append(ColorHex(s.colors.normalColor)).Append(" x").Append(s.colors.colorMultiplier).Append('}');
            }
            if (s is Button b)
            {
                sb.Append(" handlers=[");
                for (var i = 0; i < b.onClick.GetPersistentEventCount(); i++)
                {
                    sb.Append(i > 0 ? "," : "").Append(b.onClick.GetPersistentMethodName(i));
                }
                sb.Append(']');
            }
            var pad = s.GetComponent<UIGamePad>();
            sb.Append(" pad=").Append(pad != null ? (string.IsNullOrEmpty(pad.m_zinputKey) ? pad.m_keyCode.ToString() : pad.m_zinputKey) : "none");
            var tip = s.GetComponent<UITooltip>();
            sb.Append(" tooltip=").Append(tip != null ? tip.m_topic : "none");
            sb.Append(" images=[");
            var first = true;
            foreach (var img in s.GetComponentsInChildren<Image>(true))
            {
                sb.Append(first ? "" : ", ").Append(img.name).Append(':').Append(img.sprite != null ? img.sprite.name : "-");
                first = false;
            }
            sb.Append(']');
        }
    }

    private static void DescribeGroups(StringBuilder sb, InventoryGui gui)
    {
        var groups = gui.m_uiGroups;
        sb.Append("\n focus groups under the inventory:");
        foreach (var g in gui.GetComponentsInChildren<UIGroupHandler>(true))
        {
            sb.Append("\n  '").Append(UiUtil.Path(g.transform)).Append("' priority=").Append(g.m_groupPriority)
                .Append(" index=").Append(groups != null ? Array.IndexOf(groups, g) : -1)
                .Append(" active=").Append(g.IsActive)
                .Append(" canvasGroup=").Append(g.GetComponent<CanvasGroup>() != null);
        }
        sb.Append("\n other active focus groups:");
        foreach (var g in Object.FindObjectsByType<UIGroupHandler>(FindObjectsSortMode.None))
        {
            if (!g.transform.IsChildOf(gui.transform))
            {
                sb.Append("\n  '").Append(UiUtil.Path(g.transform)).Append("' priority=").Append(g.m_groupPriority);
            }
        }
    }

    private static void DescribeCanvases(StringBuilder sb, InventoryGui gui)
    {
        var any = gui.GetComponentInParent<Canvas>(true);
        var root = any != null ? any.rootCanvas : null;
        if (root != null)
        {
            var scaler = root.GetComponent<CanvasScaler>();
            sb.Append("\n root canvas '").Append(root.name).Append("' mode=").Append(root.renderMode)
                .Append(" order=").Append(root.sortingOrder).Append(" scale=").Append(root.scaleFactor.ToString("F3"));
            if (scaler != null)
            {
                sb.Append(" scaler=").Append(scaler.uiScaleMode).Append(" ref=").Append(scaler.referenceResolution)
                    .Append(" match=").Append(scaler.matchWidthOrHeight);
            }
            foreach (var c in root.GetComponentsInChildren<Canvas>(true))
            {
                if (c != root && c.overrideSorting)
                {
                    sb.Append("\n  canvas '").Append(UiUtil.Path(c.transform)).Append("' order=").Append(c.sortingOrder)
                        .Append(" active=").Append(c.gameObject.activeInHierarchy);
                }
            }
        }
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (c.isRootCanvas && c != root)
            {
                sb.Append("\n  other root canvas '").Append(c.name).Append("' mode=").Append(c.renderMode).Append(" order=").Append(c.sortingOrder);
            }
        }
    }

    private static void DescribeTree(StringBuilder sb, string label, Transform t, int depth)
    {
        sb.Append("\n ").Append(label).Append(':');
        if (t == null)
        {
            sb.Append(" none");
            return;
        }
        var lines = 0;
        WalkDetailed(sb, t, t, 0, depth, null, ref lines);
    }

    // One line per object: name, active, components, rect (in top's space, or world when top is null), anchors, pivot,
    // size delta, texts (font, size), images (sprite, type), scroll rects (content, viewport, bars), layout groups,
    // persistent click handlers, and which TextsDialog field point at it. Rows of our list are not expanded.
    private static void WalkDetailed(StringBuilder sb, Transform t, Transform top, int level, int depth, Dictionary<Object, string> refs, ref int lines)
    {
        if (++lines > MaxLines)
        {
            if (lines == MaxLines + 1)
            {
                sb.Append("\n  ... (dump cut at ").Append(MaxLines).Append(" lines)");
            }
            return;
        }
        sb.Append("\n  ").Append(' ', level * 2).Append('\'').Append(t.name).Append("' ").Append(t.gameObject.activeSelf ? "on" : "OFF")
            .Append(" [").Append(Components(t)).Append(']');
        if (refs != null && refs.TryGetValue(t.gameObject, out var field))
        {
            sb.Append(" <= ").Append(field);
        }
        if (t is RectTransform rt)
        {
            var r = top != null ? UiUtil.RectInAncestor(rt, top) : UiUtil.WorldRect(rt);
            sb.Append(top != null ? " rect=" : " world=").Append(UiUtil.Fmt(r))
                .Append(" a=").Append(V(rt.anchorMin)).Append('-').Append(V(rt.anchorMax))
                .Append(" p=").Append(V(rt.pivot)).Append(" sd=").Append(V(rt.sizeDelta)).Append(" ap=").Append(V(rt.anchoredPosition));
            if (rt.localScale != Vector3.one)
            {
                sb.Append(" scale=").Append(rt.localScale.ToString("F2"));
            }
        }
        var text = t.GetComponent<TMP_Text>();
        if (text != null)
        {
            var s = (text.text ?? "").Replace("\n", "\\n");
            sb.Append(" text='").Append(s.Length > 40 ? s.Substring(0, 40) + "..." : s).Append("' font=")
                .Append(text.font != null ? text.font.name : "-").Append(" size=").Append(text.fontSize.ToString("F1"))
                .Append(text.enableAutoSizing ? $" auto={text.fontSizeMin:F0}-{text.fontSizeMax:F0}" : "")
                .Append(" align=").Append(text.alignment).Append(" color=").Append(ColorHex(text.color));
        }
        AppendImage(sb, t.GetComponent<Image>());
        var sr = t.GetComponent<ScrollRect>();
        if (sr != null)
        {
            sb.Append(" scroll{content=").Append(sr.content != null ? sr.content.name : "-")
                .Append(" viewport=").Append(sr.viewport != null ? sr.viewport.name : "-")
                .Append(" v=").Append(sr.vertical).Append(" h=").Append(sr.horizontal)
                .Append(" vbar=").Append(sr.verticalScrollbar != null ? sr.verticalScrollbar.name : "-")
                .Append(" move=").Append(sr.movementType).Append(" sens=").Append(sr.scrollSensitivity).Append('}');
        }
        var bar = t.GetComponent<Scrollbar>();
        if (bar != null)
        {
            sb.Append(" bar{dir=").Append(bar.direction).Append(" handle=").Append(bar.handleRect != null ? bar.handleRect.name : "-").Append('}');
        }
        var layout = t.GetComponent<LayoutGroup>();
        if (layout != null)
        {
            sb.Append(" layout{pad=").Append(layout.padding).Append(" align=").Append(layout.childAlignment);
            if (layout is HorizontalOrVerticalLayoutGroup hv)
            {
                sb.Append(" spacing=").Append(hv.spacing);
            }
            sb.Append('}');
        }
        var button = t.GetComponent<Button>();
        if (button != null)
        {
            for (var i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                sb.Append(" onClick=").Append(button.onClick.GetPersistentMethodName(i));
            }
            if (!button.interactable)
            {
                sb.Append(" notInteractable");
            }
        }
        var pad = t.GetComponent<UIGamePad>();
        if (pad != null)
        {
            sb.Append(" pad=").Append(string.IsNullOrEmpty(pad.m_zinputKey) ? pad.m_keyCode.ToString() : pad.m_zinputKey)
                .Append(pad.m_hint != null ? " padHint=" + pad.m_hint.name : "");
        }
        var group = t.GetComponent<UIGroupHandler>();
        if (group != null)
        {
            sb.Append(" group{priority=").Append(group.m_groupPriority).Append(" active=").Append(group.IsActive).Append('}');
        }
        if (level >= depth || t.name == "MC_Compendium_Row" || t.name == "MC_Compendium_Templates")
        {
            if (t.childCount > 0)
            {
                sb.Append(" (+").Append(t.childCount).Append(" children)");
            }
            return;
        }
        for (var i = 0; i < t.childCount; i++)
        {
            WalkDetailed(sb, t.GetChild(i), top, level + 1, depth, refs, ref lines);
        }
    }

    private static void AppendImage(StringBuilder sb, Image img)
    {
        if (img == null)
        {
            return;
        }
        sb.Append(" img{").Append(img.sprite != null ? img.sprite.name : "-").Append(' ').Append(img.type)
            .Append(" color=").Append(ColorHex(img.color)).Append(img.enabled ? "" : " disabled")
            .Append(img.raycastTarget ? " ray" : "").Append('}');
    }

    private static string V(Vector2 v) => $"({v.x:0.##},{v.y:0.##})";

    private static string ColorHex(Color c) => "#" + ColorUtility.ToHtmlStringRGBA(c);

    private static string Components(Transform t)
    {
        var names = new List<string>();
        foreach (var c in t.GetComponents<Component>())
        {
            if (c != null && !(c is Transform) && !(c is CanvasRenderer))
            {
                names.Add(c.GetType().Name);
            }
        }
        return string.Join(",", names);
    }
#endif
}
