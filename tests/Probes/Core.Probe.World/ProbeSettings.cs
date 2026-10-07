using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace MC.Core.ProbeWorldMod;

// Me read what tools/Test-InWorld.ps1 put in env of game process (only that process: script remove them after launch).
//   MC_INWORLD_DIR       run folder (saves/ + shots/ go inside). Not set = probe idle.
//   MC_SELFTEST_FILTER   comma list of substrings: run only tests whose name hold one (probe.* always run).
//   MC_SELFTEST_TIMEOUT  seconds per test (default 120).
//   MC_INWORLD_KEEP      "1" = stay in world after DONE, no quit.
// Multiplayer run (tools/Test-Multiplayer.ps1) add:
//   MC_MP_JOIN           "host:port" of dedicated server: me join it instead of starting own world.
//   MC_MP_PASSWORD       its password.
//   MC_MP_SCENARIO       scenario name (SelfTest.Modded, ...): which multiplayer tests run.
internal static class ProbeSettings
{
    internal const string DirVar = "MC_INWORLD_DIR";
    internal const string FilterVar = "MC_SELFTEST_FILTER";
    internal const string TimeoutVar = "MC_SELFTEST_TIMEOUT";
    internal const string KeepVar = "MC_INWORLD_KEEP";
    internal const string JoinVar = "MC_MP_JOIN";
    internal const string PasswordVar = "MC_MP_PASSWORD";
    internal const string ScenarioVar = "MC_MP_SCENARIO";
    internal const string RefusingVar = "MC_MP_REFUSING"; // comma list: GUIDs of mods that refuse a player whose game has them missing OR turned off
    internal const string InstallOnlyVar = "MC_MP_INSTALL_ONLY"; // comma list: GUIDs of mods whose older check only refuses a player without the mod installed

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

    // Multiplayer run: server address, password, scenario. Join null = single-player run.
    internal static string Join { get; private set; }
    internal static string Password { get; private set; } = "";
    internal static string Scenario { get; private set; } = "";
    internal static bool IsMultiplayer => !string.IsNullOrEmpty(Join);
    internal static string[] Refusing { get; private set; } = new string[0];
    internal static string[] InstallOnly { get; private set; } = new string[0];

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

        var join = Environment.GetEnvironmentVariable(JoinVar);
        Join = string.IsNullOrEmpty(join) ? null : join.Trim();
        Password = Environment.GetEnvironmentVariable(PasswordVar) ?? "";
        Scenario = IsMultiplayer ? (Environment.GetEnvironmentVariable(ScenarioVar) ?? "").Trim() : "";
        Refusing = List(RefusingVar);
        InstallOnly = List(InstallOnlyVar);
        return true;
    }

    private static string[] List(string variable) => (Environment.GetEnvironmentVariable(variable) ?? "")
        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();

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
