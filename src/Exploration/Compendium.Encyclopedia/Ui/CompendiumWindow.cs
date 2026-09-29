using System;
using System.Collections.Generic;
using GUIFramework;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MC.Exploration.CompendiumEncyclopediaMod;

// Me = the Encyclopedia window (design 3.7-3.8). Stripped clone of the Valheim Compendium dialog (m_textsDialog), shown
// in its place by the Encyclopedia top tab (TopTabs), with: the two top tabs Texts | Encyclopedia, 8 category tabs,
// search row, virtualized list on the left (rows = clones of the crafting row), detail pane on the
// right (pooled lines from DetailBuilder). Modal: own UIGroupHandler above every other group (gamepad), own Canvas
// sorted above the inventory with a full-screen transparent blocker (mouse). Built on first open per InventoryGui
// (CompendiumWindow.Build.cs), details in CompendiumWindow.Details.cs. Only renders ListRow / DetailView: never an
// entry's real name or sprite by itself (spoiler rule). Session memory (tab, selection, search) until logout.
internal static partial class CompendiumWindow
{
    internal const string WindowName = "MC_Compendium_Window";
    private const float SearchDelay = 0.1f;
    private static readonly int VisibleParam = Animator.StringToHash("visible");

    private struct SelKey
    {
        internal bool Set;
        internal EntryKind Kind;
        internal string Key;

        internal static SelKey Of(Entry e) => e == null ? default : new SelKey { Set = true, Kind = e.Kind, Key = e.Key };

        internal bool Is(Entry e) => Set && e != null && e.Kind == Kind && e.Key == Key;
    }

    // ---------------------------------------------------------------- objects (per InventoryGui)

    private static InventoryGui _gui;
    private static bool _refused;
    private static GameObject _root;
    private static RectTransform _rootRT;
    private static Canvas _canvas;
    private static Canvas _rootCanvas;
    private static UIGroupHandler _group;
    private static RectTransform _blocker;
    private static Rect _blockerRect;
    private static RectTransform _templates;
    private static GameObject _rowTemplate;
    private static ScrollRect _listScroll;
    private static RectTransform _listRoot;
    private static RectTransform _listViewport;
    private static ScrollRect _detailScroll;
    private static RectTransform _detailViewport;
    private static RectTransform _detailContent;
    private static TMP_Text _topic;
    private static TMP_Text _title;
    private static TMP_Text _counter;
    private static Button _close;
    private static readonly Button[] TabButtons = new Button[Tabs.Count];
    private static readonly TMP_Text[] TabLabels = new TMP_Text[Tabs.Count];
    private static GuiInputField _search;
    private static float _rowH = 30f;
    private static int _tabRows;
    private static bool _dumpPending;
    private static readonly List<RowView> Pool = new List<RowView>();
    private static int _poolNeed;

    // ---------------------------------------------------------------- data (per open)

    private static Catalog _catalog;
    private static Knowledge _k;
    private static List<ListRow> _rows = new List<ListRow>();
    private static bool _preparing;
    private static int _selected = -1;

    // ---------------------------------------------------------------- session (until logout)

    private static InventoryGui _sessionGui;
    private static int _tab;
    private static string _searchText = "";
    private static readonly SelKey[] LastSel = new SelKey[Tabs.Count];
    private static SelKey _searchSel;
    private static float _searchDue = -1f;

    internal static bool IsOpen { get; private set; }

    /// <summary>Window refused (cannot be built) on this InventoryGui: side button stay away until the next one.</summary>
    internal static bool RefusedFor(InventoryGui gui) => _refused && gui != null && ReferenceEquals(_sessionGui, gui);

#if DEBUG
    /// <summary>Self tests play a refused build on this InventoryGui, then put the flag back. Objects not touched.</summary>
    internal static void SetRefusedForTest(InventoryGui gui, bool refused)
    {
        if (!ReferenceEquals(_sessionGui, gui))
        {
            ForgetObjects();
            ResetSession();
            _sessionGui = gui;
        }
        _refused = refused;
    }
#endif

    internal static GuiInputField SearchField => _search;

    // ---------------------------------------------------------------- open / close

