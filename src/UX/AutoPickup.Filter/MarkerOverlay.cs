using System;
using System.Collections.Generic;
using System.Text;
using MC.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MC.UX.AutoPickupFilterMod;

// Me = the small badge on slots whose item type is in the active list: red "no entry" disc (Ignored, Skip ignored
// mode), green check disc (Selected, Only selected mode), nothing in Everything mode. Only player grid and container
// panel grid. Badge = Image child of the slot, made the first time a slot need it, raycastTarget off (never eat
// clicks or hover). Sprites drawn in code once (no asset bundle). Per frame: loop live item list, int lookups,
// Image.enabled set only when it change; no allocation.
internal static class MarkerOverlay
{
    private const string MarkName = "MC_LootFilterMark";
    private const float Size = 16f;
    private const float Inset = 3f;
    private const int TexSize = 32;

    private enum Corner
    {
        TopRight,
        TopLeft,
        BottomLeft,
        BottomRight,
    }

    private sealed class GridMarks
    {
        public string Name;
        public InventoryGrid Grid;
        public InventoryElement First;
        public int Count = -1;
        public Image[] Marks = new Image[0];
        public bool[] Shown = new bool[0];
        public bool[] Want = new bool[0];
        public Sprite ShownSprite;
        public bool AnyShown;
        public bool CornerChosen;
        public Corner Corner;
    }

    private static readonly GridMarks PlayerMarks = new GridMarks { Name = "player grid" };
    private static readonly GridMarks ContainerMarks = new GridMarks { Name = "container grid" };
    private static readonly Vector3[] Corners = new Vector3[4];
    private static readonly List<Rect> Occupied = new List<Rect>();

    private static Texture2D _texIgnored, _texSelected;
    private static Sprite _ignored, _selected;

    // UpdateGui postfix call me for every grid each frame it repaint.
    internal static void Update(InventoryGrid grid)
    {
        var gui = InventoryGui.instance;
        if (gui == null)
        {
            return;
        }
        GridMarks marks;
        if (ReferenceEquals(grid, gui.m_playerGrid))
        {
            marks = PlayerMarks;
        }
        else if (ReferenceEquals(grid, gui.ContainerGrid))
        {
            marks = ContainerMarks;
        }
        else
        {
            return;
        }
        if (!ReferenceEquals(marks.Grid, grid))
        {
            // Other InventoryGui (scene reload): old badges died with old slots.
            Forget(marks);
            marks.Grid = grid;
        }

        if (!Plugin.ShowMarkersOn || !FilterState.EnsureLocal() || FilterState.Mode == FilterMode.Everything)
        {
            HideAll(marks);
            return;
        }

        var elements = grid.m_elements;
        var n = elements.Count;
        if (n != marks.Count || (n > 0 && !ReferenceEquals(elements[0], marks.First)))
        {
            // Grid rebuilt its slots (other chest size, inventory rows): old badges go with old slots.
            Rebuild(marks, elements);
        }
        if (n == 0)
        {
            return;
        }
        if (!marks.CornerChosen && !TryPickCorner(marks, elements[0]))
        {
            return; // grid at zero scale this frame: try next frame
        }
        var inv = grid.m_inventory;
        if (inv == null)
        {
            return;
        }

        var want = marks.Want;
        Array.Clear(want, 0, n);
        var width = grid.m_width;
        foreach (var item in inv.GetAllItems())
        {
            var idx = item.m_gridPos.y * width + item.m_gridPos.x;
            // Extended inventory mods put items outside vanilla grid area: no badge, never an exception.
            if ((uint)idx >= (uint)n)
            {
                continue;
            }
            if (FilterState.IsMarked(item))
            {
                want[idx] = true;
            }
        }

        EnsureSprites();
        var sprite = FilterState.Mode == FilterMode.SkipIgnored ? _ignored : _selected;
        if (!ReferenceEquals(marks.ShownSprite, sprite))
        {
            // Mode switched: repaint shown badges with new sprite.
            for (var i = 0; i < n; i++)
            {
                if (marks.Shown[i] && marks.Marks[i] != null)
                {
                    marks.Marks[i].sprite = sprite;
                }
            }
            marks.ShownSprite = sprite;
        }

        var any = false;
        for (var i = 0; i < n; i++)
        {
            var w = want[i];
            any |= w;
            if (w == marks.Shown[i])
            {
                continue;
            }
            var badge = marks.Marks[i];
            if (badge == null)
            {
                if (!w)
                {
                    marks.Shown[i] = false;
                    continue;
                }
                var element = elements[i];
                if (element == null)
                {
                    continue;
                }
                badge = CreateBadge(element, marks.Corner);
                marks.Marks[i] = badge;
            }
            if (w)
            {
                badge.sprite = sprite;
            }
            badge.enabled = w;
            marks.Shown[i] = w;
        }
        marks.AnyShown = any;
    }

