using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Building.LightsSwitchableMod;

// Me set one instance flag on each light in world: m_canRefill = false. Vanilla then refuse fuel from hotbar item,
// and MC Batch Station Feeding skip it (no Shift+E x5, no batch hint). Game copy flags from prefab when object
// spawn, so new lights get it in Fireplace.Awake postfix; lights already there get it here. Me remember the value me
// replaced and put back only that (never touch a fire me did not change: other mods may set it too).
internal static class LiveLights
{
    private static readonly Dictionary<Fireplace, bool> Changed = new Dictionary<Fireplace, bool>();
    private static int _pruneAt = 512;

    internal static void Apply(Fireplace fire)
    {
        if (!LightRules.IsManaged(fire))
        {
            return;
        }
        if (!Changed.ContainsKey(fire))
        {
            // Lights come and go with zones all session long: drop dead ones when the map has doubled.
            if (Changed.Count >= _pruneAt)
            {
                Prune();
                _pruneAt = Math.Max(512, Changed.Count * 2);
            }
            Changed[fire] = fire.m_canRefill;
        }
        fire.m_canRefill = false;
    }

    internal static void ApplyAll()
    {
        Prune();
        foreach (var fire in Object.FindObjectsByType<Fireplace>(FindObjectsSortMode.None))
        {
            try
            {
                if (LightRules.IsManaged(fire))
                {
                    Apply(fire);
                }
                else
                {
                    // Removed from the list while on: its own value again.
                    Restore(fire);
                }
            }
            catch (Exception e)
            {
                PatchGuard.Report("LiveLights.ApplyAll", e);
            }
        }
    }

    internal static void RestoreAll()
    {
        foreach (var pair in Changed)
        {
            try
            {
                if (pair.Key != null)
                {
                    pair.Key.m_canRefill = pair.Value;
                }
            }
            catch (Exception e)
            {
                PatchGuard.Report("LiveLights.RestoreAll", e);
            }
        }
        Changed.Clear();
    }

    private static void Restore(Fireplace fire)
    {
        if (Changed.TryGetValue(fire, out var original))
        {
            fire.m_canRefill = original;
            Changed.Remove(fire);
        }
    }

    // Destroyed lights (unloaded zone, removed piece) leave dead keys: drop them now and then.
    private static void Prune()
    {
        List<Fireplace> dead = null;
        foreach (var fire in Changed.Keys)
        {
            if (fire == null)
            {
                (dead ??= new List<Fireplace>()).Add(fire);
            }
        }
        if (dead != null)
        {
            foreach (var fire in dead)
            {
                Changed.Remove(fire);
            }
        }
    }
}
