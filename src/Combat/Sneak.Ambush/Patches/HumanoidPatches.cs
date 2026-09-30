using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.SneakAmbushMod.Patches;

// ALWAYS ON. Throw guard (E5): local player, Smoke Screen in hand, feature inactive (off, or server without the mod)
// or rules pending -> refuse the attack (skip original, result false), bomb stays in the stack, short message at most
// every 3 s. Without me a throw on a vanilla server spawn objects other games cannot create. Active and rules here:
// two bool reads and out. Sibling mods (Dual Wielding, Weapon Moveset, Tower Shield Wall) tolerate a skipped original.
[AlwaysOnPatch]
[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
internal static class HumanoidPatches
{
    internal const string InactiveMessage =
        "Smoke Screen does nothing here: " + ModInfo.Name + " is turned off (or the server does not have it).";
    internal const string PendingMessage = "Smoke Screen: waiting for the server's " + ModInfo.Name + " settings.";
    private const float MessageInterval = 3f;

    private static float _lastMessage = -100f;

#if DEBUG
    // Self test: next refused throw show its message at once (not held back by the 3 s gap of an earlier one).
    internal static void TestResetMessageTimer() => _lastMessage = -100f;
#endif

    [HarmonyPrefix]
    private static bool Prefix(Humanoid __instance, ref bool __result)
    {
        if (!ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return true;
        }
        try
        {
            var active = Plugin.FeatureActive;
            var pending = active && ServerRules.IsPending;
            if (active && !pending)
            {
                return true;
            }
            if (!SmokeContent.IsSmokeScreen(__instance.GetCurrentWeapon()))
            {
                return true;
            }
            __result = false;
            var now = Time.time;
            if (now - _lastMessage >= MessageInterval)
            {
                _lastMessage = now;
                __instance.Message(MessageHud.MessageType.TopLeft, pending ? PendingMessage : InactiveMessage);
            }
            return false;
        }
        catch (Exception e)
        {
            PatchGuard.Report("Humanoid.StartAttack prefix", e);
            return true;
        }
    }
}
