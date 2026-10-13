#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Exploration.ViewSpyglassMod;

// Debug build only. Me = self tests that press real keys and mouse buttons (SelfTest.Key: state of the real devices,
// read by ZInput, the game's PlayerController and every mod like a person's press). Player's own controller stays ON
// here: the older tests call Player.SetControls themselves, these go the whole road. One small test per thing
// (TESTING.md items in brackets):
//   spyglass.input.click      real left click raises (smooth: no frame jumps the camera or the zoom, round view closes
//                             in step by step, own body shown only before the camera is in the head), real click
//                             lowers, real Block lowers [T03 T07]
//   spyglass.input.esc        real Esc with the spyglass up: pause menu, it comes down behind it; Esc closes [T22 T09]
//   spyglass.input.sit        on a real chair piece a real click stands you up and raises nothing; standing, the
//                             next click raises [T10]
//   spyglass.input.dualwield  Dual Wielding's real keys (swap hands, main hand held while equipping) with the
//                             spyglass: nothing swapped, never a pair [X03]
//   spyglass.input.dive       into real deep water with the spyglass up, then the real Crouch key: Swim Dive dives
//                             right after the view came back, and comes up again [X05]
// Keys are read from this game's bindings and from the other mod's settings, never assumed.
internal static partial class SelfTests
{
    private const string InputClickTest = "spyglass.input.click";
    private const string InputEscTest = "spyglass.input.esc";
    private const string InputSitTest = "spyglass.input.sit";
    private const string InputDualTest = "spyglass.input.dualwield";
    private const string InputDiveTest = "spyglass.input.dive";

    private const string SwimDiveGuid = "MC.Exploration.Swimming.Dive";

    // ---------- helpers ----------

    // Keyboard key or mouse button a game button is bound to in this game (player's own binding). None = not a key
    // the test can press (path say what it is).
    private static KeyCode BoundKey(string button, out string path)
    {
        var def = ZInput.instance != null ? ZInput.instance.GetButtonDef(button) : null;
        path = def != null ? def.GetActionPath() : null;
        if (string.IsNullOrEmpty(path))
        {
            return KeyCode.None;
        }
        for (var key = KeyCode.Backspace; key < KeyCode.JoystickButton0; key++)
        {
            if (ZInput.IsKeyCodeValid(key) && string.Equals(ZInput.KeyCodeToPath(key), path, StringComparison.OrdinalIgnoreCase))
            {
                return key;
            }
        }
        return KeyCode.None;
    }

    // Key setting of another plugin as it is set in this game (BepInEx config, no type of theirs). None = not found.
    private static KeyCode OtherKey(string guid, string section, string key)
    {
        if (!Chainloader.PluginInfos.TryGetValue(guid, out var info) || info.Instance == null)
        {
            return KeyCode.None;
        }
        return info.Instance.Config.TryGetEntry<KeyCode>(section, key, out var entry) ? entry.Value : KeyCode.None;
    }

    // Spyglass given and in right hand, player's own controller left on.
    private static ItemDrop.ItemData HoldForRealInput(SpyRig rig, Checks c)
    {
        var spy = rig.Give(SpyglassContent.ItemName);
        if (spy != null)
        {
            rig.Player.EquipItem(spy, false);
        }
        var controller = rig.Player.GetComponent<PlayerController>();
        return c.Check(spy != null && rig.Player.GetRightItem() == spy && controller != null && controller.enabled,
            "spyglass given and in the right hand, the player's own controller on") ? spy : null;
    }

    // One real press: down, held (real time), up, one more frame. sent[0] false = no input driver took it.
    private static IEnumerator Press(KeyCode key, bool[] sent, float hold = 0.15f)
    {
        sent[0] = SelfTest.Key(key, true);
        yield return new WaitForSecondsRealtime(hold);
        SelfTest.Key(key, false);
        yield return null;
    }

    // Me look at every drawn frame of a raise: no frame may jump. Shares are of the whole way (camera: vanilla spot
    // to the eye; zoom: vanilla field of view to the zoomed one), for a frame of 1/30 s; longer frames count less.
    private sealed class RaiseWatch
    {
        internal float CamShare;
        internal float FovShare;
        internal bool IrisBack;
        internal int IrisSteps;
        internal float FirstIris = -1f;
        internal bool ShownAgain;
        internal float GapStart = -2f;
        internal float GapLastSeen = -2f;
        internal int SeenFrames;
        internal int Frames;

