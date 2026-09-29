using System;
using System.Collections.Generic;
using System.Text;
using GUIFramework;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MC.UX.CraftingSearchSortMod;

// Me = the UI: one row (search field + Sort button) right above the recipe list, and the sort menu.
// Row take the top strip of the list: list top edge move down by row height + gap (ScrollRect, viewport/mask
// if they not follow, scrollbar, base content size), so nothing vanilla is covered. Every saved value is put back
// on deactivate, but only onto the same live InventoryGui (new instance after logout = start fresh).
// Widgets are clones of vanilla ones (build menu search field, container Take all button, recipe row) so they look
// native. Clones lose gamepad bits: navigation None (D-pad never land on them), UIGamePad / hints / tooltips gone.
internal static class SearchUi
{
    private const float Gap = 4f;
    private const float MenuPad = 2f;
    private const float SmallRow = 24f;

    private static readonly Vector3[] Corners = new Vector3[4];

    private static InventoryGui _owner;
    private static bool _refused;
    private static bool _warned;

    // Layout we changed, and old values.
    private static bool _layoutApplied;
    private static ScrollRect _scroll;
    private static RectTransform _scrollRT, _vpRT, _maskRT, _sbRT;
    private static Vector2 _savedScrollMax, _savedVpMin, _savedVpMax, _savedMaskMin, _savedMaskMax, _savedSbMax;
    private static bool _vpMoved, _maskMoved, _sbMoved;
    private static float _savedBaseSize;
    private static float _rowHeight = 30f;

    // Row.
    private static GameObject _row;
    private static RectTransform _rowRT;
    private static GuiInputField _field;
    private static Button _sortButton;
    private static TMP_Text _sortLabel;
    private static int _labelOption = -1;
    private static UIGroupHandler _craftGroup;
    private static CanvasGroup _craftCanvasGroup;

    // Menu.
    private static GameObject _menu;
    private static RectTransform _menuRT;
    private static Camera _eventCamera;

    internal static bool MenuOpen;

    // Controller keep these.
    internal static bool FieldFocused;
    internal static int ActivateFieldAtFrame = -1;

    internal static GuiInputField Field => _field;

    // Postfix call me each list build. Cheap when row exist.
    internal static void Ensure(InventoryGui gui)
    {
        if (gui == null)
        {
            return;
        }
        if (!ReferenceEquals(_owner, gui))
        {
            // Old instance died with its scene: drop refs, restore nothing onto new one.
            Forget();
            _owner = gui;
            CraftSearch.OnNewGui();
        }
        else if (_refused || _row != null)
        {
            return;
        }

        try
        {
            if (!_layoutApplied && !ApplyLayout(gui))
            {
                Refuse();
                return;
            }
            BuildRow(gui);
        }
        catch (Exception e)
        {
            // Half-made row worse than none: remove it, put layout back, no retry on this instance.
            PatchGuard.Report($"{nameof(SearchUi)}.{nameof(Ensure)}", e);
            DestroyRow();
            RestoreLayout();
            _refused = true;
        }
    }

    // Deactivate: menu, focus, row gone; layout back if still same live InventoryGui.
    internal static void Destroy()
    {
        CloseMenu();
        DropFocus();
        DestroyRow();
        if (_owner != null && ReferenceEquals(_owner, InventoryGui.instance))
        {
            RestoreLayout();
        }
        Forget();
    }

    private static void Refuse()
    {
        _refused = true;
        if (!_warned)
        {
            _warned = true;
            Log.Warning("Crafting list layout not recognised (another UI mod?): search and sort buttons not shown. "
                        + "The remembered sort still applies.");
        }
    }

    private static void Forget()
    {
        _owner = null;
        _refused = false;
        _layoutApplied = false;
        _scroll = null;
        _scrollRT = _vpRT = _maskRT = _sbRT = null;
        _vpMoved = _maskMoved = _sbMoved = false;
        _row = null;
        _rowRT = null;
        _field = null;
        _sortButton = null;
        _sortLabel = null;
        _labelOption = -1;
        _craftGroup = null;
        _craftCanvasGroup = null;
        _menu = null;
        _menuRT = null;
        MenuOpen = false;
        FieldFocused = false;
        ActivateFieldAtFrame = -1;
    }

    // ---------------------------------------------------------------- layout

