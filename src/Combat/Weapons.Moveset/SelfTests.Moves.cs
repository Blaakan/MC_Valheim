#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.WeaponsMovesetMod;

// Debug build only. More in-world self tests of the moves (single player), one per TESTING.md item or small group:
//   moveset.lines          T01 / T06: the Debug lines word for word with the default numbers, the sword jump attack
//                          really hits the dummy, press right after a roll
//   moveset.aim-hit        T05: knife jump attack sweeps down onto a Boar in the air; a two-handed sword jump attack
//                          that hits after touchdown is level and strikes the dummy
//   moveset.numbers        T09 (+ M03's hit): damage x3 on the hit, stagger x10 staggers a Troll in one roll attack
//                          (never one normal swing), push x5 on a Greyling; the hit survives the wire copy
//   moveset.air            T03: second attack of the same air time is a normal swing
//   moveset.combo          T10: secondary after a jump, ground combo untouched, first swing after a jump attack,
//                          knife_secondary starts a new combo
//   moveset.settings       T12 / T15: README default table, typo in the config file, settings change path, live
//                          animation and switch changes through own settings (in memory)
//   moveset.cooldown       T16: default never blocks the roll loop, 5 s blocks with its Debug line, 0 = back to back
//   moveset.stamina-jump   T13: jump attack cost, x3, fallback to a normal swing, no stamina = vanilla fail
//   moveset.facing         T14: roll attack turns to the camera or to the stick, no stick = no turn
//   moveset.gamepad        T17: Alternative layouts jump from crouch / block, Default layout rolls
//   moveset.interrupt      T18: a roll that starts while a roll attack is starting drops it cleanly
//   moveset.flow           T19: FlowStart 2 / 0.7 / 0, FlowBlend 0 / 0.15 / 0.4 (blend length, same hit time)
//   moveset.iframes        T20: real hits during the roll attack hurt, hits during the roll's i-frames do not
//   moveset.after-roll     T21: battleaxe, RollAttack off and secondary after a roll keep the vanilla stand-up
//   moveset.crouch-roll    T25: roll from a crouch (sneak roll): the roll attack flows out of it too (cut into the
//                          roll, or out of its last blend), never after a pose; NOTE next to a standing roll
//   moveset.ledge          roll that ends in the air: roll attack that flows, or normal swing; never a jump attack
//   moveset.ledge-iframes  same, lifted while invulnerable: the roll attack keeps its margin after the i-frames
//   moveset.toggle         L01: mod off and on in memory while a move plays
//   moveset.log            L03: no error from the mod since the game started, only the warnings tests provoke
// Rules only through ServerRules.TestRules or MoveRules.TestOwn (never the config file); everything put back.
internal static partial class SelfTests
{
    private const string LinesName = "moveset.lines";
    private const string AimHitName = "moveset.aim-hit";
    private const string NumbersName = "moveset.numbers";
    private const string AirName = "moveset.air";
    private const string ComboName = "moveset.combo";
    private const string SettingsName = "moveset.settings";
    private const string CooldownName = "moveset.cooldown";
    private const string StaminaJumpName = "moveset.stamina-jump";
    private const string FacingName = "moveset.facing";
    private const string GamepadName = "moveset.gamepad";
    private const string InterruptName = "moveset.interrupt";
    private const string FlowName = "moveset.flow";
    private const string IframesName = "moveset.iframes";
    private const string AfterRollName = "moveset.after-roll";
    private const string CrouchRollName = "moveset.crouch-roll";
    private const string LedgeName = "moveset.ledge";
    private const string LedgeIframesName = "moveset.ledge-iframes";
    private const string ToggleName = "moveset.toggle";
    private const string LogName = "moveset.log";

    private const string BlockerText = "Inactive: turned off by its own self test (in memory only).";

    private static void RegisterMoves()
    {
        SelfTest.Register(LinesName, RunLines);
        SelfTest.Register(AimHitName, RunAimHit);
        SelfTest.Register(NumbersName, RunNumbers);
        SelfTest.Register(AirName, RunAir);
        SelfTest.Register(ComboName, RunCombo);
        SelfTest.Register(SettingsName, RunSettings);
        SelfTest.Register(CooldownName, RunCooldown);
        SelfTest.Register(StaminaJumpName, RunStaminaJump);
        SelfTest.Register(FacingName, RunFacing);
        SelfTest.Register(GamepadName, RunGamepad);
        SelfTest.Register(InterruptName, RunInterrupt);
        SelfTest.Register(FlowName, RunFlow);
        SelfTest.Register(IframesName, RunIframes);
        SelfTest.Register(AfterRollName, RunAfterRoll);
        SelfTest.Register(CrouchRollName, RunCrouchRoll);
        SelfTest.Register(LedgeName, RunLedge);
        SelfTest.Register(LedgeIframesName, RunLedgeIframes);
        SelfTest.Register(ToggleName, RunToggle);
    }

    private static void UnregisterMoves()
    {
        SelfTest.Unregister(LinesName);
        SelfTest.Unregister(AimHitName);
        SelfTest.Unregister(NumbersName);
        SelfTest.Unregister(AirName);
        SelfTest.Unregister(ComboName);
        SelfTest.Unregister(SettingsName);
        SelfTest.Unregister(CooldownName);
        SelfTest.Unregister(StaminaJumpName);
        SelfTest.Unregister(FacingName);
        SelfTest.Unregister(GamepadName);
        SelfTest.Unregister(InterruptName);
        SelfTest.Unregister(FlowName);
        SelfTest.Unregister(IframesName);
        SelfTest.Unregister(AfterRollName);
        SelfTest.Unregister(CrouchRollName);
        SelfTest.Unregister(LedgeName);
        SelfTest.Unregister(LedgeIframesName);
        SelfTest.Unregister(ToggleName);
    }

    private static void RegisterLog() => SelfTest.Register(LogName, RunLog);

    private static void UnregisterLog() => SelfTest.Unregister(LogName);

    // ---------- shared steps ----------

    private static Row RowOf(string prefab)
    {
        foreach (var row in Rows)
        {
            if (row.Prefab == prefab)
            {
                return row;
            }
        }
        return null;
    }

    private static MoveRules Loose()
    {
        var rules = MoveRules.Defaults();
        rules.Cooldown = 0f; // steps come fast one after other; the cooldown has its own test
        return rules;
    }

    private static BaseUnityPlugin PluginInstance() =>
        Chainloader.PluginInfos.TryGetValue(ModInfo.Guid, out var info) ? info.Instance : null;

    // This attack over: done, or no longer the player's current one.
    private static IEnumerator AttackOver(Player p, Attack a, float seconds)
    {
        if (a == null)
        {
            yield break;
        }
        var until = Time.fixedTime + seconds;
        while (Time.fixedTime < until && !a.m_attackDone && ReferenceEquals(p.m_currentAttack, a))
        {
            yield return Fixed;
        }
    }

    // Down without fall damage (me lifted the player): the game measures the fall from the highest point, me keep
    // that point close above the player until the feet touch.
    private static IEnumerator SoftLand(Player p, float seconds)
    {
        var until = Time.fixedTime + seconds;
        while (Time.fixedTime < until && !p.IsOnGround())
        {
            p.m_maxAirAltitude = Mathf.Min(p.m_maxAirAltitude, p.transform.position.y + 2f);
            yield return Fixed;
        }
        p.m_maxAirAltitude = Mathf.Min(p.m_maxAirAltitude, p.transform.position.y + 2f);
    }

    // Normal first swing on the ground with the foe put in front of the hit event. hit = what the foe took (null: none).
    private static IEnumerator GroundHit(Rig rig, Character foe, Box<HitRec> hit, bool park = true)
    {
        var p = rig.P;
        hit.Value = null;
        yield return Settle(rig);
        HitTap.Present = foe;
        var from = HitTap.Hits.Count;
        var last = p.m_currentAttack;
        var box = new Box<Attack>();
        Press(p);
        yield return WaitNewAttack(p, last, 0.6f, box);
        var a = box.Value;
        var until = Time.fixedTime + 2f;
        while (a != null && Time.fixedTime < until && !a.m_attackDone && ReferenceEquals(p.m_currentAttack, a)
               && HitTap.OnTarget(foe, from).Count == 0)
        {
            yield return Fixed;
        }
        var hits = HitTap.OnTarget(foe, from);
        hit.Value = hits.Count > 0 ? hits[0] : null;
        HitTap.Present = null;
        if (park)
        {
            Place(foe, ParkSpot(rig, 3)); // out of the way of the next roll
        }
    }

    // Roll attack (buffered press) with the foe put in front of the hit event.
    private static IEnumerator RollHit(Rig rig, Character foe, RollRun run, Box<HitRec> hit, bool park = true)
    {
        var p = rig.P;
        hit.Value = null;
        yield return Settle(rig, ServerRules.Current);
        HitTap.Present = foe;
        var from = HitTap.Hits.Count;
        yield return Roll(rig, run);
        p.m_queuedAttackTimer = 0f;
        var a = run.Started;
        var until = Time.fixedTime + 2f;
        while (a != null && Time.fixedTime < until && !a.m_attackDone && ReferenceEquals(p.m_currentAttack, a)
               && HitTap.OnTarget(foe, from).Count == 0)
        {
            yield return Fixed;
        }
        var hits = HitTap.OnTarget(foe, from);
        hit.Value = hits.Count > 0 ? hits[0] : null;
        HitTap.Present = null;
        if (park)
        {
            Place(foe, ParkSpot(rig, 3));
        }
    }

    // ---------- moveset.lines ----------

    private const string JumpDefaults = "damage x1.2, stagger x2, push x1, stamina x1, aim 30°";
    private const string RollDefaults = "damage x1.3, stagger x1.5, push x1, stamina x1";

    private static readonly Regex EntryLine = new Regex(
        @"^Jump attack swing_longsword2 started after \d+(\.\d+)? s; the next attack starts the combo at its first swing\.$");

    private static readonly Regex CutLine = new Regex(
        @"^Roll attack: SwordIron \(Swords\) plays swing_longsword1; " + RollDefaults.Replace(".", @"\.")
        + @"; cut into the roll (\d+(?:\.\d+)?) s after it started "
        + @"\(cross-fade from the roll, (?:state from the animation controller|learned state)\); (?:own|self-test) settings\.$");

    private static readonly Regex AfterLine = new Regex(
        @"^Roll attack: SwordIron \(Swords\) plays swing_longsword1; " + RollDefaults.Replace(".", @"\.")
        + @"; (\d+(?:\.\d+)?) s after the roll ended "
        + @"\(cross-fade from the roll, (?:state from the animation controller|learned state)\); (?:own|self-test) settings\.$");

    private static float Number(Match m) =>
        m.Success ? float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : -1f;

