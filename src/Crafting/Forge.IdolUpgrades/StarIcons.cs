using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MC.Crafting.ForgeIdolUpgradesMod;

// Me paint 1-3 gold star on idol icon. Game icon live in big packed atlas (IconAtlas 4096x4096, BC7, not readable),
// so me: copy icon square on GPU (Blit) -> read it back (ReadPixels) -> paint star on CPU -> make new sprite.
// Me never touch game texture: me paint on own copy. Me keep every made sprite in cache, so after first time
// Get = one dictionary look, no garbage. Main thread only (Blit, ReadPixels, Sprite.Create are main thread stuff).
internal static class StarIcons
{
    public const int MaxStars = 3;

    // Me no make giant texture by mistake. Idol icon = 64x64; 1024 = lots of room.
    private const int MaxSide = 1024;

    // Me colour = screen colour (sRGB), same as picked in paint program. Gold body light on top, dark at bottom.
    private static readonly Color GoldLight = new Color(1f, 0.9f, 0.4f, 1f);
    private static readonly Color GoldDark = new Color(0.92f, 0.58f, 0.07f, 1f);
    // Me dark rim so star show on bright icon and on dark slot too.
    private static readonly Color Rim = new Color(0.09f, 0.05f, 0.01f, 0.95f);

    private sealed class Entry
    {
        public Sprite Source;     // game sprite me copy from (me check it still alive when pruning)
        public Sprite Made;       // me own sprite; null when Failed
        public Texture2D Texture; // me own texture under Made
        public bool Failed;       // me could not make it: give game sprite back, no retry every frame
        public float RetryAt;     // > 0 = copy came back empty: game sprite until this time, then try again
    }

    private sealed class EmptyCopyException : Exception
    {
    }

    private const float RetrySeconds = 1f;
    private static bool _loggedEmpty;

    // Me key = sprite id moved up 3 bit + middle flag (bit 2) + star count (1..3, bits 0-1). Long key = no boxing.
    private static readonly Dictionary<long, Entry> Cache = new Dictionary<long, Entry>();

    // Me map made-sprite id -> game sprite. Clear use it to put game sprite back into live Image before kill.
    private static readonly Dictionary<int, Sprite> SourceOfMade = new Dictionary<int, Sprite>();

    private static readonly List<long> DeadKeys = new List<long>();

    // Me reuse these for star corners: 10 corner = 5 tip + 5 inner. Static = no new array per star.
    private static readonly float[] StarX = new float[10];
    private static readonly float[] StarY = new float[10];

    private static bool _loggedFailure;

    // Debug self-test set true before first Get, then ImageConversion.EncodeToPNG(made.texture) to look at it.
    // Normal = false: Apply throw away CPU copy, texture live only on GPU like game icon.
    internal static bool KeepReadable { get; set; }

    // Me give icon with N star (1..3). Bad input, 0 star or any trouble = give source back. Me never throw.
    // middle = star row lower, across the icon's middle: requirement slots print the item name over the icon top.
    internal static Sprite Get(Sprite source, int stars, bool middle = false)
    {
        try
        {
            // Unity null check (== null), not ?. : dead sprite count as null.
            if (stars <= 0 || source == null)
            {
                return source;
            }
            if (stars > MaxStars)
            {
                stars = MaxStars;
            }

            var key = ((long)source.GetInstanceID() << 3) | (middle ? 4L : 0L) | (long)stars;
            if (Cache.TryGetValue(key, out var entry))
            {
                if (entry.Failed)
                {
                    return source;
                }
                if (entry.RetryAt > 0f)
                {
                    if (Time.realtimeSinceStartup < entry.RetryAt)
                    {
                        return source;
                    }
                    Cache.Remove(key);
                    return Make(key, source, stars, middle);
                }
                if (entry.Made != null)
                {
                    return entry.Made;
                }
                // Somebody kill me sprite. Me drop entry, make again below.
                Drop(key, entry);
            }
            return Make(key, source, stars, middle);
        }
        catch (Exception e)
        {
            LogOnce(source, e);
            return source;
        }
    }

    // Me tell if sprite is one me made (tests and callers that must not star a star).
    internal static bool IsStarSprite(Sprite sprite) => sprite != null && SourceOfMade.ContainsKey(sprite.GetInstanceID());

    // Me throw away every made texture and sprite. First me swap game sprite back into live Image so nothing
    // show white box. Self tests only (fresh cache): the mod itself keep its sprites the whole game session, also
    // after a toggle off, because messages still waiting in the game's queues hold them (a destroyed sprite there
    // show as a white square). At most 16 idols x 3 levels x 2 layouts, 16 KB each.
    internal static void Clear()
    {
        try
        {
            PutBackGameSprites();
        }
        catch (Exception e)
        {
            Log.Warning($"Could not restore plain idol icons in open windows: {e.Message}");
        }

        foreach (var pair in Cache)
        {
            DestroyOwned(pair.Value);
        }
        Cache.Clear();
        SourceOfMade.Clear();
        _loggedFailure = false;
    }

