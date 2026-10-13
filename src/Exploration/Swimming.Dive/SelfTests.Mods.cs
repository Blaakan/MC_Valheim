#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SwimmingDiveMod;

// Debug build only. Live tests with another MC mod loaded in the same game (every MC mod is loaded in a test run).
// The other mod is found by GUID in the shared registry; missing or not active = the test fails (a skipped check is
// no proof). Never a type of the other mod: its effect in the game (hands, status effects, HUD objects), its log
// lines, or a static of its own read by name.
//   dive.dualwield       X02 Dual Wielding: a pair in hand, both put away while swimming and while hovering above the
//                        sea floor; the real hide key shows nothing while hovering, shows the pair where the diver
//                        touches the floor (touch faked with the field the game reads), rising puts both away again;
//                        no warning or error line of Dual Wielding
//   dive.sneak-icons     X03 Sneak Ambush: control on land (crouching shows the game's stealth mark and the mod's
//                        "Holding still" icon); Crouch held in water and under water with the tint: no stealth mark,
//                        no crouch icon of Sneak Ambush (Holding still, Fog, In the mist), never sneaking
//   dive.moveset-attack  X01 Weapon Moveset: at the sea floor Jump taps then an attack: the attack reaches the game's
//                        attack start, no jump attack; back on land a real jump then an attack is a jump attack again
internal static partial class SelfTests
{
    private const string DualWieldName = "dive.dualwield";
    private const string SneakIconsName = "dive.sneak-icons";
    private const string MovesetAttackName = "dive.moveset-attack";

    private const string DualWieldGuid = "MC.Combat.Weapons.DualWield";
    private const string SneakGuid = "MC.Combat.Sneak.Ambush";
    private const string SneakState = "MC.Combat.SneakAmbushMod.StealthState";

    // Vanilla item names (used by passing tests of Trinkets on Demand and Weapon Moveset).
    private const string PairWeapon = "KnifeCopper";
    private const string MovesetSword = "SwordIron";

    // ---------- shared tools ----------

    private static bool OtherModActive(string guid)
    {
        var view = FeatureRegistry.Find(guid);
        return view != null && view.Value.IsActive;
    }

    // Me listen to another plugin's own log lines while a test run (every level), and to error lines of any other
    // logger that name its code. Plugin found by GUID in BepInEx: log source name = its plugin name.
    private sealed class OtherModTap : ILogListener
    {
        private readonly object _gate = new object();
        private readonly List<KeyValuePair<LogLevel, string>> _lines = new List<KeyValuePair<LogLevel, string>>();
        private readonly string _source;
        private readonly string _namespace;
        private bool _on;

        internal readonly bool Found;
        internal readonly string Name;

