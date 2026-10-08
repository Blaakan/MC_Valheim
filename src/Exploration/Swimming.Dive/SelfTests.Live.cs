#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SwimmingDiveMod;

// Debug build only. Shared tools of the live tests, and the live tests that need only deep open water:
//   dive.motion   Crouch down at swim speed (own speed, real speed, straight, stamina, log line), hold depth 10 s and
//                 with both keys 5 s, W A S D / look turn / auto-run do nothing deep, Jump up at the same speed and
//                 handed back with no jump, W still swims while not deep, short dip handed back (T01-T05)
//   dive.stamina  drain forward at the surface = vanilla formula, still at the surface free, still deep = same drain
//                 and never back, out of breath (message once, forced rise, drowning each second, no new dive), Swim
//                 skill only while moving (T05-T07, T09, T10)
//   dive.rules    own settings in force for a host, speed x2, idle rise 0.5, encumbered (T25, T26, T28, M07)
//   dive.multiplier  UnderwaterStaminaMultiplier 0 and 2, ground contact (faked) = stamina back, surface untouched by
//                 the multiplier (T27)
//   dive.waves    wind pinned at 0.3 (waves the hand-back handle): Crouch + Jump at the surface ride the waves with no
//                 drain (T32)
//   dive.bug.storm-waves  REAL BUG, alone: storm at its strongest wind, a fast wave rise over the swimmer who hold
//                 Crouch + Jump, the dive count him as deep, hold him under and drain him (T32, storm part)
//   dive.keys     the real keys (ZInput buttons pressed by code, PlayerController on): Crouch tap, Jump in open water,
//                 Jump with contact, Crouch held, Jump taps under water, gamepad buttons (T01, T02, T05, T11, T12, X03)
//   dive.menus    real Crouch key held under water with the inventory, the large map and the chat open: depth held, keys
//                 work again when closed (T30; own test: the chat needs the game window in front)
//   dive.moveset  Weapon Moveset's jump token (read by name): armed by a real jump at the surface, never by Jump under
//                 water (X01; own test: it reads another mod)
internal static partial class SelfTests
{
    private const string MotionName = "dive.motion";
    private const string StaminaName = "dive.stamina";
    private const string RulesName = "dive.rules";
    private const string KeysName = "dive.keys";
    private const string MenusName = "dive.menus";
    private const string MovesetName = "dive.moveset";
    private const string MultiplierName = "dive.multiplier";
    private const string WavesName = "dive.waves";
    private const string BugStormWavesName = "dive.bug.storm-waves";

    private const string StartedDefault =
        "Dive started (dive speed x1, no key under water: hold depth, stamina drain while diving x1).";
    private const string EndedSurface = "Dive ended: back at the surface.";
    private const string OutOfBreath = "Out of breath";

    private static readonly WaitForFixedUpdate FixedTick = new WaitForFixedUpdate();
    private static readonly WaitForEndOfFrame FrameEnd = new WaitForEndOfFrame();

    // ---------- shared tools ----------

    // Me listen to my own log lines while a test run (Debug lines too: BepInEx give every level to every listener), and
    // to error lines of any other logger that name my code.
    private sealed class LogTap : ILogListener
    {
        private const string OwnNamespace = "SwimmingDiveMod";

        private readonly object _gate = new object();
        private readonly List<KeyValuePair<LogLevel, string>> _lines = new List<KeyValuePair<LogLevel, string>>();
        private bool _on;

        internal LogTap()
        {
            _on = true;
            BepInEx.Logging.Logger.Listeners.Add(this);
        }

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            try
            {
                if (!_on || eventArgs == null || eventArgs.Source == null)
                {
                    return;
                }
                var text = eventArgs.Data as string ?? eventArgs.Data?.ToString();
                if (text == null)
                {
                    return;
                }
                // Not my logger: only an error whose text names my code (exception of mine that reached Unity or the
                // framework: the stack trace holds my namespace). Else "no error" would miss it.
                if (eventArgs.Source.SourceName != ModInfo.Name
                    && ((eventArgs.Level & (LogLevel.Error | LogLevel.Fatal)) == 0
                        || text.IndexOf(OwnNamespace, StringComparison.Ordinal) < 0))
                {
                    return;
                }
                lock (_gate)
                {
                    _lines.Add(new KeyValuePair<LogLevel, string>(eventArgs.Level, text));
                }
            }
            catch
            {
                // Me never throw inside the logger.
            }
        }

        // Lines holding this text.
        internal int Count(string part)
        {
            var n = 0;
            lock (_gate)
            {
                foreach (var line in _lines)
                {
                    if (line.Value.IndexOf(part, StringComparison.Ordinal) >= 0)
                    {
                        n++;
                    }
                }
            }
            return n;
        }

        // Warnings and errors that are not self-test result lines (nor the one a test expect).
        internal int Problems(string allowed, out string first)
        {
            var n = 0;
            first = "";
            lock (_gate)
            {
                foreach (var line in _lines)
                {
                    if ((line.Key & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) == 0
                        || line.Value.StartsWith(SelfTest.Prefix, StringComparison.Ordinal)
                        || (allowed != null && line.Value.IndexOf(allowed, StringComparison.Ordinal) >= 0))
                    {
                        continue;
                    }
                    if (n == 0)
                    {
                        var cut = line.Value.IndexOf('\n');
                        first = cut > 0 ? line.Value.Substring(0, cut).TrimEnd() : line.Value;
                    }
                    n++;
                }
            }
            return n;
        }

        internal void Clear()
        {
            lock (_gate)
            {
                _lines.Clear();
            }
        }

        internal void Stop()
        {
            _on = false;
            BepInEx.Logging.Logger.Listeners.Remove(this);
        }

