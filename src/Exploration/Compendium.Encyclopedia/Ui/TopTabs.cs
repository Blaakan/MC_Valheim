using System;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MC.Exploration.CompendiumEncyclopediaMod;

// Me = the two top-level tabs "Texts" | "Encyclopedia" (design 3.6 "Nested tabs"). Same two tabs, same rects, in two
// places:
//   - the game's Valheim Compendium dialog (m_textsDialog, opened by the raven button): a strip "MC_Compendium_TopTabs"
//     added as its last child while it is shown (TextsDialog.Setup postfix), destroyed when it hides (controller's
//     OnDisable) or when the feature goes off. Me never move or change anything of the vanilla dialog; closed, it is
//     exactly vanilla (other mods that clone it never copy our strip);
//   - our window (a clone of that dialog): the same strip, made in its one-time layout.
// Place: on the title line, left of the title, from the panes' left edge (the title line's left part is free in both:
// our window's counter sits on its right part, our category tabs under it). Look: clones of the crafting panel's
// Craft tab (m_tabCraft), current tab = not interactable (vanilla look).
// Switch: Encyclopedia = vanilla dialog closed the vanilla way (OnClose), our window opened in the same frame, same
// place and size (it is a clone of that dialog), so it looks like a tab change. Texts = our window closed, then
// InventoryGui.OnOpenTexts (the vanilla way: side panel group, list filled fresh).
// Controller: LT = Texts, RT = Encyclopedia in both (like the crafting panel's LT = Craft, RT = Upgrade). A direct pick,
// not a toggle: read twice in one frame (both dialogs' Update in the switch frame) it change nothing.
// Session memory: me remember last tab picked here, until a new InventoryGui (logout). Raven button (OnOpenTexts) reopen it:
// last pick Encyclopedia = the vanilla dialog opens, then our window at once (same frame).
internal static class TopTabs
{
    internal const string StripName = "MC_Compendium_TopTabs";
    internal const string LeftKey = "JoyLTrigger";
    internal const string RightKey = "JoyRTrigger";

    private const float Gap = 4f;
    private const float MaxTabWidth = 150f;
    private const float MinTabWidth = 60f;
    private static readonly int VisibleParam = Animator.StringToHash("visible");

    internal enum Choice
    {
        Texts,
        Encyclopedia,
    }

    // Session (per InventoryGui).
    private static InventoryGui _sessionGui;
    private static Choice _last = Choice.Texts;
    private static bool _refused;
    private static bool _stripFailed;
    private static Button _raven;

    // Strip on the vanilla dialog (only while it is shown).
    private static GameObject _strip;
    private static TopTabsController _controller;
    private static TextsDialog _stripDialog;
    private static UIGroupHandler _stripGroup;
    private static Button _texts;
    private static Button _encyclopedia;
    private static bool _dumped;

    internal static Choice Last => _last;
    internal static GameObject VanillaStrip => _strip;
    internal static Button VanillaTexts => _texts;
    internal static Button VanillaEncyclopedia => _encyclopedia;
    internal static bool Refused => _refused;

#if DEBUG
    /// <summary>Self tests put the remembered tab back after them (never saved anywhere).</summary>
    internal static void SetLastForTest(Choice c) => _last = c;

    /// <summary>Self tests play a refused window on this InventoryGui (strip gone), then put the flag back.</summary>
    internal static void SetRefusedForTest(bool refused)
    {
        _refused = refused;
        if (refused)
        {
            DestroyStrip(hideNow: true);
        }
    }
#endif

    // ---------------------------------------------------------------- session

    private static void Session(InventoryGui gui)
    {
        if (ReferenceEquals(_sessionGui, gui))
        {
            return;
        }
        // New InventoryGui (logout, other world): old strip died with the old dialog. Fresh start, Texts first.
        _sessionGui = gui;
        _last = Choice.Texts;
        _refused = false;
        _stripFailed = false;
        _raven = null;
        _dumped = false;
        ForgetStrip();
    }

