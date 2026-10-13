#if DEBUG
using System;
using System.Collections;
using MC.Exploration.SailingSkillMod.Patches;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SailingSkillMod;

// Debug build only. Me = self tests that press real keys (SelfTest.Key: state of the real keyboard, read by ZInput,
// the game and every mod like a person's press). One small test per thing (TESTING.md item in brackets):
//   sailing.input.tab   real Console key opens the console; "raiseskill Sa" in its input box with the caret at the
//                       end, real Tab press: the game's own Tab code completes it to "raiseskill Sailing"; same for
//                       "resetskill Sa" [T01]. The letters themselves are put in the box as text (the harness drives
//                       keys, not typed characters).
//   sailing.input.dive  at sea at Sailing 100 (real skill), swimmer beside the ship: normal map reveal; real Crouch
//                       key held: Swim Dive takes the player under, still the normal reveal down there; key up: back
//                       at the surface, normal reveal; on the deck again: the wider reveal; no warning or error from
//                       either mod [X02]
// Keys are read from this game's bindings, never assumed.
internal static partial class SelfTests
{
    private const string InputTabName = "sailing.input.tab";
    private const string InputDiveName = "sailing.input.dive";

    private const string SwimDiveGuid = "MC.Exploration.Swimming.Dive";
    private const string SwimDiveSource = "Swim Dive";   // its log source (plugin name)

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

    // ---------- sailing.input.tab (T01) ----------

    private static IEnumerator RunInputTab()
    {
        var c = new Checks(InputTabName);
        var console = global::Console.instance;
        if (console == null || console.m_input == null || console.m_chatWindow == null || Player.m_localPlayer == null)
        {
            SelfTest.Fail(InputTabName, "no console or no local player");
            yield break;
        }
        var watch = LogMark();
        var input = console.m_input;
        var window = console.m_chatWindow.gameObject;
        // Console must be allowed like for a player who uses it (launch option -console): in memory only, put back.
        var wasEnabled = global::Console.m_consoleEnabled;
        var wasShown = global::Console.IsVisible();
        var keepText = input.text;
        var inventoryWasShown = InventoryGui.IsVisible();
        try
        {
            global::Console.m_consoleEnabled = true;
            if (wasShown)
            {
                window.SetActive(false);
                yield return null;
            }

            // Real Console key (F5 unless rebound): the console opens.
            var key = BoundKey("Console", out var path);
            c.Check(key != KeyCode.None, $"the Console button is not on a key the test can press ('{path}')");
            var sent = false;
            if (key != KeyCode.None)
            {
                sent = SelfTest.Key(key, true);
                yield return new WaitForSecondsRealtime(0.15f);
                SelfTest.Key(key, false);
                var opened = new Box();
                yield return WaitFor(() => global::Console.IsVisible(), 1.5f, opened);
            }
            c.Check(sent, "no input driver took the key press (the test only works in a probe run)");
            if (!c.Check(global::Console.IsVisible(), $"a real {key} press did not open the console"))
            {
                // Opened the direct way, so the Tab press below is still checked.
                window.SetActive(true);
                input.ActivateInputField();
                yield return null;
            }

            foreach (var command in new[] { "raiseskill", "resetskill" })
            {
                var line = command + " Sa";
                // The typed letters: text in the box, caret behind the last one (where typing leaves it). The box builds
                // its text on the next frame; only then it takes the caret.
                input.text = line;
                yield return null;
                yield return null;
                input.caretPosition = line.Length;
                if (input.caretPosition != line.Length)
                {
                    input.MoveTextEnd(false);
                }
                yield return null;
                var shown = global::Console.IsVisible();
                if (!c.Check(shown && input.text == line && input.caretPosition == line.Length,
                        $"the console's input box does not hold '{line}' with the caret at its end (it holds '{input.text}', caret at {input.caretPosition}, "
                        + $"console shown {shown}): the Tab press was not checked"))
                {
                    continue;
                }
                var tab = SelfTest.Key(KeyCode.Tab, true);
                yield return new WaitForSecondsRealtime(0.12f);
                SelfTest.Key(KeyCode.Tab, false);
                yield return null;
                yield return null;
                c.Check(tab && input.text == command + " Sailing",
                    $"a real Tab press after '{line}' in the console gave '{input.text}', expected '{command} Sailing' (input driver took the key: {tab})");
            }
            c.Check(!InventoryGui.IsVisible() || inventoryWasShown, "the Tab presses in the console opened the inventory");
            CheckQuiet(c, watch);
            c.Report();
        }
        finally
        {
            SelfTest.ReleaseInput();
            try
            {
                input.text = keepText;
                window.SetActive(wasShown);
                global::Console.m_consoleEnabled = wasEnabled;
                if (!inventoryWasShown && InventoryGui.instance != null && InventoryGui.IsVisible())
                {
                    InventoryGui.instance.Hide();
                }
            }
            catch (Exception e)
            {
                Log.Error($"Sailing self test clean-up failed: {e}");
            }
        }
    }

