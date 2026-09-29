using UnityEngine;

namespace MC.Exploration.CompendiumEncyclopediaMod;

// Me = the generic creature icon (decision 15): a paw print drawn in code once (no asset file), for a discovered
// creature whose trophy is not discovered (or that has no trophy), in the list row and the detail head. Neutral on
// purpose: the first pick (the map's red "!" event pin) read like a warning. Light greys only, like BookIcon, so any
// image colour or tint still works. 64x64, transparent, 4x4 supersampling. Shapes: one main pad (wide rounded oval)
// and four toe ovals in an arc above it, the outer two tilted outward. Destroyed on feature off (texture and sprite
// are ours).
internal static class PawIcon
{
    private const int Size = 64;
    private const int Samples = 4;
    private const float Outline = 1.6f;

    private static Texture2D _texture;
    private static Sprite _sprite;

    private static readonly Color Fill = new Color(0.93f, 0.93f, 0.93f, 1f);
    private static readonly Color FillShade = new Color(0.80f, 0.80f, 0.80f, 1f);
    private static readonly Color Edge = new Color(0.50f, 0.50f, 0.50f, 1f);

    // Ovals: centre x, centre y (texture y go up), radius x, radius y, tilt in degrees (counter-clockwise).
    private static readonly Vector4[] Shapes =
    {
        new Vector4(32f, 20f, 15.5f, 12.5f), // main pad
        new Vector4(11.5f, 36f, 6.2f, 8.2f), // outer left toe
        new Vector4(24.5f, 48f, 6.6f, 8.8f), // inner left toe
        new Vector4(39.5f, 48f, 6.6f, 8.8f), // inner right toe
        new Vector4(52.5f, 36f, 6.2f, 8.2f), // outer right toe
    };

    private static readonly float[] Tilts = { 0f, 28f, 8f, -8f, -28f };

    internal static Sprite Get()
    {
        if (_sprite != null)
        {
            return _sprite;
        }
        _texture = Draw();
        _sprite = Sprite.Create(_texture, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        _sprite.name = "MC_Compendium_Paw";
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
            name = "MC_Compendium_Paw",
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
                // Premultiplied sum of the sub-samples, then back to straight alpha (same as BookIcon).
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

    // Colour of one sub-sample: outline near the edge of a shape, fill inside (a little darker toward the bottom of
    // each shape, so the pads read as rounded), nothing outside.
    private static Color Sample(Vector2 p)
    {
        for (var i = 0; i < Shapes.Length; i++)
        {
            var s = Shapes[i];
            var d = OvalDistance(p, s, Tilts[i], out var v);
            if (d > 0f)
            {
                continue;
            }
            if (d > -Outline)
            {
                return Edge;
            }
            return Color.Lerp(Fill, FillShade, Mathf.Clamp01(-v) * 0.7f);
        }
        return new Color(0f, 0f, 0f, 0f);
    }

    // Approximate signed distance to a tilted oval (negative inside), in pixels. v = local height in -1..1.
    private static float OvalDistance(Vector2 p, Vector4 s, float tiltDeg, out float v)
    {
        var rad = tiltDeg * Mathf.Deg2Rad;
        var cos = Mathf.Cos(rad);
        var sin = Mathf.Sin(rad);
        var dx = p.x - s.x;
        var dy = p.y - s.y;
        var lx = dx * cos + dy * sin;
        var ly = -dx * sin + dy * cos;
        var nx = lx / s.z;
        var ny = ly / s.w;
        v = ny;
        var k = Mathf.Sqrt(nx * nx + ny * ny);
        return (k - 1f) * Mathf.Min(s.z, s.w);
    }
}
