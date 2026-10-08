#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.CreaturesMoraleMod;

// Debug build only. More in-world self tests (TESTING.md items in brackets), same stage and hooks as SelfTests.cs:
//   morale.kinds     Boar and Neck at rank 3: calm when far, run with the alert icon when close, calm again, plate
//                    shows the name only (T01); rank 2: they attack (T02); rank 2 -> 3 while a Boar chases (T09)
//   morale.ranks     stars (T07), ranks across biomes at rank 6 (T08), elites one boss later (T32)
//   morale.rest      afraid Greydwarfs near a campfire and a bed: Resting then Rested, Bed.CheckEnemies, no combat
//                    music; hostile ones stop all of it (T03, X04)
//   morale.kills     rank from a boss kill and its log line (T04), rank-up message only on a kill that raises the rank
//                    (T37), real credited kills: kill step message, kill bonus, at most one boss early, messages off
//                    (T05, T06, T25, X03)
//   morale.missiles  real arrow, thrown spear, Ooze bomb and harpoon projectiles (T11, T12, X06)
// The others are in SelfTestsWorld2.cs. Me never write config.
internal static partial class SelfTests
{
    private const string KindsName = "morale.kinds";
    private const string RanksName = "morale.ranks";
    private const string RestName = "morale.rest";
    private const string KillsName = "morale.kills";
    private const string MissilesName = "morale.missiles";

    private static Vector3 Turn(Vector3 direction, float degrees) => Quaternion.Euler(0f, degrees, 0f) * direction;

    // ================================================================ morale.kinds

