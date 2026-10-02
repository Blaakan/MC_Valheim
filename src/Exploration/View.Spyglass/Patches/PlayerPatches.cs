using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.ViewSpyglassMod.Patches;

// Me = the player side of the spyglass. Local player only, except LateUpdate (arm pose of every player).
//   SetControls     click edges to Scope; spyglass up = feet, attack, block, jump, crouch, run all zero (look stay
//                   free: it goes through SetMouseLook)
//   SetMouseLook    aim slower by the zoom (mouse, gamepad stick and gyro all come through here)
//   AlwaysRotateCamera  body turn with the aim while the spyglass is up (vanilla does it for bow draw and block)
//   UpdateHover     nothing to pick or use while looking (vanilla does the same in build mode)
//   OnDamaged       a real hit lowers it
//   Update          Scope.Tick (state, checks, zoom)
//   LateUpdate      ArmPose (after the animator)
[HarmonyPatch(typeof(Player))]
internal static class PlayerPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(Player.SetControls))]
    private static void SetControls_Prefix(Player __instance, ref Vector3 movedir, ref bool attack, ref bool attackHold,
        ref bool secondaryAttack, ref bool secondaryAttackHold, ref bool block, ref bool blockHold, ref bool jump,
        ref bool crouch, ref bool run, ref bool autoRun, ref bool dodge)
    {
        if (!ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            if (!Scope.OnControls(__instance, attack, attackHold, block, out var zeroAll))
            {
                return;
            }
            attack = false;
            attackHold = false;
            secondaryAttack = false;
            secondaryAttackHold = false;
            if (zeroAll)
            {
                movedir = Vector3.zero;
                block = false;
                blockHold = false;
                jump = false;
                crouch = false;
                run = false;
                autoRun = false;
                dodge = false;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.SetControls prefix", e);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(Player.SetMouseLook))]
    private static void SetMouseLook_Prefix(Player __instance, ref Vector2 mouseLook)
    {
        if (!Scope.Active || !ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            mouseLook *= Scope.LookScale;
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.SetMouseLook prefix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Player.AlwaysRotateCamera))]
    private static void AlwaysRotateCamera_Postfix(Player __instance, ref bool __result)
    {
        if (!__result && Scope.Engaged && ReferenceEquals(__instance, Player.m_localPlayer))
        {
            __result = true;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Player.UpdateHover))]
    private static void UpdateHover_Postfix(Player __instance)
    {
        if (Scope.Engaged && ReferenceEquals(__instance, Player.m_localPlayer))
        {
            __instance.m_hovering = null;
            __instance.m_hoveringCreature = null;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Player.OnDamaged))]
    private static void OnDamaged_Postfix(Player __instance, HitData hit)
    {
        if (!Scope.Engaged || !ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            Scope.OnDamaged(hit);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.OnDamaged postfix", e);
        }
    }

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
            // Real time: the pause menu (time scale 0) must not freeze the spyglass half up behind it.
            Scope.Tick(__instance, Time.unscaledDeltaTime);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.Update postfix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Player.LateUpdate))]
    private static void LateUpdate_Postfix(Player __instance)
    {
        try
        {
            ArmPose.Update(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.LateUpdate postfix", e);
        }
    }
}
