#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.WeaponsMovesetMod;

// Debug build only. Multiplayer self tests (tools/Test-Multiplayer.ps1): this client joined to a real dedicated
// server, both with the mod. Client tests (scenario "modded") call server halves ("moveset.mp.s.*") through the
// probes. The server stands in for "the other player": it is another game with the mod that gets this player's
// animation triggers and state the same way a second player's game does. Config files of a multiplayer run are
// throwaway copies, so the server halves change real settings there (never in a single-player run).
//   moveset.mp.join            the server found this player compatible ("has Weapon Moveset: allowed"), its settings
//                              are in force here
//   moveset.mp.settings        M01 / M06: a setting changed on the server arrives once, the jump attack uses it, own
//                              config untouched; a slider drag = one message; a server animation is played
//   moveset.mp.observer        M09: on the server's copy of this player the roll attack follows the roll with no
//                              stand-up (cross-faded there too), the battleaxe swing keeps the stand-up
//   moveset.mp.server-log      M11: roll attacks as a dedicated server has them (no copy of the player there) and with
//                              a copy on the server: no warning or error of the mod on the server
// A dedicated server makes objects only around its reference position, and the dedicated server build puts that at
// (1000000, 0, 1000000) in EVERY Game.FixedUpdate (read in the server's own assembly_valheim.dll, 1.0.17; the client
// build in .ref has no such line): out of the world, so it has no copy of any player, wherever they stand. Runs 1 and
// 2 (player 230 m, then 2.8 m from the world centre) both said "player=0;players=0"; run 2 set the position once and
// waited 30 s for nothing: it was gone one physics step later. Server step "watch ...|near" now puts it back on the
// player after every physics step for the length of the watch (ServerWatch, HoldRef).
//   moveset.mp.early-cut       M10: FlowStart 0, the server never sees this player invulnerable when the roll attack's
//                              trigger arrives nor during the swing
//   moveset.mp.client-toggle   M08 (c) / L01 / L02: mod off on this game with AllowPlayersWithoutMod on = not refused,
//                              vanilla moves; on again = settings again, moves back
//   moveset.mp.server-toggle   M04: server turns the mod off and on: moves stop, come back with the settings, every
//                              player is checked again
//   moveset.mp.version         M07 (pretended version numbers): server's verdict text, client's status and moves
//   moveset.mp.vanilla-server  M05 (scenario "vanilla-server", me inactive): status text, vanilla combat
internal static partial class SelfTests
{
    private const string MpJoinName = "moveset.mp.join";
    private const string MpSettingsName = "moveset.mp.settings";
    private const string MpObserverName = "moveset.mp.observer";
    private const string MpServerLogName = "moveset.mp.server-log";
    private const string MpEarlyCutName = "moveset.mp.early-cut";
    private const string MpClientToggleName = "moveset.mp.client-toggle";
    private const string MpServerToggleName = "moveset.mp.server-toggle";
    private const string MpVersionName = "moveset.mp.version";
    private const string MpNoServerName = "moveset.mp.vanilla-server";

    private const string VanillaServerScenario = "vanilla-server";
    private const string ProbeSetEnabled = "probe.set-enabled"; // server probe's own step: "<GUID>=on|off"

    private const string StepMark = "moveset.mp.s.mark";
    private const string StepCount = "moveset.mp.s.count";
    private const string StepRules = "moveset.mp.s.rules";
    private const string StepSet = "moveset.mp.s.set";
    private const string StepReset = "moveset.mp.s.reset";
    private const string StepDrag = "moveset.mp.s.drag";
    private const string StepWatch = "moveset.mp.s.watch";
    private const string StepWatched = "moveset.mp.s.watched";
    private const string StepVersion = "moveset.mp.s.version";

    private const string GeneralSection = "General";
    private const string AllowKey = "AllowPlayersWithoutMod";

    private static void RegisterMp()
    {
        SelfTest.RegisterMultiplayer(MpJoinName, SelfTest.Modded, RunMpJoin);
        SelfTest.RegisterMultiplayer(MpSettingsName, SelfTest.Modded, RunMpSettings);
        SelfTest.RegisterMultiplayer(MpObserverName, SelfTest.Modded, RunMpObserver);
        SelfTest.RegisterMultiplayer(MpServerLogName, SelfTest.Modded, RunMpServerLog);
        SelfTest.RegisterMultiplayer(MpEarlyCutName, SelfTest.Modded, RunMpEarlyCut);
        SelfTest.RegisterMultiplayer(MpClientToggleName, SelfTest.Modded, RunMpClientToggle);
        SelfTest.RegisterMultiplayer(MpServerToggleName, SelfTest.Modded, RunMpServerToggle);
        SelfTest.RegisterMultiplayer(MpVersionName, SelfTest.Modded, RunMpVersion);
        SelfTest.RegisterServerStep(StepMark, ServerMark);
        SelfTest.RegisterServerStep(StepCount, ServerCount);
        SelfTest.RegisterServerStep(StepRules, ServerRulesStep);
        SelfTest.RegisterServerStep(StepSet, ServerSet);
        SelfTest.RegisterServerStep(StepReset, ServerReset);
        SelfTest.RegisterServerStep(StepDrag, ServerDrag);
        SelfTest.RegisterServerStep(StepWatch, ServerWatch);
        SelfTest.RegisterServerStep(StepWatched, ServerWatched);
        SelfTest.RegisterServerStep(StepVersion, ServerFakeVersion);
    }

    private static void UnregisterMp()
    {
        SelfTest.UnregisterMultiplayer(MpJoinName);
        SelfTest.UnregisterMultiplayer(MpSettingsName);
        SelfTest.UnregisterMultiplayer(MpObserverName);
        SelfTest.UnregisterMultiplayer(MpServerLogName);
        SelfTest.UnregisterMultiplayer(MpEarlyCutName);
        SelfTest.UnregisterMultiplayer(MpClientToggleName);
        SelfTest.UnregisterMultiplayer(MpServerToggleName);
        SelfTest.UnregisterMultiplayer(MpVersionName);
        SelfTest.UnregisterServerStep(StepMark);
        SelfTest.UnregisterServerStep(StepCount);
        SelfTest.UnregisterServerStep(StepRules);
        SelfTest.UnregisterServerStep(StepSet);
        SelfTest.UnregisterServerStep(StepReset);
        SelfTest.UnregisterServerStep(StepDrag);
        SelfTest.UnregisterServerStep(StepWatch);
        SelfTest.UnregisterServerStep(StepWatched);
        SelfTest.UnregisterServerStep(StepVersion);
        if (_serverWatch != null)
        {
            _serverWatch.Stop = true;
            RestoreRef(_serverWatch);
        }
    }

    // Registered at every start and never taken away: this test runs while me inactive (server without the mod).
    private static void RegisterMpInactive()
    {
        SelfTest.RegisterMultiplayer(MpNoServerName, VanillaServerScenario, RunMpNoServer);
    }

    // ================= server halves =================

    private static string Inv(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static ZNetPeer FindPeer(string name)
    {
        var net = ZNet.instance;
        if (net == null)
        {
            return null;
        }
        foreach (var peer in net.GetPeers())
        {
            if (peer != null && peer.m_playerName == name)
            {
                return peer;
            }
        }
        return null;
    }

    // This game's copy of a player (a dedicated server has none, unless a watch holds its area on the player).
    private static Player FindPlayer(string name)
    {
        foreach (var player in Player.GetAllPlayers())
        {
            if (player != null && player.GetPlayerName() == name)
            {
                return player;
            }
        }
        return null;
    }

    // AllowPlayersWithoutMod back to false, but only when every connected player is compatible right now: a test
    // must never get its own client refused. True = it is false now.
    private static bool AllowOff()
    {
        if (!Plugin.AllowPlayersWithoutMod.Value)
        {
            return true;
        }
        var net = ZNet.instance;
        if (net != null)
        {
            foreach (var peer in net.GetPeers())
            {
                if (peer != null && peer.IsReady() && !NetworkGate.PeerCompatible(peer))
                {
                    return false;
                }
            }
        }
        Plugin.AllowPlayersWithoutMod.Value = false;
        return true;
    }

    private static string ServerState() =>
        $"{ServerRules.Current.Describe()}|{ServerRules.Source}|allow {Plugin.AllowPlayersWithoutMod.Value}";

    // "" -> the number of log lines of this mod so far (a later count starts there).
    private static IEnumerator ServerMark(string arg, object[] reply)
    {
        SelfTest.Answer(reply, true, LogTap.Mark.ToString(CultureInfo.InvariantCulture));
        yield break;
    }

    // "<from>|<D|I|W|E|A>|<text>" -> "<count>|<up to 5 of those lines, joined by ' // '>".
    private static IEnumerator ServerCount(string arg, object[] reply)
    {
        var parts = (arg ?? "").Split(new[] { '|' }, 3);
        if (parts.Length < 3 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var from))
        {
            SelfTest.Answer(reply, false, "argument must be <from>|<level>|<text>");
            yield break;
        }
        var levels = parts[1] == "D" ? Dbg : parts[1] == "I" ? Inf : parts[1] == "W" ? Wrn : parts[1] == "E" ? Err : Dbg | Inf | Wrn | Err;
        var lines = LogTap.Since(from, levels, parts[2].Length > 0 ? parts[2] : null);
        var text = string.Join(" // ", lines.Take(5).Select(l => l.Text).ToArray());
        SelfTest.Answer(reply, true, lines.Count.ToString(CultureInfo.InvariantCulture) + "|" + text);
    }

