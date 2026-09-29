using System;
using System.Collections;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MC.Exploration.CompendiumEncyclopediaMod;

// Me = the opt-in Encyclopedia button in the inventory side panel (design 3.6; setting Display.SideButton, default off:
// then me never exist and the vanilla row is untouched; Sync add or remove me live). Clone of the game's Valheim
// Compendium button (found by its saved click handler OnOpenTexts, fallback the other side buttons), stripped
// (CloneUtil), book icon drawn in code, tooltip "Encyclopedia", placed in the row of the vanilla side controls:
//   - parent has a LayoutGroup: sibling right after the anchor, the layout place it;
//   - else (vanilla): SideRow lay the row out again with ours right after the anchor, evenly spaced inside the panel
//     background; vanilla controls put back exactly when me go away (feature off, button hidden);
//   - SideRow gave up (another mod keep moving the row) or cannot fit: line of the active controls measured (axis with
//     the larger spread, median step), one step after the last; overlap or off screen = one step before the first.
//   Self-check once per InventoryGui (after the panel settled): inside the panel background, no overlap, on screen;
//   still bad = one Warning with the rects.
// Controller: own UIGamePad on View/Select (JoyBack), group forced to the side panel group; the real gate is the
// UIGamePad.ButtonPressed prefix (PadAllowed), so a rejected press never take the shared pad lock (map, Sort Chest).
// Everything me make die in Destroy (feature off) and come back in Create (feature on / new InventoryGui).
internal static class SideButton
{
    internal const string ButtonName = "MC_Compendium_Button";
    internal const string PadKey = "JoyBack";
    internal const int SidePanelGroup = 2;

    private const float FallbackGap = 4f;
    private const int SettleFrames = 90;
    private static readonly int VisibleParam = Animator.StringToHash("visible");
    private static readonly string[] AnchorMethods = { "OnOpenTexts", "OnOpenTrophies", "OnOpenSkills", "OnOpenAchievements" };

    internal enum Placement
    {
        None,
        Row,
        LayoutGroup,
        AfterLast,
        BeforeFirst,
    }

    private static InventoryGui _gui;
    private static Button _anchor;
    private static string _anchorMethod = "";
    private static GameObject _go;
    private static RectTransform _rt;
    private static Button _button;
    private static UIGamePad _pad;
    private static UITooltip _tip;
    private static Image _icon;
    private static bool _missingWarned;
    private static bool _hidden;
    private static bool _checkStarted;
    private static bool _preferBefore;
    private static Placement _placement;
    private static string _lastCheck = "not run";

    internal static Button Button => _button;
    internal static RectTransform Rect => _rt;
    internal static Button AnchorButton => _anchor;
    internal static string AnchorMethod => _anchorMethod;
    internal static UIGamePad Pad => _pad;
    internal static UITooltip Tooltip => _tip;
    internal static Image Icon => _icon;
    internal static Placement CurrentPlacement => _placement;
    internal static string LastCheck => _lastCheck;
    internal static bool Exists => _go != null;
    internal static string RowLayoutState => SideRow.State;

    // ---------------------------------------------------------------- life

    /// <summary>
    /// Follow the opt-in setting (OnActivated, setting changed, self tests): on = make the button (placed at once when
    /// the inventory is shown); off = button gone, vanilla side controls back exactly.
    /// </summary>
    internal static void Sync(InventoryGui gui)
    {
        if (!Plugin.SideButtonOn)
        {
            if (_go != null || _gui != null)
            {
                Destroy();
            }
            return;
        }
        if (gui == null)
        {
            return;
        }
        if (InventoryShown(gui))
        {
            OnShow(gui);
        }
        else
        {
            Create(gui);
        }
    }

    // InventoryGui.Awake postfix, Sync, Show postfix. Never make twice. Setting off = nothing.
    internal static void Create(InventoryGui gui)
    {
        if (gui == null || !Plugin.SideButtonOn)
        {
            return;
        }
        if (!ReferenceEquals(_gui, gui))
        {
            // Old InventoryGui died with its scene (logout): our clone and its row died with it. Fresh start.
            SideRow.Forget();
            DestroyObjects();
            Forget();
            _gui = gui;
        }
        // Window refused on this InventoryGui before (setting turned on later, or off and on): no button, a dead one
        // would do nothing on click.
        _hidden |= CompendiumWindow.RefusedFor(gui);
        if (_go != null || _hidden)
        {
            return;
        }
        _anchor = FindAnchor(gui, out _anchorMethod);
        if (_anchor == null)
        {
            if (!_missingWarned)
            {
                _missingWarned = true;
                Log.Warning("Valheim Compendium button not found (UI mod?): no Encyclopedia button.");
            }
            return;
        }
        try
        {
            Build(gui);
            Place();
        }
        catch
        {
            SideRow.Restore();
            DestroyObjects();
            throw;
        }
    }

