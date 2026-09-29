using System.Diagnostics;
#if DEBUG
using System;
using System.Text;
using MC.Shared;
#endif

namespace MC.Exploration.CompendiumEncyclopediaMod;

// Debug build only (calls vanish in Release): console command "compendium_debug [entry key or prefab]" (F5).
// No argument: catalog state, counts per tab, knowledge summary. With argument: that entry's detail lines (real
// knowledge) in the console and the log. For the UI author and for T05 notes; not a cheat, change nothing.
internal static class DebugCommand
{
#if DEBUG
    private const string Name = "compendium_debug";
    private static Terminal.ConsoleCommand _command;
#endif

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        _command = new Terminal.ConsoleCommand(Name,
            "[entry key or prefab] - Encyclopedia debug: catalog counts and knowledge, or one entry's details",
            new Terminal.ConsoleEvent(Run), isCheat: false);
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        if (_command != null && Terminal.commands.TryGetValue(Name, out var current) && ReferenceEquals(current, _command))
        {
            Terminal.commands.Remove(Name);
        }
        _command = null;
#endif
    }

#if DEBUG
    private static void Run(Terminal.ConsoleEventArgs args)
    {
        try
        {
            var cat = CatalogService.EnsureReady();
            if (cat == null)
            {
                Say(args, CatalogService.IsBuilding ? "Encyclopedia catalog is building, try again in a second." : "No world loaded.");
                return;
            }
            OwnRecords.RecordCurrentBiome();
            var k = Knowledge.Take(cat, Plugin.RevealAllOn);
            if (args.Length < 2)
            {
                var sb = new StringBuilder();
                sb.Append($"Encyclopedia: {cat.ItemCount} items, {cat.PieceCount} pieces, {cat.CreatureCount} creatures, "
                          + $"{cat.HiddenCount} hidden until known, {cat.NoSourceCount} items with no source. ");
                for (var t = 0; t < Tabs.Count; t++)
                {
                    ListBuilder.CountTab(cat, k, (CatalogTab)t, out var d, out var l);
                    sb.Append((CatalogTab)t).Append(' ').Append(d).Append('/').Append(l).Append(t < Tabs.Count - 1 ? ", " : ". ");
                }
                sb.Append(k.Summary());
                Say(args, sb.ToString());
                return;
            }
            var key = args.ArgsAll.Trim();
            var entry = cat.FindItem(key) ?? cat.FindPiece(key) ?? cat.FindCreature(key) ?? cat.FindItemByPrefab(key);
            if (entry == null && cat.PiecesByPrefab.TryGetValue(key, out var p))
            {
                entry = p;
            }
            if (entry == null && cat.CreaturesByPrefab.TryGetValue(key, out var cr))
            {
                entry = cr;
            }
            if (entry == null)
            {
                Say(args, $"No Encyclopedia entry '{key}' (use a token like $item_wood or a prefab name like Wood).");
                return;
            }
            Say(args, DetailBuilder.Build(entry, k).Dump());
        }
        catch (Exception e)
        {
            PatchGuard.Report("compendium_debug", e);
        }
    }

    private static void Say(Terminal.ConsoleEventArgs args, string text)
    {
        Log.Info(text);
        if (args.Context != null)
        {
            args.Context.AddString(text);
        }
    }
#endif
}
