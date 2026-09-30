using System;
using HarmonyLib;
using MC.Shared;

namespace MC.Combat.SneakAmbushMod.Patches;

// Me = forget (design 2.11), on creature's owner, per AI tick (20 Hz): target is a Player, not sensed for
// ForgetSeconds (vanilla's own timer), and smoke hide it (or creature blinded) -> timer 31 s. Next tick vanilla give up
// (> 30 s): not alerted, target cleared, 5 s before next search. Other code hooking the give-up still see it happen.
// Exit at once when no cloud loaded. Creature Morale's UpdateAI prefix may skip vanilla on rout frames: then
// UpdateTarget not run, me do nothing that tick (routing creatures have no target).
// HuntPlayer creatures (raids, hunt spawns): vanilla UpdateAI call SetAlerted(true) every tick before UpdateTarget,
// and MonsterAI.SetAlerted(true) put the timer back to 0, so timer never reach ForgetSeconds: they never give up
// (vanilla 30 s give-up neither; D14). Arrow landing near a pursuer (RPC_OnNearProjectileHit) reset it too.
[HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateTarget),
    new[] { typeof(Humanoid), typeof(float), typeof(bool), typeof(bool) },
    new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Out })]
internal static class MonsterAIPatches
{
    internal const float GiveUpTime = 31f; // vanilla give up above 30 s unsensed

    [HarmonyPostfix]
    private static void Postfix(MonsterAI __instance)
    {
        if (SmokeRegistry.Count == 0)
        {
            return;
        }
        try
        {
            if (!(__instance.m_targetCreature is Player target) || target == null)
            {
                return;
            }
            var rules = ServerRules.Current;
            if (rules.IsPending || __instance.m_timeSinceSensedTargetCreature < rules.ForgetSeconds
                || __instance.m_timeSinceSensedTargetCreature >= GiveUpTime)
            {
                return;
            }
            var creature = __instance.m_character;
            if (creature == null || creature.m_eye == null)
            {
                return;
            }
            if (SmokeRegistry.Hides(__instance.transform, creature.m_eye.position, target, hearing: false, out _))
            {
                __instance.m_timeSinceSensedTargetCreature = GiveUpTime;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("MonsterAI.UpdateTarget postfix", e);
        }
    }
}
