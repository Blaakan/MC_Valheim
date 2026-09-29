using System;
using System.Collections.Generic;
using GUIFramework;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MC.Exploration.CompendiumEncyclopediaMod;

// Me build the window once per InventoryGui, on first open (design 3.7 "Creation" and "Layout"). Vanilla 1.0.16 dialog
// (Debug dump "Encyclopedia texts dialog source"): Texts (full screen) = blur (off), darken, Closebutton (full screen,
// transparent, OnClose = click outside close), Texts_frame (1200x800) = bkg (wood panel), topic (title), TextList (left
// pane: SkillList scroll view with ListRoot and the row template, SkillListScroll bar), TextArea (right pane: ScrollArea
// scroll view whose Content hold Name = topic and Description = text, TextScroll bar), Closebutton ("Close", bottom).
// Trap: its m_leftScrollRect field point at the RIGHT scroll view (ScrollArea). So me find the scroll views by structure:
// list = ScrollRect above m_listRoot, details = ScrollRect above m_textArea.
//  1. clone under an inactive holder (our top tabs strip on the vanilla dialog, when there, removed first), read
//     references, destroy TextsDialog (no other mod's TextsDialog patch ever run on our copy), remove leftover list
//     rows, the row template, UIGroupHandlers, ScrollRectEnsureVisible, then CloneUtil.Strip;
//  2. title text -> "Encyclopedia"; every close button (saved OnClose call: the "Close" button and the full-screen
//     click-outside one) -> our Close;
//  3. root: CanvasGroup ignoreParentGroups, own Canvas (overrideSorting, root order + 1) + GraphicRaycaster,
//     UIGroupHandler (priority above all, set at each open), WindowController, full-screen transparent blocker;
//  4. layout, at the first open once the window is live (TryLayout), all measured from the clone (math only, no
//     world transform: work at any animation scale): top tabs "Texts | Encyclopedia" on the title line, left side
//     (TopTabs, same rects as on the vanilla dialog); a band for the 8 category tabs above both panes (1 row, or 2
//     when 8 do not fit), pane boxes (their dark backgrounds with them) moved down by it; search row at the top of the
//     left pane box; "Discovered N / M" on the title line, right side; list content and our own detail content
//     (topic inside it).
internal static partial class CompendiumWindow
{
    private const float Gap = 4f;
    private const float BandBottomGap = 6f;
    private const float PaneInset = 4f;
    private const float MinTabWidth = 90f;

    private static bool _laidOut;
    private static GameObject _topStrip;
    private static Button _topTexts;
    private static Button _topEncyclopedia;
    private static Scrollbar _pendingLeftSb;
    private static Scrollbar _pendingRightSb;
    private static TMP_Text _pendingTextArea;

