using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.SneakAmbushMod.Patches;

// Me = health bars through smoke (design 2.12, G9, E2), local player's game. EnemyHud.TestShow false = vanilla
// destroy the plate (one per frame) and make a new one later (a new plate show only after aiming again). Players,
// bosses and tames never touched (smoke hide players only, D15: a tame's bar gone would lie). Blind and reveal are
// creature states: not here. Every frame per character: exit on false or no cloud loaded.
[HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.TestShow))]
internal static class EnemyHudPatches
{
    [HarmonyPostfix]
    private static void Postfix(Character c, ref bool __result)
    {
        if (!__result || SmokeRegistry.Count == 0 || c == null)
        {
            return;
        }
        try
        {
            if (c.IsPlayer() || c.IsBoss() || c.IsTamed())
            {
                return;
            }
            var rules = ServerRules.Current;
            if (rules.IsPending || rules.HealthBars == HealthBarMode.Off)
            {
                return;
            }
            var player = Player.m_localPlayer;
            if (player == null)
            {
                return;
            }
            if (SmokeRegistry.HidesHealthBar(SmokeRegistry.TracedPoint(player), c.GetCenterPoint(), rules))
            {
                __result = false;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("EnemyHud.TestShow postfix", e);
        }
    }
}
