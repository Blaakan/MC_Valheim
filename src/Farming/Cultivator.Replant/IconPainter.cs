using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MC.Farming.CultivatorReplantMod;

// Me paint on copy of game icon. Game icon live in big packed atlas (BC7, not readable), so me: copy icon square on
// GPU (Blit) -> read it back (ReadPixels) -> caller paint on CPU -> make new sprite. Me never touch game texture.
// Me = Forge Idol Upgrades' StarIcons.Compose made general: caller give cache key and paint callback.
// Every made sprite kept whole session (never destroyed: item prefabs, pieces and messages waiting in game queues
// hold them; a destroyed sprite there show as white square). Main thread only (Blit, ReadPixels, Sprite.Create are
// main thread stuff). No graphics device (dedicated server) = null, caller keep plain icon.
internal static class IconPainter
{
    // Me no make giant texture by mistake. Item icon = 64x64; 1024 = lots of room.
    private const int MaxSide = 1024;
    // Copy came back empty (window minimized, device busy): me try same key again after this.
    private const float RetrySeconds = 1f;

    // Colour of one point of a layer (straight alpha), x/y in pixel, y up. PaintSampled call it many time per pixel.
    internal delegate Color PixelSampler(float x, float y);

    private sealed class Entry
    {
        public Sprite Source;  // game sprite me copy from
        public Sprite Made;    // me own sprite; null while not made
        public bool Failed;    // me could not make it: null back, no retry
        public float RetryAt;  // > 0 = copy came back empty: null until this time, then try again
    }

    private sealed class EmptyCopyException : Exception
    {
    }

    private static readonly Dictionary<string, Entry> Cache = new Dictionary<string, Entry>(StringComparer.Ordinal);
    // Ids of sprites me made (tests: "is this icon painted?").
    private static readonly HashSet<int> MadeIds = new HashSet<int>();
    // One log line per key, never more.
    private static readonly HashSet<string> Logged = new HashSet<string>(StringComparer.Ordinal);
    private static bool _loggedNoDevice;

    // True while paint callback run on linear-data copy in Linear space: helpers turn screen colour into linear.
    private static bool _toLinear;

    // Scratch corners for FillDiamond (main thread only, so one static set is enough).
    private static readonly float[] DiamondX = new float[4];
    private static readonly float[] DiamondY = new float[4];

    internal static bool HasGraphics => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

    // Me give painted copy of source: paint get pixel array (row 0 = bottom) + width + height of sprite rect.
    // Same key = same sprite whole session (key must name source and look, e.g. "tier5.<sprite id>").
    // Null = no graphics device, bad sprite, failure (one log line per key) or empty GPU copy (try again in 1 s).
    // Me never throw.
    internal static Sprite Compose(Sprite source, string key, Action<Color32[], int, int> paint)
    {
        try
        {
            // Unity null check (== null), not ?. : dead sprite count as null.
            if (source == null || string.IsNullOrEmpty(key) || paint == null)
            {
                return null;
            }
            if (Cache.TryGetValue(key, out var entry) && entry.Source == source)
            {
                if (entry.Made != null)
                {
                    return entry.Made;
                }
                if (entry.Failed)
                {
                    return null;
                }
                if (Time.realtimeSinceStartup < entry.RetryAt)
                {
                    return null;
                }
                // Retry time over, or somebody kill me sprite: me make again below.
            }
            return Make(source, key, paint);
        }
        catch (Exception e)
        {
            LogFailure(key, source, e);
            return null;
        }
    }

    // Me tell if sprite is one me made (self tests).
    internal static bool IsPainted(Sprite sprite) => sprite != null && MadeIds.Contains(sprite.GetInstanceID());

    // Me give CPU copy of sprite pixels (row 0 = bottom), painted by paint when given, as sRGB bytes (ready for PNG).
    // No cache, no texture kept. For export and tests. Null on trouble (one warning). Main thread only.
    internal static Color32[] CopyPixels(Sprite source, Action<Color32[], int, int> paint, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (source == null)
        {
            return null;
        }
        if (!HasGraphics)
        {
            LogNoDevice();
            return null;
        }
        Texture2D tex = null;
        try
        {
            tex = Copy(source, source.name + "_mccopy", out var px, out var linearData);
            width = tex.width;
            height = tex.height;
            if (paint != null)
            {
                RunPaint(px, width, height, linearData, paint);
            }
            if (linearData && QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                // Linear-data bytes look too dark in PNG viewer: me turn them into screen colour.
                ToGamma(px);
            }
            return px;
        }
        catch (Exception e)
        {
            Log.Warning($"Could not copy the icon '{source.name}' ({e.GetType().Name}: {e.Message}).");
            width = 0;
            height = 0;
            return null;
        }
        finally
        {
            if (tex != null)
            {
                Object.Destroy(tex);
            }
        }
    }

