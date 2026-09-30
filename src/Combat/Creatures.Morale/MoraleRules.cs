using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MC.Combat.CreaturesMoraleMod;

// Me = every gameplay setting of the mod in one snapshot (design 4.4, 5.2): nine home biome lists, bosses ahead,
// elites, star and kill rules, fear range, night hunters, provoke and corner numbers, packs, rout numbers. Server send
// its own to every player (ServerRules): same creature rules for everybody. Personal setting (progress messages) and
// AllowPlayersWithoutMod not in here.
// Me also hold the parsed tables (prefab hash -> home level, elite set, leader hash -> followers, kill steps). Names
// become name.GetStableHashCode() = ZDO prefab hash, so no ZNetScene needed: me build at plugin start, before any
// world. Unknown names wait for the check with ZNetScene (PrefabTokens); me only keep the name list for it.
// Rank of a creature (design 2.3) = max(home level, spawn level) + BossesAhead (+ EliteExtraBosses for elites).
// Snapshot never change after Build: want other numbers = Clone, change, Build, use the new object.
internal sealed class MoraleRules
{
    // Bosses of the ladder (Character.m_bossOrder 1..8, design 1.7). Rank 0 = no boss yet.
    internal const int BossCount = 8;

    // Home biome lists: 8 land biomes (level = their boss) + the Ocean (level 3). Biomes.HomeLevels same order.
    internal const int HomeCount = 9;

    // Ranges (config and wire clamp use the same).
    internal const int MinBossesAhead = 0;
    internal const int MaxBossesAhead = BossCount;
    internal const int MinEliteExtra = 0;
    internal const int MaxEliteExtra = 3;
    internal const int MinStarRank = 0;
    internal const int MaxStarRank = 5;
    internal const int MaxKillSteps = 5;
    internal const int MinKillStep = 1;
    internal const int MaxKillStep = 100000;
    internal const int MinBossesSkipped = 0;
    internal const int MaxBossesSkipped = BossCount;
    internal const float MinFearRange = 3f;   // user rule: no aggressive creature ignores a player (decision 34)
    internal const float MaxFearRange = 40f;
    internal const float MinProvokedSeconds = 5f;
    internal const float MaxProvokedSeconds = 300f;
    internal const float MinNearMissRange = 0f;
    internal const float MaxNearMissRange = 30f;
    internal const float MinCorneredRange = 0f;
    internal const float MaxCorneredRange = 6f;
    internal const float MinCorneredSeconds = 1f;
    internal const float MaxCorneredSeconds = 30f;
    internal const float MinRoutRadius = 5f;
    internal const float MaxRoutRadius = 60f;
    internal const float MinRoutSeconds = 3f;
    internal const float MaxRoutSeconds = 60f;
    internal const float MinShakenSeconds = 0f;
    internal const float MaxShakenSeconds = 600f;

    // Config keys of the home lists, index = Biomes.HomeLevels index.
    internal static readonly string[] HomeKeys =
    {
        "Meadows", "BlackForest", "Swamp", "Mountain", "Plains", "Mistlands", "Ashlands", "DeepNorth", "Ocean",
    };

    // Biome names for players (config text). Same index as HomeKeys.
    internal static readonly string[] HomeNames =
    {
        "the Meadows", "the Black Forest", "the Swamp", "the Mountains", "the Plains", "the Mistlands", "the Ashlands",
        "the Deep North", "the Ocean",
    };

    // Boss names for players (config text, messages), index = boss order - 1.
    internal static readonly string[] BossNames =
    {
        "Eikthyr", "The Elder", "Bonemass", "Moder", "Yagluth", "The Queen", "Fader", "Kall Fimbulbringer",
    };

