using System.Collections.Generic;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MC.Exploration.CompendiumEncyclopediaMod;

// Me = the detail pane (design 3.7 "Detail pane"): the right scroll view's content replaced by our own; the vanilla
// text area hidden. m_textAreaTopic show the entry title; vanilla keep it inside the old content (layout group), so me
// move it into ours as first line (centred, wrapped) and hide the old content. Under it a head block (big icon or "?"
// mark + subtitle), then DetailBuilder lines top to bottom, stacked by hand from pooled objects (pools grow to the
// largest detail seen):
//   Header    = orange text; Paragraph = wrapped text (own game text as the game write it);
//   Row       = row clone (icon + text, paragraph text size) when the line has an icon or a link, else wrapped text;
//   Collapsed = row with the "?" mark.
// A row is a link (Button on) only when its first clickable reference is a discovered entry: click open that entry.
internal static partial class CompendiumWindow
{
    private const float DetailPad = 6f;
    private const float IndentStep = 18f;
    private const float HeaderGapAbove = 8f;
    private const float LineGap = 2f;

    private static readonly Color SubtitleColor = new Color(0.8f, 0.8f, 0.8f, 1f);

    private static TMP_Text _paraTemplate;
    private static TMP_Text _headerTemplate;
    private static readonly List<TMP_Text> Paras = new List<TMP_Text>();
    private static readonly List<TMP_Text> HeaderTexts = new List<TMP_Text>();
    private static readonly List<RowView> DetailRows = new List<RowView>();
    private static int _parasUsed;
    private static int _headersUsed;
    private static int _detailRowsUsed;
    private static RectTransform _hero;
    private static Image _heroIcon;
    private static TMP_Text _heroMark;
    private static TMP_Text _heroSubtitle;
    private static DetailView _view;
    private static bool _topicInContent;
    private static readonly List<string> Rendered = new List<string>();

    /// <summary>Detail shown now (self tests).</summary>
    internal static DetailView ShownDetails => _view;

    /// <summary>Text of every rendered detail line, in order (self tests).</summary>
    internal static IReadOnlyList<string> RenderedLines => Rendered;

    /// <summary>Head block shows a real sprite (self tests: never for an undiscovered entry).</summary>
    internal static bool HeroShowsSprite => _heroIcon != null && _heroIcon.enabled && _heroIcon.sprite != null;

    internal static Sprite HeroSprite => HeroShowsSprite ? _heroIcon.sprite : null;

    internal static bool HeroShowsMark => _heroMark != null && _heroMark.gameObject.activeSelf;

#if DEBUG
    /// <summary>Detail row objects (self tests click them). Only the first <see cref="DetailRowsShown"/> are shown.</summary>
    internal static IReadOnlyList<RowView> DetailRowViews => DetailRows;

    internal static int DetailRowsShown => _detailRowsUsed;

    /// <summary>Text size of the detail paragraphs (self tests compare the detail rows with it). 0 = no window.</summary>
    internal static float ParagraphFontSize => _paraTemplate != null ? _paraTemplate.fontSize : 0f;

    /// <summary>Paragraph text objects shown now (self tests read their wrap mode).</summary>
    internal static IReadOnlyList<TMP_Text> ParagraphTexts => Paras;

    internal static int ParagraphsShown => _parasUsed;

    internal static ScrollRect DetailScroll => _detailScroll;

    internal static RectTransform DetailContent => _detailContent;
#endif