    /// <summary>
    /// Encyclopedia top tab, raven redirect, opt-in side button (and self tests). Guarded: only with the inventory
    /// shown and a live local player. True = the window is open now.
    /// </summary>
    internal static bool Open()
    {
        var gui = InventoryGui.instance;
        if (gui == null || gui.m_animator == null || !gui.m_animator.GetBool(VisibleParam))
        {
            return false;
        }
        var p = Player.m_localPlayer;
        if (p == null || p.IsDead() || p.IsTeleporting() || p.InCutscene())
        {
            return false;
        }
        if (!ReferenceEquals(_sessionGui, gui))
        {
            // New InventoryGui = new session (logout, other world): old objects died with the old one.
            ForgetObjects();
            ResetSession();
            _sessionGui = gui;
        }
        if (IsOpen)
        {
            return true;
        }
        if (_root == null)
        {
            if (_refused)
            {
                return false;
            }
            if (!TryBuild(gui))
            {
                _refused = true;
                SideButton.HideForThisSession();
                TopTabs.HideForThisSession();
                return false;
            }
        }

        CloseVanillaDialogs(gui);
        var groups = gui.m_uiGroups;
        if (groups != null && groups.Length > SideButton.SidePanelGroup && groups[SideButton.SidePanelGroup] != null)
        {
            gui.SetActiveGroup(groups[SideButton.SidePanelGroup], playSound: false);
        }
        _group.m_groupPriority = TopPriority(gui);
        _root.SetActive(true);
        IsOpen = true;
        if (!_laidOut && !TryLayout())
        {
            return false;
        }
        _rootRT.SetAsLastSibling();
        ApplySorting();
        UpdateBlocker();
        RefreshTabLabels();
        TopTabs.SetCurrent(_topTexts, _topEncyclopedia, TopTabs.Choice.Encyclopedia);
        if (_search != null)
        {
            _search.SetTextWithoutNotify(_searchText);
        }
        OwnRecords.RecordCurrentBiome();
        Refresh(resetScroll: true, logKnowledge: true);
        return true;
    }

    /// <summary>
    /// Close the window only. <paramref name="selectButton"/>: with a controller, put the selection back on our side
    /// button when it is there, else on the game's Valheim Compendium button (Esc/B/close button). From Hide
    /// (inventory going away), our Texts tab or a vanilla dialog opening: false.
    /// Cheap when closed (Hide runs every frame while dead).
    /// </summary>
    internal static void Close(bool selectButton)
    {
        if (!IsOpen)
        {
            return;
        }
        IsOpen = false;
        try
        {
            DropSearchFocus();
        }
        catch (Exception e)
        {
            PatchGuard.Report("Encyclopedia close (search focus)", e);
        }
        if (_searchDue >= 0f)
        {
            _searchDue = -1f; // text kept in _searchText; applied at the next open
        }
        if (_root != null)
        {
            _root.SetActive(false);
        }
        if (selectButton && ZInput.IsGamepadActive() && EventSystem.current != null)
        {
            var target = SideButton.Button != null && SideButton.Button.gameObject.activeInHierarchy
                ? SideButton.Button
                : TopTabs.RavenButton(InventoryGui.instance);
            if (target != null && target.gameObject.activeInHierarchy)
            {
                EventSystem.current.SetSelectedGameObject(target.gameObject);
            }
        }
    }

    /// <summary>Feature off: close, destroy everything, give the gamepad focus back to the side panel.</summary>
    internal static void Destroy()
    {
        var wasOpen = IsOpen;
        var gui = _gui;
        Close(selectButton: false);
        DestroyObjects();
        ForgetObjects();
        FocusGuard.Reset();
        if (wasOpen && gui != null && ReferenceEquals(gui, InventoryGui.instance))
        {
            var groups = gui.m_uiGroups;
            if (groups != null && groups.Length > SideButton.SidePanelGroup && groups[SideButton.SidePanelGroup] != null)
            {
                gui.SetActiveGroup(groups[SideButton.SidePanelGroup], playSound: false);
            }
        }
    }

