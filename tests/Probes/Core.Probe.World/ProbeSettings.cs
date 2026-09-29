using System;
using System.Globalization;
using System.IO;

namespace MC.Core.ProbeWorldMod;

// Me read what tools/Test-InWorld.ps1 put in env of game process (only that process: script remove them after launch).
//   MC_INWORLD_DIR       run folder (saves/ + shots/ go inside). Not set = probe idle.
//   MC_SELFTEST_FILTER   comma list of substrings: run only tests whose name hold one (probe.* always run).
//   MC_SELFTEST_TIMEOUT  seconds per test (default 120).
//   MC_INWORLD_KEEP      "1" = stay in world after DONE, no quit.
internal static class ProbeSettings
{
    internal const string DirVar = "MC_INWORLD_DIR";
    internal const string FilterVar = "MC_SELFTEST_FILTER";
    internal const string TimeoutVar = "MC_SELFTEST_TIMEOUT";
    internal const string KeepVar = "MC_INWORLD_KEEP";

    // Throwaway character + world. Fixed seed: same start place every run.
    internal const string CharacterName = "MCProbe";
    internal const string WorldName = "MCProbe";
    internal const string WorldSeed = "MCProbe01";

    internal static string RunDir { get; private set; }
    internal static string SaveDir { get; private set; }
    internal static string ShotDir { get; private set; }
    internal static string[] Filters { get; private set; } = new string[0];
    internal static float TestTimeout { get; private set; } = 120f;
    internal static bool Keep { get; private set; }

    // False = env var missing: probe stay idle.
    internal static bool Load()
    {
        var dir = Environment.GetEnvironmentVariable(DirVar);
        if (string.IsNullOrEmpty(dir))
        {
            return false;
        }
        RunDir = Path.GetFullPath(dir.Trim());
        SaveDir = Path.Combine(RunDir, "saves");
        ShotDir = Path.Combine(RunDir, "shots");

        var filter = Environment.GetEnvironmentVariable(FilterVar);
        Filters = string.IsNullOrEmpty(filter)
            ? new string[0]
            : filter.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < Filters.Length; i++)
        {
            Filters[i] = Filters[i].Trim();
        }

        var timeout = Environment.GetEnvironmentVariable(TimeoutVar);
        TestTimeout = float.TryParse(timeout, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) && t > 0f ? t : 120f;
        Keep = Environment.GetEnvironmentVariable(KeepVar) == "1";
        return true;
    }

    // Me run test when no filter, or name hold one filter piece (case ignored).
    internal static bool Wanted(string testName)
    {
        if (Filters.Length == 0)
        {
            return true;
        }
        foreach (var f in Filters)
        {
            if (f.Length > 0 && testName.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }
        return false;
    }
}
