using System.Runtime.CompilerServices;
using UnityEngine;

namespace MC.Combat.ShieldsTowerWallMod;

// Me = bash stagger on the creature's owner (design 2.6, decisions 19-21). Vanilla take stagger from the damage left
// after resistances, NG+ armor and group scaling: a small bash would lose it. So on the victim's owner, for a bash hit
// on a non-player (PlayerHit, skill Blocking, 1 < stagger multiplier < 100, attacker a Player):
//   RPC_Damage prefix:  S = (blunt+slash+pierce+lightning as received) x multiplier x NG+ health factor; hit stored,
//                       multiplier set 0 (vanilla add none). Within the lock limit: multiplier 1, nothing stored
//                       (stagger like a punch).
//   ApplyDamage prefix: was vanilla going to return at its first line (dead, teleport, cutscene)?
//   ApplyDamage postfix: no = AddStaggerDamage(S) after vanilla, also when landed damage was 0.1 or less (vanilla
//                       return before stagger there). Staggered by this bash = lock time stored (a creature already
//                       staggering, by someone else, ignore the new stagger: no lock). Landed 0.1 or less = vanilla
//                       skipped m_onDamaged too: me call it once (MonsterAI alert + target the bearer; a bash never
//                       set up a free sneak attack).
//   RPC_Damage finalizer: stored hit put back as before the call (not landed = no stagger; nested calls safe; also
//                       when RPC_Damage or another patch throw).
// Lock time per creature in a weak table (freed with it, lost when ownership move: at most one extra stagger).
// Players (PvP) keep vanilla stagger.
internal static class BashStagger
{
    // Stored bash hit of the RPC_Damage call running now.
    internal struct Scope
    {
        internal HitData Hit;
        internal Character Victim;
        internal float Stagger;
    }

    private sealed class LockTime
    {
        internal float Time;
    }

    private static readonly ConditionalWeakTable<Character, LockTime> Locks = new ConditionalWeakTable<Character, LockTime>();
    private static Scope _scope;

    // Hot path: ApplyDamage prefix compare against it.
    internal static HitData StoredHit => _scope.Hit;

    internal static Scope SaveScope() => _scope;

    internal static void RestoreScope(Scope scope) => _scope = scope;

    // Cheap test first (enum, enum, floats). Caller checked the victim is owned here. Multiplier bounds keep other
    // Blocking-skill hits out (vanilla tower block-charge counter x1000, a plain x1 shield hit of another mod), so
    // BashStagger minimum is 2, never 1.
    internal static bool IsBashHit(HitData hit) =>
        hit.m_hitType == HitData.HitType.PlayerHit
        && hit.m_skill == Skills.SkillType.Blocking
        && hit.m_staggerMultiplier > 1f
        && hit.m_staggerMultiplier < 100f;

    // RPC_Damage prefix: victim a non-player owned here, hit passed IsBashHit.
    internal static void OnIncoming(Character victim, HitData hit, TowerRules rules)
    {
        if (!(hit.GetAttacker() is Player))
        {
            return;
        }
        var lockSeconds = rules.BashStaggerLock;
        if (lockSeconds > 0f && Locks.TryGetValue(victim, out var locked) && Time.time - locked.Time < lockSeconds)
        {
            hit.m_staggerMultiplier = 1f;
            return;
        }
        var ngFactor = 1f;
        if (Game.m_worldLevel > 0 && Game.instance != null)
        {
            ngFactor = Game.m_worldLevel * Game.instance.m_worldLevelEnemyHPMultiplier;
        }
        _scope.Hit = hit;
        _scope.Victim = victim;
        _scope.Stagger = hit.m_damage.GetTotalStaggerDamage() * hit.m_staggerMultiplier * ngFactor;
        hit.m_staggerMultiplier = 0f;
    }

    // ApplyDamage prefix, hit is the stored one: true = vanilla pass its first-line return.
    internal static bool WillApply(Character victim, HitData hit)
    {
        if (!ReferenceEquals(victim, _scope.Victim))
        {
            return false;
        }
        return !(victim.IsDebugFlying() || victim.IsDead() || victim.IsTeleporting() || victim.InCutscene()
                 || CinematicsManager.IsPlaying());
    }

    // ApplyDamage postfix, WillApply said yes. Stored hit used once.
    internal static void AfterApply(Character victim, HitData hit)
    {
        var stagger = _scope.Stagger;
        _scope = default;
        // AddStaggerDamage say true whenever the bar reach the threshold, but RPC_Stagger ignore a creature already
        // staggering: then this bash staggered nothing and start no lock. Animator state is read before the new
        // stagger trigger (it change at the next animator update).
        var already = victim.IsStaggering();
        if (victim.AddStaggerDamage(stagger, hit.m_dir, hit) && !already)
        {
            Locks.GetOrCreateValue(victim).Time = Time.time;
        }
        var landed = hit.GetTotalDamage();
        if (landed <= 0.1f && victim.m_onDamaged != null)
        {
            victim.m_onDamaged(landed, hit.GetAttacker());
        }
    }

    internal static void Reset()
    {
        _scope = default;
    }

#if DEBUG
    // Self tests only: when a bash last started the lock on this creature (false = never).
    internal static bool DebugLockTime(Character victim, out float time)
    {
        time = 0f;
        if (victim == null || !Locks.TryGetValue(victim, out var locked))
        {
            return false;
        }
        time = locked.Time;
        return true;
    }
#endif
}
