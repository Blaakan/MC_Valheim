using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.SneakAmbushMod.Patches;

// Me = the stealth math in one place (design 2.5). SEMan.ModifyStealth run only from Player.UpdateStealth (and again
// from SecondaryAttacks' UpdateStealth postfix): after every status effect (vanilla and other mods'), me multiply
//   early(s) x (1 - still) x (1 - foliage, if FoliageBonus and touching) x (1 - fog share)
// then floor: never under min(VisibilityFloor, before), never above before. Vanilla clamp 0..1 next.
// Also keep factor without still bonus (snap) and "capped" (cue texts). Rules pending = vanilla, nothing kept.
// Local player's SEMan only (its own game compute its factor; ZDO "Stealth" carry it).
[HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyStealth))]
internal static class SEManPatches
{
    [HarmonyPostfix]
    private static void Postfix(SEMan __instance, ref float stealth)
    {
        var player = Player.m_localPlayer;
        if (!ReferenceEquals(__instance.m_character, player) || ReferenceEquals(player, null))
        {
            return;
        }
        try
        {
            var rules = ServerRules.Current;
            if (rules.IsPending)
            {
                StealthState.Capped = false;
                StealthState.LastWithoutStill = stealth;
                return;
            }
            var before = stealth;
            var others = StealthState.Early(rules, StealthState.SkillFactor) * StealthState.FoliageFactor(rules)
                         * StealthState.FogFactor(rules);
            var still = StealthState.StillActive ? StealthState.StillFactor(rules) : 1f;
            stealth = StealthState.Apply(before, others * still, rules.VisibilityFloor, out var capped);
            StealthState.Capped = capped;
            StealthState.LastWithoutStill = StealthState.Apply(before, others, rules.VisibilityFloor, out _);
        }
        catch (Exception e)
        {
            PatchGuard.Report("SEMan.ModifyStealth postfix", e);
        }
    }
}
