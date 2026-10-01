using System;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.DeepNorthAwakeningMod;

// Me = the blizzard of a storming area (design 2.4). EnvMan.GetEnvironmentOverride postfix ask me only when vanilla
// has no override (raid, boss, AltBiome, vanilla invasion and env zones win). Local player outdoors, inside the Deep
// North line (same geometry as the cells, sea too; a border cell reach past the line), in an awake, storming cell that
// is not cleared (after Kall) = Deep North blizzard. Answer cached CheckSeconds (vanilla ask every frame).
// Blizzard name: Twilight_SnowStorm (asset list); not found = a Deep North weather with "storm" in its name; none = no
// blizzard (logged once).
internal static class Storms
{
    internal const string PreferredEnv = "Twilight_SnowStorm";
    internal const float CheckSeconds = 0.5f;

    private static EnvMan _envFor;
    private static string _env;
    private static float _nextCheck;
    private static bool _storm;

    // Postfix body. Null = no override from me.
    internal static string Override()
    {
        var now = Time.time;
        if (now >= _nextCheck)
        {
            _nextCheck = now + CheckSeconds;
            _storm = InStorm();
        }
        return _storm ? Env() : null;
    }

    private static bool InStorm()
    {
        var rules = ServerRules.Current;
        if (!WorldState.Awake(rules) || rules.StormShare <= 0)
        {
            return false;
        }
        var player = Player.m_localPlayer;
        if (player == null || player.InInterior())
        {
            return false;
        }
        return StormAt(player.transform.position, rules);
    }

    // Deep North, awake, not cleared (after Kall) and storming cell at this position (self test use it too).
    internal static bool StormAt(Vector3 position, AwakeningRules rules)
    {
        if (!WorldGenerator.IsDeepnorth(position.x, position.z))
        {
            return false;
        }
        var cell = WorldState.CellAt(position);
        return WorldState.IsAwakeCell(cell, rules) && !HeldCells.IsCleared(cell) && WorldState.IsStormingCell(cell, rules);
    }

    // Blizzard env name for this EnvMan (resolved once per EnvMan).
    internal static string Env()
    {
        var env = EnvMan.instance;
        if (env == null)
        {
            return null;
        }
        if (!ReferenceEquals(env, _envFor))
        {
            _envFor = env;
            _env = Resolve(env);
        }
        return _env;
    }

    private static string Resolve(EnvMan env)
    {
        if (env.GetEnv(PreferredEnv) != null)
        {
            return PreferredEnv;
        }
        foreach (var biome in env.m_biomes)
        {
            if (biome == null || biome.m_biome != Heightmap.Biome.DeepNorth || biome.m_environments == null)
            {
                continue;
            }
            foreach (var entry in biome.m_environments)
            {
                var name = entry != null ? entry.m_environment : null;
                if (!string.IsNullOrEmpty(name) && name.IndexOf("storm", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Log.Info($"The weather {PreferredEnv} is missing; Deep North storms use {name}.");
                    return name;
                }
            }
        }
        Log.Warning($"No Deep North storm weather found ({PreferredEnv} is missing); the areas storm without a blizzard.");
        return null;
    }

    internal static void Clear()
    {
        _envFor = null;
        _env = null;
        _nextCheck = 0f;
        _storm = false;
    }
}
