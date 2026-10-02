using System.Collections.Generic;
using UnityEngine;

namespace MC.Exploration.ViewSpyglassMod;

// Me = the spyglass item icon, drawn in code once (no asset file, like Sailing Skill's SkillIcon): spyglass lying
// diagonal, eyepiece bottom left, wide lens top right. Three telescope tubes of polished bronze with darker rings,
// leather wrap with stitches on the big tube, crystal lens at the end. Shaded like a cylinder lit from the upper left,
// dark outline so it read on the dark inventory. 4x4 supersampling for soft edges. Kept whole session (the item
// always point at it): never destroyed. Package icon (icon.png) come from same drawing (Debug self test export it).
internal static class SpyglassIcon
{
    internal const int Size = 128;
    private const int Samples = 4;

    private static Sprite _sprite;

    private static readonly Color Brass = new Color(0.86f, 0.62f, 0.30f, 1f);
    private static readonly Color DarkBrass = new Color(0.58f, 0.38f, 0.17f, 1f);
    private static readonly Color Leather = new Color(0.36f, 0.21f, 0.12f, 1f);
    private static readonly Color Stitch = new Color(0.72f, 0.60f, 0.42f, 1f);
    private static readonly Color Outline = new Color(0.10f, 0.07f, 0.05f, 1f);
    private static readonly Color GlassDeep = new Color(0.10f, 0.20f, 0.30f, 1f);
    private static readonly Color GlassRim = new Color(0.55f, 0.78f, 0.90f, 1f);

    private enum Look : byte
    {
        Metal,
        DarkMetal,
        Leather,
    }

    // One piece of the tube: from t0 to t1 along the axis (0 = eyepiece, 1 = lens), half width w0 -> w1 (pixels of
    // a 128 icon).
    private struct Part
    {
        internal float T0;
        internal float T1;
        internal float W0;
        internal float W1;
        internal Look Look;
    }

    // Draw order: later parts paint over earlier ones (rings over tubes).
    private static readonly Part[] Parts =
    {
        new Part { T0 = 0.045f, T1 = 0.31f, W0 = 5.6f, W1 = 5.6f, Look = Look.Metal },     // eyepiece tube
        new Part { T0 = 0.31f, T1 = 0.60f, W0 = 7.2f, W1 = 7.2f, Look = Look.Metal },      // middle tube
        new Part { T0 = 0.60f, T1 = 0.90f, W0 = 8.8f, W1 = 8.8f, Look = Look.Metal },      // main tube
        new Part { T0 = 0.66f, T1 = 0.86f, W0 = 9.5f, W1 = 9.5f, Look = Look.Leather },    // leather wrap
        new Part { T0 = 0.0f, T1 = 0.06f, W0 = 6.8f, W1 = 6.8f, Look = Look.DarkMetal },   // eye cup
        new Part { T0 = 0.29f, T1 = 0.335f, W0 = 7.6f, W1 = 7.6f, Look = Look.DarkMetal }, // ring 1
        new Part { T0 = 0.58f, T1 = 0.625f, W0 = 9.2f, W1 = 9.2f, Look = Look.DarkMetal }, // ring 2
        new Part { T0 = 0.89f, T1 = 0.965f, W0 = 8.8f, W1 = 11.2f, Look = Look.Metal },    // flare
        new Part { T0 = 0.955f, T1 = 1.0f, W0 = 11.6f, W1 = 11.6f, Look = Look.DarkMetal },// lip
    };

    // Axis in 128-pixel space, y up.
    private static readonly Vector2 Start = new Vector2(19f, 21f);
    private static readonly Vector2 End = new Vector2(107f, 105f);
    private const float OutlinePx = 1.5f;

