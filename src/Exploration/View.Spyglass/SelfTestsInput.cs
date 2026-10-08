#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Logging;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Exploration.ViewSpyglassMod;

// Debug build only. Me = more in-world self tests (TESTING.md items in brackets). Me drive spyglass like the game
// do: Player.SetControls (what PlayerController send every physics tick from mouse and keys) and
// Player.SetMouseLook, player's own controller off meanwhile. Settings forced only in memory (Scope.Test*,
// ScopeOverlay.Test*, ServerRules.Test*): never the config file.
//   spyglass.click         left click raises (about 0.6 s, camera slide and zoom never go back, own body hidden
//                          whenever the camera is in the head, arm to the eye), click lowers and walking works at
//                          once, Block lowers, a new click while it comes down raises it again from there, middle
//                          click nothing, Block blocks with the fists, never a punch [T03 T07 T15 T27]
//   spyglass.feet          auto-run stops on raise; back, left, right, run, jump, crouch, dodge do nothing while up;
//                          aim slowed by the zoom; body turns to the aim [T05]
//   spyglass.wheel         wheel steps x1.25 up to the cap and down to x1.5, view and aim follow, pick kept after
//                          lower and raise, wheel under a lower cap = new pick, vanilla camera distance kept [T06]
//   spyglass.zoom-pick     a lower zoom cap limits the view [T25]
//   spyglass.bug.zoom-pick-lost   (fail today) the cap gone again, the zoom the player had is back [T25]
//   spyglass.hold          HoldToLook: held = up, released = down, Block lowers, a tap never gets up [T08]
//   spyglass.lowers        lowers by itself: a hit (not burning), inventory, map, pause menu (game paused), R, a
//                          hotbar weapon [T09 T22]
//   spyglass.lowers-chat   typing in the chat lowers it [T09]
//   spyglass.aborts        a teleport and death (played with the dead flag) drop it at once [M07 T26]
//   spyglass.stagger       a stagger without a hit lowers it (when the stagger animation starts at all)
//   spyglass.cannot-raise  sitting (click stands up), equip animation, build mode: never up [T10]
//   spyglass.helm          at a ship's helm the click lets go of the helm, never up [T24]
//   spyglass.pending       rules pending: one message per 3 s, recipe hidden, works afterwards [M05]
internal static partial class SelfTests
{
    private const string ClickTest = "spyglass.click";
    private const string FeetTest = "spyglass.feet";
    private const string WheelTest = "spyglass.wheel";
    private const string ZoomPickTest = "spyglass.zoom-pick";
    private const string ZoomPickBugTest = "spyglass.bug.zoom-pick-lost";
    private const string HoldTest = "spyglass.hold";
    private const string LowersTest = "spyglass.lowers";
    private const string ChatTest = "spyglass.lowers-chat";
    private const string AbortsTest = "spyglass.aborts";
    private const string StaggerTest = "spyglass.stagger";
    private const string CannotTest = "spyglass.cannot-raise";
    private const string HelmTest = "spyglass.helm";
    private const string PendingTest = "spyglass.pending";

    // ---------- log tap ----------

    // Me listen to log from first activation on: warnings and errors of this mod (spyglass.log, server state of
    // multiplayer test), its info lines (the "Using the server's rules" line), error lines of anyone ("no error"
    // windows). Self test lines left out. Log events come from other threads too.
    private sealed class LogTap : ILogListener
    {
        private readonly object _lock = new object();
        private readonly List<string> _problems = new List<string>();
        private readonly List<string> _info = new List<string>();
        private readonly List<string> _errors = new List<string>();

        public void LogEvent(object sender, LogEventArgs e)
        {
            try
            {
                if (e == null || e.Data == null)
                {
                    return;
                }
                var level = e.Level;
                var bad = (level & (LogLevel.Error | LogLevel.Fatal)) != 0;
                var ours = e.Source != null && e.Source.SourceName == ModInfo.Name;
                if (!bad && !ours)
                {
                    return;
                }
                var text = e.Data.ToString();
                if (text.StartsWith(SelfTest.Prefix, StringComparison.Ordinal))
                {
                    return;
                }
                lock (_lock)
                {
                    if (bad)
                    {
                        _errors.Add($"[{level}: {(e.Source != null ? e.Source.SourceName : "?")}] {text}");
                    }
                    if (!ours)
                    {
                        return;
                    }
                    if (bad || (level & LogLevel.Warning) != 0)
                    {
                        _problems.Add($"[{level}] {text}");
                    }
                    else if ((level & (LogLevel.Info | LogLevel.Message)) != 0)
                    {
                        _info.Add(text);
                    }
                }
            }
            catch (Exception)
            {
                // Me never break the logger.
            }
        }

        public void Dispose()
        {
        }

        internal int InfoMark
        {
            get
            {
                lock (_lock)
                {
                    return _info.Count;
                }
            }
        }

        internal int ErrorMark
        {
            get
            {
                lock (_lock)
                {
                    return _errors.Count;
                }
            }
        }

        internal List<string> Problems()
        {
            lock (_lock)
            {
                return new List<string>(_problems);
            }
        }

        internal List<string> InfoSince(int mark)
        {
            lock (_lock)
            {
                return _info.GetRange(Mathf.Min(mark, _info.Count), Mathf.Max(0, _info.Count - mark));
            }
        }

        internal List<string> ErrorsSince(int mark)
        {
            lock (_lock)
            {
                return _errors.GetRange(Mathf.Min(mark, _errors.Count), Mathf.Max(0, _errors.Count - mark));
            }
        }
    }

    private static LogTap _tap;

    // First activation: me listen from now to end of session (also while feature off).
    private static void InstallTap()
    {
        if (_tap != null)
        {
            return;
        }
        _tap = new LogTap();
        BepInEx.Logging.Logger.Listeners.Add(_tap);
    }

    private static string First(List<string> lines, int max = 3)
    {
        var shown = lines.Count > max ? lines.GetRange(0, max) : lines;
        return string.Join(" | ", shown.ToArray());
    }

    // ---------- helpers ----------

    private static bool Ready(string name, out Player player, out GameCamera cam)
    {
        player = Player.m_localPlayer;
        cam = GameCamera.instance;
        if (player == null || cam == null || cam.m_camera == null || Hud.instance == null || ObjectDB.instance == null
            || ZNetScene.instance == null || !SpyglassContent.Built)
        {
            SelfTest.Fail(name, "no player, camera, HUD or spyglass item");
            return false;
        }
        return true;
    }

    // Me give spyglass, put it in right hand, switch player's own controller off. Null (and failed check) when not.
    private static ItemDrop.ItemData Hold(SpyRig rig, Checks c)
    {
        rig.TakeControls();
        var spy = rig.Give(SpyglassContent.ItemName);
        if (spy != null)
        {
            rig.Player.EquipItem(spy, false);
        }
        return c.Check(spy != null && rig.Player.GetRightItem() == spy, "spyglass given and in the right hand") ? spy : null;
    }

    // What PlayerController send every physics tick, by name.
    private static void Controls(Player p, Vector3 move = default, bool attack = false, bool attackHold = false,
        bool secondary = false, bool secondaryHold = false, bool block = false, bool blockHold = false,
        bool jump = false, bool crouch = false, bool run = false, bool autoRun = false, bool dodge = false)
    {
        p.SetControls(move, attack, attackHold, secondary, secondaryHold, block, blockHold, jump, crouch, run, autoRun, dodge);
    }

    private static IEnumerator FixedSteps(int n)
    {
        for (var i = 0; i < n; i++)
        {
            yield return new WaitForFixedUpdate();
        }
    }

    // Real time (pause menu stop game time).
    private static IEnumerator WaitReal(Func<bool> done, float timeout)
    {
        var end = Time.realtimeSinceStartup + timeout;
        while (!done() && Time.realtimeSinceStartup < end)
        {
            yield return null;
        }
    }

