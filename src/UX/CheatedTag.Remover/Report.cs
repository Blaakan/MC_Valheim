using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MC.UX.CheatedTagRemoverMod;

// Me = the words a player read after a command. Pure text from counts: no game call, self test check it alone.
// Lines split by '\n' (one console line each).
internal static class Report
{
    internal const string NothingFound = "No cheated tag found.";

    // Scan: counts only, nothing changed. wholeWorld false = a game alone (own items and loaded objects).
    internal static string Scan(SweepCounts found, bool wholeWorld, bool mismatch = false)
    {
        var text = new StringBuilder();
        var all = WithOpenChests(found);
        if (all.Everything == 0)
        {
            text.Append(NothingFound);
        }
        else
        {
            text.Append("Cheated tags found (nothing was changed): ").Append(List(all)).Append('.');
            if (found.StoragesInUse > 0)
            {
                text.Append('\n').Append(Count(found.StoragesInUse, "of these chests is", "of these chests are"))
                    .Append(" open on a player right now (or was left open by a player who lost the connection)")
                    .Append(Places(found)).Append(": ").Append(ClearCommand.ClearName)
                    .Append(" leaves such a chest alone until it has been closed.");
            }
            text.Append('\n').Append("Type ").Append(ClearCommand.ClearName).Append(" to remove them.");
        }
        AppendUnknown(text, found);
        if (!wholeWorld)
        {
            AppendLocalNote(text, false, mismatch);
        }
        return text.ToString();
    }

    // Clear on the game that hold the world. removed = before minus after (plus own carried items), after = what
    // the last check still found.
    internal static string Clear(SweepCounts removed, SweepCounts after, int playersWith, int playersWithout, bool saving)
    {
        var text = new StringBuilder();
        if (removed.Everything == 0)
        {
            text.Append(after.World + after.StoredItemsInUse == 0 ? NothingFound : "No cheated tag could be removed.");
        }
        else
        {
            text.Append("Cheated tags removed: ").Append(List(removed)).Append('.');
        }
        AppendOpenChests(text, after);
        if (after.World > 0)
        {
            text.Append('\n').Append("Still tagged: ").Append(List(after))
                .Append(". Another player's game keeps writing these objects right now (a creature it moves, a station "
                        + "it runs). Type ").Append(ClearCommand.ClearName).Append(" again in a moment.");
        }
        else if (removed.Everything > 0 && after.StoragesInUse == 0)
        {
            text.Append('\n').Append("Checked again: no cheated tag is left in the world.");
        }
        AppendUnknown(text, after);
        if (playersWith > 0)
        {
            text.Append('\n').Append(Count(playersWith, "player online has", "players online have"))
                .Append(" this mod: their games were asked to clean the items they carry.");
        }
        if (playersWithout > 0)
        {
            text.Append('\n').Append(Count(playersWithout, "player online does", "players online do"))
                .Append(" not have this mod: the items they carry keep the tag, and can spread it again.");
        }
        if (saving)
        {
            text.Append('\n').Append("Saving the world.");
        }
        return text.ToString();
    }

    // Client alone (server has no copy of me, it is off there, or it is another version): own items and loaded area
    // only. counts = what this game wrote, said at once (everything written was its own).
    internal static string Local(SweepCounts counts, bool write, bool mismatch)
    {
        if (!write)
        {
            return Scan(counts, false, mismatch);
        }
        var text = new StringBuilder();
        if (counts.Everything - counts.StoredItemsInUse == 0)
        {
            text.Append(counts.StoragesInUse == 0 ? "No cheated tag found on your game." : "No cheated tag could be removed.");
        }
        else
        {
            text.Append("Cheated tags removed: ").Append(List(counts)).Append('.');
        }
        AppendOpenChests(text, counts);
        AppendUnknown(text, counts);
        AppendLocalNote(text, true, mismatch);
        return text.ToString();
    }

    // Client alone, after it looked again: some of what it wrote belong to other players' games, which can undo it.
    internal static string LocalChecked(SweepCounts removed, SweepCounts after, bool mismatch)
    {
        var text = new StringBuilder();
        if (removed.Everything == 0)
        {
            text.Append(after.World + after.StoredItemsInUse == 0 ? "No cheated tag found on your game." : "No cheated tag could be removed.");
        }
        else
        {
            text.Append("Cheated tags removed: ").Append(List(removed)).Append('.');
        }
        AppendOpenChests(text, after);
        if (after.World > 0)
        {
            // Counts are of what is loaded now: an object that just came into view is in too.
            text.Append('\n').Append("Still tagged around you: ").Append(List(after))
                .Append(". Another player's game controls these objects right now, or they were just loaded. Type ")
                .Append(ClearCommand.ClearName).Append(" again in a moment.");
        }
        AppendUnknown(text, after);
        AppendLocalNote(text, true, mismatch);
        return text.ToString();
    }