    private static IEnumerator ServerRulesStep(string arg, object[] reply)
    {
        SelfTest.Answer(reply, true, ServerState());
        yield break;
    }

    // "<section>|<key>=<value>": the server owner changes one setting (its own throwaway config of the run).
    private static IEnumerator ServerSet(string arg, object[] reply)
    {
        var plugin = PluginInstance();
        var text = arg ?? "";
        var bar = text.IndexOf('|');
        var eq = text.IndexOf('=');
        if (plugin == null || !SelfTest.IsMultiplayerRun || bar <= 0 || eq <= bar)
        {
            SelfTest.Answer(reply, false, "argument must be <section>|<key>=<value> (multiplayer run only)");
            yield break;
        }
        var section = text.Substring(0, bar);
        var key = text.Substring(bar + 1, eq - bar - 1);
        var value = text.Substring(eq + 1);
        if (section == GeneralSection)
        {
            if (key != AllowKey)
            {
                SelfTest.Answer(reply, false, "of the General section a test may only set AllowPlayersWithoutMod");
                yield break;
            }
            if (value == "true")
            {
                Plugin.AllowPlayersWithoutMod.Value = true;
            }
            else if (!AllowOff())
            {
                SelfTest.Answer(reply, false, "a connected player is not compatible right now: AllowPlayersWithoutMod stays on (nobody gets refused by a test)");
                yield break;
            }
        }
        else
        {
            var def = new ConfigDefinition(section, key);
            if (!plugin.Config.ContainsKey(def))
            {
                SelfTest.Answer(reply, false, $"no setting [{section}] {key}");
                yield break;
            }
            plugin.Config[def].SetSerializedValue(value);
        }
        yield return null;
        SelfTest.Answer(reply, true, ServerState());
    }

    // Every move setting back to its default; AllowPlayersWithoutMod back to false when nobody would be refused.
    private static IEnumerator ServerReset(string arg, object[] reply)
    {
        var plugin = PluginInstance();
        if (plugin == null || !SelfTest.IsMultiplayerRun)
        {
            SelfTest.Answer(reply, false, "multiplayer run only");
            yield break;
        }
        var changed = 0;
        foreach (var def in plugin.Config.Keys.ToArray())
        {
            if (def.Section == GeneralSection)
            {
                continue;
            }
            var entry = plugin.Config[def];
            if (!Equals(entry.BoxedValue, entry.DefaultValue))
            {
                entry.BoxedValue = entry.DefaultValue;
                changed++;
            }
        }
        var refusing = AllowOff();
        yield return null;
        SelfTest.Answer(reply, refusing, $"{changed} setting(s) put back|{ServerState()}");
    }

    // "<section>|<key>=<v1>,<v2>,...": like a slider dragged: one value per two frames. Answer once the settings
    // message went out (or 2.5 s): "sent=<messages>;delay=<s from the last value to the message>;drag=<s>".
    private static IEnumerator ServerDrag(string arg, object[] reply)
    {
        var plugin = PluginInstance();
        var text = arg ?? "";
        var bar = text.IndexOf('|');
        var eq = text.IndexOf('=');
        if (plugin == null || !SelfTest.IsMultiplayerRun || bar <= 0 || eq <= bar)
        {
            SelfTest.Answer(reply, false, "argument must be <section>|<key>=<v1>,<v2>,... (multiplayer run only)");
            yield break;
        }
        var def = new ConfigDefinition(text.Substring(0, bar), text.Substring(bar + 1, eq - bar - 1));
        if (def.Section == GeneralSection || !plugin.Config.ContainsKey(def))
        {
            SelfTest.Answer(reply, false, $"no move setting [{def.Section}] {def.Key}");
            yield break;
        }
        var entry = plugin.Config[def];
        var mark = LogTap.Mark;
        var first = Time.unscaledTime;
        var last = first;
        foreach (var value in text.Substring(eq + 1).Split(','))
        {
            entry.SetSerializedValue(value);
            last = Time.unscaledTime;
            yield return null;
            yield return null;
        }
        var sentAt = -1f;
        var end = Time.unscaledTime + 2.5f;
        while (Time.unscaledTime < end)
        {
            if (sentAt < 0f && LogTap.Count(mark, Dbg, "Sent the move settings to ") > 0)
            {
                sentAt = Time.unscaledTime;
            }
            if (sentAt >= 0f && Time.unscaledTime - sentAt > 1f)
            {
                break; // one more second: a second message would show
            }
            yield return null;
        }
        var sent = LogTap.Count(mark, Dbg, "Sent the move settings to ");
        SelfTest.Answer(reply, true, $"sent={sent};delay={Inv(sentAt < 0f ? -1f : sentAt - last)};drag={Inv(last - first)}");
    }

    // What the server sees of one player while a client test plays (ServerWatch starts it, ServerWatched reads it).
    private sealed class Watch
    {
        internal string Name;
        internal float Until;
        internal bool Stop;
        internal bool Running;
        internal bool HadPlayer;
        internal float Distance = -1f;
        internal int Ticks;
        internal int Rolls;                                       // rolls seen on the copy's Animator
        internal readonly List<int> Gaps = new List<int>();       // per roll: idle ticks before the attack after it (-1 = none came)
        internal int Cuts;                                        // cross-fades my remote roll flow did here
        internal int InvAtCut;                                    // ... while this game still read the player invulnerable
        internal readonly List<string> Triggers = new List<string>(); // attack triggers that arrived ("!" = invulnerable then)
        internal int InvAtTrigger;                                // roll attack triggers that arrived while invulnerable
        internal int InvInSwing;                                  // ticks in the half second after one, still invulnerable
        internal int InvTicks;                                    // ticks this game read the player invulnerable at all
        internal float SwingUntil;
        internal int AlertFrom;
        internal int LogFrom;
        internal bool RefMoved;                                   // me hold this game's reference position on the player
        internal Vector3 RefWas;
        internal Vector3 RefAt;                                   // where me hold it (the player's own reference position)
        internal float HoldUntil;                                 // hold never outlive this
    }

    private static Watch _serverWatch;

    // Reference position of this game back where the watch found it (Watching's end, or the tests being taken away).
    // Also ends HoldRef (it loops while RefMoved).
    private static void RestoreRef(Watch watch)
    {
        if (watch == null || !watch.RefMoved)
        {
            return;
        }
        watch.RefMoved = false;
        var net = ZNet.instance;
        if (net != null)
        {
            net.SetReferencePosition(watch.RefWas);
        }
    }

    // Dedicated server build put its reference position at (1000000, 0, 1000000) in EVERY Game.FixedUpdate, so a
    // position set once is gone one physics step later (run 2: "moved=1;waited=30.035" and still "players=0"). Me put
    // it back on the player after each physics step: Unity resume WaitForFixedUpdate after every FixedUpdate of the
    // step and before the frame's Update, where ZoneSystem, ZNetScene and ZDOMan read it. Runs on the plugin, not on
    // the step. Ends with the watch (RestoreRef), with a newer watch, or at HoldUntil.
    private static IEnumerator HoldRef(Watch watch)
    {
        var step = new WaitForFixedUpdate();
        while (watch.RefMoved && ReferenceEquals(_serverWatch, watch) && Time.realtimeSinceStartup < watch.HoldUntil)
        {
            var net = ZNet.instance;
            if (net == null)
            {
                break;
            }
            var peer = FindPeer(watch.Name);
            if (peer != null)
            {
                watch.RefAt = peer.GetRefPos(); // area follow the player
            }
            net.SetReferencePosition(watch.RefAt);
            yield return step;
        }
        RestoreRef(watch);
    }

