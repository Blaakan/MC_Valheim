using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MC.UX.ContainerSortMod;

// Me = the two buttons on the container panel: Sort, and criterion (By name / By type / By biome).
// Both are clones of vanilla Place stacks (same look, sound, scaling), children of its parent, so they hide with the
// panel. Controller: View/Select = Sort, left stick click = criterion, only while the chest grid is focused.
// Everything me make die in Destroy (feature off) and come back in Create (feature on / new InventoryGui).
internal static class SortChestUi
{
    private const string SortName = "MC_ContainerSort_Sort";
    private const string CriterionName = "MC_ContainerSort_Criterion";

    // ZInput button names. Never JoyRStick: Loot Pickup Filter use right stick click (player grid).
    internal const string SortKey = "JoyBack";
    internal const string CriterionKey = "JoyLStick";

    private const string SortTopic = "Sort";
    private const string CriterionTopic = "Sort order";
    private const string CriterionTip = "Click to change how Sort orders items: by name, by type (weapons, armour, food, "
                                        + "materials...) or by biome (Meadows to Deep North).";

    private const float ColumnGap = 8f;
    private const float RowGap = 6f;
    // Multiplayer: panel wait for ZDO owner, can take many frames.
    private const float WaitActiveSeconds = 2f;
    private const int WaitSettleFrames = 60;
    // Open animation may start near zero scale: all rects one point, overlap test see nothing.
    private const float DegenerateScale = 1e-3f;

    private sealed class Widget
    {
        internal GameObject Go;
        internal RectTransform Rect;
        internal TMP_Text Label;
        internal UITooltip Tip;
        internal UIGamePad Pad;
        internal TMP_Text Glyph;
        internal string Key;
    }

    private static InventoryGui _gui;
    private static Widget _sort;
    private static Widget _criterion;
    private static bool _warnedMissing;
    private static bool _parentHasLayoutGroup;

    // Per (container width, height, inventory rows): true = second row (no room next to Place stacks).
    private static readonly Dictionary<long, bool> SecondRow = new Dictionary<long, bool>();
    private static readonly HashSet<long> Checked = new HashSet<long>();
    private static readonly Vector3[] Corners = new Vector3[4];

    // ---------------------------------------------------------------- life

    // InventoryGui.Awake postfix, OnActivated, OnShow. Never make twice.
    internal static void Create(InventoryGui gui)
    {
        if (gui == null)
        {
            return;
        }
        if (!ReferenceEquals(_gui, gui))
        {
            // Old InventoryGui died with its scene (logout): our clones died with it. Fresh start.
            DestroyObjects();
            Forget();
            _gui = gui;
        }
        if (Alive(_sort) && Alive(_criterion))
        {
            return;
        }
        DestroyObjects(); // half made = make both again

        var src = gui.m_stackAllButton;
        if (src == null)
        {
            if (!_warnedMissing)
            {
                _warnedMissing = true;
                Log.Warning("Place stacks button not found (UI mod?), Sort button not added.");
            }
            return;
        }

        try
        {
            _sort = CloneButton(gui, src, SortName, SortKey, OnSortClicked);
            _criterion = CloneButton(gui, src, CriterionName, CriterionKey, OnCriterionClicked);
            if (_sort == null || _criterion == null)
            {
                DestroyObjects();
                return;
            }
            // Order right after Place stacks (matter only if parent lay out its children itself).
            var index = src.transform.GetSiblingIndex();
            _sort.Go.transform.SetSiblingIndex(index + 1);
            _criterion.Go.transform.SetSiblingIndex(index + 2);
            var parent = src.transform.parent;
            _parentHasLayoutGroup = parent != null && parent.GetComponent<LayoutGroup>() != null;
            CheckVanillaKeys(gui);
            ApplyLayout(gui, secondRow: false);
            RefreshTexts();
            Log.Debug($"Sort buttons created{(_parentHasLayoutGroup ? " (parent has a layout group: it places them)" : "")}.");
        }
        catch
        {
            DestroyObjects();
            throw;
        }
    }

    // Feature off: both buttons gone at once. Vanilla buttons never touched.
    internal static void Destroy()
    {
        DestroyObjects();
        Forget();
    }

    private static void DestroyObjects()
    {
        Kill(_sort);
        Kill(_criterion);
        _sort = null;
        _criterion = null;
    }

    private static void Kill(Widget widget)
    {
        if (widget == null || widget.Go == null)
        {
            return;
        }
        // SetActive false first: our tooltip hide itself (UITooltip.OnDisable).
        widget.Go.SetActive(false);
        Object.Destroy(widget.Go);
    }

    private static void Forget()
    {
        _gui = null;
        _parentHasLayoutGroup = false;
        SecondRow.Clear();
        Checked.Clear();
    }