    private static IEnumerator RunLines()
    {
        var p = LocalPlayer(LinesName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(LinesName);
        var rig = new Rig(LinesName, p);
        try
        {
            // The item's numbers are the defaults. Own settings when they are the defaults (lines then end "own
            // settings."), else the defaults forced in memory (lines end "self-test settings.") and me say so.
            var own = ServerRules.Current;
            c.Check(ServerRules.Source == RulesSource.Own && MoveEdit.SourceText(RulesSource.Own) == "own",
                "single player: the settings in force are this game's own, named \"own settings\" in the Debug lines");
            var rules = own;
            if (!SameRules(own, MoveRules.Defaults()))
            {
                rules = MoveRules.Defaults();
                ServerRules.TestRules = rules;
                SelfTest.Note(LinesName, "this game's own move settings are not the defaults: the defaults are forced in memory, "
                                         + "so the Debug lines end \"self-test settings.\" instead of \"own settings.\"");
            }
            var source = MoveEdit.SourceText(ServerRules.Source);
            var held = new Box<ItemDrop.ItemData>();
            yield return Equip(rig, SwordRow, held);
            var weapon = held.Value;
            if (weapon == null)
            {
                c.Check(false, "could not equip SwordIron");
                c.Report();
                yield break;
            }
            var shared = weapon.m_shared.m_attack;
            var dummy = rig.SpawnDummy();
            c.Check(dummy != null, "training dummy spawned");
            HitTap.Start();
            var box = new Box<Attack>();

            // T01: jump attack, looking down at the dummy (level swings pass over it at the top of the jump). Later
            // presses only when the first one missed: the hit may land in the air or right after landing.
            HitRec landed = null;
            var tries = "";
            var delays = new[] { 0.1f, 0.3f, 0.45f };
            for (var i = 0; i < delays.Length && landed == null; i++)
            {
                yield return Settle(rig, rules);
                rig.PlaceDummy(dummy);
                Look(p, 35f);
                yield return Fixed;
                yield return Fixed;
                var mark = LogTap.Mark;
                var from = HitTap.Hits.Count;
                var run = new JumpRun();
                yield return JumpPress(rig, run, delays[i], 1.2f);
                yield return AttackOver(p, run.Started, 2.5f);
                yield return WaitLanded(p, 3f);
                var landAt = Time.fixedTime;
                if (i == 0)
                {
                    c.Check(run.IsMove && run.Move.Kind == MoveKind.Jump && run.Move.Trigger == "swing_longsword2" && run.StartAir,
                        $"SwordIron: jump, attack in the air = the jump attack swing_longsword2 (got {run.Move.Kind} {run.Move.Trigger})");
                    var want = $"Jump attack: SwordIron (Swords) plays swing_longsword2; {JumpDefaults}; {source} settings.";
                    var got = LogTap.First(mark, Dbg, "Jump attack: ");
                    c.Check(got == want && LogTap.Count(mark, Dbg, "Jump attack: ") == 1,
                        $"Debug line \"{want}\" (got \"{got ?? "none"}\")");
                    var entry = LogTap.First(mark, Dbg, "Jump attack swing_longsword2 started after ");
                    c.Check(entry != null && EntryLine.IsMatch(entry),
                        $"then Debug \"Jump attack swing_longsword2 started after ... s; the next attack starts the combo at its first swing.\" (got \"{entry ?? "none"}\")");
                }
                if (run.IsMove && dummy != null)
                {
                    foreach (var h in HitTap.OnTarget(dummy.Body, from))
                    {
                        if (Near(h.Stagger, run.Clone.m_staggerMultiplier) && landed == null)
                        {
                            landed = h;
                            tries += $"press {S(delays[i])} s after the jump: hit {S(h.At - run.PressAt)} s after the press "
                                     + (h.Air ? "(still in the air)" : $"({S(h.At - landAt)} s around the landing)") + "; ";
                        }
                    }
                }
                if (landed == null)
                {
                    tries += $"press {S(delays[i])} s after the jump: no hit; ";
                }
            }
            Look(p, 0f);
            if (dummy != null)
            {
                Place(dummy.Body, ParkSpot(rig, 3)); // out of the way of the rolls below
            }
            c.Check(landed != null && landed.Shown > 0f,
                "the sword jump attack hits the dummy (a hit of the move: its stagger multiplier, damage applied)");
            SelfTest.Note(LinesName, $"sword jump attack on the dummy {F(DummyDistance)} m ahead, looking 35 degrees down: {tries}");

            // The next attack on the ground: the normal first swing.
            yield return Settle(rig, rules);
            var last = p.m_currentAttack;
            var moved = MoveTracker.LastMove.Clone;
            Press(p);
            yield return WaitNewAttack(p, last, 0.6f, box);
            c.Check(Vanilla(box.Value, shared) && box.Value.m_currentAttackCainLevel == 0 && ReferenceEquals(MoveTracker.LastMove.Clone, moved),
                $"after the jump attack the next attack is the normal first swing (got {Fired(box.Value)})");
            yield return AttackOver(p, box.Value, 2f);

            // T06: roll attack with a press in the second half of the roll.
            yield return Settle(rig, rules);
            var rollMark = LogTap.Mark;
            var roll = new RollRun();
            yield return Roll(rig, roll);
            p.m_queuedAttackTimer = 0f;
            var line = LogTap.First(rollMark, Dbg, "Roll attack: ");
            var cut = CutLine.Match(line ?? "");
            c.Check(roll.Clone != null && cut.Success && Number(cut) >= 0.84f && Number(cut) <= 0.9f,
                $"Debug \"Roll attack: SwordIron (Swords) plays swing_longsword1; {RollDefaults}; cut into the roll 0.86 s after it started "
                + $"(cross-fade from the roll, ...); {source} settings.\" (got \"{line ?? "none"}\")");
            c.Check(line != null && line.EndsWith($"; {source} settings.", StringComparison.Ordinal), $"the roll attack line ends \"{source} settings.\"");
            yield return AttackOver(p, roll.Started, 2f);

            // Held through the roll and after: the same, never dropped for a first swing.
            yield return Settle(rig, rules);
            rollMark = LogTap.Mark;
            var hold = new RollRun();
            rig.TakeController();
            yield return Roll(rig, hold, true, q => Hold(q, false));
            var stillCurrent = hold.Clone != null && ReferenceEquals(p.m_currentAttack, hold.Clone);
            yield return WaitTicks(0.2f);
            stillCurrent &= ReferenceEquals(p.m_currentAttack, hold.Clone);
            rig.GiveController();
            c.Check(hold.Clone != null && hold.Move.Cut && hold.EnteredAt >= 0f && stillCurrent
                    && CutLine.IsMatch(LogTap.First(rollMark, Dbg, "Roll attack: ") ?? ""),
                "attack held through a roll: the same roll attack line, the move enters and stays the current attack");
            c.Check(LogTap.Count(rollMark, Dbg, "was dropped before it started") == 0,
                "attack held through a roll: no \"was dropped before it started\" Debug line");
            yield return AttackOver(p, hold.Started, 2f);

            // Press right after the roll: still a roll attack, out of the end of the roll.
            yield return Settle(rig, rules);
            rollMark = LogTap.Mark;
            var edge = new Box<float>();
            last = p.m_currentAttack;
            p.Dodge(rig.Forward);
            yield return WaitRollEdge(p, 4f, false, edge);
            yield return Fixed;
            yield return Fixed;
            Press(p);
            yield return WaitNewAttack(p, last, 0.6f, box);
            line = LogTap.First(rollMark, Dbg, "Roll attack: ");
            var after = AfterLine.Match(line ?? "");
            c.Check(box.Value != null && ReferenceEquals(box.Value, MoveTracker.LastMove.Clone) && after.Success && Number(after) <= 0.2f,
                $"press right after a roll: Debug \"Roll attack: ...; ... s after the roll ended (cross-fade from the roll, ...); ...\" (got \"{line ?? "none"}\")");
            yield return AttackOver(p, box.Value, 2f);
            yield return WaitIdle(p, 4f);
            c.Report();
        }
        finally
        {
            HitTap.Stop();
            rig.Restore();
        }
    }

    // ---------- moveset.aim-hit ----------

    private static IEnumerator RunAimHit()
    {
        var p = LocalPlayer(AimHitName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(AimHitName);
        var rig = new Rig(AimHitName, p);
        try
        {
            var rules = Loose();
            ServerRules.TestRules = rules;
            HitTap.Start();
            var held = new Box<ItemDrop.ItemData>();

            // 1. Knife, Boar right in front, looking down at it: the swing must come down onto it while the player is
            //    still in the air. The hit's moment in the jump matters (too high = over the Boar even when tilted,
            //    too late = on the ground): first try, then tries timed from the hit delay just measured.
            yield return Equip(rig, RowOf("KnifeCopper"), held);
            var knife = held.Value;
            var boar = SpawnFoe(rig, "Boar", 0, 1000f);
            c.Check(knife != null && boar != null, "KnifeCopper equipped and a Boar spawned (AI off)");
            if (knife != null && boar != null)
            {
                var ok = false;
                var delay = -1f;
                var notes = "";
                var targets = new[] { -1f, 0.72f, 0.66f, 0.78f, 0.6f };
                for (var i = 0; i < targets.Length && !ok; i++)
                {
                    var press = targets[i] < 0f || delay < 0f ? 0.4f : Mathf.Clamp(targets[i] - delay, 0.04f, 0.7f);
                    yield return Settle(rig, rules);
                    PlaceAhead(p, boar.Body, rig.Forward, 0.1f);
                    boar.Go.transform.rotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, rig.Forward));
                    Look(p, 40f);
                    yield return Fixed;
                    yield return Fixed;
                    var from = HitTap.Hits.Count;
                    var run = new JumpRun();
                    yield return JumpPress(rig, run, press, 1.2f);
                    yield return AttackOver(p, run.Started, 2f);
                    var events = HitTap.Of(run.Clone);
                    var hits = HitTap.OnTarget(boar.Body, from);
                    if (events.Count > 0)
                    {
                        delay = events[0].At - run.PressAt;
                    }
                    ok = run.IsMove && run.Move.Kind == MoveKind.Jump && events.Count > 0 && events[0].Air && events[0].Pitch >= 10f
                         && hits.Count > 0 && hits[0].Air && Near(hits[0].Stagger, run.Clone.m_staggerMultiplier);
                    notes += $"press {S(press)} s after the jump: hit event {(events.Count > 0 ? S(events[0].At - run.JumpAt) + " s into the jump, " + (events[0].Air ? "in the air" : "on the ground") + ", swing " + F(events[0].Pitch) + " degrees down" : "none")}, "
                             + $"Boar {(hits.Count > 0 ? "hit" : "not hit")}; ";
                    yield return WaitLanded(p, 3f);
                }
                Look(p, 0f);
                c.Check(ok, "KnifeCopper: a jump attack next to a Boar while looking down at it sweeps down (at least 10 degrees) and hits it while the player is still in the air");
                SelfTest.Note(AimHitName, "knife and Boar: " + notes);
                Park(rig, boar, 0);
            }
            rig.TakeBack(knife);

            // 2. Two-handed sword, pressed just before landing: starts in the air, hits after touchdown, level, and
            //    strikes the dummy (a swing still tilted down would stop in the ground).
            yield return Equip(rig, RowOf("THSwordKrom"), held);
            var krom = held.Value;
            c.Check(krom != null, "THSwordKrom equipped");
            if (krom != null)
            {
                var shared = krom.m_shared.m_attack;
                var distance = Mathf.Clamp(shared.m_attackRange - 0.4f, 1.5f, 2.5f);
                var dummy = rig.SpawnDummy(distance);
                c.Check(dummy != null, "training dummy spawned");
                yield return Settle(rig, rules);
                p.Jump();
                var jumpAt = Time.fixedTime;
                yield return WaitTicks(0.2f);
                yield return WaitLanded(p, 3f);
                var airtime = Time.fixedTime - jumpAt;
                var lookDown = Mathf.Atan2(1.6f, distance) * Mathf.Rad2Deg;
                var ok = false;
                var notes = "";
                var early = new[] { 0.12f, 0.2f, 0.06f };
                for (var i = 0; i < early.Length && !ok && dummy != null; i++)
                {
                    yield return Settle(rig, rules);
                    rig.PlaceDummy(dummy, distance);
                    Look(p, lookDown);
                    yield return Fixed;
                    yield return Fixed;
                    var mark = LogTap.Mark;
                    var from = HitTap.Hits.Count;
                    var run = new JumpRun();
                    yield return JumpPress(rig, run, airtime - early[i], 1.6f);
                    yield return WaitLanded(p, 3f);
                    var landAt = Time.fixedTime;
                    yield return AttackOver(p, run.Started, 3f);
                    var events = HitTap.Of(run.Clone);
                    var hits = HitTap.OnTarget(dummy.Body, from);
                    var lineOk = LogTap.Count(mark, Dbg, "Jump attack: THSwordKrom (Greatswords) plays greatsword2;") == 1;
                    ok = run.IsMove && run.StartAir && lineOk && events.Count > 0 && !events[0].Air && Mathf.Abs(events[0].Pitch) <= 1f
                         && hits.Count > 0 && !hits[0].Air && hits[0].At >= landAt - 0.001f && hits[0].Shown > 0f;
                    notes += $"press {S(early[i])} s before landing: started {(run.StartAir ? "in the air" : "on the ground")}, hit event "
                             + $"{(events.Count > 0 ? S(events[0].At - landAt) + " s after landing, swing " + F(events[0].Pitch) + " degrees down" : "none")}, "
                             + $"dummy {(hits.Count > 0 ? "hit" : "not hit")}, Debug line {(lineOk ? "there" : "missing")}; ";
                }
                Look(p, 0f);
                c.Check(ok, $"THSwordKrom: a jump attack started in the air just before landing (Debug \"Jump attack: THSwordKrom ...\") "
                            + $"hits after touchdown, level although the player looks at the dummy's feet, and strikes the dummy {F(distance)} m away");
                SelfTest.Note(AimHitName, $"two-handed sword and dummy (airtime {S(airtime)} s, looking {F(lookDown)} degrees down): {notes}");
            }
            yield return Settle(rig, rules);
            c.Report();
        }
        finally
        {
            HitTap.Stop();
            rig.Restore();
        }
    }

    // ---------- moveset.numbers ----------

