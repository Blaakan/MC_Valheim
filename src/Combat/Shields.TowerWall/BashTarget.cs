using System.Collections.Generic;
using UnityEngine;

namespace MC.Combat.ShieldsTowerWallMod;

// Me = one heavy stagger per bash (design 2.6, decision 35). Bearer's game, local player's running bash only.
// Vanilla multi-hit give every creature in the arc the bash (full damage and push each: m_lowerDamagePerHit off). Only
// ONE of them keep the bash stagger multiplier: the creature nearest the middle of the swing (flat angle from the
// bearer's facing, then the nearer one). Every other creature of that swing get multiplier 1: not a bash hit any more
// for BashStagger on its owner, so it stagger like a punch (landed x 1). One bash = one stagger at most, like one
// buckler parry (decision 31), also in a group.
//   OnAttackTrigger prefix, first Hit event of our clone (AttackPatches): Begin.
//   AddHitPoint postfix, our clone only: keep vanilla's hit list. DoMeleeAttack fill it for the whole sweep before its
//       first Damage call, so at the first Damage the list hold every object this swing hit.
//   Character.Damage prefix while open (CharacterPatches): first bash hit = pick; bash hit on another = x1.
//   OnAttackTrigger finalizer: End (also when vanilla or another patch throw).
// The pick is geometry only: the lock limit live on each creature's owner (maybe another game), so a pick inside its
// lock staggers like a punch and the others too (that bash staggers nobody).
internal static class BashTarget
{
    // Two creatures this close in angle (degrees) count as level: the nearer one win.
    internal const float AngleTie = 1f;

    private static Attack _attack;
    private static List<Attack.HitPoint> _hits;
    private static Character _pick;
    private static bool _picked;

#if DEBUG
    // Self tests only: creature picked by the last bash Hit event (null = none yet), never reset.
    internal static Character DebugLastPick;
#endif

    // Our clone while its Hit event run (null = none). AddHitPoint postfix and Damage prefix compare against it.
    internal static Attack Open => _attack;

    internal static void Begin(Attack attack)
    {
        _attack = attack;
        _hits = null;
        _pick = null;
        _picked = false;
    }

    internal static void End()
    {
        _attack = null;
        _hits = null;
        _pick = null;
        _picked = false;
    }

    // AddHitPoint postfix, our clone: vanilla's list (same object every call of one sweep).
    internal static void OnHitList(List<Attack.HitPoint> list) => _hits = list;

    // Damage prefix, scope open. Bash hit = PlayerHit, skill Blocking, multiplier above 1 (BashStagger.IsBashHit); a
    // nested hit in the same call (block push on the bearer) has multiplier 1 and stay untouched.
    internal static void OnDamage(Character victim, HitData hit)
    {
        if (!BashStagger.IsBashHit(hit))
        {
            return;
        }
        if (!_picked)
        {
            _picked = true;
            _pick = Pick(victim);
#if DEBUG
            DebugLastPick = _pick;
#endif
        }
        if (!ReferenceEquals(victim, _pick))
        {
            hit.m_staggerMultiplier = 1f;
        }
    }

    // Creature of the hit list nearest the middle of the swing. No list (postfix never came) = the first one hit.
    private static Character Pick(Character first)
    {
        var bearer = _attack != null ? _attack.m_character : null;
        if (_hits == null || bearer == null)
        {
            return first;
        }
        var origin = bearer.transform.position;
        var forward = bearer.transform.forward;
        forward.y = 0f;
        Character best = null;
        var bestAngle = float.MaxValue;
        var bestDistance = float.MaxValue;
        foreach (var point in _hits)
        {
            if (point == null || point.go == null)
            {
                continue;
            }
            var c = point.go.GetComponent<Character>();
            if (c == null)
            {
                continue;
            }
            var to = c.transform.position - origin;
            to.y = 0f;
            var distance = to.magnitude;
            var angle = distance > 0.001f && forward.sqrMagnitude > 1e-6f ? Vector3.Angle(forward, to) : 0f;
            if (best == null || angle < bestAngle - AngleTie
                || (angle <= bestAngle + AngleTie && distance < bestDistance))
            {
                best = c;
                bestAngle = angle;
                bestDistance = distance;
            }
        }
        return best != null ? best : first;
    }
}
