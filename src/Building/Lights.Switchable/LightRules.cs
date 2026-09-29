using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Building.LightsSwitchableMod;

// Me know which fires are "lights". List of prefab names come from the rules in force (server's list for everybody,
// else own config). Me keep their stable hash (same number the ZDO keep as prefab id), so check per fire = one ZDO
// read + one set lookup, no string, no allocation. Fire that can cook (has a Burning effect area: campfire, hearth,
// bonfire, braziers) is never a light, even when listed: user rule, fires used for cooking or crafting keep fuel.
internal static class LightRules
{
    private static HashSet<int> _hashes = new HashSet<int>();
    private static LightsRules _rulesSeen;
    private static ZNetScene _sceneSeen;

    // Light of this mod? Fire with no ZDO (build ghost, dead object) = never. Fire another mod made burn without fuel
    // (m_infiniteFuel on the instance) = never: fuel 0 cannot put it out, and the on/off state would leave it dark for
    // good once that mod or this one is gone. That mod keep it.
    internal static bool IsManaged(Fireplace fire)
    {
        if (fire == null || fire.m_infiniteFuel)
        {
            return false;
        }
        var nview = fire.m_nview;
        if (nview == null)
        {
            return false;
        }
        var zdo = nview.GetZDO();
        return zdo != null && Hashes().Contains(zdo.GetPrefab());
    }

    // Prefab of a live fire (for vanilla flag values). Null when scene or prefab gone.
    internal static Fireplace PrefabOf(Fireplace fire)
    {
        var scene = ZNetScene.instance;
        var zdo = fire != null && fire.m_nview != null ? fire.m_nview.GetZDO() : null;
        if (scene == null || zdo == null)
        {
            return null;
        }
        var prefab = scene.GetPrefab(zdo.GetPrefab());
        return prefab != null ? prefab.GetComponent<Fireplace>() : null;
    }

    // Can this piece cook or light a cauldron? Any Burning effect area on it (CookingStation.IsFireLit and
    // CraftingStation.CheckFire look for those).
    internal static bool CanCook(GameObject prefab)
    {
        foreach (var area in prefab.GetComponentsInChildren<EffectArea>(true))
        {
            if ((area.m_type & EffectArea.Type.Burning) != 0)
            {
                return true;
            }
        }
        return false;
    }

    // World scene change or rules change = build set again (and say in log what me skip). Else cached set.
    private static HashSet<int> Hashes()
    {
        var rules = ServerRules.Current;
        var scene = ZNetScene.instance;
        if (ReferenceEquals(rules, _rulesSeen) && ReferenceEquals(scene, _sceneSeen))
        {
            return _hashes;
        }
        _rulesSeen = rules;
        _sceneSeen = scene;
        try
        {
            _hashes = Resolve(rules.Lights, scene);
        }
        catch (Exception e)
        {
            PatchGuard.Report("LightRules.Resolve", e);
            _hashes = new HashSet<int>();
        }
        return _hashes;
    }

    private static HashSet<int> Resolve(string text, ZNetScene scene)
    {
        var hashes = new HashSet<int>();
        if (scene == null || string.IsNullOrEmpty(text))
        {
            return hashes;
        }
        foreach (var part in text.Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var name = part.Trim();
            if (name.Length == 0)
            {
                continue;
            }
            var prefab = scene.GetPrefab(name);
            if (prefab == null)
            {
                Log.Info($"Light '{name}' from the Lights setting is not in this game (typo, or its mod is not installed): ignored.");
            }
            else if (prefab.GetComponent<Fireplace>() == null)
            {
                Log.Warning($"Light '{name}' from the Lights setting is not a fire the game can light: ignored.");
            }
            else if (CanCook(prefab))
            {
                Log.Info($"'{name}' from the Lights setting can be used for cooking: it stays a normal fire that burns fuel.");
            }
            else
            {
                hashes.Add(name.GetStableHashCode());
            }
        }
        return hashes;
    }

    // World load: build now, so the log lines come at load (and a Debug count).
    internal static void ReportNow()
    {
        _sceneSeen = null;
        Log.Debug($"Switchable lights: {Hashes().Count} piece(s) from the list.");
    }
}
