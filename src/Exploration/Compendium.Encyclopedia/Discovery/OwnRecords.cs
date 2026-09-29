using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MC.Shared;

namespace MC.Exploration.CompendiumEncyclopediaMod;

/// <summary>
/// Me keep the mod's two own records in <c>Player.m_customData</c> (saved with the character by vanilla; vanilla keep
/// unknown keys when mod gone):
///   Seen    "1\n$enemy_boar\n$enemy_deer"  (version line, then Character.m_name per line, sorted ordinal)
///   Biomes  "1\n265"                         (version line, then Heightmap.Biome flags, decimal, invariant)
/// Rules (like Creature Kill and Tame Counts CounterStore): new value built whole and assigned once, only when
/// something new; parse cached on raw string reference; unreadable lines skipped; names with line break refused;
/// first line not "1" = newer version wrote it: read what me can, never write, one Warning. Local player only.
/// </summary>
internal static class OwnRecords
{
    internal const string SeenKey = ModInfo.Guid + ".Seen";
    internal const string BiomesKey = ModInfo.Guid + ".Biomes";
    private const string FormatVersion = "1";

    private static readonly HashSet<string> EmptySet = new HashSet<string>(StringComparer.Ordinal);

    private static string _seenRaw;
    private static HashSet<string> _seen = EmptySet;
    private static bool _seenWritable = true;
    private static string _biomesRaw;
    private static Heightmap.Biome _biomes;
    private static bool _biomesWritable = true;
    private static bool _newerWarned;

    // ---------------------------------------------------------------- Seen (creatures met)

    /// <summary>Creature names met by this character. Cached set: caller only read it.</summary>
    internal static HashSet<string> GetSeen(Player player)
    {
        ParseSeen(player);
        return _seen;
    }

    /// <summary>
    /// Record a creature met (vanilla showed its name plate). Local player only. Cheap when already known: one set lookup. Return true
    /// when newly recorded.
    /// </summary>
    internal static bool MarkSeen(string name)
    {
        var player = Player.m_localPlayer;
        if (player == null || player.m_customData == null || string.IsNullOrEmpty(name))
        {
            return false;
        }
        ParseSeen(player);
        if (_seen.Contains(name))
        {
            return false;
        }
        if (name.IndexOf('\n') >= 0 || name.IndexOf('\r') >= 0)
        {
            return false; // would break stored lines; vanilla names never have one
        }
        if (!_seenWritable)
        {
            WarnNewer(SeenKey, _seenRaw);
            return false;
        }
        var updated = new HashSet<string>(_seen, StringComparer.Ordinal) { name };
        var names = new List<string>(updated);
        names.Sort(StringComparer.Ordinal);
        var sb = new StringBuilder(8 + names.Count * 20);
        sb.Append(FormatVersion);
        foreach (var n in names)
        {
            sb.Append('\n').Append(n);
        }
        var raw = sb.ToString();
        player.m_customData[SeenKey] = raw;
        _seenRaw = raw;
        _seen = updated;
        _seenWritable = true;
        Log.Debug($"Met {name} (its name plate was shown).");
        return true;
    }