    private static bool Alive(Widget widget) => widget != null && widget.Go != null;

    // ---------------------------------------------------------------- show

    // InventoryGui.Show postfix (and OnActivated). Me read gui.m_currentContainer, not Show argument: another mod's
    // prefix may skip vanilla Show, but my postfix still run.
    internal static void OnShow(InventoryGui gui)
    {
        if (gui == null)
        {
            return;
        }
        if (!ReferenceEquals(_gui, gui) || !Alive(_sort) || !Alive(_criterion))
        {
            Create(gui);
        }
        if (!Alive(_sort) || !Alive(_criterion))
        {
            return;
        }

        var container = gui.m_currentContainer;
        var show = container != null && ContainerSorter.IsEligible(container);
        _sort.Go.SetActive(show);
        _criterion.Go.SetActive(show);
        if (!show)
        {
            return;
        }

        var inventory = container.GetInventory();
        var width = inventory.GetWidth();
        var height = inventory.GetHeight();
        var player = Player.m_localPlayer;
        var rows = player != null ? player.GetInventory().GetHeight() : 0;
        var combo = Combo(width, height, rows);
        ApplyLayout(gui, SecondRow.TryGetValue(combo, out var second) && second);
        RefreshTexts();
        FixPadGroups(gui);
        Log.Debug($"Opened {Describe(container)} {width}x{height}.");

        // New size combination: check layout once the panel is really up (next frames).
        if (gui.isActiveAndEnabled && Checked.Add(combo))
        {
            gui.StartCoroutine(SelfCheck(gui, container, combo, width, height, rows));
        }
    }

    private static long Combo(int width, int height, int rows) => ((long)width << 32) | ((long)(height & 0xFFFF) << 16) | (long)(rows & 0xFFFF);

    private static string Describe(Container container)
    {
        var root = container.transform.root;
        var rootName = Utils.GetPrefabName(root.gameObject);
        return root == container.transform ? rootName : rootName + "/" + container.gameObject.name;
    }

    // Labels, tooltips, controller glyphs from current setting. Setting change (button, panel, file) call me too.
    internal static void RefreshTexts()
    {
        if (!Alive(_sort) || !Alive(_criterion))
        {
            return;
        }
        var criterion = Plugin.SortBy.Value;
        SetLabel(_sort, "Sort");
        SetLabel(_criterion, CriterionLabel(criterion));
        SetTip(_sort, SortTopic, "Rearrange this container from top left to bottom right, " + CriterionWords(criterion) + ".");
        SetTip(_criterion, CriterionTopic, CriterionTip);
        SetGlyph(_sort);
        SetGlyph(_criterion);
    }

    private static string CriterionLabel(SortCriterion criterion)
    {
        switch (criterion)
        {
            case SortCriterion.Name:
                return "By name";
            case SortCriterion.Biome:
                return "By biome";
            default:
                return "By type";
        }
    }

    private static string CriterionWords(SortCriterion criterion)
    {
        switch (criterion)
        {
            case SortCriterion.Name:
                return "by name";
            case SortCriterion.Biome:
                return "by biome";
            default:
                return "by type";
        }
    }

    private static void SetLabel(Widget widget, string text)
    {
        if (widget.Label != null && widget.Label.text != text)
        {
            widget.Label.text = text; // plain text, never a $token: no localizer rewrite it
        }
    }

    private static void SetTip(Widget widget, string topic, string text)
    {
        if (widget.Tip != null)
        {
            widget.Tip.Set(topic, text);
        }
    }

    // Glyph of the bound button (xbox / ps5 / switch sprite), asked again at each opening: controller may change.
    private static void SetGlyph(Widget widget)
    {
        if (widget.Glyph == null || widget.Pad == null || string.IsNullOrEmpty(widget.Pad.m_zinputKey))
        {
            return;
        }
        var glyph = Localization.instance.Localize("$KEY_" + widget.Key);
        if (widget.Glyph.text != glyph)
        {
            widget.Glyph.text = glyph;
        }
    }

    // ---------------------------------------------------------------- clicks

