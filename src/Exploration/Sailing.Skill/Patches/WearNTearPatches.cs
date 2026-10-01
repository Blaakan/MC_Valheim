using System;
using System.Globalization;
using HarmonyLib;
using MC.Shared;

namespace MC.Exploration.SailingSkillMod.Patches;

// S7 ship damage. Every hit on a ship (WearNTear.Damage from ship's own code, creatures, collisions, fire, Ashlands
// ocean) go by RPC to the ship's owner: RPC_Damage. Prefix there, before resistances: damage x (1 -
// DamageReductionAtMax * s), s = best Sailing level aboard (HelmSkill.Damage01). Damage text, private-area check and
// health loss all see the reduced hit.
// Not reduced: hits by players (PvP, a player's own axe, catapults: attacker a Player or hit type PlayerHit/Catapult),
// capsized ship (upside-down damage must still break wrecks), weather wear (rain, ash, lava, snow: WearNTear.UpdateWear
// call ApplyDamage direct, never here: right, a moored ship has no sailor). Repair and deconstruct are no damage.
// Every building piece hit come here too: ship test (TryGetComponent) first, then owner.
[HarmonyPatch(typeof(WearNTear), nameof(WearNTear.RPC_Damage))]
internal static class WearNTearPatches
{
    [HarmonyPrefix]
    private static void Prefix(WearNTear __instance, HitData hit)
    {
        try
        {
            if (hit == null || !__instance.TryGetComponent(out Ship ship))
            {
                return;
            }
            var nview = __instance.m_nview;
            if (nview == null || !nview.IsValid() || !nview.IsOwner())
            {
                return;
            }
            var rules = ServerRules.Current;
            if (rules.IsPending || rules.DamageReductionAtMax <= 0f || !Reducible(hit, ship))
            {
                return;
            }
            var s = HelmSkill.Damage01(ship);
            if (s <= 0f)
            {
                return;
            }
            var before = hit.GetTotalDamage();
            hit.m_damage.Modify(SailMath.DamageFactor(rules.DamageReductionAtMax, s));
            Log.Debug($"{Utils.GetPrefabName(ship.gameObject)} hit ({hit.m_hitType}): {N(before)} -> "
                      + $"{N(hit.GetTotalDamage())} damage before resistances (best Sailing aboard {s * 100f:0}).");
        }
        catch (Exception e)
        {
            PatchGuard.Report("WearNTear.RPC_Damage prefix", e);
        }
    }

    private static string N(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    // Hit a sailor can soften: not from a player, ship not upside down.
    internal static bool Reducible(HitData hit, Ship ship)
    {
        if (hit.m_hitType == HitData.HitType.PlayerHit || hit.m_hitType == HitData.HitType.Catapult)
        {
            return false;
        }
        if (ship.transform.up.y < 0f)
        {
            return false;
        }
        var attacker = hit.GetAttacker();
        return attacker == null || !attacker.IsPlayer();
    }
}
