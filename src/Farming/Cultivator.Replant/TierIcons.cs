using System;
using System.Collections.Generic;
using System.IO;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Farming.CultivatorReplantMod;

// Me = the mod's icon marks, painted on copies of game icons (IconPainter).
// Transplant item (E4): produce icon + small soil mound with green two-leaf sprout in upper right corner (~40% of
// icon), so a transplant never look like the food. Upper right because slot bottom is stack text ("5/20", whole slot
// wide) and upper left is hotbar key digit; upper right of transplant slot stay empty (no quality number: produce
// max quality 1, no no-portal mark, no food mark). Cultivator level 4-7 (E3): vanilla icon + faceted gem in upper
// right corner (~28%) in tier colour; game quality number sit there too, so InventoryGridPatches hide it for 4-7.
// Gem never on transplant, sprout never on cultivator: same corner no clash. Both marks have dark outline so they
// read on any icon and on dark slot. 4x4 supersampling for soft edges. Sizes follow icon size (64 px icon or 256 px
// package icon; package icon keep sprout lower right, gem upper right, see ExportPngs).
internal static class TierIcons
{
    private const float SproutPart = 0.40f;
    private const float GemPart = 0.28f;
    private const float MarginPart = 0.02f;
    // Gem copy came back null (empty GPU copy, failure): me ask IconPainter again only after this.
    private const float RetrySeconds = 1f;

    private static readonly Color Clear = new Color(0f, 0f, 0f, 0f);

    // Sprout colours (screen colour, sRGB).
    private static readonly Color SproutOutline = new Color(0.08f, 0.06f, 0.035f, 0.95f);
    private static readonly Color SoilLight = new Color32(0x8b, 0x5a, 0x2b, 0xff);
    private static readonly Color SoilDark = new Color32(0x4e, 0x30, 0x18, 0xff);
    private static readonly Color SoilRim = new Color32(0xb5, 0x80, 0x4c, 0xff);
    private static readonly Color Pebble = new Color32(0x36, 0x22, 0x11, 0xff);
    private static readonly Color Stem = new Color32(0x4c, 0x8f, 0x2a, 0xff);
    private static readonly Color LeafLight = new Color32(0x9a, 0xd6, 0x5c, 0xff);
    private static readonly Color LeafDark = new Color32(0x3f, 0x8a, 0x25, 0xff);
    private static readonly Color LeafVein = new Color32(0x2c, 0x66, 0x19, 0xff);

    private static readonly Color GemOutline = new Color(0.05f, 0.04f, 0.05f, 0.95f);

    // One gem look per tier: body colour + edge colour, facets made from body (light from upper left).
    private sealed class GemStyle
    {
        internal readonly Color Edge;
        internal readonly Color CrownLeft;
        internal readonly Color Table;
        internal readonly Color CrownRight;
        internal readonly Color PavilionLeft;
        internal readonly Color PavilionMiddle;
        internal readonly Color PavilionRight;

        internal GemStyle(Color body, Color edge)
        {
            Edge = edge;
            CrownLeft = Color.Lerp(body, Color.white, 0.45f);
            Table = Color.Lerp(body, Color.white, 0.25f);
            CrownRight = Color.Lerp(body, Color.white, 0.08f);
            PavilionLeft = Color.Lerp(body, Color.white, 0.12f);
            PavilionMiddle = Color.Lerp(body, Color.black, 0.18f);
            PavilionRight = Color.Lerp(body, Color.black, 0.45f);
        }
    }

    // Index = quality - FirstNewTier: 4 black metal, 5 eitr, 6 flametal, 7 bloodgold.
    private static readonly GemStyle[] Gems =
    {
        new GemStyle(new Color32(0x4a, 0x4f, 0x57, 0xff), new Color32(0xc3, 0xca, 0xd4, 0xff)), // dark steel, light edge
        new GemStyle(new Color32(0x38, 0xc6, 0xf4, 0xff), new Color32(0xd2, 0xf5, 0xff, 0xff)), // eitr cyan
        new GemStyle(new Color32(0xff, 0x7a, 0x1f, 0xff), new Color32(0xff, 0xd5, 0x8a, 0xff)), // flametal orange
        new GemStyle(new Color32(0xc8, 0x32, 0x3c, 0xff), new Color32(0xf2, 0xc1, 0x4e, 0xff)), // bloodgold crimson, gold edge
    };

    // Paint callbacks made once: no new delegate per call.
    private static readonly Action<Color32[], int, int> SproutPainter = PaintSprout;
    private static readonly Action<Color32[], int, int>[] GemPainters = MakeGemPainters();

    private sealed class TierEntry
    {
        internal string Key;    // IconPainter key, made once (no string per frame)
        internal Sprite Made;
        internal float NextTry;
    }

