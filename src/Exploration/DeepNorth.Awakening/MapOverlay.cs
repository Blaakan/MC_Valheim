using System;
using System.Collections.Generic;
using MC.Shared;
using Unity.Collections;
using UnityEngine;

namespace MC.Exploration.DeepNorthAwakeningMod;

// Me = the invaded areas on the map (design 2.8, rule MapAreas). No pin, no extra image: me tint the vanilla map colour
// texture (Minimap.m_mapTexture). Large map and minimap both draw it under the vanilla fog of war, so only explored
// places show it, touching areas merge into one region (one tint per pixel, never added twice), and a cleared area get
// its own colours back. Water is drawn by the map shader from height, so me only touch land.
//   Scan:   once per map texture, ScanPixelsPerFrame a frame: each pixel of the north band that is Deep North land
//           (vanilla line WorldGenerator.IsDeepnorth, inside the world, above water in the map height texture) get its
//           cell (Cells.Grid) and keep its colour.
//   Paint:  when what to show change (ShowAreas, awake share of the stage, seed, Kall, cleared list): pixel of an awake cell not cleared =
//           purple fill, or darker purple edge when next to Deep North land not shown; others = own colour. RGB24 bytes
//           written in place, one Apply.
//   Base:   the game made the texture again (Minimap.GenerateWorldMap, cache load) = fresh colours: scan again. The map
//           cache on disk is written from the generated arrays, never from the texture: the tint is never saved.
//   Off:    rules off, north asleep, rules pending, feature off: own colours back.
internal static class MapOverlay
{
    private const int NoCell = int.MinValue;
    private const int ScanPixelsPerFrame = 40000;

    // Lowest world z any Deep North land can have: line |(x, z + 4000)| > 12000 + 100 x angle (angle -1..1), inside
    // the water edge (10500 m): z > (11900^2 - 4000^2 - 10500^2) / 8000 = 1920 m. Margin below.
    private const float BandMinZ = 1800f;

    internal static readonly Color32 FillColor = new Color32(150, 70, 215, 255);
    internal static readonly Color32 EdgeColor = new Color32(95, 25, 160, 255);
    internal const float FillMix = 0.6f;
    internal const float EdgeMix = 0.9f;

    private static Minimap _map;
    private static Texture2D _tex;
    private static bool _failed;
    private static int _size;
    private static float _pixel;
    private static int _half;
    private static float _halfPixel;

    // Box of map pixels that can be Deep North land: rows _r0.., columns _c0.., _bw x _bh.
    private static int _r0;
    private static int _c0;
    private static int _bw;
    private static int _bh;
    private static int[] _cells;
    private static byte[] _base;
    private static Cells.Grid _grid;
    private static int _scanNext;
    private static bool _ready;
    private static bool _tinted;

    // Paint running now.
    private static readonly Dictionary<int, bool> ShownCache = new Dictionary<int, bool>();
    private static AwakeningRules _paintRules;
    private static int _paintSeed;
    private static float _paintCoverage;

    // What the last paint showed.
    private static bool _painted;
    private static bool _show;
    private static float _coverage;
    private static int _seed;
    private static bool _kall;
    private static int _clearedVersion;

    internal static bool Ready => _ready;

    internal static bool Tinted => _tinted;

    // Minimap.Update postfix (every frame on a game with a map): cheap compares when nothing changed.
    internal static void Update(Minimap map)
    {
        if (_failed || map == null)
        {
            return;
        }
        if (!ReferenceEquals(map, _map) || _tex != map.m_mapTexture)
        {
            // New world or new texture: nothing of ours on it.
            Reset(map);
        }
        if (!map.m_hasGenerated || _tex == null)
        {
            return;
        }
        if (!_ready)
        {
            Scan();
            return;
        }
        // Only what Paint read: a storm or star setting (new rules object) never repaint.
        var rules = ServerRules.Current;
        var show = rules != null && !rules.IsPending && rules.MapAreas && WorldState.Stage >= 1;
        var coverage = show ? rules.Coverage(WorldState.Stage) : 0f;
        if (_painted && show == _show && coverage == _coverage && WorldState.Seed == _seed
            && WorldState.KallDefeated == _kall && HeldCells.ClearedVersion == _clearedVersion)
        {
            return;
        }
        _painted = true;
        _show = show;
        _coverage = coverage;
        _seed = WorldState.Seed;
        _kall = WorldState.KallDefeated;
        _clearedVersion = HeldCells.ClearedVersion;
        Paint(show ? rules : null);
    }

