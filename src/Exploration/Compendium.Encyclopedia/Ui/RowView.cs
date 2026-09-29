using System;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MC.Exploration.CompendiumEncyclopediaMod;

// Me = one row object: clone of the crafting list row (m_recipeElementPrefab): background, icon, name, "selected"
// highlight, Button. Used by the virtualized list (entry and header rows) and by the detail pane (reference rows).
// Me lay out the children myself (icon left, "?" mark on the icon, name after it), so the look follow the vanilla
// row but the geometry do not depend on the prefab's inner layout. Undiscovered = icon Image off, "?" mark on: the
// real sprite is never assigned (decision 15).
internal sealed class RowView
{
    internal enum Mode : byte
    {
        Unset,
        Entry,
        Text,
        Header,
    }

    internal static readonly Color HeaderColor = new Color(1f, 0.647f, 0f, 1f); // TMP "orange", as vanilla headers
    internal static readonly Color KnownColor = Color.white;
    internal static readonly Color UnknownColor = new Color(0.66f, 0.66f, 0.66f, 1f); // vanilla "cannot craft" grey
    internal static readonly Color MarkColor = new Color(0.75f, 0.75f, 0.75f, 1f);

    private const float Pad = 4f;

    internal GameObject Go;
    internal RectTransform Rt;
    internal Button Button;
    internal Image Bg;
    internal Image Icon;
    internal TMP_Text Name;
    internal TMP_Text Mark;
    internal GameObject Selected;
    internal float NameLeftEntry;
    internal float NameLeftText;
    internal Mode Current = Mode.Unset;

    /// <summary>List rows: index of the bound list row (-1 = none).</summary>
    internal int Bound = -1;

    /// <summary>Entry opened on click (list: the row's entry; details: the reference target).</summary>
    internal Entry Target;

    // ---------------------------------------------------------------- template

