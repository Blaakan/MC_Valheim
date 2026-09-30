using System;
using System.Collections.Generic;
using System.Text;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.CreaturesMoraleMod;

// Me = one player's decoded standing: boss rank + the kill bonuses that can still change something. Never change
// after made (cache and every game share the object).
internal sealed class StandingData
{
    internal readonly int BossRank;
    internal readonly int[] Tokens;
    internal readonly byte[] Bonuses;

    internal StandingData(int bossRank, int[] tokens, byte[] bonuses)
    {
        BossRank = bossRank;
        Tokens = tokens ?? new int[0];
        Bonuses = bonuses ?? new byte[0];
    }

    internal int Count => Tokens.Length;

    // Kill bonus toward one name token (0 = none listed). At most 16 entries: plain loop, no allocation.
    internal int BonusFor(int tokenHash)
    {
        for (var i = 0; i < Tokens.Length; i++)
        {
            if (Tokens[i] == tokenHash)
            {
                return Bonuses[i];
            }
        }
        return 0;
    }
}

// Me = player standing (design 2.2): boss rank (highest boss this character helped kill, from the vanilla lifetime kill
// table) + kill bonus per kind of creature (kill steps reached), published on the player's OWN ZDO so every game can
// judge creatures against it (vanilla crown-mode pattern). Nothing saved by me: the vanilla profile is the source.
// Wire (player ZDO <guid>.Standing), layout 1:
//   byte layout = 1, byte bossRank (0..8; 255 = no standing, mod turned off), byte n (0..16),
//   n x { int tokenHash (m_name.GetStableHashCode()), byte bonus 1..5 }        -> 3 + 5n bytes, at most 83
// Player ZDO is resent on every move: only bonuses that can still change an outcome are listed (filter in Compute).
// Written only when bytes differ from the ones on the ZDO. Publish: spawn, credited kill, rules change, activation.
// Never while off, while the server rules are pending, or without a local player.
internal static class Standing
{
    internal const byte Layout = 1;
    internal const byte NoRank = 255;
    internal const int MaxListed = 16;

    // One kind of creature with a kill bonus (all ranked tokens, for messages; Listed = on the wire).
    internal struct Entry
    {
        internal string Token;
        internal int Hash;
        internal int Bonus;
        internal bool Listed;
    }

    internal sealed class Computed
    {
        internal int BossRank;
        internal bool Forced;
        internal StandingData Published;
        internal readonly List<Entry> Bonuses = new List<Entry>();
        internal bool Capped;
    }

#if DEBUG
    // Self test: this rank and these kills instead of the profile's (fed into the normal publish path). Null = real.
    internal sealed class Override
    {
        internal readonly int BossRank;
        internal readonly Dictionary<string, float> Kills = new Dictionary<string, float>();

        internal Override(int bossRank)
        {
            BossRank = bossRank;
        }
    }

    private static Override _testOverride;

    internal static Override TestOverride
    {
        get => _testOverride;
        set
        {
            _testOverride = value;
            PublishNow();
        }
    }
#endif

#if DEBUG
    // Self test: progress messages on or off whatever ShowProgressMessages say (tests never write config). Null = setting.
    internal static bool? TestMessages { get; set; }

    // Self test: like a credited kill (Game.RPC_RegisterKill postfix) that bring this override: messages as after a kill.
    internal static void TestKill(Override next)
    {
        _testOverride = next;
        Publish(true);
    }

    // Self test: Time.time the boss rank message is due (-1 = none waiting).
    internal static float RankMessageAt => _rankMessageAt;
#endif

    internal const string RankUpText = "Weaker creatures now keep out of your way";

    // E5. Vanilla boss death message (BaseAI.OnDeath -> MessageAll Center) come in the same Character.OnDeath call,
    // right after the kill credit, and a Center message replace the text at once (no queue), fading in 4 s. Me show
    // mine after that fade, else nobody ever see it.
    internal const float RankMessageDelay = 4.5f;

    private static bool _quitting;
    private static bool _capLogged;
    private static string _lastLogged;