    // Player whose game cleaned its own side (asked by the server, or before it asked the server itself).
    internal static string Helped(SweepCounts counts)
    {
        return counts.Everything == 0 ? "" : "Cheated tags removed on your game: " + List(counts) + ".";
    }

    // "3 building pieces, 12 items in 2 chests or other storage". Open chests are not in (see WithOpenChests).
    internal static string List(SweepCounts c)
    {
        var parts = new List<string>();
        Add(parts, c.Pieces, "building piece", "building pieces");
        Add(parts, c.Creatures, "creature", "creatures");
        Add(parts, c.OtherObjects, "other object", "other objects");
        Add(parts, c.Queues, "station queue", "station queues");
        if (c.StoredItems > 0)
        {
            parts.Add(Count(c.StoredItems, "item", "items") + " in "
                      + Count(c.Storages, "chest or other storage", "chests or other storage"));
        }
        Add(parts, c.SingleItems, "item on the ground or on a stand", "items on the ground or on stands");
        Add(parts, c.CarriedItems, "item you carry", "items you carry");
        return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
    }

    // World part of before minus after, never below zero (a player without me can tag something new in between).
    // Open chests count as tagged on both sides.
    internal static SweepCounts Removed(SweepCounts before, SweepCounts after, int carried)
    {
        return new SweepCounts
        {
            Pieces = Math.Max(0, before.Pieces - after.Pieces),
            Creatures = Math.Max(0, before.Creatures - after.Creatures),
            OtherObjects = Math.Max(0, before.OtherObjects - after.OtherObjects),
            Queues = Math.Max(0, before.Queues - after.Queues),
            StoredItems = Math.Max(0, before.StoredItems + before.StoredItemsInUse - after.StoredItems - after.StoredItemsInUse),
            Storages = Math.Max(0, before.Storages + before.StoragesInUse - after.Storages - after.StoragesInUse),
            SingleItems = Math.Max(0, before.SingleItems - after.SingleItems),
            CarriedItems = carried,
        };
    }

    private static SweepCounts WithOpenChests(SweepCounts c)
    {
        return new SweepCounts
        {
            Pieces = c.Pieces,
            Creatures = c.Creatures,
            OtherObjects = c.OtherObjects,
            Queues = c.Queues,
            StoredItems = c.StoredItems + c.StoredItemsInUse,
            Storages = c.Storages + c.StoragesInUse,
            SingleItems = c.SingleItems,
            CarriedItems = c.CarriedItems,
        };
    }

    // Chest skipped as "in use": a window open on another player, or the mark a player left when its game died
    // with the window open and another player's game got the chest (me cannot tell them apart).
    private static void AppendOpenChests(StringBuilder text, SweepCounts c)
    {
        if (c.StoragesInUse <= 0)
        {
            return;
        }
        text.Append('\n').Append("Not removed: ").Append(Count(c.StoredItemsInUse, "item", "items")).Append(" in ")
            .Append(Count(c.StoragesInUse, "chest", "chests"))
            .Append(" that a player has open right now, or that a player who lost the connection left open")
            .Append(Places(c)).Append(". Open and close ")
            .Append(c.StoragesInUse == 1 ? "that chest" : "those chests").Append(" once, then type ")
            .Append(ClearCommand.ClearName).Append(" again.");
    }

    // " (at 120 -340; 5 80)": map position x z of the first few.
    private static string Places(SweepCounts c)
    {
        if (c.InUsePlaces.Count == 0)
        {
            return "";
        }
        var parts = new List<string>();
        foreach (var place in c.InUsePlaces)
        {
            parts.Add(Whole(place.x) + " " + Whole(place.z));
        }
        return " (at " + string.Join("; ", parts) + (c.StoragesInUse > c.InUsePlaces.Count ? "; ..." : "") + ")";
    }

    private static string Whole(float value) => ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture);

    private static void AppendUnknown(StringBuilder text, SweepCounts c)
    {
        if (c.UnknownBlobs > 0)
        {
            text.Append('\n').Append("Left untouched: ").Append(Count(c.UnknownBlobs, "saved item list", "saved item lists"))
                .Append(" in a format this version of the mod does not know.");
        }
    }

    private static void AppendLocalNote(StringBuilder text, bool write, bool mismatch)
    {
        text.Append('\n');
        if (mismatch)
        {
            text.Append("The server has another version of ").Append(ModInfo.Name);
        }
        else
        {
            text.Append("The server does not have ").Append(ModInfo.Name).Append(" (or has it turned off)");
        }
        text.Append(", so only the items you carry and the objects loaded around you were ")
            .Append(write ? "cleaned" : "counted").Append(". For the whole world, ")
            .Append(mismatch ? "use the same version on the server and on your game" : "install the mod on the server too")
            .Append(", or open a copy of the world in single player and type the command there.");
    }

    private static void Add(List<string> parts, int n, string one, string many)
    {
        if (n > 0)
        {
            parts.Add(Count(n, one, many));
        }
    }

    private static string Count(int n, string one, string many) => n + " " + (n == 1 ? one : many);
}
