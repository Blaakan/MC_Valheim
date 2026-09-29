using System;
using System.Collections.Generic;
using MC.Shared;

namespace MC.UX.AutoPickupFilterMod;

// Me = chat (/lootfilter) and F5 console commands. Not cheats: never taint achievements, chat allow them.
// Way to add or remove items you do not carry (inventory UI only mark items at hand). Console Tab only complete the
// FIRST argument, so item name is always first. Registered while feature Active, removed on deactivate.
internal static class LootFilterCommand
{
    private const string MainName = "lootfilter";
    private const string IgnoreName = "lootfilter_ignore";
    private const string SelectName = "lootfilter_select";
    private const string Usage =
        "lootfilter [item] | lootfilter mode everything|skip|only | lootfilter clear ignored|selected | lootfilter defaults"
        + " | lootfilter_ignore <item> | lootfilter_select <item>";

    private static Terminal.ConsoleCommand _main;
    private static Terminal.ConsoleCommand _ignore;
    private static Terminal.ConsoleCommand _select;

    private static readonly List<string> MainOptions = new List<string>();
    private static int _mainOptionsStamp = -1;

    internal static void Register()
    {
        // Constructor put itself in static Terminal.commands. alwaysRefresh: first Tab may come in main menu before
        // ObjectDB fill; our fetcher return a cached list, rebuilt only when ObjectDB changed.
        _main = new Terminal.ConsoleCommand(MainName,
            "[item] - loot pickup filter: no argument = show mode and lists; item = add/remove it in the list of the "
            + "current mode; also: mode everything|skip|only, clear ignored|selected, defaults",
            new Terminal.ConsoleEvent(RunMain), isCheat: false, optionsFetcher: MainTabOptions, alwaysRefreshTabOptions: true);
        _ignore = new Terminal.ConsoleCommand(IgnoreName,
            "[item] - add/remove an item in the loot pickup filter's Ignored list",
            new Terminal.ConsoleEvent(RunIgnore), isCheat: false, optionsFetcher: ItemTabOptions, alwaysRefreshTabOptions: true);
        _select = new Terminal.ConsoleCommand(SelectName,
            "[item] - add/remove an item in the loot pickup filter's Selected list",
            new Terminal.ConsoleEvent(RunSelect), isCheat: false, optionsFetcher: ItemTabOptions, alwaysRefreshTabOptions: true);
    }

    internal static void Unregister()
    {
        Remove(MainName, _main);
        Remove(IgnoreName, _ignore);
        Remove(SelectName, _select);
        _main = _ignore = _select = null;
    }

    // Only our own entry: another mod may have taken the name after us.
    private static void Remove(string name, Terminal.ConsoleCommand cmd)
    {
        if (cmd != null && Terminal.commands.TryGetValue(name, out var current) && ReferenceEquals(current, cmd))
        {
            Terminal.commands.Remove(name);
        }
    }

    private static List<string> ItemTabOptions() => ItemCatalog.AllNames();

    private static List<string> MainTabOptions()
    {
        var names = ItemCatalog.AllNames();
        if (_mainOptionsStamp != ItemCatalog.Stamp || MainOptions.Count == 0)
        {
            _mainOptionsStamp = ItemCatalog.Stamp;
            MainOptions.Clear();
            MainOptions.Add("mode");
            MainOptions.Add("clear");
            MainOptions.Add("defaults");
            MainOptions.AddRange(names);
        }
        return MainOptions;
    }

    // ---------------------------------------------------------------- runs

    private static void RunMain(Terminal.ConsoleEventArgs args) => Run(args, RunMainBody);

    private static void RunIgnore(Terminal.ConsoleEventArgs args) => Run(args, (ctx, words) => ToggleItem(ctx, words, FilterState.Ignored));

    private static void RunSelect(Terminal.ConsoleEventArgs args) => Run(args, (ctx, words) => ToggleItem(ctx, words, FilterState.Selected));

    // Common guard: never throw into Terminal, need a character.
    private static void Run(Terminal.ConsoleEventArgs args, Action<Terminal, List<string>> body)
    {
        var ctx = args.Context;
        try
        {
            if (!FilterState.EnsureLocal())
            {
                ctx.AddString("Load a character first.");
                return;
            }
            // Split(' ') keep empty words for double spaces: drop them. words[0] = command.
            var words = new List<string>();
            foreach (var w in args.Args)
            {
                if (!string.IsNullOrEmpty(w))
                {
                    words.Add(w);
                }
            }
            body(ctx, words);
        }
        catch (Exception e)
        {
            PatchGuard.Report($"{nameof(LootFilterCommand)}.{args[0]}", e);
            ctx.AddString("The loot filter command failed; see BepInEx/LogOutput.log.");
        }
    }