    // Minimap.GenerateWorldMap / cache load postfix: the texture hold fresh colours (no tint).
    internal static void BaseChanged(Minimap map)
    {
        if (ReferenceEquals(map, _map))
        {
            Reset(map);
        }
    }

    // A patch threw: stop for this world (no exception every frame).
    internal static void Fail()
    {
        _failed = true;
    }

    private static void Reset(Minimap map)
    {
        _map = map;
        _tex = map != null ? map.m_mapTexture : null;
        _cells = null;
        _base = null;
        _grid = null;
        _scanNext = 0;
        _ready = false;
        _tinted = false;
        _painted = false;
    }

    private static bool Begin()
    {
        var wg = WorldGenerator.instance;
        var seed = WorldState.Seed;
        if (wg == null || seed == 0 || ZoneSystem.instance == null)
        {
            return false;
        }
        _size = _map.m_textureSize;
        _pixel = _map.m_pixelSize;
        if (_tex.format != TextureFormat.RGB24 || !_tex.isReadable || _tex.width != _size || _tex.height != _size)
        {
            Log.Warning($"The map texture is {_tex.format} {_tex.width}x{_tex.height} (readable {_tex.isReadable}), not "
                        + "what this mod expects: the invaded areas are not shown on the map.");
            _failed = true;
            return false;
        }
        if (_tex.GetRawTextureData<byte>().Length < _size * _size * 3)
        {
            Log.Warning("The map texture data is shorter than expected: the invaded areas are not shown on the map.");
            _failed = true;
            return false;
        }
        _half = _size / 2;
        _halfPixel = _pixel / 2f;
        var edge = WorldGenerator.waterEdge;
        _r0 = Mathf.Clamp(Row(BandMinZ), 0, _size - 1);
        var r1 = Mathf.Clamp(Row(edge) + 1, 0, _size - 1);
        _c0 = Mathf.Clamp(Row(-edge) - 1, 0, _size - 1);
        var c1 = Mathf.Clamp(Row(edge) + 1, 0, _size - 1);
        _bh = r1 - _r0 + 1;
        _bw = c1 - _c0 + 1;
        _cells = new int[_bw * _bh];
        _base = new byte[_bw * _bh * 3];
        _grid = new Cells.Grid(seed, Cells.Square(-edge) - 2, Cells.Square(BandMinZ) - 2, Cells.Square(edge) + 2,
            Cells.Square(edge) + 2);
        _scanNext = 0;
        return true;
    }

    // Texture row (or column) of a world coordinate: inverse of vanilla's pixel centre (i - size/2) x pixel + pixel/2.
    private static int Row(float world) => Mathf.FloorToInt((world - _halfPixel) / _pixel) + _half;

