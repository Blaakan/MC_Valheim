using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.SneakAmbushMod.Patches;

// ALWAYS ON (framework applies me at start under <guid>.alwayson; toggle never remove me). Me register the sneak-attack
// XP RPC on every Player's ZNetView, whatever the toggle said when the player spawned: a game with the feature off
// has the handler and ignores the call (no "Failed to find rpc method" on the sender). Handler check feature state and
// rules itself. Once per Player.
[AlwaysOnPatch]
[HarmonyPatch(typeof(Player), nameof(Player.Awake))]
internal static class PlayerAwakePatches
{
    [HarmonyPostfix]
    private static void Postfix(Player __instance)
    {
        try
        {
            SneakXp.Register(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.Awake postfix", e);
        }
    }
}
