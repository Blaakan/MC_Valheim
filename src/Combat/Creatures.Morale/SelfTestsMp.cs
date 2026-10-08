#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.CreaturesMoraleMod;

// Debug build only. Multiplayer self tests (tools/Test-Multiplayer.ps1, scenario "modded": one client joined to a real
// dedicated server, both with every MC mod). Client tests + their server halves (TESTING.md items in brackets):
//   morale.mp.rules        the server's settings reach the client and are used there; a change on the server reaches it
//                          about a second later with the log line; progress messages stay the client's own (M06)
//   morale.mp.shared       what the server holds for the other players' games: this player's standing, a creature's
//                          alert while it runs and when it calms, its provocation record, its rout keys; a leader's death
//                          is sent as a Rout message through the server (M01, M02, M03, M05, M10, M14: the parts one
//                          client and the server can show)
//   morale.mp.off-allowed  the client turns the mod off while the server lets such players stay: both logs, standing
//                          withdrawn on the server, creatures attack that player; back on: afraid again (M08)
//   morale.mp.server-off   the server turns the mod off: the client's copy follows, creatures notice the player again,
//                          nobody is refused; back on: afraid again (M11)
// The other player of these items is stood in for by the server (its ZDO copies, its log, what it relays): no second
// client exists in the run. Server settings are changed on the server only (its config is a throwaway copy).
internal static partial class SelfTests
{
    private const string MpRulesName = "morale.mp.rules";
    private const string MpSharedName = "morale.mp.shared";
    private const string MpOffAllowedName = "morale.mp.off-allowed";
    private const string MpServerOffName = "morale.mp.server-off";

    private const string SrvSet = "morale.mp.srv.set";
    private const string SrvRules = "morale.mp.srv.rules";
    private const string SrvZdo = "morale.mp.srv.zdo";
    private const string SrvRout = "morale.mp.srv.rout";
    private const string SrvAllow = "morale.mp.srv.allow";
    private const string SrvPeer = "morale.mp.srv.peer";
    private const string ProbeSetEnabled = "probe.set-enabled"; // the server probe's own step: "<guid>=on|off"

    private static void RegisterMultiplayer()
    {
        SelfTest.RegisterMultiplayer(MpRulesName, SelfTest.Modded, RunMpRules);
        SelfTest.RegisterMultiplayer(MpSharedName, SelfTest.Modded, RunMpShared);
        SelfTest.RegisterMultiplayer(MpOffAllowedName, SelfTest.Modded, RunMpOffAllowed);
        SelfTest.RegisterMultiplayer(MpServerOffName, SelfTest.Modded, RunMpServerOff);
        SelfTest.RegisterServerStep(SrvSet, ServerSet);
        SelfTest.RegisterServerStep(SrvRules, ServerRulesStep);
        SelfTest.RegisterServerStep(SrvZdo, ServerZdoStep);
        SelfTest.RegisterServerStep(SrvRout, ServerRoutStep);
        SelfTest.RegisterServerStep(SrvAllow, ServerAllowStep);
        SelfTest.RegisterServerStep(SrvPeer, ServerPeerStep);
    }

    private static void UnregisterMultiplayer()
    {
        foreach (var name in new[] { MpRulesName, MpSharedName, MpOffAllowedName, MpServerOffName })
        {
            SelfTest.UnregisterMultiplayer(name);
        }
        foreach (var step in new[] { SrvSet, SrvRules, SrvZdo, SrvRout, SrvAllow, SrvPeer })
        {
            SelfTest.UnregisterServerStep(step);
        }
    }

    // ================================================================ server halves