    // Default home lists (design 2.3), every name checked in the 1.0.16 dump, all MonsterAI. Same 79 creatures as the
    // first version's eight per-boss lists.
    internal static readonly string[] DefaultHomeLists =
    {
        "Boar, Neck, Greyling, Hen, Skeleton_Meadows, Skeleton_Meadows_noarcher",
        "Greydwarf, Greydwarf_Shaman, Greydwarf_Elite, Troll, Troll_sleeping, Skeleton, Skeleton_NoArcher, Ghost, "
        + "Ghost_sleeping, Bjorn, Bjorn_sleeping",
        "Draugr, Draugr_sleeping, Draugr_Ranged, Draugr_Ranged_sleeping, Draugr_Elite, Draugr_Elite_sleeping, "
        + "Skeleton_Swamps, Skeleton_Swamps_noarcher, Skeleton_Poison, Blob, BlobElite, Leech, Leech_cave, Surtling, "
        + "Bat_Swamp, Wraith, Writhan, Abomination",
        "Wolf, Ulv, Hatchling, Bat, BlobFrost, Skeleton_Mountains, Skeleton_Mountains_noarcher, Fenring, "
        + "Fenring_Cultist, StoneGolem",
        "Goblin, GoblinArcher, GoblinShaman, GoblinBrute, Deathsquito, BlobTar, Lox, Unbjorn",
        "Seeker, SeekerBrood, SeekerBrute, Tick, Gjall",
        "Charred_Melee, Charred_Archer, Charred_Twitcher, Charred_Mage, Asksvin, Asksvin_hatchling, Volture, BlobLava, "
        + "Morgen, Morgen_NonSleeping, FallenValkyrie, BonemawSerpent",
        "Greydwarf_Frozen, Greydwarf_Shaman_Frozen, Skeleton_DeepNorth, BlobMork, BlobMorkMini, Elaking, ElakingLantern, "
        + "Moose",
        "Serpent",
    };

    // Default elites (design 2.3): every default pack leader + the big creatures of each biome.
    internal const string DefaultElites =
        "Greydwarf_Shaman, Greydwarf_Elite, Troll, Troll_sleeping, Bjorn, Bjorn_sleeping, Draugr_Elite, "
        + "Draugr_Elite_sleeping, BlobElite, Wraith, Writhan, Abomination, Fenring_Cultist, StoneGolem, GoblinShaman, "
        + "GoblinBrute, Unbjorn, SeekerBrute, Gjall, Charred_Mage, Morgen, Morgen_NonSleeping, FallenValkyrie, "
        + "Greydwarf_Shaman_Frozen";

    internal const string DefaultKillSteps = "100, 400";

    // Default packs (design 2.7, E4). First pack = user example (Greydwarf Brute = Greydwarf_Elite).
    internal const string DefaultPacks =
        "Troll, Troll_sleeping, Greydwarf_Elite, Greydwarf_Shaman > Greydwarf, Greyling; "
        + "Greydwarf_Shaman_Frozen > Greydwarf_Frozen; "
        + "GoblinBrute, GoblinShaman > Goblin, GoblinArcher; "
        + "Draugr_Elite, Draugr_Elite_sleeping > Draugr, Draugr_sleeping, Draugr_Ranged, Draugr_Ranged_sleeping; "
        + "Fenring_Cultist > Fenring, Ulv; "
        + "SeekerBrute > Seeker, SeekerBrood; "
        + "Charred_Mage > Charred_Melee, Charred_Archer, Charred_Twitcher";

    // Settings as the user wrote them (wire carry these).
    internal string[] HomeLists = new string[HomeCount];
    internal int BossesAhead = 2;
    internal string Elites = DefaultElites;
    internal int EliteExtraBosses = 1;
    internal int StarRank = 1;
    internal string KillSteps = DefaultKillSteps;
    internal int MaxBossesSkippedByKills = 1;
    internal bool NightHuntersCanBeAfraid = true;
    internal float FearRange = 12f;
    internal float ProvokedSeconds = 30f;
    internal float NearMissRange = 4f;
    internal float CorneredRange = 3f;
    internal float CorneredSeconds = 2f;
    internal string Packs = DefaultPacks;
    internal float RoutRadius = 25f;
    internal float RoutSeconds = 15f;
    internal float ShakenSeconds = 60f;

