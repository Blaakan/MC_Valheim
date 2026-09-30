using MC.Shared;

namespace MC.Combat.CreaturesMoraleMod;

// Me = cornered (design 2.6, decision 30): while an afraid creature RUN from player P (fear frames), P stay within
// CorneredRange of it for CorneredSeconds in a row -> provoked by P, target P, alerted: it turn and strike.
// Two cases, one rule: it cannot get away (no path, wall, pen, water behind), or P keep up with it.
// Needs the fear: a creature that never sensed P never run, so a sneaking unseen player, a player in smoke or a player
// next to it while it eat is not cornering it; a creature that walk up to a standing player run as soon as it sense
// them, and the player who do not follow never corner it. Time counted every fear frame (one squared distance).
internal static class Cornered
{
    // Pure. One frame: player within range = time grows by dt, else back to 0. True = long enough, strike.
    // Range 0 = off.
    internal static bool Advance(ref float time, float distanceSqr, float dt, float range, float seconds)
    {
        if (range <= 0f || distanceSqr > range * range)
        {
            time = 0f;
            return false;
        }
        time += dt;
        return time >= seconds;
    }

    // Provoked by P (end the fear), then vanilla-like: target P when it has none, alerted (it strikes).
    internal static void Strike(MonsterAI ai, CreatureState state, Player player, MoraleRules rules)
    {
        state.CorneredTime = 0f;
        Provocation.Provoke(ai, state, player, rules, Attitudes.Now());
        Fear.Stop(ai, state, false);
        ai.SetTarget(player);
        ai.SetAlerted(true);
        Log.Debug($"Cornered: {ai.m_character.m_name} fights {player.GetPlayerName()} (it could not get away).");
    }
}
