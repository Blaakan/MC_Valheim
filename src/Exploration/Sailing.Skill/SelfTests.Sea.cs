#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Logging;
using MC.Exploration.SailingSkillMod.Patches;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SailingSkillMod;

// Debug build only. Me = tests on real water: player sent to the open sea of sailing.ship (fast teleport), Karve spawned
// and steered, wind fixed with the game's own debug wind (console "wind"), ship speed read every physics step.
//   sailing.reach   wind from the side, Full sail, Sailing 0 and 100: same top speed reached sooner, stop from full
//                   speed (brake), and the same stop with nobody at the helm (drift sideways and heel: NOTE only)
//   sailing.upwind  34 degrees off the wind (nothing at 0, sails at 100), 20 degrees (nothing at 100), wind from
//                   behind (faster at 100), turning under sail
//   sailing.paddle  calm, paddling (Slow): same top speed reached sooner, turning
//   sailing.heel    calm, wind from the side, Full sail: Sailing 100 heels and drifts sideways like Sailing 0, against
//                   a ship whose whole sail force is x1.5 (the yardstick that the measure can tell)
//   sailing.crew    passenger standing on the deck of a ship under sail earns nothing (also crouching), map reveal on
//                   the deck, swimmer inside the ship's volume (normal map reveal, still protects the ship), swims
//                   away, climbs aboard
// Speeds on water are noisy (waves): every number is a mean over a window, limits are wide. A rig that does not come
// up (no sea, no teleport, ship not afloat) is a FAIL here, never a silent pass: these tests stand for test-list
// items.
internal static partial class SelfTests
{
    private const string ReachName = "sailing.reach";
    private const string UpwindName = "sailing.upwind";
    private const string PaddleName = "sailing.paddle";
    private const string HeelName = "sailing.heel";
    private const string CrewName = "sailing.crew";

    // Wind strength of the sail tests. Waves grow with the wind (vanilla WaterVolume), the sail's pull only from 0.25
    // to 1: half wind keeps the sea moderate and the ship still fast.
    private const float SailWind = 0.5f;

    private sealed class Sea
    {
        internal ShipRig Rig;
        internal readonly WindScope Wind = new WindScope();
        internal Vector3 Start;
        internal float Draught;   // ship height minus water surface, afloat at rest
        internal bool Ok;
        internal bool Away;       // player sent to the sea: must come back
    }

    // Me send player to open sea, fix wind, spawn ship with bow <offWind> degrees off the wind, put player at helm.
    private static IEnumerator SetUpSea(Sea sea, Checks c, float windAngle, float windIntensity, float offWind)
    {
        sea.Ok = false;
        var rig = sea.Rig;
        var player = rig.Player;
        var water = ZoneSystem.instance.m_waterLevel;
        var found = new Box();
        yield return FindOcean(rig.Origin, found);
        if (!c.Check(found.Ok, "no open sea within 5 km of the player: nothing measured"))
        {
            yield break;
        }
        var point = found.Point;
        sea.Away = true;
        var arrived = new Box();
        yield return TeleportAndWait(player, new Vector3(point.x, water + 1.5f, point.z), 40f, arrived, fast: true);
        if (!c.Check(arrived.Ok, "the teleport to the sea did not finish: nothing measured"))
        {
            yield break;
        }
        if (!c.Check(sea.Wind.Ok, "no weather (EnvMan): nothing measured"))
        {
            yield break;
        }
        sea.Wind.Set(windAngle, windIntensity);
        var probe = new Vector3(point.x + 8f, water - 0.5f, point.z);
        var surface = -10000f;
        var until = Time.time + 10f;
        while (Time.time < until)
        {
            surface = Floating.GetLiquidLevel(probe);
            if (surface > water - 5f)
            {
                break;
            }
            yield return new WaitForSeconds(0.25f);
        }
        if (!c.Check(surface > water - 5f, "no water at the sea point after 10 s: nothing measured"))
        {
            yield break;
        }
        // 8 m from the swimming player (no overlap push).
        var forward = HeadingOffWind(sea.Wind.Dir, offWind);
        var start = new Vector3(probe.x, surface + 0.3f, probe.z);
        if (!c.Check(rig.Spawn(start, Quaternion.LookRotation(forward, Vector3.up), kinematic: false), "no ship prefab (Karve) or it did not spawn"))
        {
            yield break;
        }
        yield return new WaitForSeconds(2f);
        if (!c.Check(rig.Boat != null && rig.TakeHelm(), "the ship is gone or has no helm attach point"))
        {
            yield break;
        }
        var aboard = new Box();
        yield return rig.WaitAboard(aboard);
        var ship = rig.Boat;
        if (!c.Check(aboard.Ok && ship.IsOwner() && ReferenceEquals(player.GetControlledShip(), ship) && ship.transform.up.y > 0.8f,
                "the player is not at the helm of an upright ship of his own"))
        {
            yield break;
        }
        var level = Floating.GetLiquidLevel(new Vector3(ship.transform.position.x, water - 0.5f, ship.transform.position.z));
        sea.Draught = level > water - 5f ? Mathf.Clamp(ship.transform.position.y - level, -2f, 2f) : 0f;
        sea.Start = start;
        sea.Ok = true;
    }

