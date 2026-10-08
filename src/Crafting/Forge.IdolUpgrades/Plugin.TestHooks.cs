#if DEBUG
using System.Collections;
using BepInEx.Bootstrap;
using MC.Shared;
using UnityEngine;

namespace MC.Crafting.ForgeIdolUpgradesMod;

// Debug build only (file empty in Release). Me = self test hooks of the plugin:
//   TestSetOff  play "player turn mod off / on again" through real framework path (OnDeactivated + patches removed,
//               then patches back + OnActivated). Me never touch Enabled setting: framework ask LocalBlocker.
//               Status line change in memory only (config file save paused while off; back on = same text as file).
//   TestRun     run coroutine on plugin object (test watchdog that must outlive a test).
internal sealed partial class Plugin
{
    private const string TestOffText = "Inactive: turned off by a self test (Debug build).";

    private static bool _testOff;
    private static bool _saveWas = true;

    internal static bool TestIsOff => _testOff;

    private static Plugin Self =>
        Chainloader.PluginInfos.TryGetValue(ModInfo.Guid, out var info) ? info.Instance as Plugin : null;

    internal static bool TestActive
    {
        get
        {
            var self = Self;
            return self != null && self.IsActive;
        }
    }

    internal static string TestStatus
    {
        get
        {
            var self = Self;
            return self != null ? $"{self.State}: {self.StatusText}" : "plugin not found";
        }
    }

    // True = mod now in asked state. Never while client of a server: server would refuse this player.
    internal static bool TestSetOff(bool off)
    {
        var self = Self;
        if (self == null)
        {
            return false;
        }
        if (_testOff != off)
        {
            var net = ZNet.instance;
            if (off && net != null && !net.IsServer())
            {
                return false;
            }
            if (off)
            {
                _saveWas = self.Config.SaveOnConfigSet;
                self.Config.SaveOnConfigSet = false;
            }
            _testOff = off;
            try
            {
                FeatureRegistry.RefreshAll();
            }
            finally
            {
                if (!off)
                {
                    self.Config.SaveOnConfigSet = _saveWas;
                }
            }
        }
        return self.IsActive != off;
    }

    internal static Coroutine TestRun(IEnumerator routine)
    {
        var self = Self;
        return self != null && routine != null ? self.StartCoroutine(routine) : null;
    }

    protected override string LocalBlocker() => _testOff ? TestOffText : null;
}
#endif