    // Parsed tables (Build). Hot path read them: lookups only, no allocation.
    private Dictionary<int, int> _homeLevelByPrefab = new Dictionary<int, int>();
    private HashSet<int> _elites = new HashSet<int>();
    private Dictionary<int, HashSet<int>> _followersByLeader = new Dictionary<int, HashSet<int>>();
    private int[] _killSteps = new int[0];

    // For the name check with ZNetScene (PrefabTokens) and the log: every distinct name, names in two home lists,
    // elites in no home list, and what me could not read (bad numbers, pack without '>'; null = nothing wrong).
    internal List<string> Names { get; private set; } = new List<string>();
    internal List<string> NamesInSeveralHomeLists { get; private set; } = new List<string>();
    internal List<string> ElitesWithoutHome { get; private set; } = new List<string>();
    internal string ParseProblems { get; private set; }

    internal MoraleRules()
    {
        Array.Copy(DefaultHomeLists, HomeLists, HomeCount);
    }

    internal int[] KillStepValues => _killSteps;
    internal int RankedPrefabCount => _homeLevelByPrefab.Count;
    internal int EliteCount => _elites.Count;
    internal int LeaderCount => _followersByLeader.Count;

    // This game's own config.
    internal static MoraleRules Own()
    {
        var r = new MoraleRules();
        for (var i = 0; i < HomeCount; i++)
        {
            r.HomeLists[i] = Plugin.HomeLists[i].Value ?? "";
        }
        r.BossesAhead = Plugin.BossesAhead.Value;
        r.Elites = Plugin.Elites.Value ?? "";
        r.EliteExtraBosses = Plugin.EliteExtraBosses.Value;
        r.StarRank = Plugin.StarRank.Value;
        r.KillSteps = Plugin.KillSteps.Value ?? "";
        r.MaxBossesSkippedByKills = Plugin.MaxBossesSkippedByKills.Value;
        r.NightHuntersCanBeAfraid = Plugin.NightHuntersCanBeAfraid.Value;
        r.FearRange = Plugin.FearRange.Value;
        r.ProvokedSeconds = Plugin.ProvokedSeconds.Value;
        r.NearMissRange = Plugin.NearMissRange.Value;
        r.CorneredRange = Plugin.CorneredRange.Value;
        r.CorneredSeconds = Plugin.CorneredSeconds.Value;
        r.Packs = Plugin.Packs.Value ?? "";
        r.RoutRadius = Plugin.RoutRadius.Value;
        r.RoutSeconds = Plugin.RoutSeconds.Value;
        r.ShakenSeconds = Plugin.ShakenSeconds.Value;
        r.Build();
        return r;
    }

    // Self test copy: change numbers on the copy, then Build. Never touch a snapshot already in use.
    internal MoraleRules Clone()
    {
        var r = (MoraleRules)MemberwiseClone();
        r.HomeLists = (string[])HomeLists.Clone();
        r.Build();
        return r;
    }

    // Home biome level of a creature (1..8) by ZDO prefab hash. False = in no home list = never afraid.
    internal bool TryGetHomeLevel(int prefabHash, out int level) => _homeLevelByPrefab.TryGetValue(prefabHash, out level);

    internal bool IsElite(int prefabHash) => _elites.Contains(prefabHash);

    // Rank (design 2.3): the boss rank a player need before a 0-star creature of this prefab, spawned where its spawn
    // level say (0 = no spawn level: home only), is afraid of them. False = in no home list.
    internal bool TryGetRank(int prefabHash, int spawnLevel, out int rank)
    {
        if (!_homeLevelByPrefab.TryGetValue(prefabHash, out var home))
        {
            rank = 0;
            return false;
        }
        rank = RankFor(home, spawnLevel, _elites.Contains(prefabHash));
        return true;
    }