    private static void OnSortClicked()
    {
        try
        {
            if (PadPressOutsideGrid(SortKey))
            {
                return;
            }
            ContainerSorter.TrySortOpenContainer();
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(OnSortClicked), e);
        }
    }

    // Change criterion only, no sort (one action per button, no surprise save).
    private static void OnCriterionClicked()
    {
        try
        {
            if (PadPressOutsideGrid(CriterionKey))
            {
                return;
            }
            var next = Plugin.SortBy.Value switch
            {
                SortCriterion.Name => SortCriterion.Type,
                SortCriterion.Type => SortCriterion.Biome,
                _ => SortCriterion.Name,
            };
            Plugin.SortBy.Value = next; // BepInEx save the file; SettingChanged refresh texts too
            RefreshTexts();
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(OnCriterionClicked), e);
        }
    }

    // Controller key press that reach us while chest grid NOT focused = not for us (safety net for the frames
    // before FixPadGroups could run). Mouse click never have our pad key down.
    private static bool PadPressOutsideGrid(string key)
    {
        var gui = InventoryGui.instance;
        if (gui == null || !ZInput.GetButtonDown(key))
        {
            return false;
        }
        var group = ContainerGroup(gui);
        return group != null && !group.IsActive;
    }

    // ---------------------------------------------------------------- clone

    private static Widget CloneButton(InventoryGui gui, Button src, string name, string key, UnityAction onClick)
    {
        var parent = src.transform.parent;
        // Clone in an inactive holder: nothing of the clone wake up (Awake/OnEnable/Start) before me strip it.
        var holder = new GameObject("MC_ContainerSort_Build");
        holder.SetActive(false);
        holder.transform.SetParent(parent, false);
        try
        {
            var go = Object.Instantiate(src.gameObject, holder.transform, false);
            go.name = name;
            var widget = Configure(gui, go, key, onClick);
            if (widget == null)
            {
                return null; // dies with holder
            }
            go.transform.SetParent(parent, false);
            return widget;
        }
        finally
        {
            Object.Destroy(holder);
        }
    }

    private static Widget Configure(InventoryGui gui, GameObject go, string key, UnityAction onClick)
    {
        var button = go.GetComponent<Button>();
        var rect = go.transform as RectTransform;
        if (button == null || rect == null)
        {
            return null;
        }
        // New event: drop Place stacks listener (runtime ones are not cloned, persistent ones would be).
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(onClick);
        button.interactable = true;
        // D-pad never move selection onto our buttons.
        foreach (var selectable in go.GetComponentsInChildren<Selectable>(true))
        {
            selectable.navigation = new Navigation { mode = Navigation.Mode.None };
        }

        // Controller part. Pad on the button itself only; other pads in children = not ours.
        var pad = go.GetComponent<UIGamePad>();
        foreach (var other in go.GetComponentsInChildren<UIGamePad>(true))
        {
            if (other != pad)
            {
                Object.DestroyImmediate(other);
            }
        }
        // Hint outside the clone = vanilla's own object: never touch (UIGamePad set it active every frame).
        GameObject hint = null;
        if (pad != null && pad.m_hint != null && pad.m_hint != go && pad.m_hint.transform.IsChildOf(go.transform))
        {
            hint = pad.m_hint;
        }

        // Vanilla glyph switchers (UIInputHint) show Place stacks keys: their objects go, except our pad hint.
        var inputHints = go.GetComponentsInChildren<UIInputHint>(true);
        var hintObjects = new List<GameObject>();
        foreach (var ih in inputHints)
        {
            AddIfInside(hintObjects, ih.m_gamepadHint, go);
            AddIfInside(hintObjects, ih.m_mouseKeyboardHint, go);
            AddIfInside(hintObjects, ih.m_gamepadMouseHint, go);
            if (ih.m_inputLayoutSettings != null)
            {
                foreach (var setting in ih.m_inputLayoutSettings)
                {
                    if (setting != null)
                    {
                        AddIfInside(hintObjects, setting.m_hintObject, go);
                    }
                }
            }
        }

        // Label = first text not inside a hint.
        TMP_Text label = null;
        foreach (var text in go.GetComponentsInChildren<TMP_Text>(true))
        {
            if (Under(text.transform, hint) || UnderAny(text.transform, hintObjects))
            {
                continue;
            }
            label = text;
            break;
        }

        foreach (var obj in hintObjects)
        {
            if (hint != null && (obj == hint || hint.transform.IsChildOf(obj.transform) || obj.transform.IsChildOf(hint.transform)))
            {
                continue;
            }
            if (label != null && label.transform.IsChildOf(obj.transform))
            {
                continue;
            }
            obj.SetActive(false);
            Object.Destroy(obj);
        }
        foreach (var ih in inputHints)
        {
            Object.DestroyImmediate(ih);
        }
        // No auto-localize on our texts (plain English, set by me).
        foreach (var localize in go.GetComponentsInChildren<Localize>(true))
        {
            Object.DestroyImmediate(localize);
        }

        TMP_Text glyph = null;
        if (pad != null)
        {
            // Same frame as Instantiate, before its first Update: never answer Place stacks' key, never fire because
            // some other group is active.
            pad.m_keyCode = KeyCode.None;
            pad.m_zinputKey = key;
            pad.alternativeGroupHandler = null;
            if (hint != null)
            {
                glyph = hint.GetComponentInChildren<TMP_Text>(true);
                if (glyph == null)
                {
                    // Hint without text: no glyph, key still work.
                    hint.SetActive(false);
                    hint = null;
                }
            }
            pad.m_hint = hint;
        }

        var tip = SetupTooltip(gui, go);

        return new Widget
        {
            Go = go,
            Rect = rect,
            Label = label,
            Tip = tip,
            Pad = pad,
            Glyph = glyph,
            Key = key,
        };
    }

    // Keep Place stacks tooltip look when it has one; else the inventory slot tooltip (topic + text).
    private static UITooltip SetupTooltip(InventoryGui gui, GameObject go)
    {
        var tip = go.GetComponent<UITooltip>();
        foreach (var other in go.GetComponentsInChildren<UITooltip>(true))
        {
            if (other != tip)
            {
                Object.DestroyImmediate(other);
            }
        }
        if (tip != null && tip.m_tooltipPrefab == null)
        {
            tip.m_tooltipPrefab = SlotTooltipPrefab(gui);
            if (tip.m_tooltipPrefab == null)
            {
                Object.DestroyImmediate(tip);
                tip = null;
            }
        }
        if (tip == null)
        {
            var prefab = SlotTooltipPrefab(gui);
            if (prefab == null)
            {
                return null;
            }
            tip = go.AddComponent<UITooltip>();
            tip.m_tooltipPrefab = prefab;
        }
        tip.m_gamepadFocusObject = null; // may point at a vanilla object outside the clone
        return tip;
    }

    private static GameObject SlotTooltipPrefab(InventoryGui gui)
    {
        var grid = gui.m_playerGrid;
        var element = grid != null && grid.m_elementPrefab != null ? grid.m_elementPrefab.GetComponent<InventoryElement>() : null;
        return element != null && element.m_tooltip != null ? element.m_tooltip.m_tooltipPrefab : null;
    }

    private static void AddIfInside(List<GameObject> list, GameObject obj, GameObject root)
    {
        if (obj != null && obj != root && obj.transform.IsChildOf(root.transform) && !list.Contains(obj))
        {
            list.Add(obj);
        }
    }

    private static bool Under(Transform t, GameObject obj) => obj != null && t.IsChildOf(obj.transform);

    private static bool UnderAny(Transform t, List<GameObject> objects)
    {
        foreach (var obj in objects)
        {
            if (Under(t, obj))
            {
                return true;
            }
        }
        return false;
    }

    // Vanilla Take all / Place stacks already on one of our keys (other game version, UI mod): key stay theirs,
    // our button = mouse only. Else one press would fire whichever pad update first (shared 2-frame lock).
    private static void CheckVanillaKeys(InventoryGui gui)
    {
        foreach (var vanilla in new[] { gui.m_takeAllButton, gui.m_stackAllButton })
        {
            var pad = vanilla != null ? vanilla.GetComponent<UIGamePad>() : null;
            if (pad == null || string.IsNullOrEmpty(pad.m_zinputKey))
            {
                continue;
            }
            foreach (var widget in new[] { _sort, _criterion })
            {
                if (widget.Pad != null && widget.Pad.m_zinputKey == pad.m_zinputKey)
                {
                    Log.Warning($"The game's '{vanilla.name}' button already uses the controller key {pad.m_zinputKey}: "
                                + $"'{widget.Go.name}' works with the mouse only.");
                    widget.Pad.m_zinputKey = null;
                    if (widget.Pad.m_hint != null)
                    {
                        widget.Pad.m_hint.SetActive(false);
                        widget.Pad.m_hint = null;
                    }
                    widget.Glyph = null;
                }
            }
        }
    }

    // UIGamePad take its group from the nearest parent in Start. Me make sure it is the chest grid group, so our
    // keys (and glyphs) work only while the chest grid is focused. Only after Start ran (it would overwrite).
    private static void FixPadGroups(InventoryGui gui)
    {
        var group = ContainerGroup(gui);
        if (group == null)
        {
            return;
        }
        FixPadGroup(_sort, group);
        FixPadGroup(_criterion, group);
    }

    private static void FixPadGroup(Widget widget, UIGroupHandler group)
    {
        var pad = Alive(widget) ? widget.Pad : null;
        if (pad == null || pad.m_button == null || ReferenceEquals(pad.m_group, group))
        {
            return; // no pad, Start not run yet, or already right
        }
        Log.Debug($"{widget.Go.name}: controller group '{(pad.m_group != null ? pad.m_group.name : "none")}' -> "
                  + $"chest grid group '{group.name}'.");
        pad.m_group = group;
    }

    private static UIGroupHandler ContainerGroup(InventoryGui gui)
    {
        var grid = gui.ContainerGrid;
        if (grid != null && grid.m_uiGroup != null)
        {
            return grid.m_uiGroup;
        }
        var groups = gui.m_uiGroups;
        return groups != null && groups.Length > 0 ? groups[0] : null;
    }

    // ---------------------------------------------------------------- layout

    // Offsets from Place stacks, in its parent's space: follow the vanilla buttons wherever they (or a UI mod) put
    // them. A = same row after Place stacks. B = new row on the side away from the grid.
    private static void ApplyLayout(InventoryGui gui, bool secondRow)
    {
        if (_parentHasLayoutGroup || !Alive(_sort) || !Alive(_criterion))
        {
            return;
        }
        var stack = gui.m_stackAllButton != null ? gui.m_stackAllButton.transform as RectTransform : null;
        var parent = stack != null ? stack.parent : null;
        if (parent == null)
        {
            return;
        }
        // Work in parent space. Our clones = Place stacks size, same anchors: anchoredPosition offset = parent offset.
        var stackRect = ParentRect(stack, parent);
        var take = gui.m_takeAllButton != null ? gui.m_takeAllButton.transform as RectTransform : null;
        var horizontal = true;
        var dir = 1f;
        var gap = ColumnGap;
        if (take != null && take.gameObject.activeSelf)
        {
            var takeRect = ParentRect(take, parent);
            var d = stackRect.center - takeRect.center;
            if (d.magnitude >= 1f)
            {
                // Row goes from Take all to Place stacks; me continue it the same way, with the same gap.
                horizontal = Mathf.Abs(d.x) >= Mathf.Abs(d.y);
                dir = (horizontal ? d.x : d.y) >= 0f ? 1f : -1f;
                var measured = horizontal
                    ? (dir > 0f ? stackRect.xMin - takeRect.xMax : takeRect.xMin - stackRect.xMax)
                    : (dir > 0f ? stackRect.yMin - takeRect.yMax : takeRect.yMin - stackRect.yMax);
                if (measured >= 0f && measured <= 3f * ColumnGap)
                {
                    gap = measured;
                }
            }
        }
        var pitch = (horizontal ? stackRect.width : stackRect.height) + gap;
        var along = horizontal ? new Vector2(dir * pitch, 0f) : new Vector2(0f, dir * pitch);
        var b = stack.anchoredPosition;
        Vector2 sortPos;
        Vector2 criterionPos;
        if (!secondRow)
        {
            sortPos = b + along;
            criterionPos = b + 2f * along;
        }
        else
        {
            // New row across, away from the grid: criterion under Place stacks, Sort under the slot before it.
            var across = SecondRowOffset(gui, parent, stackRect, horizontal);
            sortPos = b - along + across;
            criterionPos = b + across;
        }
        _sort.Rect.anchoredPosition = sortPos;
        _criterion.Rect.anchoredPosition = criterionPos;
    }

    // Offset across the button row (row sideways = up/down), toward the side away from the grid.
    private static Vector2 SecondRowOffset(InventoryGui gui, Transform parent, Rect stackRect, bool horizontal)
    {
        var grid = gui.ContainerGrid != null ? gui.ContainerGrid.transform as RectTransform : null;
        var gridCenter = grid != null ? ParentRect(grid, parent).center : stackRect.center + (horizontal ? Vector2.up : Vector2.left);
        if (horizontal)
        {
            var dy = stackRect.height + RowGap;
            return new Vector2(0f, gridCenter.y > stackRect.center.y ? -dy : dy);
        }
        var dx = stackRect.width + ColumnGap;
        return new Vector2(gridCenter.x > stackRect.center.x ? -dx : dx, 0f);
    }

    // Rect of rt in parent's local space (handles other pivots, anchors, scales).
    private static Rect ParentRect(RectTransform rt, Transform parent)
    {
        rt.GetWorldCorners(Corners);
        var min = (Vector2)parent.InverseTransformPoint(Corners[0]);
        var max = min;
        for (var i = 1; i < 4; i++)
        {
            var p = (Vector2)parent.InverseTransformPoint(Corners[i]);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    // ---------------------------------------------------------------- self-check

    // Runs once per new size combination, a few frames after the panel opened (layout settled). Pick row A or B
    // (first that overlap nothing), warn when none fits or buttons leave the screen, dump layout at Debug level.
    private static IEnumerator SelfCheck(InventoryGui gui, Container container, long combo, int width, int height, int rows)
    {
        var panel = gui.m_container;
        // Panel show only once this client own the chest ZDO: on a server that can be many frames after Show.
        var deadline = Time.unscaledTime + WaitActiveSeconds;
        while (StillOurs(gui) && panel != null && !panel.gameObject.activeInHierarchy
               && ReferenceEquals(gui.m_currentContainer, container) && Time.unscaledTime < deadline)
        {
            yield return null;
        }
        // One more frame: UIGamePad.Start ran, layout groups done.
        yield return null;
        if (!StillOurs(gui))
        {
            yield break;
        }
        if (panel == null || !panel.gameObject.activeInHierarchy || !_sort.Go.activeInHierarchy
            || !ReferenceEquals(gui.m_currentContainer, container))
        {
            Checked.Remove(combo); // panel never came up (or other chest now): try again next opening
            yield break;
        }
        // Pads started now: put them on chest grid group at once (glyphs only while chest grid focused).
        Guarded(nameof(FixPadGroups), () => FixPadGroups(gui));

        // Open animation near zero scale: every rect one point, overlap test see nothing. Wait a bit; still collapsed
        // = judge nothing, keep nothing, try again next opening.
        for (var i = 0; i < WaitSettleFrames && Degenerate(panel); i++)
        {
            yield return null;
            if (!StillOurs(gui) || panel == null || !panel.gameObject.activeInHierarchy
                || !ReferenceEquals(gui.m_currentContainer, container))
            {
                Checked.Remove(combo);
                yield break;
            }
        }
        if (Degenerate(panel))
        {
            Checked.Remove(combo);
            yield break;
        }
        Guarded(nameof(CheckLayout), () => CheckLayout(gui, combo, width, height, rows));

        // Screen check after show animation: wait until panel stop moving (or give up).
        var lastPos = panel.position;
        var lastScale = panel.lossyScale;
        var stable = 0;
        for (var i = 0; i < WaitSettleFrames && stable < 2; i++)
        {
            yield return null;
            if (!StillOurs(gui) || panel == null)
            {
                yield break;
            }
            var pos = panel.position;
            var scale = panel.lossyScale;
            if (pos == lastPos && scale == lastScale)
            {
                stable++;
            }
            else
            {
                stable = 0;
                lastPos = pos;
                lastScale = scale;
            }
        }
        if (!panel.gameObject.activeInHierarchy || !_sort.Go.activeInHierarchy || !ReferenceEquals(gui.m_currentContainer, container))
        {
            yield break;
        }
        Guarded(nameof(CheckScreen), () => CheckScreen(width, height, rows));
        Guarded(nameof(DumpLayout), () => DumpLayout(gui, width, height, rows));
    }

    // 1e-3, not 0.5: overlap tests same at any scale; only a collapsed panel lie. Small GUI scale must still check.
    private static bool Degenerate(Transform panel)
    {
        var scale = panel.lossyScale;
        return Mathf.Abs(scale.x) < DegenerateScale || Mathf.Abs(scale.y) < DegenerateScale;
    }

    private static bool StillOurs(InventoryGui gui) =>
        gui != null && ReferenceEquals(_gui, gui) && Alive(_sort) && Alive(_criterion);

    private static void Guarded(string site, Action step)
    {
        try
        {
            step();
        }
        catch (Exception e)
        {
            PatchGuard.Report($"{nameof(SortChestUi)}.{site}", e);
        }
    }

    private static void CheckLayout(InventoryGui gui, long combo, int width, int height, int rows)
    {
        FixPadGroups(gui);
        Canvas.ForceUpdateCanvases();
        if (_parentHasLayoutGroup)
        {
            var problem = FindProblem(gui);
            if (problem != null)
            {
                WarnLayout(problem, width, height, rows);
            }
            return;
        }

        ApplyLayout(gui, secondRow: false);
        var problemA = FindProblem(gui);
        if (problemA == null)
        {
            SecondRow[combo] = false;
            return;
        }
        ApplyLayout(gui, secondRow: true);
        var problemB = FindProblem(gui);
        if (problemB == null)
        {
            SecondRow[combo] = true;
            Log.Info($"No room for the Sort buttons next to Place stacks on a {width}x{height} container ({problemA}): "
                     + "they go in a second row.");
            return;
        }
        ApplyLayout(gui, secondRow: false);
        SecondRow[combo] = false;
        WarnLayout(problemA, width, height, rows);
    }

    private static void WarnLayout(string problem, int width, int height, int rows)
    {
        Log.Warning($"Sort buttons {problem} on a {width}x{height} container with {rows} inventory rows; "
                    + "another UI mod may have moved the panel.");
    }

    // Null = fine. Else what is wrong, as words for the log.
    private static string FindProblem(InventoryGui gui)
    {
        var sort = WorldRect(_sort.Rect);
        var criterion = WorldRect(_criterion.Rect);
        var tolerance = 0.05f * Mathf.Max(1e-3f, Mathf.Min(sort.height, criterion.height));
        if (Overlaps(sort, criterion, tolerance))
        {
            return "overlap each other";
        }
        var take = gui.m_takeAllButton != null ? gui.m_takeAllButton.transform as RectTransform : null;
        var stack = gui.m_stackAllButton != null ? gui.m_stackAllButton.transform as RectTransform : null;
        var grid = gui.ContainerGrid;
        var obstacles = new (string, RectTransform)[]
        {
            ("Take all", take),
            ("Place stacks", stack),
            ("the container name", gui.m_containerName != null ? gui.m_containerName.rectTransform : null),
            ("the container weight", gui.m_containerWeight != null ? gui.m_containerWeight.rectTransform : null),
            ("the container grid", grid != null ? grid.transform as RectTransform : null),
            ("the grid scrollbar", grid != null && grid.m_scrollbar != null ? grid.m_scrollbar.transform as RectTransform : null),
        };
        foreach (var (what, rt) in obstacles)
        {
            if (rt == null || !rt.gameObject.activeInHierarchy)
            {
                continue;
            }
            var r = WorldRect(rt);
            if (Overlaps(sort, r, tolerance) || Overlaps(criterion, r, tolerance))
            {
                return "overlap " + what;
            }
        }

        // Allowed area = panel, grown to hold the vanilla buttons (they may hang out of the panel rect).
        var area = WorldRect(gui.m_container);
        if (take != null && take.gameObject.activeInHierarchy)
        {
            area = Union(area, WorldRect(take));
        }
        if (stack != null && stack.gameObject.activeInHierarchy)
        {
            area = Union(area, WorldRect(stack));
        }
        if (!Inside(sort, area, tolerance) || !Inside(criterion, area, tolerance))
        {
            return "stick out of the container panel";
        }
        return null;
    }

    // Overlay canvas: world units = screen pixels. Other canvas modes: no screen check.
    private static void CheckScreen(int width, int height, int rows)
    {
        var canvas = _sort.Go.GetComponentInParent<Canvas>();
        var root = canvas != null ? canvas.rootCanvas : null;
        if (root == null || root.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            Log.Debug("Sort buttons: canvas is not screen-space overlay, screen check skipped.");
            return;
        }
        var screen = new Rect(0f, 0f, Screen.width, Screen.height);
        if (!Inside(WorldRect(_sort.Rect), screen, 1f) || !Inside(WorldRect(_criterion.Rect), screen, 1f))
        {
            WarnLayout("are off screen", width, height, rows);
        }
    }

    private static Rect WorldRect(RectTransform rt)
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

    private static bool Overlaps(Rect a, Rect b, float tolerance) =>
        Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) > tolerance
        && Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) > tolerance;

    private static bool Inside(Rect inner, Rect outer, float tolerance) =>
        inner.xMin >= outer.xMin - tolerance && inner.xMax <= outer.xMax + tolerance
        && inner.yMin >= outer.yMin - tolerance && inner.yMax <= outer.yMax + tolerance;

    private static Rect Union(Rect a, Rect b) =>
        Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

    // ---------------------------------------------------------------- debug dump

    // Debug level, once per size combination: where vanilla put the panel parts and which keys its buttons use.
    // This is the data to confirm layout and controller keys in game (design 7.3).
    private static void DumpLayout(InventoryGui gui, int width, int height, int rows)
    {
        var sb = new StringBuilder();
        sb.Append("Container panel layout (").Append(width).Append('x').Append(height).Append(" container, ")
            .Append(rows).Append(" inventory rows, screen ").Append(Screen.width).Append('x').Append(Screen.height).Append("):");
        var grid = gui.ContainerGrid;
        Describe(sb, "m_container", gui.m_container, gui);
        Describe(sb, "Take all", gui.m_takeAllButton != null ? gui.m_takeAllButton.transform : null, gui);
        Describe(sb, "Place stacks", gui.m_stackAllButton != null ? gui.m_stackAllButton.transform : null, gui);
        Describe(sb, "Sort", _sort.Go.transform, gui);
        Describe(sb, "Criterion", _criterion.Go.transform, gui);
        Describe(sb, "m_containerName", gui.m_containerName != null ? gui.m_containerName.transform : null, gui);
        Describe(sb, "m_containerWeight", gui.m_containerWeight != null ? gui.m_containerWeight.transform : null, gui);
        Describe(sb, "container grid", grid != null ? grid.transform : null, gui);
        Describe(sb, "grid root", grid != null && grid.m_gridRoot != null ? grid.m_gridRoot : null, gui);
        Describe(sb, "grid scrollbar", grid != null && grid.m_scrollbar != null ? grid.m_scrollbar.transform : null, gui);
        Describe(sb, "m_player", gui.m_player, gui);

        var buttonsParent = gui.m_stackAllButton != null ? gui.m_stackAllButton.transform.parent : null;
        sb.Append("\n buttons parent components: ").Append(Components(buttonsParent));
        sb.Append("\n m_container components: ").Append(Components(gui.m_container));
        var canvas = gui.m_container != null ? gui.m_container.GetComponentInParent<Canvas>() : null;
        sb.Append("\n root canvas: ").Append(canvas != null ? canvas.rootCanvas.renderMode.ToString() : "none");

        var groups = gui.m_uiGroups;
        if (groups != null)
        {
            for (var i = 0; i < groups.Length; i++)
            {
                sb.Append("\n uiGroups[").Append(i).Append("] = ").Append(groups[i] != null ? Path(groups[i].transform) : "null");
            }
        }
        var containerGroup = ContainerGroup(gui);
        sb.Append("\n container grid group: ").Append(containerGroup != null ? Path(containerGroup.transform) : "none");
        DescribePad(sb, "Take all", gui.m_takeAllButton, containerGroup);
        DescribePad(sb, "Place stacks", gui.m_stackAllButton, containerGroup);
        DescribePad(sb, "Sort", _sort.Go.GetComponent<Button>(), containerGroup);
        DescribePad(sb, "Criterion", _criterion.Go.GetComponent<Button>(), containerGroup);
        Log.Debug(sb.ToString());
    }

    private static void Describe(StringBuilder sb, string label, Transform t, InventoryGui gui)
    {
        sb.Append("\n ").Append(label).Append(": ");
        if (t == null)
        {
            sb.Append("null");
            return;
        }
        sb.Append(Path(t)).Append(" active=").Append(t.gameObject.activeInHierarchy);
        if (gui.m_container != null)
        {
            sb.Append(" underContainer=").Append(t.IsChildOf(gui.m_container));
        }
        if (t is RectTransform rt)
        {
            var world = WorldRect(rt);
            sb.Append(" anchors=").Append(rt.anchorMin).Append('-').Append(rt.anchorMax)
                .Append(" pivot=").Append(rt.pivot)
                .Append(" pos=").Append(rt.anchoredPosition)
                .Append(" size=").Append(rt.sizeDelta)
                .Append(" rect=").Append(rt.rect)
                .Append(" world=(").Append(world.xMin.ToString("F0")).Append(',').Append(world.yMin.ToString("F0"))
                .Append(")-(").Append(world.xMax.ToString("F0")).Append(',').Append(world.yMax.ToString("F0")).Append(')');
        }
    }

    private static void DescribePad(StringBuilder sb, string label, Button button, UIGroupHandler containerGroup)
    {
        sb.Append("\n pad ").Append(label).Append(": ");
        var pad = button != null ? button.GetComponent<UIGamePad>() : null;
        if (pad == null)
        {
            sb.Append("none");
            return;
        }
        var parentGroup = pad.GetComponentInParent<UIGroupHandler>();
        sb.Append("key=").Append(string.IsNullOrEmpty(pad.m_zinputKey) ? "-" : pad.m_zinputKey)
            .Append(" keyCode=").Append(pad.m_keyCode)
            .Append(" hint=").Append(pad.m_hint != null ? Path(pad.m_hint.transform) : "none")
            .Append(" altGroup=").Append(pad.alternativeGroupHandler != null ? pad.alternativeGroupHandler.name : "none")
            .Append(" parentGroup=").Append(parentGroup != null ? parentGroup.name : "none")
            .Append(" parentGroupIsContainerGrid=").Append(parentGroup != null && ReferenceEquals(parentGroup, containerGroup))
            .Append(" group=").Append(pad.m_group != null ? pad.m_group.name : "none");
        sb.Append(" blocking=[");
        if (pad.m_blockingElements != null)
        {
            for (var i = 0; i < pad.m_blockingElements.Count; i++)
            {
                var obj = pad.m_blockingElements[i];
                sb.Append(i > 0 ? ", " : "").Append(obj != null ? obj.name : "null");
            }
        }
        sb.Append(']');
    }

    private static string Components(Transform t)
    {
        if (t == null)
        {
            return "none";
        }
        var names = new List<string>();
        foreach (var component in t.GetComponents<Component>())
        {
            names.Add(component != null ? component.GetType().Name : "missing");
        }
        return string.Join(", ", names);
    }

    private static string Path(Transform t)
    {
        var sb = new StringBuilder(t.name);
        for (var p = t.parent; p != null; p = p.parent)
        {
            sb.Insert(0, p.name + "/");
        }
        return sb.ToString();
    }
}
