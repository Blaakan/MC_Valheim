using System;
using System.IO;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;

namespace MC.Shared;

// Me = base of every MC mod plugin. Me own the feature life:
//   Enabled (config) + dependencies ok + network ok  ->  patches on (Active)
//   anything wrong                                  ->  patches off, Status say why, nothing crash
// Toggle is live: flip Enabled in config file, ConfigurationManager, or MC Mods panel. No restart.
// Mod code: override BindConfig / OnActivated / OnDeactivated. No own Awake/Start/Update/OnDestroy (they hide mine).
internal abstract class ModPlugin : BaseUnityPlugin
{
    private ConfigEntry<string> _statusEntry;
    private bool _patched;
    private bool _destroyed;       // OnDestroy ran (game quitting): never activate again
    private bool _failed;          // activation blew up; user toggle clear it (try again)
    private string _failedText;
    private string _initError;     // Awake blew up; never cleared, feature stay off whole session
    private FileSystemWatcher _watcher;
    private volatile bool _configFileChanged;

    // Build generate this from csproj (see ModInfo.g.cs).
    protected abstract ModDescriptor Descriptor { get; }

    internal ModDescriptor Mod => Descriptor;
    public ConfigEntry<bool> Enabled { get; private set; }
    public ModState State { get; private set; } = ModState.Starting;
    public string StatusText { get; private set; } = "Starting";
    public bool IsActive => State == ModState.Active;
    protected Harmony Harmony { get; private set; }

    // Folder of this mod's dll. Me use it for mod files (translations, asset bundles): install place differ
    // (plugins/MC_Valheim/<Category>/<Guid>/ direct/Nexus, plugins/MC-<Package>/ mod managers). Never hardcode.
    protected string ModFolder => Path.GetDirectoryName(Info.Location);

    // Mod bind own settings here (after General.Enabled).
    protected virtual void BindConfig()
    {
    }

    // Feature just went live (patches on). Set up UI/objects here.
    protected virtual void OnActivated()
    {
    }

    // Feature going off (patches still on during this call). Undo what OnActivated made.
    protected virtual void OnDeactivated()
    {
    }

    // Default: every [HarmonyPatch] class in mod dll. Override for custom patching.
    protected virtual void ApplyPatches(Harmony harmony) => harmony.PatchAll(GetType().Assembly);

    protected void Awake()
    {
        Log.Init(Logger);
        var d = Descriptor;

        Enabled = Config.Bind("General", "Enabled", true, new ConfigDescription(
            "Turn this feature on or off. Takes effect immediately, no restart needed.",
            null, new ConfigurationManagerAttributes { Order = 100 }));
        _statusEntry = Config.Bind("General", "Status", "Starting", new ConfigDescription(
            "Written by the mod: shows whether the feature is active, and if not, why. Editing it has no effect.",
            null, new ConfigurationManagerAttributes { Order = 99, ReadOnly = true }));
        Harmony = new Harmony(d.Guid);

        // Register FIRST: even if rest of start blow up, mod still show in panel with Error,
        // and dependents see "failing to start" instead of thinking me fine.
        FeatureRegistry.Register(this);
        Enabled.SettingChanged += (_, _) =>
        {
            _failed = false; // user toggle = try again after activation error
            FeatureRegistry.RefreshAll();
        };

        try
        {
            BindConfig();
            NetworkGate.Install(d, () => IsActive);
            WatchConfigFile();
        }
        catch (Exception e)
        {
            Log.Error($"Could not start (the game may have changed). Feature stays off this session. {e}");
            _initError = "Error: could not start (the game may have changed). See BepInEx/LogOutput.log.";
        }

        FeatureRegistry.RefreshAll();

        // Debug build only: me JIT every method now so broken game refs show in smoke test.
        JitCheck.Run(GetType().Assembly, d.Requires);
        Log.Ready(d.Guid, d.Version, d.Build);
    }

    protected void Start()
    {
        // All plugins did Awake now. Re-check (late dependencies), then leader add the panel.
        FeatureRegistry.RefreshAll();
        if (FeatureRegistry.IsLeader(Descriptor.Guid))
        {
            gameObject.AddComponent<FeaturePanel>();
        }
    }

    protected void Update()
    {
        if (!_configFileChanged)
        {
            return;
        }

        // Also fire after our own Status save. Harmless: same values = no SettingChanged = nothing happen.
        // No "ignore own write" timer: it would eat a real user edit made right after a status change.
        // _failed NOT reset here, else error state retry every save = loop. Only Enabled toggle reset it.
        _configFileChanged = false;
        try
        {
            Config.Reload();
            Log.Debug("Config file reloaded from disk.");
        }
        catch (Exception e)
        {
            Log.Warning($"Could not reload config: {e.Message}");
        }
        // Reload may have put old Status line back from file snapshot; Refresh re-assert it.
        FeatureRegistry.RefreshAll();
    }