    // Me put ship back at start, still, sails down, bow as told; then <settle> seconds to sit on the water.
    private static IEnumerator Afloat(Sea sea, Vector3 forward, float settle = 1f)
    {
        var p = sea.Start;
        var level = Floating.GetLiquidLevel(new Vector3(p.x, ZoneSystem.instance.m_waterLevel - 0.5f, p.z));
        if (level > ZoneSystem.instance.m_waterLevel - 5f)
        {
            p.y = level + sea.Draught;
        }
        ResetBoat(sea.Rig.Boat, p, forward);
        var until = Time.time + settle;
        while (Time.time < until)
        {
            yield return new WaitForFixedUpdate();
        }
    }

    // Ship gone, wind back, player home. Normal end of a sea test (finally does the rough version).
    private static IEnumerator LeaveSea(Sea sea, Checks c)
    {
        var rig = sea.Rig;
        HelmSkill.TestLocalLevel = null;
        if (rig.Boat != null)
        {
            yield return rig.Leave(rig.Boat.transform.position + rig.Boat.transform.right * 20f + Vector3.up);
        }
        sea.Wind.Restore();
        if (sea.Away)
        {
            var home = new Box();
            yield return TeleportAndWait(rig.Player, rig.Origin, 40f, home, fast: true);
            c.Check(home.Ok, "the player did not get back to the start after the voyage");
            sea.Away = !home.Ok;
        }
    }

    // What the ship did, one entry per physics step.
    private sealed class Trace
    {
        internal readonly List<float> T = new List<float>();
        internal readonly List<float> Fwd = new List<float>();    // speed along the bow
        internal readonly List<float> Side = new List<float>();   // speed sideways
        internal readonly List<float> Roll = new List<float>();   // heel, degrees
        internal readonly List<float> Head = new List<float>();   // compass heading, degrees

        internal int Count => T.Count;

        internal float Last => T.Count > 0 ? T[T.Count - 1] : 0f;

        internal float Mean(List<float> values, float from, float to)
        {
            var sum = 0f;
            var n = 0;
            for (var i = 0; i < T.Count; i++)
            {
                if (T[i] >= from && T[i] <= to)
                {
                    sum += values[i];
                    n++;
                }
            }
            return n > 0 ? sum / n : 0f;
        }

        // First moment the half-second mean of the forward speed reaches the value (-1 = never).
        internal float TimeTo(float value)
        {
            const int window = 25;
            var sum = 0f;
            for (var i = 0; i < Fwd.Count; i++)
            {
                sum += Fwd[i];
                if (i >= window)
                {
                    sum -= Fwd[i - window];
                }
                if (i >= window - 1 && sum / window >= value)
                {
                    return T[i];
                }
            }
            return -1f;
        }

        // Degrees the bow turned between two moments (either way).
        internal float Turned(float from, float to)
        {
            var a = float.NaN;
            var b = float.NaN;
            for (var i = 0; i < T.Count; i++)
            {
                if (float.IsNaN(a) && T[i] >= from)
                {
                    a = Head[i];
                }
                if (T[i] <= to)
                {
                    b = Head[i];
                }
            }
            return float.IsNaN(a) || float.IsNaN(b) ? 0f : Mathf.Abs(Mathf.DeltaAngle(a, b));
        }
    }

    // Me read ship every physics step for <seconds>, or until done say so. eachStep run first every step (hold a key).
    private static IEnumerator Record(Ship ship, Trace trace, float seconds, Func<Trace, bool> done = null, Action eachStep = null)
    {
        var t0 = Time.time;
        while (ship != null && ship.m_body != null)
        {
            yield return new WaitForFixedUpdate();
            if (ship == null || ship.m_body == null)
            {
                break;
            }
            if (eachStep != null)
            {
                eachStep();
            }
            var t = Time.time - t0;
            var v = ship.m_body.linearVelocity;
            var forward = ship.transform.forward;
            trace.T.Add(t);
            trace.Fwd.Add(Vector3.Dot(v, forward));
            trace.Side.Add(Vector3.Dot(v, ship.transform.right));
            trace.Roll.Add(Mathf.Asin(Mathf.Clamp(ship.transform.right.y, -1f, 1f)) * Mathf.Rad2Deg);
            trace.Head.Add(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg);
            if (t >= seconds || (done != null && done(trace)))
            {
                break;
            }
        }
    }

    private static void HoldRudder(Ship ship) => ship.ApplyControlls(new Vector3(1f, 0f, 0f));

    // ---------- sailing.reach ----------

    private sealed class ReachRun
    {
        internal float Top;
        internal float T90;
        internal float Seconds;
        internal float Early;      // forward speed 2 s after the sails went up
        internal float Leeway;     // mean sideways speed at top speed
        internal float Heel;       // mean heel at top speed, degrees
        internal float StopFrom;
        internal float Stop2;
        internal float Stop35;
        internal Vector3 SailForce;
        internal Vector3 SailVelocity;