    private static string Inv(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Vec(Vector3 v) => Inv(v.x) + "," + Inv(v.y) + "," + Inv(v.z);

    // "FearRange=20;KillSteps=7" = the server owner changing those settings (real config entries of the server: the
    // edit settles, then the server sends its rules to every player). "reset" = back to the defaults.
    private static IEnumerator ServerSet(string arg, object[] reply)
    {
        yield return null;
        if (arg == "reset")
        {
            Plugin.FearRange.Value = (float)Plugin.FearRange.DefaultValue;
            Plugin.KillSteps.Value = (string)Plugin.KillSteps.DefaultValue;
            Plugin.RoutSeconds.Value = (float)Plugin.RoutSeconds.DefaultValue;
            Plugin.ProvokedSeconds.Value = (float)Plugin.ProvokedSeconds.DefaultValue;
            Plugin.HomeLists[0].Value = (string)Plugin.HomeLists[0].DefaultValue;
            SelfTest.Answer(reply, true, "server settings back to the defaults");
            yield break;
        }
        foreach (var part in (arg ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var key = eq > 0 ? part.Substring(0, eq).Trim() : "";
            var value = eq > 0 ? part.Substring(eq + 1).Trim() : "";
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number);
            switch (key)
            {
                case "FearRange":
                    Plugin.FearRange.Value = number;
                    break;
                case "RoutSeconds":
                    Plugin.RoutSeconds.Value = number;
                    break;
                case "ProvokedSeconds":
                    Plugin.ProvokedSeconds.Value = number;
                    break;
                case "KillSteps":
                    Plugin.KillSteps.Value = value;
                    break;
                case "Meadows": // the first home biome list, whole text
                    Plugin.HomeLists[0].Value = value;
                    break;
                default:
                    SelfTest.Answer(reply, false, $"unknown server setting '{key}'");
                    yield break;
            }
        }
        SelfTest.Answer(reply, true, "server settings changed: " + arg);
    }

    private static IEnumerator ServerRulesStep(string arg, object[] reply)
    {
        yield return null;
        var rules = ServerRules.Current;
        SelfTest.Answer(reply, rules != null, rules != null
            ? $"fear={Inv(rules.FearRange)}|rout={Inv(rules.RoutSeconds)}|kill={rules.KillSteps}|desc={rules.Describe()}"
            : "no rules on the server");
    }

    // arg "<user id>:<id>" of a ZDO: what the server holds for it (what it sends to every other player's game).
    private static IEnumerator ServerZdoStep(string arg, object[] reply)
    {
        yield return null;
        var parts = (arg ?? "").Split(':');
        var man = ZDOMan.instance;
        if (parts.Length != 2 || man == null || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var user)
            || !uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            SelfTest.Answer(reply, false, $"bad ZDO id '{arg}'");
            yield break;
        }
        var zdo = man.GetZDO(new ZDOID(user, id));
        if (zdo == null)
        {
            SelfTest.Answer(reply, true, "found=0");
            yield break;
        }
        SelfTest.Answer(reply, true,
            $"found=1|owner={zdo.GetOwner().ToString(CultureInfo.InvariantCulture)}|alert={(zdo.GetBool(ZDOVars.s_alert) ? 1 : 0)}"
            + $"|rout={CreatureKeys.GetRoutUntil(zdo).ToString(CultureInfo.InvariantCulture)}|from={Vec(CreatureKeys.GetRoutFrom(zdo, Vector3.zero))}"
            + $"|prov={Hex(zdo.GetByteArray(CreatureKeys.Provokers))}|standing={Hex(zdo.GetByteArray(CreatureKeys.Standing))}");
    }

    // Rout messages the server handled so far, and the last one.
    private static IEnumerator ServerRoutStep(string arg, object[] reply)
    {
        yield return null;
        SelfTest.Answer(reply, true, $"count={Rout.TestReceived}|pos={Vec(Rout.TestLastPosition)}|leader={Rout.TestLastLeader}");
    }

