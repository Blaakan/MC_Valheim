using UnityEngine;

namespace MC.Combat.CreaturesMoraleMod;

// Me = fighting back after a hit or a near miss (design 2.6), on the creature's owner, and the sneak-attack rule (E3).
// Provocation record = per player deadline in the creature ZDO (CreatureKeys): survives ownership changes, every game
// can read it. Vanilla attacker flags are NOT used (zero-damage hits set them, never cleared, keyed by name).
internal static class Provocation
{
    // P's entry = now + ProvokedSeconds, and the fear end at once (it turn to fight). Skipped: mod off or rules pending,
    // not owner, exempt, P has no standing (hostile anyway), creature in no home biome list (never afraid: record
    // useless) and not routed or shaken now; the ZDO write only when P's entry is older than 1 s. A routed follower is
    // afraid of everyone until its shaken time end, listed or not: its record must work then, else a hit never make it
    // fight back.
    internal static bool Provoke(MonsterAI ai, CreatureState state, Player player, MoraleRules rules, long now)
    {
        if (!Plugin.Live || ServerRules.Pending || player == null)
        {
            return false;
        }
        var nview = ai.m_nview;
        if (nview == null || !nview.IsValid() || !nview.IsOwner())
        {
            return false;
        }
        state.EnsureFacts(rules);
        if (state.Exempt || StandingCache.Get(player) == null)
        {
            return false;
        }
        var zdo = nview.GetZDO();
        if (!state.HasRank && CreatureKeys.GetRoutUntil(zdo) + Attitudes.SecondsToTicks(rules.ShakenSeconds) <= now)
        {
            return false;
        }
        Fear.Stop(ai, state, false);
        return CreatureKeys.Provoke(zdo, player.GetPlayerID(), now, Attitudes.SecondsToTicks(rules.ProvokedSeconds));
    }

    // Character.RPC_Damage prefix, victim's owner, before vanilla resolve the hit. First E3 (attitude BEFORE this hit),
    // then provocation. Me never alert here: vanilla check the alert for this hit's sneak attack after the prefix.
    internal static void OnHit(Character victim, HitData hit)
    {
        if (!Plugin.Live || ServerRules.Pending || hit == null)
        {
            return;
        }
        var ai = victim.m_baseAI as MonsterAI;
        if (ai == null)
        {
            return;
        }
        var nview = victim.m_nview;
        if (nview == null || !nview.IsValid() || !nview.IsOwner() || victim.IsDead() || victim.GetHealth() <= 0f)
        {
            return;
        }
        var player = hit.GetAttacker() as Player;
        if (player == null)
        {
            return;
        }
        var rules = ServerRules.Current;
        var state = CreatureState.Get(ai);
        state.EnsureFacts(rules);
        if (state.Exempt)
        {
            return;
        }

        // E3: afraid creature that watch you walk up is aware of you, not surprised (vanilla Passive Mobs rule,
        // Character.RPC_Damage). One running from you is alerted: vanilla already give no sneak attack. One that cannot
        // see you (behind it, smoke, far while you sneak) still take the sneak attack.
        if (hit.m_backstabBonus > 1f && !ai.IsAlerted() && Attitudes.Judge(ai, player) == Attitude.Afraid
            && ai.CanSeeTarget(player))
        {
            hit.m_backstabBonus = 1f;
        }

        // Any damage type before resistances (fire, poison, spirit too; blocked and armor-absorbed hits too).
        if (hit.GetTotalDamage() <= 0f)
        {
            return;
        }
        // Record also in a Passive Mobs world: a shaken follower (afraid of all, rout still on there) must fight back.
        Provoke(ai, state, player, rules, Attitudes.Now());
        // Vanilla-like (MonsterAI.SetTarget: only with no target). Vanilla do it for damaging hits in OnDamaged; this
        // add it for fire- or poison-only hits and absorbed hits, which never reach OnDamaged with their attacker.
        // Passive Mobs world: who a hit make a creature target is vanilla's own (design 2.8), me add nothing.
        if (!Attitudes.PassiveMobs())
        {
            ai.SetTarget(player);
        }
    }

    // MonsterAI.RPC_OnNearProjectileHit prefix (owner). True = let vanilla run (alert, target shooter).
    internal static bool OnNearMiss(MonsterAI ai, Vector3 center, ZDOID attackerId)
    {
        if (!Plugin.Live || ServerRules.Pending || attackerId.IsNone())
        {
            return true;
        }
        var nview = ai.m_nview;
        if (nview == null || !nview.IsValid() || !nview.IsOwner() || Attitudes.PassiveMobs())
        {
            return true; // vanilla do nothing then anyway
        }
        var scene = ZNetScene.instance;
        var go = scene != null ? scene.FindInstance(attackerId) : null;
        var player = go != null ? go.GetComponent<Character>() as Player : null;
        if (player == null)
        {
            return true;
        }
        var rules = ServerRules.Current;
        var state = CreatureState.Get(ai);
        state.EnsureFacts(rules);
        if (state.Exempt)
        {
            return true;
        }
        var range = rules.NearMissRange;
        if (range > 0f && (center - ai.transform.position).sqrMagnitude <= range * range)
        {
            Provoke(ai, state, player, rules, Attitudes.Now());
            return true;
        }
        // Farther: an afraid creature ignores the impact (vanilla alert every enemy within the weapon's hit noise).
        return Attitudes.Judge(ai, player) != Attitude.Afraid;
    }
}
