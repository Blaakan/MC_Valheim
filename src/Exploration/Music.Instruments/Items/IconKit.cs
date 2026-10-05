using UnityEngine;

namespace MC.Exploration.MusicInstrumentsMod;

// Me = little paint box for icons made from math. Shapes as distance (negative inside, positive outside, in pixel),
// noise, light, colour stacking, and the supersampled render into a pixel array. No texture, no Unity object, no
// native call: only Color/Vector2/Vector3/Mathf math, so same code also run outside the game (offline icon preview).
// Space: 128-pixel design space, y up (texture row 0 = bottom). Main thread or not, me no care: no shared state.
// Colour = sRGB byte like PNG. Valheim draw UI in linear space: half-see-through soft stuff (glow, shadow) look
// brighter and wider in game than in PNG viewer, so keep it weak.
internal static class IconKit
{
    internal const float Design = 128f;
    internal const int Samples = 4;

    // Colour of one point (straight alpha). Render call it Samples x Samples times per pixel.
    internal delegate Color Sampler(Vector2 p);

    // Distance field of one shape (for light from its slope).
    internal delegate float Field(Vector2 q);

    internal static readonly Color Clear = new Color(0f, 0f, 0f, 0f);

    // Light come from upper left, a bit toward viewer (z = toward viewer). Same light flat on the picture.
    internal static readonly Vector3 Light = new Vector3(-0.52f, 0.60f, 0.61f).normalized;
    internal static readonly Vector2 Light2D = new Vector2(-0.7071f, 0.7071f);

    // ---------- render ----------

