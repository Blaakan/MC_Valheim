using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Farming.HarpoonHooksTamesMod.Patches;

// Vanilla Projectile.IsValidTarget = only place that say "hit" or "fly past" (OnHit and DoAOE both ask it).
// Player without PvP: tame never valid (BaseAI.IsEnemy say friend), harpoon fly through.
// Me (postfix, keep every vanilla rule and other mods' prefixes): local player's harpoon + tame nobody ride
// (TameRules.CanHook) = valid, whatever tame AI do. Tame in harpoon path catch it, enemy behind not (vanilla PvP same).
// Rest of OnHit run as vanilla (hit effect, attach, Character.Damage -> hook on owner).
// Also: harpoon hit on tame give no Spears skill, no adrenaline (else free farm on a pen).
// Framework only patch this while feature Active. Every body catch own errors: never throw into game.
[HarmonyPatch]
internal static class ProjectilePatches
{
    // Last projectile + tame me logged "fly past" for. FixedUpdate ray look 1.5 step ahead, so same tame get asked
    // 2-3 physics steps in a row: me log once per pair. Plain ref compare only (no Unity ==).
    private static Projectile s_lastSkipProjectile;
    private static Character s_lastSkipTarget;

    // Me turned on/off: forget last pair.
    internal static void ClearSkipLog()
    {
        s_lastSkipProjectile = null;
        s_lastSkipTarget = null;
    }

    // Run per collider a projectile touch (not per frame). Harpoon check first: most projectiles leave at once.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.IsValidTarget))]
    private static void IsValidTarget_Postfix(Projectile __instance, IDestructible destr, ref bool __result)
    {
        try
        {
#if DEBUG
            // Debug build only: self test play a thrower without me (vanilla answer stay).
            if (TestSwitches.ThrowerSideOff)
            {
                return;
            }
#endif
            if (!HarpoonEffect.IsHarpoon(__instance.m_statusEffectHash))
            {
                HarpoonEffect.ReportRemovedHookOnce(__instance, destr); // other mod eat hook? me say once
                return;
            }

            var local = Player.m_localPlayer;
            if (local == null || __instance.m_owner != local)
            {
                return; // Unity ==, never ?.  Other player's harpoon = their game decide.
            }

            if (!(destr is Character character) || !TameRules.IsTame(character))
            {
                return;
            }

            if (!__result)
            {
                if (!TameRules.CanHook(character, out var refusal))
                {
                    LogSkipOnce(__instance, character, refusal); // rider on it: fly past, like vanilla
                    return;
                }
                if (__instance.m_dodgeable && character.IsDodgeInvincible())
                {
                    return; // keep vanilla dodge rule
                }
                __result = true;
            }

            // Now tame WILL be hit (also PvP hits vanilla already allowed). OnHit read these two fields right after
            // Damage. Single-hit projectile only: area or pass-through projectile use them for other targets too.
            if (__instance.m_aoe <= 0f && !__instance.m_onlyStopOnTerrain)
            {
                __instance.m_raiseSkillAmount = 0f; // hook friend = no spear skill
                __instance.m_adrenaline = 0f;       // and no rage juice
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report(nameof(IsValidTarget_Postfix), e);
        }
    }

    // Harpoon fly past a tame with no word in game: me say why in Debug log, once per projectile + tame.
    // String made only here (skip path, once per pair), never on the normal hit path.
    private static void LogSkipOnce(Projectile projectile, Character character, string refusal)
    {
        if (ReferenceEquals(projectile, s_lastSkipProjectile) && ReferenceEquals(character, s_lastSkipTarget))
        {
            return;
        }
        s_lastSkipProjectile = projectile;
        s_lastSkipTarget = character;
        Log.Debug($"Harpoon passed tame {character.m_name}: {refusal}.");
    }
}