        public void Dispose()
        {
            _on = false;
        }
    }

    // One live test: checks, rig, log tap.
    private sealed class Session
    {
        internal Checks C;
        internal DiveRig Rig;
        internal Player P;
        internal LogTap Tap;
    }

    private static Session Begin(string name)
    {
        var player = Player.m_localPlayer;
        if (player == null || ZoneSystem.instance == null || WorldGenerator.instance == null || GameCamera.instance == null
            || EnvMan.instance == null || ZNet.instance == null || ZNetScene.instance == null)
        {
            SelfTest.Fail(name, "no local player or no world");
            return null;
        }
        var s = new Session { C = new Checks(name), P = player, Rig = new DiveRig(player, name), Tap = new LogTap() };
        s.Rig.Undo("log tap", s.Tap.Stop);
        return s;
    }

    // First thing of every live test: a test before may still be sending the player home (a test cut short leaves that
    // to the game). The rig remembers the place the player stands at, so the player must have arrived.
    private static IEnumerator Settle()
    {
        var t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 40f)
        {
            var player = Player.m_localPlayer;
            if (player == null || ZNetScene.instance == null
                || (!player.IsTeleporting() && ZNetScene.instance.IsAreaReady(player.transform.position)))
            {
                break;
            }
            yield return null;
        }
    }

    // Default rules and all view settings on, in memory.
    private static void ApplyDefaults()
    {
        ServerRules.TestPending = false;
        ServerRules.TestRules = new DiveRules();
        Visuals.TestAllOn = true;
    }

    // Travel to a water spot, wait until the player swim and float at rest. ok.Ok false = test stop (check already
    // failed).
    private static IEnumerator EnterWater(Session s, Vector3 spot, bool takeControls, Box ok)
    {
        var player = s.P;
        var level = ZoneSystem.instance.m_waterLevel;
        var trip = new Box();
        s.Rig.Travelled = true;
        yield return s.Rig.Travel(new Vector3(spot.x, level + 1.5f, spot.z), Quaternion.identity, trip, TravelTimeout);
        if (!s.C.Check(trip.Ok, "travel to the water: " + trip.Detail))
        {
            yield break;
        }
        if (takeControls)
        {
            s.Rig.TakeControls();
        }
        var waitStart = Time.time;
        while (!player.IsSwimming() && Time.time - waitStart < 12f)
        {
            yield return new WaitForSeconds(0.2f);
        }
        if (!s.C.Check(player.IsSwimming() && player.InWater(),
                $"swimming after the trip (feet {F(player.transform.position.y)}, liquid {F(player.GetLiquidLevel())})"))
        {
            yield break;
        }
        yield return new WaitForSeconds(2.5f);
        s.C.Note($"in the water after {trip.Detail}: feet {F(player.transform.position.y)}, rest height {F(Rest(player))}, "
                 + $"ground {F(ZoneSystem.instance.GetGroundHeight(player.transform.position))}");
        ok.Ok = true;
    }

    private static IEnumerator GoHome(Session s)
    {
        ClearOverrides();
        var trip = new Box();
        yield return s.Rig.Travel(s.Rig.Origin, s.Rig.OriginRotation, trip, TravelTimeout);
        s.Rig.Back = trip.Ok;
        s.C.Check(trip.Ok, "travel back to the spawn: " + trip.Detail);
    }

    // No warning or error line of mine while the test ran.
    private static void CheckLog(Session s, string allowed = null)
    {
        var n = s.Tap.Problems(allowed, out var first);
        s.C.Check(n == 0, $"{n} warning or error line(s) from {ModInfo.Name} during the test, first: {first}");
    }

    // Vanilla rest height of the feet, and how far the feet are under it (m, down > 0).
    private static float Rest(Player p) => p.GetLiquidLevel() - p.m_swimDepth;

    private static float Under(Player p) => Rest(p) - p.transform.position.y;

    private static IEnumerator Ticks(int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return FixedTick;
        }
    }

    // Camera and fog of this frame are written (LateUpdate done), next SetEnv not yet.
    private static IEnumerator ViewSettled(int frames = 1)
    {
        for (var i = 0; i < frames; i++)
        {
            yield return null;
        }
        yield return FrameEnd;
    }

    // Vanilla swim drain per second right now (Player.OnSwimming: Swim skill, gear, status effects, world rates).
    private static float SwimDrain(Player p)
    {
        var rate = Mathf.Lerp(p.m_swimStaminaDrainMinSkill, p.m_swimStaminaDrainMaxSkill,
            p.GetSkills().GetSkillFactor(Skills.SkillType.Swim));
        rate += rate * p.GetEquipmentSwimStaminaModifier();
        p.m_seman.ModifySwimStaminaUsage(rate, ref rate);
        return rate * Game.m_moveStaminaRate * Game.m_staminaRate;
    }

    // Test Crouch held until the feet are this far under the rest height, then no key. Own speed carry the diver
    // about 0.8 m further before it hold.
    private static IEnumerator DiveTo(Player p, float under, float timeout, Box result)
    {
        result.Ok = false;
        DiveInput.TestUp = false;
        DiveInput.TestDown = true;
        var t0 = Time.time;
        while (Under(p) < under && Time.time - t0 < timeout)
        {
            yield return FixedTick;
        }
        DiveInput.TestDown = false;
        result.Ok = Under(p) >= under && DiveState.Diving && DiveState.Deep;
        result.Detail = $"{F(Under(p))} m under the rest height after {F(Time.time - t0)} s (diving {DiveState.Diving}, deep "
                        + $"{DiveState.Deep})";
    }

    // Test Jump held until the dive is over (back at the surface), then no key.
    private static IEnumerator Surface(Player p, float timeout, Box result)
    {
        result.Ok = false;
        DiveInput.TestDown = false;
        DiveInput.TestUp = true;
        var t0 = Time.time;
        while (DiveState.Diving && Time.time - t0 < timeout)
        {
            yield return FixedTick;
        }
        DiveInput.TestUp = false;
        result.Ok = !DiveState.Diving;
        result.Detail = $"{F(Time.time - t0)} s, feet {F(Under(p))} m under the rest height";
    }

    private static bool Near(Color a, Color b, float tolerance) =>
        Near(a.r, b.r, tolerance) && Near(a.g, b.g, tolerance) && Near(a.b, b.b, tolerance);

    // Another MC mod's static member, read by name (no compile-time link: the other mod may be missing).
    private static bool TryReadStatic(string pluginGuid, string typeName, string member, out object value)
    {
        value = null;
        try
        {
            if (!Chainloader.PluginInfos.TryGetValue(pluginGuid, out var info) || info == null || info.Instance == null)
            {
                return false;
            }
            var type = info.Instance.GetType().Assembly.GetType(typeName);
            if (type == null)
            {
                return false;
            }
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var property = type.GetProperty(member, flags);
            if (property != null)
            {
                value = property.GetValue(null, null);
                return true;
            }
            var field = type.GetField(member, flags);
            if (field != null)
            {
                value = field.GetValue(null);
                return true;
            }
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Nearest water whose floor is between min and max m under the water level (WorldGenerator heights, no zone
    // needed), the 4 points `around` m away within `slack` m of that range; not Ashlands (hot water) nor Deep North.
    private static bool FindWater(Vector3 origin, float min, float max, float around, float slack, out Vector3 spot,
        out float depth)
    {
        var gen = WorldGenerator.instance;
        var level = ZoneSystem.instance.m_waterLevel;
        for (var r = 40f; r <= 5000f; r += 20f)
        {
            var step = r < 800f ? 4 : 8;
            for (var a = 0; a < 360; a += step)
            {
                var x = origin.x + Mathf.Sin(a * Mathf.Deg2Rad) * r;
                var z = origin.z + Mathf.Cos(a * Mathf.Deg2Rad) * r;
                if (x * x + z * z > 9500f * 9500f)
                {
                    continue;
                }
                var d = level - gen.GetHeight(x, z);
                if (d < min || d > max)
                {
                    continue;
                }
                var biome = gen.GetBiome(x, z);
                if (biome == Heightmap.Biome.AshLands || biome == Heightmap.Biome.DeepNorth)
                {
                    continue;
                }
                if (!Between(level - gen.GetHeight(x + around, z), min - slack, max + slack)
                    || !Between(level - gen.GetHeight(x - around, z), min - slack, max + slack)
                    || !Between(level - gen.GetHeight(x, z + around), min - slack, max + slack)
                    || !Between(level - gen.GetHeight(x, z - around), min - slack, max + slack))
                {
                    continue;
                }
                spot = new Vector3(x, level, z);
                depth = d;
                return true;
            }
        }
        spot = Vector3.zero;
        depth = 0f;
        return false;
    }

    private static bool Between(float value, float min, float max) => value >= min && value <= max;

    // Deep open water for a live test, or the failed check that say why there is none.
    private static bool DeepSpot(Session s, out Vector3 spot)
    {
        if (FindDeepWater(s.Rig.Origin, out spot, out var depth))
        {
            s.C.Note($"deep water at {F(spot)}, {F(Vector3.Distance(Flat(spot), Flat(s.Rig.Origin)))} m from the start, about "
                     + $"{F(depth)} m deep (WorldGenerator)");
            return true;
        }
        s.C.Check(false, $"no water {F(MinWaterDepth)} m deep within 5 km of the start: not tested");
        return false;
    }

    // A game button by its ZInput name (what the keyboard, mouse or gamepad binding press).
    private static ZInput.ButtonDef Button(string name) => ZInput.instance != null ? ZInput.instance.GetButtonDef(name) : null;

    // ---------- dive.motion ----------

    private static IEnumerator RunMotion()
    {
        yield return Settle();
        var s = Begin(MotionName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        var tap = s.Tap;
        try
        {
            ApplyDefaults();
            CalmWind(rig);
            if (!DeepSpot(s, out var spot))
            {
                c.Report();
                yield break;
            }
            var ok = new Box();
            yield return EnterWater(s, spot, true, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            var dt = Time.fixedDeltaTime;
            var swim = DiveController.SwimSpeed(player);
            var box = new Box();

            // ---- at the surface the movement keys work as in the normal game (T05; control for the deep checks) ----
            var from = Flat(player.transform.position);
            for (var i = 0; i < 75; i++)
            {
                rig.Drive(Vector3.forward);
                yield return FixedTick;
            }
            var swam = Vector3.Distance(from, Flat(player.transform.position));
            c.Check(swam > 1f && !DiveState.Diving, $"at the surface W swims forward ({F(swam)} m in 1.5 s)");
            var yaw = player.transform.eulerAngles.y;
            player.SetLookDir(Quaternion.Euler(0f, yaw + 90f, 0f) * Vector3.forward);
            for (var i = 0; i < 40; i++)
            {
                rig.Drive(Vector3.forward);
                yield return FixedTick;
            }
            var turned = Mathf.Abs(Mathf.DeltaAngle(yaw, player.transform.eulerAngles.y));
            c.Check(turned > 15f, $"at the surface W with the view turned a quarter turns the character ({F(turned)} degrees)");
            rig.Drive(Vector3.zero, false, false, true);
            yield return Ticks(3);
            var autoRunSurface = player.m_autoRun;
            // Any movement key end auto-run (vanilla).
            rig.Drive(Vector3.back);
            yield return FixedTick;
            rig.Drive(Vector3.zero);
            c.Check(autoRunSurface && !player.m_autoRun, "at the surface the auto-run key starts auto-run, a movement key ends it");
            yield return new WaitForSeconds(1.5f);

            // ---- Crouch = straight down at the swimming speed, stamina goes down (T01, G1) ----
            rig.TakeStaminaRate();
            yield return new WaitForSeconds(0.4f);
            rig.Refill();
            var drain = SwimDrain(player);
            tap.Clear();
            var top = Flat(player.transform.position);
            var s0 = player.GetStamina();
            var maxSide = 0f;
            DiveInput.TestUp = false;
            DiveInput.TestDown = true;
            for (var i = 0; i < 125; i++)
            {
                yield return FixedTick;
                maxSide = Mathf.Max(maxSide, Vector3.Distance(top, Flat(player.transform.position)));
            }
            var vyDown = DiveState.Vy;
            c.Check(DiveState.Diving && DiveState.Deep, $"Crouch dives (diving {DiveState.Diving}, deep {DiveState.Deep})");
            c.Check(Mathf.Abs(vyDown + swim) <= 0.06f * swim,
                $"Crouch held 2.5 s: own speed {F(vyDown)} m/s, swimming speed {F(swim)}");
            var y0 = player.transform.position.y;
            for (var i = 0; i < 25; i++)
            {
                yield return FixedTick;
                maxSide = Mathf.Max(maxSide, Vector3.Distance(top, Flat(player.transform.position)));
            }
            var realDown = (y0 - player.transform.position.y) / (25 * dt);
            c.Check(Mathf.Abs(realDown - swim) <= 0.12f * swim,
                $"going down at about the swimming speed: {F(realDown)} m/s measured (swimming speed {F(swim)})");
            c.Check(maxSide < 0.4f, $"straight down: at most {F(maxSide)} m sideways in 3 s");
            var used = s0 - player.GetStamina();
            c.Check(used > 0.7f * drain * 3f && used < 1.1f * drain * 3f,
                $"stamina goes down while descending: {F(used)} in 3 s (swim drain {F(drain)} per second)");
            c.Check(tap.Count(StartedDefault) == 1, $"Debug log line \"{StartedDefault}\" written once ({tap.Count(StartedDefault)})");
            DiveInput.TestDown = false;
            rig.GiveStaminaRateBack();
            rig.Refill();

            // ---- no key 10 s, then Crouch + Jump 5 s: same depth (T03, G2) ----
            yield return new WaitForSeconds(1f);
            var low = player.transform.position.y;
            var high = low;
            var deepAll = true;
            for (var i = 0; i < 450; i++)
            {
                yield return FixedTick;
                low = Mathf.Min(low, player.transform.position.y);
                high = Mathf.Max(high, player.transform.position.y);
                deepAll &= DiveState.Diving && DiveState.Deep;
            }
            c.Check(deepAll && high - low < 0.3f,
                $"no key for 10 s under water: same depth (moved {F(high - low)} m after the first second, deep all the time {deepAll})");
            DiveInput.TestDown = true;
            DiveInput.TestUp = true;
            low = player.transform.position.y;
            high = low;
            deepAll = true;
            var handed = false;
            for (var i = 0; i < 250; i++)
            {
                yield return FixedTick;
                low = Mathf.Min(low, player.transform.position.y);
                high = Mathf.Max(high, player.transform.position.y);
                deepAll &= DiveState.Diving && DiveState.Deep;
                handed |= DiveState.HandedBack;
            }
            DiveInput.TestDown = false;
            DiveInput.TestUp = false;
            c.Check(deepAll && !handed && high - low < 0.3f,
                $"Crouch and Jump together for 5 s under water: same depth (moved {F(high - low)} m, deep all the time {deepAll}, "
                + $"left to the normal game {handed})");

            // ---- W A S D, look turn, auto-run under water: nothing (T04, G2) ----
            var before = Flat(player.transform.position);
            var blocked = false;
            var kept = 0;
            foreach (var dir in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            {
                for (var i = 0; i < 40; i++)
                {
                    rig.Drive(dir);
                    yield return FixedTick;
                    blocked |= DiveState.BlockedAbove;
                    if (player.m_moveDir.sqrMagnitude > 0.01f)
                    {
                        kept++;
                    }
                }
            }
            rig.Drive(Vector3.zero);
            var side = Vector3.Distance(before, Flat(player.transform.position));
            c.Check(!blocked, "open water: nothing solid above the diver");
            c.Check(side < 0.4f && kept == 0,
                $"W, S, A, D under water: no sideways move ({F(side)} m in 3.2 s, movement kept on {kept} ticks)");
            var yawDeep = player.transform.eulerAngles.y;
            player.SetLookDir(Quaternion.Euler(0f, yawDeep + 90f, 0f) * Vector3.forward);
            for (var i = 0; i < 50; i++)
            {
                rig.Drive(Vector3.forward);
                yield return FixedTick;
            }
            rig.Drive(Vector3.zero);
            var turnedDeep = Mathf.Abs(Mathf.DeltaAngle(yawDeep, player.transform.eulerAngles.y));
            c.Check(turnedDeep < 2f, $"view turned a quarter and W under water: the character does not turn ({F(turnedDeep)} degrees)");
            rig.Drive(Vector3.zero, false, false, true);
            yield return Ticks(2);
            var autoRunDeep = player.m_autoRun;
            for (var i = 0; i < 50; i++)
            {
                rig.Drive(Vector3.zero);
                yield return FixedTick;
            }
            side = Vector3.Distance(before, Flat(player.transform.position));
            c.Check(!autoRunDeep && !player.m_autoRun && side < 0.5f,
                $"auto-run key under water: no auto-run (on {autoRunDeep}), no move ({F(side)} m from the start of the key checks)");
            var yKeys = player.transform.position.y;
            DiveInput.TestDown = true;
            yield return Ticks(50);
            var wentDown = yKeys - player.transform.position.y;
            DiveInput.TestDown = false;

            // ---- Jump = up at the same speed, handed back at the surface, no jump (T02, G2) ----
            DiveInput.TestUp = true;
            yield return Ticks(100);
            var yRise = player.transform.position.y;
            yield return Ticks(25);
            var vyUp = DiveState.Vy;
            var realUp = (player.transform.position.y - yRise) / (25 * dt);
            c.Check(wentDown > 0.5f && vyUp > 0f, $"Crouch and Jump still move the diver ({F(wentDown)} m down in 1 s, then up)");
            c.Check(DiveState.Diving && Mathf.Abs(vyUp - swim) <= 0.06f * swim && Mathf.Abs(vyUp + vyDown) <= 0.1f * swim,
                $"Jump held 2.5 s: own speed {F(vyUp)} m/s up, as fast as going down ({F(vyDown)})");
            // Own speed alone prove nothing when something hold the body: the height itself must rise that fast.
            c.Check(DiveState.Diving && Mathf.Abs(realUp - swim) <= 0.12f * swim,
                $"rising at about the swimming speed: {F(realUp)} m/s measured (swimming speed {F(swim)}, going down {F(realDown)})");
            var jumpTimer = player.m_jumpTimer;
            var jumped = false;
            var t0 = Time.time;
            while (DiveState.Diving && Time.time - t0 < 8f)
            {
                yield return FixedTick;
                jumped |= player.m_jumpTimer < jumpTimer;
                jumpTimer = player.m_jumpTimer;
            }
            DiveInput.TestUp = false;
            var above = float.NegativeInfinity;
            for (var i = 0; i < 100; i++)
            {
                yield return FixedTick;
                above = Mathf.Max(above, -Under(player));
                jumped |= player.m_jumpTimer < jumpTimer;
                jumpTimer = player.m_jumpTimer;
            }
            c.Check(!DiveState.Diving && player.m_body.useGravity && above < 0.35f && !jumped,
                $"at the surface: handed back to normal swimming, no jump out of the water (feet at most {F(above)} m above the "
                + $"rest height, jump {jumped}, gravity {player.m_body.useGravity})");
            c.Check(Mathf.Abs(Under(player)) < 0.3f, $"floating at the usual height ({F(Under(player))} m under the rest height)");
            c.Check(tap.Count(EndedSurface) == 1, $"Debug log line \"{EndedSurface}\" written once ({tap.Count(EndedSurface)})");

            // ---- going down but head still out (not deep): W still swims (T04) ----
            yield return new WaitForSeconds(1f);
            var a0 = Flat(player.transform.position);
            var shallowTicks = 0;
            var keptShallow = 0;
            DiveInput.TestDown = true;
            for (var i = 0; i < 60 && !DiveState.Deep; i++)
            {
                rig.Drive(Vector3.forward);
                yield return FixedTick;
                if (DiveState.Diving && !DiveState.Deep)
                {
                    shallowTicks++;
                    if (player.m_moveDir.sqrMagnitude > 0.25f)
                    {
                        keptShallow++;
                    }
                }
            }
            var shallowMove = Vector3.Distance(a0, Flat(player.transform.position));
            DiveInput.TestDown = false;
            rig.Drive(Vector3.zero);
            c.Check(shallowTicks >= 8 && keptShallow == shallowTicks && shallowMove > 0.1f,
                $"going down just below the surface: W still swims forward ({F(shallowMove)} m in {shallowTicks} ticks before "
                + $"the diver was deep, movement kept on {keptShallow})");
            yield return Surface(player, 6f, box);

            // ---- Crouch let go before the head is under: the normal game lifts the swimmer back (T01) ----
            yield return new WaitForSeconds(1.5f);
            var ended = tap.Count(EndedSurface);
            DiveInput.TestDown = true;
            yield return Ticks(12);
            var dipDiving = DiveState.Diving;
            var dipDeep = DiveState.Deep;
            DiveInput.TestDown = false;
            yield return Ticks(3);
            var dipOver = !DiveState.Diving;
            yield return Ticks(100);
            c.Check(dipDiving && !dipDeep && dipOver && Mathf.Abs(Under(player)) < 0.3f && player.m_body.useGravity
                    && tap.Count(EndedSurface) == ended + 1,
                $"Crouch let go just below the surface: the normal game lifts the swimmer back (diving {dipDiving}, deep {dipDeep}, "
                + $"over {dipOver}, {F(Under(player))} m under the rest height 2 s later, \"{EndedSurface}\" lines "
                + $"{tap.Count(EndedSurface) - ended})");

            yield return GoHome(s);
            CheckLog(s);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- dive.stamina ----------

    // "Out of breath" messages asked from the HUD while the counter patch is on (Harmony prefix of MessageHud.ShowMessage).
    private static int _outOfBreathShown;

    private static void CountMessage(string text)
    {
        if (text == OutOfBreath)
        {
            _outOfBreathShown++;
        }
    }

    private static IEnumerator RunStamina()
    {
        yield return Settle();
        var s = Begin(StaminaName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        try
        {
            ApplyDefaults();
            CalmWind(rig);
            if (!DeepSpot(s, out var spot))
            {
                c.Report();
                yield break;
            }
            var ok = new Box();
            yield return EnterWater(s, spot, true, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            var dt = Time.fixedDeltaTime;
            var box = new Box();
            // Swim skill grow while the test swim: put back at the end. Health too (drowning part).
            var swimSkill = rig.SaveSkill(Skills.SkillType.Swim);
            rig.SaveHealth();
            rig.TakeStaminaRate();
            yield return new WaitForSeconds(0.4f);
            var drain = SwimDrain(player);
            c.Note($"vanilla swim drain {F(drain)} per second (Swim factor {F(player.GetSkills().GetSkillFactor(Skills.SkillType.Swim))}, "
                   + $"stamina rate {F(Game.m_staminaRate)}, move stamina rate {F(Game.m_moveStaminaRate)}), max stamina "
                   + $"{F(player.GetMaxStamina())}");
            if (!c.Check(drain > 0.5f, "the swim drain is not zero once the probe's StaminaRate 0 is taken off"))
            {
                c.Report();
                yield break;
            }

            // ---- swimming forward at the surface drains (T05, reference of T06) ----
            rig.Refill();
            for (var i = 0; i < 15; i++)
            {
                rig.Drive(Vector3.forward);
                yield return FixedTick;
            }
            var a = player.GetStamina();
            for (var i = 0; i < 100; i++)
            {
                rig.Drive(Vector3.forward);
                yield return FixedTick;
            }
            var surfaceRate = (a - player.GetStamina()) / (100 * dt);
            rig.Drive(Vector3.zero);
            c.Check(Mathf.Abs(surfaceRate - drain) <= 0.12f * drain && !DiveState.Diving,
                $"swimming forward at the surface drains {F(surfaceRate)} per second (vanilla formula {F(drain)})");

            // ---- floating still at the surface is free (T05, T07, G4) ----
            yield return new WaitForSeconds(1.5f);
            a = player.GetStamina();
            yield return Ticks(125);
            c.Check(Mathf.Abs(a - player.GetStamina()) < 0.2f && !DiveState.Diving,
                $"floating still at the surface for 2.5 s: stamina stays ({F(a)} -> {F(player.GetStamina())})");

            // ---- still under water = the forward-swimming drain, and it never comes back (T06, G4) ----
            rig.Refill();
            yield return DiveTo(player, 3.5f, 6f, box);
            c.Check(box.Ok, "dive to 3.5 m: " + box.Detail);
            yield return Ticks(65);
            rig.Refill();
            a = player.GetStamina();
            yield return Ticks(100);
            var deepRate = (a - player.GetStamina()) / (100 * dt);
            c.Check(DiveState.Deep && Mathf.Abs(DiveState.Vy) < 0.1f && Mathf.Abs(deepRate - surfaceRate) <= 0.12f * surfaceRate
                    && Mathf.Abs(deepRate - drain) <= 0.12f * drain,
                $"holding still under water drains {F(deepRate)} per second, as swimming forward at the surface "
                + $"({F(surfaceRate)}; vanilla formula {F(drain)})");
            rig.Refill();
            var rose = false;
            var prev = player.GetStamina();
            a = prev;
            for (var i = 0; i < 125; i++)
            {
                yield return FixedTick;
                var now = player.GetStamina();
                rose |= now > prev + 0.0001f;
                prev = now;
            }
            c.Check(!rose && a - prev > 1f, $"stamina never comes back while staying down ({F(a)} -> {F(prev)} in 2.5 s, rose {rose})");

            // ---- out of breath (T09) ----
            rig.Refill();
            yield return DiveTo(player, 4.5f, 6f, box);
            c.Check(box.Ok, "dive to 4.5 m: " + box.Detail);
            yield return Ticks(65);
            player.SetHealth(player.GetMaxHealth());
            var counter = new Harmony(ModInfo.Guid + ".selftest");
            rig.Undo("message counter", () => counter.UnpatchSelf());
            _outOfBreathShown = 0;
            counter.Patch(AccessTools.Method(typeof(MessageHud), nameof(MessageHud.ShowMessage)),
                prefix: new HarmonyMethod(typeof(SelfTests), nameof(CountMessage)));
            var startY = player.transform.position.y;
            var health0 = player.GetHealth();
            var lastHit = player.m_lastHit;
            var hitTicks = new List<int>();
            var seenText = false;
            var rising = false;
            var ticks = 0;
            // Crouch stays held the whole time.
            DiveInput.TestDown = true;
            player.m_stamina = 0f;
            while (ticks < 400)
            {
                yield return FixedTick;
                ticks++;
                if (!ReferenceEquals(player.m_lastHit, lastHit))
                {
                    lastHit = player.m_lastHit;
                    if (lastHit != null && lastHit.m_hitType == HitData.HitType.Drowning)
                    {
                        hitTicks.Add(ticks);
                    }
                }
                if (ticks == 50)
                {
                    rising = DiveState.Diving && DiveState.Vy > 0.5f && player.transform.position.y > startY + 0.4f;
                }
                var hud = MessageHud.instance;
                seenText |= hud != null && hud.m_messageText != null && hud.m_messageText.text == OutOfBreath;
                if (!DiveState.Diving && ticks > 50)
                {
                    break;
                }
            }
            var surfaced = !DiveState.Diving && Under(player) < 0.6f;
            var riseTicks = ticks;
            var again = false;
            for (var i = 0; i < 75; i++)
            {
                yield return FixedTick;
                ticks++;
                again |= DiveState.Diving;
                if (!ReferenceEquals(player.m_lastHit, lastHit))
                {
                    lastHit = player.m_lastHit;
                    if (lastHit != null && lastHit.m_hitType == HitData.HitType.Drowning)
                    {
                        hitTicks.Add(ticks);
                    }
                }
                var hud = MessageHud.instance;
                seenText |= hud != null && hud.m_messageText != null && hud.m_messageText.text == OutOfBreath;
            }
            c.Check(rising && surfaced,
                $"no stamina under water: the diver floats up even while Crouch is held (rising after 1 s {rising}, at the "
                + $"surface after {F(riseTicks * dt)} s {surfaced})");
            c.Check(_outOfBreathShown == 1 && seenText,
                $"\"{OutOfBreath}\" shown once at the top left (asked {_outOfBreathShown} time(s), text on screen {seenText})");
            var gap = hitTicks.Count >= 2 ? (hitTicks[1] - hitTicks[0]) * dt : 0f;
            c.Check(hitTicks.Count >= 2 && gap > 0.9f && gap < 1.2f && player.GetHealth() < health0 - 0.5f,
                $"drowning damage every second with no stamina ({hitTicks.Count} drowning hits in {F(ticks * dt)} s, "
                + $"{F(gap)} s apart, health {F(health0)} -> {F(player.GetHealth())})");
            c.Check(!again && _outOfBreathShown == 1,
                "at the surface with no stamina, Crouch still held: no new dive, no second message");
            rig.Refill();
            yield return Ticks(3);
            c.Check(DiveState.Diving, "stamina back, Crouch still held: a new dive starts");
            DiveInput.TestDown = false;
            counter.UnpatchSelf();
            player.SetHealth(player.GetMaxHealth());
            yield return Surface(player, 6f, box);

            // ---- Swim skill: up while diving down and up, not while holding still (T10) ----
            rig.GiveStaminaRateBack();
            yield return new WaitForSeconds(0.4f);
            rig.Refill();
            swimSkill.m_level = 0f;
            swimSkill.m_accumulator = 0f;
            yield return DiveTo(player, 2.5f, 5f, box);
            for (var cycle = 0; cycle < 4; cycle++)
            {
                DiveInput.TestUp = false;
                DiveInput.TestDown = true;
                yield return Ticks(50);
                DiveInput.TestDown = false;
                DiveInput.TestUp = true;
                yield return Ticks(50);
            }
            DiveInput.TestUp = false;
            c.Check(swimSkill.m_level > 0f || swimSkill.m_accumulator > 0f,
                $"8 s of diving down and up from Swim 0: Swim rises (level {F(swimSkill.m_level)}, progress "
                + $"{F(swimSkill.m_accumulator)})");
            yield return Ticks(70);
            var level = swimSkill.m_level;
            var progress = swimSkill.m_accumulator;
            var stillDeep = DiveState.Deep && Mathf.Abs(DiveState.Vy) < 0.1f;
            yield return Ticks(225);
            c.Check(stillDeep && DiveState.Deep && swimSkill.m_level == level && swimSkill.m_accumulator == progress,
                $"4.5 s holding still under water: Swim does not rise (level {F(level)} -> {F(swimSkill.m_level)}, progress "
                + $"{F(progress)} -> {F(swimSkill.m_accumulator)}, deep and still {stillDeep})");
            yield return Surface(player, 8f, box);

            yield return GoHome(s);
            CheckLog(s);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- dive.rules ----------

    // Weather with the strongest wind the game has (console "env <name>"): ThunderStorm when it is there.
    private static string StormEnv()
    {
        var env = EnvMan.instance;
        if (env == null || env.m_environments == null)
        {
            return null;
        }
        EnvSetup best = null;
        foreach (var e in env.m_environments)
        {
            if (e == null || string.IsNullOrEmpty(e.m_name))
            {
                continue;
            }
            if (e.m_name == "ThunderStorm")
            {
                return e.m_name;
            }
            if (best == null || e.m_windMax > best.m_windMax || (e.m_windMax == best.m_windMax && e.m_windMin > best.m_windMin))
            {
                best = e;
            }
        }
        return best != null ? best.m_name : null;
    }

    private static IEnumerator RunRules()
    {
        yield return Settle();
        var s = Begin(RulesName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        var tap = s.Tap;
        try
        {
            // ---- single player (a host with nobody in): own settings in force (M07) ----
            ClearOverrides();
            var own = DiveRules.Own();
            c.Check(!ServerRules.UsingServer && !ServerRules.IsPending && ServerRules.Current.Describe() == own.Describe(),
                $"single player / host: the game's own settings are in force ({ServerRules.Current.Describe()})");

            ApplyDefaults();
            CalmWind(rig);
            if (!DeepSpot(s, out var spot))
            {
                c.Report();
                yield break;
            }
            var ok = new Box();
            yield return EnterWater(s, spot, true, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            var dt = Time.fixedDeltaTime;
            var swim = DiveController.SwimSpeed(player);
            var box = new Box();

            // ---- DiveSpeedMultiplier 2: twice as fast both ways (T26) ----
            ServerRules.TestRules = new DiveRules { DiveSpeedMultiplier = 2f };
            tap.Clear();
            DiveInput.TestUp = false;
            DiveInput.TestDown = true;
            yield return Ticks(80);
            var vy = DiveState.Vy;
            var y0 = player.transform.position.y;
            yield return Ticks(15);
            var real = (y0 - player.transform.position.y) / (15 * dt);
            DiveInput.TestDown = false;
            c.Check(DiveState.Deep && Mathf.Abs(vy + 2f * swim) <= 0.06f * 2f * swim && Mathf.Abs(real - 2f * swim) <= 0.12f * 2f * swim,
                $"dive speed x2: Crouch goes down at {F(real)} m/s (own speed {F(vy)}), twice the swimming speed {F(swim)}");
            c.Check(tap.Count("Dive started (dive speed x2,") == 1, "Debug log line \"Dive started (dive speed x2, ...\" written once");
            DiveInput.TestUp = true;
            yield return Ticks(80);
            vy = DiveState.Vy;
            y0 = player.transform.position.y;
            yield return Ticks(12);
            var realUp = (player.transform.position.y - y0) / (12 * dt);
            c.Check(DiveState.Diving && Mathf.Abs(vy - 2f * swim) <= 0.06f * 2f * swim && Mathf.Abs(realUp - 2f * swim) <= 0.12f * 2f * swim,
                $"dive speed x2: Jump goes up at {F(realUp)} m/s (own speed {F(vy)}), twice the swimming speed {F(swim)}");
            yield return Surface(player, 6f, box);
            c.Check(box.Ok, "dive speed x2: back at the surface: " + box.Detail);
            yield return new WaitForSeconds(1.5f);

            // ---- IdleRiseSpeed 0.5: no key = up half a metre per second, handed back at the surface (T25) ----
            ServerRules.TestRules = new DiveRules { IdleRiseSpeed = 0.5f };
            yield return DiveTo(player, 1.6f, 5f, box);
            c.Check(box.Ok, "idle rise 0.5: dive to 1.6 m: " + box.Detail);
            yield return Ticks(75);
            y0 = player.transform.position.y;
            yield return Ticks(75);
            vy = DiveState.Vy;
            var rise = player.transform.position.y - y0;
            c.Check(DiveState.Diving && vy > 0.44f && vy < 0.56f && rise > 0.6f && rise < 0.9f,
                $"idle rise 0.5: with no key the diver floats up {F(rise)} m in 1.5 s (own speed {F(vy)} m/s)");
            var t0 = Time.time;
            while (DiveState.Diving && Time.time - t0 < 12f)
            {
                yield return FixedTick;
            }
            yield return Ticks(75);
            c.Check(!DiveState.Diving && player.m_body.useGravity && Mathf.Abs(Under(player)) < 0.3f,
                $"idle rise 0.5: handed back to normal swimming at the surface ({F(Time.time - t0)} s later, "
                + $"{F(Under(player))} m under the rest height)");
            ServerRules.TestRules = new DiveRules();

            // ---- encumbered players dive too (T28) ----
            var carry = player.m_maxCarryWeight;
            rig.Undo("carry weight", () => player.m_maxCarryWeight = carry);
            // No raven with the "encumbered" hint for the throwaway player.
            if (!player.m_shownTutorials.Contains("encumbered"))
            {
                player.m_shownTutorials.Add("encumbered");
                rig.Undo("encumbered hint", () => player.m_shownTutorials.Remove("encumbered"));
            }
            player.m_maxCarryWeight = -1000f;
            yield return Ticks(5);
            var encumbered = player.IsEncumbered();
            y0 = player.transform.position.y;
            DiveInput.TestDown = true;
            yield return Ticks(110);
            vy = DiveState.Vy;
            var fell = y0 - player.transform.position.y;
            var stillEncumbered = player.IsEncumbered();
            // Swimming speed as the game gives it to an encumbered swimmer right now.
            var swimLoaded = DiveController.SwimSpeed(player);
            DiveInput.TestDown = false;
            c.Check(encumbered && stillEncumbered && DiveState.Diving && DiveState.Deep && swimLoaded > 0.5f && fell > swimLoaded
                    && Mathf.Abs(vy + swimLoaded) <= 0.08f * swimLoaded,
                $"encumbered (carry limit below the load): Crouch dives {F(fell)} m in 2.2 s at {F(vy)} m/s (swimming speed "
                + $"while encumbered {F(swimLoaded)}, not encumbered {F(swim)})");
            yield return Surface(player, 8f, box);
            c.Check(box.Ok, "encumbered: Jump rises back to the surface: " + box.Detail);
            player.m_maxCarryWeight = carry;
            yield return new WaitForSeconds(1f);

            yield return GoHome(s);
            CheckLog(s);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- dive.multiplier ----------

    private static IEnumerator RunMultiplier()
    {
        yield return Settle();
        var s = Begin(MultiplierName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        try
        {
            ApplyDefaults();
            CalmWind(rig);
            if (!DeepSpot(s, out var spot))
            {
                c.Report();
                yield break;
            }
            var ok = new Box();
            yield return EnterWater(s, spot, true, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            var dt = Time.fixedDeltaTime;
            var box = new Box();
            rig.SaveSkill(Skills.SkillType.Swim);
            rig.SaveHealth();
            rig.TakeStaminaRate();
            yield return new WaitForSeconds(0.4f);
            var drain = SwimDrain(player);
            if (!c.Check(drain > 0.5f, $"the swim drain is not zero once the probe's StaminaRate 0 is taken off ({F(drain)} per second)"))
            {
                c.Report();
                yield break;
            }

            // ---- reference with multiplier 1: forward at the surface, still under water ----
            rig.Refill();
            for (var i = 0; i < 15; i++)
            {
                rig.Drive(Vector3.forward);
                yield return FixedTick;
            }
            var a = player.GetStamina();
            for (var i = 0; i < 75; i++)
            {
                rig.Drive(Vector3.forward);
                yield return FixedTick;
            }
            var surfaceRate = (a - player.GetStamina()) / (75 * dt);
            rig.Drive(Vector3.zero);
            yield return new WaitForSeconds(1.5f);
            rig.Refill();
            yield return DiveTo(player, 3.5f, 6f, box);
            c.Check(box.Ok, "dive to 3.5 m: " + box.Detail);
            yield return Ticks(65);
            rig.Refill();
            a = player.GetStamina();
            yield return Ticks(75);
            var deepRate = (a - player.GetStamina()) / (75 * dt);
            c.Check(Mathf.Abs(surfaceRate - drain) <= 0.12f * drain && Mathf.Abs(deepRate - drain) <= 0.12f * drain,
                $"multiplier 1: {F(surfaceRate)} per second swimming forward at the surface, {F(deepRate)} holding still under "
                + $"water (vanilla formula {F(drain)})");

            // ---- UnderwaterStaminaMultiplier 0: free, and no stamina back while hovering (T27) ----
            var drainMin = player.m_swimStaminaDrainMinSkill;
            var drainMax = player.m_swimStaminaDrainMaxSkill;
            ServerRules.TestRules = new DiveRules { UnderwaterStaminaMultiplier = 0f };
            player.m_stamina = player.GetMaxStamina() * 0.6f;
            // Longer than the game's stamina regen delay (1 s): a regen would have started.
            yield return Ticks(60);
            var least = player.GetStamina();
            var most = least;
            for (var i = 0; i < 125; i++)
            {
                yield return FixedTick;
                least = Mathf.Min(least, player.GetStamina());
                most = Mathf.Max(most, player.GetStamina());
            }
            c.Check(DiveState.Deep && most - least < 0.1f && Mathf.Abs(most - player.GetMaxStamina() * 0.6f) < 0.1f,
                $"multiplier 0: holding still under water costs nothing and no stamina comes back while hovering "
                + $"({F(least)}..{F(most)} in 2.5 s, set to {F(player.GetMaxStamina() * 0.6f)} before)");
            a = player.GetStamina();
            DiveInput.TestDown = true;
            yield return Ticks(40);
            DiveInput.TestDown = false;
            c.Check(Mathf.Abs(a - player.GetStamina()) < 0.1f, $"multiplier 0: diving costs nothing ({F(a)} -> {F(player.GetStamina())})");
            // Touching a slope or rock = "on ground" for the game: stamina comes back there (vanilla rule). Contact faked
            // with the field vanilla reads (IsOnGround: m_lastGroundTouch).
            yield return Ticks(65);
            a = player.GetStamina();
            for (var i = 0; i < 125; i++)
            {
                player.m_lastGroundTouch = 0f;
                yield return FixedTick;
            }
            c.Check(player.GetStamina() - a > 0.5f && DiveState.Diving,
                $"multiplier 0, touching the sea floor (ground contact faked): stamina comes back ({F(a)} -> {F(player.GetStamina())} "
                + "in 2.5 s)");
            yield return Ticks(15);

            // ---- multiplier 2: twice the drain (T27) ----
            ServerRules.TestRules = new DiveRules { UnderwaterStaminaMultiplier = 2f };
            rig.Refill();
            yield return Ticks(3);
            a = player.GetStamina();
            yield return Ticks(75);
            var doubleRate = (a - player.GetStamina()) / (75 * dt);
            c.Check(DiveState.Deep && Mathf.Abs(doubleRate - 2f * deepRate) <= 0.12f * 2f * deepRate,
                $"multiplier 2: holding still under water drains {F(doubleRate)} per second, twice {F(deepRate)}");
            c.Check(player.m_swimStaminaDrainMinSkill == drainMin && player.m_swimStaminaDrainMaxSkill == drainMax,
                $"the player's own drain values are as before the dive ({F(player.m_swimStaminaDrainMinSkill)} / "
                + $"{F(player.m_swimStaminaDrainMaxSkill)}, were {F(drainMin)} / {F(drainMax)})");

            // ---- surface swimming is the same with both multipliers (T27) ----
            // Up with multiplier 1 (x2 could empty the bar on the way), then x2 again at the surface.
            ServerRules.TestRules = new DiveRules();
            rig.Refill();
            yield return Surface(player, 8f, box);
            c.Check(box.Ok, "back at the surface: " + box.Detail);
            ServerRules.TestRules = new DiveRules { UnderwaterStaminaMultiplier = 2f };
            yield return new WaitForSeconds(1f);
            rig.Refill();
            for (var i = 0; i < 15; i++)
            {
                rig.Drive(Vector3.forward);
                yield return FixedTick;
            }
            a = player.GetStamina();
            for (var i = 0; i < 75; i++)
            {
                rig.Drive(Vector3.forward);
                yield return FixedTick;
            }
            var surfaceDouble = (a - player.GetStamina()) / (75 * dt);
            ServerRules.TestRules = new DiveRules { UnderwaterStaminaMultiplier = 0f };
            a = player.GetStamina();
            for (var i = 0; i < 75; i++)
            {
                rig.Drive(Vector3.forward);
                yield return FixedTick;
            }
            var surfaceFree = (a - player.GetStamina()) / (75 * dt);
            rig.Drive(Vector3.zero);
            c.Check(!DiveState.Diving && Mathf.Abs(surfaceDouble - surfaceRate) <= 0.12f * surfaceRate
                    && Mathf.Abs(surfaceFree - surfaceRate) <= 0.12f * surfaceRate,
                $"swimming forward at the surface drains the same with multiplier 2 ({F(surfaceDouble)}) and 0 ({F(surfaceFree)}) "
                + $"as with 1 ({F(surfaceRate)})");
            ServerRules.TestRules = new DiveRules();
            rig.GiveStaminaRateBack();
            yield return new WaitForSeconds(1f);

            yield return GoHome(s);
            CheckLog(s);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- dive.waves, dive.bug.storm-waves ----------

    // Wind pinned for dive.waves (console "wind 0 0.3"). Vanilla buoyancy (Character.UpdateSwimming) pull a swimmer up
    // at 5 m/s per metre under the rest height, so on water rising at w m/s a plain swimmer sit w / 5 m under it. At
    // this wind no wave rise at 2.5 m/s, so no swimmer get DiveLogic.EnterMargin (0.5 m) under. In a storm waves do:
    // that is dive.bug.storm-waves.
    private const float WavesWind = 0.3f;

    // What Crouch + Jump held at the surface did, physics tick by physics tick.
    private sealed class SurfaceHold
    {
        internal bool Diving;                          // dive on a few ticks after the keys went down
        internal int Ticks;
        internal int DeepTicks;
        internal int VanillaTicks;                     // vertical left to the normal game (hand-back, gravity on)
        internal int FirstDeep = -1;                   // tick of the first deep tick
        internal float UnderAtFirstDeep;
        internal float RiseAtFirstDeep;                // water at the swimmer over the 0.2 s before, m/s
        internal float MaxUnder = float.NegativeInfinity;
        internal float HardUnder = float.PositiveInfinity;   // caller: feet this far under the rest height = the hard case
        internal int FirstHard = -1;                   // tick the hard case first came
        internal float FeetLow;
        internal float FeetHigh;
        internal float WaterLow;
        internal float WaterHigh;
        internal float Before;
        internal float After;

        internal float Seconds => Ticks * Time.fixedDeltaTime;

        internal float FeetRange => FeetHigh - FeetLow;

        internal float WaterRange => WaterHigh - WaterLow;

        // Hard case came, and 1 s went by after it (a deep tick or a drain it cause had time to show).
        internal bool HardSeen => FirstHard >= 0 && Ticks - FirstHard >= 50;

        internal string Facts()
        {
            var text = $"deep {DeepTicks} of {Ticks} ticks, left to the normal game {VanillaTicks} of {Ticks}, feet at most "
                       + $"{F(MaxUnder)} m under the rest height, the swimmer rose and fell {F(FeetRange)} m while the water rose "
                       + $"and fell {F(WaterRange)} m, stamina {F(Before)} -> {F(After)}";
            if (FirstDeep >= 0)
            {
                text += $"; first counted as deep after {F(FirstDeep * Time.fixedDeltaTime)} s, feet {F(UnderAtFirstDeep)} m under "
                        + $"the rest height while the water rose at {F(RiseAtFirstDeep)} m/s";
            }
            return text;
        }
    }

    // Pinned wind reached (the game finish a wind change under way first, up to 5 s, then take 5 s for the new one),
    // then 1 s more on the waves of that wind. Bounded: the caller look at the wind after.
    private static IEnumerator WaitWind(float want)
    {
        var t0 = Time.time;
        while (EnvMan.instance != null && Mathf.Abs(EnvMan.instance.GetWindIntensity() - want) > 0.02f && Time.time - t0 < 12f)
        {
            yield return new WaitForSeconds(0.2f);
        }
        yield return new WaitForSeconds(1f);
    }

    // Plain swimmer, no key: how much it rise and fall (Point.x) and how far under the rest height the normal game let
    // it get (Value).
    private static IEnumerator PlainSwim(Player player, int ticks, Box result)
    {
        var under = float.NegativeInfinity;
        var low = player.transform.position.y;
        var high = low;
        for (var i = 0; i < ticks; i++)
        {
            yield return FixedTick;
            under = Mathf.Max(under, Under(player));
            low = Mathf.Min(low, player.transform.position.y);
            high = Mathf.Max(high, player.transform.position.y);
        }
        result.Value = under;
        result.Point = new Vector3(high - low, 0f, 0f);
    }

    // Stamina full, then Crouch + Jump held from now. Stop: minTicks done and `enough`, or maxTicks, or 5 s after the
    // first deep tick (long enough to see what deep then do to the swimmer).
    private static IEnumerator HoldAtSurface(Player player, DiveRig rig, int minTicks, int maxTicks,
        Func<SurfaceHold, bool> enough, SurfaceHold h)
    {
        const int AfterDeepTicks = 250;
        const int RiseTicks = 10;
        rig.Refill();
        h.Before = player.GetStamina();
        DiveInput.TestDown = true;
        DiveInput.TestUp = true;
        yield return Ticks(3);
        h.Diving = DiveState.Diving;
        h.FeetLow = player.transform.position.y;
        h.FeetHigh = h.FeetLow;
        h.WaterLow = player.GetLiquidLevel();
        h.WaterHigh = h.WaterLow;
        var levels = new float[RiseTicks];
        for (var i = 0; i < RiseTicks; i++)
        {
            levels[i] = h.WaterLow;
        }
        while (h.Ticks < maxTicks)
        {
            yield return FixedTick;
            var level = player.GetLiquidLevel();
            var feet = player.transform.position.y;
            var under = level - player.m_swimDepth - feet;
            var slot = h.Ticks % RiseTicks;
            var rise = (level - levels[slot]) / (RiseTicks * Time.fixedDeltaTime);
            levels[slot] = level;
            h.Ticks++;
            if (DiveState.Deep)
            {
                h.DeepTicks++;
                if (h.FirstDeep < 0)
                {
                    h.FirstDeep = h.Ticks;
                    h.UnderAtFirstDeep = under;
                    h.RiseAtFirstDeep = rise;
                }
            }
            if (DiveState.Diving && DiveState.HandedBack && player.m_body.useGravity)
            {
                h.VanillaTicks++;
            }
            h.MaxUnder = Mathf.Max(h.MaxUnder, under);
            if (h.FirstHard < 0 && under >= h.HardUnder)
            {
                h.FirstHard = h.Ticks;
            }
            h.FeetLow = Mathf.Min(h.FeetLow, feet);
            h.FeetHigh = Mathf.Max(h.FeetHigh, feet);
            h.WaterLow = Mathf.Min(h.WaterLow, level);
            h.WaterHigh = Mathf.Max(h.WaterHigh, level);
            if (h.FirstDeep >= 0 ? h.Ticks - h.FirstDeep >= AfterDeepTicks : h.Ticks >= minTicks && enough(h))
            {
                break;
            }
        }
        h.After = player.GetStamina();
        DiveInput.TestDown = false;
        DiveInput.TestUp = false;
    }

    // Waves the hand-back to the normal game handle (wind pinned at WavesWind): Crouch + Jump at the surface ride them
    // with no drain (T32, G4). Storm waves: dive.bug.storm-waves.
    private static IEnumerator RunWaves()
    {
        yield return Settle();
        var s = Begin(WavesName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        try
        {
            ApplyDefaults();
            if (!DeepSpot(s, out var spot))
            {
                c.Report();
                yield break;
            }
            // Wind pinned before the trip: the game's 5 s wind change is done on arrival.
            PinWind(rig, WavesWind);
            var ok = new Box();
            yield return EnterWater(s, spot, true, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            yield return WaitWind(WavesWind);
            var wind = EnvMan.instance.GetWindIntensity();
            c.Note($"wind pinned at {F(WavesWind)}: wind now {F(wind)}");
            // Other wind = other waves (stronger: the storm bug; weaker: nothing to ride): not this test.
            if (!c.Check(Mathf.Abs(wind - WavesWind) <= 0.02f, $"the wind is the pinned one (wanted {F(WavesWind)}, is {F(wind)})"))
            {
                c.Report();
                yield break;
            }

            // ---- a plain swimmer on these waves (no key): how far under the rest height the game itself lets it get ----
            var plain = new Box();
            yield return PlainSwim(player, 250, plain);
            c.Note($"plain swimmer for 5 s, no key: rose and fell {F(plain.Point.x)} m, feet at most {F(plain.Value)} m under the "
                   + $"rest height (the dive counts a diver as deep from {F(DiveLogic.EnterMargin)} m)");

            // ---- Crouch + Jump held 10 s (longer, up to 20 s, while the water moved under 0.4 m): rides the waves like
            // that swimmer, no drain (T32, G4) ----
            rig.TakeStaminaRate();
            yield return new WaitForSeconds(0.4f);
            var h = new SurfaceHold();
            yield return HoldAtSurface(player, rig, 500, 1000, x => x.WaterRange >= 0.4f, h);
            var held = $"waves at wind {F(WavesWind)}, Crouch and Jump held {F(h.Seconds)} s at the surface";
            c.Note($"{held}: {h.Facts()}");
            // No waves = the wave part not tested at all: say so with a failed check, not a pass.
            c.Check(h.WaterRange >= 0.4f,
                $"{held}: there are waves to ride (the water at the swimmer rose and fell {F(h.WaterRange)} m, wanted 0.4 m or more)");
            c.Check(h.Diving && h.DeepTicks == 0 && h.VanillaTicks == h.Ticks,
                $"{held}: floats at the swimming height on the waves ({h.Facts()})");
            c.Check(h.Diving && Mathf.Abs(h.After - h.Before) < 0.2f, $"{held}: the stamina bar stays full ({h.Facts()})");
            // Really carried up and down by the waves (a swimmer held at one height would not move).
            c.Check(h.FeetRange >= 0.6f * h.WaterRange,
                $"{held}: rises and falls with the waves (the swimmer {F(h.FeetRange)} m, the water under it {F(h.WaterRange)} m)");
            rig.GiveStaminaRateBack();
            yield return new WaitForSeconds(1f);

            yield return GoHome(s);
            CheckLog(s);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // REAL BUG, alone here (T32, storm part; found by the run of 2026-10-07, natural ThunderStorm, wind 0.595: deep 366 of
    // 500 ticks, feet 4.8 m under the rest height, stamina 45.9 -> 2.6). Storm wave rise faster than 2.5 m/s, vanilla
    // buoyancy then leave even a plain swimmer more than DiveLogic.EnterMargin under the rest height. With Crouch held
    // (Crouch + Jump = stay) DiveController.Tick count that as deep: from then me hold the height (target 0, gravity
    // off), the wave go on rising over the diver, no sideways swimming, still-diver drain, until a trough come down to
    // him. Fail until the mod is fixed. Wind pinned at the strongest the storm weather itself give, so the fast wave
    // come soon; held up to 40 s. A pass need the fast wave too (feet past EnterMargin under the rest height at least
    // once, 1 s or more before the end, and still never deep): a run where none came fail as "prove nothing", never
    // pass by luck (round 1 passed by luck: natural wind 0.438, no fast wave in its 10 s).
    private static IEnumerator RunBugStormWaves()
    {
        yield return Settle();
        var s = Begin(BugStormWavesName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        try
        {
            ApplyDefaults();
            if (!DeepSpot(s, out var spot))
            {
                c.Report();
                yield break;
            }
            var env = EnvMan.instance;
            var storm = StormEnv();
            EnvSetup setup = null;
            if (!string.IsNullOrEmpty(storm))
            {
                foreach (var e in env.m_environments)
                {
                    if (e != null && e.m_name == storm)
                    {
                        setup = e;
                        break;
                    }
                }
            }
            if (!c.Check(setup != null, "the game has a weather to force (EnvMan.m_environments)"))
            {
                c.Report();
                yield break;
            }
            // Storm forced (console "env <name>") and wind pinned (console "wind") before the trip: weather blend and
            // wind change (5 s) are done on arrival.
            var envBefore = env.m_debugEnv;
            rig.Undo("weather", () =>
            {
                if (EnvMan.instance != null)
                {
                    EnvMan.instance.m_debugEnv = envBefore;
                }
            });
            env.m_debugEnv = storm;
            var strongest = Mathf.Clamp(setup.m_windMax, 0.05f, 1f);
            PinWind(rig, strongest);
            var ok = new Box();
            yield return EnterWater(s, spot, true, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            yield return WaitWind(strongest);
            c.Note($"storm weather '{storm}' forced (its wind goes from {F(setup.m_windMin)} to {F(setup.m_windMax)}), wind pinned "
                   + $"at its strongest: wind now {F(env.GetWindIntensity())}");

            // The normal game alone on this sea (no key, mod not involved): the reason behind the bug.
            var plain = new Box();
            yield return PlainSwim(player, 300, plain);
            c.Note($"plain swimmer for 6 s, no key: rose and fell {F(plain.Point.x)} m, feet at most {F(plain.Value)} m under the "
                   + $"rest height (the dive counts a diver as deep from {F(DiveLogic.EnterMargin)} m)");

            rig.TakeStaminaRate();
            yield return new WaitForSeconds(0.4f);
            // Hard case = feet clearly past the margin where the dive count a held swimmer as deep (0.05 m more: the
            // sample here is after the physics step, the dive look before the next one).
            var h = new SurfaceHold { HardUnder = DiveLogic.EnterMargin + 0.05f };
            yield return HoldAtSurface(player, rig, 500, 2000, x => x.HardSeen, h);
            var held = $"storm (wind {F(strongest)}), Crouch and Jump held {F(h.Seconds)} s at the surface";
            c.Note($"{held}: {h.Facts()}");
            c.Check(h.Diving && h.DeepTicks == 0 && h.VanillaTicks == h.Ticks,
                $"{held}: floats at the swimming height on the waves ({h.Facts()})");
            c.Check(h.Diving && Mathf.Abs(h.After - h.Before) < 0.2f, $"{held}: the stamina bar stays full ({h.Facts()})");
            if (h.DeepTicks == 0)
            {
                c.Check(h.HardSeen,
                    $"{held}: a wave fast enough to tell came (the feet were at most {F(h.MaxUnder)} m under the rest height, "
                    + $"wanted {F(h.HardUnder)} m or more at least 1 s before the end: {F(DiveLogic.EnterMargin)} m is where a held "
                    + "swimmer was counted as deep). None came: this run proves nothing, run it again");
            }
            rig.GiveStaminaRateBack();
            env.m_debugEnv = envBefore;
            yield return new WaitForSeconds(1f);

            yield return GoHome(s);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- dive.keys ----------

    private static readonly string[] KeyNames =
    {
        "Crouch", "Jump", "JoyCrouch", "JoyJump", "Forward", "Backward", "Left", "Right", "AutoRun", "Chat",
    };

    private static void ReleaseKeys()
    {
        foreach (var name in KeyNames)
        {
            var button = Button(name);
            if (button != null)
            {
                button.Release();
            }
        }
    }

    private static IEnumerator RunKeys()
    {
        yield return Settle();
        var s = Begin(KeysName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        var tap = s.Tap;
        try
        {
            // Real keys: no test key override. Rules and view still forced in memory.
            ApplyDefaults();
            DiveInput.ClearTest();
            CalmWind(rig);
            var crouch = Button("Crouch");
            var jump = Button("Jump");
            var joyCrouch = Button("JoyCrouch");
            var joyJump = Button("JoyJump");
            if (!c.Check(crouch != null && jump != null && joyCrouch != null && joyJump != null,
                    $"the game has the buttons the mod reads by name: Crouch {crouch != null}, Jump {jump != null}, JoyCrouch "
                    + $"{joyCrouch != null}, JoyJump {joyJump != null}"))
            {
                c.Report();
                yield break;
            }
            if (!DeepSpot(s, out var spot))
            {
                c.Report();
                yield break;
            }
            rig.Undo("keys", ReleaseKeys);
            var ok = new Box();
            yield return EnterWater(s, spot, false, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            var dt = Time.fixedDeltaTime;
            var controller = player.GetComponent<PlayerController>();
            if (!c.Check(controller != null && controller.enabled && player.TakeInput(),
                    $"the player takes keyboard input (controller on {controller != null && controller.enabled}, no menu open "
                    + $"{player.TakeInput()})"))
            {
                c.Report();
                yield break;
            }
            c.Note($"controller layout {ZInput.InputLayout}, gamepad active {ZInput.IsGamepadActive()}");
            var jumpSkill = rig.SaveSkill(Skills.SkillType.Jump);
            var profile = Game.instance.GetPlayerProfile();

            // ---- a short tap of Crouch: a small dip, the normal game lifts back; never sneaking (T05, X03) ----
            tap.Clear();
            var sneaked = false;
            var tapDiving = false;
            var tapUnder = 0f;
            crouch.Press();
            for (var i = 0; i < 10; i++)
            {
                yield return FixedTick;
                tapDiving |= DiveState.Diving;
                sneaked |= player.IsCrouching();
                tapUnder = Mathf.Max(tapUnder, Under(player));
            }
            crouch.Release();
            for (var i = 0; i < 100; i++)
            {
                yield return FixedTick;
                sneaked |= player.IsCrouching();
                tapUnder = Mathf.Max(tapUnder, Under(player));
            }
            c.Check(tapDiving && !DiveState.Diving && tapUnder < 0.5f && Mathf.Abs(Under(player)) < 0.3f,
                $"a short tap of the Crouch key: a dip of {F(tapUnder)} m, then the normal game lifts the swimmer back "
                + $"(dive seen {tapDiving}, {F(Under(player))} m under the rest height 2 s later)");
            c.Check(tap.Count("Dive started (") == 1 && tap.Count(EndedSurface) == 1,
                $"Crouch tap: one \"Dive started\" and one \"{EndedSurface}\" line ({tap.Count("Dive started (")}, "
                + $"{tap.Count(EndedSurface)})");

            // ---- Jump in open water: nothing (T05) ----
            var jumps = profile.GetStat(PlayerStatType.Jumps);
            var jumpTimer = player.m_jumpTimer;
            var jumped = false;
            var dived = false;
            var above = float.NegativeInfinity;
            jump.Press();
            for (var i = 0; i < 15; i++)
            {
                yield return FixedTick;
                jumped |= player.m_jumpTimer < jumpTimer;
                jumpTimer = player.m_jumpTimer;
                dived |= DiveState.Diving;
                above = Mathf.Max(above, -Under(player));
            }
            jump.Release();
            for (var i = 0; i < 35; i++)
            {
                yield return FixedTick;
                jumped |= player.m_jumpTimer < jumpTimer;
                jumpTimer = player.m_jumpTimer;
                above = Mathf.Max(above, -Under(player));
            }
            c.Check(!jumped && !dived && above < 0.35f && profile.GetStat(PlayerStatType.Jumps) == jumps,
                $"Jump key in open water at the surface: nothing (jump {jumped}, dive {dived}, feet at most {F(above)} m above the "
                + "rest height)");

            // ---- Jump with something solid touched, not diving: still a real jump (T05: climbing out at the shore).
            // Contact faked with the field vanilla reads (m_hitWorldTime, time since the last world hit). ----
            jumped = false;
            jump.Press();
            for (var i = 0; i < 6; i++)
            {
                player.m_hitWorldTime = 0f;
                yield return FixedTick;
                jumped |= player.m_jumpTimer < jumpTimer;
                jumpTimer = player.m_jumpTimer;
            }
            jump.Release();
            c.Check(jumped && profile.GetStat(PlayerStatType.Jumps) > jumps,
                $"Jump key while touching something solid at the surface (contact faked), not diving: a real jump, as in the "
                + $"normal game (jump {jumped}, jumps counted {F(profile.GetStat(PlayerStatType.Jumps) - jumps)})");
            yield return new WaitForSeconds(2.5f);

            // ---- Crouch held: dives; never sneaking (T01, X03) ----
            crouch.Press();
            for (var i = 0; i < 75; i++)
            {
                yield return FixedTick;
                sneaked |= player.IsCrouching();
            }
            c.Check(DiveState.Diving && DiveState.Deep && Under(player) > 1.5f,
                $"Crouch key held 1.5 s: dives ({F(Under(player))} m under the rest height, deep {DiveState.Deep})");

            // Deeper for the Jump taps below (menus under water have their own test: dive.menus).
            for (var i = 0; i < 50; i++)
            {
                yield return FixedTick;
                sneaked |= player.IsCrouching();
            }
            crouch.Release();
            c.Check(!sneaked && !player.IsCrouching() && !player.m_crouchToggled,
                $"Crouch key in water: the player never sneaks (sneak pose seen {sneaked}, crouch toggle {player.m_crouchToggled})");
            yield return Ticks(65);

            // ---- Jump key tapped while touching something solid under water: only up, no jump (T05, T14; what Weapon
            // Moveset makes of it: dive.moveset) ----
            rig.TakeStaminaRate();
            yield return new WaitForSeconds(0.4f);
            rig.Refill();
            var drain = SwimDrain(player);
            jumps = profile.GetStat(PlayerStatType.Jumps);
            var skillLevel = jumpSkill.m_level;
            var skillProgress = jumpSkill.m_accumulator;
            jumpTimer = player.m_jumpTimer;
            jumped = false;
            var yTap = player.transform.position.y;
            var s0 = player.GetStamina();
            var tapTicks = 0;
            for (var press = 0; press < 4; press++)
            {
                jump.Press();
                for (var i = 0; i < 8; i++)
                {
                    player.m_hitWorldTime = 0f;
                    yield return FixedTick;
                    tapTicks++;
                    jumped |= player.m_jumpTimer < jumpTimer;
                    jumpTimer = player.m_jumpTimer;
                }
                jump.Release();
                for (var i = 0; i < 8; i++)
                {
                    player.m_hitWorldTime = 0f;
                    yield return FixedTick;
                    tapTicks++;
                    jumped |= player.m_jumpTimer < jumpTimer;
                    jumpTimer = player.m_jumpTimer;
                }
            }
            var tapUsed = s0 - player.GetStamina();
            var tapRise = player.transform.position.y - yTap;
            c.Check(DiveState.Diving && !jumped && profile.GetStat(PlayerStatType.Jumps) == jumps && jumpSkill.m_level == skillLevel
                    && jumpSkill.m_accumulator == skillProgress,
                $"Jump key tapped 4 times while touching something solid under water (contact faked): no jump, no Jump skill "
                + $"(jump {jumped}, jumps counted {F(profile.GetStat(PlayerStatType.Jumps) - jumps)}, Jump progress {F(skillProgress)} "
                + $"-> {F(jumpSkill.m_accumulator)})");
            c.Check(tapUsed <= drain * tapTicks * dt * 1.15f + 0.5f,
                $"Jump taps under water: no jump stamina, only the swim drain ({F(tapUsed)} in {F(tapTicks * dt)} s, swim drain "
                + $"{F(drain)} per second)");
            c.Check(tapRise > 0.05f, $"Jump taps under water: the diver only rises ({F(tapRise)} m)");
            rig.GiveStaminaRateBack();
            rig.Refill();

            // ---- Jump key held: up, handed back at the surface, no jump (T02) ----
            jump.Press();
            var t0 = Time.time;
            while (DiveState.Diving && Time.time - t0 < 8f)
            {
                yield return FixedTick;
                jumped |= player.m_jumpTimer < jumpTimer;
                jumpTimer = player.m_jumpTimer;
            }
            var roseIn = Time.time - t0;
            jump.Release();
            above = float.NegativeInfinity;
            for (var i = 0; i < 75; i++)
            {
                yield return FixedTick;
                above = Mathf.Max(above, -Under(player));
                jumped |= player.m_jumpTimer < jumpTimer;
                jumpTimer = player.m_jumpTimer;
            }
            c.Check(!DiveState.Diving && !jumped && above < 0.35f,
                $"Jump key held: rises and is handed back at the surface in {F(roseIn)} s, no jump out of the water (feet at most "
                + $"{F(above)} m above the rest height)");

            // ---- gamepad buttons (T11): the crouch and jump buttons of the controller, by their game names ----
            yield return new WaitForSeconds(1f);
            joyCrouch.Press();
            for (var i = 0; i < 75; i++)
            {
                yield return FixedTick;
                sneaked |= player.IsCrouching();
            }
            var padDeep = DiveState.Diving && DiveState.Deep;
            var padUnder = Under(player);
            joyCrouch.Release();
            joyJump.Press();
            t0 = Time.time;
            while (DiveState.Diving && Time.time - t0 < 6f)
            {
                yield return FixedTick;
                jumped |= player.m_jumpTimer < jumpTimer;
                jumpTimer = player.m_jumpTimer;
            }
            joyJump.Release();
            c.Check(padDeep && padUnder > 1.2f && !DiveState.Diving && !jumped,
                $"gamepad crouch button held: dives ({F(padUnder)} m in 1.5 s); gamepad jump button held: rises back to the "
                + $"surface in {F(Time.time - t0)} s, no jump");
            c.Check(!sneaked && !player.IsCrouching(), "gamepad crouch button in water: the player never sneaks");
            yield return new WaitForSeconds(1f);

            ReleaseKeys();
            yield return GoHome(s);
            CheckLog(s);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- dive.menus ----------

    // Chat window closed like Enter or Escape leave it: no typing focus, input line hidden.
    private static void CloseChat()
    {
        var chat = Chat.instance;
        if (chat == null || chat.m_input == null)
        {
            return;
        }
        if (UnityEngine.EventSystems.EventSystem.current != null)
        {
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        }
        chat.m_input.gameObject.SetActive(false);
    }

    // Own test (not part of dive.keys): the chat needs the typing focus, which the game only gives while its window is
    // in front. No focus = this test fails and says so, the key tests stay untouched.
    private static IEnumerator RunMenus()
    {
        yield return Settle();
        var s = Begin(MenusName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        try
        {
            // Real keys: no test key override. Rules and view still forced in memory.
            ApplyDefaults();
            DiveInput.ClearTest();
            CalmWind(rig);
            var crouch = Button("Crouch");
            var jump = Button("Jump");
            var chatKey = Button("Chat");
            if (!c.Check(crouch != null && jump != null && chatKey != null,
                    $"the game has the buttons Crouch {crouch != null}, Jump {jump != null}, Chat {chatKey != null}"))
            {
                c.Report();
                yield break;
            }
            if (!DeepSpot(s, out var spot))
            {
                c.Report();
                yield break;
            }
            rig.Undo("keys", ReleaseKeys);
            var ok = new Box();
            yield return EnterWater(s, spot, false, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            var controller = player.GetComponent<PlayerController>();
            var gui = InventoryGui.instance;
            var map = Minimap.instance;
            var chat = Chat.instance;
            if (!c.Check(controller != null && controller.enabled && player.TakeInput() && gui != null && map != null
                         && chat != null && chat.m_input != null,
                    $"the player takes keyboard input and the game has an inventory window, a map and a chat (controller on "
                    + $"{controller != null && controller.enabled}, no menu open {player.TakeInput()}, inventory {gui != null}, map "
                    + $"{map != null}, chat {chat != null})"))
            {
                c.Report();
                yield break;
            }

            // ---- Crouch key held from here to the end of the chat part ----
            crouch.Press();
            yield return Ticks(40);
            c.Check(DiveState.Diving && Under(player) > 0.3f,
                $"Crouch key held: the dive starts ({F(Under(player))} m under the rest height after 0.8 s)");

            // ---- inventory (opened by the game's own call, as the Tab key does) ----
            rig.Undo("inventory", () =>
            {
                if (InventoryGui.instance != null && InventoryGui.IsVisible())
                {
                    InventoryGui.instance.Hide();
                }
            });
            gui.Show(null);
            // Own speed gone first (about 1 s), then the hold is measured.
            yield return Ticks(60);
            var yMenu = player.transform.position.y;
            var drift = 0f;
            var open = true;
            for (var i = 0; i < 75; i++)
            {
                yield return FixedTick;
                drift = Mathf.Max(drift, Mathf.Abs(player.transform.position.y - yMenu));
                open &= InventoryGui.IsVisible();
            }
            c.Check(open && DiveState.Deep && drift < 0.3f,
                $"inventory open under water with Crouch held: the diver stays at the same depth (moved {F(drift)} m in 1.5 s, "
                + $"{F(Under(player))} m under the rest height)");
            gui.Hide();
            yield return Ticks(20);
            yMenu = player.transform.position.y;
            yield return Ticks(30);
            c.Check(!InventoryGui.IsVisible() && yMenu - player.transform.position.y > 0.4f,
                $"inventory closed: Crouch goes down again ({F(yMenu - player.transform.position.y)} m in 0.6 s)");

            // ---- large map (the game's own call, as the M key does) ----
            var mode = map.m_mode;
            rig.Undo("map", () =>
            {
                if (Minimap.instance != null && Minimap.instance.m_mode == Minimap.MapMode.Large)
                {
                    Minimap.instance.SetMapMode(Minimap.MapMode.Small);
                }
            });
            map.SetMapMode(Minimap.MapMode.Large);
            yield return Ticks(60);
            yMenu = player.transform.position.y;
            drift = 0f;
            open = true;
            for (var i = 0; i < 75; i++)
            {
                yield return FixedTick;
                drift = Mathf.Max(drift, Mathf.Abs(player.transform.position.y - yMenu));
                open &= Minimap.IsOpen();
            }
            c.Check(open && DiveState.Deep && drift < 0.3f,
                $"map open under water with Crouch held: the diver stays at the same depth (moved {F(drift)} m in 1.5 s)");
            map.SetMapMode(mode == Minimap.MapMode.Large ? Minimap.MapMode.Small : mode);
            yield return Ticks(20);
            yMenu = player.transform.position.y;
            yield return Ticks(30);
            c.Check(!Minimap.IsOpen() && yMenu - player.transform.position.y > 0.4f,
                $"map closed: Crouch goes down again ({F(yMenu - player.transform.position.y)} m in 0.6 s)");

            // ---- chat (opened with the game's Chat button) ----
            rig.Undo("chat", () =>
            {
                if (Chat.instance != null && Chat.instance.HasFocus())
                {
                    CloseChat();
                }
            });
            chatKey.Press();
            yield return null;
            yield return null;
            chatKey.Release();
            for (var i = 0; i < 10 && !chat.HasFocus(); i++)
            {
                yield return null;
            }
            if (c.Check(chat.HasFocus(),
                    "the Chat key opens the chat with the typing focus (the game gives it only while its window is in front; "
                    + "without it the chat under water is not checked)"))
            {
                yield return Ticks(60);
                yMenu = player.transform.position.y;
                drift = 0f;
                open = true;
                for (var i = 0; i < 60; i++)
                {
                    yield return FixedTick;
                    drift = Mathf.Max(drift, Mathf.Abs(player.transform.position.y - yMenu));
                    open &= chat.HasFocus();
                }
                c.Check(open && DiveState.Deep && drift < 0.3f,
                    $"chat open under water with Crouch held: the diver stays at the same depth (moved {F(drift)} m in 1.2 s, "
                    + $"chat open all the time {open})");
                CloseChat();
                yield return Ticks(20);
                yMenu = player.transform.position.y;
                yield return Ticks(30);
                c.Check(!chat.HasFocus() && yMenu - player.transform.position.y > 0.4f,
                    $"chat closed: Crouch goes down again ({F(yMenu - player.transform.position.y)} m in 0.6 s)");
            }
            crouch.Release();

            // ---- menus closed: Jump works again too ----
            var yJump = player.transform.position.y;
            var deepBefore = DiveState.Diving && DiveState.Deep;
            jump.Press();
            var t0 = Time.time;
            while (DiveState.Diving && Time.time - t0 < 12f)
            {
                yield return FixedTick;
            }
            jump.Release();
            c.Check(deepBefore && !DiveState.Diving && player.transform.position.y - yJump > 1f,
                $"menus closed: the Jump key rises {F(player.transform.position.y - yJump)} m, back to the surface in "
                + $"{F(Time.time - t0)} s");
            yield return new WaitForSeconds(1f);

            ReleaseKeys();
            yield return GoHome(s);
            CheckLog(s);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- dive.moveset ----------

    private const string MovesetGuid = "MC.Combat.Weapons.Moveset";
    private const string MovesetTracker = "MC.Combat.WeaponsMovesetMod.MoveTracker";

    // Weapon Moveset's jump token, read by name (no compile-time link): live now, and the time of the last jump it
    // counted (it writes that time each time it arms the token, so "time unchanged" = "no token armed").
    private static bool JumpToken(out bool live, out float at)
    {
        live = false;
        at = 0f;
        if (!TryReadStatic(MovesetGuid, MovesetTracker, "JumpLive", out var l) || !(l is bool isLive)
            || !TryReadStatic(MovesetGuid, MovesetTracker, "_jumpAt", out var a) || !(a is float jumpAt))
        {
            return false;
        }
        live = isLive;
        at = jumpAt;
        return true;
    }

    // Own test (not part of dive.keys): it reads another mod. Weapon Moveset arms a jump token on a real jump and ends
    // it on landing only; a swimmer never lands, so a token from a jump before the dive is still live under water. The
    // question for Swim Dive is only: does Jump under water arm one. Control first: the same key with the same faked
    // touch at the surface (not diving) is a real jump and does arm it.
    private static IEnumerator RunMoveset()
    {
        yield return Settle();
        var s = Begin(MovesetName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        try
        {
            ApplyDefaults();
            DiveInput.ClearTest();
            CalmWind(rig);
            var crouch = Button("Crouch");
            var jump = Button("Jump");
            if (!c.Check(crouch != null && jump != null, $"the game has the buttons Crouch {crouch != null}, Jump {jump != null}"))
            {
                c.Report();
                yield break;
            }
            if (!c.Check(JumpToken(out _, out _),
                    "Weapon Moveset is loaded and its jump token can be read (MoveTracker.JumpLive and _jumpAt); without it "
                    + "nothing is checked here"))
            {
                c.Report();
                yield break;
            }
            if (!DeepSpot(s, out var spot))
            {
                c.Report();
                yield break;
            }
            rig.Undo("keys", ReleaseKeys);
            var ok = new Box();
            yield return EnterWater(s, spot, false, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            var controller = player.GetComponent<PlayerController>();
            if (!c.Check(controller != null && controller.enabled && player.TakeInput(),
                    $"the player takes keyboard input (controller on {controller != null && controller.enabled}, no menu open "
                    + $"{player.TakeInput()})"))
            {
                c.Report();
                yield break;
            }
            // The control jump below raises Jump: put back at the end.
            rig.SaveSkill(Skills.SkillType.Jump);

            // ---- control: Jump at the surface with a solid touch (faked), not diving = a real jump, token armed ----
            JumpToken(out var liveBefore, out var atBefore);
            var jumpTimer = player.m_jumpTimer;
            var jumped = false;
            jump.Press();
            for (var i = 0; i < 6; i++)
            {
                player.m_hitWorldTime = 0f;
                yield return FixedTick;
                jumped |= player.m_jumpTimer < jumpTimer;
                jumpTimer = player.m_jumpTimer;
            }
            jump.Release();
            JumpToken(out var liveControl, out var atControl);
            if (!c.Check(jumped && liveControl && atControl != atBefore,
                    $"control: Jump at the surface while touching something solid (contact faked), not diving, is a real jump and "
                    + $"Weapon Moveset arms its jump token (jump {jumped}, token live {liveBefore} -> {liveControl}, last jump time "
                    + $"{F(atBefore)} -> {F(atControl)})"))
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(2.5f);

            // ---- dive, then Jump tapped under water with the same touch: no token armed ----
            crouch.Press();
            yield return Ticks(100);
            crouch.Release();
            c.Check(DiveState.Diving && DiveState.Deep && Under(player) > 1.5f,
                $"Crouch key held 2 s: dives ({F(Under(player))} m under the rest height, deep {DiveState.Deep})");
            yield return Ticks(65);
            JumpToken(out var liveDeep, out var atDeep);
            c.Note($"under water before the taps: jump token live {liveDeep}, last jump time {F(atDeep)} (the jump at the surface; "
                   + "Weapon Moveset ends a token on landing, and a swimmer never lands)");
            jumpTimer = player.m_jumpTimer;
            jumped = false;
            var armed = false;
            for (var press = 0; press < 4; press++)
            {
                jump.Press();
                for (var i = 0; i < 8; i++)
                {
                    player.m_hitWorldTime = 0f;
                    yield return FixedTick;
                    jumped |= player.m_jumpTimer < jumpTimer;
                    jumpTimer = player.m_jumpTimer;
                    armed |= !JumpToken(out _, out var at) || at != atDeep;
                }
                jump.Release();
                for (var i = 0; i < 8; i++)
                {
                    player.m_hitWorldTime = 0f;
                    yield return FixedTick;
                    jumped |= player.m_jumpTimer < jumpTimer;
                    jumpTimer = player.m_jumpTimer;
                    armed |= !JumpToken(out _, out var at) || at != atDeep;
                }
            }
            JumpToken(out var liveAfter, out var atAfter);
            c.Check(DiveState.Diving && !jumped && !armed && atAfter == atDeep && atDeep == atControl,
                $"Jump key tapped 4 times under water while touching something solid (contact faked): no jump, and Weapon Moveset "
                + $"arms no jump token (jump {jumped}, last jump time {F(atDeep)} -> {F(atAfter)}, token live {liveDeep} -> "
                + $"{liveAfter}, still diving {DiveState.Diving})");

            // ---- back up ----
            jump.Press();
            var t0 = Time.time;
            while (DiveState.Diving && Time.time - t0 < 8f)
            {
                yield return FixedTick;
            }
            jump.Release();
            c.Check(!DiveState.Diving, $"Jump key held: back at the surface in {F(Time.time - t0)} s");
            yield return new WaitForSeconds(1f);

            ReleaseKeys();
            yield return GoHome(s);
            CheckLog(s);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }
}
#endif
