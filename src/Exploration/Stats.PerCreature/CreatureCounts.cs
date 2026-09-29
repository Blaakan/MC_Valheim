using System;
using System.Collections.Generic;
using MC.Shared;

namespace MC.Exploration.StatsPerCreatureMod;

/// <summary>
/// Me = public read door for kill and tame numbers of the local character. MC Encyclopedia read same tames through documented key.
/// Creature key everywhere = <c>Character.m_name</c> (token like <c>"$enemy_greyling"</c>; variants and star levels
/// share it). Kills = vanilla numbers, lifetime slot of the profile (every world, since Call to Arms update), me only
/// read them. Tames = me count them, from <see cref="CountingSince"/>, only while mod on.
/// Main thread only. Me never throw. What each member need:
/// - kill members (GetKills, GetKillBreakdown, kill half of GetCreatureNames) need only game + profile: 0 or empty on
///   main menu;
/// - tame members (GetTames, CountingSince, tame half of GetCreatureNames) also need local character: 0 / null /
///   empty on main menu, while world load (before first spawn) and in respawn wait after death;
/// - <see cref="IsAvailable"/> = both ready.
/// Work when feature off too (only read). Names, meaning and stored format never change; new members may come
/// (then <see cref="ApiVersion"/> go up).
/// </summary>
public static class CreatureCounts
{
    /// <summary>
    /// Me go up only when new members added. Old members never change. Me property, not const: const get baked into
    /// the other mod when it build, so it never see the installed number.
    /// </summary>
    public static int ApiVersion => 1;

    /// <summary>
    /// <c>Player.m_customData</c> key of the tame counts. Format version 1: lines split by <c>\n</c>; line 1 =
    /// <c>1</c>; each other line = count (plain digits), one TAB, creature name (rest of line).
    /// </summary>
    public const string TamesDataKey = ModInfo.Guid + ".Tames";

    /// <summary><c>Player.m_customData</c> key of the day tame counting started: <c>yyyy-MM-dd</c>, invariant culture.</summary>
    public const string CountingSinceDataKey = ModInfo.Guid + ".CountingSince";

    // Lifetime slot (DifficultyRequirement.RawStats). Other slots = achievement copies, never sum them.
    private const int LifetimeSlot = 0;

    /// <summary>
    /// True = every member ready: in world AND local character spawned. False on main menu, while world load (before
    /// first spawn) and in respawn wait after death (game destroy dead character until new one spawn). While false,
    /// kills may still read right, but GetTames = 0, CountingSince = null, GetCreatureNames miss tame-only names.
    /// Caller cache data? Read again when me turn true (after load, after each respawn).
    /// </summary>
    public static bool IsAvailable
    {
        get
        {
            try
            {
                // Unity == on local player, never ?. on it.
                return GetEnemyStats() != null && Player.m_localPlayer != null;
            }
            catch (Exception e)
            {
                PatchGuard.Report($"{nameof(CreatureCounts)}.{nameof(IsAvailable)}", e);
                return false;
            }
        }
    }

    /// <summary>All-time kills of this creature, every weapon type (vanilla total).</summary>
    public static int GetKills(string creatureName) => GetKills(creatureName, KillModifiers.MixedAndTotal);

    /// <summary>
    /// Kills of this creature for one vanilla bucket. <c>MixedAndTotal</c> = total of every kill;
    /// <c>Unarmed</c>/<c>Magic</c>/<c>Ranged</c>/<c>Melee</c> = kills made with only that weapon family;
    /// <c>CountNone</c> = 0.
    /// </summary>
    public static int GetKills(string creatureName, KillModifiers modifier)
    {
        try
        {
            if (creatureName == null || modifier < KillModifiers.MixedAndTotal || modifier >= KillModifiers.CountNone)
            {
                return 0;
            }
            return ReadKills(GetEnemyStats(), creatureName, (int)modifier);
        }
        catch (Exception e)
        {
            PatchGuard.Report($"{nameof(CreatureCounts)}.{nameof(GetKills)}", e);
            return 0;
        }
    }

    /// <summary>
    /// All-time kills of this creature split by weapon type. Parts add up to Total: <c>Other</c> = the rest (kills with
    /// several weapon types, with no weapon, after vanilla full-heal reset, or from before the game saved weapon
    /// types = before Deep North update). Never call Other "mixed".
    /// </summary>
    public static KillBreakdown GetKillBreakdown(string creatureName)
    {
        try
        {
            var stats = GetEnemyStats();
            if (creatureName == null || stats == null)
            {
                return default;
            }
            return new KillBreakdown(
                ReadKills(stats, creatureName, (int)KillModifiers.MixedAndTotal),
                ReadKills(stats, creatureName, (int)KillModifiers.Melee),
                ReadKills(stats, creatureName, (int)KillModifiers.Ranged),
                ReadKills(stats, creatureName, (int)KillModifiers.Magic),
                ReadKills(stats, creatureName, (int)KillModifiers.Unarmed));
        }
        catch (Exception e)
        {
            PatchGuard.Report($"{nameof(CreatureCounts)}.{nameof(GetKillBreakdown)}", e);
            return default;
        }
    }

