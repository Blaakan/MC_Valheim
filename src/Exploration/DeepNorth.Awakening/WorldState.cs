using System;
using System.Globalization;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.DeepNorthAwakeningMod;

// Me = what this game knows of the world (design 2.1, 2.6). Stone count = valued global key "mc_dn_stones <n>" (only
// the server write it; vanilla send the key list to every player on change). Stage = min(n, 3), awake = stage >= 1.
// Kall defeated = vanilla key defeated_frozenking_p3. Keys read every RefreshSeconds (ZNet.Update postfix), hot paths
// read the cached fields. Seed = world seed (every game know it: terrain is built from it).
internal static class WorldState
{
    internal const string StonesKey = "mc_dn_stones";
    internal const string KallKey = "defeated_frozenking_p3";
    internal const int MaxStage = 3;
    internal const int MaxStones = 100;
    internal const float RefreshSeconds = 0.5f;

    private static int _stones;
    private static bool _hasKey;
    private static bool _kall;
    private static float _nextRefresh;
    private static bool _badLogged;
    private static int _loggedStage = -1;
    private static bool _loggedKall;
    private static WorldGenerator _seedFrom;
    private static int _seed;

#if DEBUG
    // Self test force world state (never the real key). Null = normal.
    internal static int? TestStones { get; set; }
    internal static bool? TestKall { get; set; }
#endif

    // Nature vs Jotun on (stage >= 1, rules not pending, NatureFightsBack). Cached: BaseAI.IsEnemy is hot.
    internal static bool HostilityOn { get; private set; }

    internal static int Stones
    {
        get
        {
#if DEBUG
            if (TestStones.HasValue)
            {
                return TestStones.Value;
            }
#endif
            return _stones;
        }
    }

    internal static bool HasStonesKey => _hasKey;

    internal static bool KallDefeated
    {
        get
        {
#if DEBUG
            if (TestKall.HasValue)
            {
                return TestKall.Value;
            }
#endif
            return _kall;
        }
    }

    internal static int Stage => StageOf(Stones);

    // Pure (self test).
    internal static int StageOf(int stones) => stones <= 0 ? 0 : stones >= MaxStage ? MaxStage : stones;

    // World seed; 0 when no world.
    internal static int Seed
    {
        get
        {
            var wg = WorldGenerator.instance;
            if (wg == null)
            {
                return 0;
            }
            if (!ReferenceEquals(wg, _seedFrom))
            {
                _seedFrom = wg;
                _seed = wg.m_world != null ? wg.m_world.m_seed : 0;
            }
            return _seed;
        }
    }

    // Rules in force and the north awake (stage >= 1): area spawns, storms. Pending client = vanilla.
    internal static bool Awake(AwakeningRules rules) => rules != null && !rules.IsPending && Stage >= 1;

    internal static int CellAt(Vector3 position) => Cells.At(Seed, position.x, position.z);

    internal static bool IsAwakeCell(int cellId, AwakeningRules rules) => Cells.IsAwake(Seed, cellId, rules.Coverage(Stage));

    // Storm now in this cell (no awake check).
    internal static bool IsStormingCell(int cellId, AwakeningRules rules)
    {
        var net = ZNet.instance;
        if (net == null)
        {
            return false;
        }
        rules.StormSeconds(out var min, out var max);
        return Cells.IsStorming(Seed, cellId, net.GetTimeSeconds(), rules.StormShare / 100f, min, max);
    }

    // ZNet.Update postfix: cheap clock check, keys read twice a second.
    internal static void RefreshIfDue()
    {
        var now = Time.unscaledTime;
        if (now < _nextRefresh)
        {
            return;
        }
        _nextRefresh = now + RefreshSeconds;
        Refresh();
    }

