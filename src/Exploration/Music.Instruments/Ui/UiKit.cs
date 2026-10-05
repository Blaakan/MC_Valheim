using System;
using System.Collections.Generic;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace MC.Exploration.MusicInstrumentsMod;

// Me = small UI kit for our own canvases (song window, mini-game HUD). Look borrowed from live vanilla parts found at
// build time (fonts, wood frame, craft button, scroll bar); every part has a plain fallback (UI mod removed it, prefab
// names changed). Layout in 1920x1080 reference units: CanvasScaler + vanilla GuiScaler (game GUI scale slider).
internal static class UiKit
{
    internal static readonly Color Gold = new Color(1f, 0.8f, 0.38f, 1f);
    internal static readonly Color HeaderOrange = new Color(1f, 0.647f, 0f, 1f);  // TMP "orange", like vanilla headers
    internal static readonly Color TextLight = new Color(0.93f, 0.9f, 0.84f, 1f);
    internal static readonly Color TextDim = new Color(0.7f, 0.67f, 0.61f, 1f);
    internal static readonly Color TextGrey = new Color(0.55f, 0.53f, 0.5f, 1f);
    internal static readonly Color ErrorRed = new Color(1f, 0.5f, 0.4f, 1f);
    internal static readonly Color FrameFallback = new Color(0.25f, 0.17f, 0.1f, 0.97f);
    internal static readonly Color PaneFallback = new Color(0f, 0f, 0f, 0.45f);

    private static readonly List<string> StripNotes = new List<string>();

    // Dedicated server: no graphics device, no UI at all.
    internal static bool Headless => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;

    // ---------------------------------------------------------------- canvas