    private static bool ApplyLayout(InventoryGui gui)
    {
        var ensure = gui.m_recipeEnsureVisible;
        ScrollRect scroll = null;
        if (ensure != null)
        {
            scroll = ensure.GetComponent<ScrollRect>();
        }
        if (scroll == null && gui.m_recipeListRoot != null)
        {
            scroll = gui.m_recipeListRoot.GetComponentInParent<ScrollRect>();
        }
        if (scroll == null)
        {
            return false;
        }
        var scrollRT = scroll.transform as RectTransform;
        var parent = scrollRT != null ? scrollRT.parent as RectTransform : null;
        if (parent == null)
        {
            return false;
        }
        var vp = scroll.viewport;
        var mask = ensure != null ? ensure.maskTransform : null;
        DumpLayout(gui, scroll, "before");

        // Layout system would overwrite our offsets: refuse (not expected in vanilla, it size the list itself).
        if (parent.GetComponent<LayoutGroup>() != null || scrollRT.GetComponent<ContentSizeFitter>() != null
            || (vp != null && vp.GetComponent<ContentSizeFitter>() != null))
        {
            return false;
        }

        _rowHeight = gui.m_recipeListSpace > 1f ? gui.m_recipeListSpace : 30f;
        var shift = _rowHeight + Gap;
        var useVp = vp != null && vp != scrollRT;
        var useMask = mask != null && mask != scrollRT && mask != vp;

        // Gaps between clip rects and ScrollRect edges before, so they keep them after.
        Edges(scrollRT, parent, out var scrollTop, out var scrollBottom);
        float vpTopGap = 0f, vpBottomGap = 0f, maskTopGap = 0f, maskBottomGap = 0f;
        if (useVp)
        {
            Edges(vp, parent, out var t, out var b);
            vpTopGap = scrollTop - t;
            vpBottomGap = b - scrollBottom;
        }
        if (useMask)
        {
            Edges(mask, parent, out var t, out var b);
            maskTopGap = scrollTop - t;
            maskBottomGap = b - scrollBottom;
        }

        _scroll = scroll;
        _scrollRT = scrollRT;
        _vpRT = useVp ? vp : null;
        _maskRT = useMask ? mask : null;
        _savedScrollMax = scrollRT.offsetMax;
        _savedBaseSize = gui.m_recipeListBaseSize;
        _layoutApplied = true; // from here, restore needed

        // Only top edge move, whatever the anchors.
        scrollRT.offsetMax = new Vector2(_savedScrollMax.x, _savedScrollMax.y - shift);
        Edges(scrollRT, parent, out var newTop, out var newBottom);
        if (useVp)
        {
            _vpMoved = FollowEdges(vp, parent, newTop - vpTopGap, newBottom + vpBottomGap, out _savedVpMin, out _savedVpMax);
        }
        if (useMask)
        {
            _maskMoved = FollowEdges(mask, parent, newTop - maskTopGap, newBottom + maskBottomGap,
                out _savedMaskMin, out _savedMaskMax);
        }

        // Scrollbar outside the ScrollRect with top at old list top: move its top too. A child follow by itself.
        var sb = gui.m_recipeListScroll != null ? gui.m_recipeListScroll.transform as RectTransform : null;
        if (sb != null && !sb.IsChildOf(scrollRT) && sb.parent != null)
        {
            Edges(sb, parent, out var sbTop, out _);
            if (Mathf.Abs(sbTop - scrollTop) <= 2f)
            {
                _sbRT = sb;
                _savedSbMax = sb.offsetMax;
                sb.offsetMax = new Vector2(_savedSbMax.x, _savedSbMax.y + ConvertY(parent, sb.parent, -shift));
                _sbMoved = true;
            }
        }

        // Else a short list keep a shift-tall scroll range. Content sized again by the postfix right after.
        gui.m_recipeListBaseSize = Mathf.Max(0f, _savedBaseSize - Mathf.Abs(ConvertY(parent, gui.m_recipeListRoot, shift)));

        DumpLayout(gui, scroll, "after");
        return true;
    }

