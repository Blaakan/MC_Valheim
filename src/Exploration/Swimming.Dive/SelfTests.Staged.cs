#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SwimmingDiveMod;

// Debug build only. Registration of the tests added after the first lists (SelfTests.Input.cs, SelfTests.Real.cs,
// SelfTests.Mods.cs) and the staged tests (single-player run only, after the normal tests):
//   dive.disabled.log      L03: a listener put on at plugin load. End of the first game (every test of every mod ran,
//                          the relogs too): no warning or error line of mine in the whole session but the one a test
//                          asks for (made-up dive mod); second game (me off): none at all
//   dive.relog.underwater  T22: 5 m down with camera, tint and turned surfaces, then a real log out. Main menu: the
//                          fog is the menu's own, not the tint. Back in the world: the player is where it logged out,
//                          lifted to the surface by the normal game, no dive, view normal, no warning or error line
//                          of mine over the whole relog. Then the trip home
//   dive.disabled.vanilla  L02: game started with Enabled = false: state Disabled, no patch of mine applied, the real
//                          Crouch key held 3 s in deep water does nothing, the camera never goes under, no tint.
//                          Control at the end of the first game (me on): the same patch look-up finds every dive patch
// What the probe check by itself for every mod (no code here): probe.relog.<GUID>, probe.quit.<GUID>,
// probe.disabled.<GUID> (docs/modding/framework.md, "Staged tests").
internal static partial class SelfTests
{
    private const string RelogName = "dive.relog.underwater";
    private const string DisabledName = "dive.disabled.vanilla";
    private const string SessionLogName = "dive.disabled.log";

    // Single-player tests added to the first list (run after it, same rules). The real death last: it leaves a new
    // player object behind.
    private static readonly KeyValuePair<string, Func<IEnumerator>>[] MoreTests =
    {
        new KeyValuePair<string, Func<IEnumerator>>(PadName, RunPad),
        new KeyValuePair<string, Func<IEnumerator>>(PadAltName, RunPadAlt),
        new KeyValuePair<string, Func<IEnumerator>>(RebindName, RunRebind),
        new KeyValuePair<string, Func<IEnumerator>>(RebindSettingsName, RunRebindSettings),
        new KeyValuePair<string, Func<IEnumerator>>(StormKeysName, RunStormKeys),
        new KeyValuePair<string, Func<IEnumerator>>(DualWieldName, RunDualWield),
        new KeyValuePair<string, Func<IEnumerator>>(SneakIconsName, RunSneakIcons),
        new KeyValuePair<string, Func<IEnumerator>>(MovesetAttackName, RunMovesetAttack),
        new KeyValuePair<string, Func<IEnumerator>>(TarPitName, RunTarPit),
        new KeyValuePair<string, Func<IEnumerator>>(SerpentHuntName, RunSerpentHunt),
        new KeyValuePair<string, Func<IEnumerator>>(DeathName, RunDeath),
    };

    // SelfTests.Register (OnActivated).
    private static void RegisterMore()
    {
        foreach (var test in MoreTests)
        {
            SelfTest.Register(test.Key, test.Value);
        }
        SelfTest.RegisterRelog(RelogName, RelogBefore, RelogMenu, RelogAfter);
    }

    // SelfTests.Unregister (OnDeactivated).
    private static void UnregisterMore()
    {
        foreach (var test in MoreTests)
        {
            SelfTest.Unregister(test.Key);
        }
        SelfTest.UnregisterRelog(RelogName);
    }

    // Plugin.BindConfig (Debug build only): test of a game started with me off. OnActivated never run there, so me
    // register here; never unregistered.
    internal static void RegisterAtBind()
    {
        _session = new SessionTap();
        SelfTest.RegisterDisabledStart(DisabledName, DisabledLeave, DisabledCheck);
        SelfTest.RegisterDisabledStart(SessionLogName, SessionLogLeave, SessionLogCheck);
    }

    // ---------- dive.disabled.log (L03) ----------