    // InventoryGui.Update prefix, only while open: Esc / B close only the window when vanilla's own key gate would
    // run (design 1.2, 3.8). m_shownFrames = 0 make vanilla's "m_shownFrames > 1" false this frame: no Hide.
    internal static void HandleCloseKeys(InventoryGui gui)
    {
        if (gui == null || gui.m_craftTimer >= 0f)
        {
            return;
        }
        if ((Chat.instance != null && Chat.instance.HasFocus()) || Console.IsVisible() || Menu.IsVisible())
        {
            return;
        }
        if (TextViewer.instance == null || TextViewer.instance.IsVisible())
        {
            return;
        }
        var p = Player.m_localPlayer;
        if (p == null || p.InCutscene() || GameCamera.InFreeFly() || Minimap.IsOpen())
        {
            return;
        }
        if (!ZInput.GetKeyDown(KeyCode.Escape, logWarning: false) && !ZInput.GetButtonDown("JoyButtonB"))
        {
            return;
        }
        CloseFromKey(gui);
    }

    /// <summary>What Esc/B do once the gate passed (self test call it directly: a key press cannot be faked).</summary>
    internal static void CloseFromKey(InventoryGui gui)
    {
        Close(selectButton: true);
        ZInput.ResetButtonStatus("JoyButtonB");
        gui.m_shownFrames = 0;
    }

    private static void OnCloseClicked()
    {
        try
        {
            Close(selectButton: true);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Encyclopedia close button", e);
        }
    }

    // Vanilla dialogs through their own close paths; split / variant dialog, drag, foreign focused text field.
    // Valheim Compendium part = the normal path: our Encyclopedia top tab sits on it, so opening us from there closes it
    // the vanilla way (OnClose, Jewelcrafting clean up there). Skills / Trophies / Achievements part = safety net only:
    // while one is open, its priority-2 group make the side row non-interactable (root CanvasGroup, UIGroupHandler), so
    // the opt-in button and its pad cannot reach Open then (same lock as the vanilla side buttons). Drag, split /
    // variant dialog and text field parts still matter (item held on the cursor, Crafting Search and Sort field focused).
    private static void CloseVanillaDialogs(InventoryGui gui)
    {
        if (gui.m_textsDialog != null && gui.m_textsDialog.gameObject.activeSelf)
        {
            gui.m_textsDialog.OnClose();
        }
        if (gui.m_skillsDialog != null && gui.m_skillsDialog.gameObject.activeSelf)
        {
            gui.m_skillsDialog.OnClose();
        }
        if (gui.m_trophiesPanel != null && gui.m_trophiesPanel.activeSelf)
        {
            gui.OnCloseTrophies();
        }
        if (gui.m_achievementsPanel != null && gui.m_achievementsPanel.gameObject.activeSelf)
        {
            gui.OnCloseAchievements();
        }
        if (gui.m_splitDialog != null && gui.m_splitDialog.IsActive)
        {
            gui.HideSplitDialog();
        }
        if (gui.m_variantDialog != null && gui.m_variantDialog.gameObject.activeSelf)
        {
            gui.m_variantDialog.gameObject.SetActive(false);
        }
        gui.SetupDragItem(null, null, 1);
        var es = EventSystem.current;
        var selected = es != null ? es.currentSelectedGameObject : null;
        if (selected != null && selected.GetComponent<TMP_InputField>() != null && (_search == null || selected != _search.gameObject))
        {
            es.SetSelectedGameObject(null); // e.g. Crafting Search and Sort's field let go of the keyboard
        }
    }

    // Our group above every group under the inventory and every active group in the scene (HUD, menus).
    private static int TopPriority(InventoryGui gui)
    {
        var max = int.MinValue;
        foreach (var g in gui.GetComponentsInChildren<UIGroupHandler>(true))
        {
            if (g != null && g != _group)
            {
                max = Mathf.Max(max, g.m_groupPriority);
            }
        }
        foreach (var g in Object.FindObjectsByType<UIGroupHandler>(FindObjectsSortMode.None))
        {
            if (g != null && g != _group)
            {
                max = Mathf.Max(max, g.m_groupPriority);
            }
        }
        return max == int.MinValue ? 1 : max + 1;
    }

    // Set again after activation: a Canvas added while inactive may reset overrideSorting when it wakes.
    private static void ApplySorting()
    {
        if (_canvas == null)
        {
            return;
        }
        _canvas.overrideSorting = true;
        if (_rootCanvas != null)
        {
            _canvas.sortingLayerID = _rootCanvas.sortingLayerID;
            _canvas.sortingOrder = _rootCanvas.sortingOrder + 1;
        }
        else
        {
            _canvas.sortingOrder = 1;
        }
    }