    private static void RestoreLayout()
    {
        if (!_layoutApplied)
        {
            return;
        }
        _layoutApplied = false;
        if (_scrollRT != null)
        {
            _scrollRT.offsetMax = _savedScrollMax;
        }
        if (_vpMoved && _vpRT != null)
        {
            _vpRT.offsetMin = _savedVpMin;
            _vpRT.offsetMax = _savedVpMax;
        }
        if (_maskMoved && _maskRT != null)
        {
            _maskRT.offsetMin = _savedMaskMin;
            _maskRT.offsetMax = _savedMaskMax;
        }
        if (_sbMoved && _sbRT != null)
        {
            _sbRT.offsetMax = _savedSbMax;
        }
        if (_owner != null)
        {
            _owner.m_recipeListBaseSize = _savedBaseSize;
        }
        _vpMoved = _maskMoved = _sbMoved = false;
    }

    // Top and bottom of a rect, in "space" local units.
    private static void Edges(RectTransform rt, Transform space, out float top, out float bottom)
    {
        rt.GetWorldCorners(Corners);
        var a = space.InverseTransformPoint(Corners[0]).y;
        var b = space.InverseTransformPoint(Corners[1]).y;
        top = Mathf.Max(a, b);
        bottom = Mathf.Min(a, b);
    }

    // Move rect edges (only those more than half a unit off) to wanted top/bottom (in space units).
    private static bool FollowEdges(RectTransform rt, Transform space, float wantTop, float wantBottom,
        out Vector2 savedMin, out Vector2 savedMax)
    {
        savedMin = rt.offsetMin;
        savedMax = rt.offsetMax;
        if (rt.parent == null)
        {
            return false;
        }
        var moved = false;
        Edges(rt, space, out var top, out _);
        if (Mathf.Abs(top - wantTop) > 0.5f)
        {
            rt.offsetMax = new Vector2(rt.offsetMax.x, rt.offsetMax.y + ConvertY(space, rt.parent, wantTop - top));
            moved = true;
        }
        Edges(rt, space, out _, out var bottom);
        if (Mathf.Abs(bottom - wantBottom) > 0.5f)
        {
            rt.offsetMin = new Vector2(rt.offsetMin.x, rt.offsetMin.y + ConvertY(space, rt.parent, wantBottom - bottom));
            moved = true;
        }
        return moved;
    }

    // Vertical length from one transform's units to another's.
    private static float ConvertY(Transform from, Transform to, float dy)
    {
        if (from == null || to == null)
        {
            return dy;
        }
        return to.InverseTransformVector(from.TransformVector(new Vector3(0f, dy, 0f))).y;
    }

    // ---------------------------------------------------------------- row

    private static void BuildRow(InventoryGui gui)
    {
        // Built inactive: no clone Awake/Start run before me strip it.
        var row = new GameObject("MC_CraftSearchRow", typeof(RectTransform));
        row.SetActive(false);
        _row = row;
        _rowRT = (RectTransform)row.transform;
        _rowRT.SetParent(_scrollRT.parent, false);
        _rowRT.SetSiblingIndex(_scrollRT.GetSiblingIndex() + 1);
        row.AddComponent<LayoutElement>().ignoreLayout = true;

        // Take the freed strip exactly: offsets relative to the list's top anchor line.
        _rowRT.anchorMin = new Vector2(_scrollRT.anchorMin.x, _scrollRT.anchorMax.y);
        _rowRT.anchorMax = _scrollRT.anchorMax;
        _rowRT.pivot = new Vector2(0.5f, 1f);
        _rowRT.offsetMax = _savedScrollMax;
        _rowRT.offsetMin = new Vector2(_scrollRT.offsetMin.x, _savedScrollMax.y - _rowHeight);
        _rowRT.localScale = Vector3.one;

        var buttonWidth = Mathf.Clamp(_rowRT.rect.width * 0.4f, 90f, 160f);
        BuildSortButton(gui, buttonWidth);
        BuildField(gui, _sortButton != null ? buttonWidth + Gap : 0f);
        row.AddComponent<SearchController>();

        var groups = gui.m_uiGroups;
        _craftGroup = groups != null && groups.Length > 3 ? groups[3] : null;
        _craftCanvasGroup = _craftGroup != null ? _craftGroup.GetComponent<CanvasGroup>() : null;

        row.SetActive(true);
        if (_field != null)
        {
            _field.SetTextWithoutNotify("");
        }
        CraftSearch.OnFieldCreated();
        _labelOption = -1;
        RefreshButtonLabel();
        DumpRow();
    }

    private static void DestroyRow()
    {
        if (_row != null)
        {
            _row.SetActive(false); // gone this frame; controller OnDisable release focus
            UnityEngine.Object.Destroy(_row);
        }
        _row = null;
        _rowRT = null;
        _field = null;
        _sortButton = null;
        _sortLabel = null;
        FieldFocused = false;
    }

