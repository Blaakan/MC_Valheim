using System;
using UnityEngine;
using UnityEngine.UI;

namespace MC.Farming.FishingFightMod;

// Me = the catch bar on screen (calm phase only) and the optional struggle arrow. Built in code under the vanilla
// HUD root (Hud.m_rootObject): Ctrl+F3, the gamepad HUD toggle and cutscenes hide it with the rest of the HUD.
//   Frame + track (dark), zone (green while the fish is in it, orange when not), fish marker = the hooked fish's
//   own item icon, line meter beside the track (how much line already came in).
//   Arrow beside the crosshair, on the side to turn the rod to (Display.ShowStruggleArrow, off by default), green
//   when the rod is right, red when not.
// Hud.Update postfix call Tick every frame: no fight = one null check (root hidden once). Bar values drawn between the
// last two physics ticks, so it move smooth at any frame rate. Objects made once per Hud, destroyed by DestroyAll
// (feature off) or with the Hud (logout).
internal static class FightHud
{
    // Track size in HUD units (scaled by Display.BarScale).
    private const float TrackWidth = 34f;
    private const float TrackHeight = 300f;
    private const float FramePad = 4f;
    private const float FishSize = 34f;
    private const float MeterWidth = 7f;
    private const float MeterGap = 6f;
    private const float ArrowSize = 56f;
    private const float ArrowBesideCrosshair = 120f;

    private static readonly Color FrameColor = new Color(0.16f, 0.11f, 0.07f, 0.88f);
    private static readonly Color TrackColor = new Color(0.05f, 0.07f, 0.09f, 0.75f);
    private static readonly Color ZoneInColor = new Color(0.40f, 0.85f, 0.35f, 0.65f);
    private static readonly Color ZoneOutColor = new Color(0.90f, 0.55f, 0.20f, 0.55f);
    private static readonly Color MeterBackColor = new Color(0.05f, 0.07f, 0.09f, 0.75f);
    private static readonly Color MeterColor = new Color(0.95f, 0.80f, 0.35f, 0.95f);
    private static readonly Color ArrowGood = new Color(0.40f, 0.90f, 0.35f, 0.95f);
    private static readonly Color ArrowWrong = new Color(0.95f, 0.30f, 0.25f, 0.95f);
    private static readonly Color FishTint = Color.white;

    private static Hud _hud;
    private static GameObject _root;
    private static RectTransform _bar;
    private static RectTransform _zone;
    private static Image _zoneImage;
    private static RectTransform _fish;
    private static Image _fishImage;
    private static RectTransform _meterFill;
    private static RectTransform _arrow;
    private static Image _arrowImage;
    private static Sprite _white;
    private static Sprite _triangle;
    private static Texture2D _whiteTex;
    private static Texture2D _triangleTex;
    private static bool _shown;

    // Last values put on screen (self test read them).
    internal static bool BarShown { get; private set; }
    internal static bool ArrowShown { get; private set; }

    // Root on screen (Tick must run once more to hide it).
    internal static bool Shown => _shown;

#if DEBUG
    // Self test: arrow on or off without touching the config. Null = config.
    internal static bool? TestArrow;
#endif

    internal static void Tick()
    {
        var fight = Fight.Current;
        if (fight == null)
        {
            if (_shown)
            {
                Hide();
            }
            return;
        }
        var hud = Hud.instance;
        if (hud == null || hud.m_rootObject == null)
        {
            return;
        }
        if (!ReferenceEquals(hud, _hud) || _root == null)
        {
            Build(hud);
        }
        Draw(fight);
    }