    // ---------- sailing.input.dive (X02) ----------

    private static IEnumerator RunInputDive()
    {
        var c = new Checks(InputDiveName);
        if (!OtherActive(SwimDiveGuid, out var diveState))
        {
            // No pass without a check: a skipped test must not read as proof.
            SelfTest.Fail(InputDiveName, $"Swim Dive is not loaded and active ({diveState}): nothing of TESTING.md X02 was checked");
            yield break;
        }
        var rig = ShipRig.Create(InputDiveName);
        if (rig == null)
        {
            yield break;
        }
        var watch = LogMark();
        // Lines of Swim Dive heard so far (any level): the dive below must add some, else "no warning or error from
        // Swim Dive" would be read from a watcher that does not hear that mod.
        var diveHeard = _watch != null ? _watch.Heard(SwimDiveSource) : 0;
        var sea = new Sea { Rig = rig };
        var started = Time.realtimeSinceStartup;
        var player = rig.Player;
        var seman = player.GetSEMan();
        var wasWet = seman != null && seman.HaveStatusEffect(SEMan.s_statusEffectWet);
        try
        {
            var rules = new SailingRules();
            ServerRules.TestRules = rules;
            var crouch = BoundKey("Crouch", out var crouchPath);
            if (!c.Check(crouch != KeyCode.None, $"Crouch (Swim Dive's dive key) is not on a key the test can press ('{crouchPath}')"))
            {
                c.Report();
                yield break;
            }
            yield return SetUpSea(sea, c, 0f, 0.3f, 90f);
            var map = Minimap.instance;
            if (!sea.Ok || !c.Check(map != null, "no minimap: the map reveal cannot be read"))
            {
                yield return LeaveSea(sea, c);
                c.Report();
                yield break;
            }
            var ship = rig.Boat;
            var controls = ship.m_shipControlls;
            var water = ZoneSystem.instance.m_waterLevel;
            var radius = map.m_exploreRadius;
            var wide = radius * SailMath.Scale(rules.RevealBonusAtMax, 1f);

            // Real skill, no test level: Sailing 100 on the character.
            HelmSkill.TestLocalLevel = null;
            rig.BackupSkills();
            SetSailing(player, 100f, 0f);
            RevealRadius(player, out var atHelm, out _);
            c.Check(wide > radius + 1f && Near(atHelm, wide, 0.01f),
                $"at the helm at Sailing 100: reveal radius {F(atHelm)}, expected the wider {F(wide)} (normal {F(radius)}): the checks below would prove little");

            // Over the side: swimmer 5 m beside the hull, ship lying still.
            rig.ReleaseHelm();
            ResetBoat(ship, ship.transform.position, ship.transform.forward);
            var side = Flat(ship.transform.right).normalized;
            var p = ship.transform.position + side * 5f;
            var level = Floating.GetLiquidLevel(new Vector3(p.x, water - 0.5f, p.z));
            p.y = (level > water - 5f ? level : water) - player.m_swimDepth;
            MovePlayer(player, p);
            float Under() => player.GetLiquidLevel() - player.m_swimDepth - player.transform.position.y;
            float Beside() => ship != null ? Flat(player.transform.position - ship.transform.position).magnitude : -1f;
            var swims = new Box();
            yield return WaitFor(() => player.IsSwimming() && player.GetStandingOnShip() == null && !player.IsAttached(), 4f, swims);
            if (!c.Check(swims.Ok, "the player put in the water 5 m beside the ship does not swim: nothing of the dive was checked"))
            {
                yield return LeaveSea(sea, c);
                c.Report();
                yield break;
            }
            // The splash is over, the swimmer rides the waves at the swimming height.
            yield return new WaitForSeconds(1f);
            var calm = new Box();
            yield return WaitFor(() => player.IsSwimming() && Under() < 0.4f, 8f, calm);
            yield return new WaitForSeconds(0.5f);
            var atRest = Under();
            c.Check(player.IsSwimming() && atRest < 0.6f && Beside() < 12f,
                $"swimming at the surface beside the ship before the dive ({F(atRest)} m under the swimming height, {F(Beside())} m from the ship)");
            c.Check(!MinimapPatches.Aboard(player), "a swimmer beside the ship counts as aboard for the map");
            RevealRadius(player, out var swimming, out _);
            c.Check(Near(swimming, radius, 0.01f), $"swimming beside the ship at Sailing 100: reveal radius {F(swimming)}, expected the normal {F(radius)}");
            c.Check(player.TakeInput(), "the player cannot take input (a window is open): the dive key would not be read");

            // Real Crouch key held: Swim Dive takes the player under. Map reveal read once down there.
            var sent = SelfTest.Key(crouch, true);
            var deepest = atRest;
            var underReveal = -1f;
            var underDepth = 0f;
            var t0 = Time.time;
            while (Time.time - t0 < 4f)
            {
                yield return new WaitForFixedUpdate();
                var depth = Under();
                deepest = Mathf.Max(deepest, depth);
                if (underReveal < 0f && depth > atRest + 1f)
                {
                    RevealRadius(player, out underReveal, out _);
                    underDepth = depth;
                }
            }
            SelfTest.Key(crouch, false);
            c.Check(sent, $"no input driver held {crouch} (the test only works in a probe run)");
            c.Check(deepest > atRest + 1f,
                $"holding the real Crouch key beside the ship dives with Swim Dive ({F(deepest)} m under the swimming height within 4 s, {F(atRest)} m before)");
            c.Check(underReveal > 0f && Near(underReveal, radius, 0.01f),
                $"diving beside the ship at Sailing 100: reveal radius {F(underReveal)} at {F(underDepth)} m under, expected the normal {F(radius)} (-1 = never deep enough to read it)");

            // Key up: back at the surface, still the normal reveal.
            var up = new Box();
            yield return WaitFor(() => player.IsSwimming() && Under() < 0.6f, 15f, up);
            c.Check(up.Ok, $"key released: the diver is not back at the surface after 15 s ({F(Under())} m under the swimming height)");
            RevealRadius(player, out var surfaced, out _);
            c.Check(Near(surfaced, radius, 0.01f) && !MinimapPatches.Aboard(player),
                $"back at the surface beside the ship ({F(Beside())} m): reveal radius {F(surfaced)}, expected the normal {F(radius)}");

            // Climbs aboard: the wider reveal.
            if (c.Check(ship != null && controls != null && controls.m_attachPoint != null, "the ship is gone: climbing aboard not checked"))
            {
                MovePlayer(player, controls.m_attachPoint.position + Vector3.up * 0.6f);
                var back = new Box();
                yield return WaitFor(() => player.GetStandingOnShip() == ship, 4f, back);
                if (c.Check(back.Ok, "the player put back on the deck does not stand on it"))
                {
                    RevealRadius(player, out var climbed, out _);
                    c.Check(Near(climbed, wide, 0.01f), $"back on the deck at Sailing 100: reveal radius {F(climbed)}, expected the wider {F(wide)}");
                }
            }

            // No warning or error from either mod.
            var theirs = Problems(watch, SwimDiveSource);
            c.Check(theirs.Count == 0, "Swim Dive logged warnings or errors while the player dived beside the ship: " + Join(theirs));
            // Swim Dive writes Debug lines when a dive starts and ends ("Dive started ...", "Dive ended: ...").
            var heardNow = _watch != null ? _watch.Heard(SwimDiveSource) : 0;
            c.Check(heardNow > diveHeard,
                $"the log watcher heard no line from the log source '{SwimDiveSource}' during the dive (its Debug lines about the dive were expected): "
                + "'no warning or error from Swim Dive' proves nothing");
            rig.RestoreSkills();
            yield return LeaveSea(sea, c);
            c.Note($"took {F(Time.realtimeSinceStartup - started)} s");
            CheckQuiet(c, watch);
            c.Report();
        }
        finally
        {
            SelfTest.ReleaseInput();
            try
            {
                player.SetCrouch(false);
                if (!wasWet && seman != null)
                {
                    seman.RemoveStatusEffect(SEMan.s_statusEffectWet, true);
                }
            }
            catch (Exception e)
            {
                Log.Error($"Sailing self test clean-up failed: {e}");
            }
            sea.Wind.Restore();
            rig.Restore();
        }
    }
}
#endif