    private static void RunMainBody(Terminal ctx, List<string> words)
    {
        if (words.Count < 2)
        {
            PrintStatus(ctx);
            return;
        }
        switch (words[1].ToLowerInvariant())
        {
            case "mode":
                SetMode(ctx, words.Count > 2 ? words[2] : null);
                return;
            case "clear":
                Clear(ctx, words.Count > 2 ? words[2] : null);
                return;
            case "defaults":
                FilterState.ApplyDefaults();
                ctx.AddString($"Loot filter lists replaced by the Defaults of the config file: {FilterState.Ignored.Count} ignored, "
                              + $"{FilterState.Selected.Count} selected.");
                return;
        }
        var list = FilterState.ActiveList;
        if (list == null)
        {
            ctx.AddString("The filter mode is Everything, so there is no list to change. Use lootfilter mode skip (or only) "
                          + "first, or lootfilter_ignore / lootfilter_select.");
            return;
        }
        ToggleItem(ctx, words, list);
    }

    private static void PrintStatus(Terminal ctx)
    {
        ctx.AddString($"{ModInfo.Name}: mode {FilterState.ModeLabel(FilterState.Mode)}, auto pickup "
                      + (Player.m_enableAutoPickup ? "on." : "off (turn it on with the auto pickup key)."));
        ctx.AddString($"Ignored ({FilterState.Ignored.Count}): {FilterState.DescribeForConsole(FilterState.Ignored)}");
        ctx.AddString($"Selected ({FilterState.Selected.Count}): {FilterState.DescribeForConsole(FilterState.Selected)}");
        ctx.AddString("Commands: " + Usage);
    }

    private static void SetMode(Terminal ctx, string word)
    {
        FilterMode mode;
        switch (word?.ToLowerInvariant())
        {
            case "everything":
            case "all":
                mode = FilterMode.Everything;
                break;
            case "skip":
            case "skipignored":
            case "ignored":
                mode = FilterMode.SkipIgnored;
                break;
            case "only":
            case "onlyselected":
            case "selected":
                mode = FilterMode.OnlySelected;
                break;
            default:
                ctx.AddString("Use: lootfilter mode everything|skip|only");
                return;
        }
        FilterState.SetMode(mode);
        FilterUi.ShowModeMessage();
        var loc = Localization.instance;
        var text = FilterUi.ModeMessage();
        ctx.AddString(loc != null ? loc.Localize(text) : text);
    }

    private static void Clear(Terminal ctx, string word)
    {
        HashSet<string> list;
        switch (word?.ToLowerInvariant())
        {
            case "ignored":
            case "ignore":
                list = FilterState.Ignored;
                break;
            case "selected":
            case "select":
                list = FilterState.Selected;
                break;
            default:
                ctx.AddString("Use: lootfilter clear ignored|selected");
                return;
        }
        var n = FilterState.ClearList(list);
        ctx.AddString($"Loot filter: {FilterState.ListName(list)} list cleared ({n} removed).");
    }

    // words[1] = item. Known item: exact spawn name. Unknown but already in the list (item of a mod not installed
    // now): still removable.
    private static void ToggleItem(Terminal ctx, List<string> words, HashSet<string> list)
    {
        if (words.Count < 2)
        {
            ctx.AddString($"Use: {words[0]} <item> (the spawn name; press Tab to complete).");
            return;
        }
        var typed = words[1];
        var key = ItemCatalog.Resolve(typed) ?? FindInList(list, typed);
        if (key == null)
        {
            ctx.AddString($"Unknown item: {typed} (use the spawn name; press Tab to complete)");
            return;
        }
        var added = FilterState.Toggle(list, key);
        var name = ItemCatalog.DisplayName(key);
        var shown = string.Equals(name, key, StringComparison.Ordinal) ? key : $"{key} ({name})";
        var ignored = ReferenceEquals(list, FilterState.Ignored);
        ctx.AddString(added
            ? (ignored ? $"Ignored by auto pickup: {shown}" : $"Selected for auto pickup: {shown}")
            : (ignored ? $"No longer ignored: {shown}" : $"No longer selected: {shown}"));
        var active = FilterState.ActiveList;
        if (!ReferenceEquals(active, list))
        {
            ctx.AddString($"(The current mode is {FilterState.ModeLabel(FilterState.Mode)}: this list applies in "
                          + (ignored ? "Skip ignored" : "Only selected") + " mode.)");
        }
    }

    private static string FindInList(HashSet<string> list, string typed)
    {
        foreach (var key in list)
        {
            if (string.Equals(key, typed, StringComparison.OrdinalIgnoreCase))
            {
                return key;
            }
        }
        return null;
    }
}
