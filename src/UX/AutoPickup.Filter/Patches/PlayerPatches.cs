using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.UX.AutoPickupFilterMod.Patches;

// Player.AutoPickup = the only place filter act. Prefix open the scope (IsPiece postfix answer "piece" for rejected
// drops = vanilla skip them before RequestOwn), finalizer close it whatever happen. Vanilla loop untouched.
// Player.Interact prefix = remember hand harvest spots (harvest grace), only for Use targets that spawn drops (bush,
// fermenter, cooking station, beehive, sap collector, item/armor stand, archery target, smelter empty switch, saddle
// off). Never change the call.
// Framework only patch these while feature Active. Every body catch own errors: never throw into game.
[HarmonyPatch]
internal static class PlayerPatches
{
    // First: scope open before other mods' prefixes that copy the loop (their copy still call IsPiece).
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(Player), nameof(Player.AutoPickup), new[] { typeof(float) })]
    private static void AutoPickup_Prefix(Player __instance)
    {
        try
        {
            if (!ReferenceEquals(__instance, Player.m_localPlayer))
            {
                return;
            }
            FilterState.EnsureLoaded(__instance);
            if (!AutoPickupGuard.ForeignCheckDone)
            {
                AutoPickupGuard.WarnForeignPatches();
            }
            var on = Player.m_enableAutoPickup;
            // V just turned auto pickup on again: say which filter apply (vanilla "On" message show first).
            if (on && AutoPickupScope.LastEnabled == false && FilterState.Mode != FilterMode.Everything)
            {
                FilterUi.ShowModeMessage();
            }
            AutoPickupScope.LastEnabled = on;
            AutoPickupScope.Active = on && FilterState.Mode != FilterMode.Everything;
        }
        catch (Exception e)
        {
            AutoPickupScope.Active = false;
            PatchGuard.Report(nameof(AutoPickup_Prefix), e);
        }
    }

    // Void finalizer: exception (vanilla or other mod) still rethrown, me only make sure scope never leak.
    [HarmonyFinalizer]
    [HarmonyPatch(typeof(Player), nameof(Player.AutoPickup), new[] { typeof(float) })]
    private static void AutoPickup_Finalizer()
    {
        AutoPickupScope.Active = false;
    }

    // Use key on bush, station, stand... Drops of these come through auto pickup: remember the spot. Only kinds that
    // spawn drops on Use (DropsOnUse); door, chest, bed, portal, crafting station, tame, sign spawn nothing, so no spot
    // there (else fight drops next to them slip through filter). Same early gates as vanilla (hold repeat 0.2 s,
    // attack, dodge).
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Player), nameof(Player.Interact), new[] { typeof(GameObject), typeof(bool), typeof(bool) })]
    private static void Interact_Prefix(Player __instance, GameObject go, bool hold, bool alt)
    {
        if (go == null || (hold && Time.time - __instance.m_lastHoverInteractTime < 0.2f))
        {
            return;
        }
        try
        {
            if (!ReferenceEquals(__instance, Player.m_localPlayer) || __instance.InAttack() || __instance.InDodge())
            {
                return;
            }
            // Me copy vanilla: no Interactable = vanilla do nothing = nothing drop.
            var target = go.GetComponentInParent<Interactable>();
            if (target == null || !DropsOnUse(target, alt))
            {
                return;
            }
            HarvestGrace.Record(go.transform.position);
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(Interact_Prefix), e);
        }
    }

    // Only these spawn drop on Use (design 1.6 table). Item, fish (picked straight into inventory), door, chest, bed,
    // portal, station, tame, sign... spawn nothing: false. Interactables of other mods: false (no harvest grace).
    private static bool DropsOnUse(Interactable target, bool alt)
    {
        switch (target)
        {
            case Pickable _:
            case PickableItem _:
            case Fermenter _:
            case CookingStation _:
            case Beehive _:
            case SapCollector _:
            case ItemStand _:
            case ArcheryTarget _:
                return true;
            case Sadle _:
                // Alt use (Shift + E) take saddle off: saddle drop next to tame. Plain E = ride, nothing drop.
                return alt;
            case Switch sw:
                // Smelter: only empty-output switch drop; add ore / add fuel switch spawn nothing.
                var smelter = sw.GetComponentInParent<Smelter>();
                if (smelter != null)
                {
                    return ReferenceEquals(smelter.m_emptyOreSwitch, sw);
                }
                // Stone oven (and any cooking station with food switch): E on food switch take done food out = drop.
                // CookingStation.Interact say no when station have food switch, so oven only reach here. Fuel switch no.
                var cooking = sw.GetComponentInParent<CookingStation>();
                if (cooking != null)
                {
                    return ReferenceEquals(cooking.m_addFoodSwitch, sw);
                }
                // Armor stand slots are switches (ArmorStand.UseItem -> RPC_DropItemByName -> DropItem); pose switch no.
                var stand = sw.GetComponentInParent<ArmorStand>();
                return stand != null && !ReferenceEquals(stand.m_changePoseSwitch, sw);
            default:
                return false;
        }
    }
}