    // Message baseline (E5): last published rank and bonuses of this session's character.
    private static int _lastRank = -1;
    private static readonly Dictionary<int, int> LastBonuses = new Dictionary<int, int>();
    private static float _rankMessageAt = -1f;

    // Boss rank message waiting (ZNet.Update postfix poll it: one float compare).
    internal static bool MessagePending => _rankMessageAt >= 0f;

    static Standing()
    {
        Application.quitting += () => _quitting = true;
    }

    // ---------- pure ----------

    // Standing from a kill table. forcedRank >= 0 replace the boss rank (Debug ForceBossRank, self test override).
    internal static Computed Compute(IDictionary<string, float> kills, IDictionary<string, int> bossOrders,
        MoraleRules rules, IList<PrefabTokens.RankedToken> ranked, int forcedRank)
    {
        var result = new Computed
        {
            BossRank = forcedRank >= 0 ? Mathf.Min(forcedRank, MoraleRules.BossCount) : BossLadder.RankOf(kills, bossOrders),
            Forced = forcedRank >= 0,
        };
        var listed = new List<Entry>();
        if (kills != null && ranked != null)
        {
            foreach (var token in ranked)
            {
                if (!kills.TryGetValue(token.Token, out var count))
                {
                    continue;
                }
                var bonus = rules.KillBonus(count >= int.MaxValue ? int.MaxValue : (int)count);
                if (bonus <= 0)
                {
                    continue;
                }
                var entry = new Entry
                {
                    Token = token.Token,
                    Hash = token.Hash,
                    Bonus = Mathf.Min(bonus, MoraleRules.MaxKillSteps),
                    Listed = token.CanMatter(result.BossRank, rules.StarRank, rules.MaxBossesSkippedByKills),
                };
                if (entry.Listed)
                {
                    listed.Add(entry);
                }
                result.Bonuses.Add(entry);
            }
        }
        // Highest bonus first, then by hash: same table = same bytes (no needless rewrite).
        listed.Sort((a, b) => a.Bonus != b.Bonus ? b.Bonus.CompareTo(a.Bonus) : a.Hash.CompareTo(b.Hash));
        if (listed.Count > MaxListed)
        {
            result.Capped = true;
            listed.RemoveRange(MaxListed, listed.Count - MaxListed);
        }
        var tokens = new int[listed.Count];
        var bonuses = new byte[listed.Count];
        for (var i = 0; i < listed.Count; i++)
        {
            tokens[i] = listed[i].Hash;
            bonuses[i] = (byte)listed[i].Bonus;
        }
        // Cap may drop some: Listed flag follow the wire.
        for (var i = 0; i < result.Bonuses.Count; i++)
        {
            var e = result.Bonuses[i];
            if (e.Listed && Array.IndexOf(tokens, e.Hash) < 0)
            {
                e.Listed = false;
                result.Bonuses[i] = e;
            }
        }
        result.Published = new StandingData(result.BossRank, tokens, bonuses);
        return result;
    }

    internal static byte[] Encode(StandingData data)
    {
        var n = data.Count;
        var bytes = new byte[3 + 5 * n];
        bytes[0] = Layout;
        bytes[1] = (byte)Mathf.Clamp(data.BossRank, 0, MoraleRules.BossCount);
        bytes[2] = (byte)n;
        for (var i = 0; i < n; i++)
        {
            var at = 3 + 5 * i;
            CreatureKeys.WriteInt(bytes, at, data.Tokens[i]);
            bytes[at + 4] = data.Bonuses[i];
        }
        return bytes;
    }

    // "No standing" (turned off). Fresh array (ZDO compare by reference).
    internal static byte[] WithdrawnBytes() => new[] { Layout, NoRank, (byte)0 };

