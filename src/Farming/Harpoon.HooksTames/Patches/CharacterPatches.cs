using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Farming.HarpoonHooksTamesMod.Patches;

// Thrower side. Character.Damage = last stop on thrower's game before the hit go (RPC) to the game that own the tame
// (maybe this game, maybe friend without mod). Me empty the harm out of the harpoon hit there: no damage, no push,
// no stagger, no backstab, no block. Hook hash and attacker stay: owner's vanilla RPC_Damage need both to hook.
// Owner then run only vanilla: add SE_Harpooned, pull, stamina drain, line break. Zero damage = ApplyDamage stop early:
// no health loss, no number, no alert, no flee.
// Framework only patch these while feature Active. Every body catch own errors: never throw into game.
[HarmonyPatch]
internal static class CharacterPatches
{
    // Last, after EpicLoot prefixes: damage other mods add to the hit (enchantments) get zeroed too.
    // Return false ONLY for extra proc hit on the tame me protect right now; any error = true (vanilla run).
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyAfter("randyknapp.mods.epicloot")]
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    private static bool Damage_Prefix(Character __instance, HitData hit, out bool __state)
    {
        __state = false;
        try
        {
#if DEBUG
            // Debug build only: self test play a thrower without me (owner side stay on). Finalizer still run.
            if (TestSwitches.ThrowerSideOff)
            {
                return true;
            }
#endif
            // Every hit in game come here. Scope check = plain ref compare; then hash (most hits: 0).
            if (HitScope.IsNestedHit(__instance, hit))
            {
                Log.Debug($"Blocked an extra hit on hooked tame {__instance.m_name} during the harpoon hit.");
                return false; // proc hit on same friend: nothing sent
            }
            if (hit == null || !HarpoonEffect.IsHarpoon(hit.m_statusEffectHash) || !hit.HaveAttacker())
            {
                return true;
            }

            var local = Player.m_localPlayer;
            if (local == null || hit.m_attacker != local.GetZDOID())
            {
                return true; // not my harpoon
            }
            if (!TameRules.IsTame(__instance))
            {
                return true; // wild creature, player: vanilla hit
            }

            hit.m_damage = default(HitData.DamageTypes);
            hit.m_pushForce = 0f;
            hit.m_staggerMultiplier = 0f;
            hit.m_backstabBonus = 1f;
            hit.m_blockable = false; // blocking tame would eat the hook and spend stamina on nothing
            __state = HitScope.Open(__instance);
            Log.Debug($"Hooked tame {__instance.m_name} (simulated by "
                      + (__instance.m_nview.IsOwner() ? "this game" : "another player")
                      + "): no damage, push or stagger.");
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(Damage_Prefix), e);
        }
        return true;
    }

    // Finalizer run after every mod's postfix, even on exception: scope never leak to next hit.
    // Void finalizer = original exception (if any) still thrown as vanilla.
    [HarmonyFinalizer]
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    private static void Damage_Finalizer(bool __state)
    {
        try
        {
            if (__state)
            {
                HitScope.Close();
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(Damage_Finalizer), e);
        }
    }
}

// Owner side (game that simulate the tame; always this game in single player). Vanilla RPC_Damage mark the thrower
// as attacker of the tame even for zero damage = kill credit later. Hook is not attack: me put the three ZDO keys
// back after vanilla. Hook itself (SE, SetAttacker) untouched. Own class: one __state type per class.
[HarmonyPatch]
internal static class CharacterRpcDamagePatches
{
    // Any player's zero-damage harpoon hit on a tame = hook (damaging PvP hit keep vanilla credit).
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
    private static void RPC_Damage_Prefix(Character __instance, HitData hit, out AttackerMarks __state)
    {
        __state = null;
        try
        {
#if DEBUG
            // Debug build only: self test play a tame owner without me (friend's game, vanilla server).
            if (TestSwitches.OwnerSideOff)
            {
                return;
            }
#endif
            // Cheap hash check first: this run for every hit on the owner.
            if (hit == null || !HarpoonEffect.IsHarpoon(hit.m_statusEffectHash))
            {
                return;
            }
            var nview = __instance.m_nview;
            if (nview == null || !nview.IsValid() || !nview.IsOwner())
            {
                return; // only owner write the mark
            }
            if (__instance.IsPlayer() || !__instance.IsTamed() || hit.GetTotalDamage() > 0f)
            {
                return;
            }
            if (!(hit.GetAttacker() is Player player))
            {
                return; // attacker not loaded: vanilla stop early, write nothing
            }
            __state = AttackerMarks.Take(nview.GetZDO(), player.GetPlayerName());
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(RPC_Damage_Prefix), e);
        }
    }

    // After vanilla (SE added, SetAttacker done): only changed keys go back, earlier real hit keep its mark.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
    private static void RPC_Damage_Postfix(Character __instance, AttackerMarks __state)
    {
        if (__state == null)
        {
            return;
        }
        try
        {
            var nview = __instance.m_nview;
            if (nview != null && __state.Restore(nview.GetZDO()))
            {
                Log.Debug($"Cleared the attacker mark a harpoon hook left on {__instance.m_name}.");
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(RPC_Damage_Postfix), e);
        }
    }
}
