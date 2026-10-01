using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.TrinketsOnDemandMod.Patches;

// Me = the local player's bar: hold a full bar (AddAdrenaline), no drain + income (UpdateStats(float)), trigger and
// feedback (Update), combat signals (RPC_OnTargeted, RPC_HitWhileDodging). Every patch: local player first
// (ReferenceEquals, one compare), other Player objects keep vanilla. Game code read rules through ServerRules.Current,
// pending (client waiting for server rules) = vanilla.
[HarmonyPatch(typeof(Player))]
internal static class PlayerPatches
{
    // First: hide the effects before other mods' prefixes read them (they must not pop either).
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(nameof(Player.AddAdrenaline))]
    private static void AddAdrenaline_Prefix(Player __instance, float v, out bool __state)
    {
        __state = false;
        if (!ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            __state = FullBar.Enter(__instance, v);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.AddAdrenaline prefix", e);
        }
    }

    // Void finalizer: an exception of vanilla (or another patch) still go up as without me; effects put back anyway.
    [HarmonyFinalizer]
    [HarmonyPatch(nameof(Player.AddAdrenaline))]
    private static void AddAdrenaline_Finalizer(bool __state)
    {
        if (!__state)
        {
            return;
        }
        try
        {
            FullBar.Exit();
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.AddAdrenaline finalizer", e);
        }
    }

    // Overload: UpdateStats() is the profile statistics method; me want the float one (FixedUpdate, owner, not dead).
    [HarmonyPrefix]
    [HarmonyPatch(nameof(Player.UpdateStats), typeof(float))]
    private static void UpdateStats_Prefix(Player __instance)
    {
        if (!ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            if (!ServerRules.Current.IsPending)
            {
                Income.HoldDrain(__instance);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.UpdateStats prefix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Player.UpdateStats), typeof(float))]
    private static void UpdateStats_Postfix(Player __instance, float dt)
    {
        if (!ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            var rules = ServerRules.Current;
            if (rules.IsPending || __instance.IsTeleporting() || __instance.InIntro())
            {
                return; // vanilla skipped its stats too (intro, teleport), or vanilla rules
            }
            Income.Tick(__instance, dt, rules);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.UpdateStats postfix", e);
        }
    }

    // Every frame per Player object: one reference compare; local player: key read, rules read, two float reads while
    // the bar is not full. No alloc.
    [HarmonyPostfix]
    [HarmonyPatch(nameof(Player.Update))]
    private static void Update_Postfix(Player __instance)
    {
        if (!ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            Trigger.Tick(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.Update postfix", e);
        }
    }

    // An alerted monster targets the local player (its owner's game send this about every 0.5 s).
    [HarmonyPostfix]
    [HarmonyPatch(nameof(Player.RPC_OnTargeted))]
    private static void RPC_OnTargeted_Postfix(Player __instance, bool alerted)
    {
        if (!alerted || !ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            CombatState.MarkTargeted();
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.RPC_OnTargeted postfix", e);
        }
    }

    // Perfect dodge: someone swung or shot at the local player (the RPC carries no attacker).
    [HarmonyPrefix]
    [HarmonyPatch(nameof(Player.RPC_HitWhileDodging))]
    private static void RPC_HitWhileDodging_Prefix(Player __instance)
    {
        if (!ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            CombatState.MarkExchange();
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.RPC_HitWhileDodging prefix", e);
        }
    }
}