    // arg seconds > 0: for that long the server lets players stay whose game cannot play by its rules (as with
    // AllowPlayersWithoutMod on) and records its own log lines. "0" = over.
    private static IEnumerator ServerAllowStep(string arg, object[] reply)
    {
        yield return null;
        float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds);
        if (seconds > 0f)
        {
            PlayerCheck.TestAllowUntil = Time.realtimeSinceStartup + seconds;
            Watch.Begin();
            SelfTest.Answer(reply, true, $"players that are not compatible may stay for {Inv(seconds)} s");
        }
        else
        {
            PlayerCheck.TestAllowUntil = -1f;
            Watch.End();
            SelfTest.Answer(reply, true, "back to the server's AllowPlayersWithoutMod setting");
        }
    }

    // The (one) connected player as the server sees it, and what the server logged about it since ServerAllowStep.
    private static IEnumerator ServerPeerStep(string arg, object[] reply)
    {
        yield return null;
        var net = ZNet.instance;
        var peers = net != null ? net.GetPeers() : null;
        if (peers == null || peers.Count == 0 || peers[0] == null)
        {
            SelfTest.Answer(reply, false, "no player connected to the server");
            yield break;
        }
        var peer = peers[0];
        var ready = NetworkGate.PeerReady(peer);
        var man = ZDOMan.instance;
        var character = man != null ? man.GetZDO(peer.m_characterID) : null;
        SelfTest.Answer(reply, true,
            $"peers={peers.Count}|hasMod={(NetworkGate.PeerHasMod(peer) ? 1 : 0)}|ready={(ready.HasValue ? (ready.Value ? "1" : "0") : "?")}"
            + $"|compatible={(NetworkGate.PeerCompatible(peer) ? 1 : 0)}|problem={NetworkGate.PeerProblem(peer) ?? ""}"
            + $"|kicked={(ZNet.PeersToDisconnectAfterKick.ContainsKey(peer) ? 1 : 0)}"
            + $"|turnedOff={Watch.Count($" turned {ModInfo.Name} off on their game.")}|turnedOn={Watch.Count($" turned {ModInfo.Name} on on their game.")}"
            + $"|allowed={Watch.Count("AllowPlayersWithoutMod is on, so they may play", 0, LogLevel.Warning)}|refused={Watch.Count("Refused ")}"
            + $"|standing={Hex(character != null ? character.GetByteArray(CreatureKeys.Standing) : null)}");
    }

    // ================================================================ client helpers

    private sealed class FieldsRef
    {
        internal Dictionary<string, string> Map = new Dictionary<string, string>();

        internal string this[string key] => Map.TryGetValue(key, out var value) ? value : "";
    }

    private static Dictionary<string, string> Fields(string detail)
    {
        var map = new Dictionary<string, string>();
        foreach (var part in (detail ?? "").Split('|'))
        {
            var eq = part.IndexOf('=');
            if (eq > 0)
            {
                map[part.Substring(0, eq)] = part.Substring(eq + 1);
            }
        }
        return map;
    }

    private static string IdOf(ZNetView view)
    {
        var id = view.GetZDO().m_uid;
        return id.UserID.ToString(CultureInfo.InvariantCulture) + ":" + id.ID.ToString(CultureInfo.InvariantCulture);
    }

    // Ask the server step again and again until its answer fits (the ZDO copies travel a few frames behind).
    private static IEnumerator AskUntil(string step, string arg, Func<FieldsRef, bool> fits, float timeout, Action eachFrame, FieldsRef fields, Waiter w)
    {
        var start = Time.time;
        w.Met = false;
        while (true)
        {
            var reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(step, arg, reply);
            fields.Map = reply.Answered && reply.Ok ? Fields(reply.Detail) : new Dictionary<string, string> { ["error"] = reply.ToString() };
            if (reply.Answered && reply.Ok && fits(fields))
            {
                w.Met = true;
                break;
            }
            if (Time.time - start >= timeout || !reply.Answered)
            {
                break;
            }
            var next = Time.time + 0.25f;
            while (Time.time < next)
            {
                eachFrame?.Invoke();
                yield return null;
            }
        }
        w.Took = Time.time - start;
    }

    private static bool ConnectedClient()
    {
        var net = ZNet.instance;
        return net != null && !net.IsServer() && Player.m_localPlayer != null && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected;
    }

    // Start of every multiplayer test: a client of the dedicated server, hooks off, server settings at their defaults.
    private static IEnumerator MpBegin(Checks c, string test, Waiter w)
    {
        w.Met = false;
        if (!c.Check(SelfTest.IsMultiplayerRun && ConnectedClient(), "this test needs a client connected to the dedicated server of a multiplayer run"))
        {
            yield break;
        }
        ResetHooks();
        var reply = new SelfTest.ServerReply();
        yield return SelfTest.CallServer(SrvSet, "reset", reply);
        if (!c.Check(reply.Answered && reply.Ok, $"the server half put the server's settings back to their defaults ({reply})"))
        {
            yield break;
        }
        yield return Until(() => !ServerRules.Pending && ServerRules.UsingServer && Mathf.Approximately(ServerRules.Current.FearRange, 12f)
                                 && Mathf.Approximately(ServerRules.Current.RoutSeconds, 15f), 6f, null, w);
        c.Check(w.Met, $"{test} setup: the client plays by the server's rules, at their defaults (pending {ServerRules.Pending}, using the server's {ServerRules.UsingServer})");
    }

    // ================================================================ morale.mp.rules

    private static IEnumerator RunMpRules()
    {
        var c = new Checks(MpRulesName);
        Stage s = null;
        var w = new Waiter();
        var reply = new SelfTest.ServerReply();
        try
        {
            yield return MpBegin(c, MpRulesName, w);
            if (!w.Met)
            {
                c.Report();
                yield break;
            }
            s = Stage.Begin(MpRulesName, c);
            var hud = MessageHud.instance;
            if (s == null || !c.Check(hud != null && !Hud.IsUserHidden(), "the HUD is hidden or missing: messages cannot be checked"))
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;

            yield return SelfTest.CallServer(SrvRules, "", reply);
            var server = Fields(reply.Detail);
            c.Check(reply.Answered && reply.Ok && server.TryGetValue("desc", out var described) && described == ServerRules.Current.Describe(),
                $"M06: the rules in force on the client are the server's own (server: {reply.Detail}; client: {ServerRules.Current.Describe()})");

            // The server owner changes two settings.
            var mark = Watch.Mark();
            yield return SelfTest.CallServer(SrvSet, "FearRange=20;KillSteps=7", reply);
            c.Check(reply.Answered && reply.Ok, $"the server changed FearRange and KillSteps ({reply})");
            yield return Until(() => Mathf.Approximately(ServerRules.Current.FearRange, 20f), 6f, null, w);
            var line = Watch.Last("Using the server's creature rules:", mark);
            c.Check(w.Met && Watch.Count("Using the server's creature rules:", mark, LogLevel.Info) == 1 && line.Contains("fear range 20 m") && line.Contains("kill steps 7 ("),
                $"M06: after the server's settings change the client logs \"Using the server's creature rules: ...\" once more, with \"fear range 20 m\" "
                + $"(after {F(w.Took)} s; line: \"{line}\")");
            var steps = ServerRules.Current.KillStepValues;
            c.Check(Plugin.KillSteps.Value == MoraleRules.DefaultKillSteps && Mathf.Approximately(Plugin.FearRange.Value, 12f) && steps.Length == 1 && steps[0] == 7,
                "M06: the client's own settings (kill steps 100, 400; fear range 12) are untouched and not used: the server's kill step 7 is");

            // One more Greyling kill reaches the server's kill step: bonus, message (the client's own choice), fear.
            var greyling = TokenOf("Greyling");
            var text = KillStepText(greyling, 7);
            Func<int> bonus = () =>
            {
                var standing = StandingCache.Get(player);
                return standing != null ? standing.BonusFor(Hash(greyling)) : -1;
            };
            Standing.TestMessages = true;
            var before = new Standing.Override(2);
            before.Kills[greyling] = 6f;
            Standing.TestOverride = before;
            var shown = Shown(text);
            c.Check(bonus() == 0, $"M06 setup: rank 2 and 6 Greyling kills: no bonus yet (bonus {bonus()})");
            var after = new Standing.Override(2);
            after.Kills[greyling] = 7f;
            Standing.TestKill(after);
            c.Check(bonus() == 1 && Shown(text) == shown + 1 && InCorner(text),
                $"M06: with the server's KillSteps the 7th Greyling kill gives the bonus and its corner message (bonus {bonus()}, shown {Shown(text) - shown} time(s), "
                + $"in the corner {InCorner(text)})");
            var spot = s.Spot(here, s.Forward, 16f, true);
            var witness = SpawnMeadows(s, c, "Greyling", spot, here - spot);
            if (witness != null)
            {
                yield return Until(() => FearOf(witness) == player && witness.m_targetCreature == null, 3.5f, () =>
                {
                    s.Noise();
                    Stage.Place(witness.m_character, spot, player.transform.position - spot);
                }, w);
                c.Check(w.Met && Attitudes.Judge(witness, player) == Attitude.Afraid,
                    $"M06: a Greyling 16 m away is now afraid of this player and runs: the server's kill step and its fear range of 20 m both apply "
                    + $"(Judge {Attitudes.Judge(witness, player)}, runs from {Who(FearOf(witness))})");
                s.Destroy(witness);
            }
            // The player's own ShowProgressMessages still decides about messages.
            Standing.TestMessages = false;
            Standing.TestOverride = before;
            shown = Shown(text);
            Standing.TestKill(after);
            c.Check(bonus() == 1 && Shown(text) == shown, "M06: with the player's own progress messages off the same kill step shows no message (the bonus still counts)");

            // A home biome list edit on the server, twice (the same names in another order). The second one changes
            // nothing the summary shows (custom lists, same count) and is logged again all the same.
            var meadows = MoraleRules.SplitNames(MoraleRules.DefaultHomeLists[0]);
            if (c.Check(meadows.Count >= 3, $"M06: the default Meadows list has three names to reorder (it has {meadows.Count})"))
            {
                var lines = new string[2];
                var logged = true;
                for (var i = 0; i < 2; i++)
                {
                    var moved = meadows[i];
                    meadows[i] = meadows[i + 1];
                    meadows[i + 1] = moved;
                    mark = Watch.Mark();
                    yield return SelfTest.CallServer(SrvSet, "Meadows=" + string.Join(", ", meadows.ToArray()), reply);
                    yield return Until(() => Watch.Count("Using the server's creature rules:", mark, LogLevel.Info) > 0, 6f, null, w);
                    yield return Wait(0.5f, null);
                    lines[i] = Watch.Last("Using the server's creature rules:", mark);
                    logged &= reply.Answered && reply.Ok && w.Met && Watch.Count("Using the server's creature rules:", mark, LogLevel.Info) == 1;
                }
                c.Check(logged && lines[0] == lines[1] && lines[0].Contains("custom home biomes"),
                    $"M06: a home biome list edit on the server logs \"Using the server's creature rules: ...\" again, also the second edit, which the summary "
                    + $"does not show (first: \"{lines[0]}\"; second: \"{lines[1]}\")");
            }

            // The server puts its settings back: the client follows again.
            mark = Watch.Mark();
            yield return SelfTest.CallServer(SrvSet, "reset", reply);
            yield return Until(() => Mathf.Approximately(ServerRules.Current.FearRange, 12f), 6f, null, w);
            line = Watch.Last("Using the server's creature rules:", mark);
            c.Check(reply.Answered && reply.Ok && w.Met && Watch.Count("Using the server's creature rules:", mark, LogLevel.Info) == 1 && line.Contains("fear range 12 m"),
                $"M06: the server's next change (back to the defaults) is logged and used the same way (line: \"{line}\")");
            c.Report();
        }
        finally
        {
            Finish(s);
        }
    }

    // ================================================================ morale.mp.shared

    private static IEnumerator RunMpShared()
    {
        var c = new Checks(MpSharedName);
        Stage s = null;
        var w = new Waiter();
        var reply = new SelfTest.ServerReply();
        var fields = new FieldsRef();
        try
        {
            yield return MpBegin(c, MpSharedName, w);
            if (!w.Met)
            {
                c.Report();
                yield break;
            }
            s = Stage.Begin(MpSharedName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            var me = ZDOMan.GetSessionID().ToString(CultureInfo.InvariantCulture);
            // Short rout and provocation, set on the server like a server owner would.
            yield return SelfTest.CallServer(SrvSet, "RoutSeconds=4;ProvokedSeconds=10", reply);
            yield return Until(() => Mathf.Approximately(ServerRules.Current.RoutSeconds, 4f) && Mathf.Approximately(ServerRules.Current.ProvokedSeconds, 10f), 6f, null, w);
            if (!c.Check(reply.Answered && reply.Ok && w.Met, $"setup: the server's RoutSeconds 4 and ProvokedSeconds 10 reached the client ({reply})"))
            {
                c.Report();
                yield break;
            }

            // ----- M01: this player's standing, as the server holds it for every other game -----
            Standing.TestOverride = new Standing.Override(8);
            var standing = player.m_nview.GetZDO().GetByteArray(CreatureKeys.Standing);
            yield return AskUntil(SrvZdo, IdOf(player.m_nview), f => f["standing"] == Hex(standing), 6f, null, fields, w);
            c.Check(w.Met && standing != null && standing.Length >= 3 && standing[1] == 8,
                $"M01: the server holds this player's published standing (boss rank 8), which every other player's game judges creatures by "
                + $"(client {Hex(standing)}, server {fields["standing"]}{fields["error"]})");

            // ----- M14: a creature that runs is alerted on the server's copy, and no longer once it calmed -----
            var at = s.Spot(here, s.Forward, 6f, true);
            var g = s.Spawn("Greydwarf", at, here - at);
            if (!c.Check(g != null, "could not spawn a Greydwarf"))
            {
                c.Report();
                yield break;
            }
            Tough(g.m_character);
            Action hold = () =>
            {
                s.Noise();
                Stage.Place(g.m_character, at, player.transform.position - at);
            };
            yield return Until(() => FearOf(g) == player && g.IsAlerted() && ZdoAlert(g), 3.5f, hold, w);
            c.Check(w.Met, $"M14 setup: the Greydwarf this game controls runs from the rank 8 player (runs from {Who(FearOf(g))})");
            var gid = IdOf(g.m_nview);
            yield return AskUntil(SrvZdo, gid, f => f["found"] == "1" && f["alert"] == "1", 6f, hold, fields, w);
            c.Check(w.Met && fields["owner"] == me,
                $"M14: while it runs, the server's copy of the creature says alerted: the state every other player's game shows the alert icon from "
                + $"(found {fields["found"]}, alert {fields["alert"]}, owner is this game {fields["owner"] == me}{fields["error"]})");
            at = s.Spot(here, s.Forward, 20f, false);
            yield return Until(() => FearOf(g) == null && !g.IsAlerted() && !ZdoAlert(g), 3.5f, hold, w);
            c.Check(w.Met, "M14 setup: 20 m away it calms down on this game");
            yield return AskUntil(SrvZdo, gid, f => f["found"] == "1" && f["alert"] == "0", 6f, hold, fields, w);
            c.Check(w.Met, $"M14: once it calmed the server's copy says not alerted (the icon goes away on every screen) (alert {fields["alert"]}{fields["error"]})");

            // ----- M03: the provocation record travels with the creature -----
            at = s.Spot(here, s.Forward, 20f, true);
            yield return Wait(0.5f, hold);
            Hit(g.m_character, player, 1f, 0f, 1f);
            var record = g.m_nview.GetZDO().GetByteArray(CreatureKeys.Provokers);
            var hasEntry = CreatureKeys.TryGetUntil(record, player.GetPlayerID(), out var until);
            c.Check(hasEntry && SecondsLeft(until) > 8.5 && SecondsLeft(until) <= 10.01 && g.m_targetCreature == player,
                $"M03 setup: the hit provokes it for the server's ProvokedSeconds (10 s; {F(SecondsLeft(until))} s left)");
            yield return AskUntil(SrvZdo, gid, f => f["prov"] == Hex(record) && f["prov"].Length > 4, 6f, hold, fields, w);
            c.Check(w.Met, $"M03: the server's copy holds the provocation record (this player and the time it ends): a game that takes the creature over reads it "
                           + $"(client {Hex(record)}, server {fields["prov"]}{fields["error"]})");
            s.Destroy(g);

            // ----- M02 / M05: a leader's death goes out as a Rout message through the server -----
            Standing.TestOverride = new Standing.Override(0);
            Rout.Clear();
            yield return SelfTest.CallServer(SrvRout, "", reply);
            int.TryParse(Fields(reply.Detail).TryGetValue("count", out var countText) ? countText : "", out var received);
            c.Check(reply.Answered && reply.Ok && ZNet.instance.GetPeerConnections() > 0 && !Rout.ForceBroadcast,
                $"M02 setup: connected to the server, so a leader's death is broadcast ({reply})");
            var pack = SpawnPack(s, "Troll", "Greydwarf", 2, 15f);
            if (!c.Check(pack.Leader != null && pack.Followers.Count == 2, "M02: could not spawn the Troll and two Greydwarfs"))
            {
                c.Report();
                yield break;
            }
            var spots = new List<Vector3>();
            foreach (var f in pack.Followers)
            {
                spots.Add(Stage.Ground(f.transform.position));
            }
            Action holdPack = () =>
            {
                for (var i = 0; i < pack.Followers.Count; i++)
                {
                    if (pack.Followers[i] != null)
                    {
                        Stage.Place(pack.Followers[i].m_character, spots[i], spots[i] - pack.Center);
                    }
                }
            };
            yield return Wait(1f, holdPack);
            var mark = Watch.Mark();
            yield return KillLeader(s, pack, 3f, holdPack, w);
            c.Check(w.Met && Rout.LastAppliedHere == 2 && Watch.Count($"Rout: {TokenOf("Troll")} died, applied to 2 creature(s) here.", mark) == 1,
                $"M02: the Troll's death is sent as a Rout message and applied here to the 2 followers this game controls (applied {Rout.LastAppliedHere})");
            var wanted = received + 1;
            yield return AskUntil(SrvRout, "", f => f["count"] == wanted.ToString(CultureInfo.InvariantCulture), 6f, holdPack, fields, w);
            var serverPos = fields["pos"].Split(',');
            var near = serverPos.Length == 3 && float.TryParse(serverPos[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var px)
                       && float.TryParse(serverPos[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var pz)
                       && Mathf.Abs(px - pack.DeathPos.x) < 2f && Mathf.Abs(pz - pack.DeathPos.z) < 2f;
            c.Check(w.Met && near && fields["leader"] == Hash("Troll").ToString(CultureInfo.InvariantCulture),
                $"M02 / M10: the dedicated server got that one Rout message, with the place the Troll fell and its kind; the server passes it on to every other "
                + $"player's game (messages {fields["count"]}, expected {wanted}; place {fields["pos"]}, fell at {pack.DeathPos}{fields["error"]})");
            var f0 = pack.Followers[0];
            var id0 = IdOf(f0.m_nview);
            var routUntil = RoutUntilOf(f0).ToString(CultureInfo.InvariantCulture);
            yield return AskUntil(SrvZdo, id0, f => f["rout"] == routUntil && f["alert"] == "1", 5f, holdPack, fields, w);
            c.Check(w.Met && fields["from"] == Vec(RoutFromOf(f0)),
                $"M05 / M14: the server's copy of a fleeing follower holds the time its rout ends, the place it flees from and its alert "
                + $"(rout end matches {fields["rout"] == routUntil}, alert {fields["alert"]}, from {fields["from"]} / {Vec(RoutFromOf(f0))}{fields["error"]})");
            // The rout ends (4 s): the alert goes on this game, then on the server's copy.
            yield return WaitUntilTime(pack.KillTime + 4f, holdPack);
            yield return Until(() => !f0.IsAlerted() && !ZdoAlert(f0), 2.5f, holdPack, w);
            c.Check(w.Met, "M14 setup: when the rout ends the follower (15 m from the rank 0 player) is unalerted on this game");
            yield return AskUntil(SrvZdo, id0, f => f["alert"] == "0", 6f, holdPack, fields, w);
            c.Check(w.Met, $"M14: when the rout ends the server's copy says not alerted (alert {fields["alert"]}{fields["error"]})");
            pack.Destroy(s);
            Rout.Clear();
            yield return SelfTest.CallServer(SrvSet, "reset", reply);
            c.Check(reply.Answered && reply.Ok, $"the server's settings are back at their defaults ({reply})");
            c.Report();
        }
        finally
        {
            Finish(s);
        }
    }

    // ================================================================ morale.mp.off-allowed

    private static IEnumerator RunMpOffAllowed()
    {
        var c = new Checks(MpOffAllowedName);
        Stage s = null;
        var w = new Waiter();
        var reply = new SelfTest.ServerReply();
        var fields = new FieldsRef();
        try
        {
            yield return MpBegin(c, MpOffAllowedName, w);
            if (!w.Met)
            {
                c.Report();
                yield break;
            }
            s = Stage.Begin(MpOffAllowedName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            Standing.TestOverride = new Standing.Override(8);
            var at = s.Spot(here, s.Forward, 6f, true);
            var g = s.Spawn("Greydwarf", at, here - at);
            if (!c.Check(g != null, "could not spawn a Greydwarf"))
            {
                c.Report();
                yield break;
            }
            Tough(g.m_character);
            Action hold = () =>
            {
                s.Noise();
                Stage.Place(g.m_character, at, player.transform.position - at);
            };
            yield return Until(() => FearOf(g) == player && g.IsAlerted(), 3.5f, hold, w);
            c.Check(w.Met, "M08 setup: the Greydwarf runs from the rank 8 player");

            // The server lets such players stay for 90 s. Without that answer the mod is never turned off here: the
            // server would refuse this player and end the whole run.
            yield return SelfTest.CallServer(SrvAllow, "90", reply);
            if (!c.Check(reply.Answered && reply.Ok, $"the server half lets players with the mod turned off stay ({reply})"))
            {
                c.Report();
                yield break;
            }
            var mark = Watch.Mark();
            var offAt = Time.time;
            SetBlocked(true);
            c.Check(!Plugin.Live && ModStateNow() != nameof(MC.Shared.ModState.Active), $"M08: the mod is off on this game (state {ModStateNow()})");
            c.Check(Watch.Count($"Told the server that {ModInfo.Name} is now off on this game.", mark, LogLevel.Info) == 1
                    && Watch.Count("Standing withdrawn:", mark, LogLevel.Info) == 1,
                $"M08: the client log says \"Told the server that {ModInfo.Name} is now off on this game.\" and \"Standing withdrawn: ...\"");
            yield return Until(() => g.m_targetCreature == player, 6f, hold, w);
            c.Check(w.Met, $"M08 (allowed): the Greydwarf attacks that player once it hears them (target {Who(g.m_targetCreature)})");
            yield return WaitUntilTime(offAt + 7f, hold);
            c.Check(ConnectedClient(), $"M08 (allowed): 7 s after turning the mod off the player is still connected (status {ZNet.GetConnectionStatus()})");
            yield return AskUntil(SrvPeer, "", f => f["turnedOff"] == "1" && f["allowed"] == "1", 5f, hold, fields, w);
            c.Check(w.Met && fields["ready"] == "0" && fields["compatible"] == "0" && fields["problem"] == "has the mod turned off" && fields["kicked"] == "0"
                    && fields["refused"] == "0" && fields["standing"] == Hex(Standing.WithdrawnBytes()),
                $"M08 (allowed): the server logged \"<player> turned {ModInfo.Name} off on their game.\" and the warning that lets them play, refused nobody, "
                + $"and holds that player's standing as withdrawn (turned off {fields["turnedOff"]}, allowed {fields["allowed"]}, problem \"{fields["problem"]}\", "
                + $"refused {fields["refused"]}, kicked {fields["kicked"]}, standing {fields["standing"]}{fields["error"]})");

            // Back on.
            mark = Watch.Mark();
            SetBlocked(false);
            yield return Until(() => Plugin.Live && ModStateNow() == nameof(MC.Shared.ModState.Active) && !ServerRules.Pending, 6f, hold, w);
            c.Check(w.Met && Watch.Count($"Told the server that {ModInfo.Name} is now on on this game.", mark, LogLevel.Info) == 1,
                $"M08: turned back on: active again with the server's rules, and the server is told (state {ModStateNow()}, rules pending {ServerRules.Pending})");
            Standing.TestOverride = new Standing.Override(8);
            yield return Until(() => FearOf(g) == player && g.m_targetCreature == null, 4f, hold, w);
            c.Check(w.Met, $"M08: turned back on: the Greydwarf is afraid of that player again (runs from {Who(FearOf(g))}, target {Who(g.m_targetCreature)})");
            var standing = player.m_nview.GetZDO().GetByteArray(CreatureKeys.Standing);
            yield return Wait(2.5f, hold); // past the server's 1 s grace for its check of this player
            yield return AskUntil(SrvPeer, "", f => f["turnedOn"] == "1" && f["standing"] == Hex(standing), 5f, hold, fields, w);
            c.Check(w.Met && fields["ready"] == "1" && fields["compatible"] == "1" && fields["kicked"] == "0" && fields["refused"] == "0" && ConnectedClient(),
                $"M08: turned back on: the server sees the player as compatible again, refuses nobody and holds their standing again "
                + $"(compatible {fields["compatible"]}, refused {fields["refused"]}, standing {fields["standing"]}{fields["error"]})");
            yield return SelfTest.CallServer(SrvAllow, "0", reply);
            s.Destroy(g);
            c.Report();
        }
        finally
        {
            if (Plugin.TestBlocked)
            {
                SetBlocked(false);
            }
            Finish(s);
        }
    }

    // ================================================================ morale.mp.server-off

    private static IEnumerator RunMpServerOff()
    {
        var c = new Checks(MpServerOffName);
        Stage s = null;
        var w = new Waiter();
        var reply = new SelfTest.ServerReply();
        try
        {
            yield return MpBegin(c, MpServerOffName, w);
            if (!w.Met)
            {
                c.Report();
                yield break;
            }
            s = Stage.Begin(MpServerOffName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var here = player.transform.position;
            Standing.TestOverride = new Standing.Override(4);
            var at = s.Spot(here, s.Forward, 6f, true);
            var g = s.Spawn("Greydwarf", at, here - at);
            if (!c.Check(g != null, "could not spawn a Greydwarf"))
            {
                c.Report();
                yield break;
            }
            Tough(g.m_character);
            Action hold = () =>
            {
                s.Noise();
                Stage.Place(g.m_character, at, player.transform.position - at);
            };
            yield return Until(() => FearOf(g) == player && g.IsAlerted(), 3.5f, hold, w);
            c.Check(w.Met, "M11 setup: the Greydwarf runs from the rank 4 player");

            // The server owner turns the mod off (the server probe's own step writes the server's Enabled).
            yield return SelfTest.CallServer(ProbeSetEnabled, ModInfo.Guid + "=off", reply);
            var turnedOff = reply.Answered && reply.Ok;
            c.Check(turnedOff, $"the server turned {ModInfo.Name} off ({reply})");
            if (turnedOff)
            {
                yield return Until(() => ModStateNow() == nameof(MC.Shared.ModState.ServerMissing), 10f, hold, w);
                var view = FeatureRegistry.Find(ModInfo.Guid);
                var status = view != null ? view.Value.Status : "";
                c.Check(w.Met && !Plugin.Live && status.IndexOf("the server has this mod turned off", StringComparison.OrdinalIgnoreCase) >= 0,
                    $"M11: the client's copy turns off: \"{status}\" (state {ModStateNow()})");
                yield return Until(() => g.m_targetCreature == player, 6f, hold, w);
                c.Check(w.Met, $"M11: the Greydwarf that ran stops running and notices the player again, as in the normal game (target {Who(g.m_targetCreature)})");
                c.Check(ConnectedClient(), $"M11: nobody is refused: the player is still connected (status {ZNet.GetConnectionStatus()})");
            }
            // Always back on, whatever the checks above said.
            yield return SelfTest.CallServer(ProbeSetEnabled, ModInfo.Guid + "=on", reply);
            c.Check(reply.Answered && reply.Ok, $"the server turned {ModInfo.Name} back on ({reply})");
            yield return Until(() => Plugin.Live && ModStateNow() == nameof(MC.Shared.ModState.Active) && !ServerRules.Pending, 12f, hold, w);
            c.Check(w.Met, $"M11: back on: the client's copy is active again with the server's rules (state {ModStateNow()}, rules pending {ServerRules.Pending})");
            Standing.TestOverride = new Standing.Override(4);
            yield return Until(() => FearOf(g) == player && g.m_targetCreature == null, 4f, hold, w);
            c.Check(w.Met, $"M11: back on: the Greydwarf is afraid again and runs (runs from {Who(FearOf(g))}, target {Who(g.m_targetCreature)})");
            yield return Wait(3f, hold); // past the server's check of the players that were in while it was off
            c.Check(ConnectedClient(), $"M11: still connected after the server's copy came back (status {ZNet.GetConnectionStatus()})");
            s.Destroy(g);
            c.Report();
        }
        finally
        {
            Finish(s);
        }
    }
}
#endif