    // Blocker = whole root canvas in window space (catch every click outside the window frame). Every frame while
    // open (WindowController): 4 corners, set only when changed.
    internal static void UpdateBlocker()
    {
        if (_blocker == null || _rootRT == null)
        {
            return;
        }
        Rect r;
        var canvasRt = _rootCanvas != null ? _rootCanvas.transform as RectTransform : null;
        if (canvasRt == null || !UiUtil.TryRectIn(canvasRt, _rootRT, out r))
        {
            r = new Rect(-10000f, -10000f, 20000f, 20000f); // zero scale (animation): cover a lot
        }
        else
        {
            r = Rect.MinMaxRect(r.xMin - 8f, r.yMin - 8f, r.xMax + 8f, r.yMax + 8f);
        }
        if (r == _blockerRect)
        {
            return;
        }
        _blockerRect = r;
        UiUtil.PlaceIn(_blocker, _rootRT, r);
    }

    // ---------------------------------------------------------------- events

    /// <summary>CatalogService.CatalogReady: a build finished while open = bind the list now.</summary>
    internal static void OnCatalogReady()
    {
        if (IsOpen)
        {
            Refresh(resetScroll: true);
        }
    }

    /// <summary>ShowUndiscovered / RevealAll changed: apply to an open window at once.</summary>
    internal static void OnDisplaySettingsChanged()
    {
        if (IsOpen)
        {
            Refresh(resetScroll: false);
        }
    }

    // ---------------------------------------------------------------- refresh

    private static bool Searching => Names.SearchKey(_searchText ?? "").Length > 0;

    // Catalog (maybe still building), new knowledge snapshot, rows, counter, selection, details.
    private static void Refresh(bool resetScroll, bool logKnowledge = false)
    {
        if (!IsOpen || _root == null)
        {
            return;
        }
        _catalog = CatalogService.EnsureReady();
        if (_catalog == null)
        {
            ShowPreparing();
            return;
        }
        _preparing = false;
        _k = Knowledge.Take(_catalog, Plugin.RevealAllOn);
        if (logKnowledge)
        {
            Log.Debug(_k.Summary());
        }
        _rows = Searching
            ? ListBuilder.BuildSearch(_catalog, _k, _searchText)
            : ListBuilder.BuildTab(_catalog, _k, (CatalogTab)_tab, Plugin.ShowUndiscoveredOn);
        if (_counter != null)
        {
            _counter.text = string.Format(Labels.DiscoveredFormat, _k.DiscoveredCount, _k.ListedCount);
        }
        _selected = FindRow(Searching ? _searchSel : LastSel[_tab]);
        if (_selected < 0)
        {
            _selected = NextEntryRow(-1, 1);
        }
        UpdateTabs();
        BindList(resetScroll);
        if (_selected >= 0)
        {
            EnsureVisible(_selected);
        }
        ShowDetails(_selected >= 0 ? _rows[_selected].Entry : null);
    }

    private static void ShowPreparing()
    {
        _preparing = true;
        _k = null;
        _rows = new List<ListRow>();
        _selected = -1;
        if (_counter != null)
        {
            _counter.text = "";
        }
        UpdateTabs();
        BindList(resetScroll: true);
        ShowDetails(null);
    }

