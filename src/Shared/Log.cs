using BepInEx.Logging;

namespace MC.Shared;

// Me hold plugin logger so static Harmony patch can talk too.
// Plugin call Log.Init(Logger) first thing in Awake.
internal static class Log
{
    // tools/Test-Smoke.ps1 look for this in LogOutput.log. Me no change it.
    public const string ReadyMarker = "[MC:ready]";

    private static ManualLogSource _source;

    public static void Init(ManualLogSource source) => _source = source;

    // Plugin call this at end of Awake, after patch done. Smoke test know mod alive.
    public static void Ready(string guid, string version) => Info($"{ReadyMarker} {guid} {version}");

    public static void Debug(object message) => _source?.LogDebug(message);
    public static void Info(object message) => _source?.LogInfo(message);
    public static void Warning(object message) => _source?.LogWarning(message);
    public static void Error(object message) => _source?.LogError(message);
}
