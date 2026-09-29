namespace MC.Exploration.CompendiumEncyclopediaMod;

/// <summary>Biome helpers: progression order, tokens, first biome of a mask.</summary>
internal static class Biomes
{
    /// <summary>Progression order (Meadows first, Deep North last). Detail lines and creature groups follow it.</summary>
    internal static readonly Heightmap.Biome[] Progression =
    {
        Heightmap.Biome.Meadows,
        Heightmap.Biome.BlackForest,
        Heightmap.Biome.Swamp,
        Heightmap.Biome.Ocean,
        Heightmap.Biome.Mountain,
        Heightmap.Biome.Plains,
        Heightmap.Biome.Mistlands,
        Heightmap.Biome.AshLands,
        Heightmap.Biome.DeepNorth,
    };

    /// <summary>Every single biome flag of <see cref="Progression"/> OR-ed.</summary>
    internal static readonly Heightmap.Biome AllKnownFlags = Build();

    /// <summary>Vanilla token, same as BiomeSector.GetBiomeName: "$biome_" + lower-case enum name.</summary>
    internal static string Token(Heightmap.Biome biome) => BiomeSector.GetBiomeName(biome);

    /// <summary>Rank in progression order (0 = Meadows). Not a single known flag = Progression.Length.</summary>
    internal static int Rank(Heightmap.Biome biome)
    {
        for (var i = 0; i < Progression.Length; i++)
        {
            if (Progression[i] == biome)
            {
                return i;
            }
        }
        return Progression.Length;
    }

    /// <summary>First biome of the mask in progression order; None when mask has none.</summary>
    internal static Heightmap.Biome First(Heightmap.Biome mask)
    {
        foreach (var b in Progression)
        {
            if ((mask & b) != 0)
            {
                return b;
            }
        }
        return Heightmap.Biome.None;
    }

    private static Heightmap.Biome Build()
    {
        var all = Heightmap.Biome.None;
        foreach (var b in Progression)
        {
            all |= b;
        }
        return all;
    }
}