        internal float Kept => StopFrom > 0.01f ? Stop35 / StopFrom : 0f;

        internal string Text(float level) =>
            $"Sailing {F(level)}: top {F(Top)} m/s after {F(Seconds)} s (90% at {F(T90)} s, {F(Early)} m/s at 2 s), sideways "
            + $"{F(Leeway)} m/s, heel {F(Heel)} deg; Stop from {F(StopFrom)} m/s: {F(Stop2)} at 2 s, {F(Stop35)} at 3.4 s";
    }

    private static IEnumerator RunReach()
    {
        var c = new Checks(ReachName);
        var rig = ShipRig.Create(ReachName);
        if (rig == null)
        {
            yield break;
        }
        var watch = LogMark();
        var sea = new Sea { Rig = rig };
        var started = Time.realtimeSinceStartup;
        try
        {
            var rules = new SailingRules();
            ServerRules.TestRules = rules;
            yield return SetUpSea(sea, c, 0f, SailWind, 90f);
            if (!sea.Ok)
            {
                yield return LeaveSea(sea, c);
                c.Report();
                yield break;
            }
            var player = rig.Player;
            var ship = rig.Boat;
            var across = HeadingOffWind(sea.Wind.Dir, 90f);
            var runs = new ReachRun[2];
            for (var li = 0; li < 2; li++)
            {
                var run = runs[li] = new ReachRun();
                HelmSkill.TestLocalLevel = li * 100f;
                yield return Afloat(sea, across);
                ship.m_speed = Ship.Speed.Full;
                var trace = new Trace();
                // Long enough for top speed at both levels (Sailing 100 gets there sooner): no early stop, waves
                // would fool it.
                yield return Record(ship, trace, li == 0 ? 17f : 13f);
                run.Seconds = trace.Last;
                run.Top = trace.Mean(trace.Fwd, trace.Last - 2f, trace.Last);
                run.T90 = trace.TimeTo(0.9f * run.Top);
                run.Early = trace.Mean(trace.Fwd, 1.75f, 2.25f);
                run.Leeway = Mathf.Abs(trace.Mean(trace.Side, trace.Last - 3f, trace.Last));
                run.Heel = Mathf.Abs(trace.Mean(trace.Roll, trace.Last - 3f, trace.Last));
                run.StopFrom = trace.Mean(trace.Fwd, trace.Last - 0.5f, trace.Last);
                run.SailForce = ship.m_sailForce;
                run.SailVelocity = ship.m_windChangeVelocity;
                ship.m_speed = Ship.Speed.Stop;
                var coast = new Trace();
                yield return Record(ship, coast, 3.6f);
                run.Stop2 = coast.Mean(coast.Fwd, 1.8f, 2.2f);
                run.Stop35 = coast.Mean(coast.Fwd, 3.2f, 3.6f);
                c.Note(run.Text(li * 100f));
            }
            var low = runs[0];
            var high = runs[1];
            if (c.Check(low.Top > 1.5f && high.Top > 1.5f && low.T90 > 0f && high.T90 > 0f,
                    $"the ship did not get under way with the wind from the side (top {F(low.Top)} / {F(high.Top)} m/s)"))
            {
                // Speeding up: same top speed, sooner.
                c.Check(Near(high.Top, low.Top, low.Top * 0.1f), $"top speed {F(high.Top)} m/s at Sailing 100 against {F(low.Top)} at 0, expected about the same");
                c.Check(high.T90 <= low.T90 * 0.85f, $"90% of top speed after {F(high.T90)} s at Sailing 100 against {F(low.T90)} s at 0, expected about a third sooner");
                c.Check(high.Early > low.Early * 1.1f, $"2 s after the sails went up: {F(high.Early)} m/s at Sailing 100 against {F(low.Early)} at 0, expected clearly more");
                // Drift sideways and heel: only in the NOTE here. With this wind the waves roll the ship more than the
                // sail heels it, and the big waves take 9 to 13 s each: a mean over 3 s is the wave me happen to be on
                // (two runs 2026-10-07: heel 12.3 against 8.2 degrees, then 7.3 against 13.1, Sailing 0 against 100).
                // sailing.heel measures both on a calm sea, against a ship whose whole sail force is x1.5.
                // Stopping.
                c.Check(low.Kept > 0.45f && low.Stop35 > 1f, $"Sailing 0 at Stop: {F(low.Stop35)} m/s left after 3.4 s ({F(low.Kept * 100f)}%), expected a long drift");
                c.Check(high.Kept < 0.2f && high.Stop35 < 0.6f, $"Sailing 100 at Stop: {F(high.Stop35)} m/s left after 3.4 s ({F(high.Kept * 100f)}%), expected nearly stopped");

                // Nobody at the helm at Sailing 100: the same ship state as the Sailing 0 run the moment it went to Stop.
                HelmSkill.TestLocalLevel = 100f;
                yield return Afloat(sea, across);
                player.StopDoodadControl();   // vanilla: the helm is free, the player stands up
                var controls = ship.m_shipControlls;
                // Sits attached, not steering: the push below would throw a standing player off the deck.
                player.AttachStart(controls.m_attachPoint, null, false, false, true, controls.m_attachAnimation, controls.m_detachOffset);
                yield return new WaitForSeconds(0.7f);   // the ship owner's helmsman value (kept 0.5 s) ran out
                c.Check(controls.GetUser() == 0L && player.GetControlledShip() == null && HelmSkill.Helmsman(ship) == null
                        && ship.IsPlayerInBoat(player),
                    "nobody-at-the-helm state not reached (helm still taken, or the player left the ship)");
                ship.m_body.linearVelocity = ship.transform.forward * low.StopFrom;
                ship.m_sailForce = low.SailForce;
                ship.m_windChangeVelocity = low.SailVelocity;
                ship.m_speed = Ship.Speed.Stop;
                ShipFixedUpdatePatches.LastShip = null;
                var drift = new Trace();
                yield return Record(ship, drift, 3.6f);
                var kept = low.StopFrom > 0.01f ? drift.Mean(drift.Fwd, 3.2f, 3.6f) / low.StopFrom : 0f;
                c.Note($"Sailing 100 aboard, nobody at the helm, Stop from {F(low.StopFrom)} m/s: {F(kept * 100f)}% left after 3.4 s "
                       + $"(Sailing 0 at the helm {F(low.Kept * 100f)}%, Sailing 100 at the helm {F(high.Kept * 100f)}%)");
                c.Check(ReferenceEquals(ShipFixedUpdatePatches.LastShip, ship) && ShipFixedUpdatePatches.LastAccel == 1f
                        && ShipFixedUpdatePatches.LastTurn == 1f,
                    "nobody at the helm: the ship's physics step still used the skill");
                c.Check(Mathf.Abs(kept - low.Kept) <= 0.15f && kept > high.Kept * 2f,
                    $"nobody at the helm at Sailing 100: {F(kept * 100f)}% of the speed left after 3.4 s, expected the normal drift "
                    + $"({F(low.Kept * 100f)}%)");
                player.AttachStop();
            }
            var like = FieldsLikePrefab(ship, rig.PrefabShip, out var fields);
            c.Check(like, "ship fields not put back after the voyage: " + fields);
            yield return LeaveSea(sea, c);
            c.Note($"took {F(Time.realtimeSinceStartup - started)} s");
            CheckQuiet(c, watch);
            c.Report();
        }
        finally
        {
            sea.Wind.Restore();
            rig.Restore();
        }
    }