    internal static void Refresh()
    {
        var zs = ZoneSystem.instance;
        if (zs == null)
        {
            _hasKey = false;
            _stones = 0;
            _kall = false;
        }
        else
        {
            _hasKey = zs.GetGlobalKey(StonesKey, out var value);
            _stones = _hasKey ? ParseCount(value) : 0;
            _kall = zs.GetGlobalKey(KallKey);
        }
        var rules = ServerRules.Current;
        HostilityOn = Stage >= 1 && rules != null && !rules.IsPending && rules.NatureFightsBack;
        HeldCells.ForgetIfNoKall();
        LogChanges();
    }

    // Pure (self test): key value to a count. Junk = 0 (warned once), clamped to 0..MaxStones.
    internal static int ParseCount(string value)
    {
        if (!int.TryParse((value ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            if (!_badLogged && !string.IsNullOrEmpty(value))
            {
                _badLogged = true;
                Log.Warning($"The global key {StonesKey} holds \"{value}\", which is not a number; the Deep North counts it as 0 broken Malicious Ice.");
            }
            return 0;
        }
        return n < 0 ? 0 : n > MaxStones ? MaxStones : n;
    }

    // Server only (single player, host, dedicated). Key routed to the server, applied at once there, sent to everyone.
    internal static void WriteStones(int count)
    {
        var zs = ZoneSystem.instance;
        if (zs == null)
        {
            return;
        }
        count = count < 0 ? 0 : count > MaxStones ? MaxStones : count;
        zs.SetGlobalKey(StonesKey + " " + count.ToString(CultureInfo.InvariantCulture));
        Refresh();
    }

    // World end, feature off.
    internal static void Clear()
    {
        _stones = 0;
        _hasKey = false;
        _kall = false;
        _nextRefresh = 0f;
        _loggedStage = -1;
        _loggedKall = false;
        HostilityOn = false;
        _seedFrom = null;
    }

    private static void LogChanges()
    {
        if (ZoneSystem.instance == null)
        {
            return;
        }
        var stage = Stage;
        if (stage != _loggedStage)
        {
            if (_loggedStage >= 0 || stage > 0)
            {
                Log.Info(stage == 0
                    ? "The Deep North sleeps (no Malicious Ice broken yet)."
                    : $"The Deep North is awake: stage {stage} ({Stones} Malicious Ice broken).");
            }
            _loggedStage = stage;
        }
        var kall = KallDefeated;
        if (kall != _loggedKall)
        {
            if (kall)
            {
                Log.Info("Kall Fimbulbringer is defeated: the Deep North areas no longer spawn over time; an area that is not cleared spawns its Jotun once each time players enter it.");
            }
            _loggedKall = kall;
        }
    }

    // Short text for the console command and logs.
    internal static string Describe(AwakeningRules rules)
    {
        var stage = Stage;
        var text = $"{Stones} Malicious Ice broken, stage {stage}";
        if (stage > 0 && rules != null && !rules.IsPending)
        {
            CountAwake(rules, out var awake, out var total);
            text += $", {awake} of {total} Deep North areas awake";
        }
        text += KallDefeated ? ", Kall defeated" : ", Kall not defeated";
        if (!HasStonesKey)
        {
            text += " (no count saved in this world yet)";
        }
        return text;
    }

    internal static void CountAwake(AwakeningRules rules, out int awake, out int total)
    {
        awake = 0;
        total = 0;
        var seed = Seed;
        var coverage = rules.Coverage(Stage);
        for (var i = -Cells.WorldSquares; i <= Cells.WorldSquares; i++)
        {
            for (var j = -Cells.WorldSquares; j <= Cells.WorldSquares; j++)
            {
                if (!Cells.IsDeepNorthCell(seed, i, j))
                {
                    continue;
                }
                Cells.SeedPoint(seed, i, j, out var x, out var z);
                if (x * x + z * z > 10500f * 10500f)
                {
                    continue;
                }
                total++;
                if (coverage > 0f && Cells.AwakeValue(seed, i, j) < coverage)
                {
                    awake++;
                }
            }
        }
    }
}