    // False = unreadable (caller treat as no standing, log once). True with null = withdrawn (no standing).
    internal static bool TryDecode(byte[] bytes, out StandingData standing)
    {
        standing = null;
        if (bytes == null || bytes.Length < 3 || bytes[0] != Layout)
        {
            return false;
        }
        var rank = bytes[1];
        var n = bytes[2];
        if (rank == NoRank)
        {
            return bytes.Length == 3 && n == 0;
        }
        if (rank > MoraleRules.BossCount || n > MaxListed || bytes.Length != 3 + 5 * n)
        {
            return false;
        }
        var tokens = new int[n];
        var bonuses = new byte[n];
        for (var i = 0; i < n; i++)
        {
            var at = 3 + 5 * i;
            tokens[i] = CreatureKeys.ReadInt(bytes, at);
            bonuses[i] = bytes[at + 4];
            if (bonuses[i] < 1 || bonuses[i] > MoraleRules.MaxKillSteps)
            {
                return false;
            }
        }
        standing = new StandingData(rank, tokens, bonuses);
        return true;
    }

    // ---------- publish ----------

    internal static void PublishNow() => Publish(false);

    // Game.RPC_RegisterKill postfix: vanilla already counted the kill. Progress messages only here.
    internal static void PublishAfterKill() => Publish(true);

    private static void Publish(bool afterKill)
    {
        if (!Plugin.Live || ServerRules.Pending)
        {
            return;
        }
        var player = Player.m_localPlayer;
        if (player == null)
        {
            return;
        }
        var nview = player.m_nview;
        var game = Game.instance;
        if (nview == null || !nview.IsValid() || game == null)
        {
            return;
        }
        var profile = game.GetPlayerProfile();
        var rules = ServerRules.Current;
        var orders = BossLadder.Get();
        var ranked = PrefabTokens.Get(rules);
        if (profile == null || rules == null || orders == null || ranked == null)
        {
            return;
        }
        var kills = LifetimeKills(profile);
        var forced = -1;
#if DEBUG
        if (_testOverride != null)
        {
            kills = _testOverride.Kills;
            forced = _testOverride.BossRank;
        }
        else if (Plugin.ForceBossRank != null && Plugin.ForceBossRank.Value >= 0)
        {
            forced = Plugin.ForceBossRank.Value;
        }
#endif
        var computed = Compute(kills, orders, rules, ranked, forced);
        if (computed.Capped && !_capLogged)
        {
            _capLogged = true;
            Log.Info($"More than {MaxListed} kinds of creature have a kill bonus that matters; only the {MaxListed} "
                     + "highest are shared with the other players' games.");
        }
        var bytes = Encode(computed.Published);
        var zdo = nview.GetZDO();
        if (!CreatureKeys.SameBytes(bytes, zdo.GetByteArray(CreatureKeys.Standing)))
        {
            zdo.Set(CreatureKeys.Standing, bytes);
            StandingCache.Invalidate(player);
        }
        LogIfChanged(computed);
        if (afterKill && _lastRank >= 0 && ShowMessages())
        {
            ShowProgress(computed, rules, ranked);
        }
        _lastRank = computed.BossRank;
        LastBonuses.Clear();
        foreach (var e in computed.Bonuses)
        {
            LastBonuses[e.Hash] = e.Bonus;
        }
    }

    // Character's lifetime kills per name token (slot 0, cheated or not, every world: the game's own table).
    // Self test morale.publish read it too.
    internal static Dictionary<string, float> LifetimeKills(PlayerProfile profile)
    {
        var stats = profile.m_playerStats;
        if (stats == null || stats.Length == 0 || stats[0] == null || stats[0].m_enemyStats == null
            || stats[0].m_enemyStats.Length == 0)
        {
            return null;
        }
        return stats[0].m_enemyStats[0];
    }

    private static bool ShowMessages()
    {
#if DEBUG
        if (TestMessages.HasValue)
        {
            return TestMessages.Value;
        }
#endif
        return Plugin.ShowProgressMessages != null && Plugin.ShowProgressMessages.Value;
    }