    /// <summary>
    /// Row template from the crafting row prefab, under <paramref name="holder"/> (inactive). Null = prefab not
    /// recognised (no "name" text). Height <paramref name="rowH"/>.
    /// </summary>
    internal static GameObject BuildTemplate(GameObject prefab, Transform holder, float rowH)
    {
        if (prefab == null)
        {
            return null;
        }
        var go = Object.Instantiate(prefab, holder, false);
        go.name = "MC_Compendium_RowTemplate";
        // Crafting-only parts: durability bar and quality number.
        foreach (var part in new[] { "Durability", "QualityLevel" })
        {
            var t = go.transform.Find(part);
            if (t != null)
            {
                Object.DestroyImmediate(t.gameObject);
            }
        }
        CloneUtil.Strip(go, "row template", Navigation.Mode.None);
        var nameT = go.transform.Find("name");
        var name = nameT != null ? nameT.GetComponent<TMP_Text>() : null;
        if (name == null || !(go.transform is RectTransform rt))
        {
            Object.DestroyImmediate(go);
            return null;
        }
        UiUtil.TopStretch(rt, 0f, 0f, 0f, rowH);
        foreach (var fitter in go.GetComponentsInChildren<LayoutElement>(true))
        {
            Object.DestroyImmediate(fitter);
        }

        var iconT = go.transform.Find("icon");
        var icon = iconT != null ? iconT.GetComponent<Image>() : null;
        if (icon == null)
        {
            if (iconT != null)
            {
                Object.DestroyImmediate(iconT.gameObject); // "icon" without Image: make a real one
            }
            var iconGo = new GameObject("icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(go.transform, false);
            icon = iconGo.GetComponent<Image>();
        }
        var iconSize = Mathf.Max(8f, rowH - 6f);
        var irt = icon.rectTransform;
        irt.anchorMin = new Vector2(0f, 0.5f);
        irt.anchorMax = new Vector2(0f, 0.5f);
        irt.pivot = new Vector2(0f, 0.5f);
        irt.sizeDelta = new Vector2(iconSize, iconSize);
        irt.anchoredPosition = new Vector2(Pad, 0f);
        irt.localScale = Vector3.one;
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        icon.color = Color.white;

        var nrt = name.rectTransform;
        nrt.anchorMin = Vector2.zero;
        nrt.anchorMax = Vector2.one;
        nrt.pivot = new Vector2(0f, 0.5f);
        nrt.offsetMin = new Vector2(Pad + iconSize + Pad * 1.5f, 0f);
        nrt.offsetMax = new Vector2(-Pad, 0f);
        nrt.localScale = Vector3.one;
        name.textWrappingMode = TextWrappingModes.NoWrap;
        name.overflowMode = TextOverflowModes.Ellipsis;
        name.horizontalAlignment = HorizontalAlignmentOptions.Left;
        name.verticalAlignment = VerticalAlignmentOptions.Middle;
        name.raycastTarget = false;
        name.richText = true;

        // "?" mark: copy of the name text, over the icon rect.
        var markGo = Object.Instantiate(name.gameObject, go.transform, false);
        markGo.name = "mark";
        var mark = markGo.GetComponent<TMP_Text>();
        var mrt = mark.rectTransform;
        mrt.anchorMin = irt.anchorMin;
        mrt.anchorMax = irt.anchorMax;
        mrt.pivot = irt.pivot;
        mrt.sizeDelta = irt.sizeDelta;
        mrt.anchoredPosition = irt.anchoredPosition;
        mark.text = Labels.UnknownMark;
        mark.enableAutoSizing = false;
        mark.fontSize = Mathf.Max(10f, iconSize * 0.8f);
        mark.fontStyle = FontStyles.Bold;
        mark.horizontalAlignment = HorizontalAlignmentOptions.Center;
        mark.verticalAlignment = VerticalAlignmentOptions.Middle;
        mark.color = MarkColor;
        mark.overflowMode = TextOverflowModes.Overflow;
        markGo.SetActive(false);

        var selected = go.transform.Find("selected");
        if (selected != null)
        {
            selected.gameObject.SetActive(false);
        }
        go.SetActive(true);
        return go;
    }

    /// <summary>
    /// One row from the template: made under the inactive <paramref name="holder"/>, stripped (rule: every clone),
    /// then moved to <paramref name="parent"/>. Click goes to <paramref name="onClick"/> with this row.
    /// </summary>
    internal static RowView Create(GameObject template, Transform holder, Transform parent, string kind, Action<RowView> onClick)
    {
        var go = Object.Instantiate(template, holder, false);
        go.name = "MC_Compendium_Row";
        CloneUtil.Strip(go, kind, Navigation.Mode.None);
        var row = new RowView
        {
            Go = go,
            Rt = (RectTransform)go.transform,
            Button = go.GetComponent<Button>(),
            Bg = FindBackground(go),
            Icon = go.transform.Find("icon").GetComponent<Image>(),
            Name = go.transform.Find("name").GetComponent<TMP_Text>(),
            Mark = go.transform.Find("mark").GetComponent<TMP_Text>(),
        };
        var selected = go.transform.Find("selected");
        row.Selected = selected != null ? selected.gameObject : null;
        row.NameLeftEntry = row.Name.rectTransform.offsetMin.x;
        row.NameLeftText = Pad;
        if (row.Button != null)
        {
            row.Button.onClick.AddListener(() =>
            {
                try
                {
                    onClick(row);
                }
                catch (Exception e)
                {
                    PatchGuard.Report("Encyclopedia row click", e);
                }
            });
        }
        go.transform.SetParent(parent, false);
        return row;
    }

    // Row background = the Button's tinted graphic (crafting row 1.0.16: child "bkg", raw colour light grey, tinted
    // almost clear by the Button). A row with its Button off is not tinted any more: its background must go too, else
    // headers and plain detail rows show a light grey bar.
    private static Image FindBackground(GameObject go)
    {
        var button = go.GetComponent<Button>();
        var icon = go.transform.Find("icon");
        if (button != null && button.targetGraphic is Image target && (icon == null || target.transform != icon))
        {
            return target;
        }
        var root = go.GetComponent<Image>();
        if (root != null)
        {
            return root;
        }
        var bkg = go.transform.Find("bkg");
        return bkg != null ? bkg.GetComponent<Image>() : null;
    }

    // ---------------------------------------------------------------- state

    /// <summary>Entry = icon + name, clickable. Text = name only from the left edge. Header = Text, orange, never clickable.</summary>
    internal void SetMode(Mode mode, bool clickable)
    {
        if (mode == Mode.Header)
        {
            clickable = false;
        }
        if (Bg != null && Bg.enabled != clickable)
        {
            Bg.enabled = clickable;
        }
        if (Button != null && Button.enabled != clickable)
        {
            Button.enabled = clickable;
        }
        if (mode != Current)
        {
            var nrt = Name.rectTransform;
            nrt.offsetMin = new Vector2(mode == Mode.Entry ? NameLeftEntry : NameLeftText, nrt.offsetMin.y);
            if (mode != Mode.Entry)
            {
                SetIcon(IconKind.None, null);
            }
            Current = mode;
        }
        if (mode == Mode.Header)
        {
            SetSelected(false);
        }
    }

    internal void SetText(string text, Color color)
    {
        if (!ReferenceEquals(Name.text, text))
        {
            Name.text = text;
        }
        if (Name.color != color)
        {
            Name.color = color;
        }
    }

    /// <summary>Icon slot: sprite, paw print (generic creature, <see cref="PawIcon"/>), "?" mark, or nothing.</summary>
    internal void SetIcon(IconKind kind, Sprite sprite)
    {
        var showMark = false;
        Sprite show = null;
        switch (kind)
        {
            case IconKind.Sprite:
                show = sprite;
                break;
            case IconKind.GenericCreature:
                show = PawIcon.Get();
                break;
            case IconKind.Unknown:
                showMark = true;
                break;
        }
        if (!ReferenceEquals(Icon.sprite, show))
        {
            Icon.sprite = show;
        }
        var iconOn = show != null;
        if (Icon.enabled != iconOn)
        {
            Icon.enabled = iconOn;
        }
        if (Mark.gameObject.activeSelf != showMark)
        {
            Mark.gameObject.SetActive(showMark);
        }
    }

    internal void SetSelected(bool on)
    {
        if (Selected != null && Selected.activeSelf != on)
        {
            Selected.SetActive(on);
        }
    }

    internal void Show(bool on)
    {
        if (Go.activeSelf != on)
        {
            Go.SetActive(on);
        }
    }
}