    internal static bool InventoryShown(InventoryGui gui) =>
        gui != null && gui.m_animator != null && gui.m_animator.GetBool(VisibleParam);

    internal static bool VanillaShown(InventoryGui gui) =>
        gui != null && gui.m_textsDialog != null && gui.m_textsDialog.gameObject.activeSelf;

    /// <summary>The game's Valheim Compendium button (raven), found by its prefab-saved OnOpenTexts call. Cached.</summary>
    internal static Button RavenButton(InventoryGui gui)
    {
        if (gui == null)
        {
            return null;
        }
        Session(gui);
        if (_raven != null)
        {
            return _raven;
        }
        foreach (var b in gui.GetComponentsInChildren<Button>(true))
        {
            if (b == null || b.name == SideButton.ButtonName)
            {
                continue;
            }
            var ev = b.onClick;
            for (var i = 0; i < ev.GetPersistentEventCount(); i++)
            {
                if (ev.GetPersistentMethodName(i) == "OnOpenTexts")
                {
                    _raven = b;
                    return b;
                }
            }
        }
        return null;
    }

    // ---------------------------------------------------------------- vanilla dialog

    /// <summary>TextsDialog.Setup postfix (the dialog was just shown or refilled): strip on it, current tab = Texts.</summary>
    internal static void OnVanillaShown(TextsDialog dialog)
    {
        var gui = InventoryGui.instance;
        if (gui == null || dialog == null || !ReferenceEquals(dialog, gui.m_textsDialog) || !dialog.gameObject.activeSelf)
        {
            return;
        }
        Session(gui);
        if (_refused || _stripFailed)
        {
            return;
        }
        if (_strip != null && ReferenceEquals(_stripDialog, dialog))
        {
            SetCurrent(_texts, _encyclopedia, Choice.Texts);
            return;
        }
        ForgetStrip();
        try
        {
            if (!BuildVanillaStrip(gui, dialog))
            {
                _stripFailed = true;
            }
        }
        catch (Exception e)
        {
            _stripFailed = true;
            PatchGuard.Report("Encyclopedia tabs on the Valheim Compendium", e);
        }
        if (_stripFailed)
        {
            DestroyStrip(hideNow: true);
        }
    }

    /// <summary>
    /// InventoryGui.OnOpenTexts postfix (raven button, or our Texts tab): last pick Encyclopedia = go on to our window
    /// at once. Our own Texts tab set Texts before calling it, so it never bounces back.
    /// </summary>
    internal static void OnRavenOpened()
    {
        var gui = InventoryGui.instance;
        if (gui == null || !VanillaShown(gui) || CompendiumWindow.IsOpen)
        {
            return;
        }
        Session(gui);
        if (_last != Choice.Encyclopedia || _refused || _stripFailed || _strip == null)
        {
            return;
        }
        OpenEncyclopedia(gui);
    }

    /// <summary>Feature on while the vanilla dialog is shown: the tabs appear at once.</summary>
    internal static void OnActivated(InventoryGui gui)
    {
        if (VanillaShown(gui))
        {
            OnVanillaShown(gui.m_textsDialog);
        }
    }

    /// <summary>Feature off: strip gone at once, the vanilla dialog exactly as the game made it. Session memory kept.</summary>
    internal static void Destroy()
    {
        DestroyStrip(hideNow: true);
    }

    /// <summary>Our window cannot be built on this InventoryGui: no Encyclopedia tab until the next one.</summary>
    internal static void HideForThisSession()
    {
        _refused = true;
        _last = Choice.Texts;
        DestroyStrip(hideNow: true);
    }

