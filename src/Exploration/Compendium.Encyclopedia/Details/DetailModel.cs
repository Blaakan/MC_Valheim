using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MC.Exploration.CompendiumEncyclopediaMod;

/// <summary>
/// Me = a reference to another catalog entry (or a biome) inside a detail line, already resolved against the
/// knowledge snapshot: <see cref="Name"/> is the real name only when <see cref="Known"/>, else "???"; <see cref="Icon"/>
/// is null unless known. UI print <see cref="Name"/>, draw per <see cref="IconKind"/>, and make it a link only when
/// <see cref="Clickable"/>. UI never read <see cref="Entry"/>'s name or icon itself.
/// </summary>
internal sealed class RefTarget
{
    /// <summary>Target entry. Null = biome ref, or target not listed in the catalog (always "???").</summary>
    internal readonly Entry Entry;

    /// <summary>Biome of a biome ref (None for entry refs).</summary>
    internal readonly Heightmap.Biome Biome;

    internal readonly bool Known;

    /// <summary>Text to print: real name when known, "???" otherwise.</summary>
    internal readonly string Name;

    /// <summary>How to draw the icon slot.</summary>
    internal readonly IconKind IconKind;

    /// <summary>Sprite for <see cref="IconKind.Sprite"/>, else null.</summary>
    internal readonly Sprite Icon;

    private RefTarget(Entry entry, Heightmap.Biome biome, bool known, string name, IconKind iconKind, Sprite icon)
    {
        Entry = entry;
        Biome = biome;
        Known = known;
        Name = name;
        IconKind = iconKind;
        Icon = icon;
    }

    /// <summary>Known entry ref can open that entry (link, extra).</summary>
    internal bool Clickable => Known && Entry != null;

    internal bool IsBiome => Entry == null && Biome != Heightmap.Biome.None;

    internal static RefTarget Of(Entry e, Knowledge k)
    {
        var iconKind = Presentation.Icon(e, k, out var sprite);
        var known = e != null && k.IsKnown(e);
        return new RefTarget(e, Heightmap.Biome.None, known, known ? e.DisplayName : Labels.Unknown, iconKind, sprite);
    }

    internal static RefTarget OfBiome(Heightmap.Biome biome, Knowledge k)
    {
        var known = k.IsBiomeKnown(biome);
        return new RefTarget(null, biome, known, Presentation.BiomeName(biome, k), known ? IconKind.None : IconKind.Unknown, null);
    }
}

/// <summary>
/// One piece of a detail line: plain text, or a <see cref="RefTarget"/>. <see cref="OwnText"/> = the entry's own game
/// text (description, stats, lore) shown as the game writes it.
/// </summary>
internal readonly struct DetailPart
{
    internal readonly string Text;
    internal readonly RefTarget Ref;
    internal readonly bool OwnText;

    internal DetailPart(string text, RefTarget target, bool ownText)
    {
        Text = text;
        Ref = target;
        OwnText = ownText;
    }

    internal bool IsRef => Ref != null;

    /// <summary>What to print (ref = resolved name).</summary>
    internal string Display => Ref != null ? Ref.Name : Text ?? "";
}

/// <summary>Kind of detail line (how UI style it).</summary>
internal enum DetailLineKind : byte
{
    /// <summary>Orange section title ("Crafting", "Drops"...). May hold refs ("Quality 2 (Forge level 2)").</summary>
    Header,

    /// <summary>Block of rich text (description and stats, lore, notices). Multi-line.</summary>
    Paragraph,

    /// <summary>One row: text and refs in order. <see cref="DetailLine.IconRef"/> = icon for the row (first ref).</summary>
    Row,

    /// <summary>"??? ×N not discovered yet": N unknown targets of a long list folded in one row ("?" mark icon).</summary>
    Collapsed,
}

/// <summary>One line of the detail pane. UI render lines top to bottom; <see cref="Indent"/> = nesting level.</summary>
internal sealed class DetailLine
{
    internal DetailLineKind Kind;
    internal int Indent;
    internal readonly List<DetailPart> Parts = new List<DetailPart>(4);

    /// <summary>Collapsed: how many unknown targets folded.</summary>
    internal int CollapsedCount;

    /// <summary>First ref of the line (row icon), or null.</summary>
    internal RefTarget IconRef
    {
        get
        {
            foreach (var p in Parts)
            {
                if (p.Ref != null)
                {
                    return p.Ref;
                }
            }
            return null;
        }
    }

    /// <summary>Printed text of the line (refs resolved, tags kept).</summary>
    internal string Text()
    {
        if (Parts.Count == 1)
        {
            return Parts[0].Display;
        }
        var sb = new StringBuilder();
        foreach (var p in Parts)
        {
            sb.Append(p.Display);
        }
        return sb.ToString();
    }
}

/// <summary>
/// Me = the detail pane of one entry, UI-agnostic: title, icon, subtitle, lines. Built by
/// <see cref="DetailBuilder.Build"/>. <see cref="Title"/> / <see cref="Icon"/> already resolved against knowledge.
/// </summary>
internal sealed class DetailView
{
    internal Entry Entry;

    /// <summary>Entry discovered (else title "???" and only the "Not discovered yet" text).</summary>
    internal bool Known;

    internal string Title = Labels.Unknown;
    internal IconKind IconKind = IconKind.Unknown;
    internal Sprite Icon;
    internal string Subtitle = "";
    internal readonly List<DetailLine> Lines = new List<DetailLine>(32);

    /// <summary>Whole build threw: lines = one "Details unavailable (see the log)." paragraph.</summary>
    internal bool Failed;

    /// <summary>Blocks that threw and were dropped (each logged once per site by PatchGuard).</summary>
    internal int FailedBlocks;

    /// <summary>What failed (block name: message), for self tests and logs.</summary>
    internal readonly List<string> Errors = new List<string>();

    /// <summary>All lines as plain text (debug, logs, tests).</summary>
    internal string Dump()
    {
        var sb = new StringBuilder();
        sb.Append(Title).Append(" | ").Append(Subtitle);
        foreach (var l in Lines)
        {
            sb.Append('\n').Append(' ', l.Indent * 2).Append('[').Append(l.Kind).Append("] ").Append(l.Text());
        }
        return sb.ToString();
    }
}
