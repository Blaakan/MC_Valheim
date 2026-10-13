#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.CreaturesMoraleMod;

// Debug build only. The real thing where older tests used a stand-in (TESTING.md items in brackets):
//   morale.boss-kill      real Eikthyr and The Elder, really killed by the player: the game's own kill credit feeds the
//                         boss rank (no forced rank), the boss's own death message, no rank-up message unless the kill
//                         raises the rank far enough, Boars and Necks afraid with BossesAhead = 0 (T04, T37)
//   morale.raid           the real raid army_eikthyr started on the player: the game's own event spawner send the
//                         raid, its creatures attack a rank 8 player (T20)
//   morale.night-hunters  the game's own world spawn entry of the hunting Fulings, run by the game's spawner at night
//                         after Yagluth: the group stand down for a rank 7 player, run when the player is close,
//                         hunts with NightHuntersCanBeAfraid off (T21)
// A boss kill leave marks outside a test stage: me put each one back (kill table and boss counters of the character,
// world and character keys, the dream waiting for the next sleep), and the one write that would leave the throwaway
// saves (the game's "CampaignProgress" preference) is held back while the test kills.
internal static partial class SelfTests
{
    private const string BossKillName = "morale.boss-kill";
    private const string RaidName = "morale.raid";
    private const string NightHuntersName = "morale.night-hunters";
    private const string RaidEvent = "army_eikthyr";
    private const string RaidTutorial = "randomevent";
    private const string HunterPrefab = "Goblin";

    // ================================================================ boss kill marks

    private static bool _holdCampaignProgress;

    // Harmony prefix on PlatformPrefs.SetInt while a boss-kill test runs: Game.RPC_RegisterKill write the highest
    // boss killed to the player's real preferences (not part of the throwaway saves). False = not written.
    private static bool CampaignProgressPrefix(string name) => !_holdCampaignProgress || name != "CampaignProgress";

    // Me = everything a real boss kill changes for good, taken before and put back after.
    private sealed class BossMarks
    {
        private static readonly PlayerStatType[] Counters =
        {
            PlayerStatType.BossKills, PlayerStatType.BossLastHits, PlayerStatType.BossKillSolo, PlayerStatType.BossKillMultiplayer,
        };

        private readonly Player _player;
        private readonly PlayerProfile _profile;
        private readonly List<string> _tokens = new List<string>();
        private readonly List<float?[,]> _kills = new List<float?[,]>();
        private readonly float[,] _counters;
        private readonly List<string> _keys = new List<string>();
        private readonly List<bool> _hadWorldKey = new List<bool>();
        private readonly List<bool> _hadOwnKey = new List<bool>();
        private readonly string _dream;
        private readonly bool _hadBossCount;
        private readonly float _bossCount;
        private Harmony _guard;

        internal BossMarks(Player player, PlayerProfile profile, IEnumerable<string> bossTokens)
        {
            _player = player;
            _profile = profile;
            var stats = profile.m_playerStats;
            foreach (var token in bossTokens)
            {
                _tokens.Add(token);
                var saved = new float?[stats.Length, 5];
                for (var i = 0; i < stats.Length; i++)
                {
                    for (var k = 0; k < 5; k++)
                    {
                        var table = stats[i] != null && stats[i].m_enemyStats != null && k < stats[i].m_enemyStats.Length ? stats[i].m_enemyStats[k] : null;
                        saved[i, k] = table != null && table.TryGetValue(token, out var value) ? value : (float?)null;
                    }
                }
                _kills.Add(saved);
            }
            _counters = new float[stats.Length, Counters.Length];
            for (var i = 0; i < stats.Length; i++)
            {
                for (var k = 0; k < Counters.Length; k++)
                {
                    _counters[i, k] = stats[i] != null && stats[i].m_stats.TryGetValue(Counters[k], out var value) ? value : 0f;
                }
            }
            _dream = CinematicsManager.m_dreamCinematic;
            var zones = ZoneSystem.instance;
            _hadBossCount = zones != null && zones.GetGlobalKey(GlobalKeys.activeBosses, out _bossCount);
        }

        // False = the preference could not be guarded: no boss may die in this test.
        internal bool Guard(out string problem)
        {
            problem = null;
            try
            {
                var target = AccessTools.Method(typeof(PlatformPrefs), nameof(PlatformPrefs.SetInt), new[] { typeof(string), typeof(int) });
                if (target == null)
                {
                    problem = "PlatformPrefs.SetInt not found";
                    return false;
                }
                _guard = new Harmony(ModInfo.Guid + ".selftest.boss");
                _guard.Patch(target, prefix: new HarmonyMethod(typeof(SelfTests), nameof(CampaignProgressPrefix)));
                _holdCampaignProgress = true;
                return true;
            }
            catch (Exception e)
            {
                problem = e.Message;
                return false;
            }
        }

