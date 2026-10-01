using UnityEngine;

namespace MC.Exploration.SailingSkillMod;

// Me = the Sailing skill icon, drawn in code once (no asset file, like Encyclopedia's BookIcon): longship seen from
// the side, square sail with two stripes on a mast and yard, hull below. Light cream on transparent with a dark
// outline, so it read on the dark Skills panel and on the level-up message. 64x64, 4x4 supersampling for soft edges.
// Kept whole session (the skill definition always point at it): never destroyed.
internal static class SkillIcon
{
    private const int Size = 64;
    private const int Samples = 4;
    private const float Outline = 1.4f;

    private static Sprite _sprite;

    private static readonly Color Light = new Color(0.96f, 0.92f, 0.82f, 1f);
    private static readonly Color Stripe = new Color(0.78f, 0.70f, 0.56f, 1f);
    private static readonly Color Wood = new Color(0.86f, 0.78f, 0.64f, 1f);
    private static readonly Color Dark = new Color(0.22f, 0.18f, 0.14f, 1f);

    // Texture y go up. Convex quads, counter-clockwise.
    private static readonly Vector2[] Hull = { new Vector2(13f, 6f), new Vector2(51f, 6f), new Vector2(60f, 17f), new Vector2(4f, 17f) };
    private static readonly Vector2[] Sail = { new Vector2(14f, 22f), new Vector2(50f, 22f), new Vector2(53f, 53f), new Vector2(11f, 53f) };
    private static readonly Vector2[] Yard = { new Vector2(8f, 53f), new Vector2(56f, 53f), new Vector2(56f, 57f), new Vector2(8f, 57f) };
    private static readonly Vector2[] Mast = { new Vector2(30.5f, 16f), new Vector2(33.5f, 16f), new Vector2(33.5f, 61f), new Vector2(30.5f, 61f) };

    internal static Sprite Get()
    {
        if (_sprite != null)
        {
            return _sprite;
        }
        var tex = Draw();
        _sprite = Sprite.Create(tex, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        _sprite.name = "MC_Sailing_Skill";
        _sprite.hideFlags = HideFlags.HideAndDontSave;
        return _sprite;
    }

    private static Texture2D Draw()
    {
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            name = "MC_Sailing_Skill",
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

    // Colour of one sub-sample, painter order from front to back: sail, yard, hull, mast.
    private static Color Sample(Vector2 p)
    {
        if (Inside(Sail, p, out var d))
        {
            if (d < Outline)
            {
                return Dark;
            }
            // Two vertical stripes, like a Viking sail; slight shade toward the bottom (the sail bellies out).
            var u = Mathf.InverseLerp(Mathf.Lerp(14f, 11f, (p.y - 22f) / 31f), Mathf.Lerp(50f, 53f, (p.y - 22f) / 31f), p.x);
            var stripe = (u > 0.24f && u < 0.36f) || (u > 0.64f && u < 0.76f);
            var shade = Mathf.Clamp01((30f - p.y) / 10f) * 0.15f;
            return Color.Lerp(stripe ? Stripe : Light, Dark, shade);
        }
        if (Inside(Yard, p, out d))
        {
            return d < 1f ? Dark : Wood;
        }
        if (Inside(Hull, p, out d))
        {
            if (d < Outline)
            {
                return Dark;
            }
            // Shield rim line along the top of the hull.
            return Mathf.Abs(p.y - 13.5f) < 0.8f ? Dark : Wood;
        }
        if (Inside(Mast, p, out d))
        {
            return d < 0.8f ? Dark : Wood;
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
}