    // ---------- sailing.upwind ----------

    private static IEnumerator RunUpwind()
    {
        var c = new Checks(UpwindName);
        var rig = ShipRig.Create(UpwindName);
        if (rig == null)
        {
            yield break;
        }
        var watch = LogMark();
        var sea = new Sea { Rig = rig };
        var started = Time.realtimeSinceStartup;
        try
        {
            var rules = new SailingRules();
            ServerRules.TestRules = rules;
            yield return SetUpSea(sea, c, 0f, SailWind, 34f);
            if (!sea.Ok)
            {
                yield return LeaveSea(sea, c);
                c.Report();
                yield break;
            }
            var ship = rig.Boat;
            var wind = sea.Wind.Dir;

            // 34 degrees off the wind: inside the normal game's no-go zone, outside Sailing 100's.
            var close = new float[2];
            for (var li = 0; li < 2; li++)
            {
                HelmSkill.TestLocalLevel = li * 100f;
                yield return Afloat(sea, HeadingOffWind(wind, 34f));
                ship.m_speed = Ship.Speed.Full;
                var trace = new Trace();
                yield return Record(ship, trace, 4.5f);
                close[li] = trace.Mean(trace.Fwd, 2f, 4.5f);
            }
            c.Note($"34 degrees off the wind, Full sail, 2 to 4.5 s: {F(close[0])} m/s at Sailing 0, {F(close[1])} m/s at 100");
            c.Check(Mathf.Abs(close[0]) < 0.35f, $"34 degrees off the wind at Sailing 0: the ship makes {F(close[0])} m/s, expected none (no-go zone)");
            c.Check(close[1] > 0.6f && close[1] > close[0] + 0.4f, $"34 degrees off the wind at Sailing 100: the ship makes {F(close[1])} m/s, expected to sail");

            // 20 degrees: inside Sailing 100's no-go zone too.
            HelmSkill.TestLocalLevel = 100f;
            yield return Afloat(sea, HeadingOffWind(wind, 20f));
            ship.m_speed = Ship.Speed.Full;
            var pinch = new Trace();
            yield return Record(ship, pinch, 3.6f);
            var pinched = pinch.Mean(pinch.Fwd, 1.5f, 3.6f);
            c.Check(Mathf.Abs(pinched) < 0.35f, $"20 degrees off the wind at Sailing 100: the ship makes {F(pinched)} m/s, expected none");

            // Wind from behind until top speed, then the rudder held over for 3 s.
            var top = new float[2];
            var turned = new float[2];
            for (var li = 0; li < 2; li++)
            {
                HelmSkill.TestLocalLevel = li * 100f;
                yield return Afloat(sea, HeadingOffWind(wind, 180f));
                ship.m_speed = Ship.Speed.Full;
                var trace = new Trace();
                yield return Record(ship, trace, li == 0 ? 14f : 11f);
                top[li] = trace.Mean(trace.Fwd, trace.Last - 2f, trace.Last);
                var turn = new Trace();
                yield return Record(ship, turn, 3f, null, () => HoldRudder(ship));
                turned[li] = turn.Turned(0f, 3f);
                c.Note($"wind from behind, Sailing {li * 100}: top {F(top[li])} m/s after {F(trace.Last)} s; rudder held 3 s: turned {F(turned[li])} degrees");
            }
            if (c.Check(top[0] > 1.5f && top[1] > 1.5f, $"the ship did not get under way with the wind from behind ({F(top[0])} / {F(top[1])} m/s)"))
            {
                c.Check(top[1] >= top[0] * 1.05f, $"wind from behind: {F(top[1])} m/s at Sailing 100 against {F(top[0])} at 0, expected faster");
                c.Check(turned[0] > 2f && turned[1] >= turned[0] * 1.3f,
                    $"rudder held 3 s under sail: turned {F(turned[1])} degrees at Sailing 100 against {F(turned[0])} at 0, expected clearly more");
            }
            var like = FieldsLikePrefab(ship, rig.PrefabShip, out var fields);
            c.Check(like, "ship fields not put back after the voyage: " + fields);
            yield return LeaveSea(sea, c);
            c.Note($"took {F(Time.realtimeSinceStartup - started)} s");
            CheckQuiet(c, watch);
            c.Report();
        }
        finally
        {
            sea.Wind.Restore();
            rig.Restore();
        }
    }