    private static void SetupDetails(Scrollbar rightSb, TMP_Text textArea)
    {
        _detailViewport = _detailScroll.viewport != null ? _detailScroll.viewport : (RectTransform)_detailScroll.transform;
        var old = _detailScroll.content;
        var cgo = new GameObject("MC_Compendium_Detail", typeof(RectTransform));
        cgo.transform.SetParent(_detailViewport, false);
        _detailContent = (RectTransform)cgo.transform;
        UiUtil.TopStretch(_detailContent, 0f, 0f, 0f, 10f);
        _detailScroll.content = _detailContent;
        _detailScroll.horizontal = false;
        _detailScroll.vertical = true;
        if (_detailScroll.verticalScrollbar == null && rightSb != null)
        {
            _detailScroll.verticalScrollbar = rightSb;
        }

        _paraTemplate = MakeTextTemplate(textArea, "MC_Compendium_Text", textArea.color);
        _headerTemplate = MakeTextTemplate(textArea, "MC_Compendium_Header", RowView.HeaderColor);
        textArea.gameObject.SetActive(false);
        // Topic (vanilla: first child of the old content, placed by its layout group) = first line of our content,
        // scrolling with the text as in vanilla. Placed by hand: its layout parts go.
        if (_topic.transform.IsChildOf(_detailScroll.transform))
        {
            _topicInContent = true;
            _topic.transform.SetParent(_detailContent, false);
            DropLayoutParts(_topic.gameObject);
            _topic.enableAutoSizing = false;
            _topic.textWrappingMode = TextWrappingModes.Normal;
            _topic.overflowMode = TextOverflowModes.Overflow;
            _topic.horizontalAlignment = HorizontalAlignmentOptions.Center;
            _topic.verticalAlignment = VerticalAlignmentOptions.Top;
            _topic.raycastTarget = false;
        }
        if (old != null && old != textArea.rectTransform && old != _detailViewport && old != _detailContent && !_topic.transform.IsChildOf(old))
        {
            old.gameObject.SetActive(false);
        }
        MakeHero();
    }

    // Copy of the vanilla text area: same font, material, size. Layout parts removed (me size it by hand).
    private static TMP_Text MakeTextTemplate(TMP_Text src, string name, Color color)
    {
        var go = Object.Instantiate(src.gameObject, _templates, false);
        go.name = name;
        CloneUtil.Strip(go, "detail text", Navigation.Mode.None);
        for (var i = go.transform.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
        }
        foreach (var c in go.GetComponents<Component>())
        {
            if (c is ILayoutController || c is LayoutElement)
            {
                Object.DestroyImmediate(c);
            }
        }
        go.SetActive(true);
        var t = go.GetComponent<TMP_Text>();
        var size = t.enableAutoSizing ? t.fontSizeMax : t.fontSize;
        t.enableAutoSizing = false;
        t.fontSize = size;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.overflowMode = TextOverflowModes.Overflow;
        t.horizontalAlignment = HorizontalAlignmentOptions.Left;
        t.verticalAlignment = VerticalAlignmentOptions.Top;
        t.raycastTarget = false;
        t.richText = true;
        t.color = color;
        t.text = "";
        return t;
    }

    private static void MakeHero()
    {
        var go = new GameObject("MC_Compendium_Head", typeof(RectTransform));
        go.transform.SetParent(_detailContent, false);
        _hero = (RectTransform)go.transform;
        var h = HeroHeight;

        var iconGo = new GameObject("icon", typeof(RectTransform), typeof(Image));
        iconGo.transform.SetParent(_hero, false);
        _heroIcon = iconGo.GetComponent<Image>();
        _heroIcon.preserveAspect = true;
        _heroIcon.raycastTarget = false;
        var irt = _heroIcon.rectTransform;
        irt.anchorMin = new Vector2(0f, 0.5f);
        irt.anchorMax = new Vector2(0f, 0.5f);
        irt.pivot = new Vector2(0f, 0.5f);
        irt.sizeDelta = new Vector2(h, h);
        irt.anchoredPosition = Vector2.zero;

        _heroMark = Object.Instantiate(_paraTemplate.gameObject, _hero, false).GetComponent<TMP_Text>();
        _heroMark.name = "mark";
        CloneUtil.Strip(_heroMark.gameObject, "detail head", Navigation.Mode.None);
        var mrt = _heroMark.rectTransform;
        mrt.anchorMin = irt.anchorMin;
        mrt.anchorMax = irt.anchorMax;
        mrt.pivot = irt.pivot;
        mrt.sizeDelta = irt.sizeDelta;
        mrt.anchoredPosition = irt.anchoredPosition;
        _heroMark.text = Labels.UnknownMark;
        _heroMark.fontSize = h * 0.7f;
        _heroMark.fontStyle = FontStyles.Bold;
        _heroMark.horizontalAlignment = HorizontalAlignmentOptions.Center;
        _heroMark.verticalAlignment = VerticalAlignmentOptions.Middle;
        _heroMark.color = RowView.MarkColor;
        _heroMark.gameObject.SetActive(false);

        _heroSubtitle = Object.Instantiate(_paraTemplate.gameObject, _hero, false).GetComponent<TMP_Text>();
        _heroSubtitle.name = "subtitle";
        CloneUtil.Strip(_heroSubtitle.gameObject, "detail head", Navigation.Mode.None);
        var srt = _heroSubtitle.rectTransform;
        srt.anchorMin = Vector2.zero;
        srt.anchorMax = Vector2.one;
        srt.pivot = new Vector2(0f, 0.5f);
        srt.offsetMin = new Vector2(h + 8f, 0f);
        srt.offsetMax = Vector2.zero;
        _heroSubtitle.textWrappingMode = TextWrappingModes.NoWrap;
        _heroSubtitle.overflowMode = TextOverflowModes.Ellipsis;
        _heroSubtitle.verticalAlignment = VerticalAlignmentOptions.Middle;
        _heroSubtitle.color = SubtitleColor;
        go.SetActive(false);
    }

