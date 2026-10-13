#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.WeaponsDualWieldMod;

// Debug build only. Self test that press real keys, the real mouse button and a test gamepad (SelfTest.Key / Pad:
// device state events, the road a real press takes; docs/modding/framework.md, "Real input in tests"). Same class and
// helper as SelfTests.cs. One small test per TESTING.md item, so one can be fixed or dropped alone:
//   dual.input.swap         T04: real H with a pair: one swap (equip action, top-left message with the icon, equip
//                           effect), a second press swap back, H during a real swing (real attack button) do nothing
//   dual.input.keys         T31: SwapHandsKey = J: real J swaps, real H do nothing; None: neither; MainHandKey = None:
//                           real Left Alt held + the real hotbar key pair like a normal equip; control with Left Alt
//                           bound: the weapon alone in the main hand
//   dual.input.pad          T31: SwapHandsKey = JoystickButton0: the warning once, and the real South button of a test
//                           gamepad, real H and real J swap nothing
//   dual.input.swap-attack  T32: real H, then the real attack button held while the swap runs: no swing before the
//                           hands changed, then the first swing start with the new main weapon (time NOTEd); real H,
//                           then one click: NOTE whether a swing follows, never one with the old hands
//   dual.input.swap-walk    T32: real W held (player kept on its spot): real H and a real hotbar equip of the same
//                           kind of weapon slow the player to the same speed
// Keys are bound in memory only (Controls.TestSwapKey / TestMainKey, then CacheKeys: what a changed setting does),
// never in the config file; dual.mp.keys change the real settings (multiplayer run: throwaway config).
// Always SelfTest.ReleaseInput in finally.
internal static partial class SelfTests
{
    private const string InputSwapName = "dual.input.swap";
    private const string InputKeysName = "dual.input.keys";
    private const string InputPadName = "dual.input.pad";
    private const string InputSwapAttackName = "dual.input.swap-attack";
    private const string InputSwapWalkName = "dual.input.swap-walk";

    private const string GamepadWarning =
        "Controls.SwapHandsKey = JoystickButton0: gamepad buttons are not supported yet, so swapping your weapons between "
        + "your hands is off. Pick a keyboard key or a mouse button.";

    // ---------- helpers ----------