    private static IEnumerator RunNumbers()
    {
        var p = LocalPlayer(NumbersName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(NumbersName);
        var rig = new Rig(NumbersName, p);
        var swords = p.GetSkills().GetSkill(Skills.SkillType.Swords);
        var clubs = p.GetSkills().GetSkill(Skills.SkillType.Clubs);
        var swordsWas = new Vector2(swords.m_level, swords.m_accumulator);
        var clubsWas = new Vector2(clubs.m_level, clubs.m_accumulator);
        try
        {
            // Like TESTING T09: Swords 50 (steadier damage). In memory, put back after.
            swords.m_level = 50f;
            HitTap.Start();
            var held = new Box<ItemDrop.ItemData>();
            yield return Equip(rig, SwordRow, held);
            var sword = held.Value;
            var dummy = rig.SpawnDummy();
            var troll = SpawnFoe(rig, "Troll", 0);
            c.Check(sword != null && dummy != null && troll != null, "SwordIron equipped, training dummy and Troll spawned (AI off)");
            if (sword == null || dummy == null || troll == null)
            {
                c.Report();
                yield break;
            }
            var shared = sword.m_shared.m_attack;
            // No backstab bonus on the first hit of a creature that never saw the player: it would triple one number.
            dummy.Body.m_backstabTime = Time.time;
            troll.Body.m_backstabTime = Time.time;
            var normal = new Box<HitRec>();
            var move = new Box<HitRec>();

            // Damage x3 on the dummy. Damage and knockback of one hit carry the same random skill factor, so
            // damage / knockback has none: the move's is 3 times the normal swing's.
            var rules = Loose();
            rules.Roll.Damage = 3f;
            ServerRules.TestRules = rules;
            yield return GroundHit(rig, dummy.Body, normal);
            var run = new RollRun();
            yield return RollHit(rig, dummy.Body, run, move);
            var n = normal.Value;
            var m = move.Value;
            c.Check(n != null && m != null && run.Clone != null && n.Push > 0f && m.Push > 0f && n.Shown > 0f && m.Shown > 0f,
                "a normal swing and a roll attack each hit the dummy, with damage applied");
            if (n != null && m != null && n.Push > 0f && m.Push > 0f)
            {
                var sent = m.Damage / m.Push / (n.Damage / n.Push);
                var shown = m.Shown / m.Push / (n.Shown / n.Push);
                c.Check(Near(sent, 3f, 0.01f) && Near(shown, 3f, 0.01f),
                    $"Roll attack DamageMultiplier 3: the roll attack's damage is 3 times a normal swing's once the random skill factor is taken out (hit {F(sent)}, damage number {F(shown)})");
                c.Check(Near(m.Stagger, MoveEdit.Stagger(shared.m_staggerMultiplier, rules.Roll.Stagger)) && Near(n.Stagger, shared.m_staggerMultiplier),
                    $"the hits carry their stagger multipliers (roll attack {F(m.Stagger)}, normal swing {F(n.Stagger)})");
                c.Check(m.Wire && m.WireFromPlayer && Near(m.WireStagger, m.Stagger) && Near(m.WirePush, m.Push) && Near(m.WireDamage, m.Damage),
                    "the roll attack's hit reaches the target's owner through the game's own hit message with its damage, stagger multiplier and knockback unchanged (what a player without the mod receives)");
                SelfTest.Note(NumbersName, $"dummy: normal swing {F(n.Shown)} damage (knockback {F(n.Push)}), roll attack x3 {F(m.Shown)} damage (knockback {F(m.Push)})");
            }

            // Stagger x10 on a Troll: one roll attack staggers it, one normal swing never does.
            rules = Loose();
            rules.Roll.Stagger = 10f;
            ServerRules.TestRules = rules;
            var bar = troll.Body.GetMaxHealth() * troll.Body.m_staggerDamageFactor;
            c.Check(bar > 0f, $"the Troll has a stagger bar ({F(bar)})");
            troll.Body.m_staggerDamage = 0f;
            var staggers = HitTap.Staggered.Count;
            yield return GroundHit(rig, troll.Body, normal);
            n = normal.Value;
            var filled = n != null ? n.BarAfter - n.BarBefore : -1f;
            var staggeredByNormal = HitTap.Staggered.Skip(staggers).Any(x => ReferenceEquals(x, troll.Body)) || troll.Body.IsStaggering();
            // Luckiest and unluckiest skill factor at this skill (Skills.GetRandomSkillFactor).
            var mid = Mathf.Lerp(0.4f, 1f, p.GetSkillFactor(Skills.SkillType.Swords));
            var low = Mathf.Clamp01(mid - 0.15f);
            var high = Mathf.Clamp01(mid + 0.15f);
            c.Check(n != null && filled > 0f && !staggeredByNormal, $"one normal swing does not stagger the Troll (bar +{F(filled)} of {F(bar)})");
            c.Check(filled > 0f && filled * high / low < bar,
                $"never from one normal swing: even the luckiest one (x{F(high / low)}) stays under the bar ({F(filled * high / low)} of {F(bar)})");
            Park(rig, troll, 0);
            yield return WaitTicks(0.3f);
            troll.Body.SetHealth(troll.Body.GetMaxHealth());
            troll.Body.m_staggerDamage = 0f;
            staggers = HitTap.Staggered.Count;
            run = new RollRun();
            yield return RollHit(rig, troll.Body, run, move);
            m = move.Value;
            var seen = new Box<bool>();
            yield return WaitFor(() => troll.Body.IsStaggering(), 0.6f, seen);
            var staggerCalled = HitTap.Staggered.Skip(staggers).Any(x => ReferenceEquals(x, troll.Body));
            c.Check(m != null && run.Clone != null && Near(m.Stagger, MoveEdit.Stagger(shared.m_staggerMultiplier, 10f)),
                $"Roll attack StaggerMultiplier 10: the roll attack's hit carries stagger x{F(MoveEdit.Stagger(shared.m_staggerMultiplier, 10f))} (got {(m != null ? F(m.Stagger) : "no hit")})");
            c.Check(staggerCalled && seen.Value, $"the Troll staggers from that one roll attack (stagger asked {staggerCalled}, stagger animation {seen.Value})");
            c.Check(filled > 0f && n != null && m != null
                    && filled * (low / high) * rules.Roll.Damage * (m.Stagger / n.Stagger) >= bar,
                $"always from one roll attack: even the unluckiest one fills the bar ({F(filled * (low / high) * rules.Roll.Damage * (m != null && n != null ? m.Stagger / n.Stagger : 0f))} of {F(bar)})");
            Park(rig, troll, 0);

            // Push x5 with the Club on a Greyling: much more knockback, and it really flies further.
            rig.TakeBack(sword);
            yield return Equip(rig, RowOf("Club"), held);
            var club = held.Value;
            var grey = SpawnFoe(rig, "Greyling", 1, 2000f);
            c.Check(club != null && grey != null, "Club equipped and a Greyling spawned (AI off)");
            if (club != null && grey != null)
            {
                grey.Body.m_backstabTime = Time.time;
                rules = Loose();
                rules.Roll.Push = 5f;
                ServerRules.TestRules = rules;
                var flown = new Box<float>();
                yield return GroundHit(rig, grey.Body, normal, false);
                yield return Flight(grey, flown);
                var normalFlight = flown.Value;
                n = normal.Value;
                Park(rig, grey, 1);
                yield return WaitTicks(0.3f);
                run = new RollRun();
                yield return RollHit(rig, grey.Body, run, move, false);
                yield return Flight(grey, flown);
                m = move.Value;
                Park(rig, grey, 1);
                c.Check(n != null && m != null && run.Clone != null && n.Push > 0f && n.Damage > 0f && m.Damage > 0f,
                    "a normal Club swing and a Club roll attack each hit the Greyling");
                if (n != null && m != null && n.Push > 0f && n.Damage > 0f && m.Damage > 0f)
                {
                    var ratio = m.Push / m.Damage / (n.Push / n.Damage);
                    var want = rules.Roll.Push / rules.Roll.Damage;
                    c.Check(Near(ratio, want, 0.01f),
                        $"Roll attack PushMultiplier 5: knockback per damage is {F(want)} times a normal swing's (5 / the 1.3 damage bonus), got {F(ratio)}");
                    c.Check(m.PushAfter > n.PushAfter * 2f && flown.Value > normalFlight,
                        $"the Greyling flies back much further: knockback speed {F(m.PushAfter)} against {F(n.PushAfter)}, {F(flown.Value)} m against {F(normalFlight)} m in the first second");
                }
            }
            yield return Settle(rig);
            c.Report();
        }
        finally
        {
            swords.m_level = swordsWas.x;
            swords.m_accumulator = swordsWas.y;
            clubs.m_level = clubsWas.x;
            clubs.m_accumulator = clubsWas.y;
            HitTap.Stop();
            rig.Restore();
        }
    }

    // How far a foe moves over the ground in the second after a hit.
    private static IEnumerator Flight(Foe foe, Box<float> metres)
    {
        metres.Value = 0f;
        if (foe == null || !foe.Alive)
        {
            yield break;
        }
        var from = foe.Body.transform.position;
        var until = Time.fixedTime + 1f;
        while (Time.fixedTime < until && foe.Alive)
        {
            yield return Fixed;
            var d = foe.Body.transform.position - from;
            d.y = 0f;
            metres.Value = Mathf.Max(metres.Value, d.magnitude);
        }
    }

    // ---------- moveset.air ----------

    private static IEnumerator RunAir()
    {
        var p = LocalPlayer(AirName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(AirName);
        var rig = new Rig(AirName, p);
        var health = p.GetHealth();
        try
        {
            ServerRules.TestRules = Loose(); // no cooldown: only the "first attack of the jump" rule can say no
            var held = new Box<ItemDrop.ItemData>();
            yield return Equip(rig, SwordRow, held);
            var weapon = held.Value;
            if (weapon == null)
            {
                c.Check(false, "could not equip SwordIron");
                c.Report();
                yield break;
            }
            var shared = weapon.m_shared.m_attack;
            yield return Settle(rig);
            var mark = LogTap.Mark;
            var prev = p.m_currentAttack;
            p.Jump();
            c.Check(p.m_jumpTimer == 0f && MoveTracker.JumpLive, "jump taken, token live");
            yield return Fixed;
            // "From a height": me lift the player right after the jump (the token lives until the feet touch ground).
            Put(p, rig.Home + Vector3.up * 40f);
            yield return WaitTicks(0.1f);
            c.Check(!p.IsOnGround() && MoveTracker.JumpLive, "high in the air after the jump: jump token still live");
            Press(p);
            var box = new Box<Attack>();
            yield return WaitNewAttack(p, prev, 0.4f, box);
            var first = box.Value;
            c.Check(first != null && ReferenceEquals(MoveTracker.LastMove.Clone, first) && MoveTracker.LastMove.Kind == MoveKind.Jump && !p.IsOnGround(),
                $"the first attack of the jump is the jump attack (got {Fired(first)})");
            Attack second = null;
            var secondAir = false;
            var t0 = Time.fixedTime;
            while (first != null && second == null && !p.IsOnGround() && Time.fixedTime - t0 < 6f)
            {
                Press(p);
                yield return Fixed;
                var current = p.m_currentAttack;
                if (current != null && !ReferenceEquals(current, first))
                {
                    second = current;
                    secondAir = !p.IsOnGround();
                }
            }
            p.m_queuedAttackTimer = 0f;
            c.Check(second != null && secondAir, $"a second attack started before landing ({S(Time.fixedTime - t0)} s after the first)");
            c.Check(second != null && ReferenceEquals(MoveTracker.LastMove.Clone, first) && Vanilla(second, shared),
                $"the second attack of the same air time is a normal swing (got {Fired(second)})");
            c.Check(LogTap.Count(mark, Dbg, "Jump attack: ") == 1, $"only one \"Jump attack\" Debug line ({LogTap.Count(mark, Dbg, "Jump attack: ")})");
            yield return SoftLand(p, 8f);
            c.Check(p.IsOnGround() && p.GetHealth() >= health - 0.01f, "landed without fall damage (test set-up)");
            yield return WaitIdle(p, 4f);
            c.Report();
        }
        finally
        {
            if (p != null && p.GetHealth() < health)
            {
                p.SetHealth(health);
            }
            rig.Restore();
        }
    }

    // ---------- moveset.combo ----------

    private static IEnumerator RunCombo()
    {
        var p = LocalPlayer(ComboName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(ComboName);
        var rig = new Rig(ComboName, p);
        try
        {
            var rules = Loose();
            ServerRules.TestRules = rules;
            var held = new Box<ItemDrop.ItemData>();
            yield return Equip(rig, SwordRow, held);
            var sword = held.Value;
            if (sword == null)
            {
                c.Check(false, "could not equip SwordIron");
                c.Report();
                yield break;
            }
            var shared = sword.m_shared.m_attack;
            var special = sword.m_shared.m_secondaryAttack;

            // (a) Secondary attack in a jump: the normal secondary, no move. (After a roll: moveset.after-roll.)
            yield return Settle(rig);
            var mark = LogTap.Mark;
            var run = new JumpRun();
            yield return JumpPress(rig, run, 0.1f, 1f, PressSecondary);
            c.Check(run.Started != null && run.StartAir && run.Clone == null && p.m_currentAttackIsSecondary
                    && special != null && run.Started.m_attackAnimation == special.m_attackAnimation
                    && Near(run.Started.m_damageMultiplier, special.m_damageMultiplier),
                $"secondary attack in a jump: the weapon's normal secondary {(special != null ? special.m_attackAnimation : "?")} (got {Fired(run.Started)})");
            c.Check(!MoveTracker.JumpLive && LogTap.Count(mark, Dbg, "Jump attack") == 0,
                "secondary attack in a jump: the jump token is used up and there is no move Debug line");
            yield return AttackOver(p, run.Started, 3f);

            // (b) Three presses on the ground: the weapon's own three swings.
            yield return Settle(rig);
            mark = LogTap.Mark;
            var moved = MoveTracker.LastMove.Clone;
            var steps = new List<int>();
            var plain = true;
            var lastAttack = p.m_currentAttack;
            var t0 = Time.fixedTime;
            while (steps.Count < 3 && Time.fixedTime - t0 < 6f)
            {
                Press(p);
                yield return Fixed;
                var current = p.m_currentAttack;
                if (current != null && !ReferenceEquals(current, lastAttack))
                {
                    lastAttack = current;
                    steps.Add(current.m_currentAttackCainLevel);
                    plain &= Vanilla(current, shared);
                }
            }
            p.m_queuedAttackTimer = 0f;
            c.Check(steps.SequenceEqual(new[] { 0, 1, 2 }) && plain && ReferenceEquals(MoveTracker.LastMove.Clone, moved),
                $"combo on the ground with no jump or roll: the three normal swings (steps {string.Join(", ", steps.Select(s => s.ToString()).ToArray())})");
            c.Check(LogTap.Count(mark, Dbg, " attack: ") == 0, "combo on the ground: no move Debug line");
            yield return AttackOver(p, lastAttack, 3f);

            // (d) Attack mashed during a jump attack: what follows is the first swing of the combo.
            yield return Settle(rig);
            run = new JumpRun();
            yield return JumpPress(rig, run);
            var following = new Box<Attack>();
            yield return NextAttack(p, run.Started, 5f, following);
            c.Check(run.IsMove && Vanilla(following.Value, shared) && following.Value.m_currentAttackCainLevel == 0
                    && ReferenceEquals(MoveTracker.LastMove.Clone, run.Clone),
                $"attack mashed during a jump attack: the next attack is the first swing of the combo (got {Fired(following.Value)})");
            yield return AttackOver(p, following.Value, 3f);
            rig.TakeBack(sword);

            // (e) A roll animation that is not a step of the weapon's own chain: the next attack starts a new combo.
            yield return Equip(rig, RowOf("KnifeCopper"), held);
            var knife = held.Value;
            c.Check(knife != null, "KnifeCopper equipped");
            if (knife != null)
            {
                var stab = knife.m_shared.m_attack;
                rules = Loose();
                rules.RollTriggers[Families.Index(WeaponFamily.Knives)] = "knife_secondary";
                ServerRules.TestRules = rules;
                MoveTriggers.Invalidate();
                yield return Settle(rig);
                mark = LogTap.Mark;
                var roll = new RollRun();
                yield return Roll(rig, roll);
                var clone = roll.Clone;
                c.Check(clone != null && roll.Move.Kind == MoveKind.Roll && roll.Move.Trigger == "knife_secondary" && roll.Move.Step == -1,
                    $"Roll attack animations Knives = knife_secondary: the roll attack plays it (got {roll.Move.Kind} {roll.Move.Trigger})");
                if (clone != null)
                {
                    var entered = new Box<bool>();
                    yield return WaitFor(() => MoveTracker.LastEntryDelay >= 0f || !MoveTracker.Watching, 1f, entered);
                    var line = LogTap.First(mark, Dbg, "Roll attack knife_secondary started after ");
                    c.Check(MoveTracker.LastEntryDelay >= 0f && clone.m_attackAnimation == "knife_secondary"
                            && line != null && line.EndsWith("the next attack starts a new combo.", StringComparison.Ordinal),
                        $"it enters, and Debug says \"... the next attack starts a new combo.\" (got \"{line ?? "none"}\")");
                    Attack next = null;
                    t0 = Time.fixedTime;
                    while (next == null && Time.fixedTime - t0 < 5f)
                    {
                        Press(p);
                        yield return Fixed;
                        var current = p.m_currentAttack;
                        if (current != null && !ReferenceEquals(current, clone))
                        {
                            next = current;
                        }
                    }
                    p.m_queuedAttackTimer = 0f;
                    c.Check(Vanilla(next, stab) && next.m_currentAttackCainLevel == 0 && ReferenceEquals(MoveTracker.LastMove.Clone, clone),
                        $"the next attack is the first stab (got {Fired(next)})");
                    yield return AttackOver(p, next, 3f);
                }
            }
            yield return Settle(rig);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- moveset.settings ----------

    // README table "Default animations per weapon type", typed here on its own (not read from the code).
    private static readonly string[][] ReadmeDefaults =
    {
        new[] { "Swords", "swing_longsword2", "swing_longsword1" },
        new[] { "Maces", "swing_longsword2", "swing_longsword1" },
        new[] { "Axes", "swing_axe2", "swing_axe1" },
        new[] { "Battleaxes", "battleaxe_attack2", "Off" },
        new[] { "DualAxes", "dualaxes3", "dualaxes1" },
        new[] { "Greatswords", "greatsword2", "greatsword1" },
        new[] { "Atgeirs", "atgeir_attack2", "atgeir_attack1" },
        new[] { "Knives", "knife_stab2", "knife_stab1" },
        new[] { "DualKnives", "dual_knives2", "dual_knives1" },
        new[] { "Spears", "Off", "Off" },
        new[] { "Fists", "unarmed_attack1", "unarmed_attack1" },
        new[] { "Sledges", "Off", "Off" },
    };

    // Like a change in ConfigurationManager, in memory: new own settings object, then the mod's own "settings changed".
    private static void ApplyOwn(MoveRules rules)
    {
        ServerRules.TestRules = null;
        MoveRules.TestOwn = rules;
        ServerRules.OwnChanged();
    }

    private static IEnumerator RunSettings()
    {
        var p = LocalPlayer(SettingsName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(SettingsName);
        var rig = new Rig(SettingsName, p);
        try
        {
            foreach (var row in ReadmeDefaults)
            {
                var family = Families.All.FirstOrDefault(f => Families.Key(f) == row[0]);
                c.Check(family != WeaponFamily.None && MoveTriggers.Default(MoveKind.Jump, family) == row[1]
                        && MoveTriggers.Default(MoveKind.Roll, family) == row[2],
                    $"README default animations of {row[0]}: jump {row[1]}, roll {row[2]}");
            }
            c.Check(ReadmeDefaults.Length == Families.Count, "README table has every weapon type");
            CheckTypo(c);
            CheckWiring(c);

            var held = new Box<ItemDrop.ItemData>();
            yield return Equip(rig, SwordRow, held);
            var weapon = held.Value;
            if (weapon == null)
            {
                c.Check(false, "could not equip SwordIron");
                c.Report();
                yield break;
            }
            var shared = weapon.m_shared.m_attack;
            var swords = Families.Index(WeaponFamily.Swords);
            var box = new Box<Attack>();

            // T12: Jump attack animations Swords = greatsword2.
            var rules = Loose();
            rules.JumpTriggers[swords] = "greatsword2";
            ApplyOwn(rules);
            c.Check(ReferenceEquals(ServerRules.Current, rules) && ServerRules.Source == RulesSource.Own,
                "a settings change is in force at once (own settings, new snapshot)");
            yield return Settle(rig);
            var mark = LogTap.Mark;
            var run = new JumpRun();
            yield return JumpPress(rig, run);
            var line = LogTap.First(mark, Dbg, "Jump attack: ");
            c.Check(run.IsMove && run.Move.Trigger == "greatsword2" && run.Move.Step == -1 && run.EnteredAt >= 0f
                    && run.EnteredAt - run.StartAt <= MoveTracker.StartDeadline,
                $"Swords = greatsword2: the sword's jump attack plays greatsword2 and enters it (got {run.Move.Trigger})");
            c.Check(line != null && line.StartsWith("Jump attack: SwordIron (Swords) plays greatsword2;", StringComparison.Ordinal)
                    && line.EndsWith("; own settings.", StringComparison.Ordinal),
                $"Debug \"Jump attack: SwordIron (Swords) plays greatsword2; ...; own settings.\" (got \"{line ?? "none"}\")");
            var entry = LogTap.First(mark, Dbg, "Jump attack greatsword2 started after ");
            c.Check(entry != null && entry.EndsWith("the next attack starts a new combo.", StringComparison.Ordinal),
                $"Debug \"... the next attack starts a new combo.\" (got \"{entry ?? "none"}\")");
            yield return AttackOver(p, run.Started, 3f);
            yield return Settle(rig);
            var last = p.m_currentAttack;
            Press(p);
            yield return WaitNewAttack(p, last, 0.6f, box);
            c.Check(Vanilla(box.Value, shared) && box.Value.m_currentAttackCainLevel == 0, $"after it, the next attack is the first swing (got {Fired(box.Value)})");
            yield return AttackOver(p, box.Value, 3f);

            // T12: Off.
            rules = Loose();
            rules.JumpTriggers[swords] = MoveTriggers.Off;
            ApplyOwn(rules);
            yield return Settle(rig);
            mark = LogTap.Mark;
            run = new JumpRun();
            yield return JumpPress(rig, run);
            c.Check(run.Started != null && run.Clone == null && Vanilla(run.Started, shared) && LogTap.Count(mark, Dbg, "Jump attack") == 0,
                $"Swords = Off: a normal swing, no \"Jump attack\" Debug line (got {Fired(run.Started)})");
            yield return AttackOver(p, run.Started, 3f);

            // T15: JumpAttack = false. Jump attacks stop at once, roll attacks still work.
            yield return Settle(rig);
            rules = Loose();
            rules.JumpAttack = false;
            ApplyOwn(rules);
            mark = LogTap.Mark;
            run = new JumpRun();
            yield return JumpPress(rig, run);
            c.Check(run.Started != null && run.Clone == null && Vanilla(run.Started, shared) && LogTap.Count(mark, Dbg, "Jump attack") == 0,
                $"JumpAttack = false: the very next jump + attack is a normal swing (got {Fired(run.Started)})");
            yield return AttackOver(p, run.Started, 3f);
            yield return Settle(rig);
            var roll = new RollRun();
            yield return Roll(rig, roll);
            p.m_queuedAttackTimer = 0f;
            c.Check(roll.Clone != null && roll.Move.Kind == MoveKind.Roll, "JumpAttack = false: roll attacks still work");
            yield return AttackOver(p, roll.Started, 3f);

            // T15: JumpAttack = false and Window = 2: roll, jump right after it, attack in the air = normal swing.
            rules = Loose();
            rules.JumpAttack = false;
            rules.Window = 2f;
            ApplyOwn(rules);
            yield return Settle(rig);
            mark = LogTap.Mark;
            var moved = MoveTracker.LastMove.Clone;
            var edge = new Box<float>();
            p.Dodge(rig.Forward);
            yield return WaitRollEdge(p, 4f, false, edge);
            var jumped = false;
            var t0 = Time.fixedTime;
            while (!jumped && Time.fixedTime - t0 < 0.3f)
            {
                p.Jump();
                jumped = p.m_jumpTimer == 0f;
                if (!jumped)
                {
                    yield return Fixed;
                }
            }
            var sinceRoll = Time.fixedTime - edge.Value;
            c.Check(edge.Value >= 0f && jumped && MoveTracker.JumpLive, $"roll, then a jump right after it ({S(sinceRoll)} s after the roll ended)");
            yield return WaitTicks(0.1f);
            last = p.m_currentAttack;
            var air = !p.IsOnGround();
            Press(p);
            yield return WaitNewAttack(p, last, 0.4f, box);
            c.Check(air && Vanilla(box.Value, shared) && ReferenceEquals(MoveTracker.LastMove.Clone, moved),
                $"JumpAttack = false, Window = 2: roll, jump, attack in the air is a normal swing (got {Fired(box.Value)})");
            c.Check(LogTap.Count(mark, Dbg, "Roll attack") == 0 && LogTap.Count(mark, Dbg, "Jump attack") == 0,
                "no \"Roll attack\" Debug line: the jump owns the air attack");
            yield return AttackOver(p, box.Value, 3f);

            // T15: RollAttack = false. Roll attacks stop at once, jump attacks work.
            yield return Settle(rig);
            rules = Loose();
            rules.RollAttack = false;
            ApplyOwn(rules);
            mark = LogTap.Mark;
            roll = new RollRun();
            yield return Roll(rig, roll);
            p.m_queuedAttackTimer = 0f;
            c.Check(roll.Clone == null && roll.Started != null && !roll.StartedInRoll && Vanilla(roll.Started, shared)
                    && LogTap.Count(mark, Dbg, "Roll attack") == 0,
                $"RollAttack = false: the very next roll + attack is a normal swing after the roll (got {Fired(roll.Started)})");
            yield return AttackOver(p, roll.Started, 3f);
            yield return Settle(rig);
            run = new JumpRun();
            yield return JumpPress(rig, run);
            c.Check(run.IsMove && run.Move.Kind == MoveKind.Jump, "RollAttack = false: jump attacks work");
            yield return AttackOver(p, run.Started, 3f);
            yield return Settle(rig);
            c.Report();
        }
        finally
        {
            MoveRules.TestOwn = null;
            ServerRules.OwnChanged();
            MoveTriggers.Invalidate();
            rig.Restore();
        }
    }

    // T12, last part: a value that is not in the list, typed into a config file, comes back as the default when
    // BepInEx reads the file. Own throwaway file, bound with the plugin's own value list (never the player's file).
    private static void CheckTypo(Checks c)
    {
        var entry = Plugin.JumpAnimation[Families.Index(WeaponFamily.Swords)];
        var list = entry != null ? entry.Description.AcceptableValues : null;
        if (list == null)
        {
            c.Check(false, "the Swords jump animation setting has its value list");
            return;
        }
        string path = null;
        try
        {
            path = Path.Combine(Application.temporaryCachePath, "MC_MovesetSelfTest_" + Guid.NewGuid().ToString("N") + ".cfg");
            File.WriteAllText(path, $"[{entry.Definition.Section}]\n\n{entry.Definition.Key} = swing_longsword9\n");
            var file = new ConfigFile(path, false);
            var copy = file.Bind(entry.Definition.Section, entry.Definition.Key, (string)entry.DefaultValue,
                new ConfigDescription("self test copy", list));
            c.Check(copy.Value == "swing_longsword2" && (string)entry.DefaultValue == "swing_longsword2",
                $"\"swing_longsword9\" typed as Swords in [Jump attack animations] of a config file is read back as swing_longsword2 (got {copy.Value})");
            c.Check(!list.IsValid("swing_longsword9") && (string)list.Clamp("swing_longsword9") == "swing_longsword2",
                "the setting's value list refuses the typo and gives its first value, the default");
            MoveTriggers.ResetWarnings();
            MoveTriggers.Invalidate();
            var rules = MoveRules.Defaults();
            rules.JumpTriggers[Families.Index(WeaponFamily.Swords)] = copy.Value;
            c.Check(MoveTriggers.Resolve(rules, MoveKind.Jump, WeaponFamily.Swords) == "swing_longsword2" && MoveTriggers.WarningCount == 0,
                "that value is used with no warning");
        }
        catch (Exception e)
        {
            c.Check(false, $"typo check threw {e.GetType().Name}: {e.Message}");
        }
        finally
        {
            MoveTriggers.ResetWarnings();
            MoveTriggers.Invalidate();
            try
            {
                if (path != null && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // Temp file: the system cleans it.
            }
        }
    }

    // Every rule setting change reaches the mod's own handler, which makes a new settings snapshot; General does
    // not. Me call the handler by hand with the real setting objects: nothing is written.
    private static void CheckWiring(Checks c)
    {
        var plugin = PluginInstance();
        var handler = typeof(Plugin).GetMethod("OnSettingChanged", BindingFlags.NonPublic | BindingFlags.Static);
        var field = typeof(ConfigFile).GetField("SettingChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        var subscribed = false;
        if (plugin != null && handler != null && field != null && field.GetValue(plugin.Config) is Delegate chain)
        {
            foreach (var one in chain.GetInvocationList())
            {
                subscribed |= one.Method == handler;
            }
        }
        c.Check(subscribed, "the mod listens to every change of its config file's settings (ConfigFile.SettingChanged -> Plugin.OnSettingChanged)");
        if (handler == null)
        {
            return;
        }
        ServerRules.TestRules = null;
        MoveRules.TestOwn = null;
        var before = ServerRules.Current;
        handler.Invoke(null, new object[] { null, new SettingChangedEventArgs(Plugin.JumpAttack) });
        var after = ServerRules.Current;
        c.Check(!ReferenceEquals(before, after) && SameRules(before, after),
            "a rule setting change (JumpAttack) makes a new settings snapshot at the next use");
        handler.Invoke(null, new object[] { null, new SettingChangedEventArgs(Plugin.AllowPlayersWithoutMod) });
        c.Check(ReferenceEquals(after, ServerRules.Current), "AllowPlayersWithoutMod is not a move rule: no new snapshot");
    }

    // ---------- moveset.cooldown ----------

    private static readonly Regex CooldownLine = new Regex(@"^Roll attack skipped: cooldown \((\d+(?:\.\d+)?) s left\)\.$");

    private static IEnumerator RunCooldown()
    {
        var p = LocalPlayer(CooldownName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(CooldownName);
        var rig = new Rig(CooldownName, p);
        try
        {
            var held = new Box<ItemDrop.ItemData>();
            yield return Equip(rig, SwordRow, held);
            var sword = held.Value;
            if (sword == null)
            {
                c.Check(false, "could not equip SwordIron");
                c.Report();
                yield break;
            }
            var shared = sword.m_shared.m_attack;
            var edge = new Box<float>();
            var box = new Box<Attack>();

            // Default settings (Cooldown 1): roll attack, roll again as soon as the swing ends, attack: a roll attack.
            var rules = MoveRules.Defaults();
            ServerRules.TestRules = rules;
            MoveTracker.Reset();
            yield return Settle(rig);
            var mark = LogTap.Mark;
            var first = new RollRun();
            yield return Roll(rig, first);
            p.m_queuedAttackTimer = 0f;
            c.Check(first.Clone != null && first.EnteredAt >= 0f, "default settings: the first roll attack plays");
            var entry = MoveTracker.LastMoveAt;
            var last = p.m_currentAttack;
            var t0 = Time.fixedTime;
            while (!p.m_inDodge && Time.fixedTime - t0 < 4f)
            {
                if (p.m_queuedDodgeTimer <= 0.1f)
                {
                    p.Dodge(-rig.Forward); // asked again and again: the roll starts as soon as the swing lets it
                }
                yield return Fixed;
            }
            yield return WaitRollEdge(p, 4f, true, edge);
            yield return WaitNewAttack(p, last, 0.6f, box);
            p.m_queuedAttackTimer = 0f;
            var gap = MoveTracker.LastStartAt - entry;
            c.Check(box.Value != null && !ReferenceEquals(MoveTracker.LastMove.Clone, first.Clone) && MoveTracker.LastMove.Kind == MoveKind.Roll
                    && ReferenceEquals(box.Value, MoveTracker.LastMove.Clone),
                $"default settings: roll again as soon as the swing ends, attack = a roll attack ({S(gap)} s after the first one's start)");
            c.Check(gap >= MoveRules.DefaultCooldown && LogTap.Count(mark, Dbg, "skipped: cooldown") == 0,
                $"a normal roll takes longer than the 1 s cooldown ({S(gap)} s), nothing skipped");
            yield return AttackOver(p, box.Value, 3f);

            // Cooldown 5: the second attack is a normal swing, with its Debug line.
            rules = MoveRules.Defaults();
            rules.Cooldown = 5f;
            ServerRules.TestRules = rules;
            MoveTracker.Reset();
            yield return Settle(rig);
            var one = new RollRun();
            yield return Roll(rig, one);
            p.m_queuedAttackTimer = 0f;
            yield return AttackOver(p, one.Started, 3f);
            yield return Settle(rig);
            mark = LogTap.Mark;
            var two = new RollRun();
            yield return Roll(rig, two);
            p.m_queuedAttackTimer = 0f;
            c.Check(one.Clone != null && two.Clone == null && two.Started != null && !two.StartedInRoll && Vanilla(two.Started, shared),
                $"Cooldown 5: the second roll's attack is a normal swing after the roll (got {Fired(two.Started)})");
            var line = LogTap.First(mark, Dbg, "Roll attack skipped: cooldown");
            var match = CooldownLine.Match(line ?? "");
            var left = 5f - (two.StartAt - MoveTracker.LastMoveAt);
            c.Check(match.Success && LogTap.Count(mark, Dbg, "Roll attack skipped: cooldown") == 1 && Mathf.Abs(Number(match) - left) <= 0.06f,
                $"Cooldown 5: Debug \"Roll attack skipped: cooldown ({F(left)} s left).\" once (got \"{line ?? "none"}\")");
            yield return AttackOver(p, two.Started, 3f);
            rig.TakeBack(sword);

            // Cooldown 0, knife: jump attack, land, jump again at once, attack: a jump attack.
            yield return Equip(rig, RowOf("KnifeCopper"), held);
            var knife = held.Value;
            c.Check(knife != null, "KnifeCopper equipped");
            if (knife != null)
            {
                ServerRules.TestRules = Loose();
                MoveTracker.Reset();
                yield return Settle(rig);
                var jump = new JumpRun();
                yield return JumpPress(rig, jump, 0.1f, 1.2f);
                c.Check(jump.IsMove && jump.Move.Kind == MoveKind.Jump, "Cooldown 0: the first jump attack plays");
                var jumped = false;
                t0 = Time.fixedTime;
                while (!jumped && Time.fixedTime - t0 < 5f)
                {
                    yield return Fixed;
                    if (p.IsOnGround() && !p.InAttack() && Time.fixedTime - jump.JumpAt > 0.3f)
                    {
                        p.Jump();
                        jumped = p.m_jumpTimer == 0f && MoveTracker.JumpLive;
                    }
                }
                c.Check(jumped, "landed and jumped again as soon as the game took the jump");
                yield return WaitTicks(0.1f);
                last = p.m_currentAttack;
                Press(p);
                yield return WaitNewAttack(p, last, 0.4f, box);
                c.Check(box.Value != null && !ReferenceEquals(MoveTracker.LastMove.Clone, jump.Clone) && MoveTracker.LastMove.Kind == MoveKind.Jump
                        && ReferenceEquals(box.Value, MoveTracker.LastMove.Clone),
                    $"Cooldown 0: the second jump right after gives a jump attack too ({S(MoveTracker.LastStartAt - jump.StartAt)} s after the first)");
                yield return AttackOver(p, box.Value, 3f);
            }
            yield return Settle(rig);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- moveset.stamina-jump ----------

    // Jump + one press with the stamina watched. jumpCost: what the jump took. swingDrop: biggest one-tick drop after
    // the press (the swing, paid when it enters). staminaAfterJump >= 0: stamina set to that right before the press.
    private static IEnumerator JumpStamina(Rig rig, JumpRun run, Box<float> jumpCost, Box<float> swingDrop, float staminaAfterJump = -1f)
    {
        var p = rig.P;
        var before = MoveTracker.LastMove.Clone;
        var prev = p.m_currentAttack;
        var s0 = p.m_stamina;
        p.Jump();
        run.JumpAt = Time.fixedTime;
        run.Jumped = p.m_jumpTimer == 0f;
        run.TokenAtJump = MoveTracker.JumpLive;
        jumpCost.Value = s0 - p.m_stamina;
        yield return WaitTicks(0.1f);
        if (staminaAfterJump >= 0f)
        {
            p.m_stamina = staminaAfterJump;
        }
        Press(p);
        run.PressAt = Time.fixedTime;
        var previous = p.m_stamina;
        swingDrop.Value = 0f;
        var after = 0;
        while (Time.fixedTime - run.PressAt < 1.4f && after < 3)
        {
            yield return Fixed;
            var now = Time.fixedTime;
            var s = p.m_stamina;
            swingDrop.Value = Mathf.Max(swingDrop.Value, previous - s);
            previous = s;
            if (run.LandAt < 0f && now - run.JumpAt >= MoveTracker.JumpGroundLock && p.IsOnGround())
            {
                run.LandAt = now;
            }
            var current = p.m_currentAttack;
            if (run.Started == null && current != null && !ReferenceEquals(current, prev))
            {
                run.Started = current;
                run.StartAt = now;
                run.StartAir = !p.IsOnGround();
            }
            if (run.Clone == null && !ReferenceEquals(MoveTracker.LastMove.Clone, before))
            {
                run.Move = MoveTracker.LastMove;
                run.Clone = run.Move.Clone;
            }
            if (run.Started != null && run.Started.m_wasInAttack)
            {
                if (run.EnteredAt < 0f)
                {
                    run.EnteredAt = now;
                }
                after++;
            }
            if (run.Started == null && run.LandAt >= 0f && now - run.LandAt > 0.3f)
            {
                break; // landed and nothing started
            }
        }
    }

    private static IEnumerator RunStaminaJump()
    {
        var p = LocalPlayer(StaminaJumpName);
        if (p == null)
        {
            yield break;
        }
        var zone = ZoneSystem.instance;
        if (zone == null)
        {
            SelfTest.Fail(StaminaJumpName, "no ZoneSystem");
            yield break;
        }
        var c = new Checks(StaminaJumpName);
        var rig = new Rig(StaminaJumpName, p);
        var god = p.InGodMode();
        var hadRate = zone.GetGlobalKey(GlobalKeys.StaminaRate, out float rate);
        try
        {
            p.SetGodMode(false);
            zone.RemoveGlobalKey(GlobalKeys.StaminaRate);
            var ready = new Box<bool>();
            yield return WaitFor(() => Game.m_staminaRate > 0f, 3f, ready);
            c.Check(ready.Value, $"stamina is used again (rate {F(Game.m_staminaRate)})");
            HitTap.Start(); // counts the stamina bar's "empty" flashes
            var held = new Box<ItemDrop.ItemData>();
            yield return Equip(rig, SwordRow, held);
            var weapon = held.Value;
            if (weapon == null)
            {
                c.Check(false, "could not equip SwordIron");
                c.Report();
                yield break;
            }
            var shared = weapon.m_shared.m_attack;
            var probe = shared.Clone();
            probe.m_character = p;
            probe.m_weapon = weapon;
            var cost = probe.GetAttackStamina();
            c.Check(cost > 0.5f && p.GetMaxStamina() > cost * 3f + 15f, $"a sword swing costs stamina ({F(cost)}) and the player can hold a jump plus three swings ({F(p.GetMaxStamina())})");
            var jumpCost = new Box<float>();
            var drop = new Box<float>();

            // 1. Default: the jump attack costs the jump plus one normal swing.
            var rules = Loose();
            ServerRules.TestRules = rules;
            yield return Settle(rig);
            p.m_stamina = p.GetMaxStamina();
            yield return Fixed;
            var run = new JumpRun();
            yield return JumpStamina(rig, run, jumpCost, drop);
            c.Check(run.IsMove && run.Move.Kind == MoveKind.Jump && jumpCost.Value > 0.5f && Mathf.Abs(drop.Value - cost) <= 0.6f,
                $"a jump attack costs the jump ({F(jumpCost.Value)}) plus one normal swing ({F(drop.Value)}, a swing is {F(cost)})");
            yield return AttackOver(p, run.Started, 3f);

            // 2. StaminaMultiplier 3: the swing costs three swings.
            rules = Loose();
            rules.Jump.Stamina = 3f;
            ServerRules.TestRules = rules;
            yield return Settle(rig);
            p.m_stamina = p.GetMaxStamina();
            yield return Fixed;
            run = new JumpRun();
            yield return JumpStamina(rig, run, jumpCost, drop);
            c.Check(run.IsMove && Near(run.Clone.m_attackStamina, shared.m_attackStamina * 3f) && Mathf.Abs(drop.Value - cost * 3f) <= 0.6f,
                $"Jump attack StaminaMultiplier 3: the jump attack's swing costs three swings ({F(drop.Value)}, expected {F(cost * 3f)})");
            yield return AttackOver(p, run.Started, 3f);

            // 3. Stamina for one swing but not three after the jump: a normal swing at once, its Debug line, no flash.
            yield return Settle(rig);
            p.m_stamina = p.GetMaxStamina();
            yield return Fixed;
            var mark = LogTap.Mark;
            var flashes = HitTap.Flashes;
            run = new JumpRun();
            yield return JumpStamina(rig, run, jumpCost, drop, cost * 2f);
            c.Check(run.Started != null && run.Clone == null && run.StartAir && run.StartAt - run.PressAt <= 2f * Time.fixedDeltaTime + 0.001f
                    && Vanilla(run.Started, shared),
                $"stamina for one swing but not three: a normal swing on the first try (got {Fired(run.Started)})");
            c.Check(LogTap.Count(mark, Dbg, "Jump attack skipped: stamina for a normal swing only.") == 1 && LogTap.Count(mark, Dbg, "Jump attack: ") == 0,
                "Debug \"Jump attack skipped: stamina for a normal swing only.\" once, no jump attack line");
            c.Check(HitTap.Flashes == flashes, $"the stamina bar does not flash ({HitTap.Flashes - flashes} flashes)");
            yield return AttackOver(p, run.Started, 3f);

            // 4. Too little stamina for any swing after the jump: no attack in that jump, the bar flashes like in the
            //    game without the move (same jump with the jump attack turned off).
            var withMove = 0;
            var without = 0;
            for (var pass = 0; pass < 2; pass++)
            {
                rules = Loose();
                rules.Jump.Stamina = 3f;
                rules.JumpAttack = pass == 0;
                ServerRules.TestRules = rules;
                yield return Settle(rig);
                p.m_stamina = p.GetMaxStamina();
                yield return Fixed;
                mark = LogTap.Mark;
                flashes = HitTap.Flashes;
                var moved = MoveTracker.LastMove.Clone;
                run = new JumpRun();
                yield return JumpStamina(rig, run, jumpCost, drop, cost * 0.5f);
                yield return WaitLanded(p, 3f);
                yield return WaitTicks(0.3f);
                var count = HitTap.Flashes - flashes;
                if (pass == 0)
                {
                    withMove = count;
                    c.Check(run.Started == null && ReferenceEquals(MoveTracker.LastMove.Clone, moved),
                        "too little stamina for any swing: no attack in that jump");
                    c.Check(count >= 1 && LogTap.Count(mark, Dbg, "Jump attack") == 0, $"the swing fails like in the game: the stamina bar flashes ({count} calls while the press is remembered), no move line");
                    c.Check(!MoveTracker.JumpLive, "the jump token ends on landing");
                }
                else
                {
                    without = count;
                }
            }
            c.Check(Mathf.Abs(withMove - without) <= 1,
                $"the same number of flash calls as the game makes without the move for one press ({withMove} with the jump attack on, {without} with it off)");
            p.m_stamina = p.GetMaxStamina();
            yield return Settle(rig);
            c.Report();
        }
        finally
        {
            HitTap.Stop();
            if (p != null)
            {
                p.SetGodMode(god);
                p.m_stamina = p.GetMaxStamina();
            }
            if (zone != null)
            {
                if (hadRate)
                {
                    zone.SetGlobalKey(GlobalKeys.StaminaRate, rate);
                }
                else
                {
                    zone.RemoveGlobalKey(GlobalKeys.StaminaRate);
                }
            }
            rig.Restore();
        }
    }

    // ---------- moveset.facing ----------

    // Stick held toward a world direction, the way the controller gives it (relative to the camera).
    private static void Stick(Player p, Vector3 world)
    {
        var look = p.m_lookDir;
        look.y = 0f;
        look.Normalize();
        var side = Vector3.Cross(Vector3.up, look);
        var local = new Vector3(Vector3.Dot(world, side), 0f, Vector3.Dot(world, look));
        p.SetControls(local, false, false, false, false, false, false, false, false, false, false);
    }

    private static void LookAlong(Player p, Vector3 dir)
    {
        dir.y = 0f;
        p.m_lookYaw = Quaternion.LookRotation(dir.normalized);
        p.SetMouseLook(Vector2.zero);
    }

    // One roll attack: camera turned to look once the roll's first half is over, stick (null = controller left
    // alone, keyboard case) held toward stick. expect: where the strike must face (zero = where it faced the tick
    // before: no turn). The dummy is put 1.5 m that way when the attack starts and must be hit.
    private static IEnumerator FacingCase(Rig rig, Checks c, Dummy dummy, string what, Vector3 look, Vector3? stick, Vector3 expect)
    {
        var p = rig.P;
        yield return Settle(rig, ServerRules.Current);
        if (stick.HasValue)
        {
            rig.TakeController();
        }
        var prev = p.m_currentAttack;
        var before = MoveTracker.LastMove.Clone;
        var from = HitTap.Hits.Count;
        p.Dodge(rig.Forward);
        var rollStart = -1f;
        var startAt = -1f;
        Attack started = null;
        var facing = Vector3.zero;
        var facingBefore = p.transform.forward;
        var turn = 0f;
        var t0 = Time.fixedTime;
        while (Time.fixedTime - t0 < 4f)
        {
            yield return Fixed;
            var now = Time.fixedTime;
            if (rollStart < 0f && p.m_inDodge)
            {
                rollStart = now;
            }
            if (rollStart < 0f)
            {
                if (now - t0 > 0.6f)
                {
                    break;
                }
                continue;
            }
            var current = p.m_currentAttack;
            if (started == null && current != null && !ReferenceEquals(current, prev))
            {
                started = current;
                startAt = now;
                facing = p.transform.forward;
                turn = Vector3.Angle(facingBefore, facing);
                var target = expect == Vector3.zero ? facing : expect;
                var pos = p.transform.position + target.normalized * 1.5f;
                pos.y = ZoneSystem.instance.GetGroundHeight(pos);
                Place(dummy.Body, pos);
                Physics.SyncTransforms();
            }
            if (started == null)
            {
                facingBefore = p.transform.forward;
                if (now - rollStart > 0.5f)
                {
                    LookAlong(p, look);
                    if (stick.HasValue)
                    {
                        Stick(p, stick.Value);
                    }
                }
                if (p.m_inDodge)
                {
                    Press(p);
                }
            }
            else if (started.m_attackDone || HitTap.OnTarget(dummy.Body, from).Count > 0 || now - startAt > 1.5f)
            {
                break;
            }
        }
        if (stick.HasValue)
        {
            rig.GiveController();
        }
        p.m_queuedAttackTimer = 0f;
        var isMove = started != null && !ReferenceEquals(MoveTracker.LastMove.Clone, before) && ReferenceEquals(MoveTracker.LastMove.Clone, started)
                     && MoveTracker.LastMove.Kind == MoveKind.Roll;
        c.Check(isMove, $"{what}: a roll attack started");
        if (expect == Vector3.zero)
        {
            c.Check(started != null && turn < 10f, $"{what}: the strike does not turn (turned {F(turn)} degrees on the start tick)");
        }
        else
        {
            var off = started != null ? Vector3.Angle(facing, expect) : 180f;
            c.Check(off < 8f, $"{what}: the strike faces the expected way ({F(off)} degrees off)");
        }
        c.Check(HitTap.OnTarget(dummy.Body, from).Count > 0, $"{what}: it hits the target standing that way");
        yield return AttackOver(p, started, 3f);
        Place(dummy.Body, ParkSpot(rig, 3)); // out of the next roll's way
    }

    private static IEnumerator RunFacing()
    {
        var p = LocalPlayer(FacingName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(FacingName);
        var rig = new Rig(FacingName, p);
        var option = p.AttackTowardsPlayerLookDir;
        try
        {
            ServerRules.TestRules = Loose();
            HitTap.Start();
            var held = new Box<ItemDrop.ItemData>();
            yield return Equip(rig, SwordRow, held);
            var dummy = rig.SpawnDummy(8f);
            if (held.Value == null || dummy == null)
            {
                c.Check(false, "SwordIron equipped and training dummy spawned");
                c.Report();
                yield break;
            }
            SelfTest.Note(FacingName, $"the game's attack direction option was {(option ? "on (attacks follow the stick)" : "off (attacks follow the camera)")} before the test");
            var back = -rig.Forward;
            p.AttackTowardsPlayerLookDir = false;
            yield return FacingCase(rig, c, dummy, "option off, looking back while rolling", back, null, back);
            p.AttackTowardsPlayerLookDir = true;
            yield return FacingCase(rig, c, dummy, "option on, stick held in the roll direction, looking back", back, rig.Forward, rig.Forward);
            yield return FacingCase(rig, c, dummy, "option on, stick pulled back toward the target", back, back, back);
            yield return FacingCase(rig, c, dummy, "option on, no stick input", back, Vector3.zero, Vector3.zero);
            p.AttackTowardsPlayerLookDir = option;
            yield return Settle(rig);
            c.Report();
        }
        finally
        {
            if (p != null)
            {
                p.AttackTowardsPlayerLookDir = option;
            }
            HitTap.Stop();
            rig.Restore();
        }
    }

    // ---------- moveset.gamepad ----------

    private static void Pad(InputLayout layout)
    {
        ZInput.InputLayout = layout;
        ZInput.m_inputSource = ZInput.InputSource.Gamepad;
        ZInput.s_inputSwitchingMode = ZInput.InputSource.AutomaticNonBlocking;
    }

    private static void PadJump(Player p, bool blockHold) =>
        p.SetControls(Vector3.zero, false, false, false, false, false, blockHold, true, false, false, false);

    private static IEnumerator RunGamepad()
    {
        var p = LocalPlayer(GamepadName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(GamepadName);
        var rig = new Rig(GamepadName, p);
        var layout = ZInput.InputLayout;
        var source = ZInput.m_inputSource;
        var switching = ZInput.s_inputSwitchingMode;
        try
        {
            ServerRules.TestRules = Loose();
            var held = new Box<ItemDrop.ItemData>();
            yield return Equip(rig, SwordRow, held);
            if (held.Value == null)
            {
                c.Check(false, "could not equip SwordIron");
                c.Report();
                yield break;
            }
            var box = new Box<Attack>();
            var crouching = new Box<bool>();
            rig.TakeController();
            Pad(InputLayout.Alternative1);
            c.Check(ZInput.IsGamepadActive() && ZInput.IsNonClassicFunctionality(),
                "gamepad made the active input with the Alternative1 layout (in memory, for the test)");

            // Alternative layouts: crouch, press jump = a jump (the crouch ends), then a jump attack.
            foreach (var alt in new[] { InputLayout.Alternative1, InputLayout.Alternative2 })
            {
                foreach (var block in new[] { false, true })
                {
                    var what = $"{alt} layout, {(block ? "block held" : "crouching")}, jump pressed";
                    yield return Settle(rig);
                    if (!block)
                    {
                        p.SetCrouch(true);
                        yield return WaitFor(() => p.IsCrouching(), 2f, crouching);
                        c.Check(crouching.Value, $"{what}: the player crouches first");
                    }
                    var moved = MoveTracker.LastMove.Clone;
                    Pad(alt);
                    PadJump(p, block);
                    var jumped = p.m_jumpTimer == 0f && MoveTracker.JumpLive;
                    c.Check(jumped && p.m_queuedDodgeTimer <= 0f && !p.m_crouchToggled,
                        $"{what}: the player jumps (no roll) and the crouch ends");
                    Release(p);
                    yield return WaitTicks(0.1f);
                    var last = p.m_currentAttack;
                    Press(p);
                    yield return WaitNewAttack(p, last, 0.4f, box);
                    c.Check(box.Value != null && !ReferenceEquals(MoveTracker.LastMove.Clone, moved) && MoveTracker.LastMove.Kind == MoveKind.Jump
                            && ReferenceEquals(box.Value, MoveTracker.LastMove.Clone),
                        $"{what}: attacking in the air gives a jump attack");
                    yield return AttackOver(p, box.Value, 3f);
                    yield return WaitLanded(p, 3f);
                    c.Check(!p.IsCrouching(), $"{what}: not crouching after the jump");
                }
            }

            // Default layout: the same press rolls, and the attack is a roll attack.
            yield return Settle(rig);
            p.SetCrouch(true);
            yield return WaitFor(() => p.IsCrouching(), 2f, crouching);
            var before = MoveTracker.LastMove.Clone;
            var prev = p.m_currentAttack;
            Pad(InputLayout.Default);
            PadJump(p, false);
            c.Check(crouching.Value && p.m_queuedDodgeTimer > 0f && !MoveTracker.JumpLive,
                "Default layout, crouching, jump pressed: the game asks for a roll, not a jump");
            Release(p);
            var edge = new Box<float>();
            yield return WaitRollEdge(p, 4f, true, edge);
            yield return WaitNewAttack(p, prev, 0.6f, box);
            p.m_queuedAttackTimer = 0f;
            c.Check(edge.Value >= 0f && box.Value != null && !ReferenceEquals(MoveTracker.LastMove.Clone, before)
                    && MoveTracker.LastMove.Kind == MoveKind.Roll && ReferenceEquals(box.Value, MoveTracker.LastMove.Clone),
                "Default layout: the player rolls and the attack is a roll attack");
            p.SetCrouch(false);
            yield return AttackOver(p, box.Value, 3f);
            rig.GiveController();
            ZInput.InputLayout = layout;
            ZInput.m_inputSource = source;
            ZInput.s_inputSwitchingMode = switching;
            yield return Settle(rig);
            c.Report();
        }
        finally
        {
            ZInput.InputLayout = layout;
            ZInput.m_inputSource = source;
            ZInput.s_inputSwitchingMode = switching;
            if (p != null)
            {
                p.SetCrouch(false);
            }
            rig.Restore();
        }
    }

    // ---------- moveset.interrupt ----------

    private static IEnumerator RunInterrupt()
    {
        var p = LocalPlayer(InterruptName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(InterruptName);
        var rig = new Rig(InterruptName, p);
        try
        {
            ServerRules.TestRules = Loose();
            var held = new Box<ItemDrop.ItemData>();
            yield return Equip(rig, SwordRow, held);
            if (held.Value == null)
            {
                c.Check(false, "could not equip SwordIron");
                c.Report();
                yield break;
            }
            var animator = p.m_zanim.m_animator;
            for (var pass = 0; pass < 2; pass++)
            {
                // Pass 0: the roll attack starts after the roll with its trigger alone (no attack state: 0.2 s of
                // "starting"), so the second roll always gets in. Pass 1: as played (cross-fade, starts at once).
                var what = pass == 0 ? "roll attack still starting, roll again at once" : "roll attack as played, roll again at once";
                RollFlow.TestNoState = pass == 0;
                yield return Settle(rig);
                var mark = LogTap.Mark;
                var before = MoveTracker.LastMove.Clone;
                var resets = MoveTracker.TriggerResets;
                var failures = MoveTracker.StartFailures;
                var warnings = MoveTracker.StartWarnings;
                p.Dodge(rig.Forward);
                Attack clone = null;
                var starting = false;
                var seq = 0;
                var t0 = Time.fixedTime;
                while (clone == null && Time.fixedTime - t0 < 4f)
                {
                    yield return Fixed;
                    if (p.m_inDodge)
                    {
                        Press(p);
                    }
                    if (!ReferenceEquals(MoveTracker.LastMove.Clone, before))
                    {
                        clone = MoveTracker.LastMove.Clone;
                        starting = MoveTracker.WatchStarting && ReferenceEquals(MoveTracker.Watched.Clone, clone);
                        seq = MoveTracker.RollSeq;
                        p.m_queuedAttackTimer = 0f;
                        p.Dodge(-rig.Forward); // block + jump, pressed at once
                    }
                }
                if (clone == null)
                {
                    c.Check(false, $"{what}: the roll attack started");
                    continue;
                }
                var rolledAgain = false;
                var entered = false;
                var swungAfterRoll = false;
                t0 = Time.fixedTime;
                while (Time.fixedTime - t0 < 1.8f)
                {
                    yield return Fixed;
                    if (MoveTracker.RollSeq > seq)
                    {
                        rolledAgain = true;
                    }
                    entered |= clone.m_wasInAttack || (MoveTracker.LastEntryDelay >= 0f && ReferenceEquals(MoveTracker.LastMove.Clone, clone));
                    RollFlow.NextOrCurrent(animator, out _, out var tag);
                    if (rolledAgain && tag == RollFlow.AttackTag)
                    {
                        swungAfterRoll = true;
                    }
                }
                var dropLine = LogTap.Count(mark, Dbg, "Roll attack swing_longsword1 was dropped before it started (a roll started); trigger reset.");
                if (pass == 0)
                {
                    c.Check(starting && !MoveTracker.LastMove.Cut && MoveTracker.LastMove.Flow == FlowResult.TriggerOnly,
                        $"{what}: the roll attack was still starting when the second roll was asked");
                    c.Check(rolledAgain && !entered, $"{what}: the second roll plays and the roll attack never enters its animation");
                    c.Check(clone.m_attackDone && !ReferenceEquals(p.m_currentAttack, clone) && MoveTracker.TriggerResets == resets + 1,
                        $"{what}: the move is stopped, taken off the player, its trigger reset once");
                    c.Check(dropLine == 1, $"{what}: Debug \"Roll attack swing_longsword1 was dropped before it started (a roll started); trigger reset.\" ({dropLine})");
                }
                else
                {
                    c.Check((entered && dropLine == 0) || (!entered && rolledAgain && dropLine == 1),
                        $"{what}: either the roll attack plays or the second roll plays with the move dropped (entered {entered}, second roll {rolledAgain}, dropped {dropLine})");
                    SelfTest.Note(InterruptName, $"{what}: the roll attack {(entered ? "played" : "was dropped")}, the second roll {(rolledAgain ? "started within 1.8 s" : "did not start")}");
                }
                c.Check(!swungAfterRoll, $"{what}: no swing plays after the second roll started");
                c.Check(MoveTracker.StartFailures == failures && MoveTracker.StartWarnings == warnings && LogTap.Count(mark, Wrn, "did not start") == 0,
                    $"{what}: no \"did not start\" warning");
                p.m_queuedDodgeTimer = 0f;
                yield return WaitIdle(p, 4f);
            }
            RollFlow.TestNoState = false;
            yield return Settle(rig);
            c.Report();
        }
        finally
        {
            RollFlow.TestNoState = false;
            rig.Restore();
        }
    }

    // ---------- moveset.flow ----------

    private static readonly Regex AnyCutLine = new Regex(@"; cut into the roll (\d+(?:\.\d+)?) s after it started \(");

    private static IEnumerator RunFlow()
    {
        var p = LocalPlayer(FlowName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(FlowName);
        var rig = new Rig(FlowName, p);
        try
        {
            HitTap.Start();
            var held = new Box<ItemDrop.ItemData>();
            yield return Equip(rig, SwordRow, held);
            if (held.Value == null)
            {
                c.Check(false, "could not equip SwordIron");
                c.Report();
                yield break;
            }
            var animator = p.m_zanim.m_animator;
            var dt = Time.fixedDeltaTime;
            var distances = "";

            // FlowStart 2 (longer than the roll), attack held: the swing starts as the roll ends, out of its blend.
            var rules = Loose();
            rules.FlowStart = 2f;
            ServerRules.TestRules = rules;
            yield return Settle(rig);
            var mark = LogTap.Mark;
            var run = new RollRun();
            rig.TakeController();
            yield return Roll(rig, run, true, q => Hold(q, false));
            rig.GiveController();
            var line = LogTap.First(mark, Dbg, "Roll attack: ");
            c.Check(run.Clone != null && run.Move.Kind == MoveKind.Roll && !run.Move.Cut && !run.StartedInRoll
                    && run.RollEnd >= 0f && Mathf.Abs(run.StartAt - run.RollEnd - dt) < 0.5f * dt,
                $"FlowStart 2: no cut; the roll attack starts on the tick after the roll ends ({Span(run.RollEnd, run.StartAt)} s)");
            c.Check(run.Clone != null && run.Move.Flow == FlowResult.CrossFade && run.EnteredAt >= 0f && run.Delay <= 3f * dt + 0.001f && run.GapTicks <= 3,
                $"FlowStart 2: still no stand-up: cross-faded out of the roll's end, in its animation {S(run.Delay)} s later (idle for {S(run.Gap)} s at most; the stand-up takes about 0.22 s)");
            c.Check(line != null && line.Contains(" s after the roll ended (cross-fade from the roll, "),
                $"FlowStart 2: Debug \"... s after the roll ended (cross-fade from the roll, ...)\" (got \"{line ?? "none"}\")");
            yield return AttackOver(p, run.Started, 3f);

            // FlowBlend 0 / 0.15 / 0.4: the blend lasts that long, the hit comes at the same moment.
            var hitAt = new List<float>();
            foreach (var blend in new[] { 0f, 0.15f, 0.4f })
            {
                rules = Loose();
                rules.FlowBlend = blend;
                ServerRules.TestRules = rules;
                yield return Settle(rig);
                var from = p.transform.position;
                run = new RollRun();
                yield return Roll(rig, run);
                p.m_queuedAttackTimer = 0f;
                var until = Time.fixedTime + 1f;
                while (animator.IsInTransition(0) && Time.fixedTime < until)
                {
                    yield return Fixed;
                }
                var blended = run.StartAt >= 0f ? Time.fixedTime - run.StartAt : -1f;
                var fired = new Box<bool>();
                var clone = run.Clone;
                yield return WaitFor(() => clone != null && HitTap.Of(clone).Count > 0, 2f, fired);
                var hit = fired.Value ? HitTap.Of(clone)[0].At - run.StartAt : -1f;
                if (hit >= 0f)
                {
                    hitAt.Add(hit);
                }
                c.Check(clone != null && run.Move.Cut && Mathf.Abs(RollFlow.LastBlend - blend) < 0.0005f,
                    $"FlowBlend {F(blend)}: the cross-fade out of the roll is asked with {F(blend)} s (got {F(RollFlow.LastBlend)})");
                c.Check(blended >= 0f && Mathf.Abs(blended - dt - blend) <= 0.07f,
                    $"FlowBlend {F(blend)}: the Animator blends for about that long ({S(blended)} s from the attack start to the end of the blend)");
                c.Check(hit >= 0f, $"FlowBlend {F(blend)}: the swing's hit moment comes ({S(hit)} s after the start)");
                yield return AttackOver(p, run.Started, 3f);
                yield return WaitIdle(p, 4f);
                if (Mathf.Abs(blend - MoveRules.DefaultFlowBlend) < 0.001f)
                {
                    distances += $"FlowStart 0.85: {F(Flat(p.transform.position - from))} m; ";
                }
            }
            c.Check(hitAt.Count == 3 && hitAt.Max() - hitAt.Min() <= dt + 0.001f,
                $"the hit comes at the same moment whatever the blend ({string.Join(", ", hitAt.Select(S).ToArray())} s after the start)");

            // FlowStart 0.7 and 0: where the roll attack cuts in.
            foreach (var start in new[] { 0.7f, 0f })
            {
                rules = Loose();
                rules.FlowStart = start;
                ServerRules.TestRules = rules;
                yield return Settle(rig);
                var from = p.transform.position;
                mark = LogTap.Mark;
                run = new RollRun();
                yield return Roll(rig, run);
                p.m_queuedAttackTimer = 0f;
                line = LogTap.First(mark, Dbg, "Roll attack: ");
                var said = Number(AnyCutLine.Match(line ?? ""));
                if (start > 0f)
                {
                    c.Check(run.Clone != null && run.Move.Cut && Mathf.Abs(run.Cut - start) <= 1.5f * dt && Mathf.Abs(said - run.Cut) <= 0.011f,
                        $"FlowStart {F(start)}: the roll attack cuts into the roll {S(run.Cut)} s after it started, and its Debug line says so ({F(said)})");
                }
                else
                {
                    var off = run.IframesEnd >= 0f ? run.IframesEnd - run.RollStart : -1f;
                    c.Check(run.Clone != null && run.Move.Cut && off >= 0f && run.Cut - off >= MoveTracker.IframeMargin - 0.5f * dt
                            && run.Cut - off <= MoveTracker.IframeMargin + 2.5f * dt && said >= 0.58f && said <= 0.7f,
                        $"FlowStart 0: the roll attack starts 0.2 s after the roll's invulnerability ends (cut {S(run.Cut)} s, invulnerable until {S(off)} s; Debug \"cut into the roll {F(said)} s after it started\")");
                }
                c.Check(run.GapTicks == 0 && !run.InvulnAfterStart, $"FlowStart {F(start)}: no stand-up, never invulnerable from the attack start on");
                yield return AttackOver(p, run.Started, 3f);
                yield return WaitIdle(p, 4f);
                distances += $"FlowStart {F(start)}: {F(Flat(p.transform.position - from))} m; ";
            }

            // Roll with no attack, for the distance note.
            yield return Settle(rig);
            var home = p.transform.position;
            var edge = new Box<float>();
            p.Dodge(rig.Forward);
            yield return WaitRollEdge(p, 4f, false, edge);
            yield return WaitIdle(p, 4f);
            distances += $"roll with no attack: {F(Flat(p.transform.position - home))} m";
            SelfTest.Note(FlowName, "ground covered from the roll's start until the player stands still again (roll + roll attack): " + distances);
            yield return Settle(rig);
            c.Report();
        }
        finally
        {
            HitTap.Stop();
            rig.Restore();
        }
    }

    private static float Flat(Vector3 v)
    {
        v.y = 0f;
        return v.magnitude;
    }

    // ---------- moveset.iframes ----------

    // One small dodgeable hit on the player, through the game's own damage path. Returns the health it took.
    private static float Poke(Player p)
    {
        var before = p.GetHealth();
        var hit = new HitData { m_dodgeable = true, m_blockable = false, m_staggerMultiplier = 0f };
        hit.m_damage.m_damage = 3f; // plain damage: no armor math, no armor wear, no stagger, no knockback
        hit.m_point = p.GetCenterPoint();
        hit.m_dir = -p.transform.forward;
        p.Damage(hit);
        return before - p.GetHealth();
    }

    private static IEnumerator RunIframes()
    {
        var p = LocalPlayer(IframesName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(IframesName);
        var rig = new Rig(IframesName, p);
        var god = p.InGodMode();
        var health = p.GetHealth();
        try
        {
            p.SetGodMode(false);
            var held = new Box<ItemDrop.ItemData>();
            yield return Equip(rig, SwordRow, held);
            if (held.Value == null)
            {
                c.Check(false, "could not equip SwordIron");
                c.Report();
                yield break;
            }
            c.Check(p.GetMaxHealth() >= 20f, $"the player has health to lose ({F(p.GetMaxHealth())})");
            yield return Settle(rig);
            p.SetHealth(p.GetMaxHealth());
            c.Check(Poke(p) > 0.5f, "control: a hit outside a roll hurts the player (god mode off for this test)");
            var rolls = new[] { MoveRules.DefaultFlowStart, MoveRules.DefaultFlowStart, 0f };
            for (var i = 0; i < rolls.Length; i++)
            {
                var what = $"roll {i + 1} (FlowStart {F(rolls[i])})";
                var rules = Loose();
                rules.FlowStart = rolls[i];
                ServerRules.TestRules = rules;
                yield return Settle(rig);
                p.SetHealth(p.GetMaxHealth());
                var before = MoveTracker.LastMove.Clone;
                var prev = p.m_currentAttack;
                p.Dodge(rig.Forward);
                var rollStart = -1f;
                var startAt = -1f;
                Attack started = null;
                var during = -1f;       // health lost to the hit inside the roll's invulnerability
                var atStart = -1f;
                var later = -1f;
                var inSwing = -1f;
                var invulnerableAtStart = false;
                var t0 = Time.fixedTime;
                while (Time.fixedTime - t0 < 4f)
                {
                    yield return Fixed;
                    var now = Time.fixedTime;
                    if (rollStart < 0f && p.m_inDodge)
                    {
                        rollStart = now;
                    }
                    if (rollStart < 0f)
                    {
                        if (now - t0 > 0.6f)
                        {
                            break;
                        }
                        continue;
                    }
                    if (during < 0f && now - rollStart >= 0.1f && p.IsDodgeInvincible())
                    {
                        during = Poke(p);
                    }
                    var current = p.m_currentAttack;
                    if (started == null && current != null && !ReferenceEquals(current, prev))
                    {
                        started = current;
                        startAt = now;
                        invulnerableAtStart = p.IsDodgeInvincible();
                        atStart = Poke(p);
                    }
                    if (started == null && p.m_inDodge)
                    {
                        Press(p);
                    }
                    if (started != null && later < 0f && now - startAt >= 2f * Time.fixedDeltaTime - 0.001f)
                    {
                        later = Poke(p);
                    }
                    if (started != null && inSwing < 0f && now - startAt >= 0.25f)
                    {
                        inSwing = Poke(p);
                        break;
                    }
                }
                p.m_queuedAttackTimer = 0f;
                var isMove = started != null && !ReferenceEquals(MoveTracker.LastMove.Clone, before) && ReferenceEquals(MoveTracker.LastMove.Clone, started)
                             && MoveTracker.LastMove.Cut;
                c.Check(isMove, $"{what}: a roll attack cut into the roll");
                c.Check(Mathf.Abs(during) < 0.001f, $"{what}: a hit during the roll's invulnerability does nothing (lost {F(during)} health)");
                c.Check(!invulnerableAtStart && atStart > 0.5f && later > 0.5f && inSwing > 0.5f,
                    $"{what}: hits on the roll attack's start tick, two ticks later and 0.25 s into the swing all hurt (lost {F(atStart)}, {F(later)}, {F(inSwing)} health)");
                yield return AttackOver(p, started, 3f);
            }
            yield return Settle(rig);
            c.Report();
        }
        finally
        {
            if (p != null)
            {
                p.SetHealth(health);
                p.SetGodMode(god);
            }
            rig.Restore();
        }
    }

    // ---------- moveset.after-roll ----------

    private static IEnumerator RunAfterRoll()
    {
        var p = LocalPlayer(AfterRollName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(AfterRollName);
        var rig = new Rig(AfterRollName, p);
        try
        {
            var rules = Loose();
            ServerRules.TestRules = rules;
            var dt = Time.fixedDeltaTime;
            var held = new Box<ItemDrop.ItemData>();

            // 1. Battleaxe (its roll attack is off), attack held through the roll.
            yield return Equip(rig, RowOf("Battleaxe"), held);
            var axe = held.Value;
            c.Check(axe != null, "Battleaxe equipped");
            if (axe != null)
            {
                yield return Settle(rig);
                var mark = LogTap.Mark;
                var run = new RollRun();
                rig.TakeController();
                yield return Roll(rig, run, true, q => Hold(q, false));
                rig.GiveController();
                c.Check(run.Clone == null && run.Started != null && !run.StartedInRoll && run.RollEnd >= 0f
                        && Mathf.Abs(run.StartAt - run.RollEnd - dt) < 0.5f * dt && Vanilla(run.Started, axe.m_shared.m_attack),
                    $"Battleaxe, attack held through a roll: its normal swing starts only once the roll is over (got {Fired(run.Started)}, {Span(run.RollEnd, run.StartAt)} s after the roll)");
                c.Check(run.GapTicks > 0 && run.Refusals == 0 && LogTap.Count(mark, Dbg, "Roll attack") == 0,
                    $"Battleaxe: the usual stand-up in between (idle for {S(run.Gap)} s), no \"Roll attack\" Debug line");
                yield return AttackOver(p, run.Started, 3f);
            }
            rig.TakeBack(axe);

            yield return Equip(rig, SwordRow, held);
            var sword = held.Value;
            c.Check(sword != null, "SwordIron equipped");
            if (sword != null)
            {
                // 2. Sword with RollAttack = false, attack held through the roll.
                var off = Loose();
                off.RollAttack = false;
                ServerRules.TestRules = off;
                yield return Settle(rig);
                var mark = LogTap.Mark;
                var run = new RollRun();
                rig.TakeController();
                yield return Roll(rig, run, true, q => Hold(q, false));
                rig.GiveController();
                c.Check(run.Clone == null && run.Started != null && !run.StartedInRoll && run.RollEnd >= 0f
                        && Mathf.Abs(run.StartAt - run.RollEnd - dt) < 0.5f * dt && Vanilla(run.Started, sword.m_shared.m_attack),
                    $"SwordIron with RollAttack = false, attack held through a roll: a normal swing once the roll is over (got {Fired(run.Started)})");
                c.Check(run.GapTicks > 0 && run.Skips == 0 && run.Refusals == 0 && LogTap.Count(mark, Dbg, "Roll attack") == 0,
                    $"RollAttack = false: the usual stand-up in between (idle for {S(run.Gap)} s), the roll is never opened for an attack, no \"Roll attack\" Debug line");
                yield return AttackOver(p, run.Started, 3f);

                // 3. Sword, secondary attack pressed during the roll.
                ServerRules.TestRules = rules;
                yield return Settle(rig);
                mark = LogTap.Mark;
                run = new RollRun();
                yield return Roll(rig, run, true, PressSecondary);
                p.m_queuedSecondAttackTimer = 0f;
                var special = sword.m_shared.m_secondaryAttack;
                c.Check(run.Clone == null && run.Started != null && !run.StartedInRoll && p.m_currentAttackIsSecondary && special != null
                        && run.Started.m_attackAnimation == special.m_attackAnimation && Near(run.Started.m_damageMultiplier, special.m_damageMultiplier),
                    $"SwordIron, secondary attack pressed during a roll: the normal secondary once the roll is over (got {Fired(run.Started)})");
                c.Check(run.GapTicks > 0 && run.Skips == 0 && run.Refusals == 0 && LogTap.Count(mark, Dbg, "Roll attack") == 0
                        && float.IsNegativeInfinity(MoveTracker.RollEndAt),
                    $"secondary after a roll: the usual stand-up in between (idle for {S(run.Gap)} s), no \"Roll attack\" Debug line, the roll's chance is used up");
                yield return AttackOver(p, run.Started, 3f);
            }
            yield return Settle(rig);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- moveset.crouch-roll ----------

    // Own small test. First run: moveset.x.sneak asked "cut into the roll, no idle tick" of a roll attack out of a
    // sneak roll (crouch + jump) and failed on it twice, with no numbers, while the hit and the backstab were fine.
    // What a roll attack must do out of ANY roll: flow out of it, cut into it (then no idle tick at all) or out of its
    // last blend, in its animation within three ticks; never the roll, a pose, then the roll attack (cross-fade =
    // the Animator was still in the roll). Which of the two a roll from a crouch gives is NOTEd next to a standing
    // roll; every check says its numbers.
    private static IEnumerator RunCrouchRoll()
    {
        var p = LocalPlayer(CrouchRollName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(CrouchRollName);
        var rig = new Rig(CrouchRollName, p);
        try
        {
            var rules = Loose();
            ServerRules.TestRules = rules;
            var held = new Box<ItemDrop.ItemData>();
            yield return Equip(rig, SwordRow, held);
            if (held.Value == null)
            {
                c.Check(false, "could not equip SwordIron");
                c.Report();
                yield break;
            }
            var dt = Time.fixedDeltaTime;
            var failures = MoveTracker.StartFailures;
            var alerts = LogTap.AlertMark;
            var crouching = new Box<bool>();

            // Standing roll: the yardstick.
            yield return Settle(rig);
            var standing = new RollRun();
            yield return Roll(rig, standing);
            p.m_queuedAttackTimer = 0f;
            yield return AttackOver(p, standing.Started, 3f);
            SelfTest.Note(CrouchRollName, $"standing roll, attack pressed in the roll: {RollWhat(standing)}");

            // Twice: the second crouch comes right after a roll attack.
            for (var pass = 0; pass < 2; pass++)
            {
                var what = $"roll from a crouch {pass + 1}, attack pressed in the roll";
                yield return Settle(rig);
                p.SetCrouch(true);
                yield return WaitFor(() => p.IsCrouching(), 2f, crouching);
                var mark = LogTap.Mark;
                var run = new RollRun();
                yield return Roll(rig, run);
                p.m_queuedAttackTimer = 0f;
                var isRoll = run.Clone != null && ReferenceEquals(run.Started, run.Clone) && run.Move.Kind == MoveKind.Roll;
                var said = LogTap.First(mark, Dbg, "Roll attack: ");
                SelfTest.Note(CrouchRollName, $"{what}: {RollWhat(run)}");
                c.Check(crouching.Value && run.RollStart >= 0f && isRoll,
                    $"{what}: the player crouched, rolled, and the attack is a roll attack (crouching {crouching.Value}; {RollWhat(run)})");
                if (isRoll)
                {
                    c.Check(run.Move.Flow == FlowResult.CrossFade && run.EnteredAt >= 0f && run.EnteredAt - run.StartAt <= 3f * dt + 0.001f,
                        $"{what}: the roll attack flows out of the roll (cross-fade while the Animator is still in the roll) and is in its animation within three ticks ({RollWhat(run)})");
                    c.Check(!run.Move.Cut || (run.GapTicks == 0 && run.Cut + 0.001f >= rules.FlowStart),
                        $"{what}: when it is cut into the roll, that is at the cut point or later, with no idle pose before the swing ({RollWhat(run)})");
                    c.Check(!run.InvulnAfterStart, $"{what}: the roll's invulnerability is over when the roll attack starts");
                    c.Check(said != null && said.Contains(run.Move.Cut ? "; cut into the roll " : " s after the roll ended (") && said.Contains("(cross-fade from the roll, "),
                        $"{what}: Debug line of the roll attack says how it left the roll (cross-fade from the roll) (got \"{said ?? "none"}\")");
                }
                yield return AttackOver(p, run.Started, 3f);
                p.SetCrouch(false);
            }
            var bad = LogTap.AlertsSince(alerts, Wrn | Err);
            c.Check(MoveTracker.StartFailures == failures && bad.Count == 0,
                $"no move cancelled by the start check, no warning and no error from {ModInfo.Name} ({bad.Count}{(bad.Count > 0 ? ", first: " + bad[0].Text : "")})");
            yield return Settle(rig);
            c.Report();
        }
        finally
        {
            if (p != null)
            {
                p.SetCrouch(false);
            }
            rig.Restore();
        }
    }

    // ---------- moveset.ledge / moveset.ledge-iframes ----------

    private sealed class LedgeRun
    {
        internal float RollStart = -1f;
        internal float RollEnd = -1f;
        internal float LiftAt = -1f;
        internal float InvulnerableOff = -1f; // tick other players' games stop seeing the roll's invulnerability
        internal float StartAt = -1f;
        internal float EnteredAt = -1f;
        internal Attack Started;
        internal Attack Clone;
        internal MoveInfo Move;
        internal bool StartAir;
        internal bool EnterAir;               // still in the air when the attack was in its animation
        internal bool InvulnerableAtStart;
        internal bool InRollAtPress;          // Animator still in the roll (or its blend out) when me pressed
        internal bool RollingAtFlowStart;     // roll animation still playing when the cut point came
        internal bool JumpToken;

        internal bool IsMove => Clone != null && ReferenceEquals(Clone, Started);
    }

    // Under the 4 m from which the game hurts a falling player.
    private const float LedgeLift = 3.5f;

    // Roll, then the ground goes: me lift the player liftAt s into the roll (negative: on the tick the roll ends).
    // press 0: buffered while rolling; 1: once, on the tick the roll ends; 2: once, 0.3 s after the roll ended.
    private static IEnumerator Ledge(Rig rig, LedgeRun run, float liftAt, int press, float flowStart)
    {
        var p = rig.P;
        var animator = p.m_zanim.m_animator;
        var before = MoveTracker.LastMove.Clone;
        var prev = p.m_currentAttack;
        p.Dodge(rig.Forward);
        var t0 = Time.fixedTime;
        var pressed = false;
        var wasInvulnerable = false;
        while (Time.fixedTime - t0 < 5f)
        {
            yield return Fixed;
            var now = Time.fixedTime;
            var inDodge = p.m_inDodge;
            if (run.RollStart < 0f && inDodge)
            {
                run.RollStart = now;
            }
            if (run.RollStart < 0f)
            {
                if (now - t0 > 0.6f)
                {
                    break;
                }
                continue;
            }
            var invulnerable = p.IsDodgeInvincible();
            if (wasInvulnerable && !invulnerable && run.InvulnerableOff < 0f)
            {
                run.InvulnerableOff = now;
            }
            wasInvulnerable = invulnerable;
            if (run.RollEnd < 0f && !inDodge)
            {
                run.RollEnd = now;
            }
            if (!run.RollingAtFlowStart && inDodge && now - run.RollStart + 0.001f >= flowStart && RollFlow.InRoll(animator))
            {
                run.RollingAtFlowStart = true;
            }
            if (run.LiftAt < 0f && ((liftAt >= 0f && now - run.RollStart >= liftAt) || (liftAt < 0f && run.RollEnd >= 0f)))
            {
                run.LiftAt = now;
                Put(p, p.transform.position + Vector3.up * LedgeLift);
            }
            if (run.LiftAt >= 0f)
            {
                p.m_maxAirAltitude = Mathf.Min(p.m_maxAirAltitude, p.transform.position.y + 2f); // no fall damage
            }
            run.JumpToken |= MoveTracker.JumpLive;
            var current = p.m_currentAttack;
            if (run.Started == null && current != null && !ReferenceEquals(current, prev))
            {
                run.Started = current;
                run.StartAt = now;
                run.StartAir = !p.IsOnGround();
                run.InvulnerableAtStart = invulnerable;
            }
            if (run.Clone == null && !ReferenceEquals(MoveTracker.LastMove.Clone, before))
            {
                run.Move = MoveTracker.LastMove;
                run.Clone = run.Move.Clone;
            }
            if (run.Started == null)
            {
                if (press == 0 && inDodge)
                {
                    Press(p);
                }
                else if (!pressed && run.RollEnd >= 0f && (press == 1 || (press == 2 && now - run.RollEnd >= 0.3f)))
                {
                    pressed = true;
                    run.InRollAtPress = RollFlow.InRoll(animator);
                    Press(p);
                }
                if (run.RollEnd >= 0f && now - run.RollEnd > 1.2f)
                {
                    break;
                }
            }
            else
            {
                if (run.EnteredAt < 0f && run.Started.m_wasInAttack)
                {
                    run.EnteredAt = now;
                    run.EnterAir = !p.IsOnGround();
                }
                if (run.EnteredAt >= 0f || now - run.StartAt > 1f)
                {
                    break;
                }
            }
        }
        p.m_queuedAttackTimer = 0f;
    }

    private static string LedgeWhat(LedgeRun run) =>
        run.Started == null ? "no attack"
        : run.IsMove ? $"{run.Move.Kind} attack {run.Move.Trigger} ({(run.Move.Cut ? "cut into the roll" : "after the roll")}, {run.Move.Flow})"
        : $"normal swing {Fired(run.Started)}";

    private static IEnumerator RunLedge()
    {
        var p = LocalPlayer(LedgeName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(LedgeName);
        var rig = new Rig(LedgeName, p);
        var health = p.GetHealth();
        try
        {
            var rules = Loose();
            ServerRules.TestRules = rules;
            HitTap.Start();
            var held = new Box<ItemDrop.ItemData>();
            yield return Equip(rig, SwordRow, held);
            var weapon = held.Value;
            if (weapon == null)
            {
                c.Check(false, "could not equip SwordIron");
                c.Report();
                yield break;
            }
            var shared = weapon.m_shared.m_attack;
            var dt = Time.fixedDeltaTime;
            var failures = MoveTracker.StartFailures;
            var warnings = MoveTracker.StartWarnings;
            var mark = LogTap.Mark;
            string[] names =
            {
                "(a) ground gone late in the roll, attack pressed during the roll",
                "(b) ground gone as the roll ends, attack pressed at once",
                "(c) ground gone as the roll ends, attack pressed 0.3 s later",
            };
            for (var k = 0; k < 3; k++)
            {
                var what = names[k];
                yield return Settle(rig);
                Look(p, 30f);
                yield return Fixed;
                var caseMark = LogTap.Mark;
                var run = new LedgeRun();
                yield return Ledge(rig, run, k == 0 ? 0.6f : -1f, k, rules.FlowStart);
                var clone = run.Clone;
                var said = LogTap.First(caseMark, Dbg, "Roll attack: ");
                c.Check(LogTap.Count(caseMark, Dbg, "Jump attack") == 0, $"{what}: no \"Jump attack\" Debug line");
                c.Check(run.Started != null && run.StartAir, $"{what}: an attack started in the air (got {LedgeWhat(run)})");
                c.Check(!run.JumpToken && (clone == null || run.Move.Kind == MoveKind.Roll), $"{what}: never a jump attack (no jump was made)");
                if (run.IsMove)
                {
                    c.Check(run.Move.Flow == FlowResult.CrossFade && run.EnteredAt >= 0f && run.EnteredAt - run.StartAt <= 3f * dt + 0.001f,
                        $"{what}: the roll attack flows out of the roll (cross-fade) and is in its animation within three ticks (flow {run.Move.Flow}, {Span(run.StartAt, run.EnteredAt)} s)");
                    c.Check(Near(clone.m_maxYAngle, shared.m_maxYAngle), $"{what}: a roll attack's aim is the weapon's own (no jump attack tilt)");
                    c.Check(said != null && said.Contains(run.Move.Cut ? "; cut into the roll " : " s after the roll ended (") && said.Contains("(cross-fade from the roll, "),
                        $"{what}: Debug line of the roll attack says how it left the roll (cross-fade from the roll) (got \"{said ?? "none"}\")");
                    c.Check(run.EnterAir, $"{what}: the roll attack is in its animation while the player is still in the air");
                    // The hit event must come (first run: it did, in the air or at the landing): a check that only
                    // runs "if it came" could pass having looked at nothing. Level in the air and on the ground alike.
                    var fired = new Box<bool>();
                    yield return WaitFor(() => HitTap.Of(clone).Count > 0, 1.5f, fired);
                    var ev = fired.Value ? HitTap.Of(clone)[0] : null;
                    c.Check(ev != null && Mathf.Abs(ev.Pitch) <= Mathf.Max(0.5f, shared.m_maxYAngle + 0.5f),
                        $"{what}: the swing is level although the player looks 30 degrees down ({(ev == null ? "no hit event within 1.5 s" : $"pitch {F(ev.Pitch)}, {(ev.Air ? "in the air" : "on the ground")}")})");
                }
                else if (run.Started != null)
                {
                    c.Check(Vanilla(run.Started, shared) && said == null, $"{what}: else a normal swing, with no \"Roll attack\" Debug line (got {Fired(run.Started)})");
                }
                if (k == 0 && run.RollingAtFlowStart)
                {
                    c.Check(run.IsMove && run.Move.Cut, $"{what}: the roll animation still played at the cut point, so the attack is a roll attack cut into the roll (got {LedgeWhat(run)})");
                }
                // (b) and (c) must really be the two situations of the item (first run: they were): a press on the tick
                // the roll ends finds the Animator in the roll's blend out, a press 0.3 s later finds it out of it.
                if (k == 1)
                {
                    c.Check(run.InRollAtPress && run.IsMove && !run.Move.Cut,
                        $"{what}: the Animator still blends out of the roll at the press ({run.InRollAtPress}), and the attack is a roll attack flowing out of it (got {LedgeWhat(run)})");
                }
                if (k == 2)
                {
                    c.Check(!run.InRollAtPress && run.Started != null && !run.IsMove,
                        $"{what}: the Animator has left the roll at the press ({!run.InRollAtPress}), and the attack is a normal swing (got {LedgeWhat(run)})");
                }
                SelfTest.Note(LedgeName, $"{what}: {LedgeWhat(run)}; lifted {Span(run.RollStart, run.LiftAt)} s into the roll, the roll (m_inDodge) lasted "
                                         + $"{Span(run.RollStart, run.RollEnd)} s (about 0.88 s on the ground), roll animation {(run.RollingAtFlowStart ? "still" : "no longer")} playing at "
                                         + $"FlowStart, attack {Span(run.RollStart, run.StartAt)} s after the roll started");
                yield return AttackOver(p, run.Started, 3f);
                yield return SoftLand(p, 6f);
                Look(p, 0f);
            }
            c.Check(MoveTracker.StartFailures == failures && MoveTracker.StartWarnings == warnings && LogTap.Count(mark, Wrn, "did not start") == 0,
                "no move was cancelled by the start check, no \"did not start\" warning");
            c.Check(p.GetHealth() >= health - 0.01f, "no fall damage (test set-up)");
            yield return Settle(rig);
            c.Report();
        }
        finally
        {
            if (p != null && p.GetHealth() < health)
            {
                p.SetHealth(health);
            }
            HitTap.Stop();
            rig.Restore();
        }
    }

    // Own small test: the ground goes while the roll is still invulnerable. Whatever the Animator does with a roll in
    // the air, a roll attack must not start while other players' games still see the invulnerability, nor less than
    // the margin after it went off.
    private static IEnumerator RunLedgeIframes()
    {
        var p = LocalPlayer(LedgeIframesName);
        if (p == null)
        {
            yield break;
        }
        var rig = new Rig(LedgeIframesName, p);
        var health = p.GetHealth();
        try
        {
            var rules = Loose();
            ServerRules.TestRules = rules;
            var held = new Box<ItemDrop.ItemData>();
            yield return Equip(rig, SwordRow, held);
            if (held.Value == null)
            {
                SelfTest.Fail(LedgeIframesName, "could not equip SwordIron");
                yield break;
            }
            // Default FlowStart, then FlowStart 0: the earliest a roll attack may cut in, so the margin itself is
            // what holds it back (first run, default only: the roll kept playing in the air and the cut came 0.42 s
            // after the invulnerability, which any margin allows).
            var said = new List<string>();
            string broken = null;
            foreach (var flowStart in new[] { rules.FlowStart, 0f })
            {
                var these = Loose();
                these.FlowStart = flowStart;
                ServerRules.TestRules = these;
                yield return Settle(rig);
                var run = new LedgeRun();
                yield return Ledge(rig, run, 0.15f, 0, flowStart);
                var text = $"FlowStart {F(flowStart)}, lifted {Span(run.RollStart, run.LiftAt)} s into the roll: {LedgeWhat(run)} {Span(run.RollStart, run.StartAt)} s after the roll started; "
                           + $"the roll (m_inDodge) lasted {Span(run.RollStart, run.RollEnd)} s, its invulnerability went off for other players after "
                           + $"{Span(run.RollStart, run.InvulnerableOff)} s";
                yield return AttackOver(p, run.Started, 3f);
                yield return SoftLand(p, 6f);
                yield return WaitIdle(p, 4f);
                if (!run.IsMove || run.Move.Kind != MoveKind.Roll)
                {
                    said.Add("no roll attack started, so nothing can carry the roll's invulnerability (" + text + ")");
                }
                else if (!run.InvulnerableAtStart && run.InvulnerableOff >= 0f
                         && run.StartAt - run.InvulnerableOff >= MoveTracker.IframeMargin - 0.5f * Time.fixedDeltaTime)
                {
                    said.Add($"the roll attack started {Span(run.InvulnerableOff, run.StartAt)} s after the invulnerability went off, at least the {F(MoveTracker.IframeMargin)} s margin ({text})");
                }
                else
                {
                    broken = $"the roll attack started {(run.InvulnerableAtStart ? "while still invulnerable" : Span(run.InvulnerableOff, run.StartAt) + " s after the invulnerability went off")}, "
                             + $"less than the {F(MoveTracker.IframeMargin)} s margin other players' games need ({text})";
                    break;
                }
            }
            if (broken != null)
            {
                SelfTest.Fail(LedgeIframesName, broken);
            }
            else
            {
                SelfTest.Pass(LedgeIframesName, string.Join(" | ", said.ToArray()));
            }
        }
        finally
        {
            if (p != null && p.GetHealth() < health)
            {
                p.SetHealth(health);
            }
            rig.Restore();
        }
    }

    // ---------- moveset.toggle ----------

    private static IEnumerator RunToggle()
    {
        var p = LocalPlayer(ToggleName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(ToggleName);
        var rig = new Rig(ToggleName, p);
        try
        {
            var rules = Loose();
            ServerRules.TestRules = rules;
            var held = new Box<ItemDrop.ItemData>();
            yield return Equip(rig, SwordRow, held);
            var weapon = held.Value;
            if (weapon == null)
            {
                c.Check(false, "could not equip SwordIron");
                c.Report();
                yield break;
            }
            var shared = weapon.m_shared.m_attack;
            c.Check(ActiveNow() && PatchedCount() == 5, $"before: the mod is active with its {PatchedCount()} combat patches on");

            // A jump attack is playing when the mod goes off.
            yield return Settle(rig);
            var run = new JumpRun();
            yield return JumpPress(rig, run);
            c.Check(run.IsMove && run.EnteredAt >= 0f, "a jump attack is playing");
            var clone = run.Clone;
            Plugin.TestBlocker = BlockerText;
            FeatureRegistry.RefreshAll();
            var offAt = Time.fixedTime;
            c.Check(!ActiveNow() && PatchedCount() == 0, $"turned off: the mod is inactive ({StateNow()}: {StatusNow()}) and none of its combat patches is left ({PatchedCount()})");
            c.Check(clone != null && ReferenceEquals(p.m_currentAttack, clone) && !clone.m_attackDone,
                "turned off: the move that was playing is still the current attack");
            c.Check(clone != null && Near(clone.m_maxYAngle, run.Move.MaxYBefore), "turned off: the playing move's aim is back to the weapon's own");
            var over = new Box<bool>();
            yield return WaitFor(() => clone == null || clone.m_attackDone, 3f, over);
            c.Check(over.Value && Time.fixedTime - offAt >= 0.1f, $"the move finishes by itself ({S(Time.fixedTime - offAt)} s after the mod went off)");
            yield return WaitLanded(p, 3f);
            yield return WaitIdle(p, 4f);

            // Off: jump + attack and roll + held attack are vanilla.
            Put(p, rig.Home);
            Face(p, rig.Forward);
            yield return Fixed;
            var mark = LogTap.Mark;
            var off = new JumpRun();
            yield return JumpPress(rig, off);
            c.Check(off.Jumped && !off.TokenAtJump && off.Started != null && off.Clone == null && Vanilla(off.Started, shared),
                $"off: jump + attack is a normal swing (got {Fired(off.Started)})");
            yield return AttackOver(p, off.Started, 3f);
            yield return WaitLanded(p, 3f);
            yield return WaitIdle(p, 4f);
            Put(p, rig.Home);
            Face(p, rig.Forward);
            yield return Fixed;
            var roll = new RollRun();
            rig.TakeController();
            yield return Roll(rig, roll, true, q => Hold(q, false));
            rig.GiveController();
            c.Check(roll.Clone == null && roll.Started != null && !roll.StartedInRoll && roll.GapTicks > 0 && Vanilla(roll.Started, shared),
                $"off: attack held through a roll is a normal swing after the roll and its stand-up (got {Fired(roll.Started)}, idle for {S(roll.Gap)} s)");
            c.Check(LogTap.Count(mark, Dbg, "Jump attack") == 0 && LogTap.Count(mark, Dbg, "Roll attack") == 0, "off: no move Debug line");
            yield return AttackOver(p, roll.Started, 3f);
            yield return WaitIdle(p, 4f);

            // On again: the moves come back at once.
            Plugin.TestBlocker = null;
            FeatureRegistry.RefreshAll();
            c.Check(ActiveNow() && PatchedCount() == 5, $"turned on again: active, {PatchedCount()} combat patches back");
            ServerRules.TestRules = rules; // turning off cleared the test rules
            Put(p, rig.Home);
            Face(p, rig.Forward);
            yield return Fixed;
            var on = new JumpRun();
            yield return JumpPress(rig, on);
            c.Check(on.TokenAtJump && on.IsMove && on.Move.Kind == MoveKind.Jump, $"on again: the very next jump + attack is a jump attack (got {Fired(on.Started)})");
            yield return AttackOver(p, on.Started, 3f);
            yield return Settle(rig);
            var back = new RollRun();
            yield return Roll(rig, back);
            p.m_queuedAttackTimer = 0f;
            c.Check(back.Clone != null && back.Move.Kind == MoveKind.Roll, "on again: roll attacks are back too");
            yield return AttackOver(p, back.Started, 3f);
            yield return Settle(rig);
            c.Report();
        }
        finally
        {
            if (Plugin.TestBlocker != null)
            {
                Plugin.TestBlocker = null;
                FeatureRegistry.RefreshAll();
            }
            rig.Restore();
        }
    }

    // ---------- moveset.log ----------

    // Own small test, last: the mod logged no error since the game started, and no warning but those a self test
    // provokes on purpose (bad animation names in moveset.triggers, the start check in moveset.watchdog).
    private static IEnumerator RunLog()
    {
        yield return null;
        var errors = LogTap.AlertsSince(0, Err);
        var warnings = LogTap.AlertsSince(0, Wrn);
        var odd = warnings.Where(w => w.Text.IndexOf("is not an attack animation of this game", StringComparison.Ordinal) < 0
                                      && w.Text.IndexOf(" did not start for ", StringComparison.Ordinal) < 0).ToList();
        SelfTest.Note(LogName, $"{warnings.Count} warning(s) and {errors.Count} error(s) from {ModInfo.Name} since the game started "
                               + $"({warnings.Count - odd.Count} warning(s) provoked on purpose by self tests)");
        if (!LogTap.Installed)
        {
            SelfTest.Fail(LogName, "the log tap was not installed");
        }
        else if (errors.Count > 0)
        {
            SelfTest.Fail(LogName, $"{errors.Count} error line(s) from the mod, first: {errors[0].Text}");
        }
        else if (odd.Count > 0)
        {
            SelfTest.Fail(LogName, $"{odd.Count} warning(s) no test provokes on purpose, first: {odd[0].Text}");
        }
        else
        {
            SelfTest.Pass(LogName, "no error and no unexpected warning from the mod in this session (jump and roll attacks with every weapon type ran before)");
        }
    }
}
#endif