    private static IEnumerator RunKinds()
    {
        var c = new Checks(KindsName);
        Stage s = null;
        var w = new Waiter();
        try
        {
            s = Stage.Begin(KindsName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            var rules = NewRules(null);
            ServerRules.TestRules = rules;
            var plate = new PlateRef();
            var kinds = new[] { "Boar", "Neck" };

            // ----- T01: rank 3 (Bonemass), Meadows creatures are afraid -----
            foreach (var prefab in kinds)
            {
                Standing.TestOverride = new Standing.Override(3);
                var at = s.Spot(here, s.Forward, 25f, true);
                var ai = SpawnMeadows(s, c, prefab, at, here - at);
                if (ai == null)
                {
                    continue;
                }
                var ch = ai.m_character;
                Action hold = () =>
                {
                    s.Noise();
                    Stage.Place(ch, at, player.transform.position - at);
                };
                var ran = false;
                var alerted = false;
                var picked = false;
                yield return Wait(3f, () =>
                {
                    hold();
                    ran |= FearOf(ai) != null;
                    alerted |= ai.IsAlerted();
                    picked |= ai.m_targetCreature != null;
                });
                c.Check(Attitudes.Judge(ai, player) == Attitude.Afraid, $"T01 {prefab}: afraid of a rank 3 player (Judge {Attitudes.Judge(ai, player)})");
                c.Check(!ran && !alerted && !picked && !ZdoAlert(ai),
                    $"T01 {prefab}: 25 m from the noisy player it neither runs nor attacks and is not alerted (ran {ran}, alerted {alerted}, picked a target {picked})");
                if (PlateOf(ch) != null)
                {
                    yield return PlateShows(ch, false, 1.5f, hold, w, plate);
                    c.Check(w.Met && PlateClean(plate.Data, ch),
                        $"T01 {prefab}: far away its shown plate has no alert icon, no aware icon and only its name ({DescribeIcons(plate.Data)}; texts \"{PlateWords(plate.Data)}\")");
                }

                // The player comes within FearRange and is heard: it runs, alert icon on.
                at = s.Spot(here, s.Forward, 8f, true);
                yield return Until(() => FearOf(ai) == player && ai.IsAlerted() && ai.m_targetCreature == null && ZdoAlert(ai), 3f, hold, w);
                c.Check(w.Met, $"T01 {prefab}: 8 m from the noisy player it runs from them within 3 s, alerted, no target (runs from {Who(FearOf(ai))}, "
                               + $"alerted {ai.IsAlerted()}, target {Who(ai.m_targetCreature)})");
                // X02: for MC Encyclopedia this creature is "never met" from here on, until its plate is shown.
                var encyclopedia = EncyclopediaOn();
                var metBefore = encyclopedia ? ForgetInEncyclopedia(player, ch.m_name) : null;
                yield return PlateShows(ch, true, 2f, hold, w, plate);
                c.Check(w.Met && PlateClean(plate.Data, ch),
                    $"T01 {prefab}: while it runs its plate shows the alert icon, no aware icon and only its name ({DescribeIcons(plate.Data)}; texts \"{PlateWords(plate.Data)}\")");
                if (encyclopedia)
                {
                    yield return null;
                    var met = MetInEncyclopedia(player, ch.m_name);
                    c.Check(met, $"X02 {prefab}: an afraid creature whose plate is shown while it runs from the player counts as met for MC Encyclopedia");
                    if (!met && metBefore != null)
                    {
                        player.m_customData[EncyclopediaSeenKey] = metBefore;
                    }
                }
                else
                {
                    SelfTest.Note(KindsName, "X02: MC Encyclopedia is not active in this run; its 'met' record is not checked");
                }

                // Let go: it runs away, never attacks, the player is never sensed.
                var startDist = Dist(ai, player);
                picked = false;
                var dropped = false;
                var sensed = false;
                yield return Wait(2.5f, () =>
                {
                    s.Noise();
                    picked |= ai.m_targetCreature != null;
                    dropped |= !ai.IsAlerted() && FearOf(ai) == player;
                    sensed |= player.IsSensed();
                });
                var ranDist = Dist(ai, player);
                if (ranDist <= startDist + 1f && !ai.FoundPath())
                {
                    SelfTest.Note(KindsName, $"T01 {prefab}: no navmesh path to run on here ({F(startDist)} m -> {F(ranDist)} m); the running distance is not checked");
                }
                else
                {
                    c.Check(ranDist > startDist + 1f, $"T01 {prefab}: it runs away from the player ({F(startDist)} m -> {F(ranDist)} m in 2.5 s)");
                }
                c.Check(!picked && !sensed && !dropped,
                    $"T01 {prefab}: while it runs it never picks a target, never drops the alert and the player is never sensed (picked {picked}, sensed {sensed}, alert dropped {dropped})");

                // About 16 m away: calm, icon off.
                at = s.Spot(here, s.Forward, rules.FearRange + Fear.Margin + 4f, false);
                yield return Until(() => FearOf(ai) == null && !ai.IsAlerted() && ai.m_targetCreature == null, 3.5f, hold, w);
                c.Check(w.Met && !ZdoAlert(ai), $"T01 {prefab}: {F(Dist(ai, player))} m away it stops running and calms within 3.5 s, ZDO alert off "
                                                + $"(runs from {Who(FearOf(ai))}, alerted {ai.IsAlerted()})");
                if (PlateOf(ch) != null)
                {
                    yield return PlateShows(ch, false, 1.5f, hold, w, plate);
                    c.Check(w.Met && PlateClean(plate.Data, ch), $"T01 {prefab}: calm again, its plate has no icon and only its name ({DescribeIcons(plate.Data)})");
                }
                else
                {
                    SelfTest.Note(KindsName, $"T01 {prefab}: no plate {F(Dist(ai, player))} m away; the calm look is checked through the alert state only");
                }
                s.Destroy(ai);
            }

            // ----- T02: rank 2 (The Elder), one boss short: normal game -----
            Standing.TestOverride = new Standing.Override(2);
            foreach (var prefab in kinds)
            {
                var spot = s.Spot(here, s.Forward, 4f, true);
                var ai = SpawnMeadows(s, c, prefab, spot, here - spot);
                if (ai == null)
                {
                    continue;
                }
                var feared = false;
                yield return Until(() => ai.m_targetCreature == player && ai.HaveTarget() && player.IsSensed(), 5f, () =>
                {
                    s.Noise();
                    feared |= FearOf(ai) != null;
                }, w);
                c.Check(w.Met && !feared && Attitudes.Judge(ai, player) == Attitude.Hostile,
                    $"T02 {prefab}: at rank 2 it notices and targets the player within 5 s as in the normal game (target {Who(ai.m_targetCreature)}, "
                    + $"player sensed {player.IsSensed()}, ran {feared}, Judge {Attitudes.Judge(ai, player)})");
                s.Destroy(ai);
            }

            // ----- T09: the standing changes while a Boar chases -----
            var chaseSpot = s.Spot(here, s.Forward, 4f, true);
            var chaser = SpawnMeadows(s, c, "Boar", chaseSpot, here - chaseSpot);
            if (chaser != null)
            {
                yield return Until(() => chaser.m_targetCreature == player, 5f, s.Noise, w);
                c.Check(w.Met, "T09 setup: at rank 2 the Boar chases the player");
                Standing.TestOverride = new Standing.Override(3);
                yield return Until(() => FearOf(chaser) == player && chaser.m_targetCreature == null, 2.5f, s.Noise, w);
                c.Check(w.Met, $"T09: rank 3 set mid-chase: the Boar drops the player and runs within 2.5 s (runs from {Who(FearOf(chaser))}, "
                               + $"target {Who(chaser.m_targetCreature)})");
                if (w.Met)
                {
                    SelfTest.Note(KindsName, $"T09: the Boar turned {F(w.Took)} s after the standing changed");
                }
                var away = s.Spot(here, s.Forward, rules.FearRange + Fear.Margin + 4f, false);
                yield return Until(() => FearOf(chaser) == null && !chaser.IsAlerted() && !ZdoAlert(chaser), 3.5f, () =>
                {
                    s.Noise();
                    Stage.Place(chaser.m_character, away, player.transform.position - away);
                }, w);
                c.Check(w.Met, $"T09: about 20 m away it calms down within 3.5 s (alerted {chaser.IsAlerted()}, runs from {Who(FearOf(chaser))})");
                s.Destroy(chaser);
            }
            c.Report();
        }
        finally
        {
            Finish(s);
        }
    }

    // ================================================================ morale.ranks

    private static IEnumerator RunRanks()
    {
        var c = new Checks(RanksName);
        Stage s = null;
        var w = new Waiter();
        try
        {
            s = Stage.Begin(RanksName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            ServerRules.TestRules = NewRules(null);

            // ----- T07: stars. Rank 4: a 2-star Greydwarf attacks, a plain one next to it runs -----
            Standing.TestOverride = new Standing.Override(4);
            var starSpot = s.Spot(here, Turn(s.Forward, -25f), 6f, true);
            var plainSpot = s.Spot(here, Turn(s.Forward, 25f), 6f, true);
            var star = SpawnMeadows(s, c, "Greydwarf", starSpot, here - starSpot);
            var plain = SpawnMeadows(s, c, "Greydwarf", plainSpot, here - plainSpot);
            if (star != null && plain != null)
            {
                star.m_character.SetLevel(3);
                Action hold = () =>
                {
                    s.Noise();
                    Stage.Place(star.m_character, starSpot, player.transform.position - starSpot);
                    Stage.Place(plain.m_character, plainSpot, player.transform.position - plainSpot);
                };
                var plainPicked = false;
                yield return Until(() => star.m_targetCreature == player && FearOf(plain) == player, 5f, () =>
                {
                    hold();
                    plainPicked |= plain.m_targetCreature == player;
                }, w);
                c.Check(w.Met && !plainPicked, $"T07: rank 4, side by side: the 2-star Greydwarf targets the player (target {Who(star.m_targetCreature)}), "
                                               + $"the plain one runs from them and never targets them (runs from {Who(FearOf(plain))}, targeted the player {plainPicked})");
                c.Check(Attitudes.Judge(star, player) == Attitude.Hostile && Attitudes.Judge(plain, player) == Attitude.Afraid,
                    $"T07: Judge says Hostile for the 2-star one and Afraid for the plain one ({Attitudes.Judge(star, player)} / {Attitudes.Judge(plain, player)})");
            }
            s.Destroy(star);
            s.Destroy(plain);

            // Rank 5: a 2-star Boar (Meadows rank 3 + 2 stars) is afraid; at rank 4 it is not.
            var boarSpot = s.Spot(here, s.Forward, 6f, true);
            var starBoar = SpawnMeadows(s, c, "Boar", boarSpot, here - boarSpot);
            if (starBoar != null)
            {
                starBoar.m_character.SetLevel(3);
                c.Check(Attitudes.Judge(starBoar, player) == Attitude.Hostile, "T07: a 2-star Boar still attacks a rank 4 player");
                Standing.TestOverride = new Standing.Override(5);
                c.Check(Attitudes.Judge(starBoar, player) == Attitude.Afraid, "T07: a 2-star Boar is afraid of a rank 5 player");
                yield return Until(() => FearOf(starBoar) == player && starBoar.m_targetCreature == null, 3.5f, () =>
                {
                    s.Noise();
                    Stage.Place(starBoar.m_character, boarSpot, player.transform.position - boarSpot);
                }, w);
                c.Check(w.Met, $"T07: the 2-star Boar runs from the rank 5 player within 3.5 s (runs from {Who(FearOf(starBoar))}, target {Who(starBoar.m_targetCreature)})");
                s.Destroy(starBoar);
            }

            // ----- T08: rank 6 in the Meadows, one creature at a time -----
            Standing.TestOverride = new Standing.Override(6);
            foreach (var prefab in new[] { "Troll", "Draugr", "Wolf", "Goblin", "GoblinBrute" })
            {
                var afraid = prefab != "Goblin" && prefab != "GoblinBrute";
                var spot = s.Spot(here, s.Forward, 8f, true);
                var ai = SpawnMeadows(s, c, prefab, spot, here - spot);
                if (ai == null)
                {
                    continue;
                }
                var state = CreatureState.Get(ai);
                Action hold = () =>
                {
                    s.Noise();
                    Stage.Place(ai.m_character, spot, player.transform.position - spot);
                };
                var judged = Attitudes.Judge(ai, player);
                c.Check(judged == (afraid ? Attitude.Afraid : Attitude.Hostile),
                    $"T08 {prefab}: a rank 6 player in the Meadows: Judge {judged}, expected {(afraid ? "Afraid" : "Hostile")} (its rank {state.Rank})");
                if (afraid)
                {
                    var picked = false;
                    yield return Until(() => FearOf(ai) == player && ai.m_targetCreature == null, 3.5f, hold, w);
                    c.Check(w.Met, $"T08 {prefab}: it runs from the player within 3.5 s (runs from {Who(FearOf(ai))}, target {Who(ai.m_targetCreature)})");
                    yield return Wait(1f, () =>
                    {
                        hold();
                        picked |= ai.m_targetCreature == player;
                    });
                    c.Check(!picked, $"T08 {prefab}: it never targets the player while it runs");
                }
                else
                {
                    var feared = false;
                    yield return Until(() => ai.m_targetCreature == player, 5f, () =>
                    {
                        hold();
                        feared |= FearOf(ai) != null;
                    }, w);
                    c.Check(w.Met && !feared, $"T08 {prefab}: it targets the player within 5 s and never runs (target {Who(ai.m_targetCreature)}, ran {feared})");
                }
                s.Destroy(ai);
            }

            // ----- T32: elites one boss later -----
            Standing.TestOverride = new Standing.Override(4);
            var names = new[] { "Greydwarf", "Greydwarf_Shaman", "Greydwarf_Elite", "Troll" };
            var angles = new[] { -60f, -20f, 20f, 60f };
            var group = new List<MonsterAI>();
            var spots = new List<Vector3>();
            for (var i = 0; i < names.Length; i++)
            {
                var spot = s.Spot(here, Turn(s.Forward, angles[i]), 9f, true);
                var ai = SpawnMeadows(s, c, names[i], spot, here - spot);
                if (ai != null)
                {
                    group.Add(ai);
                    spots.Add(spot);
                }
            }
            if (c.Check(group.Count == names.Length, "T32: could not spawn the Greydwarf, the Shaman, the Brute and the Troll"))
            {
                Action hold = () =>
                {
                    s.Noise();
                    for (var i = 0; i < group.Count; i++)
                    {
                        Stage.Place(group[i].m_character, spots[i], player.transform.position - spots[i]);
                    }
                };
                yield return Until(() => FearOf(group[0]) == player && group[0].m_targetCreature == null && group[1].m_targetCreature == player
                                         && group[2].m_targetCreature == player && group[3].m_targetCreature == player, 6f, hold, w);
                c.Check(w.Met, $"T32: rank 4: the Greydwarf runs, the Shaman, the Brute and the Troll target the player within 6 s ({DescribePack(group, player)})");
                c.Check(Attitudes.Judge(group[0], player) == Attitude.Afraid && Attitudes.Judge(group[1], player) == Attitude.Hostile
                        && Attitudes.Judge(group[2], player) == Attitude.Hostile && Attitudes.Judge(group[3], player) == Attitude.Hostile,
                    "T32: rank 4: Judge says Afraid for the Greydwarf and Hostile for the three elites");
                Standing.TestOverride = new Standing.Override(5);
                c.Check(group.TrueForAll(g => Attitudes.Judge(g, player) == Attitude.Afraid), "T32: rank 5: all four are afraid");
                yield return Until(() => group.TrueForAll(g => FearOf(g) == player && g.m_targetCreature == null), 4f, hold, w);
                c.Check(w.Met, $"T32: rank 5: all four drop the player and run within 4 s ({DescribePack(group, player)})");
            }
            foreach (var g in group)
            {
                s.Destroy(g);
            }
            c.Report();
        }
        finally
        {
            Finish(s);
        }
    }

    // ================================================================ morale.rest

    // Campfire, bed, three afraid Greydwarfs 10 m away that run from the seated (noisy) player. What they must not
    // break: Player.IsSensed (rest, bed, music all read it), Resting -> Rested, Bed.CheckEnemies, MusicMan's combat
    // timer (only a creature that targets the player while alerted starts it). Then hostile ones: all of it breaks.
    private static IEnumerator RunRest()
    {
        var c = new Checks(RestName);
        Stage s = null;
        var w = new Waiter();
        var local = Player.m_localPlayer;
        var seman = local != null ? local.GetSEMan() : null;
        var env = EnvMan.instance;
        var oldEnv = env != null ? env.m_debugEnv : "";
        var hadRested = seman != null && seman.HaveStatusEffect(SEMan.s_statusEffectRested);
        try
        {
            s = Stage.Begin(RestName, c);
            if (s == null || seman == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            // Rain would stop the rest and the fire (nothing the mod decides): clear sky for this test.
            if (env != null)
            {
                env.m_debugEnv = "Clear";
            }
            if (seman.HaveStatusEffect(SEMan.s_statusEffectWet))
            {
                seman.RemoveStatusEffect(SEMan.s_statusEffectWet, true);
                SelfTest.Note(RestName, "the player was wet (no rest while wet); dried for this test");
            }
            seman.RemoveStatusEffect(SEMan.s_statusEffectRested, true);
            ServerRules.TestRules = NewRules(null);
            Standing.TestOverride = new Standing.Override(4);

            var fireGo = s.SpawnAny("fire_pit", Stage.Ground(here + s.Right * 1.8f), s.Forward);
            var bedGo = s.SpawnAny("bed", Stage.Ground(here - s.Right * 3f), s.Forward);
            var fire = fireGo != null ? fireGo.GetComponent<Fireplace>() : null;
            var bed = bedGo != null ? bedGo.GetComponent<Bed>() : null;
            if (!c.Check(fire != null && bed != null, "could not spawn a campfire (fire_pit) and a bed"))
            {
                c.Report();
                yield break;
            }
            var pack = new List<MonsterAI>();
            var spots = new List<Vector3>();
            foreach (var angle in new[] { -25f, 0f, 25f })
            {
                var spot = s.Spot(here, Turn(s.Forward, angle), 10f, true);
                var g = s.Spawn("Greydwarf", spot, here - spot);
                if (g != null)
                {
                    pack.Add(g);
                    spots.Add(spot);
                }
            }
            if (!c.Check(pack.Count == 3, "could not spawn three Greydwarfs"))
            {
                c.Report();
                yield break;
            }
            var distance = 10f;
            Action hold = () =>
            {
                s.Noise();
                for (var i = 0; i < pack.Count; i++)
                {
                    var spot = Stage.Ground(here + (spots[i] - here).normalized * distance);
                    Stage.Place(pack[i].m_character, spot, player.transform.position - spot);
                }
            };
            yield return Until(() => fire.IsBurning() && seman.HaveStatusEffect(SEMan.s_statusEffectCampFire), 5f, hold, w);
            c.Check(w.Met, $"setup: the campfire burns and warms the player (burning {fire.IsBurning()}, warm {seman.HaveStatusEffect(SEMan.s_statusEffectCampFire)})");
            // Fights of earlier tests: combat music state and the sensed timer run out first.
            var music = MusicMan.instance;
            yield return Until(() => !player.IsSensed() && !player.IsTargeted() && (music == null || music.m_combatTimer <= 0f), 10f, hold, w);
            c.Check(w.Met && music != null, "setup: no creature targets the player and the combat music state is off before the watch starts");

            var sat = player.StartEmote("sit", false);
            yield return Until(() => player.IsSitting(), 3f, hold, w);
            c.Check(sat && w.Met, "setup: the player sits down at the fire");
            var cozy = ObjectDB.instance != null ? ObjectDB.instance.GetStatusEffect(SEMan.s_statusEffectResting) as SE_Cozy : null;
            var delay = cozy != null ? cozy.m_delay : 10f;
            var sensed = false;
            var targeted = false;
            var attacked = false;
            var combat = false;
            var ran = false;
            var resting = false;
            yield return Until(() => seman.HaveStatusEffect(SEMan.s_statusEffectRested), delay + 6f, () =>
            {
                hold();
                sensed |= player.IsSensed();
                targeted |= player.IsTargeted();
                combat |= music != null && music.m_combatTimer > 0f;
                resting |= seman.HaveStatusEffect(SEMan.s_statusEffectResting);
                foreach (var g in pack)
                {
                    attacked |= g.m_targetCreature == player;
                    ran |= FearOf(g) == player;
                }
            }, w);
            c.Check(ran, $"T03: the afraid Greydwarfs 10 m away run from the noisy rank 4 player ({DescribePack(pack, player)})");
            c.Check(!attacked && !sensed && !targeted,
                $"T03: none of them ever targets the player, who is never sensed or targeted (attacked {attacked}, sensed {sensed}, targeted {targeted})");
            c.Check(resting && w.Met, $"T03: seated at the fire the player gets Resting, then Rested after the {F(delay)} s of rest "
                                      + $"(Resting seen {resting}, Rested {seman.HaveStatusEffect(SEMan.s_statusEffectRested)}, waited {F(w.Took)} s)");
            c.Check(!combat, "T03: no combat music: the game's combat music state never started while the Greydwarfs ran");
            c.Check(bed.CheckEnemies(player), "T03 / X04: Bed.CheckEnemies (the enemy check of a night sleep and of a day sleep) lets the player sleep "
                                              + "while afraid Greydwarfs run 10 m away");

            // The same Greydwarfs calm, beyond the fear range.
            distance = 18f;
            yield return Until(() => pack.TrueForAll(g => FearOf(g) == null && !g.IsAlerted()), 4f, hold, w);
            c.Check(w.Met && !player.IsSensed() && bed.CheckEnemies(player),
                $"X04: with the afraid Greydwarfs calm 18 m away the bed's enemy check still lets the player sleep ({DescribePack(pack, player)})");

            // Contrast: rank 0, the same Greydwarfs 6 m away attack: sensed, no rest, combat music state, bed refused.
            // Me take the campfire away first. A Greydwarf keep off fire: vanilla MonsterAI.UpdateAI leave at AvoidFire
            // before the chase code that alert it (SetAlerted when it see its target). Held next to the fire it target
            // the player (sensed) but never get alerted, and only an alerted creature start the combat music state
            // (Player.RPC_OnTargeted). First run: sensed True, combat timer 0 for the whole 8 s.
            player.StopEmote();
            s.Destroy(fire);
            yield return null;
            distance = 6f;
            Standing.TestOverride = new Standing.Override(0);
            yield return Until(() => player.IsSensed() && music != null && music.m_combatTimer > 0f, 8f, hold, w);
            c.Check(w.Met, $"contrast, rank 0, campfire removed (creatures that keep off fire are never alerted next to one): hostile Greydwarfs 6 m away "
                           + $"make the player sensed and start the combat music state within 8 s (sensed {player.IsSensed()}, "
                           + $"combat timer {(music != null ? F(music.m_combatTimer) : "none")}; {DescribePack(pack, player)})");
            c.Check(!bed.CheckEnemies(player), "X04 contrast: with hostile Greydwarfs near, the bed's enemy check refuses the sleep");
            c.Report();
        }
        finally
        {
            if (local != null && seman != null)
            {
                local.StopEmote();
                var rested = seman.HaveStatusEffect(SEMan.s_statusEffectRested);
                if (rested && !hadRested)
                {
                    seman.RemoveStatusEffect(SEMan.s_statusEffectRested, true);
                }
                else if (!rested && hadRested)
                {
                    seman.AddStatusEffect(SEMan.s_statusEffectRested, true);
                }
            }
            if (env != null)
            {
                env.m_debugEnv = oldEnv;
            }
            Finish(s);
        }
    }

    // ================================================================ morale.kills

    // Real kill by the player (vanilla credit: Character.OnDeath -> Game.RPC_RegisterKill -> our postfix), no loot.
    // Met = the profile's kill table reached `expected` for that name token.
    private static IEnumerator KillCredited(Stage s, MonsterAI victim, Dictionary<string, float> table, string token, int expected, Waiter w)
    {
        NoDrops(victim);
        Kill(victim.m_character, s.Player);
        yield return Until(() => table.TryGetValue(token, out var count) && count >= expected, 4f, null, w);
    }

    private static IEnumerator RunKills()
    {
        var c = new Checks(KillsName);
        Stage s = null;
        var w = new Waiter();
        try
        {
            s = Stage.Begin(KillsName, c);
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
            if (!c.Check(hud != null && hud.m_messageCenterText != null && !Hud.IsUserHidden() && table != null && Localization.instance != null,
                    "the HUD is hidden or missing, or the profile has no kill table: messages and kill counts cannot be checked"))
            {
                c.Report();
                yield break;
            }

            float Count(string token) => table.TryGetValue(token, out var value) ? value : 0f;

            int Bonus(string token)
            {
                var standing = StandingCache.Get(player);
                return standing != null ? standing.BonusFor(Hash(token)) : -1;
            }

            // ----- T04 (first half): a kill that raises the boss rank to 1 -----
            ServerRules.TestRules = NewRules(null);
            Standing.TestMessages = true;
            Standing.TestOverride = new Standing.Override(0);
            var boarSpot = s.Spot(here, s.Forward, 6f, true);
            var boar = SpawnMeadows(s, c, "Boar", boarSpot, here - boarSpot);
            if (boar == null)
            {
                c.Report();
                yield break;
            }
            Action holdBoar = () =>
            {
                s.Noise();
                Stage.Place(boar.m_character, boarSpot, player.transform.position - boarSpot);
            };
            var mark = Watch.Mark();
            Standing.TestKill(new Standing.Override(1));
            c.Check(!Standing.MessagePending, "T04: the kill that raises the boss rank to 1 (Eikthyr) schedules no rank-up message with the default settings");
            c.Check(Watch.Count("Standing: boss rank 1 (Eikthyr)", mark, BepInEx.Logging.LogLevel.Info) == 1,
                $"T04: the log shows \"Standing: boss rank 1 (Eikthyr), ...\" once (last Standing line: \"{Watch.Last("Standing:", mark)}\")");
            var published = StandingCache.Get(player);
            c.Check(published != null && published.BossRank == 1, "T04: the published standing is boss rank 1");
            yield return Until(() => boar.m_targetCreature == player, 5f, holdBoar, w);
            c.Check(w.Met && Attitudes.Judge(boar, player) == Attitude.Hostile,
                $"T04: boars still attack a rank 1 player (target {Who(boar.m_targetCreature)}, Judge {Attitudes.Judge(boar, player)})");

            // ----- T37: BossesAhead = 0 set afterwards, then a second kill of the same boss -----
            var rankUps = Shown(Standing.RankUpText);
            ServerRules.TestRules = NewRules(r => r.BossesAhead = 0);
            c.Check(!Standing.MessagePending, "T37: setting BossesAhead = 0 after the kill schedules no rank-up message");
            c.Check(Attitudes.Judge(boar, player) == Attitude.Afraid, $"T37: with BossesAhead = 0 the Boar is afraid of the rank 1 player at once (Judge {Attitudes.Judge(boar, player)})");
            yield return Until(() => FearOf(boar) == player && boar.m_targetCreature == null, 3f, holdBoar, w);
            c.Check(w.Met, $"T37: the Boar that attacked drops the player and runs within 3 s (runs from {Who(FearOf(boar))}, target {Who(boar.m_targetCreature)})");
            Standing.TestKill(new Standing.Override(1));
            c.Check(!Standing.MessagePending && Standing.RankMessageAt < 0f,
                "T37: a second kill of the same boss (the rank stays 1) schedules no rank-up message");
            s.Destroy(boar);
            // A character with no boss kill that has BossesAhead = 0 before its first Eikthyr kill: the message comes.
            Standing.TestOverride = new Standing.Override(0);
            Standing.TestKill(new Standing.Override(1));
            var due = Standing.RankMessageAt - Time.time;
            c.Check(Standing.MessagePending && due >= 4f && due <= Standing.RankMessageDelay + 0.01f,
                $"T37: with BossesAhead = 0 the first Eikthyr kill (rank 0 -> 1) schedules the rank-up message 4 to 4.5 s later (due in {F(due)} s)");
            c.Check(Shown(Standing.RankUpText) == rankUps, "T37: the rank-up message is not shown at once (the game's boss death message comes first)");
            yield return Until(() => !Standing.MessagePending, Standing.RankMessageDelay + 1f, null, w);
            c.Check(w.Met && Shown(Standing.RankUpText) == rankUps + 1 && hud.m_messageCenterText.text == Standing.RankUpText,
                $"T37: about 4.5 s later the center message reads \"{Standing.RankUpText}\" (now \"{hud.m_messageCenterText.text}\", "
                + $"shown {Shown(Standing.RankUpText) - rankUps} time(s))");

            // ----- T25: personal settings -----
            var display = new List<string>();
            var file = Plugin.ShowProgressMessages != null ? Plugin.ShowProgressMessages.ConfigFile : null;
            if (file != null)
            {
                foreach (var key in file.Keys)
                {
                    if (key.Section == "Display")
                    {
                        display.Add(key.Key);
                    }
                }
            }
            c.Check(display.Count == 1 && display[0] == "ShowProgressMessages",
                $"T25: the Display section holds only ShowProgressMessages (it holds: {string.Join(", ", display.ToArray())})");

            var greyling = TokenOf("Greyling");
            var killSpot = s.Spot(here, s.Forward, 5f, true);
            if (!c.Check(!string.IsNullOrEmpty(greyling), "the Greyling prefab has no name token"))
            {
                c.Report();
                yield break;
            }
            var n = (int)Count(greyling);
            Standing.TestMessages = false;
            ServerRules.TestRules = NewRules(r => r.KillSteps = (n + 1).ToString(CultureInfo.InvariantCulture));
            Standing.TestOverride = new Standing.Override(2) { ProfileKills = true };
            c.Check(Bonus(greyling) == 0, $"T25 setup: rank 2, {n} Greyling kill(s), kill step {n + 1}: no Greyling bonus yet (bonus {Bonus(greyling)})");
            var text = KillStepText(greyling, n + 1);
            var shown = Shown(text);
            var victim = SpawnMeadows(s, c, "Greyling", killSpot, here - killSpot);
            if (victim == null)
            {
                c.Report();
                yield break;
            }
            yield return Wait(0.3f, null);
            mark = Watch.Mark();
            yield return KillCredited(s, victim, table, greyling, n + 1, w);
            if (!c.Check(w.Met, $"a Greyling killed by the player is credited to the character's kill table ({greyling}: {n} -> {F(Count(greyling))})"))
            {
                c.Report();
                yield break;
            }
            yield return null;
            c.Check(Shown(text) == shown, $"T25: with the progress messages off the reached kill step shows no corner message (\"{text}\")");
            c.Check(Bonus(greyling) == 1 && Watch.Last("Standing:", mark).Contains(" +1"),
                $"T25: the standing still changes: Greyling bonus {Bonus(greyling)}, Standing line \"{Watch.Last("Standing:", mark)}\"");

            // ----- T05: kill step with messages on, through three real kills -----
            n = (int)Count(greyling);
            Standing.TestMessages = true;
            ServerRules.TestRules = NewRules(r => r.KillSteps = (n + 3).ToString(CultureInfo.InvariantCulture));
            Standing.TestOverride = new Standing.Override(2) { ProfileKills = true };
            var witnessSpot = s.Spot(here, Turn(s.Forward, -35f), 8f, true);
            var otherSpot = s.Spot(here, Turn(s.Forward, 35f), 8f, true);
            var witness = SpawnMeadows(s, c, "Greyling", witnessSpot, here - witnessSpot);
            var other = SpawnMeadows(s, c, "Boar", otherSpot, here - otherSpot);
            if (witness == null || other == null)
            {
                c.Report();
                yield break;
            }
            Action holdBoth = () =>
            {
                s.Noise();
                Stage.Place(witness.m_character, witnessSpot, player.transform.position - witnessSpot);
                Stage.Place(other.m_character, otherSpot, player.transform.position - otherSpot);
            };
            c.Check(Attitudes.Judge(witness, player) == Attitude.Hostile && Attitudes.Judge(other, player) == Attitude.Hostile && Bonus(greyling) == 0,
                $"T05 setup: rank 2, kill step {n + 3}: Greylings and Boars attack ({Attitudes.Judge(witness, player)} / {Attitudes.Judge(other, player)}, bonus {Bonus(greyling)})");
            text = KillStepText(greyling, n + 3);
            shown = Shown(text);
            for (var i = 1; i <= 3; i++)
            {
                victim = SpawnMeadows(s, c, "Greyling", killSpot, here - killSpot);
                if (victim == null)
                {
                    break;
                }
                yield return Wait(0.3f, holdBoth);
                mark = Watch.Mark();
                yield return KillCredited(s, victim, table, greyling, n + i, w);
                yield return null;
                c.Check(w.Met, $"T05: kill {i} of 3 is credited ({greyling}: {F(Count(greyling))})");
                if (i < 3)
                {
                    c.Check(Shown(text) == shown && Bonus(greyling) == 0, $"T05: after kill {i} of 3 there is no message and no bonus yet (bonus {Bonus(greyling)})");
                }
            }
            c.Check(Shown(text) == shown + 1 && InCorner(text) && hud.m_messageCenterText.text != text,
                $"T05: the third kill shows the corner message \"{text}\" once, in the corner and not in the middle of the screen "
                + $"(shown {Shown(text) - shown} time(s), in the corner {InCorner(text)})");
            c.Check(Bonus(greyling) == 1 && Watch.Last("Standing:", mark).Contains(Localization.instance.Localize(greyling) + " +1"),
                $"T05: the Standing line shows the bonus (bonus {Bonus(greyling)}, line \"{Watch.Last("Standing:", mark)}\")");
            c.Check((int)Count(greyling) == n + 3,
                $"X03: the character's Greyling kill count (the game's own table, which MC Creature Kill and Tame Counts shows) is the count the kill step used: {F(Count(greyling))}, expected {n + 3}");
            c.Check(Attitudes.Judge(witness, player) == Attitude.Afraid && Attitudes.Judge(other, player) == Attitude.Hostile,
                $"T05: other Greylings are now afraid (one boss early), Boars still attack ({Attitudes.Judge(witness, player)} / {Attitudes.Judge(other, player)})");
            yield return Until(() => FearOf(witness) == player && witness.m_targetCreature == null && other.m_targetCreature == player, 5f, holdBoth, w);
            c.Check(w.Met, $"T05: the other Greyling runs from the player and the Boar targets them within 5 s (Greyling runs from {Who(FearOf(witness))}, "
                           + $"Boar target {Who(other.m_targetCreature)})");
            s.Destroy(witness);
            s.Destroy(other);

            // ----- T06: kills bring a kind at most one boss early -----
            var greydwarf = TokenOf("Greydwarf");
            var m = (int)Count(greydwarf);
            Standing.TestMessages = true;
            ServerRules.TestRules = NewRules(r => r.KillSteps = string.Format(CultureInfo.InvariantCulture, "{0}, {1}", m + 1, m + 2));
            Standing.TestOverride = new Standing.Override(2) { ProfileKills = true };
            var watcher = SpawnMeadows(s, c, "Greydwarf", witnessSpot, here - witnessSpot);
            if (watcher == null)
            {
                c.Report();
                yield break;
            }
            Action holdWatcher = () =>
            {
                s.Noise();
                Stage.Place(watcher.m_character, witnessSpot, player.transform.position - witnessSpot);
            };
            var first = KillStepText(greydwarf, m + 1);
            var second = KillStepText(greydwarf, m + 2);
            var shownFirst = Shown(first);
            var shownSecond = Shown(second);
            for (var i = 1; i <= 2; i++)
            {
                victim = SpawnMeadows(s, c, "Greydwarf", killSpot, here - killSpot);
                if (victim == null)
                {
                    break;
                }
                yield return Wait(0.3f, holdWatcher);
                yield return KillCredited(s, victim, table, greydwarf, m + i, w);
                yield return null;
                c.Check(w.Met, $"T06: Greydwarf kill {i} of 2 is credited ({greydwarf}: {F(Count(greydwarf))})");
            }
            c.Check(Shown(first) == shownFirst && Shown(second) == shownSecond,
                "T06: rank 2 and two Greydwarf kill steps: no corner message (Greydwarfs need rank 4, kills bring a kind at most one boss early)");
            c.Check(Bonus(greydwarf) == 0 && Attitudes.Judge(watcher, player) == Attitude.Hostile,
                $"T06: the bonus is not listed and other Greydwarfs still attack (bonus {Bonus(greydwarf)}, Judge {Attitudes.Judge(watcher, player)})");
            yield return Until(() => watcher.m_targetCreature == player, 5f, holdWatcher, w);
            c.Check(w.Met, $"T06: rank 2: the other Greydwarf targets the player within 5 s (target {Who(watcher.m_targetCreature)})");
            Standing.TestOverride = new Standing.Override(3) { ProfileKills = true };
            c.Check(Attitudes.Judge(watcher, player) == Attitude.Afraid && Bonus(greydwarf) >= 1,
                $"T06: rank 3 with the same kills: Greydwarfs are afraid (Judge {Attitudes.Judge(watcher, player)}, bonus {Bonus(greydwarf)})");
            yield return Until(() => FearOf(watcher) == player && watcher.m_targetCreature == null, 3.5f, holdWatcher, w);
            c.Check(w.Met, $"T06: rank 3: it drops the player and runs within 3.5 s (runs from {Who(FearOf(watcher))}, target {Who(watcher.m_targetCreature)})");
            s.Destroy(watcher);
            c.Report();
        }
        finally
        {
            Finish(s);
        }
    }

    // ================================================================ morale.missiles

    private static IEnumerator RunMissiles()
    {
        var c = new Checks(MissilesName);
        Stage s = null;
        var w = new Waiter();
        try
        {
            s = Stage.Begin(MissilesName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            var rules = NewRules(null);
            ServerRules.TestRules = rules;
            Standing.TestOverride = new Standing.Override(4);

            var bow = ItemOf("Bow");
            var arrow = ItemOf("ArrowWood");
            if (!c.Check(bow != null && arrow != null && bow.m_shared.m_attack != null && arrow.m_shared.m_attack != null,
                    "T11: the items Bow and ArrowWood exist"))
            {
                c.Report();
                yield break;
            }
            var arrowPrefab = arrow.m_shared.m_attack.m_attackProjectile != null ? arrow.m_shared.m_attack.m_attackProjectile : bow.m_shared.m_attack.m_attackProjectile;
            var arrowNoise = bow.m_shared.m_attack.m_attackHitNoise + arrow.m_shared.m_attack.m_attackHitNoise; // as Attack.FireProjectileBurst adds them
            var arrowDamage = arrow.m_shared.m_damages;
            arrowDamage.Add(bow.GetDamage());
            c.Check(arrowPrefab != null && arrowPrefab.GetComponent<Projectile>() != null && arrowNoise > 6.5f,
                $"T11 setup: the arrow's projectile exists and a bow shot's impact is heard {F(arrowNoise)} m around (more than the 5-7 m of this test)");
            if (arrowPrefab == null || arrowPrefab.GetComponent<Projectile>() == null)
            {
                c.Report();
                yield break;
            }

            // ----- T11: an arrow into the ground 2 m from an afraid Greydwarf, the player 20 m away -----
            var nearSpot = s.Spot(here, s.Forward, 20f, true);
            var near = s.Spawn("Greydwarf", nearSpot, here - nearSpot);
            if (!c.Check(near != null, "T11: could not spawn a Greydwarf"))
            {
                c.Report();
                yield break;
            }
            Action holdNear = () => Stage.Place(near.m_character, nearSpot, player.transform.position - nearSpot);
            yield return Wait(1.2f, holdNear);
            c.Check(Attitudes.Judge(near, player) == Attitude.Afraid && !near.IsAlerted() && near.m_targetCreature == null && FearOf(near) == null,
                $"T11 setup: an afraid, calm Greydwarf 20 m from the rank 4 player (alerted {near.IsAlerted()}, runs from {Who(FearOf(near))})");
            var side = Vector3.Cross(Vector3.up, (nearSpot - here).normalized);
            Fire(s, arrowPrefab, Stage.Ground(nearSpot + side * 2f) + Vector3.up * 4f, Vector3.down * 30f, arrowNoise,
                PlayerHit(player, arrowDamage, Skills.SkillType.Bows), bow, arrow);
            yield return Until(() => Provoked(near, player) && near.m_targetCreature == player && near.IsAlerted(), 2.5f, holdNear, w);
            c.Check(w.Met, $"T11: an arrow of the player that lands 2 m from the afraid Greydwarf provokes it: it targets the player, alerted, within 2.5 s "
                           + $"(provoked {Provoked(near, player)}, target {Who(near.m_targetCreature)}, alerted {near.IsAlerted()})");
            s.Destroy(near);

            // ----- T11: an arrow on a Deer 6 m from afraid Greydwarfs, then a spear in the ground 10 m from them -----
            var baseSpot = s.Spot(here, s.Forward, 20f, true);
            var away = (baseSpot - here).normalized;
            side = Vector3.Cross(Vector3.up, away);
            var leftSpot = Stage.Ground(baseSpot - side * 1.5f);
            var rightSpot = Stage.Ground(baseSpot + side * 1.5f);
            var deerSpot = Stage.Ground(baseSpot + away * 6f);
            var left = s.Spawn("Greydwarf", leftSpot, here - leftSpot);
            var right = s.Spawn("Greydwarf", rightSpot, here - rightSpot);
            var deerGo = s.SpawnAny("Deer", deerSpot, here - deerSpot);
            var deer = deerGo != null ? deerGo.GetComponent<Character>() : null;
            if (!c.Check(left != null && right != null && deer != null, "T11: could not spawn two Greydwarfs and a Deer"))
            {
                c.Report();
                yield break;
            }
            Tough(deer);
            Action holdPair = () =>
            {
                Stage.Place(left.m_character, leftSpot, player.transform.position - leftSpot);
                Stage.Place(right.m_character, rightSpot, player.transform.position - rightSpot);
                if (deer != null)
                {
                    Stage.Place(deer, deerSpot, Vector3.zero);
                }
            };
            Func<bool> pairCalm = () => !Provoked(left, player) && !Provoked(right, player) && !left.IsAlerted() && !right.IsAlerted()
                                        && left.m_targetCreature == null && right.m_targetCreature == null;
            yield return Wait(1.2f, holdPair);
            c.Check(pairCalm() && Attitudes.Judge(left, player) == Attitude.Afraid, "T11 setup: two afraid, calm Greydwarfs 20 m from the player");
            var deerHealth = deer.GetHealth();
            var deerDistance = Vector3.Distance(deer.transform.position, left.transform.position);
            Fire(s, arrowPrefab, deer.GetCenterPoint() + Vector3.up * 3f, Vector3.down * 30f, arrowNoise,
                PlayerHit(player, arrowDamage, Skills.SkillType.Bows), bow, arrow);
            var stirred = false;
            yield return Wait(1.5f, () =>
            {
                holdPair();
                stirred |= !pairCalm();
            });
            c.Check(!stirred, $"T11: an arrow on a Deer {F(deerDistance)} m from afraid Greydwarfs (inside the bow's {F(arrowNoise)} m noise, beyond NearMissRange "
                              + $"{F(rules.NearMissRange)} m): they are not provoked, not alerted and target nobody ({DescribePack(new List<MonsterAI> { left, right }, player)})");
            SelfTest.Note(MissilesName, $"T11: the arrow {(deer != null && deer.GetHealth() < deerHealth ? "hit" : "missed")} the Deer (the item allows both)");

            var spear = ItemOf("SpearFlint");
            var throwAttack = spear != null ? spear.m_shared.m_secondaryAttack : null;
            var spearPrefab = throwAttack != null ? throwAttack.m_attackProjectile : null;
            if (c.Check(spearPrefab != null && spearPrefab.GetComponent<Projectile>() != null, "T11: the SpearFlint has a thrown projectile (its secondary attack)"))
            {
                Fire(s, spearPrefab, Stage.Ground(baseSpot + away * 10f) + Vector3.up * 4f, Vector3.down * 30f, throwAttack.m_attackHitNoise,
                    PlayerHit(player, spear.GetDamage(), Skills.SkillType.Spears), null, null);
                stirred = false;
                yield return Wait(1.5f, () =>
                {
                    holdPair();
                    stirred |= !pairCalm();
                });
                c.Check(!stirred && throwAttack.m_attackHitNoise >= 10f,
                    $"T11: a thrown SpearFlint that lands 10 m from them (inside its {F(throwAttack.m_attackHitNoise)} m noise): they do not come for the player "
                    + $"({DescribePack(new List<MonsterAI> { left, right }, player)})");
            }
            // Contrast: the same arrow on the Deer once they are hostile (rank 0): the normal game alerts them.
            var calmBefore = pairCalm();
            Standing.TestOverride = new Standing.Override(0);
            if (deer != null)
            {
                Fire(s, arrowPrefab, deer.GetCenterPoint() + Vector3.up * 3f, Vector3.down * 30f, arrowNoise,
                    PlayerHit(player, arrowDamage, Skills.SkillType.Bows), bow, arrow);
            }
            yield return Until(() => left.IsAlerted() && right.IsAlerted(), 1f, holdPair, w);
            c.Check(calmBefore && w.Met, $"T11 contrast, rank 0: the same arrow on the Deer alerts both (now hostile) Greydwarfs within a second, as in the normal game "
                                         + $"(calm before the shot {calmBefore}, alerted {left.IsAlerted()} / {right.IsAlerted()})");
            s.Destroy(left);
            s.Destroy(right);
            if (deer != null)
            {
                s.Destroy(deer);
            }
            Standing.TestOverride = new Standing.Override(4);

            // ----- T12: poison-only hit, then a real Ooze bomb, from 15 m -----
            var bombSpot = s.Spot(here, s.Forward, 15f, true);
            var poisoned = s.Spawn("Greydwarf", bombSpot, here - bombSpot);
            if (c.Check(poisoned != null, "T12: could not spawn a Greydwarf"))
            {
                Tough(poisoned.m_character);
                Action holdPoisoned = () => Stage.Place(poisoned.m_character, bombSpot, player.transform.position - bombSpot);
                yield return Wait(1.2f, holdPoisoned);
                c.Check(Attitudes.Judge(poisoned, player) == Attitude.Afraid && !poisoned.IsAlerted(), "T12 setup: an afraid, calm Greydwarf 15 m away");
                HitWith(poisoned.m_character, player, h => h.m_damage.m_poison = 1f, 1f);
                yield return Until(() => Provoked(poisoned, player) && poisoned.m_targetCreature == player, 1f, holdPoisoned, w);
                c.Check(w.Met, $"T12: a poison-only hit provokes it and it targets the player within 1 s (provoked {Provoked(poisoned, player)}, "
                               + $"target {Who(poisoned.m_targetCreature)})");
                s.Destroy(poisoned);
            }
            var bomb = ItemOf("BombOoze");
            var bombPrefab = bomb != null && bomb.m_shared.m_attack != null ? bomb.m_shared.m_attack.m_attackProjectile : null;
            var bombed = s.Spawn("Greydwarf", bombSpot, here - bombSpot);
            if (c.Check(bombPrefab != null && bombPrefab.GetComponent<Projectile>() != null && bombed != null, "T12: the BombOoze has a projectile, and a Greydwarf to throw it at"))
            {
                Tough(bombed.m_character);
                Action holdBombed = () => Stage.Place(bombed.m_character, bombSpot, player.transform.position - bombSpot);
                yield return Wait(1.2f, holdBombed);
                c.Check(Attitudes.Judge(bombed, player) == Attitude.Afraid && !bombed.IsAlerted() && !Provoked(bombed, player), "T12 setup: a second afraid, calm Greydwarf 15 m away");
                // On the ground 1 m beside it: the bomb bursts into its cloud there (what hurts is the cloud, not the flask).
                var beside = Vector3.Cross(Vector3.up, (bombSpot - here).normalized);
                Fire(s, bombPrefab, Stage.Ground(bombSpot + beside * 1f) + Vector3.up * 3f, Vector3.down * 12f, bomb.m_shared.m_attack.m_attackHitNoise,
                    PlayerHit(player, bomb.GetDamage(), bomb.m_shared.m_skillType), bomb, null);
                yield return Until(() => Provoked(bombed, player) && bombed.m_targetCreature == player, 4f, holdBombed, w);
                c.Check(w.Met, $"T12: an Ooze bomb of the player that bursts next to the afraid Greydwarf 15 m away provokes it: it targets the player within 4 s "
                               + $"(provoked {Provoked(bombed, player)}, target {Who(bombed.m_targetCreature)}, poisoned {bombed.m_character.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectPoison)})");
            }
            s.Destroy(bombed);
            // The bomb's cloud would poison (and so provoke) the next creature that stands here.
            var clouds = ClearAreas(bombSpot, 8f);
            SelfTest.Note(MissilesName, $"T12: {clouds} bomb cloud(s) removed after the step");
            yield return null;

            // ----- X06: the harpoon projectile (vanilla part; the hook itself belongs to Harpoon Hooks Tames) -----
            yield return HarpoonSteps(s, c);
            c.Report();
        }
        finally
        {
            Finish(s);
        }
    }

    private static IEnumerator HarpoonSteps(Stage s, Checks c)
    {
        var player = s.Player;
        var here = player.transform.position;
        var w = new Waiter();
        var harpoon = ItemOf("SpearChitin");
        var attack = harpoon != null ? harpoon.m_shared.m_attack : null;
        var prefab = attack != null ? attack.m_attackProjectile : null;
        if (prefab == null && harpoon != null && harpoon.m_shared.m_secondaryAttack != null)
        {
            attack = harpoon.m_shared.m_secondaryAttack;
            prefab = attack.m_attackProjectile;
        }
        if (!c.Check(prefab != null && prefab.GetComponent<Projectile>() != null, "X06: the SpearChitin (harpoon) has a projectile"))
        {
            yield break;
        }
        Standing.TestOverride = new Standing.Override(3);
        var wildSpot = s.Spot(here, s.Forward, 15f, true);
        var wild = SpawnMeadows(s, c, "Boar", wildSpot, here - wildSpot);
        if (wild == null)
        {
            yield break;
        }
        Tough(wild.m_character);
        Action holdWild = () => Stage.Place(wild.m_character, wildSpot, player.transform.position - wildSpot);
        yield return Wait(1.2f, holdWild);
        c.Check(Attitudes.Judge(wild, player) == Attitude.Afraid && !wild.IsAlerted(), "X06 setup: an afraid, calm wild Boar 15 m from the rank 3 player");
        Fire(s, prefab, wild.m_character.GetCenterPoint() + Vector3.up * 2.5f, Vector3.down * 30f, attack.m_attackHitNoise,
            PlayerHit(player, harpoon.GetDamage(), harpoon.m_shared.m_skillType), null, null);
        yield return Until(() => Provoked(wild, player) && wild.m_targetCreature == player, 2.5f, holdWild, w);
        c.Check(w.Met, $"X06: a harpoon of the player that hits an afraid wild Boar provokes it: it targets the player within 2.5 s "
                       + $"(provoked {Provoked(wild, player)}, target {Who(wild.m_targetCreature)})");
        s.Destroy(wild);

        // A harpoon on the player's own tame, afraid Boars 6 m from it.
        var tameSpot = s.Spot(here, s.Forward, 15f, true);
        var away = (tameSpot - here).normalized;
        var farSpot = Stage.Ground(tameSpot + away * 6f);
        var tame = SpawnMeadows(s, c, "Boar", tameSpot, here - tameSpot);
        var bystander = SpawnMeadows(s, c, "Boar", farSpot, here - farSpot);
        if (tame == null || bystander == null)
        {
            yield break;
        }
        tame.MakeTame();
        Tough(tame.m_character);
        Action holdTame = () =>
        {
            Stage.Place(tame.m_character, tameSpot, player.transform.position - tameSpot);
            Stage.Place(bystander.m_character, farSpot, player.transform.position - farSpot);
        };
        yield return Wait(1.2f, holdTame);
        c.Check(tame.m_character.IsTamed() && Attitudes.Judge(bystander, player) == Attitude.Afraid && !bystander.IsAlerted(),
            "X06 setup: a tame Boar and an afraid, calm wild Boar 6 m from it");
        Fire(s, prefab, tame.m_character.GetCenterPoint() + Vector3.up * 2.5f, Vector3.down * 30f, attack.m_attackHitNoise,
            PlayerHit(player, harpoon.GetDamage(), harpoon.m_shared.m_skillType), null, null);
        var stirred = false;
        yield return Wait(1.5f, () =>
        {
            holdTame();
            stirred |= Provoked(bystander, player) || bystander.IsAlerted() || bystander.m_targetCreature != null;
        });
        c.Check(!stirred, $"X06: a harpoon on the player's own tame {F(Vector3.Distance(tameSpot, farSpot))} m from an afraid Boar (beyond NearMissRange): that Boar "
                          + $"does not come for the player (provoked {Provoked(bystander, player)}, alerted {bystander.IsAlerted()}, target {Who(bystander.m_targetCreature)})");
        s.Destroy(tame);
        s.Destroy(bystander);
    }
}
#endif