    // Feature off: vanilla side controls back exactly where they were; button, pad, tooltip, drawn sprite gone at once.
    internal static void Destroy()
    {
        SideRow.Restore();
        DestroyObjects();
        Forget();
        BookIcon.Destroy();
    }

    // Window cannot be built on this InventoryGui (texts dialog missing): button hidden until next InventoryGui, the
    // vanilla row back as it was. Button made later (setting on after, off and on): Create ask the window again.
    internal static void HideForThisSession()
    {
        _hidden = true;
        SideRow.Restore();
        if (_go != null)
        {
            _go.SetActive(false);
        }
    }

    private static void DestroyObjects()
    {
        if (_go != null)
        {
            // SetActive false first: our tooltip hide itself (UITooltip.OnDisable).
            _go.SetActive(false);
            Object.Destroy(_go);
        }
        _go = null;
        _rt = null;
        _button = null;
        _pad = null;
        _tip = null;
        _icon = null;
    }

    private static void Forget()
    {
        _gui = null;
        _anchor = null;
        _anchorMethod = "";
        _hidden = false;
        _checkStarted = false;
        _preferBefore = false;
        _placement = Placement.None;
        _lastCheck = "not run";
    }

    private static bool StillOurs(InventoryGui gui) => gui != null && ReferenceEquals(_gui, gui) && _go != null;

    /// <summary>Animator flag, not IsVisible(): IsVisible stay true one frame after Hide.</summary>
    internal static bool InventoryShown(InventoryGui gui) =>
        gui != null && gui.m_animator != null && gui.m_animator.GetBool(VisibleParam);

    // ---------------------------------------------------------------- show

    // InventoryGui.Show postfix. UI mods may move vanilla buttons after Awake: place again (cheap, math only).
    internal static void OnShow(InventoryGui gui)
    {
        if (gui == null || !Plugin.SideButtonOn)
        {
            return;
        }
        if (!ReferenceEquals(_gui, gui) || _go == null)
        {
            Create(gui);
        }
        if (_go == null)
        {
            return;
        }
        if (_go.activeSelf == _hidden)
        {
            _go.SetActive(!_hidden);
        }
        Place();
        FixPadGroup(gui);
        if (!_checkStarted && gui.isActiveAndEnabled)
        {
            _checkStarted = true;
            gui.StartCoroutine(SelfCheck(gui));
        }
    }

    // ---------------------------------------------------------------- clicks and pad gate

    private static void OnClicked()
    {
        try
        {
            CompendiumWindow.Open();
        }
        catch (Exception e)
        {
            PatchGuard.Report("Encyclopedia button click", e);
        }
    }

    /// <summary>
    /// UIGamePad.ButtonPressed prefix ask me for our pad: View/Select open the window only while the inventory is shown,
    /// the side panel group is the active one, the window is closed, the player is alive and our search is not typing.
    /// </summary>
    internal static bool PadAllowed()
    {
        var gui = InventoryGui.instance;
        if (gui == null || !ReferenceEquals(gui, _gui) || gui.m_animator == null || !gui.m_animator.GetBool(VisibleParam))
        {
            return false;
        }
        if (gui.ActiveGroup != SidePanelGroup || CompendiumWindow.IsOpen || FocusGuard.Active)
        {
            return false;
        }
        var p = Player.m_localPlayer;
        return p != null && !p.IsDead() && !p.IsTeleporting() && !p.InCutscene();
    }

    // ---------------------------------------------------------------- build

