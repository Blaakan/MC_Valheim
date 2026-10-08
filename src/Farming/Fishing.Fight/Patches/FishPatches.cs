using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Farming.FishingFightMod.Patches;

// Me = the hooked fish do what the fight say (design 2.4). Only the fish of Fight.Current, only on its owner (the
// fisher claim it on hook). Every other fish: one reference compare, vanilla.
//   CustomFixedUpdate prefix: vanilla never start its own escapes (me own the timing). Calm = escape time below 0, no
//     waypoint: vanilla make a hooked fish passive (Stop, dragged by the line). Struggle = escape time = time left
//     (vanilla wiggle and splash), a waypoint far along the run so vanilla call SwimDirection.
//   SwimDirection prefix: in a struggle me swim it myself, fast, across the line (vanilla turn at m_turnRate, often
//     10 deg/s: a side run would never show).
//   RandomizeWaypoint prefix: no new random waypoint (nor a nibble on another float) while me drive it.
//   Every other fish this game own: a hooked flag left behind with no float (fisher left mid-fight) is cleared, so
//   it stop splashing for everyone.
[HarmonyPatch(typeof(Fish))]
internal static class FishPatches
{
    // Struggle swim: how fast the fish turn to its run (deg/s) and catch the run speed (part of the gap per second).
    private const float TurnRate = 540f;
    private const float SteerGain = 6f;

    // Vanilla escape wiggle (Fish.CustomFixedUpdate): +-12 deg at sin(t*40).
    private const float WiggleDegrees = 12f;

    [HarmonyPrefix]
    [HarmonyPatch(nameof(Fish.CustomFixedUpdate))]
    private static void CustomFixedUpdate_Prefix(Fish __instance)
    {
        var fight = Fight.Current;
        if (fight == null || !ReferenceEquals(fight.Fish, __instance))
        {
            ClearLeftHook(__instance);
            return;
        }
        try
        {
            var nview = __instance.m_nview;
            if (nview == null || !nview.IsValid() || !nview.IsOwner())
            {
                return;
            }
            __instance.m_nextEscape = float.MaxValue;
            __instance.m_waypointFF = null;
            if (fight.Phase == FightPhase.Struggle && fight.RunDir != Vector3.zero)
            {
                __instance.m_escapeTime = Mathf.Max(fight.PhaseLeft, 0.05f);
                __instance.m_haveWaypoint = true;
                // Vanilla "reached" test compare x and y (not z): 1 m down keep it far in any run direction.
                __instance.m_waypoint = __instance.transform.position + fight.RunDir * 5f + Vector3.down;
                __instance.m_swimTimer = 1f;
                __instance.m_blockChange = Time.time + 1f;
            }
            else
            {
                __instance.m_escapeTime = -1f;
                __instance.m_haveWaypoint = false;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Fish.CustomFixedUpdate prefix", e);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(Fish.SwimDirection))]
    private static bool SwimDirection_Prefix(Fish __instance, float dt)
    {
        var fight = Fight.Current;
        if (fight == null || !ReferenceEquals(fight.Fish, __instance) || fight.Phase != FightPhase.Struggle
            || fight.RunDir == Vector3.zero)
        {
            return true;
        }
        try
        {
            Swim(fight, __instance, dt);
            return false;
        }
        catch (Exception e)
        {
            PatchGuard.Report("Fish.SwimDirection prefix", e);
            return true;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(Fish.RandomizeWaypoint))]
    private static bool RandomizeWaypoint_Prefix(Fish __instance, ref bool __result)
    {
        var fight = Fight.Current;
        if (fight == null || !ReferenceEquals(fight.Fish, __instance))
        {
            return true;
        }
        __result = false;
        return false;
    }

    // Fish this game own, hooked to nothing here, but its ZDO still say hooked: its fisher left mid-fight (or vanilla
    // dropped the float without letting it go). Every game splash such a fish forever while s_escape (float) > 0:
    // me clear both keys once. A real fight never match: the fisher's game own the fish and hold the float.
    // Hot (every fish, every tick): float check first (null for almost every fish), one ZDO int read for owned fish.
    private static void ClearLeftHook(Fish fish)
    {
        if (fish.m_fishingFloat != null)
        {
            return;
        }
        try
        {
            var nview = fish.m_nview;
            if (nview == null || !nview.IsValid() || !nview.IsOwner())
            {
                return;
            }
            var zdo = nview.GetZDO();
            if (zdo.GetInt(ZDOVars.s_hooked) != 1)
            {
                return;
            }
            zdo.Set(ZDOVars.s_hooked, 0);
            zdo.Set(ZDOVars.s_escape, 0f);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Fish.CustomFixedUpdate prefix (left hook)", e);
        }
    }

    private static void Swim(Fight fight, Fish fish, float dt)
    {
        var run = fight.RunDir;
        var pos = fish.transform.position;
        // Shore or shallows ahead: the fish turn and run the other way, only when that way is deeper (both shallow,
        // near the beach: keep the side, the player's rule must not swap every second).
        var ahead = fish.GetPointDepth(pos + run * 2f);
        if (ahead < fish.m_minDepth && fight.OtherRunDir != Vector3.zero)
        {
            var other = fish.GetPointDepth(pos + fight.OtherRunDir * 2f);
            if (FightLogic.TurnFromShallows(ahead, other, fish.m_minDepth))
            {
                fight.RequestFlip();
            }
        }
        var body = fish.m_body;
        if (fish.m_isJumping && body.linearVelocity.y > 0f)
        {
            // Vanilla leave a rising jump alone too.
            return;
        }
        if (!fish.m_isJumping)
        {
            var heading = Quaternion.RotateTowards(fish.transform.rotation, Quaternion.LookRotation(run, Vector3.up),
                TurnRate * dt);
            var wiggle = Quaternion.AngleAxis(Mathf.Sin(Time.time * 40f) * WiggleDegrees, Vector3.up);
            body.rotation = heading * wiggle;
        }
        var target = run * FightLogic.FishRunSpeed(fight.Profile.D01);
        var change = target - body.linearVelocity;
        // Hold depth; never push up out of the water (vanilla rule).
        if (fish.m_inWater < pos.y + fish.m_height && change.y > 0f)
        {
            change.y = 0f;
        }
        body.AddForce(change * Mathf.Clamp01(SteerGain * dt), ForceMode.VelocityChange);
    }
}
