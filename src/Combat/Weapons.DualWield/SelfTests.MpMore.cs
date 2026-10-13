#if DEBUG
using System;
using System.Collections;
using System.Linq;
using BepInEx.Bootstrap;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.WeaponsDualWieldMod;

// Debug build only. More multiplayer self tests (tools/Test-Multiplayer.ps1, scenario modded: this game is ONE client
// joined to a real dedicated server, fresh throwaway config on both sides). Same class and helper as SelfTests.Mp.cs.
//   dual.mp.keys           T31 with the real settings (a multiplayer run may write them): SwapHandsKey = J, None,
//                          JoystickButton0 and back to H, MainHandKey = None and back to LeftAlt, each followed by the
//                          real keys (J, H, Left Alt + the hotbar key)
//   dual.mp.version-panel  M03 / M04, the player's side: the server say it have another network version: the mod's
//                          status reads "... no can talk to each other", no pair can be made, no server rules are used
//                          and nothing is warned; the server's real answer again: active, pair and rule back
//   dual.mp.allow-back     M04: a still-connected player whose copy says another network version, let in with
//                          AllowPlayersWithoutMod = true; the server switch back to false: about a second later it
//                          refuse that player (its own check, its "Refused ..." line checked on the server) and this
//                          game is back at the main menu with "Incompatible version". Runs last: the player is out after
//                          it (the probe join again before the next test).
// Server halves: dual.mp.server-claim (the mod's hello answer sent again, with another or the real network version),
// dual.mp.server-refuse (starts a watcher on the server that switch the setting back and check its own refusal;
// its PASS / FAIL lines are in the server's log, where the script read them).
internal static partial class SelfTests
{
    private const string MpKeysName = "dual.mp.keys";
    private const string MpVersionPanelName = "dual.mp.version-panel";
    private const string MpAllowBackName = "dual.mp.allow-back";

    private const string ServerClaimStep = "dual.mp.server-claim";
    private const string ServerRefuseStep = "dual.mp.server-refuse";

    // Version text the server claim in dual.mp.version-panel.
    private const string ClaimedVersion = "9.9.9";

    // OnActivated (RegisterAuto), after every other multiplayer test of the mod. The one that get the player refused last.
    private static void RegisterMoreMultiplayerTests()
    {
        SelfTest.RegisterMultiplayer(MpKeysName, SelfTest.Modded, RunMpKeys);
        SelfTest.RegisterMultiplayer(MpVersionPanelName, SelfTest.Modded, RunMpVersionPanel);
        SelfTest.RegisterMultiplayer(MpAllowBackName, SelfTest.Modded, RunMpAllowBack);
        SelfTest.RegisterServerStep(ServerClaimStep, ServerClaim);
        SelfTest.RegisterServerStep(ServerRefuseStep, ServerRefuse);
    }

    // OnDeactivated (UnregisterAuto).
    private static void UnregisterMoreMultiplayerTests()
    {
        SelfTest.UnregisterMultiplayer(MpKeysName);
        SelfTest.UnregisterMultiplayer(MpVersionPanelName);
        SelfTest.UnregisterMultiplayer(MpAllowBackName);
        SelfTest.UnregisterServerStep(ServerClaimStep);
        SelfTest.UnregisterServerStep(ServerRefuseStep);
    }

    // Server step started without waiting for its answer (clean-up in a finally block, where a test no can wait).
    private static void FireServerStep(string step, string arg)
    {
        var plugin = Chainloader.PluginInfos.TryGetValue(ModInfo.Guid, out var info) ? info.Instance as Plugin : null;
        if (plugin != null)
        {
            plugin.StartCoroutine(SelfTest.CallServer(step, arg, new SelfTest.ServerReply()));
        }
    }

    // ---------- server halves ----------

    // The mod's answer to a player's hello (framework NetworkGate: "<guid>.HelloAck", "1|on|<network version>|<version>"),
    // sent again to the one player: arg "other" = with another network version (what a server with a newer build says),
    // "real" = the real one.
    private static IEnumerator ServerClaim(string arg, object[] reply)
    {
        yield return null;
        var peer = OnlyPeer();
        if (peer == null || peer.m_rpc == null)
        {
            SelfTest.Answer(reply, false, "the server has no connected player");
            yield break;
        }
        var payload = arg == "other"
            ? $"1|on|{ModInfo.NetworkVersion + 1}|{ClaimedVersion}"
            : $"1|on|{ModInfo.NetworkVersion}|{ModInfo.Version}";
        peer.m_rpc.Invoke(ModInfo.Guid + ".HelloAck", payload);
        yield return null;
        SelfTest.Answer(reply, true, "sent=" + payload);
    }