    // Key = sprite id moved up 2 bit + tier index (0..3). Long key = no boxing, no string on hot path.
    private static readonly Dictionary<long, TierEntry> TierCache = new Dictionary<long, TierEntry>();

    // Transplant item icon: produce icon + sprout mark. Null = could not paint (no graphics device, failure, empty
    // GPU copy): caller use the produce icon. Called at item build time (main menu ObjectDB), main thread.
    internal static Sprite TransplantIcon(Sprite produce)
    {
        if (produce == null)
        {
            return null;
        }
        return IconPainter.Compose(produce, "transplant." + produce.GetInstanceID(), SproutPainter);
    }

    // Cultivator icon for this quality: null below FirstNewTier (vanilla icon stay). Hot path (GetIcon postfix and
    // InventoryGrid.UpdateGui backup, every slot every frame): one dictionary look + Unity null check after first
    // time, no allocation.
    internal static Sprite CultivatorIcon(Sprite vanilla, int quality)
    {
        if (quality < PlantCatalog.FirstNewTier || ReferenceEquals(vanilla, null))
        {
            return null;
        }
        if (quality > PlantCatalog.MaxTier)
        {
            // Refined past bloodgold by other mod: bloodgold gem.
            quality = PlantCatalog.MaxTier;
        }
        var tier = quality - PlantCatalog.FirstNewTier;
        var id = vanilla.GetInstanceID();
        var key = ((long)id << 2) | (long)tier;
        if (TierCache.TryGetValue(key, out var entry))
        {
            if (entry.Made != null)
            {
                return entry.Made;
            }
            if (Time.realtimeSinceStartup < entry.NextTry)
            {
                return null;
            }
        }
        else
        {
            entry = new TierEntry { Key = "tier" + quality + "." + id };
            TierCache[key] = entry;
        }
        entry.Made = IconPainter.Compose(vanilla, entry.Key, GemPainters[tier]);
        if (entry.Made == null)
        {
            entry.NextTry = Time.realtimeSinceStartup + RetrySeconds;
        }
        return entry.Made;
    }

    private static Action<Color32[], int, int>[] MakeGemPainters()
    {
        var painters = new Action<Color32[], int, int>[Gems.Length];
        for (var i = 0; i < Gems.Length; i++)
        {
            var style = Gems[i];
            painters[i] = (px, w, h) => PaintGem(px, w, h, style);
        }
        return painters;
    }

    // Sprout box in upper right corner (y up), clear of stack text along slot bottom (it reach ~23% of icon height).
    private static void PaintSprout(Color32[] px, int w, int h)
    {
        float size = Math.Min(w, h);
        var s = size * SproutPart;
        var m = Mathf.Max(0.5f, size * MarginPart);
        DrawSprout(px, w, h, w - m - s, h - m - s, s);
    }

    private static void PaintGem(Color32[] px, int w, int h, GemStyle style)
    {
        float size = Math.Min(w, h);
        var g = size * GemPart;
        var m = Mathf.Max(0.5f, size * MarginPart);
        DrawGem(px, w, h, w - m - g, h - m - g, g, style);
    }

    // Sprout mark in box (ox, oy) side s, y up. Soil mound (half ellipse, flat bottom) with pebbles, curved stem,
    // two pointed leaves. One dark outline around the whole mark (no line between stem and mound).
    private static void DrawSprout(Color32[] px, int w, int h, float ox, float oy, float s)
    {
        var ow = Mathf.Max(1.1f, s * 0.065f);
        var baseY = oy + ow;
        float X(float u) => ox + u * s;
        float Y(float v) => baseY + v * s;

        // Mound: top half of ellipse standing on baseY.
        var mcx = X(0.5f);
        var mrx = s * 0.5f - ow;
        var mry = s * 0.27f;
        // Stem: two pieces, small bend, from inside mound up to leaf root.
        float s0x = X(0.50f), s0y = Y(0.18f), s1x = X(0.47f), s1y = Y(0.38f), tx = X(0.52f), ty = Y(0.56f);
        var stemR = Mathf.Max(0.7f, s * 0.045f);
        // Leaves from stem tip: left small, right bigger.
        float lx = X(0.10f), ly = Y(0.78f), rx = X(0.92f), ry = Y(0.84f);
        var leftW = s * 0.22f;
        var rightW = s * 0.24f;
        var rimW = Mathf.Max(0.6f, s * 0.07f);
        var pebbleR = Mathf.Max(0.5f, s * 0.032f);

        Color Sample(float x, float y)
        {
            var dEllipse = IconPainter.EllipseDistance(x, y, mcx, baseY, mrx, mry, 0f);
            var dMound = Mathf.Max(dEllipse, baseY - y);
            var dStem = Mathf.Min(IconPainter.SegmentDistance(x, y, s0x, s0y, s1x, s1y),
                IconPainter.SegmentDistance(x, y, s1x, s1y, tx, ty)) - stemR;
            var dLeft = IconPainter.LeafDistance(x, y, tx, ty, lx, ly, leftW);
            var dRight = IconPainter.LeafDistance(x, y, tx, ty, rx, ry, rightW);
            var dAll = Mathf.Min(Mathf.Min(dMound, dStem), Mathf.Min(dLeft, dRight));
            if (dAll > ow)
            {
                return Clear;
            }
            if (dAll > 0f)
            {
                return SproutOutline;
            }
            var c = Stem;
            if (dLeft <= 0f)
            {
                c = LeafColor(x, y, tx, ty, lx, ly, leftW);
            }
            if (dRight <= 0f)
            {
                c = LeafColor(x, y, tx, ty, rx, ry, rightW);
            }
            if (dMound <= 0f)
            {
                // Soil darker at bottom, light rim along round top, a few pebbles.
                c = Color.Lerp(SoilDark, SoilLight, Mathf.Clamp01((y - baseY) / mry));
                if (dEllipse > -rimW && y > baseY + mry * 0.35f)
                {
                    c = Color.Lerp(c, SoilRim, 0.5f);
                }
                if (IconPainter.CircleDistance(x, y, X(0.28f), Y(0.08f), pebbleR) <= 0f
                    || IconPainter.CircleDistance(x, y, X(0.67f), Y(0.11f), pebbleR) <= 0f
                    || IconPainter.CircleDistance(x, y, X(0.47f), Y(0.04f), pebbleR * 0.8f) <= 0f)
                {
                    c = Pebble;
                }
            }
            return c;
        }

        IconPainter.PaintSampled(px, w, h, ox, oy, ox + s, oy + s, Sample);
    }