    // ---------- sailing.paddle ----------

    private static IEnumerator RunPaddle()
    {
        var c = new Checks(PaddleName);
        var rig = ShipRig.Create(PaddleName);
        if (rig == null)
        {
            yield break;
        }
        var watch = LogMark();
        var sea = new Sea { Rig = rig };
        var started = Time.realtimeSinceStartup;
        try
        {
            var rules = new SailingRules();
            ServerRules.TestRules = rules;
            // Calm: smallest waves the game has.
            yield return SetUpSea(sea, c, 0f, 0.05f, 90f);
            if (!sea.Ok)
            {
                yield return LeaveSea(sea, c);
                c.Report();
                yield break;
            }
            var ship = rig.Boat;
            var heading = HeadingOffWind(sea.Wind.Dir, 90f);
            var top = new float[2];
            var t90 = new float[2];
            var turned = new float[2];
            for (var li = 0; li < 2; li++)
            {
                HelmSkill.TestLocalLevel = li * 100f;
                yield return Afloat(sea, heading);
                ship.m_speed = Ship.Speed.Slow;
                var trace = new Trace();
                yield return Record(ship, trace, li == 0 ? 26f : 18f);
                top[li] = trace.Mean(trace.Fwd, trace.Last - 2f, trace.Last);
                t90[li] = trace.TimeTo(0.9f * top[li]);
                var turn = new Trace();
                yield return Record(ship, turn, 3f, null, () => HoldRudder(ship));
                turned[li] = turn.Turned(0f, 3f);
                c.Check(ship.m_speed == Ship.Speed.Slow, $"Sailing {li * 100}: the ship did not stay at Slow (paddle)");
                c.Note($"paddling, Sailing {li * 100}: top {F(top[li])} m/s after {F(trace.Last)} s (90% at {F(t90[li])} s); rudder held 3 s: "
                       + $"turned {F(turned[li])} degrees");
            }
            if (c.Check(top[0] > 1f && top[1] > 1f && t90[0] > 0f && t90[1] > 0f, $"the ship did not get under way paddling ({F(top[0])} / {F(top[1])} m/s)"))
            {
                c.Check(Near(top[1], top[0], top[0] * 0.1f), $"paddling top speed {F(top[1])} m/s at Sailing 100 against {F(top[0])} at 0, expected about the same");
                c.Check(t90[1] <= t90[0] * 0.85f, $"paddling: 90% of top speed after {F(t90[1])} s at Sailing 100 against {F(t90[0])} s at 0, expected about a third sooner");
                c.Check(turned[0] > 2f && turned[1] >= turned[0] * 1.3f,
                    $"rudder held 3 s while paddling: turned {F(turned[1])} degrees at Sailing 100 against {F(turned[0])} at 0, expected clearly more");
            }
            var like = FieldsLikePrefab(ship, rig.PrefabShip, out var fields);
            c.Check(like, "ship fields not put back after the voyage: " + fields);
            yield return LeaveSea(sea, c);
            c.Note($"took {F(Time.realtimeSinceStartup - started)} s");
            CheckQuiet(c, watch);
            c.Report();
        }
        finally
        {
            sea.Wind.Restore();
            rig.Restore();
        }
    }

    // ---------- sailing.heel ----------