    // Deactivate: badges, sprites, textures gone. Remade lazily when feature come back.
    internal static void DestroyAll()
    {
        DestroyBadges(PlayerMarks);
        DestroyBadges(ContainerMarks);
        Forget(PlayerMarks);
        Forget(ContainerMarks);
        if (_ignored != null)
        {
            UnityEngine.Object.Destroy(_ignored);
        }
        if (_selected != null)
        {
            UnityEngine.Object.Destroy(_selected);
        }
        if (_texIgnored != null)
        {
            UnityEngine.Object.Destroy(_texIgnored);
        }
        if (_texSelected != null)
        {
            UnityEngine.Object.Destroy(_texSelected);
        }
        _ignored = _selected = null;
        _texIgnored = _texSelected = null;
    }

    private static void HideAll(GridMarks marks)
    {
        if (!marks.AnyShown)
        {
            return;
        }
        for (var i = 0; i < marks.Shown.Length; i++)
        {
            if (!marks.Shown[i])
            {
                continue;
            }
            if (marks.Marks[i] != null)
            {
                marks.Marks[i].enabled = false;
            }
            marks.Shown[i] = false;
        }
        marks.AnyShown = false;
    }

    private static void Rebuild(GridMarks marks, List<InventoryElement> elements)
    {
        DestroyBadges(marks);
        var n = elements.Count;
        marks.Count = n;
        marks.First = n > 0 ? elements[0] : null;
        marks.Marks = new Image[n];
        marks.Shown = new bool[n];
        marks.Want = new bool[n];
        marks.AnyShown = false;
        marks.ShownSprite = null;
    }

    private static void DestroyBadges(GridMarks marks)
    {
        for (var i = 0; i < marks.Marks.Length; i++)
        {
            var badge = marks.Marks[i];
            if (badge != null)
            {
                UnityEngine.Object.Destroy(badge.gameObject);
            }
            marks.Marks[i] = null;
        }
    }

    private static void Forget(GridMarks marks)
    {
        marks.Grid = null;
        marks.First = null;
        marks.Count = -1;
        marks.Marks = new Image[0];
        marks.Shown = new bool[0];
        marks.Want = new bool[0];
        marks.ShownSprite = null;
        marks.AnyShown = false;
        marks.CornerChosen = false;
    }