        private readonly Player _player;
        private readonly GameCamera _cam;
        private readonly float _path;
        private readonly float _fovSpan;
        private Vector3 _pos;
        private float _fov;
        private float _iris = float.MaxValue;
        private bool _hidden;

        internal RaiseWatch(Player player, GameCamera cam, float path, float fovSpan)
        {
            _player = player;
            _cam = cam;
            _path = Mathf.Max(0.5f, path);
            _fovSpan = Mathf.Max(1f, fovSpan);
            _pos = cam.transform.position;
            _fov = cam.m_camera.fieldOfView;
        }

        internal void Sample()
        {
            var pos = _cam.transform.position;
            var fov = _cam.m_camera.fieldOfView;
            var scale = Mathf.Max(1f, Time.unscaledDeltaTime * 30f);
            CamShare = Mathf.Max(CamShare, (pos - _pos).magnitude / _path / scale);
            FovShare = Mathf.Max(FovShare, Mathf.Abs(fov - _fov) / _fovSpan / scale);
            _pos = pos;
            _fov = fov;
            if (ScopeOverlay.Shown)
            {
                var iris = ScopeOverlay.TestDark.y;
                if (FirstIris < 0f)
                {
                    FirstIris = iris;
                }
                IrisBack |= iris > _iris + 0.0001f;
                if (iris < _iris - 0.0001f)
                {
                    IrisSteps++;
                }
                _iris = iris;
            }
            var gap = ArmPose.EyepieceGap(_player);
            if (GapStart < -1f)
            {
                GapStart = gap;
            }
            if (BodyFar(_player))
            {
                _hidden = true;
            }
            else
            {
                ShownAgain |= _hidden;
                GapLastSeen = gap;
                SeenFrames++;
            }
            Frames++;
        }
    }

    // ---------- spyglass.input.click ----------

