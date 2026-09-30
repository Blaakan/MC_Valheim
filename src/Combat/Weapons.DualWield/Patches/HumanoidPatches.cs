using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.WeaponsDualWieldMod.Patches;

// Me = hand rules of the local player (design 2.2, 2.4, 2.5). Humanoid methods also run for every creature: first
// check is always "local player?" (one reference compare). Framework patch me only while feature Active; no stand-down
// check (other dual wield mod = framework remove my patches).
//   EquipItem          prefix (Low): end of a queued swap (hands swap); rows K, 1, 2, 3, T (decide pure, then apply
//                      with re-sync finally)
//                      postfix: marked item landed right through vanilla = marker off, restore candidate
//   HideHandItems      prefix + postfix: any hide during eat (full hide, or right-hand hide of a second eat) keep the
//                      eat-hidden main weapon
//   ShowHandItems      prefix: eat return wait while attack / dodge / swim; hidden off hand too = draw both
//   SetupAnimationState postfix: pair stance (template's DualAxes / Knives)
//   StartAttack        prefix (First) + finalizer: "special asked", lone off hand settled, refuse in eat window and
//                      while a swap is queued
[HarmonyPatch(typeof(Humanoid))]
internal static class HumanoidPatches
{
    // Low: other mods' prefixes first. One of them skipped vanilla (__runOriginal false) = me do nothing.
    // __state = right hand was empty before (postfix: restore candidate only then).
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Low)]
    [HarmonyPatch(nameof(Humanoid.EquipItem))]
    private static bool EquipItem_Prefix(Humanoid __instance, ItemDrop.ItemData item, bool triggerEquipEffects,
        ref bool __result, bool __runOriginal, out bool __state)
    {
        __state = false;
        if (!__runOriginal || item == null || !ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return __runOriginal;
        }
        var player = (Player)__instance;
        __state = player.m_rightItem == null;

        // Queued swap end (vanilla's queue call EquipItem for the off-hand weapon, already equipped): hands swap here,
        // never vanilla (it would refuse, or put the item in the main hand if the pair changed).
        if (Hands.IsQueuedSwap(item))
        {
            try
            {
                __result = Hands.CompleteSwap(player, triggerEquipEffects);
            }
            catch (Exception e)
            {
                PatchGuard.Report("Humanoid.EquipItem prefix (swap)", e);
                __result = false;
            }
            return false;
        }

        // Step 1: decide, no change. Failure here = vanilla run on untouched hands.
        EquipRow row;
        try
        {
            row = Hands.Decide(player, item);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Humanoid.EquipItem prefix (decide)", e);
            return true;
        }

        switch (row)
        {
            case EquipRow.MainHandKey:
                try
                {
                    Hands.ConsumeIntent(item);
                }
                catch (Exception e)
                {
                    PatchGuard.Report("Humanoid.EquipItem prefix (main-hand key)", e);
                }
                return true;
            case EquipRow.Torch:
                try
                {
                    Hands.ReleaseForTorch(player, triggerEquipEffects);
                }
                catch (Exception e)
                {
                    PatchGuard.Report("Humanoid.EquipItem prefix (torch)", e);
                }
                return true;
            case EquipRow.Restore:
            case EquipRow.OffHand:
            case EquipRow.MainKeepOff:
                // Step 2: apply. Failure = hands re-synced inside Apply; vanilla never run on half-changed hands.
                try
                {
                    Hands.Apply(player, item, row, triggerEquipEffects);
                    __result = true;
                }
                catch (Exception e)
                {
                    PatchGuard.Report("Humanoid.EquipItem prefix (apply)", e);
                    try
                    {
                        __result = player.IsItemEquiped(item);
                    }
                    catch (Exception)
                    {
                        __result = false;
                    }
                }
                return false;
            default:
                return true;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Humanoid.EquipItem))]
    private static void EquipItem_Postfix(Humanoid __instance, ItemDrop.ItemData item, bool __result, bool __state)
    {
        try
        {
            if (__result && item != null && ReferenceEquals(__instance, Player.m_localPlayer))
            {
                Hands.OnEquipped((Player)__instance, item, __state);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Humanoid.EquipItem postfix", e);
        }
    }

    // Every frame while swimming off the ground (UpdateEquipment call it): one reference compare, then three slot
    // checks. Both kinds of hide: full hide (dive, station) and right-hand hide of every eat (SetUseHandVisual). Second
    // eat inside eat window = right-hand hide while main weapon already hidden: vanilla would write the empty right
    // hand over it (left hand not empty, so no early return) and the pair break.
    [HarmonyPrefix]
    [HarmonyPatch(nameof(Humanoid.HideHandItems))]
    private static void HideHandItems_Prefix(Humanoid __instance, out ItemDrop.ItemData __state)
    {
        __state = null;
        try
        {
            if (ReferenceEquals(__instance, Player.m_localPlayer))
            {
                __state = Hands.EatHiddenToKeep((Player)__instance);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Humanoid.HideHandItems prefix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Humanoid.HideHandItems))]
    private static void HideHandItems_Postfix(Humanoid __instance, ItemDrop.ItemData __state)
    {
        if (__state == null)
        {
            return;
        }
        try
        {
            Hands.KeepEatHidden((Player)__instance, __state);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Humanoid.HideHandItems postfix", e);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(Humanoid.ShowHandItems))]
    private static bool ShowHandItems_Prefix(Humanoid __instance, ref bool onlyRightHand)
    {
        if (!onlyRightHand || !ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return true;
        }
        try
        {
            return Hands.BeforeEatReturn((Player)__instance, ref onlyRightHand);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Humanoid.ShowHandItems prefix", e);
            return true;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Humanoid.SetupAnimationState))]
    private static void SetupAnimationState_Postfix(Humanoid __instance)
    {
        if (!ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            Hands.ApplyStance((Player)__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Humanoid.SetupAnimationState postfix", e);
        }
    }

    // First: before Weapon Moveset, Tower Shield Wall and Sneak Ambush prefixes. May skip vanilla (eat window); they
    // tolerate it. Their skip of vanilla: finalizer still clear my flags.
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(nameof(Humanoid.StartAttack))]
    private static bool StartAttack_Prefix(Humanoid __instance, bool secondaryAttack, ref bool __result)
    {
        if (!ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return true;
        }
        try
        {
            if (Hands.BeginAttack((Player)__instance, secondaryAttack))
            {
                return true;
            }
            __result = false;
            return false;
        }
        catch (Exception e)
        {
            PatchGuard.Report("Humanoid.StartAttack prefix", e);
            return true;
        }
    }

    // Every attack of every character: two bool writes.
    [HarmonyFinalizer]
    [HarmonyPatch(nameof(Humanoid.StartAttack))]
    private static void StartAttack_Finalizer()
    {
        try
        {
            Hands.EndAttack();
        }
        catch (Exception e)
        {
            PatchGuard.Report("Humanoid.StartAttack finalizer", e);
        }
    }
}
