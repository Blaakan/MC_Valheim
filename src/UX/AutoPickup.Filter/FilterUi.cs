using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx.Configuration;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MC.UX.AutoPickupFilterMod;

// Me = inventory screen side of the filter:
// - mode button above top-left of player panel (clone of vanilla Take all: same look and sound), cycle the modes,
//   tooltip explain modes and show both lists;
// - mark gesture (MarkKey, default middle click) on a slot of your inventory or an open chest;
// - controller gestures while your inventory grid is focused (R3 mark, LT+R3 mode, RT+R3 lists panel);
// - lists panel for controller (our own copy of the tooltip look, pinned next to the button);
// - top-left messages.
// All objects me make are destroyed on deactivate and made again when needed (live toggle).
internal static class FilterUi
{
    private const string ButtonName = "MC_LootFilterButton";
    private const string ListsName = "MC_LootFilterLists";
    private const float ButtonWidth = 220f;
    private const float ButtonHeight = 32f;
    private const float ButtonGap = 6f;
    private const int MaxNamesPerList = 40;
    private const float MarkMessageInterval = 1f;
    private const string TooltipTopic = "Auto pickup filter";

    private static readonly Vector3[] Corners = new Vector3[4];
    private static readonly Vector3[] Corners2 = new Vector3[4];
    private static readonly StringBuilder Sb = new StringBuilder();

    // Button.
    private static InventoryGui _gui;
    private static bool _refused;
    private static bool _warned;
    private static Button _button;
    private static RectTransform _buttonRT;
    private static TMP_Text _label;
    private static UITooltip _tooltip;
    private static GameObject _tooltipPrefab;
    private static Canvas _rootCanvas;
    private static bool _worldPlaced;
    internal static bool PlacementDirty;
    private static Vector2 _lastPlayerRectSize;
    private static Vector3 _lastPlayerPos;
    private static float _lastPlayerScale;
    private static int _lastScreenHeight;

    // Lists panel (controller).
    private static GameObject _listsPanel;
    private static TMP_Text _listsTopic;
    private static TMP_Text _listsText;
    private static bool _listsWarned;
    internal static bool ListsShown;

    // What label/tooltip show now.
    private static int _shownVersion = -1;
    private static bool _shownOn;
    private static bool _shownPad;
    private static bool _shownPadOnly;
    private static string _listsTextCache = "";

    // Mark key, read once per setting change.
    private static KeyCode _markMain = KeyCode.None;
    private static KeyCode[] _markMods = new KeyCode[0];
    private static string _markGesture = "Middle-click";
    private static bool _markIsMouse = true;
    private static string _markUnusable; // not null = MarkKey set but game cannot read it

    private static float _lastMarkMessage = -10f;

    // UI raycast for "is the slot really on top" (only on a key press).
    private static PointerEventData _ped;
    private static EventSystem _pedOwner;
    private static readonly List<RaycastResult> Hits = new List<RaycastResult>();
    private static FieldInfo _tooltipInstanceField;
    private static bool _tooltipFieldLooked;

    // ---------------------------------------------------------------- life

    internal static void EnsureButton(InventoryGui gui)
    {
        if (gui == null)
        {
            return;
        }
        if (!ReferenceEquals(_gui, gui))
        {
            // Old InventoryGui died with its scene (logout): its button too. Start fresh on the new one.
            Forget();
            _gui = gui;
        }
        if (_refused || _button != null)
        {
            return;
        }
        try
        {
            Build(gui);
        }
        catch (Exception e)
        {
            PatchGuard.Report($"{nameof(FilterUi)}.{nameof(EnsureButton)}", e);
            DestroyButton();
            _gui = gui;
            _refused = true;
        }
    }

    internal static void DestroyButton()
    {
        HideListsPanel();
        if (_tooltip != null)
        {
            UITooltip.HideTooltip();
        }
        if (_button != null)
        {
            _button.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(_button.gameObject);
        }
        Forget();
    }

    private static void Forget()
    {
        _gui = null;
        _refused = false;
        _button = null;
        _buttonRT = null;
        _label = null;
        _tooltip = null;
        _tooltipPrefab = null;
        _rootCanvas = null;
        _worldPlaced = false;
        PlacementDirty = true;
        _lastPlayerRectSize = Vector2.zero;
        _lastPlayerScale = 0f;
        _listsPanel = null;
        _listsTopic = null;
        _listsText = null;
        ListsShown = false;
        _shownVersion = -1;
    }