    private static bool TryBuild(InventoryGui gui)
    {
        try
        {
            if (Build(gui))
            {
                _dumpPending = true;
                return true;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Encyclopedia window build", e);
        }
        DestroyObjects();
        ForgetObjects();
        return false;
    }

    private static bool Refuse(string why)
    {
        Log.Warning($"Encyclopedia window cannot be built ({why}; UI mod?): no Encyclopedia tab or button for this session.");
        return false;
    }

    // First open only, right after SetActive(true): rects of an inactive hierarchy may be stale, so the layout is
    // measured once the window is live. Failure = window destroyed, top tabs and button gone for this session.
    private static bool TryLayout()
    {
        try
        {
            Canvas.ForceUpdateCanvases();
            Layout(_gui, _pendingLeftSb, _pendingRightSb);
            SetupList(_pendingLeftSb);
            SetupDetails(_pendingRightSb, _pendingTextArea);
            _laidOut = true;
            _pendingLeftSb = null;
            _pendingRightSb = null;
            _pendingTextArea = null;
            return true;
        }
        catch (Exception e)
        {
            PatchGuard.Report("Encyclopedia window layout", e);
        }
        var gui = _gui;
        IsOpen = false;
        DestroyObjects();
        ForgetObjects();
        _refused = true;
        SideButton.HideForThisSession();
        TopTabs.HideForThisSession();
        if (gui != null && gui.m_uiGroups != null && gui.m_uiGroups.Length > SideButton.SidePanelGroup)
        {
            gui.SetActiveGroup(gui.m_uiGroups[SideButton.SidePanelGroup], playSound: false);
        }
        return false;
    }

    private static bool Build(InventoryGui gui)
    {
        DebugLayoutDump.TextsSource(gui);
        var src = gui.m_textsDialog;
        if (src == null)
        {
            return Refuse("the Valheim Compendium dialog is missing");
        }
        if (gui.m_recipeElementPrefab == null)
        {
            return Refuse("the crafting list row is missing");
        }
        _gui = gui;
        var parent = src.transform.parent;
        var holder = new GameObject("MC_Compendium_Build");
        holder.SetActive(false);
        holder.transform.SetParent(parent, false);
        try
        {
            var go = Object.Instantiate(src.gameObject, holder.transform, false);
            go.name = WindowName;
            go.SetActive(false);
            // Our top tabs sit on the vanilla dialog while it is shown (we are often built from its Encyclopedia tab):
            // the copy goes, the window makes its own in the layout.
            for (var i = go.transform.childCount - 1; i >= 0; i--)
            {
                var child = go.transform.GetChild(i);
                if (child.name == TopTabs.StripName)
                {
                    Object.DestroyImmediate(child.gameObject);
                }
            }
            var dialog = go.GetComponent<TextsDialog>();
            if (dialog == null)
            {
                return Refuse("the dialog has no TextsDialog component");
            }
            var listRoot = dialog.m_listRoot;
            var topic = dialog.m_textAreaTopic;
            var textArea = dialog.m_textArea;
            var element = dialog.m_elementPrefab;
            // By structure, not by the m_leftScrollRect field (it points at the text scroll view in 1.0.16).
            var leftScroll = DialogParts.ListScroll(listRoot);
            var rightScroll = DialogParts.TextScroll(textArea);
            var leftSb = leftScroll != null && leftScroll.verticalScrollbar != null ? leftScroll.verticalScrollbar : dialog.m_leftScrollbar;
            var rightSb = rightScroll != null && rightScroll.verticalScrollbar != null ? rightScroll.verticalScrollbar : dialog.m_rightScrollbar;
            var closes = DialogParts.FindPersistent(go, "OnClose");
            Object.DestroyImmediate(dialog);
            if (listRoot == null || leftScroll == null || topic == null || textArea == null || rightScroll == null
                || ReferenceEquals(rightScroll, leftScroll) || !(go.transform is RectTransform rootRt))
            {
                return Refuse($"its list, text area or scroll views were not found: list root {listRoot != null}, list scroll view "
                              + $"{leftScroll != null}, topic {topic != null}, text area {textArea != null}, text scroll view "
                              + $"{rightScroll != null}, distinct {!ReferenceEquals(rightScroll, leftScroll)}");
            }

            // Leftovers: rows vanilla (or Jewelcrafting) put in the list, the vanilla row template (it sits in the
            // list's scroll view), vanilla focus groups, auto-scroll helper.
            for (var i = listRoot.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(listRoot.GetChild(i).gameObject);
            }
            if (element != null && element != go && element.transform.IsChildOf(go.transform) && !listRoot.IsChildOf(element.transform)
                && !rightScroll.transform.IsChildOf(element.transform))
            {
                Object.DestroyImmediate(element);
            }
            foreach (var g in go.GetComponentsInChildren<UIGroupHandler>(true))
            {
                Object.DestroyImmediate(g);
            }
            foreach (var v in go.GetComponentsInChildren<ScrollRectEnsureVisible>(true))
            {
                Object.DestroyImmediate(v);
            }
            var close = PickVisibleClose(closes);
            var title = DialogParts.FindTitle(go, topic, textArea, leftScroll, rightScroll, closes);
            CloneUtil.Strip(go, "window", Navigation.Mode.None);

            go.transform.SetParent(parent, false); // same rect as the vanilla dialog; still inactive
            _root = go;
            _rootRT = rootRt;

            var canvasGroup = go.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = go.AddComponent<CanvasGroup>();
            }
            canvasGroup.ignoreParentGroups = true; // inventory groups' CanvasGroups must not grey us out (design 1.3 trap)
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;

            var parentCanvas = parent.GetComponentInParent<Canvas>(true);
            _rootCanvas = parentCanvas != null ? parentCanvas.rootCanvas : null;
            _canvas = go.GetComponent<Canvas>();
            if (_canvas == null)
            {
                _canvas = go.AddComponent<Canvas>();
            }
            ApplySorting();
            if (go.GetComponent<GraphicRaycaster>() == null)
            {
                go.AddComponent<GraphicRaycaster>();
            }
            _group = go.AddComponent<UIGroupHandler>();
            _group.m_defaultElement = null;
            _group.m_groupPriority = TopPriority(gui);
            go.AddComponent<WindowController>();

            var templates = new GameObject("MC_Compendium_Templates", typeof(RectTransform));
            templates.SetActive(false);
            templates.transform.SetParent(rootRt, false);
            _templates = (RectTransform)templates.transform;

            var blocker = new GameObject("MC_Compendium_Blocker", typeof(RectTransform), typeof(Image));
            blocker.transform.SetParent(rootRt, false);
            blocker.transform.SetAsFirstSibling();
            var blockImage = blocker.GetComponent<Image>();
            blockImage.color = new Color(0f, 0f, 0f, 0f);
            blockImage.raycastTarget = true;
            // Invisible but drawn: a culled transparent mesh may get no depth, and then no raycast hit.
            blocker.GetComponent<CanvasRenderer>().cullTransparentMesh = false;
            _blocker = (RectTransform)blocker.transform;

            _topic = topic;
            _topic.text = "";
            _title = title != null ? title : MakeTitle();
            if (_title != null)
            {
                _title.text = Labels.WindowTitle;
            }
            _listScroll = leftScroll;
            _listRoot = listRoot;
            _detailScroll = rightScroll;
            _close = close;
            // Every saved OnClose button (the "Close" button, the full-screen click-outside one) close our window,
            // like vanilla close its dialog. Their events are new and empty after the strip.
            foreach (var b in closes)
            {
                b.onClick.AddListener(OnCloseClicked);
            }
            if (closes.Count == 0)
            {
                UiUtil.Trace("Encyclopedia window: no close button found (Esc, B and Tab still close it).");
            }

            _rowH = Mathf.Clamp(gui.m_recipeListSpace, 22f, 48f);
            _rowTemplate = RowView.BuildTemplate(gui.m_recipeElementPrefab, _templates, _rowH);
            if (_rowTemplate == null)
            {
                return Refuse("the crafting list row has no name text");
            }
            _pendingLeftSb = leftSb;
            _pendingRightSb = rightSb;
            _pendingTextArea = textArea;
            _laidOut = false;
            UiUtil.Trace($"Encyclopedia window built from '{src.name}': list '{leftScroll.name}', details '{rightScroll.name}', title "
                         + $"{(title != null ? "'" + title.name + "'" : "added")}, close buttons {closes.Count} (visible "
                         + $"'{(close != null ? close.name : "none")}'), row height {_rowH:F0}, group priority {_group.m_groupPriority}.");
            return true;
        }
        finally
        {
            Object.Destroy(holder);
        }
    }