    // Leaf shade: upper side light, lower side dark, thin vein along the middle.
    private static Color LeafColor(float x, float y, float ax, float ay, float bx, float by, float width)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var len = Mathf.Sqrt(dx * dx + dy * dy);
        if (len < 1e-4f)
        {
            return LeafDark;
        }
        var nx = -dy / len;
        var ny = dx / len;
        if (ny < 0f)
        {
            // Normal point up: upper half of leaf = lit side.
            nx = -nx;
            ny = -ny;
        }
        var side = ((x - ax) * nx + (y - ay) * ny) / (width * 0.5f);
        var along = ((x - ax) * dx + (y - ay) * dy) / (len * len);
        var c = Color.Lerp(LeafDark, LeafLight, Mathf.Clamp01(0.45f + 0.55f * side));
        if (Mathf.Abs(side) < 0.12f && along > 0.12f && along < 0.85f)
        {
            c = Color.Lerp(c, LeafVein, 0.55f);
        }
        return c;
    }

    // Gem mark in box (ox, oy) side g, y up. Brilliant cut seen from side: flat top (table), crown down to the
    // girdle, pavilion down to a point. Six facets shaded from upper left, edge band in tier edge colour, girdle line,
    // white sparkle on the table, dark outline.
    private static void DrawGem(Color32[] px, int w, int h, float ox, float oy, float g, GemStyle style)
    {
        var ow = Mathf.Max(1.1f, g * 0.075f);
        var r = g * 0.5f - ow;
        var cx = ox + g * 0.5f;
        var cy = oy + g * 0.5f;
        // Unit shape (x -1..1, y -0.85..0.85): top corners, shoulders (girdle at y 0.4), bottom point.
        var xs = new[] { cx - 0.55f * r, cx + 0.55f * r, cx + r, cx, cx - r };
        var ys = new[] { cy + 0.85f * r, cy + 0.85f * r, cy + 0.4f * r, cy - 0.85f * r, cy + 0.4f * r };
        var edgeW = Mathf.Max(0.6f, r * 0.13f);
        var girdleW = Mathf.Max(0.35f, r * 0.05f);
        // Sparkle: 4-point star on the table, left of middle.
        var spx = cx - 0.22f * r;
        var spy = cy + 0.6f * r;
        var arm = r * 0.3f;
        var armW = arm * 0.32f;

        Color Sample(float x, float y)
        {
            var d = IconPainter.PolygonDistance(x, y, xs, ys, 5);
            if (d > ow)
            {
                return Clear;
            }
            if (d > 0f)
            {
                return GemOutline;
            }
            var u = (x - cx) / r;
            var v = (y - cy) / r;
            Color c;
            if (v >= 0.4f)
            {
                // Crown: lines from top corners (+-0.55, 0.85) down to (+-0.32, 0.4) split left, table, right.
                var t = (v - 0.4f) / 0.45f;
                var split = 0.32f + 0.23f * t;
                c = u < -split ? style.CrownLeft : u > split ? style.CrownRight : style.Table;
            }
            else
            {
                // Pavilion: lines from (+-0.32, 0.4) down to the point split left, middle, right.
                var t = (v + 0.85f) / 1.25f;
                var split = 0.32f * t;
                c = u < -split ? style.PavilionLeft : u > split ? style.PavilionRight : style.PavilionMiddle;
            }
            if (Mathf.Abs(y - (cy + 0.4f * r)) < girdleW)
            {
                c = Color.Lerp(c, style.Edge, 0.5f);
            }
            if (d > -edgeW)
            {
                c = Color.Lerp(c, style.Edge, 0.85f);
            }
            var dSparkle = Mathf.Min(
                Mathf.Min(IconPainter.LeafDistance(x, y, spx - arm, spy, spx + arm, spy, armW),
                    IconPainter.LeafDistance(x, y, spx, spy - arm, spx, spy + arm, armW)),
                IconPainter.CircleDistance(x, y, spx, spy, armW * 0.7f));
            if (dSparkle <= 0f)
            {
                c = Color.Lerp(c, Color.white, 0.95f);
            }
            return c;
        }

        IconPainter.PaintSampled(px, w, h, ox, oy, ox + g, oy + g, Sample);
    }

