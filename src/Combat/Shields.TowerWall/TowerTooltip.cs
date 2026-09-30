using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MC.Combat.ShieldsTowerWallMod;

// Me = extra tooltip lines of a tower shield (design 2.3, 2.5, 2.6): "Cannot parry", bash stagger multiplier, bash
// cooldown (left out at 0), what bracing do. Vanilla already print "Two-handed", block armor, blunt damage, stamina use, knockback, "Movement -30%".
// English text, no localization key (decision 17). Tooltip path only (allocation fine).
internal static class TowerTooltip
{
    private const string Times = "×";

    // GetTooltip postfix: item carry tower data of the applied rules.
    internal static string Append(ItemDrop.ItemData item, string text)
    {
        var rules = TowerSync.Applied;
        if (rules == null || !TowerData.IsAppliedTower(item))
        {
            return text;
        }
        var sb = new StringBuilder(text);
        sb.Append("\n<color=orange>Cannot parry</color>");
        sb.Append("\nBash stagger: <color=orange>").Append(Times).Append(F(rules.BashStagger)).Append("</color>");
        if (rules.BashCooldown > 0f)
        {
            sb.Append("\nBash cooldown: <color=orange>").Append(F(rules.BashCooldown)).Append(" s</color>");
        }
        var line = BracedLine(rules);
        if (line.Length > 0)
        {
            sb.Append('\n').Append(line);
        }
        return sb.ToString();
    }

    // "Braced: movement -30%, blocks attacks from the front that normally cannot be blocked; while you have stamina
    // for another block: stagger -80%, no knockback from the front". Each part carry its own condition, as the code
    // apply it: slow and made-blockable whenever braced; stagger resist (every side) and push only while the brace
    // hold. Parts at 0 left out.
    internal static string BracedLine(TowerRules rules)
    {
        var always = new List<string>();
        var held = new List<string>();
        if (rules.BraceSlowPercent > 0)
        {
            always.Add($"movement -{rules.BraceSlowPercent}%");
        }
        if (rules.BlockUnblockableAttacks)
        {
            always.Add("blocks attacks from the front that normally cannot be blocked");
        }
        if (rules.BraceStaggerResistPercent > 0)
        {
            held.Add($"stagger -{rules.BraceStaggerResistPercent}%");
        }
        if (rules.BraceKnockbackResistPercent >= 100)
        {
            held.Add("no knockback from the front");
        }
        else if (rules.BraceKnockbackResistPercent > 0)
        {
            held.Add($"knockback from the front -{rules.BraceKnockbackResistPercent}%");
        }
        if (always.Count == 0 && held.Count == 0)
        {
            return "";
        }
        var sb = new StringBuilder("Braced");
        if (always.Count > 0)
        {
            sb.Append(": <color=orange>").Append(string.Join(", ", always.ToArray())).Append("</color>");
        }
        if (held.Count > 0)
        {
            sb.Append(always.Count > 0 ? "; while" : ", while").Append(" you have stamina for another block: <color=orange>")
                .Append(string.Join(", ", held.ToArray())).Append("</color>");
        }
        return sb.ToString();
    }

    private static string F(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