    private static int FindRow(SelKey key)
    {
        if (!key.Set)
        {
            return -1;
        }
        for (var i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].Kind == ListRowKind.Entry && key.Is(_rows[i].Entry))
            {
                return i;
            }
        }
        return -1;
    }

    private static int NextEntryRow(int from, int dir)
    {
        for (var i = from + dir; i >= 0 && i < _rows.Count; i += dir)
        {
            if (_rows[i].Kind == ListRowKind.Entry)
            {
                return i;
            }
        }
        return -1;
    }

    // ---------------------------------------------------------------- tabs

    private static void RefreshTabLabels()
    {
        for (var t = 0; t < Tabs.Count; t++)
        {
            if (TabLabels[t] != null)
            {
                TabLabels[t].text = Tabs.LocalizedLabel((CatalogTab)t);
            }
        }
    }

    // Current tab = not interactable (vanilla look). While searching every tab is clickable (a click leave the search).
    private static void UpdateTabs()
    {
        var searching = Searching;
        for (var t = 0; t < Tabs.Count; t++)
        {
            var b = TabButtons[t];
            if (b != null)
            {
                var on = searching || t != _tab;
                if (b.interactable != on)
                {
                    b.interactable = on;
                }
            }
        }
    }

    private static void OnTabClicked(int tab)
    {
        try
        {
            SelectTab(tab);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Encyclopedia tab click", e);
        }
    }

    /// <summary>Show a tab (leave the search if any). New knowledge snapshot.</summary>
    internal static void SelectTab(int tab)
    {
        if (!IsOpen || tab < 0 || tab >= Tabs.Count)
        {
            return;
        }
        ClearSearchText();
        _tab = tab;
        Refresh(resetScroll: true);
    }

    internal static void CycleTab(int dir) => SelectTab(((_tab + dir) % Tabs.Count + Tabs.Count) % Tabs.Count);

    // ---------------------------------------------------------------- selection

    private static void OnRowClicked(RowView row)
    {
        if (row.Bound < 0 || row.Bound >= _rows.Count || _preparing)
        {
            return;
        }
        if (GamepadRumble.instance != null)
        {
            GamepadRumble.instance.PlayGlobalSelectVibration();
        }
        Select(row.Bound);
    }

    private static void Select(int index)
    {
        if (index < 0 || index >= _rows.Count || _rows[index].Kind != ListRowKind.Entry)
        {
            return;
        }
        _selected = index;
        var entry = _rows[index].Entry;
        if (Searching)
        {
            _searchSel = SelKey.Of(entry);
        }
        else
        {
            LastSel[_tab] = SelKey.Of(entry);
        }
        UpdateSelectionMarks();
        EnsureVisible(index);
        ShowDetails(entry);
    }

    /// <summary>Gamepad: next / previous entry row (headers skipped). True = moved.</summary>
    internal static bool MoveSelection(int dir)
    {
        if (_preparing || _rows.Count == 0)
        {
            return false;
        }
        var next = NextEntryRow(_selected < 0 ? (dir > 0 ? -1 : _rows.Count) : _selected, dir);
        if (next < 0)
        {
            return false;
        }
        Select(next);
        return true;
    }

    /// <summary>Reference link: open the target's tab and row (leave the search).</summary>
    internal static void OpenEntry(Entry e)
    {
        if (!IsOpen || e == null || _k == null || !_k.IsKnown(e))
        {
            return;
        }
        ClearSearchText();
        _tab = (int)e.Tab;
        LastSel[_tab] = SelKey.Of(e);
        Refresh(resetScroll: true);
    }

    // ---------------------------------------------------------------- virtualized list

    private static void BindList(bool resetScroll)
    {
        if (_listRoot == null)
        {
            return;
        }
        SizeListContent();
        if (resetScroll && _listScroll != null)
        {
            _listScroll.StopMovement();
            _listScroll.verticalNormalizedPosition = 1f;
        }
        Relayout(force: true);
    }

    private static int RowCount => _preparing ? 1 : _rows.Count;

    private static void SizeListContent()
    {
        ListMetrics(out _, out var viewH);
        var h = Mathf.Max(viewH, RowCount * _rowH);
        if (Mathf.Abs(_listRoot.rect.height - h) > 0.5f)
        {
            _listRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
        }
    }

    // Offset = how far the content top is above the viewport top; both in content units.
    private static void ListMetrics(out float offset, out float viewH)
    {
        // Fallback (zero scale during the open animation): content top-anchored in the viewport, so y = scroll.
        offset = Mathf.Max(0f, _listRoot.anchoredPosition.y);
        viewH = _listViewport != null ? _listViewport.rect.height : 300f;
        if (_listViewport != null && UiUtil.TryRectIn(_listViewport, _listRoot, out var r) && r.height > 1f)
        {
            offset = Mathf.Max(0f, _listRoot.rect.yMax - r.yMax);
            viewH = r.height;
        }
    }

    private static void OnListScrolled(Vector2 _)
    {
        try
        {
            Relayout(force: false);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Encyclopedia list scroll", e);
        }
    }

    // Row for list index i = Pool[i % need]: scrolling by one row rebinds one row object.
    private static void Relayout(bool force)
    {
        if (_listRoot == null || _rowTemplate == null)
        {
            return;
        }
        ListMetrics(out var offset, out var viewH);
        var n = RowCount;
        var need = Mathf.Max(1, Mathf.CeilToInt(viewH / _rowH) + 2);
        if (need != _poolNeed)
        {
            _poolNeed = need;
            force = true;
        }
        while (Pool.Count < need)
        {
            Pool.Add(RowView.Create(_rowTemplate, _templates, _listRoot, "list row", OnRowClicked));
        }
        var first = Mathf.Clamp(Mathf.FloorToInt(offset / _rowH), 0, Mathf.Max(0, n - 1));
        for (var j = 0; j < Pool.Count; j++)
        {
            if (j >= need)
            {
                Pool[j].Show(false);
                Pool[j].Bound = -1;
            }
        }
        for (var s = 0; s < need; s++)
        {
            var idx = first + s;
            var row = Pool[idx % need];
            if (idx >= n)
            {
                row.Show(false);
                row.Bound = -1;
                continue;
            }
            if (force || row.Bound != idx || !row.Go.activeSelf)
            {
                Bind(row, idx);
            }
        }
    }

    private static void Bind(RowView row, int idx)
    {
        row.Bound = idx;
        UiUtil.TopStretch(row.Rt, 0f, 0f, idx * _rowH, _rowH);
        row.Show(true);
        if (_preparing)
        {
            row.Target = null;
            row.SetMode(RowView.Mode.Text, clickable: false);
            row.SetText(Labels.Preparing, RowView.UnknownColor);
            row.SetSelected(false);
            return;
        }
        var r = _rows[idx];
        if (r.Kind == ListRowKind.Header)
        {
            row.Target = null;
            row.SetMode(RowView.Mode.Header, clickable: false);
            row.SetText(r.Text, RowView.HeaderColor);
            return;
        }
        row.Target = r.Entry;
        row.SetMode(RowView.Mode.Entry, clickable: true);
        row.SetText(r.Text, r.Known ? RowView.KnownColor : RowView.UnknownColor);
        row.SetIcon(r.IconKind, r.Icon);
        row.SetSelected(idx == _selected);
    }

    private static void UpdateSelectionMarks()
    {
        foreach (var row in Pool)
        {
            if (row.Bound >= 0 && row.Current == RowView.Mode.Entry)
            {
                row.SetSelected(row.Bound == _selected);
            }
        }
    }

    private static void EnsureVisible(int idx)
    {
        if (_listScroll == null || idx < 0)
        {
            return;
        }
        ListMetrics(out var offset, out var viewH);
        var contentH = Mathf.Max(viewH, RowCount * _rowH);
        var range = contentH - viewH;
        if (range <= 0.5f)
        {
            return;
        }
        var top = idx * _rowH;
        float want;
        if (top < offset)
        {
            want = top;
        }
        else if (top + _rowH > offset + viewH)
        {
            want = top + _rowH - viewH;
        }
        else
        {
            return;
        }
        _listScroll.StopMovement();
        _listScroll.verticalNormalizedPosition = 1f - Mathf.Clamp01(want / range);
        Relayout(force: false);
    }

    // ---------------------------------------------------------------- search

    private static void OnSearchChanged(string text)
    {
        try
        {
            _searchText = text ?? "";
            _searchDue = Time.unscaledTime + SearchDelay;
        }
        catch (Exception e)
        {
            PatchGuard.Report("Encyclopedia search", e);
        }
    }

    /// <summary>WindowController: apply the search 0.1 s after the last keystroke.</summary>
    internal static void TickSearch()
    {
        if (_searchDue >= 0f && Time.unscaledTime >= _searchDue)
        {
            _searchDue = -1f;
            Refresh(resetScroll: true);
        }
    }

    /// <summary>Field let go of the keyboard (Enter, Esc, click elsewhere): apply a pending search now.</summary>
    internal static void ApplySearchNow()
    {
        if (_searchDue >= 0f)
        {
            _searchDue = -1f;
            Refresh(resetScroll: true);
        }
    }

    private static void ClearSearchText()
    {
        _searchDue = -1f;
        if (_searchText.Length == 0)
        {
            return;
        }
        _searchText = "";
        if (_search != null)
        {
            _search.SetTextWithoutNotify("");
        }
    }

    internal static void DropSearchFocus()
    {
        var field = _search;
        if (field == null)
        {
            return;
        }
        if (field.isFocused)
        {
            field.DeactivateInputField();
        }
        ReleaseSearchSelection();
    }

    /// <summary>GuiInputField give EventSystem navigation back only on deselect.</summary>
    internal static void ReleaseSearchSelection()
    {
        var es = EventSystem.current;
        if (es != null && _search != null && es.currentSelectedGameObject == _search.gameObject)
        {
            es.SetSelectedGameObject(null);
        }
    }

    // ---------------------------------------------------------------- gamepad scroll

    /// <summary>Right stick Y (> 0 = down), like the vanilla texts dialog.</summary>
    internal static void ScrollDetails(float stickY)
    {
        if (_detailScroll == null || _detailContent == null || _detailViewport == null)
        {
            return;
        }
        var contentH = _detailContent.rect.height;
        var viewH = _detailViewport.rect.height;
        if (contentH <= viewH + 0.5f)
        {
            return;
        }
        var size = viewH / contentH;
        _detailScroll.verticalNormalizedPosition =
            Mathf.Clamp01(_detailScroll.verticalNormalizedPosition - stickY * 10f * Time.unscaledDeltaTime * (1f - size));
    }

    // ---------------------------------------------------------------- forget

    private static void DestroyObjects()
    {
        if (_root != null)
        {
            _root.SetActive(false);
            Object.Destroy(_root); // blocker, canvas, group, pools, templates go with it
        }
    }

    private static void ForgetObjects()
    {
        IsOpen = false;
        _gui = null;
        _refused = false;
        _root = null;
        _rootRT = null;
        _canvas = null;
        _rootCanvas = null;
        _group = null;
        _blocker = null;
        _blockerRect = default;
        _templates = null;
        _rowTemplate = null;
        _listScroll = null;
        _listRoot = null;
        _listViewport = null;
        _detailScroll = null;
        _detailViewport = null;
        _detailContent = null;
        _topic = null;
        _title = null;
        _counter = null;
        _close = null;
        Array.Clear(TabButtons, 0, TabButtons.Length);
        Array.Clear(TabLabels, 0, TabLabels.Length);
        _search = null;
        _dumpPending = false;
        _laidOut = false;
        _topStrip = null;
        _topTexts = null;
        _topEncyclopedia = null;
        _pendingLeftSb = null;
        _pendingRightSb = null;
        _pendingTextArea = null;
        Pool.Clear();
        _poolNeed = 0;
        ForgetDetails();
        _catalog = null;
        _k = null;
        _rows = new List<ListRow>();
        _preparing = false;
        _selected = -1;
        _searchDue = -1f;
    }

    private static void ResetSession()
    {
        _tab = 0;
        _searchText = "";
        Array.Clear(LastSel, 0, LastSel.Length);
        _searchSel = default;
    }

    // ---------------------------------------------------------------- read-only views (self tests, dumps)

    internal static GameObject Root => _root;
    internal static Canvas WindowCanvas => _canvas;
    internal static Canvas RootCanvas => _rootCanvas;
    internal static UIGroupHandler Group => _group;
    internal static RectTransform Blocker => _blocker;
    internal static int CurrentTab => _tab;
    internal static bool Preparing => _preparing;
    internal static IReadOnlyList<ListRow> Rows => _rows;
    internal static Entry SelectedEntry => _selected >= 0 && _selected < _rows.Count ? _rows[_selected].Entry : null;
    internal static IReadOnlyList<RowView> ListPool => Pool;
    internal static int TabRowCount => _tabRows;
    internal static Knowledge CurrentKnowledge => _k;
    internal static TMP_Text TitleText => _title;
    internal static TMP_Text CounterText => _counter;
    internal static TMP_Text TopicText => _topic;
    internal static IReadOnlyList<Button> TabButtonList => TabButtons;
    internal static Button CloseButton => _close;
    internal static GuiInputField Search => _search;
    internal static RectTransform ListViewport => _listViewport;
    internal static RectTransform DetailViewport => _detailViewport;
    internal static GameObject TopStrip => _topStrip;
    internal static Button TopTextsButton => _topTexts;
    internal static Button TopEncyclopediaButton => _topEncyclopedia;

    /// <summary>Select the first list row matching (self tests). True = found.</summary>
    internal static bool SelectWhere(Func<ListRow, bool> match)
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].Kind == ListRowKind.Entry && match(_rows[i]))
            {
                Select(i);
                return true;
            }
        }
        return false;
    }
}