    private static void Draw(Fight fight)
    {
        var scale = Plugin.BarScale != null ? Plugin.BarScale.Value : 1f;
        var offsetX = Plugin.BarOffsetX != null ? Plugin.BarOffsetX.Value : 260f;
        var offsetY = Plugin.BarOffsetY != null ? Plugin.BarOffsetY.Value : 0f;
        var showArrow = Plugin.ShowStruggleArrow != null && Plugin.ShowStruggleArrow.Value;
#if DEBUG
        showArrow = TestArrow ?? showArrow;
#endif

        if (!_root.activeSelf)
        {
            _root.SetActive(true);
        }
        _shown = true;

        var calm = fight.Phase == FightPhase.Calm;
        SetActive(_bar.gameObject, calm);
        BarShown = calm;
        if (calm)
        {
            _bar.anchoredPosition = new Vector2(offsetX, offsetY);
            _bar.localScale = new Vector3(scale, scale, 1f);
            // Between the last two physics ticks.
            var t = Time.fixedDeltaTime > 0f ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime) : 1f;
            var bar = fight.Bar;
            var zonePos = Mathf.Lerp(fight.PrevZone, bar.ZonePos, t);
            var fishPos = Mathf.Lerp(fight.PrevFish, bar.FishPos, t);
            _zone.anchoredPosition = new Vector2(0f, zonePos * TrackHeight);
            _zone.sizeDelta = new Vector2(0f, bar.ZoneSize * TrackHeight);
            var inZone = bar.InZone;
            _zoneImage.color = inZone ? ZoneInColor : ZoneOutColor;
            _fish.anchoredPosition = new Vector2(0f, fishPos * TrackHeight);
            if (fight.Icon != null && !ReferenceEquals(_fishImage.sprite, fight.Icon))
            {
                _fishImage.sprite = fight.Icon;
            }
            var progress = fight.StartLine > 0f ? Mathf.Clamp01(1f - fight.LineLength / fight.StartLine) : 0f;
            _meterFill.sizeDelta = new Vector2(0f, progress * TrackHeight);
        }