    private static void Build(InventoryGui gui)
    {
        var src = gui.m_takeAllButton;
        var player = gui.m_player;
        var grid = gui.m_playerGrid;
        if (src == null || player == null || grid == null)
        {
            _refused = true;
            if (!_warned)
            {
                _warned = true;
                Log.Warning("Inventory layout not recognised (another UI mod?): the Auto pickup button is not shown. "
                            + "The filter, the mark gesture and the lootfilter command still work.");
            }
            return;
        }

        // Parent: player panel. If it (or a parent below inventory root) carry a UI group with CanvasGroup, that
        // group turn our button non-clickable while another group is active (after a recipe click): then hang it
        // on inventory root and follow the panel by world position.
        Transform parent = player;
        _worldPlaced = false;
        var root = gui.m_inventoryRoot;
        if (root != null)
        {
            for (var t = (Transform)player; t != null && t != root; t = t.parent)
            {
                if (t.GetComponent<UIGroupHandler>() != null && t.GetComponent<CanvasGroup>() != null)
                {
                    _worldPlaced = true;
                    parent = root;
                    break;
                }
            }
        }
        Log.Debug($"Loot filter button parent: {(_worldPlaced ? "inventory root (player panel group has a CanvasGroup)" : "player panel")}.");

        // Clone in an inactive holder: nothing of the clone Start before me strip it.
        var holder = new GameObject("MC_LootFilterBuild");
        holder.SetActive(false);
        holder.transform.SetParent(parent, false);
        GameObject go;
        try
        {
            go = UnityEngine.Object.Instantiate(src.gameObject, holder.transform, false);
            go.name = ButtonName;
            Strip(go);
            NoNavigation(go);
            go.transform.SetParent(parent, false);
        }
        finally
        {
            UnityEngine.Object.Destroy(holder);
        }

        var button = go.GetComponent<Button>();
        if (button == null)
        {
            UnityEngine.Object.Destroy(go);
            _refused = true;
            return;
        }
        button.onClick = new Button.ButtonClickedEvent(); // drop copied persistent call (Take all)
        button.onClick.AddListener(OnModeButtonClicked);
        button.interactable = true;

        var rt = (RectTransform)go.transform;
        var anchor = _worldPlaced ? new Vector2(0.5f, 0.5f) : new Vector2(0f, 1f);
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = Vector2.zero;
        rt.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        rt.anchoredPosition = Vector2.zero;
        go.SetActive(true); // source sit in hidden container panel
        rt.SetAsLastSibling();

        _label = go.GetComponentInChildren<TMP_Text>(true);
        if (_label != null)
        {
            var size = _label.enableAutoSizing ? _label.fontSizeMax : _label.fontSize;
            _label.enableAutoSizing = true;
            _label.fontSizeMax = size;
            _label.fontSizeMin = Mathf.Min(10f, size);
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            _label.overflowMode = TextOverflowModes.Ellipsis;
        }

        // Tooltip look = the item tooltip of inventory slots.
        var element = grid.m_elementPrefab != null ? grid.m_elementPrefab.GetComponent<InventoryElement>() : null;
        _tooltipPrefab = element != null && element.m_tooltip != null ? element.m_tooltip.m_tooltipPrefab : null;
        if (_tooltipPrefab != null)
        {
            _tooltip = go.AddComponent<UITooltip>();
            _tooltip.m_tooltipPrefab = _tooltipPrefab;
        }
        else
        {
            Log.Debug("No inventory tooltip prefab found: the Auto pickup button has no tooltip.");
        }

        var canvas = go.GetComponentInParent<Canvas>();
        _rootCanvas = canvas != null ? canvas.rootCanvas : null;
        _button = button;
        _buttonRT = rt;
        PlacementDirty = true;
        _shownVersion = -1;
    }

    // Gamepad hotkeys, key hints, tooltips, auto-localize of Take all: not ours. Gone before they Start.
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

    // Hint object inside the clone = our copy: remove. Outside = vanilla's own: leave it.
    private static void DropHint(GameObject hint, GameObject clone)
    {
        if (hint == null || hint == clone || !hint.transform.IsChildOf(clone.transform))
        {
            return;
        }
        hint.SetActive(false);
        UnityEngine.Object.Destroy(hint);
    }

