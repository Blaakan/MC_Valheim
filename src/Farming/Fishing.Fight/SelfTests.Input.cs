#if DEBUG
using System;
using System.Collections;
using BepInEx.Bootstrap;
using MC.Shared;
using UnityEngine;

namespace MC.Farming.FishingFightMod;

// Debug build only. Self tests with REAL gamepad input (same class as SelfTests.cs, rig of SelfTests.cs): the probe
// change the state of a test gamepad (SelfTest.Pad / PadStick), so ZInput, PlayerController and every mod read a press
// like from a person. One small test per thing: real input is new, each can be fixed or dropped alone.
//   fishing.pad.zone     T16 (as T03): block button of the Default layout (left trigger) held = the zone speed up to
//                        the top and stay, let go = fall, bounce, settle; fish inside = softer both ways (dry land)
//   fishing.pad.turn     T16 (as T07): trigger held + right stick turn the rod away from the run: line in at the
//                        normal cost, float still on screen; mirrored for a fish running left (shore, real water)
//   fishing.pad.trinket  X02: Trinkets on Demand on, stamina trinket worn, full bar: trigger held (reel) in a fight +
//                        right stick pressed = the trinket fire, the trigger keep reeling, stamina show the effect
// Me never drive the player here: PlayerController is on and read the pad. Layout forced to Default in memory only
// (no saved preference touched) and put back. Always SelfTest.ReleaseInput in finally (PadDone).
internal static partial class SelfTests
{
    private const string PadZoneName = "fishing.pad.zone";
    private const string PadTurnName = "fishing.pad.turn";
    private const string PadTrinketName = "fishing.pad.trinket";

    private const string TrinketsGuid = "MC.Combat.Trinkets.OnDemand";

    // Vanilla name (docs/game/combat.md, runtime item dump of 1.0.16).
    private const string StaminaTrinket = "TrinketBronzeStamina";

    // Smallest stamina gain the trinket test can tell from the reel that go on meanwhile (reel of a fighting Perch
    // take about 10 a second): at once, and per second. Smaller = that sign not used (a check on it could not fail).
    private const float ReadableUpFront = 5f;
    private const float ReadableOverTime = 2f;

    private static void RegisterInput()
    {
        SelfTest.Register(PadZoneName, RunPadZone);
        SelfTest.Register(PadTurnName, RunPadTurn);
        SelfTest.Register(PadTrinketName, RunPadTrinket);
    }

    private static void UnregisterInput()
    {
        SelfTest.Unregister(PadZoneName);
        SelfTest.Unregister(PadTurnName);
        SelfTest.Unregister(PadTrinketName);
    }

    // ---------- gamepad helpers ----------

    // Me = what a gamepad test changed outside the rig: the gamepad layout (memory only).
    private sealed class PadRig
    {
        internal InputLayout LayoutBefore;
        internal bool Forced;
    }

    // Input driver there (every input call say false when no probe drive the game), Default layout in force.
    private static bool PadStart(Checks c, PadRig pad)
    {
        pad.LayoutBefore = ZInput.InputLayout;
        if (!c.Check(SelfTest.ReleaseInput() && ZInput.instance != null,
                "an input driver is there (real gamepad presses need a run of tools/Test-InWorld.ps1)"))
        {
            return false;
        }
        if (pad.LayoutBefore != InputLayout.Default)
        {
            // Like Settings > Controls (ZInput.ChangeInputLayout) minus the saved preference.
            ZInput.InputLayout = InputLayout.Default;
            ZInput.instance.UpdateGamepadInputLayout();
            pad.Forced = true;
            c.Note($"this player's gamepad layout is {pad.LayoutBefore}: the Default layout is used for the test (in memory) and put back after");
        }
        return true;
    }

    // Finally of a gamepad test (no yield, never throw): everything up, test gamepad gone, layout back.
    private static void PadDone(PadRig pad)
    {
        try
        {
            SelfTest.ReleaseInput();
            if (pad.Forced && ZInput.instance != null)
            {
                pad.Forced = false;
                ZInput.InputLayout = pad.LayoutBefore;
                ZInput.instance.UpdateGamepadInputLayout();
            }
        }
        catch (Exception e)
        {
            Log.Warning($"Self test clean-up (gamepad) failed: {e.Message}");
        }
    }

    // Trigger that is the block button of the Default layout: the left one, or the right one for a player who
    // swapped the triggers in the game's settings (the game swap every binding of both).
    private static string BlockTrigger(Checks c)
    {
        if (!ZInput.SwapTriggers)
        {
            return "LeftTrigger";
        }
        c.Note("this player swapped the gamepad triggers in the game's settings: the block button is the right trigger");
        return "RightTrigger";
    }