    // Me scale pixel array to new size, smooth (Catmull-Rom bicubic on premultiplied colour: no dark fringe at
    // see-through edge). Package icon only (64 px icon -> 256 px), so speed no matter.
    internal static Color32[] Resample(Color32[] src, int sw, int sh, int dw, int dh)
    {
        var dst = new Color32[dw * dh];
        var wx = new float[4];
        var wy = new float[4];
        var stepX = sw / (float)dw;
        var stepY = sh / (float)dh;
        for (var y = 0; y < dh; y++)
        {
            var fy = (y + 0.5f) * stepY - 0.5f;
            var iy = Mathf.FloorToInt(fy);
            CubicWeights(fy - iy, wy);
            for (var x = 0; x < dw; x++)
            {
                var fx = (x + 0.5f) * stepX - 0.5f;
                var ix = Mathf.FloorToInt(fx);
                CubicWeights(fx - ix, wx);
                float r = 0f, g = 0f, b = 0f, a = 0f;
                for (var j = 0; j < 4; j++)
                {
                    var row = Mathf.Clamp(iy - 1 + j, 0, sh - 1) * sw;
                    for (var i = 0; i < 4; i++)
                    {
                        var c = src[row + Mathf.Clamp(ix - 1 + i, 0, sw - 1)];
                        var k = wx[i] * wy[j] * c.a;
                        r += c.r * k;
                        g += c.g * k;
                        b += c.b * k;
                        a += k;
                    }
                }
                // a = alpha in 0..255. Almost nothing = see-through (no wild colour from tiny divide).
                if (a < 1f)
                {
                    continue;
                }
                dst[y * dw + x] = new Color32(Byte255(r / a), Byte255(g / a), Byte255(b / a), Byte255(a));
            }
        }
        return dst;
    }

    // ---------- shape distance: negative inside, positive outside, in pixel ----------

    internal static float CircleDistance(float x, float y, float cx, float cy, float radius)
    {
        var dx = x - cx;
        var dy = y - cy;
        return Mathf.Sqrt(dx * dx + dy * dy) - radius;
    }

    // Me = close guess of ellipse distance (exact on the axes, good enough for 1 px soft edge). angle in degree,
    // turn of rx axis from x axis, counter-clockwise.
    internal static float EllipseDistance(float x, float y, float cx, float cy, float rx, float ry, float angleDeg)
    {
        var px = x - cx;
        var py = y - cy;
        if (angleDeg != 0f)
        {
            var a = -angleDeg * Mathf.Deg2Rad;
            var cos = Mathf.Cos(a);
            var sin = Mathf.Sin(a);
            var tx = px * cos - py * sin;
            py = px * sin + py * cos;
            px = tx;
        }
        if (rx <= 0f || ry <= 0f)
        {
            return Mathf.Sqrt(px * px + py * py);
        }
        var k0 = Mathf.Sqrt(px * px / (rx * rx) + py * py / (ry * ry));
        if (k0 < 1e-5f)
        {
            return -Mathf.Min(rx, ry);
        }
        var k1 = Mathf.Sqrt(px * px / (rx * rx * rx * rx) + py * py / (ry * ry * ry * ry));
        return k0 * (k0 - 1f) / k1;
    }