    private static void BuildField(InventoryGui gui, float rightInset)
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
            Log.Debug("No vanilla text field to copy: crafting search field not shown.");
            return;
        }

        var go = UnityEngine.Object.Instantiate(src.gameObject, _rowRT, false);
        go.name = "MC_CraftSearchField";
        var field = go.GetComponent<GuiInputField>();
        if (field == null)
        {
            UnityEngine.Object.Destroy(go);
            return;
        }
        // Persistent listeners point at vanilla objects: off. Runtime ones (BuildUi, GuiInputField.Start) not copied.
        // Never replace the events: GuiInputField.Start add its own navigation-restore listeners to them.
        Mute(field.onValueChanged);
        Mute(field.onEndEdit);
        Mute(field.onSubmit);
        Mute(field.onSelect);
        Mute(field.onDeselect);
        Mute(field.OnInputSubmit);
        Strip(go);
        NoNavigation(go);
        go.SetActive(true); // chat input source is inactive

        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = new Vector2(-rightInset, 0f);
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;

        field.lineType = TMP_InputField.LineType.SingleLine;
        field.characterLimit = 40;
        field.restoreOriginalTextOnEscape = false;
        field.onFocusSelectAll = true;
        field.readOnly = false;
        field.interactable = true;

        var size = RowFontSize(gui);
        if (size > 0f)
        {
            SetFontSize(field.textComponent, size);
            SetFontSize(field.placeholder as TMP_Text, size);
        }
        if (field.placeholder is TMP_Text placeholder && string.IsNullOrEmpty(placeholder.text))
        {
            placeholder.text = "Search";
        }

        field.onValueChanged.AddListener(CraftSearch.OnTextChanged);
        field.onSubmit.AddListener(OnFieldDone);
        field.onEndEdit.AddListener(OnFieldDone);
        go.AddComponent<SearchFieldPointer>();
        _field = field;
    }

    private static void BuildSortButton(InventoryGui gui, float width)
    {
        // Container panel text button (Sort Chest clone its neighbour, Place stacks): both mods look alike.
        var src = gui.m_takeAllButton != null ? gui.m_takeAllButton : gui.m_stackAllButton;
        if (src == null)
        {
            Log.Debug("No vanilla text button to copy: crafting sort button not shown.");
            return;
        }

        var go = UnityEngine.Object.Instantiate(src.gameObject, _rowRT, false);
        go.name = "MC_CraftSortButton";
        var button = go.GetComponent<Button>();
        if (button == null)
        {
            UnityEngine.Object.Destroy(go);
            return;
        }
        button.onClick = new Button.ButtonClickedEvent(); // drop any copied persistent call
        button.onClick.AddListener(ToggleMenu);
        Strip(go);
        NoNavigation(go);
        button.interactable = true;
        go.SetActive(true); // source sit in hidden container panel

        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.offsetMin = new Vector2(-width, 0f);
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;

        _sortLabel = go.GetComponentInChildren<TMP_Text>(true);
        if (_sortLabel != null)
        {
            _sortLabel.overflowMode = TextOverflowModes.Ellipsis;
            _sortLabel.textWrappingMode = TextWrappingModes.NoWrap;
        }
        go.AddComponent<SortButtonPointer>();
        _sortButton = button;
    }

    private static void OnFieldDone(string _) => CraftSearch.RequestApplyNow();

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

    // Gamepad hotkeys, key hints, tooltips, auto-localize: vanilla source's, not ours. Gone before they Start.
    private static void Strip(GameObject clone)
    {
        foreach (var pad in clone.GetComponentsInChildren<UIGamePad>(true))
        {
            if (pad == null)
            {
                continue;
            }
            DropHint(pad.m_hint, clone);
            UnityEngine.Object.DestroyImmediate(pad);
        }
        foreach (var hint in clone.GetComponentsInChildren<UIInputHint>(true))
        {
            if (hint == null)
            {
                continue;
            }
            DropHint(hint.m_gamepadHint, clone);
            DropHint(hint.m_mouseKeyboardHint, clone);
            DropHint(hint.m_gamepadMouseHint, clone);
            if (hint.m_inputLayoutSettings != null)
            {
                foreach (var s in hint.m_inputLayoutSettings)
                {
                    if (s != null)
                    {
                        DropHint(s.m_hintObject, clone);
                    }
                }
            }
            UnityEngine.Object.DestroyImmediate(hint);
        }
        foreach (var tip in clone.GetComponentsInChildren<UITooltip>(true))
        {
            if (tip != null)
            {
                UnityEngine.Object.DestroyImmediate(tip);
            }
        }
        foreach (var loc in clone.GetComponentsInChildren<Localize>(true))
        {
            if (loc != null)
            {
                UnityEngine.Object.DestroyImmediate(loc);
            }
        }
    }

    // Hint object inside the clone = our copy: remove. Outside = vanilla's own (Instantiate keep outside refs): leave it.
    private static void DropHint(GameObject hint, GameObject clone)
    {
        if (hint == null || hint == clone || !hint.transform.IsChildOf(clone.transform))
        {
            return;
        }
        hint.SetActive(false);
        UnityEngine.Object.Destroy(hint);
    }

    // D-pad / stick never select our controls (no controller trap), and they never point back at vanilla ones.
    private static void NoNavigation(GameObject clone)
    {
        foreach (var s in clone.GetComponentsInChildren<Selectable>(true))
        {
            s.navigation = new Navigation { mode = Navigation.Mode.None };
        }
    }

    private static float RowFontSize(InventoryGui gui)
    {
        var prefab = gui.m_recipeElementPrefab;
        var name = prefab != null ? prefab.transform.Find("name") : null;
        var text = name != null ? name.GetComponent<TMP_Text>() : null;
        if (text == null)
        {
            return 0f;
        }
        return text.enableAutoSizing ? text.fontSizeMax : text.fontSize;
    }

    private static void SetFontSize(TMP_Text text, float size)
    {
        if (text == null)
        {
            return;
        }
        text.enableAutoSizing = false;
        text.fontSize = size;
    }

    internal static void RefreshButtonLabel(bool force = false)
    {
        if (_sortLabel == null)
        {
            return;
        }
        var option = CraftSearch.Option;
        if (!force && option == _labelOption)
        {
            return;
        }
        _labelOption = option;
        _sortLabel.text = "Sort: " + RecipeCategory.Label(option);
    }

    internal static void SetFieldTextSilently(string text)
    {
        if (_field != null)
        {
            _field.SetTextWithoutNotify(text);
        }
    }

    // ---------------------------------------------------------------- focus

    // Leave field: stop typing and deselect (GuiInputField give EventSystem navigation back only on deselect).
    internal static void DropFocus()
    {
        var field = _field;
        FieldFocused = false;
        ActivateFieldAtFrame = -1;
        if (field == null)
        {
            return;
        }
        if (field.isFocused)
        {
            field.DeactivateInputField();
        }
        ReleaseSelection();
    }

    // Focus already gone (Esc, Enter): if field still the selected object, deselect it.
    internal static void ReleaseSelection()
    {
        var es = EventSystem.current;
        if (es != null && _field != null && es.currentSelectedGameObject == _field.gameObject)
        {
            es.SetSelectedGameObject(null);
        }
    }

    internal static void FocusField()
    {
        var field = _field;
        if (field == null || !field.isActiveAndEnabled || field.isFocused)
        {
            return;
        }
        CloseMenu();
        field.ActivateInputField(); // GuiInputField version: navigation off while typing, Steam keyboard in Big Picture
    }

    // Left press on field or Sort button. Only when crafting panel's group has a CanvasGroup that is not
    // interactable (then clicks do nothing): switch to crafting group like a recipe row click, activate field after.
    internal static void OnControlPointerDown(bool isField)
    {
        var group = _craftGroup;
        var canvasGroup = _craftCanvasGroup;
        if (_owner == null || group == null || canvasGroup == null || canvasGroup.interactable)
        {
            return;
        }
        _owner.SetActiveGroup(group, playSound: false);
        if (isField)
        {
            ActivateFieldAtFrame = Time.frameCount + 1;
        }
    }

    internal static void ScrollToTop()
    {
        if (_scroll != null)
        {
            _scroll.verticalNormalizedPosition = 1f;
        }
    }

    // ---------------------------------------------------------------- menu

    internal static void ToggleMenu()
    {
        try
        {
            // Mouse click leave button selected; a later gamepad A (Submit) would click it again. Deselect.
            var es = EventSystem.current;
            if (es != null && _sortButton != null && es.currentSelectedGameObject == _sortButton.gameObject)
            {
                es.SetSelectedGameObject(null);
            }
            if (MenuOpen)
            {
                CloseMenu();
            }
            else
            {
                OpenMenu();
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report($"{nameof(SearchUi)}.{nameof(ToggleMenu)}", e);
            CloseMenu();
        }
    }

    internal static void CloseMenu()
    {
        if (!MenuOpen)
        {
            return;
        }
        MenuOpen = false;
        if (_menu != null)
        {
            _menu.SetActive(false);
            UnityEngine.Object.Destroy(_menu);
        }
        _menu = null;
        _menuRT = null;
    }

    // Menu = recipe-row clones, one per option; built on open, destroyed on close. No full-screen blocker
    // (a stuck blocker would make the whole inventory unclickable): controller close it on outside click.
    private static void OpenMenu()
    {
        var gui = _owner;
        if (gui == null || _rowRT == null || gui.m_recipeElementPrefab == null || gui.m_crafting == null)
        {
            return;
        }
        DropFocus();

        var options = new List<int>(RecipeCategory.Count);
        for (var o = 0; o < RecipeCategory.Count; o++)
        {
            if (o == RecipeCategory.Default || o == RecipeCategory.Name || CraftList.Counts[o] > 0 || o == CraftSearch.Option)
            {
                options.Add(o);
            }
        }

        // Geometry in m_crafting space: top-left at row's bottom-left, columns of list width, inside m_crafting.
        var crafting = gui.m_crafting;
        var bounds = crafting.rect;
        _rowRT.GetWorldCorners(Corners);
        var origin = crafting.InverseTransformPoint(Corners[0]);
        var colWidth = gui.m_recipeListRoot != null ? gui.m_recipeListRoot.rect.width : 0f;
        if (colWidth < 60f)
        {
            colWidth = _rowRT.rect.width;
        }
        var listHeight = VisibleListHeight(crafting);
        var rowH = _rowHeight;
        Columns(options.Count, listHeight, rowH, out var perCol, out var cols);
        if (cols * colWidth + 2f * MenuPad > bounds.width && rowH > SmallRow)
        {
            rowH = SmallRow;
            Columns(options.Count, listHeight, rowH, out perCol, out cols);
        }
        if (cols * colWidth + 2f * MenuPad > bounds.width)
        {
            colWidth = (bounds.width - 2f * MenuPad) / cols;
        }
        var width = cols * colWidth + 2f * MenuPad;
        var height = Mathf.Min(perCol, options.Count) * rowH + 2f * MenuPad;
        var x = Mathf.Max(bounds.xMin, Mathf.Min(origin.x, bounds.xMax - width));
        var y = Mathf.Min(bounds.yMax, Mathf.Max(origin.y, bounds.yMin + height));

        var menu = new GameObject("MC_CraftSortMenu", typeof(RectTransform));
        menu.SetActive(false);
        var rt = (RectTransform)menu.transform;
        rt.SetParent(crafting, false);
        rt.SetAsLastSibling(); // drawn over the list
        menu.AddComponent<LayoutElement>().ignoreLayout = true;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(width, height);
        rt.localScale = Vector3.one;
        rt.localPosition = new Vector3(x, y, 0f);

        // Dark base always readable; list background sprite on top when the list has one (native look).
        var bg = menu.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.9f);
        bg.raycastTarget = true;
        var skinSrc = _scroll != null ? _scroll.GetComponent<Image>() : null;
        if (skinSrc != null && skinSrc.sprite != null)
        {
            var skin = new GameObject("bg", typeof(RectTransform));
            var skinRT = (RectTransform)skin.transform;
            skinRT.SetParent(rt, false);
            skinRT.anchorMin = Vector2.zero;
            skinRT.anchorMax = Vector2.one;
            skinRT.offsetMin = Vector2.zero;
            skinRT.offsetMax = Vector2.zero;
            var img = skin.AddComponent<Image>();
            img.sprite = skinSrc.sprite;
            img.type = skinSrc.type;
            img.color = skinSrc.color;
            img.raycastTarget = false;
        }

        for (var n = 0; n < options.Count; n++)
        {
            var option = options[n];
            var entry = UnityEngine.Object.Instantiate(gui.m_recipeElementPrefab, rt, false);
            entry.name = "MC_SortOption_" + RecipeCategory.Id(option);
            var button = entry.GetComponent<Button>();
            if (button != null)
            {
                // Vanilla add row listener at runtime only, but a persistent one would fire too: replace, never add onto.
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(() => CraftSearch.ChooseOption(option));
            }
            Strip(entry);
            NoNavigation(entry);
            var ert = (RectTransform)entry.transform;
            ert.anchorMin = ert.anchorMax = new Vector2(0f, 1f);
            ert.pivot = new Vector2(0f, 1f);
            ert.sizeDelta = new Vector2(colWidth, rowH);
            ert.anchoredPosition = new Vector2(MenuPad + n / perCol * colWidth, -(MenuPad + n % perCol * rowH));
            ert.localScale = Vector3.one;
            FillEntry(entry, option);
            entry.SetActive(true);
        }

        var canvas = _rowRT.GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            canvas = canvas.rootCanvas;
        }
        _eventCamera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

        menu.SetActive(true);
        _menu = menu;
        _menuRT = rt;
        MenuOpen = true;
    }

    private static void Columns(int count, float listHeight, float rowH, out int perCol, out int cols)
    {
        perCol = Mathf.Max(1, Mathf.FloorToInt((listHeight - 2f * MenuPad) / rowH));
        cols = Mathf.Max(1, (count + perCol - 1) / perCol);
    }

    // Visible list height (viewport or ScrollRect), in m_crafting units.
    private static float VisibleListHeight(RectTransform crafting)
    {
        var rt = _vpRT != null ? _vpRT : _scrollRT;
        if (rt == null)
        {
            return _rowHeight * 10f;
        }
        Edges(rt, crafting, out var top, out var bottom);
        return Mathf.Max(_rowHeight, top - bottom);
    }

    // Exactly the recipe-row look: label + count, icon of first row of that category, highlight on active option.
    private static void FillEntry(GameObject entry, int option)
    {
        var t = entry.transform;
        var nameText = Child<TMP_Text>(t, "name");
        if (nameText != null)
        {
            var label = RecipeCategory.Label(option);
            nameText.text = option == RecipeCategory.Default || option == RecipeCategory.Name
                ? label
                : label + " (" + CraftList.Counts[option] + ")";
            nameText.color = Color.white;
        }
        var icon = Child<Image>(t, "icon");
        if (icon != null)
        {
            var sprite = option == RecipeCategory.Default || option == RecipeCategory.Name ? null : CraftList.Icons[option];
            icon.sprite = sprite;
            icon.color = Color.white;
            icon.enabled = sprite != null;
        }
        SetChildActive(t, "Durability", false);
        SetChildActive(t, "QualityLevel", false);
        SetChildActive(t, "selected", option == CraftSearch.Option);
    }

    private static T Child<T>(Transform parent, string name) where T : Component
    {
        var child = parent.Find(name);
        return child != null ? child.GetComponent<T>() : null;
    }

    private static void SetChildActive(Transform parent, string name, bool active)
    {
        var child = parent.Find(name);
        if (child != null)
        {
            child.gameObject.SetActive(active);
        }
    }

    // Mouse over menu or Sort button? (Sort button own click toggle the menu.)
    internal static bool PointerOverMenuOrButton()
    {
        var pos = (Vector2)ZInput.pointerPosition;
        if (_menuRT != null && RectTransformUtility.RectangleContainsScreenPoint(_menuRT, pos, _eventCamera))
        {
            return true;
        }
        return _sortButton != null
               && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)_sortButton.transform, pos, _eventCamera);
    }

    // ---------------------------------------------------------------- debug dump

    // Debug aid, once per InventoryGui: where things sit, what drives the layout, so placement (T01, T27) and
    // gamepad default element (T28) can be checked from the log without screenshots.
    private static void DumpLayout(InventoryGui gui, ScrollRect scroll, string phase)
    {
        try
        {
            var space = gui.m_crafting;
            var sb = new StringBuilder();
            sb.Append("Crafting panel layout (").Append(phase).Append(" search row), rects in m_crafting space:");
            if (phase == "before")
            {
                Describe(sb, "m_crafting", space, space);
                sb.Append(" canvasGroup=").Append(space != null && space.GetComponent<CanvasGroup>() != null);
                Describe(sb, "scroll parent", scroll.transform.parent, space);
            }
            Describe(sb, "scroll", scroll.transform, space);
            Describe(sb, "viewport", scroll.viewport, space);
            var ensure = gui.m_recipeEnsureVisible;
            var mask = ensure != null ? ensure.maskTransform : null;
            Describe(sb, "mask", mask, space);
            if (phase == "before")
            {
                Describe(sb, "listRoot", gui.m_recipeListRoot, space);
                Describe(sb, "scrollbar", gui.m_recipeListScroll != null ? gui.m_recipeListScroll.transform : null, space);
                Describe(sb, "tabCraft", gui.m_tabCraft != null ? gui.m_tabCraft.transform : null, space);
                Describe(sb, "tabUpgrade", gui.m_tabUpgrade != null ? gui.m_tabUpgrade.transform : null, space);
                Describe(sb, "stationName", gui.m_craftingStationName != null ? gui.m_craftingStationName.transform : null, space);
                Describe(sb, "repair", gui.m_repairButton != null ? gui.m_repairButton.transform : null, space);
                var groups = gui.m_uiGroups;
                var group = groups != null && groups.Length > 3 ? groups[3] : null;
                sb.Append("\n  uiGroups[3]=").Append(group != null ? group.name : "null")
                    .Append(" defaultElement=").Append(group != null && group.m_defaultElement != null ? group.m_defaultElement.name : "null")
                    .Append(" listBaseSize=").Append(gui.m_recipeListBaseSize);
            }
            else
            {
                sb.Append("\n  moved: viewport=").Append(_vpMoved).Append(" mask=").Append(_maskMoved)
                    .Append(" scrollbar=").Append(_sbMoved).Append(" listBaseSize=").Append(gui.m_recipeListBaseSize);
            }
            Log.Debug(sb.ToString());
        }
        catch (Exception e)
        {
            Log.Debug($"Crafting layout dump failed: {e.Message}");
        }
    }

    private static void DumpRow()
    {
        try
        {
            var sb = new StringBuilder("Crafting search row built:");
            Describe(sb, "row", _rowRT, _owner != null ? _owner.m_crafting : null);
            if (_field != null)
            {
                DescribeTree(sb, _field.transform, 1);
            }
            if (_sortButton != null)
            {
                DescribeTree(sb, _sortButton.transform, 1);
            }
            Log.Debug(sb.ToString());
        }
        catch (Exception e)
        {
            Log.Debug($"Crafting row dump failed: {e.Message}");
        }
    }

    private static void Describe(StringBuilder sb, string label, Transform t, RectTransform space)
    {
        sb.Append("\n  ").Append(label).Append(": ");
        if (t == null)
        {
            sb.Append("null");
            return;
        }
        sb.Append('\'').Append(t.name).Append("' active=").Append(t.gameObject.activeInHierarchy);
        if (t is RectTransform rt)
        {
            sb.Append(" anchors=").Append(rt.anchorMin).Append('-').Append(rt.anchorMax)
                .Append(" offsets=").Append(rt.offsetMin).Append('-').Append(rt.offsetMax);
            if (space != null)
            {
                rt.GetWorldCorners(Corners);
                var a = space.InverseTransformPoint(Corners[0]);
                var b = space.InverseTransformPoint(Corners[2]);
                sb.Append(" rect=(").Append(a.x.ToString("F0")).Append(',').Append(a.y.ToString("F0")).Append(")-(")
                    .Append(b.x.ToString("F0")).Append(',').Append(b.y.ToString("F0")).Append(')');
            }
        }
        sb.Append(" components=");
        AppendComponents(sb, t);
    }

    private static void DescribeTree(StringBuilder sb, Transform t, int depth)
    {
        sb.Append('\n').Append(' ', 2 + depth * 2).Append(t.name).Append(": ");
        AppendComponents(sb, t);
        for (var i = 0; i < t.childCount && depth < 4; i++)
        {
            DescribeTree(sb, t.GetChild(i), depth + 1);
        }
    }

    private static void AppendComponents(StringBuilder sb, Transform t)
    {
        var first = true;
        foreach (var c in t.GetComponents<Component>())
        {
            if (c == null)
            {
                continue;
            }
            if (!first)
            {
                sb.Append(',');
            }
            first = false;
            sb.Append(c.GetType().Name);
            if (c is LayoutGroup || c is ContentSizeFitter || c is LayoutElement)
            {
                sb.Append("(layout)");
            }
        }
    }
}