    private static Sprite Make(long key, Sprite source, int stars, bool middle)
    {
        // Me only get here on cache miss (rare). Good time to drop entries whose game sprite died (atlas reload).
        PruneDeadSources();

        var entry = new Entry { Source = source };
        try
        {
            entry.Texture = Compose(source, stars, middle);
            var rect = source.rect;
            // Sprite.Create want pivot as 0..1 of rect; sprite.pivot is in pixel. Me divide, so pivot stay same.
            var pivot = new Vector2(source.pivot.x / rect.width, source.pivot.y / rect.height);
            // FullRect: no tight mesh (need readable texture, cost time). Last false: no physics shape, not needed.
            entry.Made = Sprite.Create(entry.Texture, new Rect(0f, 0f, entry.Texture.width, entry.Texture.height),
                pivot, source.pixelsPerUnit, 0u, SpriteMeshType.FullRect, source.border, false);
            if (entry.Made == null)
            {
                throw new InvalidOperationException("Sprite.Create returned nothing");
            }
            entry.Made.name = source.name + "_mcstars" + stars;
            // DontUnloadUnusedAsset inside HideAndDontSave: Resources.UnloadUnusedAssets (scene change) no kill it.
            // Me own it, so me must Destroy it in Clear.
            entry.Made.hideFlags = HideFlags.HideAndDontSave;
            SourceOfMade[entry.Made.GetInstanceID()] = source;
        }
        catch (EmptyCopyException)
        {
            DestroyOwned(entry);
            entry.RetryAt = Time.realtimeSinceStartup + RetrySeconds;
            if (!_loggedEmpty)
            {
                _loggedEmpty = true;
                Log.Debug($"Star icon for '{source.name}' came back empty (game window minimized?): plain icon for now, trying again.");
            }
        }
        catch (Exception e)
        {
            DestroyOwned(entry);
            entry.Failed = true;
            LogOnce(source, e);
        }
        Cache[key] = entry;
        return entry.Failed ? source : entry.Made;
    }

    private static Texture2D Compose(Sprite source, int stars, bool middle)
    {
        // Me no GPU = no Blit (headless run). Me give up, caller use plain icon.
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            throw new InvalidOperationException("no graphics device");
        }
        var atlas = source.texture;
        if (atlas == null)
        {
            throw new InvalidOperationException("sprite has no texture");
        }
        // Tight-packed sprite: textureRect throw, piece is not a square. Rotated: piece lie on side in atlas.
        // Idol icons are neither (checked in game: rect packing, textureRect ok), so me just refuse these.
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