        internal OtherModTap(string guid)
        {
            Name = guid;
            if (Chainloader.PluginInfos.TryGetValue(guid, out var info) && info != null && info.Instance != null)
            {
                Found = true;
                _source = info.Metadata.Name;
                _namespace = info.Instance.GetType().Namespace ?? guid;
                Name = _source;
                _on = true;
                BepInEx.Logging.Logger.Listeners.Add(this);
            }
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
                if (eventArgs.Source.SourceName != _source
                    && ((eventArgs.Level & (LogLevel.Error | LogLevel.Fatal)) == 0
                        || text.IndexOf(_namespace, StringComparison.Ordinal) < 0))
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

        // Warnings and errors (self-test result lines are not).
        internal int Problems(out string first)
        {
            var n = 0;
            first = "";
            lock (_gate)
            {
                foreach (var line in _lines)
                {
                    if ((line.Key & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) == 0
                        || line.Value.StartsWith(SelfTest.Prefix, StringComparison.Ordinal))
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

        internal void Stop()
        {
            if (_on)
            {
                _on = false;
                BepInEx.Logging.Logger.Listeners.Remove(this);
            }
        }

        public void Dispose()
        {
            _on = false;
        }
    }

    private static void CheckOtherLog(Checks c, OtherModTap other)
    {
        var n = other.Problems(out var first);
        c.Check(n == 0, $"{n} warning or error line(s) from {other.Name} during the test, first: {first}");
    }

    // What was in hand before the test goes back after the test weapons are taken away again.
    private static void KeepHands(DiveRig rig)
    {
        var player = rig.Player;
        var hadRight = player.m_rightItem ?? player.m_hiddenRightItem;
        var hadLeft = player.m_leftItem ?? player.m_hiddenLeftItem;
        rig.Undo("hands", () =>
        {
            if (hadRight != null && player.GetInventory().ContainsItem(hadRight))
            {
                player.EquipItem(hadRight, false);
            }
            if (hadLeft != null && player.GetInventory().ContainsItem(hadLeft))
            {
                player.EquipItem(hadLeft, false);
            }
        });
    }

    // Test Crouch held until the diver hovers above the sea floor (own speed 0 for 20 ticks, well under the surface).
    private static IEnumerator DiveToFloor(Player player, Box result)
    {
        DiveInput.TestUp = false;
        DiveInput.TestDown = true;
        var still = 0;
        var ticks = 0;
        while (ticks < 500 && still < 20)
        {
            yield return FixedTick;
            ticks++;
            still = DiveState.Diving && Mathf.Abs(DiveState.Vy) < 0.001f && Under(player) > 2f ? still + 1 : 0;
        }
        result.Value = FloorGap(player);
        result.Ok = still >= 20 && DiveState.Deep && result.Value > 0.03f && result.Value < 0.6f && player.IsSwimming();
        result.Detail = $"{F(result.Value)} m above the sea floor after {F(ticks * Time.fixedDeltaTime)} s, {F(Under(player))} m under "
                        + $"the rest height (deep {DiveState.Deep}, swimming {player.IsSwimming()})";
    }

    // One physics tick with the touch of something solid faked the way dive.floor does it (the fields the game reads).
    private static object TouchTick(Player player)
    {
        player.m_lastGroundTouch = 0f;
        return FixedTick;
    }

    // ---------- dive.dualwield (X02) ----------

    private static bool PairInHands(Player p, ItemDrop.ItemData a, ItemDrop.ItemData b) =>
        (ReferenceEquals(p.m_rightItem, a) && ReferenceEquals(p.m_leftItem, b))
        || (ReferenceEquals(p.m_rightItem, b) && ReferenceEquals(p.m_leftItem, a));

    private static bool PairPutAway(Player p, ItemDrop.ItemData a, ItemDrop.ItemData b) =>
        p.m_rightItem == null && p.m_leftItem == null
        && ((ReferenceEquals(p.m_hiddenRightItem, a) && ReferenceEquals(p.m_hiddenLeftItem, b))
            || (ReferenceEquals(p.m_hiddenRightItem, b) && ReferenceEquals(p.m_hiddenLeftItem, a)));

    private static string HandsText(Player p) =>
        $"right {(p.m_rightItem != null ? p.m_rightItem.m_shared.m_name : "empty")}, left "
        + $"{(p.m_leftItem != null ? p.m_leftItem.m_shared.m_name : "empty")}, put away right "
        + $"{(p.m_hiddenRightItem != null ? p.m_hiddenRightItem.m_shared.m_name : "none")}, put away left "
        + $"{(p.m_hiddenLeftItem != null ? p.m_hiddenLeftItem.m_shared.m_name : "none")}";

    private static IEnumerator RunDualWield()
    {
        yield return Settle();
        var s = Begin(DualWieldName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        try
        {
            if (!c.Check(OtherModActive(DualWieldGuid), "Dual Wielding is not loaded and active: nothing of TESTING.md X02 was checked"))
            {
                c.Report();
                yield break;
            }
            var other = new OtherModTap(DualWieldGuid);
            rig.Undo("other mod log tap", other.Stop);
            ApplyDefaults();
            CalmWind(rig);
            if (!InputReady(c))
            {
                c.Report();
                yield break;
            }
            if (!c.Check(KeyOf("Hide", out var hideKey, out var hidePath), $"the hide key is a key the test can press ('{hidePath}')"))
            {
                c.Report();
                yield break;
            }
            if (!FindWater(rig.Origin, 5.5f, 7.5f, 5f, 1f, out var spot, out var mapDepth))
            {
                c.Check(false, "no water 5.5 to 7.5 m deep with an even floor within 5 km of the start: not tested");
                c.Report();
                yield break;
            }
            c.Note($"water about {F(mapDepth)} m deep at {F(spot)} (WorldGenerator)");

            // The pair in hand on land first (the game refuses to equip while swimming).
            KeepHands(rig);
            var first = GiveItem(rig, PairWeapon);
            var second = GiveItem(rig, PairWeapon);
            if (!c.Check(first != null && second != null, $"two {PairWeapon} given"))
            {
                c.Report();
                yield break;
            }
            player.EquipItem(first, false);
            yield return Ticks(5);
            player.EquipItem(second, false);
            yield return Ticks(10);
            if (!c.Check(PairInHands(player, first, second), $"two {PairWeapon} are held as a pair on land ({HandsText(player)})"))
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
            c.Check(PairPutAway(player, first, second), $"swimming: both weapons of the pair are put away ({HandsText(player)})");

            // ---- hovering above the sea floor: both stay away, the hide key shows nothing ----
            var box = new Box();
            yield return DiveToFloor(player, box);
            if (!c.Check(box.Ok, "dive to the sea floor: " + box.Detail))
            {
                c.Report();
                yield break;
            }
            c.Check(PairPutAway(player, first, second), $"hovering above the sea floor: both stay put away ({HandsText(player)})");
            var hideSeen = false;
            SelfTest.Key(hideKey, true);
            for (var i = 0; i < 8; i++)
            {
                yield return FixedTick;
                hideSeen |= ZInput.GetButton("Hide");
            }
            SelfTest.Key(hideKey, false);
            yield return Ticks(25);
            c.Check(hideSeen && PairPutAway(player, first, second) && !player.IsOnGround(),
                $"hovering (nothing touched): the real hide key shows no weapon, as for a swimmer in the normal game (key read "
                + $"{hideSeen}; {HandsText(player)})");

            // ---- touching the floor (touch faked): the hide key shows the pair ----
            for (var i = 0; i < 10; i++)
            {
                yield return TouchTick(player);
            }
            var grounded = player.IsOnGround();
            hideSeen = false;
            SelfTest.Key(hideKey, true);
            for (var i = 0; i < 8; i++)
            {
                yield return TouchTick(player);
                hideSeen |= ZInput.GetButton("Hide");
            }
            SelfTest.Key(hideKey, false);
            var shown = false;
            for (var i = 0; i < 75 && !shown; i++)
            {
                yield return TouchTick(player);
                shown = PairInHands(player, first, second);
            }
            c.Check(grounded && hideSeen && shown && DiveState.Diving && DiveState.Deep,
                $"touching the sea floor (ground contact faked, on ground {grounded}): the real hide key shows the pair (key read "
                + $"{hideSeen}; {HandsText(player)})");
            // Still shown while the touch lasts (the game puts hand items away only when swimming and not on ground).
            for (var i = 0; i < 25; i++)
            {
                yield return TouchTick(player);
            }
            c.Check(!shown || PairInHands(player, first, second), $"still touching: the pair stays in hand ({HandsText(player)})");

            // ---- rising: both put away again ----
            DiveInput.TestDown = false;
            DiveInput.TestUp = true;
            yield return Ticks(50);
            c.Check(PairPutAway(player, first, second), $"rising from the sea floor: both put away again ({HandsText(player)})");
            yield return Surface(player, 8f, box);
            c.Check(box.Ok && PairPutAway(player, first, second),
                $"back at the surface ({box.Detail}): both still put away ({HandsText(player)})");
            SelfTest.ReleaseInput();
            yield return new WaitForSeconds(0.5f);

            yield return GoHome(s);
            CheckLog(s);
            CheckOtherLog(c, other);
        }
        finally
        {
            SelfTest.ReleaseInput();
            rig.Restore();
        }
        c.Report();
    }

    // ---------- dive.sneak-icons (X03) ----------

    // Sneak Ambush's icons are status effects named "<its GUID>.<cue>" (its StealthCues): looked up by that name.
    private static bool SneakCue(Player p, string cue) =>
        p.GetSEMan().HaveStatusEffect((SneakGuid + "." + cue).GetStableHashCode());

    // An icon of Sneak Ambush that needs crouching: Holding still, Fog, In the mist.
    private static bool SneakCrouchCue(Player p) => SneakCue(p, "HoldingStill") || SneakCue(p, "Fog") || SneakCue(p, "InMist");

    // The game's own stealth mark (eye and bar above the hotbar: Hud.UpdateStealth).
    private static bool StealthMarkOn()
    {
        var hud = Hud.instance;
        if (hud == null)
        {
            return false;
        }
        return (hud.m_hidden != null && hud.m_hidden.activeSelf) || (hud.m_targeted != null && hud.m_targeted.activeSelf)
               || (hud.m_targetedAlert != null && hud.m_targetedAlert.activeSelf)
               || (hud.m_stealthBar != null && hud.m_stealthBar.gameObject.activeSelf);
    }

    private static IEnumerator RunSneakIcons()
    {
        yield return Settle();
        var s = Begin(SneakIconsName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        try
        {
            if (!c.Check(OtherModActive(SneakGuid), "Sneak Ambush is not loaded and active: nothing of TESTING.md X03 was checked"))
            {
                c.Report();
                yield break;
            }
            var other = new OtherModTap(SneakGuid);
            rig.Undo("other mod log tap", other.Stop);
            ApplyDefaults();
            DiveInput.ClearTest();
            CalmWind(rig);
            var crouch = Button("Crouch");
            var jump = Button("Jump");
            if (!c.Check(crouch != null && jump != null && Hud.instance != null, "the game has the buttons Crouch and Jump and a HUD"))
            {
                c.Report();
                yield break;
            }
            if (!DeepSpot(s, out var spot))
            {
                c.Report();
                yield break;
            }

            // ---- control on land: crouching still shows the game's stealth mark and Sneak Ambush's "Holding still"
            // icon. Without it the "no icon" checks below would prove nothing ----
            var crouchedBefore = player.m_crouchToggled;
            rig.Undo("crouch", () => player.SetCrouch(crouchedBefore));
            if (!c.Check(!player.IsSwimming() && !StealthMarkOn() && !SneakCrouchCue(player),
                    $"before the control: standing on land, no stealth mark, no crouch icon of Sneak Ambush (mark {StealthMarkOn()})"))
            {
                c.Report();
                yield break;
            }
            player.SetCrouch(true);
            yield return WaitFor(() => StealthMarkOn() && SneakCue(player, "HoldingStill"), 7f);
            var markControl = StealthMarkOn();
            var cueControl = SneakCue(player, "HoldingStill");
            player.SetCrouch(false);
            yield return WaitFor(() => !StealthMarkOn() && !SneakCrouchCue(player), 5f);
            if (!c.Check(markControl && cueControl,
                    $"control on land, crouching still: the game's stealth mark shows ({markControl}) and Sneak Ambush's \"Holding "
                    + $"still\" icon is on the player ({cueControl}; its icons may be turned off in its settings, or its still "
                    + "bonus set to 0: then nothing can be checked here)"))
            {
                c.Report();
                yield break;
            }
            c.Check(!StealthMarkOn() && !SneakCrouchCue(player), "control: standing up again takes the stealth mark and the icon away");

            rig.Undo("keys", ReleaseKeys);
            var ok = new Box();
            yield return EnterWater(s, spot, false, ok);
            if (!ok.Ok || !TakesInput(c, player))
            {
                c.Report();
                yield break;
            }

            // ---- Crouch held at the surface: a dive, never a sneak: no mark, no icon ----
            var mark = false;
            var cue = false;
            var sneaked = false;
            crouch.Press();
            for (var i = 0; i < 125; i++)
            {
                yield return FixedTick;
                mark |= StealthMarkOn();
                cue |= SneakCrouchCue(player);
                sneaked |= player.IsCrouching();
            }
            crouch.Release();
            c.Check(DiveState.Diving && DiveState.Deep && Under(player) > 1.5f,
                $"Crouch key held 2.5 s at the surface: dives ({F(Under(player))} m under the rest height)");
            c.Check(!mark && !cue && !sneaked && !player.m_crouchToggled,
                $"Crouch held in water: no stealth mark of the game ({mark}), no crouch icon of Sneak Ambush ({cue}), never "
                + $"sneaking ({sneaked})");

            // ---- under water with the tint: no fog icon, no mark ----
            yield return Ticks(40);
            yield return ViewSettled(2);
            var tintOn = UnderwaterView.Active && UnderwaterView.FogApplied;
            var density = RenderSettings.fogDensity;
            mark = false;
            cue = false;
            var fogCue = false;
            for (var i = 0; i < 150; i++)
            {
                yield return FixedTick;
                mark |= StealthMarkOn();
                cue |= SneakCrouchCue(player);
                fogCue |= SneakCue(player, "Fog");
                tintOn &= UnderwaterView.FogApplied;
            }
            c.Check(tintOn, $"under water for 3 s with the tint on the whole time (fog density on screen {F(density)})");
            c.Check(!fogCue && !cue && !mark,
                $"under water with the tint (fog density on screen {F(density)}): no Fog icon of Sneak Ambush ({fogCue}), no other "
                + $"crouch icon ({cue}), no stealth mark ({mark})");
            // Its state is read by name. Not readable = a failed check, never a quiet skip: dive.view read the same
            // names for its fog compare and only write a NOTE when it cannot.
            var haveFlag = TryReadStatic(SneakGuid, SneakState, "FogShown", out var fogShown) && fogShown is bool;
            c.Check(haveFlag && !(bool)fogShown,
                haveFlag
                    ? "Sneak Ambush's own \"fog icon wanted\" state is off under water"
                    : "Sneak Ambush's \"fog icon wanted\" state could not be read by name (StealthState.FogShown): not checked");
            var sneakFog = SneakFog();
            c.Check(sneakFog.HasValue && (sneakFog.Value < density - 0.001f || UnderwaterView.BaseDensity >= density - 0.0001f),
                sneakFog.HasValue
                    ? $"the fog Sneak Ambush counts ({F(sneakFog.Value)}) is the weather's, not the tint on screen (density "
                      + $"{F(density)}; the weather's own fog under the tint is {F(UnderwaterView.BaseDensity)})"
                    : "the fog Sneak Ambush counts could not be read by name (StealthState.FogDensity): not checked here, and "
                      + "dive.view skipped its fog compare too");

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
            CheckOtherLog(c, other);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- dive.moveset-attack (X01) ----------

    // Weapon Moveset's last started move, read by name: the time it started (changes with every move) and its kind
    // ("Jump", "Roll").
    private static bool MovesetLastMove(out float at, out string kind)
    {
        at = -1f;
        kind = "";
        if (!TryReadStatic(MovesetGuid, MovesetTracker, "LastStartAt", out var a) || !(a is float startAt))
        {
            return false;
        }
        at = startAt;
        if (TryReadStatic(MovesetGuid, MovesetTracker, "LastMove", out var move) && move != null)
        {
            var field = move.GetType().GetField("Kind", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var value = field != null ? field.GetValue(move) : null;
            kind = value != null ? value.ToString() : "";
        }
        return true;
    }

    // Attack starts of the local player counted while the counter patch is on (Harmony prefix of Humanoid.StartAttack,
    // the method where Weapon Moveset decides about a jump attack).
    private static int _attackStarts;

    private static void CountAttackStart(Humanoid __instance)
    {
        if (ReferenceEquals(__instance, Player.m_localPlayer))
        {
            _attackStarts++;
        }
    }

    private static IEnumerator RunMovesetAttack()
    {
        yield return Settle();
        var s = Begin(MovesetAttackName);
        if (s == null)
        {
            yield break;
        }
        var c = s.C;
        var rig = s.Rig;
        var player = s.P;
        try
        {
            if (!c.Check(OtherModActive(MovesetGuid) && MovesetLastMove(out _, out _) && JumpToken(out _, out _),
                    "Weapon Moveset is not loaded and active (or its move state cannot be read by name): nothing of TESTING.md X01 "
                    + "was checked"))
            {
                c.Report();
                yield break;
            }
            var other = new OtherModTap(MovesetGuid);
            rig.Undo("other mod log tap", other.Stop);
            ApplyDefaults();
            DiveInput.ClearTest();
            CalmWind(rig);
            var jump = Button("Jump");
            var attack = Button("Attack");
            var hide = Button("Hide");
            if (!c.Check(jump != null && attack != null && hide != null, "the game has the buttons Jump, Attack and Hide"))
            {
                c.Report();
                yield break;
            }
            if (!FindWater(rig.Origin, 5.5f, 7.5f, 5f, 1f, out var spot, out var mapDepth))
            {
                c.Check(false, "no water 5.5 to 7.5 m deep with an even floor within 5 km of the start: not tested");
                c.Report();
                yield break;
            }
            c.Note($"water about {F(mapDepth)} m deep at {F(spot)} (WorldGenerator)");
            KeepHands(rig);
            var sword = GiveItem(rig, MovesetSword);
            if (!c.Check(sword != null, $"{MovesetSword} given"))
            {
                c.Report();
                yield break;
            }
            player.EquipItem(sword, false);
            yield return Ticks(10);
            if (!c.Check(ReferenceEquals(player.m_rightItem, sword), $"{MovesetSword} is in the right hand on land"))
            {
                c.Report();
                yield break;
            }
            rig.Undo("keys", () =>
            {
                ReleaseKeys();
                attack.Release();
                hide.Release();
            });
            rig.SaveSkill(Skills.SkillType.Jump);
            var counter = new HarmonyLib.Harmony(ModInfo.Guid + ".selftest.attack");
            rig.Undo("attack start counter", counter.UnpatchSelf);
            counter.Patch(HarmonyLib.AccessTools.Method(typeof(Humanoid), nameof(Humanoid.StartAttack)),
                prefix: new HarmonyLib.HarmonyMethod(typeof(SelfTests), nameof(CountAttackStart)));

            var ok = new Box();
            yield return EnterWater(s, spot, false, ok);
            if (!ok.Ok || !TakesInput(c, player))
            {
                c.Report();
                yield break;
            }

            // ---- a real jump at the surface first (touch faked, not diving): Weapon Moveset arms its jump token. A
            // swimmer never lands, so the token is still live when the dive begins: the "jump in, dive at once" case ----
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
            JumpToken(out var liveAtSurface, out _);
            c.Check(jumped && liveAtSurface,
                $"before the dive: a real jump at the surface (touch faked, not diving) arms Weapon Moveset's jump token (jump "
                + $"{jumped}, token live {liveAtSurface})");
            yield return new WaitForSeconds(2.5f);

            // ---- at the sea floor, touching it (touch faked): Jump taps, then an attack ----
            var box = new Box();
            yield return DiveToFloor(player, box);
            if (!c.Check(box.Ok, "dive to the sea floor: " + box.Detail))
            {
                c.Report();
                yield break;
            }
            JumpToken(out var liveUnder, out _);
            c.Note($"hovering above the sea floor: jump token live {liveUnder} (from the jump at the surface)");
            MovesetLastMove(out var moveBefore, out _);
            JumpToken(out _, out var tokenBefore);
            jumpTimer = player.m_jumpTimer;
            jumped = false;
            for (var press = 0; press < 3; press++)
            {
                jump.Press();
                for (var i = 0; i < 8; i++)
                {
                    player.m_hitWorldTime = 0f;
                    yield return TouchTick(player);
                    jumped |= player.m_jumpTimer < jumpTimer;
                    jumpTimer = player.m_jumpTimer;
                }
                jump.Release();
                for (var i = 0; i < 8; i++)
                {
                    player.m_hitWorldTime = 0f;
                    yield return TouchTick(player);
                    jumped |= player.m_jumpTimer < jumpTimer;
                    jumpTimer = player.m_jumpTimer;
                }
            }
            JumpToken(out _, out var tokenAfter);
            c.Check(DiveState.Diving && !jumped && tokenAfter == tokenBefore,
                $"Jump key tapped 3 times at the sea floor (touch faked): no jump, Weapon Moveset arms no jump token (jump "
                + $"{jumped}, last jump time {F(tokenBefore)} -> {F(tokenAfter)})");
            // Weapon out (the hide key works where the diver touches the floor), then the attack key.
            hide.Press();
            for (var i = 0; i < 4; i++)
            {
                yield return TouchTick(player);
            }
            hide.Release();
            var drawn = false;
            for (var i = 0; i < 75 && !drawn; i++)
            {
                yield return TouchTick(player);
                drawn = ReferenceEquals(player.m_rightItem, sword);
            }
            for (var i = 0; i < 40; i++)
            {
                yield return TouchTick(player);
            }
            _attackStarts = 0;
            attack.Press();
            for (var i = 0; i < 10; i++)
            {
                yield return TouchTick(player);
            }
            attack.Release();
            for (var i = 0; i < 65; i++)
            {
                yield return TouchTick(player);
            }
            var startsUnder = _attackStarts;
            MovesetLastMove(out var moveUnder, out var kindUnder);
            c.Check(drawn && startsUnder > 0,
                $"attack key at the sea floor after the Jump taps: the attack reached the game's attack start (weapon drawn {drawn}, "
                + $"attack starts {startsUnder}); else \"no jump attack\" would prove nothing");
            c.Check(moveUnder == moveBefore && DiveState.Diving,
                $"attack at the sea floor after the Jump taps: no jump attack, Weapon Moveset started no move (last move time "
                + $"{F(moveBefore)} -> {F(moveUnder)}, kind '{kindUnder}')");

            // ---- back up and home ----
            DiveInput.TestUp = true;
            yield return Surface(player, 10f, box);
            c.Check(box.Ok, "back at the surface: " + box.Detail);
            DiveInput.ClearTest();
            yield return GoHome(s);
            if (!rig.Back)
            {
                c.Report();
                yield break;
            }

            // ---- on land again: a real jump, then an attack in the air = a jump attack as usual ----
            var landEnd = Time.time + 6f;
            while (Time.time < landEnd && (player.IsSwimming() || !player.IsOnGround() || player.InAttack()))
            {
                yield return FixedTick;
            }
            ApplyDefaults();
            if (!ReferenceEquals(player.m_rightItem, sword))
            {
                hide.Press();
                yield return Ticks(4);
                hide.Release();
                yield return WaitFor(() => ReferenceEquals(player.m_rightItem, sword), 2f);
            }
            yield return Ticks(50);
            MovesetLastMove(out var moveLand, out _);
            jumpTimer = player.m_jumpTimer;
            var landJump = false;
            _attackStarts = 0;
            jump.Press();
            for (var i = 0; i < 6; i++)
            {
                yield return FixedTick;
                landJump |= player.m_jumpTimer < jumpTimer;
                jumpTimer = player.m_jumpTimer;
            }
            jump.Release();
            attack.Press();
            for (var i = 0; i < 10; i++)
            {
                yield return FixedTick;
            }
            attack.Release();
            var kindLand = "";
            var moveNow = moveLand;
            for (var i = 0; i < 60 && moveNow == moveLand; i++)
            {
                yield return FixedTick;
                MovesetLastMove(out moveNow, out kindLand);
            }
            MovesetLastMove(out moveNow, out kindLand);
            c.Check(ReferenceEquals(player.m_rightItem, sword) && landJump && _attackStarts > 0 && moveNow != moveLand && kindLand == "Jump",
                $"back on land after the swim: a real jump, then the attack key in the air, is a jump attack again (weapon in hand "
                + $"{ReferenceEquals(player.m_rightItem, sword)}, jump {landJump}, attack starts {_attackStarts}, Weapon Moveset's last "
                + $"move time {F(moveLand)} -> {F(moveNow)}, kind '{kindLand}'; its jump attack may be turned off in its settings)");
            var calmEnd = Time.time + 4f;
            while (Time.time < calmEnd && (player.InAttack() || !player.IsOnGround()))
            {
                yield return FixedTick;
            }
            counter.UnpatchSelf();
            ReleaseKeys();
            CheckLog(s);
            CheckOtherLog(c, other);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }
}
#endif
