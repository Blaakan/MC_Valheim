using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.CreaturesMoraleMod.Patches;

// Me = the creature's senses and death (design 2.5, 2.6, 2.7). Only while Active, like every patch. Bodies catch own
// errors. Hot ones exit on plain checks first, no allocation.
//   CanSenseTarget(Character, bool) postfix: afraid or routed toward a player = that player not sensed. FindEnemy skip
//       them and pick the next sensed enemy (a strong player's tames, a weaker friend). A kept target is sensed by
//       CanSee/CanHearTarget, never here: a provoked fight is untouched (same split as vanilla Passive Mobs).
//   FindEnemy postfix: hunters' fallback (closest player within 200 m, no sense check) gave an afraid-of player -> the
//       closest player it is hostile or provoked toward, or null. (Fear use CanSee/CanHearTarget, not this gate.)
//   OnDeath postfix: pack leader died -> rout (owner only; Character.OnDeath call m_onDeath there only).
//   InStealthRange postfix (sneaking player's own game): afraid creatures do not count, only them near = slow Sneak XP.
[HarmonyPatch]
internal static class BaseAIPatches
{
#if DEBUG
    private static float _nextSneakLog;
#endif

    [HarmonyPostfix]
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.CanSenseTarget), new[] { typeof(Character), typeof(bool) })]
    private static void CanSenseTarget_Postfix(BaseAI __instance, Character target, ref bool __result)
    {
        if (!__result)
        {
            return;
        }
        var ai = __instance as MonsterAI;
        var player = target as Player;
        if (ai == null || player == null)
        {
            return;
        }
        try
        {
            var nview = ai.m_nview;
            if (nview == null || !nview.IsOwner())
            {
                return;
            }
            var attitude = Attitudes.Judge(ai, player);
            if (attitude == Attitude.Afraid || attitude == Attitude.Routed)
            {
                __result = false;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("BaseAI.CanSenseTarget postfix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.FindEnemy))]
    private static void FindEnemy_Postfix(BaseAI __instance, ref Character __result)
    {
        var player = __result as Player;
        var ai = __instance as MonsterAI;
        if (player == null || ai == null)
        {
            return;
        }
        try
        {
            var nview = ai.m_nview;
            if (nview == null || !nview.IsOwner())
            {
                return;
            }
            var attitude = Attitudes.Judge(ai, player);
            if (attitude != Attitude.Afraid && attitude != Attitude.Routed)
            {
                return;
            }
            __result = ClosestHostileToward(ai);
        }
        catch (Exception e)
        {
            PatchGuard.Report("BaseAI.FindEnemy postfix", e);
        }
    }

    // Like vanilla's fallback (no sense check, ghost and debug fly skipped), but only players it would attack.
    private static Player ClosestHostileToward(MonsterAI ai)
    {
        Player best = null;
        var bestSqr = CreatureCheck.HuntRange * CreatureCheck.HuntRange;
        var pos = ai.transform.position;
        foreach (var p in Player.GetAllPlayers())
        {
            if (!CreatureCheck.Counts(p))
            {
                continue;
            }
            var sqr = (p.transform.position - pos).sqrMagnitude;
            if (sqr >= bestSqr)
            {
                continue;
            }
            var attitude = Attitudes.Judge(ai, p);
            if (attitude == Attitude.Hostile || attitude == Attitude.Provoked)
            {
                best = p;
                bestSqr = sqr;
            }
        }
        return best;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.OnDeath))]
    private static void OnDeath_Postfix(BaseAI __instance)
    {
        try
        {
            Rout.OnDeath(__instance);
        }
        catch (Exception e)
        {
            PatchGuard.Report("BaseAI.OnDeath postfix", e);
        }
    }

    // Vanilla said true = some enemy creature within its view range or 10 m, none alerted. True only if one of them
    // is not afraid of me. Called once per second while sneaking.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.InStealthRange))]
    private static void InStealthRange_Postfix(Character me, ref bool __result)
    {
        if (!__result)
        {
            return;
        }
        var player = me as Player;
        if (player == null)
        {
            return;
        }
        try
        {
            if (ServerRules.Pending || StandingCache.Get(player) == null)
            {
                return; // nothing is afraid of a player without standing
            }
            if (AnyNotAfraid(player))
            {
                return;
            }
            __result = false;
#if DEBUG
            if (Time.time >= _nextSneakLog)
            {
                _nextSneakLog = Time.time + 10f;
                Log.Debug("Sneak: only afraid creatures near, slow rate.");
            }
#endif
        }
        catch (Exception e)
        {
            PatchGuard.Report("BaseAI.InStealthRange postfix", e);
        }
    }

    // Same creatures vanilla counted (enemy of me, within its view range or 10 m).
    private static bool AnyNotAfraid(Player me)
    {
        var pos = me.transform.position;
        var all = BaseAI.BaseAIInstances;
        for (var i = 0; i < all.Count; i++)
        {
            var ai = all[i];
            if (ai == null || !BaseAI.IsEnemy(me, ai.m_character))
            {
                continue;
            }
            var distance = Vector3.Distance(pos, ai.transform.position);
            if (distance >= ai.m_viewRange && distance >= 10f)
            {
                continue;
            }
            var monster = ai as MonsterAI;
            if (monster == null || Attitudes.Judge(monster, me) != Attitude.Afraid)
            {
                return true;
            }
        }
        return false;
    }
}