    // The close button you see = one with a label (vanilla: "Close" at the bottom), the smallest if several; the
    // full-screen one (no label) is the click-outside area. Rects of a clone under the plain holder are 0 x 0 (no
    // RectTransform parent to stretch in), so the label decides first.
    private static Button PickVisibleClose(List<Button> closes)
    {
        Button best = null;
        var bestLabeled = false;
        var bestArea = float.MaxValue;
        foreach (var b in closes)
        {
            var rt = (RectTransform)b.transform;
            var labeled = b.GetComponentInChildren<TMP_Text>(true) != null || b.GetComponentInChildren<Text>(true) != null;
            var stretched = rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one;
            var area = stretched ? float.MaxValue : Mathf.Abs(rt.sizeDelta.x * rt.sizeDelta.y);
            if (best == null || (labeled && !bestLabeled) || (labeled == bestLabeled && area < bestArea))
            {
                best = b;
                bestLabeled = labeled;
                bestArea = area;
            }
        }
        return best != null && (bestLabeled || bestArea < float.MaxValue) ? best : null;
    }

    // No title found: a copy of the topic text at the top of the window.
    private static TMP_Text MakeTitle()
    {
        Log.Warning("Encyclopedia window: the dialog title was not found; a title was added at the top.");
        var go = Object.Instantiate(_topic.gameObject, _templates, false);
        go.name = "MC_Compendium_Title";
        CloneUtil.Strip(go, "title", Navigation.Mode.None);
        DropLayoutParts(go);
        go.transform.SetParent(_rootRT, false);
        var t = go.GetComponent<TMP_Text>();
        var r = _rootRT.rect;
        var h = Mathf.Max(24f, _topic.rectTransform.rect.height);
        UiUtil.PlaceIn(t.rectTransform, _rootRT, new Rect(r.xMin + 16f, r.yMax - h - 8f, r.width - 32f, h));
        t.horizontalAlignment = HorizontalAlignmentOptions.Center;
        return t;
    }