    // Root overlay canvas, outside scenes, inactive (caller fill it, then show it). Game GUI scale from GuiScaler (its
    // Awake take the CanvasScaler, so scaler first; both wake when the root goes active).
    internal static GameObject MakeRootCanvas(string name, int order, out Canvas canvas)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(go);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        canvas.pixelPerfect = false;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        // First frame before GuiScaler.Update: screen part of its formula (GUI scale slider comes next frame).
        scaler.scaleFactor = Mathf.Max(0.1f, Mathf.Min(Screen.width / 1920f, Screen.height / 1080f));
        go.AddComponent<GuiScaler>();
        return go;
    }

    // Me take wanted order, but go above game render-scale picture (FrameBufferScaler canvas) when it sit higher:
    // under it, nobody see us. Order is prefab data: me read it live (copy of Spyglass ScopeOverlay).
    internal static int OrderAboveFrameBuffer(int wanted)
    {
        try
        {
            var fbs = UnityEngine.Object.FindAnyObjectByType<FrameBufferScaler>(FindObjectsInactive.Include);
            var canvas = fbs != null ? fbs.GetComponentInParent<Canvas>(true) : null;
            var root = canvas != null ? canvas.rootCanvas : null;
            if (root != null && wanted <= root.sortingOrder)
            {
                return root.sortingOrder + 1;
            }
        }
        catch (Exception e)
        {
            Log.Debug("Music UI: render-scale canvas not read: " + e.Message);
        }
        return wanted;
    }

    // ---------------------------------------------------------------- rects

    internal static RectTransform Rect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.localScale = Vector3.one;
        return rt;
    }

    // Box from the parent's bottom-left corner: x, y = bottom-left of the box.
    internal static void At(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
    }

    // Box from the parent's top-left corner: top = distance from the parent top to the box top.
    internal static void FromTop(RectTransform rt, float x, float top, float w, float h)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -top);
        rt.sizeDelta = new Vector2(w, h);
    }

    internal static void Fill(RectTransform rt, float inset = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    internal static void SetActive(GameObject go, bool on)
    {
        if (go != null && go.activeSelf != on)
        {
            go.SetActive(on);
        }
    }

    // ---------------------------------------------------------------- images

    internal static Image MakeImage(Transform parent, string name, Color color, bool raycast = false)
    {
        var rt = Rect(parent, name);
        var image = rt.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = raycast;
        return image;
    }

    // Copy the look (sprite, slicing, tint) of a vanilla image. False = nothing usable there.
    internal static bool CopyLook(Image to, Image from)
    {
        if (to == null || from == null || from.sprite == null)
        {
            return false;
        }
        to.sprite = from.sprite;
        to.type = from.type;
        to.fillCenter = from.fillCenter;
        to.pixelsPerUnitMultiplier = from.pixelsPerUnitMultiplier;
        to.preserveAspect = false;
        to.color = from.color;
        return true;
    }

    internal static Image FindImage(Transform root, string path)
    {
        if (root == null)
        {
            return null;
        }
        var t = root.Find(path);
        return t != null ? t.GetComponent<Image>() : null;
    }

    // First image under root whose sprite name hold part (ignoring case).
    internal static Image FindImageBySprite(Transform root, string part)
    {
        if (root == null)
        {
            return null;
        }
        foreach (var image in root.GetComponentsInChildren<Image>(true))
        {
            if (image != null && image.sprite != null
                && image.sprite.name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return image;
            }
        }
        return null;
    }

    // Vanilla dialog wood frame: Texts dialog Texts_frame/bkg (1.0.16 dump; unverified names), else any woodpanel
    // sprite in that dialog. Null = none found.
    internal static Image WoodFrameSource()
    {
        var gui = InventoryGui.instance;
        var dialog = gui != null && gui.m_textsDialog != null ? gui.m_textsDialog.transform : null;
        var image = FindImage(dialog, "Texts_frame/bkg");
        if (image != null && image.sprite != null)
        {
            return image;
        }
        return FindImageBySprite(dialog, "woodpanel");
    }

    // Vanilla dark pane behind lists: Texts dialog Texts_frame/TextList (item_background), else none.
    internal static Image PaneSource()
    {
        var gui = InventoryGui.instance;
        var dialog = gui != null && gui.m_textsDialog != null ? gui.m_textsDialog.transform : null;
        var image = FindImage(dialog, "Texts_frame/TextList");
        return image != null && image.sprite != null ? image : null;
    }

    // ---------------------------------------------------------------- texts

    // Body text of dialogs: crafting description, else HUD hover name.
    internal static TMP_Text BodyStyle()
    {
        var gui = InventoryGui.instance;
        if (gui != null && gui.m_recipeDecription != null && gui.m_recipeDecription.font != null)
        {
            return gui.m_recipeDecription;
        }
        return HudStyle();
    }

    // Dialog title (Norsebold, orange in vanilla): Texts dialog Texts_frame/topic (unverified name), else its topic
    // text, else body.
    internal static TMP_Text TitleStyle()
    {
        var gui = InventoryGui.instance;
        var dialog = gui != null ? gui.m_textsDialog : null;
        if (dialog != null)
        {
            var t = dialog.transform.Find("Texts_frame/topic");
            var text = t != null ? t.GetComponent<TMP_Text>() : null;
            if (text != null && text.font != null)
            {
                return text;
            }
            if (dialog.m_textAreaTopic != null && dialog.m_textAreaTopic.font != null)
            {
                return dialog.m_textAreaTopic;
            }
        }
        return BodyStyle();
    }

    // Text over the world (HUD hover name: outlined).
    internal static TMP_Text HudStyle()
    {
        var hud = Hud.instance;
        if (hud != null && hud.m_hoverName != null && hud.m_hoverName.font != null)
        {
            return hud.m_hoverName;
        }
        var gui = InventoryGui.instance;
        return gui != null && gui.m_recipeName != null && gui.m_recipeName.font != null ? gui.m_recipeName : null;
    }

    // Big centre message text (MessageHud), else HUD style.
    internal static TMP_Text BigStyle()
    {
        var msg = MessageHud.instance;
        if (msg != null && msg.m_messageCenterText != null && msg.m_messageCenterText.font != null)
        {
            return msg.m_messageCenterText;
        }
        return HudStyle();
    }

    // Text in the look of a vanilla text (font, material with its outline, spacing): clone of that live text, cut down
    // to the text alone. Parent must be inactive (our roots are while built): nothing of the clone wakes before the
    // strip. Clone not possible: new TMP text with that font.
    // Box must be taller than one line of the font: TMP Ellipsis draw NOTHING when the first line no fit in height.
    internal static TextMeshProUGUI MakeText(Transform parent, string name, TMP_Text style, float size, Color color,
        TextAlignmentOptions align, bool wrap = false)
    {
        var t = CloneText(parent, style);
        if (t == null)
        {
            t = NewText(parent, style);
        }
        // Source may be switched off: InventoryGui.UpdateRecipe turn off recipe description with no recipe picked,
        // clone copy that flag, text never draw.
        t.enabled = true;
        t.gameObject.name = name;
        t.fontSize = size;
        t.enableAutoSizing = false;
        t.enableVertexGradient = false;
        t.color = color;
        t.alignment = align;
        t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.richText = false;
        t.raycastTarget = false;
        // Clone may carry a reveal effect or paging of its source: show everything, from the start.
        t.margin = Vector4.zero;
        t.maxVisibleCharacters = 99999;
        t.maxVisibleWords = 99999;
        t.maxVisibleLines = 99999;
        t.firstVisibleCharacter = 0;
        t.pageToDisplay = 1;
        t.isRightToLeftText = false;
        t.text = "";
        if (!t.gameObject.activeSelf)
        {
            t.gameObject.SetActive(true);
        }
        return t;
    }

    private static TextMeshProUGUI CloneText(Transform parent, TMP_Text style)
    {
        if (!(style is TextMeshProUGUI) || style.font == null || parent == null || parent.gameObject.activeInHierarchy)
        {
            return null;
        }
        GameObject go = null;
        try
        {
            go = UnityEngine.Object.Instantiate(style.gameObject, parent, false);
            for (var i = go.transform.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
            }
            var parts = go.GetComponents<Component>();
            for (var i = parts.Length - 1; i >= 0; i--)
            {
                var c = parts[i];
                if (c == null || c is Transform || c is CanvasRenderer || c is TextMeshProUGUI)
                {
                    continue;
                }
                UnityEngine.Object.DestroyImmediate(c);
            }
            var t = go.GetComponent<TextMeshProUGUI>();
            if (t == null)
            {
                UnityEngine.Object.DestroyImmediate(go);
                return null;
            }
            var rt = t.rectTransform;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            t.canvasRenderer.SetAlpha(1f); // source may be mid fade (centre messages)
            return t;
        }
        catch (Exception e)
        {
            Log.Debug("Music UI: text clone failed, new text used: " + e.Message);
            if (go != null)
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
            return null;
        }
    }

    // Me make new TMP text with that font, object off first: TMP Awake run only after font and size set (no default
    // font look-up, TMP settings defaults no stomp ours).
    private static TextMeshProUGUI NewText(Transform parent, TMP_Text style)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.SetActive(false);
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        ApplyFont(t, style);
        t.fontSize = 20f;
        t.fontStyle = FontStyles.Normal;
        return t;
    }

    internal static void ApplyFont(TMP_Text t, TMP_Text style)
    {
        if (style != null && style.font != null)
        {
            t.font = style.font;
            if (style.fontSharedMaterial != null)
            {
                t.fontSharedMaterial = style.fontSharedMaterial;
            }
            return;
        }
        try
        {
            var fallback = TMP_Settings.defaultFontAsset;
            if (fallback != null)
            {
                t.font = fallback;
            }
        }
        catch (Exception)
        {
            // No TMP settings asset: TMP pick what it can.
        }
    }

    // Text only when it changed (TMP rebuild its mesh on every set).
    internal static void SetText(TMP_Text t, string value)
    {
        if (t != null && !string.Equals(t.text, value, StringComparison.Ordinal))
        {
            t.text = value;
        }
    }

    internal static void SetColor(Graphic g, Color c)
    {
        if (g != null && g.color != c)
        {
            g.color = c;
        }
    }

    // ---------------------------------------------------------------- buttons

    // Clone of a vanilla button (look, sounds, colour transitions) with a new label and click. Source null or not a
    // button: plain button of our own. label = its text.
    internal static Button MakeButton(Transform parent, string name, Button source, string text, Action onClick,
        out TMP_Text label)
    {
        Button button = null;
        label = null;
        if (source != null)
        {
            try
            {
                var go = UnityEngine.Object.Instantiate(source.gameObject, parent, false);
                go.name = name;
                Strip(go, "button");
                button = go.GetComponent<Button>();
                label = PickLabel(go);
                if (button == null || label == null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                    button = null;
                    label = null;
                }
            }
            catch (Exception e)
            {
                Log.Debug("Music UI: button clone failed, plain button used: " + e.Message);
                button = null;
                label = null;
            }
        }
        if (button == null)
        {
            var bg = MakeImage(parent, name, new Color(0.36f, 0.26f, 0.16f, 0.95f), raycast: true);
            button = bg.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.15f, 1.1f, 1f, 1f);
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.6f);
            button.colors = colors;
            var t = MakeText(bg.transform, "Text", BodyStyle(), 20f, TextLight, TextAlignmentOptions.Center);
            Fill(t.rectTransform, 4f);
            label = t;
        }
        var go2 = button.gameObject;
        if (!go2.activeSelf)
        {
            go2.SetActive(true); // source may sit hidden (craft panel closed)
        }
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(() => onClick());
        button.interactable = true;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        var rt = (RectTransform)go2.transform;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        var size = label.enableAutoSizing ? label.fontSizeMax : label.fontSize;
        size = Mathf.Clamp(size, 14f, 24f);
        label.enableAutoSizing = true;
        label.fontSizeMax = size;
        label.fontSizeMin = Mathf.Min(11f, size);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.richText = false;
        label.raycastTarget = false;
        label.enabled = true; // source label may be switched off
        label.text = text;
        if (button.targetGraphic != null)
        {
            button.targetGraphic.enabled = true;
        }
        return button;
    }

    // Button label = first shown text with words in it (a leftover glyph text stay out), else first text at all.
    private static TMP_Text PickLabel(GameObject go)
    {
        TMP_Text first = null;
        foreach (var t in go.GetComponentsInChildren<TMP_Text>(true))
        {
            if (t == null)
            {
                continue;
            }
            if (first == null)
            {
                first = t;
            }
            if (t.gameObject.activeSelf && !string.IsNullOrEmpty(t.text))
            {
                return t;
            }
        }
        return first;
    }

    // Plain vertical scroll bar (fallback when the vanilla recipe list bar is missing).
    internal static Scrollbar MakeScrollbar(Transform parent, string name)
    {
        var bg = MakeImage(parent, name, new Color(0f, 0f, 0f, 0.35f), raycast: true);
        var area = Rect(bg.transform, "Sliding Area");
        Fill(area, 1f);
        var handle = MakeImage(area, "Handle", new Color(0.72f, 0.6f, 0.42f, 0.9f), raycast: true);
        Fill(handle.rectTransform);
        var sb = bg.gameObject.AddComponent<Scrollbar>();
        sb.handleRect = handle.rectTransform;
        sb.targetGraphic = handle;
        sb.direction = Scrollbar.Direction.BottomToTop;
        sb.navigation = new Navigation { mode = Navigation.Mode.None };
        return sb;
    }

    // Clone of a vanilla scroll bar (stripped), else plain.
    internal static Scrollbar CloneScrollbar(Transform parent, string name, Scrollbar source)
    {
        if (source != null)
        {
            try
            {
                var go = UnityEngine.Object.Instantiate(source.gameObject, parent, false);
                go.name = name;
                Strip(go, "scroll bar");
                var sb = go.GetComponent<Scrollbar>();
                if (sb != null && sb.handleRect != null)
                {
                    sb.onValueChanged = new Scrollbar.ScrollEvent();
                    sb.direction = Scrollbar.Direction.BottomToTop;
                    sb.interactable = true;
                    if (!go.activeSelf)
                    {
                        go.SetActive(true);
                    }
                    return sb;
                }
                UnityEngine.Object.DestroyImmediate(go);
            }
            catch (Exception e)
            {
                Log.Debug("Music UI: scroll bar clone failed, plain bar used: " + e.Message);
            }
        }
        return MakeScrollbar(parent, name);
    }

    // ---------------------------------------------------------------- clone strip

    // Copy of Compendium's CloneUtil.Strip core. Clone must sit under an inactive parent (nothing woke yet). Me
    // remove: UIGamePad (+ its hint object), UIInputHint (+ hint objects), UITooltip, Localize (would translate our
    // labels back), components of other mods; every Button gets a new empty onClick; navigation off.
    internal static void Strip(GameObject clone, string kind)
    {
        if (clone == null)
        {
            return;
        }
        StripNotes.Clear();
        foreach (var pad in clone.GetComponentsInChildren<UIGamePad>(true))
        {
            if (pad == null)
            {
                continue;
            }
            DropInside(pad.m_hint, clone);
            Kill(pad);
        }
        foreach (var hint in clone.GetComponentsInChildren<UIInputHint>(true))
        {
            if (hint == null)
            {
                continue;
            }
            DropInside(hint.m_gamepadHint, clone);
            DropInside(hint.m_mouseKeyboardHint, clone);
            DropInside(hint.m_gamepadMouseHint, clone);
            if (hint.m_inputLayoutSettings != null)
            {
                foreach (var s in hint.m_inputLayoutSettings)
                {
                    if (s != null)
                    {
                        DropInside(s.m_hintObject, clone);
                    }
                }
            }
            Kill(hint);
        }
        foreach (var tip in clone.GetComponentsInChildren<UITooltip>(true))
        {
            if (tip != null)
            {
                Kill(tip);
            }
        }
        foreach (var loc in clone.GetComponentsInChildren<Localize>(true))
        {
            if (loc != null)
            {
                Kill(loc);
            }
        }
        var all = clone.GetComponentsInChildren<Component>(true);
        for (var i = all.Length - 1; i >= 0; i--)
        {
            var c = all[i];
            if (c != null && !IsVanilla(c.GetType()))
            {
                Kill(c);
            }
        }
        foreach (var b in clone.GetComponentsInChildren<Button>(true))
        {
            if (b != null)
            {
                b.onClick = new Button.ButtonClickedEvent();
            }
        }
        foreach (var s in clone.GetComponentsInChildren<Selectable>(true))
        {
            if (s != null)
            {
                s.navigation = new Navigation { mode = Navigation.Mode.None };
            }
        }
        if (StripNotes.Count > 0)
        {
            Log.Debug("Music UI clone (" + kind + ") removed: " + string.Join(", ", StripNotes) + ".");
        }
    }

    // Vanilla = Unity, TextMeshPro and the game's own assemblies. Everything else = some mod's addition.
    private static bool IsVanilla(Type t)
    {
        var name = t.Assembly.GetName().Name ?? "";
        return name.StartsWith("assembly_", StringComparison.Ordinal)
               || name.StartsWith("UnityEngine", StringComparison.Ordinal)
               || name.StartsWith("Unity.", StringComparison.Ordinal)
               || name == "gui_framework" || name == "SoftReferenceableAssets" || name == "Splatform";
    }

    private static void Kill(Component c)
    {
        var name = c.GetType().Name;
        try
        {
            UnityEngine.Object.DestroyImmediate(c);
            StripNotes.Add(name);
        }
        catch (Exception e)
        {
            Log.Debug("Music UI clone: could not remove " + name + ": " + e.Message);
        }
    }

    // Hint object inside the clone = our copy: remove. Outside = vanilla's own: leave it.
    private static void DropInside(GameObject obj, GameObject clone)
    {
        if (obj == null || obj == clone || !obj.transform.IsChildOf(clone.transform))
        {
            return;
        }
        StripNotes.Add(obj.name);
        UnityEngine.Object.DestroyImmediate(obj);
    }

    // ---------------------------------------------------------------- focus groups

    // Above every UI focus group in the scene (inactive ones too): the highest active group owns the gamepad and
    // toggles its CanvasGroup; vanilla pads in lower groups do not fire while we are up.
    internal static int TopPriority(UIGroupHandler own)
    {
        var max = int.MinValue;
        foreach (var g in UnityEngine.Object.FindObjectsByType<UIGroupHandler>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (g != null && g != own)
            {
                max = Math.Max(max, g.m_groupPriority);
            }
        }
        if (max == int.MinValue)
        {
            return 1;
        }
        return max >= int.MaxValue - 1 ? int.MaxValue : max + 1;
    }