        // World key and character key this boss set when it dies: remembered so they go away again.
        internal void Remember(Character boss)
        {
            var key = boss != null ? boss.m_defeatSetGlobalKey : null;
            if (string.IsNullOrEmpty(key) || _keys.Contains(key))
            {
                return;
            }
            var zones = ZoneSystem.instance;
            _keys.Add(key);
            _hadWorldKey.Add(zones != null && zones.GetGlobalKey(key));
            _hadOwnKey.Add(_player.HaveUniqueKey(key));
        }

        // Kill table of the character as it was for these bosses (a character that have killed none of them: 0 kills).
        internal void RestoreKills()
        {
            var stats = _profile.m_playerStats;
            for (var t = 0; t < _tokens.Count; t++)
            {
                for (var i = 0; i < stats.Length; i++)
                {
                    for (var k = 0; k < 5; k++)
                    {
                        var table = stats[i] != null && stats[i].m_enemyStats != null && k < stats[i].m_enemyStats.Length ? stats[i].m_enemyStats[k] : null;
                        if (table == null)
                        {
                            continue;
                        }
                        var saved = _kills[t][i, k];
                        if (saved.HasValue)
                        {
                            table[_tokens[t]] = saved.Value;
                        }
                        else
                        {
                            table.Remove(_tokens[t]);
                        }
                    }
                }
            }
        }

        internal void Restore()
        {
            Step("kill table", RestoreKills);
            Step("boss counters", () =>
            {
                var stats = _profile.m_playerStats;
                for (var i = 0; i < stats.Length; i++)
                {
                    for (var k = 0; k < Counters.Length; k++)
                    {
                        if (stats[i] != null)
                        {
                            stats[i].m_stats[Counters[k]] = _counters[i, k];
                        }
                    }
                }
            });
            Step("world and character keys", () =>
            {
                var zones = ZoneSystem.instance;
                for (var i = 0; i < _keys.Count; i++)
                {
                    var key = _keys[i];
                    if (!_hadOwnKey[i])
                    {
                        Player.m_addUniqueKeyQueue.RemoveAll(queued => queued == key);
                        if (_player != null && _player.HaveUniqueKey(key))
                        {
                            _player.RemoveUniqueKey(key);
                        }
                    }
                    if (!_hadWorldKey[i] && zones != null && zones.GetGlobalKey(key))
                    {
                        zones.RemoveGlobalKey(key);
                    }
                }
                if (zones == null)
                {
                    return;
                }
                // An alerted boss counts itself in this world key and take itself out when it dies; a boss the stage
                // destroys (test cut short) never does.
                var has = zones.GetGlobalKey(GlobalKeys.activeBosses, out float now);
                if (_hadBossCount && (!has || now != _bossCount))
                {
                    zones.SetGlobalKey(GlobalKeys.activeBosses, _bossCount);
                }
                else if (!_hadBossCount && has)
                {
                    zones.RemoveGlobalKey(GlobalKeys.activeBosses);
                }
            });
            Step("dream", () => CinematicsManager.m_dreamCinematic = _dream ?? "");
            Step("preference guard", () =>
            {
                _holdCampaignProgress = false;
                if (_guard != null)
                {
                    _guard.UnpatchSelf();
                    _guard = null;
                }
            });
        }

        private static void Step(string what, Action step)
        {
            try
            {
                step();
            }
            catch (Exception e)
            {
                Log.Error($"{SelfTest.Prefix} boss test clean-up of {what} failed: {e}");
            }
        }
    }

    private sealed class BossKill
    {
        internal bool Spawned;
        internal bool Credited;
        internal float Took;
        internal string DeathText = "";
        internal bool DeathShown;
        internal int LogMark;
    }

    // A real boss, really killed by the player: Character.Damage -> death -> the game's own kill credit
    // (Game.RPC_RegisterKill, where the mod listens) -> the boss's own death message. No loot, no body.
    private static IEnumerator KillRealBoss(Stage s, Checks c, BossMarks marks, string label, string prefab, Vector3 spot,
        Dictionary<string, float> table, Action eachFrame, BossKill kill)
    {
        var w = new Waiter();
        var boss = s.Spawn(prefab, spot, s.Player.transform.position - spot);
        kill.Spawned = boss != null && boss.m_character != null && boss.m_character.IsBoss() && !string.IsNullOrEmpty(boss.m_character.m_name);
        if (!c.Check(kill.Spawned, $"{label}: could not spawn {prefab} as a boss"))
        {
            yield break;
        }
        var token = boss.m_character.m_name;
        marks.Remember(boss.m_character);
        NoDrops(boss);
        kill.DeathText = !string.IsNullOrEmpty(boss.m_deathMessage) ? Localization.instance.Localize(boss.m_deathMessage) : "";
        yield return Wait(0.5f, eachFrame);
        var before = table.TryGetValue(token, out var n) ? n : 0f;
        kill.LogMark = Watch.Mark();
        // An older message (the last boss's death line, a rank-up message) may still stand in the middle of the
        // screen: emptied, so what stand there after the kill was shown by this kill.
        var hud = MessageHud.instance;
        if (hud != null && hud.m_messageCenterText != null)
        {
            hud.m_messageCenterText.text = "";
        }
        Kill(boss.m_character, s.Player);
        // A boss with a death animation is credited when the animation ends.
        yield return Until(() => table.TryGetValue(token, out var count) && count >= before + 1f, 20f, eachFrame, w);
        kill.Credited = w.Met;
        kill.Took = w.Took;
        c.Check(w.Met, $"{label}: the {prefab} the player killed is credited to the character's kill table within 20 s ({token}: {F(before)} -> "
                       + $"{F(table.TryGetValue(token, out var after) ? after : 0f)}, waited {F(w.Took)} s)");
        // The death message and the kill credit come from the same death call (BaseAI.OnDeath -> MessageAll, shown
        // at once on this game). The middle of the screen was emptied before the kill, so the text there now is new.
        kill.DeathShown = kill.DeathText.Length > 0 && hud != null && hud.m_messageCenterText.text == kill.DeathText;
        yield return null;
    }