    private static void DropLayoutParts(GameObject go)
    {
        foreach (var c in go.GetComponents<Component>())
        {
            if (c is ILayoutController || c is LayoutElement)
            {
                Object.DestroyImmediate(c);
            }
        }
    }

    // ---------------------------------------------------------------- layout

    private static void Layout(InventoryGui gui, Scrollbar leftSb, Scrollbar rightSb)
    {
        var root = _rootRT;
        var leftRt = (RectTransform)_listScroll.transform;
        // Pane box = the scroll view's ancestor right under the object holding both (vanilla: TextList, TextArea, with
        // their dark backgrounds). Moving the boxes keep background, scroll view and bar together.
        var area = DialogParts.PaneArea(root, _listScroll, _detailScroll, leftSb, rightSb, out var leftPane, out var rightPane);
        var leftSbRt = leftSb != null ? leftSb.transform as RectTransform : null;
        var rightSbRt = rightSb != null ? rightSb.transform as RectTransform : null;
        var left = UiUtil.RectInAncestor(leftPane, root);
        if (leftSbRt != null)
        {
            left = UiUtil.Union(left, UiUtil.RectInAncestor(leftSbRt, root));
        }
        var top = area.yMax;
        var width = area.width;
        // Title line, measured before anything moves (the title never moves; the vanilla dialog has the same rect).
        var titleRect = _title != null ? UiUtil.RectInAncestor(_title.rectTransform, root) : new Rect(area.xMin, top + Gap, width, _rowH);

        // Tabs: clones of the crafting panel's Craft tab, one row of 8 (or two rows of 4 when too narrow).
        var tabSrc = TopTabs.TabSource(gui, out var tabH);
        var srcW = 120f;
        if (tabSrc != null && tabSrc.transform is RectTransform tabRt)
        {
            srcW = Mathf.Max(40f, tabRt.rect.width);
        }
        MakeTopTabs(tabSrc, area, titleRect, tabH);
        var oneRow = (width - (Tabs.Count - 1) * Gap) / Tabs.Count;
        _tabRows = oneRow >= Mathf.Min(MinTabWidth, srcW) ? 1 : 2;
        var perRow = Tabs.Count / _tabRows;
        var tabW = (width - (perRow - 1) * Gap) / perRow;
        for (var t = 0; t < Tabs.Count; t++)
        {
            var row = t / perRow;
            var col = t % perRow;
            var rect = new Rect(area.xMin + col * (tabW + Gap), top - (row + 1) * tabH - row * Gap, tabW, tabH);
            MakeTab(tabSrc, t, rect);
        }
        var band = _tabRows * tabH + (_tabRows - 1) * Gap + BandBottomGap;
        if (_tabRows > 1)
        {
            UiUtil.Trace($"Encyclopedia tabs: 8 tabs do not fit in {width:F0} units (one row would be {oneRow:F0} wide): two rows.");
        }
        ShiftTop(leftPane, band);
        ShiftTop(rightPane, band);
        if (leftSbRt != null && !leftSbRt.IsChildOf(leftPane))
        {
            ShiftTop(leftSbRt, band);
        }
        if (rightSbRt != null && !rightSbRt.IsChildOf(rightPane))
        {
            ShiftTop(rightSbRt, band);
        }
        if (!_topic.rectTransform.IsChildOf(rightPane) && !_topic.rectTransform.IsChildOf(leftPane))
        {
            MoveDown(_topic.rectTransform, band);
        }

        // Search row: top strip of the left pane box (its scroll view and bar start under it).
        var searchH = _rowH;
        var leftTop = left.yMax - band;
        var searchRect = new Rect(left.xMin + PaneInset, leftTop - PaneInset - searchH, left.width - 2f * PaneInset, searchH);
        if (MakeSearch(searchRect))
        {
            var d = searchH + 2f * PaneInset;
            ShiftTop(leftRt, d);
            if (leftSbRt != null && !leftSbRt.IsChildOf(leftRt))
            {
                ShiftTop(leftSbRt, d);
            }
        }

        // Counter on the title line, right side (ends at the panes' right edge, left of a close button there; the top
        // tabs take the left side).
        var h = Mathf.Max(_rowH, Mathf.Min(titleRect.height, 2f * _rowH));
        var counterW = Mathf.Max(80f, Mathf.Min(area.xMax - titleRect.xMax - 2f * Gap, width * 0.4f));
        var counterRect = new Rect(area.xMax - counterW, titleRect.center.y - h * 0.5f, counterW, h);
        if (_close != null && _close.transform is RectTransform closeRt)
        {
            var closeRect = UiUtil.RectInAncestor(closeRt, root);
            if (UiUtil.Overlaps(counterRect, closeRect, 1f))
            {
                counterRect.xMax = Mathf.Max(counterRect.xMin + 40f, closeRect.xMin - Gap);
            }
        }
        MakeCounter(counterRect);
        UiUtil.Trace($"Encyclopedia window layout: panes {UiUtil.Fmt(area)} (boxes '{leftPane.name}', '{rightPane.name}'), tabs {_tabRows} "
                     + $"row(s) of {tabW:F0}x{tabH:F0}, band {band:F0}, search {UiUtil.Fmt(searchRect)}, title {UiUtil.Fmt(titleRect)}, "
                     + $"counter {UiUtil.Fmt(counterRect)}.");
    }