    // Pure: harder of home and spawn biome, plus bosses ahead, plus the elite bonus.
    internal int RankFor(int homeLevel, int spawnLevel, bool elite) =>
        Math.Max(homeLevel, spawnLevel) + BossesAhead + (elite ? EliteExtraBosses : 0);

    internal bool IsLeader(int prefabHash) => _followersByLeader.ContainsKey(prefabHash);

    internal bool IsFollower(int leaderHash, int followerHash) =>
        _followersByLeader.TryGetValue(leaderHash, out var followers) && followers.Contains(followerHash);

    // Kill bonus for a lifetime kill count: number of kill steps reached (0.. MaxKillSteps).
    internal int KillBonus(int kills)
    {
        var bonus = 0;
        for (var i = 0; i < _killSteps.Length && kills >= _killSteps[i]; i++)
        {
            bonus++;
        }
        return bonus;
    }

    // Text -> tables. Fresh tables every time (old snapshot may still be read by someone).
    internal void Build()
    {
        var problems = new StringBuilder();
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var homes = new Dictionary<int, int>();
        var listOf = new Dictionary<string, int>(StringComparer.Ordinal);
        var several = new List<string>();

        void Note(string name)
        {
            if (seen.Add(name))
            {
                names.Add(name);
            }
        }

        for (var i = 0; i < HomeCount; i++)
        {
            var level = Biomes.HomeLevels[i];
            foreach (var name in SplitNames(HomeLists[i]))
            {
                Note(name);
                if (listOf.TryGetValue(name, out var other))
                {
                    // Same name in two lists: easiest (lowest level) win (design 5.1). Same list twice = no warning.
                    if (other != i && !several.Contains(name))
                    {
                        several.Add(name);
                    }
                    var hash = name.GetStableHashCode();
                    if (level < homes[hash])
                    {
                        homes[hash] = level;
                    }
                    continue;
                }
                listOf[name] = i;
                homes[name.GetStableHashCode()] = level;
            }
        }

        var elites = new HashSet<int>();
        var eliteOrphans = new List<string>();
        foreach (var name in SplitNames(Elites))
        {
            Note(name);
            elites.Add(name.GetStableHashCode());
            if (!listOf.ContainsKey(name) && !eliteOrphans.Contains(name))
            {
                eliteOrphans.Add(name);
            }
        }

        var followers = new Dictionary<int, HashSet<int>>();
        var packs = (Packs ?? "").Split(';');
        foreach (var rawPack in packs)
        {
            var pack = rawPack.Trim();
            if (pack.Length == 0)
            {
                continue;
            }
            var parts = pack.Split('>');
            if (parts.Length != 2)
            {
                Add(problems, $"pack \"{pack}\" needs exactly one '>' between leaders and followers (ignored)");
                continue;
            }
            var leaderNames = SplitNames(parts[0]);
            var followerNames = SplitNames(parts[1]);
            if (leaderNames.Count == 0 || followerNames.Count == 0)
            {
                Add(problems, $"pack \"{pack}\" needs at least one leader and one follower (ignored)");
                continue;
            }
            foreach (var name in leaderNames)
            {
                Note(name);
            }
            foreach (var name in followerNames)
            {
                Note(name);
            }
            foreach (var leader in leaderNames)
            {
                var key = leader.GetStableHashCode();
                if (!followers.TryGetValue(key, out var set))
                {
                    set = new HashSet<int>();
                    followers[key] = set;
                }
                foreach (var follower in followerNames)
                {
                    set.Add(follower.GetStableHashCode());
                }
            }
        }

        _killSteps = ParseKillSteps(KillSteps, problems);
        _homeLevelByPrefab = homes;
        _elites = elites;
        _followersByLeader = followers;
        Names = names;
        NamesInSeveralHomeLists = several;
        ElitesWithoutHome = eliteOrphans;
        ParseProblems = problems.Length > 0 ? problems.ToString() : null;
    }

