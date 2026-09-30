using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.ShieldsTowerWallMod.Patches;

// Me = hands, bash start and per-tick upkeep (design 2.0, 2.1, 2.2, 2.5, 2.6, 2.7).
//   EquipItem prefix (High: before other mods read the item type): player = heal the tower copy (type two-handed now,
//     vanilla EquipItem then empty both hands on every path); non-player = its copy back to vanilla (decision 25).
//   StartAttack prefix: local player with a tower in the left hand = bash cooldown first (too soon after the last bash
//     start: refuse, __result false, vanilla skipped; the only skip of this patch, decision 30; a press made once the
//     swing is over is kept buffered until the time is up, BashWatch.KeepPress), then heal (bash set as
//     the tower's m_attack, built from the player's unarmed attack) before vanilla look at HavePrimaryAttack. Vanilla
//     then clone and play it. Sibling MC mods on this method (Dual Wielding, Weapon Moveset, Sneak Ambush) tolerate a
//     skipped original; my postfix act only on __result true.
//   StartAttack postfix: started a bash = BashWatch (watchdogs, one-hit rule, cooldown start, swing speed).
//   UpdateBlock postfix (every FixedUpdate per owned humanoid; one reference compare for others): Braced upkeep,
//     watchdogs, after vanilla wrote m_internalBlockingState.
//   Pickup prefix: player never auto-equip a tower (vanilla never auto-equip shields); non-player = copy vanilla.
[HarmonyPatch]
internal static class HumanoidPatches
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.High)]
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
    private static void EquipItem_Prefix(Humanoid __instance, ItemDrop.ItemData item)
    {
        try
        {
            if (item == null || TowerCatalog.SnapshotOf(item) == null)
            {
                return;
            }
            if (__instance.IsPlayer())
            {
                TowerData.Heal(item, __instance);
            }
            else
            {
                TowerData.RevertCopy(item);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Humanoid.EquipItem prefix", e);
        }
    }

    // Held attack button call StartAttack every FixedUpdate: two reference compares when no tower; with a tower in
    // cooldown, two more compares, the kept press and out (no heal, no allocation).
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack), new[] { typeof(Character), typeof(bool) })]
    private static bool StartAttack_Prefix(Humanoid __instance, bool secondaryAttack, ref bool __result)
    {
        if (!ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return true;
        }
        try
        {
            if (TowerCatalog.HeldListed(__instance) == null)
            {
                return true;
            }
            // Bash = primary attack of the tower (right hand empty: two-handed). Too soon = refused like vanilla
            // refuse an attack while the last one play: a press made once the swing is over stay buffered until the
            // time is up (KeepPress), a held button retry next tick.
            if (!secondaryAttack && __instance.m_rightItem == null && BashWatch.InCooldown((Player)__instance)
                && TowerCatalog.Held(__instance) != null)
            {
                BashWatch.KeepPress((Player)__instance);
                __result = false;
                return false;
            }
            TowerData.Heal(__instance.m_leftItem, __instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Humanoid.StartAttack prefix", e);
        }
        return true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack), new[] { typeof(Character), typeof(bool) })]
    private static void StartAttack_Postfix(Humanoid __instance, bool __result)
    {
        if (!__result || !ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            var attack = __instance.m_currentAttack;
            if (attack == null || TowerCatalog.Held(__instance) == null
                || !ReferenceEquals(attack.GetWeapon(), __instance.m_leftItem))
            {
                return;
            }
            BashWatch.OnStarted((Player)__instance, attack);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Humanoid.StartAttack postfix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UpdateBlock))]
    private static void UpdateBlock_Postfix(Humanoid __instance)
    {
        if (!ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            var player = (Player)__instance;
            Brace.Upkeep(player);
            BashWatch.Tick(player);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Humanoid.UpdateBlock postfix", e);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Pickup))]
    private static void Pickup_Prefix(Humanoid __instance, GameObject go, ref bool autoequip)
    {
        try
        {
            if (go == null)
            {
                return;
            }
            var drop = go.GetComponent<ItemDrop>();
            if (drop == null || drop.m_itemData == null)
            {
                return;
            }
            if (__instance.IsPlayer())
            {
                if (autoequip && TowerCatalog.TowerOf(drop.m_itemData) != null)
                {
                    autoequip = false;
                }
            }
            else
            {
                TowerData.RevertCopy(drop.m_itemData);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Humanoid.Pickup prefix", e);
        }
    }
}
