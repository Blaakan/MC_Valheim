#if DEBUG
using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.CrossbowStaysLoadedMod.Patches;

// Debug build only. First spawn: me list every reload weapon with timings, so tester see which items
// the mod touch, and if swapping preloaded crossbows beat a real reload (equip time vs reload time).
[HarmonyPatch]
internal static class DebugReloadDump
{
    private static bool _done;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    private static void OnSpawned_Postfix(Player __instance)
    {
        try
        {
            if (_done || !ReferenceEquals(__instance, Player.m_localPlayer) || ObjectDB.instance == null)
            {
                return;
            }
            _done = true;

            foreach (var prefab in ObjectDB.instance.m_items)
            {
                if (prefab == null)
                {
                    continue;
                }
                var drop = prefab.GetComponent<ItemDrop>();
                if (drop == null || drop.m_itemData?.m_shared?.m_attack == null)
                {
                    continue;
                }

                var shared = drop.m_itemData.m_shared;
                var a = shared.m_attack;
                if (!a.m_requiresReload)
                {
                    continue;
                }

                Log.Info($"[reload weapon] {prefab.name}: equip {shared.m_equipDuration}s, reload {a.m_reloadTime}s "
                         + $"(half at skill 100), reload stamina {a.m_reloadStaminaDrain}, reload eitr {a.m_reloadEitrDrain}, "
                         + $"durability/shot {shared.m_useDurabilityDrain}, skill {shared.m_skillType}, "
                         + $"consumes itself {a.m_consumeItem}, keeps load (config) {LoadedState.IsEligible(prefab.name, shared)}");
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(DebugReloadDump), e);
        }
    }
}
#endif
