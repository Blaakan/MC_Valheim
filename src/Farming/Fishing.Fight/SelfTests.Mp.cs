#if DEBUG
using System;
using System.Collections;
using System.Globalization;
using MC.Shared;
using UnityEngine;

namespace MC.Farming.FishingFightMod;

// Debug build only. Multiplayer self tests (tools/Test-Multiplayer.ps1): one client joined to a real dedicated
// server. Client halves here, server halves (RegisterServerStep) below them. The server stands in for "the other
// game": what it holds is what every other player gets.
//   fishing.mp.rules      (modded) the server's rules reach the client at join and live when the server owner changes
//                         them (WrongSideStamina 8, BarSize 0.5): client log line, zone half the bar, wrong side x8
//                         in a live fight (dry land rig), and back to the defaults
//   fishing.mp.watch      (modded) a fight on the client as the server sees it: fish hooked + escape key on while it
//                         fights, off when calm, hooked flag off when let go
//   fishing.mp.left-hook  (modded) a fish left with the hooked flag and no float, handed to the server: the game
//                         that runs it next clears it (the server when the fish lies in its own area, else the
//                         player the server gives it to: here the test client), and the server's copy shows it off
//   fishing.mp.version    (modded) a hello with another network version: the server refuses it (reason logged, client
//                         told "Incompatible version"); the test takes the cut back before it happens and stays in
//   fishing.mp.no-server  (vanilla-server) the mod turned itself off (status says the server lacks it), no patch, and
//                         a hooked fish is reeled and caught the normal game's way
// Live fights here run on dry land where the player stands (random world: no shore search). Rules in the rules test
// are the server's real ones (no ServerRules.TestRules).
internal static partial class SelfTests
{
    private const string MpRulesName = "fishing.mp.rules";
    private const string MpWatchName = "fishing.mp.watch";
    private const string MpLeftHookName = "fishing.mp.left-hook";
    private const string MpVersionName = "fishing.mp.version";
    private const string MpNoServerName = "fishing.mp.no-server";

    private const string StepRules = "fishing.mp.server-rules";
    private const string StepFish = "fishing.mp.server-fish";
    private const string StepLeftHook = "fishing.mp.server-left-hook";
    private const string StepRefuse = "fishing.mp.server-refuse";
    private const string StepCompatible = "fishing.mp.server-compatible";

    // Scenario of tools/Test-Multiplayer.ps1 where the server runs no MC mod.
    private const string VanillaServerScenario = "vanilla-server";

    private static void RegisterMultiplayer()
    {
        SelfTest.RegisterMultiplayer(MpRulesName, SelfTest.Modded, RunMpRules);
        SelfTest.RegisterMultiplayer(MpWatchName, SelfTest.Modded, RunMpWatch);
        SelfTest.RegisterMultiplayer(MpLeftHookName, SelfTest.Modded, RunMpLeftHook);
        SelfTest.RegisterMultiplayer(MpVersionName, SelfTest.Modded, RunMpVersion);
        // Stays registered when the mod goes off: on a server without the mod that is the state under test.
        SelfTest.RegisterMultiplayer(MpNoServerName, VanillaServerScenario, RunMpNoServer);
        SelfTest.RegisterServerStep(StepRules, ServerRulesStep);
        SelfTest.RegisterServerStep(StepFish, ServerFishStep);
        SelfTest.RegisterServerStep(StepLeftHook, ServerLeftHookStep);
        SelfTest.RegisterServerStep(StepRefuse, ServerRefuseStep);
        SelfTest.RegisterServerStep(StepCompatible, ServerCompatibleStep);
    }

    private static void UnregisterMultiplayer()
    {
        SelfTest.UnregisterMultiplayer(MpRulesName);
        SelfTest.UnregisterMultiplayer(MpWatchName);
        SelfTest.UnregisterMultiplayer(MpLeftHookName);
        SelfTest.UnregisterMultiplayer(MpVersionName);
        SelfTest.UnregisterServerStep(StepRules);
        SelfTest.UnregisterServerStep(StepFish);
        SelfTest.UnregisterServerStep(StepLeftHook);
        SelfTest.UnregisterServerStep(StepRefuse);
        SelfTest.UnregisterServerStep(StepCompatible);
    }