    // Me listen from plugin load to game end: warning and error lines of my own logger, and error lines of any other
    // logger that name my code. Only a count and the first three lines kept. Not counted: self-test result lines, and
    // the one warning a test asks for (dive.exits make up another dive mod: "... also handles diving ...").
    private sealed class SessionTap : ILogListener
    {
        private const string OwnNamespace = "SwimmingDiveMod";
        private const string AskedFor = "also handles diving";

        private readonly object _gate = new object();
        private readonly List<string> _first = new List<string>();
        private int _count;
        // Every line of my own logger, any level (ready line come right after me): 0 = me deaf, "no warning" prove nothing.
        private int _heard;

        internal SessionTap()
        {
            BepInEx.Logging.Logger.Listeners.Add(this);
        }

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            try
            {
                if (eventArgs == null || eventArgs.Source == null)
                {
                    return;
                }
                if (eventArgs.Source.SourceName == ModInfo.Name)
                {
                    System.Threading.Interlocked.Increment(ref _heard);
                }
                if ((eventArgs.Level & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) == 0)
                {
                    return;
                }
                var text = eventArgs.Data as string ?? eventArgs.Data?.ToString();
                if (text == null)
                {
                    return;
                }
                if (eventArgs.Source.SourceName != ModInfo.Name
                    && ((eventArgs.Level & (LogLevel.Error | LogLevel.Fatal)) == 0
                        || text.IndexOf(OwnNamespace, StringComparison.Ordinal) < 0))
                {
                    return;
                }
                if (text.StartsWith(SelfTest.Prefix, StringComparison.Ordinal)
                    || text.IndexOf(AskedFor, StringComparison.Ordinal) >= 0)
                {
                    return;
                }
                lock (_gate)
                {
                    _count++;
                    if (_first.Count < 3)
                    {
                        var cut = text.IndexOf('\n');
                        _first.Add(cut > 0 ? text.Substring(0, cut).TrimEnd() : text);
                    }
                }
            }
            catch
            {
                // Me never throw inside the logger.
            }
        }

        internal int Problems(out string first, out int heard)
        {
            heard = System.Threading.Volatile.Read(ref _heard);
            lock (_gate)
            {
                first = string.Join(" | ", _first.ToArray());
                return _count;
            }
        }