    private sealed class HeelRun
    {
        internal float Early;     // forward speed 2 s after the sails went up
        internal float Fwd;       // forward speed at the end
        internal float Leeway;    // mean sideways speed, 4 s to the end
        internal float Heel;      // mean heel, 4 s to the end, degrees
    }

    // Test list T16: at Sailing 100 the ship picks up speed sooner but drifts sideways and heels as at 0. Calm sea
    // (smallest waves the game has: wave height goes with the wind, the sail still pulls a quarter), wind from the side,
    // Full sail from standing still, three times: Sailing 0, Sailing 100, and Sailing 0 with the ship's whole sail force
    // x1.5 (m_sailForceFactor of this one ship, put back after): what a boost that is NOT only along the bow would
    // look like. The third run is the yardstick: the measure must see its extra heel and drift, and Sailing 100 must
    // sit nearer to the plain ship than to it. Same window for every run (heel and drift come from the sail's push,
    // full after 4 s; they do not wait for top speed).
    private static IEnumerator RunHeel()
    {
        var c = new Checks(HeelName);
        var rig = ShipRig.Create(HeelName);
        if (rig == null)
        {
            yield break;
        }
        var watch = LogMark();
        var sea = new Sea { Rig = rig };
        var started = Time.realtimeSinceStartup;
        try
        {
            var rules = new SailingRules();
            ServerRules.TestRules = rules;
            yield return SetUpSea(sea, c, 0f, 0.05f, 90f);
            if (!sea.Ok)
            {
                yield return LeaveSea(sea, c);
                c.Report();
                yield break;
            }
            var ship = rig.Boat;
            var across = HeadingOffWind(sea.Wind.Dir, 90f);
            var boost = SailMath.Scale(rules.AccelerationBonusAtMax, 1f);
            var sailFactor = ship.m_sailForceFactor;
            const float seconds = 11f;
            var names = new[] { "Sailing 0", "Sailing 100", $"Sailing 0 with the whole sail force x{F(boost)}" };
            var runs = new HeelRun[3];
            for (var i = 0; i < 3; i++)
            {
                var run = runs[i] = new HeelRun();
                HelmSkill.TestLocalLevel = i == 1 ? 100f : 0f;
                yield return Afloat(sea, across);
                ship.m_sailForceFactor = i == 2 ? sailFactor * boost : sailFactor;
                ship.m_speed = Ship.Speed.Full;
                var trace = new Trace();
                yield return Record(ship, trace, seconds);
                ship.m_speed = Ship.Speed.Stop;
                ship.m_sailForceFactor = sailFactor;
                run.Early = trace.Mean(trace.Fwd, 1.75f, 2.25f);
                run.Fwd = trace.Mean(trace.Fwd, trace.Last - 1f, trace.Last);
                run.Leeway = Mathf.Abs(trace.Mean(trace.Side, 4f, trace.Last));
                run.Heel = Mathf.Abs(trace.Mean(trace.Roll, 4f, trace.Last));
                c.Note($"{names[i]}: {F(run.Early)} m/s 2 s after the sails went up, {F(run.Fwd)} m/s after {F(trace.Last)} s; from 4 s on: "
                       + $"sideways {F(run.Leeway)} m/s, heel {F(run.Heel)} degrees");
            }
            var low = runs[0];
            var high = runs[1];
            var whole = runs[2];
            if (c.Check(low.Fwd > 0.8f && high.Fwd > 0.8f && whole.Fwd > 0.8f,
                    $"the ship did not get under way on the calm sea ({F(low.Fwd)} / {F(high.Fwd)} / {F(whole.Fwd)} m/s)"))
            {
                // Sooner under way.
                c.Check(high.Early > low.Early * 1.1f, $"2 s after the sails went up: {F(high.Early)} m/s at Sailing 100 against {F(low.Early)} at 0, expected clearly more");
                // The yardstick must show: a sail force x1.5 in every direction heels and pushes sideways clearly more.
                var heelSpread = whole.Heel - low.Heel;
                var leewaySpread = whole.Leeway - low.Leeway;
                var sees = c.Check(low.Heel > 0.5f && heelSpread > 0.3f && heelSpread > low.Heel * 0.1f,
                    $"heel measure not valid: {F(low.Heel)} degrees at Sailing 0, {F(whole.Heel)} with the whole sail force x{F(boost)} "
                    + "(expected clearly more: the test could not tell a sideways boost)");
                var drifts = c.Check(low.Leeway > 0.05f && leewaySpread > 0.02f && leewaySpread > low.Leeway * 0.1f,
                    $"sideways drift measure not valid: {F(low.Leeway)} m/s at Sailing 0, {F(whole.Leeway)} with the whole sail force "
                    + $"x{F(boost)} (expected clearly more: the test could not tell a sideways boost)");
                // No extra heel or drift: nearer to the plain ship than to the yardstick, either way.
                if (sees)
                {
                    c.Check(Mathf.Abs(high.Heel - low.Heel) <= heelSpread * 0.5f,
                        $"heel {F(high.Heel)} degrees at Sailing 100 against {F(low.Heel)} at 0 ({F(whole.Heel)} with the whole sail force "
                        + $"x{F(boost)}), expected about the same as at 0");
                }
                if (drifts)
                {
                    c.Check(Mathf.Abs(high.Leeway - low.Leeway) <= leewaySpread * 0.5f,
                        $"sideways drift {F(high.Leeway)} m/s at Sailing 100 against {F(low.Leeway)} at 0 ({F(whole.Leeway)} with the whole "
                        + $"sail force x{F(boost)}), expected about the same as at 0");
                }
            }
            var like = FieldsLikePrefab(ship, rig.PrefabShip, out var fields);
            c.Check(like, "ship fields not put back after the voyage: " + fields);
            yield return LeaveSea(sea, c);
            c.Note($"took {F(Time.realtimeSinceStartup - started)} s");
            CheckQuiet(c, watch);
            c.Report();
        }
        finally
        {
            sea.Wind.Restore();
            rig.Restore();
        }
    }

