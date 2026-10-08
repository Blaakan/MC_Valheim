#if DEBUG
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.StatsPerCreatureMod;

// Tame tests. Me tame only creatures me spawned, with the game's own Tameable.Tame (what the console "tame" and the
// taming timer call): never TameAllInArea, it would tame every wild boar loaded around the spawn stones.
internal static partial class SelfTests
{
    private const string TameToken = "$enemy_boar";
    private const string WolfToken = "$enemy_wolf";

    private static string Stored(params string[] lines) => "1\n" + string.Join("\n", lines);

    private static bool SameLines(List<string> found, params string[] expected)
    {
        if (found.Count != expected.Length)
        {
            return false;
        }
        for (var i = 0; i < expected.Length; i++)
        {
            if (found[i] != expected[i])
            {
                return false;
            }
        }
        return true;
    }

    private static string Lines(List<string> found) => found.Count == 0 ? "(none)" : string.Join(" || ", found.ToArray());

    // Frozen tameable creature at the spot, two frames to settle.
    private static IEnumerator SpawnTameable(Rig rig, string prefab, Vector3 spot, Tameable[] result)
    {
        var character = rig.Spawn(prefab, spot, true);
        result[0] = character != null ? character.GetComponent<Tameable>() : null;
        yield return null;
        yield return null;
    }

    // ---------- percreature.tames ----------