        // Color space: Valheim run Linear (PlayerSettings m_ActiveColorSpace = 1). In Linear, GPU turn sRGB
        // texel to linear when it read atlas; sRGB render texture turn it back when it write. Default = sRGB
        // in Linear, plain in Gamma, so byte come out same as atlas byte either way. Texture2D linear:false =
        // sRGB too, so ReadPixels copy byte as is and UI read new texture same way as atlas.
        // Atlas with linear data (not sRGB): me keep whole chain linear instead.
        var linearData = !atlas.isDataSRGB;
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
                name = source.name + "_mcstars" + stars,
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
            // GPU copy can come back empty (game window minimized, device busy): never keep an empty icon, the
            // caller show the plain one and me try again a bit later.
            var seen = false;
            for (var i = 0; i < px.Length && !seen; i++)
            {
                seen = px[i].a > 8;
            }
            if (!seen)
            {
                throw new EmptyCopyException();
            }
            // Linear-data texture in Linear space: me turn screen colour into linear so star look same gold.
            var toLinear = linearData && QualitySettings.activeColorSpace == ColorSpace.Linear;
            DrawStars(px, w, h, stars, toLinear, middle);
            tex.SetPixels32(px);
            // Upload to GPU. makeNoLongerReadable = drop CPU copy (save memory) unless test want to read it.
            tex.Apply(false, !KeepReadable);
            return tex;
        }
        catch
        {
            Object.Destroy(tex);
            throw;
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

    // Me lay star in one row along top edge (or across the middle), pushed right: slot show hotbar key digit
    // top-left, and top-right is where game print quality number (me hide it for idols). Size follow icon (64 px ->
    // star ~15 px).
    private static void DrawStars(Color32[] px, int w, int h, int stars, bool toLinear, bool middle)
    {
        float size = Math.Min(w, h);
        var outer = Mathf.Max(4f, size * 0.12f);  // tip radius (min 4 px: smaller = blob)
        var inner = outer * 0.45f;                // fat star: still read as star when tiny
        var rim = Mathf.Max(1f, outer * 0.22f);   // dark rim width in pixel
        var step = outer * 1.95f;                 // centre to centre: rims just touch
        // Star point up: top tip at cy + outer, low tips at cy - outer * cos(36) (= 0.809).
        var cy = middle ? h * 0.45f : h - 1f - rim - outer;
        // Side tips reach outer * cos(18) (= 0.951) from centre. Rightmost star touch right edge minus rim.
        var cx0 = w - 1f - rim - outer * 0.951f - (stars - 1) * step;

        var light = toLinear ? GoldLight.linear : GoldLight;
        var dark = toLinear ? GoldDark.linear : GoldDark;
        var rimColor = toLinear ? Rim.linear : Rim;
        for (var s = 0; s < stars; s++)
        {
            DrawStar(px, w, h, cx0 + s * step, cy, outer, inner, rim, light, dark, rimColor);
        }
    }

    // Me draw one star with signed distance: d < 0 inside, d > 0 outside, in pixel. Coverage = clamp(0.5 - d)
    // give 1 px soft edge (anti-alias) without supersampling. Rim = same shape grown by part of rim width,
    // gold = same shape shrunk by rest, so rim sit half out, half in.
    private static void DrawStar(Color32[] px, int w, int h, float cx, float cy, float outer, float inner, float rim,
        Color light, Color dark, Color rimColor)
    {
        for (var i = 0; i < 10; i++)
        {
            var angle = (90f + 36f * i) * Mathf.Deg2Rad;
            var radius = (i & 1) == 0 ? outer : inner;
            StarX[i] = cx + radius * Mathf.Cos(angle);
            StarY[i] = cy + radius * Mathf.Sin(angle);
        }

        var pad = rim + 1.5f;
        var x0 = Math.Max(0, Mathf.FloorToInt(cx - outer - pad));
        var x1 = Math.Min(w - 1, Mathf.CeilToInt(cx + outer + pad));
        var y0 = Math.Max(0, Mathf.FloorToInt(cy - outer - pad));
        var y1 = Math.Min(h - 1, Mathf.CeilToInt(cy + outer + pad));
        var rimOut = rim * 0.6f;
        var fillIn = rim * 0.4f;

        for (var y = y0; y <= y1; y++)
        {
            // Texture row 0 = bottom, so bigger y = higher. Pixel centre = +0.5.
            var py = y + 0.5f;
            var t = Mathf.Clamp01((py - (cy - outer)) / (2f * outer));
            var fill = Color.Lerp(dark, light, t);
            for (var x = x0; x <= x1; x++)
            {
                var d = SignedDistance(x + 0.5f, py);
                var rimA = Mathf.Clamp01(rimOut + 0.5f - d) * rimColor.a;
                var fillA = Mathf.Clamp01(0.5f - fillIn - d);
                if (rimA <= 0f && fillA <= 0f)
                {
                    continue;
                }
                var idx = y * w + x;
                var c = px[idx];
                Over(ref c, rimColor, rimA);
                Over(ref c, fill, fillA);
                px[idx] = c;
            }
        }
    }

    // Me measure distance from point to star edge (10 segment), sign by even-odd crossing test.
    private static float SignedDistance(float px, float py)
    {
        var inside = false;
        var best = float.MaxValue;
        for (int i = 0, j = 9; i < 10; j = i++)
        {
            float ax = StarX[j], ay = StarY[j], bx = StarX[i], by = StarY[i];
            if ((by > py) != (ay > py) && px < (ax - bx) * (py - by) / (ay - by) + bx)
            {
                inside = !inside;
            }
            float ex = bx - ax, ey = by - ay, wx = px - ax, wy = py - ay;
            var t = Mathf.Clamp01((wx * ex + wy * ey) / (ex * ex + ey * ey));
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

    // Me put colour on top of pixel, straight (not premultiplied) alpha, like texture store it.
    private static void Over(ref Color32 dst, Color src, float a)
    {
        if (a <= 0f)
        {
            return;
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

    private static byte ToByte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

    private static void PruneDeadSources()
    {
        DeadKeys.Clear();
        foreach (var pair in Cache)
        {
            if (pair.Value.Source == null)
            {
                DeadKeys.Add(pair.Key);
            }
        }
        foreach (var key in DeadKeys)
        {
            Drop(key, Cache[key]);
        }
        DeadKeys.Clear();
    }

    private static void Drop(long key, Entry entry)
    {
        Cache.Remove(key);
        DestroyOwned(entry);
    }

    private static void DestroyOwned(Entry entry)
    {
        // ReferenceEquals: me want id even when sprite already dead (GetInstanceID still work on dead wrapper).
        if (!ReferenceEquals(entry.Made, null))
        {
            SourceOfMade.Remove(entry.Made.GetInstanceID());
            if (entry.Made != null)
            {
                Object.Destroy(entry.Made);
            }
            entry.Made = null;
        }
        if (entry.Texture != null)
        {
            Object.Destroy(entry.Texture);
        }
        entry.Texture = null;
    }

    // Me swap game sprite back into every Image (open or hidden) that still show me star sprite.
    private static void PutBackGameSprites()
    {
        if (SourceOfMade.Count == 0)
        {
            return;
        }
        var images = Object.FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var image in images)
        {
            var sprite = image.sprite;
            if (sprite != null && SourceOfMade.TryGetValue(sprite.GetInstanceID(), out var original))
            {
                image.sprite = original;
            }
        }
    }

    private static void LogOnce(Sprite source, Exception e)
    {
        if (_loggedFailure)
        {
            return;
        }
        _loggedFailure = true;
        var name = source != null ? source.name : "(none)";
        Log.Warning($"Could not add stars to icon '{name}' ({e.GetType().Name}: {e.Message}). Upgraded idols show the plain icon.");
    }
}
