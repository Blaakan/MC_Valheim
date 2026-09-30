using UnityEngine;

namespace MC.Combat.CreaturesMoraleMod;

// What one creature do about one player (design 2.1). Shaken (E2) = Afraid of every player with a standing.
internal enum Attitude : byte
{
    Hostile,   // vanilla (default, and everything me do not cover)
    Afraid,    // never pick that player as target; run from them when close and sensed, drop them if targeted (G1)
    Provoked,  // vanilla fight: that player hit it, shot next to it or cornered it (G3)
    Routed,    // run from its dead leader, ignore everything (G4), toward everyone
}

// Everything Decide need, gathered by Judge (or made up by self tests).
internal struct AttitudeFacts
{
    internal bool Active;         // mod on on this game
    internal bool Pending;        // client still wait for server rules
    internal bool Exempt;         // boss, tame, raid, dummy, Dvergr, boss fight... (2.8)
    internal long Now;            // server clock ticks
    internal long RoutUntil;      // creature ZDO, 0 = never routed
    internal long ShakenTicks;    // ShakenSeconds in ticks
    internal bool HasStanding;    // player published a standing (mod on there)
    internal bool Provoked;       // live provocation by this player (creature ZDO)
    internal bool PassiveMobs;    // world modifier
    internal bool HasRank;        // creature prefab in a home biome list
    internal int Rank;            // home or spawn biome level + bosses ahead (+ elite), design 2.3
    internal int Level;           // 1 = no star
    internal int BossRank;        // player's
    internal int KillBonus;       // player's, toward this creature's name token
    internal int StarRank;        // rule
    internal int MaxBossesSkipped; // rule
}

// Me = the decision (design 2.4): pure Decide, and Judge that read the inputs for a live creature and player. Every
// game get the same answer from the same data (ZDO keys, published standing, rules), except the boss-fight exemption
// (depend on loaded bosses) and a client still waiting for the server rules (everyone hostile).
internal static class Attitudes
{
    // Order matter (design 2.4).
    internal static Attitude Decide(in AttitudeFacts f)
    {
        if (!f.Active || f.Pending)
        {
            return Attitude.Hostile;
        }
        if (f.Exempt)
        {
            return Attitude.Hostile;
        }
        if (f.Now < f.RoutUntil)
        {
            return Attitude.Routed;
        }
        if (!f.HasStanding)
        {
            return Attitude.Hostile;
        }
        if (f.Provoked)
        {
            return Attitude.Provoked;
        }
        if (f.RoutUntil > 0L && f.Now < f.RoutUntil + f.ShakenTicks)
        {
            return Attitude.Afraid;
        }
        if (f.PassiveMobs)
        {
            return Attitude.Hostile;
        }
        if (!f.HasRank)
        {
            return Attitude.Hostile;
        }
        return Outclasses(f.BossRank, f.KillBonus, f.Rank, f.Level, f.StarRank, f.MaxBossesSkipped)
            ? Attitude.Afraid
            : Attitude.Hostile;
    }

    // Standing >= nerve, and kills bring a kind at most maxSkip bosses early (design 2.3).
    internal static bool Outclasses(int bossRank, int killBonus, int rank, int level, int starRank, int maxSkip)
    {
        var nerve = rank + (Mathf.Max(level, 1) - 1) * starRank;
        return bossRank + killBonus >= nerve && bossRank >= rank - maxSkip;
    }

    // Live creature vs live player, any game. Cheap: cached creature facts, one ZDO long, a standing lookup, and an
    // in-place scan of the provokers array (only with a standing). No allocation.
    // Inputs read in Decide's order; an input Decide will not reach is not read (same answer, less work).
    internal static Attitude Judge(MonsterAI ai, Player player)
    {
        var net = ZNet.instance;
        var nview = ai != null ? ai.m_nview : null;
        var f = new AttitudeFacts
        {
            Active = Plugin.Live && net != null && player != null && nview != null && nview.IsValid(),
            Pending = ServerRules.Pending,
        };
        if (!f.Active || f.Pending)
        {
            return Decide(in f);
        }
        var rules = ServerRules.Current;
        var state = CreatureState.Get(ai);
        state.EnsureFacts(rules);
        f.Exempt = state.Exempt;
        if (f.Exempt)
        {
            return Decide(in f);
        }
        var zdo = nview.GetZDO();
        var standing = StandingCache.Get(player);
        f.Now = net.GetTime().Ticks;
        f.RoutUntil = CreatureKeys.GetRoutUntil(zdo);
        f.ShakenTicks = SecondsToTicks(rules.ShakenSeconds);
        f.HasStanding = standing != null;
        f.PassiveMobs = PassiveMobs();
        f.HasRank = state.HasRank;
        f.Rank = state.Rank;
        f.Level = ai.m_character != null ? ai.m_character.GetLevel() : 1;
        f.StarRank = rules.StarRank;
        f.MaxBossesSkipped = rules.MaxBossesSkippedByKills;
        if (standing != null)
        {
            f.BossRank = standing.BossRank;
            f.KillBonus = standing.BonusFor(state.TokenHash);
            f.Provoked = CreatureKeys.IsProvokedBy(zdo, player.GetPlayerID(), f.Now);
        }
        return Decide(in f);
    }

    internal static bool PassiveMobs()
    {
        var zone = ZoneSystem.instance;
        return zone != null && zone.GetGlobalKey(GlobalKeys.PassiveMobs);
    }

    internal static long Now()
    {
        var net = ZNet.instance;
        return net != null ? net.GetTime().Ticks : 0L;
    }

    internal static long SecondsToTicks(float seconds) => (long)(seconds * System.TimeSpan.TicksPerSecond);
}