    // Starts the watcher below on the server and answer at once: the player it is about is refused in a moment, an
    // answer after that would never reach it. Arg = the reason the refusal must name.
    private static IEnumerator ServerRefuse(string arg, object[] reply)
    {
        yield return null;
        var plugin = Chainloader.PluginInfos.TryGetValue(ModInfo.Guid, out var info) ? info.Instance as Plugin : null;
        var peer = OnlyPeer();
        if (plugin == null || peer == null)
        {
            SelfTest.Answer(reply, false, plugin == null ? "the mod's plugin is not on the server" : "the server has no connected player");
            yield break;
        }
        if (!Plugin.AllowPlayersWithoutMod.Value)
        {
            SelfTest.Answer(reply, false, "AllowPlayersWithoutMod is not on: nothing to switch back");
            yield break;
        }
        plugin.StartCoroutine(RefuseWatch(peer.m_playerName, arg ?? ""));
        SelfTest.Answer(reply, true, "armed");
    }

    // Server: AllowPlayersWithoutMod back to false (the real setting: its change handler check every connected player
    // again after the grace), then the verdict of that check, its log line and how long it took. Result lines under the
    // client test's name, in the server's log.
    private static IEnumerator RefuseWatch(string playerName, string reason)
    {
        // The "armed" answer go out first.
        yield return new WaitForSecondsRealtime(0.5f);
        var before = PlayerCheck.VerdictCount;
        var switched = Time.unscaledTime;
        Plugin.AllowPlayersWithoutMod.Value = false;
        var until = Time.unscaledTime + PlayerCheck.GraceSeconds + 6f;
        while (Time.unscaledTime < until && PlayerCheck.VerdictCount == before)
        {
            yield return null;
        }
        var took = Time.unscaledTime - switched;
        var checkedAgain = PlayerCheck.VerdictCount > before;
        var text = PlayerCheck.LastText ?? "";
        var ok = checkedAgain && PlayerCheck.LastVerdict == JoinVerdict.Refuse
                 && text.StartsWith("Refused " + playerName, StringComparison.Ordinal)
                 && text.Contains($": their game {reason}. This server requires {ModInfo.Name} on every player")
                 && LogWatch.Saw(text)
                 && took >= PlayerCheck.GraceSeconds - 0.2f && took <= PlayerCheck.GraceSeconds + 2f;
        var detail = $"server: AllowPlayersWithoutMod back to false, the still-connected player checked again {checkedAgain} after "
                     + $"{F2(took)} s (grace {F2(PlayerCheck.GraceSeconds)} s), verdict {PlayerCheck.LastVerdict}, log line '{text}'";
        if (ok)
        {
            SelfTest.Pass(MpAllowBackName, detail);
        }
        else
        {
            SelfTest.Fail(MpAllowBackName, detail + $" (expected a refusal about a second later, naming '{reason}')");
        }
    }

    // ---------- dual.mp.keys (T31, the real settings) ----------

