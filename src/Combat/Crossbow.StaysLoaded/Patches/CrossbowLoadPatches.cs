using System;
using System.Collections.Generic;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.CrossbowStaysLoadedMod.Patches;

// Vanilla: Player keep ONE ref, m_weaponLoaded. Put crossbow away = ref gone = reload again.
// Me: stamp "loaded" on the item itself (see LoadedState). Stamp travel with item.
//   reload finish     -> stamp     (SetWeaponLoaded postfix)
//   block while held  -> re-stamp  (BlockAttack postfix; block eat durability, and stands/saves copy item without unequip)
//   put away loaded   -> re-stamp  (UnequipItem prefix)
//   crossbow fire     -> unstamp   (OnAttackTrigger postfix)
//   repair            -> re-stamp loaded ones, drop stale ones (durability jump up would revive stale stamp)
//   equip stamped     -> loaded now, no reload, no stamina/eitr cost (UpdateWeaponLoading prefix)
// Framework only patch these while feature Active. Every body catch own errors: never throw into game.
[HarmonyPatch]
internal static class CrossbowLoadPatches
{
    // Reload done (only vanilla non-null caller of SetWeaponLoaded). Me stamp item.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Player), nameof(Player.SetWeaponLoaded))]
    private static void SetWeaponLoaded_Postfix(Player __instance, ItemDrop.ItemData weapon)
    {
        try
        {
            if (weapon != null && ReferenceEquals(__instance, Player.m_localPlayer) && LoadedState.IsEligible(weapon))
            {
                LoadedState.Mark(weapon);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(SetWeaponLoaded_Postfix), e);
        }
    }

    // Every fixed tick for local player, right where vanilla decide to reload. Covers every equip path
    // (hotbar, inventory, drag, swim/sit re-show, login, stands). Stamped + still valid = load now.
    // Cheap checks first: this run 50 times a second.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Player), nameof(Player.UpdateWeaponLoading))]
    private static void UpdateWeaponLoading_Prefix(Player __instance, ItemDrop.ItemData weapon)
    {
        try
        {
            if (weapon == null
                || ReferenceEquals(__instance.m_weaponLoaded, weapon)
                || !ReferenceEquals(__instance, Player.m_localPlayer)
                || !weapon.m_shared.m_attack.m_requiresReload
                || !LoadedState.HasStamp(weapon))
            {
                return;
            }

            // Same gate as vanilla QueueReloadAction: no load while grappling or right after a shot.
            if (__instance.m_grappling > 0f || __instance.m_blockReload > 0f || !LoadedState.IsEligible(weapon))
            {
                return;
            }

            if (!LoadedState.IsLoaded(weapon))
            {
                LoadedState.Clear(weapon); // stale (fired elsewhere) or junk: vanilla reload as usual
                return;
            }

            // Drop any queued reload, then load. SetWeaponLoaded also set ZDO bool, so others see the bolt.
            __instance.CancelReloadAction();
            __instance.SetWeaponLoaded(weapon);
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(UpdateWeaponLoading_Prefix), e);
        }
    }

    // Put away while loaded. Me refresh stamp before vanilla forget the load.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
    private static void UnequipItem_Prefix(Humanoid __instance, ItemDrop.ItemData item)
    {
        try
        {
            if (item != null
                && __instance is Player player
                && ReferenceEquals(player.m_weaponLoaded, item)
                && ReferenceEquals(player, Player.m_localPlayer)
                && LoadedState.IsEligible(item))
            {
                LoadedState.Mark(item);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(UnequipItem_Prefix), e);
        }
    }

    // Block eat durability of the held crossbow. Me refresh stamp now, so item stand / logout (which copy or save
    // the item without unequip) carry fresh stamp.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.BlockAttack))]
    private static void BlockAttack_Postfix(Humanoid __instance)
    {
        try
        {
            if (__instance is Player player && ReferenceEquals(player, Player.m_localPlayer))
            {
                var weapon = player.m_weaponLoaded;
                if (weapon != null && LoadedState.IsEligible(weapon))
                {
                    LoadedState.Mark(weapon);
                }
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(BlockAttack_Postfix), e);
        }
    }

    // Shot go. Vanilla clear m_weaponLoaded at end of OnAttackTrigger. Me clear stamp of the weapon that fired.
    // Early return (no ammo, stagger) = still loaded = stamp stay.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger))]
    private static void OnAttackTrigger_Postfix(Attack __instance)
    {
        try
        {
            if (__instance.m_requiresReload
                && __instance.m_character is Player player
                && ReferenceEquals(player, Player.m_localPlayer)
                && !player.IsWeaponLoaded())
            {
                LoadedState.Clear(__instance.m_weapon);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(OnAttackTrigger_Postfix), e);
        }
    }

    // Repair change durability. Me remember which items were loaded, re-stamp them after.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.RepairOneItem))]
    private static void RepairOneItem_Prefix(out List<ItemDrop.ItemData> __state)
    {
        __state = null;
        try
        {
            var player = Player.m_localPlayer;
            if (player == null)
            {
                return;
            }

            foreach (var item in player.GetInventory().GetAllItems())
            {
                if (!LoadedState.IsEligible(item))
                {
                    continue;
                }
                if (LoadedState.IsLoaded(item) || ReferenceEquals(player.m_weaponLoaded, item))
                {
                    (__state ??= new List<ItemDrop.ItemData>()).Add(item);
                }
                else if (LoadedState.HasStamp(item))
                {
                    // Stale (fired somewhere without us). Repair to max would make it look valid again: drop it now.
                    LoadedState.Clear(item);
                }
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(RepairOneItem_Prefix), e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.RepairOneItem))]
    private static void RepairOneItem_Postfix(List<ItemDrop.ItemData> __state)
    {
        try
        {
            if (__state == null)
            {
                return;
            }
            foreach (var item in __state)
            {
                LoadedState.Mark(item);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(RepairOneItem_Postfix), e);
        }
    }

    // Tooltip say "Loaded", so player know which crossbow hold a bolt.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
        typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool))]
    private static void GetTooltip_Postfix(ItemDrop.ItemData item, bool crafting, bool appending, ref string __result)
    {
        try
        {
            if (crafting || appending || !Plugin.ShowLoadedInTooltip.Value || !LoadedState.IsEligible(item))
            {
                return;
            }

            var player = Player.m_localPlayer;
            var loadedInHand = player != null && ReferenceEquals(player.m_weaponLoaded, item);
            if (loadedInHand || LoadedState.IsLoaded(item))
            {
                __result += "\n<color=orange>Loaded</color>";
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(GetTooltip_Postfix), e);
        }
    }
}
