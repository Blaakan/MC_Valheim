#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Logging;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.CreaturesMoraleMod;

// Debug build only. In-world self tests, second part (TESTING.md items in brackets):
//   morale.fight     hit on a running Greydwarf: it fights with the alert kept, calm again after the provocation with
//                    the player far, runs again when they walk up (T10); cornered Boar: trapped for the default 2 s,
//                    chased, 5 m away, sneaked up on (T13)
//   morale.senses    a walking player behind it is heard, slow Sneak XP log line only near afraid creatures (T14),
//                    sneak attack from behind with the fear on (T30), FearRange 5 and the 5 s out-of-sight release
//                    (T29), the player turns ghost while it runs (T36)
//   morale.packs     rout with Greydwarf_Shaman and Greydwarf_Elite (T16); a sleeping Draugr near a killed
//                    Draugr_Elite (T28)
//   morale.packs-other  the seven other packs: the leader killed, its followers run (T19)
//   morale.rout-hit  rout log line and plate icon (T15), far follower joins only inside the radius (T17), hit on a
//                    fleeing follower (T18), rout's end with a building target (T34 a)
//   morale.rout-end  follower in no home list (T27), ShakenSeconds 0: the fight at a rout's end keeps the alert (T34 b)
//   morale.exempt    Eikthyr and a Boar near it, raid creature, training dummy, Dvergr (T20); Fuling night hunter (T21)
//   morale.tame      a Wolf eats and tames, taming pauses while it runs (T22); tame against an afraid Greydwarf (T23,
//                    T33)
//   morale.toggle    mod turned off and on through the framework, also during a rout (L01, L02)
//   morale.settings  rule changes live: name check warning, edits settle once, RoutSeconds, FearRange (T26); the
//                    overlap warning for another creature AI mod (X09)
//   morale.sleeper   a hit on a sleeping afraid Draugr that faces the player: it wakes, is provoked and fights (T35)
//   morale.spawn-biome  a Skeleton that spawned in the Mountains, then in the Plains: attacks or runs by that biome's
//                    rank (T31, the behaviour; the Judge table on more biomes is in morale.calm)
//   morale.handover  a creature another game owned for a moment: fear, provocation and rout go on (M03, M05, M13,
//                    this game's side only)
// Tests named morale.bug.* hold ONE check each that fails because the mod (or its TESTING.md) is wrong; they fail
// until the user decides. Never "fix" them by changing the check:
//   morale.bug.sleeper-sneak  the same hit must be a sneak attack, as in the normal game (T35): the mod takes it away
//   morale.bug.rank-message   T04's second half as written: a second Eikthyr kill after BossesAhead = 0 brings the
//                             rank-up message (the mod and design E5 say: only a kill that raises the rank; T37)
internal static partial class SelfTests
{
    private const string FightName = "morale.fight";
    private const string SensesName = "morale.senses";
    private const string PacksName = "morale.packs";
    private const string RoutHitName = "morale.rout-hit";
    private const string RoutEndName = "morale.rout-end";
    private const string ExemptName = "morale.exempt";
    private const string TameName = "morale.tame";
    private const string ToggleName = "morale.toggle";
    private const string SettingsName = "morale.settings";
    private const string SleeperName = "morale.sleeper";
    private const string HandoverName = "morale.handover";
    private const string PacksOtherName = "morale.packs-other";
    private const string SpawnBiomeName = "morale.spawn-biome";
    private const string BugSleeperSneakName = "morale.bug.sleeper-sneak";
    private const string BugRankMessageName = "morale.bug.rank-message";

    private static void RegisterMore()
    {
        SelfTest.Register(KindsName, RunKinds);
        SelfTest.Register(RanksName, RunRanks);
        SelfTest.Register(RestName, RunRest);
        SelfTest.Register(KillsName, RunKills);
        SelfTest.Register(MissilesName, RunMissiles);
        SelfTest.Register(FightName, RunFight);
        SelfTest.Register(SensesName, RunSenses);
        SelfTest.Register(PacksName, RunPacks);
        SelfTest.Register(RoutHitName, RunRoutHit);
        SelfTest.Register(RoutEndName, RunRoutEnd);
        SelfTest.Register(ExemptName, RunExempt);
        SelfTest.Register(TameName, RunTame);
        SelfTest.Register(ToggleName, RunToggle);
        SelfTest.Register(SettingsName, RunSettings);
        SelfTest.Register(SleeperName, RunSleeper);
        SelfTest.Register(HandoverName, RunHandover);
        SelfTest.Register(PacksOtherName, RunPacksOther);
        SelfTest.Register(SpawnBiomeName, RunSpawnBiome);
        SelfTest.Register(BugSleeperSneakName, RunBugSleeperSneak);
        SelfTest.Register(BugRankMessageName, RunBugRankMessage);
    }

    private static void UnregisterMore()
    {
        foreach (var name in new[]
                 {
                     KindsName, RanksName, RestName, KillsName, MissilesName, FightName, SensesName, PacksName, RoutHitName,
                     RoutEndName, ExemptName, TameName, ToggleName, SettingsName, SleeperName, HandoverName, PacksOtherName,
                     SpawnBiomeName, BugSleeperSneakName, BugRankMessageName,
                 })
        {
            SelfTest.Unregister(name);
        }
    }

    // ================================================================ morale.fight

