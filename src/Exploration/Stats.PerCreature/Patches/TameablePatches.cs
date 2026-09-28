using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.StatsPerCreatureMod.Patches;

// Tame credit, owner fallback. Vanilla Tameable.Tame (run on creature owner) send "has been tamed" only to closest
// player within 30 m. Nobody that close = no message = message path count nothing. Then me (the owner, my game ran
// Tame) take the tame, but ONLY if me the closest player my game see. Me see only players loaded near me
// (Player.s_players = my near zones), not whole server. Other loaded player closer (maybe the one who fed it; owner
// follow active area, not the pen) = nobody get it: missed tame better than tame given to wrong player. Known hole:
// creature at edge of my active area, friend closer but past my loaded zones = me no see friend, me still take it.
// Same 30 m test as vanilla, same frame = never both paths for one tame.
// Observe only: me never change what Tame do. Framework only patch this while feature Active.
[HarmonyPatch]
internal static class TameablePatches
{
    private const float MessageRange = 30f;

    // Same guard as vanilla Tame: true = this call will tame. (Tame first bump CreatureTamed even when it tame
    // nothing, e.g. console "tame" on already tame creature. Me no count those.)
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Tameable), nameof(Tameable.Tame))]
    private static void Tame_Prefix(Tameable __instance, out bool __state)
    {
        __state = false;
        try
        {
            var nview = __instance.m_nview;
            __state = nview != null
                      && nview.IsValid()
                      && nview.IsOwner()
                      && __instance.m_monsterAI != null
                      && __instance.m_character != null
                      && !__instance.IsTamed();
        }
        catch (Exception e)
        {
            PatchGuard.Report("Tameable.Tame prefix", e);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Tameable), nameof(Tameable.Tame))]
    private static void Tame_Postfix(Tameable __instance, bool __state)
    {
        if (!__state)
        {
            return;
        }
        try
        {
            // Really tamed? (Other mod prefix may skip vanilla.) MakeTame set it right away on owner.
            var local = Player.m_localPlayer;
            if (local == null || !__instance.IsTamed() || __instance.m_character == null)
            {
                return;
            }
            var pos = __instance.transform.position;
            // Somebody within 30 m got vanilla message: message path count it (on their game or ours).
            if (Player.GetClosestPlayer(pos, MessageRange) != null)
            {
                return;
            }
            // Closest among loaded players only, same list vanilla use for its 30 m message.
            if (Player.GetClosestPlayer(pos, float.MaxValue) != local)
            {
                return;
            }
            CounterStore.AddTame(__instance.m_character.m_name, "owner");
        }
        catch (Exception e)
        {
            PatchGuard.Report("Tameable.Tame postfix", e);
        }
    }
}
