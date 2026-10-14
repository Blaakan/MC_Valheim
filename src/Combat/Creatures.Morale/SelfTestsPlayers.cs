#if DEBUG
using System;
using System.Collections;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Combat.CreaturesMoraleMod;

// Debug build only. What one game can show of the "two players" items (TESTING.md items in brackets):
//   morale.two-players  a second player's character next to creatures this game controls: each player judged by
//                       their own published standing, a player whose game have no mod (no standing) is fair game
//                       (M01, M07, this game's side); two players provoke the same Greydwarf, each anger with its own
//                       timer; a hit by the second player long after the first one's provocation ran out (M04)
// The other player = a stand-in: the game's own player prefab made without network start, with a ZDO another game
// "owns" (never this one), its player id and its standing written on that ZDO, known to the scene so a hit or a
// projectile can name it as attacker. It live inside one frame only (no physics step or AI tick ever see it), so
// every step with it is: make it, let the game's real code run (hit, projectile noise, enemy search), read, remove.
// What a second player's own game does, show or owns stay a by-hand check.
internal static partial class SelfTests
{
    private const string TwoPlayersName = "morale.two-players";

    private sealed class StandIn
    {
        internal Player Body;
        internal ZDO Zdo;
        private GameObject _go;
        private ZNetView _view;

        // bossRank < 0 = that player's game have no mod: nothing published. Null = could not be made.
        internal static StandIn Make(Vector3 at, long playerId, int bossRank)
        {
            if (Game.instance == null || Game.instance.m_playerPrefab == null || ZDOMan.instance == null || ZNetScene.instance == null)
            {
                return null;
            }
            var made = new StandIn();
            try
            {
                ZNetView.m_forceDisableInit = true;
                try
                {
                    made._go = Object.Instantiate(Game.instance.m_playerPrefab, at, Quaternion.identity);
                }
                finally
                {
                    ZNetView.m_forceDisableInit = false;
                }
                made.Body = made._go.GetComponent<Player>();
                made._view = made.Body != null ? made.Body.m_nview : null;
                if (made._view == null)
                {
                    made.Remove();
                    return null;
                }
                // Its ZDO sits far outside the loaded area and belongs to a game that is not there.
                made.Zdo = ZDOMan.instance.CreateNewZDO(at + new Vector3(3000f, 0f, 3000f), 0);
                made.Zdo.SetOwner(ZDOMan.GetSessionID() + 1L);
                made.Zdo.Set(ZDOVars.s_playerID, playerId);
                // What a real remote player publish and creatures read for a body another game owns (Player.GetStealthFactor,
                // Character.GetNoiseRange read the ZDO, not the fields): standing in plain view, walking noise. Without
                // them the stand-in is unseen and unheard (first run: enemy search picked nobody).
                made.Zdo.Set(ZDOVars.s_stealth, 1f);
                made.Zdo.Set(ZDOVars.s_noise, 30f);
                if (bossRank >= 0)
                {
                    made.Zdo.Set(CreatureKeys.Standing, Standing.Encode(new StandingData(bossRank, null, null)));
                }
                made._view.m_zdo = made.Zdo;
                ZNetScene.instance.m_instances[made.Zdo] = made._view;
                return made;
            }
            catch (Exception e)
            {
                SelfTest.Note(TwoPlayersName, $"making the stand-in for another player failed: {e.Message}");
                made.Remove();
                return null;
            }
        }

        internal bool Good(Player local) =>
            Body != null && !ReferenceEquals(Body, local) && Body.IsPlayer() && _view != null && _view.IsValid() && !_view.IsOwner()
            && !Body.GetZDOID().IsNone() && Player.GetAllPlayers().Contains(Body);

        internal void Remove()
        {
            try
            {
                if (Zdo != null && ZNetScene.instance != null)
                {
                    ZNetScene.instance.m_instances.Remove(Zdo);
                }
                if (_view != null)
                {
                    _view.m_zdo = null;
                }
                if (_go != null)
                {
                    Object.DestroyImmediate(_go);
                }
                if (Zdo != null && ZDOMan.instance != null)
                {
                    Zdo.SetOwner(ZDOMan.GetSessionID());
                    ZDOMan.instance.DestroyZDO(Zdo);
                }
            }
            catch (Exception e)
            {
                Log.Error($"{SelfTest.Prefix} clean-up of the stand-in player failed: {e}");
            }
            _go = null;
            _view = null;
            Body = null;
            Zdo = null;
        }
    }

    private static bool ProvokedById(MonsterAI ai, long playerId) =>
        ai != null && ai.m_nview != null && ai.m_nview.IsValid() && CreatureKeys.IsProvokedBy(ai.m_nview.GetZDO(), playerId, Attitudes.Now());

    private static bool UntilOf(MonsterAI ai, long playerId, out long until)
    {
        until = 0L;
        return ai != null && ai.m_nview != null && ai.m_nview.IsValid()
               && CreatureKeys.TryGetUntil(ai.m_nview.GetZDO().GetByteArray(CreatureKeys.Provokers), playerId, out until);
    }

