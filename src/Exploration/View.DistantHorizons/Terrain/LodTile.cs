using UnityEngine;

namespace MC.Exploration.ViewDistantHorizonsMod;

internal enum TileState : byte
{
    // Known needed, not yet handed to builder thread.
    Wanted,
    // Queued on HeightmapBuilder; heights get sampled off main thread.
    Requested,
    // Heights ready (held in LodTile.BuildData when direct fetch available); mesh not built yet.
    Ready,
    // GameObject + Heightmap mesh exist.
    Built,
    // Mesh building throw again and again; me retry only after long cooldown (see RetryAfter).
    Failed,
}

internal enum DisplayMode : byte
{
    Hidden,
    // Drawn at ground level; part of visible surface cut.
    Surface,
    // Drawn lowered under finer tiles: fill LOD-boundary cracks.
    Lowered,
}

// Me = one quadtree tile and its runtime objects.
internal sealed class LodTile
{
    public readonly TileKey Key;
    public readonly float Size;
    // World-space centre, y = 0. Must match transform position at build time.
    public readonly Vector3 Center;
    public readonly int Width;
    public readonly float Scale;

    public TileState State = TileState.Wanted;
    public DisplayMode Mode = DisplayMode.Hidden;
    // Heights computed exactly like real zones (see ExactTerrainBuilder), not distant approximation.
    public bool Exact;
    public GameObject Go;
    public Heightmap Heightmap;
    public MeshRenderer Renderer;
    // Heightmap's render mesh (skirt logic also edit it in place).
    public Mesh Mesh;
    // False while tile lie entirely under loaded real terrain.
    public bool Drawn = true;

    // Heights taken from builder thread, held till mesh built so they cannot be evicted.
    public HeightmapBuilder.HMBuildData BuildData;

    // Console material experiment: material tile was built with, kept while swap shown.
    public Material OriginalMaterial;
    public Material SwapMaterial;
    // Built with real-zone material (no near-camera dissolve; skirt logic cover it instead).
    public bool UsesZoneMaterial;

    // Hash of loaded real zones under tile last time its surface got pushed down; 0 = untouched.
    public int CoverSignature;
    // Original render-mesh vertices, kept while some pushed under loaded zones.
    public Vector3[] BaseVertices;
    public Vector3[] WorkVertices;

    public int FailCount;
    // Unscaled time: before it, me no retry tile after failure.
    public float RetryAfter;

    public LodTile(TileKey key, float size, Vector3 center, int width, float scale)
    {
        Key = key;
        Size = size;
        Center = center;
        Width = width;
        Scale = scale;
    }

    public float MinX => Center.x - Size * 0.5f;
    public float MinZ => Center.z - Size * 0.5f;
    public float MaxX => Center.x + Size * 0.5f;
    public float MaxZ => Center.z + Size * 0.5f;

    public override string ToString() => $"{Key} {State}/{Mode} size={Size} scale={Scale}";
}