    // ---------- helpers ----------

    private static bool IsClient => ZNet.instance != null && !ZNet.instance.IsServer()
                                    && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected;

    private static string IdText(ZDOID id) =>
        id.UserID.ToString(CultureInfo.InvariantCulture) + ":" + id.ID.ToString(CultureInfo.InvariantCulture);

    private static bool TryId(string text, out ZDOID id)
    {
        id = ZDOID.None;
        var parts = (text ?? "").Split(':');
        if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var user)
            || !uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }
        id = new ZDOID(user, number);
        return true;
    }

    // Tell the server step to run without waiting for its answer (clean-up in a finally, where nothing can wait).
    private static void FireServer(string step, string arg)
    {
        try
        {
            var outer = SelfTest.CallServer(step, arg, new SelfTest.ServerReply());
            if (outer.MoveNext() && outer.Current is IEnumerator inner)
            {
                // First move sends the rpc; closing it forgets the answer.
                inner.MoveNext();
                (inner as IDisposable)?.Dispose();
            }
            (outer as IDisposable)?.Dispose();
        }
        catch (Exception e)
        {
            SelfTest.Note(step, "could not ask the server from clean-up: " + e.Message);
        }
    }

    // The client waits until the rules in force read like this text (the server's Describe).
    private static IEnumerator WaitRules(string describe, float seconds, Box result)
    {
        var start = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - start < seconds)
        {
            if (ServerRules.UsingServer && ServerRules.Current.Describe() == describe)
            {
                result.Ok = true;
                result.Detail = $"{F(Time.realtimeSinceStartup - start)} s";
                yield break;
            }
            yield return null;
        }
        result.Ok = false;
        result.Detail = $"not after {F(seconds)} s: {ServerRules.Current.Describe()}";
    }

    // ---------- fishing.mp.rules ----------

    private static IEnumerator RunMpRules() => RunLive(MpRulesName, false, MpRulesBody);

    private static IEnumerator MpRulesBody(Checks c, FishRig rig, Stage st, LogTap tap)
    {
        var p = rig.P;
        if (!c.Check(IsClient, "connected to a server as a client"))
        {
            yield break;
        }
        var defaults = FightRules.Default.Describe();
        var changed = false;
        try
        {
            // Rules of the join: the server's own, asked after the handshake.
            var reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepRules, "reset", reply);
            if (!c.Check(reply.Answered && reply.Ok, "server step (rules back to the defaults): " + reply))
            {
                yield break;
            }
            var joined = new Box();
            yield return WaitRules(reply.Detail, 8f, joined);
            c.Check(joined.Ok && ServerRules.UsingServer && !ServerRules.IsPending && reply.Detail == defaults,
                $"the client plays by the rules the server sent at the join, the server's defaults ({joined.Detail})");

            // The server owner changes two settings: they arrive live.
            var mark = tap.Mark();
            changed = true;
            reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepRules, "set", reply);
            if (!c.Check(reply.Answered && reply.Ok, "server step (WrongSideStamina 8, BarSize 0.5): " + reply))
            {
                yield break;
            }
            var live = new Box();
            yield return WaitRules(reply.Detail, 8f, live);
            var rules = ServerRules.Current;
            c.Check(live.Ok && Near(rules.WrongSideStamina, 8f) && Near(rules.BarSize, 0.5f),
                $"the server's changed rules arrive on the client while connected ({live.Detail}): wrong side x{F(rules.WrongSideStamina)}, bar {F(rules.BarSize)}");
            var line = tap.Find("Using the server's rules: catch bar 50%", mark);
            c.Check(line != null && line.IndexOf("(wrong side x8)", StringComparison.Ordinal) >= 0,
                $"client log line \"Using the server's rules: catch bar 50% ...\" with the wrong side x8 (got \"{line}\")");
            c.Check(Plugin.WrongSideStamina != null && Near(Plugin.WrongSideStamina.Value, FightRules.Default.WrongSideStamina)
                    && Plugin.BarSize != null && Near(Plugin.BarSize.Value, FightRules.Default.BarSize),
                "the client's own settings are untouched (they apply again in single player)");

            // A fight by those rules: zone half the bar at Fishing 0, wrong side about eight times the right side.
            FightHud.TestDisplay = DefaultDisplay;
            Fight.TestPhase = FightPhase.Calm;
            Fight.TestPin = FishPin.Away;
            var hook = new Hooked();
            yield return HookNew(rig, st, 1, hook);
            var fight = hook.Fight;
            if (c.Check(fight != null, "fight starts under the server's rules"))
            {
                var ff = hook.Float;
                var fish = hook.Fish;
                rig.Fill(p.GetMaxStamina());
                yield return new WaitForFixedUpdate();
                yield return null;
                yield return null;
                var h = FightHud.TestTrackHeight;
                c.Check(Near(fight.Bar.ZoneSize, 0.5f, 0.005f) && Near(FightHud.TestZoneHeight, 0.5f * h, 2f),
                    $"the zone is half the bar at Fishing 0 (zone {F(fight.Bar.ZoneSize)}, drawn {F(FightHud.TestZoneHeight)} of {F(h)})");

                var turn = rules.RodAngle + 15f;
                Fight.TestPin = null;
                Fight.TestSide = FightLogic.Right;
                Fight.TestPhase = FightPhase.Struggle;
                rig.Fill(p.GetMaxStamina());
                rig.Face(Quaternion.Euler(0f, turn, 0f) * Flat(ff.transform.position - p.transform.position));
                rig.Reel(true);
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                var wrong = fight.Verdict;
                var line0 = ff.m_lineLength;
                var stamina0 = p.GetStamina();
                var t0 = Time.time;
                yield return new WaitForSeconds(0.3f);
                var wrongRate = (stamina0 - p.GetStamina()) / Mathf.Max(0.01f, Time.time - t0);
                var wrongLine = ff != null ? ff.m_lineLength - line0 : -1f;
                rig.Fill(p.GetMaxStamina());
                if (ff != null)
                {
                    rig.Face(Quaternion.Euler(0f, -turn, 0f) * Flat(ff.transform.position - p.transform.position));
                }
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                var good = fight.Verdict;
                stamina0 = p.GetStamina();
                t0 = Time.time;
                yield return new WaitForSeconds(0.3f);
                var goodRate = (stamina0 - p.GetStamina()) / Mathf.Max(0.01f, Time.time - t0);
                rig.Reel(false);
                var ratio = goodRate > 0.01f ? wrongRate / goodRate : 0f;
                c.Check(wrong == RodVerdict.Wrong && good == RodVerdict.Good && Mathf.Abs(wrongLine) < 0.05f,
                    $"fish runs right: rod on its side is wrong (no line in), rod on the other side is right (line {F(wrongLine)} m on the wrong side)");
                c.Check(goodRate > 0.5f && ratio > 6.5f && ratio < 9.5f,
                    $"by the server's rules the wrong side costs about eight times the right side ({F(wrongRate)}/s vs {F(goodRate)}/s, x{F(ratio)})");
                c.Note($"fish out of the water in this rig: {(fish != null ? fish.IsOutOfWater().ToString() : "gone")}");
            }
            Remove(hook);
            ClearOverrides();
            yield return null;

            // The server owner puts the settings back: the client follows.
            reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepRules, "reset", reply);
            changed = !(reply.Answered && reply.Ok);
            var back = new Box();
            yield return WaitRules(defaults, 8f, back);
            c.Check(reply.Answered && reply.Ok && back.Ok, $"settings put back on the server: the client follows ({back.Detail})");
        }
        finally
        {
            if (changed)
            {
                FireServer(StepRules, "reset");
            }
        }
    }

    // Server half: the server owner's own settings ("set" = WrongSideStamina 8 and BarSize 0.5, anything else = the
    // defaults). The real config path: the push to every player follows by itself. Answer = the server's rules text.
    private static IEnumerator ServerRulesStep(string arg, object[] reply)
    {
        var net = ZNet.instance;
        if (net == null || !net.IsServer() || Plugin.WrongSideStamina == null || Plugin.BarSize == null)
        {
            SelfTest.Answer(reply, false, "not a server, or the settings are not bound");
            yield break;
        }
        var d = FightRules.Default;
        var set = arg == "set";
        Plugin.WrongSideStamina.Value = set ? 8f : d.WrongSideStamina;
        Plugin.BarSize.Value = set ? 0.5f : d.BarSize;
        yield return null;
        SelfTest.Answer(reply, true, FightRules.Own().Describe());
    }

    // ---------- fishing.mp.watch ----------

    private static IEnumerator RunMpWatch() => RunLive(MpWatchName, false, MpWatchBody);

    private static IEnumerator MpWatchBody(Checks c, FishRig rig, Stage st, LogTap tap)
    {
        var p = rig.P;
        if (!c.Check(IsClient, "connected to a server as a client"))
        {
            yield break;
        }
        ServerRules.TestRules = new FightRules { OffBarStamina = 0f };
        FightHud.TestDisplay = DefaultDisplay;
        Fight.TestPin = FishPin.Away;
        Fight.TestSide = FightLogic.Right;
        Fight.TestPhase = FightPhase.Struggle;
        var hook = new Hooked();
        yield return HookNew(rig, st, 1, hook);
        if (!c.Check(hook.Fight != null, "fight starts on the client"))
        {
            yield break;
        }
        var fish = hook.Fish;
        var id = IdText(fish.GetZDOID());
        rig.Fill(p.GetMaxStamina());
        yield return new WaitForSeconds(0.3f);
        var zdo = fish.m_nview.GetZDO();
        c.Check(zdo.GetInt(ZDOVars.s_hooked) == 1 && zdo.GetFloat(ZDOVars.s_escape) > 0f, "client: the fish fights (hooked flag and escape key on)");
        var reply = new SelfTest.ServerReply();
        yield return SelfTest.CallServer(StepFish, "fight|" + id, reply);
        c.Check(reply.Answered && reply.Ok,
            "server (what every other player gets): the fish is hooked and its escape key is on, so their games splash at it: " + reply);

        Fight.TestPhase = FightPhase.Calm;
        rig.Fill(p.GetMaxStamina());
        yield return new WaitForSeconds(0.2f);
        reply = new SelfTest.ServerReply();
        yield return SelfTest.CallServer(StepFish, "calm|" + id, reply);
        c.Check(reply.Answered && reply.Ok, "server: calm between fights, still hooked, escape key off (no splashes): " + reply);

        // Float gone: the fight ends and lets the fish go.
        Kill(hook.Float != null ? hook.Float.gameObject : null);
        yield return new WaitForSeconds(0.3f);
        c.Check(Fight.Current == null && fish != null && IsFree(fish), "client: float gone, the fish is let go");
        reply = new SelfTest.ServerReply();
        yield return SelfTest.CallServer(StepFish, "free|" + id, reply);
        c.Check(reply.Answered && reply.Ok, "server: the fish is no longer hooked for anyone: " + reply);
    }

    // Server half: the fish's ZDO as the server holds it. arg "<fight|calm|free>|<zdo id>"; waits up to 8 s for the
    // state (the client's change takes a moment to arrive).
    private static IEnumerator ServerFishStep(string arg, object[] reply)
    {
        var parts = (arg ?? "").Split('|');
        if (parts.Length != 2 || !TryId(parts[1], out var id) || ZDOMan.instance == null)
        {
            SelfTest.Answer(reply, false, "bad argument '" + arg + "'");
            yield break;
        }
        var want = parts[0];
        var seen = "the server has no such fish";
        var start = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - start < 8f)
        {
            var zdo = ZDOMan.instance.GetZDO(id);
            if (zdo != null)
            {
                var hooked = zdo.GetInt(ZDOVars.s_hooked);
                var escape = zdo.GetFloat(ZDOVars.s_escape);
                var mine = zdo.GetOwner() == ZDOMan.GetSessionID();
                seen = $"hooked {hooked}, escape {F(escape)}, run by {(mine ? "the server" : "a player")}";
                var ok = want == "fight" ? hooked == 1 && escape > 0f && !mine
                    : want == "calm" ? hooked == 1 && Near(escape, 0f) && !mine
                    : want == "free" && hooked == 0 && Near(escape, 0f);
                if (ok)
                {
                    SelfTest.Answer(reply, true, seen + $" ({F(Time.realtimeSinceStartup - start)} s)");
                    yield break;
                }
            }
            yield return null;
        }
        SelfTest.Answer(reply, false, $"wanted '{want}', the server still sees: {seen}");
    }

    // ---------- fishing.mp.left-hook ----------

    private static IEnumerator RunMpLeftHook() => RunLive(MpLeftHookName, false, MpLeftHookBody);

    // Who runs the fish, as the server step says it (the client test reads these words).
    private const string RunByServer = "run by the server";
    private const string RunByPlayer = "run by the player who handed it over";

    private static IEnumerator MpLeftHookBody(Checks c, FishRig rig, Stage st, LogTap tap)
    {
        if (!c.Check(IsClient, "connected to a server as a client"))
        {
            yield break;
        }
        var server = ZNet.instance.GetServerPeer();
        if (!c.Check(server != null && server.m_uid != 0L, "the server's id is known"))
        {
            yield break;
        }
        // A Perch 7 m ahead (out of reach of the pickup), run by this game at first.
        var fish = SpawnFish(rig, st, 1, st.FloatPos);
        for (var i = 0; i < 25; i++)
        {
            yield return new WaitForFixedUpdate();
        }
        if (!c.Check(fish != null && fish.m_nview != null && fish.m_nview.IsValid() && fish.m_nview.IsOwner(), "a free Perch run by the client"))
        {
            yield break;
        }
        // What a fisher who left mid-fight leaves behind: hooked flag and escape time on, no float. Handed to the
        // server in the same breath (this game's own copy of the mod would clear it on its next tick).
        // A dedicated server run only what lie near its own reference point (first run: "in the server's world
        // False" for a fish 230 m from the world centre). A fish elsewhere it give to a player near the fish within
        // 2 s (ZDOMan.ReleaseNearbyZDOS): that is how the next game get the fish of a fisher who left. Here the only
        // player near is this one. So two true ways: the server run the fish and its copy of the mod clear it, or
        // the server give it back and this game's copy clear it on the first ticks it run it. Either way the
        // server's copy (what every other player get) must end with both keys off.
        var zdo = fish.m_nview.GetZDO();
        var id = IdText(zdo.m_uid);
        var me = ZDOMan.GetSessionID();
        var persistent = zdo.Persistent;
        zdo.Set(ZDOVars.s_hooked, 1);
        zdo.Set(ZDOVars.s_escape, 3f);
        zdo.SetOwner(server.m_uid);
        var sent = zdo.DataRevision;
        c.Check(zdo.GetInt(ZDOVars.s_hooked) == 1 && zdo.GetFloat(ZDOVars.s_escape) > 0f && !fish.m_nview.IsOwner(),
            "left-behind state set on the client and the fish handed to the server");

        // Tick by tick on this game: flags left alone while it does not run the fish, cleared at once when it does.
        var start = Time.realtimeSinceStartup;
        var heldTicks = 0;
        var ownTicks = 0;
        var backAt = -1f;
        var cleared = false;
        var mine = false;
        var gone = false;
        while (Time.realtimeSinceStartup - start < 10f)
        {
            yield return new WaitForFixedUpdate();
            gone = fish == null || fish.m_nview == null || !fish.m_nview.IsValid();
            if (gone)
            {
                break;
            }
            mine = fish.m_nview.IsOwner();
            if (mine && backAt < 0f)
            {
                backAt = Time.realtimeSinceStartup - start;
            }
            if (zdo.GetInt(ZDOVars.s_hooked) == 0 && Near(zdo.GetFloat(ZDOVars.s_escape), 0f))
            {
                cleared = true;
                break;
            }
            if (mine)
            {
                ownTicks++;
            }
            else
            {
                heldTicks++;
            }
        }
        var waited = Time.realtimeSinceStartup - start;
        var state = gone
            ? "the fish is gone"
            : $"hooked {zdo.GetInt(ZDOVars.s_hooked)}, escape {F(zdo.GetFloat(ZDOVars.s_escape))}, run by this game {mine}";
        c.Note($"after the hand-off: {state} after {F(waited)} s; this game did not run the fish on {heldTicks} ticks with the flags on, "
               + $"ran it again after {Seconds(backAt)} and for {ownTicks} ticks with the flags on; saved with the world: {persistent}");
        c.Check(!gone && cleared,
            $"the hooked flag and the escape time left behind go off within 10 s of the hand-off: the fish stops splashing ({state}, {F(waited)} s)");
        if (cleared && mine)
        {
            c.Check(ownTicks <= 3,
                $"the server gave the fish back to this player (the only game near it) {Seconds(backAt)} after the hand-off, and this game's copy of the mod cleared it within 3 ticks of running it ({ownTicks})");
        }

        var reply = new SelfTest.ServerReply();
        yield return SelfTest.CallServer(StepLeftHook,
            id + "|" + sent.ToString(CultureInfo.InvariantCulture) + "|" + me.ToString(CultureInfo.InvariantCulture), reply);
        c.Check(reply.Answered && reply.Ok,
            "server (the other game: what every other player gets): the left-behind state arrived there, the fish is run by a game with the mod and both keys are off: " + reply);
        if (reply.Answered && reply.Ok && cleared)
        {
            c.Check(reply.Detail.IndexOf(mine ? RunByPlayer : RunByServer, StringComparison.Ordinal) >= 0,
                mine
                    ? "the server agrees: it gave the fish to this player, whose copy of the mod cleared it"
                    : "the server agrees: it runs the fish itself and its copy of the mod cleared it");
        }
    }

    // Server half. arg = "<fish id>|<data revision at the hand-off>|<id of the player who handed it over>". Waits up
    // to 8 s until the server's copy of the fish is newer than the handed-over state (so the state did arrive, and
    // what me read is not the fish from before it) with both keys off, and run by a game that has the mod: the
    // server itself (fish object in its world) or the player it gave the fish to. Says what it sees otherwise.
    private static IEnumerator ServerLeftHookStep(string arg, object[] reply)
    {
        var parts = (arg ?? "").Split('|');
        var id = ZDOID.None;
        var sent = 0u;
        var player = 0L;
        if (parts.Length != 3 || !TryId(parts[0], out id)
            || !uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out sent)
            || !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out player)
            || ZDOMan.instance == null || ZNetScene.instance == null)
        {
            SelfTest.Answer(reply, false, "bad argument '" + arg + "'");
            yield break;
        }
        var seen = "the server has no such fish";
        var start = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - start < 8f)
        {
            var zdo = ZDOMan.instance.GetZDO(id);
            if (zdo != null)
            {
                var hooked = zdo.GetInt(ZDOVars.s_hooked);
                var escape = zdo.GetFloat(ZDOVars.s_escape);
                var owner = zdo.GetOwner();
                var here = ZNetScene.instance.FindInstance(zdo) != null;
                var byServer = owner == ZDOMan.GetSessionID() && here;
                var byPlayer = owner == player && player != 0L;
                var who = byServer ? RunByServer
                    : byPlayer ? RunByPlayer
                    : owner == ZDOMan.GetSessionID() ? "given to the server, which has no fish object for it"
                    : owner == 0L ? "run by nobody"
                    : "run by another game";
                seen = $"hooked {hooked}, escape {F(escape)}, {who}, data revision {zdo.DataRevision} (handed over at {sent})";
                if (zdo.DataRevision > sent && (byServer || byPlayer) && hooked == 0 && Near(escape, 0f))
                {
                    SelfTest.Answer(reply, true, seen + $" ({F(Time.realtimeSinceStartup - start)} s)");
                    yield break;
                }
            }
            yield return null;
        }
        SelfTest.Answer(reply, false, "not cleared on the server: " + seen);
    }

    // ---------- fishing.mp.version ----------

    private static IEnumerator RunMpVersion()
    {
        var c = new Checks(MpVersionName);
        var net = ZNet.instance;
        var server = net != null && !net.IsServer() ? net.GetServerPeer() : null;
        var rpc = server != null ? server.m_rpc : null;
        if (!IsClient || rpc == null)
        {
            SelfTest.Fail(MpVersionName, "not connected to a server as a client");
            yield break;
        }
        // The hello of this mod (framework NetworkGate): "1|<network version>|<version>|<on|off>".
        var helloRpc = ModInfo.Guid + ".Hello";
        var otherVersion = ModInfo.NetworkVersion + 1;
        var told = -1;
        var faked = false;
        try
        {
            var reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepCompatible, "", reply);
            if (!c.Check(reply.Answered && reply.Ok, "before: the server finds this player compatible: " + reply))
            {
                c.Report();
                yield break;
            }

            // The refusal reaches the game as the "Error" rpc: the test reads it instead of leaving the server.
            faked = true;
            rpc.Register<int>("Error", (r, code) => told = code);
            rpc.Invoke(helloRpc, $"1|{otherVersion}|{ModInfo.Version}|on");
            reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepRefuse, "", reply);
            var waited = Time.realtimeSinceStartup;
            while (told < 0 && Time.realtimeSinceStartup - waited < 3f)
            {
                yield return null;
            }
            c.Check(reply.Answered && reply.Ok, "server: a player whose copy has another network version is refused: " + reply);
            c.Check(reply.Detail.IndexOf("another version of the mod", StringComparison.Ordinal) >= 0
                    && reply.Detail.IndexOf("network version " + otherVersion, StringComparison.Ordinal) >= 0,
                $"server: the reason names the other version (network version {otherVersion})");
            c.Check(told == (int)ZNet.ConnectionStatus.ErrorVersion,
                $"the refused game is told \"Incompatible version\" (error code {told}, expected {(int)ZNet.ConnectionStatus.ErrorVersion})");
        }
        finally
        {
            if (faked)
            {
                // True hello again, and the game's own error handler back.
                rpc.Invoke(helloRpc, $"1|{ModInfo.NetworkVersion}|{ModInfo.Version}|on");
                rpc.Register<int>("Error", net.RPC_Error);
            }
        }
        var again = new SelfTest.ServerReply();
        yield return SelfTest.CallServer(StepCompatible, "", again);
        c.Check(again.Answered && again.Ok, "after the true hello: compatible again, no cut waiting on the server: " + again);
        // Longer than the server waits before it cuts a refused player.
        yield return new WaitForSecondsRealtime(PlayerCheck.DisconnectDelay + 2f);
        c.Check(IsClient && Player.m_localPlayer != null, "the test player is still in the game");
        c.Report();
    }

    // Server half: the only connected player. Waits for its hello with another network version, runs the join check
    // on it (PlayerCheck, what a join runs), waits for the refusal (error sent, cut queued), then takes the cut back
    // so the test client stays. Answer = the reason the server logged.
    private static IEnumerator ServerRefuseStep(string arg, object[] reply)
    {
        var net = ZNet.instance;
        var peers = net != null && net.IsServer() ? net.GetPeers() : null;
        if (peers == null || peers.Count != 1 || peers[0] == null || !peers[0].IsReady())
        {
            SelfTest.Answer(reply, false, $"needs exactly one connected player (has {(peers != null ? peers.Count : 0)})");
            yield break;
        }
        var peer = peers[0];
        var start = Time.realtimeSinceStartup;
        while (NetworkGate.PeerCompatible(peer) && Time.realtimeSinceStartup - start < 4f)
        {
            yield return null;
        }
        if (NetworkGate.PeerCompatible(peer))
        {
            SelfTest.Answer(reply, false, "the hello with another network version did not arrive: the player is still compatible");
            yield break;
        }
        var problem = NetworkGate.PeerProblem(peer) ?? "";
        PlayerCheck.Schedule(peer);
        start = Time.realtimeSinceStartup;
        while (!ZNet.PeersToDisconnectAfterKick.ContainsKey(peer) && Time.realtimeSinceStartup - start < PlayerCheck.GraceSeconds + 3f)
        {
            yield return null;
        }
        var refused = ZNet.PeersToDisconnectAfterKick.ContainsKey(peer);
        if (refused)
        {
            ZNet.PeersToDisconnectAfterKick.Remove(peer);
        }
        SelfTest.Answer(reply, refused,
            refused
                ? $"refused {F(Time.realtimeSinceStartup - start)} s after the check was asked (the player {problem}); the cut was taken back for the test"
                : $"not refused within {F(PlayerCheck.GraceSeconds + 3f)} s although the player {problem}");
    }

    // Server half: the only connected player is compatible (waits up to 4 s) and no cut waits for it.
    private static IEnumerator ServerCompatibleStep(string arg, object[] reply)
    {
        var net = ZNet.instance;
        var peers = net != null && net.IsServer() ? net.GetPeers() : null;
        if (peers == null || peers.Count != 1 || peers[0] == null)
        {
            SelfTest.Answer(reply, false, $"needs exactly one connected player (has {(peers != null ? peers.Count : 0)})");
            yield break;
        }
        var peer = peers[0];
        var start = Time.realtimeSinceStartup;
        while (!NetworkGate.PeerCompatible(peer) && Time.realtimeSinceStartup - start < 4f)
        {
            yield return null;
        }
        var compatible = NetworkGate.PeerCompatible(peer);
        var cut = ZNet.PeersToDisconnectAfterKick.ContainsKey(peer);
        SelfTest.Answer(reply, compatible && !cut,
            compatible
                ? (cut ? "compatible, but a cut still waits for the player" : "compatible, no cut waiting")
                : "not compatible: the player " + (NetworkGate.PeerProblem(peer) ?? "?"));
    }

    // ---------- fishing.mp.no-server ----------

    private static IEnumerator RunMpNoServer() => RunLive(MpNoServerName, false, MpNoServerBody);

    // Server without the mod: this copy is off (the test stays registered for that), fishing is the normal game's.
    private static IEnumerator MpNoServerBody(Checks c, FishRig rig, Stage st, LogTap tap)
    {
        var p = rig.P;
        var found = FeatureRegistry.Find(ModInfo.Guid);
        if (!c.Check(IsClient && found != null, "connected to a server as a client"))
        {
            yield break;
        }
        var me = found.Value;
        c.Check(me.State == nameof(ModState.ServerMissing)
                && me.Status.IndexOf("the server does not have this mod", StringComparison.OrdinalIgnoreCase) >= 0,
            $"the mod turned itself off and says why ({me.State}: {me.Status})");
        c.Check(!OwnPatchOn(typeof(FishingFloat), nameof(FishingFloat.FixedUpdate))
                && !OwnPatchOn(typeof(Fish), nameof(Fish.CustomFixedUpdate)) && !OwnPatchOn(typeof(Hud), nameof(Hud.Update)),
            "none of the mod's patches is on the float, the fish or the HUD");

        // A hooked Perch: no fight, no bar; Block reels it in and it is caught the normal game's way.
        var hook = new Hooked();
        yield return HookNew(rig, st, 1, hook);
        var ff = hook.Float;
        var fish = hook.Fish;
        if (!c.Check(ff != null && fish != null && ReferenceEquals(ff.GetCatch(), fish), "a Perch is hooked on the client's float"))
        {
            yield break;
        }
        var caughtText = Localize("$msg_fishing_catched " + fish.GetHoverName());
        var before = rig.Inv.CountItems(st.FishName);
        var took = hook.Fight != null;
        var bar = false;
        var line0 = ff.m_lineLength;
        rig.Reel(false);
        rig.Fill(p.GetMaxStamina());
        yield return new WaitForSeconds(0.6f);
        took |= Fight.Current != null;
        bar |= FightHud.Shown;
        c.Check(ff != null && Near(ff.m_lineLength, line0, 0.01f), "without Block the line holds (the normal game's reel)");
        var start = Time.time;
        rig.Reel(true);
        while (ff != null && Time.time - start < 25f)
        {
            rig.Fill(p.GetMaxStamina());
            yield return new WaitForFixedUpdate();
            took |= Fight.Current != null;
            bar |= FightHud.Shown;
        }
        rig.Reel(false);
        yield return null;
        c.Check(!took && !bar, "no fight and no catch bar at any time");
        c.Check(ff == null && rig.Inv.CountItems(st.FishName) == before + 1 && CenterText().StartsWith(caughtText, StringComparison.Ordinal),
            $"holding Block reels the fish in and it is caught as in the normal game ({F(Time.time - start)} s, message \"{CenterText()}\")");
    }
}
#endif