    // This game's reference position is on the watched player right now (me hold it there).
    private static bool RefHeld(Watch watch)
    {
        var net = ZNet.instance;
        return watch != null && watch.RefMoved && net != null && Utils.DistanceXZ(net.GetReferencePosition(), watch.RefAt) < 1f;
    }

    // Test-only hook on the server: every animation trigger of the watched player, as it arrives here.
    private static void WatchTriggerPre(ZSyncAnimation __instance, string name)
    {
        try
        {
            var watch = _serverWatch;
            if (watch == null || !watch.Running || __instance == null || string.IsNullOrEmpty(name))
            {
                return;
            }
            var rules = ServerRules.Current;
            if (rules == null || Array.IndexOf(rules.RollTriggers, name) < 0)
            {
                return;
            }
            var player = __instance.GetComponent<Player>();
            if (player == null || player.GetPlayerName() != watch.Name)
            {
                return;
            }
            var invulnerable = player.IsDodgeInvincible();
            watch.Triggers.Add(name + (invulnerable ? "!" : ""));
            if (invulnerable)
            {
                watch.InvAtTrigger++;
            }
            watch.SwingUntil = Time.realtimeSinceStartup + 0.5f;
        }
        catch (Exception e)
        {
            PatchGuard.Report("SelfTests server watch ZSyncAnimation.RPC_SetTrigger", e);
        }
    }

    // How long the server may take to make its copy of the player once its reference position is held on them (zones
    // around it first, one per 0.1 s, then the objects; same run, Tower Shield Wall's held step: 4.2 s, 5510 objects).
    private const float CopyWait = 25f;

    // "<player name>|<seconds>[|near]": start watching that player's copy on this game. Answers at once, or with
    // "near" once the copy is here and the area around it is made (at most CopyWait + 10 s).
    //   near: a dedicated server makes objects only around its reference position, and its build keeps that out of
    //   the world (HoldRef): NO copy of any player there, wherever they stand (runs 1 and 2: "player=0;players=0", so
    //   the server halves saw nothing). For the length of the watch me hold the reference position on the player,
    //   like a host standing next to them: the server makes its copy the way a host does. Watching puts it back.
    // Answer: "player=<1 copy here>;distance=<copy, m from the world centre>;players=<copies of any player>;
    //   moved=<1 me held the position>;held=<1 it is on the player now>;waited=<s>;ref=<m from the world centre of
    //   the position as found>;area=<1 the player stood inside this game's own area as found>;objects=<made here>".
    private static IEnumerator ServerWatch(string arg, object[] reply)
    {
        var parts = (arg ?? "").Split('|');
        var plugin = PluginInstance();
        if (plugin == null || parts[0].Length == 0)
        {
            SelfTest.Answer(reply, false, "argument must be <player name>|<seconds>[|near]");
            yield break;
        }
        var seconds = parts.Length > 1 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? Mathf.Clamp(s, 1f, 60f) : 20f;
        var near = parts.Length > 2 && parts[2] == "near";
        var old = _serverWatch;
        if (old != null)
        {
            // Old watch must be over first: its end puts the reference position back, never over a new watch's.
            old.Stop = true;
            var wait = Time.realtimeSinceStartup + 1.5f;
            while (old.Running && Time.realtimeSinceStartup < wait)
            {
                yield return null;
            }
            RestoreRef(old);
        }
        var watch = new Watch
        {
            Name = parts[0],
            AlertFrom = LogTap.AlertMark,
            LogFrom = LogTap.Mark,
        };
        _serverWatch = watch;
        var copy = FindPlayer(watch.Name);
        var t0 = Time.realtimeSinceStartup;
        var net = ZNet.instance;
        var peer = FindPeer(watch.Name);
        var refFound = net != null ? net.GetReferencePosition() : Vector3.zero;
        var inArea = net != null && peer != null && ZoneSystem.instance != null && ZNetScene.InActiveArea(peer.GetRefPos(), refFound);
        if (copy != null && !inArea && peer != null && Player.m_localPlayer == null)
        {
            // Copy left by a watch that held the area a moment ago: the area is elsewhere again, this game's next
            // object pass drops everything it made there. Wait for that: a copy about to go is no copy.
            var gone = Time.realtimeSinceStartup + 5f;
            while (copy != null && Time.realtimeSinceStartup < gone)
            {
                yield return null;
                copy = FindPlayer(watch.Name);
            }
        }
        if (copy == null && near && SelfTest.IsMultiplayerRun && net != null && net.IsServer() && Player.m_localPlayer == null && peer != null)
        {
            watch.RefWas = refFound;
            watch.RefAt = peer.GetRefPos();
            watch.RefMoved = true;
            watch.HoldUntil = Time.realtimeSinceStartup + CopyWait + 10f + seconds + 5f;
            net.SetReferencePosition(watch.RefAt);
            plugin.StartCoroutine(HoldRef(watch));
            while (copy == null && !watch.Stop && Time.realtimeSinceStartup - t0 < CopyWait)
            {
                yield return null;
                copy = FindPlayer(watch.Name);
            }
            // Area still being made (up to 100 objects per pass, 30 passes a second, long frames: triggers would
            // arrive late on the copy's Animator). Wait until the object count stood still for 0.75 s (10 s at most).
            var scene = ZNetScene.instance;
            var count = -1;
            var since = Time.realtimeSinceStartup;
            var quiet = since + 10f;
            while (copy != null && scene != null && !watch.Stop && Time.realtimeSinceStartup - since < 0.75f && Time.realtimeSinceStartup < quiet)
            {
                yield return null;
                var now = scene.m_instances.Count;
                if (now != count)
                {
                    count = now;
                    since = Time.realtimeSinceStartup;
                }
            }
            copy = FindPlayer(watch.Name);
        }
        var moved = watch.RefMoved;
        var held = RefHeld(watch);
        watch.HadPlayer = copy != null;
        if (copy != null)
        {
            watch.Distance = Flat(copy.transform.position);
        }
        if (watch.Stop)
        {
            RestoreRef(watch);
            SelfTest.Answer(reply, false, "the watch was stopped before it started");
            yield break;
        }
        watch.Until = Time.realtimeSinceStartup + seconds;
        watch.Running = true;
        plugin.StartCoroutine(Watching(watch)); // ends by itself at Until (or when the next step stops it)
        yield return null;
        SelfTest.Answer(reply, true, $"player={(copy != null ? 1 : 0)};distance={Inv(watch.Distance)};players={Player.GetAllPlayers().Count};"
                                     + $"moved={(moved ? 1 : 0)};held={(held ? 1 : 0)};waited={Inv(Time.realtimeSinceStartup - t0)};"
                                     + $"ref={Flat(refFound).ToString("0", CultureInfo.InvariantCulture)};area={(inArea ? 1 : 0)};"
                                     + $"objects={(ZNetScene.instance != null ? ZNetScene.instance.m_instances.Count : 0)}");
    }

    private static IEnumerator Watching(Watch watch)
    {
        var harmony = new Harmony(ModInfo.Guid + ".selftest.watch");
        try
        {
            harmony.Patch(AccessTools.Method(typeof(ZSyncAnimation), nameof(ZSyncAnimation.RPC_SetTrigger)),
                prefix: new HarmonyMethod(typeof(SelfTests), nameof(WatchTriggerPre)));
            var cuts = RollFlow.RemoteCuts;
            var phase = 0; // 0 none, 1 roll seen, 2 attack after a roll
            var gap = 0;
            while (!watch.Stop && Time.realtimeSinceStartup < watch.Until)
            {
                yield return Fixed;
                var copy = FindPlayer(watch.Name);
                var animator = copy != null && copy.m_zanim != null ? copy.m_zanim.m_animator : null;
                if (animator == null)
                {
                    continue;
                }
                watch.HadPlayer = true;
                watch.Ticks++;
                var invulnerable = copy.IsDodgeInvincible();
                if (invulnerable)
                {
                    watch.InvTicks++;
                }
                if (RollFlow.RemoteCuts != cuts)
                {
                    watch.Cuts += RollFlow.RemoteCuts - cuts;
                    cuts = RollFlow.RemoteCuts;
                    if (invulnerable)
                    {
                        watch.InvAtCut++;
                    }
                }
                if (invulnerable && Time.realtimeSinceStartup < watch.SwingUntil)
                {
                    watch.InvInSwing++;
                }
                RollFlow.NextOrCurrent(animator, out _, out var tag);
                var attack = tag == RollFlow.AttackTag;
                if (phase == 0)
                {
                    if (!attack && RollFlow.InRoll(animator))
                    {
                        phase = 1;
                        gap = 0;
                        watch.Rolls++;
                    }
                }
                else if (phase == 1)
                {
                    if (attack)
                    {
                        watch.Gaps.Add(gap);
                        phase = 2;
                    }
                    else if (InGap(animator) && ++gap > 40)
                    {
                        watch.Gaps.Add(-1);
                        phase = 0;
                    }
                }
                else if (!attack)
                {
                    phase = 0;
                }
            }
        }
        finally
        {
            watch.Running = false;
            harmony.UnpatchSelf();
            RestoreRef(watch);
        }
    }