    /// <summary>
    /// Tames of this creature counted by this mod while it was on, since <see cref="CountingSince"/>. Need local
    /// character: 0 when none now (main menu, world loading, respawn wait). See <see cref="IsAvailable"/>.
    /// </summary>
    public static int GetTames(string creatureName)
    {
        try
        {
            var player = Player.m_localPlayer;
            if (creatureName == null || player == null)
            {
                return 0;
            }
            return CounterStore.GetTames(player, creatureName);
        }
        catch (Exception e)
        {
            PatchGuard.Report($"{nameof(CreatureCounts)}.{nameof(GetTames)}", e);
            return 0;
        }
    }

    /// <summary>
    /// Day (local date) this mod started counting tames for this character. Null = not started (mod never ran for this
    /// character), or no local character now (main menu, world loading, respawn wait). See <see cref="IsAvailable"/>.
    /// </summary>
    public static DateTime? CountingSince
    {
        get
        {
            try
            {
                var player = Player.m_localPlayer;
                return player == null ? null : CounterStore.GetCountingSince(player);
            }
            catch (Exception e)
            {
                PatchGuard.Report($"{nameof(CreatureCounts)}.{nameof(CountingSince)}", e);
                return null;
            }
        }
    }

    /// <summary>
    /// New list, caller own it: every name with all-time kills >= 1 or tames >= 1. No order. Names without '$'
    /// (player names from old PvP saves, modded creatures without token) come too. No local character now (world
    /// loading, respawn wait) = tame-only names missing. See <see cref="IsAvailable"/>.
    /// </summary>
    public static List<string> GetCreatureNames()
    {
        var names = new List<string>();
        try
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var stats = GetEnemyStats();
            if (stats != null && stats.Length > 0 && stats[0] != null)
            {
                foreach (var pair in stats[0])
                {
                    if (pair.Key != null && ToCount(pair.Value) >= 1 && seen.Add(pair.Key))
                    {
                        names.Add(pair.Key);
                    }
                }
            }

            var player = Player.m_localPlayer;
            if (player != null)
            {
                foreach (var pair in CounterStore.GetAllTames(player))
                {
                    if (pair.Value >= 1 && seen.Add(pair.Key))
                    {
                        names.Add(pair.Key);
                    }
                }
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report($"{nameof(CreatureCounts)}.{nameof(GetCreatureNames)}", e);
        }
        return names;
    }

    /// <summary>
    /// Kills of one creature by weapon type. Other = Total minus the four types, never below 0.
    /// </summary>
    public readonly struct KillBreakdown
    {
        /// <summary>Every kill (vanilla total).</summary>
        public readonly int Total;

        /// <summary>Kills with only melee weapons (swords, knives, clubs, polearms, spears, axes, pickaxes...).</summary>
        public readonly int Melee;

        /// <summary>Kills with only bows or crossbows.</summary>
        public readonly int Ranged;

        /// <summary>Kills with only magic (elemental or blood magic).</summary>
        public readonly int Magic;

        /// <summary>Kills with bare fists only.</summary>
        public readonly int Unarmed;

        /// <summary>Rest: several weapon types, no weapon, full-heal reset, or before game saved weapon types.</summary>
        public readonly int Other;

        internal KillBreakdown(int total, int melee, int ranged, int magic, int unarmed)
        {
            Total = total;
            Melee = melee;
            Ranged = ranged;
            Magic = magic;
            Unarmed = unarmed;
            var rest = (long)total - melee - ranged - magic - unarmed;
            Other = rest > 0 ? (int)rest : 0;
        }
    }

    // Lifetime kill dictionaries (index = KillModifiers). Null = no game or no profile.
    private static Dictionary<string, float>[] GetEnemyStats()
    {
        var game = Game.instance;
        if (game == null)
        {
            return null;
        }
        var profile = game.GetPlayerProfile();
        var slots = profile?.m_playerStats;
        if (slots == null || slots.Length <= LifetimeSlot || slots[LifetimeSlot] == null)
        {
            return null;
        }
        return slots[LifetimeSlot].m_enemyStats;
    }

    private static int ReadKills(Dictionary<string, float>[] stats, string name, int bucket)
    {
        if (stats == null || bucket < 0 || bucket >= stats.Length)
        {
            return 0;
        }
        var dict = stats[bucket];
        return dict != null && dict.TryGetValue(name, out var value) ? ToCount(value) : 0;
    }

    // Vanilla store float. Me round to whole number; junk (negative, NaN) = 0.
    private static int ToCount(float value)
    {
        if (!(value > 0f))
        {
            return 0;
        }
        if (value >= int.MaxValue)
        {
            return int.MaxValue;
        }
        return (int)(value + 0.5f);
    }
}
