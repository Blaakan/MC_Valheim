using System.Globalization;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.TrinketsOnDemandMod;

// Me = bows and crossbows earn more adrenaline per hit, in proportion to their slower attack cycle (G6).
// Vanilla: a projectile pays a flat Projectile.m_adrenaline (its instance field, from the ammo's projectile prefab)
// once per hit on a character, in Projectile.OnHit (2 for every arrow and bolt); a melee swing pays m_attackAdrenaline
// per enemy hit (1 on one-hand weapons, swing about 0.9 s; 2 on two-hand ones, slower swing; measured 1.0.16).
// Me: Attack.FireProjectileBurst prefix (local player, weapon skill Bows or Crossbows) work out a factor and open a
// context (weapon, factor); Projectile.Setup postfix (same call, one per projectile) multiply the new projectile
// instance's own m_adrenaline when owner = local player and item = context weapon; finalizer close the context.
// Prefab never touched; nothing re-applied at hit time (Harpoon Hooks Tames zero the field at hit time: 0 stay 0).
//   cycle  = bow: draw% x Lerp(D, 0.2 D, skill) + ShotSeconds    crossbow: GetWeaponLoadingTime() + ShotSeconds
//   cycle  = min(cycle, time since the last local bow/crossbow attack)   (swap burst: pre-loaded crossbows, bow swaps)
//   mult   = clamp(cycle / RangedReferenceSeconds, 1, RangedMaxMultiplier)
//   factor = 1 + (mult - 1) / (projectiles x bursts)                      (multi-projectile weapons share the bonus)
// Time since last shot counted per attack (clone), not per burst or projectile. First shot = nominal cycle.
internal static class RangedBonus
{
    // Release / fire animation part of a shot, seconds (unverified: design seed, see TESTING and design 9).
    internal const float ShotSeconds = 0.5f;

    private static Attack _attack;           // clone of the last local ranged attack (bursts reuse its factor)
    private static float _factor = 1f;
    private static float _lastShotAt = float.NegativeInfinity;

    // Open context (Setup postfix read it): cheap static reads.
    internal static bool Open;
    internal static ItemDrop.ItemData Weapon;
    internal static float Factor = 1f;

#if DEBUG
    // Self test read the last scaled projectile: base, factor, result, and the inputs.
    internal struct Record
    {
        internal int Serial;
        internal Skills.SkillType Skill;
        internal float DrawPercentage;
        internal float Nominal;
        internal float SinceLast;
        internal float Factor;
        internal float Base;
        internal float Result;
        internal int Projectiles;
        internal int Bursts;
    }

    internal static Record Last;
    private static float _lastNominal;
    private static float _lastSince;
    private static float _lastDraw;
#endif

    internal static void Reset()
    {
        _attack = null;
        _factor = 1f;
        _lastShotAt = float.NegativeInfinity;
        Close();
    }

    internal static void Close()
    {
        Open = false;
        Weapon = null;
        Factor = 1f;
    }

    // FireProjectileBurst prefix, local player's attack only. True = context open (finalizer close it).
    internal static bool Begin(Attack attack, Player player, TrinketRules rules)
    {
        var weapon = attack.m_weapon;
        if (weapon == null || weapon.m_shared == null || !rules.RangedBonusOn)
        {
            return false;
        }
        var skill = weapon.m_shared.m_skillType;
        if (skill != Skills.SkillType.Bows && skill != Skills.SkillType.Crossbows)
        {
            return false; // other reload weapons (Dundr, grappling hook), staffs, spears: vanilla
        }
        if (!ReferenceEquals(attack, _attack))
        {
            var now = Time.time;
            var since = now - _lastShotAt;
            _lastShotAt = now;
            _attack = attack;
            float nominal;
            if (skill == Skills.SkillType.Bows)
            {
                var template = weapon.m_shared.m_attack;
                var draw = attack.m_bowDraw ? attack.m_attackDrawPercentage : 0f;
                nominal = BowCycle(draw, template != null ? template.m_drawDurationMin : 0f,
                    player.GetSkillFactor(skill));
#if DEBUG
                _lastDraw = draw;
#endif
            }
            else
            {
                nominal = CrossbowCycle(weapon.GetWeaponLoadingTime());
#if DEBUG
                _lastDraw = 0f;
#endif
            }
            _factor = FactorFor(nominal, since, rules.RangedReferenceSeconds, rules.RangedMaxMultiplier,
                attack.m_projectiles, attack.m_projectileBursts);
            // One line per shot (not per frame): testers read the factor here.
            Log.Debug("Ranged shot (" + skill + "): cycle " + nominal.ToString("0.##", CultureInfo.InvariantCulture)
                      + " s, " + (float.IsPositiveInfinity(since) ? "first shot" : since.ToString("0.##", CultureInfo.InvariantCulture)
                                                                            + " s since the last shot")
                      + " -> adrenaline per hit x" + _factor.ToString("0.##", CultureInfo.InvariantCulture) + ".");
#if DEBUG
            _lastNominal = nominal;
            _lastSince = since;
#endif
        }
        if (_factor <= 1f)
        {
            return false;
        }
        Open = true;
        Weapon = weapon;
        Factor = _factor;
        return true;
    }

    // Projectile.Setup postfix while the context is open.
    internal static void Apply(Projectile projectile, Character owner, ItemDrop.ItemData item)
    {
        if (!ReferenceEquals(item, Weapon) || !ReferenceEquals(owner, Player.m_localPlayer))
        {
            return;
        }
        var before = projectile.m_adrenaline;
        if (before <= 0f)
        {
            return; // 0 stay 0 (and never turn a loss into a bigger one)
        }
        projectile.m_adrenaline = before * Factor;
#if DEBUG
        Last = new Record
        {
            Serial = Last.Serial + 1,
            Skill = item.m_shared.m_skillType,
            DrawPercentage = _lastDraw,
            Nominal = _lastNominal,
            SinceLast = _lastSince,
            Factor = Factor,
            Base = before,
            Result = projectile.m_adrenaline,
            Projectiles = _attack != null ? _attack.m_projectiles : 0,
            Bursts = _attack != null ? _attack.m_projectileBursts : 0,
        };
#endif
    }

    // Pure (self test): nominal bow cycle. Full draw = Lerp(D, 0.2 D, skill factor) (Humanoid.GetAttackDrawPercentage).
    internal static float BowCycle(float drawPercentage, float drawDurationMin, float skillFactor)
    {
        var full = Mathf.Lerp(drawDurationMin, drawDurationMin * 0.2f, Mathf.Clamp01(skillFactor));
        return Mathf.Clamp01(drawPercentage) * Mathf.Max(0f, full) + ShotSeconds;
    }

    // Pure (self test): nominal crossbow cycle (loading time already has the skill in it).
    internal static float CrossbowCycle(float loadingTime) => Mathf.Max(0f, loadingTime) + ShotSeconds;

    // Pure (self test): the factor. Bad numbers (NaN, reference 0) = 1 (vanilla).
    internal static float FactorFor(float nominal, float sinceLast, float reference, float maxMultiplier,
        int projectiles, int bursts)
    {
        if (float.IsNaN(nominal) || float.IsNaN(sinceLast) || reference <= 0f || float.IsNaN(reference)
            || maxMultiplier <= 1f || float.IsNaN(maxMultiplier))
        {
            return 1f;
        }
        var cycle = Mathf.Min(nominal, sinceLast);
        var mult = Mathf.Clamp(cycle / reference, 1f, maxMultiplier);
        var n = Mathf.Max(1, projectiles) * Mathf.Max(1, bursts);
        return 1f + (mult - 1f) / n;
    }
}