    private static IEnumerator RunInputClick()
    {
        var c = new Checks(InputClickTest);
        if (!Ready(InputClickTest, out var player, out var cam))
        {
            yield break;
        }
        var rig = new SpyRig(player, InputClickTest);
        try
        {
            rig.KeepPlace();
            rig.Noon();
            var attack = BoundKey("Attack", out var attackPath);
            var block = BoundKey("Block", out var blockPath);
            if (!c.Check(attack != KeyCode.None && block != KeyCode.None,
                    $"Attack and Block are on keys or mouse buttons the test can press (Attack '{attackPath}', Block '{blockPath}')"))
            {
                c.Report();
                yield break;
            }
            var spy = HoldForRealInput(rig, c);
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            ServerRules.TestRules = new SpyglassRules();
            Scope.TestHoldToLook = false;
            rig.Look(OpenYaw(player, out _), -1f);
            Scope.TestSetMagnification(4f);
            // Third person from a normal distance (a camera zoomed right in hides the body from the first frame on).
            var distance = cam.m_distance;
            rig.OnRestore("camera distance", () => cam.m_distance = distance);
            cam.m_distance = Mathf.Max(distance, 4f);
            yield return new WaitForSeconds(1f);
            yield return WaitReal(() => Scope.CanRaise(player), 4f);
            yield return new WaitForEndOfFrame();
            var vanillaFov = cam.m_camera.fieldOfView;
            var zoomedFov = ScopeCamera.ZoomedFov(vanillaFov, 4f);
            var path = (cam.transform.position - player.m_eye.position).magnitude;
            var errors = _tap != null ? _tap.ErrorMark : 0;
            var problems = _tap != null ? _tap.Problems().Count : 0;

            // Real left click: up. Me watch every drawn frame from the press until it is at the eye.
            var watch = new DrawWatch();
            var smooth = new RaiseWatch(player, cam, path, vanillaFov - zoomedFov);
            if (!c.Check(SelfTest.Key(attack, true), $"the input driver pressed {attack} (not: no probe drives this game)"))
            {
                c.Report();
                yield break;
            }
            var pressed = Time.unscaledTime;
            var started = -1f;
            var released = false;
            while (Scope.State != ScopeState.Raised && Time.unscaledTime - pressed < 4f)
            {
                yield return new WaitForEndOfFrame();
                if (!released && Time.unscaledTime - pressed > 0.12f)
                {
                    SelfTest.Key(attack, false);
                    released = true;
                }
                if (Scope.Active)
                {
                    if (started < 0f)
                    {
                        started = Time.unscaledTime;
                    }
                    watch.Sample(player, cam, true);
                    smooth.Sample();
                }
            }
            SelfTest.Key(attack, false);
            var took = started >= 0f ? Time.unscaledTime - started : -1f;
            if (!c.Check(Scope.State == ScopeState.Raised, $"a real press of {attack} (the Attack button) raises the spyglass ({Scope.State})"))
            {
                c.Report();
                yield break;
            }
            c.Note($"raise began {F(started - pressed)} s after the press and took {F(took)} s over {smooth.Frames} frames; camera way {F(path)} m, "
                   + $"largest step of one frame {F(smooth.CamShare)} of the way, zoom {F(smooth.FovShare)} of its change; round view from {F(smooth.FirstIris)} "
                   + $"circle sizes in {smooth.IrisSteps} steps; body drawn on {smooth.SeenFrames} frames, eyepiece {F(smooth.GapStart)} m -> {F(smooth.GapLastSeen)} m from the eye meanwhile");
            c.Check(took > Scope.RaiseSeconds - 0.2f && took < Scope.RaiseSeconds + 0.35f, $"up in about {F(Scope.RaiseSeconds)} s (took {F(took)} s)");
            c.Check(!watch.ProgressBack && !watch.SlideBack && !watch.ZoomBack, "the camera slides to the eye and zooms in without ever going back");
            c.Check(smooth.CamShare <= 0.25f && smooth.FovShare <= 0.25f,
                $"no jerk: no frame moves the camera or the zoom by more than a quarter of the way (camera {F(smooth.CamShare)}, zoom {F(smooth.FovShare)})");
            c.Check(!watch.BodySeen, "own body hidden on every frame the camera is in the head or at the feet (never the inside of the head)");
            c.Check(!smooth.ShownAgain && smooth.SeenFrames > 0, $"own body and arm drawn only at the start: once hidden they stay hidden until it is up ({smooth.SeenFrames} frames drawn)");
            c.Check(smooth.GapStart >= 0f && smooth.GapLastSeen >= 0f && smooth.GapLastSeen < smooth.GapStart,
                $"while still drawn the arm lifts the spyglass toward the eye (eyepiece {F(smooth.GapStart)} m -> {F(smooth.GapLastSeen)} m from the eye)");
            c.Check(smooth.FirstIris > 1.5f && !smooth.IrisBack && smooth.IrisSteps >= 5 && Near(ScopeOverlay.TestDark.y, 1f, 0.001f),
                $"the round view closes in from beyond the screen edge, step by step, never opening again (from {F(smooth.FirstIris)} circle sizes, {smooth.IrisSteps} steps)");
            c.Check(!player.InAttack() && player.m_queuedAttackTimer <= 0f, "the click never punches");
            yield return new WaitForSeconds(0.3f);
            yield return new WaitForEndOfFrame();
            var gap = (cam.transform.position - player.m_eye.position).magnitude;
            c.Check(gap < 0.05f && Near(cam.m_camera.fieldOfView, zoomedFov, 0.05f) && Flag(player),
                $"at the eye: camera {F(gap)} m from it, zoom x4 (fov {F(cam.m_camera.fieldOfView)}), raised flag up");

            // Real click again: down.
            var sent = new bool[1];
            yield return Press(attack, sent);
            yield return WaitReal(() => Scope.State == ScopeState.Idle, 3f);
            yield return Frames(3);
            yield return new WaitForEndOfFrame();
            gap = (cam.transform.position - player.m_eye.position).magnitude;
            c.Check(sent[0] && Scope.State == ScopeState.Idle && NotBack(player).Length == 0,
                $"a second real click lowers it ({Scope.State})" + (NotBack(player).Length == 0 ? "" : ": " + NotBack(player)));
            c.Check(Near(cam.m_camera.fieldOfView, vanillaFov, 0.01f) && gap > 0.5f && Hud.instance.m_crosshair.enabled,
                $"camera back in third person ({F(gap)} m from the eye, fov {F(cam.m_camera.fieldOfView)}), crosshair back");

            // Up again by a real click, real Block: down.
            yield return WaitReal(() => Scope.CanRaise(player), 4f);
            yield return Press(attack, sent);
            yield return WaitReal(() => Scope.State == ScopeState.Raised, 3f);
            if (c.Check(Scope.State == ScopeState.Raised, $"a third real click raises it again ({Scope.State})"))
            {
                yield return Press(block, sent);
                yield return WaitReal(() => Scope.State == ScopeState.Idle, 3f);
                yield return Frames(3);
                c.Check(sent[0] && Scope.State == ScopeState.Idle && NotBack(player).Length == 0,
                    $"a real press of {block} (the Block button) lowers it ({Scope.State})");
            }
            yield return LowerDown();
            var logged = ErrorsNamingMe(errors);
            c.Check(_tap != null && ProblemsSince(problems).Count == 0 && logged.Count == 0,
                "no warning or error from the spyglass meanwhile" + (logged.Count == 0 ? "" : ": " + First(logged)));
            c.Report();
        }
        finally
        {
            SelfTest.ReleaseInput();
            rig.Restore();
        }
    }

