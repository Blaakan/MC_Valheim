#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using MC.Farming.BreedingStarInheritanceMod.Patches;
using MC.Shared;
using UnityEngine;

namespace MC.Farming.BreedingStarInheritanceMod;

// Debug build only. Me = what self tests need from the mod without touching config or parsing guesses:
//   switches the mod read through one accessor each (ForceOff -> Plugin.LocalBlocker, RollOverride -> birth roll);
//   notes the patches leave (every birth and conception they handled: numbers, not log text);
//   session log of this mod's own log source (exact Debug / Info / Warning lines, from first activation on).
// Release build has none of this.
internal static partial class SelfTests
{
    // ---------- switches ----------

    // True = Plugin.LocalBlocker say "off": patches gone, OnDeactivated ran. Test call FeatureRegistry.RefreshAll
    // after set and after clear. Never cleared by Unregister (that run inside the turn off).
    internal static bool ForceOff;

    internal const string ForceOffText = "Inactive: turned off by a self-test.";

    // Set = every birth use this roll (0..1) instead of Unity random.
    internal static float? RollOverride;

    internal static float TestRoll(float roll)
    {
        return RollOverride ?? roll;
    }

    // ---------- notes from the patches ----------

    internal enum ConceptionKind : byte
    {
        Partner,    // nearest ready partner noted
        BredAlone,  // species breed alone, no partner near
        Unseen,     // game counted partner me cannot see
    }

    // One birth the patch handled (copy of its state: safe to keep).
    internal struct BirthNote
    {
        internal int Seq;
        internal bool Decided;
        internal string Prefab;
        internal int Own;
        internal int Partner;
        internal PartnerSource Source;
        internal float PartnerDistance;
        internal bool FarmerKnown;
        internal bool FarmerLocal;
        internal float FarmingLevel;
        internal string FarmerName;
        internal SettingsSource Settings;
        internal BirthDecision Decision;
        internal int MinOffspring;
    }

    internal struct ConceptionNote
    {
        internal int Seq;
        internal ConceptionKind Kind;
        internal int Own;
        internal int Partner;
        internal float Distance;
    }

    private const int MaxBirthNotes = 1024;

    internal static int BirthCount;
    internal static BirthNote LastBirth;
    internal static readonly List<BirthNote> Births = new List<BirthNote>();
    internal static int ConceptionCount;
    internal static ConceptionNote LastConception;

    // Called by ProcreationPatches.OnBirth (birth happened in this call). Never throw into the patch.
    internal static void NoteBirth(Procreation proc, in BirthState st)
    {
        try
        {
            var note = new BirthNote
            {
                Seq = ++BirthCount,
                Decided = st.Decided,
                Prefab = proc != null ? Utils.GetPrefabName(proc.gameObject) : "?",
                Own = st.OriginalLevel,
                Partner = st.Partner,
                Source = st.Source,
                PartnerDistance = st.PartnerDistance,
                FarmerKnown = st.FarmerKnown,
                FarmerLocal = st.FarmerLocal,
                FarmingLevel = st.FarmingLevel,
                FarmerName = st.Farmer != null ? st.Farmer.GetPlayerName() : "",
                Settings = st.Settings,
                Decision = st.Decision,
                MinOffspring = proc != null ? proc.m_minOffspringLevel : 0,
            };
            LastBirth = note;
            if (Births.Count >= MaxBirthNotes)
            {
                Births.RemoveRange(0, MaxBirthNotes / 2);
            }
            Births.Add(note);
        }
        catch (Exception)
        {
            // Test note only: never break a birth.
        }
    }

    internal static void NoteConception(ConceptionKind kind, int own, int partner, float distance)
    {
        LastConception = new ConceptionNote
        {
            Seq = ++ConceptionCount,
            Kind = kind,
            Own = own,
            Partner = partner,
            Distance = distance,
        };
    }

    // ---------- log of one log source ----------

