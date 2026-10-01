using System;

namespace MC.Exploration.DeepNorthAwakeningMod;

// Me = the area pattern (design 2.2, 2.4). World cut in cells of a jittered grid: one seed point per Size m square,
// moved up to Jitter x Size from the square centre (hash of world seed + square). A position belong to the cell of the
// nearest seed point among the 3 x 3 squares around it (Voronoi). Deep North cell = seed point in the Deep North
// (vanilla geometry, WorldGenerator.IsDeepnorth). Each cell has a fixed value in [0,1): awake at stage s when value <
// coverage(s), so a stage keep every cell of the stage before (pockets grow, then merge).
// Storm: each cell own schedule from world time (same on every game): cycles of mean/share s, per-cell phase, one storm
// per cycle with seeded length (min..max) and seeded start. Uptime = share (when length fit in the cycle).
// Pure math, integer hashes only: same answer on every machine. No Unity object. Network version: Size, Jitter and the
// salts are part of the wire (players must agree on the pattern).
internal static class Cells
{
    internal const float Size = 400f;
    internal const float Jitter = 0.35f;

    private const uint SaltX = 0x68E31DA4u;
    private const uint SaltZ = 0xB5297A4Du;
    private const uint SaltAwake = 0x1B56C4E9u;
    private const uint SaltPhase = 0x7FEB352Du;
    private const uint SaltLength = 0x846CA68Bu;
    private const uint SaltStart = 0x2C1B3C6Du;

    // Grid square index of a coordinate.
    internal static int Square(float v) => (int)Math.Floor(v / Size);

    // Cell id: square indices packed (16 bits each, world fit in +-32k squares many times over).
    internal static int Id(int i, int j) => (i << 16) | (j & 0xFFFF);

    internal static void FromId(int id, out int i, out int j)
    {
        i = id >> 16;
        j = (short)(id & 0xFFFF);
    }

    internal static uint Mix(uint x)
    {
        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        x *= 0x846CA68Bu;
        x ^= x >> 16;
        return x;
    }

    internal static uint Hash(int seed, int i, int j, uint salt)
    {
        var h = Mix((uint)seed * 0x9E3779B1u ^ salt);
        h = Mix(h ^ (uint)i * 0x85EBCA77u);
        h = Mix(h ^ (uint)j * 0xC2B2AE3Du);
        return h;
    }

    private static uint Hash(int seed, int i, int j, long n, uint salt)
    {
        var h = Hash(seed, i, j, salt);
        h = Mix(h ^ (uint)n * 0x27D4EB2Fu);
        h = Mix(h ^ (uint)(n >> 32) * 0x165667B1u);
        return h;
    }

    // Top 24 bits as [0,1).
    internal static float Unit(uint h) => (h >> 8) * (1f / 16777216f);

    internal static void SeedPoint(int seed, int i, int j, out float x, out float z)
    {
        var jx = (Unit(Hash(seed, i, j, SaltX)) - 0.5f) * 2f * Jitter;
        var jz = (Unit(Hash(seed, i, j, SaltZ)) - 0.5f) * 2f * Jitter;
        x = (i + 0.5f + jx) * Size;
        z = (j + 0.5f + jz) * Size;
    }

    // Cell of a world position (x, z). Returns the id, gives the square indices.
    internal static int At(int seed, float x, float z, out int ci, out int cj)
    {
        var si = Square(x);
        var sj = Square(z);
        ci = si;
        cj = sj;
        var best = float.MaxValue;
        for (var di = -1; di <= 1; di++)
        {
            for (var dj = -1; dj <= 1; dj++)
            {
                SeedPoint(seed, si + di, sj + dj, out var px, out var pz);
                var dx = px - x;
                var dz = pz - z;
                var d = dx * dx + dz * dz;
                if (d < best)
                {
                    best = d;
                    ci = si + di;
                    cj = sj + dj;
                }
            }
        }
        return Id(ci, cj);
    }

    internal static int At(int seed, float x, float z) => At(seed, x, z, out _, out _);