    // Top-level tabs "Texts" | "Encyclopedia" (TopTabs): same rects as on the vanilla dialog (same measure, same
    // clone source), Encyclopedia current. No room or no source = none (Esc still closes; the vanilla dialog keeps its
    // own pair only when it has room too).
    private static void MakeTopTabs(Button tabSrc, Rect area, Rect titleRect, float tabH)
    {
        var template = tabSrc != null ? tabSrc.gameObject : _rowTemplate;
        if (template == null || !TopTabs.TryPlace(area, titleRect, tabH, out var textsRect, out var encyRect))
        {
            Log.Warning("Encyclopedia window: no room for the Texts / Encyclopedia tabs left of the title (UI mod?).");
            return;
        }
        var strip = TopTabs.MakeStrip(_rootRT, template, textsRect, encyRect, out _topTexts, out _topEncyclopedia);
        if (strip == null)
        {
            return;
        }
        TopTabs.SetCurrent(_topTexts, _topEncyclopedia, TopTabs.Choice.Encyclopedia);
        strip.SetActive(true);
        _topStrip = strip;
        UiUtil.Trace($"Encyclopedia window top tabs: texts {UiUtil.Fmt(textsRect)}, encyclopedia {UiUtil.Fmt(encyRect)}, title "
                     + $"{UiUtil.Fmt(titleRect)} (window units).");
    }

