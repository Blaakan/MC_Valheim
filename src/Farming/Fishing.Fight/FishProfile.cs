using System;
using System.Collections.Generic;

namespace MC.Farming.FishingFightMod;

// How the fish marker move in the catch bar (Stardew Valley names, same maths in CatchBar).
internal enum FishMotion : byte
{
    Mixed,    // jump to new spots now and then, small darts
    Smooth,   // glide to new spots, no darts
    Dart,     // Mixed plus big sudden darts
    Sinker,   // Mixed plus slow drift down
    Floater,  // Mixed plus slow drift up
}

// Me = how hard one hooked fish is. Difficulty 0..100 (Stardew scale: 15 easy, 50 middle, 80+ hard) from the
// species (vanilla prefab name, by biome tier), plus each star, times the server's FishDifficulty. Fish of other mods:
// biome where it swim. Difficulty drive the marker in the bar, how often and how long the fish fight, how fast it take
// line (FightLogic).
internal readonly struct FishProfile
{
    internal readonly float Difficulty;
    internal readonly FishMotion Motion;

    internal FishProfile(float difficulty, FishMotion motion)
    {
        Difficulty = difficulty;
        Motion = motion;
    }

    // 0..1, for lerps.
    internal float D01 => Difficulty / 100f;

    // Hardness added by each star (quality above 1).
    internal const float StarStep = 8f;

    internal const float MinDifficulty = 5f;
    internal const float MaxDifficulty = 100f;

    // Vanilla fish, prefab name -> base difficulty, motion. Names from the game's prefab list (docs/game
    // farming-cooking.md section 8, design 1.4). Order = biome tier.
    private static readonly Dictionary<string, KeyValuePair<float, FishMotion>> Known =
        new Dictionary<string, KeyValuePair<float, FishMotion>>(StringComparer.OrdinalIgnoreCase)
        {
            { "Fish1", Pair(15f, FishMotion.Mixed) },       // Perch, Meadows
            { "Fish2", Pair(30f, FishMotion.Dart) },        // Pike, Meadows + Black Forest
            { "Fish5", Pair(35f, FishMotion.Mixed) },       // Trollfish, Black Forest
            { "Fish6", Pair(40f, FishMotion.Smooth) },      // Giant Herring, Swamp
            { "Fish3", Pair(45f, FishMotion.Smooth) },      // Tuna, Ocean
            { "Fish4_cave", Pair(50f, FishMotion.Dart) },   // Tetra, Mountain caves
            { "Fish7", Pair(55f, FishMotion.Sinker) },      // Grouper, Plains
            { "Fish8", Pair(55f, FishMotion.Mixed) },       // Coral Cod, Ocean
            { "Fish12", Pair(60f, FishMotion.Floater) },    // Pufferfish, deep Ocean / Mistlands shores
            { "Fish9", Pair(65f, FishMotion.Sinker) },      // Anglerfish, Mistlands
            { "Fish11", Pair(75f, FishMotion.Dart) },       // Magmafish, Ashlands
            { "Fish10", Pair(80f, FishMotion.Mixed) },      // Northern Salmon, Deep North
        };

    // Unknown species (other mods' fish): biome where it swim.
    internal static float BiomeDifficulty(Heightmap.Biome biome)
    {
        switch (biome)
        {
            case Heightmap.Biome.Meadows: return 15f;
            case Heightmap.Biome.BlackForest: return 30f;
            case Heightmap.Biome.Swamp: return 40f;
            case Heightmap.Biome.Ocean: return 45f;
            case Heightmap.Biome.Mountain: return 50f;
            case Heightmap.Biome.Plains: return 55f;
            case Heightmap.Biome.Mistlands: return 65f;
            case Heightmap.Biome.AshLands: return 75f;
            case Heightmap.Biome.DeepNorth: return 80f;
            default: return 40f;
        }
    }

    // Pure (self test hammer it). Quality 1 = no star.
    internal static FishProfile For(string prefabName, int quality, Heightmap.Biome biome, float multiplier)
    {
        float baseDifficulty;
        FishMotion motion;
        if (prefabName != null && Known.TryGetValue(prefabName, out var known))
        {
            baseDifficulty = known.Key;
            motion = known.Value;
        }
        else
        {
            baseDifficulty = BiomeDifficulty(biome);
            motion = FishMotion.Mixed;
        }
        var stars = Math.Max(0, quality - 1);
        var d = (baseDifficulty + stars * StarStep) * multiplier;
        if (float.IsNaN(d))
        {
            d = 40f;
        }
        return new FishProfile(Math.Max(MinDifficulty, Math.Min(MaxDifficulty, d)), motion);
    }

    internal static bool IsKnown(string prefabName) => prefabName != null && Known.ContainsKey(prefabName);

    private static KeyValuePair<float, FishMotion> Pair(float d, FishMotion m) => new KeyValuePair<float, FishMotion>(d, m);
}
