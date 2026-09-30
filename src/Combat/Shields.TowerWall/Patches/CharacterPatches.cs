using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.ShieldsTowerWallMod.Patches;

// Me = hits (design 2.5, 2.6). RPC_Damage run on the victim's owner, Damage on the attacker's game.
//   RPC_Damage prefix (every hit on every owned character; one bool, one enum and one reference compare otherwise):
//     - bash hit on a non-player owned here: BashStagger store it and its stagger, multiplier 0 (or 1 in lock limit);
//     - victim = local player with a tower: per-hit brace decision (stagger resist, push), frontal attack hits made
//       blockable while braced (Brace).
//   RPC_Damage finalizer: both per-call states put back as before the call. BlockAttack can nest an RPC_Damage (the
//     block push on a local attacker), so state is saved and restored, never just cleared. Finalizer, not postfix:
//     Harmony skip postfixes when the original (or an earlier postfix) throw, and a stale scope would stay for every
//     later hit (saved as "outer", put back again).
//   ApplyDamage prefix + postfix: the stored bash hit on this character only (one reference compare otherwise).
//   ApplyPushback(Vector3, float) prefix: local braced bearer, frontal push, brace hold = push scaled.
//   Damage prefix (bearer side, before the hit go to the victim's owner): while our bash's Hit event run, only the
//     picked creature keep the bash stagger multiplier, the others get x1 (BashTarget). One static read otherwise.
// Creature Morale and Harpoon Hooks Tames also prefix RPC_Damage: me only zero the bash's stagger multiplier and set
// m_blockable on my own bearer's hits; they read damage and attacker, me never touch those: no order needed. Harpoon
// Hooks Tames also prefix Damage: it touch only harpoon hits, me only bash hits.
[HarmonyPatch]
internal static class CharacterPatches
{
    internal struct DamageState
    {
        internal bool Saved;
        internal BashStagger.Scope Bash;
        internal Brace.HitScope Brace;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    private static void Damage_Prefix(Character __instance, HitData hit)
    {
        if (BashTarget.Open == null || hit == null)
        {
            return;
        }
        try
        {
            BashTarget.OnDamage(__instance, hit);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Character.Damage prefix", e);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
    private static void RPC_Damage_Prefix(Character __instance, HitData hit, out DamageState __state)
    {
        __state.Bash = BashStagger.SaveScope();
        __state.Brace = Brace.SaveScope();
        __state.Saved = true;
        var rules = TowerSync.Applied;
        if (rules == null || hit == null)
        {
            return;
        }
        try
        {
            if (BashStagger.IsBashHit(hit) && !__instance.IsPlayer() && __instance.m_nview != null
                && __instance.m_nview.IsOwner())
            {
                BashStagger.OnIncoming(__instance, hit, rules);
            }
            else if (ReferenceEquals(__instance, Player.m_localPlayer))
            {
                Brace.OnIncomingHit((Player)__instance, hit, rules);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Character.RPC_Damage prefix", e);
        }
    }

    // Void finalizer: an exception of vanilla (or another patch) still go up as without me. Not saved = an earlier
    // prefix threw before mine ran: nothing of mine to put back.
    [HarmonyFinalizer]
    [HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
    private static void RPC_Damage_Finalizer(DamageState __state)
    {
        if (!__state.Saved)
        {
            return;
        }
        try
        {
            BashStagger.RestoreScope(__state.Bash);
            Brace.RestoreScope(__state.Brace);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Character.RPC_Damage finalizer", e);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
    private static void ApplyDamage_Prefix(Character __instance, HitData hit, out bool __state)
    {
        __state = false;
        if (hit == null || !ReferenceEquals(hit, BashStagger.StoredHit))
        {
            return;
        }
        try
        {
            __state = BashStagger.WillApply(__instance, hit);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Character.ApplyDamage prefix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
    private static void ApplyDamage_Postfix(Character __instance, HitData hit, bool __state)
    {
        if (!__state || !ReferenceEquals(hit, BashStagger.StoredHit))
        {
            return;
        }
        try
        {
            BashStagger.AfterApply(__instance, hit);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Character.ApplyDamage postfix", e);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Character), nameof(Character.ApplyPushback), new[] { typeof(Vector3), typeof(float) })]
    private static void ApplyPushback_Prefix(Character __instance, Vector3 dir, ref float pushForce)
    {
        if (!ReferenceEquals(__instance, Player.m_localPlayer) || pushForce == 0f)
        {
            return;
        }
        try
        {
            Brace.ScalePush((Player)__instance, dir, ref pushForce);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Character.ApplyPushback prefix", e);
        }
    }
}
