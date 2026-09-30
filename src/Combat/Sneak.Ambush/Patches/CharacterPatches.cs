using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.SneakAmbushMod.Patches;

// Me = hits on creatures, on the victim's owner (Character.RPC_Damage: large method reached through the RPC
// delegate, never inlined, so late activation work).
//   sneak-attack XP (design 2.1): prefix keep m_backstabTime when hit could backstab (bonus > 1); postfix: changed =
//     vanilla backstab happened -> SneakXp.OnBackstab. Only vanilla backstabs pay (Tower bash, Smoke Screen: x1).
//   reveal (design 2.10, D31): while a cloud is loaded, a Player hit that carry damage BEFORE resistances (fire-,
//     poison-, spirit-only, fully resisted, NG+ armour-eaten included; same test as Creature Morale's provocation)
//     and pass vanilla's early exits -> postfix: that creature see that player through smoke for RevealSeconds
//     (renewed each hit) if still alive. Smoke Screen hit carry no damage: never reveal.
// Other hits: two reference checks, one compare. Creature Morale's prefix may set bonus to 1 first (calm creature
// that see you): then vanilla leave m_backstabTime alone and me pay nothing. Order of prefixes do not matter.
[HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
internal static class CharacterPatches
{
    internal struct HitState
    {
        internal Player Attacker;
        internal bool WatchBackstab;
        internal float BackstabTime;
        internal bool Reveal;
    }

    [HarmonyPrefix]
    private static void Prefix(Character __instance, HitData hit, out HitState __state)
    {
        __state = default;
        if (hit == null || ReferenceEquals(__instance.m_baseAI, null))
        {
            return;
        }
        var backstab = hit.m_backstabBonus > 1f;
        var cloud = SmokeRegistry.Count > 0;
        if (!backstab && !cloud)
        {
            return;
        }
        try
        {
            var nview = __instance.m_nview;
            if (nview == null || !nview.IsValid() || !nview.IsOwner())
            {
                return;
            }
            var attacker = hit.GetAttacker() as Player;
            if (attacker == null)
            {
                return;
            }
            __state.Attacker = attacker;
            if (backstab)
            {
                __state.WatchBackstab = true;
                __state.BackstabTime = __instance.m_backstabTime;
            }
            if (cloud && hit.m_damage.GetTotalDamage() > 0f && PassesVanillaExits(__instance, hit))
            {
                __state.Reveal = true;
            }
        }
        catch (Exception e)
        {
            __state = default;
            PatchGuard.Report("Character.RPC_Damage prefix", e);
        }
    }

    [HarmonyPostfix]
    private static void Postfix(Character __instance, HitData hit, HitState __state)
    {
        if (ReferenceEquals(__state.Attacker, null))
        {
            return;
        }
        try
        {
            if (__state.Reveal && !__instance.IsDead() && __instance.GetHealth() > 0f)
            {
                var rules = ServerRules.Current;
                if (!rules.IsPending && rules.RevealSeconds > 0f)
                {
                    AiMemory.Reveal(__instance, __state.Attacker, SmokeRegistry.Now + rules.RevealSeconds);
                }
            }
            if (__state.WatchBackstab && __instance.m_backstabTime != __state.BackstabTime)
            {
                SneakXp.OnBackstab(__instance, __state.Attacker, hit);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("Character.RPC_Damage postfix", e);
        }
    }

    // Vanilla's exits before damage (debug flying, dead, teleporting, cutscene, dodged): such a hit is no hit.
    private static bool PassesVanillaExits(Character victim, HitData hit)
    {
        if (victim.IsDebugFlying() || victim.GetHealth() <= 0f || victim.IsDead() || victim.IsTeleporting()
            || victim.InCutscene())
        {
            return false;
        }
        return !(hit.m_dodgeable && victim.IsDodgeInvincible());
    }
}
