using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.ViewDistantHorizonsMod;

// Me = build request whose heights come out exactly like real zone heightmaps (per 64 m zone: four corner biomes'
// height functions blended with smoothstep weights, plus paint mask), not like distant-LOD approximation (one biome
// per vertex, then smoothed). Finest LOD levels use me, so far terrain meet real terrain with no step.
internal sealed class ExactBuildData : HeightmapBuilder.HMBuildData
{
    public ExactBuildData(Vector3 center, int width, float scale, WorldGenerator worldGen)
        : base(center, width, scale, true, worldGen)
    {
    }
}

// Me run exact builds on game's own HeightmapBuilder thread (only thread besides main thread that may use world
// generator): me hand request straight to its queue and compute it in prefix on HeightmapBuilder.Build.
// Private members not reachable = me fall back to nothing.
internal static class ExactTerrainBuilder
{
    private static int s_computed;
    private static int s_errorLogged;

    public static int Computed => s_computed;

    // Me queue exact build unless equal request already queued or ready. Main thread.
    public static bool TryEnqueue(HeightmapBuilder builder, Vector3 center, int width, float scale, WorldGenerator worldGen)
    {
        if (builder == null || worldGen == null) return false;
        object gate = builder.m_lock; // null once builder disposed (quit)
        List<HeightmapBuilder.HMBuildData> queue = builder.m_toBuild;
        List<HeightmapBuilder.HMBuildData> ready = builder.m_ready;
        if (gate == null || queue == null || ready == null) return false;
        lock (gate)
        {
            for (int i = 0; i < ready.Count; i++)
                if (ready[i].IsEqual(center, width, scale, true, worldGen)) return true;
            for (int i = 0; i < queue.Count; i++)
                if (queue[i].IsEqual(center, width, scale, true, worldGen)) return true;
            queue.Add(new ExactBuildData(center, width, scale, worldGen));
        }
        return true;
    }

    // Builder thread (HeightmapBuilder.Build prefix). Anything thrown here kill game's terrain thread, so never let it
    // escape. True = me handled it (exact job): vanilla Build must be skipped.
    internal static bool TryBuild(HeightmapBuilder.HMBuildData data)
    {
        if (!(data is ExactBuildData exact)) return false;
        try
        {
            Compute(exact);
        }
        catch (Exception e)
        {
            // PatchGuard = main-thread only: me use own once-flag here.
            if (System.Threading.Interlocked.Exchange(ref s_errorLogged, 1) == 0)
                Log.Error($"Exact terrain build failed at {exact.m_center} (further errors hidden): {e}");
            Fallback(exact);
        }
        return true;
    }

    private static float SmoothStep(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    private struct ZoneCorners
    {
        public Heightmap.Biome B00, B10, B01, B11;
        public bool Uniform;
        public float OriginX, OriginZ;
    }

    private static void Compute(ExactBuildData data)
    {
        int num = data.m_width + 1;
        int num2 = num * num;
        WorldGenerator wg = data.m_worldGen;
        Vector3 origin = data.m_center + new Vector3(data.m_width * data.m_scale * -0.5f, 0f, data.m_width * data.m_scale * -0.5f);
        float span = data.m_width * data.m_scale;

        data.m_cornerBiomes = new BiomeSector[4];
        data.m_cornerBiomes[0] = wg.GetBiomeSector(origin.x, origin.z);
        data.m_cornerBiomes[1] = wg.GetBiomeSector(origin.x + span, origin.z);
        data.m_cornerBiomes[2] = wg.GetBiomeSector(origin.x, origin.z + span);
        data.m_cornerBiomes[3] = wg.GetBiomeSector(origin.x + span, origin.z + span);

        data.m_baseHeights = new List<float>(num2);
        for (int i = 0; i < num2; i++) data.m_baseHeights.Add(0f);
        data.m_baseMask = new Color[num2];

        var zones = new Dictionary<long, ZoneCorners>();
        for (int k = 0; k < num; k++)
        {
            float wy = origin.z + k * data.m_scale;
            for (int l = 0; l < num; l++)
            {
                float wx = origin.x + l * data.m_scale;
                // ZoneSystem.GetZone: zone centres sit at multiples of 64 m, zone heightmap start 32 m before.
                int zx = Mathf.FloorToInt((wx + 32f) / 64f);
                int zy = Mathf.FloorToInt((wy + 32f) / 64f);
                long zkey = ((long)zx << 32) ^ (uint)zy;
                if (!zones.TryGetValue(zkey, out ZoneCorners zc))
                {
                    zc.OriginX = zx * 64f - 32f;
                    zc.OriginZ = zy * 64f - 32f;
                    zc.B00 = wg.GetBiome(zc.OriginX, zc.OriginZ);
                    zc.B10 = wg.GetBiome(zc.OriginX + 64f, zc.OriginZ);
                    zc.B01 = wg.GetBiome(zc.OriginX, zc.OriginZ + 64f);
                    zc.B11 = wg.GetBiome(zc.OriginX + 64f, zc.OriginZ + 64f);
                    zc.Uniform = zc.B00 == zc.B10 && zc.B00 == zc.B01 && zc.B00 == zc.B11;
                    zones[zkey] = zc;
                }

                float h;
                Color mask;
                if (zc.Uniform)
                {
                    h = wg.GetBiomeHeight(zc.B00, wx, wy, out mask);
                }
                else
                {
                    // Same blend as HeightmapBuilder.Build do for real zones (t2 across x, t across z).
                    float t2 = SmoothStep((wx - zc.OriginX) / 64f);
                    float t = SmoothStep((wy - zc.OriginZ) / 64f);
                    float h00 = wg.GetBiomeHeight(zc.B00, wx, wy, out Color m00);
                    float h10 = wg.GetBiomeHeight(zc.B10, wx, wy, out Color m10);
                    float h01 = wg.GetBiomeHeight(zc.B01, wx, wy, out Color m01);
                    float h11 = wg.GetBiomeHeight(zc.B11, wx, wy, out Color m11);
                    h = Mathf.Lerp(Mathf.Lerp(h00, h10, t2), Mathf.Lerp(h01, h11, t2), t);
                    mask = Color.Lerp(Color.Lerp(m00, m10, t2), Color.Lerp(m01, m11, t2), t);
                }
                data.m_baseHeights[k * num + l] = h;
                data.m_baseMask[k * num + l] = mask;
            }
        }
        System.Threading.Interlocked.Increment(ref s_computed);
    }

    private static void Fallback(ExactBuildData data)
    {
        int num2 = (data.m_width + 1) * (data.m_width + 1);
        if (data.m_cornerBiomes == null)
            data.m_cornerBiomes = new[] { BiomeSector.EmptyMeadows, BiomeSector.EmptyMeadows, BiomeSector.EmptyMeadows, BiomeSector.EmptyMeadows };
        if (data.m_baseHeights == null || data.m_baseHeights.Count != num2)
        {
            data.m_baseHeights = new List<float>(num2);
            for (int i = 0; i < num2; i++) data.m_baseHeights.Add(0f);
        }
        if (data.m_baseMask == null || data.m_baseMask.Length != num2) data.m_baseMask = new Color[num2];
    }
}
