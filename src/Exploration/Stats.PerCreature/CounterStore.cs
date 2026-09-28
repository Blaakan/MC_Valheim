using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MC.Shared;

namespace MC.Exploration.StatsPerCreatureMod;

// Me keep mod's own data in Player.m_customData (saved with character, survive death, travel in .fch, vanilla keep
// unknown keys when mod gone):
//   Tames          "1\n3\t$enemy_boar\n2\t$enemy_wolf"   (version line, then count TAB name per line)
//   CountingSince  "2026-09-28"                          (day tame counting started, written once, never moved)
// Kills: me never store, never write. Vanilla profile already have them (CreatureCounts read there).
// Parse cache keyed on raw string reference: many reads per page build = one dictionary lookup each.
internal static class CounterStore
{
    // Vanilla Tameable.Tame send "<m_name> $hud_tamedone" to closest player. Only place game use this token.
    internal const string Suffix = " $hud_tamedone";

    private const string FormatVersion = "1";
    private const string DateFormat = "yyyy-MM-dd";

    private static readonly Dictionary<string, int> Empty = new Dictionary<string, int>();

    private static string _cachedRaw;
    private static Dictionary<string, int> _cachedTames = Empty;
    private static bool _cachedWritable = true;
    private static bool _newerFormatWarned;

    // Message look like tame message? Give creature name. Cheap: no allocation unless it match.
    internal static bool TryParseTameMessage(string msg, out string name)
    {
        name = null;
        if (msg == null || msg.Length <= Suffix.Length || !msg.EndsWith(Suffix, StringComparison.Ordinal))
        {
            return false;
        }
        name = msg.Substring(0, msg.Length - Suffix.Length);
        return true;
    }

    // One more tame of this creature for local player. Build whole new string, assign once.
    internal static void AddTame(string name, string via)
    {
        var player = Player.m_localPlayer;
        if (player == null || string.IsNullOrEmpty(name) || player.m_customData == null)
        {
            return;
        }
        if (name.IndexOf('\n') >= 0 || name.IndexOf('\r') >= 0)
        {
            // Line break in name would break stored lines. Vanilla names never have one.
            Log.Warning($"Tame not counted: creature name '{name}' contains a line break.");
            return;
        }

        // Tame before start date written (should not happen: spawn write it)? Start now, so page tell truth.
        EnsureStarted(player);

        Parse(player, out var raw, out var tames, out var writable);
        if (!writable)
        {
            WarnNewerFormat(raw);
            return;
        }

        var updated = new Dictionary<string, int>(tames, StringComparer.Ordinal);
        updated.TryGetValue(name, out var count);
        updated[name] = count < int.MaxValue ? count + 1 : count;

        var newRaw = Serialize(updated);
        player.m_customData[CreatureCounts.TamesDataKey] = newRaw;
        _cachedRaw = newRaw;
        _cachedTames = updated;
        _cachedWritable = true;

        Log.Debug($"Tame counted: {name} ({via}). Stored {CreatureCounts.TamesDataKey} = {Escape(newRaw)}");
    }

    internal static int GetTames(Player player, string name)
    {
        Parse(player, out _, out var tames, out _);
        return tames.TryGetValue(name, out var count) ? count : 0;
    }

    // Cached dictionary. Caller only read it.
    internal static IReadOnlyDictionary<string, int> GetAllTames(Player player)
    {
        Parse(player, out _, out var tames, out _);
        return tames;
    }

    // First run for this character: write start day. Never overwrite (second install or toggle keep old day).
    internal static void EnsureStarted(Player player)
    {
        if (player == null || player.m_customData == null
            || player.m_customData.ContainsKey(CreatureCounts.CountingSinceDataKey))
        {
            return;
        }
        var today = DateTime.Now.ToString(DateFormat, CultureInfo.InvariantCulture);
        player.m_customData[CreatureCounts.CountingSinceDataKey] = today;
        Log.Debug($"Counting started: {today} (tames are counted from this date).");
    }

    internal static DateTime? GetCountingSince(Player player)
    {
        if (player == null || player.m_customData == null
            || !player.m_customData.TryGetValue(CreatureCounts.CountingSinceDataKey, out var raw)
            || !DateTime.TryParseExact(raw, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return null;
        }
        return date;
    }

    // Feature off: forget cache (next read parse again).
    internal static void ClearCache()
    {
        _cachedRaw = null;
        _cachedTames = Empty;
        _cachedWritable = true;
    }

    // Read stored tames of this player. Same raw string object as last time = cached result.
    private static void Parse(Player player, out string raw, out Dictionary<string, int> tames, out bool writable)
    {
        raw = null;
        if (player == null || player.m_customData == null
            || !player.m_customData.TryGetValue(CreatureCounts.TamesDataKey, out raw)
            || string.IsNullOrEmpty(raw))
        {
            // Nothing stored = nothing counted yet. Free to write.
            tames = Empty;
            writable = true;
            return;
        }
        if (ReferenceEquals(raw, _cachedRaw))
        {
            tames = _cachedTames;
            writable = _cachedWritable;
            return;
        }

        tames = new Dictionary<string, int>(StringComparer.Ordinal);
        var lines = raw.Split('\n');
        // Line 1 not "1" = newer mod wrote it. Me read what me can, never write (no damage to newer data).
        writable = lines[0].TrimEnd('\r') == FormatVersion;
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

        _cachedRaw = raw;
        _cachedTames = tames;
        _cachedWritable = writable;
    }

    // Stable order (ordinal by name): same counts = same string.
    private static string Serialize(Dictionary<string, int> tames)
    {
        var names = new List<string>(tames.Keys);
        names.Sort(StringComparer.Ordinal);
        var sb = new StringBuilder(16 + names.Count * 24);
        sb.Append(FormatVersion);
        foreach (var name in names)
        {
            var count = tames[name];
            if (count <= 0)
            {
                continue;
            }
            sb.Append('\n').Append(count.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(name);
        }
        return sb.ToString();
    }

    private static void WarnNewerFormat(string raw)
    {
        if (_newerFormatWarned)
        {
            return;
        }
        _newerFormatWarned = true;
        var firstLine = raw == null ? "" : raw.Split('\n')[0];
        Log.Warning($"The stored tame counts ({CreatureCounts.TamesDataKey}) use format version '{firstLine}', "
                    + "which this version of the mod does not know (probably written by a newer version). "
                    + "They are shown but new tames are not counted, so that data is never damaged. Update the mod.");
    }

    // Log show line breaks and tabs as \n and \t, so stored value fit on one log line.
    private static string Escape(string raw) => raw.Replace("\n", "\\n").Replace("\t", "\\t");
}