    // Me = pointed leaf (two circle arcs) from a to b, fattest in middle = width.
    internal static float LeafDistance(float x, float y, float ax, float ay, float bx, float by, float width)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var len = Mathf.Sqrt(dx * dx + dy * dy);
        if (len < 1e-4f || width <= 0f)
        {
            return CircleDistance(x, y, ax, ay, 0f);
        }
        var half = len * 0.5f;
        // Me no fatter than round.
        var hw = Mathf.Min(width * 0.5f, half);
        var radius = (half * half + hw * hw) / (2f * hw);
        var off = radius - hw;
        var nx = -dy / len;
        var ny = dx / len;
        var mx = (ax + bx) * 0.5f;
        var my = (ay + by) * 0.5f;
        var d1 = CircleDistance(x, y, mx + nx * off, my + ny * off, radius);
        var d2 = CircleDistance(x, y, mx - nx * off, my - ny * off, radius);
        return Mathf.Max(d1, d2);
    }

    // Distance to line piece a-b (never negative). Capsule = this minus radius.
    internal static float SegmentDistance(float x, float y, float ax, float ay, float bx, float by)
    {
        float ex = bx - ax, ey = by - ay, wx = x - ax, wy = y - ay;
        var len2 = ex * ex + ey * ey;
        var t = len2 > 0f ? Mathf.Clamp01((wx * ex + wy * ey) / len2) : 0f;
        float dx = wx - ex * t, dy = wy - ey * t;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    // Distance to polygon edge (count corners), sign by even-odd crossing test. Any polygon, also not convex.
    internal static float PolygonDistance(float x, float y, float[] xs, float[] ys, int count)
    {
        var inside = false;
        var best = float.MaxValue;
        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            float ax = xs[j], ay = ys[j], bx = xs[i], by = ys[i];
            if ((by > y) != (ay > y) && x < (ax - bx) * (y - by) / (ay - by) + bx)
            {
                inside = !inside;
            }
            float ex = bx - ax, ey = by - ay, wx = x - ax, wy = y - ay;
            var len2 = ex * ex + ey * ey;
            var t = len2 > 0f ? Mathf.Clamp01((wx * ex + wy * ey) / len2) : 0f;
            float dx = wx - ex * t, dy = wy - ey * t;
            var d2 = dx * dx + dy * dy;
            if (d2 < best)
            {
                best = d2;
            }
        }
        var dist = Mathf.Sqrt(best);
        return inside ? -dist : dist;
    }

    // ---------- fill helpers: 1 px soft edge from distance, outline (if width > 0) all outside the shape ----------

    internal static void FillCircle(Color32[] px, int w, int h, float cx, float cy, float radius, Color fill,
        Color outline, float outlineWidth)
    {
        FillShape(px, w, h, cx - radius, cy - radius, cx + radius, cy + radius,
            (x, y) => CircleDistance(x, y, cx, cy, radius), fill, outline, outlineWidth);
    }

    internal static void FillEllipse(Color32[] px, int w, int h, float cx, float cy, float rx, float ry, float angleDeg,
        Color fill, Color outline, float outlineWidth)
    {
        var r = Mathf.Max(rx, ry);
        FillShape(px, w, h, cx - r, cy - r, cx + r, cy + r,
            (x, y) => EllipseDistance(x, y, cx, cy, rx, ry, angleDeg), fill, outline, outlineWidth);
    }

    internal static void FillPolygon(Color32[] px, int w, int h, float[] xs, float[] ys, int count, Color fill,
        Color outline, float outlineWidth)
    {
        if (xs == null || ys == null || count < 3 || xs.Length < count || ys.Length < count)
        {
            return;
        }
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (var i = 0; i < count; i++)
        {
            x0 = Mathf.Min(x0, xs[i]);
            x1 = Mathf.Max(x1, xs[i]);
            y0 = Mathf.Min(y0, ys[i]);
            y1 = Mathf.Max(y1, ys[i]);
        }
        FillShape(px, w, h, x0, y0, x1, y1, (x, y) => PolygonDistance(x, y, xs, ys, count), fill, outline,
            outlineWidth);
    }

    // Diamond = 4 corner: top, right, bottom, left.
    internal static void FillDiamond(Color32[] px, int w, int h, float cx, float cy, float halfWidth, float halfHeight,
        Color fill, Color outline, float outlineWidth)
    {
        DiamondX[0] = cx;
        DiamondY[0] = cy + halfHeight;
        DiamondX[1] = cx + halfWidth;
        DiamondY[1] = cy;
        DiamondX[2] = cx;
        DiamondY[2] = cy - halfHeight;
        DiamondX[3] = cx - halfWidth;
        DiamondY[3] = cy;
        FillPolygon(px, w, h, DiamondX, DiamondY, 4, fill, outline, outlineWidth);
    }

    // Me paint layer over pixels in box x0..x1, y0..y1 (pixel, y up): sampler asked samples x samples time per
    // pixel, me average (premultiplied) = soft edge everywhere, also between colour areas inside the layer.
    internal static void PaintSampled(Color32[] px, int w, int h, float x0, float y0, float x1, float y1,
        PixelSampler sampler, int samples = 4)
    {
        if (px == null || sampler == null)
        {
            return;
        }
        if (samples < 1)
        {
            samples = 1;
        }
        var ix0 = Math.Max(0, Mathf.FloorToInt(x0));
        var ix1 = Math.Min(w - 1, Mathf.CeilToInt(x1));
        var iy0 = Math.Max(0, Mathf.FloorToInt(y0));
        var iy1 = Math.Min(h - 1, Mathf.CeilToInt(y1));
        var step = 1f / samples;
        float n = samples * samples;
        for (var y = iy0; y <= iy1; y++)
        {
            for (var x = ix0; x <= ix1; x++)
            {
                float r = 0f, g = 0f, b = 0f, a = 0f;
                for (var sy = 0; sy < samples; sy++)
                {
                    for (var sx = 0; sx < samples; sx++)
                    {
                        var c = sampler(x + (sx + 0.5f) * step, y + (sy + 0.5f) * step);
                        if (c.a <= 0f)
                        {
                            continue;
                        }
                        r += c.r * c.a;
                        g += c.g * c.a;
                        b += c.b * c.a;
                        a += c.a;
                    }
                }
                if (a <= 0f)
                {
                    continue;
                }
                var idx = y * w + x;
                var dst = px[idx];
                Blend(ref dst, new Color(r / a, g / a, b / a, 1f), a / n);
                px[idx] = dst;
            }
        }
    }

    // Me put colour (screen colour, straight alpha) on top of pixel with strength a (0..1, times colour alpha).
    internal static void Blend(ref Color32 dst, Color src, float a)
    {
        a *= src.a;
        if (a <= 0f)
        {
            return;
        }
        if (a > 1f)
        {
            a = 1f;
        }
        if (_toLinear)
        {
            src = src.linear;
        }
        const float inv255 = 1f / 255f;
        var keep = dst.a * inv255 * (1f - a);
        var outA = a + keep;
        if (outA <= 0f)
        {
            return;
        }
        var norm = 1f / outA;
        dst.r = ToByte((src.r * a + dst.r * inv255 * keep) * norm);
        dst.g = ToByte((src.g * a + dst.g * inv255 * keep) * norm);
        dst.b = ToByte((src.b * a + dst.b * inv255 * keep) * norm);
        dst.a = ToByte(outA);
    }

    // above over below, both straight alpha (for samplers that stack layers).
    internal static Color Over(Color below, Color above)
    {
        var outA = above.a + below.a * (1f - above.a);
        if (outA <= 0f)
        {
            return new Color(0f, 0f, 0f, 0f);
        }
        var keep = below.a * (1f - above.a);
        return new Color((above.r * above.a + below.r * keep) / outA, (above.g * above.a + below.g * keep) / outA,
            (above.b * above.a + below.b * keep) / outA, outA);
    }

    // ---------- inside ----------

    private static Sprite Make(Sprite source, string key, Action<Color32[], int, int> paint)
    {
        var entry = new Entry { Source = source };
        Cache[key] = entry;
        if (!HasGraphics)
        {
            entry.Failed = true;
            LogNoDevice();
            return null;
        }

        Texture2D tex = null;
        try
        {
            tex = Copy(source, source.name + "_mc_" + key, out var px, out var linearData);
            var w = tex.width;
            var h = tex.height;
            RunPaint(px, w, h, linearData, paint);
            tex.SetPixels32(px);
            // Upload to GPU, drop CPU copy (save memory): texture live only on GPU like game icon.
            tex.Apply(false, true);
            var rect = source.rect;
            // Sprite.Create want pivot as 0..1 of rect; sprite.pivot is in pixel. Me divide, so pivot stay same.
            var pivot = new Vector2(source.pivot.x / rect.width, source.pivot.y / rect.height);
            // FullRect: no tight mesh (need readable texture, cost time). Last false: no physics shape, not needed.
            var made = Sprite.Create(tex, new Rect(0f, 0f, w, h), pivot, source.pixelsPerUnit, 0u,
                SpriteMeshType.FullRect, source.border, false);
            if (made == null)
            {
                throw new InvalidOperationException("Sprite.Create returned nothing");
            }
            made.name = source.name + "_mc_" + key;
            // HideAndDontSave = DontUnloadUnusedAsset too: Resources.UnloadUnusedAssets (scene change) no kill it.
            made.hideFlags = HideFlags.HideAndDontSave;
            entry.Made = made;
            MadeIds.Add(made.GetInstanceID());
            return made;
        }
        catch (EmptyCopyException)
        {
            if (tex != null)
            {
                Object.Destroy(tex);
            }
            entry.RetryAt = Time.realtimeSinceStartup + RetrySeconds;
            if (Logged.Add("empty:" + key))
            {
                Log.Debug($"Icon '{key}' came back empty (game window minimized?): plain icon for now, trying again later.");
            }
            return null;
        }
        catch (Exception e)
        {
            if (tex != null)
            {
                Object.Destroy(tex);
            }
            entry.Failed = true;
            LogFailure(key, source, e);
            return null;
        }
    }

    // Me copy sprite rect out of atlas into new CPU-side texture (not yet uploaded) + its pixels. Throw on trouble,
    // EmptyCopyException when GPU copy came back see-through. Caller own texture (must Destroy it).
    private static Texture2D Copy(Sprite source, string name, out Color32[] pixels, out bool linearData)
    {
        var atlas = source.texture;
        if (atlas == null)
        {
            throw new InvalidOperationException("sprite has no texture");
        }
        // Tight-packed sprite: textureRect throw, piece is not a square. Rotated: piece lie on side in atlas.
        // Game item icons are neither (Forge Idol Upgrades check it in game: rect packing), so me just refuse these.
        if (source.packed && source.packingMode == SpritePackingMode.Tight)
        {
            throw new NotSupportedException("tightly packed sprite");
        }
        if (source.packed && source.packingRotation != SpritePackingRotation.None)
        {
            throw new NotSupportedException("sprite is rotated in its atlas");
        }

        // rect = sprite size (what UI show). textureRect = where pixels sit in atlas. textureRectOffset = where
        // that piece go inside rect (atlas may cut empty border). Me rebuild full rect size, empty around piece.
        var rect = source.rect;
        var tr = source.textureRect;
        var trOffset = source.textureRectOffset;
        var w = Mathf.RoundToInt(rect.width);
        var h = Mathf.RoundToInt(rect.height);
        var ox = Mathf.Clamp(Mathf.RoundToInt(trOffset.x), 0, Math.Max(0, w));
        var oy = Mathf.Clamp(Mathf.RoundToInt(trOffset.y), 0, Math.Max(0, h));
        var cw = Math.Min(Mathf.RoundToInt(tr.width), w - ox);
        var ch = Math.Min(Mathf.RoundToInt(tr.height), h - oy);
        if (w <= 0 || h <= 0 || w > MaxSide || h > MaxSide || cw <= 0 || ch <= 0 || atlas.width <= 0 || atlas.height <= 0)
        {
            throw new InvalidOperationException($"bad sprite size {w}x{h}, piece {cw}x{ch}");
        }

        // Color space: Valheim run Linear. In Linear, GPU turn sRGB texel to linear when it read atlas; sRGB render
        // texture turn it back when it write. Default = sRGB in Linear, plain in Gamma, so byte come out same as
        // atlas byte either way. Texture2D linear:false = sRGB too, so ReadPixels copy byte as is and UI read new
        // texture same way as atlas. Atlas with linear data (not sRGB): me keep whole chain linear instead.
        linearData = !atlas.isDataSRGB;
        var rt = RenderTexture.GetTemporary(cw, ch, 0, RenderTextureFormat.ARGB32,
            linearData ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.Default);
        if (rt == null)
        {
            // Null target = Blit draw on screen. Me stop before that.
            throw new InvalidOperationException("no temporary render texture");
        }
        // Blit make rt the active target. Me save old one and put it back after, else camera/UI draw go wrong.
        var previous = RenderTexture.active;
        Texture2D tex = null;
        try
        {
            // Blit read atlas at uv * scale + offset. Scale = piece size / atlas size, offset = piece corner.
            // One rt pixel = one atlas texel, sampled at texel centre = exact copy, mip 0, no blur.
            // Plain Blit (no material) = copy shader, no blending: alpha come through untouched.
            var scale = new Vector2(cw / (float)atlas.width, ch / (float)atlas.height);
            var offset = new Vector2(tr.x / atlas.width, tr.y / atlas.height);
            Graphics.Blit(atlas, rt, scale, offset);

            // RGBA32 = byte layout match Color32. No mip chain: UI icon draw about 1:1.
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false, linearData)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = atlas.filterMode,
            };
            RenderTexture.active = rt;
            // ReadPixels read active target (origin bottom-left) into tex CPU copy at (ox, oy).
            tex.ReadPixels(new Rect(0f, 0f, cw, ch), ox, oy, false);
        }
        catch
        {
            if (tex != null)
            {
                Object.Destroy(tex);
            }
            throw;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }

        try
        {
            var px = tex.GetPixels32();
            if (cw != w || ch != h)
            {
                // New texture start with junk. Me wipe area outside copied piece to see-through.
                ClearOutside(px, w, h, ox, oy, cw, ch);
            }
            // GPU copy can come back empty (game window minimized, device busy): never keep an empty icon.
            var seen = false;
            for (var i = 0; i < px.Length && !seen; i++)
            {
                seen = px[i].a > 8;
            }
            if (!seen)
            {
                throw new EmptyCopyException();
            }
            pixels = px;
            return tex;
        }
        catch
        {
            Object.Destroy(tex);
            throw;
        }
    }

    private static void RunPaint(Color32[] px, int w, int h, bool linearData, Action<Color32[], int, int> paint)
    {
        // Linear-data texture in Linear space: helpers turn screen colour into linear so mark look same colour.
        _toLinear = linearData && QualitySettings.activeColorSpace == ColorSpace.Linear;
        try
        {
            paint(px, w, h);
        }
        finally
        {
            _toLinear = false;
        }
    }

    private static void FillShape(Color32[] px, int w, int h, float x0, float y0, float x1, float y1,
        Func<float, float, float> distance, Color fill, Color outline, float outlineWidth)
    {
        if (px == null)
        {
            return;
        }
        var ow = Mathf.Max(0f, outlineWidth);
        var pad = ow + 1.5f;
        var ix0 = Math.Max(0, Mathf.FloorToInt(x0 - pad));
        var ix1 = Math.Min(w - 1, Mathf.CeilToInt(x1 + pad));
        var iy0 = Math.Max(0, Mathf.FloorToInt(y0 - pad));
        var iy1 = Math.Min(h - 1, Mathf.CeilToInt(y1 + pad));
        for (var y = iy0; y <= iy1; y++)
        {
            // Texture row 0 = bottom, so bigger y = higher. Pixel centre = +0.5.
            var py = y + 0.5f;
            for (var x = ix0; x <= ix1; x++)
            {
                var d = distance(x + 0.5f, py);
                var outlineA = ow > 0f ? Mathf.Clamp01(ow + 0.5f - d) : 0f;
                var fillA = Mathf.Clamp01(0.5f - d);
                if (outlineA <= 0f && fillA <= 0f)
                {
                    continue;
                }
                var idx = y * w + x;
                var c = px[idx];
                Blend(ref c, outline, outlineA);
                Blend(ref c, fill, fillA);
                px[idx] = c;
            }
        }
    }

    private static void ClearOutside(Color32[] px, int w, int h, int ox, int oy, int cw, int ch)
    {
        for (var y = 0; y < h; y++)
        {
            var rowIn = y >= oy && y < oy + ch;
            for (var x = 0; x < w; x++)
            {
                if (!rowIn || x < ox || x >= ox + cw)
                {
                    px[y * w + x] = default;
                }
            }
        }
    }

    private static void ToGamma(Color32[] px)
    {
        const float inv255 = 1f / 255f;
        for (var i = 0; i < px.Length; i++)
        {
            var c = px[i];
            c.r = ToByte(Mathf.LinearToGammaSpace(c.r * inv255));
            c.g = ToByte(Mathf.LinearToGammaSpace(c.g * inv255));
            c.b = ToByte(Mathf.LinearToGammaSpace(c.b * inv255));
            px[i] = c;
        }
    }

    // Catmull-Rom weights for the 4 texels around a point t (0..1) past the second one.
    private static void CubicWeights(float t, float[] w)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        w[0] = 0.5f * (-t3 + 2f * t2 - t);
        w[1] = 0.5f * (3f * t3 - 5f * t2 + 2f);
        w[2] = 0.5f * (-3f * t3 + 4f * t2 + t);
        w[3] = 0.5f * (t3 - t2);
    }

    private static byte ToByte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

    private static byte Byte255(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v), 0, 255);

    private static void LogNoDevice()
    {
        if (_loggedNoDevice)
        {
            return;
        }
        _loggedNoDevice = true;
        Log.Debug("No graphics device (dedicated server?): items keep their plain icons.");
    }

    private static void LogFailure(string key, Sprite source, Exception e)
    {
        if (!Logged.Add(key ?? "(no key)"))
        {
            return;
        }
        var name = source != null ? source.name : "(none)";
        Log.Warning($"Could not paint the icon '{key}' from '{name}' ({e.GetType().Name}: {e.Message}). The plain icon is shown instead.");
    }
}