    protected void OnDestroy()
    {
        // Game quit destroy plugins before ZNet.OnDestroy; NetworkGate then call RefreshAll. Dead plugin must never
        // start again (patch + OnActivated on destroyed object = error at every quit).
        _destroyed = true;
        _watcher?.Dispose();
        if (_patched)
        {
            try
            {
                OnDeactivated();
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }
        Harmony?.UnpatchSelf();
        _patched = false;
    }

    // Registry call me. Return true when patched-ness changed (so dependents re-check).
    internal bool Refresh()
    {
        if (_destroyed)
        {
            return false;
        }
        var (state, text) = ComputeState();
        var wasPatched = _patched;
        var wantPatched = state == ModState.Active;

        if (wantPatched && !_patched)
        {
            try
            {
                ApplyPatches(Harmony);
                _patched = true;
                OnActivated();
                Log.Info("Activated.");
            }
            catch (Exception e)
            {
                // Half-started feature is worse than none. Me undo what started, remove patches, stay off until user toggle.
                Log.Error($"Could not activate (the game may have changed). Feature turned off. {e}");
                if (_patched)
                {
                    try
                    {
                        OnDeactivated();
                    }
                    catch (Exception e2)
                    {
                        Log.Error(e2);
                    }
                }
                SafeUnpatch();
                _failed = true;
                _failedText = "Error: could not start (the game may have changed). See BepInEx/LogOutput.log.";
                state = ModState.Error;
                text = _failedText;
            }
        }
        else if (!wantPatched && _patched)
        {
            // Stop never fail the state: patches go away no matter what, mod end up in wanted state (e.g. Off).
            try
            {
                OnDeactivated();
            }
            catch (Exception e)
            {
                Log.Error($"OnDeactivated failed; patches removed anyway. {e}");
            }
            SafeUnpatch();
            Log.Info($"Deactivated: {text}");
        }

        if (state != State || text != StatusText)
        {
            State = state;
            StatusText = text;
        }

        // Always re-assert: config Reload can put stale Status back from the file snapshot,
        // and user may hand-edit it. Same value = no save.
        WriteStatus();

        if (wasPatched != _patched)
        {
            NetworkGate.OnActiveChanged();
        }
        return wasPatched != _patched;
    }

    private void SafeUnpatch()
    {
        try
        {
            Harmony.UnpatchSelf();
        }
        catch (Exception e)
        {
            Log.Error(e);
        }
        _patched = false;
    }

    private (ModState, string) ComputeState()
    {
        var d = Descriptor;
        if (_initError != null)
        {
            return (ModState.Error, _initError);
        }

        if (_failed)
        {
            return (ModState.Error, _failedText);
        }

        if (!Enabled.Value)
        {
            return (ModState.Disabled, "Off (disabled in settings).");
        }

        foreach (var guid in d.Requires)
        {
            var dep = FeatureRegistry.Find(guid);
            if (!Chainloader.PluginInfos.TryGetValue(guid, out var info) || info.Instance == null)
            {
                var name = dep?.Name ?? guid;
                return (ModState.MissingDependency, $"Inactive: needs {name}, which is not installed (or failed to load).");
            }

            if (dep == null)
            {
                // Loaded but never registered = its Awake blew up before registry. Not fine.
                return (ModState.DependencyInactive, $"Inactive: needs {info.Metadata.Name}, which failed to start.");
            }

            if (!dep.Value.IsActive)
            {
                return (ModState.DependencyInactive, $"Inactive: needs {dep.Value.Name}, which is {dep.Value.ShortState}.");
            }
        }

        var net = NetworkGate.Check(d);
        if (net != null)
        {
            return net.Value;
        }

        return (ModState.Active, "Active.");
    }

    private void WriteStatus()
    {
        // Value setter do nothing when equal, so no useless save. Save wake watcher; reload then no-op.
        if (_statusEntry != null && _statusEntry.Value != StatusText)
        {
            _statusEntry.Value = StatusText;
        }
    }

    private void WatchConfigFile()
    {
        try
        {
            var path = Config.ConfigFilePath;
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(path), Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                IncludeSubdirectories = false,
            };
            // Watcher fire on other thread. Me only set flag; Update do real work on main thread.
            FileSystemEventHandler flag = (_, _) => _configFileChanged = true;
            _watcher.Changed += flag;
            _watcher.Created += flag;
            _watcher.Renamed += (_, _) => _configFileChanged = true;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception e)
        {
            Log.Warning($"Config file watching unavailable ({e.Message}); edits made while the game runs apply after restart.");
        }
    }
}