    // ---------- spyglass.input.esc ----------

    private static IEnumerator RunInputEsc()
    {
        var c = new Checks(InputEscTest);
        if (!Ready(InputEscTest, out var player, out _))
        {
            yield break;
        }
        var menu = Menu.instance;
        var rig = new SpyRig(player, InputEscTest);
        try
        {
            var spy = HoldForRealInput(rig, c);
            if (spy == null || !c.Check(menu != null && menu.m_root != null && !menu.m_root.gameObject.activeSelf, "pause menu exists and is closed"))
            {
                c.Report();
                yield break;
            }
            // Menu left open would freeze every test after this one.
            rig.OnRestore("menu", () =>
            {
                if (menu != null && menu.m_root != null && menu.m_root.gameObject.activeSelf)
                {
                    menu.Hide();
                }
            });
            Scope.TestHoldToLook = false;
            Scope.TestSetMagnification(4f);
            yield return new WaitForSeconds(0.5f);
            yield return RaiseUp(c, player, "Esc");
            yield return Frames(5);

            var sent = new bool[1];
            yield return Press(KeyCode.Escape, sent);
            yield return WaitReal(() => menu.m_root.gameObject.activeSelf, 2f);
            var open = menu.m_root.gameObject.activeSelf;
            yield return Seconds(0.3f);
            var paused = Game.IsPaused() && Time.timeScale == 0f;
            yield return WaitReal(() => Scope.State == ScopeState.Idle, 2f);
            // Two more frames, then what is drawn: the round view object itself (not only the mod's own flag) and the
            // camera's zoom, behind the open menu.
            yield return Frames(2);
            yield return new WaitForEndOfFrame();
            var behind = NotBack(player);
            var drawn = OnScreen();
            var view = GameCamera.instance;
            var zoomBack = view != null && view.m_camera != null && Near(view.m_camera.fieldOfView, view.m_fov, 0.01f);
            c.Check(sent[0] && open, $"the real Esc key opens the pause menu with the spyglass up (key sent {sent[0]}, menu open {open})");
            c.Check(paused, $"the game is paused behind it (paused {Game.IsPaused()}, time scale {F(Time.timeScale)})");
            c.Check(Scope.State == ScopeState.Idle && !ScopeOverlay.Shown && behind.Length == 0 && drawn.Length == 0 && zoomBack,
                $"the spyglass comes down behind the menu although the game is paused ({Scope.State}; round view object off and zoom back {drawn.Length == 0 && zoomBack})"
                + (behind.Length == 0 ? "" : ": " + behind) + (drawn.Length == 0 ? "" : ": " + drawn));
            SelfTest.Screenshot(InputEscTest, "menu");
            yield return null;
            yield return null;

            // Esc again: menu closed, game runs, spyglass still in hand and down.
            yield return Press(KeyCode.Escape, sent);
            yield return WaitReal(() => !menu.m_root.gameObject.activeSelf && Time.timeScale > 0f, 3f);
            var closed = !menu.m_root.gameObject.activeSelf && Time.timeScale > 0f;
            c.Check(closed, $"Esc again closes the menu and the game runs (menu open {menu.m_root.gameObject.activeSelf}, time scale {F(Time.timeScale)})");
            if (!closed)
            {
                menu.Hide();
            }
            yield return Seconds(0.5f);
            c.Check(Scope.State == ScopeState.Idle && player.GetRightItem() == spy && NotBack(player).Length == 0,
                $"after the menu: spyglass still in the hand, down ({Scope.State})");
            yield return RaiseUp(c, player, "after the menu");
            yield return LowerDown();
            c.Report();
        }
        finally
        {
            SelfTest.ReleaseInput();
            rig.Restore();
        }
    }