    private static bool BuildVanillaStrip(InventoryGui gui, TextsDialog dialog)
    {
        var root = dialog.transform as RectTransform;
        var left = DialogParts.ListScroll(dialog.m_listRoot);
        var right = DialogParts.TextScroll(dialog.m_textArea);
        if (root == null || left == null || right == null || ReferenceEquals(left, right))
        {
            Log.Warning("The Valheim Compendium dialog's list or text area was not found (UI mod?): no Encyclopedia tab on it.");
            return false;
        }
        var leftSb = left.verticalScrollbar != null ? left.verticalScrollbar : dialog.m_leftScrollbar;
        var rightSb = right.verticalScrollbar != null ? right.verticalScrollbar : dialog.m_rightScrollbar;
        var area = DialogParts.PaneArea(root, left, right, leftSb, rightSb, out _, out _);
        var closes = DialogParts.FindPersistent(dialog.gameObject, "OnClose");
        var title = DialogParts.FindTitle(dialog.gameObject, dialog.m_textAreaTopic, dialog.m_textArea, left, right, closes);
        var titleRect = title != null ? UiUtil.RectInAncestor(title.rectTransform, root) : default;
        var src = TabSource(gui, out var tabH);
        if (src == null || !TryPlace(area, titleRect, tabH, out var textsRect, out var encyRect))
        {
            Log.Warning(src == null
                ? "The crafting panel's tab was not found (UI mod?): no Encyclopedia tab on the Valheim Compendium."
                : "No room for the Encyclopedia tabs left of the Valheim Compendium title (UI mod?): no Encyclopedia tab on it.");
            return false;
        }
        var strip = MakeStrip(root, src.gameObject, textsRect, encyRect, out var texts, out var ency);
        if (strip == null)
        {
            return false;
        }
        _controller = strip.AddComponent<TopTabsController>();
        _strip = strip;
        _stripDialog = dialog;
        _stripGroup = dialog.GetComponent<UIGroupHandler>();
        _texts = texts;
        _encyclopedia = ency;
        SetCurrent(texts, ency, Choice.Texts);
        strip.transform.SetAsLastSibling();
        strip.SetActive(true);
        if (!_dumped)
        {
            _dumped = true;
            UiUtil.Trace($"Encyclopedia tabs on the Valheim Compendium: texts {UiUtil.Fmt(textsRect)}, encyclopedia {UiUtil.Fmt(encyRect)}, title "
                         + $"{(title != null ? "'" + title.name + "' " + UiUtil.Fmt(titleRect) : "not found")}, panes {UiUtil.Fmt(area)} (dialog units).");
            DebugLayoutDump.VanillaTopTabs(gui, dialog);
        }
        return true;
    }

    private static void ForgetStrip()
    {
        _strip = null;
        _controller = null;
        _stripDialog = null;
        _stripGroup = null;
        _texts = null;
        _encyclopedia = null;
    }

    // hideNow false = from the controller's OnDisable (the dialog is being deactivated: no SetActive then).
    private static void DestroyStrip(bool hideNow)
    {
        var s = _strip;
        ForgetStrip();
        if (s == null)
        {
            return;
        }
        if (hideNow)
        {
            s.SetActive(false);
        }
        Object.Destroy(s);
    }

    /// <summary>Controller's OnDisable: the vanilla dialog hid (Esc, B, close, our Encyclopedia tab, Hide).</summary>
    internal static void OnVanillaHidden(TopTabsController controller)
    {
        if (controller == null || !ReferenceEquals(controller, _controller))
        {
            return;
        }
        DestroyStrip(hideNow: false);
    }

    /// <summary>Controller's Update (vanilla dialog shown): LT / RT.</summary>
    internal static void VanillaTick(TopTabsController controller)
    {
        if (!ReferenceEquals(controller, _controller) || !ZInput.IsExclusiveGamepadActive() || FocusGuard.Active)
        {
            return;
        }
        if (_stripGroup != null && !_stripGroup.IsActive)
        {
            return; // something above the dialog has the controller
        }
        if (ZInput.GetButtonDown(RightKey))
        {
            Select(Choice.Encyclopedia);
        }
        else if (ZInput.GetButtonDown(LeftKey))
        {
            Select(Choice.Texts);
        }
    }