    // ================================================================ morale.boss-kill

    private static IEnumerator RunBossKill()
    {
        var c = new Checks(BossKillName);
        Stage s = null;
        BossMarks marks = null;
        var w = new Waiter();
        var env = EnvMan.instance;
        var oldEnv = env != null ? env.m_debugEnv : "";
        var local = Player.m_localPlayer;
        var hadWet = local != null && local.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectWet);
        try
        {
            s = Stage.Begin(BossKillName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            var hud = MessageHud.instance;
            var profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
            var table = profile != null ? Standing.LifetimeKills(profile) : null;
            var ladder = BossLadder.Get();
            if (!c.Check(hud != null && hud.m_messageCenterText != null && !Hud.IsUserHidden() && table != null && ladder != null && Localization.instance != null,
                    "the HUD is hidden or missing, or the profile has no kill table: boss kills and their messages cannot be checked"))
            {
                c.Report();
                yield break;
            }
            if (!c.Check(Plugin.ForceBossRank == null || Plugin.ForceBossRank.Value < 0,
                    "ForceBossRank is set in the config (Debug section): the real boss rank cannot be checked; set it to -1"))
            {
                c.Report();
                yield break;
            }
            marks = new BossMarks(player, profile, ladder.Keys);
            if (!c.Check(marks.Guard(out var problem), $"the game's campaign progress preference could not be guarded ({problem}): no boss is killed"))
            {
                c.Report();
                yield break;
            }
            // A boss near the player start its own weather; clear sky keep the player dry.
            if (env != null)
            {
                env.m_debugEnv = "Clear";
            }
            ResetHooks();
            ServerRules.TestRules = NewRules(null);
            Standing.TestMessages = true;

            // A character that have killed no boss: every boss taken out of its kill table (put back at the end).
            foreach (var boss in ladder.Keys)
            {
                table.Remove(boss);
            }
            Standing.PublishNow();
            var standing = StandingCache.Get(player);
            if (!c.Check(standing != null && standing.BossRank == 0, $"T04 setup: a character with no boss kill has boss rank 0 (rank {(standing != null ? standing.BossRank : -1)})"))
            {
                c.Report();
                yield break;
            }
            var eikSpot = s.Spot(here, s.Forward, 10f, true);
            var boarSpot = s.Spot(here, Turn(s.Forward, 70f), 6f, true);
            var neckSpot = s.Spot(here, Turn(s.Forward, -70f), 6f, true);
            MonsterAI boar = null;
            MonsterAI neck = null;
            Action hold = () =>
            {
                s.Noise();
                if (boar != null)
                {
                    Stage.Place(boar.m_character, boarSpot, player.transform.position - boarSpot);
                }
                if (neck != null)
                {
                    Stage.Place(neck.m_character, neckSpot, player.transform.position - neckSpot);
                }
            };

            // ----- T04, T37: the first Eikthyr kill, default settings -----
            var first = new BossKill();
            yield return KillRealBoss(s, c, marks, "T04", "Eikthyr", eikSpot, table, hold, first);
            if (!first.Credited)
            {
                c.Report();
                yield break;
            }
            standing = StandingCache.Get(player);
            c.Check(standing != null && standing.BossRank == 1, $"T04: after the real Eikthyr kill the published boss rank is 1 (rank {(standing != null ? standing.BossRank : -1)})");
            c.Check(Watch.Count("Standing: boss rank 1 (Eikthyr)", first.LogMark, LogLevel.Info) == 1 && Watch.Count("forced for testing", first.LogMark) == 0,
                $"T04: the log shows \"Standing: boss rank 1 (Eikthyr), ...\" once, not forced (last Standing line: \"{Watch.Last("Standing:", first.LogMark)}\")");
            c.Check(first.DeathShown, $"T04: Eikthyr's own death message is shown (\"{first.DeathText}\"; middle of the screen now: \"{hud.m_messageCenterText.text}\")");
            c.Check(!Standing.MessagePending && Standing.RankMessageAt < 0f,
                "T04 / T37: the first Eikthyr kill with the default settings schedules no \"Weaker creatures now keep out of your way\"");
            boar = SpawnMeadows(s, c, "Boar", boarSpot, here - boarSpot);
            if (boar != null)
            {
                yield return Until(() => boar.m_targetCreature == player, 8f, hold, w);
                c.Check(w.Met && Attitudes.Judge(boar, player) == Attitude.Hostile,
                    $"T04 / T37: boars still attack the rank 1 player (target {Who(boar.m_targetCreature)}, Judge {Attitudes.Judge(boar, player)})");
            }

            // ----- T37: BossesAhead = 0 afterwards: no message, Boars and Necks afraid -----
            ServerRules.TestRules = NewRules(r => r.BossesAhead = 0);
            c.Check(!Standing.MessagePending, "T37: setting BossesAhead = 0 after the kill schedules no rank-up message");
            neck = SpawnMeadows(s, c, "Neck", neckSpot, here - neckSpot);
            if (boar != null && neck != null)
            {
                c.Check(Attitudes.Judge(boar, player) == Attitude.Afraid && Attitudes.Judge(neck, player) == Attitude.Afraid,
                    $"T37: with BossesAhead = 0 Boars and Necks are afraid of the rank 1 player (Boar {Attitudes.Judge(boar, player)}, Neck {Attitudes.Judge(neck, player)})");
                yield return Until(() => FearOf(boar) == player && boar.m_targetCreature == null && FearOf(neck) == player && neck.m_targetCreature == null, 4f, hold, w);
                c.Check(w.Met, $"T37: within 4 s the Boar drops the player and both run (Boar runs from {Who(FearOf(boar))}, target {Who(boar.m_targetCreature)}; "
                               + $"Neck runs from {Who(FearOf(neck))}, target {Who(neck.m_targetCreature)})");
            }
            if (boar != null)
            {
                s.Destroy(boar);
                boar = null;
            }
            if (neck != null)
            {
                s.Destroy(neck);
                neck = null;
            }

            // Seconds went by since the first kill (Boar and Neck steps): the screen's own message log must have no
            // rank-up line after Eikthyr's death message, and none may wait (the flag alone is the mod's word).
            c.Check(!Standing.MessagePending && ShownAfter(Standing.RankUpText, first.DeathText) == 0,
                $"T04 / T37: since the first Eikthyr kill no \"{Standing.RankUpText}\" was shown and none is waiting "
                + $"(shown {ShownAfter(Standing.RankUpText, first.DeathText)} time(s) after its death message, waiting {Standing.MessagePending})");

            // ----- T04, T37: a second kill of the same boss: its death message, no rank-up message -----
            var second = new BossKill();
            yield return KillRealBoss(s, c, marks, "T04", "Eikthyr", eikSpot, table, hold, second);
            if (second.Credited)
            {
                standing = StandingCache.Get(player);
                c.Check(standing != null && standing.BossRank == 1, $"T04: a second Eikthyr kill leaves the boss rank at 1 (rank {(standing != null ? standing.BossRank : -1)})");
                c.Check(second.DeathShown, $"T04: Eikthyr's own death message is shown again (middle of the screen now: \"{hud.m_messageCenterText.text}\")");
                c.Check(!Standing.MessagePending && Standing.RankMessageAt < 0f && ShownAfter(Standing.RankUpText, second.DeathText) <= 0,
                    "T04 / T37: with BossesAhead = 0 the second Eikthyr kill (the rank stays 1) schedules and shows no \"Weaker creatures now keep out of your way\" "
                    + $"(due {Standing.MessagePending}, shown after the death message {ShownAfter(Standing.RankUpText, second.DeathText)} time(s))");
            }

            // ----- T37: another character with no boss kill, BossesAhead = 0 first, then the kill -----
            marks.RestoreKills();
            foreach (var boss in ladder.Keys)
            {
                table.Remove(boss);
            }
            Standing.PublishNow();
            standing = StandingCache.Get(player);
            c.Check(standing != null && standing.BossRank == 0 && !Standing.MessagePending, "T37 setup: a character with no boss kill again (boss rank 0), BossesAhead = 0");
            var third = new BossKill();
            yield return KillRealBoss(s, c, marks, "T37", "Eikthyr", eikSpot, table, hold, third);
            if (third.Credited)
            {
                var due = Standing.RankMessageAt - Time.time;
                c.Check(Standing.MessagePending && due > 0f && due <= Standing.RankMessageDelay + 0.01f,
                    $"T37: with BossesAhead = 0 the kill that raises the boss rank from 0 to 1 schedules the rank-up message (due {Standing.MessagePending}, in {F(due)} s)");
                c.Check(third.DeathShown && ShownAfter(Standing.RankUpText, third.DeathText) == 0 && hud.m_messageCenterText.text != Standing.RankUpText,
                    $"T37: right after the kill the middle of the screen holds Eikthyr's death message, not the rank-up message (now \"{hud.m_messageCenterText.text}\")");
                yield return Until(() => !Standing.MessagePending, Standing.RankMessageDelay + 1.5f, hold, w);
                c.Check(w.Met && ShownAfter(Standing.RankUpText, third.DeathText) == 1 && hud.m_messageCenterText.text == Standing.RankUpText,
                    $"T37: about 4.5 s after the kill, once Eikthyr's death message has faded, the middle of the screen reads \"{Standing.RankUpText}\" "
                    + $"(now \"{hud.m_messageCenterText.text}\", shown {ShownAfter(Standing.RankUpText, third.DeathText)} time(s) after the death message, waited {F(w.Took)} s)");
            }

            // ----- T04 (optional step): The Elder, the next boss, on the rank 1 character -----
            var elderSpot = s.Spot(here, s.Forward, 14f, true);
            var elder = new BossKill();
            yield return KillRealBoss(s, c, marks, "T04", "gd_king", elderSpot, table, hold, elder);
            if (elder.Credited)
            {
                standing = StandingCache.Get(player);
                var due = Standing.RankMessageAt - Time.time;
                c.Check(standing != null && standing.BossRank == 2 && Watch.Count("Standing: boss rank 2 (The Elder)", elder.LogMark, LogLevel.Info) == 1,
                    $"T04: the real kill of The Elder raises the boss rank to 2 and the log shows \"Standing: boss rank 2 (The Elder), ...\" once "
                    + $"(rank {(standing != null ? standing.BossRank : -1)}, last Standing line: \"{Watch.Last("Standing:", elder.LogMark)}\")");
                c.Check(Standing.MessagePending && due > 0f && due <= Standing.RankMessageDelay + 0.01f && elder.DeathShown,
                    $"T04: The Elder's own death message is shown and the rank-up message is due after it (death message shown {elder.DeathShown}, "
                    + $"rank-up due {Standing.MessagePending} in {F(due)} s)");
                yield return Until(() => !Standing.MessagePending, Standing.RankMessageDelay + 1.5f, hold, w);
                c.Check(w.Met && ShownAfter(Standing.RankUpText, elder.DeathText) == 1 && hud.m_messageCenterText.text == Standing.RankUpText,
                    $"T04: about 4.5 s after that kill the middle of the screen reads \"{Standing.RankUpText}\" (now \"{hud.m_messageCenterText.text}\", "
                    + $"shown {ShownAfter(Standing.RankUpText, elder.DeathText)} time(s) after The Elder's death message)");
            }
            c.Report();
        }
        finally
        {
            if (marks != null)
            {
                marks.Restore();
            }
            if (env != null)
            {
                env.m_debugEnv = oldEnv;
            }
            if (local != null && !hadWet)
            {
                local.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectWet, true);
            }
            Finish(s);
            // The character's real kill table is back: message baseline and published standing follow it.
            Standing.Clear();
            Standing.PublishNow();
        }
    }

    // ================================================================ morale.raid

    private static HashSet<int> CreaturesNow()
    {
        var ids = new HashSet<int>();
        foreach (var ai in BaseAI.BaseAIInstances)
        {
            if (ai != null)
            {
                ids.Add(ai.GetInstanceID());
            }
        }
        return ids;
    }

    // Creatures that appeared since `before` was taken (world or raid spawns: nothing the stage tracks).
    private static List<MonsterAI> NewCreatures(HashSet<int> before, Func<MonsterAI, bool> keep)
    {
        var found = new List<MonsterAI>();
        foreach (var ai in BaseAI.BaseAIInstances)
        {
            var monster = ai as MonsterAI;
            if (monster == null || before.Contains(ai.GetInstanceID()) || monster.m_nview == null || !monster.m_nview.IsValid())
            {
                continue;
            }
            if (keep == null || keep(monster))
            {
                found.Add(monster);
            }
        }
        return found;
    }

    private static int DestroyNewCreatures(HashSet<int> before)
    {
        var scene = ZNetScene.instance;
        var doomed = new List<GameObject>();
        foreach (var ai in BaseAI.BaseAIInstances)
        {
            if (ai != null && !before.Contains(ai.GetInstanceID()) && ai.m_character != null && !ai.m_character.IsPlayer() && !ai.m_character.IsTamed())
            {
                doomed.Add(ai.gameObject);
            }
        }
        foreach (var go in doomed)
        {
            if (go != null && scene != null)
            {
                scene.Destroy(go);
            }
        }
        return doomed.Count;
    }

    private static bool RaidFlag(MonsterAI ai) => ai != null && ai.m_nview != null && ai.m_nview.IsValid() && ai.m_nview.GetZDO().GetBool(ZDOVars.s_eventCreature);

    private static IEnumerator RunRaid()
    {
        var c = new Checks(RaidName);
        Stage s = null;
        var w = new Waiter();
        var events = RandEventSystem.instance;
        var started = false;
        var local = Player.m_localPlayer;
        var sawTutorial = local != null && local.HaveSeenTutorial(RaidTutorial);
        HashSet<int> before = null;
        try
        {
            s = Stage.Begin(RaidName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            RandomEvent raid = null;
            var names = new List<string>();
            if (events != null)
            {
                foreach (var ev in events.m_events)
                {
                    if (ev == null)
                    {
                        continue;
                    }
                    names.Add(ev.m_name);
                    if (ev.m_name == RaidEvent && ev.m_enabled)
                    {
                        raid = ev;
                    }
                }
            }
            if (!c.Check(raid != null, $"T20: this game has no enabled raid named {RaidEvent} (its raids: {string.Join(", ", names.ToArray())})"))
            {
                c.Report();
                yield break;
            }
            var sends = new List<string>();
            foreach (var d in raid.m_spawn)
            {
                if (d != null && d.m_prefab != null)
                {
                    sends.Add(d.m_prefab.name);
                }
            }
            SelfTest.Note(RaidName, $"T20: the raid {RaidEvent} exists in this game (name checked); it sends {string.Join(", ", sends.ToArray())}, lasts {F(raid.m_duration)} s, "
                                    + $"spawns start {F(raid.m_spawnerDelay)} s after it begins");
            if (!c.Check(events.GetCurrentRandomEvent() == null && !RandEventSystem.InEvent(), "T20 setup: another raid or event is already running here"))
            {
                c.Report();
                yield break;
            }
            var rules = NewRules(null);
            ServerRules.TestRules = rules;
            Standing.TestOverride = new Standing.Override(8);
            // The raven would come and explain raids to the fresh test character: marked as seen for the test.
            player.SetSeenTutorial(RaidTutorial);
            before = CreaturesNow();
            // The stage pause the world's spawner; a raid is sent by that same spawner.
            SpawnSystem.m_nospawn = false;
            started = true;
            events.SetRandomEventByName(RaidEvent, here);
            yield return Until(() => events.GetActiveEvent() != null && events.GetActiveEvent().m_name == RaidEvent, 5f, null, w);
            if (!c.Check(w.Met, $"T20: the raid {RaidEvent} started on the player is active within 5 s (the same call as the console command event)"))
            {
                c.Report();
                yield break;
            }
            List<MonsterAI> raiders = null;
            yield return Until(() =>
            {
                raiders = NewCreatures(before, RaidFlag);
                return raiders.Count > 0;
            }, 60f, null, w);
            if (!c.Check(w.Met, "T20: the raid's first creature arrives within 60 s (spawned by the game's own event spawner, with the raid flag)"))
            {
                c.Report();
                yield break;
            }
            SelfTest.Note(RaidName, $"T20: first raid creature {F(w.Took)} s after the raid became active");
            var raider = raiders[0];
            var name = raider.m_character != null ? raider.m_character.m_name : "?";
            var state = CreatureState.Get(raider);
            state.ForgetFacts();
            state.EnsureFacts(rules);
            var level = raider.m_character != null ? raider.m_character.GetLevel() : 1;
            var wouldFear = state.HasRank && Attitudes.Outclasses(8, 0, state.Rank, level, rules.StarRank, rules.MaxBossesSkippedByKills);
            SelfTest.Note(RaidName, $"T20: the raid creature is a {Localization.instance.Localize(name)} {F(Dist(raider, player))} m away; outside a raid it "
                                    + $"{(wouldFear ? "would be afraid of" : "would attack")} a rank 8 player");
            c.Check(state.Exempt && Attitudes.Judge(raider, player) == Attitude.Hostile,
                $"T20: a creature of the real raid always behaves as in the normal game toward the rank 8 player (exempt {state.Exempt}, Judge {Attitudes.Judge(raider, player)})");
            var ran = false;
            yield return Until(() => !Alive(raider) || (raider.m_targetCreature == player && raider.IsAlerted()), 20f, () =>
            {
                s.Noise();
                ran |= raider != null && FearOf(raider) != null;
            }, w);
            c.Check(w.Met && Alive(raider) && !ran,
                $"T20: the raid creature attacks the rank 8 player: the player is its target and it is alerted within 20 s, it never runs "
                + $"(target {Who(Alive(raider) ? raider.m_targetCreature : null)}, ran {ran}, waited {F(w.Took)} s)");
            var all = NewCreatures(before, RaidFlag);
            var afraid = 0;
            foreach (var other in all)
            {
                if (Attitudes.Judge(other, player) != Attitude.Hostile || FearOf(other) != null)
                {
                    afraid++;
                }
            }
            c.Check(afraid == 0, $"T20: none of the {all.Count} raid creature(s) here is afraid of the rank 8 player ({afraid} afraid or running)");
            c.Report();
        }
        finally
        {
            if (started)
            {
                SpawnSystem.m_nospawn = true;
                if (events != null && events.GetCurrentRandomEvent() != null && events.GetCurrentRandomEvent().m_name == RaidEvent)
                {
                    events.ResetRandomEvent();
                }
                if (before != null)
                {
                    var removed = DestroyNewCreatures(before);
                    SelfTest.Note(RaidName, $"raid stopped; {removed} creature(s) that came during the test removed");
                }
                if (!sawTutorial && local != null)
                {
                    local.m_shownTutorials.Remove(RaidTutorial);
                }
            }
            Finish(s);
        }
    }

    // ================================================================ morale.night-hunters

    private static SpawnSystem SpawnerAt(Vector3 point)
    {
        var zone = ZoneSystem.GetZone(point);
        foreach (var system in SpawnSystem.m_instances)
        {
            if (system != null && system.m_nview != null && system.m_nview.IsValid() && system.m_nview.IsOwner()
                && ZoneSystem.GetZone(system.transform.position).Equals(zone))
            {
                return system;
            }
        }
        return null;
    }

    private static void SetWorldTime(double t)
    {
        var env = EnvMan.instance;
        var net = ZNet.instance;
        if (env == null || net == null || env.m_dayLengthSec <= 0)
        {
            return;
        }
        double length = env.m_dayLengthSec;
        net.SetNetTime(t);
        env.m_smoothDayFraction = env.RescaleDayFraction(Mathf.Clamp01((float)(t % length / length)));
    }

    private static IEnumerator RunNightHunters()
    {
        var c = new Checks(NightHuntersName);
        Stage s = null;
        var w = new Waiter();
        var zones = ZoneSystem.instance;
        var net = ZNet.instance;
        var env = EnvMan.instance;
        var timeBefore = net != null ? net.GetTimeSeconds() : 0.0;
        var timeMoved = false;
        string keySet = null;
        HashSet<int> before = null;
        SpawnSystem system = null;
        var salts = new List<string>();
        try
        {
            s = Stage.Begin(NightHuntersName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            system = SpawnerAt(here);
            if (!c.Check(system != null && env != null && net != null && net.IsServer() && !env.m_debugTimeOfDay,
                    "T21: no world spawner of this game for the player's zone (or the time of day is forced): the game's own spawn cannot be run"))
            {
                c.Report();
                yield break;
            }
            SpawnSystem.SpawnData entry = null;
            var hunters = new List<string>();
            foreach (var list in system.m_spawnLists)
            {
                if (list == null)
                {
                    continue;
                }
                foreach (var d in list.m_spawners)
                {
                    if (d == null || d.m_prefab == null || !d.m_enabled || !d.m_huntPlayer)
                    {
                        continue;
                    }
                    hunters.Add($"{d.m_prefab.name} ({d.m_biome}, key {d.m_requiredGlobalKey})");
                    if (entry == null && d.m_prefab.name == HunterPrefab && system.m_heightmap != null && system.m_heightmap.HaveBiome(d.m_biome))
                    {
                        entry = d;
                    }
                }
            }
            if (!c.Check(entry != null, $"T21: the world spawn table has no hunting {HunterPrefab} for the biome the player stands in (hunting spawns: {string.Join("; ", hunters.ToArray())})"))
            {
                c.Report();
                yield break;
            }
            SelfTest.Note(NightHuntersName, $"T21: the game's hunting Fuling spawn: biomes {entry.m_biome}, after {entry.m_requiredGlobalKey}, "
                                            + $"{(entry.m_spawnAtDay ? "day and night" : "night only")}, group {entry.m_groupSizeMin}-{entry.m_groupSizeMax}, "
                                            + $"every {F(entry.m_spawnInterval)} s with a {F(entry.m_spawnChance)} % chance, up to {entry.m_maxSpawned}");
            var rules = NewRules(null);
            ServerRules.TestRules = rules;
            Standing.TestOverride = new Standing.Override(7);

            // The world as that spawn want it: its boss dead (world key), night.
            if (!string.IsNullOrEmpty(entry.m_requiredGlobalKey) && !zones.GetGlobalKey(entry.m_requiredGlobalKey))
            {
                zones.SetGlobalKey(entry.m_requiredGlobalKey);
                keySet = entry.m_requiredGlobalKey;
            }
            timeMoved = true;
            double length = env.m_dayLengthSec;
            SetWorldTime(env.GetDay(timeBefore) * length + NightRaw * length);
            yield return Until(() => EnvMan.IsNight() && !EnvMan.IsDay(), 5f, null, w);
            if (!c.Check(w.Met, $"T21 setup: it is night (night {EnvMan.IsNight()}, day {EnvMan.IsDay()})"))
            {
                c.Report();
                yield break;
            }

            // The game's own spawner, with its own entry: only the long wait and the dice are taken out (interval 1 s,
            // chance 100 %). Every other rule of the spawn run as in the normal game (world key, night, biome, a
            // spot 40 to 80 m from the player, group size, hunt flag, despawn at day).
            before = CreaturesNow();
            var sure = entry.Clone();
            sure.m_spawnChance = 100f;
            sure.m_spawnInterval = 1f;
            List<MonsterAI> group = null;
            for (var attempt = 0; attempt < 6; attempt++)
            {
                var salt = "mc_morale_" + attempt + "_";
                salts.Add(salt);
                SpawnSystem.m_tempNearPlayers.Clear();
                SpawnSystem.m_tempNearPlayers.Add(player);
                SpawnSystem.m_nospawn = false;
                try
                {
                    system.UpdateSpawnList(new List<SpawnSystem.SpawnData> { sure }, net.GetTime(), false, salt);
                }
                finally
                {
                    SpawnSystem.m_nospawn = true;
                }
                yield return null;
                group = NewCreatures(before, ai => Utils.GetPrefabName(ai.gameObject) == HunterPrefab);
                if (group.Count > 0)
                {
                    break;
                }
            }
            if (!c.Check(group != null && group.Count > 0, "T21: the game's spawner, run with its own hunting Fuling entry at night after Yagluth, spawns a group near the player"))
            {
                c.Report();
                yield break;
            }
            var flagged = group.TrueForAll(g => g.m_nview.GetZDO().GetBool(ZDOVars.s_huntPlayer) && !RaidFlag(g));
            var stars = 0;
            foreach (var g in group)
            {
                if (g.m_character != null && g.m_character.GetLevel() > 1)
                {
                    stars++;
                    g.m_character.SetLevel(1);
                }
            }
            if (stars > 0)
            {
                SelfTest.Note(NightHuntersName, $"T21: {stars} of the {group.Count} spawned Fuling(s) had stars (each star needs one more boss); stars taken off for the rank 7 steps");
            }
            var lead = group[0];
            SelfTest.Note(NightHuntersName, $"T21: {group.Count} hunting Fuling(s) spawned by the game {F(Dist(lead, player))} m from the player");
            yield return Wait(1.2f, s.Noise);
            var facts = true;
            foreach (var g in group)
            {
                var state = CreatureState.Get(g);
                state.ForgetFacts();
                state.EnsureFacts(rules);
                facts &= state.NightHunter && !state.Exempt && state.HasRank && Attitudes.Judge(g, player) == Attitude.Afraid;
            }
            c.Check(flagged && facts,
                $"T21: the Fulings the game spawned carry its hunt flag, and the mod sees them as night hunters that are afraid of the rank 7 player "
                + $"(hunt flag {flagged}, night hunter and afraid {facts})");

            // ----- rank 7: they do not come -----
            var hunted = false;
            yield return Wait(4f, () =>
            {
                s.Noise();
                foreach (var g in group)
                {
                    hunted |= Alive(g) && g.m_targetCreature == player;
                }
            });
            // The first one must still be there: a group that died or was unloaded would pass "all that live stand down".
            var standDown = Alive(lead) && group.TrueForAll(g => !Alive(g) || (CreatureState.TryGet(g, out var st) && st.HuntStandDown && !g.HuntPlayer()));
            c.Check(!hunted && standDown, $"T21: rank 7: the group of hunting Fulings does not attack the player: no target, they stand down (targeted the player {hunted}, "
                                          + $"stand down {standDown}, first Fuling alive {Alive(lead)}; {DescribePack(group, player)})");

            // ----- rank 7: they run when the player come close -----
            var near = Stage.Ground(lead.transform.position + (here - lead.transform.position).normalized * 7f);
            s.MovePlayer(near);
            var attacked = false;
            yield return Until(() => FearOf(lead) == player, 5f, () =>
            {
                s.Noise();
                attacked |= lead.m_targetCreature == player;
            }, w);
            c.Check(w.Met && !attacked, $"T21: rank 7: with the player 7 m away the Fuling runs from them and does not attack (runs from {Who(FearOf(lead))}, "
                                        + $"targeted the player {attacked}, {F(Dist(lead, player))} m)");

            // ----- NightHuntersCanBeAfraid off: they hunt -----
            ServerRules.TestRules = NewRules(r => r.NightHuntersCanBeAfraid = false);
            yield return Until(() => Alive(lead) && group.TrueForAll(g => !Alive(g) || (g.m_targetCreature == player && g.HuntPlayer())), 10f, s.Noise, w);
            c.Check(w.Met && Alive(lead) && Attitudes.Judge(lead, player) == Attitude.Hostile,
                $"T21: with NightHuntersCanBeAfraid off the group hunts the rank 7 player: every Fuling targets them within 10 s ({DescribePack(group, player)})");
            c.Report();
        }
        finally
        {
            SpawnSystem.m_nospawn = true; // the stage puts its own value back in Finish
            if (before != null)
            {
                DestroyNewCreatures(before);
            }
            if (keySet != null && zones != null)
            {
                zones.RemoveGlobalKey(keySet);
            }
            if (timeMoved)
            {
                SetWorldTime(timeBefore);
            }
            if (system != null && system.m_nview != null && system.m_nview.IsValid())
            {
                // Spawn timers this test wrote into the zone (one per try): back to "never".
                foreach (var salt in salts)
                {
                    system.m_nview.GetZDO().Set((salt + HunterPrefab + 1).GetStableHashCode(), 0L);
                }
            }
            Finish(s);
        }
    }
}
#endif