    // ---------- spyglass.input.sit ----------

    private static IEnumerator RunInputSit()
    {
        var c = new Checks(InputSitTest);
        if (!Ready(InputSitTest, out var player, out _))
        {
            yield break;
        }
        var rig = new SpyRig(player, InputSitTest);
        GameObject seat = null;
        try
        {
            rig.KeepPlace();
            var attack = BoundKey("Attack", out var attackPath);
            if (!c.Check(attack != KeyCode.None, $"Attack is on a key or mouse button the test can press ('{attackPath}')"))
            {
                c.Report();
                yield break;
            }
            // A real chair of the game: first building piece with a seat on land.
            GameObject prefab = null;
            foreach (var p in ZNetScene.instance.m_prefabs)
            {
                if (p == null || p.GetComponent<Piece>() == null)
                {
                    continue;
                }
                var found = p.GetComponentInChildren<Chair>(true);
                if (found != null && !found.m_inShip)
                {
                    prefab = p;
                    break;
                }
            }
            var spot = Vector3.zero;
            if (!c.Check(prefab != null && FreeSpot(player, new[] { 1.2f, 1.5f, 1f }, 0.5f, out spot), "a chair piece of the game and free ground next to the player"))
            {
                c.Report();
                yield break;
            }
            var away = spot - player.transform.position;
            away.y = 0f;
            seat = Object.Instantiate(prefab, spot, Quaternion.LookRotation(-away.normalized));
            var chair = seat.GetComponentInChildren<Chair>();
            c.Note($"chair: {prefab.name}, {F(away.magnitude)} m from the player");
            var spy = HoldForRealInput(rig, c);
            if (spy == null || !c.Check(chair != null, "the chair has its seat"))
            {
                c.Report();
                yield break;
            }
            ServerRules.TestRules = new SpyglassRules();
            Scope.TestHoldToLook = false;
            Scope.TestSetMagnification(4f);
            yield return new WaitForSeconds(0.7f);

            // Sit down like the Use key on the chair does.
            chair.Interact(player, false, false);
            yield return WaitReal(() => player.IsAttached(), 2f);
            yield return Frames(10);
            if (!c.Check(player.IsAttached() && player.GetRightItem() == spy, "sitting on the chair with the spyglass in the hand"))
            {
                c.Report();
                yield break;
            }

            // Real left click: stands up, raises nothing.
            var sent = new bool[1];
            var raised = false;
            yield return Press(attack, sent);
            var until = Time.realtimeSinceStartup + 1.2f;
            while (Time.realtimeSinceStartup < until)
            {
                raised |= Scope.State != ScopeState.Idle;
                yield return null;
            }
            c.Check(sent[0] && !player.IsAttached(), $"sitting: a real click stands you up, as in the normal game (key sent {sent[0]}, still sitting {player.IsAttached()})");
            c.Check(!raised && Scope.State == ScopeState.Idle && !ScopeOverlay.Shown, $"sitting: that click raises nothing ({Scope.State})");
            c.Check(!player.InAttack() && player.m_queuedAttackTimer <= 0f, "and it never punches");

            // Standing now: the same real click raises (so the click above did reach the game and the spyglass).
            yield return WaitReal(() => Scope.CanRaise(player), 4f);
            yield return Press(attack, sent);
            yield return WaitReal(() => Scope.State == ScopeState.Raised, 3f);
            c.Check(Scope.State == ScopeState.Raised, $"standing again, the next real click raises it ({Scope.State})");
            yield return LowerDown();
            c.Report();
        }
        finally
        {
            SelfTest.ReleaseInput();
            rig.Restore();
            if (seat != null && ZNetScene.instance != null)
            {
                ZNetScene.instance.Destroy(seat);
            }
        }
    }