#if DEBUG
    // ---------------------------------------------------------------- layout dump

    // One line per object down to depth: name, active flag, box in parent units, text.
    internal static void Describe(System.Text.StringBuilder sb, Transform t, int depth, int maxDepth)
    {
        if (t == null)
        {
            return;
        }
        sb.Append(' ', depth * 2).Append(t.name).Append(t.gameObject.activeInHierarchy ? " [on]" : " [off]");
        if (t is RectTransform rt)
        {
            var r = rt.rect;
            sb.Append(" pos=").Append(rt.anchoredPosition.x.ToString("F0")).Append(',').Append(rt.anchoredPosition.y.ToString("F0"))
                .Append(" size=").Append(r.width.ToString("F0")).Append('x').Append(r.height.ToString("F0"));
        }
        var text = t.GetComponent<TMP_Text>();
        if (text != null && !string.IsNullOrEmpty(text.text))
        {
            var s = text.text.Length > 60 ? text.text.Substring(0, 60) + "..." : text.text;
            sb.Append(" text=\"").Append(s).Append('"').Append(DrawFlag(text));
        }
        var canvas = t.GetComponent<Canvas>();
        if (canvas != null && canvas.isRootCanvas)
        {
            sb.Append(" order=").Append(canvas.sortingOrder).Append(" scale=").Append(canvas.scaleFactor.ToString("F2"));
        }
        sb.Append('\n');
        if (depth >= maxDepth)
        {
            return;
        }
        for (var i = 0; i < t.childCount; i++)
        {
            Describe(sb, t.GetChild(i), depth + 1, maxDepth);
        }
    }

    // Me flag a text that would not show: component off, or shown with text but no glyph built (box too low for the
    // font: TMP Ellipsis then draw nothing). "" = fine.
    internal static string DrawFlag(TMP_Text text)
    {
        if (text == null || string.IsNullOrEmpty(text.text))
        {
            return "";
        }
        if (!text.enabled)
        {
            return " [TEXT OFF]";
        }
        if (text.gameObject.activeInHierarchy && text.textInfo != null && text.textInfo.characterCount == 0)
        {
            return " [NOT DRAWN: " + (text.font != null ? text.font.name : "no font") + " " + text.fontSize.ToString("F0")
                   + "pt in " + text.rectTransform.rect.height.ToString("F0") + " high]";
        }
        return "";
    }

    // Screen box (pixels, bottom-left origin) of every image and text under root, hidden ones too: where a vanilla
    // HUD part sit, to check we no cover it. Nothing found = "none".
    internal static void DescribeScreenBox(System.Text.StringBuilder sb, string label, Transform root)
    {
        sb.Append(label).Append(": ");
        if (root == null)
        {
            sb.Append("none\n");
            return;
        }
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        var corners = new Vector3[4];
        var found = false;
        foreach (var g in root.GetComponentsInChildren<Graphic>(true))
        {
            if (g == null)
            {
                continue;
            }
            g.rectTransform.GetWorldCorners(corners);
            min = Vector2.Min(min, corners[0]);
            max = Vector2.Max(max, corners[2]);
            found = true;
        }
        if (!found)
        {
            sb.Append("none\n");
            return;
        }
        sb.Append(root.gameObject.activeInHierarchy ? "[on] (" : "[off] (").Append(min.x.ToString("F0")).Append(',')
            .Append(min.y.ToString("F0")).Append(")-(").Append(max.x.ToString("F0")).Append(',').Append(max.y.ToString("F0"))
            .Append(")\n");
    }
#endif
}