    // ---------- sailing.crew ----------

    private static IEnumerator RunCrew()
    {
        var c = new Checks(CrewName);
        var rig = ShipRig.Create(CrewName);
        if (rig == null)
        {
            yield break;
        }
        var watch = LogMark();
        var sea = new Sea { Rig = rig };
        var started = Time.realtimeSinceStartup;
        var crouched = false;
        try
        {
            var rules = new SailingRules();
            ServerRules.TestRules = rules;
            yield return SetUpSea(sea, c, 0f, 0.3f, 90f);
            if (!sea.Ok)
            {
                yield return LeaveSea(sea, c);
                c.Report();
                yield break;
            }
            var player = rig.Player;
            var ship = rig.Boat;
            var controls = ship.m_shipControlls;
            var map = Minimap.instance;
            c.Check(map != null && rig.Wear != null, "no minimap or the ship has no WearNTear: map reveal or ship damage not checked");
            var radius = map != null ? map.m_exploreRadius : 0f;
            var wide = radius * SailMath.Scale(rules.RevealBonusAtMax, 1f);
            var cut = SailMath.DamageFactor(rules.DamageReductionAtMax, 1f);
            var shipName = Utils.GetPrefabName(ship.gameObject);

            // ----- at the helm under sail: XP comes in (so the watch below is live) -----
            HelmSkill.TestLocalLevel = null;
            rig.BackupSkills();
            var sailing = SetSailing(player, 50f, 0f);
            yield return Afloat(sea, HeadingOffWind(sea.Wind.Dir, 90f));
            // At the helm at Stop on real water (the ship bobs, it does not travel): nothing.
            SailingXp.Reset();
            yield return new WaitForSeconds(3.4f);
            c.Check(ship.m_speed == Ship.Speed.Stop && sailing.m_accumulator == 0f,
                $"3 s at the helm of a ship at Stop on the water gave {F(sailing.m_accumulator)} Sailing XP");
            ship.m_speed = Ship.Speed.Full;
            yield return new WaitForSeconds(6.5f);
            c.Check(sailing.m_accumulator > 0f && ship.GetSpeed() > 1.2f,
                $"6.5 s under sail at the helm: {F(sailing.m_accumulator)} Sailing XP at {F(ship.GetSpeed())} m/s, expected some (check below not valid)");

            // ----- passenger: helm let go the vanilla way, the ship sails on (the game keeps the sail up) -----
            player.StopDoodadControl();
            var standing = new Box();
            yield return WaitFor(() => player.GetStandingOnShip() == ship, 3f, standing);
            if (c.Check(standing.Ok && controls.GetUser() == 0L && player.GetControlledShip() == null,
                    "the player who let go of the helm does not stand on the deck of the sailing ship"))
            {
                yield return new WaitForSeconds(1.1f);   // a sample that was due is through
                var held = sailing.m_accumulator;
                var from = ship.transform.position;
                yield return new WaitForSeconds(3.3f);
                var moved = Flat(ship.transform.position - from).magnitude;
                c.Check(moved > 3.6f && ship.IsPlayerInBoat(player),
                    $"passenger check not valid: the ship moved {F(moved)} m in 3.3 s, player still aboard {ship.IsPlayerInBoat(player)}");
                c.Check(sailing.m_accumulator == held, $"a passenger standing on the deck of a moving ship earned {F(sailing.m_accumulator - held)} Sailing XP");

                // Crouching there (Sneak Ambush's case): still nothing for Sailing.
                player.SetCrouch(true);
                crouched = true;
                from = ship.transform.position;
                yield return new WaitForSeconds(2.3f);
                moved = Flat(ship.transform.position - from).magnitude;
                c.Check(player.m_crouchToggled && moved > 2.5f, $"crouch check not valid: crouching {player.m_crouchToggled}, ship moved {F(moved)} m");
                c.Check(sailing.m_accumulator == held, $"a passenger crouching on the deck earned {F(sailing.m_accumulator - held)} Sailing XP");
                player.SetCrouch(false);
                crouched = false;

                // Map reveal on the deck at sea.
                if (map != null && c.Check(player.GetStandingOnShip() == ship, "the passenger no longer stands on the deck (map reveal not checked)"))
                {
                    HelmSkill.TestLocalLevel = 100f;
                    RevealRadius(player, out var deck, out var left);
                    HelmSkill.TestLocalLevel = 0f;
                    RevealRadius(player, out var deckLow, out _);
                    c.Check(Near(deck, wide, 0.01f) && Near(deckLow, radius, 0.01f) && left == radius,
                        $"standing on the deck at sea: reveal radius {F(deck)} at Sailing 100 and {F(deckLow)} at 0, expected {F(wide)} and {F(radius)}");
                }
            }
            rig.RestoreSkills();

            // ----- swimmer right next to the hull, inside the ship's volume -----
            var here = ship.transform.position;
            ResetBoat(ship, here, ship.transform.forward);
            HelmSkill.TestLocalLevel = 0f;
            var full = DamageTaken(rig, HitData.HitType.EnemyHit, null);
            HelmSkill.TestLocalLevel = 100f;
            var water = ZoneSystem.instance.m_waterLevel;
            var swimming = false;
            var side = Vector3.zero;
            foreach (var sign in new[] { 1f, -1f })
            {
                foreach (var distance in new[] { 1.5f, 1.9f, 2.3f, 2.7f, 3.1f, 3.6f, 4.2f })
                {
                    side = Flat(ship.transform.right).normalized * sign;
                    var p = ship.transform.position + side * distance;
                    var level = Floating.GetLiquidLevel(new Vector3(p.x, water - 0.5f, p.z));
                    p.y = (level > water - 5f ? level : water) - player.m_swimDepth;
                    MovePlayer(player, p);
                    var until = Time.time + 0.9f;
                    while (Time.time < until && !swimming)
                    {
                        yield return new WaitForFixedUpdate();
                        swimming = ship.IsPlayerInBoat(player) && player.IsSwimming() && player.GetStandingOnShip() == null && !player.IsAttached();
                    }
                    if (swimming)
                    {
                        break;
                    }
                }
                if (swimming)
                {
                    break;
                }
            }
            if (c.Check(swimming, "could not put a swimmer inside the ship's volume next to the hull (swimmer checks not done)"))
            {
                c.Check(!MinimapPatches.Aboard(player), "a swimmer next to the hull counts as aboard for the map");
                if (map != null)
                {
                    RevealRadius(player, out var swim, out _);
                    c.Check(Near(swim, radius, 0.01f), $"swimming next to the ship at Sailing 100: reveal radius {F(swim)}, expected the normal {F(radius)}");
                }
                // Ship damage counts everyone inside the ship's volume (vanilla's own "aboard"), a swimmer included.
                var mark = LogMark();
                var beside = DamageTaken(rig, HitData.HitType.EnemyHit, null);
                c.Check(Near(beside, full * cut, 0.01f) && LoggedExact(mark, LogLevel.Debug, DamageLine(shipName, HitData.HitType.EnemyHit, 10f, 10f * cut, 100f)),
                    $"Sailing-100 swimmer inside the ship's volume: the ship took {F(beside)}, expected {F(full * cut)} (half) and the Debug line");

                // Swims away: out of the volume, full damage, normal map.
                MovePlayer(player, player.transform.position + side * 25f);
                var gone = new Box();
                yield return WaitFor(() => !ship.IsPlayerInBoat(player), 3f, gone);
                if (c.Check(gone.Ok, "the swimmer 25 m away is still counted aboard"))
                {
                    var away = DamageTaken(rig, HitData.HitType.EnemyHit, null);
                    c.Check(Near(away, full, 0.01f), $"Sailing-100 player swam away: the ship took {F(away)}, expected the full {F(full)}");
                    if (map != null)
                    {
                        RevealRadius(player, out var far, out _);
                        c.Check(Near(far, radius, 0.01f), $"swimming 25 m from the ship at Sailing 100: reveal radius {F(far)}, expected {F(radius)}");
                    }
                }

                // Climbs aboard: the wider reveal.
                MovePlayer(player, controls.m_attachPoint.position + Vector3.up * 0.6f);
                var back = new Box();
                yield return WaitFor(() => player.GetStandingOnShip() == ship, 4f, back);
                if (map != null && c.Check(back.Ok, "the player put back on the deck does not stand on it"))
                {
                    RevealRadius(player, out var climbed, out _);
                    c.Check(Near(climbed, wide, 0.01f), $"back on the deck at Sailing 100: reveal radius {F(climbed)}, expected {F(wide)}");
                }
            }
            var diveProblems = Problems(watch, "Swim Dive");
            c.Check(diveProblems.Count == 0, "Swim Dive logged warnings or errors while the player swam next to the ship: " + Join(diveProblems));

            yield return LeaveSea(sea, c);
            c.Note($"took {F(Time.realtimeSinceStartup - started)} s");
            CheckQuiet(c, watch);
            c.Report();
        }
        finally
        {
            if (crouched)
            {
                rig.Player.SetCrouch(false);
            }
            sea.Wind.Restore();
            rig.Restore();
        }
    }
}
#endif
