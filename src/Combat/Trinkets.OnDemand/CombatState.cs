using UnityEngine;

namespace MC.Combat.TrinketsOnDemandMod;

// Me = "is the local player in a fight" for the income (G2). Two timestamps (Time.time):
//   exchange: local player hit a real foe (Character.Damage on attacker's game), a real foe hit the local player
//             (Character.RPC_Damage on the victim's game, blocked or not), or a perfect dodge (Player.RPC_HitWhileDodging).
//   targeted: a monster that is alerted targets the local player (Player.RPC_OnTargeted with alerted, sent about every
//             0.5 s by its owner's game while it keeps the target).
// In a fight = last exchange within CombatLingerSeconds, or (still targeted by an alerted monster and last exchange
// within EngagedWindowSeconds): kiting and ranged approach keep the income while the fight goes on, a caged or stuck
// mob alone pays at most EngagedWindowSeconds after the last real hit.
// Real foe = enemy of the player (BaseAI.IsEnemy), not a player (PvP out), not tamed, not dead, and not an AI skip
// target (m_aiSkipTarget: training dummy, ShadowPerson, root tentacle). Not Faction.TrainingDummy: FrostWisp and
// Frysling have that faction too (docs/game/combat.md).
internal static class CombatState
{
    // Alerted targeting counts this long (Player.IsTargeted uses 1 s; the RPC comes every 0.5 s).
    internal const float TargetedWindowSeconds = 1.5f;

    // Targeting alone keeps a fight going only this long after the last real exchange.
    internal const float EngagedWindowSeconds = 20f;

    private static float _lastExchange = float.NegativeInfinity;
    private static float _lastTargeted = float.NegativeInfinity;

    internal static float LastExchange => _lastExchange;
    internal static float LastTargeted => _lastTargeted;

    internal static void Reset()
    {
        _lastExchange = float.NegativeInfinity;
        _lastTargeted = float.NegativeInfinity;
    }

    internal static void MarkExchange() => _lastExchange = Time.time;

#if DEBUG
    // Self test put the fight in the past (or now) without real hits.
    internal static void SetForTest(float lastExchange, float lastTargeted)
    {
        _lastExchange = lastExchange;
        _lastTargeted = lastTargeted;
    }
#endif

    internal static void MarkTargeted() => _lastTargeted = Time.time;

    internal static bool InCombat(float linger) => InCombat(Time.time, _lastExchange, _lastTargeted, linger);

    // Pure (self test hammer it).
    internal static bool InCombat(float now, float lastExchange, float lastTargeted, float linger)
    {
        var sinceExchange = now - lastExchange;
        if (sinceExchange <= linger)
        {
            return true;
        }
        return now - lastTargeted <= TargetedWindowSeconds && sinceExchange <= EngagedWindowSeconds;
    }

    // Real foe of the local player (see header). Cheap tests first.
    internal static bool IsRealFoe(Player local, Character c)
    {
        if (c == null || local == null || ReferenceEquals(c, local))
        {
            return false;
        }
        if (c.m_aiSkipTarget || c.IsPlayer() || c.IsDead() || c.IsTamed())
        {
            return false;
        }
        return BaseAI.IsEnemy(local, c);
    }
}