    private static IEnumerator Seconds(float seconds)
    {
        var end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            yield return null;
        }
    }

    // One click of Attack like PlayerController send it: pressed on one physics tick (edge and hold), still held on
    // next one, then released.
    private static IEnumerator Click(Player p)
    {
        yield return new WaitForFixedUpdate();
        Controls(p, attack: true, attackHold: true);
        yield return new WaitForFixedUpdate();
        Controls(p, attackHold: true);
        yield return new WaitForFixedUpdate();
        Controls(p);
    }

    // Me put spyglass up through test request (click path has own tests), in real time.
    private static IEnumerator RaiseUp(Checks c, Player player, string why)
    {
        yield return WaitReal(() => Scope.CanRaise(player), 4f);
        Scope.TestRequestRaise();
        yield return WaitReal(() => Scope.State == ScopeState.Raised, 3f);
        c.Check(Scope.State == ScopeState.Raised, $"{why}: spyglass up first ({Scope.State})");
    }

    private static IEnumerator LowerDown()
    {
        if (Scope.Engaged)
        {
            Scope.TestRequestLower();
        }
        yield return WaitReal(() => Scope.State == ScopeState.Idle, 3f);
        yield return null;
    }

    private static bool Flag(Player player) => player.m_nview.GetZDO().GetBool(Scope.RaisedKey);

    private static bool BodyFar(Player player) => player.m_lodGroup != null && player.m_lodGroup.localReferencePoint.x > 99999f;

    // Player killed by what the test did (game replace a dead player 10 s later): failed check that say after what,
    // and true = stop, nothing left to test on.
    private static bool Lost(Checks c, Player player, string after)
    {
        if (player != null && !player.IsDead() && ReferenceEquals(player, Player.m_localPlayer))
        {
            return false;
        }
        c.Check(false, $"the player died after {after}: the rest of the test has no player to look at");
        return true;
    }

    // Damage to deal so that `wanted` points get through whatever armour the test player wear (HitData.ApplyArmor:
    // armour under half the damage = taken off, else damage squared / 4 armour).
    private static float ThroughArmor(Player player, float wanted)
    {
        var armor = Mathf.Max(0f, player.GetBodyArmor());
        return armor < wanted ? wanted + armor : Mathf.Sqrt(4f * armor * wanted);
    }

    // What spyglass still hold on to ("" = all given back: state, round view, flag for other players, own body, fog,
    // message to Distant Horizons).
    private static string NotBack(Player player)
    {
        var bad = new List<string>();
        if (Scope.State != ScopeState.Idle)
        {
            bad.Add("state " + Scope.State);
        }
        if (ScopeOverlay.Shown)
        {
            bad.Add("round view shown");
        }
        if (Flag(player))
        {
            bad.Add("raised flag up");
        }
        if (ScopeCamera.BodyHidden)
        {
            bad.Add("body hidden");
        }
        if (ScopeCamera.FogApplied)
        {
            bad.Add("fog cleared");
        }
        var slot = ViewBoost.Peek();
        if (slot != null && slot[3] > 0.0)
        {
            bad.Add("far-view message on");
        }
        return string.Join(", ", bad.ToArray());
    }

    // Top-left message on screen now or waiting in queue.
    private static bool MessageSeen(string text)
    {
        var hud = MessageHud.instance;
        if (hud == null)
        {
            return false;
        }
        foreach (var m in hud.m_msgQeue)
        {
            if (m != null && m.m_text == text)
            {
                return true;
            }
        }
        return hud.m_messageText != null && hud.m_messageText.text == text;
    }

    // Me look at what is drawn, end of every frame: raise never go back, own body hidden on every frame camera is in
    // the head (closer than 0.45 m to eye) or within 2 m of feet (game's own rule), shown otherwise.
    private sealed class DrawWatch
    {
        internal int Frames;
        internal bool ProgressBack;
        internal bool SlideBack;
        internal bool ZoomBack;
        internal bool BodySeen;
        internal bool BodyGone;
        private float _progress = -1f;
        private float _gap = float.MaxValue;
        private float _fov = float.MaxValue;

        internal void Sample(Player player, GameCamera cam, bool rising)
        {
            var camPos = cam.transform.position;
            var gap = (camPos - player.m_eye.position).magnitude;
            var feet = (camPos - player.transform.position).magnitude;
            var fov = cam.m_camera.fieldOfView;
            var progress = Scope.Progress;
            if (Scope.Active)
            {
                var far = BodyFar(player);
                if ((gap < 0.44f || feet < 1.99f) && !far)
                {
                    BodySeen = true;
                }
                if (gap > 0.46f && feet > 2.01f && far)
                {
                    BodyGone = true;
                }
            }
            if (rising)
            {
                ProgressBack |= progress < _progress - 0.0001f;
                SlideBack |= gap > _gap + 0.03f;
                ZoomBack |= fov > _fov + 0.01f;
                _progress = progress;
                _gap = gap;
                _fov = fov;
            }
            Frames++;
        }
    }

    // ---------- spyglass.click ----------

    private static IEnumerator RunClick()
    {
        var c = new Checks(ClickTest);
        if (!Ready(ClickTest, out var player, out var cam))
        {
            yield break;
        }
        var rig = new SpyRig(player, ClickTest);
        try
        {
            rig.KeepPlace();
            rig.Noon();
            var spy = Hold(rig, c);
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            rig.Look(OpenYaw(player, out _), -1f);
            Scope.TestSetMagnification(4f);
            yield return new WaitForSeconds(1f);
            var vanillaFov = cam.m_camera.fieldOfView;

            // Middle click (secondary attack): nothing.
            yield return new WaitForFixedUpdate();
            Controls(player, secondary: true, secondaryHold: true);
            yield return FixedSteps(2);
            Controls(player);
            yield return Frames(3);
            c.Check(Scope.State == ScopeState.Idle && !player.InAttack() && player.m_queuedSecondAttackTimer <= 0f
                    && player.m_queuedAttackTimer <= 0f, $"middle click: no raise, no punch ({Scope.State})");

            // Block: the fists block, like with any tool in hand.
            Controls(player, block: true, blockHold: true);
            yield return FixedSteps(3);
            c.Check(player.IsBlocking() && Scope.State == ScopeState.Idle, "Block blocks with the fists while the spyglass is down");
            player.m_blocking = false;
            Controls(player);
            yield return FixedSteps(2);

            // Left click: up. Me watch every drawn frame until it at the eye.
            var watch = new DrawWatch();
            yield return new WaitForFixedUpdate();
            Controls(player, attack: true, attackHold: true);
            var t0 = Time.unscaledTime;
            var released = false;
            while (Scope.State != ScopeState.Raised && Time.unscaledTime - t0 < 3f)
            {
                yield return new WaitForEndOfFrame();
                if (!released && Time.unscaledTime - t0 > 0.05f)
                {
                    Controls(player);
                    released = true;
                }
                watch.Sample(player, cam, true);
            }
            Controls(player);
            var took = Time.unscaledTime - t0;
            c.Check(Scope.State == ScopeState.Raised, $"left click raises it ({Scope.State})");
            c.Check(took > Scope.RaiseSeconds - 0.15f && took < Scope.RaiseSeconds + 0.35f,
                $"up in about {F(Scope.RaiseSeconds)} s (took {F(took)} s, {watch.Frames} frames)");
            c.Check(!watch.ProgressBack && !watch.SlideBack && !watch.ZoomBack,
                $"camera slides to the eye and zooms in without going back (progress {!watch.ProgressBack}, slide {!watch.SlideBack}, zoom {!watch.ZoomBack})");
            c.Check(!watch.BodySeen, "own body hidden on every frame the camera is in the head or at the feet");
            c.Check(!player.InAttack() && player.m_queuedAttackTimer <= 0f, "the click never punches");
            c.Check(Flag(player), "raised flag up for other players");
            yield return new WaitForSeconds(0.3f);
            yield return new WaitForEndOfFrame();
            var gap = (cam.transform.position - player.m_eye.position).magnitude;
            var armUp = ArmPose.EyepieceGap(player);
            c.Check(gap < 0.05f && Near(cam.m_camera.fieldOfView, ScopeCamera.ZoomedFov(vanillaFov, 4f), 0.05f),
                $"camera at the eye ({F(gap)} m), zoomed x4 (fov {F(cam.m_camera.fieldOfView)})");
            // Arm itself: spyglass.pose check it from outside (here own body is not drawn).
            c.Note($"eyepiece {F(armUp)} m from the eyes while up");
            c.Check(ScopeOverlay.Shown && Near(ScopeOverlay.TestDark.y, 1f, 0.001f), "round view closed in");

            // Left click again: down, and walking works at once.
            var from = player.transform.position;
            yield return new WaitForFixedUpdate();
            Controls(player, attack: true, attackHold: true);
            yield return null;
            yield return null;
            c.Check(!Scope.Engaged && !Flag(player), $"left click lowers it ({Scope.State}), flag down");
            var lowerStart = Time.unscaledTime;
            while (Scope.State == ScopeState.Lowering && Time.unscaledTime - lowerStart < 2f)
            {
                rig.Drive(Vector3.forward);
                yield return new WaitForFixedUpdate();
            }
            var walked = (player.transform.position - from).magnitude;
            var lowerTook = Time.unscaledTime - lowerStart;
            rig.Drive(Vector3.zero);
            c.Check(walked > 0.1f, $"walking works while it comes down (walked {F(walked)} m in {F(lowerTook)} s)");
            yield return Frames(3);
            yield return new WaitForSeconds(0.4f);
            yield return new WaitForEndOfFrame();
            gap = (cam.transform.position - player.m_eye.position).magnitude;
            var armDown = ArmPose.EyepieceGap(player);
            c.Check(NotBack(player).Length == 0, "down: " + (NotBack(player).Length == 0 ? "everything given back" : NotBack(player)));
            c.Check(Near(cam.m_camera.fieldOfView, vanillaFov, 0.01f) && gap > 0.5f,
                $"camera back in third person ({F(gap)} m from the eye, fov {F(cam.m_camera.fieldOfView)})");
            c.Note($"eyepiece {F(armDown)} m from the eyes once down");
            c.Check(Hud.instance.m_crosshair.enabled, "crosshair back");

            // Up again, Block: down.
            yield return Click(player);
            yield return WaitReal(() => Scope.State == ScopeState.Raised, 3f);
            c.Check(Scope.State == ScopeState.Raised, "click raises it again");
            yield return new WaitForFixedUpdate();
            Controls(player, block: true, blockHold: true);
            yield return null;
            yield return null;
            c.Check(Scope.State == ScopeState.Lowering || Scope.State == ScopeState.Idle, $"Block lowers it ({Scope.State})");
            player.m_blocking = false;
            Controls(player);
            yield return WaitReal(() => Scope.State == ScopeState.Idle, 3f);
            c.Check(Scope.State == ScopeState.Idle && NotBack(player).Length == 0,
                "down after Block: " + (NotBack(player).Length == 0 ? "everything given back" : NotBack(player)));
            yield return Frames(3);
            yield return new WaitForEndOfFrame();
            gap = (cam.transform.position - player.m_eye.position).magnitude;
            c.Check(Near(cam.m_camera.fieldOfView, vanillaFov, 0.01f) && gap > 0.5f && Hud.instance.m_crosshair.enabled,
                $"after Block too: camera back in third person ({F(gap)} m from the eye, fov {F(cam.m_camera.fieldOfView)}), crosshair back");

            // A new click while it comes down: up again from where it is.
            yield return Click(player);
            yield return WaitReal(() => Scope.State == ScopeState.Raised, 3f);
            yield return new WaitForFixedUpdate();
            Controls(player, attack: true, attackHold: true);
            yield return WaitReal(() => Scope.State == ScopeState.Lowering && Scope.Progress < 0.8f, 1f);
            Controls(player);
            var half = Scope.Progress;
            if (c.Check(Scope.State == ScopeState.Lowering && half > 0.1f, $"caught it half way down ({Scope.State}, {F(half)})"))
            {
                Controls(player, attack: true, attackHold: true);
                var again = Time.unscaledTime;
                var low = half;
                var idle = false;
                var rising = new DrawWatch();
                var last = half;
                var jump = 0f;
                while (Scope.State != ScopeState.Raised && Time.unscaledTime - again < 3f)
                {
                    yield return new WaitForEndOfFrame();
                    idle |= Scope.State == ScopeState.Idle;
                    low = Mathf.Min(low, Scope.Progress);
                    // One frame never lift it more than that frame's share of the raise time.
                    jump = Mathf.Max(jump, Scope.Progress - last - Time.unscaledDeltaTime / Scope.RaiseSeconds);
                    last = Scope.Progress;
                    if (Scope.State == ScopeState.Raising)
                    {
                        rising.Sample(player, cam, true);
                    }
                    if (Time.unscaledTime - again > 0.05f)
                    {
                        Controls(player);
                    }
                }
                Controls(player);
                var back = Time.unscaledTime - again;
                c.Check(Scope.State == ScopeState.Raised && !idle && low > 0.05f,
                    $"click while it comes down raises it again from there (lowest {F(low)}, never fully down {!idle})");
                c.Check(!rising.ProgressBack && !rising.SlideBack && !rising.ZoomBack && !rising.BodySeen,
                    $"no jump on the way back up: camera and zoom only move toward the eye ({rising.Frames} frames)");
                c.Check(jump < 0.03f, $"and it never skips ahead: each frame lifts it by that frame's time only (largest extra step {F(jump)})");
                c.Check(back < Scope.RaiseSeconds * (1f - half) + 0.3f, $"back up in {F(back)} s from {F(half)}");
                c.Check(Flag(player) && ScopeOverlay.Shown, "raised flag and round view up again");
                // And the next click lowers it as usual.
                yield return Click(player);
                yield return WaitReal(() => Scope.State == ScopeState.Idle, 3f);
                c.Check(Scope.State == ScopeState.Idle && NotBack(player).Length == 0, $"the next click lowers it as usual ({Scope.State})");
            }
            yield return LowerDown();
            c.Check(Scope.State == ScopeState.Idle, "lowered at the end");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- spyglass.feet ----------

    private static IEnumerator RunFeet()
    {
        var c = new Checks(FeetTest);
        if (!Ready(FeetTest, out var player, out var cam))
        {
            yield break;
        }
        var rig = new SpyRig(player, FeetTest);
        try
        {
            rig.KeepPlace();
            rig.Noon();
            var spy = Hold(rig, c);
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            var yaw = OpenYaw(player, out _);
            rig.Look(yaw, 0f);
            Scope.TestSetMagnification(4f);
            Scope.TestAimSensitivity = 1f;
            yield return new WaitForSeconds(0.5f);

            // Auto-run, then the click.
            var start = player.transform.position;
            yield return new WaitForFixedUpdate();
            Controls(player, autoRun: true);
            yield return new WaitForFixedUpdate();
            Controls(player);
            var on = player.m_autoRun;
            yield return new WaitForSeconds(0.7f);
            var ran = (player.transform.position - start).magnitude;
            c.Check(on && ran > 0.5f, $"auto-run on before the raise (ran {F(ran)} m)");
            yield return Click(player);
            yield return null;
            c.Check(!player.m_autoRun, "auto-run stops when the spyglass goes up");
            yield return WaitReal(() => Scope.State == ScopeState.Raised, 3f);
            c.Check(Scope.State == ScopeState.Raised, "raised by the click");
            yield return new WaitForSeconds(0.3f);
            var stand = player.transform.position;
            var until = Time.time + 1f;
            while (Time.time < until)
            {
                Controls(player);
                yield return new WaitForFixedUpdate();
            }
            var drift = (player.transform.position - stand).magnitude;
            c.Check(drift < 0.05f, $"standing still after the raise (moved {F(drift)} m)");

            // Every way of moving, like keys send it: nothing while spyglass up.
            var names = new[] { "W", "S", "A", "D", "W + Shift (run)", "Space (jump)", "Ctrl (crouch)", "dodge key" };
            for (var i = 0; i < names.Length; i++)
            {
                var at = player.transform.position;
                var crouched = false;
                var dodged = false;
                var running = false;
                var lifted = 0f;
                var first = true;
                until = Time.time + 0.6f;
                while (Time.time < until)
                {
                    var move = i == 0 || i == 4 ? Vector3.forward : i == 1 ? Vector3.back : i == 2 ? Vector3.left : i == 3 ? Vector3.right : Vector3.zero;
                    Controls(player, move, run: i == 4, jump: first && i == 5, crouch: first && i == 6, dodge: first && i == 7);
                    first = false;
                    yield return new WaitForFixedUpdate();
                    crouched |= player.IsCrouching();
                    dodged |= player.InDodge();
                    running |= player.m_run;
                    lifted = Mathf.Max(lifted, Mathf.Abs(player.transform.position.y - at.y));
                }
                Controls(player);
                var moved = (player.transform.position - at).magnitude;
                c.Check(moved < 0.05f && lifted < 0.1f && !crouched && !dodged && !running && Scope.State == ScopeState.Raised,
                    $"{names[i]}: nothing (moved {F(moved)} m, up {F(lifted)} m, crouch {crouched}, dodge {dodged}, run {running}, {Scope.State})");
            }

            // Mouse: slower by zoom (same speed on screen as without zoom), body turn to the aim.
            yield return Frames(3);
            var yaw0 = player.m_lookYaw.eulerAngles.y;
            player.SetMouseLook(new Vector2(160f, 0f));
            var turned = Mathf.DeltaAngle(yaw0, player.m_lookYaw.eulerAngles.y);
            c.Check(Near(turned, 160f / 4f, 0.5f), $"mouse turns the view by a quarter at x4 (160 in, {F(turned)} deg)");
            var pitch0 = player.m_lookPitch;
            player.SetMouseLook(new Vector2(0f, 40f));
            c.Check(Near(pitch0 - player.m_lookPitch, 40f / 4f, 0.5f), $"up and down slowed the same ({F(pitch0 - player.m_lookPitch)} deg)");
            yield return new WaitForSeconds(1f);
            var off = Mathf.Abs(Mathf.DeltaAngle(player.transform.eulerAngles.y, player.m_lookYaw.eulerAngles.y));
            c.Check(off < 5f, $"body turned to face the aim ({F(off)} deg off)");
            c.Check((player.transform.position - stand).magnitude < 0.1f, "still on the same spot");

            yield return LowerDown();
            c.Check(Scope.State == ScopeState.Idle, "lowered at the end");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- spyglass.wheel ----------

    private static IEnumerator Notch(float direction)
    {
        Scope.TestScroll = direction;
        yield return null;
        yield return null;
    }

    private static IEnumerator RunWheel()
    {
        var c = new Checks(WheelTest);
        if (!Ready(WheelTest, out var player, out var cam))
        {
            yield break;
        }
        var rig = new SpyRig(player, WheelTest);
        var distance = cam.m_distance;
        try
        {
            rig.Noon();
            var spy = Hold(rig, c);
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            // Default rules and aim speed, whatever player's settings say.
            ServerRules.TestRules = new SpyglassRules();
            Scope.TestAimSensitivity = 1f;
            rig.Look(OpenYaw(player, out _), -1f);
            yield return new WaitForSeconds(0.5f);
            var vanillaFov = cam.m_camera.fieldOfView;
            Scope.TestSetMagnification(4f);
            yield return RaiseUp(c, player, "wheel");
            yield return Frames(5);
            c.Check(Near(ScopeCamera.CurrentMagnification, 4f, 0.01f), $"starts at x4 ({F(ScopeCamera.CurrentMagnification)})");

            // Up, notch by notch, to the cap.
            var steps = new List<string> { "4" };
            var ok = true;
            var zoom = 4f;
            for (var i = 0; i < 8; i++)
            {
                yield return Notch(1f);
                var want = Mathf.Min(8f, zoom * 1.25f);
                ok &= Near(Scope.Magnification, want, 0.002f);
                zoom = Scope.Magnification;
                steps.Add(F(zoom));
            }
            c.Check(ok && Near(zoom, 8f, 0.001f), $"wheel up: steps of x1.25 to the cap x8 ({string.Join(" ", steps.ToArray())})");
            yield return Frames(5);
            yield return new WaitForEndOfFrame();
            c.Check(Near(ScopeCamera.CurrentMagnification, 8f, 0.01f) && Near(cam.m_camera.fieldOfView, ScopeCamera.ZoomedFov(vanillaFov, 8f), 0.05f),
                $"the view follows: x8, fov {F(cam.m_camera.fieldOfView)}");
            var yaw0 = player.m_lookYaw.eulerAngles.y;
            player.SetMouseLook(new Vector2(80f, 0f));
            var turned = Mathf.DeltaAngle(yaw0, player.m_lookYaw.eulerAngles.y);
            player.SetMouseLook(new Vector2(-80f, 0f));
            c.Check(Near(turned, 80f / 8f, 0.3f), $"aim follows the zoom: 80 in turns {F(turned)} deg at x8");

            // Down to x1.5.
            steps.Clear();
            steps.Add("8");
            ok = true;
            for (var i = 0; i < 12; i++)
            {
                yield return Notch(-1f);
                var want = Mathf.Max(Scope.MinMagnification, zoom / 1.25f);
                ok &= Near(Scope.Magnification, want, 0.002f);
                zoom = Scope.Magnification;
                steps.Add(F(zoom));
            }
            c.Check(ok && Near(zoom, Scope.MinMagnification, 0.001f), $"wheel down: steps to x1.5 ({string.Join(" ", steps.ToArray())})");
            yield return Frames(5);
            yaw0 = player.m_lookYaw.eulerAngles.y;
            player.SetMouseLook(new Vector2(30f, 0f));
            turned = Mathf.DeltaAngle(yaw0, player.m_lookYaw.eulerAngles.y);
            player.SetMouseLook(new Vector2(-30f, 0f));
            c.Check(Near(turned, 30f / 1.5f, 0.3f), $"aim follows the zoom: 30 in turns {F(turned)} deg at x1.5");

            // Vanilla wheel zoom (camera distance) undone while spyglass up.
            ScopeCamera.TestWheelNudge = -1.5f;
            yield return Frames(3);
            c.Check(Near(cam.m_distance, distance, 0.001f), $"camera distance kept while up ({F(cam.m_distance)}, was {F(distance)})");

            // Pick kept after lowering and raising.
            yield return Notch(1f);
            yield return Notch(1f);
            var pick = Scope.Magnification;
            c.Check(Near(pick, Scope.MinMagnification * 1.25f * 1.25f, 0.002f), $"picked x{F(pick)}");
            yield return LowerDown();
            yield return Frames(3);
            c.Check(Scope.State == ScopeState.Idle && Near(cam.m_distance, distance, 0.001f),
                $"lowered: camera distance as before ({F(cam.m_distance)}, was {F(distance)})");
            c.Check(Near(Scope.Magnification, pick, 0.001f), $"pick kept while down (x{F(Scope.Magnification)})");
            yield return RaiseUp(c, player, "second raise");
            yield return Frames(5);
            c.Check(Near(ScopeCamera.CurrentMagnification, pick, 0.01f), $"raised again at the picked zoom (x{F(ScopeCamera.CurrentMagnification)})");

            // Wheel used under lower cap: what it reached = new pick, also when cap gone.
            Scope.TestSetMagnification(6f);
            ServerRules.TestRules = new SpyglassRules { MaxMagnification = 3f };
            yield return Frames(3);
            c.Check(Near(Scope.Magnification, 3f, 0.001f), $"cap x3: shown x{F(Scope.Magnification)}");
            yield return Notch(1f);
            c.Check(Near(Scope.Magnification, 3f, 0.001f), "wheel up at the cap stays at x3");
            yield return Notch(-1f);
            c.Check(Near(Scope.Magnification, 3f / 1.25f, 0.002f), $"wheel down from the cap: x{F(Scope.Magnification)}");
            ServerRules.TestRules = new SpyglassRules();
            yield return Frames(3);
            c.Check(Near(Scope.Magnification, 3f / 1.25f, 0.002f), $"cap gone: the wheel's value stays (x{F(Scope.Magnification)})");
            yield return LowerDown();

            // Spyglass down: vanilla wheel zoom work as always (and prove the stand-in move the camera).
            ScopeCamera.TestWheelNudge = -0.5f;
            yield return Frames(3);
            var want2 = Mathf.Clamp(distance - 0.5f, cam.m_minDistance, cam.m_maxDistance);
            c.Check(Near(cam.m_distance, want2, 0.001f), $"down: the camera wheel zoom works ({F(cam.m_distance)}, want {F(want2)})");
            c.Report();
        }
        finally
        {
            cam.m_distance = distance;
            rig.Restore();
        }
    }

    // ---------- spyglass.zoom-pick ----------

    // Start zoom 4, wheel never used, cap 3: raised (and left up, some frames in).
    private static IEnumerator UnderLowCap(Checks c, Player player)
    {
        Scope.TestStartMagnification = 4f;
        Scope.TestRawMagnification = -1f;
        ServerRules.TestRules = new SpyglassRules { MaxMagnification = 3f };
        yield return RaiseUp(c, player, "cap x3");
        yield return Frames(10);
    }

    // First half of T25: a lower cap limit the view. (What become of the pick afterwards: test below.)
    private static IEnumerator RunZoomPick()
    {
        var c = new Checks(ZoomPickTest);
        if (!Ready(ZoomPickTest, out var player, out var cam))
        {
            yield break;
        }
        var rig = new SpyRig(player, ZoomPickTest);
        try
        {
            var spy = Hold(rig, c);
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            // Zoom of this game without cap first (StartMagnification 4, default rules): x4.
            Scope.TestStartMagnification = 4f;
            Scope.TestRawMagnification = -1f;
            ServerRules.TestRules = new SpyglassRules();
            c.Check(Near(Scope.Magnification, 4f, 0.001f), $"StartMagnification 4, no cap in the way: x4 (x{F(Scope.Magnification)})");
            rig.Look(OpenYaw(player, out _), -1f);
            yield return new WaitForSeconds(0.3f);
            var vanillaFov = cam.m_camera.fieldOfView;
            // StartMagnification 4, wheel never used, cap 3.
            yield return UnderLowCap(c, player);
            yield return new WaitForEndOfFrame();
            c.Check(Near(ScopeCamera.CurrentMagnification, 3f, 0.01f) && Near(cam.m_camera.fieldOfView, ScopeCamera.ZoomedFov(vanillaFov, 3f), 0.05f),
                $"under the cap the view shows x3 (x{F(ScopeCamera.CurrentMagnification)}, fov {F(cam.m_camera.fieldOfView)})");
            yield return LowerDown();
            c.Check(Scope.State == ScopeState.Idle, "lowered at the end");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- spyglass.bug.zoom-pick-lost ----------

    // Known to fail today, alone here so spyglass.zoom-pick pass: Scope.UpdateZoom write the capped zoom back into
    // the pick every frame the spyglass is up, also with no wheel input, so one raise under a lower cap replace the
    // zoom the player had (README: the zoom you pick is kept until you quit the game).
    private static IEnumerator RunZoomPickBug()
    {
        var c = new Checks(ZoomPickBugTest);
        if (!Ready(ZoomPickBugTest, out var player, out _))
        {
            yield break;
        }
        var rig = new SpyRig(player, ZoomPickBugTest);
        try
        {
            var spy = Hold(rig, c);
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            yield return UnderLowCap(c, player);
            var under = ScopeCamera.CurrentMagnification;
            yield return LowerDown();
            ServerRules.TestRules = new SpyglassRules();
            yield return Frames(2);
            var down = Scope.Magnification;
            yield return RaiseUp(c, player, "cap x8");
            yield return Frames(10);
            c.Check(Near(down, 4f, 0.001f) && Near(ScopeCamera.CurrentMagnification, 4f, 0.01f),
                $"cap back to x8, wheel never used: x4 again (x{F(under)} under the cap, x{F(down)} once the cap is gone, raised again at x{F(ScopeCamera.CurrentMagnification)})");
            yield return LowerDown();
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- spyglass.hold ----------

    // Attack held for a while, like PlayerController send it (edge on first tick only). Stop early when stop say so.
    private static IEnumerator HoldAttack(Player player, float seconds, bool edge, Func<bool> stop = null)
    {
        var until = Time.time + seconds;
        var first = edge;
        while (Time.time < until && (stop == null || !stop()))
        {
            Controls(player, attack: first, attackHold: true);
            first = false;
            yield return new WaitForFixedUpdate();
        }
    }

    private static IEnumerator RunHold()
    {
        var c = new Checks(HoldTest);
        if (!Ready(HoldTest, out var player, out _))
        {
            yield break;
        }
        var rig = new SpyRig(player, HoldTest);
        try
        {
            var spy = Hold(rig, c);
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            Scope.TestHoldToLook = true;
            yield return new WaitForSeconds(0.3f);

            // Held: up, and it stay up.
            yield return new WaitForFixedUpdate();
            yield return HoldAttack(player, 2f, true, () => Scope.State == ScopeState.Raised);
            c.Check(Scope.State == ScopeState.Raised, $"Attack held: up ({Scope.State})");
            var stayed = true;
            var until = Time.time + 1f;
            while (Time.time < until)
            {
                Controls(player, attackHold: true);
                yield return new WaitForFixedUpdate();
                stayed &= Scope.State == ScopeState.Raised;
            }
            c.Check(stayed, "stays up as long as Attack is held");

            // Released: down.
            Controls(player);
            yield return null;
            yield return null;
            c.Check(!Scope.Engaged, $"Attack released: comes down ({Scope.State})");
            yield return WaitReal(() => Scope.State == ScopeState.Idle, 3f);
            c.Check(Scope.State == ScopeState.Idle, "down after the release");

            // Held again, Block: down, and it stay down while Attack still held.
            yield return new WaitForFixedUpdate();
            yield return HoldAttack(player, 2f, true, () => Scope.State == ScopeState.Raised);
            c.Check(Scope.State == ScopeState.Raised, "Attack held again: up");
            Controls(player, attackHold: true, block: true, blockHold: true);
            yield return null;
            yield return null;
            c.Check(Scope.State == ScopeState.Lowering || Scope.State == ScopeState.Idle, $"Block lowers it in hold mode too ({Scope.State})");
            player.m_blocking = false;
            var up = false;
            until = Time.time + 1f;
            while (Time.time < until)
            {
                Controls(player, attackHold: true);
                yield return new WaitForFixedUpdate();
                up |= Scope.Engaged;
            }
            c.Check(!up && Scope.State == ScopeState.Idle, "stays down after Block while Attack is still held");
            Controls(player);
            yield return FixedSteps(2);

            // A tap: never get to the eye.
            yield return new WaitForFixedUpdate();
            Controls(player, attack: true, attackHold: true);
            yield return new WaitForFixedUpdate();
            Controls(player);
            var top = 0f;
            var raised = false;
            var tap = Time.unscaledTime;
            while (Time.unscaledTime - tap < 1.5f)
            {
                yield return null;
                top = Mathf.Max(top, Scope.Progress);
                raised |= Scope.State == ScopeState.Raised;
            }
            c.Check(!raised && top < 0.5f && Scope.State == ScopeState.Idle,
                $"a tap comes down at once (highest {F(top)}, {Scope.State})");

            // Click mode again: click toggle.
            Scope.TestHoldToLook = false;
            yield return Click(player);
            yield return WaitReal(() => Scope.State == ScopeState.Raised, 3f);
            yield return new WaitForSeconds(0.5f);
            c.Check(Scope.State == ScopeState.Raised, "HoldToLook off again: a click raises it and it stays up");
            yield return LowerDown();
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- spyglass.lowers ----------

    private static IEnumerator RunLowers()
    {
        var c = new Checks(LowersTest);
        if (!Ready(LowersTest, out var player, out var cam))
        {
            yield break;
        }
        var rig = new SpyRig(player, LowersTest);
        try
        {
            rig.Noon();
            var spy = Hold(rig, c);
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            Scope.TestSetMagnification(4f);
            yield return new WaitForSeconds(0.3f);

            // (a) A hit, god mode off like a normal player. Run keep god mode on, and there a hurt player is kept at
            // 1 health (Character.ApplyDamage), with no food to heal: earlier tests may leave almost none. Full
            // health first, so the 3 point hit and the short fire never kill (a dead player is replaced 10 s later:
            // nothing left to test on). God mode off only around the hit itself (damage land inside the call), so
            // nothing else of the world kill the player meanwhile.
            var god = player.InGodMode();
            var health = player.GetHealth();
            var burning = player.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectBurning);
            rig.OnRestore("god mode, health, fire", () =>
            {
                if (!burning)
                {
                    player.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectBurning, true);
                }
                player.SetHealth(health);
                player.SetGodMode(god);
            });
            var full = player.GetMaxHealth();
            c.Note($"health {F(health)} of {F(full)} before the test (god mode {god}): set to full for the hit and the fire");
            if (!c.Check(full >= 20f, $"max health {F(full)} leaves room for a 3 point hit and a short fire"))
            {
                c.Report();
                yield break;
            }
            player.SetHealth(full);
            yield return RaiseUp(c, player, "hit");
            var hit = new HitData { m_point = player.GetCenterPoint(), m_dir = -player.transform.forward, m_hitType = HitData.HitType.EnemyHit };
            hit.m_damage.m_blunt = ThroughArmor(player, 3f);
            player.SetGodMode(false);
            player.Damage(hit);
            var unprotected = !player.InGodMode();
            player.SetGodMode(god);
            yield return null;
            yield return null;
            c.Check(unprotected, "god mode was off for the hit");
            if (Lost(c, player, $"a 3 point hit at health {F(full)}"))
            {
                c.Report();
                yield break;
            }
            c.Check(player.GetHealth() < full - 0.5f, $"the hit hurt (health {F(player.GetHealth())}, was {F(full)})");
            c.Check(!Scope.Engaged, $"a hit lowers it ({Scope.State})");
            yield return WaitReal(() => Scope.State == ScopeState.Idle, 3f);
            c.Check(NotBack(player).Length == 0, "down after the hit" + (NotBack(player).Length == 0 ? "" : ": " + NotBack(player)));

            // (b) Fire: burning that follow never lower it. (God mode as the run has it: burning hurt all the same,
            // game only stop health at 1.)
            yield return RaiseUp(c, player, "fire");
            var before = player.GetHealth();
            var fire = new HitData { m_point = player.GetCenterPoint(), m_dir = Vector3.up, m_hitType = HitData.HitType.EnemyHit };
            fire.m_damage.m_fire = ThroughArmor(player, 10f);
            player.Damage(fire);
            var burned = false;
            var stayed = true;
            var until = Time.time + 2.6f;
            while (Time.time < until && !player.IsDead())
            {
                yield return null;
                burned |= player.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectBurning);
                stayed &= Scope.State == ScopeState.Raised;
            }
            if (Lost(c, player, $"10 points of fire at health {F(before)}"))
            {
                c.Report();
                yield break;
            }
            c.Check(burned && player.GetHealth() < before - 0.5f,
                $"on fire and burning (burning {burned}, health {F(player.GetHealth())}, was {F(before)})");
            c.Check(stayed, "burning does not lower it");
            player.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectBurning, true);
            player.SetHealth(health);
            yield return LowerDown();

            // (c) Inventory.
            yield return RaiseUp(c, player, "inventory");
            rig.OnRestore("inventory", () =>
            {
                if (InventoryGui.instance != null && InventoryGui.IsVisible())
                {
                    InventoryGui.instance.Hide();
                }
            });
            InventoryGui.instance.Show(null);
            yield return null;
            yield return null;
            c.Check(Scope.State == ScopeState.Lowering || Scope.State == ScopeState.Idle, $"opening the inventory lowers it ({Scope.State})");
            yield return WaitReal(() => Scope.State == ScopeState.Idle, 3f);
            c.Check(NotBack(player).Length == 0, "down behind the inventory");
            InventoryGui.instance.Hide();
            yield return WaitReal(() => !InventoryGui.IsVisible(), 3f);

            // (c) Map.
            var map = Minimap.instance;
            if (c.Check(map != null && !Game.m_noMap, "this world has a map"))
            {
                var mode = map.m_mode;
                rig.OnRestore("map", () => map.SetMapMode(mode));
                yield return RaiseUp(c, player, "map");
                map.SetMapMode(Minimap.MapMode.Large);
                yield return null;
                yield return null;
                c.Check(Minimap.IsOpen() && (Scope.State == ScopeState.Lowering || Scope.State == ScopeState.Idle),
                    $"opening the map lowers it ({Scope.State})");
                yield return WaitReal(() => Scope.State == ScopeState.Idle, 3f);
                c.Check(NotBack(player).Length == 0, "down behind the map");
                map.SetMapMode(mode);
                yield return WaitReal(() => !Minimap.IsOpen(), 3f);
            }

            // (c) Pause menu: game stop, spyglass still come down behind it (real time).
            var menu = Menu.instance;
            if (c.Check(menu != null, "pause menu exists"))
            {
                yield return RaiseUp(c, player, "menu");
                rig.OnRestore("menu", () =>
                {
                    if (Menu.IsVisible())
                    {
                        menu.Hide();
                    }
                });
                menu.Show();
                yield return Seconds(0.3f);
                var paused = Game.IsPaused() && Time.timeScale == 0f;
                yield return WaitReal(() => Scope.State == ScopeState.Idle, 2f);
                c.Check(Scope.State == ScopeState.Idle && !ScopeOverlay.Shown, $"Esc lowers it behind the menu ({Scope.State})");
                c.Check(paused, $"while the game is paused (paused {Game.IsPaused()}, time scale {F(Time.timeScale)})");
                c.Check(NotBack(player).Length == 0, "everything given back behind the paused menu" + (NotBack(player).Length == 0 ? "" : ": " + NotBack(player)));
                menu.Hide();
                yield return WaitReal(() => !Menu.IsVisible() && Time.timeScale > 0f, 3f);
                c.Check(!Menu.IsVisible() && Time.timeScale > 0f, "menu closed, game running again");
            }

            // (d) R (hide key): at once, spyglass put away.
            yield return RaiseUp(c, player, "R");
            player.HideHandItems();
            yield return null;
            yield return null;
            c.Check(Scope.State == ScopeState.Idle && !ScopeOverlay.Shown && !Flag(player), $"R: view back at once ({Scope.State})");
            c.Check(player.GetRightItem() == null && player.m_hiddenRightItem == spy, "R: spyglass put away");
            yield return new WaitForSeconds(0.3f);
            player.ShowHandItems();
            yield return Frames(3);
            c.Check(player.GetRightItem() == spy && Scope.State == ScopeState.Idle, "R again: back in hand, still down");

            // (e) Another weapon from the hotbar.
            var sword = rig.Give("SwordIron");
            if (c.Check(sword != null, "SwordIron given"))
            {
                yield return RaiseUp(c, player, "hotbar weapon");
                if (sword.m_gridPos.y == 0)
                {
                    player.UseHotbarItem(sword.m_gridPos.x + 1);
                }
                else
                {
                    c.Note("the sword is not in the hotbar row: used from the inventory (the same equip call)");
                    player.UseItem(null, sword, false);
                }
                yield return WaitReal(() => player.GetRightItem() == sword, 4f);
                yield return null;
                yield return null;
                c.Check(player.GetRightItem() == sword, "the hotbar weapon is in hand");
                c.Check(Scope.State == ScopeState.Idle && !ScopeOverlay.Shown && !Flag(player),
                    $"another weapon from the hotbar: view back at once ({Scope.State})");
                yield return Frames(3);
                c.Check(Near(cam.m_camera.fieldOfView, cam.m_fov, 0.01f), "camera zoom back");
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- spyglass.lowers-chat ----------

    // Own test: it need the cursor in the chat box, this run may not manage that (then it fail and say so).
    private static IEnumerator RunChat()
    {
        var c = new Checks(ChatTest);
        if (!Ready(ChatTest, out var player, out _))
        {
            yield break;
        }
        var rig = new SpyRig(player, ChatTest);
        try
        {
            var spy = Hold(rig, c);
            var chat = Chat.instance;
            if (spy == null || !c.Check(chat != null && chat.m_input != null && chat.m_chatWindow != null, "chat box exists"))
            {
                c.Report();
                yield break;
            }
            yield return RaiseUp(c, player, "chat");
            rig.OnRestore("chat", () =>
            {
                var events = UnityEngine.EventSystems.EventSystem.current;
                if (events != null && events.currentSelectedGameObject == chat.m_input.gameObject)
                {
                    events.SetSelectedGameObject(null);
                }
                chat.m_input.DeactivateInputField();
                chat.m_input.gameObject.SetActive(false);
                chat.Hide();
            });
            // What Enter key do (Chat.Update).
            chat.m_hideTimer = 0f;
            chat.m_chatWindow.gameObject.SetActive(true);
            chat.m_input.gameObject.SetActive(true);
            chat.m_input.ActivateInputField();
            yield return WaitReal(() => chat.HasFocus(), 2f);
            if (c.Check(chat.HasFocus(), "cursor in the chat box (not possible in this run: chat not tested)"))
            {
                yield return null;
                yield return null;
                c.Check(Scope.State == ScopeState.Lowering || Scope.State == ScopeState.Idle, $"typing in the chat lowers it ({Scope.State})");
                yield return WaitReal(() => Scope.State == ScopeState.Idle, 3f);
                c.Check(NotBack(player).Length == 0, "down behind the chat" + (NotBack(player).Length == 0 ? "" : ": " + NotBack(player)));
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- spyglass.aborts ----------

    // Me wait for the teleport to end and look at every frame of it: seen[0] = first thing the spyglass still held
    // on to meanwhile ("" = nothing, ever).
    private static IEnumerator WatchTeleport(Player player, float timeout, string[] seen)
    {
        seen[0] = "";
        var end = Time.realtimeSinceStartup + timeout;
        while (player != null && player.IsTeleporting() && Time.realtimeSinceStartup < end)
        {
            yield return null;
            if (seen[0].Length == 0 && player != null)
            {
                seen[0] = NotBack(player);
            }
        }
    }

    private static IEnumerator RunAborts()
    {
        var c = new Checks(AbortsTest);
        if (!Ready(AbortsTest, out var player, out var cam))
        {
            yield break;
        }
        var rig = new SpyRig(player, AbortsTest);
        try
        {
            rig.KeepPlace();
            rig.Noon();
            var spy = Hold(rig, c);
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            Scope.TestSetMagnification(4f);
            yield return new WaitForSeconds(0.3f);

            // Teleport (to same spot): view jump back the moment it start.
            yield return RaiseUp(c, player, "teleport");
            var here = player.transform.position;
            var rot = player.transform.rotation;
            var started = false;
            var t0 = Time.realtimeSinceStartup;
            while (!started && Time.realtimeSinceStartup - t0 < 5f)
            {
                started = player.TeleportTo(here, rot, false);
                if (!started)
                {
                    yield return null;
                }
            }
            if (c.Check(started && player.IsTeleporting(), "teleport started with the spyglass up"))
            {
                yield return null;
                yield return null;
                c.Check(player.IsTeleporting() && NotBack(player).Length == 0,
                    "teleport: view back at once" + (NotBack(player).Length == 0 ? "" : ": " + NotBack(player)));
                Scope.TestRequestRaise();
                yield return Frames(3);
                c.Check(Scope.State == ScopeState.Idle, "no raise during the teleport");
                var during = new string[1];
                yield return WatchTeleport(player, 20f, during);
                c.Check(during[0].Length == 0, "nothing of the spyglass shows on any frame of the teleport" + (during[0].Length == 0 ? "" : ": " + during[0]));
                yield return Frames(5);
                c.Check(!player.IsTeleporting() && Scope.State == ScopeState.Idle && !ScopeOverlay.Shown, "still down after arriving");
                c.Check(Hud.instance.m_crosshair.enabled && Near(cam.m_camera.fieldOfView, cam.m_fov, 0.01f), "crosshair and zoom normal after arriving");
                yield return RaiseUp(c, player, "after the teleport");
                yield return LowerDown();
            }

            // Death, played with dead flag for a few frames (real death move inventory to a tombstone and respawn
            // the player in the middle of the run).
            yield return RaiseUp(c, player, "death");
            var zdo = player.m_nview.GetZDO();
            rig.OnRestore("dead flag", () => zdo.Set(ZDOVars.s_dead, false));
            zdo.Set(ZDOVars.s_dead, true);
            yield return null;
            yield return null;
            var dead = player.IsDead();
            var left = NotBack(player);
            zdo.Set(ZDOVars.s_dead, false);
            c.Check(dead && left.Length == 0, "dead: view back at once" + (left.Length == 0 ? "" : ": " + left));
            yield return Frames(5);
            c.Check(!player.IsDead() && Scope.State == ScopeState.Idle && Hud.instance.m_crosshair.enabled && !BodyFar(player) == player.m_lodVisible,
                "alive again: still down, crosshair on, body as the game wants it");
            yield return new WaitForSeconds(1f);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- spyglass.stagger ----------

    // Own test: stagger with no hit behind it (in play a stagger come with a hit, and the hit lower it anyway).
    private static IEnumerator RunStagger()
    {
        var c = new Checks(StaggerTest);
        if (!Ready(StaggerTest, out var player, out _))
        {
            yield break;
        }
        var rig = new SpyRig(player, StaggerTest);
        try
        {
            rig.KeepPlace();
            var spy = Hold(rig, c);
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(0.3f);
            yield return RaiseUp(c, player, "stagger");
            player.Stagger(-player.transform.forward);
            yield return WaitReal(() => player.IsStaggering() || !Scope.Engaged, 2f);
            if (!player.IsStaggering() && Scope.State == ScopeState.Raised)
            {
                // Stagger = animation state: it may not start while own body is not drawn.
                c.Note("the stagger animation did not start within 2 s with the spyglass up: nothing to check");
                yield return LowerDown();
            }
            else
            {
                yield return null;
                yield return null;
                c.Check(Scope.State == ScopeState.Lowering || Scope.State == ScopeState.Idle, $"a stagger lowers it ({Scope.State})");
                yield return WaitReal(() => Scope.State == ScopeState.Idle, 3f);
                c.Check(NotBack(player).Length == 0, "down after the stagger" + (NotBack(player).Length == 0 ? "" : ": " + NotBack(player)));
                yield return WaitReal(() => !player.IsStaggering(), 5f);
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- spyglass.cannot-raise ----------

    // Armour item that take longest to put on (game play the equip animation for it).
    private static GameObject SlowArmor()
    {
        GameObject best = null;
        var bestTime = 0f;
        foreach (var prefab in ObjectDB.instance.m_items)
        {
            var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            var s = drop != null ? drop.m_itemData.m_shared : null;
            if (s == null || s.m_icons == null || s.m_icons.Length == 0 || !string.IsNullOrEmpty(s.m_dlc))
            {
                continue;
            }
            var type = s.m_itemType;
            if (type != ItemDrop.ItemData.ItemType.Chest && type != ItemDrop.ItemData.ItemType.Legs && type != ItemDrop.ItemData.ItemType.Helmet)
            {
                continue;
            }
            if (s.m_equipDuration > bestTime && s.m_equipDuration <= 3f)
            {
                best = prefab;
                bestTime = s.m_equipDuration;
            }
        }
        return best;
    }

    private static IEnumerator RunCannot()
    {
        var c = new Checks(CannotTest);
        if (!Ready(CannotTest, out var player, out _))
        {
            yield break;
        }
        var rig = new SpyRig(player, CannotTest);
        GameObject seat = null;
        try
        {
            rig.KeepPlace();
            var spy = Hold(rig, c);
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(0.3f);

            // Sitting, like a chair seat the player (Chair.Interact): seat point, chair's own animation.
            var animation = "attach_chair";
            var offset = new Vector3(0f, 0.5f, 0f);
            foreach (var prefab in ZNetScene.instance.m_prefabs)
            {
                var chair = prefab != null ? prefab.GetComponentInChildren<Chair>(true) : null;
                if (chair != null && !chair.m_inShip)
                {
                    animation = chair.m_attachAnimation;
                    offset = chair.m_detachOffset;
                    c.Note($"sitting like on {prefab.name} (animation {animation})");
                    break;
                }
            }
            seat = new GameObject("MC_SpyglassTestSeat");
            seat.transform.position = player.transform.position + Vector3.up * 0.4f;
            seat.transform.rotation = player.transform.rotation;
            player.AttachStart(seat.transform, null, false, false, false, animation, offset);
            yield return Frames(5);
            c.Check(player.IsAttached() && player.GetRightItem() == spy, "sitting with the spyglass in hand");
            Scope.TestRequestRaise();
            yield return Frames(3);
            c.Check(Scope.State == ScopeState.Idle, "sitting: cannot raise");
            yield return Click(player);
            yield return Frames(3);
            c.Check(!player.IsAttached(), "sitting: the click stands you up, as in the normal game");
            c.Check(Scope.State == ScopeState.Idle, $"sitting: that click does not raise it ({Scope.State})");
            yield return new WaitForSeconds(0.5f);
            c.Check(Scope.State == ScopeState.Idle && !player.InAttack(), "still down, no punch, after standing up");
            if (player.IsAttached())
            {
                player.AttachStop();
            }

            // Equip animation of something else (armour).
            var armorPrefab = SlowArmor();
            var armor = armorPrefab != null ? rig.Give(armorPrefab.name) : null;
            if (c.Check(armor != null, "an armour piece with an equip time found"))
            {
                c.Note($"equip animation of {armorPrefab.name} ({F(armor.m_shared.m_equipDuration)} s)");
                // What player wore in those slots go back on at the end.
                var worn = new[] { player.m_helmetItem, player.m_chestItem, player.m_legItem };
                rig.OnRestore("armour", () =>
                {
                    player.ClearActionQueue();
                    if (player.IsItemEquiped(armor))
                    {
                        player.UnequipItem(armor, false);
                    }
                    foreach (var item in worn)
                    {
                        if (item != null && player.GetInventory().ContainsItem(item) && !player.IsItemEquiped(item))
                        {
                            player.EquipItem(item, false);
                        }
                    }
                });
                yield return WaitReal(() => Scope.CanRaise(player), 3f);
                player.UseItem(null, armor, false);
                yield return WaitReal(() => player.InMinorAction(), 2f);
                if (c.Check(player.InMinorAction(), "equip animation playing"))
                {
                    Scope.TestRequestRaise();
                    yield return null;
                    yield return null;
                    var playing = player.InMinorAction();
                    var requestState = Scope.State;
                    yield return new WaitForFixedUpdate();
                    Controls(player, attack: true, attackHold: true);
                    yield return null;
                    yield return null;
                    var clickPlaying = player.InMinorAction();
                    var clickState = Scope.State;
                    Controls(player);
                    c.Check(playing && requestState == ScopeState.Idle, $"equip animation: cannot raise ({requestState}, animation still playing {playing})");
                    c.Check(clickPlaying && clickState == ScopeState.Idle && !player.InAttack(),
                        $"equip animation: the click does nothing ({clickState}, animation still playing {clickPlaying})");
                }
                yield return WaitReal(() => !player.IsEquipActionQueued(armor) && !player.InMinorAction(), 6f);
                yield return LowerDown();
                player.UnequipItem(armor, false);
            }

            // Build mode: hammer take the spyglass's place, so it never in hand there.
            var hammer = rig.Give("Hammer");
            if (c.Check(hammer != null, "Hammer given"))
            {
                player.EquipItem(spy, false);
                player.EquipItem(hammer, false);
                yield return Frames(5);
                c.Check(player.GetRightItem() == hammer && !player.IsItemEquiped(spy) && player.InPlaceMode(),
                    "build mode: the hammer replaced the spyglass (it is never in hand in build mode)");
                Scope.TestRequestRaise();
                yield return Click(player);
                yield return Frames(3);
                c.Check(Scope.State == ScopeState.Idle, $"build mode: nothing to raise ({Scope.State})");
                player.UnequipItem(hammer, false);
                yield return Frames(3);
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
            if (seat != null)
            {
                Object.Destroy(seat);
            }
        }
    }

    // ---------- spyglass.helm ----------

    private static void MovePlayer(Player player, Vector3 position)
    {
        player.transform.position = position;
        var body = player.m_body;
        if (body != null)
        {
            body.position = position;
            body.linearVelocity = Vector3.zero;
        }
    }

    // Spawned ship gone, player out of its list first (destroyed ship never send the trigger exit).
    private static void RemoveShip(Player player, GameObject go)
    {
        if (go == null)
        {
            return;
        }
        var ship = go.GetComponent<Ship>();
        if (ship != null)
        {
            if (ship.IsPlayerInBoat(player))
            {
                ship.m_players.Remove(player);
                player.InNumShipVolumes = Mathf.Max(0, player.InNumShipVolumes - 1);
            }
            Ship.s_currentShips.Remove(ship);
        }
        if (ZNetScene.instance != null)
        {
            ZNetScene.instance.Destroy(go);
        }
    }

    private static IEnumerator RunHelm()
    {
        var c = new Checks(HelmTest);
        if (!Ready(HelmTest, out var player, out _))
        {
            yield break;
        }
        var rig = new SpyRig(player, HelmTest);
        GameObject shipGo = null;
        try
        {
            rig.KeepPlace();
            var spy = Hold(rig, c);
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            // Karve, else the first network prefab with a Ship (same pick as the Sailing self tests).
            var prefab = ZNetScene.instance.GetPrefab("Karve");
            if (prefab == null || prefab.GetComponent<Ship>() == null)
            {
                prefab = null;
                foreach (var p in ZNetScene.instance.m_prefabs)
                {
                    if (p != null && p.GetComponent<Ship>() != null)
                    {
                        prefab = p;
                        break;
                    }
                }
            }
            if (!c.Check(prefab != null, "a ship prefab exists"))
            {
                c.Report();
                yield break;
            }
            // Held still in the air above the player (no water needed, nothing in the way).
            var origin = rig.Origin;
            rig.OnRestore("ship", () =>
            {
                if (player.GetDoodadController() != null)
                {
                    player.StopDoodadControl();
                }
                if (player.IsAttached())
                {
                    player.AttachStop();
                }
                MovePlayer(player, origin);
                RemoveShip(player, shipGo);
                shipGo = null;
            });
            shipGo = Object.Instantiate(prefab, origin + Vector3.up * 30f, Quaternion.identity);
            var ship = shipGo.GetComponent<Ship>();
            if (ship != null && ship.m_body != null)
            {
                ship.m_body.isKinematic = true;
            }
            yield return null;
            yield return null;
            var helm = ship != null ? ship.m_shipControlls : null;
            var view = shipGo.GetComponent<ZNetView>();
            if (!c.Check(helm != null && helm.m_attachPoint != null && view != null && view.GetZDO() != null, $"{prefab.name} spawned with a helm"))
            {
                c.Report();
                yield break;
            }
            // Helm taken like game grant it (ShipControlls.RPC_RequestRespons): weapons stay in hand.
            view.GetZDO().Set(ZDOVars.s_user, player.GetPlayerID());
            player.StartDoodadControl(helm);
            player.AttachStart(helm.m_attachPoint, null, false, false, true, helm.m_attachAnimation, helm.m_detachOffset);
            yield return FixedSteps(5);
            c.Check(player.GetDoodadController() != null && player.IsAttached() && player.GetRightItem() == spy,
                "at the helm with the spyglass in hand");
            Scope.TestRequestRaise();
            yield return Frames(3);
            c.Check(Scope.State == ScopeState.Idle, "at the helm: cannot raise");
            yield return Click(player);
            yield return Frames(3);
            c.Check(player.GetDoodadController() == null && !player.IsAttached(), "the click lets go of the helm, as in the normal game");
            c.Check(Scope.State == ScopeState.Idle && !Flag(player) && !player.InAttack(), $"and the spyglass stays down ({Scope.State})");
            // Off the ship before it go (trigger volume see player leave).
            MovePlayer(player, origin);
            for (var i = 0; i < 10 && ship != null && ship.IsPlayerInBoat(player); i++)
            {
                yield return new WaitForFixedUpdate();
            }
            yield return new WaitForSeconds(0.5f);
            c.Check(Scope.State == ScopeState.Idle, "still down after leaving the ship");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- spyglass.pending ----------

    private static IEnumerator RunPending()
    {
        var c = new Checks(PendingTest);
        if (!Ready(PendingTest, out var player, out _))
        {
            yield break;
        }
        var rig = new SpyRig(player, PendingTest);
        try
        {
            var spy = Hold(rig, c);
            var recipe = SpyglassContent.CraftRecipe;
            if (spy == null || !c.Check(recipe != null, "recipe registered"))
            {
                c.Report();
                yield break;
            }
            ServerRules.TestRules = new SpyglassRules();
            SpyglassContent.Rebuild();
            c.Check(recipe.m_enabled, "recipe shown with rules in force");
            rig.OnRestore("recipe", () => SpyglassContent.Rebuild());
            // Me wait out the 3 s pause of an earlier message.
            yield return Seconds(3.2f);
            yield return PendingClicks(c, player, recipe, () =>
            {
                ServerRules.TestPending = true;
                SpyglassContent.Rebuild();
            });
            ServerRules.TestPending = false;
            SpyglassContent.Rebuild();
            c.Check(!ServerRules.IsPending && recipe.m_enabled, "rules here: recipe shown again");
            yield return Click(player);
            yield return WaitReal(() => Scope.State == ScopeState.Raised, 3f);
            c.Check(Scope.State == ScopeState.Raised, $"rules here: the click raises it ({Scope.State})");
            yield return LowerDown();
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // Client waiting for server rules (start make that): every click stay down, message show once per 3 s, recipe
    // hidden. About 4 s.
    private static IEnumerator PendingClicks(Checks c, Player player, Recipe recipe, Action start)
    {
        var sent = Scope.TestPendingMessages;
        start();
        c.Check(ServerRules.IsPending, "waiting for the server's rules");
        var first = Time.realtimeSinceStartup;
        yield return Click(player);
        yield return null;
        yield return null;
        c.Check(Scope.State == ScopeState.Idle, $"waiting: the click does not raise it ({Scope.State})");
        c.Check(Scope.TestPendingMessages == sent + 1 && MessageSeen(Scope.PendingMessage),
            $"waiting: message \"{Scope.PendingMessage}\" ({Scope.TestPendingMessages - sent} sent, on screen {MessageSeen(Scope.PendingMessage)})");
        yield return Seconds(1f);
        yield return Click(player);
        yield return null;
        yield return null;
        c.Check(Scope.State == ScopeState.Idle && Scope.TestPendingMessages == sent + 1,
            $"a click a second later: still down, no second message ({Scope.TestPendingMessages - sent} sent)");
        yield return WaitReal(() => Time.realtimeSinceStartup - first > 3.3f, 5f);
        yield return Click(player);
        yield return null;
        yield return null;
        c.Check(Scope.State == ScopeState.Idle && Scope.TestPendingMessages == sent + 2,
            $"a click 3 s later: the message again ({Scope.TestPendingMessages - sent} sent)");
        c.Check(ServerRules.IsPending && !recipe.m_enabled, "still waiting: recipe hidden");
    }
}
#endif
