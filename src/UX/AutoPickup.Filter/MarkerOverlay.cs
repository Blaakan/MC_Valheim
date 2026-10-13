using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MC.UX.AutoPickupFilterMod;

// Me = the small badge on slots whose item type is in the active list: red "no entry" disc (Ignored, Skip ignored
// mode), green check disc (Selected, Only selected mode), nothing in Everything mode. Only player grid and container
// panel grid. Badge = Image child of the slot, made the first time a slot need it, raycastTarget off (never eat
// clicks or hover). Sprites drawn in code once (no asset bundle). Per frame: loop live item list, int lookups,
// Image.enabled set only when it change; no allocation.
// Place = left edge of the slot, half-way up, same for every slot. Game slot parts all sit in a corner or at the
// bottom (slot prefab, measured in runs on game 1.0.17; slot is 64 wide): hotbar number top-left (first row of your
// grid, its text box end 22.4 under the slot top), quality number / no-portal icon / food icon top-right, stack
// amount and durability bar along the bottom. Left middle (24 to 40 under the top) is the one place none of them
// use. 0.1.0 picked a corner and sat on the hotbar number.
internal static class MarkerOverlay
{
    private const string MarkName = "MC_LootFilterMark";
    private const float Size = 16f;
    private const float Inset = 3f;
    private const int TexSize = 32;

    private sealed class GridMarks
    {
        public InventoryGrid Grid;
        public InventoryElement First;
        public int Count = -1;
        public Image[] Marks = new Image[0];
        public bool[] Shown = new bool[0];
        public bool[] Want = new bool[0];
        public Sprite ShownSprite;
        public bool AnyShown;
    }

    private static readonly GridMarks PlayerMarks = new GridMarks();
    private static readonly GridMarks ContainerMarks = new GridMarks();

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
                badge = CreateBadge(element);
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
    }

    // Anchored on the slot like the game's own slot parts: left edge, half-way up (why there: top of file).
    private static Image CreateBadge(InventoryElement element)
    {
        var go = new GameObject(MarkName, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(element.transform, false);
        var anchor = new Vector2(0f, 0.5f);
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.sizeDelta = new Vector2(Size, Size);
        rt.anchoredPosition = new Vector2(Inset, 0f);
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        var image = go.AddComponent<Image>();
        image.raycastTarget = false;
        image.preserveAspect = true;
        image.enabled = false;
        rt.SetAsLastSibling();
        return image;
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