    // D-pad never move selection from a slot onto our button.
    private static void NoNavigation(GameObject clone)
    {
        foreach (var s in clone.GetComponentsInChildren<Selectable>(true))
        {
            s.navigation = new Navigation { mode = Navigation.Mode.None };
        }
    }

    // ---------------------------------------------------------------- per frame

    // InventoryGui.Update postfix, only while inventory visible.
    internal static void Tick(InventoryGui gui)
    {
        if (!FilterState.EnsureLocal())
        {
            return;
        }
        if (!ReferenceEquals(_gui, gui) || (!_refused && _button == null))
        {
            EnsureButton(gui);
        }
        Place(gui);
        // Mark key alone in own try: its failure never skip controller gestures or label refresh.
        try
        {
            HandleMarkKey(gui);
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(HandleMarkKey), e);
        }
        HandleGamepad(gui);
        Refresh(force: false);
        DebugLayoutDump.Run(gui, _buttonRT, _worldPlaced);
    }

    // ---------------------------------------------------------------- placement

    // Docked on top of player panel, left edge on the grid's left edge. Anchored mode: redo only when panel size,
    // position or scale changed (show animation, inventory rows) or a setting moved it. World mode: every frame.
    private static void Place(InventoryGui gui)
    {
        if (_buttonRT == null)
        {
            return;
        }
        var player = gui.m_player;
        if (player == null)
        {
            return;
        }
        var scale = player.lossyScale.x;
        if (Mathf.Abs(scale) <= 1e-3f)
        {
            return;
        }
        var size = player.rect.size;
        var pos = player.position;
        var screenHeight = Screen.height;
        if (!_worldPlaced && !PlacementDirty && size == _lastPlayerRectSize && pos == _lastPlayerPos && scale == _lastPlayerScale
            && screenHeight == _lastScreenHeight)
        {
            return;
        }
        var gridLeft = GridLeft(gui, player);
        if (float.IsNaN(gridLeft) || float.IsInfinity(gridLeft))
        {
            return; // keep dirty, try next frame
        }
        var offX = Plugin.ButtonOffsetX.Value;
        var offY = Plugin.ButtonOffsetY.Value;
        var before = _buttonRT.position;
        if (_worldPlaced)
        {
            var rect = player.rect;
            var local = new Vector3(rect.xMin + gridLeft + offX, rect.yMax + ButtonGap + offY, 0f);
            _buttonRT.position = player.TransformPoint(local);
        }
        else
        {
            _buttonRT.anchoredPosition = new Vector2(gridLeft + offX, ButtonGap + offY);
        }
        KeepOnScreen();
        PlacementDirty = false;
        _lastPlayerRectSize = size;
        _lastPlayerPos = pos;
        _lastPlayerScale = scale;
        _lastScreenHeight = screenHeight;
        if (ListsShown && _buttonRT.position != before)
        {
            PositionListsPanel();
        }
    }

    // Distance from player panel's left edge to first slot's left edge, in panel units.
    private static float GridLeft(InventoryGui gui, RectTransform player)
    {
        var grid = gui.m_playerGrid;
        RectTransform target = null;
        if (grid != null)
        {
            var elements = grid.m_elements;
            if (elements.Count > 0 && elements[0] != null)
            {
                target = elements[0].transform as RectTransform;
            }
            if (target == null)
            {
                target = grid.transform as RectTransform;
            }
        }
        if (target == null)
        {
            return 0f;
        }
        target.GetWorldCorners(Corners);
        return player.InverseTransformPoint(Corners[0]).x - player.rect.xMin;
    }

    // Screen-space canvas (world units = pixels): never let the button leave the screen at the top or the sides.
    private static void KeepOnScreen()
    {
        if (_rootCanvas == null || _rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            return;
        }
        _buttonRT.GetWorldCorners(Corners2);
        var dx = 0f;
        var dy = 0f;
        if (Corners2[2].y > Screen.height)
        {
            dy = Screen.height - Corners2[2].y;
        }
        if (Corners2[0].x < 0f)
        {
            dx = -Corners2[0].x;
        }
        else if (Corners2[2].x > Screen.width)
        {
            dx = Screen.width - Corners2[2].x;
        }
        if (dx != 0f || dy != 0f)
        {
            _buttonRT.position += new Vector3(dx, dy, 0f);
        }
    }

    // ---------------------------------------------------------------- label, tooltip, lists

    internal static void Refresh(bool force)
    {
        if (!FilterState.EnsureLocal())
        {
            return;
        }
        var on = Player.m_enableAutoPickup;
        var pad = ZInput.IsGamepadActive();
        // Controller alone: lists panel speak controller only (mouse touched = mouse words back). Rebuilt only on flip.
        var padOnly = pad && Plugin.GamepadControls.Value && ZInput.IsExclusiveGamepadActive();
        if (!force && FilterState.Version == _shownVersion && on == _shownOn && pad == _shownPad && padOnly == _shownPadOnly)
        {
            return;
        }
        _shownVersion = FilterState.Version;
        _shownOn = on;
        _shownPad = pad;
        _shownPadOnly = padOnly;

        if (_label != null)
        {
            var mode = FilterState.ModeLabel(FilterState.Mode);
            _label.text = on ? "Auto pickup: " + mode : "Auto pickup: Off (" + mode + ")";
        }
        _listsTextCache = BuildListsText(on, pad, padOnly);
        if (_tooltip != null)
        {
            _tooltip.Set(TooltipTopic, _listsTextCache);
        }
        if (ListsShown)
        {
            FillListsPanel();
        }
    }

    // Tooltip body, shared with lists panel. Built only when something changed. padOnly = controller alone in use:
    // no mouse words (button cannot be clicked, chest slots cannot be marked with a controller).
    private static string BuildListsText(bool on, bool pad, bool padOnly)
    {
        var sb = Sb;
        sb.Clear();
        sb.Append(padOnly ? "Hold LT and click the right stick to change the mode.\n" : "Click to change the mode.\n");
        sb.Append("Everything: pick up every item, as in the normal game.\n");
        sb.Append("Skip ignored: pick up everything except ignored items (red mark).\n");
        sb.Append("Only selected: pick up only selected items (green mark).\n\n");
        if (padOnly)
        {
            sb.Append("Right stick click on an item in your inventory adds it to or removes it from the list of the "
                      + "current mode. RT + right stick click closes this list.\n");
        }
        else
        {
            if (_markUnusable != null)
            {
                sb.Append("Marking with the mouse or keyboard is off: the game cannot read the MarkKey '")
                    .Append(_markUnusable).Append("'. Pick another key.\n");
            }
            else if (_markMain == KeyCode.None)
            {
                sb.Append("Marking with the mouse is off (MarkKey setting).\n");
            }
            else if (_markIsMouse)
            {
                sb.Append(_markGesture).Append(" an item in your inventory or a chest to add it to or remove it from the list of the current mode.\n");
            }
            else
            {
                sb.Append("Hover an item in your inventory or a chest and press ").Append(_markGesture)
                    .Append(" to add it to or remove it from the list of the current mode.\n");
            }
            if (Plugin.GamepadControls.Value && pad)
            {
                sb.Append("Controller (your inventory focused): right stick click = mark the item, LT + right stick click = "
                          + "change the mode, RT + right stick click = show the lists.\n");
            }
        }
        sb.Append("Items you do not carry: type /lootfilter in chat.\n\n");

        if (FilterState.Mode == FilterMode.OnlySelected)
        {
            AppendList(sb, "Selected", FilterState.Selected, active: true);
            AppendList(sb, "Ignored", FilterState.Ignored, active: false);
        }
        else
        {
            AppendList(sb, "Ignored", FilterState.Ignored, active: FilterState.Mode == FilterMode.SkipIgnored);
            AppendList(sb, "Selected", FilterState.Selected, active: false);
        }

        sb.Append('\n').Append(on ? "Auto pickup is on" : "Auto pickup is off")
            .Append(": turn it on or off with $KEY_AutoPickup. ")
            .Append(Plugin.ExemptHarvest.Value
                ? "Picking up or harvesting with $KEY_Use is never filtered."
                : "Picking up with $KEY_Use is never filtered.");
        return sb.ToString();
    }

    // "Ignored (5): Resin, Stone, ..." sorted by name in game language; tokens stay tokens (localized on show).
    private static void AppendList(StringBuilder sb, string title, HashSet<string> set, bool active)
    {
        sb.Append(title);
        if (active)
        {
            sb.Append(", in use now");
        }
        sb.Append(" (").Append(set.Count).Append("): ");
        if (set.Count == 0)
        {
            sb.Append("none\n");
            return;
        }
        var entries = new List<KeyValuePair<string, string>>(set.Count);
        foreach (var key in set)
        {
            entries.Add(new KeyValuePair<string, string>(ItemCatalog.DisplayName(key), ItemCatalog.Token(key)));
        }
        entries.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.CurrentCultureIgnoreCase));
        var shown = Mathf.Min(entries.Count, MaxNamesPerList);
        for (var i = 0; i < shown; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }
            sb.Append(entries[i].Value);
        }
        if (entries.Count > shown)
        {
            sb.Append(", +").Append(entries.Count - shown).Append(" more (type /lootfilter in chat for all)");
        }
        sb.Append('\n');
    }

    internal static void ToggleListsPanel(InventoryGui gui)
    {
        if (ListsShown)
        {
            HideListsPanel();
            return;
        }
        ShowListsPanel(gui);
    }

    private static void ShowListsPanel(InventoryGui gui)
    {
        var prefab = _tooltipPrefab;
        if (prefab == null && gui.m_playerGrid != null && gui.m_playerGrid.m_elementPrefab != null)
        {
            var element = gui.m_playerGrid.m_elementPrefab.GetComponent<InventoryElement>();
            prefab = element != null && element.m_tooltip != null ? element.m_tooltip.m_tooltipPrefab : null;
        }
        Transform anchorOwner = _buttonRT != null ? _buttonRT : gui.m_player;
        var canvas = anchorOwner != null ? anchorOwner.GetComponentInParent<Canvas>() : null;
        if (prefab == null || canvas == null)
        {
            WarnLists("no tooltip look found");
            return;
        }

        // Our own instance (not UITooltip's shared one): the two never fight.
        var panel = UnityEngine.Object.Instantiate(prefab, canvas.transform);
        panel.name = ListsName;
        foreach (var tip in panel.GetComponentsInChildren<UITooltip>(true))
        {
            UnityEngine.Object.DestroyImmediate(tip);
        }
        foreach (var loc in panel.GetComponentsInChildren<Localize>(true))
        {
            UnityEngine.Object.DestroyImmediate(loc);
        }
        var topic = Utils.FindChild(panel.transform, "Topic");
        var text = Utils.FindChild(panel.transform, "Text");
        _listsTopic = topic != null ? topic.GetComponent<TMP_Text>() : null;
        _listsText = text != null ? text.GetComponent<TMP_Text>() : null;
        if (_listsTopic == null || _listsText == null || panel.transform.childCount == 0)
        {
            UnityEngine.Object.Destroy(panel);
            _listsTopic = null;
            _listsText = null;
            WarnLists("tooltip look has no Topic/Text texts");
            return;
        }
        panel.SetActive(true);
        _listsPanel = panel;
        ListsShown = true;
        FillListsPanel();
    }

    private static void WarnLists(string why)
    {
        if (_listsWarned)
        {
            return;
        }
        _listsWarned = true;
        Log.Warning($"Loot filter lists panel cannot be shown ({why}). Type lootfilter in the chat or console to see the lists.");
    }

    internal static void HideListsPanel()
    {
        if (!ListsShown && _listsPanel == null)
        {
            return;
        }
        ListsShown = false;
        if (_listsPanel != null)
        {
            _listsPanel.SetActive(false);
            UnityEngine.Object.Destroy(_listsPanel);
        }
        _listsPanel = null;
        _listsTopic = null;
        _listsText = null;
    }

    private static void FillListsPanel()
    {
        if (_listsPanel == null || _listsTopic == null || _listsText == null)
        {
            HideListsPanel();
            return;
        }
        var loc = Localization.instance;
        _listsTopic.text = loc != null ? loc.Localize(TooltipTopic) : TooltipTopic;
        _listsText.text = loc != null ? loc.Localize(_listsTextCache) : _listsTextCache;
        PositionListsPanel();
    }

    // Bottom-left of the panel's visible box at the button's top-left, then kept on screen (like vanilla tooltips).
    private static void PositionListsPanel()
    {
        if (_listsPanel == null || _listsPanel.transform.childCount == 0)
        {
            return;
        }
        var box = _listsPanel.transform.GetChild(0) as RectTransform;
        if (box == null)
        {
            return;
        }
        RectTransform anchor = _buttonRT;
        if (anchor == null && _gui != null)
        {
            anchor = _gui.m_player;
        }
        if (anchor == null)
        {
            return;
        }
        anchor.GetWorldCorners(Corners);
        var point = Corners[1];
        _listsPanel.transform.position = point;
        LayoutRebuilder.ForceRebuildLayoutImmediate(box);
        box.GetWorldCorners(Corners2);
        _listsPanel.transform.position += point - Corners2[0];
        Utils.ClampUIToScreen(box);
    }

    // ---------------------------------------------------------------- gestures

    internal static void CacheMarkKey(KeyboardShortcut shortcut)
    {
        _markMain = shortcut.MainKey;
        _markMods = shortcut.Modifiers?.ToArray() ?? new KeyCode[0];
        _markUnusable = null;
        if (_markMain != KeyCode.None)
        {
            var bad = IsUsableKey(_markMain) ? _markMods.FirstOrDefault(m => !IsUsableKey(m)) : _markMain;
            if (bad != KeyCode.None)
            {
                MarkKeyUnusable(shortcut.ToString(), bad);
                return;
            }
        }
        _markIsMouse = _markMain >= KeyCode.Mouse0 && _markMain <= KeyCode.Mouse4;
        var mods = new StringBuilder();
        foreach (var m in _markMods)
        {
            mods.Append(m).Append(" + ");
        }
        switch (_markMain)
        {
            case KeyCode.Mouse0:
                _markGesture = mods + "Left-click";
                break;
            case KeyCode.Mouse1:
                _markGesture = mods + "Right-click";
                break;
            case KeyCode.Mouse2:
                _markGesture = mods + "Middle-click";
                break;
            default:
                _markGesture = _markIsMouse ? mods + _markMain.ToString() + "-click" : shortcut.ToString();
                break;
        }
        _shownVersion = -1;
        Log.Debug(_markMain == KeyCode.None ? "Loot filter mark key: none (marking with mouse/keyboard off)."
            : $"Loot filter mark key: {shortcut}.");
    }

    // Me check key once (bind, setting change): ZInput throw every frame on keyboard key it no can map (F13, Hash,
    // At...), and Mouse5/6 never fire. Static map, work before ZInput exist (BindConfig run early).
    private static bool IsUsableKey(KeyCode k)
    {
        if (!ZInput.IsKeyCodeValid(k))
        {
            return false; // None, Mouse5, Mouse6, Joystick1Button0 and up
        }
        if (k >= KeyCode.Mouse0 && k <= KeyCode.Mouse4)
        {
            return true;
        }
        if (k >= KeyCode.JoystickButton0)
        {
            return true; // ZInput fall back to South button, no throw
        }
        return ZInput.TryKeyCodeToKey(k, out _);
    }

    // Me turn marking off when game cannot read the key. Say so once, and in tooltip.
    private static void MarkKeyUnusable(string shortcut, KeyCode bad)
    {
        _markMain = KeyCode.None;
        _markMods = new KeyCode[0];
        _markIsMouse = false;
        _markUnusable = shortcut;
        _shownVersion = -1;
        var which = bad != KeyCode.None ? $"the key {bad}" : "this key";
        Log.Warning($"Controls.MarkKey = {shortcut}: the game cannot read {which}, so marking items with the mouse or "
                    + "keyboard is off. Pick another key (for example Mouse2, Mouse3, Mouse4 or a letter).");
    }

    private static void HandleMarkKey(InventoryGui gui)
    {
        if (_markMain == KeyCode.None)
        {
            return;
        }
        try
        {
            if (!ZInput.GetKeyDown(_markMain, logWarning: false))
            {
                return;
            }
            for (var i = 0; i < _markMods.Length; i++)
            {
                if (!ZInput.GetKey(_markMods[i], logWarning: false))
                {
                    return;
                }
            }
        }
        catch (ArgumentException)
        {
            // Key the check above missed (Keyboard.current[Key.None] throw ArgumentOutOfRangeException): off, once.
            MarkKeyUnusable(Plugin.MarkKey.Value.ToString(), KeyCode.None);
            return;
        }
        if (Blocked(gui, keyboard: !_markIsMouse))
        {
            return;
        }
        var grid = gui.m_playerGrid;
        var element = grid != null ? grid.GetHoveredElement() : null;
        if (element == null && gui.IsContainerOpen() && gui.m_container != null && gui.m_container.gameObject.activeInHierarchy
            && gui.ContainerGrid != null)
        {
            grid = gui.ContainerGrid;
            element = grid.GetHoveredElement();
        }
        if (element == null || !IsSlotOnTop(element))
        {
            return;
        }
        var inv = grid.GetInventory();
        var item = inv != null ? inv.GetItemAt(element.Position.x, element.Position.y) : null;
        if (item != null)
        {
            ToggleMark(item);
        }
    }

    // Controller, only while your inventory grid is the focused group. Chest grid: Sort chest use View/Select and L3,
    // nobody read R3 there.
    private static void HandleGamepad(InventoryGui gui)
    {
        if (!Plugin.GamepadControls.Value || !ZInput.IsExclusiveGamepadActive())
        {
            return;
        }
        var grid = gui.m_playerGrid;
        if (grid == null || grid.m_uiGroup == null || !grid.m_uiGroup.IsActive || !ZInput.GetButtonDown("JoyRStick"))
        {
            return;
        }
        if (Blocked(gui, keyboard: false))
        {
            return;
        }
        if (ZInput.GetButton("JoyLTrigger"))
        {
            CycleModeWithMessage();
            return;
        }
        if (ZInput.GetButton("JoyRTrigger"))
        {
            ToggleListsPanel(gui);
            return;
        }
        var item = grid.GetGamepadSelectedItem();
        if (item != null)
        {
            ToggleMark(item);
        }
    }

    // Something else own the input or cover the grids: no gesture.
    private static bool Blocked(InventoryGui gui, bool keyboard)
    {
        if (gui.m_dragGo != null)
        {
            return true;
        }
        if ((gui.m_splitDialog != null && gui.m_splitDialog.IsActive)
            || (gui.m_skillsDialog != null && gui.m_skillsDialog.gameObject.activeSelf)
            || (gui.m_textsDialog != null && gui.m_textsDialog.gameObject.activeSelf)
            || (gui.m_trophiesPanel != null && gui.m_trophiesPanel.activeSelf)
            || (gui.m_achievementsPanel != null && gui.m_achievementsPanel.gameObject.activeSelf)
            || (gui.m_variantDialog != null && gui.m_variantDialog.gameObject.activeSelf))
        {
            return true;
        }
        // Chat, console, pause menu, any text field (crafting search field of Crafting Search and Sort too).
        if ((Chat.instance != null && Chat.instance.HasFocus()) || Console.IsVisible() || Menu.IsVisible())
        {
            return true;
        }
        var es = EventSystem.current;
        var selected = es != null ? es.currentSelectedGameObject : null;
        if (selected != null)
        {
            var field = selected.GetComponent<TMP_InputField>();
            if (field != null && field.isFocused)
            {
                return true;
            }
        }
        // IMGUI text field (ConfigurationManager) has keyboard: a keyboard mark key is being typed there.
        return keyboard && GUIUtility.keyboardControl != 0;
    }

    // GetHoveredElement is pure geometry: it also find a slot hidden under a dialog or another mod's menu. Me ask
    // the UI raycast: first hit (vanilla item tooltip skipped) must be inside the slot.
    private static bool IsSlotOnTop(InventoryElement element)
    {
        var es = EventSystem.current;
        if (es == null)
        {
            return false;
        }
        if (_ped == null || !ReferenceEquals(_pedOwner, es))
        {
            _ped = new PointerEventData(es);
            _pedOwner = es;
        }
        _ped.position = ZInput.pointerPosition;
        Hits.Clear();
        es.RaycastAll(_ped, Hits);
        var tooltip = VanillaTooltipInstance();
        var slot = element.transform;
        for (var i = 0; i < Hits.Count; i++)
        {
            var go = Hits[i].gameObject;
            if (go == null)
            {
                continue;
            }
            if (tooltip != null && go.transform.IsChildOf(tooltip.transform))
            {
                continue;
            }
            var onTop = go.transform.IsChildOf(slot);
            Hits.Clear();
            return onTop;
        }
        Hits.Clear();
        return false;
    }

    // UITooltip.m_tooltip (private static, guiutils not publicized): the shown item tooltip, if any.
    private static GameObject VanillaTooltipInstance()
    {
        if (!_tooltipFieldLooked)
        {
            _tooltipFieldLooked = true;
            _tooltipInstanceField = typeof(UITooltip).GetField("m_tooltip", BindingFlags.NonPublic | BindingFlags.Static);
        }
        return _tooltipInstanceField != null ? _tooltipInstanceField.GetValue(null) as GameObject : null;
    }

    // ---------------------------------------------------------------- actions + messages

    internal static void ToggleMark(ItemDrop.ItemData item)
    {
        var icon = item.GetIcon();
        var prefab = item.m_dropPrefab;
        if (prefab == null)
        {
            SendMarkMessage("This item cannot be filtered.", icon);
            return;
        }
        var key = prefab.name;
        var name = item.m_shared != null && !string.IsNullOrEmpty(item.m_shared.m_name) ? item.m_shared.m_name : key;
        switch (FilterState.Mode)
        {
            case FilterMode.SkipIgnored:
                SendMarkMessage(FilterState.Toggle(FilterState.Ignored, key)
                    ? "Ignored by auto pickup: " + name
                    : "No longer ignored: " + name, icon);
                break;
            case FilterMode.OnlySelected:
                SendMarkMessage(FilterState.Toggle(FilterState.Selected, key)
                    ? "Selected for auto pickup: " + name
                    : "No longer selected: " + name, icon);
                break;
            default:
                SendMarkMessage(ZInput.IsExclusiveGamepadActive() && Plugin.GamepadControls.Value
                    ? "Choose Skip ignored or Only selected first: hold LT and click the right stick."
                    : "Choose Skip ignored or Only selected on the Auto pickup button first.", icon);
                break;
        }
    }

    // MessageHud show one top-left message per second and queue the rest: marking 8 items fast would replay for 8 s.
    // Badge already show the result, so me drop a mark message that come within 1 s of the last one.
    private static void SendMarkMessage(string text, Sprite icon)
    {
        var now = Time.unscaledTime;
        if (now - _lastMarkMessage < MarkMessageInterval)
        {
            return;
        }
        var player = Player.m_localPlayer;
        if (player == null)
        {
            return;
        }
        _lastMarkMessage = now;
        player.Message(MessageHud.MessageType.TopLeft, text, 0, icon);
    }

    private static void OnModeButtonClicked()
    {
        try
        {
            // Mouse click leave the button selected; Space/Enter (submit) must not click it again.
            var es = EventSystem.current;
            if (es != null && _button != null && es.currentSelectedGameObject == _button.gameObject)
            {
                es.SetSelectedGameObject(null);
            }
            CycleModeWithMessage();
        }
        catch (Exception e)
        {
            PatchGuard.Report($"{nameof(FilterUi)}.{nameof(OnModeButtonClicked)}", e);
        }
    }

    internal static void CycleModeWithMessage()
    {
        if (!FilterState.EnsureLocal())
        {
            return;
        }
        FilterState.CycleMode();
        ShowModeMessage();
        Refresh(force: false);
    }

    // Mode messages are never dropped (rare, important).
    internal static void ShowModeMessage()
    {
        var player = Player.m_localPlayer;
        if (player != null)
        {
            player.Message(MessageHud.MessageType.TopLeft, ModeMessage());
        }
    }

    internal static string ModeMessage()
    {
        string text;
        switch (FilterState.Mode)
        {
            case FilterMode.SkipIgnored:
                text = $"Auto pickup filter: Skip ignored items ({FilterState.Ignored.Count} ignored)";
                break;
            case FilterMode.OnlySelected:
                text = FilterState.Selected.Count == 0
                    ? "Auto pickup filter: Only selected items. Nothing is selected yet, so nothing is picked up automatically."
                    : $"Auto pickup filter: Only selected items ({FilterState.Selected.Count} selected)";
                break;
            default:
                text = "Auto pickup filter: Everything (normal game)";
                break;
        }
        if (!Player.m_enableAutoPickup)
        {
            // Me close first sentence, else two sentences glue.
            if (!text.EndsWith(".", StringComparison.Ordinal))
            {
                text += ".";
            }
            text += " Auto pickup is off: press $KEY_AutoPickup to turn it on.";
        }
        return text;
    }
}