    private static IEnumerator RunMpKeys()
    {
        var c = new Checks(MpKeysName);
        var player = MpPlayer(MpKeysName);
        if (player == null)
        {
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        var mainBefore = Plugin.MainHandKey.Value;
        var swapBefore = Plugin.SwapHandsKey.Value;
        var pitch = player.m_lookPitch;
        try
        {
            bench = new Bench(player);
            if (!InputReady(c, player))
            {
                c.Report();
                yield break;
            }
            // The real setting and the real keyboard: no key bound or held in memory.
            Controls.TestMainKey = null;
            Controls.TestSwapKey = null;
            Controls.TestMainHandHeld = null;
            Controls.TestForgetWarned();
            Plugin.MainHandKey.Value = KeyCode.LeftAlt;
            Plugin.SwapHandsKey.Value = KeyCode.H;
            Controls.CacheKeys();
            var sword = bench.Give(Sword);
            var axe = bench.Give(Axe);
            var mace = bench.Give(Mace);
            if (sword == null || axe == null || mace == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
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
            var seen = new bool[1];
            var quiet = new bool[1];

            // T31: set SwapHandsKey = J: J swaps, H do nothing.
            Plugin.SwapHandsKey.Value = KeyCode.J;
            yield return null;
            bench.Pair(sword, axe);
            yield return WaitIdle(player);
            yield return WaitMinor(player);
            c.Check(Controls.SwapKey == KeyCode.J && Holds(player, sword, axe),
                $"setting SwapHandsKey = J is in force at once (key in use {Controls.SwapKey}; {HandsText(player)})");
            yield return TapKey(KeyCode.H, seen);
            yield return NothingSwaps(player, sword, axe, 0.8f, quiet);
            c.Check(seen[0] && quiet[0], $"SwapHandsKey = J: a real H key press does nothing (key read {seen[0]}; {HandsText(player)})");
            yield return TapKey(KeyCode.J, seen);
            yield return WaitSwap(player);
            c.Check(seen[0] && Holds(player, axe, sword), $"SwapHandsKey = J: a real J key press swaps the hands (key read {seen[0]}; {HandsText(player)})");

            // T31: set SwapHandsKey = None: no key swaps.
            Plugin.SwapHandsKey.Value = KeyCode.None;
            yield return null;
            yield return TapKey(KeyCode.J, seen);
            var jRead = seen[0];
            yield return NothingSwaps(player, axe, sword, 0.8f, quiet);
            var jQuiet = quiet[0];
            yield return TapKey(KeyCode.H, seen);
            yield return NothingSwaps(player, axe, sword, 0.8f, quiet);
            c.Check(Controls.SwapKey == KeyCode.None && jRead && seen[0] && jQuiet && quiet[0],
                $"SwapHandsKey = None: real J and real H swap nothing (J read {jRead}, H read {seen[0]}; {HandsText(player)})");

            // T31: set MainHandKey = None: Left Alt + equip pair like a normal equip.
            Plugin.MainHandKey.Value = KeyCode.None;
            yield return null;
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
            c.Check(altRead && seen[0] && Controls.MainKey == KeyCode.None && Holds(player, sword, mace),
                $"MainHandKey = None: real Left Alt + hotbar key {slot + 1} pairs like a normal equip (Left Alt read {altRead}, "
                + $"hotbar key read {seen[0]}; {HandsText(player)})");
            Invariant(c, player, "MainHandKey = None");

            // T31: set SwapHandsKey = JoystickButton0: the warning, and no key swaps.
            var keyWarnings = Controls.WarningCount;
            Plugin.SwapHandsKey.Value = KeyCode.JoystickButton0;
            yield return null;
            c.Check(Controls.SwapKey == KeyCode.None && Controls.WarningCount == keyWarnings + 1 && Controls.LastWarning == GamepadWarning
                    && LogWatch.Saw(GamepadWarning),
                $"SwapHandsKey = JoystickButton0: the warning once, in the log ({Controls.WarningCount - keyWarnings} logged, "
                + $"'{Controls.LastWarning}')");
            yield return TapKey(KeyCode.H, seen);
            var hRead = seen[0];
            yield return NothingSwaps(player, sword, mace, 0.8f, quiet);
            var hQuiet = quiet[0];
            yield return TapKey(KeyCode.J, seen);
            yield return NothingSwaps(player, sword, mace, 0.8f, quiet);
            c.Check(hRead && seen[0] && hQuiet && quiet[0],
                $"SwapHandsKey = JoystickButton0: real H and real J swap nothing (H read {hRead}, J read {seen[0]}; {HandsText(player)})");

            // T31: both back (LeftAlt, H): H swap again, Left Alt + equip put the weapon alone in the main hand.
            Plugin.MainHandKey.Value = KeyCode.LeftAlt;
            Plugin.SwapHandsKey.Value = KeyCode.H;
            yield return null;
            yield return TapKey(KeyCode.H, seen);
            yield return WaitSwap(player);
            c.Check(seen[0] && Controls.SwapKey == KeyCode.H && Holds(player, mace, sword),
                $"SwapHandsKey back on H: a real H key press swaps again ({HandsText(player)})");
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
                $"MainHandKey back on LeftAlt: real Left Alt + hotbar key puts the mace alone in the main hand (Left Alt read "
                + $"{altRead}, hotbar key read {seen[0]}; {HandsText(player)})");
            Invariant(c, player, "keys back");
            c.Report();
        }
        finally
        {
            SelfTest.ReleaseInput();
            Plugin.MainHandKey.Value = mainBefore;
            Plugin.SwapHandsKey.Value = swapBefore;
            LookBack(player, pitch);
            if (bench != null)
            {
                bench.TakeBack();
            }
            Bench.ClearOverrides();
            Controls.CacheKeys();
            Hands.ResetState();
        }
    }

    // ---------- dual.mp.version-panel (M03 / M04, the player's side) ----------

    private static IEnumerator RunMpVersionPanel()
    {
        var c = new Checks(MpVersionPanelName);
        var player = MpPlayer(MpVersionPanelName);
        var found = FeatureRegistry.Find(ModInfo.Guid);
        if (player == null)
        {
            yield break;
        }
        if (found == null)
        {
            SelfTest.Fail(MpVersionPanelName, "the mod is not in the MC Mods list");
            yield break;
        }
        var feature = found.Value;
        yield return WaitIdle(player);
        Bench bench = null;
        var claimed = false;
        try
        {
            bench = new Bench(player);
            ServerRules.TestRules = null;
            yield return ResetServer(c);
            var sword = bench.Give(Sword);
            var axe = bench.Give(Axe);
            if (sword == null || axe == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            bench.Pair(sword, axe);
            yield return null;
            c.Check(Holds(player, sword, axe) && feature.IsActive, $"pair, mod active ({HandsText(player)})");
            var warnings = LogWatch.WarningLines().Length;
            var errors = LogWatch.ErrorLines().Length;

            // The server's answer for the mod now names another network version (a server with a newer build; the same
            // comparison a newer build make on a server with this one).
            claimed = true;
            yield return CallStep(ServerClaimStep, "other");
            c.Check(ReplyOk, $"the server said it has another network version ({_reply})");
            yield return WaitFor(() => feature.State == nameof(ModState.ServerMismatch), 10f);
            var wanted = $"Inactive: the server has version {ClaimedVersion}, you have {ModInfo.Version}, and they cannot talk to "
                         + "each other. Use matching versions.";
            c.Check(feature.State == nameof(ModState.ServerMismatch) && feature.Status == wanted,
                $"other network version: the MC Mods panel says the versions cannot talk to each other (status '{feature.Status}', "
                + $"state {feature.State})");
            c.Check(Holds(player, sword, null) && !axe.m_equipped && !Hands.IsMarked(axe),
                $"other network version: the off-hand weapon is put away ({HandsText(player)})");
            player.EquipItem(axe);
            yield return null;
            c.Check(Holds(player, axe, null) && !sword.m_equipped,
                $"other network version: no pair can be made, a second one-handed weapon replaces the first ({HandsText(player)})");
            yield return new WaitForSecondsRealtime(1.5f);
            var problems = LogProblemsSince(warnings, errors);
            c.Check(ServerRules.LastLogged == null && !LogWatch.WarningLines().Skip(warnings).Any(w => w.Contains("cannot read"))
                    && problems.Length == 0 && Connected,
                $"other network version: no server rules in use, no \"cannot read\" warning, nothing else warned, the player stays "
                + $"connected (rules logged '{ServerRules.LastLogged}', {problems.Length} warning or error line(s): "
                + $"{string.Join(" | ", problems)})");

            // The real answer again: active, the server's rules logged again, pairs work.
            yield return CallStep(ServerClaimStep, "real");
            claimed = !ReplyOk;
            c.Check(ReplyOk, $"the server said its real version again ({_reply})");
            yield return WaitFor(() => feature.IsActive, 10f);
            ServerRules.TestRules = null;
            yield return WaitFor(() => ServerRules.UsingServer && ServerRules.LastLogged != null, 8f);
            c.Check(feature.IsActive && ServerRules.UsingServer && ServerRules.LastLogged != null,
                $"matching versions again: active ('{feature.Status}'), the server's rules logged again ('{ServerRules.LastLogged}')");
            bench.Pair(sword, axe);
            yield return null;
            c.Check(Holds(player, sword, axe) && Connected, $"matching versions again: pairs work ({HandsText(player)})");
            Invariant(c, player, "versions match again");
            c.Report();
        }
        finally
        {
            if (claimed && Connected)
            {
                // Cut short between the two answers: the real one is asked for again (not waited for).
                FireServerStep(ServerClaimStep, "real");
            }
            if (bench != null)
            {
                bench.TakeBack();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
        }
    }

    // ---------- dual.mp.allow-back (M04) ----------

    private static IEnumerator RunMpAllowBack()
    {
        var c = new Checks(MpAllowBackName);
        var player = MpPlayer(MpAllowBackName);
        if (player == null)
        {
            yield break;
        }
        var serverPeer = ZNet.instance.GetServerPeer();
        var rpc = serverPeer != null ? serverPeer.m_rpc : null;
        if (rpc == null)
        {
            SelfTest.Fail(MpAllowBackName, "no connection to the server");
            yield break;
        }
        // The framework's hello of this mod (NetworkGate): "1|<network version>|<version>|on".
        var helloRpc = ModInfo.Guid + ".Hello";
        var realHello = $"1|{ModInfo.NetworkVersion}|{ModInfo.Version}|on";
        var other = ModInfo.NetworkVersion + 1;
        var otherSaid = false;
        var armed = false;
        try
        {
            ServerRules.TestRules = null;
            yield return ResetServer(c);
            yield return CallStep(ServerSetStep, "AllowPlayersWithoutMod=true");
            var allowed = ReplyOk && Field("allow") == "True";
            c.Check(allowed, $"server AllowPlayersWithoutMod = true ({_reply})");
            if (!allowed)
            {
                c.Report();
                yield break;
            }

            // M04: the player's copy now says another network version (what a build with ModNetworkVersion + 1 says
            // when it joins): with the setting on, the server let that player play.
            rpc.Invoke(helloRpc, $"1|{other}|{ModInfo.Version}|on");
            otherSaid = true;
            yield return new WaitForSecondsRealtime(0.5f);
            yield return CallStep(ServerPeerStep, "check");
            var reason = $"has another version of the mod (network version {other}, the server has {ModInfo.NetworkVersion})";
            var letIn = ReplyOk && Field("compatible") == "False" && Field("problem") == reason
                        && Field("last") == nameof(JoinVerdict.Allowed) && Field("kicked") == "False";
            c.Check(letIn, $"a copy with network version {other} is let in while AllowPlayersWithoutMod is on ({_reply})");
            yield return new WaitForSecondsRealtime(PlayerCheck.GraceSeconds + 1.5f);
            c.Check(Connected, $"that player stays connected while the setting is on (status {ZNet.GetConnectionStatus()})");
            if (!letIn || !Connected)
            {
                c.Report();
                yield break;
            }

            // M04: set it back to false: the players who are still connected are refused about a second later. The
            // server switch the setting itself half a second after its answer and check its own refusal (its lines
            // are in the server's log); this game must be back at the main menu with "Incompatible version".
            yield return CallStep(ServerRefuseStep, reason);
            armed = ReplyOk;
            c.Check(armed, $"the server will switch AllowPlayersWithoutMod back to false ({_reply})");
            if (!armed)
            {
                c.Report();
                yield break;
            }
            var asked = Time.realtimeSinceStartup;
            yield return WaitFor(() => FejdStartup.instance != null && Player.m_localPlayer == null, 30f);
            var atMenu = FejdStartup.instance != null && Player.m_localPlayer == null;
            var status = ZNet.GetConnectionStatus();
            // The server switch 0.5 s after its answer.
            var after = Time.realtimeSinceStartup - asked - 0.5f;
            c.Check(atMenu && status == ZNet.ConnectionStatus.ErrorVersion,
                $"AllowPlayersWithoutMod back to false: the still-connected player is refused, this game is back at the main menu "
                + $"with \"Incompatible version\" (at the menu {atMenu}, connection status {status})");
            c.Check(atMenu && after <= PlayerCheck.GraceSeconds + 9f,
                $"refused about a second after the switch, then the game's own log out: back at the menu {F2(after)} s after it");
            SelfTest.Note(MpAllowBackName, "the server's own PASS or FAIL line for this test (the re-check, its \"Refused ...\" log line "
                                           + "and its timing) is in the server's log");
            c.Report();
        }
        finally
        {
            if (Connected && rpc.IsConnected())
            {
                if (otherSaid)
                {
                    rpc.Invoke(helloRpc, realHello);
                }
                if (!armed)
                {
                    // Not refused: the server's setting back to their default for the test after this one (not
                    // waited for).
                    FireServerStep(ServerSetStep, "reset");
                }
            }
            Bench.ClearOverrides();
        }
    }
}
#endif