    // "100, 400" -> [100, 400]. Bad word or out of range = problem (word skipped or number pulled in), sorted, no
    // double, at most MaxKillSteps.
    internal static int[] ParseKillSteps(string text, StringBuilder problems)
    {
        var values = new List<int>();
        foreach (var word in (text ?? "").Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(word, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                Add(problems, $"kill step \"{word}\" is not a whole number (ignored)");
                continue;
            }
            if (value < MinKillStep || value > MaxKillStep)
            {
                var pulled = value < MinKillStep ? MinKillStep : MaxKillStep;
                Add(problems, $"kill step {value} is outside {MinKillStep}-{MaxKillStep} (used {pulled})");
                value = pulled;
            }
            if (!values.Contains(value))
            {
                values.Add(value);
            }
        }
        values.Sort();
        if (values.Count > MaxKillSteps)
        {
            Add(problems, $"only the first {MaxKillSteps} kill steps are used");
            values.RemoveRange(MaxKillSteps, values.Count - MaxKillSteps);
        }
        return values.ToArray();
    }

    // "a, b ,c" -> [a, b, c]. Spaces around names dropped, empty skipped.
    internal static List<string> SplitNames(string text)
    {
        var list = new List<string>();
        foreach (var raw in (text ?? "").Split(','))
        {
            var name = raw.Trim();
            if (name.Length > 0)
            {
                list.Add(name);
            }
        }
        return list;
    }

    private static void Add(StringBuilder problems, string text)
    {
        if (problems == null)
        {
            return;
        }
        if (problems.Length > 0)
        {
            problems.Append("; ");
        }
        problems.Append(text);
    }

    // Wire: layout, then every value in fixed order (design 5.2). Layout 2 since the home biome rework (2026-09-30):
    // layout 1 (eight per-boss lists) refused. Layout bump = ModNetworkVersion bump too (layout 2 = network version 2).
    internal const int Layout = 2;

    internal void Write(ZPackage pkg)
    {
        pkg.Write(Layout);
        for (var i = 0; i < HomeCount; i++)
        {
            pkg.Write(HomeLists[i] ?? "");
        }
        pkg.Write(BossesAhead);
        pkg.Write(Elites ?? "");
        pkg.Write(EliteExtraBosses);
        pkg.Write(StarRank);
        pkg.Write(KillSteps ?? "");
        pkg.Write(MaxBossesSkippedByKills);
        pkg.Write(NightHuntersCanBeAfraid);
        pkg.Write(FearRange);
        pkg.Write(ProvokedSeconds);
        pkg.Write(NearMissRange);
        pkg.Write(CorneredRange);
        pkg.Write(CorneredSeconds);
        pkg.Write(Packs ?? "");
        pkg.Write(RoutRadius);
        pkg.Write(RoutSeconds);
        pkg.Write(ShakenSeconds);
    }