    private static void Scan()
    {
        if (_cells == null && !Begin())
        {
            return;
        }
        var raw = _tex.GetRawTextureData<byte>();
        var heights = HeightData(out var haveHeights);
        var water = ZoneSystem.instance.m_waterLevel;
        var total = _bw * _bh;
        var end = Math.Min(total, _scanNext + ScanPixelsPerFrame);
        for (var k = _scanNext; k < end; k++)
        {
            var i = _r0 + k / _bw;
            var j = _c0 + k % _bw;
            var wx = (float)(j - _half) * _pixel + _halfPixel;
            var wy = (float)(i - _half) * _pixel + _halfPixel;
            var t = i * _size + j;
            var cell = NoCell;
            if (wx * wx + wy * wy <= WorldGenerator.waterEdgeSqr && WorldGenerator.IsDeepnorth(wx, wy)
                                                                  && (!haveHeights || Mathf.HalfToFloat(heights[t]) > water))
            {
                cell = _grid.At(wx, wy);
            }
            _cells[k] = cell;
            _base[k * 3] = raw[t * 3];
            _base[k * 3 + 1] = raw[t * 3 + 1];
            _base[k * 3 + 2] = raw[t * 3 + 2];
        }
        _scanNext = end;
        if (end >= total)
        {
            _ready = true;
            _grid = null;
        }
    }

    // Map height texture (RHalf, world heights): land test. Other format (a mod) = no test, every pixel counts.
    private static NativeArray<ushort> HeightData(out bool have)
    {
        var h = _map.m_heightTexture;
        if (h != null && h.isReadable && h.format == TextureFormat.RHalf && h.width == _size && h.height == _size)
        {
            have = true;
            return h.GetRawTextureData<ushort>();
        }
        have = false;
        return default;
    }

    // rules null = show nothing (own colours back).
    private static void Paint(AwakeningRules rules)
    {
        if (rules == null && !_tinted)
        {
            return;
        }
        var raw = _tex.GetRawTextureData<byte>();
        _paintRules = rules;
        _paintSeed = WorldState.Seed;
        _paintCoverage = rules != null ? rules.Coverage(WorldState.Stage) : 0f;
        ShownCache.Clear();
        var any = false;
        var total = _bw * _bh;
        for (var k = 0; k < total; k++)
        {
            var cell = _cells[k];
            if (cell == NoCell)
            {
                continue;
            }
            var bi = k / _bw;
            var bj = k - bi * _bw;
            var t = ((_r0 + bi) * _size + _c0 + bj) * 3;
            if (!Shown(cell))
            {
                raw[t] = _base[k * 3];
                raw[t + 1] = _base[k * 3 + 1];
                raw[t + 2] = _base[k * 3 + 2];
                continue;
            }
            any = true;
            var edge = Hidden(bi - 1, bj, cell) || Hidden(bi + 1, bj, cell) || Hidden(bi, bj - 1, cell) || Hidden(bi, bj + 1, cell);
            var c = edge ? EdgeColor : FillColor;
            var m = edge ? EdgeMix : FillMix;
            raw[t] = Mix(_base[k * 3], c.r, m);
            raw[t + 1] = Mix(_base[k * 3 + 1], c.g, m);
            raw[t + 2] = Mix(_base[k * 3 + 2], c.b, m);
        }
        _tex.Apply(false);
        _tinted = any;
        _paintRules = null;
        ShownCache.Clear();
    }

    // Cell shown by the paint running now (cached per paint).
    private static bool Shown(int cell)
    {
        if (_paintRules == null)
        {
            return false;
        }
        if (!ShownCache.TryGetValue(cell, out var s))
        {
            s = Cells.IsAwake(_paintSeed, cell, _paintCoverage) && !HeldCells.IsCleared(cell);
            ShownCache[cell] = s;
        }
        return s;
    }

    // Neighbour (box row bi, column bj) is Deep North land of another cell not shown: the shown pixel is an edge.
    private static bool Hidden(int bi, int bj, int cell)
    {
        if (bi < 0 || bi >= _bh || bj < 0 || bj >= _bw)
        {
            return false;
        }
        var n = _cells[bi * _bw + bj];
        return n != NoCell && n != cell && !Shown(n);
    }

    // Pure (self test): channel blend.
    internal static byte Mix(byte from, byte to, float t) => (byte)Mathf.RoundToInt(from + (to - from) * t);

    // Feature off (patches still on): own colours back, then forget.
    internal static void Restore()
    {
        if (_tinted && _tex != null && _cells != null && _ready)
        {
            Paint(null);
        }
        Forget();
    }