    internal struct LogLine
    {
        internal int Seq;
        internal LogLevel Level;
        internal string Text;
    }

    // Me listen to BepInEx log (every level reach listeners, whatever the console/file show) and keep lines of ONE
    // source. Log events can come from any thread: me lock. Old lines dropped when too many.
    private sealed class LogTap : ILogListener
    {
        private const int MaxLines = 4000;
        private readonly object _gate = new object();
        private readonly List<LogLine> _lines = new List<LogLine>();
        private readonly string _source;
        private int _seq;

        internal LogTap(string source)
        {
            _source = source;
        }

        // Number of the last line seen so far: lines after it = "since now".
        internal int Mark()
        {
            lock (_gate)
            {
                return _seq;
            }
        }

        internal List<LogLine> Since(int mark)
        {
            var result = new List<LogLine>();
            lock (_gate)
            {
                foreach (var line in _lines)
                {
                    if (line.Seq > mark)
                    {
                        result.Add(line);
                    }
                }
            }
            return result;
        }

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            try
            {
                if (eventArgs == null || eventArgs.Source == null || eventArgs.Source.SourceName != _source)
                {
                    return;
                }
                var text = eventArgs.Data as string ?? eventArgs.Data?.ToString() ?? "";
                lock (_gate)
                {
                    if (_lines.Count >= MaxLines)
                    {
                        _lines.RemoveRange(0, MaxLines / 2);
                    }
                    _lines.Add(new LogLine { Seq = ++_seq, Level = eventArgs.Level, Text = text });
                }
            }
            catch
            {
                // Me never throw inside logger.
            }
        }