    private static IEnumerator RunFight()
    {
        var c = new Checks(FightName);
        Stage s = null;
        var w = new Waiter();
        var plate = new PlateRef();
        try
        {
            s = Stage.Begin(FightName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            var pid = player.GetPlayerID();
            Standing.TestOverride = new Standing.Override(4);
            var spot = s.Spot(here, s.Forward, 5f, true);

            // ----- T10: the default provocation lasts 30 s -----
            ServerRules.TestRules = NewRules(null);
            var first = s.Spawn("Greydwarf", spot, here - spot);
            if (c.Check(first != null, "T10: could not spawn a Greydwarf"))
            {
                yield return Wait(1f, null);
                Hit(first.m_character, player, 1f, 0f, 1f);
                var ok = CreatureKeys.TryGetUntil(first.m_nview.GetZDO().GetByteArray(CreatureKeys.Provokers), pid, out var until);
                c.Check(ok && SecondsLeft(until) > 28.5 && SecondsLeft(until) <= 30.01,
                    $"T10: with the default settings a hit provokes for 30 s ({F(SecondsLeft(until))} s left)");
                s.Destroy(first);
            }

            // ----- T10: hit on a running one, with a 5 s provocation -----
            var rules = NewRules(r => r.ProvokedSeconds = 5f);
            ServerRules.TestRules = rules;
            var g = s.Spawn("Greydwarf", spot, here - spot);
            if (!c.Check(g != null, "T10: could not spawn a second Greydwarf"))
            {
                c.Report();
                yield break;
            }
            yield return Until(() => FearOf(g) == player && g.IsAlerted(), 3f, s.Noise, w);
            c.Check(w.Met, $"T10 setup: the afraid Greydwarf 5 m away runs from the noisy player (runs from {Who(FearOf(g))})");
            Hit(g.m_character, player, 1f, 0f, 1f);
            c.Check(Provoked(g, player) && g.m_targetCreature == player && FearOf(g) == null && g.IsAlerted(),
                $"T10: the hit on the running Greydwarf: at once provoked, the player its target, no longer running, still alerted "
                + $"(provoked {Provoked(g, player)}, target {Who(g.m_targetCreature)}, runs from {Who(FearOf(g))}, alerted {g.IsAlerted()})");
            var dropped = false;
            var ranAgain = false;
            yield return Wait(2f, () =>
            {
                s.Noise();
                dropped |= !g.IsAlerted();
                ranAgain |= FearOf(g) != null;
            });
            c.Check(!dropped && !ranAgain && g.m_targetCreature == player && Attitudes.Judge(g, player) == Attitude.Provoked,
                $"T10: it fights the player; the alert was never dropped between the run and the fight (one alert sound) "
                + $"(dropped {dropped}, ran again {ranAgain}, target {Who(g.m_targetCreature)})");

            // Stop fighting, 20 m away, until the provocation is over.
            var gSpot = Stage.Ground(g.transform.position);
            var backOff = s.Spot(gSpot, here - gSpot, 20f, false);
            s.MovePlayer(backOff);
            Action holdG = () =>
            {
                s.Noise();
                Stage.Place(g.m_character, gSpot, player.transform.position - gSpot);
            };
            yield return Until(() => !Provoked(g, player) && !g.IsAlerted() && g.m_targetCreature == null && FearOf(g) == null,
                rules.ProvokedSeconds + 4f, holdG, w);
            c.Check(w.Met && !ZdoAlert(g) && Attitudes.Judge(g, player) == Attitude.Afraid,
                $"T10: once the provocation is over, with the player 20 m away: no alert, no target, afraid again "
                + $"(provoked {Provoked(g, player)}, alerted {g.IsAlerted()}, target {Who(g.m_targetCreature)}, waited {F(w.Took)} s)");
            if (PlateOf(g.m_character) != null)
            {
                yield return PlateShows(g.m_character, false, 1.5f, holdG, w, plate);
                c.Check(w.Met, $"T10: its plate shows no alert icon then ({DescribeIcons(plate.Data)})");
            }
            var came = false;
            yield return Wait(1.5f, () =>
            {
                holdG();
                came |= g.m_targetCreature != null || g.IsAlerted();
            });
            c.Check(!came, "T10: it does not come for the (noisy) player 20 m away");
            s.MovePlayer(gSpot + (backOff - gSpot).normalized * 6f);
            yield return Until(() => FearOf(g) == player && g.IsAlerted() && ZdoAlert(g), 2.5f, holdG, w);
            c.Check(w.Met, $"T10: the player walks up to it: it runs again, alert icon on (runs from {Who(FearOf(g))}, alerted {g.IsAlerted()})");
            s.Destroy(g);
            s.MovePlayer(here);

            // ----- T13: cornered Boar, rank 3, default settings (3 m for 2 s) -----
            var defaults = NewRules(null);
            ServerRules.TestRules = defaults;
            Standing.TestOverride = new Standing.Override(3);
            var cpos = s.Spot(here, s.Forward, 6f, true);
            var toward = (here - cpos).normalized;

            // (a) it cannot get away (held at its spot), the player 1.5 m away
            var trapped = SpawnMeadows(s, c, "Boar", cpos, here - cpos);
            if (trapped != null)
            {
                Action holdTrapped = () =>
                {
                    s.Noise();
                    Stage.Place(trapped.m_character, cpos, player.transform.position - cpos);
                };
                yield return Wait(1.6f, holdTrapped);
                c.Check(Attitudes.Judge(trapped, player) == Attitude.Afraid && !Provoked(trapped, player), "T13a setup: the Boar is afraid, not provoked");
                s.MovePlayer(cpos + toward * 1.5f);
                var ran = false;
                yield return Until(() => Provoked(trapped, player), 5f, () =>
                {
                    holdTrapped();
                    ran |= FearOf(trapped) == player;
                }, w);
                c.Check(w.Met && ran && w.Took >= defaults.CorneredSeconds - 0.25f,
                    $"T13a: a Boar that cannot get away, the player 1.5 m from it: it tries to run, then fights after about {F(defaults.CorneredSeconds)} s "
                    + $"(ran {ran}, provoked {Provoked(trapped, player)}, after {F(w.Took)} s)");
                if (w.Met)
                {
                    c.Check(trapped.m_targetCreature == player && trapped.IsAlerted() && FearOf(trapped) == null,
                        $"T13a: it stops running and targets the player (target {Who(trapped.m_targetCreature)}, runs from {Who(FearOf(trapped))})");
                }
                s.Destroy(trapped);
                s.MovePlayer(here);
            }

            // (c) the player keeps 5 m from a trapped one
            var kept = SpawnMeadows(s, c, "Boar", cpos, here - cpos);
            if (kept != null)
            {
                s.MovePlayer(cpos + toward * 5f);
                var ran = false;
                yield return Wait(4.5f, () =>
                {
                    s.Noise();
                    Stage.Place(kept.m_character, cpos, player.transform.position - cpos);
                    ran |= FearOf(kept) == player;
                });
                c.Check(ran && !Provoked(kept, player) && kept.m_targetCreature == null,
                    $"T13c: the player 5 m from a trapped Boar for 4.5 s: it stays afraid and never attacks (ran {ran}, provoked {Provoked(kept, player)}, "
                    + $"target {Who(kept.m_targetCreature)})");
                s.Destroy(kept);
                s.MovePlayer(here);
            }

            // (d) sneaking up behind an unaware one and staying next to it
            var unaware = SpawnMeadows(s, c, "Boar", cpos, cpos - here);
            if (unaware != null)
            {
                Action holdUnaware = () =>
                {
                    s.Silence();
                    Stage.Place(unaware.m_character, cpos, cpos - player.transform.position);
                };
                yield return Wait(1f, holdUnaware);
                s.MovePlayer(cpos + toward * 1.5f);
                yield return Wait(3f, holdUnaware);
                c.Check(FearOf(unaware) == null && !Provoked(unaware, player) && !unaware.IsAlerted() && unaware.m_targetCreature == null,
                    $"T13d: a silent player 1.5 m behind an unaware Boar for 3 s: it does not notice, never runs, never attacks "
                    + $"(runs from {Who(FearOf(unaware))}, provoked {Provoked(unaware, player)}, alerted {unaware.IsAlerted()})");
                s.Destroy(unaware);
                s.MovePlayer(here);
            }

            // (b) chased in the open: the player stays 2 m from it wherever it runs
            var chased = SpawnMeadows(s, c, "Boar", cpos, here - cpos);
            if (chased != null)
            {
                var ran = false;
                yield return Until(() => Provoked(chased, player), 6f, () =>
                {
                    s.Noise();
                    var off = player.transform.position - chased.transform.position;
                    off.y = 0f;
                    s.MovePlayer(chased.transform.position + (off.sqrMagnitude > 0.01f ? off.normalized : toward) * 2f);
                    ran |= FearOf(chased) == player;
                }, w);
                c.Check(w.Met && ran, $"T13b: a Boar chased in the open, the player always 2 m from it: it turns and fights within 6 s "
                                      + $"(ran {ran}, provoked {Provoked(chased, player)}, after {F(w.Took)} s)");
                if (w.Met)
                {
                    c.Check(chased.m_targetCreature == player && FearOf(chased) == null, $"T13b: its target is the player (target {Who(chased.m_targetCreature)})");
                }
                s.Destroy(chased);
                s.MovePlayer(here);
            }
            c.Report();
        }
        finally
        {
            Finish(s);
        }
    }

    // ================================================================ morale.senses

    private static IEnumerator RunSenses()
    {
        var c = new Checks(SensesName);
        Stage s = null;
        var w = new Waiter();
        var local = Player.m_localPlayer;
        var wasGhost = local != null && local.InGhostMode();
        try
        {
            s = Stage.Begin(SensesName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            ServerRules.TestRules = NewRules(null);
            Standing.TestOverride = new Standing.Override(4);

            // ----- T14: a walking player behind it is heard -----
            var spot = s.Spot(here, s.Forward, 8f, true);
            var back = s.Spawn("Greydwarf", spot, spot - here);
            if (c.Check(back != null, "T14: could not spawn a Greydwarf"))
            {
                Action holdBack = () => Stage.Place(back.m_character, spot, spot - player.transform.position);
                yield return Wait(1.5f, () =>
                {
                    s.Silence();
                    holdBack();
                });
                c.Check(FearOf(back) == null && !back.IsAlerted() && !back.CanSeeTarget(player),
                    $"T14 setup: a silent player 8 m behind an afraid Greydwarf is not noticed (runs from {Who(FearOf(back))}, sees {back.CanSeeTarget(player)})");
                // Vanilla footsteps: Character adds noise 15 when walking, 30 when running, none when crouched.
                yield return Until(() => FearOf(back) == player, 2.5f, () =>
                {
                    player.AddNoise(15f);
                    holdBack();
                }, w);
                c.Check(w.Met, $"T14: a walking player (footstep noise 15 m) 8 m behind it is heard: it runs within 2.5 s "
                               + $"(runs from {Who(FearOf(back))}, hears {back.CanHearTarget(player)})");
                s.Destroy(back);
            }

            // ----- T14: Sneak skill near afraid creatures only -----
            s.Silence();
            var quietSpot = s.Spot(here, s.Forward, 15f, false);
            var quiet = s.Spawn("Greydwarf", quietSpot, quietSpot - here);
            if (c.Check(quiet != null, "T14: could not spawn a Greydwarf for the sneak check"))
            {
                Action holdQuiet = () =>
                {
                    s.Silence();
                    Stage.Place(quiet.m_character, quietSpot, quietSpot - player.transform.position);
                };
                yield return Wait(1.2f, holdQuiet);
                s.ClearEnemies(here, 60f, quiet);
                c.Check(!quiet.IsAlerted() && Dist(quiet, player) < quiet.m_viewRange && OpenSneakLog(),
                    $"T14 setup: an unaware afraid Greydwarf {F(Dist(quiet, player))} m away, inside its view range ({F(quiet.m_viewRange)} m)");
                var mark = Watch.Mark();
                var inRange = BaseAI.InStealthRange(player);
                c.Check(!inRange && Watch.Count("Sneak: only afraid creatures near, slow rate.", mark, LogLevel.Debug) == 1,
                    $"T14: with only an afraid creature near, sneaking counts as far from enemies (slow Sneak rate) and the Debug log says so once "
                    + $"(in stealth range {inRange}, line logged {Watch.Count("Sneak: only afraid creatures near, slow rate.", mark)} time(s))");
                Standing.TestOverride = new Standing.Override(0);
                OpenSneakLog();
                mark = Watch.Mark();
                inRange = BaseAI.InStealthRange(player);
                c.Check(inRange && Watch.Count("Sneak:", mark) == 0,
                    $"T14: near an unaware hostile creature (rank 0) the normal Sneak rate applies and the line is not logged (in stealth range {inRange})");
                Standing.TestOverride = new Standing.Override(4);
                s.Destroy(quiet);
            }

            // ----- T30: sneaking up behind it with the fear on, then a sneak attack -----
            var unawareSpot = s.Spot(here, s.Forward, 6f, true);
            var unaware = s.Spawn("Greydwarf", unawareSpot, unawareSpot - here);
            if (c.Check(unaware != null, "T30: could not spawn a Greydwarf"))
            {
                Action holdUnaware = () =>
                {
                    s.Silence();
                    Stage.Place(unaware.m_character, unawareSpot, unawareSpot - player.transform.position);
                };
                yield return Wait(1f, holdUnaware);
                s.MovePlayer(unawareSpot + (here - unawareSpot).normalized * 1.5f);
                yield return Wait(2f, holdUnaware);
                c.Check(FearOf(unaware) == null && !unaware.IsAlerted() && !unaware.CanSeeTarget(player),
                    $"T30: a silent player who comes up to 1.5 m behind a busy afraid Greydwarf: it does not run (it has not sensed them) "
                    + $"(runs from {Who(FearOf(unaware))}, alerted {unaware.IsAlerted()})");
                var before = unaware.m_character.m_backstabTime;
                Hit(unaware.m_character, player, 1f, 0f, 3f);
                c.Check(unaware.m_character.m_backstabTime != before && Provoked(unaware, player),
                    "T30: the hit from behind is a sneak attack (with the fear on), and it provokes");
                s.Destroy(unaware);
                s.MovePlayer(here);
            }

            // ----- T29: FearRange 5 -----
            ServerRules.TestRules = NewRules(r => r.FearRange = 5f);
            var farSpot = s.Spot(here, s.Forward, 8f, true);
            var at = farSpot;
            var runner = s.Spawn("Greydwarf", farSpot, here - farSpot);
            if (!c.Check(runner != null, "T29: could not spawn a Greydwarf"))
            {
                c.Report();
                yield break;
            }
            Action holdRunner = () =>
            {
                s.Noise();
                Stage.Place(runner.m_character, at, player.transform.position - at);
            };
            var feared = false;
            yield return Wait(3f, () =>
            {
                holdRunner();
                feared |= FearOf(runner) != null;
            });
            c.Check(!feared && runner.m_targetCreature == null && Attitudes.Judge(runner, player) == Attitude.Afraid,
                $"T29: FearRange 5: the afraid Greydwarf lets the noisy player stand 8 m away (ran {feared}, target {Who(runner.m_targetCreature)})");
            at = s.Spot(here, s.Forward, 4f, true);
            yield return Until(() => FearOf(runner) == player, 2.5f, holdRunner, w);
            c.Check(w.Met, "T29: FearRange 5: 4 m away it runs within 2.5 s");

            // Default range again: out of its sight and silent for about 5 s, still 8 m away.
            ServerRules.TestRules = NewRules(null);
            at = farSpot;
            yield return Until(() => FearOf(runner) == player && runner.IsAlerted(), 2.5f, holdRunner, w);
            c.Check(w.Met, "T29 setup: default FearRange 12: it runs from the noisy player 8 m away");
            // Unseen = the game's own stealth factor at 0 (a creature sees a player within view range x stealth factor).
            yield return Until(() => FearOf(runner) == null, Fear.Memory + 3f, () =>
            {
                s.Silence();
                player.m_stealthFactor = 0f;
                Stage.Place(runner.m_character, at, player.transform.position - at);
            }, w);
            c.Check(w.Met && w.Took >= Fear.Memory - 1.2f && !runner.IsAlerted() && !ZdoAlert(runner),
                $"T29: the player unseen and unheard, still 8 m away: it keeps running for about {F(Fear.Memory)} s, then calms down "
                + $"(stopped after {F(w.Took)} s, alerted {runner.IsAlerted()})");
            player.m_stealthFactor = 1f; // a standing player's own value (the game moves it there over 4 s anyway)

            // ----- T36: the player it runs from turns ghost (same path as dying) -----
            at = s.Spot(here, s.Forward, 6f, true);
            yield return Until(() => FearOf(runner) == player && runner.IsAlerted() && ZdoAlert(runner), 3.5f, holdRunner, w);
            c.Check(w.Met, $"T36 setup: the Greydwarf runs from the player 6 m away (runs from {Who(FearOf(runner))})");
            player.SetGhostMode(true);
            yield return Until(() => FearOf(runner) == null, 0.6f, holdRunner, w);
            c.Check(w.Met, "T36: the player turns ghost: at its next frame it no longer runs from them");
            var picked = false;
            yield return Until(() => !runner.IsAlerted() && !ZdoAlert(runner) && runner.m_targetCreature == null, 2.5f, () =>
            {
                holdRunner();
                picked |= runner.m_targetCreature == player;
            }, w);
            c.Check(w.Met && !picked, $"T36: within 2.5 s the run's alert is taken back (ZDO alert off) and it has no target "
                                      + $"(alerted {runner.IsAlerted()}, target {Who(runner.m_targetCreature)}, targeted the player {picked})");
            player.SetGhostMode(false);
            yield return Until(() => FearOf(runner) == player && runner.IsAlerted(), 2.5f, holdRunner, w);
            c.Check(w.Met, "T36: the player is back (not a ghost) and noisy 6 m away: it runs from them again within 2.5 s");
            s.Destroy(runner);
            c.Report();
        }
        finally
        {
            if (local != null)
            {
                local.SetGhostMode(wasGhost);
            }
            Finish(s);
        }
    }

    // ================================================================ packs (shared by several tests)

    // One pack on the stage: leader 15 m (or `distance`) in front of the player, followers 4 m around it.
    private sealed class Pack
    {
        internal MonsterAI Leader;
        internal readonly List<MonsterAI> Followers = new List<MonsterAI>();
        internal Vector3 Center;
        internal Vector3 Away;      // from the player to the leader
        internal Vector3 Side;
        internal Vector3 DeathPos;
        internal float KillTime;
        internal long Until;

        internal void Destroy(Stage s)
        {
            s.Destroy(Leader);
            foreach (var f in Followers)
            {
                s.Destroy(f);
            }
            Followers.Clear();
        }
    }

    private static Pack SpawnPack(Stage s, string leader, string follower, int count, float distance)
    {
        var here = s.Player.transform.position;
        var pack = new Pack { Center = s.Spot(here, s.Forward, distance, false) };
        pack.Away = pack.Center - here;
        pack.Away.y = 0f;
        pack.Away.Normalize();
        pack.Side = Vector3.Cross(Vector3.up, pack.Away).normalized;
        var face = here - pack.Center;
        pack.Leader = s.Spawn(leader, pack.Center, face);
        var offsets = new[] { pack.Side * 4f, -pack.Side * 4f, pack.Away * 4f, pack.Away * 4f + pack.Side * 3f };
        for (var i = 0; i < count && i < offsets.Length; i++)
        {
            var f = s.Spawn(follower, Stage.Ground(pack.Center + offsets[i]), face);
            if (f != null)
            {
                pack.Followers.Add(f);
            }
        }
        NoDrops(pack.Leader);
        return pack;
    }

    private static bool PackRouted(Pack pack)
    {
        var now = Attitudes.Now();
        foreach (var f in pack.Followers)
        {
            if (f == null || RoutUntilOf(f) <= now)
            {
                return false;
            }
        }
        return pack.Followers.Count > 0;
    }

    // The player kills the leader. Met = every follower has a rout deadline within `timeout` (leaders with a death
    // animation rout their pack when the animation ends).
    private static IEnumerator KillLeader(Stage s, Pack pack, float timeout, Action eachFrame, Waiter w)
    {
        var leader = pack.Leader;
        pack.DeathPos = leader.transform.position;
        Kill(leader.m_character, s.Player);
        yield return Until(() => PackRouted(pack), timeout, () =>
        {
            eachFrame?.Invoke();
            if (leader != null)
            {
                pack.DeathPos = leader.transform.position;
            }
        }, w);
        pack.KillTime = Time.time;
        pack.Until = 0L;
        foreach (var f in pack.Followers)
        {
            pack.Until = Math.Max(pack.Until, RoutUntilOf(f));
        }
    }

    // ================================================================ morale.packs

    private static IEnumerator PackSteps(Stage s, Checks c, string item, string leaderName, string followerName, int count, bool watchRun)
    {
        var player = s.Player;
        var w = new Waiter();
        Rout.Clear(); // a rout kept from the pack before would reach these followers at their first check
        var pack = SpawnPack(s, leaderName, followerName, count, 12f);
        if (!c.Check(pack.Leader != null && pack.Followers.Count == count, $"{item}: could not spawn {leaderName} with {count} {followerName}"))
        {
            pack.Destroy(s);
            yield break;
        }
        yield return Wait(0.7f, null);
        if (!c.Check(Alive(pack.Leader) && pack.Followers.TrueForAll(f => Alive(f) && !f.IsSleeping() && RoutUntilOf(f) == 0L),
                $"{item}: {leaderName} and its {followerName} are alive, awake and not routed before the kill"))
        {
            pack.Destroy(s);
            yield break;
        }
        yield return KillLeader(s, pack, 6f, null, w);
        c.Check(w.Met, $"{item}: {leaderName} killed by the player: its {count} {followerName} flee within 6 s");
        if (w.Met)
        {
            SelfTest.Note(s.Test, $"{item}: {leaderName}: its pack fled {F(w.Took)} s after the killing hit");
            var good = Rout.LastAppliedHere == count;
            foreach (var f in pack.Followers)
            {
                good &= Vector3.Distance(RoutFromOf(f), pack.DeathPos) <= 2.5f && Attitudes.Judge(f, player) == Attitude.Routed;
            }
            c.Check(good, $"{item}: {leaderName}: the rout reached exactly its {count} followers, they flee from where it fell and count as routed "
                          + $"(reached {Rout.LastAppliedHere})");
            if (watchRun)
            {
                yield return Wait(1.2f, null);
                c.Check(pack.Followers.TrueForAll(f => f != null && f.IsAlerted() && f.m_targetCreature == null && ZdoAlert(f)),
                    $"{item}: {leaderName}: 1.2 s later its followers run: alerted (alert icon), no target ({DescribePack(pack.Followers, player)})");
            }
        }
        pack.Destroy(s);
        Rout.Clear();
    }

    private static IEnumerator RunPacks()
    {
        var c = new Checks(PacksName);
        Stage s = null;
        var w = new Waiter();
        try
        {
            s = Stage.Begin(PacksName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            var rules = NewRules(null);
            ServerRules.TestRules = rules;
            Standing.TestOverride = new Standing.Override(0);

            // ----- T16: the other Greydwarf leaders -----
            yield return PackSteps(s, c, "T16", "Greydwarf_Shaman", "Greydwarf", 3, true);
            yield return PackSteps(s, c, "T16", "Greydwarf_Elite", "Greydwarf", 3, true);

            s.ClearEnemies(here, 50f, null);

            // ----- T28: a follower asleep 13 m from its leader's death -----
            Rout.Clear();
            s.Silence();
            var sleeperSpot = s.Spot(here, s.Forward, 13f, false);
            var sleeper = s.Spawn("Draugr_sleeping", sleeperSpot, here - sleeperSpot);
            var eliteSpot = Stage.Ground(here + s.Right * 2f);
            if (!c.Check(sleeper != null, "T28: could not spawn Draugr_sleeping"))
            {
                c.Report();
                yield break;
            }
            yield return Wait(1.2f, s.Silence);
            var distance = Dist(sleeper, player);
            c.Check(sleeper.IsSleeping() && distance > sleeper.m_wakeupRange && distance < rules.RoutRadius,
                $"T28 setup: the Draugr sleeps {F(distance)} m away: outside its wake range ({F(sleeper.m_wakeupRange)} m), inside the rout radius ({F(rules.RoutRadius)} m)");
            var elite = s.Spawn("Draugr_Elite", eliteSpot, here - eliteSpot);
            if (!c.Check(elite != null, "T28: could not spawn Draugr_Elite"))
            {
                c.Report();
                yield break;
            }
            NoDrops(elite);
            yield return Wait(0.25f, s.Silence); // killed before its first swing: a hit on the player would make noise
            var mark = Watch.Mark();
            var routLine = $"Rout: {TokenOf("Draugr_Elite")} died, applied to 0 creature(s) here.";
            Kill(elite.m_character, player);
            yield return Until(() => Watch.Count(routLine, mark) > 0, 6f, s.Silence, w);
            c.Check(w.Met, $"T28: the Draugr_Elite killed next to the player sets off its pack's rout, which reaches nobody (\"{routLine}\")");
            yield return Wait(1f, s.Silence);
            c.Check(sleeper.IsSleeping() && RoutUntilOf(sleeper) == 0L, $"T28: the sleeping Draugr stays asleep and is not routed (asleep {sleeper.IsSleeping()})");
            s.MovePlayer(sleeperSpot + (here - sleeperSpot).normalized * 5f);
            yield return Until(() => !sleeper.IsSleeping(), 6f, s.Noise, w);
            c.Check(w.Met, "T28: the player walks up within the rout's 15 s: the Draugr wakes");
            var feared = false;
            yield return Until(() => sleeper.m_targetCreature == player, 5f, () =>
            {
                s.Noise();
                feared |= FearOf(sleeper) != null;
            }, w);
            c.Check(w.Met && !feared && RoutUntilOf(sleeper) == 0L && Attitudes.Judge(sleeper, player) == Attitude.Hostile,
                $"T28: it attacks the player as in the normal game and does not flee (target {Who(sleeper.m_targetCreature)}, routed {RoutUntilOf(sleeper) != 0L}, "
                + $"Judge {Attitudes.Judge(sleeper, player)})");
            s.Destroy(sleeper);
            Rout.Clear();
            c.Report();
        }
        finally
        {
            Finish(s);
        }
    }

    // ================================================================ morale.packs-other

    // T19: the seven other packs. Own test (was part of morale.packs): each pack's followers are also watched 1.2 s
    // after the kill (they run: alerted, no target), and a kind that does not flee blocks only T19.
    private static IEnumerator RunPacksOther()
    {
        var c = new Checks(PacksOtherName);
        Stage s = null;
        try
        {
            s = Stage.Begin(PacksOtherName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var here = s.Player.transform.position;
            ServerRules.TestRules = NewRules(null);
            Standing.TestOverride = new Standing.Override(0);
            yield return PackSteps(s, c, "T19", "GoblinBrute", "Goblin", 3, true);
            yield return PackSteps(s, c, "T19", "GoblinShaman", "GoblinArcher", 3, true);
            yield return PackSteps(s, c, "T19", "Draugr_Elite", "Draugr", 3, true);
            yield return PackSteps(s, c, "T19", "Fenring_Cultist", "Fenring", 2, true);
            yield return PackSteps(s, c, "T19", "SeekerBrute", "Seeker", 3, true);
            yield return PackSteps(s, c, "T19", "Charred_Mage", "Charred_Melee", 3, true);
            yield return PackSteps(s, c, "T19", "Greydwarf_Shaman_Frozen", "Greydwarf_Frozen", 3, true);
            s.ClearEnemies(here, 50f, null);
            Rout.Clear();
            c.Report();
        }
        finally
        {
            Finish(s);
        }
    }

    // ================================================================ morale.rout-hit

    private static IEnumerator RunRoutHit()
    {
        var c = new Checks(RoutHitName);
        Stage s = null;
        var w = new Waiter();
        var plate = new PlateRef();
        try
        {
            s = Stage.Begin(RoutHitName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            var defaults = NewRules(null);
            c.Check(defaults.RoutSeconds == 15f && defaults.ShakenSeconds == 60f && defaults.RoutRadius == 25f && defaults.FearRange == 12f,
                "T15: the default rout lasts 15 s within 25 m, then 60 s of fear, fear range 12 m (these steps use 5 s and 12 s)");
            var rules = NewRules(r =>
            {
                r.RoutSeconds = 5f;
                r.ShakenSeconds = 12f;
                r.ProvokedSeconds = 15f;
            });
            ServerRules.TestRules = rules;
            Standing.TestOverride = new Standing.Override(0);
            Rout.Clear();
            var pack = SpawnPack(s, "Troll", "Greydwarf", 3, 15f);
            var farAt = Stage.Ground(pack.Center + pack.Away * 40f);
            var far = s.Spawn("Greydwarf", farAt, -pack.Away);
            if (!c.Check(pack.Leader != null && pack.Followers.Count == 3 && far != null, "could not spawn the Troll, three Greydwarfs and a far Greydwarf"))
            {
                c.Report();
                yield break;
            }
            Action holdFar = () =>
            {
                if (far != null)
                {
                    Stage.Place(far.m_character, farAt, pack.Center - farAt);
                }
            };
            yield return Wait(1f, holdFar);
            var mark = Watch.Mark();
            yield return KillLeader(s, pack, 3f, holdFar, w);
            if (!c.Check(w.Met, "the Troll killed by the player routs its three Greydwarfs"))
            {
                c.Report();
                yield break;
            }
            var routEnd = pack.KillTime + rules.RoutSeconds;
            var routLine = $"Rout: {TokenOf("Troll")} died, applied to 3 creature(s) here.";
            c.Check(Rout.LastAppliedHere == 3 && Watch.Count(routLine, mark, LogLevel.Debug) == 1,
                $"T15: the Debug log shows \"{routLine}\" once (last Rout line: \"{Watch.Last("Rout:", mark)}\")");
            c.Check(RoutUntilOf(far) == 0L, $"T17: a Greydwarf {F(Vector3.Distance(farAt, pack.DeathPos))} m from the fallen Troll does not flee");
            var f0 = pack.Followers[0];
            var f1 = pack.Followers[1];
            var f2 = pack.Followers[2];

            // T15: the fleeing follower's plate: alert icon, nothing of the mod's own (held at its spot: in plate range).
            var plateSpot = Stage.Ground(f0.transform.position);
            yield return PlateShows(f0.m_character, true, 1.5f, () =>
            {
                holdFar();
                Stage.Place(f0.m_character, plateSpot, plateSpot - pack.DeathPos);
            }, w, plate);
            c.Check(w.Met && PlateClean(plate.Data, f0.m_character),
                $"T15: a fleeing follower's plate shows the alert icon and only its name ({DescribeIcons(plate.Data)}; texts \"{PlateWords(plate.Data)}\")");

            // T18: a hit one second into the rout.
            yield return WaitUntilTime(pack.KillTime + 1f, holdFar);
            var until1 = RoutUntilOf(f1);
            Hit(f1.m_character, player, 1f, 0f, 1f);
            c.Check(RoutUntilOf(f1) == until1 && Provoked(f1, player) && Attitudes.Judge(f1, player) == Attitude.Routed,
                $"T18: a hit on a fleeing Greydwarf is recorded and leaves the rout as it was (provoked {Provoked(f1, player)}, Judge {Attitudes.Judge(f1, player)})");
            yield return Wait(1f, holdFar);
            CreatureState.TryGet(f1, out var state1);
            c.Check(f1.IsAlerted() && f1.m_targetCreature == null && state1 != null && state1.RoutEnd > Time.time,
                $"T18: a second later it still flees: alerted, no target, rout frames on (target {Who(f1.m_targetCreature)})");

            // T17: the far one comes within the radius while the rout lasts.
            c.Check(RoutUntilOf(far) == 0L, "T17: two seconds into the rout the far Greydwarf still does not flee");
            farAt = Stage.Ground(pack.DeathPos + pack.Away * 10f);
            yield return Until(() => RoutUntilOf(far) > Attitudes.Now(), 1.8f, holdFar, w);
            c.Check(w.Met && RoutUntilOf(far) == pack.Until, "T17: moved to 10 m from the fallen Troll during the rout, it joins that rout at its next check");
            s.Destroy(far);

            // Just before the end: two followers held 16 m from the player, the one that was hit 14 m away and free.
            yield return WaitUntilTime(routEnd - 0.8f, null);
            var spotA = s.Spot(here, Turn(s.Forward, -30f), 16f, true);
            var spotB = s.Spot(here, Turn(s.Forward, 30f), 16f, true);
            var spotHit = s.Spot(here, s.Forward, 14f, true);
            Stage.Place(f1.m_character, spotHit, here - spotHit);
            // T34a: a building that follower 2 goes for as soon as its rout is over.
            StaticTarget building = null;
            foreach (var pieceName in new[] { "bed", "piece_workbench", "fire_pit", "wood_wall", "piece_chest_wood" })
            {
                var piecePrefab = ZNetScene.instance.GetPrefab(pieceName);
                if (piecePrefab == null || piecePrefab.GetComponentInChildren<StaticTarget>(true) == null)
                {
                    continue;
                }
                var pieceGo = s.SpawnAny(pieceName, Stage.Ground(spotB + pack.Side * 2.5f), s.Forward);
                building = pieceGo != null ? pieceGo.GetComponentInChildren<StaticTarget>(true) : null;
                SelfTest.Note(RoutHitName, $"T34a: the building the follower goes for is a {pieceName}");
                break;
            }
            Action holdShaken = () =>
            {
                s.Noise();
                Stage.Place(f0.m_character, spotA, player.transform.position - spotA);
                Stage.Place(f2.m_character, spotB, player.transform.position - spotB);
                if (building != null && Time.time >= routEnd)
                {
                    f2.m_targetStatic = building;
                }
            };
            yield return WaitUntilTime(routEnd, holdShaken);
            yield return Until(() => !f0.IsAlerted() && !ZdoAlert(f0) && f0.m_targetCreature == null && !f2.IsAlerted() && !ZdoAlert(f2)
                                     && f2.m_targetCreature == null, 2.5f, holdShaken, w);
            c.Check(w.Met, $"T15 / T34a: within 2.5 s after the rout ends the followers 16 m from the player are unalerted (alert icon gone) with no creature target "
                           + $"({DescribePack(new List<MonsterAI> { f0, f2 }, player)})");
            if (c.Check(building != null, "T34a: a building piece (bed, workbench, ...) that a creature can go for exists"))
            {
                c.Check(w.Met && f2.m_targetStatic == building, "T34a: the follower that goes for a building when its rout ends loses the alert icon all the same "
                                                               + $"(building target kept {f2.m_targetStatic == building}, alerted {f2.IsAlerted()})");
            }
            yield return PlateShows(f0.m_character, false, 1.5f, holdShaken, w, plate);
            c.Check(w.Met && PlateClean(plate.Data, f0.m_character), $"T15: after the rout its plate shows no alert icon ({DescribeIcons(plate.Data)})");
            c.Check(Attitudes.Judge(f0, player) == Attitude.Afraid && Attitudes.Judge(f2, player) == Attitude.Afraid,
                $"T15 / T18: the followers that were not hit are afraid of the rank 0 player after the rout ({Attitudes.Judge(f0, player)} / {Attitudes.Judge(f2, player)})");
            yield return Until(() => f1.m_targetCreature == player && Attitudes.Judge(f1, player) == Attitude.Provoked, 3.5f, holdShaken, w);
            c.Check(w.Met, $"T18: the one that was hit fights the player once the rout is over (target {Who(f1.m_targetCreature)}, Judge {Attitudes.Judge(f1, player)})");

            // T15: afraid for ShakenSeconds: the player comes within 12 m, it runs with the alert icon, never attacks.
            f2.m_targetStatic = null;
            building = null;
            s.MovePlayer(spotA + (here - spotA).normalized * 8f);
            var attacked = false;
            yield return Until(() => FearOf(f0) == player && f0.IsAlerted() && ZdoAlert(f0), 2.5f, () =>
            {
                holdShaken();
                attacked |= f0.m_targetCreature == player;
            }, w);
            c.Check(w.Met && !attacked, $"T15: during its time of fear a follower runs, alerted, from the rank 0 player who comes within 12 m and never targets them "
                                        + $"(runs from {Who(FearOf(f0))}, targeted the player {attacked})");
            pack.Destroy(s);
            Rout.Clear();
            c.Report();
        }
        finally
        {
            Finish(s);
        }
    }

    // ================================================================ morale.rout-end

    private static IEnumerator RunRoutEnd()
    {
        var c = new Checks(RoutEndName);
        Stage s = null;
        var w = new Waiter();
        try
        {
            s = Stage.Begin(RoutEndName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;

            // ----- T27: Greydwarf removed from the BlackForest list, still a follower of the Troll -----
            var blackForest = MoraleRules.SplitNames(MoraleRules.DefaultHomeLists[1]).FindAll(name => name != "Greydwarf");
            var rules = NewRules(r =>
            {
                r.HomeLists[1] = string.Join(", ", blackForest.ToArray());
                r.RoutSeconds = 4f;
                r.ShakenSeconds = 6f;
                r.ProvokedSeconds = 20f;
            });
            ServerRules.TestRules = rules;
            Rout.Clear();
            var pack = SpawnPack(s, "Troll", "Greydwarf", 3, 15f);
            if (!c.Check(pack.Leader != null && pack.Followers.Count == 3, "T27: could not spawn the Troll and three Greydwarfs"))
            {
                c.Report();
                yield break;
            }
            var f0 = pack.Followers[0];
            var f1 = pack.Followers[1];
            var f2 = pack.Followers[2];
            Standing.TestOverride = new Standing.Override(8);
            var facts = CreatureState.Get(f0);
            facts.EnsureFacts(rules);
            c.Check(!facts.HasRank && Attitudes.Judge(f0, player) == Attitude.Hostile && rules.IsFollower(Hash("Troll"), Hash("Greydwarf")),
                $"T27 setup: a Greydwarf in no home biome list attacks even a rank 8 player and still follows the Troll (has a rank {facts.HasRank}, Judge {Attitudes.Judge(f0, player)})");
            Standing.TestOverride = new Standing.Override(0);
            yield return Wait(1f, null);
            yield return KillLeader(s, pack, 3f, null, w);
            if (!c.Check(w.Met && Rout.LastAppliedHere == 3, $"T27: the Troll's death routs the three Greydwarfs although they are in no home list (reached {Rout.LastAppliedHere})"))
            {
                c.Report();
                yield break;
            }
            var routEnd = pack.KillTime + rules.RoutSeconds;
            yield return WaitUntilTime(pack.KillTime + 1f, null);
            Hit(f1.m_character, player, 1f, 0f, 1f);
            c.Check(Provoked(f1, player) && Attitudes.Judge(f1, player) == Attitude.Routed,
                $"T27: the hit on a fleeing one is recorded although its kind is in no list (provoked {Provoked(f1, player)})");
            yield return WaitUntilTime(routEnd - 0.8f, null);
            var spotA = s.Spot(here, Turn(s.Forward, -30f), 16f, true);
            var spotB = s.Spot(here, Turn(s.Forward, 30f), 16f, true);
            var spotHit = s.Spot(here, s.Forward, 14f, true);
            Stage.Place(f1.m_character, spotHit, here - spotHit);
            Action holdShaken = () =>
            {
                s.Noise();
                Stage.Place(f0.m_character, spotA, player.transform.position - spotA);
                Stage.Place(f2.m_character, spotB, player.transform.position - spotB);
            };
            yield return WaitUntilTime(routEnd, holdShaken);
            yield return Until(() => f1.m_targetCreature == player && Attitudes.Judge(f1, player) == Attitude.Provoked, 3.5f, holdShaken, w);
            c.Check(w.Met, $"T27: when the rout ends the one that was hit fights the player (target {Who(f1.m_targetCreature)}, Judge {Attitudes.Judge(f1, player)})");
            yield return Until(() => !f0.IsAlerted() && !f2.IsAlerted() && f0.m_targetCreature == null && f2.m_targetCreature == null, 2.5f, holdShaken, w);
            c.Check(w.Met && Attitudes.Judge(f0, player) == Attitude.Afraid && Attitudes.Judge(f2, player) == Attitude.Afraid,
                $"T27: the others are afraid of the player after the rout: unalerted, no target ({DescribePack(new List<MonsterAI> { f0, f2 }, player)})");
            yield return WaitUntilTime(routEnd + rules.ShakenSeconds, holdShaken);
            yield return Until(() => f0.m_targetCreature == player && Attitudes.Judge(f0, player) == Attitude.Hostile, 5f, holdShaken, w);
            c.Check(w.Met, $"T27: once that time of fear is over they attack again (target {Who(f0.m_targetCreature)}, Judge {Attitudes.Judge(f0, player)})");
            pack.Destroy(s);

            // ----- T34b: ShakenSeconds 0, the player 9 m from a follower when the rout ends -----
            var noShake = NewRules(r =>
            {
                r.RoutSeconds = 4f;
                r.ShakenSeconds = 0f;
            });
            ServerRules.TestRules = noShake;
            Rout.Clear();
            var second = SpawnPack(s, "Troll", "Greydwarf", 2, 15f);
            if (!c.Check(second.Leader != null && second.Followers.Count == 2, "T34b: could not spawn the Troll and two Greydwarfs"))
            {
                c.Report();
                yield break;
            }
            yield return Wait(1f, null);
            yield return KillLeader(s, second, 3f, null, w);
            if (!c.Check(w.Met, "T34b: the Troll's death routs the two Greydwarfs"))
            {
                c.Report();
                yield break;
            }
            var g = second.Followers[0];
            var end2 = second.KillTime + noShake.RoutSeconds;
            Action follow = () =>
            {
                s.Noise();
                var off = player.transform.position - g.transform.position;
                off.y = 0f;
                s.MovePlayer(g.transform.position + (off.sqrMagnitude > 0.01f ? off.normalized : -second.Away) * 9f);
            };
            yield return WaitUntilTime(end2 - 0.8f, null);
            yield return WaitUntilTime(end2 - 0.3f, follow);
            c.Check(g.IsAlerted() && Attitudes.Judge(g, player) == Attitude.Routed, "T34b setup: the follower still flees, alerted, 0.3 s before the rout's end");
            var dropped = false;
            yield return Until(() => g.m_targetCreature == player, 4f, () =>
            {
                follow();
                dropped |= !g.IsAlerted();
            }, w);
            c.Check(w.Met && !dropped && Attitudes.Judge(g, player) == Attitude.Hostile,
                $"T34b: ShakenSeconds 0, the player 9 m from a follower when the rout ends: it turns on them and the alert never dropped in between "
                + $"(target {Who(g.m_targetCreature)}, alert dropped {dropped}, Judge {Attitudes.Judge(g, player)}, {F(w.Took)} s)");
            second.Destroy(s);
            s.MovePlayer(here);
            Rout.Clear();
            c.Report();
        }
        finally
        {
            Finish(s);
        }
    }

    // ================================================================ morale.exempt

    private static IEnumerator RunExempt()
    {
        var c = new Checks(ExemptName);
        Stage s = null;
        var w = new Waiter();
        var zones = ZoneSystem.instance;
        var env = EnvMan.instance;
        var oldEnv = env != null ? env.m_debugEnv : "";
        var local = Player.m_localPlayer;
        var hadWet = local != null && local.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectWet);
        var bossesBefore = 0f;
        var hadBossKey = zones != null && zones.GetGlobalKey(GlobalKeys.activeBosses, out bossesBefore);
        Action restoreBossKey = () =>
        {
            // An alerted boss counts itself in this world key; only its death takes it out again, and me never kill it.
            if (zones == null)
            {
                return;
            }
            var has = zones.GetGlobalKey(GlobalKeys.activeBosses, out float now);
            if (hadBossKey && (!has || now != bossesBefore))
            {
                zones.SetGlobalKey(GlobalKeys.activeBosses, bossesBefore);
            }
            else if (!hadBossKey && has)
            {
                zones.RemoveGlobalKey(GlobalKeys.activeBosses);
            }
        };
        try
        {
            s = Stage.Begin(ExemptName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            var rules = NewRules(null);
            ServerRules.TestRules = rules;
            Standing.TestOverride = new Standing.Override(8);
            // A boss near the player starts its own weather; clear sky keeps the player dry.
            if (env != null)
            {
                env.m_debugEnv = "Clear";
            }

            // ----- T20: Eikthyr, and a Boar within 60 m of it while it fights -----
            var eikSpot = s.Spot(here, s.Forward, 15f, true);
            var boarSpot = s.Spot(here, -s.Forward, 8f, true);
            var eik = s.Spawn("Eikthyr", eikSpot, here - eikSpot);
            if (c.Check(eik != null, "T20: could not spawn Eikthyr"))
            {
                MonsterAI boar = null;
                Action holdBoss = () =>
                {
                    s.Noise();
                    if (eik != null)
                    {
                        Stage.Place(eik.m_character, eikSpot, player.transform.position - eikSpot);
                    }
                    if (boar != null)
                    {
                        Stage.Place(boar.m_character, boarSpot, player.transform.position - boarSpot);
                    }
                };
                var feared = false;
                yield return Until(() => eik.IsAlerted() && eik.m_targetCreature == player, 6f, () =>
                {
                    holdBoss();
                    feared |= FearOf(eik) != null;
                }, w);
                var bossState = CreatureState.Get(eik);
                bossState.EnsureFacts(rules);
                c.Check(w.Met && !feared && bossState.Exempt && Attitudes.Judge(eik, player) == Attitude.Hostile,
                    $"T20: Eikthyr attacks a rank 8 player: alerted, the player its target, never afraid (target {Who(eik.m_targetCreature)}, alerted {eik.IsAlerted()}, "
                    + $"exempt {bossState.Exempt})");
                boar = SpawnMeadows(s, c, "Boar", boarSpot, here - boarSpot);
                if (boar != null)
                {
                    feared = false;
                    yield return Until(() => boar.m_targetCreature == player, 6f, () =>
                    {
                        holdBoss();
                        feared |= FearOf(boar) != null;
                    }, w);
                    var boarState = CreatureState.Get(boar);
                    boarState.EnsureFacts(rules);
                    c.Check(w.Met && !feared && boarState.Exempt && Attitudes.Judge(boar, player) == Attitude.Hostile,
                        $"T20: a Boar {F(Vector3.Distance(boarSpot, eikSpot))} m from the fighting Eikthyr behaves as in the normal game toward the rank 8 player: "
                        + $"it targets them and never runs (target {Who(boar.m_targetCreature)}, ran {feared}, exempt {boarState.Exempt})");
                }
                s.Destroy(eik);
                eik = null;
                restoreBossKey();
                if (boar != null)
                {
                    yield return Until(() => Attitudes.Judge(boar, player) == Attitude.Afraid && FearOf(boar) == player && boar.m_targetCreature == null, 5f, holdBoss, w);
                    c.Check(w.Met, $"T20: with the boss gone the same Boar is afraid of the rank 8 player again within 5 s (Judge {Attitudes.Judge(boar, player)}, "
                                   + $"runs from {Who(FearOf(boar))})");
                    s.Destroy(boar);
                }
            }

            // ----- T20: a raid creature (the flag every raid spawn carries: SpawnSystem sets it for event spawns) -----
            var raidSpot = s.Spot(here, s.Forward, 5f, true);
            var raider = s.Spawn("Greydwarf", raidSpot, here - raidSpot);
            if (c.Check(raider != null, "T20: could not spawn a Greydwarf"))
            {
                raider.SetEventCreature(true);
                var feared = false;
                yield return Until(() => raider.m_targetCreature == player, 5f, () =>
                {
                    s.Noise();
                    feared |= FearOf(raider) != null;
                }, w);
                var raidState = CreatureState.Get(raider);
                raidState.ForgetFacts();
                raidState.EnsureFacts(rules);
                c.Check(w.Met && !feared && raidState.Exempt && Attitudes.Judge(raider, player) == Attitude.Hostile,
                    $"T20: a raid Greydwarf attacks a rank 8 player (target {Who(raider.m_targetCreature)}, ran {feared}, exempt {raidState.Exempt})");
                s.Destroy(raider);
            }

            // ----- T20: training dummy -----
            var dummyGo = s.SpawnAny("piece_TrainingDummy", Stage.Ground(here + s.Forward * 2.5f), here - (here + s.Forward * 2.5f));
            var dummy = dummyGo != null ? dummyGo.GetComponentInChildren<Character>() : null;
            if (c.Check(dummy != null, "T20: piece_TrainingDummy has a Character to hit"))
            {
                yield return Wait(0.5f, null);
                var dummyAi = dummy.GetBaseAI() as MonsterAI;
                var health = dummy.GetHealth();
                HitWith(dummy, player, h => h.m_damage.m_slash = 5f, 1f);
                yield return null;
                c.Check(dummy.GetHealth() < health, $"T20: the training dummy takes the player's hit as usual (health {F(health)} -> {F(dummy.GetHealth())})");
                if (dummyAi != null)
                {
                    var dummyState = CreatureState.Get(dummyAi);
                    dummyState.EnsureFacts(rules);
                    c.Check(dummyState.Exempt && ProvokerCountOf(dummyAi) == 0 && Attitudes.Judge(dummyAi, player) == Attitude.Hostile,
                        $"T20: the mod leaves the training dummy alone (exempt {dummyState.Exempt}, provocation records {ProvokerCountOf(dummyAi)})");
                }
                else
                {
                    SelfTest.Note(ExemptName, "T20: the training dummy has no creature brain (MonsterAI): the mod never looks at it");
                }
                s.Destroy(dummy);
            }

            // ----- T20: a Dvergr the player hits fights back -----
            var dvergrSpot = s.Spot(here, s.Forward, 6f, true);
            var dvergr = s.Spawn("Dverger", dvergrSpot, here - dvergrSpot);
            if (c.Check(dvergr != null, "T20: could not spawn Dverger"))
            {
                Tough(dvergr.m_character);
                yield return Wait(1f, null);
                var dvergrState = CreatureState.Get(dvergr);
                dvergrState.EnsureFacts(rules);
                c.Check(dvergrState.Exempt && dvergr.m_targetCreature != player, $"T20 setup: a Dvergr is exempt and leaves the player alone until hit (exempt {dvergrState.Exempt})");
                Hit(dvergr.m_character, player, 1f, 0f, 1f);
                var feared = false;
                yield return Until(() => dvergr.m_targetCreature == player, 5f, () =>
                {
                    s.Noise();
                    feared |= FearOf(dvergr) != null;
                }, w);
                c.Check(w.Met && !feared && ProvokerCountOf(dvergr) == 0,
                    $"T20: a Dvergr the rank 8 player hits fights back as in the normal game (target {Who(dvergr.m_targetCreature)}, ran {feared}, "
                    + $"provocation records {ProvokerCountOf(dvergr)})");
                s.Destroy(dvergr);
            }

            // ----- T21: a Fuling night hunter -----
            var hunterAt = s.Spot(here, s.Forward, 16f, false);
            var hunter = SpawnMeadows(s, c, "Goblin", hunterAt, here - hunterAt);
            if (hunter != null)
            {
                Action holdHunter = () =>
                {
                    s.Noise();
                    Stage.Place(hunter.m_character, hunterAt, player.transform.position - hunterAt);
                };
                Standing.TestOverride = new Standing.Override(7);
                hunter.SetHuntPlayer(true);
                var hunted = false;
                yield return Wait(3f, () =>
                {
                    holdHunter();
                    hunted |= hunter.m_targetCreature == player;
                });
                CreatureState.TryGet(hunter, out var hunterState);
                c.Check(!hunted && hunter.m_targetCreature == null && hunterState != null && hunterState.NightHunter && hunterState.HuntStandDown && !hunter.HuntPlayer(),
                    $"T21: rank 7: a hunting Fuling 16 m away does not attack: it stands down (target {Who(hunter.m_targetCreature)}, "
                    + $"night hunter {hunterState != null && hunterState.NightHunter}, stands down {hunterState != null && hunterState.HuntStandDown})");
                yield return Wait(2f, holdHunter);
                c.Check(!hunter.IsAlerted() && hunter.m_targetCreature == null && FearOf(hunter) == null,
                    $"T21: rank 7: two seconds later it is not alerted (no forced hunt alert; too far to run) (alerted {hunter.IsAlerted()})");
                hunterAt = s.Spot(here, s.Forward, 6f, true);
                yield return Until(() => FearOf(hunter) == player && hunter.m_targetCreature == null, 3f, holdHunter, w);
                c.Check(w.Met, $"T21: rank 7: it runs when the player is within 12 m (runs from {Who(FearOf(hunter))})");
                Standing.TestOverride = new Standing.Override(6);
                yield return Until(() => hunter.m_targetCreature == player && hunter.HuntPlayer(), 8f, holdHunter, w);
                c.Check(w.Met && Attitudes.Judge(hunter, player) == Attitude.Hostile,
                    $"T21: rank 6 (one boss short for a Plains creature): it hunts the player (target {Who(hunter.m_targetCreature)}, hunting {hunter.HuntPlayer()})");
                Standing.TestOverride = new Standing.Override(7);
                ServerRules.TestRules = NewRules(r => r.NightHuntersCanBeAfraid = false);
                c.Check(hunter.HuntPlayer() && Attitudes.Judge(hunter, player) == Attitude.Hostile,
                    "T21: NightHuntersCanBeAfraid off at rank 7: it hunts at once (HuntPlayer true, Judge Hostile)");
                var feared = false;
                yield return Wait(2.5f, () =>
                {
                    holdHunter();
                    feared |= FearOf(hunter) != null;
                });
                c.Check(!feared && hunter.m_targetCreature == player, $"T21: NightHuntersCanBeAfraid off: it keeps hunting the rank 7 player (target {Who(hunter.m_targetCreature)}, ran {feared})");
                s.Destroy(hunter);
            }
            c.Report();
        }
        finally
        {
            restoreBossKey();
            if (env != null)
            {
                env.m_debugEnv = oldEnv;
            }
            if (local != null && !hadWet)
            {
                local.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectWet, true);
            }
            Finish(s);
        }
    }

    // ================================================================ morale.tame

    private static IEnumerator RunTame()
    {
        var c = new Checks(TameName);
        Stage s = null;
        var w = new Waiter();
        try
        {
            s = Stage.Begin(TameName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            ServerRules.TestRules = NewRules(null);
            Standing.TestOverride = new Standing.Override(6);
            s.Silence();

            // ----- T22: an afraid Wolf eats and tames while the player keeps away -----
            var wolfSpot = s.Spot(here, s.Forward, 25f, false);
            var wolf = SpawnMeadows(s, c, "Wolf", wolfSpot, wolfSpot - here);
            var tameable = wolf != null ? wolf.GetComponent<Tameable>() : null;
            if (!c.Check(tameable != null && wolf.m_consumeItems != null && wolf.m_consumeItems.Count > 0 && wolf.m_consumeItems[0] != null,
                    "T22: a Wolf can be tamed and eats something"))
            {
                c.Report();
                yield break;
            }
            Tough(wolf.m_character);
            var zdo = wolf.m_nview.GetZDO();
            var foodPrefab = wolf.m_consumeItems[0].gameObject;
            c.Check(Attitudes.Judge(wolf, player) == Attitude.Afraid && tameable.IsHungry(), $"T22 setup: the Wolf is afraid of the rank 6 player and hungry (Judge {Attitudes.Judge(wolf, player)})");
            var food = s.SpawnItem(foodPrefab, wolf.transform.position + wolf.transform.forward * 1.5f + Vector3.up * 0.5f);
            var nextPoke = 0f;
            var attacked = false;
            var feared = false;
            Action watchWolf = () =>
            {
                s.Silence();
                attacked |= wolf.m_targetCreature == player;
                feared |= FearOf(wolf) != null;
            };
            yield return Until(() => !tameable.IsHungry(), 25f, () =>
            {
                watchWolf();
                // Vanilla looks for food every 10 s (and needs the navmesh): me ask every second.
                if (Time.time >= nextPoke)
                {
                    nextPoke = Time.time + 1f;
                    wolf.m_consumeSearchTimer = wolf.m_consumeSearchInterval + 1f;
                }
            }, w);
            c.Check(w.Met, $"T22: the afraid Wolf 25 m from the quiet player eats the {foodPrefab.name} dropped next to it (within 25 s; took {F(w.Took)} s)");
            if (w.Met)
            {
                var left = zdo.GetFloat(ZDOVars.s_tameTimeLeft, tameable.m_tamingTime);
                yield return Until(() => zdo.GetFloat(ZDOVars.s_tameTimeLeft, tameable.m_tamingTime) < left, 4.5f, watchWolf, w);
                c.Check(w.Met && !wolf.IsAlerted(), $"T22: fed and calm, its taming goes on (time left {F(left)} s -> {F(zdo.GetFloat(ZDOVars.s_tameTimeLeft, tameable.m_tamingTime))} s)");
                // The usual taming time is 30 minutes: me jump to its last seconds.
                zdo.Set(ZDOVars.s_tameTimeLeft, 2f);
                yield return Until(() => wolf.m_character.IsTamed(), 4.5f, watchWolf, w);
                c.Check(w.Met, "T22: when its taming time is up the Wolf is tame");
            }
            c.Check(!attacked && !feared, $"T22: all along it neither attacked the player nor ran (attacked {attacked}, ran {feared})");
            s.Destroy(wolf);
            if (food != null)
            {
                s.Destroy(food);
            }

            // ----- T22: taming pauses while it runs from the player -----
            var secondSpot = s.Spot(here, s.Forward, 25f, false);
            var runAt = secondSpot;
            var second = SpawnMeadows(s, c, "Wolf", secondSpot, here - secondSpot);
            var secondTame = second != null ? second.GetComponent<Tameable>() : null;
            if (c.Check(secondTame != null, "T22: could not spawn a second Wolf"))
            {
                Tough(second.m_character);
                var zdo2 = second.m_nview.GetZDO();
                zdo2.Set(ZDOVars.s_tameLastFeeding, ZNet.instance.GetTime().Ticks); // fed (the eating is shown above)
                Action holdSecond = () => Stage.Place(second.m_character, runAt, player.transform.position - runAt);
                yield return Until(() => zdo2.GetFloat(ZDOVars.s_tameTimeLeft, secondTame.m_tamingTime) < secondTame.m_tamingTime, 4.5f, () =>
                {
                    s.Silence();
                    holdSecond();
                }, w);
                c.Check(w.Met, "T22 setup: a second, fed Wolf 25 m away is being tamed");
                runAt = s.Spot(here, s.Forward, 6f, true);
                yield return Until(() => FearOf(second) == player && second.IsAlerted(), 3f, () =>
                {
                    s.Noise();
                    holdSecond();
                }, w);
                c.Check(w.Met, $"T22: the player comes within 12 m: the Wolf runs, alerted (runs from {Who(FearOf(second))})");
                var leftRunning = zdo2.GetFloat(ZDOVars.s_tameTimeLeft, secondTame.m_tamingTime);
                var calmed = false;
                attacked = false;
                yield return Wait(6.5f, () =>
                {
                    s.Noise();
                    holdSecond();
                    calmed |= !second.IsAlerted();
                    attacked |= second.m_targetCreature == player;
                });
                var leftAfter = zdo2.GetFloat(ZDOVars.s_tameTimeLeft, secondTame.m_tamingTime);
                c.Check(!calmed && !attacked && Mathf.Approximately(leftAfter, leftRunning),
                    $"T22: while it runs (alert) the taming pauses, as in the normal game, and it never attacks (time left {F(leftRunning)} s -> {F(leftAfter)} s over 6.5 s, "
                    + $"attacked {attacked})");
                s.Destroy(second);
            }

            // ----- T23 / T33: the player's tame against an afraid Greydwarf -----
            Standing.TestOverride = new Standing.Override(4);
            var fightSpot = s.Spot(here, s.Forward, 20f, false);
            var greydwarf = s.Spawn("Greydwarf", fightSpot, here - fightSpot);
            var pet = s.Spawn("Wolf", Stage.Ground(fightSpot + s.Right * 3f), -s.Right);
            if (!c.Check(greydwarf != null && pet != null, "T23: could not spawn a Greydwarf and a Wolf"))
            {
                c.Report();
                yield break;
            }
            pet.MakeTame();
            Tough(greydwarf.m_character);
            Tough(pet.m_character);
            var wentForPlayer = false;
            var ranEarly = false;
            Action keepBack = () =>
            {
                s.Noise();
                var off = player.transform.position - greydwarf.transform.position;
                off.y = 0f;
                if (off.magnitude < 18f)
                {
                    s.MovePlayer(greydwarf.transform.position + (off.sqrMagnitude > 0.01f ? off.normalized : -s.Forward) * 21f);
                }
                wentForPlayer |= greydwarf.m_targetCreature == player;
                ranEarly |= FearOf(greydwarf) != null;
            };
            yield return Until(() => greydwarf.m_targetCreature == pet.m_character, 8f, keepBack, w);
            c.Check(w.Met && pet.m_character.IsTamed(), $"T23: the afraid Greydwarf fights the player's tamed Wolf (Greydwarf target {Who(greydwarf.m_targetCreature)}, "
                                                        + $"Wolf target {Who(pet.m_targetCreature)})");
            yield return Wait(2f, keepBack);
            c.Check(!wentForPlayer && !ranEarly, $"T23: while the (noisy) player stays about 20 m back it never goes for them and does not run (targeted the player {wentForPlayer}, ran {ranEarly})");
            Action comeClose = () =>
            {
                s.Noise();
                var off = player.transform.position - greydwarf.transform.position;
                off.y = 0f;
                s.MovePlayer(greydwarf.transform.position + (off.sqrMagnitude > 0.01f ? off.normalized : -s.Forward) * 8f);
            };
            yield return Until(() => FearOf(greydwarf) == player && greydwarf.m_targetCreature == null, 3f, comeClose, w);
            c.Check(w.Met, $"T23 / T33: the player comes within 12 m: it leaves the Wolf and runs from them (runs from {Who(FearOf(greydwarf))}, target {Who(greydwarf.m_targetCreature)})");
            // The Wolf is taken away before the hit: with it biting, the normal game could pick either of the two as
            // the Greydwarf's target, and this step is about the player.
            s.Destroy(pet);
            yield return Until(() => FearOf(greydwarf) == player && greydwarf.m_targetCreature == null, 1.5f, comeClose, w);
            c.Check(w.Met, "T33 setup: it still runs from the player, no target");
            Hit(greydwarf.m_character, player, 1f, 0f, 1f);
            c.Check(FearOf(greydwarf) == null && Provoked(greydwarf, player) && greydwarf.m_targetCreature == player && Attitudes.Judge(greydwarf, player) == Attitude.Provoked,
                $"T33: the player's hit stops its run at once: provoked, the player its target (runs from {Who(FearOf(greydwarf))}, target {Who(greydwarf.m_targetCreature)})");
            var ranAgain = false;
            yield return Wait(2.5f, () =>
            {
                comeClose();
                ranAgain |= FearOf(greydwarf) != null;
            });
            c.Check(!ranAgain && greydwarf.IsAlerted() && greydwarf.m_targetCreature == player,
                $"T33: it goes on fighting the player and does not run again (ran again {ranAgain}, target {Who(greydwarf.m_targetCreature)})");
            s.Destroy(greydwarf);
            s.MovePlayer(here);
            c.Report();
        }
        finally
        {
            Finish(s);
        }
    }

    // ================================================================ morale.toggle

    private static IEnumerator RunToggle()
    {
        var c = new Checks(ToggleName);
        Stage s = null;
        var w = new Waiter();
        var plate = new PlateRef();
        try
        {
            s = Stage.Begin(ToggleName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            var zdo = player.m_nview.GetZDO();
            var rules = NewRules(null);
            ServerRules.TestRules = rules;
            Standing.TestOverride = new Standing.Override(4);

            // ----- L01 -----
            var at = s.Spot(here, s.Forward, 5f, true);
            var g = s.Spawn("Greydwarf", at, here - at);
            if (!c.Check(g != null, "L01: could not spawn a Greydwarf"))
            {
                c.Report();
                yield break;
            }
            Action hold = () =>
            {
                s.Noise();
                Stage.Place(g.m_character, at, player.transform.position - at);
            };
            yield return Until(() => FearOf(g) == player && g.IsAlerted(), 3f, hold, w);
            c.Check(w.Met && ModStateNow() == nameof(MC.Shared.ModState.Active), $"L01 setup: the mod is active and the afraid Greydwarf runs from the player (state {ModStateNow()})");
            var mark = Watch.Mark();
            SetBlocked(true);
            var bytes = zdo.GetByteArray(CreatureKeys.Standing);
            c.Check(!Plugin.Live && ModStateNow() != nameof(MC.Shared.ModState.Active), $"L01: turned off: the mod is inactive (state {ModStateNow()})");
            c.Check(Watch.Count("Standing withdrawn:", mark, LogLevel.Info) == 1 && bytes != null && bytes.Length == 3 && bytes[1] == Standing.NoRank,
                $"L01: turned off: the log says \"Standing withdrawn: ...\" once and the player's published standing reads 'none' (bytes {Hex(bytes)})");
            var dropped = false;
            yield return Until(() => g.m_targetCreature == player, 5f, () =>
            {
                hold();
                dropped |= !g.IsAlerted();
            }, w);
            c.Check(w.Met && !dropped, $"L01: turned off: the running Greydwarf stops running and comes for the player within 5 s, still alert "
                                       + $"(target {Who(g.m_targetCreature)}, alert dropped {dropped})");
            SetBlocked(false);
            // Turning off put every test hook back (OnDeactivated): set them again.
            ServerRules.TestRules = rules;
            Standing.TestOverride = new Standing.Override(4);
            c.Check(Plugin.Live && ModStateNow() == nameof(MC.Shared.ModState.Active), $"L01: turned on: the mod is active again (state {ModStateNow()})");
            yield return Until(() => FearOf(g) == player && g.m_targetCreature == null && g.IsAlerted(), 2.5f, hold, w);
            c.Check(w.Met, $"L01: turned on: within 2.5 s it no longer attacks and runs from the close player (runs from {Who(FearOf(g))}, target {Who(g.m_targetCreature)})");
            if (w.Met)
            {
                SelfTest.Note(ToggleName, $"L01: afraid again {F(w.Took)} s after the mod came back on");
            }
            yield return PlateShows(g.m_character, true, 2f, hold, w, plate);
            c.Check(w.Met, $"L01: its plate shows the alert icon while it runs ({DescribeIcons(plate.Data)})");
            at = s.Spot(here, s.Forward, rules.FearRange + Fear.Margin + 4f, false);
            yield return Until(() => FearOf(g) == null && !g.IsAlerted() && !ZdoAlert(g), 3.5f, hold, w);
            c.Check(w.Met, $"L01: about 20 m away it calms down (alerted {g.IsAlerted()})");
            if (PlateOf(g.m_character) != null)
            {
                yield return PlateShows(g.m_character, false, 1.5f, hold, w, plate);
                c.Check(w.Met, $"L01: calm, its plate shows no icon ({DescribeIcons(plate.Data)})");
            }
            s.Destroy(g);

            // ----- L02: turned off during a rout -----
            Standing.TestOverride = new Standing.Override(0);
            var routRules = NewRules(r => r.RoutSeconds = 12f);
            ServerRules.TestRules = routRules;
            Rout.Clear();
            var pack = SpawnPack(s, "Troll", "Greydwarf", 2, 12f);
            if (!c.Check(pack.Leader != null && pack.Followers.Count == 2, "L02: could not spawn the Troll and two Greydwarfs"))
            {
                c.Report();
                yield break;
            }
            yield return Wait(1f, null);
            yield return KillLeader(s, pack, 3f, null, w);
            c.Check(w.Met, "L02 setup: the Troll's death routs the two Greydwarfs");
            yield return Wait(1f, null);
            c.Check(pack.Followers.TrueForAll(f => f != null && f.IsAlerted() && f.m_targetCreature == null && Attitudes.Judge(f, player) == Attitude.Routed),
                $"L02 setup: one second later they flee ({DescribePack(pack.Followers, player)})");
            SetBlocked(true);
            // Close to the player and noisy: the normal game makes them attack; a rout would make them ignore the player.
            var spots = new List<Vector3>();
            for (var i = 0; i < pack.Followers.Count; i++)
            {
                spots.Add(s.Spot(here, Turn(s.Forward, i == 0 ? -25f : 25f), 6f, true));
            }
            yield return Until(() => pack.Followers.TrueForAll(f => f != null && f.m_targetCreature == player), 5f, () =>
            {
                s.Noise();
                for (var i = 0; i < pack.Followers.Count; i++)
                {
                    Stage.Place(pack.Followers[i].m_character, spots[i], player.transform.position - spots[i]);
                }
            }, w);
            c.Check(w.Met && pack.Followers.TrueForAll(f => RoutUntilOf(f) > Attitudes.Now()),
                "L02: the mod turned off during the rout: the Greydwarfs stop fleeing and come for the player within 5 s, although their rout time is not over");
            SetBlocked(false);
            ServerRules.TestRules = routRules;
            Standing.TestOverride = new Standing.Override(0);
            c.Check(Plugin.Live && ModStateNow() == nameof(MC.Shared.ModState.Active), $"L02: the mod is back on (state {ModStateNow()})");
            pack.Destroy(s);
            Rout.Clear();
            c.Report();
        }
        finally
        {
            if (Plugin.TestBlocked)
            {
                SetBlocked(false);
            }
            Finish(s);
        }
    }

    // ================================================================ morale.settings

    private static IEnumerator RunSettings()
    {
        var c = new Checks(SettingsName);
        Stage s = null;
        var w = new Waiter();
        const string fakeGuid = "com.lhoffl.TruePassiveMobs";
        const string fakeOden = "mc.selftest.fake.oden";
        var addedGuid = false;
        var addedOden = false;
        try
        {
            s = Stage.Begin(SettingsName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            ServerRules.TestRules = NewRules(null);
            Standing.TestOverride = new Standing.Override(3);

            // ----- T26: Greydwarf added to the Meadows list, and a typo -----
            var spot = s.Spot(here, s.Forward, 8f, true);
            var g = SpawnMeadows(s, c, "Greydwarf", spot, here - spot);
            if (g == null)
            {
                c.Report();
                yield break;
            }
            Action hold = () =>
            {
                s.Noise();
                Stage.Place(g.m_character, spot, player.transform.position - spot);
            };
            yield return Until(() => g.m_targetCreature == player, 5f, hold, w);
            c.Check(w.Met && Attitudes.Judge(g, player) == Attitude.Hostile, "T26 setup: with the default lists a Greydwarf attacks a rank 3 player");
            c.Expect("several home biome lists");
            c.Expect("are not creatures of this game");
            var mark = Watch.Mark();
            var edited = NewRules(r => r.HomeLists[0] = r.HomeLists[0] + ", Greydwarf, Boarr");
            ServerRules.TestRules = edited;
            var warning = Watch.Last("several home biome lists", mark);
            c.Check(Watch.Count("In the creature settings", mark, LogLevel.Warning) == 1 && warning.Contains("Greydwarf") && warning.Contains("the easiest biome is used")
                    && warning.Contains("these names are not creatures of this game and are ignored: Boarr"),
                $"T26: one warning names Greydwarf (in several home biome lists, the easiest biome is used) and Boarr (not a creature of this game): \"{warning}\"");
            c.Check(PrefabTokens.UnknownNames.Contains("Boarr") && edited.NamesInSeveralHomeLists.Contains("Greydwarf") && edited.NamesInSeveralHomeLists.Count == 1,
                "T26: the name check finds exactly that typo and that double entry");
            c.Check(Attitudes.Judge(g, player) == Attitude.Afraid, $"T26: with Greydwarf in the Meadows list it is afraid of the rank 3 player at once (Judge {Attitudes.Judge(g, player)})");
            yield return Until(() => FearOf(g) == player && g.m_targetCreature == null, 3f, hold, w);
            c.Check(w.Met, $"T26: it drops the player and runs within 3 s (runs from {Who(FearOf(g))}, target {Who(g.m_targetCreature)})");
            ServerRules.TestRules = edited;
            c.Check(Watch.Count("In the creature settings", mark, LogLevel.Warning) == 1, "T26: the same settings again log no second warning");

            // Edits settle: five edits in half a second apply once, 0.75 s after the last one (what typing a name does).
            var version = ServerRules.Version;
            for (var i = 0; i < 5; i++)
            {
                ServerRules.OwnEdited();
                yield return Wait(0.1f, hold);
            }
            c.Check(ServerRules.Version == version && ServerRules.HasWork, "T26: while the edits keep coming nothing is applied yet (no warning for half-typed names)");
            var lastEdit = Time.realtimeSinceStartup;
            yield return Until(() => ServerRules.Version != version, 2f, hold, w);
            var settled = Time.realtimeSinceStartup - lastEdit;
            yield return Wait(1f, hold);
            c.Check(w.Met && ServerRules.Version == version + 1 && settled >= ServerRules.EditSettle - 0.2f && settled <= ServerRules.EditSettle + 0.6f,
                $"T26: the edits apply once, about a second after the last one (applied after {F(settled)} s, {ServerRules.Version - version} time(s))");
            s.Destroy(g);

            // RoutSeconds = 5: the next rout lasts 5 s.
            ServerRules.TestRules = NewRules(r => r.RoutSeconds = 5f);
            Standing.TestOverride = new Standing.Override(0);
            Rout.Clear();
            var routed = s.Spawn("Greydwarf", spot, here - spot);
            if (c.Check(routed != null, "T26: could not spawn a Greydwarf for the rout"))
            {
                yield return Wait(0.5f, null);
                var applied = Rout.Apply(routed.transform.position + s.Right * 3f, Hash("Troll"));
                var left = SecondsLeft(RoutUntilOf(routed));
                c.Check(applied == 1 && left > 4.5 && left <= 5.01, $"T26: RoutSeconds = 5: a rout lasts 5 s ({F(left)} s left, {applied} routed)");
                s.Destroy(routed);
            }
            Rout.Clear();

            // FearRange = 25.
            Standing.TestOverride = new Standing.Override(4);
            ServerRules.TestRules = NewRules(null);
            var farSpot = s.Spot(here, s.Forward, 20f, true);
            var far = s.Spawn("Greydwarf", farSpot, here - farSpot);
            if (c.Check(far != null, "T26: could not spawn a Greydwarf for the fear range"))
            {
                Action holdFar = () =>
                {
                    s.Noise();
                    Stage.Place(far.m_character, farSpot, player.transform.position - farSpot);
                };
                var feared = false;
                yield return Wait(2.5f, () =>
                {
                    holdFar();
                    feared |= FearOf(far) != null;
                });
                c.Check(!feared, "T26 setup: FearRange 12: an afraid Greydwarf 20 m from the noisy player does not run");
                ServerRules.TestRules = NewRules(r => r.FearRange = 25f);
                yield return Until(() => FearOf(far) == player, 3f, holdFar, w);
                c.Check(w.Met, "T26: FearRange = 25: it runs from the player 20 m away within 3 s");
                s.Destroy(far);
            }

            // ----- X09: another creature AI mod is installed (two made-up plugin entries: by GUID, by name) -----
            var infos = BepInEx.Bootstrap.Chainloader.PluginInfos;
            c.Expect("is installed and also changes when creatures attack players");
            if (!infos.ContainsKey(fakeGuid))
            {
                var info = FakePlugin(fakeGuid, "Some Passive Mobs");
                if (info != null)
                {
                    infos[fakeGuid] = info;
                    addedGuid = true;
                }
            }
            if (!infos.ContainsKey(fakeOden))
            {
                var info = FakePlugin(fakeOden, "The Mark of Oden");
                if (info != null)
                {
                    infos[fakeOden] = info;
                    addedOden = true;
                }
            }
            if (c.Check(addedGuid && addedOden, "X09: two made-up plugin entries could be added for the check"))
            {
                mark = Watch.Mark();
                Compat.Reset();
                const string tail = "is installed and also changes when creatures attack players. Both mods will run together with Creature Morale";
                c.Check(Watch.Count("TruePassiveMobs (Some Passive Mobs) " + tail, mark, LogLevel.Warning) == 1
                        && Watch.Count("The Mark of Oden " + tail, mark, LogLevel.Warning) == 1,
                    $"X09: one warning per overlapping mod, found by its GUID or by its name (warnings: {Watch.Count(tail, mark, LogLevel.Warning)}, last: \"{Watch.Last(tail, mark)}\")");
                c.Check(Watch.Count("", mark, LogLevel.Error | LogLevel.Fatal) == 0 && Plugin.Live, "X09: no error, the mod stays on");
            }
            c.Report();
        }
        finally
        {
            if (addedGuid)
            {
                BepInEx.Bootstrap.Chainloader.PluginInfos.Remove(fakeGuid);
            }
            if (addedOden)
            {
                BepInEx.Bootstrap.Chainloader.PluginInfos.Remove(fakeOden);
            }
            Finish(s);
        }
    }

    // A plugin entry as BepInEx keeps them, with only its GUID and name (all Compat reads). Null = BepInEx changed.
    private static BepInEx.PluginInfo FakePlugin(string guid, string name)
    {
        try
        {
            var info = new BepInEx.PluginInfo();
            var setter = typeof(BepInEx.PluginInfo).GetProperty("Metadata")?.GetSetMethod(true);
            if (setter == null)
            {
                return null;
            }
            setter.Invoke(info, new object[] { new BepInEx.BepInPlugin(guid, name, "1.0.0") });
            return info.Metadata != null ? info : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ================================================================ morale.sleeper, morale.bug.sleeper-sneak

    private sealed class SleeperRef
    {
        internal MonsterAI Draugr;
    }

    // T35 scene: rank 5, a Draugr asleep about 15 m from the silent player (outside its wake range), lying so that it
    // faces them with a clear line of sight (the game's sight test says it sees them, asleep or not). Draugr left
    // null = the checks already said why.
    private static IEnumerator SleeperScene(Stage s, Checks c, SleeperRef scene)
    {
        var player = s.Player;
        var here = player.transform.position;
        ServerRules.TestRules = NewRules(null);
        Standing.TestOverride = new Standing.Override(5);
        s.Silence();
        var spot = s.Spot(here, s.Forward, 15f, true);
        var d = s.Spawn("Draugr_sleeping", spot, here - spot);
        if (!c.Check(d != null, "could not spawn Draugr_sleeping"))
        {
            yield break;
        }
        yield return Wait(1.5f, s.Silence);
        Stage.Place(d.m_character, d.transform.position, player.transform.position - d.transform.position);
        // A creature that lies on the ground has its eyes low: a bump in the terrain can hide the player. Me try
        // other spots around the player until the game's sight test says it sees them.
        foreach (var angle in new[] { 30f, -30f, 60f, -60f, 90f, -90f })
        {
            if (d.CanSeeTarget(player))
            {
                break;
            }
            var other = s.Spot(here, Turn(s.Forward, angle), 15f, true);
            Stage.Place(d.m_character, other, here - other);
            yield return Wait(0.3f, s.Silence);
        }
        var distance = Dist(d, player);
        var ok = c.Check(d.IsSleeping() && !d.IsAlerted() && distance > d.m_wakeupRange && Attitudes.Judge(d, player) == Attitude.Afraid,
            $"setup: an afraid Draugr asleep {F(distance)} m from the rank 5 player, outside its wake range ({F(d.m_wakeupRange)} m) "
            + $"(asleep {d.IsSleeping()}, alerted {d.IsAlerted()}, Judge {Attitudes.Judge(d, player)})");
        ok &= c.Check(d.CanSeeTarget(player), "setup: it lies facing the player with a clear line of sight (the game's sight test says it sees them, asleep or not)");
        if (ok)
        {
            scene.Draugr = d;
        }
    }

    // T35, the part that works: the player's hit on the sleeping afraid Draugr wakes it, provokes it and it fights.
    // (The sneak attack of that hit is checked alone in morale.bug.sleeper-sneak: the mod takes it away.)
    private static IEnumerator RunSleeper()
    {
        var c = new Checks(SleeperName);
        Stage s = null;
        var w = new Waiter();
        var scene = new SleeperRef();
        try
        {
            s = Stage.Begin(SleeperName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            yield return SleeperScene(s, c, scene);
            var d = scene.Draugr;
            if (d == null)
            {
                c.Report();
                yield break;
            }
            Tough(d.m_character);
            Hit(d.m_character, player, 5f, 0f, 3f);
            c.Check(Provoked(d, player), "T35: the hit provokes it");
            c.Check(!d.IsSleeping() && d.IsAlerted(), $"T35: the hit wakes it up, alerted (asleep {d.IsSleeping()}, alerted {d.IsAlerted()})");
            var feared = false;
            var lost = false;
            yield return Until(() => d.m_targetCreature == player && Attitudes.Judge(d, player) == Attitude.Provoked, 3f, () =>
            {
                s.Silence();
                feared |= FearOf(d) != null;
            }, w);
            c.Check(w.Met, $"T35: it fights the player within 3 s: the player is its target (target {Who(d.m_targetCreature)}, Judge {Attitudes.Judge(d, player)})");
            yield return Wait(2f, () =>
            {
                s.Silence();
                feared |= FearOf(d) != null;
                lost |= d.m_targetCreature != player || !d.IsAlerted();
            });
            c.Check(!feared && !lost && !d.IsSleeping(),
                $"T35: for the next 2 s it keeps the player as target, alerted, and never runs (ran {feared}, lost the target or the alert {lost})");
            s.Destroy(d);
            c.Report();
        }
        finally
        {
            Finish(s);
        }
    }

    // T35, the sneak attack alone: REAL BUG of the mod, this test fails until the mod is fixed. Vanilla gives a sneak
    // attack on a sleeping creature (not alerted). The mod removes the bonus from an afraid creature that "can see" the
    // player (Provocation.OnHit, E3), and the game's sight test does not know about sleep: a sleeper that faces the
    // player loses the sneak attack. README and design E3: only a creature that watches you walk up.
    private static IEnumerator RunBugSleeperSneak()
    {
        var c = new Checks(BugSleeperSneakName);
        Stage s = null;
        var scene = new SleeperRef();
        try
        {
            s = Stage.Begin(BugSleeperSneakName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            yield return SleeperScene(s, c, scene);
            var d = scene.Draugr;
            if (d == null)
            {
                c.Report();
                yield break;
            }
            var before = d.m_character.m_backstabTime;
            Hit(d.m_character, player, 1f, 0f, 3f);
            // Vanilla sets this time in the same block that multiplies the damage and plays the sneak-attack effect
            // (Character.RPC_Damage).
            c.Check(d.m_character.m_backstabTime != before,
                "T35: a hit with a sneak-attack bonus on a sleeping afraid Draugr that faces the player is a sneak attack, as in the normal game (a sleeper watches nobody)");
            s.Destroy(d);
            c.Report();
        }
        finally
        {
            Finish(s);
        }
    }

    // ================================================================ morale.bug.rank-message

    // T04, second half as TESTING.md words it: BossesAhead = 0 set after the first Eikthyr kill, then a second Eikthyr
    // kill brings the rank-up message. The mod shows that message only for a kill that raises the boss rank (design
    // E5; morale.kills checks that as T37), so this fails until the user picks: the item's text or the mod. Alone here
    // so the other checks stay green. Kills played through the Debug kill hook: no boss is killed.
    private static IEnumerator RunBugRankMessage()
    {
        var c = new Checks(BugRankMessageName);
        var w = new Waiter();
        try
        {
            var hud = MessageHud.instance;
            if (!c.Check(Player.m_localPlayer != null && hud != null && !Hud.IsUserHidden(), "no local player, or the HUD is hidden or missing: messages cannot be checked"))
            {
                c.Report();
                yield break;
            }
            ResetHooks();
            ServerRules.TestRules = NewRules(null);
            Standing.TestMessages = true;
            Standing.TestOverride = new Standing.Override(0);
            Standing.TestKill(new Standing.Override(1));
            var first = Standing.MessagePending;
            ServerRules.TestRules = NewRules(r => r.BossesAhead = 0);
            Standing.TestKill(new Standing.Override(1));
            c.Check(!first && Standing.MessagePending,
                "T04 (second half, as written): after BossesAhead = 0, a second Eikthyr kill is followed by \"Weaker creatures now keep out of your way\" "
                + $"(message due after the first kill with the defaults: {first}; message due after the second kill: {Standing.MessagePending})");
            if (Standing.MessagePending)
            {
                // Let it show while the hooks are still on: nothing pops up after the test.
                yield return Until(() => !Standing.MessagePending, Standing.RankMessageDelay + 1f, null, w);
            }
            c.Report();
        }
        finally
        {
            ResetHooks();
        }
    }

    // ================================================================ morale.spawn-biome

    // T31, the behaviour side (the Judge side on more biomes is in morale.calm, SpawnBiomeSteps): one Skeleton held
    // 8 m from the noisy player, its spawn point moved to a real Mountains point, then to a real Plains point (what
    // `goto` + `spawn` there gives it: vanilla keeps the place a creature spawned at, the mod reads its biome).
    private static IEnumerator RunSpawnBiome()
    {
        var c = new Checks(SpawnBiomeName);
        Stage s = null;
        var w = new Waiter();
        try
        {
            s = Stage.Begin(SpawnBiomeName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            ServerRules.TestRules = NewRules(null);
            Standing.TestOverride = new Standing.Override(5);
            var mountain = BiomePoint(Heightmap.Biome.Mountain);
            var plains = BiomePoint(Heightmap.Biome.Plains);
            if (!c.Check(mountain != Vector3.zero && plains != Vector3.zero, "T31: this world has no Mountains or no Plains point within 10 km of its centre"))
            {
                c.Report();
                yield break;
            }
            var spot = s.Spot(here, s.Forward, 8f, true);
            var sk = s.Spawn("Skeleton", spot, here - spot);
            if (!c.Check(sk != null, "T31: could not spawn a Skeleton"))
            {
                c.Report();
                yield break;
            }
            Tough(sk.m_character);
            var state = CreatureState.Get(sk);
            Action hold = () =>
            {
                s.Noise();
                Stage.Place(sk.m_character, spot, player.transform.position - spot);
            };
            Action<Vector3> spawnedAt = point =>
            {
                sk.m_spawnPoint = point;
                sk.m_nview.GetZDO().Set(ZDOVars.s_spawnPoint, point);
                state.ForgetSpawnLevel();
            };

            // Mountains. Rank 5: it attacks (a Skeleton that spawned here, in the Meadows, would be afraid from rank 4).
            spawnedAt(mountain);
            var feared = false;
            yield return Until(() => sk.m_targetCreature == player, 6f, () =>
            {
                hold();
                feared |= FearOf(sk) != null;
            }, w);
            c.Check(w.Met && !feared && Attitudes.Judge(sk, player) == Attitude.Hostile,
                $"T31: a Skeleton that spawned in the Mountains attacks a rank 5 player: it targets them within 6 s and never runs "
                + $"(target {Who(sk.m_targetCreature)}, ran {feared}, Judge {Attitudes.Judge(sk, player)}, its rank {state.Rank})");
            // Rank 6: afraid, it runs.
            Standing.TestOverride = new Standing.Override(6);
            yield return Until(() => FearOf(sk) == player && sk.m_targetCreature == null && sk.IsAlerted(), 4f, hold, w);
            c.Check(w.Met && Attitudes.Judge(sk, player) == Attitude.Afraid,
                $"T31: rank 6: the Mountains Skeleton is afraid: it drops the player and runs from them within 4 s "
                + $"(runs from {Who(FearOf(sk))}, target {Who(sk.m_targetCreature)}, Judge {Attitudes.Judge(sk, player)})");
            var picked = false;
            yield return Wait(1f, () =>
            {
                hold();
                picked |= sk.m_targetCreature == player;
            });
            c.Check(!picked, "T31: rank 6: the Mountains Skeleton never targets the player while it runs");

            // Plains, still rank 6: it attacks.
            spawnedAt(plains);
            feared = false;
            var since = Time.time;
            yield return Until(() => sk.m_targetCreature == player && FearOf(sk) == null, 6f, hold, w);
            c.Check(w.Met && Attitudes.Judge(sk, player) == Attitude.Hostile,
                $"T31: a Skeleton that spawned in the Plains attacks a rank 6 player: it stops running and targets them within 6 s "
                + $"(target {Who(sk.m_targetCreature)}, runs from {Who(FearOf(sk))}, Judge {Attitudes.Judge(sk, player)}, its rank {state.Rank})");
            yield return Wait(1.5f, () =>
            {
                hold();
                feared |= FearOf(sk) != null;
            });
            c.Check(!feared && sk.m_targetCreature == player, $"T31: rank 6: the Plains Skeleton keeps the player as target and does not run (ran {feared}, {F(Time.time - since)} s)");
            // Rank 7: afraid.
            Standing.TestOverride = new Standing.Override(7);
            yield return Until(() => FearOf(sk) == player && sk.m_targetCreature == null, 4f, hold, w);
            c.Check(w.Met && Attitudes.Judge(sk, player) == Attitude.Afraid,
                $"T31: rank 7: the Plains Skeleton is afraid: it drops the player and runs within 4 s (runs from {Who(FearOf(sk))}, target {Who(sk.m_targetCreature)})");
            s.Destroy(sk);
            c.Report();
        }
        finally
        {
            Finish(s);
        }
    }

    // ================================================================ morale.handover

    // What this game does with a creature it takes over from another game (M03, M05, M13: the taking game's side). The
    // other owner is an id that is not this game for 0.6 s: nothing runs the creature then, its ZDO keeps the state.
    private static IEnumerator RunHandover()
    {
        var c = new Checks(HandoverName);
        Stage s = null;
        var w = new Waiter();
        try
        {
            s = Stage.Begin(HandoverName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            var rules = NewRules(r => r.ProvokedSeconds = 6f);
            ServerRules.TestRules = rules;
            Standing.TestOverride = new Standing.Override(8);

            // ----- M13: a running creature -----
            var at = s.Spot(here, s.Forward, 6f, true);
            var runner = s.Spawn("Greydwarf", at, here - at);
            if (!c.Check(runner != null, "M13: could not spawn a Greydwarf"))
            {
                c.Report();
                yield break;
            }
            var attacked = false;
            Action hold = () =>
            {
                s.Noise();
                Stage.Place(runner.m_character, at, player.transform.position - at);
                attacked |= runner.m_targetCreature == player;
            };
            yield return Until(() => FearOf(runner) == player && runner.IsAlerted() && ZdoAlert(runner), 3f, hold, w);
            c.Check(w.Met, "M13 setup: the Greydwarf runs from the rank 8 player 6 m away");
            attacked = false;
            yield return GiveAway(HandoverName, "M13", 0.6f, hold, runner);
            yield return Until(() => Gained(runner) && FearOf(runner) == player && runner.IsAlerted(), 1.8f, hold, w);
            c.Check(w.Met && !attacked, $"M13: taken over while it runs: it runs from the player again within 1.8 s and never attacks "
                                        + $"(take-over seen {Gained(runner)}, runs from {Who(FearOf(runner))}, attacked {attacked})");
            at = s.Spot(here, s.Forward, rules.FearRange + Fear.Margin + 4f, false);
            yield return Until(() => FearOf(runner) == null && !runner.IsAlerted() && !ZdoAlert(runner), 3.5f, hold, w);
            c.Check(w.Met && !attacked, $"M13: about 20 m away it calms down, alert icon gone (alerted {runner.IsAlerted()})");

            // Taken over mid-run with the player already far: the alert the other game raised still goes.
            at = s.Spot(here, s.Forward, 6f, true);
            yield return Until(() => FearOf(runner) == player && runner.IsAlerted(), 3f, hold, w);
            c.Check(w.Met, "M13 setup: it runs from the close player again");
            // The player steps back to 17 m (beyond the fear range and its margin, inside the creature's view range)
            // at the moment the other game has it.
            var runnerSpot = at;
            s.MovePlayer(runnerSpot + (here - runnerSpot).normalized * (rules.FearRange + Fear.Margin + 1f));
            yield return GiveAway(HandoverName, "M13", 0.6f, hold, runner);
            yield return Until(() => Gained(runner) && !runner.IsAlerted() && !ZdoAlert(runner) && FearOf(runner) == null, 4f, hold, w);
            c.Check(w.Met && !attacked, $"M13: taken over alerted with the player {F(Dist(runner, player))} m away: the alert is taken back within 4 s "
                                        + $"(alerted {runner.IsAlerted()}, take-over seen {Gained(runner)}, its view range {F(runner.m_viewRange)} m)");
            s.Destroy(runner);
            s.MovePlayer(here);

            // ----- M03: a provocation -----
            var fightSpot = s.Spot(here, s.Forward, 5f, true);
            var fighter = s.Spawn("Greydwarf", fightSpot, here - fightSpot);
            if (!c.Check(fighter != null, "M03: could not spawn a Greydwarf"))
            {
                c.Report();
                yield break;
            }
            Tough(fighter.m_character);
            Action holdFighter = () =>
            {
                s.Noise();
                Stage.Place(fighter.m_character, fightSpot, player.transform.position - fightSpot);
            };
            yield return Wait(0.8f, holdFighter);
            Hit(fighter.m_character, player, 1f, 0f, 1f);
            c.Check(Provoked(fighter, player) && fighter.m_targetCreature == player, "M03 setup: the rank 8 player's hit provokes it and it targets them");
            var hitTime = Time.time;
            yield return GiveAway(HandoverName, "M03", 0.6f, holdFighter, fighter);
            var ran = false;
            yield return Until(() => Gained(fighter) && fighter.m_targetCreature == player, 3.5f, () =>
            {
                holdFighter();
                ran |= FearOf(fighter) != null;
            }, w);
            c.Check(w.Met && !ran && Attitudes.Judge(fighter, player) == Attitude.Provoked,
                $"M03: taken over, it keeps fighting the player who hit it (target {Who(fighter.m_targetCreature)}, ran {ran}, Judge {Attitudes.Judge(fighter, player)})");
            yield return Until(() => !Provoked(fighter, player) && FearOf(fighter) == player && fighter.m_targetCreature == null,
                rules.ProvokedSeconds + 4f, holdFighter, w);
            var since = Time.time - hitTime;
            c.Check(w.Met && since >= rules.ProvokedSeconds - 0.2f,
                $"M03: {F(rules.ProvokedSeconds)} s after the hit the provocation ends and it runs from the close player (after {F(since)} s, runs from {Who(FearOf(fighter))})");
            s.Destroy(fighter);

            // ----- M05: a rout -----
            Standing.TestOverride = new Standing.Override(0);
            var routRules = NewRules(r =>
            {
                r.RoutSeconds = 6f;
                r.ShakenSeconds = 10f;
                r.ProvokedSeconds = 20f;
            });
            ServerRules.TestRules = routRules;
            Rout.Clear();
            var pack = SpawnPack(s, "Troll", "Greydwarf", 2, 15f);
            if (!c.Check(pack.Leader != null && pack.Followers.Count == 2, "M05: could not spawn the Troll and two Greydwarfs"))
            {
                c.Report();
                yield break;
            }
            yield return Wait(1f, null);
            yield return KillLeader(s, pack, 3f, null, w);
            if (!c.Check(w.Met, "M05 setup: the Troll's death routs the two Greydwarfs"))
            {
                c.Report();
                yield break;
            }
            var f0 = pack.Followers[0];
            var f1 = pack.Followers[1];
            var routEnd = pack.KillTime + routRules.RoutSeconds;
            yield return WaitUntilTime(pack.KillTime + 1f, null);
            Hit(f0.m_character, player, 1f, 0f, 1f);
            var until0 = RoutUntilOf(f0);
            var from0 = RoutFromOf(f0);
            c.Check(Provoked(f0, player), "M05 setup: the player's hit on a fleeing follower is recorded");
            yield return GiveAway(HandoverName, "M05", 0.6f, null, f0, f1);
            yield return Until(() =>
            {
                CreatureState.TryGet(f0, out var a);
                CreatureState.TryGet(f1, out var b);
                return Gained(f0) && Gained(f1) && a != null && b != null && a.RoutEnd > Time.time && b.RoutEnd > Time.time && f0.IsAlerted() && f1.IsAlerted();
            }, 1.8f, null, w);
            c.Check(w.Met && RoutUntilOf(f0) == until0 && Vector3.Distance(RoutFromOf(f0), from0) < 0.01f && Provoked(f0, player)
                    && f0.m_targetCreature == null && f1.m_targetCreature == null,
                $"M05: taken over mid-rout, they go on fleeing from the same place until the same time, and the hit is still recorded "
                + $"(take-over seen {Gained(f0)}/{Gained(f1)}, rout end unchanged {RoutUntilOf(f0) == until0}, hit recorded {Provoked(f0, player)})");
            yield return WaitUntilTime(routEnd - 0.8f, null);
            var hitSpot = s.Spot(here, Turn(s.Forward, -25f), 14f, true);
            var otherSpot = s.Spot(here, Turn(s.Forward, 25f), 16f, true);
            Stage.Place(f0.m_character, hitSpot, here - hitSpot);
            Action holdOther = () =>
            {
                s.Noise();
                Stage.Place(f1.m_character, otherSpot, player.transform.position - otherSpot);
            };
            yield return WaitUntilTime(routEnd, holdOther);
            yield return Until(() => f0.m_targetCreature == player && Attitudes.Judge(f0, player) == Attitude.Provoked, 3.5f, holdOther, w);
            c.Check(w.Met, $"M05: when the rout ends the one the player hit fights them (target {Who(f0.m_targetCreature)}, Judge {Attitudes.Judge(f0, player)})");
            yield return Until(() => !f1.IsAlerted() && f1.m_targetCreature == null, 2.5f, holdOther, w);
            c.Check(w.Met && Attitudes.Judge(f1, player) == Attitude.Afraid, $"M05: the other one is afraid of the player after the rout (Judge {Attitudes.Judge(f1, player)}, alerted {f1.IsAlerted()})");
            pack.Destroy(s);
            Rout.Clear();
            c.Report();
        }
        finally
        {
            Finish(s);
        }
    }
}
#endif