    // My patch on other players' animation triggers (RollFlow.OnRemoteTrigger) is on in this game.
    private static bool RemotePatchOn()
    {
        var info = Harmony.GetPatchInfo(AccessTools.Method(typeof(ZSyncAnimation), nameof(ZSyncAnimation.RPC_SetTrigger)));
        return info != null && info.Owners.Contains(ModInfo.Guid);
    }

    // Stop the watch and say what it saw.
    private static IEnumerator ServerWatched(string arg, object[] reply)
    {
        var watch = _serverWatch;
        if (watch == null)
        {
            SelfTest.Answer(reply, false, "no watch was started");
            yield break;
        }
        watch.Stop = true;
        var end = Time.realtimeSinceStartup + 1f;
        while (watch.Running && Time.realtimeSinceStartup < end)
        {
            yield return null;
        }
        var alerts = LogTap.AlertsSince(watch.AlertFrom, Wrn | Err);
        var found = LogTap.Count(watch.LogFrom, Dbg, "Found the animation state of ");
        var faded = LogTap.Count(watch.LogFrom, Dbg, "Cross-faded " + watch.Name + "'s roll attack ");
        SelfTest.Answer(reply, true,
            $"player={(watch.HadPlayer ? 1 : 0)};ticks={watch.Ticks};rolls={watch.Rolls};cuts={watch.Cuts};faded={faded};"
            + $"gaps={string.Join(",", watch.Gaps.Select(g => g.ToString(CultureInfo.InvariantCulture)).ToArray())};"
            + $"invAtCut={watch.InvAtCut};triggers={string.Join(",", watch.Triggers.ToArray())};invAtTrigger={watch.InvAtTrigger};"
            + $"invInSwing={watch.InvInSwing};inv={watch.InvTicks};mod={(RemotePatchOn() ? 1 : 0)};alerts={alerts.Count};found={found};probeMs={Inv((float)StateProbe.LastMilliseconds)};"
            + $"first={(alerts.Count > 0 ? alerts[0].Text.Replace(';', ',') : "")}");
    }

    // "<player name>|<network version>": the server's verdict for a player whose copy of the mod has another network
    // version, with no second build: for the length of one check this player's hello says that version.
    // AllowPlayersWithoutMod is on meanwhile, so the verdict is "let in, with a warning" and nobody is refused.
    // Answer = the warning line.
    private static IEnumerator ServerFakeVersion(string arg, object[] reply)
    {
        var parts = (arg ?? "").Split('|');
        var peer = FindPeer(parts[0]);
        var field = typeof(NetworkGate).GetField("ClientHellos", BindingFlags.NonPublic | BindingFlags.Static);
        var hellos = field != null ? field.GetValue(null) as Dictionary<ZRpc, string> : null;
        string real = null;
        if (parts.Length < 2 || peer == null || peer.m_rpc == null || hellos == null || !hellos.TryGetValue(peer.m_rpc, out real)
            || !SelfTest.IsMultiplayerRun)
        {
            SelfTest.Answer(reply, false, "player, or the framework's table of player hellos, not found");
            yield break;
        }
        var mark = LogTap.Mark;
        var wasOn = Plugin.AllowPlayersWithoutMod.Value;
        string line = null;
        try
        {
            Plugin.AllowPlayersWithoutMod.Value = true;
            var fields = (real ?? "").Split('|');
            hellos[peer.m_rpc] = $"{fields[0]}|{parts[1]}|{(fields.Length > 2 ? fields[2] : ModInfo.Version)}|on";
            PlayerCheck.Schedule(peer);
            var end = Time.realtimeSinceStartup + PlayerCheck.GraceSeconds + 2.5f;
            while (line == null && Time.realtimeSinceStartup < end)
            {
                yield return null;
                line = LogTap.First(mark, Wrn, "Not refusing ");
            }
        }
        finally
        {
            hellos[peer.m_rpc] = real; // the real hello first, then back to refusing
            if (!wasOn)
            {
                AllowOff();
            }
        }
        yield return null;
        var refused = LogTap.Count(mark, Wrn, "Refused ");
        SelfTest.Answer(reply, line != null && refused == 0, line ?? "no \"Not refusing\" warning within the grace time");
    }

    // ================= client side =================

    private static string MyName() => Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "";

    private static bool Connected() =>
        ZNet.instance != null && !ZNet.instance.IsServer() && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected
        && Player.m_localPlayer != null;

