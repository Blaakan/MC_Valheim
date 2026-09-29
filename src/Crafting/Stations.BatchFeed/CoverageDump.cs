using System;
using System.Collections.Generic;
using System.Text;
using MC.Shared;
using UnityEngine;

namespace MC.Crafting.StationsBatchFeedMod;

// Me = tester aid. Once per world load (and when turned on in a loaded world): one Debug line per station part
// found in ZNetScene prefabs: prefab name, component class, capacities, items, and covered / skipped (why).
// This is how prefab facts (which class the Frost Foundry and Frigid Kiln use, slot counts, fire flags) get checked
// in game. Silent at normal log levels. Null-safe: modded prefabs can be odd, each prefab in own try/catch.
// Lists what ZNetScene hold when it run (vanilla + mods registered before); pieces added later still work.
internal static class CoverageDump
{
    private const string Prefix = "Batch feed coverage: ";

    private static ZNetScene _doneFor;

    internal static void RunIfWorldLoaded()
    {
        var scene = ZNetScene.instance;
        if (scene != null)
        {
            Run(scene);
        }
    }

    internal static void Run(ZNetScene scene)
    {
        if (scene == null || ReferenceEquals(scene, _doneFor) || scene.m_prefabs == null)
        {
            return;
        }
        _doneFor = scene;

        var parts = 0;
        foreach (var prefab in scene.m_prefabs)
        {
            if (prefab == null)
            {
                continue;
            }
            try
            {
                parts += DumpPrefab(prefab);
            }
            catch (Exception e)
            {
                Log.Debug($"{Prefix}{prefab.name} could not be listed: {e.GetType().Name}: {e.Message}");
            }
        }
        Log.Debug($"{Prefix}{parts} station part(s) listed from {scene.m_prefabs.Count} prefabs "
                  + "(pieces other mods add later are not listed here but are still covered).");
    }

    private static int DumpPrefab(GameObject prefab)
    {
        var parts = 0;
        foreach (var s in prefab.GetComponentsInChildren<Smelter>(true))
        {
            Log.Debug($"{Prefix}{prefab.name} Smelter{Where(prefab, s)} name={s.m_name}: "
                      + $"input {Status(FeedTarget.SmelterInputSkip(s))} (maxOre={s.m_maxOre}, items={Conversions(s.m_conversion)}); "
                      + $"fuel {Status(FeedTarget.SmelterFuelSkip(s))} (maxFuel={s.m_maxFuel}, fuel={ItemName(s.m_fuelItem)})");
            parts++;
        }
        foreach (var f in prefab.GetComponentsInChildren<Fireplace>(true))
        {
            Log.Debug($"{Prefix}{prefab.name} Fireplace{Where(prefab, f)} name={f.m_name}: {Status(FeedTarget.FireSkip(f))} "
                      + $"(maxFuel={f.m_maxFuel}, fuel={ItemName(f.m_fuelItem)}, canRefill={f.m_canRefill}, "
                      + $"canTurnOff={f.m_canTurnOff}, infiniteFuel={f.m_infiniteFuel})");
            parts++;
        }
        foreach (var c in prefab.GetComponentsInChildren<CookingStation>(true))
        {
            var slots = c.m_slots == null ? 0 : c.m_slots.Length;
            Log.Debug($"{Prefix}{prefab.name} CookingStation{Where(prefab, c)} name={c.m_name}: "
                      + $"food {Status(FeedTarget.CookFoodSkip(c))} via {(c.m_addFoodSwitch != null ? "switch" : "station")} "
                      + $"(slots={slots}, items={Conversions(c.m_conversion)}, requireFire={c.m_requireFire}); "
                      + $"fuel {Status(FeedTarget.CookFuelSkip(c))} (maxFuel={c.m_maxFuel}, fuel={ItemName(c.m_fuelItem)}, useFuel={c.m_useFuel})");
            parts++;
        }
        foreach (var g in prefab.GetComponentsInChildren<ShieldGenerator>(true))
        {
            Log.Debug($"{Prefix}{prefab.name} ShieldGenerator{Where(prefab, g)} name={g.m_name}: fuel {Status(FeedTarget.ShieldFuelSkip(g))} "
                      + $"(maxFuel={g.m_maxFuel}, fuel={ItemNames(g.m_fuelItems)})");
            parts++;
        }
        foreach (var t in prefab.GetComponentsInChildren<Turret>(true))
        {
            Log.Debug($"{Prefix}{prefab.name} Turret{Where(prefab, t)} name={t.m_name}: ammo {Status(FeedTarget.TurretSkip(t))} "
                      + $"(maxAmmo={t.m_maxAmmo}, ammo={AmmoNames(t.m_allowedAmmo)}, defaultAmmo={ItemName(t.m_defaultAmmo)}, "
                      + $"addEffect={(t.m_addAmmoEffect == null ? "none" : "set")})");
            parts++;
        }
        return parts;
    }

    private static string Status(string skip) => skip == null ? "covered" : "skipped (" + skip + ")";

    private static string Where(GameObject prefab, Component c) =>
        c.gameObject == prefab ? "" : " on child '" + c.gameObject.name + "'";

    private static string ItemName(ItemDrop item) => item == null ? "(none)" : item.name;

    private static string ItemNames(List<ItemDrop> items)
    {
        if (items == null || items.Count == 0)
        {
            return "(none)";
        }
        var sb = new StringBuilder();
        foreach (var item in items)
        {
            sb.Append(sb.Length > 0 ? "," : "").Append(ItemName(item));
        }
        return sb.ToString();
    }

    private static string AmmoNames(List<Turret.AmmoType> ammo)
    {
        if (ammo == null || ammo.Count == 0)
        {
            return "(none)";
        }
        var sb = new StringBuilder();
        foreach (var a in ammo)
        {
            sb.Append(sb.Length > 0 ? "," : "").Append(ItemName(a.m_ammo));
        }
        return sb.ToString();
    }

    // No-source conversion (m_from null, e.g. fuel-only machine) print "(none)>Output".
    private static string Conversions(List<Smelter.ItemConversion> list)
    {
        if (list == null || list.Count == 0)
        {
            return "(none)";
        }
        var sb = new StringBuilder();
        foreach (var c in list)
        {
            if (c == null)
            {
                continue;
            }
            sb.Append(sb.Length > 0 ? "," : "").Append(ItemName(c.m_from)).Append('>').Append(ItemName(c.m_to));
        }
        return sb.ToString();
    }

    private static string Conversions(List<CookingStation.ItemConversion> list)
    {
        if (list == null || list.Count == 0)
        {
            return "(none)";
        }
        var sb = new StringBuilder();
        foreach (var c in list)
        {
            if (c == null)
            {
                continue;
            }
            sb.Append(sb.Length > 0 ? "," : "").Append(ItemName(c.m_from)).Append('>').Append(ItemName(c.m_to));
        }
        return sb.ToString();
    }
}