        public void Dispose()
        {
        }
    }

    private static SessionTap _session;

    private static void ReportSession(string when)
    {
        if (_session == null)
        {
            SelfTest.Fail(SessionLogName, "no session log listener (it is put on when the plugin loads): nothing checked");
            return;
        }
        var n = _session.Problems(out var first, out var heard);
        if (heard == 0)
        {
            // Mod log its ready line after the listener is put on (both games): none heard = listener not working.
            SelfTest.Fail(SessionLogName, $"the session log listener heard no line of {ModInfo.Name} at all {when} (not even its "
                                          + "ready line): nothing checked");
            return;
        }
        if (n == 0)
        {
            SelfTest.Pass(SessionLogName, $"no warning or error line from {ModInfo.Name} {when} ({heard} lines of the mod heard)");
        }
        else
        {
            SelfTest.Fail(SessionLogName, $"{n} warning or error line(s) from {ModInfo.Name} {when}, first: {first}");
        }
    }

    // End of the first game: every test of every mod ran with me on, and the relogs.
    private static IEnumerator SessionLogLeave()
    {
        yield return null;
        ReportSession("since the game started, with the mod on (every test of the run and the relogs; the warning that the "
                      + "made-up dive mod of dive.exits asks for is not counted)");
    }

    // Second game: me off from the start.
    private static IEnumerator SessionLogCheck()
    {
        yield return null;
        ReportSession("since the second game started, with the mod turned off");
    }

    // ---------- dive.relog.underwater (T22) ----------

    // Kept over the log out (same game process: statics live on; world objects do not).
    private static bool _relogArmed;
    private static Vector3 _relogHome;
    private static Quaternion _relogHomeRot;
    private static Vector3 _relogPoint;
    private static float _relogUnder;
    private static Color _relogTint;
    private static float _relogDensity;
    private static Vector4 _relogSunFog;
    private static float _relogWaterDistance;
    private static LogTap _relogTap;

    private static IEnumerator RelogBefore()
    {
        _relogArmed = false;
        yield return Settle();
        var s = Begin(RelogName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        var keep = false;
        try
        {
            // Default rules stay forced until the menu: no key under water = depth held, whatever this PC's settings.
            ApplyDefaults();
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
            var box = new Box();
            yield return DiveTo(player, 5f, 8f, box);
            yield return Ticks(65);
            yield return ViewSettled(2);
            var cam = GameCamera.instance;
            var camPos = cam.transform.position;
            var surface = Floating.GetLiquidLevel(camPos, 1f, LiquidType.Water);
            var ready = box.Ok && DiveState.Diving && DiveState.Deep && Under(player) >= 4.5f && UnderwaterView.Active
                        && UnderwaterView.FogApplied && UnderwaterView.FlippedCount > 0 && DiveCamera.Lifted && camPos.y < surface;
            if (!c.Check(ready,
                    $"before the log out: {F(Under(player))} m under the rest height with the camera under water ({F(surface - camPos.y)} "
                    + $"m), tint on {UnderwaterView.FogApplied}, {UnderwaterView.FlippedCount} surfaces turned (dive: {box.Detail})"))
            {
                c.Report();
                yield break;
            }
            _relogHome = rig.Origin;
            _relogHomeRot = rig.OriginRotation;
            _relogPoint = player.transform.position;
            _relogUnder = Under(player);
            _relogTint = RenderSettings.fogColor;
            _relogDensity = RenderSettings.fogDensity;
            _relogSunFog = Shader.GetGlobalVector(UnderwaterView.SunFogId);
            _relogWaterDistance = cam.m_minWaterDistance;
            // Log tap stays on over the log out; the part after it reads and stops it.
            _relogTap = s.Tap;
            _relogArmed = true;
            keep = true;
        }
        finally
        {
            // Not ready: everything back now. Ready: the diver stays down for the log out (rig never restored: its
            // player is gone after it); the menu part and the part after clean up.
            if (!keep)
            {
                rig.Restore();
            }
        }
        c.Report($"; logging out {F(_relogUnder)} m under the rest height at {F(_relogPoint)}");
    }

    private static IEnumerator RelogMenu()
    {
        var c = new Checks(RelogName);
        try
        {
            if (!c.Check(_relogArmed, "the part before the log out did not leave the diver under water: nothing to check at the main menu"))
            {
                c.Report();
                yield break;
            }
            // Menu scene writes its fog every frame (MenuScene.Update): one second, then the end of a frame.
            yield return new WaitForSecondsRealtime(1f);
            yield return ViewSettled();
            c.Check(Player.m_localPlayer == null && GameCamera.instance == null, "at the main menu: no player and no game camera");
            var fog = RenderSettings.fogColor;
            var density = RenderSettings.fogDensity;
            var sun = Shader.GetGlobalVector(UnderwaterView.SunFogId);
            c.Check(!(Near(fog, _relogTint, 0.01f) && Near(density, _relogDensity, 0.0001f)) && !NearRaw(sun, _relogSunFog),
                $"main menu: the fog is not the under-water tint the diver logged out with (colour {F(fog)}, density {F(density)}; "
                + $"the tint was {F(_relogTint)}, density {F(_relogDensity)}; fog toward the sun raw {F(sun)}, was {F(_relogSunFog)})");
            var menu = UnityEngine.Object.FindAnyObjectByType<MenuScene>();
            if (menu != null)
            {
                c.Check(Near(fog, menu.m_fogColor, 0.002f) && Near(density, menu.m_fogDensity, 0.0001f)
                        && NearRaw(sun, Raw(menu.m_sunFogColor)),
                    $"main menu: the fog is the menu's own (colour {F(fog)} vs {F(menu.m_fogColor)}, density {F(density)} vs "
                    + $"{F(menu.m_fogDensity)}, fog toward the sun raw {F(sun)} vs {F(Raw(menu.m_sunFogColor))})");
            }
            else
            {
                c.Note("no MenuScene object at the main menu: the menu's own fog values are not known, only \"not the tint\" checked");
            }
            SelfTest.Screenshot(RelogName, "main-menu");
            yield return null;
            yield return null;
        }
        finally
        {
            ClearOverrides();
        }
        c.Report();
    }

    private static IEnumerator RelogAfter()
    {
        var c = new Checks(RelogName);
        var player = Player.m_localPlayer;
        var armed = _relogArmed;
        DiveRig rig = null;
        var home = false;
        try
        {
            ClearOverrides();
            if (!c.Check(armed, "the part before the log out did not leave the diver under water: nothing to check after it"))
            {
                c.Report();
                yield break;
            }
            if (!c.Check(player != null && GameCamera.instance != null && ZoneSystem.instance != null && ZNetScene.instance != null,
                    "back in the world: a player and a camera"))
            {
                c.Report();
                yield break;
            }
            rig = new DiveRig(player, RelogName);
            // The normal game lifts the swimmer (spawned at the log out point): at the rest height within 12 s, no dive.
            var dived = false;
            var t0 = Time.time;
            while (Time.time - t0 < 12f && !(player.IsSwimming() && Mathf.Abs(Under(player)) < 0.4f))
            {
                yield return FixedTick;
                dived |= DiveState.Diving;
            }
            var liftedIn = Time.time - t0;
            for (var i = 0; i < 50; i++)
            {
                yield return FixedTick;
                dived |= DiveState.Diving;
            }
            yield return ViewSettled(2);
            var away = Vector3.Distance(Flat(player.transform.position), Flat(_relogPoint));
            c.Check(away < 6f,
                $"logged back in at the place of the log out ({F(away)} m from it; the diver logged out {F(_relogUnder)} m under the "
                + "rest height)");
            c.Check(player.IsSwimming() && player.InWater() && Mathf.Abs(Under(player)) < 0.5f,
                $"after logging back in the normal game has the swimmer at the surface ({F(Under(player))} m under the rest height "
                + $"{F(liftedIn)} s after the test part began, swimming {player.IsSwimming()})");
            c.Check(!dived && !DiveState.Diving && !DiveState.GravityOff && player.m_body.useGravity,
                $"after logging back in: no dive left over (dive seen {dived}, gravity on {player.m_body.useGravity})");
            var cam = GameCamera.instance;
            var camPos = cam.transform.position;
            var surface = Floating.GetLiquidLevel(camPos, 1f, LiquidType.Water);
            c.Check(camPos.y > surface && !DiveCamera.Lifted && Mathf.Abs(cam.m_minWaterDistance - _relogWaterDistance) < 0.0001f,
                $"after logging back in: the camera is above the water ({F(camPos.y - surface)} m), its water distance is the "
                + $"game's ({F(cam.m_minWaterDistance)}, before the dive {F(_relogWaterDistance)})");
            c.Check(!UnderwaterView.Active && !UnderwaterView.FogApplied && UnderwaterView.FlippedCount == 0 && TurnedSurfaces() == 0,
                $"after logging back in: no tint, no turned water surface ({TurnedSurfaces()} upside down, view on "
                + $"{UnderwaterView.Active}, tint on {UnderwaterView.FogApplied})");
            c.Check(!(Near(RenderSettings.fogColor, _relogTint, 0.01f) && Near(RenderSettings.fogDensity, _relogDensity, 0.0001f)),
                $"after logging back in: the fog is not the under-water tint (colour {F(RenderSettings.fogColor)}, density "
                + $"{F(RenderSettings.fogDensity)})");
            if (c.Check(_relogTap != null, "the log tap of the part before the log out is still there"))
            {
                var n = _relogTap.Problems(null, out var first);
                c.Check(n == 0, $"{n} warning or error line(s) from {ModInfo.Name} from the dive over the log out to here, first: {first}");
            }

            var trip = new Box();
            yield return rig.Travel(_relogHome, _relogHomeRot, trip, TravelTimeout);
            home = trip.Ok;
            c.Check(trip.Ok, "travel back to the start: " + trip.Detail);
        }
        finally
        {
            ClearOverrides();
            _relogArmed = false;
            if (_relogTap != null)
            {
                _relogTap.Stop();
                _relogTap = null;
            }
            try
            {
                // Cut short: send the player home (the game finishes the teleport after the test).
                if (armed && !home && player != null)
                {
                    if (player.IsTeleporting())
                    {
                        player.m_teleportTargetPos = _relogHome;
                        player.m_teleportTargetRot = _relogHomeRot;
                    }
                    else
                    {
                        player.TeleportTo(_relogHome, _relogHomeRot, true);
                    }
                }
            }
            catch (Exception e)
            {
                SelfTest.Note(RelogName, $"sending the player home failed: {e.Message}");
            }
            if (rig != null)
            {
                rig.Restore();
            }
        }
        c.Report();
    }

    // ---------- dive.disabled.vanilla (L02) ----------

    private static bool PatchedByMe(Type type, string method)
    {
        var target = AccessTools.Method(type, method);
        var info = target != null ? Harmony.GetPatchInfo(target) : null;
        return info != null && info.Owners != null && info.Owners.Contains(ModInfo.Guid);
    }

    // End of the first game, me still on: control for the "no patch of mine" check of the second game. Same look-up
    // must find every dive patch now, else "none found" in the second game would prove nothing.
    private static IEnumerator DisabledLeave()
    {
        yield return null;
        var c = new Checks(DisabledName);
        var view = FeatureRegistry.Find(ModInfo.Guid);
        if (view != null && view.Value.IsDisabledByUser)
        {
            // Run that keep me off in the first game too (Test-InWorld -AsConfigured): no game with me on, no control.
            // Me say so and do not fail that run; none of my other tests ran in it, so it tick nothing of mine.
            SelfTest.Pass(DisabledName, $"first game: {ModInfo.Name} is turned off here too (run as configured), so there is no "
                                        + "control for the patch look-up of the second game");
            yield break;
        }
        var motion = PatchedByMe(typeof(Character), nameof(Character.UpdateMotion));
        var swimming = PatchedByMe(typeof(Character), nameof(Character.UpdateSwimming));
        var jumping = PatchedByMe(typeof(Character), nameof(Character.Jump));
        var drain = PatchedByMe(typeof(Player), nameof(Player.OnSwimming));
        var cameraPosition = PatchedByMe(typeof(GameCamera), nameof(GameCamera.GetCameraPosition));
        var cameraUpdate = PatchedByMe(typeof(GameCamera), nameof(GameCamera.UpdateCamera));
        c.Check(view != null && view.Value.IsActive && motion && swimming && jumping && drain && cameraPosition && cameraUpdate,
            $"control, first game with {ModInfo.Name} on (state {(view != null ? view.Value.State : "?")}): every dive patch is found "
            + $"applied (movement {motion}, swimming {swimming}, jump {jumping}, stamina {drain}, camera position {cameraPosition}, "
            + $"camera update {cameraUpdate})");
        c.Report("; the same look-up must find none in the game started with the mod off");
    }

    // Second game of the run: every MC mod Enabled = false from the start.
    private static IEnumerator DisabledCheck()
    {
        yield return Settle();
        var s = Begin(DisabledName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        try
        {
            var view = FeatureRegistry.Find(ModInfo.Guid);
            var state = view != null ? view.Value.State : "?";
            var status = view != null ? view.Value.Status : "?";
            if (!c.Check(SelfTest.Stage == SelfTest.DisabledStage && view != null && view.Value.IsDisabledByUser,
                    $"the game was started with {ModInfo.Name} turned off: state {state}, Status \"{status}\" (stage '{SelfTest.Stage}')"))
            {
                c.Report();
                yield break;
            }
            c.Note($"Status of {ModInfo.Name}: \"{status}\"");
            var motion = PatchedByMe(typeof(Character), nameof(Character.UpdateMotion));
            var swimming = PatchedByMe(typeof(Character), nameof(Character.UpdateSwimming));
            var jumping = PatchedByMe(typeof(Character), nameof(Character.Jump));
            var drain = PatchedByMe(typeof(Player), nameof(Player.OnSwimming));
            var camera = PatchedByMe(typeof(GameCamera), nameof(GameCamera.GetCameraPosition))
                         || PatchedByMe(typeof(GameCamera), nameof(GameCamera.UpdateCamera));
            c.Check(!motion && !swimming && !jumping && !drain && !camera,
                $"turned off: none of the dive patches is applied (movement {motion}, swimming {swimming}, jump {jumping}, stamina "
                + $"{drain}, camera {camera})");
            CalmWind(rig);
            if (!InputReady(c))
            {
                c.Report();
                yield break;
            }
            if (!c.Check(KeyOf("Crouch", out var crouchKey, out var crouchPath),
                    $"Crouch is bound to a key the test can press ('{crouchPath}')"))
            {
                c.Report();
                yield break;
            }
            if (!DeepSpot(s, out var spot))
            {
                c.Report();
                yield break;
            }
            var ok = new Box();
            yield return EnterWater(s, spot, false, ok);
            if (!ok.Ok || !TakesInput(c, player))
            {
                c.Report();
                yield break;
            }
            // Plain swimmer first: how far under the rest height the game itself lets it get here.
            var plain = new Box();
            yield return PlainSwim(player, 100, plain);

            // ---- the real Crouch key held 3 s: nothing ----
            var cam = GameCamera.instance;
            var seen = false;
            var dived = false;
            var sneaked = false;
            var gravityOff = false;
            var camUnder = 0;
            var viewOn = false;
            var maxUnder = float.NegativeInfinity;
            SelfTest.Key(crouchKey, true);
            for (var i = 0; i < 150; i++)
            {
                yield return FixedTick;
                seen |= ZInput.GetButton("Crouch");
                dived |= DiveState.Diving || DiveState.GravityOff;
                sneaked |= player.IsCrouching();
                gravityOff |= !player.m_body.useGravity;
                maxUnder = Mathf.Max(maxUnder, Under(player));
                var camPos = cam.transform.position;
                if (camPos.y < Floating.GetLiquidLevel(camPos, 1f, LiquidType.Water))
                {
                    camUnder++;
                }
                viewOn |= UnderwaterView.Active || UnderwaterView.FogApplied || UnderwaterView.FlippedCount > 0 || DiveCamera.Lifted;
            }
            SelfTest.Key(crouchKey, false);
            yield return Ticks(10);
            c.Check(seen, $"the game read the real Crouch key ({crouchKey}) as held");
            c.Check(!dived && !gravityOff && maxUnder < Mathf.Max(plain.Value, 0f) + 0.3f && player.IsSwimming(),
                $"turned off: the real Crouch key held 3 s does nothing in water (feet at most {F(maxUnder)} m under the rest height; "
                + $"a plain swimmer {F(plain.Value)} m; dive {dived}, gravity turned off {gravityOff})");
            c.Check(!sneaked, "turned off: the swimmer does not sneak either (the normal game cancels crouching in water)");
            c.Check(camUnder == 0 && !viewOn && TurnedSurfaces() == 0,
                $"turned off: the camera never goes under water ({camUnder} of 150 ticks under), no tint, no turned surface "
                + $"({TurnedSurfaces()} upside down)");
            SelfTest.ReleaseInput();

            yield return GoHome(s);
            CheckLog(s);
        }
        finally
        {
            SelfTest.ReleaseInput();
            rig.Restore();
        }
        c.Report();
    }
}
#endif
