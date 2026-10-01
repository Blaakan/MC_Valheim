using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SailingSkillMod.Patches;

// S6 map reveal. Vanilla Minimap.Update call UpdateExplore every frame (map open or not): timer += Time.deltaTime,
// over m_exploreInterval -> Explore(player position, m_exploreRadius). Map data is local: own level, no network.
// Prefix: only on the frame Explore will run (same timer test as vanilla), local player aboard a ship (in a ship's
// volume, and standing on its deck or attached to it, helm included: a swimmer next to the hull does not count) ->
// radius x (1 + RevealBonusAtMax * s). Finalizer put the radius back if it still hold my value (ScaledField): mods that
// set it once, or scale and put it back themselves, keep their value; nothing grow tick after tick.
[HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdateExplore), new[] { typeof(float), typeof(Player) })]
internal static class MinimapPatches
{
#if DEBUG
    // Self test: radius the last Explore of UpdateExplore used (after my scaling), and the frame.
    internal static float LastRadius = -1f;
    internal static int LastFrame = -1;
#endif

    [HarmonyPrefix]
    private static void Prefix(Minimap __instance, Player player, out ScaledField __state)
    {
        __state = default;
        // Per frame: timer test first (vanilla's own float math: one add, one compare).
        if (__instance.m_exploreTimer + Time.deltaTime <= __instance.m_exploreInterval)
        {
            return;
        }
        try
        {
#if DEBUG
            LastRadius = __instance.m_exploreRadius;
            LastFrame = Time.frameCount;
#endif
            if (player == null || !Aboard(player))
            {
                return;
            }
            var rules = ServerRules.Current;
            if (rules.IsPending || rules.RevealBonusAtMax <= 0f)
            {
                return;
            }
            var s = HelmSkill.Local01();
            if (s <= 0f)
            {
                return;
            }
            __state = ScaledField.Scale(ref __instance.m_exploreRadius, SailMath.Scale(rules.RevealBonusAtMax, s));
#if DEBUG
            LastRadius = __instance.m_exploreRadius;
#endif
        }
        catch (Exception e)
        {
            PatchGuard.Report("Minimap.UpdateExplore prefix", e);
        }
    }

    [HarmonyFinalizer]
    private static void Finalizer(Minimap __instance, ScaledField __state)
    {
        try
        {
            __state.PutBack(ref __instance.m_exploreRadius);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Minimap.UpdateExplore finalizer", e);
        }
    }

    // Same "on a ship" test as vanilla distance stats (Ship.GetLocalShip: last ship volume the local player entered),
    // plus feet on its deck or attached to it (helm, chair), so a swimmer inside the volume does not count.
    internal static bool Aboard(Player player)
    {
        if (Ship.GetLocalShip() == null)
        {
            return false;
        }
        return player.IsAttachedToShip() || player.GetStandingOnShip() != null;
    }
}