    private static Image CreateBadge(InventoryElement element, Corner corner)
    {
        var go = new GameObject(MarkName, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(element.transform, false);
        var anchor = Anchor(corner);
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.sizeDelta = new Vector2(Size, Size);
        rt.anchoredPosition = new Vector2(anchor.x > 0.5f ? -Inset : Inset, anchor.y > 0.5f ? -Inset : Inset);
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        var image = go.AddComponent<Image>();
        image.raycastTarget = false;
        image.preserveAspect = true;
        image.enabled = false;
        rt.SetAsLastSibling();
        return image;
    }

    private static Vector2 Anchor(Corner c)
    {
        switch (c)
        {
            case Corner.TopLeft:
                return new Vector2(0f, 1f);
            case Corner.BottomLeft:
                return new Vector2(0f, 0f);
            case Corner.BottomRight:
                return new Vector2(1f, 0f);
            default:
                return new Vector2(1f, 1f);
        }
    }

    // ---------------------------------------------------------------- corner

    // Pick the slot corner that hide least of vanilla bits (quality number, no-portal icon, food icon, amount,
    // hotbar number, durability bar), from the real slot layout. Order of wish: top-right, top-left, bottom-left,
    // bottom-right. Big full-slot things (icon, background) are ignored. False = slot at zero scale, try again later.
    private static bool TryPickCorner(GridMarks marks, InventoryElement element)
    {
        marks.CornerChosen = true;
        marks.Corner = Corner.TopRight;
        if (element == null || !(element.transform is RectTransform root))
        {
            return true;
        }
        if (Mathf.Abs(root.lossyScale.x) < 1e-4f || Mathf.Abs(root.lossyScale.y) < 1e-4f)
        {
            marks.CornerChosen = false;
            return false;
        }
        var slot = root.rect;
        var bigArea = slot.width * slot.height * 0.4f;
        Occupied.Clear();
        AddGraphic(root, element.m_noteleport, bigArea);
        AddGraphic(root, element.m_food, bigArea);
        AddGraphic(root, element.m_queued, bigArea);
        AddGraphic(root, element.m_equiped, bigArea);
        AddText(root, element.m_quality, bigArea);
        AddText(root, element.m_amount, bigArea);
        var binding = element.transform.Find("binding");
        AddText(root, binding != null ? binding.GetComponent<TMP_Text>() : null, bigArea);
        if (element.m_durability != null)
        {
            AddRect(root, element.m_durability.transform as RectTransform, bigArea);
        }

        var best = Corner.TopRight;
        var bestHits = int.MaxValue;
        for (var c = Corner.TopRight; c <= Corner.BottomRight; c++)
        {
            var box = BadgeBox(slot, c);
            var hits = 0;
            foreach (var r in Occupied)
            {
                if (r.Overlaps(box))
                {
                    hits++;
                }
            }
            if (hits < bestHits)
            {
                best = c;
                bestHits = hits;
            }
            if (hits == 0)
            {
                break;
            }
        }
        marks.Corner = best;
        LogCorner(marks, slot, best, bestHits);
        return true;
    }

    private static Rect BadgeBox(Rect slot, Corner c)
    {
        var a = Anchor(c);
        var x = a.x > 0.5f ? slot.xMax - Inset - Size : slot.xMin + Inset;
        var y = a.y > 0.5f ? slot.yMax - Inset - Size : slot.yMin + Inset;
        return new Rect(x, y, Size, Size);
    }

    private static void AddGraphic(RectTransform root, Graphic g, float bigArea)
    {
        if (g != null)
        {
            AddRect(root, g.rectTransform, bigArea);
        }
    }

    private static bool LocalRect(RectTransform root, RectTransform rt, out Rect rect)
    {
        rect = default;
        if (rt == null)
        {
            return false;
        }
        rt.GetWorldCorners(Corners);
        var a = root.InverseTransformPoint(Corners[0]);
        var b = root.InverseTransformPoint(Corners[2]);
        if (float.IsNaN(a.x) || float.IsNaN(b.x) || float.IsInfinity(a.x) || float.IsInfinity(b.x))
        {
            return false;
        }
        rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        return true;
    }

    private static void AddRect(RectTransform root, RectTransform rt, float bigArea)
    {
        if (LocalRect(root, rt, out var r) && r.width * r.height <= bigArea && r.width > 0f && r.height > 0f)
        {
            Occupied.Add(r);
        }
    }

    // Text box often span whole slot width: me take only the glyph corner its alignment point to (about 18 x 14).
    private static void AddText(RectTransform root, TMP_Text text, float bigArea)
    {
        if (text == null || !LocalRect(root, text.rectTransform, out var r))
        {
            return;
        }
        var w = Mathf.Min(r.width, 18f);
        var h = Mathf.Min(r.height, 14f);
        float x, y;
        switch (text.horizontalAlignment)
        {
            case HorizontalAlignmentOptions.Left:
                x = r.xMin;
                break;
            case HorizontalAlignmentOptions.Right:
                x = r.xMax - w;
                break;
            default:
                x = r.center.x - w * 0.5f;
                break;
        }
        switch (text.verticalAlignment)
        {
            case VerticalAlignmentOptions.Top:
            case VerticalAlignmentOptions.Capline:
                y = r.yMax - h;
                break;
            case VerticalAlignmentOptions.Bottom:
            case VerticalAlignmentOptions.Baseline:
                y = r.yMin;
                break;
            default:
                y = r.center.y - h * 0.5f;
                break;
        }
        var box = new Rect(x, y, w, h);
        if (box.width * box.height <= bigArea)
        {
            Occupied.Add(box);
        }
    }

    private static void LogCorner(GridMarks marks, Rect slot, Corner corner, int hits)
    {
        var sb = new StringBuilder();
        sb.Append("Loot filter badges on the ").Append(marks.Name).Append(": corner ").Append(corner)
            .Append(hits > 0 ? " (every corner overlaps something; least covered)" : " (free)")
            .Append(", slot ").Append(slot.width.ToString("F0")).Append('x').Append(slot.height.ToString("F0"))
            .Append(", occupied boxes:");
        foreach (var r in Occupied)
        {
            sb.Append(" (").Append(r.xMin.ToString("F0")).Append(',').Append(r.yMin.ToString("F0")).Append(")-(")
                .Append(r.xMax.ToString("F0")).Append(',').Append(r.yMax.ToString("F0")).Append(')');
        }
        Log.Debug(sb.ToString());
    }

    // ---------------------------------------------------------------- sprites

    private static void EnsureSprites()
    {
        if (_ignored != null && _selected != null)
        {
            return;
        }
        if (_ignored == null)
        {
            _texIgnored = Draw(selected: false);
            _ignored = MakeSprite(_texIgnored);
        }
        if (_selected == null)
        {
            _texSelected = Draw(selected: true);
            _selected = MakeSprite(_texSelected);
        }
    }

    private static Sprite MakeSprite(Texture2D tex)
    {
        var sprite = Sprite.Create(tex, new Rect(0f, 0f, TexSize, TexSize), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        sprite.name = tex.name;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    // Disc (radius 15, dark 1.5 px rim) + white symbol, soft edges. Texture y go up.
    // Ignored: bar from top-left to bottom-right (no entry). Selected: check mark.
    private static Texture2D Draw(bool selected)
    {
        var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false)
        {
            name = selected ? "MC_LootFilter_Selected" : "MC_LootFilter_Ignored",
            filterMode = UnityEngine.FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var fill = selected ? new Color(0.25f, 0.72f, 0.30f, 1f) : new Color(0.86f, 0.20f, 0.16f, 1f);
        var rim = new Color(0.08f, 0.06f, 0.05f, 1f);
        var white = Color.white;
        var pixels = new Color[TexSize * TexSize];
        const float center = TexSize * 0.5f;
        const float radius = 15f;
        const float rimWidth = 1.5f;
        const float half = 2.3f; // symbol half thickness
        for (var y = 0; y < TexSize; y++)
        {
            for (var x = 0; x < TexSize; x++)
            {
                var px = x + 0.5f;
                var py = y + 0.5f;
                var d = Mathf.Sqrt((px - center) * (px - center) + (py - center) * (py - center));
                var alpha = Mathf.Clamp01(radius - d + 0.5f);
                if (alpha <= 0f)
                {
                    pixels[y * TexSize + x] = new Color(0f, 0f, 0f, 0f);
                    continue;
                }
                var c = Color.Lerp(fill, rim, Mathf.Clamp01(d - (radius - rimWidth) + 0.5f));
                float symbolDist;
                if (selected)
                {
                    symbolDist = Mathf.Min(SegmentDistance(px, py, 8f, 16f, 13f, 11f), SegmentDistance(px, py, 13f, 11f, 24f, 22f));
                }
                else
                {
                    // Line x + y = 32 through center; y up, so it run top-left to bottom-right. Kept inside the disc.
                    symbolDist = d > 11.5f ? 99f : Mathf.Abs(px + py - TexSize) / 1.41421356f;
                }
                var s = Mathf.Clamp01(half - symbolDist + 0.5f);
                c = Color.Lerp(c, white, s);
                c.a = alpha;
                pixels[y * TexSize + x] = c;
            }
        }
        tex.SetPixels(pixels);
        tex.Apply(false, true);
        return tex;
    }

    private static float SegmentDistance(float px, float py, float ax, float ay, float bx, float by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var t = Mathf.Clamp01(((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy));
        var cx = ax + t * dx - px;
        var cy = ay + t * dy - py;
        return Mathf.Sqrt(cx * cx + cy * cy);
    }
}