    // ---------- spyglass.input.dualwield ----------

    private static IEnumerator RunInputDualWield()
    {
        var c = new Checks(InputDualTest);
        if (!Ready(InputDualTest, out var player, out _))
        {
            yield break;
        }
        if (!OtherModActive(DualWieldGuid))
        {
            SelfTest.Fail(InputDualTest, "Dual Wielding is not loaded and active: nothing of TESTING.md X03 was checked");
            yield break;
        }
        var rig = new SpyRig(player, InputDualTest);
        try
        {
            var swap = OtherKey(DualWieldGuid, "Controls", "SwapHandsKey");
            var main = OtherKey(DualWieldGuid, "Controls", "MainHandKey");
            if (!c.Check(swap != KeyCode.None && main != KeyCode.None && swap < KeyCode.JoystickButton0 && main < KeyCode.JoystickButton0,
                    $"Dual Wielding's keys in this game can be pressed by the test (swap hands {swap}, main hand {main})"))
            {
                c.Report();
                yield break;
            }
            var spy = rig.Give(SpyglassContent.ItemName);
            var sword = rig.Give("SwordIron");
            var axe = rig.Give("AxeBronze");
            if (!c.Check(spy != null && sword != null && axe != null && _tap != null, "spyglass, SwordIron and AxeBronze given, log listened to"))
            {
                c.Report();
                yield break;
            }
            ServerRules.TestRules = new SpyglassRules();
            Scope.TestHoldToLook = false;
            Scope.TestSetMagnification(4f);
            var errors = _tap.ErrorMark;
            var problems = _tap.Problems().Count;
            var sent = new bool[1];
            bool Alone() => player.GetRightItem() == spy && player.GetLeftItem() == null && !player.IsItemEquiped(sword) && !player.IsItemEquiped(axe);

            // A pair in hand, then the spyglass: pair put away.
            player.UnequipItem(player.GetRightItem(), false);
            player.UnequipItem(player.GetLeftItem(), false);
            player.EquipItem(sword, false);
            player.EquipItem(axe, false);
            yield return Frames(3);
            var paired = player.GetRightItem() == sword && player.GetLeftItem() == axe;
            c.Check(paired, "a pair in hand (sword right, axe left)");
            // Control: the real swap key must swap a real pair. Without it, "nothing changes hands" below would pass
            // just as well with a key press that never reach Dual Wielding.
            yield return Press(swap, sent);
            yield return WaitReal(() => player.GetRightItem() == axe && player.GetLeftItem() == sword, 6f);
            var swapped = paired && sent[0] && player.GetRightItem() == axe && player.GetLeftItem() == sword;
            c.Check(swapped,
                $"control: with the pair in hand a real press of the swap key ({swap}) swaps the hands, so the key reaches Dual Wielding in this game "
                + $"(key sent {sent[0]}, axe right {player.GetRightItem() == axe}, sword left {player.GetLeftItem() == sword})");
            yield return WaitReal(() => player.GetActionQueueCount() == 0 && !player.InMinorAction(), 3f);
            yield return Seconds(0.5f);
            if (swapped)
            {
                // And back: the rest runs with the pair as it was given (sword main, axe off).
                yield return Press(swap, sent);
                yield return WaitReal(() => player.GetRightItem() == sword && player.GetLeftItem() == axe, 6f);
                c.Check(player.GetRightItem() == sword && player.GetLeftItem() == axe, "control: a second real press swaps back (sword right, axe left)");
                yield return WaitReal(() => player.GetActionQueueCount() == 0 && !player.InMinorAction(), 3f);
                yield return Seconds(0.5f);
            }
            player.EquipItem(spy, false);
            yield return Frames(3);
            c.Check(Alone(), "spyglass equipped: the pair is put away, both hands taken");

            // Real swap key with the spyglass in hand.
            yield return Press(swap, sent);
            yield return Seconds(0.8f);
            c.Check(sent[0], $"the input driver pressed {swap}");
            c.Check(Alone() && !player.IsEquipActionQueued(sword) && !player.IsEquipActionQueued(axe) && !player.IsEquipActionQueued(spy),
                $"swap key ({swap}) with the spyglass in hand: nothing changes hands, nothing queued");
            c.Check(Scope.State == ScopeState.Idle && !player.InAttack(), $"and the spyglass stays down ({Scope.State})");

            // Real swap key while looking.
            yield return RaiseUp(c, player, "swap key");
            yield return Press(swap, sent);
            yield return Seconds(0.6f);
            c.Check(Scope.State == ScopeState.Raised && Alone(), $"swap key while looking: still looking, nothing swapped ({Scope.State})");
            yield return LowerDown();

            // Main-hand key held while the spyglass is equipped over a weapon (hotbar, inventory: Humanoid.UseItem).
            player.UnequipItem(spy, false);
            player.EquipItem(sword, false);
            yield return Frames(3);
            c.Check(player.GetRightItem() == sword && player.GetLeftItem() == null, "a sword alone in the right hand");
            var held = SelfTest.Key(main, true);
            yield return Frames(3);
            // The game must read the key as held (what Dual Wielding asks at the equip), else "held" is only a word.
            var read = ZInput.GetKey(main, false);
            player.UseItem(null, spy, false);
            yield return WaitReal(() => player.GetRightItem() == spy, 5f);
            yield return Frames(3);
            SelfTest.Key(main, false);
            yield return Frames(3);
            c.Check(held && read, $"the input driver held {main} and the game read it as held at the equip (sent {held}, read {read})");
            c.Check(player.GetRightItem() == spy && player.GetLeftItem() == null && !player.IsItemEquiped(sword),
                $"spyglass equipped with the main-hand key ({main}) held: it is in the right hand, the sword put away, no pair");

            // And the other way: a weapon equipped with the key held, spyglass in hand.
            SelfTest.Key(main, true);
            yield return Frames(3);
            read = ZInput.GetKey(main, false);
            player.UseItem(null, axe, false);
            yield return WaitReal(() => player.GetRightItem() == axe, 5f);
            yield return Frames(3);
            SelfTest.Key(main, false);
            yield return Frames(3);
            c.Check(read && player.GetRightItem() == axe && player.GetLeftItem() == null && !player.IsItemEquiped(spy),
                $"a weapon equipped with the main-hand key held replaces the spyglass (never a pair with it; key read as held {read})");

            // The spyglass still works afterwards.
            player.EquipItem(spy, false);
            yield return Frames(3);
            c.Check(Alone(), "spyglass back in the right hand alone");
            yield return RaiseUp(c, player, "after the keys");
            yield return LowerDown();
            var logged = _tap.ErrorsSince(errors);
            c.Check(logged.Count == 0 && ProblemsSince(problems).Count == 0,
                "no error logged by anyone and no warning from the spyglass meanwhile" + (logged.Count == 0 ? "" : ": " + First(logged)));
            c.Report();
        }
        finally
        {
            SelfTest.ReleaseInput();
            rig.Restore();
        }
    }