    private static void ParseSeen(Player player)
    {
        string raw = null;
        if (player == null || player.m_customData == null || !player.m_customData.TryGetValue(SeenKey, out raw)
            || string.IsNullOrEmpty(raw))
        {
            if (_seenRaw != null || !ReferenceEquals(_seen, EmptySet))
            {
                _seenRaw = null;
                _seen = EmptySet;
            }
            _seenWritable = true;
            return;
        }
        if (ReferenceEquals(raw, _seenRaw))
        {
            return;
        }
        var set = new HashSet<string>(StringComparer.Ordinal);
        var lines = raw.Split('\n');
        var writable = lines[0].TrimEnd('\r') == FormatVersion;
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length > 0)
            {
                set.Add(line);
            }
        }
        _seenRaw = raw;
        _seen = set;
        _seenWritable = writable;
    }

    // ---------------------------------------------------------------- Biomes (visited, language independent)

    /// <summary>Biomes this character visited while the mod ran (flags).</summary>
    internal static Heightmap.Biome GetBiomes(Player player)
    {
        ParseBiomes(player);
        return _biomes;
    }

    /// <summary>Record a visited biome for the local player (None skipped). Return true when new.</summary>
    internal static bool RecordBiome(Heightmap.Biome biome)
    {
        var player = Player.m_localPlayer;
        biome &= Heightmap.Biome.All;
        if (player == null || player.m_customData == null || biome == Heightmap.Biome.None)
        {
            return false;
        }
        ParseBiomes(player);
        if ((_biomes & biome) == biome)
        {
            return false;
        }
        if (!_biomesWritable)
        {
            WarnNewer(BiomesKey, _biomesRaw);
            return false;
        }
        var updated = _biomes | biome;
        var raw = FormatVersion + "\n" + ((int)updated).ToString(CultureInfo.InvariantCulture);
        player.m_customData[BiomesKey] = raw;
        _biomesRaw = raw;
        _biomes = updated;
        _biomesWritable = true;
        Log.Debug($"Biome recorded: {biome}.");
        return true;
    }

    /// <summary>Record the biome the local player stands in (on activation and each window open).</summary>
    internal static void RecordCurrentBiome()
    {
        var player = Player.m_localPlayer;
        if (player != null)
        {
            RecordBiome(player.GetCurrentBiome());
        }
    }

    private static void ParseBiomes(Player player)
    {
        string raw = null;
        if (player == null || player.m_customData == null || !player.m_customData.TryGetValue(BiomesKey, out raw)
            || string.IsNullOrEmpty(raw))
        {
            _biomesRaw = null;
            _biomes = Heightmap.Biome.None;
            _biomesWritable = true;
            return;
        }
        if (ReferenceEquals(raw, _biomesRaw))
        {
            return;
        }
        var lines = raw.Split('\n');
        var writable = lines[0].TrimEnd('\r') == FormatVersion;
        var mask = Heightmap.Biome.None;
        if (lines.Length > 1 && int.TryParse(lines[1].TrimEnd('\r'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
        {
            mask = (Heightmap.Biome)v & Heightmap.Biome.All;
        }
        _biomesRaw = raw;
        _biomes = mask;
        _biomesWritable = writable;
    }

    // ---------------------------------------------------------------- reset, cache

    /// <summary>resetcharacter follow-up: remove both keys (local player).</summary>
    internal static void ResetFor(Player player)
    {
        if (player == null || player.m_customData == null)
        {
            return;
        }
        var had = player.m_customData.Remove(SeenKey) | player.m_customData.Remove(BiomesKey);
        ClearCache();
        if (had)
        {
            Log.Debug("Character reset: Encyclopedia records of met creatures and visited biomes cleared.");
        }
    }

    /// <summary>Forget parse caches (next read parse again).</summary>
    internal static void ClearCache()
    {
        _seenRaw = null;
        _seen = EmptySet;
        _seenWritable = true;
        _biomesRaw = null;
        _biomes = Heightmap.Biome.None;
        _biomesWritable = true;
    }

    private static void WarnNewer(string key, string raw)
    {
        if (_newerWarned)
        {
            return;
        }
        _newerWarned = true;
        var first = raw == null ? "" : raw.Split('\n')[0];
        Log.Warning($"The stored Encyclopedia record {key} uses format version '{first}', which this version of the mod "
                    + "does not know (probably written by a newer version). It is read but not updated, so that data is "
                    + "never damaged. Update the mod.");
    }
}

/// <summary>
/// Me read per-creature tames of MC Creature Kill and Tame Counts, softly: its documented <c>m_customData</c> key,
/// format v1 ("1", then "count TAB name" lines). No reference, no dependency: missing key = 0. Copy of its parser
/// (read only; never write). Cached on raw string reference.
/// </summary>
internal static class TamesReader
{
    internal const string Key = "MC.Exploration.Stats.PerCreature.Tames";

    private static readonly Dictionary<string, int> Empty = new Dictionary<string, int>(StringComparer.Ordinal);
    private static string _raw;
    private static Dictionary<string, int> _tames = Empty;

    /// <summary>Tames per creature name. Cached: caller only read it.</summary>
    internal static IReadOnlyDictionary<string, int> Read(Player player)
    {
        if (player == null || player.m_customData == null || !player.m_customData.TryGetValue(Key, out var raw)
            || string.IsNullOrEmpty(raw))
        {
            return Empty;
        }
        if (ReferenceEquals(raw, _raw))
        {
            return _tames;
        }
        var tames = new Dictionary<string, int>(StringComparer.Ordinal);
        var lines = raw.Split('\n');
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var tab = line.IndexOf('\t');
            if (tab <= 0 || tab == line.Length - 1)
            {
                continue;
            }
            if (!int.TryParse(line.Substring(0, tab), NumberStyles.None, CultureInfo.InvariantCulture, out var count)
                || count <= 0)
            {
                continue;
            }
            var name = line.Substring(tab + 1);
            tames.TryGetValue(name, out var old);
            tames[name] = old > int.MaxValue - count ? int.MaxValue : old + count;
        }
        _raw = raw;
        _tames = tames;
        return tames;
    }

    internal static void ClearCache()
    {
        _raw = null;
        _tames = Empty;
    }
}