        public void Dispose()
        {
        }
    }

    // This mod's own lines, whole session (from first activation). Stay on: Debug build only.
    private static LogTap _log;

    private static void StartSessionLog()
    {
        if (_log != null)
        {
            return;
        }
        _log = new LogTap(ModInfo.Name);
        BepInEx.Logging.Logger.Listeners.Add(_log);
    }

    private static int LogMark()
    {
        return _log != null ? _log.Mark() : 0;
    }

    private static List<LogLine> LogSince(int mark)
    {
        return _log != null ? _log.Since(mark) : new List<LogLine>();
    }

    // Lines since mark that hold this text (ordinal).
    private static List<string> LinesWith(int mark, string text)
    {
        var result = new List<string>();
        foreach (var line in LogSince(mark))
        {
            if (line.Text.IndexOf(text, StringComparison.Ordinal) >= 0)
            {
                result.Add(line.Text);
            }
        }
        return result;
    }

    private static int CountLines(int mark, string text)
    {
        return LinesWith(mark, text).Count;
    }

    // Last line since mark that start with this text, or null.
    private static string LastLineStarting(int mark, string start)
    {
        string found = null;
        foreach (var line in LogSince(mark))
        {
            if (line.Text.StartsWith(start, StringComparison.Ordinal))
            {
                found = line.Text;
            }
        }
        return found;
    }

    // Warning / Error / Fatal lines since mark, self test FAIL lines left out (they are test results, not mod trouble).
    private static List<LogLine> Troubles(List<LogLine> lines)
    {
        var result = new List<LogLine>();
        foreach (var line in lines)
        {
            if ((line.Level & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) == 0)
            {
                continue;
            }
            if (line.Text.StartsWith(SelfTest.Prefix, StringComparison.Ordinal))
            {
                continue;
            }
            result.Add(line);
        }
        return result;
    }

    private const string BirthLineStart = "Birth by ";
    private const string ConceivedLineStart = "Conceived: ";
    private const string ServerSettingsLineStart = "Using the server's breeding settings";
    private const string PublishedLineStart = "Published Farming level ";
    private const string WithdrewLine = "Withdrew the published Farming level (mod turned off).";

    // ---------- small helpers ----------

    // Checks of one test: count, keep what failed, one PASS or FAIL at the end.
    private sealed class Checks
    {
        private readonly string _name;
        private readonly List<string> _failures = new List<string>();
        private int _count;

        internal Checks(string name)
        {
            _name = name;
        }

        internal int Failed => _failures.Count;

        internal bool Check(bool ok, string what)
        {
            _count++;
            if (!ok)
            {
                _failures.Add(what);
            }
            return ok;
        }

        internal void Problem(string what)
        {
            _count++;
            _failures.Add(what);
        }

        internal void Note(string detail)
        {
            SelfTest.Note(_name, detail);
        }

        internal void Report(string summary)
        {
            if (_failures.Count == 0)
            {
                SelfTest.Pass(_name, $"{_count} checks OK; {summary}");
                return;
            }
            var shown = _failures.Count > 12 ? _failures.GetRange(0, 12) : _failures;
            SelfTest.Fail(_name, $"{_failures.Count} of {_count} checks failed: {string.Join("; ", shown.ToArray())}");
        }
    }

    // Answer of a nested coroutine.
    private sealed class Box
    {
        internal bool Ok;
        internal string Detail = "";
        internal GameObject Object;
    }

    private static bool Close(float a, float b)
    {
        return Math.Abs(a - b) < 0.01f;
    }

    // Same number text as the birth line ("0.##", dot).
    private static string Pct(float value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    // "a=1;b=two" -> dictionary (server step answers).
    private static Dictionary<string, string> Fields(string detail)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in (detail ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq > 0)
            {
                result[part.Substring(0, eq)] = part.Substring(eq + 1);
            }
        }
        return result;
    }

    private static string Field(Dictionary<string, string> fields, string key)
    {
        return fields.TryGetValue(key, out var value) ? value : "";
    }

    // This mod as the framework see it (state name, status text, Enabled entry). Not registered = "missing".
    private static string MyState()
    {
        var view = FeatureRegistry.Find(ModInfo.Guid);
        return view.HasValue ? view.Value.State : "missing";
    }

    private static string MyStatus()
    {
        var view = FeatureRegistry.Find(ModInfo.Guid);
        return view.HasValue ? view.Value.Status : "";
    }

    private static bool MyActive()
    {
        return MyState() == nameof(ModState.Active);
    }

    private static BepInEx.Configuration.ConfigEntry<bool> MyEnabled()
    {
        var view = FeatureRegistry.Find(ModInfo.Guid);
        return view.HasValue ? view.Value.Enabled : null;
    }

    // Other MC mod active right now? (cross-mod tests need the other mod loaded and on)
    private static bool OtherActive(string guid, out string name)
    {
        var view = FeatureRegistry.Find(guid);
        name = view.HasValue ? view.Value.Name : guid;
        return view.HasValue && view.Value.IsActive;
    }

    // Game methods that carry a patch of this mod right now (Harmony id = GUID; framework patches use other ids).
    private static List<string> OwnPatchedMethods()
    {
        var result = new List<string>();
        foreach (var method in HarmonyLib.Harmony.GetAllPatchedMethods())
        {
            var info = HarmonyLib.Harmony.GetPatchInfo(method);
            if (info == null || !info.Owners.Contains(ModInfo.Guid))
            {
                continue;
            }
            result.Add((method.DeclaringType != null ? method.DeclaringType.Name : "?") + "." + method.Name);
        }
        result.Sort(StringComparer.Ordinal);
        return result;
    }

    // What the mod patch while Active, nothing else (no SetLevel, SetQuality, EggGrow, Growup, Tameable).
    private static readonly string[] ExpectedPatches =
    {
        "Player.OnSpawned", "Procreation.Procreate", "ZNet.OnNewConnection", "ZNet.RPC_PeerInfo", "ZNet.Update",
    };

    private static bool SamePatches(List<string> found)
    {
        if (found.Count != ExpectedPatches.Length)
        {
            return false;
        }
        for (var i = 0; i < found.Count; i++)
        {
            if (found[i] != ExpectedPatches[i])
            {
                return false;
            }
        }
        return true;
    }
}
#endif