    private static float HeroHeight => Mathf.Round(_rowH * 1.6f);

    // ---------------------------------------------------------------- render

    private static void ShowDetails(Entry e)
    {
        if (_detailContent == null)
        {
            return;
        }
        _parasUsed = 0;
        _headersUsed = 0;
        _detailRowsUsed = 0;
        Rendered.Clear();
        _view = null;
        if (e == null || _k == null)
        {
            if (_topic != null)
            {
                _topic.text = "";
            }
            if (_hero != null)
            {
                _hero.gameObject.SetActive(false);
            }
            FinishDetails(0f);
            return;
        }
        var view = DetailBuilder.Build(e, _k);
        _view = view;
        if (_topic != null)
        {
            _topic.text = view.Title;
        }
        var width = Mathf.Max(60f, _detailContent.rect.width);
        var y = _topicInContent ? PlaceTopic(view.Title, DetailPad, width) : DetailPad;
        y = PlaceHero(view, y);
        foreach (var line in view.Lines)
        {
            var indent = line.Indent * IndentStep;
            var text = line.Text();
            Rendered.Add(text);
            switch (line.Kind)
            {
                case DetailLineKind.Header:
                    y = PlaceText(TakeText(HeaderTexts, ref _headersUsed, _headerTemplate), text, indent, y + HeaderGapAbove, width);
                    break;
                case DetailLineKind.Paragraph:
                    y = PlaceText(TakeText(Paras, ref _parasUsed, _paraTemplate), text, indent, y, width);
                    break;
                case DetailLineKind.Collapsed:
                    y = PlaceRow(IconKind.Unknown, null, text, null, indent, y, RowView.UnknownColor);
                    break;
                default:
                    var iconRef = line.IconRef;
                    var link = FirstLink(line);
                    var hasIcon = iconRef != null && iconRef.IconKind != IconKind.None;
                    y = !hasIcon && link == null
                        ? PlaceText(TakeText(Paras, ref _parasUsed, _paraTemplate), text, indent, y, width)
                        : PlaceRow(hasIcon ? iconRef.IconKind : IconKind.None, hasIcon ? iconRef.Icon : null, text, link, indent, y, RowView.KnownColor);
                    break;
            }
        }
        FinishDetails(y + DetailPad);
    }

    // Entry title, centred, wrapped at the pane width (vanilla topic font and colour).
    private static float PlaceTopic(string title, float y, float width)
    {
        _topic.text = title ?? "";
        var w = Mathf.Max(20f, width - 2f * DetailPad);
        var h = Mathf.Max(_topic.fontSize * 1.2f, _topic.GetPreferredValues(_topic.text, w, 0f).y);
        UiUtil.TopStretch(_topic.rectTransform, DetailPad, DetailPad, y, h);
        return y + h + LineGap;
    }

    private static RefTarget FirstLink(DetailLine line)
    {
        foreach (var p in line.Parts)
        {
            if (p.Ref != null && p.Ref.Clickable)
            {
                return p.Ref;
            }
        }
        return null;
    }

