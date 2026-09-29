using System.Collections.Generic;
using System.Text;

namespace MC.UX.CraftingSearchSortMod;

// Me remember sort per station type, per character: Player.m_customData[Key] = "station=option|station=option".
// Station = CraftingStation.m_name token ("$piece_forge"), or "none" for hand crafting. Default = no entry.
// Vanilla save and load m_customData with character and ignore keys it no know: character still load without mod.
// Never "sortcraft" unique key (vanilla console sort; unique keys also drive world checks).
internal static class SortMemory
{
    internal const string Key = ModInfo.Guid + ".Sort";

    internal static int Load(Player player, string station)
    {
        if (player == null || player.m_customData == null
            || !player.m_customData.TryGetValue(Key, out var value) || string.IsNullOrEmpty(value))
        {
            return RecipeCategory.Default;
        }
        foreach (var part in value.Split('|'))
        {
            var eq = part.IndexOf('=');
            if (eq > 0 && string.CompareOrdinal(part, 0, station, 0, eq) == 0 && station.Length == eq)
            {
                return RecipeCategory.FromId(part.Substring(eq + 1));
            }
        }
        return RecipeCategory.Default;
    }

    internal static void Save(Player player, string station, int option)
    {
        if (player == null || player.m_customData == null || string.IsNullOrEmpty(station))
        {
            return;
        }

        // Keep other stations, drop bad parts, replace this one.
        var parts = new List<string>();
        if (player.m_customData.TryGetValue(Key, out var value) && !string.IsNullOrEmpty(value))
        {
            foreach (var part in value.Split('|'))
            {
                var eq = part.IndexOf('=');
                if (eq <= 0 || eq == part.Length - 1 || part.Substring(0, eq) == station)
                {
                    continue;
                }
                parts.Add(part);
            }
        }
        if (option != RecipeCategory.Default)
        {
            parts.Add(station + "=" + RecipeCategory.Id(option));
        }

        if (parts.Count == 0)
        {
            player.m_customData.Remove(Key);
            return;
        }
        var sb = new StringBuilder();
        for (var i = 0; i < parts.Count; i++)
        {
            if (i > 0)
            {
                sb.Append('|');
            }
            sb.Append(parts[i]);
        }
        player.m_customData[Key] = sb.ToString();
    }

    // Station key from station; "|" and "=" in modded names become "_" so saved string stay parsable.
    internal static string StationKey(CraftingStation station)
    {
        if (station == null)
        {
            return "none";
        }
        var name = station.m_name;
        if (string.IsNullOrEmpty(name))
        {
            return "unnamed";
        }
        return name.IndexOf('|') < 0 && name.IndexOf('=') < 0 ? name : name.Replace('|', '_').Replace('=', '_');
    }
}
