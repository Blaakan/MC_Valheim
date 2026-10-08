#if DEBUG
using System;
using System.Collections.Generic;
using BepInEx.Logging;
using MC.Shared;

namespace MC.Farming.CultivatorReplantMod;

// Debug build only. Me = ears on my own log source, from plugin load (Plugin.BindConfig). Self tests ask me what the
// mod said: warnings and errors of the whole session (replant.log), a line that must come (server rules line, cost
// warning). Other mods' lines never kept. Log may come from any thread: lock.
internal sealed class SelfTestLog : ILogListener
{
    internal struct Line
    {
        internal LogLevel Level;
        internal string Text;
    }

    // Plenty for one test run; more = me stop keeping (count still say how many came).
    private const int Cap = 20000;

    private static readonly List<Line> Lines = new List<Line>();
    private static SelfTestLog _instance;
    private static ManualLogSource _source;
    private static int _seen;

    internal static bool Installed => _instance != null;

    internal static void Install(ManualLogSource source)
    {
        if (_instance != null || source == null)
        {
            return;
        }
        _source = source;
        _instance = new SelfTestLog();
        Logger.Listeners.Add(_instance);
    }

    // How many of my lines came so far (mark for Since).
    internal static int Mark
    {
        get
        {
            lock (Lines)
            {
                return Lines.Count;
            }
        }
    }

    // My lines kept from mark on.
    internal static List<Line> Since(int mark)
    {
        lock (Lines)
        {
            var from = Math.Max(0, Math.Min(mark, Lines.Count));
            return Lines.GetRange(from, Lines.Count - from);
        }
    }

    // How many lines from mark on hold this text.
    internal static int CountSince(int mark, string part)
    {
        var count = 0;
        foreach (var line in Since(mark))
        {
            if (line.Text.IndexOf(part, StringComparison.Ordinal) >= 0)
            {
                count++;
            }
        }
        return count;
    }

    // My warnings and errors of the whole session, self test result lines left out.
    internal static List<Line> Problems()
    {
        var list = new List<Line>();
        foreach (var line in Since(0))
        {
            if ((line.Level & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) == 0)
            {
                continue;
            }
            if (line.Text.StartsWith(SelfTest.Prefix, StringComparison.Ordinal))
            {
                continue;
            }
            list.Add(line);
        }
        return list;
    }

    // Lines that came after the keep limit (0 in any normal run).
    internal static int Dropped
    {
        get
        {
            lock (Lines)
            {
                return _seen - Lines.Count;
            }
        }
    }

    // Lines of OTHER log sources that name this mod (another mod warning about me), whole session.
    internal static List<string> Mentions()
    {
        lock (Lines)
        {
            return new List<string>(Named);
        }
    }

    private static readonly List<string> Named = new List<string>();

    public void LogEvent(object sender, LogEventArgs eventArgs)
    {
        if (eventArgs == null)
        {
            return;
        }
        if (!ReferenceEquals(eventArgs.Source, _source))
        {
            // Cheap first: only warnings and errors of others, then two text searches.
            if ((eventArgs.Level & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) == 0 || eventArgs.Data == null)
            {
                return;
            }
            var text = eventArgs.Data.ToString();
            if (text.IndexOf(ModInfo.Name, StringComparison.Ordinal) >= 0 || text.IndexOf(ModInfo.Guid, StringComparison.Ordinal) >= 0)
            {
                lock (Lines)
                {
                    if (Named.Count < 1000)
                    {
                        Named.Add((eventArgs.Source != null ? eventArgs.Source.SourceName : "?") + ": " + text);
                    }
                }
            }
            return;
        }
        lock (Lines)
        {
            _seen++;
            if (Lines.Count < Cap)
            {
                Lines.Add(new Line { Level = eventArgs.Level, Text = eventArgs.Data != null ? eventArgs.Data.ToString() : "" });
            }
        }
    }

    public void Dispose()
    {
    }
}
#endif
