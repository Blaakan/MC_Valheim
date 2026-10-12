using MC.Shared;

namespace MC.UX.CheatedTagRemoverMod;

// Me = where answers go. Command answer can come seconds later (server work, then rpc), so me remember the window
// the player typed in (F5 console or chat) and write there; gone or never set = the console. Always the log too.
// Dedicated server: log only (its console object write to the same log by itself).
internal static class Output
{
    private static Terminal _context;

#if DEBUG
    // Self test read what me said (every line, in order). Null = normal.
    internal static System.Collections.Generic.List<string> TestLines { get; set; }
#endif

    internal static void SetContext(Terminal context)
    {
        _context = context;
    }

    internal static void Say(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        var net = ZNet.instance;
        var dedicated = net != null && net.IsDedicated();
        Terminal target = _context;
        if (target == null)
        {
            target = global::Console.instance;
        }
        foreach (var line in text.Split('\n'))
        {
            if (line.Length == 0)
            {
                continue;
            }
            Log.Info(line);
#if DEBUG
            TestLines?.Add(line);
#endif
            if (!dedicated && target != null)
            {
                target.AddString(line);
            }
        }
    }
}
