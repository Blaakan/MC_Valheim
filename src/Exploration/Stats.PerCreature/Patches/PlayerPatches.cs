using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.StatsPerCreatureMod.Patches;

// Tame credit, message path: game that ran Tameable.Tame (creature owner, modded or not) send "<name> $hud_tamedone"
// to closest player within 30 m. That player get the tame. Two ways it reach us:
//   Player.Message      -> we are owner AND closest (local call)
//   Player.RPC_Message  -> other game tamed it, we closest (routed RPC to our own player)
// Hook on Player, not MessageHud: MessageHud skip messages while HUD hidden (Ctrl+F3), and other mods mute there.
// Also: spawn write start day of tame counting (custom data loaded just before OnSpawned).
// Framework only patch these while feature Active. Every body catch own errors: never throw into game.
[HarmonyPatch]
internal static class PlayerPatches
{
    // Run for every message to every player. Cheap checks first (int compare, ordinal EndsWith), Unity == last.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Player), nameof(Player.Message),
        new[] { typeof(MessageHud.MessageType), typeof(string), typeof(int), typeof(Sprite), typeof(bool) })]
    private static void Message_Postfix(Player __instance, MessageHud.MessageType type, string msg)
    {
        try
        {
            // Local player only: message our game send to a remote player count on their side, not here.
            if (type == MessageHud.MessageType.Center
                && CounterStore.TryParseTameMessage(msg, out var name)
                && __instance == Player.m_localPlayer)
            {
                CounterStore.AddTame(name, "message");
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.Message postfix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Player), nameof(Player.RPC_Message), new[] { typeof(long), typeof(int), typeof(string), typeof(int) })]
    private static void RPC_Message_Postfix(Player __instance, int type, string msg)
    {
        try
        {
            if (type == (int)MessageHud.MessageType.Center
                && CounterStore.TryParseTameMessage(msg, out var name)
                && __instance == Player.m_localPlayer)
            {
                CounterStore.AddTame(name, "message");
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.RPC_Message postfix", e);
        }
    }

    // Once per spawn. First run for this character write the day tame counting start (never moved after).
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned), new[] { typeof(bool) })]
    private static void OnSpawned_Postfix(Player __instance)
    {
        try
        {
            if (__instance == Player.m_localPlayer)
            {
                CounterStore.EnsureStarted(__instance);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.OnSpawned postfix", e);
        }
    }
}
