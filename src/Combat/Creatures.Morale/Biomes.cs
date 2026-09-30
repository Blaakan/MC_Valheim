using UnityEngine;

namespace MC.Combat.CreaturesMoraleMod;

// Me = biome of a creature (design 2.3, 1.11). Biome level = order of the biome's boss (the game's ladder):
// Meadows 1 Eikthyr, Black Forest 2 The Elder, Swamp 3 Bonemass, Mountains 4 Moder, Plains 5 Yagluth, Mistlands 6 The
// Queen, Ashlands 7 Fader, Deep North 8 Kall. Ocean has no boss: as home list it count as 3 (Bonemass), as spawn place
// it count as nothing (home decide). Fixed here, not a setting: vanilla ladder never change.
// Spawn level = biome level at the creature's spawn point (BaseAI.m_spawnPoint, from ZDO "spawnpoint", same on every
// game, never moved). Point in a dungeon (sky, y > 3000) = biome at the entrance of that zone's location. Me compute it
// once per creature per game (CreatureState keep it): no biome lookup per tick.
// Biome from WorldGenerator (same as the player's own biome, Player.UpdateBiome and EnvMan), not from the heightmap.
// SpawnSystem check the heightmap biome (blend of the zone corners), which can differ a few tens of metres from a
// border, but a heightmap exist only near a player: every game must get the same answer for the same creature.
internal static class Biomes
{
    // Home list index (MoraleRules.HomeKeys) -> level. Same order as HomeKeys.
    internal static readonly int[] HomeLevels = { 1, 2, 3, 4, 5, 6, 7, 8, 3 };

    // Not known yet (no WorldGenerator): ask again at next fact refresh.
    internal const int Unknown = -1;

    // Level of a world biome as SPAWN place. Ocean, None, several flags = 0 (no spawn level: home decide).
    internal static int LevelOf(Heightmap.Biome biome)
    {
        switch (biome)
        {
            case Heightmap.Biome.Meadows: return 1;
            case Heightmap.Biome.BlackForest: return 2;
            case Heightmap.Biome.Swamp: return 3;
            case Heightmap.Biome.Mountain: return 4;
            case Heightmap.Biome.Plains: return 5;
            case Heightmap.Biome.Mistlands: return 6;
            case Heightmap.Biome.AshLands: return 7;
            case Heightmap.Biome.DeepNorth: return 8;
            default: return 0;
        }
    }

    // Hardest level among the biome flags of a spawn table entry (PrefabTokens). Ocean and None alone = 0.
    internal static int HighestLevelOf(Heightmap.Biome biomes)
    {
        var best = 0;
        foreach (var biome in LandBiomes)
        {
            if ((biomes & biome) != 0)
            {
                best = Mathf.Max(best, LevelOf(biome));
            }
        }
        return best;
    }

    private static readonly Heightmap.Biome[] LandBiomes =
    {
        Heightmap.Biome.Meadows, Heightmap.Biome.BlackForest, Heightmap.Biome.Swamp, Heightmap.Biome.Mountain,
        Heightmap.Biome.Plains, Heightmap.Biome.Mistlands, Heightmap.Biome.AshLands, Heightmap.Biome.DeepNorth,
    };

    // Spawn level of a creature: 0..8, or Unknown when no world generator yet. Called rarely (once per creature).
    internal static int SpawnLevel(BaseAI ai)
    {
        if (ai == null)
        {
            return 0;
        }
        var point = ai.m_spawnPoint;
        if (point == Vector3.zero)
        {
            point = ai.transform.position; // no ZDO spawn point: where it stands when first judged
        }
        return LevelAt(point);
    }

    // Biome level at a world point, as spawn place. Self tests use it too.
    internal static int LevelAt(Vector3 point)
    {
        var generator = WorldGenerator.instance;
        if (generator == null)
        {
            return Unknown;
        }
        return LevelOf(generator.GetBiome(Entrance(point)));
    }

    // Dungeon inside = up in the sky over its zone (Location.Awake): use the location's own spot (the entrance). No
    // loaded location there = the point's x, z (same zone, most often same biome).
    internal static Vector3 Entrance(Vector3 point)
    {
        if (!Character.InInterior(point))
        {
            return point;
        }
        var location = Location.GetZoneLocation(point);
        return location != null ? location.transform.position : point;
    }

    // Player-facing biome name of a level, for log lines and self-test notes.
    internal static string Name(int level)
    {
        switch (level)
        {
            case 1: return "Meadows";
            case 2: return "Black Forest";
            case 3: return "Swamp";
            case 4: return "Mountains";
            case 5: return "Plains";
            case 6: return "Mistlands";
            case 7: return "Ashlands";
            case 8: return "Deep North";
            case Unknown: return "not known yet";
            default: return "none";
        }
    }
}
