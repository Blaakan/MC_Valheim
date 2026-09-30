using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.SneakAmbushMod.Patches;

// Me = local player's stealth refresh (design 2.3-2.6). Player.UpdateStealth run in owner branch of FixedUpdate
// (50 Hz); vanilla recompute target every 0.5 s (timer > 0.5), then ramp factor 0.25/s, write ZDO "Stealth".
//   prefix  still timer every call; at refresh (timer + dt > 0.5): displacement since last refresh, skill, cues
//           (foliage sphere + crown ray, fog share, mist, smoke). Stillness end with StillEndsAtOnce: force refresh
//           now (timer 0.51) and ask postfix to snap.
//   postfix Priority.Last (after SecondaryAttacks' own UpdateStealth postfix, which recompute target): snap factor
//           up at once to value without still bonus, write ZDO; at refresh sync the cue icons.
// Other players: cheap reference compare and out.
[HarmonyPatch(typeof(Player), nameof(Player.UpdateStealth))]
internal static class PlayerPatches
{
    private const float RefreshInterval = 0.5f;
    private const float CrownRay = 20f;

    private static int _viewblockMask = -1;

    [HarmonyPrefix]
    private static void Prefix(Player __instance, float dt)
    {
        if (!ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            if (!ReferenceEquals(StealthState.For, __instance))
            {
                StealthState.Reset(__instance);
            }
            var rules = ServerRules.Current;
            var refresh = __instance.m_stealthFactorUpdateTimer + dt > RefreshInterval;
            var crouch = __instance.IsCrouching();
            var still = crouch && __instance.IsOnGround() && !__instance.IsSneaking();
            if (refresh)
            {
                var pos = __instance.transform.position;
                if (StealthState.HaveRefreshPos
                    && (pos - StealthState.LastRefreshPos).sqrMagnitude >= StealthState.StillMoveLimit * StealthState.StillMoveLimit)
                {
                    still = false;
                }
                StealthState.LastRefreshPos = pos;
                StealthState.HaveRefreshPos = true;
            }
            StealthState.StillTime = still ? StealthState.StillTime + dt : 0f;

            // Stillness over while bonus on: end now (StillEndsAtOnce) or at next refresh (fade at vanilla speed).
            if (StealthState.StillActive && !still)
            {
                StealthState.StillActive = false;
                if (rules.StillEndsAtOnce && !rules.IsPending)
                {
                    if (!refresh)
                    {
                        __instance.m_stealthFactorUpdateTimer = RefreshInterval + 0.01f;
                        refresh = true;
                        // Forced refresh = new start for "moved since last refresh" too.
                        StealthState.LastRefreshPos = __instance.transform.position;
                        StealthState.HaveRefreshPos = true;
                    }
                    StealthState.SnapPending = true;
                    StealthState.SnapValue = StealthState.LastWithoutStill;
                }
            }

            StealthState.RefreshThisCall = refresh;
            if (refresh)
            {
                Refresh(__instance, rules, crouch, still);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.UpdateStealth prefix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(Player __instance)
    {
        if (!ReferenceEquals(__instance, Player.m_localPlayer))
        {
            return;
        }
        try
        {
            if (StealthState.SnapPending)
            {
                StealthState.SnapPending = false;
                // Up at once to value without still bonus (never above new target, never down).
                var snap = Mathf.Min(StealthState.SnapValue, __instance.m_stealthFactorTarget);
                if (snap > __instance.m_stealthFactor)
                {
                    __instance.m_stealthFactor = snap;
                    var zdo = __instance.m_nview != null ? __instance.m_nview.GetZDO() : null;
                    if (zdo != null)
                    {
                        zdo.Set(ZDOVars.s_stealth, snap);
                    }
                }
            }
            if (StealthState.RefreshThisCall)
            {
                StealthState.RefreshThisCall = false;
                StealthCues.Sync(__instance, ServerRules.Current);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.UpdateStealth postfix", e);
        }
    }

    // Once per 0.5 s: still decision, skill, cues. Stealth only exist crouched (vanilla); smoke cue always.
    private static void Refresh(Player player, AmbushRules rules, bool crouch, bool still)
    {
        Compat.Ensure();
        var pending = rules.IsPending;
        StealthState.StillActive = !pending && still && rules.StillBonus > 0 && StealthState.StillTime >= rules.StillDelay;
        StealthState.Crouching = crouch;
        StealthState.SkillFactor = player.GetSkillFactor(Skills.SkillType.Sneak);
        if (crouch && !pending)
        {
            var center = player.GetCenterPoint();
            var mask = ViewblockMask;
            // Same trigger rule as vanilla sight ray (BaseAI.CanSeeTarget use global default): what block creature
            // sight is what me show, trigger or not.
            StealthState.FoliageTouching = mask != 0
                && Physics.CheckSphere(center, Mathf.Max(0.01f, rules.FoliageReach), mask, QueryTriggerInteraction.UseGlobal);
            StealthState.FoliageCrown = mask != 0 && !StealthState.FoliageTouching
                && Physics.Raycast(center, Vector3.up, CrownRay, mask, QueryTriggerInteraction.UseGlobal);
            StealthState.FogShare = !player.InInterior() && StealthState.FogDensityKnown
                ? StealthState.FogShareFor(StealthState.FogDensity, rules)
                : 0f;
            StealthState.InMist = ParticleMist.IsInMist(center);
        }
        else
        {
            StealthState.FoliageTouching = false;
            StealthState.FoliageCrown = false;
            StealthState.FogShare = 0f;
            StealthState.InMist = false;
        }
        if (!crouch)
        {
            // Standing: vanilla no call ModifyStealth, so me clear "capped" here (else lingering icon keep old "max").
            StealthState.Capped = false;
        }
        StealthState.InSmoke = !pending && SmokeRegistry.Count > 0
            && SmokeRegistry.InActiveCloud(SmokeRegistry.TracedPoint(player), rules, SmokeRegistry.Now);
    }

    // Layer of bush / tree crown colliders that block creature sight and shade (vanilla m_viewBlockMask part).
    private static int ViewblockMask
    {
        get
        {
            if (_viewblockMask < 0)
            {
                var layer = LayerMask.NameToLayer("viewblock");
                _viewblockMask = layer >= 0 ? 1 << layer : 0;
            }
            return _viewblockMask;
        }
    }
}
