using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MC.Shared;

namespace MC.Exploration.StatsPerCreatureMod;

// Me build "Creatures" block that go on top of vanilla Player Statistics text. Built each Compendium open.
// Me read ONLY through public CreatureCounts (no profile, no stored string here): every page test also test API.
// English labels like rest of that vanilla page; creature names translated.
internal static class StatsSection
{
    private const string Indent = "    ";
    private const string GreyOpen = "<size=85%><color=#B0B0B0>";
    private const string GreyClose = "</color></size>";

    private static readonly StringBuilder Text = new StringBuilder(4096);
    private static readonly List<Row> Creatures = new List<Row>();
    private static readonly List<Row> OtherNames = new List<Row>();
    private static readonly Comparison<Row> ByMostKilled = CompareMostKilled;
    private static readonly Comparison<Row> ByName = CompareName;

    private readonly struct Row
    {
        internal readonly string Name;
        internal readonly CreatureCounts.KillBreakdown Kills;
        internal readonly int Tames;

        internal Row(string name, CreatureCounts.KillBreakdown kills, int tames)
        {
            Name = name;
            Kills = kills;
            Tames = tames;
        }
    }

    internal static string Build()
    {
        Creatures.Clear();
        OtherNames.Clear();
        Text.Clear();
        try
        {
            foreach (var key in CreatureCounts.GetCreatureNames())
            {
                var kills = CreatureCounts.GetKillBreakdown(key);
                var tames = CreatureCounts.GetTames(key);
                if (kills.Total < 1 && tames < 1)
                {
                    continue;
                }
                // '$' key = localization token = creature. Else player name from old PvP save, or modded creature
                // without token: own group, so player name never read as creature.
                if (key.Length > 0 && key[0] == '$')
                {
                    Creatures.Add(new Row(Localization.instance.Localize(key), kills, tames));
                }
                else
                {
                    OtherNames.Add(new Row(key, kills, tames));
                }
            }

            var order = Plugin.SortBy != null && Plugin.SortBy.Value == SortOrder.Name ? ByName : ByMostKilled;
            Creatures.Sort(order);
            OtherNames.Sort(order);
            var showWeaponTypes = Plugin.ShowWeaponTypes == null || Plugin.ShowWeaponTypes.Value;

            Text.Append("<color=orange>Creatures</color>\n");
            Text.Append(GreyOpen)
                .Append("Kills: the game's own count for this character, in every world. Tames: counted while this mod is on (since ")
                .Append(SinceText())
                .Append(").")
                .Append(GreyClose)
                .Append('\n');

            if (Creatures.Count == 0 && OtherNames.Count == 0)
            {
                Text.Append(Indent).Append("Nothing killed or tamed yet.\n");
            }
            AppendRows(Creatures, showWeaponTypes);
            if (OtherNames.Count > 0)
            {
                Text.Append(GreyOpen)
                    .Append("Other names (older PvP kills, or creatures without a translation key):")
                    .Append(GreyClose)
                    .Append('\n');
                AppendRows(OtherNames, showWeaponTypes);
            }
            // Two empty lines, then vanilla text.
            Text.Append("\n\n");

            Log.Debug($"Creatures section: {Creatures.Count + OtherNames.Count} rows ({OtherNames.Count} other names).");
            return Text.ToString();
        }
        finally
        {
            // No keep names alive between opens.
            Creatures.Clear();
            OtherNames.Clear();
        }
    }

    private static string SinceText()
    {
        var since = CreatureCounts.CountingSince;
        return since.HasValue
            ? since.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : "this mod was installed";
    }

    // "    Boar: 12 killed (melee 11, other 1), 3 tamed" / "    Wolf: 2 tamed"
    private static void AppendRows(List<Row> rows, bool showWeaponTypes)
    {
        foreach (var row in rows)
        {
            Text.Append(Indent).Append(row.Name).Append(": ");
            var k = row.Kills;
            if (k.Total >= 1)
            {
                Text.Append(Number(k.Total)).Append(" killed");
                // Only "other" known (kills from before Deep North, or all mixed): "(other 40)" say nothing, leave out.
                if (showWeaponTypes && (k.Melee > 0 || k.Ranged > 0 || k.Magic > 0 || k.Unarmed > 0))
                {
                    Text.Append(" (");
                    var first = true;
                    AppendPart("melee", k.Melee, ref first);
                    AppendPart("ranged", k.Ranged, ref first);
                    AppendPart("magic", k.Magic, ref first);
                    AppendPart("unarmed", k.Unarmed, ref first);
                    AppendPart("other", k.Other, ref first);
                    Text.Append(')');
                }
                if (row.Tames >= 1)
                {
                    Text.Append(", ");
                }
            }
            if (row.Tames >= 1)
            {
                Text.Append(Number(row.Tames)).Append(" tamed");
            }
            Text.Append('\n');
        }
    }

    private static void AppendPart(string label, int value, ref bool first)
    {
        if (value <= 0)
        {
            return;
        }
        if (!first)
        {
            Text.Append(", ");
        }
        first = false;
        Text.Append(label).Append(' ').Append(Number(value));
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    // Most killed first, then most tamed, then name.
    private static int CompareMostKilled(Row a, Row b)
    {
        var c = b.Kills.Total.CompareTo(a.Kills.Total);
        if (c != 0)
        {
            return c;
        }
        c = b.Tames.CompareTo(a.Tames);
        return c != 0 ? c : CompareName(a, b);
    }

    // Alphabetical as player read it (current culture, ignore case). Ordinal last so order always same.
    private static int CompareName(Row a, Row b)
    {
        var c = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
    }
}