    // ---------------------------------------------------------------- switching

    private static void OnClicked(Choice c)
    {
        try
        {
            Select(c);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Encyclopedia top tab click", e);
        }
    }

    /// <summary>
    /// Me show the vanilla texts (Texts) or our window (Encyclopedia). Tab clicks and LT / RT run me (self tests call
    /// me too: a trigger press cannot be faked). Encyclopedia already shown = me do nothing, memory too (window maybe
    /// opened by the side button, and that one never change the memory). Texts already shown = me only remember it.
    /// </summary>
    internal static void Select(Choice c)
    {
        var gui = InventoryGui.instance;
        if (gui == null || !InventoryShown(gui))
        {
            return;
        }
        Session(gui);
        if (c == Choice.Encyclopedia)
        {
            if (CompendiumWindow.IsOpen)
            {
                // Switch from vanilla dialog already wrote Encyclopedia (below), so second read same frame change nothing.
                return;
            }
            if (!VanillaShown(gui) || _refused)
            {
                return;
            }
            _last = Choice.Encyclopedia;
            OpenEncyclopedia(gui);
            return;
        }
        if (CompendiumWindow.IsOpen)
        {
            _last = Choice.Texts;
            CompendiumWindow.Close(selectButton: false);
            gui.OnOpenTexts();
            return;
        }
        if (VanillaShown(gui))
        {
            _last = Choice.Texts;
        }
    }

    // Our window's Open close the vanilla dialog itself (OnClose, the vanilla way). Refused (UI mod?): me go back to the
    // vanilla dialog, Texts remembered.
    private static void OpenEncyclopedia(InventoryGui gui)
    {
        if (CompendiumWindow.Open())
        {
            return;
        }
        _last = Choice.Texts;
        if (!VanillaShown(gui) && InventoryShown(gui))
        {
            gui.OnOpenTexts();
        }
    }

    // ---------------------------------------------------------------- building (shared with the window)

    /// <summary>Crafting panel's Craft tab (fallback Upgrade tab) and its height, clamped 24-40 units.</summary>
    internal static Button TabSource(InventoryGui gui, out float tabH)
    {
        var src = gui != null ? (gui.m_tabCraft != null ? gui.m_tabCraft : gui.m_tabUpgrade) : null;
        tabH = 30f;
        if (src != null && src.transform is RectTransform rt)
        {
            tabH = Mathf.Clamp(rt.rect.height, 24f, 40f);
        }
        return src;
    }

    /// <summary>
    /// Two tab rects on the title line, left of the title, from the panes' left edge (dialog units). Never lower than
    /// 2 units above the panes. False = no room (under 60 units each).
    /// </summary>
    internal static bool TryPlace(Rect area, Rect title, float tabH, out Rect texts, out Rect encyclopedia)
    {
        texts = default;
        encyclopedia = default;
        var hasTitle = title.width > 1f && title.height > 1f;
        var right = (hasTitle ? title.xMin : area.center.x - 170f) - 2f * Gap;
        var w = Mathf.Min(MaxTabWidth, (right - area.xMin - Gap) * 0.5f);
        if (w < MinTabWidth)
        {
            return false;
        }
        var cy = hasTitle ? title.center.y : area.yMax + Gap + tabH * 0.5f;
        var yMin = Mathf.Max(cy - tabH * 0.5f, area.yMax + 2f);
        texts = new Rect(area.xMin, yMin, w, tabH);
        encyclopedia = new Rect(area.xMin + w + Gap, yMin, w, tabH);
        return true;
    }