    // Persistent (prefab-saved) click handler name = how me find the Texts button (no InventoryGui field for it).
    private static Button FindAnchor(InventoryGui gui, out string method)
    {
        method = "";
        var buttons = gui.GetComponentsInChildren<Button>(true);
        foreach (var wanted in AnchorMethods)
        {
            foreach (var b in buttons)
            {
                if (b == null || b.name == ButtonName)
                {
                    continue;
                }
                var ev = b.onClick;
                for (var i = 0; i < ev.GetPersistentEventCount(); i++)
                {
                    if (ev.GetPersistentMethodName(i) == wanted)
                    {
                        method = wanted;
                        return b;
                    }
                }
            }
        }
        return null;
    }

    private static void Build(InventoryGui gui)
    {
        var parent = _anchor.transform.parent;
        // Clone in an inactive holder: nothing of the clone wake up before me strip it.
        var holder = new GameObject("MC_Compendium_Build");
        holder.SetActive(false);
        holder.transform.SetParent(parent, false);
        try
        {
            var go = Object.Instantiate(_anchor.gameObject, holder.transform, false);
            go.name = ButtonName;
            var tip = go.GetComponent<UITooltip>();
            CloneUtil.Strip(go, "side button", Navigation.Mode.Automatic, tip);
            var button = go.GetComponent<Button>();
            if (button == null || !(go.transform is RectTransform rt))
            {
                Object.DestroyImmediate(go);
                UiUtil.Trace("Valheim Compendium button has no Button/RectTransform on its root: no Encyclopedia button.");
                return;
            }
            button.onClick.AddListener(OnClicked);
            button.interactable = true;

            _icon = PickIcon(go);
            if (_icon != null)
            {
                _icon.sprite = BookIcon.Get();
                _icon.overrideSprite = null;
                _icon.preserveAspect = true;
                _icon.enabled = true;
                // Sprite swap on the icon itself would show the vanilla sprites on hover: plain colour tint instead.
                if (button.transition == Selectable.Transition.SpriteSwap && ReferenceEquals(button.targetGraphic, _icon))
                {
                    button.transition = Selectable.Transition.ColorTint;
                    button.colors = ColorBlock.defaultColorBlock;
                }
            }
            RenameLabels(go);
            _tip = SetupTooltip(gui, go, tip);
            _pad = SetupPad(gui, go);

            go.transform.SetParent(parent, false);
            _go = go;
            _rt = rt;
            _button = button;
            UiUtil.Trace($"Encyclopedia button created from '{_anchor.name}' ({_anchorMethod}), icon on "
                      + $"'{(_icon != null ? _icon.name : "none")}', tooltip {(_tip != null)}, pad {(_pad != null ? PadKey : "none")}.");
        }
        finally
        {
            Object.Destroy(holder);
        }
    }

    // Icon = child Image named like "icon", else the largest active child Image with a sprite, else the root Image
    // (button that is only an icon).
    private static Image PickIcon(GameObject go)
    {
        var root = go.GetComponent<Image>();
        Image named = null;
        Image largest = null;
        var largestArea = 0f;
        foreach (var img in go.GetComponentsInChildren<Image>(true))
        {
            if (img == null || img == root || !img.gameObject.activeSelf || img.sprite == null)
            {
                continue;
            }
            if (named == null && img.name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                named = img;
            }
            var r = img.rectTransform.rect;
            var area = Mathf.Abs(r.width * r.height);
            if (area > largestArea)
            {
                largestArea = area;
                largest = img;
            }
        }
        return named != null ? named : largest != null ? largest : root;
    }

    // A text label reading the vanilla dialog name ($inventory_texts, raw or localized) would still say "Valheim
    // Compendium": ours say "Encyclopedia". Other texts (none expected) stay.
    private static void RenameLabels(GameObject go)
    {
        const string token = "$inventory_texts";
        var localized = Localization.instance != null ? Localization.instance.Localize(token) : token;
        foreach (var t in go.GetComponentsInChildren<TMPro.TMP_Text>(true))
        {
            var s = (t.text ?? "").Trim();
            if (s == token || string.Equals(s, localized, StringComparison.OrdinalIgnoreCase))
            {
                t.text = Labels.WindowTitle;
            }
        }
    }