    private static Dictionary<string, string> Fields(string detail)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in (detail ?? "").Split(';'))
        {
            var eq = part.IndexOf('=');
            if (eq > 0)
            {
                result[part.Substring(0, eq)] = part.Substring(eq + 1);
            }
        }
        return result;
    }

    private static int Int(Dictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : -1;

    private static float Num(Dictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : -1f;

    private static string Text(Dictionary<string, string> fields, string key) => fields.TryGetValue(key, out var v) ? v : "";

    // Finally blocks cannot wait: me send the step and do not wait for the answer. (The probe's call is two nested
    // steps: the first move makes the inner one, the inner one's first move sends.)
    private static void FireServer(string step, string arg)
    {
        try
        {
            var call = SelfTest.CallServer(step, arg, new SelfTest.ServerReply());
            if (call.MoveNext() && call.Current is IEnumerator inner)
            {
                inner.MoveNext();
                (inner as IDisposable)?.Dispose();
            }
            (call as IDisposable)?.Dispose();
        }
        catch (Exception e)
        {
            SelfTest.Note("moveset.mp", $"could not send {step} to the server: {e.Message}");
        }
    }

    // Lines of the mod in the server's log since its mark `from`: how many, and the first ones.
    private static IEnumerator ServerLines(string from, string level, string text, Box<int> count, Box<string> lines)
    {
        count.Value = -1;
        lines.Value = "";
        var reply = new SelfTest.ServerReply();
        yield return SelfTest.CallServer(StepCount, $"{from}|{level}|{text}", reply);
        if (!reply.Answered || !reply.Ok)
        {
            lines.Value = reply.ToString();
            yield break;
        }
        var bar = reply.Detail.IndexOf('|');
        if (bar > 0 && int.TryParse(reply.Detail.Substring(0, bar), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            count.Value = n;
            lines.Value = reply.Detail.Substring(bar + 1);
        }
    }

    // Server's move settings back to their defaults, in force on this game. ok = done.
    private static IEnumerator MpReset(string test, Box<bool> ok)
    {
        ok.Value = false;
        var reply = new SelfTest.ServerReply();
        yield return SelfTest.CallServer(StepReset, "", reply);
        if (!reply.Answered || !reply.Ok)
        {
            SelfTest.Note(test, "server reset: " + reply);
            yield break;
        }
        var want = MoveRules.Defaults().Describe();
        yield return WaitReal(() => ActiveNow() && ServerRules.Source == RulesSource.Server && ServerRules.Current.Describe() == want, 10f, ok);
        if (!ok.Value)
        {
            SelfTest.Note(test, $"after the reset: mod {StateNow()}, settings from {ServerRules.Source}: {ServerRules.Current.Describe()}");
        }
    }

    // Combat as in the game without the mod: jump + attack, and (full) attack held through a roll.
    private static IEnumerator VanillaCombat(Rig rig, Checks c, string what, Attack shared, bool full)
    {
        var p = rig.P;
        yield return WaitLanded(p, 3f);
        yield return WaitIdle(p, 4f);
        Put(p, rig.Home);
        Face(p, rig.Forward);
        yield return Fixed;
        var mark = LogTap.Mark;
        var jump = new JumpRun();
        yield return JumpPress(rig, jump);
        c.Check(jump.Jumped && !jump.TokenAtJump && jump.Started != null && jump.Clone == null && Vanilla(jump.Started, shared),
            $"{what}: jump + attack is a normal swing (got {Fired(jump.Started)})");
        yield return AttackOver(p, jump.Started, 3f);
        yield return WaitLanded(p, 3f);
        yield return WaitIdle(p, 4f);
        if (full)
        {
            Put(p, rig.Home);
            Face(p, rig.Forward);
            yield return Fixed;
            var roll = new RollRun();
            rig.TakeController();
            yield return Roll(rig, roll, true, q => Hold(q, false));
            rig.GiveController();
            c.Check(roll.Clone == null && roll.Started != null && !roll.StartedInRoll && roll.GapTicks > 0 && Vanilla(roll.Started, shared),
                $"{what}: attack held through a roll is a normal swing after the roll and its stand-up (got {Fired(roll.Started)}, idle for {S(roll.Gap)} s)");
            yield return AttackOver(p, roll.Started, 3f);
            yield return WaitIdle(p, 4f);
        }
        c.Check(LogTap.Count(mark, Dbg, " attack: ") == 0, $"{what}: no move Debug line");
    }

    // Start of a client test: connected, mod active, server settings at defaults and here, sword in hand.
    private static IEnumerator MpStart(string test, Rig rig, Checks c, Box<ItemDrop.ItemData> sword)
    {
        var ok = new Box<bool>();
        yield return MpReset(test, ok);
        c.Check(Connected() && ok.Value, "connected to the dedicated server, the mod active, the server's default move settings in force here");
        sword.Value = null;
        if (!ok.Value)
        {
            yield break;
        }
        yield return Equip(rig, SwordRow, sword);
        c.Check(sword.Value != null, "SwordIron equipped");
    }

    // ---------- moveset.mp.join ----------

    private static IEnumerator RunMpJoin()
    {
        var p = LocalPlayer(MpJoinName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(MpJoinName);
        var ok = new Box<bool>();
        var count = new Box<int>();
        var lines = new Box<string>();
        c.Check(Connected(), "connected to the dedicated server as a client");
        c.Check(ActiveNow() && PatchedCount() == 5, $"the mod is active on this game ({StateNow()}), its combat patches on");
        yield return WaitReal(() => ServerRules.Source == RulesSource.Server, 10f, ok);
        c.Check(ok.Value, $"the settings in force here are the server's (source {ServerRules.Source})");
        yield return ServerLines("0", "D", "has " + ModInfo.Name + ": allowed.", count, lines);
        c.Check(count.Value >= 1 && lines.Value.Contains(MyName()),
            $"the server checked this player and logged \"{MyName()} ... has {ModInfo.Name}: allowed.\" ({count.Value}: {lines.Value})");
        yield return ServerLines("0", "W", "Refused ", count, lines);
        c.Check(count.Value == 0, $"the server refused nobody ({count.Value}: {lines.Value})");
        c.Report();
    }

    // ---------- moveset.mp.settings ----------

    private static IEnumerator RunMpSettings()
    {
        var p = LocalPlayer(MpSettingsName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(MpSettingsName);
        var rig = new Rig(MpSettingsName, p);
        try
        {
            var held = new Box<ItemDrop.ItemData>();
            yield return MpStart(MpSettingsName, rig, c, held);
            var plugin = PluginInstance();
            if (held.Value == null || plugin == null)
            {
                c.Report();
                yield break;
            }
            var shared = held.Value.m_shared.m_attack;
            var ok = new Box<bool>();
            var reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepRules, "", reply);
            c.Check(reply.Ok && reply.Detail.StartsWith(ServerRules.Current.Describe() + "|Own|", StringComparison.Ordinal),
                $"this game uses exactly the settings the server uses as its own (server: {reply.Detail})");
            var path = plugin.Config.ConfigFilePath;
            var fileBefore = File.ReadAllText(path);
            var ownBefore = Plugin.Jump.Damage.Value;

            // The server owner changes Jump attack DamageMultiplier.
            var mark = LogTap.Mark;
            var t0 = Time.realtimeSinceStartup;
            reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepSet, "Jump attack|DamageMultiplier=2.5", reply);
            yield return WaitReal(() => Near(ServerRules.Current.Jump.Damage, 2.5f), 6f, ok);
            c.Check(reply.Ok && ok.Value, $"the server's new Jump attack DamageMultiplier 2.5 arrives here ({Inv(Time.realtimeSinceStartup - t0)} s; {reply})");
            yield return new WaitForSecondsRealtime(0.4f);
            var info = LogTap.Since(mark, Inf, "Using the server's move settings: ");
            c.Check(info.Count == 1 && info[0].Text.Contains("jump attack (damage x2.5,"),
                $"Info \"Using the server's move settings: ...\" once, with the new value ({info.Count}: {(info.Count > 0 ? info[0].Text : "none")})");
            yield return Settle(rig, ServerRules.Current);
            mark = LogTap.Mark;
            var jump = new JumpRun();
            yield return JumpPress(rig, jump);
            var line = LogTap.First(mark, Dbg, "Jump attack: ");
            c.Check(jump.IsMove && Near(jump.Clone.m_damageMultiplier, shared.m_damageMultiplier * 2.5f) && ReferenceEquals(jump.Move.Rules, ServerRules.Current),
                "this player's jump attack hits harder: its damage multiplier is the server's 2.5");
            c.Check(line != null && line.Contains("damage x2.5,") && line.EndsWith("; server settings.", StringComparison.Ordinal),
                $"Debug \"Jump attack: ...; damage x2.5, ...; server settings.\" (got \"{line ?? "none"}\")");
            yield return AttackOver(p, jump.Started, 3f);
            c.Check(Near(Plugin.Jump.Damage.Value, ownBefore) && File.ReadAllText(path) == fileBefore,
                "this game's own setting and its config file are unchanged");

            // The server owner drags the slider: one settings message, about half a second after the last value.
            mark = LogTap.Mark;
            reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepDrag, "Jump attack|DamageMultiplier=2.6,2.7,2.8,2.9,3,3.1", reply);
            yield return WaitReal(() => Near(ServerRules.Current.Jump.Damage, 3.1f), 3f, ok);
            yield return new WaitForSecondsRealtime(0.5f);
            var drag = Fields(reply.Detail);
            info = LogTap.Since(mark, Inf, "Using the server's move settings: ");
            c.Check(reply.Ok && Int(drag, "sent") == 1 && Num(drag, "delay") >= 0.4f && Num(drag, "delay") <= 1.5f,
                $"six values in {Text(drag, "drag")} s on the server: one settings message, {Text(drag, "delay")} s after the last value (sent {Text(drag, "sent")})");
            c.Check(ok.Value && info.Count == 1 && info[0].Text.Contains("jump attack (damage x3.1,"),
                $"this game logs \"Using the server's move settings\" once, with the last value ({info.Count}: {(info.Count > 0 ? info[0].Text : "none")})");

            // The server sets Jump attack animations Swords = greatsword2.
            reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepSet, "Jump attack animations|Swords=greatsword2", reply);
            var swords = Families.Index(WeaponFamily.Swords);
            yield return WaitReal(() => ServerRules.Current.JumpTriggers[swords] == "greatsword2", 6f, ok);
            c.Check(reply.Ok && ok.Value, $"the server's Jump attack animations Swords = greatsword2 arrives here ({reply})");
            yield return Settle(rig, ServerRules.Current);
            mark = LogTap.Mark;
            jump = new JumpRun();
            yield return JumpPress(rig, jump);
            line = LogTap.First(mark, Dbg, "Jump attack: ");
            c.Check(jump.IsMove && jump.Move.Trigger == "greatsword2" && jump.EnteredAt >= 0f,
                $"this player's sword jump attack plays greatsword2 (got {jump.Move.Trigger})");
            c.Check(line != null && line.Contains(" plays greatsword2;") && line.EndsWith("; server settings.", StringComparison.Ordinal),
                $"Debug \"... plays greatsword2 ...; server settings.\" (got \"{line ?? "none"}\")");
            yield return AttackOver(p, jump.Started, 3f);

            yield return MpReset(MpSettingsName, ok);
            c.Check(ok.Value, "server settings back to the defaults, and in force here again");
            yield return Settle(rig);
            c.Report();
        }
        finally
        {
            FireServer(StepReset, "");
            rig.Restore();
        }
    }

    // ---------- moveset.mp.observer / server-log / early-cut ----------

    private static List<int> GapList(Dictionary<string, string> fields)
    {
        var list = new List<int>();
        foreach (var part in Text(fields, "gaps").Split(','))
        {
            if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            {
                list.Add(n);
            }
        }
        return list;
    }

    // One roll attack of this player (buffered press), with what this game saw.
    private static IEnumerator MpRoll(Rig rig, Checks c, string what, bool wantMove)
    {
        var p = rig.P;
        yield return Settle(rig, ServerRules.Current);
        var run = new RollRun();
        yield return Roll(rig, run);
        p.m_queuedAttackTimer = 0f;
        if (wantMove)
        {
            c.Check(run.Clone != null && run.Move.Kind == MoveKind.Roll && run.Move.Cut && run.GapTicks == 0, $"{what}: on this game the roll attack flows out of the roll");
        }
        else
        {
            c.Check(run.Clone == null && run.Started != null && run.GapTicks > 0, $"{what}: on this game a normal swing after the stand-up");
        }
        yield return AttackOver(p, run.Started, 3f);
    }

    private static IEnumerator RunMpObserver()
    {
        var p = LocalPlayer(MpObserverName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(MpObserverName);
        var rig = new Rig(MpObserverName, p);
        try
        {
            var held = new Box<ItemDrop.ItemData>();
            yield return MpStart(MpObserverName, rig, c, held);
            if (held.Value == null)
            {
                c.Report();
                yield break;
            }
            var reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepWatch, MyName() + "|40|near", reply);
            var start = Fields(reply.Detail);
            c.Check(reply.Ok && Int(start, "player") == 1,
                $"the dedicated server has its own copy of this player for the watch (by itself it has none: its own area is out of the world; the server step holds that area on this player, {Inv(Flat(p.transform.position))} m from the world centre, like a host standing next to them; {reply})");
            yield return MpRoll(rig, c, "SwordIron roll attack 1", true);
            yield return MpRoll(rig, c, "SwordIron roll attack 2", true);
            rig.TakeBack(held.Value);
            yield return Equip(rig, RowOf("Battleaxe"), held);
            c.Check(held.Value != null, "Battleaxe equipped");
            yield return MpRoll(rig, c, "Battleaxe, attack pressed during a roll", false);
            yield return new WaitForSecondsRealtime(1f);
            reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepWatched, "", reply);
            var seen = Fields(reply.Detail);
            var gaps = GapList(seen);
            // Own check: a server without graphics that never plays the copy's animations is a limit of this test
            // run, not the mod's doing. It then shows here, alone, before the checks that read that Animator.
            c.Check(reply.Ok && Int(seen, "ticks") > 0 && Int(seen, "rolls") >= 3,
                $"the server's copy of this player plays the three rolls (its Animator went into the roll {Text(seen, "rolls")} time(s) in {Text(seen, "ticks")} tick(s) of watching; "
                + "0 with ticks = a server without graphics does not play animations on its copy: then the three checks below cannot be made in this run)");
            c.Check(reply.Ok && Int(seen, "cuts") == 2 && Int(seen, "faded") == 2,
                $"the server's game cross-fades both sword roll attacks out of the roll on its copy of this player, with its Debug \"Cross-faded {MyName()}'s roll attack ...\" ({Text(seen, "cuts")} cross-fades, {Text(seen, "faded")} lines)");
            c.Check(gaps.Count >= 3 && gaps[0] >= 0 && gaps[0] <= 1 && gaps[1] >= 0 && gaps[1] <= 1,
                $"on the server's copy each sword swing grows out of the roll with no stand-up (idle ticks between roll and swing: {Text(seen, "gaps")})");
            c.Check(gaps.Count >= 3 && gaps[2] > 3,
                $"on the server's copy the battleaxe's normal swing keeps the stand-up (idle ticks: {Text(seen, "gaps")})");
            SelfTest.Note(MpObserverName, $"server saw: {reply.Detail}");
            yield return Settle(rig);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // Own small test: whatever the server does with other players' roll attacks, the mod logs no warning and no
    // error there. NOTE what it did (cross-fades, controller probe) so a "do nothing on a dedicated server" guard can
    // be decided on.
    private static IEnumerator RunMpServerLog()
    {
        var p = LocalPlayer(MpServerLogName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(MpServerLogName);
        var rig = new Rig(MpServerLogName, p);
        try
        {
            var held = new Box<ItemDrop.ItemData>();
            yield return MpStart(MpServerLogName, rig, c, held);
            if (held.Value == null)
            {
                c.Report();
                yield break;
            }
            var metres = Inv(Flat(p.transform.position));

            // Half 1, the dedicated server as it is: this player where the probe put them (230 m from the world
            // centre in run 1, 2.8 m in run 2: "player=0" both times). Its build keeps its own area out of the world
            // (HoldRef), so it has no copy of any player: near the world centre or far from it is the same to it.
            // Me check that (area found beyond the world's edge, 10.5 km) so one spot stands for both of M11's.
            // Nothing of the mod runs there on a roll attack, and nothing complains.
            var reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepWatch, MyName() + "|40", reply);
            var first = Fields(reply.Detail);
            var hadCopy = Int(first, "player") == 1;
            c.Check(reply.Ok, $"the server watches ({reply})");
            c.Check(reply.Ok && !hadCopy && Int(first, "area") == 0 && Num(first, "ref") > 20000f,
                $"the dedicated server keeps its own area out of the world ({Text(first, "ref")} m from the world centre) and has no copy of this player, who stands {metres} m from the world centre: "
                + $"near the centre or more than 200 m from it is the same to that server ({reply})");
            yield return MpRoll(rig, c, "no copy on the server, SwordIron roll attack", true);
            rig.TakeBack(held.Value);
            yield return Equip(rig, RowOf("AxeIron"), held);
            c.Check(held.Value != null, "AxeIron equipped");
            yield return MpRoll(rig, c, "no copy on the server, AxeIron roll attack", true);
            yield return new WaitForSecondsRealtime(1f);
            reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepWatched, "", reply);
            var seen = Fields(reply.Detail);
            if (!hadCopy)
            {
                c.Check(reply.Ok && Int(seen, "player") == 0 && Int(seen, "alerts") == 0 && Int(seen, "cuts") == 0 && Int(seen, "faded") == 0 && Int(seen, "found") == 0,
                    $"two roll attacks {metres} m from the world centre, no copy of this player on the server: no warning and no error from {ModInfo.Name} there, "
                    + $"no \"Cross-faded\" and no \"Found the animation state\" line ({reply})");
            }
            else
            {
                c.Check(reply.Ok && Int(seen, "alerts") == 0, $"two roll attacks: no warning and no error from {ModInfo.Name} on the server ({reply})");
            }

            // Half 2, harder than a dedicated server ever is: the server step holds the server's area on this player,
            // so it has its copy of them like a host (or a dedicated server with a mod that makes it simulate) and
            // the mod's code for other players' roll attacks runs there. Four weapon types.
            reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepWatch, MyName() + "|40|near", reply);
            c.Check(reply.Ok && Int(Fields(reply.Detail), "player") == 1,
                $"the dedicated server has its own copy of this player for the second half (its area held on this player by the server step; {reply})");
            var names = new[] { "SwordIron", "AxeIron", "KnifeCopper", "AtgeirIron" };
            foreach (var name in names)
            {
                rig.TakeBack(held.Value);
                yield return Equip(rig, RowOf(name), held);
                c.Check(held.Value != null, $"{name} equipped");
                yield return MpRoll(rig, c, $"with a copy on the server, {name} roll attack", true);
            }
            yield return new WaitForSecondsRealtime(1f);
            reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepWatched, "", reply);
            seen = Fields(reply.Detail);
            var arrived = Text(seen, "triggers").Split(',').Count(t => t.Length > 0);
            c.Check(reply.Ok && Int(seen, "player") == 1 && arrived == names.Length && Int(seen, "mod") == 1,
                $"the {names.Length} roll attack animations reached the server's copy of this player, where the mod's patch on other players' animation triggers is on and looks at each one "
                + $"(triggers: {Text(seen, "triggers")}; patch on: {Text(seen, "mod")})");
            c.Check(reply.Ok && Int(seen, "alerts") == 0,
                $"four roll attacks with four weapon types, seen by the server's game: no warning and no error from {ModInfo.Name} on the server ({Text(seen, "alerts")}: {Text(seen, "first")})");
            SelfTest.Note(MpServerLogName, $"with its copy of this player the server saw {Text(seen, "rolls")} roll(s) on that copy's Animator and cross-faded {Text(seen, "cuts")} roll attack(s) "
                                           + $"(\"Cross-faded\" lines: {Text(seen, "faded")}), \"Found the animation state\" lines: {Text(seen, "found")}, last controller probe "
                                           + $"{Text(seen, "probeMs")} ms. A dedicated server by itself has no copy of any player, so it never does this work.");
            yield return Settle(rig);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // Own small test (network timing): FlowStart 0 on the server. When the roll attack's trigger reaches the server's
    // game, that game must already see this player as no longer invulnerable, and during the swing too.
    private static IEnumerator RunMpEarlyCut()
    {
        var p = LocalPlayer(MpEarlyCutName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(MpEarlyCutName);
        var rig = new Rig(MpEarlyCutName, p);
        try
        {
            var held = new Box<ItemDrop.ItemData>();
            yield return MpStart(MpEarlyCutName, rig, c, held);
            if (held.Value == null)
            {
                c.Report();
                yield break;
            }
            var ok = new Box<bool>();
            var reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepSet, "Roll attack|FlowStart=0", reply);
            yield return WaitReal(() => ServerRules.Current.FlowStart <= 0.001f, 6f, ok);
            c.Check(reply.Ok && ok.Value, $"the server's Roll attack FlowStart 0 arrives here ({reply})");
            reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepWatch, MyName() + "|30|near", reply);
            c.Check(reply.Ok && Int(Fields(reply.Detail), "player") == 1,
                $"the dedicated server has its own copy of this player for the watch (its area held on this player by the server step, like a host standing next to them; {reply})");
            for (var i = 0; i < 3; i++)
            {
                yield return Settle(rig, ServerRules.Current);
                var run = new RollRun();
                yield return Roll(rig, run);
                p.m_queuedAttackTimer = 0f;
                var off = run.IframesEnd >= 0f ? run.IframesEnd - run.RollStart : -1f;
                c.Check(run.Clone != null && run.Move.Cut && off >= 0f && run.Cut - off >= MoveTracker.IframeMargin - 0.5f * Time.fixedDeltaTime,
                    $"roll attack {i + 1}: on this game it cuts in {S(run.Cut - off)} s after the roll's invulnerability ended (at least {F(MoveTracker.IframeMargin)} s)");
                yield return AttackOver(p, run.Started, 3f);
            }
            yield return new WaitForSecondsRealtime(1f);
            reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepWatched, "", reply);
            var seen = Fields(reply.Detail);
            var arrived = Text(seen, "triggers").Split(',').Count(t => t.Length > 0);
            c.Check(reply.Ok && arrived == 3, $"the three roll attack triggers reached the server's game ({Text(seen, "triggers")})");
            // Without this the next check could pass on a game that never sees the invulnerability at all.
            c.Check(reply.Ok && Int(seen, "inv") > 0,
                $"the server's game does see this player invulnerable during the rolls ({Text(seen, "inv")} tick(s) of {Text(seen, "ticks")} watched)");
            c.Check(arrived > 0 && Int(seen, "invAtTrigger") == 0 && Int(seen, "invInSwing") == 0 && Int(seen, "invAtCut") == 0,
                $"the server's game never sees this player invulnerable when a roll attack's trigger arrives, nor in the half second after ({Text(seen, "invAtTrigger")} at arrival, {Text(seen, "invInSwing")} tick(s) after)");
            SelfTest.Note(MpEarlyCutName, $"server saw: {reply.Detail}");
            yield return MpReset(MpEarlyCutName, ok);
            c.Check(ok.Value, "FlowStart back to its default on the server and here");
            yield return Settle(rig);
            c.Report();
        }
        finally
        {
            FireServer(StepReset, "");
            rig.Restore();
        }
    }

    // ---------- moveset.mp.client-toggle ----------

    private static IEnumerator RunMpClientToggle()
    {
        var p = LocalPlayer(MpClientToggleName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(MpClientToggleName);
        var rig = new Rig(MpClientToggleName, p);
        var view = FeatureRegistry.Find(ModInfo.Guid);
        var enabled = view != null ? view.Value.Enabled : null;
        var allowOn = false;
        try
        {
            var held = new Box<ItemDrop.ItemData>();
            yield return MpStart(MpClientToggleName, rig, c, held);
            if (held.Value == null || enabled == null)
            {
                c.Check(enabled != null, "the mod's Enabled setting found");
                c.Report();
                yield break;
            }
            var shared = held.Value.m_shared.m_attack;
            var me = MyName();
            var ok = new Box<bool>();
            var count = new Box<int>();
            var lines = new Box<string>();
            var reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepMark, "", reply);
            var from = reply.Detail;
            reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepSet, GeneralSection + "|" + AllowKey + "=true", reply);
            allowOn = reply.Answered && reply.Ok;
            c.Check(allowOn, $"the server sets AllowPlayersWithoutMod = true ({reply})");
            if (!allowOn)
            {
                c.Report(); // never turn the mod off on a server that would refuse this player
                yield break;
            }

            // Off, through the real Enabled setting (this run's own throwaway config file).
            var offAt = Time.realtimeSinceStartup;
            enabled.Value = false;
            c.Check(StateNow() == "Disabled" && StatusNow() == "Off (disabled in settings)." && PatchedCount() == 0,
                $"Enabled = false: Status \"Off (disabled in settings).\", no combat patch left (state {StateNow()}, \"{StatusNow()}\", {PatchedCount()} patched)");
            yield return VanillaCombat(rig, c, "mod turned off on this game", shared, true);
            var wait = 7.5f - (Time.realtimeSinceStartup - offAt);
            if (wait > 0f)
            {
                yield return new WaitForSecondsRealtime(wait);
            }
            c.Check(Connected(), "not refused: still connected 7.5 s after turning the mod off (the server's check comes after 1 s, its cut 4 s later)");
            yield return ServerLines(from, "I", me + " turned " + ModInfo.Name + " off on their game.", count, lines);
            c.Check(count.Value == 1, $"the server logs \"{me} turned {ModInfo.Name} off on their game.\" ({count.Value})");
            yield return ServerLines(from, "W", "Not refusing ", count, lines);
            c.Check(count.Value >= 1 && lines.Value.Contains(me) && lines.Value.Contains("their game has the mod turned off, but AllowPlayersWithoutMod is on."),
                $"the server warns \"Not refusing {me} ...: their game has the mod turned off, but AllowPlayersWithoutMod is on. ...\" ({count.Value}: {lines.Value})");
            yield return ServerLines(from, "W", "Refused ", count, lines);
            c.Check(count.Value == 0, $"the server refused nobody ({count.Value}: {lines.Value})");

            // On again.
            var mark = LogTap.Mark;
            enabled.Value = true;
            c.Check(ActiveNow() && PatchedCount() == 5, $"Enabled = true: active again at once ({StateNow()}), combat patches back");
            yield return WaitReal(() => ServerRules.Source == RulesSource.Server, 6f, ok);
            yield return new WaitForSecondsRealtime(0.5f);
            c.Check(ok.Value && LogTap.Count(mark, Inf, "Using the server's move settings: ") == 1,
                $"this game logs \"Using the server's move settings\" again, once ({LogTap.Count(mark, Inf, "Using the server's move settings: ")})");
            yield return ServerLines(from, "I", me + " turned " + ModInfo.Name + " on on their game.", count, lines);
            c.Check(count.Value == 1, $"the server logs \"{me} turned {ModInfo.Name} on on their game.\" ({count.Value})");
            yield return Settle(rig, ServerRules.Current);
            var jump = new JumpRun();
            yield return JumpPress(rig, jump);
            c.Check(jump.IsMove && jump.Move.Kind == MoveKind.Jump, "jump attacks are back");
            yield return AttackOver(p, jump.Started, 3f);
            yield return Settle(rig, ServerRules.Current);
            var roll = new RollRun();
            yield return Roll(rig, roll);
            p.m_queuedAttackTimer = 0f;
            c.Check(roll.Clone != null && roll.Move.Kind == MoveKind.Roll, "roll attacks are back");
            yield return AttackOver(p, roll.Started, 3f);

            // The server refuses again: this player, compatible, stays.
            reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepMark, "", reply);
            from = reply.Detail;
            reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepSet, GeneralSection + "|" + AllowKey + "=false", reply);
            allowOn = !(reply.Answered && reply.Ok);
            c.Check(!allowOn, $"the server sets AllowPlayersWithoutMod back to false ({reply})");
            yield return new WaitForSecondsRealtime(6.5f);
            yield return ServerLines(from, "D", "has " + ModInfo.Name + ": allowed.", count, lines);
            c.Check(Connected() && count.Value >= 1 && lines.Value.Contains(me),
                $"switched back to refusing, the server checks every player again and this one stays (\"... has {ModInfo.Name}: allowed.\" {count.Value})");
            yield return Settle(rig);
            c.Report();
        }
        finally
        {
            if (enabled != null && !enabled.Value)
            {
                enabled.Value = true; // the server hears "on" before the step below
            }
            if (allowOn)
            {
                FireServer(StepSet, GeneralSection + "|" + AllowKey + "=false"); // done only when nobody would be refused
            }
            rig.Restore();
        }
    }

    // ---------- moveset.mp.server-toggle ----------

    private static IEnumerator RunMpServerToggle()
    {
        var p = LocalPlayer(MpServerToggleName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(MpServerToggleName);
        var rig = new Rig(MpServerToggleName, p);
        var serverOff = false;
        try
        {
            var held = new Box<ItemDrop.ItemData>();
            yield return MpStart(MpServerToggleName, rig, c, held);
            if (held.Value == null)
            {
                c.Report();
                yield break;
            }
            var shared = held.Value.m_shared.m_attack;
            var me = MyName();
            var ok = new Box<bool>();
            var count = new Box<int>();
            var lines = new Box<string>();
            var reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepMark, "", reply);
            var from = reply.Detail;
            var mark = LogTap.Mark;

            // The server owner turns the mod off.
            reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(ProbeSetEnabled, ModInfo.Guid + "=off", reply);
            serverOff = reply.Answered && reply.Ok;
            c.Check(serverOff, $"the server turns the mod off ({reply})");
            yield return WaitReal(() => StateNow() == "ServerMissing", 10f, ok);
            c.Check(ok.Value && StatusNow().StartsWith("Inactive: the server has this mod turned off", StringComparison.Ordinal) && PatchedCount() == 0,
                $"this game shows \"Inactive: the server has this mod turned off ...\" and has no combat patch left (state {StateNow()}, \"{StatusNow()}\")");
            yield return VanillaCombat(rig, c, "server has the mod off", shared, true);

            // On again: the moves come back once the server's settings are here.
            reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(ProbeSetEnabled, ModInfo.Guid + "=on", reply);
            serverOff = !(reply.Answered && reply.Ok);
            c.Check(!serverOff, $"the server turns the mod back on ({reply})");
            yield return WaitReal(() => ActiveNow(), 10f, ok);
            c.Check(ok.Value && PatchedCount() == 5, $"this game's copy is active again ({StateNow()})");
            yield return WaitReal(() => ServerRules.Source == RulesSource.Server, 6f, ok);
            yield return new WaitForSecondsRealtime(0.5f);
            c.Check(ok.Value && LogTap.Count(mark, Inf, "Using the server's move settings: ") == 1,
                $"the server's settings arrive again (\"Using the server's move settings\" {LogTap.Count(mark, Inf, "Using the server's move settings: ")} time(s) since the server went off)");
            yield return Settle(rig, ServerRules.Current);
            var jump = new JumpRun();
            yield return JumpPress(rig, jump);
            c.Check(jump.IsMove && jump.Move.Kind == MoveKind.Jump, "jump attacks are back");
            yield return AttackOver(p, jump.Started, 3f);

            // Every connected player is checked again (a player without the mod would be refused here).
            yield return new WaitForSecondsRealtime(2f);
            yield return ServerLines(from, "D", "has " + ModInfo.Name + ": allowed.", count, lines);
            c.Check(count.Value >= 1 && lines.Value.Contains(me),
                $"turned on again, the server checks every connected player once more (\"{me} ... has {ModInfo.Name}: allowed.\" {count.Value})");
            yield return new WaitForSecondsRealtime(4.5f);
            c.Check(Connected(), "this player, compatible, stays connected");
            yield return Settle(rig);
            c.Report();
        }
        finally
        {
            if (serverOff)
            {
                FireServer(ProbeSetEnabled, ModInfo.Guid + "=on");
            }
            rig.Restore();
        }
    }

    // ---------- moveset.mp.version ----------

    private static IEnumerator RunMpVersion()
    {
        var p = LocalPlayer(MpVersionName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(MpVersionName);
        var rig = new Rig(MpVersionName, p);
        var gate = typeof(NetworkGate);
        var netField = gate.GetField("_serverNetVersion", BindingFlags.NonPublic | BindingFlags.Static);
        var versionField = gate.GetField("_serverVersion", BindingFlags.NonPublic | BindingFlags.Static);
        object netWas = null;
        object versionWas = null;
        var faked = false;
        try
        {
            var held = new Box<ItemDrop.ItemData>();
            yield return MpStart(MpVersionName, rig, c, held);
            if (held.Value == null)
            {
                c.Report();
                yield break;
            }
            var shared = held.Value.m_shared.m_attack;
            var ok = new Box<bool>();
            var other = ModInfo.NetworkVersion + 1;
            SelfTest.Note(MpVersionName, "no second build of the mod exists in this run: the other network version is written into the framework's "
                                         + "handshake state for the length of the checks (server: this player's hello; this game: the server's answer)");

            // Server side: its verdict for a player with network version +1, with AllowPlayersWithoutMod on.
            var reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepVersion, MyName() + "|" + other, reply);
            var reason = $"has another version of the mod (network version {other}, the server has {ModInfo.NetworkVersion})";
            c.Check(reply.Ok && reply.Detail.Contains(MyName()) && reply.Detail.Contains("their game " + reason + ", but AllowPlayersWithoutMod is on."),
                $"server, AllowPlayersWithoutMod on: Warning \"Not refusing {MyName()} ...: their game {reason}, but AllowPlayersWithoutMod is on. ...\" (got {reply})");
            yield return new WaitForSecondsRealtime(6f);
            c.Check(Connected(), "still connected after the server put the real hello back and went back to refusing");

            // This game: the server "answered" with another network version.
            c.Check(netField != null && versionField != null, "the framework's handshake fields are found (test set-up)");
            if (netField != null && versionField != null)
            {
                netWas = netField.GetValue(null);
                versionWas = versionField.GetValue(null);
                faked = true;
                netField.SetValue(null, other);
                versionField.SetValue(null, "9.9.9");
                FeatureRegistry.RefreshAll();
                c.Check(StateNow() == "ServerMismatch"
                        && StatusNow() == $"Inactive: the server has version 9.9.9, you have {ModInfo.Version}, and they cannot talk to each other. Use matching versions."
                        && PatchedCount() == 0,
                    $"this game shows the version mismatch and has no combat patch left (state {StateNow()}, \"{StatusNow()}\")");
                yield return VanillaCombat(rig, c, "server with another network version", shared, false);
                netField.SetValue(null, netWas);
                versionField.SetValue(null, versionWas);
                faked = false;
                FeatureRegistry.RefreshAll();
                yield return WaitReal(() => ActiveNow() && ServerRules.Source == RulesSource.Server, 8f, ok);
                c.Check(ok.Value && PatchedCount() == 5, $"real versions back: active again with the server's settings ({StateNow()}, {ServerRules.Source})");
            }
            yield return Settle(rig);
            c.Report();
        }
        finally
        {
            if (faked)
            {
                netField.SetValue(null, netWas);
                versionField.SetValue(null, versionWas);
                FeatureRegistry.RefreshAll();
            }
            rig.Restore();
        }
    }

    // ---------- moveset.mp.vanilla-server ----------

    // Scenario "vanilla-server": the server has no MC mod, so me inactive. This test was registered at start
    // (RegisterMpInactive) and uses no patch of mine.
    private static IEnumerator RunMpNoServer()
    {
        var p = LocalPlayer(MpNoServerName);
        if (p == null)
        {
            yield break;
        }
        var c = new Checks(MpNoServerName);
        var rig = new Rig(MpNoServerName, p);
        try
        {
            c.Check(Connected(), "connected to a dedicated server as a client");
            c.Check(StateNow() == "ServerMissing" && StatusNow().StartsWith("Inactive: the server does not have this mod.", StringComparison.Ordinal),
                $"Status reads \"Inactive: the server does not have this mod. ...\" (state {StateNow()}, \"{StatusNow()}\")");
            c.Check(PatchedCount() == 0, $"none of the mod's combat patches is on ({PatchedCount()})");
            var held = new Box<ItemDrop.ItemData>();
            yield return Equip(rig, SwordRow, held);
            c.Check(held.Value != null, "SwordIron equipped");
            if (held.Value != null)
            {
                yield return VanillaCombat(rig, c, "server without the mod", held.Value.m_shared.m_attack, true);
            }
            yield return WaitIdle(p, 4f);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }
}
#endif
