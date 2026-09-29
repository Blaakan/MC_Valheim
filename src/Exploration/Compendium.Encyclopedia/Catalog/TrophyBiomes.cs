using System;
using System.Collections.Generic;

namespace MC.Exploration.CompendiumEncyclopediaMod;

/// <summary>
/// Me = trophy prefab → biome, last fallback for creature habitat (bosses, dungeon-only creatures: no spawn entry).
/// Copy of the trophy rows of Sort Chest's curated BiomeTable (src/UX/Container.Sort/BiomeTable.cs, 1.0.16 names all
/// checked there). KEEP IN SYNC with that table when it change (design decision 20: copy, not share).
/// </summary>
internal static class TrophyBiomes
{
    private static readonly string[] Meadows = { "TrophyBoar", "TrophyDeer", "TrophyNeck", "TrophyEikthyr" };

    private static readonly string[] BlackForest =
    {
        "TrophyGreydwarf", "TrophyGreydwarfBrute", "TrophyGreydwarfShaman", "TrophyForestTroll", "TrophySkeleton",
        "TrophySkeletonPoison", "TrophyGhost", "TrophyTheElder", "TrophyBjorn", "TrophySkeletonHildir",
    };

    private static readonly string[] Swamp =
    {
        "TrophyDraugr", "TrophyDraugrElite", "TrophyDraugrFem", "TrophyBlob", "TrophyLeech", "TrophySurtling",
        "TrophyAbomination", "TrophyWraith", "TrophyBonemass", "TrophyKvastur", "TrophyWrithan",
    };

    private static readonly string[] Ocean = { "TrophySerpent" };

    private static readonly string[] Mountain =
    {
        "TrophyWolf", "TrophyFenring", "TrophyHatchling", "TrophySGolem", "TrophyCultist", "TrophyUlv",
        "TrophyDragonQueen", "TrophyCultist_Hildir", "TrophyBlob_Frost",
    };

    private static readonly string[] Plains =
    {
        "TrophyDeathsquito", "TrophyGoblin", "TrophyGoblinBrute", "TrophyGoblinShaman", "TrophyGoblinKing",
        "TrophyGrowth", "TrophyLox", "TrophyGoblinBruteBrosBrute", "TrophyGoblinBruteBrosShaman", "TrophyBjornUndead",
    };

    private static readonly string[] Mistlands =
    {
        "TrophyDvergr", "TrophyGjall", "TrophyHare", "TrophySeeker", "TrophySeekerBrute", "TrophySeekerQueen",
        "TrophyTick",
    };

    private static readonly string[] Ashlands =
    {
        "TrophyAsksvin", "TrophyBonemawSerpent", "TrophyCharredArcher", "TrophyCharredMage", "TrophyCharredMelee",
        "TrophyFader", "TrophyFallenValkyrie", "TrophyMorgen", "TrophyVolture", "TrophyBlob_Lava",
    };

    private static readonly string[] DeepNorth =
    {
        "TrophyBarka", "TrophyBlob_Morkhalla", "TrophyElaking", "TrophyJotunWarrior", "TrophyJotunWitch", "TrophyMole",
        "TrophyMoose", "TrophySeal",
    };

    private static readonly Dictionary<string, Heightmap.Biome> Map = Build();

    /// <summary>Biome of a trophy prefab name. False = not in table.</summary>
    internal static bool TryGet(string trophyPrefab, out Heightmap.Biome biome)
    {
        biome = Heightmap.Biome.None;
        return trophyPrefab != null && Map.TryGetValue(trophyPrefab, out biome);
    }

    internal static int Count => Map.Count;

    private static Dictionary<string, Heightmap.Biome> Build()
    {
        var map = new Dictionary<string, Heightmap.Biome>(StringComparer.Ordinal);
        Add(map, Meadows, Heightmap.Biome.Meadows);
        Add(map, BlackForest, Heightmap.Biome.BlackForest);
        Add(map, Swamp, Heightmap.Biome.Swamp);
        Add(map, Ocean, Heightmap.Biome.Ocean);
        Add(map, Mountain, Heightmap.Biome.Mountain);
        Add(map, Plains, Heightmap.Biome.Plains);
        Add(map, Mistlands, Heightmap.Biome.Mistlands);
        Add(map, Ashlands, Heightmap.Biome.AshLands);
        Add(map, DeepNorth, Heightmap.Biome.DeepNorth);
        return map;
    }

    // Same name twice = first (earliest biome) win, like Sort Chest.
    private static void Add(Dictionary<string, Heightmap.Biome> map, string[] names, Heightmap.Biome biome)
    {
        foreach (var name in names)
        {
            if (!map.ContainsKey(name))
            {
                map[name] = biome;
            }
        }
    }
}