    // Seed points of a block of squares, hashed once (map overlay: one lookup per map pixel). At = same answer as
    // Cells.At (same floats, same order, same ties); a point whose 3 x 3 squares leave the block = Cells.At.
    internal sealed class Grid
    {
        private readonly int _seed;
        private readonly int _i0;
        private readonly int _j0;
        private readonly int _i1;
        private readonly int _j1;
        private readonly int _h;
        private readonly float[] _x;
        private readonly float[] _z;

        internal Grid(int seed, int i0, int j0, int i1, int j1)
        {
            _seed = seed;
            _i0 = i0;
            _j0 = j0;
            _i1 = i1;
            _j1 = j1;
            _h = j1 - j0 + 1;
            var n = (i1 - i0 + 1) * _h;
            _x = new float[n];
            _z = new float[n];
            for (var i = i0; i <= i1; i++)
            {
                for (var j = j0; j <= j1; j++)
                {
                    SeedPoint(seed, i, j, out var px, out var pz);
                    var k = (i - i0) * _h + (j - j0);
                    _x[k] = px;
                    _z[k] = pz;
                }
            }
        }

        internal int At(float x, float z)
        {
            var si = Square(x);
            var sj = Square(z);
            if (si - 1 < _i0 || si + 1 > _i1 || sj - 1 < _j0 || sj + 1 > _j1)
            {
                return Cells.At(_seed, x, z);
            }
            var ci = si;
            var cj = sj;
            var best = float.MaxValue;
            for (var di = -1; di <= 1; di++)
            {
                for (var dj = -1; dj <= 1; dj++)
                {
                    var k = (si + di - _i0) * _h + (sj + dj - _j0);
                    var dx = _x[k] - x;
                    var dz = _z[k] - z;
                    var d = dx * dx + dz * dz;
                    if (d < best)
                    {
                        best = d;
                        ci = si + di;
                        cj = sj + dj;
                    }
                }
            }
            return Id(ci, cj);
        }
    }

    internal static bool IsDeepNorthCell(int seed, int i, int j)
    {
        SeedPoint(seed, i, j, out var x, out var z);
        return WorldGenerator.IsDeepnorth(x, z);
    }

    // Fixed value of a cell: awake while it is below the coverage.
    internal static float AwakeValue(int seed, int i, int j) => Unit(Hash(seed, i, j, SaltAwake));

    // coverage 0..1. Coverage 0 = none (value 0 must not count).
    internal static bool IsAwake(int seed, int i, int j, float coverage)
    {
        return coverage > 0f && AwakeValue(seed, i, j) < coverage && IsDeepNorthCell(seed, i, j);
    }

    internal static bool IsAwake(int seed, int id, float coverage)
    {
        FromId(id, out var i, out var j);
        return IsAwake(seed, i, j, coverage);
    }

    // share 0..1, lengths in seconds (min <= max). time = world seconds (ZNet.GetTimeSeconds).
    internal static bool IsStorming(int seed, int i, int j, double time, float share, float minSeconds, float maxSeconds)
    {
        if (share <= 0f || maxSeconds <= 0f)
        {
            return false;
        }
        if (share >= 1f)
        {
            return true;
        }
        double mean = (minSeconds + maxSeconds) * 0.5f;
        var cycle = mean / share;
        var t = time + Unit(Hash(seed, i, j, SaltPhase)) * cycle;
        var n = (long)Math.Floor(t / cycle);
        var u = t - n * cycle;
        var length = minSeconds + (maxSeconds - minSeconds) * (double)Unit(Hash(seed, i, j, n, SaltLength));
        if (length > cycle)
        {
            length = cycle;
        }
        var start = Unit(Hash(seed, i, j, n, SaltStart)) * (cycle - length);
        return u >= start && u < start + length;
    }

    internal static bool IsStorming(int seed, int id, double time, float share, float minSeconds, float maxSeconds)
    {
        FromId(id, out var i, out var j);
        return IsStorming(seed, i, j, time, share, minSeconds, maxSeconds);
    }

    // Square range that can hold Deep North cells (world edge 10500 m; seed jitter stays inside one extra square).
    internal const int WorldSquares = 28;
}