        var arrow = showArrow && !calm && fight.Side != 0;
        SetActive(_arrow.gameObject, arrow);
        ArrowShown = arrow;
        if (arrow)
        {
            // Rod must go away from the run: fish run right -> arrow point left.
            var pointLeft = fight.Side > 0;
            _arrow.anchoredPosition = new Vector2((pointLeft ? -ArrowBesideCrosshair : ArrowBesideCrosshair) * scale, 0f);
            _arrow.localScale = new Vector3(pointLeft ? -scale : scale, scale, 1f);
            _arrowImage.color = fight.Verdict == RodVerdict.Good ? ArrowGood : ArrowWrong;
        }
    }

    private static void Hide()
    {
        _shown = false;
        BarShown = false;
        ArrowShown = false;
        if (_root != null && _root.activeSelf)
        {
            _root.SetActive(false);
        }
    }

    private static void SetActive(GameObject go, bool on)
    {
        if (go.activeSelf != on)
        {
            go.SetActive(on);
        }
    }

    private static void Build(Hud hud)
    {
        DestroyObjects();
        EnsureSprites();
        _hud = hud;
        _root = new GameObject("MC_FishingFight", typeof(RectTransform));
        var rootRt = (RectTransform)_root.transform;
        rootRt.SetParent(hud.m_rootObject.transform, false);
        Stretch(rootRt);

        // Bar group: anchored at the screen centre, offset to the right of the crosshair.
        _bar = Rect("Bar", rootRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f));
        _bar.sizeDelta = new Vector2(TrackWidth + MeterGap + MeterWidth + FramePad * 2f, TrackHeight + FramePad * 2f);
        var frame = Rect("Frame", _bar, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        frame.sizeDelta = Vector2.zero;
        Img(frame, _white, FrameColor);

        var track = Rect("Track", _bar, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f));
        track.anchoredPosition = new Vector2(FramePad, FramePad);
        track.sizeDelta = new Vector2(TrackWidth, TrackHeight);
        Img(track, _white, TrackColor);

        _zone = Rect("Zone", track, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
        _zoneImage = Img(_zone, _white, ZoneInColor);

        _fish = Rect("Fish", track, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f));
        _fish.sizeDelta = new Vector2(FishSize, FishSize);
        _fishImage = Img(_fish, _white, FishTint);
        _fishImage.preserveAspect = true;

        var meter = Rect("Meter", _bar, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f));
        meter.anchoredPosition = new Vector2(FramePad + TrackWidth + MeterGap, FramePad);
        meter.sizeDelta = new Vector2(MeterWidth, TrackHeight);
        Img(meter, _white, MeterBackColor);
        _meterFill = Rect("Fill", meter, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
        Img(_meterFill, _white, MeterColor);

        _arrow = Rect("Arrow", rootRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        _arrow.sizeDelta = new Vector2(ArrowSize, ArrowSize);
        _arrowImage = Img(_arrow, _triangle, ArrowGood);

        _bar.gameObject.SetActive(false);
        _arrow.gameObject.SetActive(false);
        _root.SetActive(false);
        _shown = false;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
        return rt;
    }

    private static Image Img(RectTransform rt, Sprite sprite, Color color)
    {
        var image = rt.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
    }

    private static void EnsureSprites()
    {
        if (_white == null)
        {
            _whiteTex = new Texture2D(4, 4, TextureFormat.RGBA32, false)
            {
                name = "MC_FishingFight_White",
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color[16];
            for (var i = 0; i < px.Length; i++)
            {
                px[i] = Color.white;
            }
            _whiteTex.SetPixels(px);
            _whiteTex.Apply(false, true);
            _white = Sprite.Create(_whiteTex, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect);
            _white.name = "MC_FishingFight_White";
            _white.hideFlags = HideFlags.HideAndDontSave;
        }
        if (_triangle == null)
        {
            _triangleTex = DrawArrow(64);
            _triangle = Sprite.Create(_triangleTex, new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect);
            _triangle.name = "MC_FishingFight_Arrow";
            _triangle.hideFlags = HideFlags.HideAndDontSave;
        }
    }

    // White arrow pointing right (flipped in x for left), dark outline, 4x4 supersampled edges.
    private static Texture2D DrawArrow(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "MC_FishingFight_Arrow",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var pixels = new Color[size * size];
        const int samples = 4;
        var dark = new Color(0.1f, 0.08f, 0.06f, 1f);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                float r = 0f, g = 0f, b = 0f, a = 0f;
                for (var sy = 0; sy < samples; sy++)
                {
                    for (var sx = 0; sx < samples; sx++)
                    {
                        var p = new Vector2((x + (sx + 0.5f) / samples) / size, (y + (sy + 0.5f) / samples) / size);
                        var c = ArrowSample(p, dark);
                        r += c.r * c.a;
                        g += c.g * c.a;
                        b += c.b * c.a;
                        a += c.a;
                    }
                }
                const float n = samples * samples;
                pixels[y * size + x] = a > 0f ? new Color(r / a, g / a, b / a, a / n) : new Color(0f, 0f, 0f, 0f);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply(false, true);
        return tex;
    }

    // Arrow in 0..1: shaft x 0.12..0.56, y 0.38..0.62; head triangle x 0.5..0.92, half height 0.35 at its back.
    // Inside the shape shrunk by the outline = white, the rest of the shape = dark outline.
    private static Color ArrowSample(Vector2 p, Color dark)
    {
        const float outline = 0.045f;
        if (InArrow(p, outline))
        {
            return Color.white;
        }
        return InArrow(p, 0f) ? dark : new Color(0f, 0f, 0f, 0f);
    }

    private static bool InArrow(Vector2 p, float inset)
    {
        if (p.x >= 0.12f + inset && p.x <= 0.56f && p.y >= 0.38f + inset && p.y <= 0.62f - inset)
        {
            return true;
        }
        // Head slant: 0.35 down over 0.42, so a perpendicular inset is about 1.3x in y and 2.2x at the tip in x.
        if (p.x < 0.5f + inset || p.x > 0.92f - inset * 2.2f)
        {
            return false;
        }
        var half = 0.35f * (0.92f - p.x) / 0.42f - inset * 1.3f;
        return Mathf.Abs(p.y - 0.5f) <= half;
    }

    // Feature off: everything gone (sprites made again next time).
    internal static void DestroyAll()
    {
        DestroyObjects();
        if (_white != null)
        {
            UnityEngine.Object.Destroy(_white);
            _white = null;
        }
        if (_triangle != null)
        {
            UnityEngine.Object.Destroy(_triangle);
            _triangle = null;
        }
        if (_whiteTex != null)
        {
            UnityEngine.Object.Destroy(_whiteTex);
            _whiteTex = null;
        }
        if (_triangleTex != null)
        {
            UnityEngine.Object.Destroy(_triangleTex);
            _triangleTex = null;
        }
    }

    private static void DestroyObjects()
    {
        if (_root != null)
        {
            UnityEngine.Object.Destroy(_root);
        }
        _root = null;
        _bar = null;
        _zone = null;
        _zoneImage = null;
        _fish = null;
        _fishImage = null;
        _meterFill = null;
        _arrow = null;
        _arrowImage = null;
        _hud = null;
        _shown = false;
        BarShown = false;
        ArrowShown = false;
    }
}