    // World end, feature on or off: forget (the texture go with the world, or was given back by Restore).
    internal static void Forget()
    {
        Reset(null);
        _failed = false;
    }

#if DEBUG
    // Self test: map pixel at a world position. -1 = not scanned or outside the band, 0 = not Deep North land,
    // 1 = own colour, 2 = tinted.
    internal static int TestPixel(Vector3 position)
    {
        if (!_ready || _tex == null)
        {
            return -1;
        }
        var i = Row(position.z);
        var j = Row(position.x);
        var bi = i - _r0;
        var bj = j - _c0;
        if (bi < 0 || bi >= _bh || bj < 0 || bj >= _bw)
        {
            return -1;
        }
        var k = bi * _bw + bj;
        if (_cells[k] == NoCell)
        {
            return 0;
        }
        var raw = _tex.GetRawTextureData<byte>();
        var t = (i * _size + j) * 3;
        return raw[t] == _base[k * 3] && raw[t + 1] == _base[k * 3 + 1] && raw[t + 2] == _base[k * 3 + 2] ? 1 : 2;
    }

    // Self test: centre of a Deep North land pixel whose cell is (not) awake at this coverage; false = none.
    internal static bool TestFind(float coverage, bool awake, out Vector3 position)
    {
        position = Vector3.zero;
        if (!_ready)
        {
            return false;
        }
        var seed = WorldState.Seed;
        for (var k = 0; k < _cells.Length; k += 97)
        {
            var cell = _cells[k];
            if (cell == NoCell || Cells.IsAwake(seed, cell, coverage) != awake)
            {
                continue;
            }
            var i = _r0 + k / _bw;
            var j = _c0 + k % _bw;
            position = new Vector3((float)(j - _half) * _pixel + _halfPixel, 0f, (float)(i - _half) * _pixel + _halfPixel);
            // Middle of the cell's pixels: its neighbours share the cell (not an edge pixel).
            if (Same(k, cell))
            {
                return true;
            }
        }
        return false;
    }

    private static bool Same(int k, int cell)
    {
        var bi = k / _bw;
        var bj = k % _bw;
        for (var di = -2; di <= 2; di++)
        {
            for (var dj = -2; dj <= 2; dj++)
            {
                var ni = bi + di;
                var nj = bj + dj;
                if (ni < 0 || ni >= _bh || nj < 0 || nj >= _bw || _cells[ni * _bw + nj] != cell)
                {
                    return false;
                }
            }
        }
        return true;
    }

    // Self test: colour the game's map texture holds now at a world position, read from the texture itself (works after
    // Restore and Forget too). False = no map texture or outside it.
    internal static bool TestRawColor(Vector3 position, out Color32 color)
    {
        color = default;
        var map = Minimap.instance;
        var tex = map != null ? map.m_mapTexture : null;
        if (tex == null || !tex.isReadable || tex.format != TextureFormat.RGB24)
        {
            return false;
        }
        var size = map.m_textureSize;
        var pixel = map.m_pixelSize;
        var i = Mathf.FloorToInt((position.z - pixel / 2f) / pixel) + size / 2;
        var j = Mathf.FloorToInt((position.x - pixel / 2f) / pixel) + size / 2;
        var raw = tex.GetRawTextureData<byte>();
        if (i < 0 || i >= size || j < 0 || j >= size || raw.Length < size * size * 3)
        {
            return false;
        }
        var t = (i * size + j) * 3;
        color = new Color32(raw[t], raw[t + 1], raw[t + 2], 255);
        return true;
    }

    // Self test: own colour the scan kept for the pixel at a world position (also for sea pixels). False = not scanned
    // or outside the band.
    internal static bool TestOwnColor(Vector3 position, out Color32 color)
    {
        color = default;
        if (!_ready || _base == null)
        {
            return false;
        }
        var bi = Row(position.z) - _r0;
        var bj = Row(position.x) - _c0;
        if (bi < 0 || bi >= _bh || bj < 0 || bj >= _bw)
        {
            return false;
        }
        var k = (bi * _bw + bj) * 3;
        color = new Color32(_base[k], _base[k + 1], _base[k + 2], 255);
        return true;
    }