    // Never trust the wire: unknown layout, broken package or bytes left over = false (caller keep what it had);
    // numbers outside the config ranges pulled in (clamped = true). Lists parsed here, by the receiver.
    internal static bool TryRead(ZPackage pkg, out MoraleRules rules, out bool clamped)
    {
        rules = null;
        clamped = false;
        try
        {
            if (pkg == null || pkg.ReadInt() != Layout)
            {
                return false;
            }
            var r = new MoraleRules();
            for (var i = 0; i < HomeCount; i++)
            {
                r.HomeLists[i] = pkg.ReadString() ?? "";
            }
            r.BossesAhead = Clamp(pkg.ReadInt(), MinBossesAhead, MaxBossesAhead, ref clamped);
            r.Elites = pkg.ReadString() ?? "";
            r.EliteExtraBosses = Clamp(pkg.ReadInt(), MinEliteExtra, MaxEliteExtra, ref clamped);
            r.StarRank = Clamp(pkg.ReadInt(), MinStarRank, MaxStarRank, ref clamped);
            r.KillSteps = pkg.ReadString() ?? "";
            r.MaxBossesSkippedByKills = Clamp(pkg.ReadInt(), MinBossesSkipped, MaxBossesSkipped, ref clamped);
            r.NightHuntersCanBeAfraid = pkg.ReadBool();
            r.FearRange = Clamp(pkg.ReadSingle(), MinFearRange, MaxFearRange, ref clamped);
            r.ProvokedSeconds = Clamp(pkg.ReadSingle(), MinProvokedSeconds, MaxProvokedSeconds, ref clamped);
            r.NearMissRange = Clamp(pkg.ReadSingle(), MinNearMissRange, MaxNearMissRange, ref clamped);
            r.CorneredRange = Clamp(pkg.ReadSingle(), MinCorneredRange, MaxCorneredRange, ref clamped);
            r.CorneredSeconds = Clamp(pkg.ReadSingle(), MinCorneredSeconds, MaxCorneredSeconds, ref clamped);
            r.Packs = pkg.ReadString() ?? "";
            r.RoutRadius = Clamp(pkg.ReadSingle(), MinRoutRadius, MaxRoutRadius, ref clamped);
            r.RoutSeconds = Clamp(pkg.ReadSingle(), MinRoutSeconds, MaxRoutSeconds, ref clamped);
            r.ShakenSeconds = Clamp(pkg.ReadSingle(), MinShakenSeconds, MaxShakenSeconds, ref clamped);
            if (pkg.GetPos() != pkg.Size())
            {
                return false; // bytes after the last field = not this layout after all
            }
            r.Build();
            rules = r;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // One log line. Lists are long: only say default or custom and how many.
    internal string Describe()
    {
        var defaultHomes = true;
        for (var i = 0; i < HomeCount; i++)
        {
            defaultHomes &= HomeLists[i] == DefaultHomeLists[i];
        }
        var sb = new StringBuilder();
        sb.Append(defaultHomes ? "default" : "custom").Append(" home biomes (").Append(_homeLevelByPrefab.Count)
            .Append(" creatures, ").Append(Elites == DefaultElites ? "default" : "custom").Append(' ')
            .Append(_elites.Count).Append(" elites), afraid ").Append(BossesAhead)
            .Append(" boss(es) past their biome's boss, elites +").Append(EliteExtraBosses).Append(", stars +")
            .Append(StarRank).Append(" boss each, kill steps ");
        if (_killSteps.Length == 0)
        {
            sb.Append("off");
        }
        else
        {
            for (var i = 0; i < _killSteps.Length; i++)
            {
                sb.Append(i == 0 ? "" : "/").Append(_killSteps[i]);
            }
            sb.Append(" (at most ").Append(MaxBossesSkippedByKills).Append(" boss early)");
        }
        sb.Append(", fear range ").Append(F(FearRange)).Append(" m, night hunters ")
            .Append(NightHuntersCanBeAfraid ? "follow the rules" : "hunt everyone")
            .Append(", provoked ").Append(F(ProvokedSeconds)).Append(" s, near miss ").Append(F(NearMissRange))
            .Append(" m, cornered ").Append(F(CorneredRange)).Append(" m for ").Append(F(CorneredSeconds))
            .Append(" s, ").Append(Packs == DefaultPacks ? "default" : "custom").Append(" packs (")
            .Append(_followersByLeader.Count).Append(" leaders), rout ").Append(F(RoutRadius)).Append(" m for ")
            .Append(F(RoutSeconds)).Append(" s, shaken ").Append(F(ShakenSeconds)).Append(" s");
        return sb.ToString();
    }

    private static string F(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static int Clamp(int value, int min, int max, ref bool clamped)
    {
        var c = value < min ? min : value > max ? max : value;
        clamped |= c != value;
        return c;
    }

    // NaN or infinity from the wire = min (never let a NaN reach a timer or a distance).
    private static float Clamp(float value, float min, float max, ref bool clamped)
    {
        float c;
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            c = float.IsPositiveInfinity(value) ? max : min;
            clamped = true;
            return c;
        }
        c = value < min ? min : value > max ? max : value;
        clamped |= c != value;
        return c;
    }
}