    // E5. Boss rank up = one center message, RankMessageDelay later (UpdateMessage), only when the new rank make some
    // kind afraid at home without kills (two bosses ahead: nothing is afraid of a rank 1 or 2 player with the defaults);
    // else each listed kind whose bonus went up = corner message at once (corner messages have a queue).
    private static void ShowProgress(Computed computed, MoraleRules rules, IList<PrefabTokens.RankedToken> ranked)
    {
        if (computed.BossRank > _lastRank)
        {
            if (computed.BossRank >= LowestRank(ranked))
            {
                _rankMessageAt = Time.time + RankMessageDelay;
            }
            return;
        }
        var hud = MessageHud.instance;
        if (hud == null)
        {
            return;
        }
        var steps = rules.KillStepValues;
        foreach (var e in computed.Bonuses)
        {
            LastBonuses.TryGetValue(e.Hash, out var before);
            if (!e.Listed || e.Bonus <= before || e.Bonus > steps.Length)
            {
                continue;
            }
            hud.ShowMessage(MessageHud.MessageType.TopLeft,
                $"{CreatureName(e.Token)}: {steps[e.Bonus - 1]} kills. They will be afraid of you sooner.");
        }
    }

    // Lowest home rank of the ranked kinds (3 with the defaults: the Meadows). No kind = never.
    internal static int LowestRank(IList<PrefabTokens.RankedToken> ranked)
    {
        var lowest = int.MaxValue;
        if (ranked != null)
        {
            foreach (var token in ranked)
            {
                lowest = Mathf.Min(lowest, token.MinRank);
            }
        }
        return lowest;
    }

    // ZNet.Update postfix while MessagePending. Setting turned off or mod off meanwhile = nothing shown.
    internal static void UpdateMessage()
    {
        if (Time.time < _rankMessageAt)
        {
            return;
        }
        _rankMessageAt = -1f;
        var hud = MessageHud.instance;
        if (Plugin.Live && ShowMessages() && hud != null)
        {
            hud.ShowMessage(MessageHud.MessageType.Center, RankUpText);
        }
    }

    private static string CreatureName(string token)
    {
        var loc = Localization.instance;
        return loc != null ? loc.Localize(token) : token;
    }

    private static void LogIfChanged(Computed computed)
    {
        var sb = new StringBuilder();
        sb.Append("Standing: boss rank ").Append(computed.BossRank);
        if (computed.BossRank > 0)
        {
            sb.Append(" (").Append(MoraleRules.BossNames[computed.BossRank - 1]).Append(')');
        }
        if (computed.Forced)
        {
            sb.Append(", forced for testing");
        }
        sb.Append(", kill bonuses: ");
        var any = false;
        foreach (var e in computed.Bonuses)
        {
            if (!e.Listed)
            {
                continue;
            }
            sb.Append(any ? ", " : "").Append(CreatureName(e.Token)).Append(" +").Append(e.Bonus);
            any = true;
        }
        sb.Append(any ? "." : "none that matter.");
        var text = sb.ToString();
        if (text != _lastLogged)
        {
            _lastLogged = text;
            Log.Info(text);
        }
    }

    // Turned off: other games treat this player as vanilla at once (a removed key never reach them, so overwrite).
    // Guarded: world, ZDOMan, local player ZDO valid; skipped at quit; nothing to do if never published.
    internal static void Withdraw()
    {
        if (_quitting || ZNet.instance == null || ZDOMan.instance == null)
        {
            return;
        }
        var player = Player.m_localPlayer;
        if (player == null)
        {
            return;
        }
        var nview = player.m_nview;
        if (nview == null || !nview.IsValid())
        {
            return;
        }
        var zdo = nview.GetZDO();
        var current = zdo.GetByteArray(CreatureKeys.Standing);
        if (current == null || (current.Length == 3 && current[1] == NoRank))
        {
            return;
        }
        zdo.Set(CreatureKeys.Standing, WithdrawnBytes());
        StandingCache.Invalidate(player);
        _lastLogged = null;
        Log.Info("Standing withdrawn: other players' games treat you as in the normal game.");
    }

    // World end: next character start new baseline.
    internal static void Clear()
    {
        _lastRank = -1;
        LastBonuses.Clear();
        _lastLogged = null;
        _rankMessageAt = -1f;
    }
}
