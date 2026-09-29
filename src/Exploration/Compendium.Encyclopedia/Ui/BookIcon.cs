using UnityEngine;

namespace MC.Exploration.CompendiumEncyclopediaMod;

// Me = the button's book icon, drawn in code once (no asset file, decision 16): open book seen from the front, cover
// behind two pages, spine, outlines, ink lines. Drawn in light greys only: the side button's colour tint (kept from the
// cloned Valheim Compendium button) turn it gold, like the vanilla side icons (raven, valknut, shield, trophy, swords:
// about RGB 186,134,72 on screen; 1.0.16 screenshots showed our own colours multiplied by that tint). 64x64,
// transparent, 4x4 supersampling so edges stay soft. Destroyed on feature off (texture and sprite are ours).
internal static class BookIcon
{
    private const int Size = 64;
    private const int Samples = 4;

    private static Texture2D _texture;
    private static Sprite _sprite;

    private static readonly Color Page = new Color(1f, 1f, 1f, 1f);
    private static readonly Color PageShade = new Color(0.86f, 0.86f, 0.86f, 1f);
    private static readonly Color Cover = new Color(0.74f, 0.74f, 0.74f, 1f);
    private static readonly Color Dark = new Color(0.42f, 0.42f, 0.42f, 1f);
    private static readonly Color Ink = new Color(0.58f, 0.58f, 0.58f, 1f);

    // Texture y go up. Page corners: outer-bottom, spine-bottom, spine-top, outer-top (convex, counter-clockwise).
    private static readonly Vector2[] LeftPage = { new Vector2(6f, 15f), new Vector2(31f, 10f), new Vector2(31f, 51f), new Vector2(6f, 56f) };
    private static readonly Vector2[] RightPage = { new Vector2(33f, 10f), new Vector2(58f, 15f), new Vector2(58f, 56f), new Vector2(33f, 51f) };
    private static readonly Vector2[] LeftCover = { new Vector2(2f, 11f), new Vector2(32f, 5f), new Vector2(32f, 47f), new Vector2(2f, 52f) };
    private static readonly Vector2[] RightCover = { new Vector2(32f, 5f), new Vector2(62f, 11f), new Vector2(62f, 52f), new Vector2(32f, 47f) };

    internal static Sprite Get()
    {
        if (_sprite != null)
        {
            return _sprite;
        }
        _texture = Draw();
        _sprite = Sprite.Create(_texture, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        _sprite.name = "MC_Compendium_Book";
        _sprite.hideFlags = HideFlags.HideAndDontSave;
        return _sprite;
    }

    internal static void Destroy()
    {
        if (_sprite != null)
        {
            Object.Destroy(_sprite);
        }
        if (_texture != null)
        {
            Object.Destroy(_texture);
        }
        _sprite = null;
        _texture = null;
    }

    private static Texture2D Draw()
    {
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            name = "MC_Compendium_Book",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var pixels = new Color[Size * Size];
        const float step = 1f / Samples;
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                // Premultiplied sum of the sub-samples, then back to straight alpha.
                var r = 0f;
                var g = 0f;
                var b = 0f;
                var a = 0f;
                for (var sy = 0; sy < Samples; sy++)
                {
                    for (var sx = 0; sx < Samples; sx++)
                    {
                        var c = Sample(new Vector2(x + (sx + 0.5f) * step, y + (sy + 0.5f) * step));
                        r += c.r * c.a;
                        g += c.g * c.a;
                        b += c.b * c.a;
                        a += c.a;
                    }
                }
                const float n = Samples * Samples;
                pixels[y * Size + x] = a > 0f ? new Color(r / a, g / a, b / a, a / n) : new Color(0f, 0f, 0f, 0f);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply(false, true);
        return tex;
    }

    // Colour of one sub-sample, painter order: cover, pages, outlines, spine, ink.
    private static Color Sample(Vector2 p)
    {
        var inLeft = Inside(LeftPage, p, out var dLeft);
        var inRight = Inside(RightPage, p, out var dRight);
        if (inLeft || inRight)
        {
            var d = inLeft ? dLeft : dRight;
            if (d < 1.3f)
            {
                return Dark; // page outline
            }
            if (Mathf.Abs(p.x - 32f) < 1.2f)
            {
                return Dark; // spine
            }
            var quad = inLeft ? LeftPage : RightPage;
            for (var i = 0; i < 4; i++)
            {
                // Ink lines follow the page slope: from outer edge to spine edge at height v.
                var v = 0.30f + i * 0.15f;
                var from = Vector2.Lerp(quad[0], quad[3], v);
                var to = Vector2.Lerp(quad[1], quad[2], v);
                var a = Vector2.Lerp(from, to, inLeft ? 0.14f : 0.18f);
                var bEnd = Vector2.Lerp(from, to, inLeft ? 0.82f : 0.86f);
                if (i == 3)
                {
                    bEnd = Vector2.Lerp(a, bEnd, 0.6f); // short last line, like a paragraph end
                }
                if (SegmentDistance(p, a, bEnd) < 0.9f)
                {
                    return Ink;
                }
            }
            // Slight shade near the spine: pages curve into it.
            var toSpine = Mathf.Clamp01(1f - Mathf.Abs(p.x - 32f) / 7f);
            return Color.Lerp(Page, PageShade, toSpine * 0.8f);
        }
        if (Inside(LeftCover, p, out var dCoverL) || Inside(RightCover, p, out dCoverL))
        {
            return dCoverL < 1.3f ? Dark : Cover;
        }
        return new Color(0f, 0f, 0f, 0f);
    }

    // Convex quad, counter-clockwise. Inside = left of every edge; d = distance to the nearest edge line.
    private static bool Inside(Vector2[] quad, Vector2 p, out float d)
    {
        d = float.MaxValue;
        for (var i = 0; i < quad.Length; i++)
        {
            var a = quad[i];
            var b = quad[(i + 1) % quad.Length];
            var edge = b - a;
            var len = edge.magnitude;
            if (len < 1e-4f)
            {
                continue;
            }
            var cross = (edge.x * (p.y - a.y) - edge.y * (p.x - a.x)) / len;
            if (cross < 0f)
            {
                return false;
            }
            d = Mathf.Min(d, cross);
        }
        return true;
    }

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
        return Vector2.Distance(p, a + t * ab);
    }
}
