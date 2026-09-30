using System.Collections.Generic;
using System.Text;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.CreaturesMoraleMod;

// Me = what the rules' prefab names are in this game (design 2.2, 5.1), from ZNetScene:
//  - ranked tokens: name token (Character.m_name) -> lowest and highest rank of the listed prefabs that carry it, each
//    counted in its home biome and in the hardest biome of its world spawns (enabled entries of the game's SpawnSystem
//    lists and alt biome lists: Draugr also spawn in the Plains, rank 7). Standing need it to list only the kill
//    bonuses that can still change something (reskins share a token, not a rank; ranks in between cover creatures met
//    above their home biome). Nests, dungeons, spawn command, other mods: not in here (design 2.2);
//  - the name check: names that are no prefab (one Warning), names that are no creature this mod can make afraid
//    (no MonsterAI), names in several home lists (easiest win), elites in no home list (never afraid).
// Me build lazily once per ZNetScene instance and rules object; nothing kept without ZNetScene.
// Warning logged only when its text change (same rules again = silent).
internal static class PrefabTokens
{
    internal sealed class RankedToken
    {
        internal string Token;
        internal int Hash;
        internal int MinRank = int.MaxValue;
        internal int MaxRank = int.MinValue;

        internal void Add(int rank)
        {
            MinRank = Mathf.Min(MinRank, rank);
            MaxRank = Mathf.Max(MaxRank, rank);
        }

        // Some rank r in [MinRank, MaxRank] with bossRank - 2*StarRank < r <= bossRank + maxSkip: a 0-2 star one of
        // that kind is not afraid through the boss rank alone, and kills may still bring it (design 2.2).
        internal bool CanMatter(int bossRank, int starRank, int maxSkip)
        {
            var low = Mathf.Max(MinRank, bossRank - 2 * starRank + 1);
            var high = Mathf.Min(MaxRank, bossRank + maxSkip);
            return low <= high;
        }
    }

    private static ZNetScene _scene;
    private static MoraleRules _rules;
    private static List<RankedToken> _ranked;
    private static string _lastWarning;

    // For self tests: names of the rules in force that are no prefab, or no MonsterAI creature.
    internal static readonly List<string> UnknownNames = new List<string>();
    internal static readonly List<string> NotCreatures = new List<string>();

    // Ranked tokens of these rules. Null when no ZNetScene.
    internal static List<RankedToken> Get(MoraleRules rules)
    {
        var scene = ZNetScene.instance;
        if (scene == null || rules == null)
        {
            return null;
        }
        if (_ranked != null && ReferenceEquals(scene, _scene) && ReferenceEquals(rules, _rules))
        {
            return _ranked;
        }
        Build(scene, rules);
        return _ranked;
    }

    // Name check for the rules in force, when a world exists. Cheap when already done. Server side (no local player,
    // no creatures) call this when it sends rules, so a dedicated server logs bad names too.
    internal static void Ensure()
    {
        if (!ServerRules.Pending)
        {
            Get(ServerRules.Current);
        }
    }

    private static void Build(ZNetScene scene, MoraleRules rules)
    {
        var byToken = new Dictionary<string, RankedToken>();
        var ranked = new List<RankedToken>();
        var worldSpawns = WorldSpawnLevels();
        UnknownNames.Clear();
        NotCreatures.Clear();
        foreach (var name in rules.Names)
        {
            var hash = name.GetStableHashCode();
            var prefab = scene.GetPrefab(hash);
            if (prefab == null)
            {
                UnknownNames.Add(name);
                continue;
            }
            var character = prefab.GetComponent<Character>();
            if (character == null || prefab.GetComponent<MonsterAI>() == null)
            {
                NotCreatures.Add(name);
                continue;
            }
            if (!rules.TryGetRank(hash, 0, out var rank) || string.IsNullOrEmpty(character.m_name))
            {
                continue;
            }
            if (!byToken.TryGetValue(character.m_name, out var entry))
            {
                entry = new RankedToken { Token = character.m_name, Hash = character.m_name.GetStableHashCode() };
                byToken[character.m_name] = entry;
                ranked.Add(entry);
            }
            entry.Add(rank);
            if (worldSpawns.TryGetValue(hash, out var spawnLevel) && rules.TryGetRank(hash, spawnLevel, out var spawnRank))
            {
                entry.Add(spawnRank); // harder of home and that biome: never below the home rank
            }
        }
        _ranked = ranked;
        _scene = scene;
        _rules = rules;
        Warn(rules);
    }