    // Keep the anchor's tooltip look when it has one; else the inventory slot tooltip (topic + text).
    private static UITooltip SetupTooltip(InventoryGui gui, GameObject go, UITooltip tip)
    {
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
                UiUtil.Trace("No tooltip prefab found: the Encyclopedia button has no tooltip.");
                return null;
            }
            tip = go.AddComponent<UITooltip>();
            tip.m_tooltipPrefab = prefab;
        }
        tip.m_gamepadFocusObject = null; // may point at a vanilla object outside the clone
        tip.Set(Labels.ButtonTooltipTopic, Labels.ButtonTooltipText);
        return tip;
    }

    private static GameObject SlotTooltipPrefab(InventoryGui gui)
    {
        var grid = gui.m_playerGrid;
        var element = grid != null && grid.m_elementPrefab != null ? grid.m_elementPrefab.GetComponent<InventoryElement>() : null;
        return element != null && element.m_tooltip != null ? element.m_tooltip.m_tooltipPrefab : null;
    }

    // New pad on View/Select, no hint. Side panel group missing (UI mod) or a vanilla pad already on that key in it:
    // no pad, mouse only.
    private static UIGamePad SetupPad(InventoryGui gui, GameObject go)
    {
        var groups = gui.m_uiGroups;
        if (groups == null || groups.Length <= SidePanelGroup || groups[SidePanelGroup] == null)
        {
            UiUtil.Trace("Inventory has no side panel group: the Encyclopedia button works with the mouse only.");
            return null;
        }
        foreach (var other in groups[SidePanelGroup].GetComponentsInChildren<UIGamePad>(true))
        {
            // Our old button (feature off and on in the same frame) still exists until the frame ends: not a vanilla pad.
            if (other != null && other.m_zinputKey == PadKey && other.gameObject.name != ButtonName)
            {
                Log.Warning($"The game's '{other.name}' already uses the controller key {PadKey} in the side panel: "
                            + "the Encyclopedia button works with the mouse only.");
                return null;
            }
        }
        var pad = go.AddComponent<UIGamePad>();
        pad.m_zinputKey = PadKey;
        pad.m_keyCode = KeyCode.None;
        pad.m_hint = null;
        pad.m_blockingElements = new List<GameObject>();
        return pad;
    }

    // UIGamePad take its group from the nearest parent in Start. Me set the side panel group once Start ran
    // (m_button set there), so IsInteractive follow the side panel focus. PadAllowed is the real gate anyway.
    internal static void FixPadGroup(InventoryGui gui)
    {
        var pad = _pad;
        if (pad == null || pad.m_button == null || gui == null)
        {
            return;
        }
        var groups = gui.m_uiGroups;
        var group = groups != null && groups.Length > SidePanelGroup ? groups[SidePanelGroup] : null;
        if (group == null || ReferenceEquals(pad.m_group, group))
        {
            return;
        }
        UiUtil.Trace($"Encyclopedia button pad group '{(pad.m_group != null ? pad.m_group.name : "none")}' -> side panel group '{group.name}'.");
        pad.m_group = group;
    }

    // ---------------------------------------------------------------- placement

    private static void Place()
    {
        if (_rt == null || _anchor == null)
        {
            return;
        }
        var parent = _rt.parent as RectTransform;
        var anchorRt = _anchor.transform as RectTransform;
        if (parent == null || anchorRt == null)
        {
            return;
        }
        if (_hidden)
        {
            SideRow.Restore();
            return;
        }
        if (parent.GetComponent<LayoutGroup>() != null)
        {
            _placement = Placement.LayoutGroup;
            _rt.SetAsLastSibling();
            _rt.SetSiblingIndex(anchorRt.GetSiblingIndex() + 1);
            return;
        }
        if (SideRow.Apply(parent, anchorRt, _rt, _button))
        {
            _placement = Placement.Row;
            return;
        }
        // Fallback (row gave up or cannot fit): the old way, our button outside the vanilla row.
        _rt.localScale = anchorRt.localScale;
        Measure(parent, anchorRt, out var after, out var before);
        var target = _preferBefore ? before : after;
        _placement = _preferBefore ? Placement.BeforeFirst : Placement.AfterLast;
        // Clone has the anchor's anchors, pivot and size: moving by the centre delta in parent space puts it there.
        var anchorCenter = UiUtil.ParentSpaceRect(anchorRt).center;
        _rt.anchoredPosition = anchorRt.anchoredPosition + (target - anchorCenter);
    }

    // Line of the active controls under the parent (direct children holding a Selectable): axis with the larger
    // spread, step = median gap between neighbours, cross position = median of the line. One control only: to the
    // right of the anchor.
    private static void Measure(RectTransform parent, RectTransform anchorRt, out Vector2 after, out Vector2 before)
    {
        var centers = new List<Vector2>();
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i) as RectTransform;
            if (child == null || child == _rt || !child.gameObject.activeSelf || child.GetComponentInChildren<Selectable>() == null)
            {
                continue;
            }
            centers.Add(UiUtil.ParentSpaceRect(child).center);
        }
        var a = UiUtil.ParentSpaceRect(anchorRt);
        if (centers.Count < 2)
        {
            var step = a.width + FallbackGap;
            after = a.center + new Vector2(step, 0f);
            before = a.center - new Vector2(step, 0f);
            return;
        }
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (var c in centers)
        {
            minX = Mathf.Min(minX, c.x);
            maxX = Mathf.Max(maxX, c.x);
            minY = Mathf.Min(minY, c.y);
            maxY = Mathf.Max(maxY, c.y);
        }
        var horizontal = maxX - minX >= maxY - minY;
        var along = new List<float>();
        var across = new List<float>();
        foreach (var c in centers)
        {
            along.Add(horizontal ? c.x : c.y);
            across.Add(horizontal ? c.y : c.x);
        }
        along.Sort();
        var gaps = new List<float>();
        for (var i = 1; i < along.Count; i++)
        {
            var g = along[i] - along[i - 1];
            if (g >= 1f)
            {
                gaps.Add(g);
            }
        }
        var stepLen = gaps.Count > 0 ? Median(gaps) : (horizontal ? a.width : a.height) + FallbackGap;
        var cross = Median(across);
        if (horizontal)
        {
            // Left to right: after = right of the last.
            after = new Vector2(along[along.Count - 1] + stepLen, cross);
            before = new Vector2(along[0] - stepLen, cross);
        }
        else
        {
            // Top to bottom: after = below the lowest.
            after = new Vector2(cross, along[0] - stepLen);
            before = new Vector2(cross, along[along.Count - 1] + stepLen);
        }
    }

    private static float Median(List<float> values)
    {
        values.Sort();
        var n = values.Count;
        return n % 2 == 1 ? values[n / 2] : (values[n / 2 - 1] + values[n / 2]) * 0.5f;
    }

    // ---------------------------------------------------------------- self-check

    // Once per InventoryGui, after the panel stopped moving: pad group, placement check, layout dump.
    private static IEnumerator SelfCheck(InventoryGui gui)
    {
        yield return null;
        var lastPos = Vector3.zero;
        var lastScale = Vector3.zero;
        var stable = 0;
        for (var i = 0; i < SettleFrames && stable < 2; i++)
        {
            if (!StillOurs(gui))
            {
                yield break;
            }
            if (!InventoryShown(gui))
            {
                _checkStarted = false; // closed before it settled: try again at next Show
                yield break;
            }
            if (_go.activeInHierarchy && !UiUtil.Degenerate(_rt))
            {
                var pos = _rt.position;
                var scale = _rt.lossyScale;
                stable = pos == lastPos && scale == lastScale ? stable + 1 : 0;
                lastPos = pos;
                lastScale = scale;
            }
            yield return null;
        }
        if (!StillOurs(gui))
        {
            yield break;
        }
        if (!_go.activeInHierarchy || UiUtil.Degenerate(_rt) || !InventoryShown(gui))
        {
            _checkStarted = false; // panel never settled: try again at next Show
            yield break;
        }
        Guarded("pad group", () => FixPadGroup(gui));
        Guarded("placement check", () => CheckPlacement(gui));
        Guarded("layout dump", () => DebugLayoutDump.SidePanel(gui));
    }

    private static void Guarded(string site, Action step)
    {
        try
        {
            step();
        }
        catch (Exception e)
        {
            PatchGuard.Report($"{nameof(SideButton)} {site}", e);
        }
    }

    private static void CheckPlacement(InventoryGui gui)
    {
        Place(); // another mod's Show postfix may have moved the row after ours
        Canvas.ForceUpdateCanvases();
        var problem = FindProblem(gui);
        if (problem == null)
        {
            _lastCheck = $"ok ({_placement})";
            UiUtil.Trace($"Encyclopedia button placed {Describe(_placement)} ({UiUtil.Fmt(UiUtil.WorldRect(_rt))}): inside the panel, "
                         + $"no overlap, on screen. Row: {SideRow.State}.");
            return;
        }
        if (_placement == Placement.AfterLast)
        {
            _preferBefore = true;
            Place();
            Canvas.ForceUpdateCanvases();
            var second = FindProblem(gui);
            if (second == null)
            {
                _lastCheck = $"ok ({_placement}, after-last {problem})";
                UiUtil.Trace($"Encyclopedia button placed {Describe(_placement)} ({UiUtil.Fmt(UiUtil.WorldRect(_rt))}): "
                             + $"after the last control it {problem}.");
                return;
            }
            _preferBefore = false;
            Place();
        }
        _lastCheck = $"problem ({_placement}): {problem}";
        Log.Warning($"Encyclopedia button {problem} at {UiUtil.Fmt(UiUtil.WorldRect(_rt))} (screen {Screen.width}x{Screen.height}); "
                    + "another UI mod may have moved the side panel.");
    }

    private static string Describe(Placement p)
    {
        switch (p)
        {
            case Placement.Row:
                return "in the side panel row, right after the Valheim Compendium button (row spaced evenly)";
            case Placement.LayoutGroup:
                return "by the side panel's layout group, right after the Valheim Compendium button";
            case Placement.BeforeFirst:
                return "one step before the first side control";
            case Placement.AfterLast:
                return "one step after the last side control";
            default:
                return "nowhere";
        }
    }

    /// <summary>Side panel background our button must sit in (the row's, else the smallest image holding the row).</summary>
    internal static RectTransform PanelBackground
    {
        get
        {
            if (_rt == null)
            {
                return null;
            }
            if (SideRow.Background != null)
            {
                return SideRow.Background;
            }
            var parent = _rt.parent as RectTransform;
            var bg = SideRow.FindBackground(parent, _rt);
            return bg != null ? bg : parent;
        }
    }

    // Null = fine. Else what is wrong, as words for the log. World rects (screen pixels on the overlay canvas):
    // inside the side panel background, no overlap with another side control or small graphic there, clear of the
    // player / crafting / container panels, on screen.
    internal static string FindProblem(InventoryGui gui)
    {
        if (_rt == null)
        {
            return "is missing";
        }
        var mine = UiUtil.WorldRect(_rt);
        var bg = PanelBackground;
        if (bg != null)
        {
            var b = UiUtil.WorldRect(bg);
            if (!UiUtil.Inside(mine, b, 1f))
            {
                return $"is outside the side panel background '{bg.name}' {UiUtil.Fmt(b)}";
            }
        }
        var myArea = mine.width * mine.height;
        var parent = _rt.parent;
        for (var i = 0; parent != null && i < parent.childCount; i++)
        {
            var child = parent.GetChild(i) as RectTransform;
            if (child == null || child == _rt || child == bg || !child.gameObject.activeInHierarchy)
            {
                continue;
            }
            var control = child.GetComponentInChildren<Selectable>() != null;
            if (!control && child.GetComponent<Graphic>() == null)
            {
                continue;
            }
            var r = UiUtil.WorldRect(child);
            if (!control && r.width * r.height > 4f * myArea)
            {
                continue; // background or shadow of the panel, not a neighbour
            }
            if (UiUtil.Overlaps(mine, r, 1f))
            {
                return $"overlaps '{child.name}' {UiUtil.Fmt(r)}";
            }
        }
        foreach (var (what, panel) in new[] { ("player", gui.m_player), ("crafting", gui.m_crafting), ("container", gui.m_container) })
        {
            if (panel == null || !panel.gameObject.activeInHierarchy || _rt.IsChildOf(panel))
            {
                continue;
            }
            var r = UiUtil.WorldRect(panel);
            if (UiUtil.Overlaps(mine, r, 1f))
            {
                return $"overlaps the {what} panel {UiUtil.Fmt(r)}";
            }
        }
        var canvas = _rt.GetComponentInParent<Canvas>();
        var root = canvas != null ? canvas.rootCanvas : null;
        if (root != null && root.renderMode == RenderMode.ScreenSpaceOverlay
            && !UiUtil.Inside(mine, new Rect(0f, 0f, Screen.width, Screen.height), 1f))
        {
            return "is off screen";
        }
        return null;
    }
}
