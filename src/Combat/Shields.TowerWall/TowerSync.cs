using MC.Shared;
using UnityEngine;

namespace MC.Combat.ShieldsTowerWallMod;

// Me = bring tower data in line with the rules in force (design 2.0 "Rules apply", 7.3). Tower data live in item
// SharedData copies, so every change of the rules in force must be pushed into them. ServerRules raise a generation on
// each change; me wait Delay s after the last one (ConfigurationManager fire one change per key typed), compare the
// rules key with the one me applied, then apply on main thread. Me also apply again when the ZNet object changed
// (joined a server, started own world): rules in force switch there without a generation raise. ObjectDB postfix
// force an apply (new item database: new catalog).
// Apply = new catalog; running bash cancelled when its tower leave the list; bash rebuilt (fallback state reset);
// ApplyAll (prefabs + live copies, null rules = vanilla); FixHands on local player; Braced updated.
// Driven by ZNet.Update postfix (HasWork idle = one bool, one int compare, one reference compare), ObjectDB postfixes
// and OnActivated / OnDeactivated (Plugin).
internal static class TowerSync
{
    internal const float Delay = ServerRules.PushDelay;

    private static bool _active;
    private static int _appliedGeneration;
    private static ZNet _appliedNet;
    private static ObjectDB _appliedDb;
    private static string _appliedKey;       // key of rules applied; null = vanilla (nothing applied, or client waiting)
    private static TowerRules _applied;      // rules now written into items; null = vanilla
    private static string _problemsLogged;   // key whose Towers problems me already logged

    internal static bool HasWork =>
        _active && (_appliedGeneration != ServerRules.Generation || !ReferenceEquals(_appliedNet, ZNet.instance));

    // True between Activate and Deactivate (self test tower.toggle put it back on when it turned it off).
    internal static bool Active => _active;

    // Key of the rules now written into items (null = vanilla). Self tests read it.
    internal static string AppliedKey => _appliedKey;

    // Rules now written into items (null = vanilla: mod off, client waiting for the server's rules). Game code read
    // gameplay numbers from here, so numbers and item data always match.
    internal static TowerRules Applied => _active ? _applied : null;

    // OnActivated: apply at once, no delay (items may hold anything: idempotent write from snapshot).
    internal static void Activate()
    {
        _active = true;
        Apply(force: true, null);
    }

    // ObjectDB.Awake / CopyOtherDB postfix (items present): new item database = new catalog, prefabs written.
    internal static void OnObjectDB(ObjectDB db)
    {
        if (_active)
        {
            Apply(force: true, db);
        }
    }

    // OnDeactivated (patches still on). Each step alone (Plugin.Step), design 7.3 order. Quitting = plugins destroyed
    // at quit: containers and ground items not reverted (nothing left to see). Forget what me applied: next Activate
    // write everything again.
    internal static void Deactivate(bool quitting)
    {
        var player = Player.m_localPlayer;
        Plugin.Step("TowerSync.Deactivate bash", () => BashWatch.CancelAll(player)); // swing speed back too
        Plugin.Step("TowerSync.Deactivate braced", () => Brace.Remove(player));
        _active = false;
        _applied = null;
        Plugin.Step("TowerSync.Deactivate inventory", () => TowerData.RevertInventory(player));
        Plugin.Step("TowerSync.Deactivate prefabs", TowerData.RevertPrefabs);
        if (!quitting)
        {
            Plugin.Step("TowerSync.Deactivate world", TowerData.RevertWorld);
        }
        Plugin.Step("TowerSync.Deactivate state", () =>
        {
            TowerCatalog.Clear();
            TowerData.NewStamp();
            BashAttack.Reset();
            BashWatch.Reset();
            BashStagger.Reset();
            Brace.Reset();
        });
        _appliedKey = null;
        _appliedNet = null;
        _appliedDb = null;
        _problemsLogged = null;
    }

    // ZNet.Update postfix, only when HasWork.
    internal static void Update()
    {
        if (Time.unscaledTime - ServerRules.ChangedAt < Delay)
        {
            return;
        }
        Apply(force: false, null);
    }

    private static void Apply(bool force, ObjectDB dbOverride)
    {
        _appliedGeneration = ServerRules.Generation;
        _appliedNet = ZNet.instance;
        var rules = TowerRules.InForce;
        var key = rules != null ? rules.Key : null;
        var db = dbOverride != null ? dbOverride : ObjectDB.instance;
        if (!force && key == _appliedKey && ReferenceEquals(db, _appliedDb))
        {
            return;
        }
        _appliedKey = key;
        _appliedDb = db;
        _applied = rules;
        LogListProblems(rules);
        var player = Player.m_localPlayer;
        Plugin.Step("TowerSync.Apply catalog", () => TowerCatalog.Rebuild(db, rules));
        TowerData.NewStamp();
        Plugin.Step("TowerSync.Apply cancel bash", () => BashWatch.CancelDropped(player));
        Plugin.Step("TowerSync.Apply bash", () =>
        {
            BashAttack.Reset();
            if (player != null)
            {
                BashAttack.Get(player);
            }
        });
        Plugin.Step("TowerSync.Apply items", () => TowerData.ApplyAll(rules));
        Plugin.Step("TowerSync.Apply hands", () => TowerData.FixHands(player));
        Plugin.Step("TowerSync.Apply braced", () => Brace.Upkeep(player));
        if (rules == null)
        {
            Log.Debug("Waiting for the server's tower shield rules; tower shields stay normal until they arrive.");
        }
        else
        {
            Log.Debug($"Tower shield rules applied ({TowerCatalog.Count} tower shields): {rules.Describe()}.");
        }
    }

    // Towers text problems (bad number, duplicate...), once per rules key.
    private static void LogListProblems(TowerRules rules)
    {
        if (rules == null || rules.ListProblems.Count == 0 || rules.Key == _problemsLogged)
        {
            return;
        }
        _problemsLogged = rules.Key;
        var owner = ServerRules.UsingServer ? "The server's Towers setting" : "The Towers setting";
        Log.Warning($"{owner} has problems: {string.Join("; ", rules.ListProblems)}.");
    }
}
