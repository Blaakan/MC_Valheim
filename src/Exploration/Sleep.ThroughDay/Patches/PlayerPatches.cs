using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SleepThroughDayMod.Patches;

// Client part of waking up. Player.SetSleeping = only place m_sleeping change (SleepStart / SleepStop RPC).
// Prefix keep old state and arm "Good evening" swap for day-sleep wake; Message prefix swap vanilla "Good morning"
// inside that call (vanilla order stay: wake text, then Rested message); postfix (WakeMessage) disarm, act only on
// real change: remember start, log wake.
// Framework only patch this while feature Active. Every body catch own errors: never throw into game.
[HarmonyPatch]
internal static class PlayerPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Player), nameof(Player.SetSleeping))]
    private static void SetSleeping_Prefix(Player __instance, bool sleep, out bool __state)
    {
        __state = false;
        try
        {
            __state = __instance.m_sleeping;
            WakeMessage.Before(__instance, sleep);
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(SetSleeping_Prefix), e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Player), nameof(Player.SetSleeping))]
    private static void SetSleeping_Postfix(Player __instance, bool __state)
    {
        try
        {
            WakeMessage.After(__instance, __state);
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(SetSleeping_Postfix), e);
        }
    }

    // Every message of every player: one static read when nothing armed (armed only inside one wake-up call).
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Player), nameof(Player.Message),
        new[] { typeof(MessageHud.MessageType), typeof(string), typeof(int), typeof(Sprite), typeof(bool) })]
    private static void Message_Prefix(Player __instance, MessageHud.MessageType type, ref string msg)
    {
        if (WakeMessage.SwapText == null)
        {
            return;
        }
        try
        {
            WakeMessage.Swap(__instance, type, ref msg);
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(Message_Prefix), e);
        }
    }
}