    internal static Sprite Get()
    {
        if (_sprite != null)
        {
            return _sprite;
        }
        var tex = Draw(Size, false, false);
        _sprite = Sprite.Create(tex, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        _sprite.name = "MC_Spyglass";
        _sprite.hideFlags = HideFlags.HideAndDontSave;
        return _sprite;
    }

    // size: pixels (drawing scaled from 128). background: dark round plate behind (package icon). readable: keep
    // pixels on CPU (PNG export).
    internal static Texture2D Draw(int size, bool background, bool readable)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "MC_Spyglass",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var scale = Size / (float)size;
        var pixels = new Color[size * size];
        const float step = 1f / Samples;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
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
                        var p = new Vector2((x + (sx + 0.5f) * step) * scale, (y + (sy + 0.5f) * step) * scale);
                        var c = Sample(p);
                        if (background)
                        {
                            c = Over(c, Plate(p));
                        }
                        r += c.r * c.a;
                        g += c.g * c.a;
                        b += c.b * c.a;
                        a += c.a;
                    }
                }
                const float n = Samples * Samples;
                pixels[y * size + x] = a > 0f ? new Color(r / a, g / a, b / a, a / n) : new Color(0f, 0f, 0f, 0f);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply(false, !readable);
        return tex;
    }

    // Colour at p (128-pixel space).
    private static Color Sample(Vector2 p)
    {
        var axis = End - Start;
        var length = axis.magnitude;
        var dir = axis / length;
        var normal = new Vector2(-dir.y, dir.x); // points up-left: the lit side
        var rel = p - Start;
        var t = Vector2.Dot(rel, dir) / length;
        var across = Vector2.Dot(rel, normal);

        var color = new Color(0f, 0f, 0f, 0f);
        foreach (var part in Parts)
        {
            var w = Mathf.Lerp(part.W0, part.W1, Mathf.InverseLerp(part.T0, part.T1, t));
            var tPad = OutlinePx / length;
            if (t < part.T0 - tPad || t > part.T1 + tPad || Mathf.Abs(across) > w + OutlinePx)
            {
                continue;
            }
            var core = t >= part.T0 && t <= part.T1 && Mathf.Abs(across) <= w;
            color = core ? Shade(part, -across / w, t) : Outline;
        }
        // Crystal lens: ellipse standing on the wide end.
        var lensAcross = 10.4f;
        var lensAlong = 3.4f;
        var along = (t - 1f) * length;
        var e = (along * along) / (lensAlong * lensAlong) + (across * across) / (lensAcross * lensAcross);
        var eOut = (along * along) / ((lensAlong + OutlinePx) * (lensAlong + OutlinePx))
                   + (across * across) / ((lensAcross + OutlinePx) * (lensAcross + OutlinePx));
        if (e <= 1f)
        {
            var rim = Mathf.Sqrt(e);
            var glass = Color.Lerp(GlassDeep, GlassRim, rim * rim);
            // Glint upper left.
            var gx = across - 4.5f;
            var gy = along + 0.8f;
            var glint = Mathf.Exp(-(gx * gx + gy * gy * 3f) / 5f);
            color = Color.Lerp(glass, Color.white, glint * 0.85f);
        }
        else if (eOut <= 1f && t > 0.97f)
        {
            color = Outline;
        }
        return color;
    }

    // s: -1 (lit edge, up left) .. 1 (shadow edge). Cylinder shading plus a metal highlight.
    private static Color Shade(Part part, float s, float t)
    {
        var lit = 0.62f + 0.55f * Mathf.Exp(-((s + 0.35f) / 0.42f) * ((s + 0.35f) / 0.42f)) - 0.42f * Mathf.Pow(Mathf.Max(0f, s), 1.4f);
        Color c;
        switch (part.Look)
        {
            case Look.Leather:
                c = Leather * (0.55f + 0.6f * lit);
                // Stitch rows across the wrap.
                var row = Mathf.Repeat((t - part.T0) / 0.04f, 1f);
                if (row < 0.16f && Mathf.Abs(s) < 0.86f)
                {
                    c = Color.Lerp(c, Stitch, 0.75f);
                }
                break;
            case Look.DarkMetal:
                c = DarkBrass * lit;
                break;
            default:
                c = Brass * lit;
                break;
        }
        if (part.Look != Look.Leather)
        {
            var spec = Mathf.Exp(-((s + 0.52f) / 0.13f) * ((s + 0.52f) / 0.13f));
            c = Color.Lerp(c, new Color(1f, 0.95f, 0.82f, 1f), spec * 0.6f);
        }
        c.a = 1f;
        return c;
    }

    // Package icon background: dark slate plate with a soft lighter centre.
    private static Color Plate(Vector2 p)
    {
        var d = (p - new Vector2(64f, 64f)).magnitude / 64f;
        var c = Color.Lerp(new Color(0.20f, 0.24f, 0.28f, 1f), new Color(0.08f, 0.09f, 0.11f, 1f), Mathf.Clamp01(d));
        return c;
    }

    // a over b (straight alpha).
    private static Color Over(Color a, Color b)
    {
        var outA = a.a + b.a * (1f - a.a);
        if (outA <= 0f)
        {
            return new Color(0f, 0f, 0f, 0f);
        }
        var rgb = (new Vector3(a.r, a.g, a.b) * a.a + new Vector3(b.r, b.g, b.b) * b.a * (1f - a.a)) / outA;
        return new Color(rgb.x, rgb.y, rgb.z, outA);
    }

#if DEBUG
    // Debug export: the PNG files the package and docs use (self test write them; never in release).
    internal static List<KeyValuePair<string, byte[]>> ExportPngs()
    {
        var list = new List<KeyValuePair<string, byte[]>>();
        var icon = Draw(256, true, true);
        list.Add(new KeyValuePair<string, byte[]>("icon-256.png", icon.EncodeToPNG()));
        Object.Destroy(icon);
        var item = Draw(Size, false, true);
        list.Add(new KeyValuePair<string, byte[]>("item-128.png", item.EncodeToPNG()));
        Object.Destroy(item);
        return list;
    }
#endif
}
