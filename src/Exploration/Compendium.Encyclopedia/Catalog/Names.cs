using System;
using System.Globalization;
using System.Text;
using MC.Shared;

namespace MC.Exploration.CompendiumEncyclopediaMod;

/// <summary>
/// Me = localized names, sort keys and search keys. Game Localize cache only 100 entries, so each entry keep own name,
/// made again only when language change (<see cref="Stamp"/> go up). Tags (TMP rich text) stripped for sort and search.
/// </summary>
internal static class Names
{
    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;
    private const CompareOptions SortOptions = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    /// <summary>Go up at every language switch. Entry name caches compare it.</summary>
    internal static int Stamp { get; private set; } = 1;

    /// <summary>Hooked to <c>Localization.OnLanguageChange</c> while feature active. Me never throw (multicast).</summary>
    internal static void OnLanguageChanged()
    {
        try
        {
            Stamp++;
        }
        catch (Exception e)
        {
            PatchGuard.Report("Names.OnLanguageChanged", e);
        }
    }

    /// <summary>Game translation of token or text. No Localization (should not happen in world) = text itself.</summary>
    internal static string Localize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }
        var loc = Localization.instance;
        return loc == null ? text : loc.Localize(text);
    }

    /// <summary>
    /// True when translation is useless as a name: empty, or game "missing key" form "[word]" (Localize give that for a
    /// token with no translation).
    /// </summary>
    internal static bool IsMissing(string localized)
    {
        if (string.IsNullOrEmpty(localized) || localized.Trim().Length == 0)
        {
            return true;
        }
        return localized.Length >= 2 && localized[0] == '[' && localized[localized.Length - 1] == ']'
               && localized.IndexOf(']') == localized.Length - 1;
    }

    /// <summary>Text without TMP tags (anything between &lt; and &gt;).</summary>
    internal static string StripTags(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('<') < 0)
        {
            return text ?? "";
        }
        var sb = new StringBuilder(text.Length);
        var inTag = false;
        foreach (var c in text)
        {
            if (c == '<')
            {
                inTag = true;
            }
            else if (c == '>' && inTag)
            {
                inTag = false;
            }
            else if (!inTag)
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>Search form, like vanilla BuildUi.UpdateSearch: tags out, lower-case invariant, no whitespace.</summary>
    internal static string SearchKey(string text)
    {
        var plain = StripTags(text);
        var sb = new StringBuilder(plain.Length);
        foreach (var c in plain)
        {
            if (!char.IsWhiteSpace(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }
        return sb.ToString();
    }

    /// <summary>Name order: invariant culture, ignore case and accents.</summary>
    internal static int CompareNames(string a, string b) => Compare.Compare(a ?? "", b ?? "", SortOptions);

    /// <summary>Whole number, invariant culture.</summary>
    internal static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);

    /// <summary>Game keep counts as float; me round, junk (negative, NaN) = 0.</summary>
    internal static int ToCount(float value)
    {
        if (!(value > 0f))
        {
            return 0;
        }
        if (value >= int.MaxValue)
        {
            return int.MaxValue;
        }
        return (int)(value + 0.5f);
    }
}
