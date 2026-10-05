using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.MusicInstrumentsMod.Patches;

// Me = the player side. Local player only, except LateUpdate (pose of every player).
//   SetControls  clicks to Performance (Attack with an instrument = song window; Attack/Block while playing = stop);
//                window or mini-game = feet, attack, block, jump, crouch, run, dodge all zero (seated players stay
//                seated: vanilla stands you up on these inputs); autoplay = feet free (walk), actions zero
//   OnDamaged    a real hit stops the music
//   Update       Performance.Tick and Listeners.Update (real time: the pause menu must not freeze a song half way)
//   LateUpdate   InstrumentPose (after the animator)
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
            if (!Performance.OnControls(__instance, attack, block, out var zeroAll, out var zeroActions))
            {
                return;
            }
            attack = false;
            attackHold = false;
            secondaryAttack = false;
            secondaryAttackHold = false;
            if (zeroAll || zeroActions)
            {
                block = false;
                blockHold = false;
                jump = false;
                crouch = false;
                run = false;
                autoRun = false;
                dodge = false;
            }
            if (zeroAll)
            {
                movedir = Vector3.zero;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.SetControls prefix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Player.OnDamaged))]
    private static void OnDamaged_Postfix(Player __instance, HitData hit)
    {
        if (Performance.Mode == PerformanceMode.None || !ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            Performance.OnDamaged(hit);
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
            var dt = Time.unscaledDeltaTime;
            Performance.Tick(__instance, dt);
            Listeners.Update();
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
            InstrumentPose.Update(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.LateUpdate postfix", e);
        }
    }
}