    // ---------- spyglass.input.dive ----------

    private static IEnumerator RunInputDive()
    {
        var c = new Checks(InputDiveTest);
        if (!Ready(InputDiveTest, out var player, out var cam))
        {
            yield break;
        }
        if (!OtherModActive(SwimDiveGuid))
        {
            SelfTest.Fail(InputDiveTest, "Swim Dive is not loaded and active: nothing of TESTING.md X05 was checked");
            yield break;
        }
        if (WorldGenerator.instance == null || ZoneSystem.instance == null)
        {
            SelfTest.Fail(InputDiveTest, "no world generator");
            yield break;
        }
        var rig = new SpyRig(player, InputDiveTest);
        try
        {
            var crouch = BoundKey("Crouch", out var crouchPath);
            if (!c.Check(crouch != KeyCode.None, $"Crouch (Swim Dive's dive key) is on a key the test can press ('{crouchPath}')")
                || !c.Check(FindWater(rig.Origin, out var water), "deep water within 5 km of the spawn"))
            {
                c.Report();
                yield break;
            }
            var spy = HoldForRealInput(rig, c);
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            var wet = player.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectWet);
            rig.OnRestore("wet", () =>
            {
                if (!wet)
                {
                    player.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectWet, true);
                }
            });
            ServerRules.TestRules = new SpyglassRules();
            Scope.TestHoldToLook = false;
            Scope.TestSetMagnification(4f);
            var problems = _tap != null ? _tap.Problems().Count : 0;
            var errors = _tap != null ? _tap.ErrorMark : 0;

