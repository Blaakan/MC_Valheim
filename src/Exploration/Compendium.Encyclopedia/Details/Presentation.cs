using UnityEngine;

namespace MC.Exploration.CompendiumEncyclopediaMod;

/// <summary>
/// Me = the only spoiler-safe way to show an entry or biome: name, icon, "?" mark. UI and DetailBuilder never print
/// <see cref="Entry.DisplayName"/> or set <see cref="Entry.Icon"/> without going through me (goals 4, 5, 7).
/// </summary>
internal static class Presentation
{
    /// <summary>Real translated name when known, else "???". Null entry (target not listed) = "???".</summary>
    internal static string Name(Entry e, Knowledge k) => e != null && k != null && k.IsKnown(e) ? e.DisplayName : Labels.Unknown;

    /// <summary>
    /// Icon to show. Unknown = "?" mark (sprite null). Known item/piece = its sprite. Known creature = its trophy's sprite
    /// only when that trophy is known too, else generic creature icon (sprite null; UI draw the paw print,
    /// <see cref="PawIcon"/>). So an unknown trophy's sprite never shows.
    /// </summary>
    internal static IconKind Icon(Entry e, Knowledge k, out Sprite sprite)
    {
        sprite = null;
        if (e == null || k == null || !k.IsKnown(e))
        {
            return IconKind.Unknown;
        }
        if (e.Kind != EntryKind.Creature)
        {
            sprite = e.Icon;
            return sprite != null ? IconKind.Sprite : IconKind.None;
        }
        var trophy = e.Creature.Trophy;
        if (trophy != null && k.IsKnown(trophy) && trophy.Icon != null)
        {
            sprite = trophy.Icon;
            return IconKind.Sprite;
        }
        return IconKind.GenericCreature;
    }

    /// <summary>Biome name in game language when known, else "???".</summary>
    internal static string BiomeName(Heightmap.Biome biome, Knowledge k) =>
        k != null && k.IsBiomeKnown(biome) ? Names.Localize(Biomes.Token(biome)) : Labels.Unknown;
}