    /// <summary>
    /// Strip (inactive, full-size child of <paramref name="root"/>, last sibling) with the two tabs, cloned from
    /// <paramref name="template"/> while the strip is inactive (stripped before anything of them wakes up). Caller
    /// activates it. Null = the template has no Button.
    /// </summary>
    internal static GameObject MakeStrip(RectTransform root, GameObject template, Rect textsRect, Rect encyRect, out Button texts, out Button encyclopedia)
    {
        texts = null;
        encyclopedia = null;
        var go = new GameObject(StripName, typeof(RectTransform));
        go.SetActive(false);
        go.transform.SetParent(root, false);
        var rt = (RectTransform)go.transform;
        UiUtil.Fill(rt);
        texts = MakeTab(template, rt, "MC_Compendium_TopTab_Texts", Labels.TabTexts, textsRect, () => OnClicked(Choice.Texts));
        encyclopedia = MakeTab(template, rt, "MC_Compendium_TopTab_Encyclopedia", Labels.TabEncyclopedia, encyRect,
            () => OnClicked(Choice.Encyclopedia));
        if (texts == null || encyclopedia == null)
        {
            Object.Destroy(go);
            texts = null;
            encyclopedia = null;
            return null;
        }
        go.transform.SetAsLastSibling();
        return go;
    }

    private static Button MakeTab(GameObject template, RectTransform strip, string name, string text, Rect rect, UnityEngine.Events.UnityAction onClick)
    {
        var go = Object.Instantiate(template, strip, false);
        go.name = name;
        CloneUtil.Strip(go, "top tab", Navigation.Mode.None);
        var b = go.GetComponent<Button>();
        if (b == null)
        {
            Object.DestroyImmediate(go);
            return null;
        }
        b.onClick.AddListener(onClick);
        var icon = go.transform.Find("icon");
        if (icon != null)
        {
            icon.gameObject.SetActive(false); // row template fallback (window only)
        }
        var label = FirstShownText(go.transform);
        if (label != null)
        {
            var size = label.enableAutoSizing ? label.fontSizeMax : label.fontSize;
            label.enableAutoSizing = true;
            label.fontSizeMax = size;
            label.fontSizeMin = Mathf.Min(10f, size);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.text = Localization.instance != null ? Localization.instance.Localize(text) : text;
        }
        else
        {
            var legacy = go.GetComponentInChildren<Text>(true);
            if (legacy != null)
            {
                legacy.text = text;
            }
        }
        go.SetActive(true);
        UiUtil.PlaceIn((RectTransform)go.transform, strip, rect);
        return b;
    }

    // First text whose objects are switched on up to the clone root (the clone sits under an inactive parent, so
    // activeInHierarchy says nothing here). Vanilla Craft tab: "Text", not the hidden "Selected/Text (1)".
    private static TMP_Text FirstShownText(Transform root)
    {
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
        {
            var shown = true;
            for (var x = t.transform; x != null && x != root; x = x.parent)
            {
                if (!x.gameObject.activeSelf)
                {
                    shown = false;
                    break;
                }
            }
            if (shown)
            {
                return t;
            }
        }
        return null;
    }

    /// <summary>Current tab = not interactable (vanilla tab look), the other one clickable.</summary>
    internal static void SetCurrent(Button texts, Button encyclopedia, Choice current)
    {
        if (texts != null)
        {
            texts.interactable = current != Choice.Texts;
        }
        if (encyclopedia != null)
        {
            encyclopedia.interactable = current != Choice.Encyclopedia;
        }
    }
}

// Me live on the strip in the vanilla dialog: run only while that dialog is shown. LT / RT there, and the strip goes
// away when the dialog hides. Every call guarded (Unity calls these directly).
internal sealed class TopTabsController : MonoBehaviour
{
    private void Update()
    {
        try
        {
            TopTabs.VanillaTick(this);
        }
        catch (Exception e)
        {
            PatchGuard.Report($"{nameof(TopTabsController)}.{nameof(Update)}", e);
        }
    }

    private void OnDisable()
    {
        try
        {
            TopTabs.OnVanillaHidden(this);
        }
        catch (Exception e)
        {
            PatchGuard.Report($"{nameof(TopTabsController)}.{nameof(OnDisable)}", e);
        }
    }
}
