using System;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Crafting.StationsBatchFeedMod.Patches;

// Player.Interact = single door of local player's Use key (keyboard and controller, press and hold repeat).
// Me hook here, not on each station: one place for all classes, and other mods calling station Interact by code
// stay untouched. Prefix: batch press (modifier held) replace the vanilla press; plain press of a non-owner may be
// blocked by pending adds, else remembered by the postfix. Owner plain press = pure vanilla.
// Framework only patch these while feature Active. Every body catch own errors: never throw into game.
[HarmonyPatch]
internal static class PlayerPatches
{
    // Low: other mods' prefixes run first; if one cancel the press (__runOriginal false), me do nothing.
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Low)]
    [HarmonyPatch(typeof(Player), nameof(Player.Interact), new[] { typeof(GameObject), typeof(bool), typeof(bool) })]
    // alt is ref: batch key on a fire that me hand back to vanilla must still add fuel, never toggle (see below).
    private static bool Interact_Prefix(Player __instance, GameObject go, bool hold, ref bool alt, bool __runOriginal,
        out PressRecord __state)
    {
        __state = default;
        // Cheap first: hold call come every frame while Use held. Same 0.2 s gate as vanilla.
        if (!__runOriginal || (hold && Time.time - __instance.m_lastHoverInteractTime < 0.2f))
        {
            return true;
        }
        try
        {
            if (!ReferenceEquals(__instance, Player.m_localPlayer) || go == null || __instance.InAttack() || __instance.InDodge()
                || !FeedTarget.TryResolve(go, out var target))
            {
                return true;
            }

            if (!hold && BatchFeeder.BatchKeyHeld(alt))
            {
                if (BatchFeeder.TryRun(__instance, target))
                {
                    return false;
                }
                // Me say no to batch (full, no owner, owner gone...), vanilla do one press. Batch key on fire = add fuel,
                // never toggle, same as loop. Default key and controller: alt already true. Mod-only key: alt false, so
                // me set it, else vanilla Fireplace.Interact turn fire off.
                if (target.Kind == FeedKind.Fire)
                {
                    alt = true;
                }
                return true;
            }
            return !BatchFeeder.GuardPlainPress(__instance, target, hold, alt, ref __state);
        }
        catch (Exception e)
        {
            // Never leave loop state on; vanilla press go ahead.
            BatchFeeder.Muting = false;
            __state = default;
            PatchGuard.Report("Player.Interact prefix", e);
            return true;
        }
    }

    // Run even when vanilla skipped: state only set for a plain press that vanilla was allowed to do.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Player), nameof(Player.Interact), new[] { typeof(GameObject), typeof(bool), typeof(bool) })]
    private static void Interact_Postfix(Player __instance, PressRecord __state)
    {
        if (!__state.Active)
        {
            return;
        }
        try
        {
            BatchFeeder.AfterPlainPress(__instance, __state);
        }
        catch (Exception e)
        {
            PatchGuard.Report("Player.Interact postfix", e);
        }
    }
}