    // Me paint picture size x size, row 0 = bottom, straight alpha, colour clamped 0..1. Design space scaled to size.
    // Me look at pixel corners + centre first: all near same colour = smooth spot, average of those 5 is enough.
    // Else (edge, line, detail) sampler asked Samples x Samples times in the pixel, me average (premultiplied).
    // 5-point check never miss a line 0.75 px or wider (pattern gap < 0.71 px): keep details at least that wide.
    internal static Color[] Render(Sampler sampler, int size)
    {
        var pixels = new Color[size * size];
        var scale = Design / size;
        var n1 = size + 1;
        var corners = new Color[n1 * n1];
        for (var y = 0; y < n1; y++)
        {
            for (var x = 0; x < n1; x++)
            {
                corners[y * n1 + x] = sampler(new Vector2(x * scale, y * scale));
            }
        }
        const float step = 1f / Samples;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var c00 = corners[y * n1 + x];
                var c10 = corners[y * n1 + x + 1];
                var c01 = corners[(y + 1) * n1 + x];
                var c11 = corners[(y + 1) * n1 + x + 1];
                var cc = sampler(new Vector2((x + 0.5f) * scale, (y + 0.5f) * scale));
                var acc = new Accum();
                if (Near(c00, cc) && Near(c10, cc) && Near(c01, cc) && Near(c11, cc))
                {
                    acc.Add(c00);
                    acc.Add(c10);
                    acc.Add(c01);
                    acc.Add(c11);
                    acc.Add(cc);
                    acc.Count = 5;
                }
                else
                {
                    for (var sy = 0; sy < Samples; sy++)
                    {
                        for (var sx = 0; sx < Samples; sx++)
                        {
                            acc.Add(sampler(new Vector2((x + (sx + 0.5f) * step) * scale, (y + (sy + 0.5f) * step) * scale)));
                        }
                    }
                    acc.Count = Samples * Samples;
                }
                pixels[y * size + x] = acc.Result();
            }
        }
        BleedEdges(pixels, size);
        return pixels;
    }

    private static bool Near(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) < 0.03f && Mathf.Abs(a.g - b.g) < 0.03f && Mathf.Abs(a.b - b.b) < 0.03f
        && Mathf.Abs(a.a - b.a) < 0.03f;

    // Me add samples premultiplied, then give straight alpha back.
    private struct Accum
    {
        private float _r;
        private float _g;
        private float _b;
        private float _a;
        internal int Count;

        internal void Add(Color c)
        {
            if (c.a <= 0f)
            {
                return;
            }
            var ca = Mathf.Min(c.a, 1f);
            _r += Mathf.Clamp01(c.r) * ca;
            _g += Mathf.Clamp01(c.g) * ca;
            _b += Mathf.Clamp01(c.b) * ca;
            _a += ca;
        }

        internal Color Result() => _a > 0f ? new Color(_r / _a, _g / _a, _b / _a, _a / Count) : Clear;
    }

    // See-through pixels get colour of next seen pixel (alpha stay 0). Else filter and mipmaps mix black from empty
    // pixels into soft edges and glow = dark fringe. Me do few rounds, enough for the soft edge.
    internal static void BleedEdges(Color[] px, int size, int rounds = 4)
    {
        var done = new bool[px.Length];
        for (var i = 0; i < px.Length; i++)
        {
            done[i] = px[i].a > 0f;
        }
        var next = new bool[px.Length];
        for (var round = 0; round < rounds; round++)
        {
            var any = false;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var i = y * size + x;
                    next[i] = done[i];
                    if (done[i])
                    {
                        continue;
                    }
                    var r = 0f;
                    var g = 0f;
                    var b = 0f;
                    var count = 0;
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        var yy = y + dy;
                        if (yy < 0 || yy >= size)
                        {
                            continue;
                        }
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            var xx = x + dx;
                            if (xx < 0 || xx >= size || !done[yy * size + xx])
                            {
                                continue;
                            }
                            var c = px[yy * size + xx];
                            r += c.r;
                            g += c.g;
                            b += c.b;
                            count++;
                        }
                    }
                    if (count > 0)
                    {
                        px[i] = new Color(r / count, g / count, b / count, 0f);
                        next[i] = true;
                        any = true;
                    }
                }
            }
            var swap = done;
            done = next;
            next = swap;
            if (!any)
            {
                break;
            }
        }
    }

    // ---------- shape distance (negative inside, positive outside) ----------

    internal static float Circle(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;

    // How far p is inside the picture from its nearest border (design pixel; 0 on the border, positive inside: other
    // sign than the shapes here). Soft stuff (glow) fade with it so texture edge stay empty.
    internal static float BorderDistance(Vector2 p) =>
        Mathf.Min(Mathf.Min(p.x, p.y), Mathf.Min(Design - p.x, Design - p.y));

    // Axis-aligned ellipse around 0. Close guess (exact on axes), good enough for 1 px edges.
    internal static float Ellipse(Vector2 q, float rx, float ry)
    {
        var k0 = Mathf.Sqrt(q.x * q.x / (rx * rx) + q.y * q.y / (ry * ry));
        if (k0 < 1e-5f)
        {
            return -Mathf.Min(rx, ry);
        }
        var k1 = Mathf.Sqrt(q.x * q.x / (rx * rx * rx * rx) + q.y * q.y / (ry * ry * ry * ry));
        return k0 * (k0 - 1f) / k1;
    }

    // Distance to line piece a-b (never negative). Capsule = this minus radius.
    internal static float Segment(Vector2 p, Vector2 a, Vector2 b)
    {
        var e = b - a;
        var w = p - a;
        var len2 = e.x * e.x + e.y * e.y;
        var t = len2 > 0f ? Mathf.Clamp01((w.x * e.x + w.y * e.y) / len2) : 0f;
        var dx = w.x - e.x * t;
        var dy = w.y - e.y * t;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    // Axis-aligned box around 0, half size h.
    internal static float Box(Vector2 q, Vector2 h)
    {
        var dx = Mathf.Abs(q.x) - h.x;
        var dy = Mathf.Abs(q.y) - h.y;
        var ox = Mathf.Max(dx, 0f);
        var oy = Mathf.Max(dy, 0f);
        return Mathf.Min(Mathf.Max(dx, dy), 0f) + Mathf.Sqrt(ox * ox + oy * oy);
    }

    // Box around 0 with round corners: top corners radius rTop, bottom corners rBottom (each <= half size).
    internal static float RoundBox(Vector2 q, Vector2 h, float rTop, float rBottom)
    {
        var r = q.y > 0f ? rTop : rBottom;
        var dx = Mathf.Abs(q.x) - h.x + r;
        var dy = Mathf.Abs(q.y) - h.y + r;
        var ox = Mathf.Max(dx, 0f);
        var oy = Mathf.Max(dy, 0f);
        return Mathf.Min(Mathf.Max(dx, dy), 0f) + Mathf.Sqrt(ox * ox + oy * oy) - r;
    }

    // Four-corner shape (any order around), sign by crossing test.
    internal static float Quad(Vector2 p, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        var inside = false;
        var best = float.MaxValue;
        Edge(p, d, a, ref inside, ref best);
        Edge(p, a, b, ref inside, ref best);
        Edge(p, b, c, ref inside, ref best);
        Edge(p, c, d, ref inside, ref best);
        var dist = Mathf.Sqrt(best);
        return inside ? -dist : dist;
    }

    private static void Edge(Vector2 p, Vector2 a, Vector2 b, ref bool inside, ref float best)
    {
        if ((b.y > p.y) != (a.y > p.y) && p.x < (a.x - b.x) * (p.y - b.y) / (a.y - b.y) + b.x)
        {
            inside = !inside;
        }
        var s = Segment(p, a, b);
        if (s * s < best)
        {
            best = s * s;
        }
    }

    // Two shapes joined, round fillet of size k where they meet.
    internal static float SmoothMin(float a, float b, float k)
    {
        var h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
        return Mathf.Lerp(b, a, h) - k * h * (1f - h);
    }

    // Super-ellipse value: <= 1 inside. Bigger n = more square.
    internal static float SuperEllipse(float x, float y, float n) =>
        Mathf.Pow(Mathf.Abs(x), n) + Mathf.Pow(Mathf.Abs(y), n);

    // Way the field climb at q (unit, point out of the shape). fq = value at q, caller know it. Flat = zero.
    internal static Vector2 Gradient(Field f, Vector2 q, float fq, float eps)
    {
        var gx = f(new Vector2(q.x + eps, q.y)) - fq;
        var gy = f(new Vector2(q.x, q.y + eps)) - fq;
        var len = Mathf.Sqrt(gx * gx + gy * gy);
        return len > 1e-6f ? new Vector2(gx / len, gy / len) : Vector2.zero;
    }

    // Fixed turn (counter-clockwise), cos/sin made once. Apply = turn by +deg, Undo = turn by -deg.
    internal readonly struct Turn
    {
        internal readonly float Cos;
        internal readonly float Sin;

        internal Turn(float deg)
        {
            var a = deg * Mathf.Deg2Rad;
            Cos = Mathf.Cos(a);
            Sin = Mathf.Sin(a);
        }

        internal Vector2 Apply(Vector2 v) => new Vector2(v.x * Cos - v.y * Sin, v.x * Sin + v.y * Cos);

        internal Vector2 Undo(Vector2 v) => new Vector2(v.x * Cos + v.y * Sin, v.y * Cos - v.x * Sin);
    }

    // ---------- noise ----------

    // 0..1 from two ints, same every run.
    internal static float Hash(int x, int y)
    {
        unchecked
        {
            var h = (uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            h *= 0x297A2D39u;
            h ^= h >> 15;
            return (h & 0xFFFFFFu) / 16777215f;
        }
    }

    // Smooth value noise 0..1, one bump per unit.
    internal static float Noise(float x, float y)
    {
        var ix = Floor(x);
        var iy = Floor(y);
        var fx = x - ix;
        var fy = y - iy;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        var a = Hash(ix, iy);
        var b = Hash(ix + 1, iy);
        var c = Hash(ix, iy + 1);
        var d = Hash(ix + 1, iy + 1);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    // Me floor quick (Mathf.FloorToInt go through double; noise call me lots).
    private static int Floor(float v)
    {
        var i = (int)v;
        return v < i ? i - 1 : i;
    }

    // Two layers of noise (big + small), 0..1.
    internal static float Noise2(float x, float y) => Noise(x, y) * 0.65f + Noise(x * 2.3f + 17.1f, y * 2.3f + 5.3f) * 0.35f;

    // ---------- light ----------

    // Hermite step 0..1 between e0 and e1.
    internal static float SmoothStep(float e0, float e1, float x)
    {
        var t = Mathf.Clamp01((x - e0) / (e1 - e0));
        return t * t * (3f - 2f * t);
    }

    // Round tube across: s -1 (lit edge, up left) .. 1 (shadow edge). About 0.3 .. 1.1.
    internal static float Tube(float s)
    {
        var k = (s + 0.3f) / 0.5f;
        return 0.68f + 0.42f * Mathf.Exp(-k * k) - 0.38f * Mathf.Pow(Mathf.Max(0f, s), 1.3f);
    }

    // Edge that rounds off: outward = unit way out of the shape on the picture, tilt 0 (face look at viewer) .. 1
    // (steepest, maxTilt radians). Me give diffuse 0..1 and shine 0..1.
    internal static float Bevel(Vector2 outward, float tilt, float maxTilt, out float spec)
    {
        var a = Mathf.Clamp01(tilt) * maxTilt;
        var sin = Mathf.Sin(a);
        return Lit(new Vector3(outward.x * sin, outward.y * sin, Mathf.Cos(a)), out spec);
    }

    // Dome (half ball): r = point / radius (|r| <= 1), already turned to picture way. height 0..1 = how round.
    internal static float Dome(Vector2 r, float height, out float spec)
    {
        var r2 = Mathf.Min(1f, r.x * r.x + r.y * r.y);
        var z = Mathf.Sqrt(1f - r2) * height + (1f - height);
        var n = new Vector3(r.x * height, r.y * height, z);
        return Lit(n.normalized, out spec);
    }

    // Light on surface with normal n (unit). Shine = light bounced toward viewer.
    internal static float Lit(Vector3 n, out float spec)
    {
        var ndl = n.x * Light.x + n.y * Light.y + n.z * Light.z;
        var rz = 2f * ndl * n.z - Light.z;
        // rz^24 by squaring (Pow is slow, called a lot).
        var r2 = rz * rz;
        var r8 = r2 * r2 * r2 * r2;
        spec = rz > 0f ? r8 * r8 * r8 : 0f;
        return Mathf.Max(0f, ndl);
    }

    // ---------- colour ----------

    // above over below, both straight alpha.
    internal static Color Over(Color below, Color above)
    {
        if (above.a >= 1f)
        {
            return above;
        }
        if (above.a <= 0f)
        {
            return below;
        }
        var keep = below.a * (1f - above.a);
        var outA = above.a + keep;
        if (outA <= 0f)
        {
            return Clear;
        }
        return new Color((above.r * above.a + below.r * keep) / outA, (above.g * above.a + below.g * keep) / outA,
            (above.b * above.a + below.b * keep) / outA, outA);
    }

    // Colour times k, alpha kept (Unity's Color * float also scale alpha).
    internal static Color Mul(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

    internal static Color Opaque(Color c) => new Color(c.r, c.g, c.b, 1f);

    internal static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

    // Three-colour ramp: t 0 = dark, 0.5 = mid, 1 = light.
    internal static Color Ramp(Color dark, Color mid, Color light, float t)
    {
        t = Mathf.Clamp01(t);
        return t < 0.5f ? Color.Lerp(dark, mid, t * 2f) : Color.Lerp(mid, light, t * 2f - 1f);
    }
}