    private static float PlaceHero(DetailView view, float y)
    {
        if (_hero == null)
        {
            return y;
        }
        var h = HeroHeight;
        _hero.gameObject.SetActive(true);
        UiUtil.TopStretch(_hero, DetailPad, DetailPad, y, h);
        Sprite sprite = null;
        var mark = false;
        switch (view.IconKind)
        {
            case IconKind.Sprite:
                sprite = view.Icon;
                break;
            case IconKind.GenericCreature:
                sprite = PawIcon.Get();
                break;
            case IconKind.Unknown:
                mark = true;
                break;
        }
        _heroIcon.sprite = sprite;
        _heroIcon.enabled = sprite != null;
        if (_heroMark.gameObject.activeSelf != mark)
        {
            _heroMark.gameObject.SetActive(mark);
        }
        _heroSubtitle.text = view.Subtitle ?? "";
        return y + h + 2f * LineGap;
    }

    private static TMP_Text TakeText(List<TMP_Text> pool, ref int used, TMP_Text template)
    {
        if (used >= pool.Count)
        {
            var go = Object.Instantiate(template.gameObject, _templates, false);
            CloneUtil.Strip(go, "detail text line", Navigation.Mode.None);
            go.transform.SetParent(_detailContent, false);
            pool.Add(go.GetComponent<TMP_Text>());
        }
        return pool[used++];
    }

    private static float PlaceText(TMP_Text t, string text, float indent, float y, float width)
    {
        if (!t.gameObject.activeSelf)
        {
            t.gameObject.SetActive(true);
        }
        t.text = text;
        var w = Mathf.Max(20f, width - indent - 2f * DetailPad);
        var h = Mathf.Max(t.fontSize * 1.25f, t.GetPreferredValues(text, w, 0f).y);
        UiUtil.TopStretch(t.rectTransform, indent + DetailPad, DetailPad, y, h);
        return y + h + LineGap;
    }

    private static float PlaceRow(IconKind kind, Sprite sprite, string text, RefTarget link, float indent, float y, Color color)
    {
        if (_detailRowsUsed >= DetailRows.Count)
        {
            var made = RowView.Create(_rowTemplate, _templates, _detailContent, "detail row", OnDetailRowClicked);
            // Same text size as the paragraphs around it (the crafting row's own text is smaller).
            made.Name.enableAutoSizing = false;
            made.Name.fontSize = _paraTemplate.fontSize;
            DetailRows.Add(made);
        }
        var row = DetailRows[_detailRowsUsed++];
        row.Target = link != null ? link.Entry : null;
        row.SetMode(kind == IconKind.None ? RowView.Mode.Text : RowView.Mode.Entry, clickable: link != null);
        if (kind != IconKind.None)
        {
            row.SetIcon(kind, sprite);
        }
        row.SetText(text, color);
        row.SetSelected(false);
        UiUtil.TopStretch(row.Rt, indent + DetailPad, DetailPad, y, _rowH);
        row.Show(true);
        return y + _rowH;
    }

    private static void OnDetailRowClicked(RowView row)
    {
        if (row.Target == null)
        {
            return;
        }
        if (GamepadRumble.instance != null)
        {
            GamepadRumble.instance.PlayGlobalSelectVibration();
        }
        OpenEntry(row.Target);
    }

    private static void FinishDetails(float height)
    {
        for (var i = _parasUsed; i < Paras.Count; i++)
        {
            Hide(Paras[i]);
        }
        for (var i = _headersUsed; i < HeaderTexts.Count; i++)
        {
            Hide(HeaderTexts[i]);
        }
        for (var i = _detailRowsUsed; i < DetailRows.Count; i++)
        {
            DetailRows[i].Target = null;
            DetailRows[i].Show(false);
        }
        _detailContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(1f, height));
        if (_detailScroll != null)
        {
            _detailScroll.StopMovement();
            _detailScroll.verticalNormalizedPosition = 1f;
        }
    }

    private static void Hide(TMP_Text t)
    {
        if (t != null && t.gameObject.activeSelf)
        {
            t.gameObject.SetActive(false);
        }
    }

    private static void ForgetDetails()
    {
        _paraTemplate = null;
        _headerTemplate = null;
        Paras.Clear();
        HeaderTexts.Clear();
        DetailRows.Clear();
        _parasUsed = 0;
        _headersUsed = 0;
        _detailRowsUsed = 0;
        _hero = null;
        _heroIcon = null;
        _heroMark = null;
        _heroSubtitle = null;
        _topicInContent = false;
        _view = null;
        Rendered.Clear();
    }
}