    private static IEnumerator RunTwoPlayers()
    {
        var c = new Checks(TwoPlayersName);
        Stage s = null;
        var w = new Waiter();
        StandIn other = null;
        try
        {
            s = Stage.Begin(TwoPlayersName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            var pid = player.GetPlayerID();
            var otherId = pid + 7919L;
            var rules = NewRules(r =>
            {
                r.ProvokedSeconds = 4f;
                r.CorneredRange = 0f;
            });
            ServerRules.TestRules = rules;
            Standing.TestOverride = new Standing.Override(8);
            var spot = s.Spot(here, s.Forward, 5f, true);
            var toPlayer = (here - spot).normalized;

            // ----- M01: each player judged by their own progress (this game controls the Greydwarf) -----
            var g = s.Spawn("Greydwarf", spot, here - spot);
            if (!c.Check(g != null && pid != 0L, "could not spawn a Greydwarf (or the local player has no player id)"))
            {
                c.Report();
                yield break;
            }
            Action holdG = () =>
            {
                s.Noise();
                Stage.Place(g.m_character, spot, player.transform.position - spot);
            };
            yield return Until(() => FearOf(g) == player && g.m_targetCreature == null, 4f, holdG, w);
            c.Check(w.Met && Attitudes.Judge(g, player) == Attitude.Afraid, $"M01 setup: the Greydwarf is afraid of the rank 8 local player and runs (runs from {Who(FearOf(g))})");

            other = StandIn.Make(Stage.Ground(spot + toPlayer * 2.5f), otherId, 0);
            if (!c.Check(other != null && other.Good(player), "the stand-in for a second player could not be made (a second loaded player another game owns)"))
            {
                c.Report();
                yield break;
            }
            other.Body.m_noiseRange = 30f; // a walking player: heard whatever way the creature looks
            var standing = StandingCache.Get(other.Body);
            var judgedOther = Attitudes.Judge(g, other.Body);
            var judgedLocal = Attitudes.Judge(g, player);
            var pick = g.FindEnemy();
            c.Check(standing != null && standing.BossRank == 0 && judgedOther == Attitude.Hostile && judgedLocal == Attitude.Afraid,
                $"M01: with a rank 0 player 2.5 m from it and the rank 8 local player 5 m away, the same Greydwarf is hostile to the first and afraid of the second "
                + $"(rank 0 player: {judgedOther}, their standing read {(standing != null ? standing.BossRank.ToString() : "none")}; local player: {judgedLocal})");
            c.Check(pick != null && ReferenceEquals(pick, other.Body),
                $"M01: the game's own enemy search of that Greydwarf picks the rank 0 player, never the rank 8 one (picked {(pick == null ? "nobody" : ReferenceEquals(pick, player) ? "the local player" : pick.IsPlayer() ? "the other player" : pick.m_name)})");
            other.Remove();
            other = null;
            pick = g.FindEnemy();
            c.Check(!ReferenceEquals(pick, player), $"M01: with the rank 0 player gone its enemy search does not pick the rank 8 player (picked {Who(pick)})");

            // ----- M07: a player whose game have no mod publishes nothing: creatures treat them as in the normal game -----
            other = StandIn.Make(Stage.Ground(spot + toPlayer * 2.5f), otherId + 1L, -1);
            if (c.Check(other != null && other.Good(player), "the stand-in for a player without the mod could not be made"))
            {
                other.Body.m_noiseRange = 30f;
                var none = StandingCache.Get(other.Body);
                judgedOther = Attitudes.Judge(g, other.Body);
                pick = g.FindEnemy();
                c.Check(none == null && judgedOther == Attitude.Hostile && pick != null && ReferenceEquals(pick, other.Body) && Attitudes.Judge(g, player) == Attitude.Afraid,
                    $"M07: a player with no published standing (their game has no mod) next to a Greydwarf this game controls: it is hostile to them and its "
                    + $"enemy search picks them, while it stays afraid of the rank 8 local player (toward them {judgedOther}, picked them {pick != null && ReferenceEquals(pick, other.Body)})");
                other.Remove();
                other = null;
            }
            s.Destroy(g);
            yield return null;

            // ----- M04, first part: two players provoke the same Greydwarf, each anger with its own timer -----
            var a = s.Spawn("Greydwarf", spot, here - spot);
            if (c.Check(a != null, "M04: could not spawn a Greydwarf"))
            {
                Tough(a.m_character);
                Action holdA = () =>
                {
                    s.Noise();
                    Stage.Place(a.m_character, spot, player.transform.position - spot);
                };
                yield return Wait(1f, holdA);
                Hit(a.m_character, player, 1f, 0f, 1f);
                var ok1 = UntilOf(a, pid, out var until1);
                c.Check(ok1 && Provoked(a, player) && a.m_targetCreature == player,
                    $"M04: the local player's hit provokes the afraid Greydwarf and it targets them (provoked {Provoked(a, player)}, target {Who(a.m_targetCreature)})");
                yield return Wait(1.5f, holdA);

                other = StandIn.Make(Stage.Ground(spot + s.Right * 6f), otherId, 8);
                if (c.Check(other != null && other.Good(player), "M04: the stand-in for a second rank 8 player could not be made"))
                {
                    var before = Attitudes.Judge(a, other.Body);
                    // The second player's arrow land 2 m from it (what Projectile.OnHit call for every impact).
                    BaseAI.DoProjectileHitNoise(a.transform.position + s.Right * 2f, 30f, other.Body);
                    var ok2 = UntilOf(a, otherId, out var until2);
                    var still1 = UntilOf(a, pid, out var until1After);
                    var gap = SecondsBetween(until1, until2);
                    c.Check(before == Attitude.Afraid && ok2 && still1 && ProvokerCountOf(a) == 2 && Attitudes.Judge(a, other.Body) == Attitude.Provoked
                            && Attitudes.Judge(a, player) == Attitude.Provoked,
                        $"M04: the second player's near miss provokes it toward them as well: two records, one per player (afraid of them before {before}, "
                        + $"records {ProvokerCountOf(a)}, toward the second player {Attitudes.Judge(a, other.Body)}, toward the local player {Attitudes.Judge(a, player)})");
                    c.Check(a.m_targetCreature == player, $"M04: it keeps fighting the first player, its target (target {Who(a.m_targetCreature)})");
                    c.Check(until1After == until1 && gap > 1.0 && gap < 2.5,
                        $"M04: each anger has its own timer: the first player's is untouched and the second player's ends {F(gap)} s later (the near miss came 1.5 s after the hit)");
                    other.Remove();
                    other = null;
                    // The first player's 4 s end first; the second player's anger is still on then.
                    yield return Until(() => !Provoked(a, player), 5f, holdA, w);
                    c.Check(w.Met && ProvokedById(a, otherId),
                        $"M04: when the first player's provocation runs out, the second player's is still on (first over {w.Met}, second on {ProvokedById(a, otherId)})");
                }
                s.Destroy(a);
                yield return null;
            }

            // ----- M04, second part: the second player hits long after the first one's provocation ran out -----
            var b = s.Spawn("Greydwarf", spot, here - spot);
            if (c.Check(b != null, "M04: could not spawn a second Greydwarf"))
            {
                Tough(b.m_character);
                Action holdB = () =>
                {
                    s.Noise();
                    Stage.Place(b.m_character, spot, player.transform.position - spot);
                };
                yield return Wait(1f, holdB);
                Hit(b.m_character, player, 1f, 0f, 1f);
                c.Check(Provoked(b, player), "M04: the first player hits a fresh afraid Greydwarf once: provoked");
                // The first player walks away (20 m: beyond the fear range), until their provocation is over.
                s.MovePlayer(s.Spot(spot, here - spot, 20f, false));
                yield return Until(() => !Provoked(b, player) && b.m_targetCreature == null && !b.IsAlerted(), rules.ProvokedSeconds + 5f, holdB, w);
                c.Check(w.Met && Attitudes.Judge(b, player) == Attitude.Afraid,
                    $"M04 setup: once the first player's provocation is over the Greydwarf is calm and afraid of them again (provoked {Provoked(b, player)}, "
                    + $"target {Who(b.m_targetCreature)}, alerted {b.IsAlerted()}, waited {F(w.Took)} s)");

                other = StandIn.Make(Stage.Ground(spot + s.Right * 2f), otherId, 8);
                if (c.Check(other != null && other.Good(player), "M04: the stand-in for the second player could not be made"))
                {
                    HitWith(b.m_character, other.Body, h => h.m_damage.m_slash = 1f, 1f);
                    c.Check(ProvokedById(b, otherId) && b.m_targetCreature != null && ReferenceEquals(b.m_targetCreature, other.Body)
                            && Attitudes.Judge(b, other.Body) == Attitude.Provoked,
                        $"M04: the second player's hit makes it fight the second player (provoked by them {ProvokedById(b, otherId)}, "
                        + $"target is them {b.m_targetCreature != null && ReferenceEquals(b.m_targetCreature, other.Body)})");
                    c.Check(!Provoked(b, player) && Attitudes.Judge(b, player) == Attitude.Afraid,
                        $"M04: that hit does not bring back its anger at the first player (provoked by the first {Provoked(b, player)}, toward the first {Attitudes.Judge(b, player)})");
                    other.Remove();
                    other = null;
                    var wentForFirst = false;
                    yield return Wait(3f, () =>
                    {
                        holdB();
                        wentForFirst |= b.m_targetCreature == player;
                    });
                    c.Check(!wentForFirst && Attitudes.Judge(b, player) == Attitude.Afraid,
                        $"M04: in the next 3 s it never goes for the first player, 20 m away and noisy (went for them {wentForFirst})");
                }
                s.Destroy(b);
            }
            c.Report();
        }
        finally
        {
            if (other != null)
            {
                other.Remove();
            }
            Finish(s);
        }
    }

    private static double SecondsBetween(long from, long to) => (to - from) / (double)TimeSpan.TicksPerSecond;
}
#endif