    // Self test: pixels of the band whose colour is not their own now (land or sea).
    internal static int TestChangedPixels(bool landOnly)
    {
        if (!_ready || _tex == null)
        {
            return -1;
        }
        var raw = _tex.GetRawTextureData<byte>();
        var n = 0;
        var total = _bw * _bh;
        for (var k = 0; k < total; k++)
        {
            if (landOnly && _cells[k] == NoCell)
            {
                continue;
            }
            var t = ((_r0 + k / _bw) * _size + _c0 + k % _bw) * 3;
            if (raw[t] != _base[k * 3] || raw[t + 1] != _base[k * 3 + 1] || raw[t + 2] != _base[k * 3 + 2])
            {
                n++;
            }
        }
        return n;
    }

    // Self test: kinds of pixel for the paint checks, at a coverage (before Kall: no cleared area).
    internal const int TestSpotEdge = 1; // shown land next to Deep North land of another area that is not shown
    internal const int TestSpotSeam = 2; // shown land next to land of another shown area, no hidden land around
    internal const int TestSpotSea = 3;  // sea (no land) inside the Deep North line, in an awake area

    // Self test: centre of a pixel of that kind; false = none on this map.
    internal static bool TestFindSpot(float coverage, int kind, out Vector3 position)
    {
        position = Vector3.zero;
        if (!_ready || _cells == null)
        {
            return false;
        }
        var seed = WorldState.Seed;
        var awake = new Dictionary<int, bool>();
        bool AwakeCell(int cell)
        {
            if (!awake.TryGetValue(cell, out var a))
            {
                a = Cells.IsAwake(seed, cell, coverage);
                awake[cell] = a;
            }
            return a;
        }
        var total = _bw * _bh;
        for (var k = 0; k < total; k++)
        {
            var bi = k / _bw;
            var bj = k - bi * _bw;
            var wx = (float)(_c0 + bj - _half) * _pixel + _halfPixel;
            var wz = (float)(_r0 + bi - _half) * _pixel + _halfPixel;
            var cell = _cells[k];
            if (kind == TestSpotSea)
            {
                if (cell != NoCell || wx * wx + wz * wz > WorldGenerator.waterEdgeSqr || !WorldGenerator.IsDeepnorth(wx, wz)
                    || !AwakeCell(Cells.At(seed, wx, wz)))
                {
                    continue;
                }
                position = new Vector3(wx, 0f, wz);
                return true;
            }
            if (cell == NoCell || !AwakeCell(cell))
            {
                continue;
            }
            var hidden = false;
            var otherShown = false;
            for (var d = 0; d < 4; d++)
            {
                var ni = bi + (d == 0 ? -1 : d == 1 ? 1 : 0);
                var nj = bj + (d == 2 ? -1 : d == 3 ? 1 : 0);
                if (ni < 0 || ni >= _bh || nj < 0 || nj >= _bw)
                {
                    continue;
                }
                var n = _cells[ni * _bw + nj];
                if (n == NoCell || n == cell)
                {
                    continue;
                }
                if (AwakeCell(n))
                {
                    otherShown = true;
                }
                else
                {
                    hidden = true;
                }
            }
            if (kind == TestSpotEdge ? hidden : otherShown && !hidden)
            {
                position = new Vector3(wx, 0f, wz);
                return true;
            }
        }
        return false;
    }

    // Self test: Deep North land pixels found by the scan.
    internal static int TestLandPixels()
    {
        if (!_ready)
        {
            return 0;
        }
        var n = 0;
        foreach (var c in _cells)
        {
            if (c != NoCell)
            {
                n++;
            }
        }
        return n;
    }
#endif
}