#if DEBUG
    // Debug export (self test): icon-256.png = package icon (vanilla cultivator icon scaled to 256 + sprout mark lower
    // right + bloodgold gem upper right, see-through background; package icon have no stack text, so sprout keep old
    // corner and both marks fit), cultivator-<q>-<tier>.png for levels 4-7, transplant-<Key>.png = what each transplant
    // item really carry (or produce + sprout when item not built). Main thread, graphics device.
    internal static void ExportPngs(string folder)
    {
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }
        Directory.CreateDirectory(folder);
        var db = ObjectDB.instance;
        if (db == null)
        {
            Log.Warning("Icon export: no item database yet, nothing written.");
            return;
        }
        var written = 0;

        var vanilla = FirstIcon(db.GetItemPrefab(PlantCatalog.CultivatorPrefab));
        if (vanilla != null)
        {
            var px = IconPainter.CopyPixels(vanilla, null, out var w, out var h);
            if (px != null)
            {
                const int side = 256;
                var big = IconPainter.Resample(px, w, h, side, side);
                var s = side * SproutPart;
                var g = side * GemPart;
                var m = side * MarginPart;
                DrawSprout(big, side, side, side - m - s, m, s);
                DrawGem(big, side, side, side - m - g, side - m - g, g, Gems[Gems.Length - 1]);
                written += WritePng(folder, "icon-256.png", big, side, side);
            }
            for (var q = PlantCatalog.FirstNewTier; q <= PlantCatalog.MaxTier; q++)
            {
                var tier = IconPainter.CopyPixels(vanilla, GemPainters[q - PlantCatalog.FirstNewTier], out w, out h);
                written += WritePng(folder, $"cultivator-{q}-{PlantCatalog.TierName(q).Replace(' ', '-')}.png", tier, w, h);
            }
        }
        else
        {
            Log.Warning($"Icon export: no icon for '{PlantCatalog.CultivatorPrefab}'.");
        }

        foreach (var kind in PlantCatalog.All)
        {
            var w = 0;
            var h = 0;
            Color32[] px = null;
            var icon = FirstIcon(TransplantContent.ItemPrefab(kind));
            if (icon != null)
            {
                px = IconPainter.CopyPixels(icon, null, out w, out h);
            }
            else
            {
                // Item not built (or no icon): produce + sprout, same look the item would get.
                var produce = FirstIcon(db.GetItemPrefab(kind.ProduceItem));
                if (produce != null)
                {
                    px = IconPainter.CopyPixels(produce, SproutPainter, out w, out h);
                }
            }
            written += WritePng(folder, "transplant-" + kind.Key + ".png", px, w, h);
        }
        Log.Info($"Icon export: wrote {written} PNG files to {folder}.");
    }

    private static Sprite FirstIcon(GameObject prefab)
    {
        if (prefab == null)
        {
            return null;
        }
        var drop = prefab.GetComponent<ItemDrop>();
        if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null)
        {
            return null;
        }
        var icons = drop.m_itemData.m_shared.m_icons;
        return icons != null && icons.Length > 0 ? icons[0] : null;
    }

    private static int WritePng(string folder, string file, Color32[] px, int w, int h)
    {
        if (px == null || w <= 0 || h <= 0 || px.Length != w * h)
        {
            Log.Warning($"Icon export: could not make {file}.");
            return 0;
        }
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            tex.SetPixels32(px);
            tex.Apply(false, false);
            File.WriteAllBytes(Path.Combine(folder, file), tex.EncodeToPNG());
            return 1;
        }
        catch (Exception e)
        {
            Log.Warning($"Icon export: could not write {file} ({e.GetType().Name}: {e.Message}).");
            return 0;
        }
        finally
        {
            Object.Destroy(tex);
        }
    }
#endif
}