    // Prefab hash -> hardest biome level of its enabled world spawns: the zone controller's SpawnSystem lists (every
    // zone's spawner use them; a live one too when loaded, same lists) and the alt biome lists. Nothing there (no
    // ZoneSystem yet) = home ranks only until the next build.
    private static Dictionary<int, int> WorldSpawnLevels()
    {
        var levels = new Dictionary<int, int>();
        var zones = ZoneSystem.instance;
        if (zones != null && zones.m_zoneCtrlPrefab != null)
        {
            AddLists(levels, zones.m_zoneCtrlPrefab.GetComponent<SpawnSystem>());
        }
        if (SpawnSystem.m_instances != null && SpawnSystem.m_instances.Count > 0)
        {
            AddLists(levels, SpawnSystem.m_instances[0]);
        }
        if (AltBiomeList.m_altBiomes != null)
        {
            foreach (var alt in AltBiomeList.m_altBiomes)
            {
                if (alt != null && alt.m_enabled)
                {
                    AddSpawns(levels, alt.m_spawn);
                }
            }
        }
        return levels;
    }

    private static void AddLists(Dictionary<int, int> levels, SpawnSystem system)
    {
        if (system == null || system.m_spawnLists == null)
        {
            return;
        }
        foreach (var list in system.m_spawnLists)
        {
            if (list != null)
            {
                AddSpawns(levels, list.m_spawners);
            }
        }
    }

    private static void AddSpawns(Dictionary<int, int> levels, List<SpawnSystem.SpawnData> spawns)
    {
        if (spawns == null)
        {
            return;
        }
        foreach (var spawn in spawns)
        {
            if (spawn == null || !spawn.m_enabled || spawn.m_prefab == null)
            {
                continue;
            }
            var level = Biomes.HighestLevelOf(spawn.m_biome);
            var hash = spawn.m_prefab.name.GetStableHashCode();
            if (level > 0 && (!levels.TryGetValue(hash, out var had) || level > had))
            {
                levels[hash] = level;
            }
        }
    }

    private static void Warn(MoraleRules rules)
    {
        var sb = new StringBuilder();
        if (UnknownNames.Count > 0)
        {
            sb.Append("these names are not creatures of this game and are ignored: ")
                .Append(string.Join(", ", UnknownNames.ToArray()));
        }
        if (NotCreatures.Count > 0)
        {
            sb.Append(sb.Length > 0 ? "; " : "")
                .Append("these are not creatures that attack players, so they always behave as in the normal game: ")
                .Append(string.Join(", ", NotCreatures.ToArray()));
        }
        if (rules.NamesInSeveralHomeLists.Count > 0)
        {
            sb.Append(sb.Length > 0 ? "; " : "")
                .Append("these creatures are in several home biome lists, the easiest biome is used: ")
                .Append(string.Join(", ", rules.NamesInSeveralHomeLists.ToArray()));
        }
        if (rules.ElitesWithoutHome.Count > 0)
        {
            sb.Append(sb.Length > 0 ? "; " : "")
                .Append("these elites are in no home biome list, so they are never afraid: ")
                .Append(string.Join(", ", rules.ElitesWithoutHome.ToArray()));
        }
        var text = sb.Length > 0 ? sb.ToString() : null;
        if (text == _lastWarning)
        {
            return;
        }
        _lastWarning = text;
        if (text != null)
        {
            var whose = ServerRules.UsingServer ? "the server's creature settings" : "the creature settings";
            Log.Warning($"In {whose}, {text}.");
        }
    }

    internal static void Clear()
    {
        _scene = null;
        _rules = null;
        _ranked = null;
    }
}