            // Feet are locked while looking, so nobody walks into water with it up: the player drops into it.
            yield return rig.Travel(water + Vector3.up * 35f, Quaternion.identity, c, 40f);
            Scope.TestRequestRaise();
            yield return WaitReal(() => Scope.State == ScopeState.Raised || player.IsSwimming(), 2f);
            if (!c.Check(Scope.State == ScopeState.Raised && !player.IsSwimming(), $"spyglass up while falling toward the water ({Scope.State})"))
            {
                yield return rig.GoHome(c);
                c.Report();
                yield break;
            }
            yield return WaitReal(() => player.IsSwimming(), 10f);
            yield return Frames(3);
            yield return new WaitForEndOfFrame();
            if (!c.Check(player.IsSwimming() && Scope.State == ScopeState.Idle && NotBack(player).Length == 0
                         && Near(cam.m_camera.fieldOfView, cam.m_fov, 0.01f),
                    $"swimming started with the spyglass up: the view is back ({Scope.State})"))
            {
                yield return rig.GoHome(c);
                c.Report();
                yield break;
            }

            // The splash is over, the swimmer rides the waves at the swimming height.
            float Under() => player.GetLiquidLevel() - player.m_swimDepth - player.transform.position.y;
            yield return new WaitForSeconds(1f);
            yield return WaitFor(() => player.IsSwimming() && Under() < 0.4f, 8f);
            yield return new WaitForSeconds(0.5f);
            var atRest = Under();
            c.Check(player.IsSwimming() && atRest < 0.6f, $"swimming at the surface before the dive ({F(atRest)} m under the swimming height)");

            // Real Crouch key held: Swim Dive takes the player under.
            var sent = SelfTest.Key(crouch, true);
            var deepest = atRest;
            var showed = "";
            var t0 = Time.time;
            while (Time.time - t0 < 4f)
            {
                yield return new WaitForFixedUpdate();
                deepest = Mathf.Max(deepest, Under());
                if (showed.Length == 0)
                {
                    showed = OnScreen();
                }
            }
            SelfTest.Key(crouch, false);
            c.Check(sent, $"the input driver held {crouch}");
            c.Check(deepest > atRest + 1f,
                $"right after the spyglass came down, holding the real Crouch key dives ({F(deepest)} m under the swimming height within 4 s, {F(atRest)} m before)");
            c.Check(showed.Length == 0, "nothing of the spyglass shows while diving" + (showed.Length == 0 ? "" : ": " + showed));
            c.Check(player.GetRightItem() == null && player.m_hiddenRightItem == spy, "the spyglass is still put away under water");

            // Key released: back up to the surface.
            t0 = Time.time;
            while (Time.time - t0 < 15f && !(player.IsSwimming() && Under() < 0.6f))
            {
                yield return new WaitForFixedUpdate();
            }
            c.Check(player.IsSwimming() && Under() < 0.6f, $"key released: back at the surface after {F(Time.time - t0)} s ({F(Under())} m under the swimming height)");
            var logged = ErrorsNamingMe(errors);
            c.Check(_tap != null && ProblemsSince(problems).Count == 0 && logged.Count == 0,
                "no warning or error from the spyglass meanwhile" + (logged.Count == 0 ? "" : ": " + First(logged)));
            yield return rig.GoHome(c);
            c.Report();
        }
        finally
        {
            SelfTest.ReleaseInput();
            rig.Restore();
        }
    }
}
#endif
