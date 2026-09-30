using System.Collections.Generic;
using UnityEngine;

namespace MC.Combat.CreaturesMoraleMod;

// Me = per-creature memory on this game (design 7.4), by instance id. Owner part (check timer, hunt stand-down, rout
// end, fear, cornered time, flee alert) reset when this game gain the creature (OnGained). Cached facts (prefab, home
// and spawn level, rank, token, exemption) read again at most once per second, or at once when rules object change;
// any game use them (sneak attacks, sneak XP). Spawn level computed once (spawn point never move): no biome lookup per
// tick. Me drop destroyed creatures every 30 s; all gone at world end and when turned off.
internal sealed class CreatureState
{
    private const float PurgeInterval = 30f;

    private static readonly Dictionary<int, CreatureState> States = new Dictionary<int, CreatureState>();
    private static readonly List<int> Dead = new List<int>();
    private static float _nextPurge;

    internal readonly MonsterAI Ai;

    // ----- cached facts (EnsureFacts) -----
    private MoraleRules _factsRules;
    private float _factsUntil = -1f;
    private bool _tokenKnown;
    internal int PrefabHash;
    internal bool HasRank;
    internal int Rank;
    internal int HomeLevel;
    internal int SpawnLevel = Biomes.Unknown;   // biome level where it spawned (0 = none: home decide), once
    internal bool Elite;
    internal int TokenHash;
    internal bool Exempt;
    internal bool NightHunter;

    // ----- owner memory (reset by OnGained) -----
    internal float LastOwnedTime = -1000f;
    internal float NextCheck;
    internal bool JustGained;
    internal bool HuntStandDown;
    internal float RoutEnd;              // Time.time deadline of the rout frames (0 = none)
    internal Vector3 RoutFrom;
    internal bool FirstFleeFrame;        // reset flee + path timers on the next rout or fear frame
    internal bool FramesBroken;          // a rout or fear frame threw: no more such frames until unloaded (kept across gains)

    // Vanilla alert (red icon on every player's plate) is held by my flee frames: they raised it, or kept the one it had
    // and cleared its target. Calm (FleeFrames.Calm) take it back only then (design 2.5 piece 2, 2.7).
    internal bool FleeAlert;

    // Fear (design 2.5 piece 2): player it run from (null = not running), when it last sensed them.
    internal Player FearPlayer;
    internal float FearSensedAt;

    // Cornered (2.6): time the fear player stayed within CorneredRange in a row (fear frames count it).
    internal float CorneredTime;

    private CreatureState(MonsterAI ai)
    {
        Ai = ai;
    }

    // Hot path: one dictionary lookup (purge check = one float compare).
    internal static CreatureState Get(MonsterAI ai)
    {
        var now = Time.time;
        if (now >= _nextPurge)
        {
            _nextPurge = now + PurgeInterval;
            Purge();
        }
        var id = ai.GetInstanceID();
        if (!States.TryGetValue(id, out var state))
        {
            state = new CreatureState(ai);
            States[id] = state;
        }
        return state;
    }

    // No create (HuntPlayer postfix: only hunters with a state matter).
    internal static bool TryGet(MonsterAI ai, out CreatureState state) => States.TryGetValue(ai.GetInstanceID(), out state);

    internal void EnsureFacts(MoraleRules rules)
    {
        if (Time.time < _factsUntil && ReferenceEquals(rules, _factsRules))
        {
            return;
        }
        RefreshFacts(rules);
    }

    // Design 2.3, 2.8. Hunt and raid flags read from the ZDO (MonsterAI.HuntPlayer is stale on non-owners and depends on
    // this game's time of day and event state). Spawn level: once, when a WorldGenerator exist.
    internal void RefreshFacts(MoraleRules rules)
    {
        _factsRules = rules;
        _factsUntil = Time.time + 1f;
        var nview = Ai.m_nview;
        var character = Ai.m_character;
        if (nview == null || !nview.IsValid() || character == null)
        {
            Exempt = true;
            HasRank = false;
            NightHunter = false;
            return;
        }
        var zdo = nview.GetZDO();
        PrefabHash = zdo.GetPrefab();
        if (SpawnLevel == Biomes.Unknown)
        {
            SpawnLevel = Biomes.SpawnLevel(Ai);
        }
        HasRank = rules.TryGetHomeLevel(PrefabHash, out HomeLevel);
        Elite = HasRank && rules.IsElite(PrefabHash);
        Rank = HasRank ? rules.RankFor(HomeLevel, SpawnLevel > 0 ? SpawnLevel : 0, Elite) : 0;
        if (!_tokenKnown)
        {
            TokenHash = string.IsNullOrEmpty(character.m_name) ? 0 : character.m_name.GetStableHashCode();
            _tokenKnown = true;
        }
        var eventCreature = zdo.GetBool(ZDOVars.s_eventCreature);
        NightHunter = zdo.GetBool(ZDOVars.s_huntPlayer) && !eventCreature && !Ai.m_enableHuntPlayer;
        var faction = character.GetFaction();
        Exempt = character.IsTamed()
                 || character.IsBoss()
                 || faction == Character.Faction.Boss
                 || faction == Character.Faction.TrainingDummy
                 || faction == Character.Faction.Dverger
                 || Ai.m_enableHuntPlayer
                 || eventCreature
                 || (NightHunter && !rules.NightHuntersCanBeAfraid)
                 || BossFights.IsNear(Ai.transform.position);
    }

    // Force the next EnsureFacts to read again (self tests after SetLevel, tame, ...).
    internal void ForgetFacts()
    {
        _factsUntil = -1f;
    }

#if DEBUG
    // Self test: spawn point moved on purpose (m_spawnPoint + ZDO): read the spawn level again at next refresh.
    internal void ForgetSpawnLevel()
    {
        SpawnLevel = Biomes.Unknown;
        _factsUntil = -1f;
    }
#endif

    // This game (re)gained the creature: memory of an earlier time is stale (7.3). Spawn level stays (cannot change).
    internal void OnGained(float now)
    {
        CorneredTime = 0f;
        FearPlayer = null;
        HuntStandDown = false;
        FirstFleeFrame = true;
        FleeAlert = false;
        RoutEnd = 0f;
        NextCheck = now;
        JustGained = true;
    }

    private static void Purge()
    {
        Dead.Clear();
        foreach (var pair in States)
        {
            if (pair.Value.Ai == null)
            {
                Dead.Add(pair.Key);
            }
        }
        foreach (var id in Dead)
        {
            States.Remove(id);
        }
        Dead.Clear();
        StandingCache.Purge();
    }

    // World end or turned off.
    internal static void Clear()
    {
        States.Clear();
    }
}