    // Hold a control of the test gamepad until the game read it as the named button and look at the gamepad (first
    // input of a new pad can come in a frame where the game still look at the mouse: let go, press again).
    private static IEnumerator HoldPad(string control, string zinputName)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            SelfTest.Pad(control, 1f);
            var asked = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - asked < 0.7f && !(ZInput.IsGamepadActive() && ZInput.GetButton(zinputName)))
            {
                yield return null;
            }
            if (ZInput.IsGamepadActive() && ZInput.GetButton(zinputName))
            {
                yield break;
            }
            SelfTest.Pad(control, 0f);
            for (var i = 0; i < 4; i++)
            {
                yield return null;
            }
        }
    }

    // Wait until the player hold Block (or let it go), up to 1.5 s. Stamina kept full meanwhile.
    private static IEnumerator WaitBlock(FishRig rig, bool want)
    {
        var p = rig.P;
        var asked = Time.time;
        while (p.IsBlocking() != want && Time.time - asked < 1.5f)
        {
            rig.Fill(p.GetMaxStamina());
            yield return new WaitForFixedUpdate();
        }
    }

    private static IEnumerator Ticks(FishRig rig, int count)
    {
        for (var i = 0; i < count; i++)
        {
            rig.Fill(rig.P.GetMaxStamina());
            yield return new WaitForFixedUpdate();
        }
    }

    // Zone back at rest on the bottom (up to 6 s).
    private static IEnumerator ZoneAtRest(FishRig rig, CatchBar bar, Box result)
    {
        var asked = Time.time;
        while ((bar.ZonePos > 0f || bar.ZoneSpeed != 0f) && Time.time - asked < 6f)
        {
            rig.Fill(rig.P.GetMaxStamina());
            yield return new WaitForFixedUpdate();
        }
        result.Ok = bar.ZonePos <= 0f && bar.ZoneSpeed == 0f;
    }

    // Once the player hold Block (want) or let it go: two ticks, then the zone's speed change over 5 ticks = its
    // push (track per s^2) into into[index]. Not ok when the zone touched an end of the track meanwhile.
    private static IEnumerator ZonePush(FishRig rig, CatchBar bar, bool want, float[] into, int index, Box result)
    {
        var p = rig.P;
        yield return WaitBlock(rig, want);
        if (p.IsBlocking() != want)
        {
            result.Detail = want ? "the player never held Block" : "the player never let Block go";
            yield break;
        }
        yield return Ticks(rig, 2);
        var v0 = bar.ZoneSpeed;
        var pos0 = bar.ZonePos;
        var wall = false;
        var kept = true;
        for (var i = 0; i < 5; i++)
        {
            rig.Fill(p.GetMaxStamina());
            yield return new WaitForFixedUpdate();
            wall |= bar.ZonePos <= 0f || bar.ZonePos >= 1f - bar.ZoneSize;
            kept &= p.IsBlocking() == want;
        }
        into[index] = (bar.ZoneSpeed - v0) / (5f * Time.fixedDeltaTime);
        result.Ok = !wall && kept;
        result.Detail = $"speed {F(v0)} -> {F(bar.ZoneSpeed)} tracks/s, zone {F(pos0)} -> {F(bar.ZonePos)}";
    }

    // Look the rod must have: the line (fisher -> float) turned by yawOff degrees (negative = to the left).
    private static Vector3 WantLook(Player p, FishingFloat ff, float yawOff) =>
        Quaternion.Euler(0f, yawOff, 0f) * Flat(ff.transform.position - p.transform.position);

    // One frame of a player turning with the right stick: pushed toward the wanted look, less when close (the game
    // drops the first fifth of the stick's way). Returns the yaw still to go (deg, + = to the right).
    private static float SteerTo(Player p, Vector3 want)
    {
        var err = FightLogic.SignedYaw(p.m_lookYaw * Vector3.forward, want);
        var x = 0f;
        if (Mathf.Abs(err) > 3f)
        {
            x = Mathf.Sign(err) * Mathf.Clamp(Mathf.Abs(err) / 30f, 0.35f, 1f);
        }
        if (PlayerController.m_invertCameraX)
        {
            x = -x;
        }
        SelfTest.PadStick(true, new Vector2(x, 0f));
        return err;
    }

    private sealed class TurnResult
    {
        internal bool Reached;
        internal bool StickSeen;
        internal string Detail = "";
    }

    // Turn with the right stick until the look is yawOff degrees off the line and the fight judge the rod good.
    // Stamina kept full (reeling on the wrong side meanwhile cost a lot).
    private static IEnumerator TurnRod(FishRig rig, Fight fight, FishingFloat ff, float yawOff, float seconds, TurnResult result)
    {
        var p = rig.P;
        var start = Time.time;
        var yaw0 = p.m_lookYaw.eulerAngles.y;
        var err = 0f;
        while (ff != null && ReferenceEquals(Fight.Current, fight) && Time.time - start < seconds)
        {
            rig.Fill(p.GetMaxStamina());
            err = SteerTo(p, WantLook(p, ff, yawOff));
            yield return null;
            result.StickSeen |= Mathf.Abs(ZInput.GetJoyRightStickX()) > 0.05f;
            if (Mathf.Abs(err) <= 3f && fight.Verdict == RodVerdict.Good)
            {
                result.Reached = true;
                break;
            }
        }
        result.Detail = $"the look turned {F(Mathf.DeltaAngle(yaw0, p.m_lookYaw.eulerAngles.y))} deg in {F(Time.time - start)} s, "
                        + $"{F(err)} deg left to go, rod judged {fight.Verdict}, gamepad sensitivity {F(PlayerController.m_gamepadSens)}";
    }

    // ---------- fishing.pad.zone (T16 as T03) ----------

    private static IEnumerator RunPadZone() => RunLive(PadZoneName, false, PadZoneBody);

    private static IEnumerator PadZoneBody(Checks c, FishRig rig, Stage st, LogTap tap)
    {
        var p = rig.P;
        var pad = new PadRig();
        try
        {
            if (!PadStart(c, pad))
            {
                yield break;
            }
            var trigger = BlockTrigger(c);
            // Default rules, calm pinned, fish pinned off the bar first.
            ServerRules.TestRules = new FightRules();
            Fight.TestPhase = FightPhase.Calm;
            Fight.TestPin = FishPin.Away;
            FightHud.TestDisplay = DefaultDisplay;
            var hook = new Hooked();
            yield return HookNew(rig, st, 1, hook);
            var fight = hook.Fight;
            if (!c.Check(fight != null, "fight starts (float and Perch on dry land in front of the player)"))
            {
                yield break;
            }
            var bar = fight.Bar;
            var top = 1f - bar.ZoneSize;
            var h = FightHud.TestTrackHeight;
            // The game's own road from the pad to Player.SetControls (PlayerController): the test holds nothing itself.
            rig.Keys = true;
            rig.Keyboard(true);
            var rest = new Box();
            yield return ZoneAtRest(rig, bar, rest);
            c.Check(rest.Ok && !p.IsBlocking(), $"before: nothing held, the zone rests on the bottom (zone {F(bar.ZonePos)}, Block {p.IsBlocking()})");

            // Trigger held, fish outside the zone: the zone speeds up toward the top.
            yield return HoldPad(trigger, "JoyBlock");
            var read = ZInput.IsGamepadActive() && ZInput.GetButton("JoyBlock") && !ZInput.GetButton("Block");
            if (!c.Check(read,
                    $"the game reads the test gamepad's {trigger} as the block button of the Default layout (gamepad in use {ZInput.IsGamepadActive()}, gamepad Block {ZInput.GetButton("JoyBlock")}, keyboard Block {ZInput.GetButton("Block")})"))
            {
                yield break;
            }
            // 0 = up, fish outside; 1 = down, fish outside; 2 = up, fish inside; 3 = down, fish inside.
            var push = new float[4];
            var m = new Box();
            yield return ZonePush(rig, bar, true, push, 0, m);
            c.Check(m.Ok && Near(push[0], CatchBar.Lift, 0.1f),
                $"trigger held: the zone speeds up toward the top ({F(push[0])} tracks/s2, the bar's rule {F(CatchBar.Lift)}; {m.Detail}{(m.Ok ? "" : ", not measured cleanly")})");

            // ... stops at the top and stays while held.
            var heldAll = true;
            var asked = Time.time;
            while ((bar.ZonePos < top - 0.0005f || bar.ZoneSpeed != 0f) && Time.time - asked < 3f)
            {
                rig.Fill(p.GetMaxStamina());
                yield return new WaitForFixedUpdate();
                heldAll &= p.IsBlocking();
            }
            var arrived = Time.time - asked;
            var stayed = true;
            asked = Time.time;
            while (Time.time - asked < 0.5f)
            {
                rig.Fill(p.GetMaxStamina());
                yield return new WaitForFixedUpdate();
                heldAll &= p.IsBlocking();
                stayed &= Near(bar.ZonePos, top, 0.001f) && Near(bar.ZoneSpeed, 0f);
            }
            c.Check(heldAll && stayed,
                $"trigger held: the zone stops at the top and stays there (zone {F(bar.ZonePos)} of {F(top)}, there after {F(arrived)} s, Block held all along {heldAll})");
            yield return null;
            c.Check(FightHud.BarShown && Near(FightHud.TestZoneBottom, top * h, 2f),
                $"trigger held: the zone is drawn at the top of the track (bottom edge at {F(FightHud.TestZoneBottom)} of {F(h)})");

            // Let go: falls, bounces on the bottom, settles.
            SelfTest.Pad(trigger, 0f);
            m = new Box();
            yield return ZonePush(rig, bar, false, push, 1, m);
            c.Check(m.Ok && Near(push[1], -CatchBar.Gravity, 0.1f),
                $"trigger let go: the zone falls ({F(push[1])} tracks/s2, the bar's rule {F(-CatchBar.Gravity)}; {m.Detail}{(m.Ok ? "" : ", not measured cleanly")})");
            var bounced = false;
            asked = Time.time;
            while (!bounced && Time.time - asked < 3f)
            {
                rig.Fill(p.GetMaxStamina());
                yield return new WaitForFixedUpdate();
                bounced |= bar.ZonePos <= 0.0001f && bar.ZoneSpeed > 0f;
            }
            rest = new Box();
            yield return ZoneAtRest(rig, bar, rest);
            c.Check(bounced && rest.Ok && !p.IsBlocking(),
                $"trigger let go: the zone bounces on the bottom and settles (bounced {bounced}, at rest {rest.Ok})");

            // Fish inside the zone: softer up and down.
            Fight.TestPin = FishPin.InZone;
            yield return Ticks(rig, 2);
            var inside = bar.InZone;
            yield return HoldPad(trigger, "JoyBlock");
            m = new Box();
            yield return ZonePush(rig, bar, true, push, 2, m);
            var upOk = m.Ok;
            var upDetail = m.Detail;
            yield return Ticks(rig, 20);
            SelfTest.Pad(trigger, 0f);
            m = new Box();
            yield return ZonePush(rig, bar, false, push, 3, m);
            var softUp = push[0] > 0.01f ? push[2] / push[0] : 0f;
            var softDown = push[1] < -0.01f ? push[3] / push[1] : 0f;
            c.Check(inside && bar.InZone && upOk && m.Ok && softUp > 0.55f && softUp < 0.65f && softDown > 0.55f && softDown < 0.65f,
                $"with the fish inside the zone it rises and falls more softly (x{F(softUp)} up, x{F(softDown)} down, the bar's rule x{F(CatchBar.InZoneAccelScale)}; up: {upDetail}; down: {m.Detail})");
            yield return null;
            c.Check(ReferenceEquals(Fight.Current, fight) && fight.Phase == FightPhase.Calm && FightHud.BarShown,
                "the same fight is still on at the end, bar on screen");

            // Gamepad gone: the game goes back to keyboard and mouse (said, not judged: the driver's own test is probe.input).
            SelfTest.ReleaseInput();
            asked = Time.realtimeSinceStartup;
            while (ZInput.IsGamepadActive() && Time.realtimeSinceStartup - asked < 2f)
            {
                yield return null;
            }
            c.Note($"test gamepad removed: gamepad still in use {ZInput.IsGamepadActive()}");
        }
        finally
        {
            PadDone(pad);
        }
    }

    // ---------- fishing.pad.turn (T16 as T07) ----------

    private static IEnumerator RunPadTurn() => RunLive(PadTurnName, true, PadTurnBody);

    private static IEnumerator PadTurnBody(Checks c, FishRig rig, Stage st, LogTap tap)
    {
        var p = rig.P;
        var pad = new PadRig();
        try
        {
            if (!PadStart(c, pad))
            {
                yield break;
            }
            var trigger = BlockTrigger(c);
            var turn = FightRules.Default.RodAngle + 15f;
            // Default rules, a fight to the right pinned (side given by the test, like fishing.fight).
            ServerRules.TestRules = new FightRules();
            FightHud.TestDisplay = DefaultDisplay;
            Fight.TestPin = null;
            Fight.TestSide = FightLogic.Right;
            Fight.TestPhase = FightPhase.Struggle;
            var hook = new Hooked();
            yield return HookNew(rig, st, 1, hook);
            var fight = hook.Fight;
            var ff = hook.Float;
            var fish = hook.Fish;
            if (!c.Check(fight != null, "fight starts on the hooked Perch"))
            {
                yield break;
            }
            // Start like a player who looks at the float: rod along the line. From here only the gamepad moves the player.
            rig.LookLevel();
            rig.Face(Flat(ff.transform.position - p.transform.position));
            rig.Keys = true;
            rig.Keyboard(true);
            rig.Fill(p.GetMaxStamina());
            yield return HoldPad(trigger, "JoyBlock");
            var read = ZInput.IsGamepadActive() && ZInput.GetButton("JoyBlock") && !ZInput.GetButton("Block");
            if (!c.Check(read,
                    $"the game reads the test gamepad's {trigger} as the block button of the Default layout (gamepad in use {ZInput.IsGamepadActive()}, gamepad Block {ZInput.GetButton("JoyBlock")})"))
            {
                yield break;
            }
            yield return WaitBlock(rig, true);

            // Rod along the line: the wrong side for either run, no line comes in.
            var line0 = ff != null ? ff.m_lineLength : -1f;
            var wrongAll = true;
            var asked = Time.time;
            while (ff != null && ReferenceEquals(Fight.Current, fight) && Time.time - asked < 0.3f)
            {
                rig.Fill(p.GetMaxStamina());
                yield return new WaitForFixedUpdate();
                wrongAll &= fight.Reeling && fight.Verdict == RodVerdict.Wrong;
            }
            if (!c.Check(ff != null && fish != null && ReferenceEquals(Fight.Current, fight) && fight.Phase == FightPhase.Struggle,
                    "the fight is on with the trigger held"))
            {
                yield break;
            }
            c.Check(p.IsBlocking() && wrongAll && ff.m_lineLength >= line0 - 0.001f,
                $"trigger held with the rod along the line: reeling on the wrong side, no line comes in ({F(line0)} -> {F(ff.m_lineLength)} m)");

            var cost = FightLogic.ReelCost(ff.m_pullStaminaUse, fish.m_escapeStaminaUse, fight.Quality,
                ff.m_pullStaminaUseMaxSkillMultiplier, p.GetSkillFactor(Skills.SkillType.Fishing));
            for (var round = 0; round < 2; round++)
            {
                var side = round == 0 ? FightLogic.Right : FightLogic.Left;
                var runs = side > 0 ? "right" : "left";
                var rod = side > 0 ? "left" : "right";
                Fight.TestSide = side;
                var turned = new TurnResult();
                yield return TurnRod(rig, fight, ff, -turn * side, 6f, turned);
                if (ff == null || fish == null || !ReferenceEquals(Fight.Current, fight))
                {
                    c.Check(false, $"fish running {runs}: the fight ended while the rod was turned ({turned.Detail})");
                    yield break;
                }
                c.Check(turned.Reached && turned.StickSeen,
                    $"fish running {runs}: the right stick turns the rod about {F(turn)} deg to the {rod} of the line and the rod is judged right ({turned.Detail})");

                // Kept there with the stick like a player while the fish drags the float: line in, normal cost.
                rig.Fill(p.GetMaxStamina());
                yield return new WaitForFixedUpdate();
                var lineA = ff != null ? ff.m_lineLength : -1f;
                var staminaA = p.GetStamina();
                var tA = Time.time;
                var frames = 0;
                var good = 0;
                while (ff != null && ReferenceEquals(Fight.Current, fight) && Time.time - tA < 0.8f)
                {
                    SteerTo(p, WantLook(p, ff, -turn * side));
                    yield return null;
                    frames++;
                    if (fight.Reeling && fight.Verdict == RodVerdict.Good)
                    {
                        good++;
                    }
                }
                if (ff == null || !ReferenceEquals(Fight.Current, fight))
                {
                    c.Check(false, $"fish running {runs}: the fight ended during the reel on the right side");
                    yield break;
                }
                var rate = (staminaA - p.GetStamina()) / Mathf.Max(0.01f, Time.time - tA);
                c.Check(frames > 0 && good >= frames - 2 && p.IsBlocking(),
                    $"fish running {runs}: trigger held, the rod stays on the right side ({good} of {frames} frames)");
                c.Check(ff.m_lineLength < lineA - 0.05f,
                    $"fish running {runs}, rod to the {rod}: the line comes in ({F(lineA)} -> {F(ff.m_lineLength)} m in {F(Time.time - tA)} s)");
                c.Check(cost > 0.1f && Mathf.Abs(rate - cost) <= cost * 0.3f,
                    $"fish running {runs}, rod to the {rod}: stamina drops at the normal cost of reeling a fighting fish ({F(rate)}/s, expected {F(cost)}/s)");

                // The float is still on screen, toward the edge on the fish's side (the camera follows the look).
                var cam = GameCamera.instance != null ? GameCamera.instance.m_camera : null;
                var view = cam != null ? cam.WorldToViewportPoint(ff.transform.position) : Vector3.zero;
                var half = side > 0 ? view.x > 0.5f && view.x < 0.99f : view.x > 0.01f && view.x < 0.5f;
                c.Check(cam != null && view.z > 0f && half && view.y > 0.01f && view.y < 0.99f,
                    $"fish running {runs}, rod to the {rod}: the float stays on screen, toward the {runs} edge (viewport {F(view.x)}, {F(view.y)}, depth {F(view.z)})");
            }
            SelfTest.PadStick(true, Vector2.zero);
            SelfTest.Pad(trigger, 0f);
            yield return WaitBlock(rig, false);
            c.Check(!p.IsBlocking() && ReferenceEquals(Fight.Current, fight), "trigger let go: no more reeling, the fight goes on");
        }
        finally
        {
            PadDone(pad);
        }
    }

    // ---------- fishing.pad.trinket (X02) ----------

    private static IEnumerator RunPadTrinket() => RunLive(PadTrinketName, false, PadTrinketBody, TrinketsGuid);

    // Setting of another plugin as text (BepInEx config, none of its types). Null = plugin or setting not there.
    private static string SettingText(string guid, string section, string key)
    {
        if (!Chainloader.PluginInfos.TryGetValue(guid, out var info) || info.Instance == null)
        {
            return null;
        }
        var config = info.Instance.Config;
        foreach (var definition in config.Keys)
        {
            if (definition.Section == section && definition.Key == key)
            {
                var value = config[definition].BoxedValue;
                return value != null ? value.ToString() : null;
            }
        }
        return null;
    }

    // What the status effects on the player do to stamina, through the game's own questions (regen multiplier, and
    // the cost of a run, jump, attack, block, dodge, swim and sneak that would cost 10).
    private static float[] StaminaSigns(Player p)
    {
        var effects = p.GetSEMan();
        var regen = 1f;
        effects.ModifyStaminaRegen(ref regen);
        var run = 10f;
        effects.ModifyRunStaminaDrain(10f, ref run, Vector3.forward);
        var jump = 10f;
        effects.ModifyJumpStaminaUsage(10f, ref jump);
        var attack = 10f;
        effects.ModifyAttackStaminaUsage(10f, ref attack);
        var block = 10f;
        effects.ModifyBlockStaminaUsage(10f, ref block);
        var dodge = 10f;
        effects.ModifyDodgeStaminaUsage(10f, ref dodge);
        var swim = 10f;
        effects.ModifySwimStaminaUsage(10f, ref swim);
        var sneak = 10f;
        effects.ModifySneakStaminaUsage(10f, ref sneak);
        return new[] { regen, run, jump, attack, block, dodge, swim, sneak };
    }

    private static IEnumerator PadTrinketBody(Checks c, FishRig rig, Stage st, LogTap tap)
    {
        var p = rig.P;
        // No pass without a check: a skipped test must not read as proof.
        if (!c.Check(ModActive(TrinketsGuid),
                $"Trinkets on Demand is loaded and active in this run ({ModStateText(TrinketsGuid)}): without it nothing of this item is checked"))
        {
            yield break;
        }
        var pad = new PadRig();
        var worn = p.m_trinketItem;
        var adrenaline = p.m_adrenaline;
        ItemDrop.ItemData trinket = null;
        var hash = 0;
        var hadEffect = false;
        try
        {
            if (!PadStart(c, pad))
            {
                yield break;
            }
            var trigger = BlockTrigger(c);
            // Its gamepad pair is each player's own setting: the item is about the default, left trigger + right stick.
            var modifier = SettingText(TrinketsGuid, "Controls", "GamepadModifier");
            var button = SettingText(TrinketsGuid, "Controls", "GamepadButton");
            if (!c.Check(modifier == "LeftTrigger" && button == "RightStick",
                    $"Trinkets on Demand fires with left trigger + right stick, as the item says (this player's settings: GamepadModifier {modifier ?? "not found"}, GamepadButton {button ?? "not found"})"))
            {
                yield break;
            }

            // A stamina trinket worn, its effect not running.
            if (worn != null)
            {
                p.UnequipItem(worn, false);
            }
            trinket = rig.Give(StaminaTrinket);
            var effect = trinket != null && trinket.m_shared != null ? trinket.m_shared.m_fullAdrenalineSE : null;
            if (!c.Check(effect != null && p.EquipItem(trinket, false) && ReferenceEquals(p.m_trinketItem, trinket),
                    $"{StaminaTrinket} given and worn, and it has a full-bar effect"))
            {
                yield break;
            }
            p.UpdateModifiers();
            hash = effect.NameHash();
            hadEffect = p.GetSEMan().HaveStatusEffect(hash);
            p.GetSEMan().RemoveStatusEffect(hash, true);
            var max = p.GetMaxAdrenaline();
            var stats = effect as SE_Stats;
            var upFront = stats != null ? stats.m_staminaUpFront : 0f;
            var modifiers = stats != null
                            && (stats.m_staminaRegenMultiplier != 1f || stats.m_runStaminaDrainModifier != 0f
                                || stats.m_jumpStaminaUseModifier != 0f || stats.m_attackStaminaUseModifier != 0f
                                || stats.m_blockStaminaUseModifier != 0f || stats.m_blockStaminaUseFlatValue != 0f
                                || stats.m_dodgeStaminaUseModifier != 0f || stats.m_swimStaminaUseModifier != 0f
                                || stats.m_sneakStaminaUseModifier != 0f);
            if (stats != null)
            {
                c.Note($"{StaminaTrinket}: bar {F(max)}, effect '{effect.name}' for {F(effect.m_ttl)} s: stamina up front {F(stats.m_staminaUpFront)}, "
                       + $"over time {F(stats.m_staminaOverTime)}, regen x{F(stats.m_staminaRegenMultiplier)}, run {F(stats.m_runStaminaDrainModifier)}, "
                       + $"jump {F(stats.m_jumpStaminaUseModifier)}, attack {F(stats.m_attackStaminaUseModifier)}, block {F(stats.m_blockStaminaUseModifier)} "
                       + $"/ {F(stats.m_blockStaminaUseFlatValue)}, dodge {F(stats.m_dodgeStaminaUseModifier)}, swim {F(stats.m_swimStaminaUseModifier)}, "
                       + $"sneak {F(stats.m_sneakStaminaUseModifier)}");
            }
            // Stamina given bit by bit while the effect runs (per second).
            var overSeconds = stats != null ? (stats.m_staminaOverTimeDuration > 0f ? stats.m_staminaOverTimeDuration : effect.m_ttl) : 0f;
            var overRate = stats != null && stats.m_staminaOverTime > 0f && overSeconds > 0f
                ? (stats.m_staminaOverTimeIsFraction ? p.GetMaxStamina() * stats.m_staminaOverTime : stats.m_staminaOverTime) / overSeconds
                : 0f;
            // Stamina stand at half when the stick is pressed: no more than that half can show at once.
            var readUpFront = Mathf.Min(upFront, p.GetMaxStamina() * 0.5f) >= ReadableUpFront;
            var readOver = overRate >= ReadableOverTime;
            if (!c.Check(max > 0f && (readUpFront || modifiers || readOver),
                    $"{StaminaTrinket} gives a bar to fill ({F(max)}) and its effect changes stamina in a way a test can read (at least {F(ReadableUpFront)} stamina up front, at least {F(ReadableOverTime)} a second over time, regen or a stamina cost; up front {F(upFront)}, over time {F(overRate)} a second)"))
            {
                yield break;
            }

            // A fight to the right on dry land, rod already on the right side (the test faces it once; from here only
            // the gamepad acts).
            var turn = FightRules.Default.RodAngle + 15f;
            ServerRules.TestRules = new FightRules();
            FightHud.TestDisplay = DefaultDisplay;
            Fight.TestPin = null;
            Fight.TestSide = FightLogic.Right;
            Fight.TestPhase = FightPhase.Struggle;
            var hook = new Hooked();
            yield return HookNew(rig, st, 1, hook);
            var fight = hook.Fight;
            var ff = hook.Float;
            if (!c.Check(fight != null, "fight starts (float and Perch on dry land in front of the player)"))
            {
                yield break;
            }
            rig.LookLevel();
            rig.Face(WantLook(p, ff, -turn));
            rig.Keys = true;
            rig.Keyboard(true);
            rig.Fill(p.GetMaxStamina());
            yield return HoldPad(trigger, "JoyBlock");
            yield return WaitBlock(rig, true);
            yield return Ticks(rig, 15);
            if (!c.Check(ZInput.IsGamepadActive() && ZInput.GetButton("JoyBlock") && p.IsBlocking() && ff != null
                         && ReferenceEquals(Fight.Current, fight) && fight.Phase == FightPhase.Struggle && fight.Reeling,
                    $"trigger held in a fight: the player reels (gamepad in use {ZInput.IsGamepadActive()}, Block {p.IsBlocking()}, reeling {fight.Reeling})"))
            {
                yield break;
            }
            c.Check(p.TakeInput() && !Hud.InRadial() && !p.InPlaceMode(),
                "the player can take input (no window, no radial menu, no build mode)");
            // What the reel costs per second before the trinket (half a second, stamina not refilled meanwhile).
            rig.Fill(p.GetMaxStamina());
            yield return new WaitForFixedUpdate();
            var staminaB = p.GetStamina();
            var tB = Time.time;
            while (ff != null && ReferenceEquals(Fight.Current, fight) && Time.time - tB < 0.5f)
            {
                yield return new WaitForFixedUpdate();
            }
            var rateBefore = (staminaB - p.GetStamina()) / Mathf.Max(0.01f, Time.time - tB);
            c.Check(rateBefore > 2f, $"before the press the reel costs stamina ({F(rateBefore)} a second)");

            // Full bar, half the stamina: the press tells all.
            p.m_adrenaline = max;
            p.m_stamina = p.GetMaxStamina() * 0.5f;
            p.m_staminaRegenTimer = 5f;
            var signs0 = StaminaSigns(p);
            var blockAll = true;
            var reelAll = true;
            for (var i = 0; i < 3; i++)
            {
                yield return null;
                blockAll &= p.IsBlocking();
            }
            // Bar full, trigger held, stick not pressed yet: nothing fired by itself (else the press below prove nothing).
            c.Check(!p.GetSEMan().HaveStatusEffect(hash),
                "with the bar full and only the trigger held the trinket has not fired by itself (before the stick press)");
            var before = p.GetStamina();
            var room = p.GetMaxStamina() - before;
            var pressed = Time.time;
            var fired = false;
            var after = before;
            var took = 0f;
            SelfTest.Pad("RightStick", 1f);
            var asked = Time.realtimeSinceStartup;
            // Shorter than the game's own hold time of this button (0.33 s: radial menu).
            while (!fired && Time.realtimeSinceStartup - asked < 0.25f)
            {
                yield return null;
                blockAll &= p.IsBlocking();
                reelAll &= fight.Reeling;
                if (p.GetSEMan().HaveStatusEffect(hash))
                {
                    fired = true;
                    after = p.GetStamina();
                    took = Time.time - pressed;
                }
            }
            SelfTest.Pad("RightStick", 0f);
            var signs1 = StaminaSigns(p);
            c.Check(fired && p.m_adrenaline < max * 0.5f,
                $"trigger held + right stick pressed: the trinket fires (its effect is on the player {fired}, {F(took)} s after the press; adrenaline {F(p.m_adrenaline)} of {F(max)})");

            // Stamina shows the effect: what the effect says it does must be there.
            var shown = fired;
            var what = "";
            if (readUpFront)
            {
                // The reel went on meanwhile (some stamina used since 'before').
                var want = Mathf.Min(upFront, room) - 2f - 12f * took;
                var ok = after - before >= want;
                shown &= ok;
                what += $"stamina up front {F(upFront)}: {F(before)} -> {F(after)} of {F(p.GetMaxStamina())} ({(ok ? "seen" : "NOT seen")}); ";
            }
            if (modifiers)
            {
                var changed = false;
                for (var i = 0; i < signs0.Length; i++)
                {
                    changed |= Mathf.Abs(signs1[i] - signs0[i]) > 0.001f;
                }
                shown &= changed;
                what += $"regen x{F(signs0[0])} -> x{F(signs1[0])}, cost of a 10 stamina run / jump / attack / block / dodge / swim / sneak "
                        + $"{F(signs0[1])} / {F(signs0[2])} / {F(signs0[3])} / {F(signs0[4])} / {F(signs0[5])} / {F(signs0[6])} / {F(signs0[7])} -> "
                        + $"{F(signs1[1])} / {F(signs1[2])} / {F(signs1[3])} / {F(signs1[4])} / {F(signs1[5])} / {F(signs1[6])} / {F(signs1[7])} "
                        + $"({(changed ? "changed" : "NOT changed")}); ";
            }

            // The trigger keeps reeling: Block held on every frame through the press and for a second after.
            rig.Fill(p.GetMaxStamina());
            yield return new WaitForFixedUpdate();
            var staminaA = p.GetStamina();
            var tA = Time.time;
            while (ff != null && ReferenceEquals(Fight.Current, fight) && Time.time - tA < 1f)
            {
                yield return new WaitForFixedUpdate();
                blockAll &= p.IsBlocking();
                reelAll &= fight.Reeling;
            }
            var stillOn = ff != null && ReferenceEquals(Fight.Current, fight) && fight.Phase == FightPhase.Struggle;
            var used = staminaA - p.GetStamina();
            var rateAfter = used / Mathf.Max(0.01f, Time.time - tA);
            if (readOver)
            {
                // Same reel, less stamina gone per second: what the effect gives meanwhile (never more than the reel
                // takes: stamina was full at the start of the second).
                var want = 0.5f * Mathf.Min(overRate, rateBefore) - 0.5f;
                var ok = rateBefore - rateAfter >= want;
                shown &= ok;
                what += $"stamina over time {F(overRate)} a second: the reel took {F(rateBefore)} a second before, {F(rateAfter)} after ({(ok ? "seen" : "NOT seen")}); ";
            }
            c.Check(shown, "stamina shows the trinket's effect: " + (what.Length > 0 ? what : "the trinket did not fire"));
            c.Check(stillOn && blockAll && reelAll && ZInput.GetButton("JoyBlock"),
                $"the trigger keeps reeling: Block held on every frame through the stick press and for a second after, same fight (fight on {stillOn}, Block all along {blockAll}, reeling all along {reelAll})");
            c.Check(stillOn && (used > 2f || readOver),
                $"the reel goes on after the press: it still costs stamina ({F(used)} in {F(Time.time - tA)} s{(overRate > 0f ? ", while the trinket gives " + F(overRate) + " a second back" : "")})");
            // Guard only, no proof that the trinket used the press up: with the block trigger held (the game's
            // "alt keys" button of the Default layout) the normal game put no weapon away on a stick tap either
            // (Player.Update), and its radial menu want a hold of 0.33 s.
            c.Check(ReferenceEquals(p.GetCurrentWeapon(), st.Rod) && Utils.FindChild(p.transform, "_RodTop") != null && !Hud.InRadial(),
                "after the stick press the rod is still in the hands and no radial menu is open");
            SelfTest.Pad(trigger, 0f);
            yield return WaitBlock(rig, false);
        }
        finally
        {
            PadDone(pad);
            try
            {
                if (hash != 0 && !hadEffect)
                {
                    p.GetSEMan().RemoveStatusEffect(hash, true);
                }
                if (trinket != null && p.IsItemEquiped(trinket))
                {
                    p.UnequipItem(trinket, false);
                }
                if (worn != null && rig.Inv.ContainsItem(worn) && !p.IsItemEquiped(worn))
                {
                    p.EquipItem(worn, false);
                }
                p.UpdateModifiers();
                p.m_adrenaline = Mathf.Min(adrenaline, p.GetMaxAdrenaline());
            }
            catch (Exception e)
            {
                SelfTest.Note(PadTrinketName, "clean-up of the trinket failed: " + e.Message);
            }
        }
    }
}
#endif