    private static IEnumerator RunTames()
    {
        var rig = Rig.Begin(TamesName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(TamesName);
        try
        {
            var p = rig.P;
            var boarName = CreatureName(Boar);
            var wolfName = CreatureName(Wolf);
            if (!c.Check(boarName == TameToken && wolfName == WolfToken,
                    $"Boar and Wolf are named {TameToken} and {WolfToken} (found '{boarName}', '{wolfName}')"))
            {
                c.Report();
                yield break;
            }
            // Like character that never killed or tamed these two: their rows then say only what this test tame.
            rig.ForgetKills(boarName, wolfName);
            rig.SetTames(null);
            var dir = OpenDirection(rig, false, 3f);
            var side = Vector3.Cross(Vector3.up, dir);
            var near = Ground(p.transform.position + dir * 3f);
            var far = Ground(p.transform.position + OpenDirection(rig, false, 40f) * 40f);
            var holder = new Tameable[1];
            var page = new Page();
            var boarMessage = Loc(boarName + TamedSuffix);

            // ----- tame next to the player: the game's message, and the count -----
            yield return SpawnTameable(rig, Boar, near, holder);
            var boar = holder[0];
            if (!c.Check(boar != null && boar.m_monsterAI != null && boar.m_character != null, "could not spawn a Boar that can be tamed"))
            {
                c.Report();
                yield break;
            }
            var statBefore = rig.Stat(PlayerStatType.CreatureTamed);
            rig.ArmMessage();
            var mark = _watch.Mark();
            boar.Tame();
            yield return null;
            c.Check(boar.IsTamed(), "the boar is tame after the game's Tame");
            c.Check(CreatureCounts.GetTames(boarName) == 1, $"tame next to the player: Boar +1 tamed (found {CreatureCounts.GetTames(boarName)})");
            c.Check(rig.RawTames == Stored("1\t" + boarName), $"stored value is exactly '1\\n1\\t{boarName}' (found '{Esc(rig.RawTames)}')");
            c.Check(SameLines(TameLines(mark), TameLine(boarName, "message", Stored("1\t" + boarName))),
                $"one log line 'Tame counted: {boarName} (message). Stored ...' (found {Lines(TameLines(mark))})");
            c.Check(rig.CenterText == boarMessage && boarMessage != NoMessage,
                $"the game shows its 'has been tamed' message ('{boarMessage}'; center text now '{rig.CenterText}')");
            c.Check(rig.Stat(PlayerStatType.CreatureTamed) == statBefore + 1f, "the game's own Creature Tamed total went up by 1");
            c.Note($"message shown for the tame: '{rig.CenterText}'");

            // ----- same boar again (console "tame" with a tame boar around): no count -----
            var rawBefore = rig.RawTames;
            statBefore = rig.Stat(PlayerStatType.CreatureTamed);
            rig.ArmMessage();
            mark = _watch.Mark();
            boar.Tame();
            yield return null;
            c.Check(CreatureCounts.GetTames(boarName) == 1 && ReferenceEquals(rig.RawTames, rawBefore) && TameLines(mark).Count == 0,
                $"taming the already tame boar again counts nothing and rewrites nothing (count {CreatureCounts.GetTames(boarName)}, "
                + $"log {Lines(TameLines(mark))})");
            c.Check(rig.CenterText == NoMessage, "no second 'has been tamed' message for the already tame boar");
            c.Note($"vanilla Creature Tamed total for that second call: +{F(rig.Stat(PlayerStatType.CreatureTamed) - statBefore)} (vanilla quirk, not this mod)");

            yield return ReadPage(page);
            if (c.Check(page.Ok, $"Player Statistics could not be read: {page.Problem}"))
            {
                var s = ParseSection(page.Entry);
                c.Check(s.Row(Loc(boarName)) == $"{Loc(boarName)}: 1 tamed",
                    $"a boar with no kills shows 'Boar: 1 tamed' on the reopened page (found {s.Describe()})");
            }

            // ----- two boars tamed in one go (the console command tames every boar around): +1 each -----
            yield return SpawnTameable(rig, Boar, Ground(near + side * 1.5f), holder);
            var second = holder[0];
            yield return SpawnTameable(rig, Boar, Ground(near - side * 1.5f), holder);
            var third = holder[0];
            if (c.Check(second != null && third != null, "could not spawn two more boars"))
            {
                mark = _watch.Mark();
                second.Tame();
                third.Tame();
                yield return null;
                c.Check(second.IsTamed() && third.IsTamed() && CreatureCounts.GetTames(boarName) == 3 && TameLines(mark).Count == 2,
                    $"two boars tamed in the same frame: +1 each (count {CreatureCounts.GetTames(boarName)}, expected 3; log {Lines(TameLines(mark))})");
            }

            // ----- far away: no message from the game, the owner's game counts it -----
            yield return SpawnTameable(rig, Wolf, far, holder);
            var wolf = holder[0];
            if (c.Check(wolf != null && wolf.m_monsterAI != null && wolf.m_character != null, "could not spawn a Wolf that can be tamed"))
            {
                var wolfPos = wolf.transform.position;
                var distance = Vector3.Distance(wolfPos, p.transform.position);
                c.Check(distance > MessageRange + 3f && Player.GetClosestPlayer(wolfPos, MessageRange) == null
                        && Player.GetClosestPlayer(wolfPos, float.MaxValue) == p && wolf.m_nview.IsOwner(),
                    $"far setup: the wolf is {F(distance)} m away (nobody within 30 m), this game controls it and this player is the closest");
                rig.ArmMessage();
                mark = _watch.Mark();
                wolf.Tame();
                yield return null;
                c.Check(wolf.IsTamed(), "the far wolf is tame after the game's Tame");
                c.Check(rig.CenterText == NoMessage, $"no 'has been tamed' message when nobody is within 30 m (center text '{rig.CenterText}')");
                c.Check(CreatureCounts.GetTames(wolfName) == 1, $"tame far away: Wolf +1 tamed for the player whose game ran it (found {CreatureCounts.GetTames(wolfName)})");
                c.Check(SameLines(TameLines(mark), TameLine(wolfName, "owner", Stored("3\t" + boarName, "1\t" + wolfName))),
                    $"one log line 'Tame counted: {wolfName} (owner). Stored ...' (found {Lines(TameLines(mark))})");
            }

            // ----- HUD hidden (Ctrl+F3): the game shows no message, the tame still counts -----
            rig.SetHudHidden(true);
            yield return null;
            yield return null;
            yield return SpawnTameable(rig, Boar, Ground(near + dir * 1.5f), holder);
            var hiddenBoar = holder[0];
            if (c.Check(hiddenBoar != null && Hud.IsUserHidden(), "hidden HUD setup: HUD hidden and a boar spawned"))
            {
                rig.ArmMessage();
                mark = _watch.Mark();
                hiddenBoar.Tame();
                yield return null;
                c.Check(hiddenBoar.IsTamed() && rig.CenterText == NoMessage, $"with the HUD hidden the game shows no message (center text '{rig.CenterText}')");
                c.Check(CreatureCounts.GetTames(boarName) == 4
                        && SameLines(TameLines(mark), TameLine(boarName, "message", Stored("4\t" + boarName, "1\t" + wolfName))),
                    $"with the HUD hidden the tame is still counted through the message (count {CreatureCounts.GetTames(boarName)}, expected 4; "
                    + $"log {Lines(TameLines(mark))})");
            }
            rig.SetHudHidden(false);
            yield return null;

            // ----- creature that starts tame (summons, offspring): never a tame -----
            yield return SpawnTameable(rig, Boar, Ground(near - dir * 1.2f + side * 0.8f), holder);
            var born = holder[0];
            if (c.Check(born != null, "could not spawn a boar for the starts-tame case"))
            {
                born.m_startsTamed = true;
                rawBefore = rig.RawTames;
                rig.ArmMessage();
                mark = _watch.Mark();
                born.Tame();
                yield return null;
                c.Check(CreatureCounts.GetTames(boarName) == 4 && ReferenceEquals(rig.RawTames, rawBefore) && TameLines(mark).Count == 0
                        && rig.CenterText == NoMessage,
                    $"a creature that starts tame is not counted (count {CreatureCounts.GetTames(boarName)}, log {Lines(TameLines(mark))})");
            }

            yield return ReadPage(page);
            if (c.Check(page.Ok, $"Player Statistics could not be read: {page.Problem}"))
            {
                var s = ParseSection(page.Entry);
                c.Check(s.Row(Loc(boarName)) == $"{Loc(boarName)}: 4 tamed" && s.Row(Loc(wolfName)) == $"{Loc(wolfName)}: 1 tamed",
                    $"the reopened page shows 'Boar: 4 tamed' and 'Wolf: 1 tamed' (found {s.Describe()})");
            }
        }
        finally
        {
            rig.End();
        }
        c.Report();
    }

    // ---------- percreature.tame-paths ----------

    // Stand-in for a mod that mutes MessageHud (MC One Click Repair All does it during its repair loop).
    private static bool MuteShowMessage() => false;

    private static IEnumerator RunTamePaths()
    {
        var rig = Rig.Begin(PathsName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(PathsName);
        Harmony mute = null;
        try
        {
            rig.SetTames(null);
            var p = rig.P;
            var boarName = CreatureName(Boar);
            var wolfName = CreatureName(Wolf);
            if (!c.Check(boarName != null && wolfName != null, "Boar and Wolf prefabs exist"))
            {
                c.Report();
                yield break;
            }
            var dir = OpenDirection(rig, false, 4f);
            var side = Vector3.Cross(Vector3.up, dir);
            var near = Ground(p.transform.position + dir * 4f);
            var far = Ground(p.transform.position + OpenDirection(rig, false, 40f) * 40f);
            var holder = new Tameable[1];
            var boarMessage = Loc(boarName + TamedSuffix);

            // ----- the game's taming timer finishes next to the player (what feeding ends in) -----
            yield return SpawnTameable(rig, Boar, near, holder);
            var fed = holder[0];
            if (c.Check(fed != null && fed.m_nview != null && fed.m_nview.IsValid(), "could not spawn a boar for the taming timer"))
            {
                // Hungry boar: timer no run, nothing tamed.
                fed.m_nview.GetZDO().Set(ZDOVars.s_tameTimeLeft, 1f);
                var mark = _watch.Mark();
                fed.TamingUpdate();
                c.Check(fed.IsHungry() && !fed.IsTamed() && TameLines(mark).Count == 0, "a hungry boar is not tamed by the timer and nothing is counted");
                // Fed boar with 1 s left: next timer tick tame it.
                fed.ResetFeedingTimer();
                rig.ArmMessage();
                fed.TamingUpdate();
                yield return null;
                c.Check(fed.IsTamed() && CreatureCounts.GetTames(boarName) == 1
                        && SameLines(TameLines(mark), TameLine(boarName, "message", Stored("1\t" + boarName)))
                        && rig.CenterText == boarMessage,
                    $"a fed boar whose taming time runs out next to the player is counted through the message (tame {fed.IsTamed()}, count "
                    + $"{CreatureCounts.GetTames(boarName)}, log {Lines(TameLines(mark))}, center text '{rig.CenterText}')");
            }

            // ----- same, with the player far away -----
            yield return SpawnTameable(rig, Boar, far, holder);
            var fedFar = holder[0];
            if (c.Check(fedFar != null && Player.GetClosestPlayer(fedFar.transform.position, MessageRange) == null,
                    "could not spawn a boar more than 30 m away for the taming timer"))
            {
                fedFar.m_nview.GetZDO().Set(ZDOVars.s_tameTimeLeft, 1f);
                fedFar.ResetFeedingTimer();
                rig.ArmMessage();
                var mark = _watch.Mark();
                fedFar.TamingUpdate();
                yield return null;
                c.Check(fedFar.IsTamed() && CreatureCounts.GetTames(boarName) == 2
                        && SameLines(TameLines(mark), TameLine(boarName, "owner", Stored("2\t" + boarName)))
                        && rig.CenterText == NoMessage,
                    $"a fed boar whose taming time runs out while the player is far away is counted for the game that ran it, with no message "
                    + $"(tame {fedFar.IsTamed()}, count {CreatureCounts.GetTames(boarName)}, log {Lines(TameLines(mark))})");
            }

            // ----- "has been tamed" sent by another game (it controls the creature, this player is the closest) -----
            {
                rig.ArmMessage();
                var mark = _watch.Mark();
                p.m_nview.InvokeRPC("Message", (int)MessageHud.MessageType.Center, wolfName + TamedSuffix, 0);
                yield return null;
                c.Check(CreatureCounts.GetTames(wolfName) == 1
                        && SameLines(TameLines(mark), TameLine(wolfName, "message", Stored("2\t" + boarName, "1\t" + wolfName)))
                        && rig.CenterText == Loc(wolfName + TamedSuffix),
                    $"the game's tame message arriving as a network message for this player counts Wolf +1 and is shown "
                    + $"(count {CreatureCounts.GetTames(wolfName)}, log {Lines(TameLines(mark))}, center text '{rig.CenterText}')");

                // Other messages never count. Me try a few.
                var rawBefore = rig.RawTames;
                mark = _watch.Mark();
                p.m_nview.InvokeRPC("Message", (int)MessageHud.MessageType.TopLeft, wolfName + TamedSuffix, 0);
                p.m_nview.InvokeRPC("Message", (int)MessageHud.MessageType.Center, wolfName + " $hud_tamefollow", 0);
                p.m_nview.InvokeRPC("Message", (int)MessageHud.MessageType.Center, TamedSuffix, 0);
                p.Message(MessageHud.MessageType.Center, "$hud_tamedone");
                p.Message(MessageHud.MessageType.TopLeft, boarName + TamedSuffix);
                yield return null;
                c.Check(ReferenceEquals(rig.RawTames, rawBefore) && TameLines(mark).Count == 0,
                    $"a top-left message, another tame text ('follows you'), and a tame text with no creature name count nothing (log {Lines(TameLines(mark))})");
            }

            // ----- a mod mutes the message display: the tame still counts -----
            mute = new Harmony(ModInfo.Guid + ".selftest.mute");
            mute.Patch(AccessTools.Method(typeof(MessageHud), nameof(MessageHud.ShowMessage)),
                prefix: new HarmonyMethod(typeof(SelfTests), nameof(MuteShowMessage)));
            yield return SpawnTameable(rig, Boar, Ground(near + side * 1.5f), holder);
            var muted = holder[0];
            if (c.Check(muted != null, "could not spawn a boar for the muted-message case"))
            {
                rig.ArmMessage();
                var mark = _watch.Mark();
                muted.Tame();
                yield return null;
                c.Check(muted.IsTamed() && rig.CenterText == NoMessage, $"setup: with the message display muted nothing is shown (center text '{rig.CenterText}')");
                c.Check(CreatureCounts.GetTames(boarName) == 3
                        && SameLines(TameLines(mark), TameLine(boarName, "message", Stored("3\t" + boarName, "1\t" + wolfName))),
                    $"with the message display muted by another mod the tame is still counted (count {CreatureCounts.GetTames(boarName)}, expected 3; "
                    + $"log {Lines(TameLines(mark))})");
            }
            mute.UnpatchSelf();
            mute = null;
            var repair = FeatureRegistry.Find("MC.Crafting.Repair.OneClickAll");
            c.Note(repair != null ? $"One Click Repair All is loaded in this run: {repair.Value.State}" : "One Click Repair All is not loaded in this run");
        }
        finally
        {
            if (mute != null)
            {
                mute.UnpatchSelf();
            }
            rig.End();
        }
        c.Report();
    }

    // ---------- percreature.feed ----------

    // Open ground for a wild creature with its AI on, at one of these distances from the player: not in or next to a
    // no-monster area (vanilla MonsterAI runs away from it, so it would never eat).
    private static bool WildSpot(Rig rig, float[] distances, out Vector3 spot)
    {
        var from = rig.P.transform.position;
        var forward = Flat(rig.P.transform.forward);
        foreach (var d in distances)
        {
            for (var i = 0; i < 12; i++)
            {
                var angle = (i % 2 == 0 ? 1f : -1f) * ((i + 1) / 2) * 30f;
                var p = Ground(from + Quaternion.Euler(0f, angle, 0f) * forward * d);
                if (OpenGround(p) && EffectArea.IsPointInsideNoMonsterArea(p) == null && EffectArea.IsPointCloseToNoMonsterArea(p) == null)
                {
                    spot = p;
                    return true;
                }
            }
        }
        spot = Ground(from + forward * distances[0]);
        return false;
    }

    // Real feeding (no direct Tame call): wild boars, a berry on the ground next to each, the game's own AI eats it and
    // the game's own timer tames. Me only shorten the waits (time left on the taming, the search for food).
    private static IEnumerator RunFeed()
    {
        var rig = Rig.Begin(FeedName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(FeedName);
        try
        {
            rig.SetTames(null);
            var p = rig.P;
            var boarName = CreatureName(Boar);
            var foods = new[] { "Raspberry", "Blueberries" };
            var foodPrefabs = new GameObject[2];
            for (var i = 0; i < 2; i++)
            {
                foodPrefabs[i] = ObjectDB.instance.GetItemPrefab(foods[i]);
            }
            if (!c.Check(boarName != null && foodPrefabs[0] != null && foodPrefabs[1] != null, "Boar, Raspberry and Blueberries exist"))
            {
                c.Report();
                yield break;
            }
            // Boars must no see player (they attack, and alerted boar no eat).
            rig.SetGhost(true);
            var spots = new Vector3[2];
            var nearFound = WildSpot(rig, new[] { 8f, 12f, 16f, 20f, 24f }, out spots[0]);
            var farFound = WildSpot(rig, new[] { 45f, 50f, 55f }, out spots[1]);
            if (!c.Check(nearFound && farFound,
                    $"no open ground outside the no-monster areas for a wild boar (within 24 m: {nearFound}; 45 to 55 m away: {farFound}); "
                    + "a wild creature runs from such an area instead of eating"))
            {
                c.Report();
                yield break;
            }
            var labels = new[] { "near", "far" };
            var boars = new Tameable[2];
            var ais = new MonsterAI[2];
            var mark = _watch.Mark();
            for (var i = 0; i < 2; i++)
            {
                var character = rig.Spawn(Boar, spots[i], false);
                boars[i] = character != null ? character.GetComponent<Tameable>() : null;
                ais[i] = character != null ? character.GetComponent<MonsterAI>() : null;
            }
            yield return Wait(1f, null);
            if (!c.Check(boars[0] != null && boars[1] != null && ais[0] != null && ais[1] != null, "could not spawn two wild boars"))
            {
                c.Report();
                yield break;
            }
            for (var i = 0; i < 2; i++)
            {
                boars[i].m_nview.GetZDO().Set(ZDOVars.s_tameTimeLeft, 1f);
                var at = boars[i].transform.position + boars[i].transform.forward * 0.8f + Vector3.up * 0.4f;
                var food = rig.SpawnObject(foodPrefabs[i], at).GetComponent<ItemDrop>();
                if (food != null)
                {
                    // Few berries: wild boar passing by may take one too.
                    food.SetStack(3);
                }
            }
            c.Check(boars[0].IsHungry() && boars[1].IsHungry() && !boars[0].IsTamed() && !boars[1].IsTamed(), "setup: two wild, hungry boars");

            var w = new Waiter();
            var nextNudge = 0f;
            yield return Until(() => boars[0] != null && boars[1] != null && boars[0].IsTamed() && boars[1].IsTamed(), 70f, () =>
            {
                if (Time.time < nextNudge)
                {
                    return;
                }
                nextNudge = Time.time + 2f;
                for (var i = 0; i < 2; i++)
                {
                    // Vanilla look for food every 10 s: me make the next look come now. Eating itself is the game's.
                    if (ais[i] != null && boars[i] != null && boars[i].IsHungry())
                    {
                        ais[i].m_consumeSearchTimer = ais[i].m_consumeSearchInterval;
                    }
                }
            }, w);
            for (var i = 0; i < 2; i++)
            {
                var b = boars[i];
                c.Note($"{labels[i]} boar: "
                       + (b != null
                           ? $"tame {b.IsTamed()}, hungry {b.IsHungry()}, alerted {ais[i].IsAlerted()}, {F(Vector3.Distance(b.transform.position, p.transform.position))} m from the player"
                           : "gone"));
            }
            c.Check(w.Met, $"both boars must eat their berry and become tame by themselves within 70 s (took {F(w.Took)} s)");
            if (w.Met)
            {
                var lines = TameLines(mark);
                var byMessage = 0;
                var byOwner = 0;
                foreach (var line in lines)
                {
                    if (line.StartsWith($"Tame counted: {boarName} (message).", System.StringComparison.Ordinal))
                    {
                        byMessage++;
                    }
                    else if (line.StartsWith($"Tame counted: {boarName} (owner).", System.StringComparison.Ordinal))
                    {
                        byOwner++;
                    }
                }
                var nearDistance = Vector3.Distance(boars[0].transform.position, p.transform.position);
                var farDistance = Vector3.Distance(boars[1].transform.position, p.transform.position);
                c.Check(nearDistance < MessageRange && farDistance > MessageRange,
                    $"setup: the near boar ended within 30 m ({F(nearDistance)} m), the far one beyond ({F(farDistance)} m)");
                c.Check(CreatureCounts.GetTames(boarName) == 2,
                    $"natural taming by feeding: Boar +1 tamed for the boar next to the player and +1 for the one far away (count {CreatureCounts.GetTames(boarName)}, expected 2)");
                c.Check(lines.Count == 2 && byMessage == 1 && byOwner == 1,
                    $"the near boar is counted through the game's message, the far one for the game that ran the taming (log {Lines(lines)})");
            }
        }
        finally
        {
            rig.End();
        }
        c.Report();
    }

    // ---------- percreature.bug.respawn-gap ----------

    // Real bug (in-world run: Boar tamed 0, game's own Creature Tamed total +1): between the removal of the dead
    // character and the respawn (and while a world loads) there is no local player and no player in the game's player
    // list (Player.OnDestroy clear both). A fed boar can finish taming then: the game tames it, this mod counts
    // nothing, ever (Tame postfix leave when no local player, nothing remember the tame). README say "in single player
    // you always get your own tames". Test ask for the right result, so it FAIL until the mod is fixed. Only this
    // check live here: every other tame check stay in its own test.
    private static IEnumerator RunRespawnGap()
    {
        var rig = Rig.Begin(GapName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(GapName);
        var local = rig.P;
        try
        {
            rig.SetTames(null);
            var boarName = CreatureName(Boar);
            var far = Ground(local.transform.position + OpenDirection(rig, false, 40f) * 40f);
            var holder = new Tameable[1];
            yield return SpawnTameable(rig, Boar, far, holder);
            var boar = holder[0];
            if (!c.Check(boar != null && boarName != null, "could not spawn a boar"))
            {
                c.Report();
                yield break;
            }
            var statBefore = rig.Stat(PlayerStatType.CreatureTamed);
            // One block, no frame in between: game see no local player and no player at all (dead body removed =
            // out of the player list too), like in respawn wait. Me put both back in finally.
            var players = Player.s_players;
            var listed = players.IndexOf(local);
            var sawNobody = false;
            try
            {
                if (listed >= 0)
                {
                    players.RemoveAt(listed);
                }
                Player.m_localPlayer = null;
                sawNobody = Player.GetClosestPlayer(boar.transform.position, float.MaxValue) == null;
                boar.Tame();
            }
            finally
            {
                Player.m_localPlayer = local;
                if (listed >= 0 && !players.Contains(local))
                {
                    players.Insert(Mathf.Min(listed, players.Count), local);
                }
            }
            c.Check(Player.m_localPlayer == local && Player.GetClosestPlayer(local.transform.position, 1f) == local,
                "clean-up: the character is the local character again and back in the game's player list");
            c.Check(sawNobody && boar.IsTamed() && rig.Stat(PlayerStatType.CreatureTamed) == statBefore + 1f,
                $"setup: the game tamed the boar while it had no local character and no character at all, as in the respawn wait "
                + $"(saw nobody {sawNobody}, tame {boar.IsTamed()}, its own Creature Tamed total +{F(rig.Stat(PlayerStatType.CreatureTamed) - statBefore)})");
            // Character back: me run what mod do at every spawn.
            Patches.PlayerPatches.TestSpawned(local);
            yield return null;
            yield return null;
            c.Check(CreatureCounts.GetTames(boarName) == 1,
                $"a tame that finishes while the character is waiting to respawn must be counted once the character is back: Boar +1 tamed "
                + $"(found {CreatureCounts.GetTames(boarName)}; the game's own total did go up)");
        }
        finally
        {
            if (Player.m_localPlayer == null && local != null)
            {
                Player.m_localPlayer = local;
            }
            rig.End();
        }
        c.Report();
    }
}
#endif