    // Move the top edge of rt down by d window units (bottom edge stay).
    private static void ShiftTop(RectTransform rt, float d)
    {
        if (rt == null)
        {
            return;
        }
        var k = UiUtil.ScaleYTo(rt.parent, _rootRT);
        rt.offsetMax = new Vector2(rt.offsetMax.x, rt.offsetMax.y - d / k);
    }

    private static void MoveDown(RectTransform rt, float d)
    {
        var k = UiUtil.ScaleYTo(rt.parent, _rootRT);
        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, rt.anchoredPosition.y - d / k);
    }

    private static void MakeTab(Button src, int tab, Rect rect)
    {
        var fromRow = src == null;
        var go = Object.Instantiate(fromRow ? _rowTemplate : src.gameObject, _templates, false);
        go.name = "MC_Compendium_Tab" + tab;
        CloneUtil.Strip(go, "tab", Navigation.Mode.None);
        var b = go.GetComponent<Button>();
        if (b == null)
        {
            Object.DestroyImmediate(go);
            return;
        }
        b.onClick.AddListener(() => OnTabClicked(tab));
        if (fromRow)
        {
            var icon = go.transform.Find("icon");
            if (icon != null)
            {
                icon.gameObject.SetActive(false);
            }
        }
        var label = go.GetComponentInChildren<TMP_Text>(false);
        if (label != null)
        {
            var size = label.enableAutoSizing ? label.fontSizeMax : label.fontSize;
            label.enableAutoSizing = true;
            label.fontSizeMax = size;
            label.fontSizeMin = Mathf.Min(10f, size);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.text = Tabs.LocalizedLabel((CatalogTab)tab);
        }
        else
        {
            var legacy = go.GetComponentInChildren<Text>(true);
            if (legacy != null)
            {
                legacy.text = Tabs.LocalizedLabel((CatalogTab)tab);
            }
        }
        go.SetActive(true);
        go.transform.SetParent(_rootRT, false);
        UiUtil.PlaceIn((RectTransform)go.transform, _rootRT, rect);
        TabButtons[tab] = b;
        TabLabels[tab] = label;
    }

    // Build menu search field (fallback chat input), as Crafting Search and Sort does. Its saved listeners point at
    // vanilla objects: muted, never replaced (GuiInputField.Start add its own navigation listeners to them).
    private static bool MakeSearch(Rect rect)
    {
        GuiInputField src = null;
        var hud = Hud.instance;
        if (hud != null && hud.m_buildUi != null)
        {
            src = hud.m_buildUi.m_searchField;
        }
        if (src == null && Chat.instance != null)
        {
            src = Chat.instance.m_input;
        }
        if (src == null)
        {
            UiUtil.Trace("No vanilla text field to copy: the Encyclopedia search field is not shown.");
            return false;
        }
        var go = Object.Instantiate(src.gameObject, _templates, false);
        go.name = "MC_Compendium_Search";
        var field = go.GetComponent<GuiInputField>();
        if (field == null)
        {
            Object.DestroyImmediate(go);
            return false;
        }
        Mute(field.onValueChanged);
        Mute(field.onEndEdit);
        Mute(field.onSubmit);
        Mute(field.onSelect);
        Mute(field.onDeselect);
        Mute(field.OnInputSubmit);
        CloneUtil.Strip(go, "search field", Navigation.Mode.None);
        go.SetActive(true); // chat input source is inactive
        go.transform.SetParent(_rootRT, false);
        UiUtil.PlaceIn((RectTransform)go.transform, _rootRT, rect);

        field.lineType = TMP_InputField.LineType.SingleLine;
        field.characterLimit = 40;
        field.restoreOriginalTextOnEscape = false;
        field.onFocusSelectAll = true;
        field.readOnly = false;
        field.interactable = true;
        var name = _rowTemplate.transform.Find("name").GetComponent<TMP_Text>();
        var size = name.enableAutoSizing ? name.fontSizeMax : name.fontSize;
        SetFontSize(field.textComponent, size);
        if (field.placeholder is TMP_Text placeholder)
        {
            SetFontSize(placeholder, size);
            placeholder.text = Labels.SearchPlaceholder;
        }
        field.SetTextWithoutNotify("");
        field.onValueChanged.AddListener(OnSearchChanged);
        field.onEndEdit.AddListener(_ =>
        {
            try
            {
                ApplySearchNow();
            }
            catch (Exception e)
            {
                PatchGuard.Report("Encyclopedia search end", e);
            }
        });
        _search = field;
        return true;
    }

    private static void Mute(UnityEventBase e)
    {
        if (e == null)
        {
            return;
        }
        for (var i = 0; i < e.GetPersistentEventCount(); i++)
        {
            e.SetPersistentListenerState(i, UnityEventCallState.Off);
        }
    }

    private static void SetFontSize(TMP_Text text, float size)
    {
        if (text == null || size <= 0f)
        {
            return;
        }
        text.enableAutoSizing = false;
        text.fontSize = size;
    }

    // Counter: copy of the detail text (m_textArea: the game's body font), right-aligned, light grey.
    private static void MakeCounter(Rect rect)
    {
        var src = _pendingTextArea != null ? _pendingTextArea : _topic;
        var go = Object.Instantiate(src.gameObject, _templates, false);
        go.name = "MC_Compendium_Counter";
        CloneUtil.Strip(go, "counter", Navigation.Mode.None);
        DropLayoutParts(go);
        for (var i = go.transform.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
        }
        go.SetActive(true);
        go.transform.SetParent(_rootRT, false);
        var t = go.GetComponent<TMP_Text>();
        UiUtil.PlaceIn(t.rectTransform, _rootRT, rect);
        var size = src.enableAutoSizing ? src.fontSizeMax : src.fontSize;
        t.enableAutoSizing = true;
        t.fontSizeMax = Mathf.Max(12f, size);
        t.fontSizeMin = 10f;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.horizontalAlignment = HorizontalAlignmentOptions.Right;
        t.verticalAlignment = VerticalAlignmentOptions.Middle;
        t.color = new Color(0.85f, 0.85f, 0.85f, 1f);
        t.raycastTarget = false;
        t.text = "";
        _counter = t;
    }

    // List: content = the clone's m_listRoot, top-stretched in the viewport; height set per bind.
    private static void SetupList(Scrollbar leftSb)
    {
        _listViewport = _listScroll.viewport != null ? _listScroll.viewport : (RectTransform)_listScroll.transform;
        if (_listScroll.content != _listRoot)
        {
            _listScroll.content = _listRoot;
        }
        _listScroll.horizontal = false;
        _listScroll.vertical = true;
        var stretched = Mathf.Approximately(_listRoot.anchorMin.x, 0f) && Mathf.Approximately(_listRoot.anchorMax.x, 1f);
        var leftInset = stretched ? _listRoot.offsetMin.x : 0f;
        var rightInset = stretched ? -_listRoot.offsetMax.x : 0f;
        UiUtil.TopStretch(_listRoot, leftInset, rightInset, 0f, Mathf.Max(1f, _listViewport.rect.height));
        if (_listScroll.verticalScrollbar == null && leftSb != null)
        {
            _listScroll.verticalScrollbar = leftSb;
        }
        _listScroll.onValueChanged.AddListener(OnListScrolled);
    }

    /// <summary>WindowController, first frame after the first open: layout dump (Debug build) once per window.</summary>
    internal static void AfterFirstFrame()
    {
        if (!_dumpPending || _gui == null)
        {
            return;
        }
        _dumpPending = false;
        DebugLayoutDump.Window(_gui);
    }
}
