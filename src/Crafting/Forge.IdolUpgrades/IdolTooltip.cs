using System.Text;

namespace MC.Crafting.ForgeIdolUpgradesMod;

// Idol tooltip: vanilla line "Quality: 3" (idols have max quality 4 now) become "Level: 2 stars" + chance, then
// what the next upgrade cost. Text keep $ tokens: caller localize (vanilla does too).
internal static class IdolTooltip
{
    private static readonly StringBuilder Text = new StringBuilder();

    internal static string Rewrite(ItemDrop.ItemData item, int qualityLevel, bool crafting, string vanilla)
    {
        var level = UnityEngine.Mathf.Clamp(qualityLevel - 1, 0, IdolLevels.Max);
        Text.Clear();
        Text.Append("\nLevel: <color=orange>").Append(level == 0 ? "no star" : IdolsTab.Stars(level))
            .Append("</color> (").Append(level).Append(" of ").Append(IdolLevels.Max).Append(')');
        Text.Append("\nRefinement chance: <color=orange>").Append(IdolLevels.ChancePercent(level)).Append("%</color>");
        if (level < IdolLevels.Max)
        {
            var idol = IdolCatalog.IdolOf(item);
            var tier = IdolCatalog.TierOf(idol);
            var target = level + 1;
            // Cost set to 0 = left out. ": " only before first part, ", " only between two parts (was ", " right after
            // the metal: "5 Silver, " with trophies 0). Both 0: line only say where to upgrade, end with "." (was
            // "Idols tab: " and nothing after).
            Text.Append("\n<color=#A0A0A0>Upgrade at a Forge of Potential, Idols tab");
            var material = ServerRules.Current.Material[target];
            var trophies = ServerRules.Current.Trophies[target];
            var parts = 0;
            if (material > 0 && tier != null)
            {
                // Tier metal no item of this game (typo or empty in settings): cost still hold it (upgrade blocked,
                // Idols tab say why in red). No name to show.
                Text.Append(": ").Append(material).Append(' ')
                    .Append(tier.Material != null ? tier.Material.m_itemData.m_shared.m_name : "metal of its biome");
                parts++;
            }
            if (trophies > 0)
            {
                Text.Append(parts > 0 ? ", " : ": ").Append(trophies).Append(' ').Append(IdolsTab.ClassName(target).ToLowerInvariant())
                    .Append(trophies == 1 ? " trophy" : " trophies").Append(" of its biome");
                parts++;
            }
            Text.Append(parts > 0 ? "</color>" : ".</color>");
        }
        else
        {
            Text.Append("\n<color=#A0A0A0>Maximum level.</color>");
        }
        var ours = Text.ToString();

        // Vanilla quality line (only when not crafting): replace it in place, else add ours at the end.
        var line = "\n$item_quality: <color=orange>" + qualityLevel + "</color>";
        var at = crafting ? -1 : vanilla.IndexOf(line, System.StringComparison.Ordinal);
        return at >= 0 ? vanilla.Remove(at, line.Length).Insert(at, ours) : vanilla + ours;
    }
}
