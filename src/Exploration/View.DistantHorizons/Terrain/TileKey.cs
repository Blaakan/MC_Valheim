using System;

namespace MC.Exploration.ViewDistantHorizonsMod;

// Me = identity of one quadtree node. Level 0 = finest LOD; level-L tile have edge BaseTileSize * 2^L and cover
// world XZ [X*size, (X+1)*size) x [Y*size, (Y+1)*size).
internal readonly struct TileKey : IEquatable<TileKey>
{
    public readonly int Level;
    public readonly int X;
    public readonly int Y;

    public TileKey(int level, int x, int y)
    {
        Level = level;
        X = x;
        Y = y;
    }

    // Arithmetic shift floor correctly, also for negative coordinates.
    public TileKey Parent => new TileKey(Level + 1, X >> 1, Y >> 1);

    // Child index i in 0..3: bit0 = +x half, bit1 = +z half.
    public TileKey Child(int i) => new TileKey(Level - 1, (X << 1) + (i & 1), (Y << 1) + (i >> 1));

    public bool IsAncestorOf(TileKey other)
    {
        if (other.Level >= Level) return false;
        int d = Level - other.Level;
        return (other.X >> d) == X && (other.Y >> d) == Y;
    }

    public bool Equals(TileKey o) => Level == o.Level && X == o.X && Y == o.Y;
    public override bool Equals(object obj) => obj is TileKey k && Equals(k);
    public override int GetHashCode() => unchecked((Level * 73856093) ^ (X * 19349663) ^ (Y * 83492791));
    public override string ToString() => $"L{Level}({X},{Y})";
    public static bool operator ==(TileKey a, TileKey b) => a.Equals(b);
    public static bool operator !=(TileKey a, TileKey b) => !a.Equals(b);
}