    private static IEnumerator SkipFrames(int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return null;
        }
    }

    // Probe's input driver there, and the player can take a key now? Else the check fail and the test stops: a press
    // nobody reads prove nothing.
    private static bool InputReady(Checks c, Player p)
    {
        var driver = SelfTest.ReleaseInput() && ZInput.instance != null;
        c.Check(driver, "no input driver: real key presses need a run of tools/Test-InWorld.ps1 or Test-Multiplayer.ps1");
        var takes = p.TakeInput() && !Hud.InRadial() && !p.InPlaceMode() && !p.IsDead();
        c.Check(takes, "the player cannot take input now (a window or the radial menu is open, or build mode is on): a key "
                       + "press would be ignored");
        return driver && takes;
    }

    // Real tap of a keyboard key or mouse button: down, hold, up, a few frames. seen[0] = ZInput read it as held
    // meanwhile (the press reached the game: a "nothing happened" check after it means something).
    private static IEnumerator TapKey(KeyCode key, bool[] seen, float hold = 0.15f)
    {
        seen[0] = false;
        var tap = SelfTest.KeyTap(key, hold);
        while (tap.MoveNext())
        {
            yield return tap.Current;
            seen[0] |= ZInput.GetKey(key, false);
        }
        yield return SkipFrames(3);
    }

    // Real key down for two frames, then up: a press as short as the game can read (the swap key then leaves time to
    // press something else while the swap still runs). seen[0] = ZInput read it.
    private static IEnumerator QuickKey(KeyCode key, bool[] seen)
    {
        seen[0] = false;
        SelfTest.Key(key, true);
        yield return null;
        seen[0] |= ZInput.GetKey(key, false);
        yield return null;
        seen[0] |= ZInput.GetKey(key, false);
        SelfTest.Key(key, false);
    }

    // Nothing queued, nothing pending, hand as given, for 'seconds' (a swap would be queued within a frame).
    private static IEnumerator NothingSwaps(Player p, ItemDrop.ItemData right, ItemDrop.ItemData left, float seconds, bool[] quiet)
    {
        quiet[0] = true;
        var until = Time.time + seconds;
        while (Time.time < until)
        {
            quiet[0] &= Hands.PendingSwap == null && p.GetActionQueueCount() == 0 && Holds(p, right, left);
            yield return null;
        }
    }

    // View turned up to the sky, so the hotbar key no can land on something usable under the crosshair (vanilla "use
    // item on"). Gives the pitch to put back.
    private static float LookUp(Player p)
    {
        var pitch = p.m_lookPitch;
        p.m_lookPitch = -70f;
        p.UpdateEyeRotation();
        return pitch;
    }

    private static void LookBack(Player p, float pitch)
    {
        p.m_lookPitch = pitch;
        p.UpdateEyeRotation();
    }

    // Something usable under the crosshair now (the hotbar key would use the item on it)? Null = nothing.
    private static string UsableHover(Player p)
    {
        var hover = p.GetHoverObject();
        return hover != null && hover.GetComponentInParent<Interactable>() != null ? hover.name : null;
    }

    private static float FlatSpeed(Player p)
    {
        var v = p.m_currentVel;
        v.y = 0f;
        return v.magnitude;
    }

    // ---------- dual.input.swap (T04) ----------

    private static IEnumerator RunInputSwap()
    {
        var c = new Checks(InputSwapName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(InputSwapName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        SkillSave skills = null;
        var warnings = LogWatch.WarningLines().Length;
        var errors = LogWatch.ErrorLines().Length;
        try
        {
            bench = new Bench(player);
            skills = new SkillSave(player, Skills.SkillType.Swords, Skills.SkillType.Axes);
            if (!InputReady(c, player))
            {
                c.Report();
                yield break;
            }
            var sword = bench.Give(Sword);
            var axe = bench.Give(Axe);
            if (sword == null || axe == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            var byDefault = Plugin.SwapHandsKey != null ? (KeyCode)Plugin.SwapHandsKey.DefaultValue : KeyCode.None;
            c.Check(byDefault == KeyCode.H, $"the default swap key is {byDefault} (TESTING.md T04 says H)");
            Controls.TestForgetWarned();
            Controls.TestMainKey = KeyCode.LeftAlt;
            Controls.TestSwapKey = KeyCode.H;
            Controls.CacheKeys();
            bench.Pair(sword, axe);
            yield return WaitIdle(player);
            yield return WaitMinor(player);
            c.Check(Holds(player, sword, axe) && Controls.SwapKey == KeyCode.H, $"pair in hand, swap key H ({HandsText(player)})");
            c.Check(!Hud.IsUserHidden() && MessageHud.instance != null, "the HUD shows messages (not hidden)");
            var seen = new bool[1];

            // T04: one real press of H: the swap is queued as the game's equip action, then the hands change.
            var watch = new TopLeftWatch();
            var effectFrame = player.m_lastEquipEffectFrame;
            var started = Time.time;
            SelfTest.Key(KeyCode.H, true);
            string actionText = null;
            var queued = false;
            var until = Time.time + 3f;
            var released = false;
            while (Time.time < until && !Holds(player, axe, sword))
            {
                seen[0] |= ZInput.GetKey(KeyCode.H, false);
                queued |= ReferenceEquals(Hands.PendingSwap, axe);
                player.GetActionProgress(out var text, out _);
                if (text != null)
                {
                    actionText = text;
                }
                if (!released && Time.time - started > 0.1f)
                {
                    released = true;
                    SelfTest.Key(KeyCode.H, false);
                }
                yield return null;
            }
            SelfTest.Key(KeyCode.H, false);
            var took = Time.time - started;
            yield return SkipFrames(2);
            c.Check(seen[0], "the game read the real H key press (input driver)");
            c.Check(queued && Holds(player, axe, sword) && Hands.IsMarked(sword) && !Hands.IsMarked(axe),
                $"a real H key press swaps the hands: axe main, sword off (queued as an equip {queued}, after {F2(took)} s; "
                + $"{HandsText(player)})");
            c.Check(actionText == "$hud_equipping " + axe.m_shared.m_name,
                $"while the swap runs the game's action is 'Equipping <axe>' (text '{actionText}')");
            var mainText = "Main hand: " + Localization.instance.Localize(axe.m_shared.m_name);
            c.Check(watch.Saw(mainText, axe.GetIcon()),
                $"top-left message '{mainText}' with the axe's icon (messages since the key: {watch.NewTexts()})");
            c.Check(player.m_lastEquipEffectFrame != effectFrame, "the swap triggered the game's equip effect (the equip sound)");
            c.Check(ReferenceEquals(player.GetCurrentBlocker(), sword), "after the swap the sword (off hand) is the blocker");
            Invariant(c, player, "real swap key");

            // One press = one swap: nothing more follows.
            var quiet = new bool[1];
            yield return WaitSwap(player);
            yield return NothingSwaps(player, axe, sword, 1f, quiet);
            c.Check(quiet[0], $"one press is one swap: nothing else is queued in the second after it ({HandsText(player)})");

            // A second real press swap back.
            yield return TapKey(KeyCode.H, seen);
            yield return WaitSwap(player);
            c.Check(seen[0] && Holds(player, sword, axe) && Hands.IsMarked(axe) && !Hands.IsMarked(sword),
                $"a second real H key press swaps back: sword main, axe off (key read {seen[0]}; {HandsText(player)})");

            // T04: H during a swing started with the real attack button do nothing.
            yield return WaitIdle(player);
            yield return WaitMinor(player);
            yield return new WaitForSeconds(0.4f);
            SelfTest.Key(KeyCode.Mouse0, true);
            var attackRead = false;
            until = Time.time + 2f;
            while (Time.time < until && !player.InAttack())
            {
                attackRead |= ZInput.GetButton("Attack");
                yield return null;
            }
            attackRead |= ZInput.GetButton("Attack");
            SelfTest.Key(KeyCode.Mouse0, false);
            var inAttack = player.InAttack();
            c.Check(attackRead, "the real left mouse button is the game's Attack button here (default binding): the game read it");
            yield return QuickKey(KeyCode.H, seen);
            var stillAttacking = player.InAttack();
            yield return SkipFrames(2);
            c.Check(inAttack && seen[0] && Hands.PendingSwap == null && !player.IsEquipActionQueued(axe) && Holds(player, sword, axe),
                $"real H during a swing started with the real attack button: nothing (swing running at the press {inAttack}, "
                + $"still running after it {stillAttacking}, key read {seen[0]}; {HandsText(player)})");
            yield return WaitIdle(player);
            c.Check(Holds(player, sword, axe), $"after that swing: the same hands ({HandsText(player)})");
            Invariant(c, player, "key during a real swing");
            var problems = LogProblemsSince(warnings, errors);
            c.Check(problems.Length == 0, $"no warning or error from {ModInfo.Name} meanwhile ({problems.Length}: {string.Join(" | ", problems)})");
            c.Report();
        }
        finally
        {
            SelfTest.ReleaseInput();
            if (bench != null)
            {
                bench.TakeBack();
            }
            if (skills != null)
            {
                skills.Restore();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
        }
    }

    // ---------- dual.input.keys (T31) ----------

    private static IEnumerator RunInputKeys()
    {
        var c = new Checks(InputKeysName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(InputKeysName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        var pitch = player.m_lookPitch;
        var warnings = LogWatch.WarningLines().Length;
        var errors = LogWatch.ErrorLines().Length;
        try
        {
            bench = new Bench(player);
            if (!InputReady(c, player))
            {
                c.Report();
                yield break;
            }
            var sword = bench.Give(Sword);
            var axe = bench.Give(Axe);
            var mace = bench.Give(Mace);
            if (sword == null || axe == null || mace == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            var mainDefault = Plugin.MainHandKey != null ? (KeyCode)Plugin.MainHandKey.DefaultValue : KeyCode.None;
            var swapDefault = Plugin.SwapHandsKey != null ? (KeyCode)Plugin.SwapHandsKey.DefaultValue : KeyCode.None;
            c.Check(mainDefault == KeyCode.LeftAlt && swapDefault == KeyCode.H,
                $"default keys: main hand {mainDefault}, swap {swapDefault} (TESTING.md T31 puts both back to LeftAlt and H)");
            var seen = new bool[1];
            var quiet = new bool[1];

            // SwapHandsKey = J: real H do nothing, real J swaps.
            Controls.TestForgetWarned();
            Controls.TestMainKey = KeyCode.LeftAlt;
            Controls.TestSwapKey = KeyCode.J;
            Controls.CacheKeys();
            bench.Pair(sword, axe);
            yield return WaitIdle(player);
            yield return WaitMinor(player);
            c.Check(Controls.SwapKey == KeyCode.J && Holds(player, sword, axe), $"SwapHandsKey = J in force, pair in hand ({HandsText(player)})");
            yield return TapKey(KeyCode.H, seen);
            yield return NothingSwaps(player, sword, axe, 0.8f, quiet);
            c.Check(seen[0] && quiet[0], $"SwapHandsKey = J: a real H key press does nothing (key read {seen[0]}; {HandsText(player)})");
            yield return TapKey(KeyCode.J, seen);
            yield return WaitSwap(player);
            c.Check(seen[0] && Holds(player, axe, sword), $"SwapHandsKey = J: a real J key press swaps the hands (key read {seen[0]}; {HandsText(player)})");

            // SwapHandsKey = None: no key swaps.
            Controls.TestSwapKey = KeyCode.None;
            Controls.CacheKeys();
            yield return TapKey(KeyCode.J, seen);
            var jRead = seen[0];
            yield return NothingSwaps(player, axe, sword, 0.8f, quiet);
            var jQuiet = quiet[0];
            yield return TapKey(KeyCode.H, seen);
            yield return NothingSwaps(player, axe, sword, 0.8f, quiet);
            c.Check(Controls.SwapKey == KeyCode.None && jRead && seen[0] && jQuiet && quiet[0],
                $"SwapHandsKey = None: real J and real H swap nothing (J read {jRead}, H read {seen[0]}; {HandsText(player)})");

            // The hotbar key of the mace: its slot in the top row, the view turned up so the key no can land on
            // something usable under the crosshair.
            var slot = PutOnHotbar(bench.Inventory, mace);
            var hotkey = KeyCode.Alpha1 + slot;
            c.Check(slot >= 0 && slot < 8, $"a free hotbar slot for the mace (slot {slot + 1})");
            if (slot < 0 || slot >= 8)
            {
                c.Report();
                yield break;
            }
            LookUp(player);
            yield return SkipFrames(3);
            var usable = UsableHover(player);
            c.Check(usable == null, $"nothing usable under the crosshair with the view turned up (it is on {usable}): the hotbar "
                                    + "key would use the mace on it");
            if (usable != null)
            {
                c.Report();
                yield break;
            }

            // MainHandKey = None: Left Alt held + the hotbar key pair like a normal equip. The main-hand key is read
            // from the real keyboard here (not forced released).
            Controls.TestMainHandHeld = null;
            Controls.TestSwapKey = KeyCode.H;
            Controls.TestMainKey = KeyCode.None;
            Controls.CacheKeys();
            bench.Empty();
            Hands.SetMarked(sword, false);
            Hands.SetMarked(mace, false);
            player.EquipItem(sword);
            yield return WaitIdle(player);
            yield return WaitMinor(player);
            SelfTest.Key(KeyCode.LeftAlt, true);
            yield return SkipFrames(2);
            var altRead = ZInput.GetKey(KeyCode.LeftAlt, false);
            yield return TapKey(hotkey, seen);
            yield return WaitEquipped(player, mace);
            SelfTest.Key(KeyCode.LeftAlt, false);
            yield return SkipFrames(2);
            c.Check(altRead && seen[0], $"the game read the real Left Alt and the real hotbar key {slot + 1} (Left Alt {altRead}, hotbar key {seen[0]})");
            c.Check(Controls.MainKey == KeyCode.None && Holds(player, sword, mace) && Hands.IsMarked(mace),
                $"MainHandKey = None: real Left Alt + hotbar key pairs like a normal equip, the mace in the off hand ({HandsText(player)})");
            Invariant(c, player, "Left Alt unbound");

            // Control, MainHandKey = LeftAlt: the same two real keys put the mace alone in the main hand.
            Controls.TestMainKey = KeyCode.LeftAlt;
            Controls.CacheKeys();
            bench.Empty();
            Hands.SetMarked(sword, false);
            Hands.SetMarked(mace, false);
            player.EquipItem(sword);
            yield return WaitIdle(player);
            yield return WaitMinor(player);
            yield return new WaitForSeconds(0.4f);
            SelfTest.Key(KeyCode.LeftAlt, true);
            yield return SkipFrames(2);
            altRead = ZInput.GetKey(KeyCode.LeftAlt, false);
            yield return TapKey(hotkey, seen);
            yield return WaitEquipped(player, mace);
            SelfTest.Key(KeyCode.LeftAlt, false);
            yield return SkipFrames(2);
            c.Check(altRead && seen[0] && Controls.MainKey == KeyCode.LeftAlt && Holds(player, mace, null) && !sword.m_equipped,
                $"control, MainHandKey = LeftAlt: real Left Alt + hotbar key puts the mace alone in the main hand (Left Alt read "
                + $"{altRead}, hotbar key read {seen[0]}; {HandsText(player)})");
            Invariant(c, player, "Left Alt bound");
            var problems = LogProblemsSince(warnings, errors);
            c.Check(problems.Length == 0, $"no warning or error from {ModInfo.Name} meanwhile ({problems.Length}: {string.Join(" | ", problems)})");
            c.Report();
        }
        finally
        {
            SelfTest.ReleaseInput();
            LookBack(player, pitch);
            if (bench != null)
            {
                bench.TakeBack();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
        }
    }

    // ---------- dual.input.pad (T31, gamepad button) ----------

    private static IEnumerator RunInputPad()
    {
        var c = new Checks(InputPadName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(InputPadName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        SkillSave skills = null;
        var spot = player.transform.position;
        var facing = player.transform.rotation;
        var pitch = player.m_lookPitch;
        try
        {
            bench = new Bench(player);
            skills = new SkillSave(player, Skills.SkillType.Jump);
            if (!InputReady(c, player))
            {
                c.Report();
                yield break;
            }
            var sword = bench.Give(Sword);
            var axe = bench.Give(Axe);
            if (sword == null || axe == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            bench.Pair(sword, axe);
            yield return WaitIdle(player);
            yield return WaitMinor(player);

            // SwapHandsKey = JoystickButton0: one warning, the key is off.
            Controls.TestForgetWarned();
            var before = Controls.WarningCount;
            Controls.TestMainKey = KeyCode.LeftAlt;
            Controls.TestSwapKey = KeyCode.JoystickButton0;
            Controls.CacheKeys();
            var text = Controls.LastWarning;
            Controls.CacheKeys();
            c.Check(Controls.SwapKey == KeyCode.None && Controls.WarningCount == before + 1 && text == GamepadWarning,
                $"SwapHandsKey = JoystickButton0: the warning once ({Controls.WarningCount - before} logged, '{text}')");
            c.Check(LogWatch.Saw(GamepadWarning), "that warning is in the log");

            // The real button 0 of a gamepad (South: A on an Xbox pad): the game read the pad, nothing swaps. The
            // button is the game's own Use or Jump, by controller layout: the view is turned up so Use find nothing,
            // and a jump just lands.
            LookUp(player);
            yield return SkipFrames(3);
            var usable = UsableHover(player);
            c.Check(usable == null, $"nothing usable under the crosshair with the view turned up (it is on {usable}): the gamepad's "
                                    + "Use button would use it");
            if (usable != null)
            {
                c.Report();
                yield break;
            }
            var quiet = new bool[1];
            var padRead = false;
            for (var attempt = 0; attempt < 3 && !padRead; attempt++)
            {
                SelfTest.Pad("South", 1f);
                var until = Time.realtimeSinceStartup + 0.7f;
                while (Time.realtimeSinceStartup < until && !padRead)
                {
                    yield return null;
                    padRead = ZInput.IsGamepadActive();
                }
                yield return SkipFrames(2);
                SelfTest.Pad("South", 0f);
                yield return SkipFrames(4);
            }
            c.Check(padRead, "the game read the test gamepad's South button (it switched to gamepad input)");
            yield return NothingSwaps(player, sword, axe, 1f, quiet);
            c.Check(quiet[0], $"SwapHandsKey = JoystickButton0: the real gamepad button 0 swaps nothing ({HandsText(player)})");
            SelfTest.ReleaseInput();
            var land = Time.time + 4f;
            while (Time.time < land && (!player.IsOnGround() || ZInput.IsGamepadActive()))
            {
                yield return null;
            }
            c.Check(!ZInput.IsGamepadActive(), "the game is back on keyboard and mouse after the test gamepad is removed");

            // No keyboard key swaps either.
            var seen = new bool[1];
            yield return TapKey(KeyCode.H, seen);
            var hRead = seen[0];
            yield return NothingSwaps(player, sword, axe, 0.8f, quiet);
            var hQuiet = quiet[0];
            yield return TapKey(KeyCode.J, seen);
            yield return NothingSwaps(player, sword, axe, 0.8f, quiet);
            c.Check(hRead && seen[0] && hQuiet && quiet[0],
                $"SwapHandsKey = JoystickButton0: real H and real J swap nothing (H read {hRead}, J read {seen[0]}; {HandsText(player)})");

            // Both back (LeftAlt, H): the real H swap again.
            Controls.TestSwapKey = KeyCode.H;
            Controls.CacheKeys();
            yield return TapKey(KeyCode.H, seen);
            yield return WaitSwap(player);
            c.Check(seen[0] && Controls.SwapKey == KeyCode.H && Holds(player, axe, sword),
                $"SwapHandsKey back on H: the real H key swaps again ({HandsText(player)})");
            Invariant(c, player, "gamepad button as swap key");
            c.Report();
        }
        finally
        {
            SelfTest.ReleaseInput();
            LookBack(player, pitch);
            if (bench != null)
            {
                bench.TakeBack();
            }
            if (skills != null)
            {
                skills.Restore();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
            PutBack(player, spot, facing);
        }
    }

    // ---------- dual.input.swap-attack (T32, attack button during the swap) ----------

    // Real swap key, then the real attack button (held, or one short click) while the swap runs. What happened go
    // into the fields below (iterators have no out parameters).
    private static bool _saKeyRead;
    private static bool _saAttackRead;
    private static bool _saQueued;
    private static bool _saEarlyAttack;
    private static float _saSwappedAt;
    private static float _saSwingAt;
    private static string _saEarlySwings;
    private static string _saFirstSwing;

    private static IEnumerator SwapThenAttack(Player p, ItemDrop.ItemData main, ItemDrop.ItemData off, bool hold, float watch)
    {
        _saKeyRead = false;
        _saAttackRead = false;
        _saQueued = false;
        _saEarlyAttack = false;
        _saSwappedAt = -1f;
        _saSwingAt = -1f;
        _saEarlySwings = "";
        _saFirstSwing = "none";
        DualSwing.ResetRecords();
        DualSwing.Recording = true;
        var seen = new bool[1];
        yield return QuickKey(KeyCode.H, seen);
        _saKeyRead = seen[0];
        _saQueued = ReferenceEquals(Hands.PendingSwap, off) || Holds(p, off, main);
        SelfTest.Key(KeyCode.Mouse0, true);
        var pressed = Time.time;
        var down = true;
        var until = Time.time + watch;
        while (Time.time < until)
        {
            yield return null;
            if (down)
            {
                _saAttackRead |= ZInput.GetButton("Attack");
            }
            if (down && !hold && Time.time - pressed >= 0.12f)
            {
                down = false;
                SelfTest.Key(KeyCode.Mouse0, false);
            }
            if (_saSwappedAt < 0f)
            {
                if (Holds(p, off, main))
                {
                    _saSwappedAt = Time.time;
                }
                else
                {
                    // No swing may run before the hands changed.
                    _saEarlyAttack |= p.InAttack() || (p.m_currentAttack != null && !p.m_currentAttack.IsDone());
                }
            }
            if (_saSwingAt < 0f && DualSwing.Swings.Count > 0)
            {
                _saSwingAt = DualSwing.Swings[0].Time;
                _saFirstSwing = $"{DualSwing.Swings[0].Trigger} with {Name(DualSwing.Swings[0].Main)} main";
                if (hold)
                {
                    // One swing is enough: the button go up, the swing run out.
                    down = false;
                    SelfTest.Key(KeyCode.Mouse0, false);
                }
            }
            if (_saSwingAt >= 0f && !p.InAttack() && Time.time - _saSwingAt > 0.5f)
            {
                break;
            }
        }
        SelfTest.Key(KeyCode.Mouse0, false);
        yield return WaitIdle(p);
        // A swing that started with the old main weapon = a swing that no wait for the swap.
        _saEarlySwings = string.Join("; ", DualSwing.Swings.Where(s => !ReferenceEquals(s.Main, off))
            .Select(s => $"{s.Trigger} with {Name(s.Main)} main").ToArray());
    }

    private static IEnumerator RunInputSwapAttack()
    {
        var c = new Checks(InputSwapAttackName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(InputSwapAttackName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        SkillSave skills = null;
        try
        {
            bench = new Bench(player);
            skills = new SkillSave(player, Skills.SkillType.Swords, Skills.SkillType.Axes);
            if (!InputReady(c, player))
            {
                c.Report();
                yield break;
            }
            var sword = bench.Give(Sword);
            var axe = bench.Give(Axe);
            if (sword == null || axe == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            Controls.TestForgetWarned();
            Controls.TestMainKey = KeyCode.LeftAlt;
            Controls.TestSwapKey = KeyCode.H;
            Controls.CacheKeys();

            // T32: H, then the attack button held: no swing until the hands changed, then the swing start with the axe.
            bench.Pair(sword, axe);
            yield return WaitIdle(player);
            yield return WaitMinor(player);
            yield return WaitCanAttack(player);
            c.Check(Holds(player, sword, axe) && _attackGate == null,
                $"pair in hand and the player can attack ({HandsText(player)}; {_attackGate ?? "nothing in the way"})");
            yield return SwapThenAttack(player, sword, axe, true, 5f);
            c.Check(_saKeyRead && _saAttackRead, $"the game read the real H key and the real attack button (H {_saKeyRead}, attack {_saAttackRead})");
            c.Check(_saQueued && _saSwappedAt >= 0f && Holds(player, axe, sword),
                $"H, then attack held: the swap still happens (queued {_saQueued}; {HandsText(player)})");
            c.Check(!_saEarlyAttack && _saEarlySwings.Length == 0,
                $"attack held during the swap: no swing before the hands changed (attack seen before the swap {_saEarlyAttack}; "
                + $"swings with the old hands: {(_saEarlySwings.Length == 0 ? "none" : _saEarlySwings)})");
            // Swing times are physics-step times, the swap is seen at a frame's time: a step of slack.
            c.Check(_saSwingAt >= 0f && _saSwappedAt >= 0f && _saSwingAt >= _saSwappedAt - 0.05f && DualSwing.Swings.Count > 0
                    && ReferenceEquals(DualSwing.Swings[0].Main, axe) && DualSwing.Swings[0].Trigger == "dualaxes0",
                $"attack held during the swap: once the hands changed the first swing of the combo starts with the axe (first "
                + $"swing: {_saFirstSwing})");
            if (_saSwingAt >= 0f && _saSwappedAt >= 0f)
            {
                SelfTest.Note(InputSwapAttackName, $"attack held: the swing started {F2(_saSwingAt - _saSwappedAt)} s after the hands changed");
            }
            Invariant(c, player, "attack held during the swap");

            // T32: H, then one click of the attack button while the swap runs: a swing may follow or not (NOTE), but
            // never one with the old hands.
            bench.Pair(sword, axe);
            yield return WaitIdle(player);
            yield return WaitMinor(player);
            yield return new WaitForSeconds(0.5f);
            yield return WaitCanAttack(player);
            yield return SwapThenAttack(player, sword, axe, false, 3f);
            c.Check(_saKeyRead && _saAttackRead, $"one click: the game read the real H key and the real attack button (H {_saKeyRead}, attack {_saAttackRead})");
            c.Check(_saQueued && _saSwappedAt >= 0f && Holds(player, axe, sword),
                $"H, then one attack click: the swap still happens (queued {_saQueued}; {HandsText(player)})");
            c.Check(!_saEarlyAttack && _saEarlySwings.Length == 0,
                $"one attack click during the swap: no swing before the hands changed (attack seen before the swap {_saEarlyAttack}; "
                + $"swings with the old hands: {(_saEarlySwings.Length == 0 ? "none" : _saEarlySwings)})");
            SelfTest.Note(InputSwapAttackName, _saSwingAt >= 0f && _saSwappedAt >= 0f
                ? $"one click during the swap: a swing started {F2(_saSwingAt - _saSwappedAt)} s after the hands changed ({_saFirstSwing})"
                : "one click during the swap: no swing followed");
            Invariant(c, player, "one click during the swap");
            c.Report();
        }
        finally
        {
            SelfTest.ReleaseInput();
            DualSwing.Recording = false;
            DualSwing.ResetRecords();
            if (bench != null)
            {
                bench.TakeBack();
            }
            if (skills != null)
            {
                skills.Restore();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
        }
    }

    // ---------- dual.input.swap-walk (T32, speed while walking) ----------

    // Lowest speed and time under three quarters of 'full' while 'running' say the action run and for 'after'
    // seconds more; the player kept on its spot. Results in the fields below.
    private static float _walkLowest;
    private static float _walkSlowTime;

    private static IEnumerator SampleSlowdown(Player p, Vector3 spot, float full, Func<bool> running, float after)
    {
        _walkLowest = float.MaxValue;
        _walkSlowTime = 0f;
        var limit = Time.time + 4f;
        var end = -1f;
        while (Time.time < limit)
        {
            yield return null;
            Pin(p, spot);
            var speed = FlatSpeed(p);
            _walkLowest = Mathf.Min(_walkLowest, speed);
            if (speed < full * 0.75f)
            {
                _walkSlowTime += Time.deltaTime;
            }
            if (end < 0f && !running())
            {
                end = Time.time + after;
            }
            if (end >= 0f && Time.time >= end)
            {
                break;
            }
        }
    }

    private static IEnumerator RunInputSwapWalk()
    {
        var c = new Checks(InputSwapWalkName);
        var player = Player.m_localPlayer;
        if (player == null)
        {
            SelfTest.Fail(InputSwapWalkName, "no local player");
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        var spot = player.transform.position;
        var facing = player.transform.rotation;
        var pitch = player.m_lookPitch;
        var walkBefore = player.GetWalk();
        try
        {
            bench = new Bench(player);
            if (!InputReady(c, player))
            {
                c.Report();
                yield break;
            }
            var sword = bench.Give(Sword);
            var axe = bench.Give(Axe);
            var axe2 = bench.Give(Axe);
            if (sword == null || axe == null || axe2 == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            Controls.TestForgetWarned();
            Controls.TestMainKey = KeyCode.LeftAlt;
            Controls.TestSwapKey = KeyCode.H;
            Controls.CacheKeys();
            var slot = PutOnHotbar(bench.Inventory, axe2);
            var hotkey = KeyCode.Alpha1 + slot;
            c.Check(slot >= 0 && slot < 8, $"a free hotbar slot for the second axe (slot {slot + 1})");
            if (slot < 0 || slot >= 8)
            {
                c.Report();
                yield break;
            }
            LookUp(player);
            yield return SkipFrames(3);
            var usable = UsableHover(player);
            c.Check(usable == null, $"nothing usable under the crosshair with the view turned up (it is on {usable}): the hotbar "
                                    + "key would use the axe on it");
            if (usable != null)
            {
                c.Report();
                yield break;
            }
            player.SetWalk(false);
            bench.Pair(sword, axe);
            yield return WaitIdle(player);
            yield return WaitMinor(player);

            // Real W held: the player moves forward (kept on its spot), at a steady speed after a second.
            SelfTest.Key(KeyCode.W, true);
            var forwardRead = false;
            var sum = 0f;
            var samples = 0;
            var until = Time.time + 1.2f;
            while (Time.time < until)
            {
                yield return null;
                Pin(player, spot);
                forwardRead |= ZInput.GetButton("Forward");
                if (until - Time.time < 0.3f)
                {
                    sum += FlatSpeed(player);
                    samples++;
                }
            }
            var full = samples > 0 ? sum / samples : 0f;
            c.Check(forwardRead && full > 1f,
                $"the real W key is the game's Forward button here and moves the player (button read {forwardRead}, speed {F2(full)} m/s)");
            if (!forwardRead || full <= 1f)
            {
                c.Report();
                yield break;
            }

            // The swap while moving.
            var seen = new bool[1];
            yield return QuickKey(KeyCode.H, seen);
            var queued = ReferenceEquals(Hands.PendingSwap, axe) || Holds(player, axe, sword);
            yield return SampleSlowdown(player, spot, full, () => Hands.PendingSwap != null, 0.8f);
            var swapLowest = _walkLowest;
            var swapSlow = _walkSlowTime;
            c.Check(seen[0] && queued && Holds(player, axe, sword),
                $"real H while moving: the hands swap (key read {seen[0]}, queued {queued}; {HandsText(player)})");

            // Steady again, then a hotbar equip of the same kind of weapon (the second axe: same equip time).
            until = Time.time + 1.2f;
            while (Time.time < until)
            {
                yield return null;
                Pin(player, spot);
            }
            yield return QuickKey(hotkey, seen);
            yield return SkipFrames(1);
            var equipQueued = player.IsEquipActionQueued(axe2) || player.IsItemEquiped(axe2);
            yield return SampleSlowdown(player, spot, full, () => player.IsEquipActionQueued(axe2), 0.8f);
            var equipLowest = _walkLowest;
            var equipSlow = _walkSlowTime;
            c.Check(seen[0] && equipQueued && player.IsItemEquiped(axe2),
                $"real hotbar key {slot + 1} while moving: the second axe is equipped (key read {seen[0]}, queued {equipQueued}; "
                + $"{HandsText(player)})");
            SelfTest.Key(KeyCode.W, false);
            yield return SkipFrames(3);

            // T32: the speed during the swap is what a hotbar equip gives.
            var tolerance = Mathf.Max(0.4f, full * 0.25f);
            c.Check(Mathf.Abs(swapLowest - equipLowest) <= tolerance,
                $"moving at {F2(full)} m/s, the swap slows the player to {F2(swapLowest)} m/s and a hotbar equip of an axe to "
                + $"{F2(equipLowest)} m/s (the same within {F2(tolerance)} m/s)");
            SelfTest.Note(InputSwapWalkName, $"time under three quarters of the full speed: swap {F2(swapSlow)} s, hotbar equip "
                                             + $"{F2(equipSlow)} s (the swap also plays the draw animation after its equip time)");
            Invariant(c, player, "swap while moving");
            c.Report();
        }
        finally
        {
            SelfTest.ReleaseInput();
            LookBack(player, pitch);
            player.SetWalk(walkBefore);
            if (bench != null)
            {
                bench.TakeBack();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
            PutBack(player, spot, facing);
        }
    }
}
#endif
