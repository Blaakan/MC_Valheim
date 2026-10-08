#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using BepInEx.Bootstrap;
using MC.Farming.BreedingStarInheritanceMod.Patches;
using MC.Shared;
using UnityEngine;

namespace MC.Farming.BreedingStarInheritanceMod;

// Debug build only. Multiplayer self tests (tools/Test-Multiplayer.ps1): ONE client joined to a real dedicated server.
// Client tests below run on the client; their server halves (steps) run on the server and answer "key=value;..." text.
// No second player exists: "the other game" is stood in for by the server (its copy of the ZDOs, what it sends, what it
// decides about this player). Scenario "modded" (server and client run the mod):
//   breeding.mp.server-log   dedicated server loaded the mod: ready line, Activated, no warning or error of the mod
//   breeding.mp.settings     client uses the server's numbers: join line, 'server settings' births, three settings
//                            changed at once = one push and one line, two settings in one save of the server's config
//                            file = one push and one line, chance and cap of the next births, FarmerRange 10 / 40,
//                            client's own config and file untouched
//   breeding.mp.farmer       published Farming reaches the server's copy of the player: at once, after a raise, and
//                            on the new character after a respawn
//   breeding.mp.handoff      partner note reaches the server's copy (readable there) and its cleared value too; egg
//                            quality 2 hatches and grows at level 2 on the server's copy as well
//   breeding.mp.allow        server switches AllowPlayersWithoutMod on and back off: player with the mod checked
//                            again and kept
//   breeding.mp.server-off   server turns the mod off: client inactive with the "turned off" text, vanilla births;
//                            on again: one settings line, rule back, player with the mod kept
//   breeding.mp.off-stays    client turns the mod off: withdraw reaches the server's copy, player stays connected;
//                            on again: settings line again, Farming published again
//   breeding.mp.off-push     server changes a setting while the client's copy is off: turned on again the copy uses
//                            the server's current numbers and a birth says so
//   breeding.mp.bug.off-push-line  same case, the log line only: no settings line while off, exactly one when turned
//                            on again (KNOWN BUG of the mod, log only: the line comes while off and is missing after)
// Scenario "vanilla-server" (server without the mod; this mod is inactive there, so that test stays registered):
//   breeding.mp.vanilla-server  status text, no patch, nothing published, vanilla births, no note
// Client's own copy is turned off through its Enabled entry (config files of a multiplayer run are throwaway).
internal static partial class SelfTests
{
    private const string MpServerLogName = "breeding.mp.server-log";
    private const string MpSettingsName = "breeding.mp.settings";
    private const string MpFarmerName = "breeding.mp.farmer";
    private const string MpHandoffName = "breeding.mp.handoff";
    private const string MpAllowName = "breeding.mp.allow";
    private const string MpServerOffName = "breeding.mp.server-off";
    private const string MpOffStaysName = "breeding.mp.off-stays";
    private const string MpOffPushName = "breeding.mp.off-push";
    private const string MpBugOffPushLineName = "breeding.mp.bug.off-push-line";
    private const string MpVanillaServerName = "breeding.mp.vanilla-server";

    // Scenario names of tools/Test-Multiplayer.ps1 (SelfTest only names "modded").
    private const string VanillaServerScenario = "vanilla-server";

    private const string StepReset = "breeding.mp.step.reset";
    private const string StepState = "breeding.mp.step.state";
    private const string StepSet = "breeding.mp.step.set";
    private const string StepSetFile = "breeding.mp.step.set-file";
    private const string StepPlayer = "breeding.mp.step.player";
    private const string StepZdo = "breeding.mp.step.zdo";
    private const string StepAllow = "breeding.mp.step.allow";
    private const string StepLog = "breeding.mp.step.log";

    // Server probe's own step: "<GUID>=on|off" = the server owner ticking the mod in the MC Mods panel.
    private const string ProbeSetEnabled = "probe.set-enabled";

    private const string SentLineStart = "Sent the breeding settings to ";
    private const string AllowedLineEnd = " has Breeding Star Inheritance installed: allowed.";
    private const string ServerOffStatus = "Inactive: the server has this mod turned off (or it is not working there).";
    private const string ServerMissingStatus = "Inactive: the server does not have this mod. It must be installed on the server too.";
    private const string DisabledStatus = "Off (disabled in settings).";

    private static readonly string[] SettingKeys =
    {
        "ChanceAtFarming0", "ChanceAtFarming100", "ChanceWithoutFarmer", "FarmerRange", "MaxStars", "AllowPlayersWithoutMod",
    };

    private static void RegisterMultiplayer()
    {
        SelfTest.RegisterMultiplayer(MpServerLogName, SelfTest.Modded, RunMpServerLog);
        SelfTest.RegisterMultiplayer(MpSettingsName, SelfTest.Modded, RunMpSettings);
        SelfTest.RegisterMultiplayer(MpFarmerName, SelfTest.Modded, RunMpFarmer);
        SelfTest.RegisterMultiplayer(MpHandoffName, SelfTest.Modded, RunMpHandoff);
        SelfTest.RegisterMultiplayer(MpAllowName, SelfTest.Modded, RunMpAllow);
        SelfTest.RegisterMultiplayer(MpServerOffName, SelfTest.Modded, RunMpServerOff);
        SelfTest.RegisterMultiplayer(MpOffStaysName, SelfTest.Modded, RunMpOffStays);
        SelfTest.RegisterMultiplayer(MpOffPushName, SelfTest.Modded, RunMpOffPush);
        SelfTest.RegisterMultiplayer(MpBugOffPushLineName, SelfTest.Modded, RunMpBugOffPushLine);
        SelfTest.RegisterMultiplayer(MpVanillaServerName, VanillaServerScenario, RunMpVanillaServer);
        SelfTest.RegisterServerStep(StepReset, ServerReset);
        SelfTest.RegisterServerStep(StepState, ServerState);
        SelfTest.RegisterServerStep(StepSet, ServerSet);
        SelfTest.RegisterServerStep(StepSetFile, ServerSetFile);
        SelfTest.RegisterServerStep(StepPlayer, ServerPlayer);
        SelfTest.RegisterServerStep(StepZdo, ServerZdo);
        SelfTest.RegisterServerStep(StepAllow, ServerAllow);
        SelfTest.RegisterServerStep(StepLog, ServerLog);
    }

    // Me keep the vanilla-server test: on such a server this mod is inactive (OnDeactivated ran) when the probe lists
    // the tests, and that is exactly the state it checks.
    private static void UnregisterMultiplayer()
    {
        SelfTest.UnregisterMultiplayer(MpServerLogName);
        SelfTest.UnregisterMultiplayer(MpSettingsName);
        SelfTest.UnregisterMultiplayer(MpFarmerName);
        SelfTest.UnregisterMultiplayer(MpHandoffName);
        SelfTest.UnregisterMultiplayer(MpAllowName);
        SelfTest.UnregisterMultiplayer(MpServerOffName);
        SelfTest.UnregisterMultiplayer(MpOffStaysName);
        SelfTest.UnregisterMultiplayer(MpOffPushName);
        SelfTest.UnregisterMultiplayer(MpBugOffPushLineName);
        SelfTest.UnregisterServerStep(StepReset);
        SelfTest.UnregisterServerStep(StepState);
        SelfTest.UnregisterServerStep(StepSet);
        SelfTest.UnregisterServerStep(StepSetFile);
        SelfTest.UnregisterServerStep(StepPlayer);
        SelfTest.UnregisterServerStep(StepZdo);
        SelfTest.UnregisterServerStep(StepAllow);
        SelfTest.UnregisterServerStep(StepLog);
    }

    // ---------- server halves ----------

    private static IEnumerator WaitRealtime(float seconds)
    {
        var end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            yield return null;
        }
    }

    private static IEnumerator WaitFor(Func<bool> done, float seconds)
    {
        var end = Time.realtimeSinceStartup + seconds;
        while (!done() && Time.realtimeSinceStartup < end)
        {
            yield return null;
        }
    }

    // The one player of the run, as the server sees it.
    private static ZNetPeer OnlyPeer()
    {
        var net = ZNet.instance;
        if (net == null)
        {
            return null;
        }
        foreach (var peer in net.GetPeers())
        {
            if (peer != null && peer.IsReady())
            {
                return peer;
            }
        }
        return null;
    }

    private static string OwnWire()
    {
        var own = Plugin.OwnSettings();
        return ServerSettings.Encode(own.Rule, own.FarmerRange);
    }

    // Like the server owner changing settings (throwaway config of the run). Unchanged values fire nothing.
    private static void SetServerNumbers(float chance0, float chance100, float chanceWithout, float range, int maxStars)
    {
        Plugin.ChanceAtFarming0.Value = chance0;
        Plugin.ChanceAtFarming100.Value = chance100;
        Plugin.ChanceWithoutFarmer.Value = chanceWithout;
        Plugin.FarmerRange.Value = range;
        Plugin.MaxStars.Value = maxStars;
    }

    private static string LastOr(List<string> lines, string none)
    {
        return lines.Count > 0 ? lines[lines.Count - 1].Replace(';', ',') : none;
    }

    // Default numbers, AllowPlayersWithoutMod off.
    private static IEnumerator ServerReset(string arg, object[] reply)
    {
        var mark = LogMark();
        var d = RuleSettings.Defaults;
        SetServerNumbers(d.ChanceAtFarming0, d.ChanceAtFarming100, d.ChanceWithoutFarmer, 60f, d.MaxStars);
        Plugin.AllowPlayersWithoutMod.Value = false;
        yield return WaitRealtime(0.5f);
        SelfTest.Answer(reply, OwnWire() == "1|15|50|10|60|2" && !Plugin.AllowPlayersWithoutMod.Value,
            $"own={OwnWire()};allow={Plugin.AllowPlayersWithoutMod.Value};sent={CountLines(mark, SentLineStart)}");
    }

    private static IEnumerator ServerState(string arg, object[] reply)
    {
        var net = ZNet.instance;
        yield return null;
        SelfTest.Answer(reply, net != null, $"own={OwnWire()};allow={Plugin.AllowPlayersWithoutMod.Value};dedicated={net != null && net.IsDedicated()};"
                                            + $"peers={(net != null ? net.GetPeers().Count : 0)};state={MyState()};mark={LogMark()}");
    }

    // "c0|c100|cw|range|maxStars": all set in this frame, then one second for the push (and any second push).
    private static IEnumerator ServerSet(string arg, object[] reply)
    {
        var parts = (arg ?? "").Split('|');
        var c = CultureInfo.InvariantCulture;
        if (parts.Length != 5 || !float.TryParse(parts[0], NumberStyles.Float, c, out var c0)
            || !float.TryParse(parts[1], NumberStyles.Float, c, out var c100) || !float.TryParse(parts[2], NumberStyles.Float, c, out var cw)
            || !float.TryParse(parts[3], NumberStyles.Float, c, out var range) || !int.TryParse(parts[4], NumberStyles.Integer, c, out var maxStars))
        {
            SelfTest.Answer(reply, false, $"bad numbers '{arg}'");
            yield break;
        }
        var mark = LogMark();
        SetServerNumbers(c0, c100, cw, range, maxStars);
        yield return WaitRealtime(1f);
        var sent = LinesWith(mark, SentLineStart);
        SelfTest.Answer(reply, true, $"sent={sent.Count};own={OwnWire()};last={LastOr(sent, "none")}");
    }

    // "Key=Value;Key=Value": written into the server's config file in ONE save, like an owner editing the file. The
    // framework's file watcher reloads it; me wait for that, then one second for the push.
    private static IEnumerator ServerSetFile(string arg, object[] reply)
    {
        var path = ConfigPath();
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e)
        {
            SelfTest.Answer(reply, false, $"could not read the config file '{path}': {e.Message}");
            yield break;
        }
        foreach (var kv in Fields(arg))
        {
            var line = new Regex("(?m)^" + Regex.Escape(kv.Key) + " = [^\\r\\n]*");
            if (!line.IsMatch(text))
            {
                SelfTest.Answer(reply, false, $"the config file has no line '{kv.Key} = ...'");
                yield break;
            }
            text = line.Replace(text, kv.Key + " = " + kv.Value, 1);
        }
        var mark = LogMark();
        var wireBefore = OwnWire();
        try
        {
            File.WriteAllText(path, text);
        }
        catch (Exception e)
        {
            SelfTest.Answer(reply, false, $"could not write the config file '{path}': {e.Message}");
            yield break;
        }
        yield return WaitFor(() => OwnWire() != wireBefore, 10f);
        var reloaded = OwnWire() != wireBefore;
        yield return WaitRealtime(1f);
        var sent = LinesWith(mark, SentLineStart);
        SelfTest.Answer(reply, reloaded, $"reloaded={reloaded};sent={sent.Count};own={OwnWire()};last={LastOr(sent, "none")}");
    }

    // "want=<value>;timeout=<s>": wait until the server's copy of the player's character carries this published
    // Farming (-1 = withdrawn). Tells who the player is for the server.
    private static IEnumerator ServerPlayer(string arg, object[] reply)
    {
        var fields = Fields(arg);
        var c = CultureInfo.InvariantCulture;
        if (!float.TryParse(Field(fields, "want"), NumberStyles.Float, c, out var want))
        {
            SelfTest.Answer(reply, false, $"bad argument '{arg}'");
            yield break;
        }
        if (!float.TryParse(Field(fields, "timeout"), NumberStyles.Float, c, out var timeout))
        {
            timeout = 8f;
        }
        var key = FarmerSkill.Key.GetStableHashCode();
        var end = Time.realtimeSinceStartup + timeout;
        var has = false;
        var value = 0f;
        ZNetPeer peer = null;
        while (true)
        {
            peer = OnlyPeer();
            var zdo = peer != null && ZDOMan.instance != null ? ZDOMan.instance.GetZDO(peer.m_characterID) : null;
            has = zdo != null && zdo.GetFloat(key, out value);
            if ((has && value == want) || Time.realtimeSinceStartup > end)
            {
                break;
            }
            yield return null;
        }
        var net = ZNet.instance;
        SelfTest.Answer(reply, has && value == want,
            $"published={(has ? value.ToString(c) : "missing")};character={(peer != null ? peer.m_characterID.ToString() : "none")};"
            + $"peers={(net != null ? net.GetPeers().Count : 0)};hasmod={peer != null && NetworkGate.PeerHasMod(peer)};"
            + $"compatible={peer != null && NetworkGate.PeerCompatible(peer)}");
    }

    // "id=<user>:<n>;want=note|cleared|level:<n>;timeout=<s>": wait until the server's copy of that object shows it.
    //   note    = pregnant, and the partner note belongs to this pregnancy (what another game would read at birth)
    //   cleared = not pregnant, note overwritten with stamp 0
    //   level:n = saved creature level n
    private static IEnumerator ServerZdo(string arg, object[] reply)
    {
        var fields = Fields(arg);
        var idParts = Field(fields, "id").Split(':');
        var want = Field(fields, "want");
        var c = CultureInfo.InvariantCulture;
        if (idParts.Length != 2 || !long.TryParse(idParts[0], NumberStyles.Integer, c, out var user)
            || !uint.TryParse(idParts[1], NumberStyles.Integer, c, out var number) || want.Length == 0)
        {
            SelfTest.Answer(reply, false, $"bad argument '{arg}'");
            yield break;
        }
        if (!float.TryParse(Field(fields, "timeout"), NumberStyles.Float, c, out var timeout))
        {
            timeout = 10f;
        }
        var wantLevel = 0;
        if (want.StartsWith("level:", StringComparison.Ordinal))
        {
            int.TryParse(want.Substring(6), NumberStyles.Integer, c, out wantLevel);
        }
        var id = new ZDOID(user, number);
        var partnerKey = BirthRecord.PartnerLevelKey.GetStableHashCode();
        var end = Time.realtimeSinceStartup + timeout;
        var ok = false;
        var detail = "found=False";
        while (true)
        {
            var zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(id) : null;
            if (zdo != null)
            {
                var pregnant = zdo.GetLong(ZDOVars.s_pregnant, 0L);
                var stored = BirthRecord.StoredStamp(zdo);
                var read = BirthRecord.TryRead(zdo, pregnant, out var partner);
                var level = zdo.GetInt(ZDOVars.s_level, 1);
                if (want == "note")
                {
                    ok = pregnant != 0L && stored == pregnant && read;
                }
                else if (want == "cleared")
                {
                    ok = pregnant == 0L && stored == 0L;
                }
                else
                {
                    ok = wantLevel > 0 && level == wantLevel;
                }
                detail = $"found=True;pregnant={pregnant.ToString(c)};stored={stored.ToString(c)};partner={zdo.GetInt(partnerKey, -1).ToString(c)};"
                         + $"read={(read ? partner.ToString(c) : "none")};level={level.ToString(c)};tamed={zdo.GetBool(ZDOVars.s_tamed)};"
                         + $"owner={zdo.GetOwner().ToString(c)}";
            }
            if (ok || Time.realtimeSinceStartup > end)
            {
                break;
            }
            yield return null;
        }
        SelfTest.Answer(reply, ok, detail);
    }

    // AllowPlayersWithoutMod on, then back off: every connected player is checked again after the grace.
    private static IEnumerator ServerAllow(string arg, object[] reply)
    {
        var mark = LogMark();
        try
        {
            Plugin.AllowPlayersWithoutMod.Value = true;
            yield return WaitRealtime(1.5f);
            Plugin.AllowPlayersWithoutMod.Value = false;
            yield return WaitRealtime(PlayerCheck.GraceSeconds + 1.5f);
        }
        finally
        {
            Plugin.AllowPlayersWithoutMod.Value = false;
        }
        var net = ZNet.instance;
        var allowed = CountLines(mark, AllowedLineEnd);
        var refused = CountLines(mark, "Refused ");
        SelfTest.Answer(reply, true, $"allowed={allowed};refused={refused};without={CountLines(mark, "joined without Breeding Star Inheritance")};"
                                     + $"peers={(net != null ? net.GetPeers().Count : 0)};pending={PlayerCheck.HasWork}");
    }

    // "since=<mark>" (0 or missing = whole session): what this mod logged on the server.
    private static IEnumerator ServerLog(string arg, object[] reply)
    {
        int.TryParse(Field(Fields(arg), "since"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var since);
        var lines = LogSince(since);
        var troubles = Troubles(lines);
        var ready = 0;
        var activated = 0;
        var allowed = 0;
        var refused = 0;
        foreach (var line in lines)
        {
            if (line.Text.StartsWith(Log.ReadyMarker + " " + ModInfo.Guid + " ", StringComparison.Ordinal))
            {
                ready++;
            }
            else if (line.Text == "Activated.")
            {
                activated++;
            }
            else if (line.Text.EndsWith(AllowedLineEnd, StringComparison.Ordinal))
            {
                allowed++;
            }
            else if (line.Text.StartsWith("Refused ", StringComparison.Ordinal))
            {
                refused++;
            }
        }
        var net = ZNet.instance;
        yield return null;
        var first = troubles.Count > 0 ? FirstLine(troubles[0].Text).Replace(';', ',') : "none";
        SelfTest.Answer(reply, true, $"lines={lines.Count};ready={ready};activated={activated};allowed={allowed};refused={refused};"
                                     + $"troubles={troubles.Count};dedicated={net != null && net.IsDedicated()};state={MyState()};first={first}");
    }

    // ---------- client helpers ----------

    private static string ConfigPath()
    {
        return Chainloader.PluginInfos.TryGetValue(ModInfo.Guid, out var info) && info != null && info.Instance != null
            ? info.Instance.Config.ConfigFilePath
            : null;
    }

    // The mod's setting lines of a config file, as text (to prove a file was not touched).
    private static string SettingLines(string path)
    {
        try
        {
            var kept = new List<string>();
            foreach (var line in File.ReadAllLines(path))
            {
                foreach (var key in SettingKeys)
                {
                    if (line.StartsWith(key + " = ", StringComparison.Ordinal))
                    {
                        kept.Add(line.Trim());
                    }
                }
            }
            return string.Join(" | ", kept.ToArray());
        }
        catch (Exception e)
        {
            return "unreadable: " + e.Message;
        }
    }

    private static bool Connected()
    {
        return ZNet.instance != null && !ZNet.instance.IsServer() && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected
               && Player.m_localPlayer != null;
    }

    private static bool UsingServer(in RuleSettings rule, float range)
    {
        var now = Plugin.CurrentSettings();
        return now.Source == SettingsSource.Server && Same(now.Rule, rule) && now.FarmerRange == range;
    }

    private static string SettingsLine(in RuleSettings rule, float range)
    {
        return $"Using the server's breeding settings: {ServerSettings.Describe(rule, range)}. Your own settings apply again in "
               + "single player and when you host.";
    }

    // Start state of every client test: connected, mod on on the server and here, server at its default numbers and
    // the client using them. A test cut short before leaves nothing behind for the next one.
    private static IEnumerator MpPrepare(Checks c, Box box)
    {
        box.Ok = false;
        Override = null;
        RollOverride = null;
        if (!c.Check(Connected(), "this game must be a client connected to a server, with its character in the world"))
        {
            yield break;
        }
        var reply = new SelfTest.ServerReply();
        yield return SelfTest.CallServer(ProbeSetEnabled, ModInfo.Guid + "=on", reply);
        if (!c.Check(reply.Answered && reply.Ok, $"the server could not turn the mod on ({reply})"))
        {
            yield break;
        }
        var enabled = MyEnabled();
        if (enabled != null && !enabled.Value)
        {
            enabled.Value = true;
        }
        yield return WaitFor(MyActive, 10f);
        if (!c.Check(MyActive(), $"this game's copy of the mod must be active before the test (state {MyState()}: {MyStatus()})"))
        {
            yield break;
        }
        reply = new SelfTest.ServerReply();
        yield return SelfTest.CallServer(StepReset, "", reply);
        if (!c.Check(reply.Answered && reply.Ok, $"the server could not go back to its default settings ({reply})"))
        {
            yield break;
        }
        yield return WaitFor(() => UsingServer(RuleSettings.Defaults, 60f), 8f);
        var now = Plugin.CurrentSettings();
        if (!c.Check(UsingServer(RuleSettings.Defaults, 60f),
                $"the client must use the server's default numbers before the test (source {now.Source}, {ServerSettings.Describe(now.Rule, now.FarmerRange)})"))
        {
            yield break;
        }
        box.Ok = true;
    }

    // ---------- breeding.mp.server-log ----------

    private static IEnumerator RunMpServerLog()
    {
        var c = new Checks(MpServerLogName);
        var box = new Box();
        yield return MpPrepare(c, box);
        if (!box.Ok)
        {
            c.Report("");
            yield break;
        }
        var reply = new SelfTest.ServerReply();
        yield return SelfTest.CallServer(StepLog, "", reply);
        if (c.Check(reply.Answered && reply.Ok, $"server log: {reply}"))
        {
            var f = Fields(reply.Detail);
            c.Check(Field(f, "dedicated") == "True", $"the server must be a dedicated server ({reply.Detail})");
            c.Check(Field(f, "state") == nameof(ModState.Active), $"the mod must be active on the server ({reply.Detail})");
            c.Check(Field(f, "ready") == "1", $"the server log must have one '[MC:ready] {ModInfo.Guid}' line ({reply.Detail})");
            c.Check(int.TryParse(Field(f, "activated"), out var activated) && activated >= 1, $"the server log must have the 'Activated.' line ({reply.Detail})");
            c.Check(Field(f, "troubles") == "0", $"the mod logged a warning or error on the server, first: {Field(f, "first")}");
            c.Check(Field(f, "refused") == "0", $"the server refused a player with the mod ({reply.Detail})");
        }
        c.Report($"dedicated server: the mod logged its ready line and 'Activated.', is active, and logged no warning or error this session ({reply.Detail})");
    }

    // ---------- breeding.mp.settings ----------

    private static IEnumerator RunMpSettings()
    {
        var c = new Checks(MpSettingsName);
        var pen = new Pen(MpSettingsName, egg: false);
        try
        {
            var box = new Box();
            yield return MpPrepare(c, box);
            if (!box.Ok)
            {
                c.Report("");
                yield break;
            }
            var player = Player.m_localPlayer;
            var path = ConfigPath();
            var fileBefore = SettingLines(path);
            var ownBefore = Plugin.OwnSettings();
            var farming = FarmerSkill.OwnLevel(player);
            const string server = "server settings.";
            c.Check(CountLines(0, ServerSettingsLineStart + ": ") >= 1, "no 'Using the server's breeding settings' line since this game joined");
            if (PenBlocked(MpSettingsName) || !SetupPen(pen, "Boar", 1, 3))
            {
                yield break;
            }
            yield return null;
            c.Check(pen.A.m_nview.IsOwner() && pen.B.m_nview.IsOwner(), "this game must simulate the pen (own both parents)");

            // 1. Server defaults: the birth uses them and says so.
            RollOverride = 0.999f;
            {
                var chance = BirthRule.Chance(true, farming, RuleSettings.Defaults);
                var w = Begin(c, pen, pen.B, pen.A, "server defaults");
                End(c, pen, pen.B, w, "server defaults", 1, PartnerSource.Recorded, 1, server, $"-> chance {Pct(chance)}%;");
                c.Check(LastBirth.Settings == SettingsSource.Server, $"server defaults: the birth used {LastBirth.Settings} settings");
            }
            RollOverride = null;

            // 2. All chances 100 on the server, three settings changed in one frame: one push, one line here.
            var all100 = new RuleSettings(100f, 100f, 100f, 2);
            var mark = LogMark();
            var reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepSet, "100|100|100|60|2", reply);
            if (!c.Check(reply.Answered && reply.Ok, $"server set (all chances 100): {reply}"))
            {
                c.Report("");
                yield break;
            }
            var f = Fields(reply.Detail);
            c.Check(Field(f, "sent") == "1" && Field(f, "last").EndsWith("to 1 player(s): 1|100|100|100|60|2.", StringComparison.Ordinal),
                $"all chances 100: the server must log exactly one 'Sent the breeding settings to 1 player(s): 1|100|100|100|60|2.' ({reply.Detail})");
            var atOnce = UsingServer(all100, 60f);
            yield return WaitFor(() => UsingServer(all100, 60f), 5f);
            c.Check(atOnce, "all chances 100: the client did not have the new numbers when the server's step returned (a second after the change)");
            c.Check(UsingServer(all100, 60f), $"all chances 100: the client uses {ServerSettings.Describe(Plugin.CurrentSettings().Rule, Plugin.CurrentSettings().FarmerRange)}");
            c.Check(CountLines(mark, ServerSettingsLineStart) == 1 && CountLines(mark, SettingsLine(all100, 60f)) == 1,
                $"all chances 100: exactly one line '{SettingsLine(all100, 60f)}' expected, found {CountLines(mark, ServerSettingsLineStart)} settings line(s)");
            c.Check(Same(Plugin.OwnSettings().Rule, ownBefore.Rule) && !Same(ownBefore.Rule, all100),
                "all chances 100: the client's own settings must stay as they were (and differ from the server's)");
            foreach (var mother in new[] { pen.B, pen.A })
            {
                var mate = Mate(pen, mother);
                var label = $"server all chances 100, own {mother.GetLevel()}";
                var w = Begin(c, pen, mother, mate, label);
                End(c, pen, mother, w, label, 2, PartnerSource.Recorded, mate.GetLevel(), server, "-> chance 100%;", "-> level 2, extra star;");
                c.Check(LastBirth.Settings == SettingsSource.Server && Close(LastBirth.Decision.Chance, 100f),
                    $"{label}: the birth used {LastBirth.Settings} settings, chance {Pct(LastBirth.Decision.Chance)}%");
                yield return null;
            }

            // 3. Two settings changed in one save of the server's config file: one push, one line, next births use them.
            var capped = new RuleSettings(0f, 100f, 100f, 1);
            mark = LogMark();
            reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepSetFile, "ChanceAtFarming0=0;MaxStars=1", reply);
            if (!c.Check(reply.Answered && reply.Ok, $"server config file edit (ChanceAtFarming0 0, MaxStars 1): {reply}"))
            {
                c.Report("");
                yield break;
            }
            f = Fields(reply.Detail);
            c.Check(Field(f, "sent") == "1" && Field(f, "last").EndsWith("to 1 player(s): 1|0|100|100|60|1.", StringComparison.Ordinal),
                $"config file edit: the server must log exactly one 'Sent the breeding settings to 1 player(s): 1|0|100|100|60|1.' ({reply.Detail})");
            atOnce = UsingServer(capped, 60f);
            yield return WaitFor(() => UsingServer(capped, 60f), 5f);
            c.Check(atOnce, "config file edit: the client did not have both new values when the server's step returned");
            c.Check(CountLines(mark, ServerSettingsLineStart) == 1 && CountLines(mark, SettingsLine(capped, 60f)) == 1,
                $"config file edit: exactly one line '{SettingsLine(capped, 60f)}' expected, found {CountLines(mark, ServerSettingsLineStart)} settings line(s)");
            RollOverride = 0.999f;
            {
                var expected = BirthRule.Decide(3, 1, 0, capped, true, farming, 0.999f);
                var label = "server ChanceAtFarming0 0, MaxStars 1, parents 1 + 3";
                var w = Begin(c, pen, pen.B, pen.A, label);
                End(c, pen, pen.B, w, label, expected.Level, PartnerSource.Recorded, 1, server, $"-> chance {Pct(expected.Chance)}%;");
                c.Check(LastBirth.Settings == SettingsSource.Server && LastBirth.Decision.Cap == 2 && Close(LastBirth.Decision.Chance, expected.Chance),
                    $"{label}: settings {LastBirth.Settings}, cap {LastBirth.Decision.Cap}, chance {Pct(LastBirth.Decision.Chance)}%; expected Server, cap 2, {Pct(expected.Chance)}%");
            }
            RollOverride = null;
            pen.A.SetLevel(2);
            pen.B.SetLevel(2);
            {
                var label = "server MaxStars 1, parents 2 + 2";
                var w = Begin(c, pen, pen.B, pen.A, label);
                End(c, pen, pen.B, w, label, 2, PartnerSource.Recorded, 2, server, "-> base 2;", "-> level 2, at the cap: no roll;");
                c.Check(LastBirth.Settings == SettingsSource.Server && LastBirth.Decision.AtCap && LastBirth.Decision.Cap == 2,
                    $"{label}: settings {LastBirth.Settings}, at the cap {LastBirth.Decision.AtCap}, cap {LastBirth.Decision.Cap}");
            }
            yield return null;

            // 4. FarmerRange 10, then 40: each reaches the client (the range other players' Farming counts in).
            foreach (var range in new[] { 10f, 40f })
            {
                mark = LogMark();
                reply = new SelfTest.ServerReply();
                yield return SelfTest.CallServer(StepSet, $"0|100|100|{Inv(range)}|1", reply);
                if (!c.Check(reply.Answered && reply.Ok, $"server set (FarmerRange {Inv(range)}): {reply}"))
                {
                    continue;
                }
                yield return WaitFor(() => UsingServer(capped, range), 5f);
                c.Check(UsingServer(capped, range), $"FarmerRange {Inv(range)}: the client uses FarmerRange {Inv(Plugin.CurrentSettings().FarmerRange)}");
                c.Check(CountLines(mark, SettingsLine(capped, range)) == 1, $"FarmerRange {Inv(range)}: one line '{SettingsLine(capped, range)}' expected");
            }

            // 5. This game's own settings and config file were never touched.
            c.Check(Same(Plugin.OwnSettings().Rule, ownBefore.Rule) && Plugin.OwnSettings().FarmerRange == ownBefore.FarmerRange,
                "the client's own settings changed during the test");
            c.Check(SettingLines(path) == fileBefore, $"the client's config file changed: '{fileBefore}' -> '{SettingLines(path)}'");

            reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepReset, "", reply);
            c.Check(reply.Answered && reply.Ok, $"server back to defaults: {reply}");
            yield return WaitFor(() => UsingServer(RuleSettings.Defaults, 60f), 5f);
            c.Check(UsingServer(RuleSettings.Defaults, 60f), "the client did not follow the server back to its default numbers");
            c.Report($"{pen.Births} births on a client with the server's numbers: defaults ('server settings'), all chances 100 (one push, "
                     + "one line, every baby one level up), ChanceAtFarming0 0 + MaxStars 1 written in one save of the server's "
                     + "config file (one push, one line, chance and cap of the next births), FarmerRange 10 then 40; the client's own "
                     + $"settings ({ServerSettings.Describe(ownBefore.Rule, ownBefore.FarmerRange)}) and config file untouched");
        }
        finally
        {
            RollOverride = null;
            Override = null;
            Cleanup(pen);
        }
    }

    // ---------- breeding.mp.farmer ----------

    private static IEnumerator RunMpFarmer()
    {
        var c = new Checks(MpFarmerName);
        var first = Player.m_localPlayer;
        SkillSnapshot skillsBefore = null;
        try
        {
            var box = new Box();
            yield return MpPrepare(c, box);
            if (!box.Ok)
            {
                c.Report("");
                yield break;
            }
            first = Player.m_localPlayer;
            skillsBefore = SkillSnapshot.Take(first);
            var position = first.transform.position;
            var rotation = first.transform.rotation;
            c.Check(CountLines(0, PublishedLineStart) >= 1, "no 'Published Farming level' line since this game joined");

            // 1. What is on the player object reaches the server's copy.
            FarmerSkill.Reset();
            FarmerSkill.PublishNow();
            var own = FarmerSkill.OwnLevel(first);
            c.Check(FarmerSkill.ReadPublished(first) == own, $"published {Inv(FarmerSkill.ReadPublished(first))}, own Farming {Inv(own)}");
            var reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepPlayer, $"want={Inv(own)};timeout=8", reply);
            var f = Fields(reply.Detail);
            c.Check(reply.Answered && reply.Ok, $"the server's copy of the player must carry the published Farming {Inv(own)} ({reply})");
            c.Check(Field(f, "hasmod") == "True" && Field(f, "character") == first.GetZDOID().ToString(),
                $"the server must know this player with the mod and this character ({reply.Detail}; character {first.GetZDOID()})");

            // 2. A raise goes out with the next breeding tick (the throttled publish a tick does).
            SetFarming(first, 42f);
            FarmerSkill.Reset();
            FarmerSkill.PublishThrottled();
            reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepPlayer, "want=42;timeout=8", reply);
            c.Check(reply.Answered && reply.Ok, $"after a raise to 42 the server's copy must say 42 ({reply})");

            // 3. Respawn: the new character carries the value at once, and the server sees it on the new character.
            SetFarming(first, 23f);
            var firstId = first.GetZDOID();
            var mark = LogMark();
            yield return Respawn(box);
            if (c.Check(box.Ok, $"respawn: {box.Detail}"))
            {
                var now = Player.m_localPlayer;
                c.Check(now.GetZDOID() != firstId && FarmerSkill.ReadPublished(now) == 23f,
                    $"the new character must carry the published Farming 23 at once (found {Inv(FarmerSkill.ReadPublished(now))})");
                c.Check(CountLines(mark, "Published Farming level 23 for other players.") == 1, "one Debug publish line with 23 expected when the character appeared");
                var newId = now.GetZDOID().ToString();
                yield return AfterRespawn(position, rotation);
                reply = new SelfTest.ServerReply();
                yield return SelfTest.CallServer(StepPlayer, "want=23;timeout=10", reply);
                f = Fields(reply.Detail);
                c.Check(reply.Answered && reply.Ok && Field(f, "character") == newId,
                    $"after the respawn the server's copy of the NEW character ({newId}) must say 23 ({reply})");
            }
            c.Check(Connected(), "the connection was lost during the test");
            // Next test get a character that stand (wake-up after respawn over).
            var free = new Box();
            yield return WaitPlayerFree(free, 30f);
            c.Report("the published Farming level reaches the server's copy of the player: as joined, after a raise to 42 (next breeding "
                     + "tick), and on the new character right after a respawn (23, with the Debug publish line)");
        }
        finally
        {
            var now = Player.m_localPlayer;
            if (now != null)
            {
                if (skillsBefore != null)
                {
                    skillsBefore.Restore(now);
                }
                now.SetGodMode(true);
            }
            FarmerSkill.Reset();
            FarmerSkill.PublishNow();
        }
    }

    // ---------- breeding.mp.handoff ----------

    private static IEnumerator RunMpHandoff()
    {
        var c = new Checks(MpHandoffName);
        var boars = new Pen(MpHandoffName, egg: false);
        var hens = new Pen(MpHandoffName, egg: true);
        var extra = new List<GameObject>();
        try
        {
            var box = new Box();
            yield return MpPrepare(c, box);
            if (!box.Ok)
            {
                c.Report("");
                yield break;
            }
            if (PenBlocked(MpHandoffName) || !SetupPen(boars, "Boar", 1, 3))
            {
                yield break;
            }
            yield return null;

            // 1. The partner note travels with the pregnant parent: the server's copy has it, and the mod's own reader
            //    gets partner level 1 from that copy (what another player's game would use at the birth).
            Override = Never; // test numbers: this is about the note, not the server's settings
            var w = Begin(c, boars, boars.B, boars.A, "conception");
            if (w != null)
            {
                var id = boars.B.m_nview.GetZDO().m_uid.ToString();
                var reply = new SelfTest.ServerReply();
                yield return SelfTest.CallServer(StepZdo, $"id={id};want=note;timeout=10", reply);
                var f = Fields(reply.Detail);
                c.Check(reply.Answered && reply.Ok && Field(f, "read") == "1" && Field(f, "stored") == w.Stamp.ToString(CultureInfo.InvariantCulture),
                    $"the server's copy of the pregnant parent must carry the note of this pregnancy with partner level 1 ({reply})");
                // Partner out of the pen range: without the note the baby would be the parent's own level 3.
                Place(boars.A, boars.Center + boars.Side * 25f, boars.Forward);
                End(c, boars, boars.B, w, "birth", 1, PartnerSource.Recorded, 1, "self-test settings.", "own 3, partner 1 (recorded)");
                reply = new SelfTest.ServerReply();
                yield return SelfTest.CallServer(StepZdo, $"id={id};want=cleared;timeout=10", reply);
                f = Fields(reply.Detail);
                c.Check(reply.Answered && reply.Ok && Field(f, "stored") == "0" && Field(f, "partner") == "0",
                    $"after the birth the server's copy must show the note cleared by overwriting (stamp 0, partner 0), not the old one ({reply})");
            }
            Cleanup(boars);
            yield return null;

            // 2. Egg of quality 2: the game's own hatching and growing give level 2, on the server's copy too.
            if (!SetupPen(hens, "Hen", 1, 1))
            {
                yield break;
            }
            yield return null;
            Override = Always;
            w = Begin(c, hens, hens.B, hens.A, "egg");
            End(c, hens, hens.B, w, "egg", 2, PartnerSource.Recorded, 1, out var egg, true, null);
            Override = null;
            yield return null;
            yield return null;
            var chick = egg != null ? HatchNow(c, egg, "hatching", out _, out _, out _) : null;
            if (chick != null)
            {
                extra.Add(chick.gameObject);
                c.Check(chick.GetLevel() == 2, $"the hatchling is level {chick.GetLevel()}, expected 2");
                var chickId = chick.m_nview.GetZDO().m_uid.ToString();
                var reply = new SelfTest.ServerReply();
                yield return SelfTest.CallServer(StepZdo, $"id={chickId};want=level:2;timeout=10", reply);
                c.Check(reply.Answered && reply.Ok, $"the server's copy of the hatchling must be level 2 ({reply})");
                var hen = chick != null ? GrowUp(c, chick, "growing up", out _) : null;
                if (hen != null)
                {
                    extra.Add(hen.gameObject);
                    c.Check(hen.GetLevel() == 2, $"the grown hen is level {hen.GetLevel()}, expected 2");
                    var henId = hen.m_nview.GetZDO().m_uid.ToString();
                    reply = new SelfTest.ServerReply();
                    yield return SelfTest.CallServer(StepZdo, $"id={henId};want=level:2;timeout=10", reply);
                    var f = Fields(reply.Detail);
                    c.Check(reply.Answered && reply.Ok && Field(f, "tamed") == "True", $"the server's copy of the grown hen must be level 2 and tamed ({reply})");
                }
            }
            c.Report("the partner note of a pregnancy reaches the server's copy of the parent and the mod's reader gets partner level 1 "
                     + "from it; the birth (partner moved 25 m away) gives level 1 from that note and the cleared note (stamp 0) reaches "
                     + "the server; a quality-2 egg hatches and grows at level 2, also on the server's copy");
        }
        finally
        {
            Override = null;
            DestroyAll(extra);
            Cleanup(boars);
            Cleanup(hens);
        }
    }

    // ---------- breeding.mp.allow ----------

    private static IEnumerator RunMpAllow()
    {
        var c = new Checks(MpAllowName);
        var box = new Box();
        yield return MpPrepare(c, box);
        if (!box.Ok)
        {
            c.Report("");
            yield break;
        }
        var reply = new SelfTest.ServerReply();
        yield return SelfTest.CallServer(StepAllow, "", reply);
        if (c.Check(reply.Answered && reply.Ok, $"server AllowPlayersWithoutMod on then off: {reply}"))
        {
            var f = Fields(reply.Detail);
            c.Check(int.TryParse(Field(f, "allowed"), out var allowed) && allowed >= 1,
                $"switching AllowPlayersWithoutMod back off must check the connected player again ('... has Breeding Star Inheritance installed: allowed.'; {reply.Detail})");
            c.Check(Field(f, "refused") == "0" && Field(f, "without") == "0", $"a player with the mod must not be refused or reported as without it ({reply.Detail})");
            c.Check(Field(f, "peers") == "1", $"the player must still be connected for the server ({reply.Detail})");
        }
        yield return WaitRealtime(PlayerCheck.DisconnectDelay + 1f);
        c.Check(Connected(), "the player with the mod lost the connection after AllowPlayersWithoutMod was switched back off");
        c.Report($"the server switched AllowPlayersWithoutMod on and back off: the player with the mod was checked again and kept ({reply.Detail})");
    }

    // ---------- breeding.mp.server-off ----------

    private static IEnumerator RunMpServerOff()
    {
        var c = new Checks(MpServerOffName);
        var pen = new Pen(MpServerOffName, egg: false);
        try
        {
            var box = new Box();
            yield return MpPrepare(c, box);
            if (!box.Ok)
            {
                c.Report("");
                yield break;
            }
            var player = Player.m_localPlayer;
            if (PenBlocked(MpServerOffName) || !SetupPen(pen, "Boar", 1, 3))
            {
                yield break;
            }
            yield return null;
            FarmerSkill.Reset();
            FarmerSkill.PublishNow();
            var own = FarmerSkill.OwnLevel(player);
            var reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepState, "", reply);
            var serverMark = Field(Fields(reply.Detail), "mark");
            c.Check(reply.Answered && reply.Ok && serverMark.Length > 0, $"server state: {reply}");

            // 1. The server owner turns the mod off: this game follows live.
            var mark = LogMark();
            reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(ProbeSetEnabled, ModInfo.Guid + "=off", reply);
            if (!c.Check(reply.Answered && reply.Ok, $"the server could not turn the mod off ({reply})"))
            {
                c.Report("");
                yield break;
            }
            yield return WaitFor(() => MyState() == nameof(ModState.ServerMissing), 10f);
            c.Check(MyState() == nameof(ModState.ServerMissing) && MyStatus() == ServerOffStatus,
                $"server off: this game's copy is {MyState()} '{MyStatus()}', expected '{ServerOffStatus}'");
            var patched = OwnPatchedMethods();
            c.Check(patched.Count == 0, $"server off: the mod still patches [{string.Join(", ", patched.ToArray())}]");
            c.Check(FarmerSkill.ReadPublished(player) == FarmerSkill.NotTakingPart && CountLines(mark, WithdrewLine) == 1,
                $"server off: the published Farming must be withdrawn (found {Inv(FarmerSkill.ReadPublished(player))})");
            foreach (var mother in new[] { pen.B, pen.B, pen.A })
            {
                var label = $"server off, own {mother.GetLevel()}";
                var w = Begin(c, pen, mother, Mate(pen, mother), label, noted: false);
                EndVanilla(c, pen, mother, w, label, mother.GetLevel());
            }
            yield return WaitRealtime(PlayerCheck.GraceSeconds + PlayerCheck.DisconnectDelay + 1f);
            c.Check(Connected(), "server off: the connection was lost");

            // 2. Back on: one settings line, the rule is back, and the server keeps the player with the mod.
            mark = LogMark();
            reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(ProbeSetEnabled, ModInfo.Guid + "=on", reply);
            c.Check(reply.Answered && reply.Ok, $"the server could not turn the mod back on ({reply})");
            yield return WaitFor(MyActive, 10f);
            yield return WaitFor(() => UsingServer(RuleSettings.Defaults, 60f), 5f);
            yield return WaitRealtime(PlayerCheck.GraceSeconds + 1.5f);
            c.Check(MyActive() && SamePatches(OwnPatchedMethods()), $"server on again: this game's copy is {MyState()} '{MyStatus()}'");
            c.Check(UsingServer(RuleSettings.Defaults, 60f), $"server on again: settings source {Plugin.CurrentSettings().Source}");
            c.Check(CountLines(mark, ServerSettingsLineStart) == 1,
                $"server on again: exactly one 'Using the server's breeding settings' line expected, found {CountLines(mark, ServerSettingsLineStart)}");
            c.Check(FarmerSkill.ReadPublished(player) == own, $"server on again: the own Farming must be published again (found {Inv(FarmerSkill.ReadPublished(player))})");
            RollOverride = 0.999f;
            {
                var w = Begin(c, pen, pen.B, pen.A, "server on again");
                End(c, pen, pen.B, w, "server on again", 1, PartnerSource.Recorded, 1, "server settings.", "own 3, partner 1 (recorded)");
            }
            RollOverride = null;
            reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepLog, "since=" + serverMark, reply);
            if (c.Check(reply.Answered && reply.Ok, $"server log: {reply}"))
            {
                var f = Fields(reply.Detail);
                c.Check(int.TryParse(Field(f, "allowed"), out var allowed) && allowed >= 1 && Field(f, "refused") == "0",
                    $"server on again: the server must check the connected player again and keep it ('... installed: allowed.', no 'Refused'; {reply.Detail})");
                c.Check(Field(f, "troubles") == "0", $"the mod logged a warning or error on the server while it was turned off and on: {Field(f, "first")}");
            }
            c.Check(Connected(), "server on again: the connection was lost");
            c.Report($"{pen.Births} births while the server turned the mod off and on: off -> this game inactive ('{ServerOffStatus}'), no "
                     + "patch, Farming withdrawn, births at the parent's own level (3, 3, 1), still connected; on -> active, one settings "
                     + "line, Farming published, rule back ('server settings'), and the server checked the player again and kept it");
        }
        finally
        {
            RollOverride = null;
            Override = null;
            Cleanup(pen);
        }
    }

    // ---------- breeding.mp.off-stays ----------

    private static IEnumerator RunMpOffStays()
    {
        var c = new Checks(MpOffStaysName);
        var pen = new Pen(MpOffStaysName, egg: false);
        var enabled = MyEnabled();
        try
        {
            var box = new Box();
            yield return MpPrepare(c, box);
            if (!box.Ok || enabled == null)
            {
                c.Check(enabled != null, "the mod's Enabled setting was not found");
                c.Report("");
                yield break;
            }
            var player = Player.m_localPlayer;
            FarmerSkill.Reset();
            FarmerSkill.PublishNow();
            var own = FarmerSkill.OwnLevel(player);
            var reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepPlayer, $"want={Inv(own)};timeout=8", reply);
            c.Check(reply.Answered && reply.Ok, $"before: the server's copy of the player must carry the published Farming {Inv(own)} ({reply})");

            // 1. This player turns the mod off: withdraw, visible on the server; the player stays connected.
            var mark = LogMark();
            enabled.Value = false;
            yield return null;
            c.Check(MyState() == nameof(ModState.Disabled) && MyStatus() == DisabledStatus, $"turned off: state {MyState()} '{MyStatus()}'");
            c.Check(CountLines(mark, WithdrewLine) == 1 && FarmerSkill.ReadPublished(player) == FarmerSkill.NotTakingPart,
                $"turned off: one withdraw line and -1 on the player object expected (found {Inv(FarmerSkill.ReadPublished(player))})");
            reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepPlayer, "want=-1;timeout=8", reply);
            c.Check(reply.Answered && reply.Ok, $"turned off: the server's copy of the player must say -1 (not a farmer any more) ({reply})");
            // A refusal would come after the 1 s grace and cut the connection 4 s later.
            yield return WaitRealtime(PlayerCheck.GraceSeconds + PlayerCheck.DisconnectDelay + 2f);
            c.Check(Connected(), $"turned off: the player did not stay connected (status {ZNet.GetConnectionStatus()})");
            reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepPlayer, "want=-1;timeout=2", reply);
            var f = Fields(reply.Detail);
            c.Check(reply.Answered && Field(f, "peers") == "1" && Field(f, "hasmod") == "True",
                $"turned off: the server must still have this player, counted as having the mod installed ({reply})");

            // 2. On again: the settings line again, Farming published again.
            mark = LogMark();
            enabled.Value = true;
            yield return WaitFor(MyActive, 10f);
            yield return WaitFor(() => CountLines(mark, ServerSettingsLineStart) >= 1, 6f);
            yield return WaitRealtime(1.5f);
            c.Check(MyActive(), $"turned on again: state {MyState()} '{MyStatus()}'");
            c.Check(CountLines(mark, ServerSettingsLineStart) == 1 && CountLines(mark, SettingsLine(RuleSettings.Defaults, 60f)) == 1,
                $"turned on again: exactly one 'Using the server's breeding settings' line with the server's numbers expected, found {CountLines(mark, ServerSettingsLineStart)}");
            c.Check(UsingServer(RuleSettings.Defaults, 60f), $"turned on again: settings source {Plugin.CurrentSettings().Source}");
            c.Check(FarmerSkill.ReadPublished(player) == own, $"turned on again: the own Farming must be published at once (found {Inv(FarmerSkill.ReadPublished(player))})");
            reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepPlayer, $"want={Inv(own)};timeout=8", reply);
            c.Check(reply.Answered && reply.Ok, $"turned on again: the server's copy of the player must carry Farming {Inv(own)} again ({reply})");

            // 3. A copy turned on while connected decides births with the server's numbers.
            if (!PenBlocked(MpOffStaysName) && SetupPen(pen, "Boar", 1, 3))
            {
                yield return null;
                RollOverride = 0.999f;
                var w = Begin(c, pen, pen.B, pen.A, "turned on while connected");
                End(c, pen, pen.B, w, "turned on while connected", 1, PartnerSource.Recorded, 1, "server settings.", "own 3, partner 1 (recorded)");
                c.Check(LastBirth.Settings == SettingsSource.Server, $"turned on while connected: the birth used {LastBirth.Settings} settings");
                RollOverride = null;
            }
            c.Report("this player turned the mod off: withdraw line, -1 on the server's copy of the player, still connected "
                     + $"{Inv(PlayerCheck.GraceSeconds + PlayerCheck.DisconnectDelay + 2f)} s later and still known to the server as having the mod; on "
                     + "again: one settings line with the server's numbers, Farming on the server's copy again, and a birth with "
                     + "'server settings'");
        }
        finally
        {
            RollOverride = null;
            if (enabled != null && !enabled.Value)
            {
                enabled.Value = true;
            }
            Cleanup(pen);
        }
    }

    // ---------- breeding.mp.off-push ----------

    // Numbers the server change to while this game's copy is off (only ChanceAtFarming0 differ from defaults).
    private static readonly RuleSettings OffPushNumbers = new RuleSettings(25f, 50f, 10f, 2);

    // What OffPushSequence saw.
    private sealed class OffPushRun
    {
        internal bool Done;         // whole sequence ran (prepare ok, server answered)
        internal bool WentOff;      // copy was off before the server changed its number
        internal int LinesWhileOff; // 'Using the server's breeding settings' lines logged while off
        internal readonly List<string> LinesAfterOn = new List<string>(); // same lines after turned on again
    }

    // Same steps for breeding.mp.off-push and breeding.mp.bug.off-push-line: this player turn mod off (Enabled entry),
    // server change ChanceAtFarming0 to 25 meanwhile (push go to every player with mod installed), player turn mod on
    // again. Server left at the changed numbers: caller ask reset when done.
    private static IEnumerator OffPushSequence(Checks c, BepInEx.Configuration.ConfigEntry<bool> enabled, OffPushRun run)
    {
        var box = new Box();
        yield return MpPrepare(c, box);
        if (!box.Ok || enabled == null)
        {
            c.Check(enabled != null, "the mod's Enabled setting was not found");
            yield break;
        }
        enabled.Value = false;
        yield return WaitFor(() => !MyActive(), 5f);
        run.WentOff = !MyActive();
        var markOff = LogMark();
        var reply = new SelfTest.ServerReply();
        yield return SelfTest.CallServer(StepSet, "25|50|10|60|2", reply);
        if (!c.Check(reply.Answered && reply.Ok, $"server set (ChanceAtFarming0 25): {reply}"))
        {
            yield break;
        }
        yield return WaitRealtime(1.5f);
        run.LinesWhileOff = CountLines(markOff, ServerSettingsLineStart);

        var markOn = LogMark();
        enabled.Value = true;
        yield return WaitFor(MyActive, 10f);
        yield return WaitFor(() => UsingServer(OffPushNumbers, 60f), 5f);
        yield return WaitRealtime(2f);
        run.LinesAfterOn.AddRange(LinesWith(markOn, ServerSettingsLineStart));
        run.Done = true;
    }

    // Everything of the case but the log line (that part = breeding.mp.bug.off-push-line): number changed on the server
    // while this copy was off is the one in use after it is on again, and a birth says so.
    private static IEnumerator RunMpOffPush()
    {
        var c = new Checks(MpOffPushName);
        var pen = new Pen(MpOffPushName, egg: false);
        var enabled = MyEnabled();
        try
        {
            var run = new OffPushRun();
            yield return OffPushSequence(c, enabled, run);
            if (!run.Done)
            {
                c.Report("");
                yield break;
            }
            c.Check(run.WentOff, "turned off: this game's copy of the mod was still active");
            c.Check(MyActive(), $"turned on again: state {MyState()} '{MyStatus()}'");
            c.Check(UsingServer(OffPushNumbers, 60f), $"turned on again: the numbers in use must be the server's current ones (source {Plugin.CurrentSettings().Source}, "
                                                      + $"{ServerSettings.Describe(Plugin.CurrentSettings().Rule, Plugin.CurrentSettings().FarmerRange)})");
            var chance = -1f;
            if (!PenBlocked(MpOffPushName) && SetupPen(pen, "Boar", 1, 3))
            {
                yield return null;
                chance = BirthRule.Chance(true, FarmerSkill.OwnLevel(Player.m_localPlayer), OffPushNumbers);
                RollOverride = 0.999f;
                var w = Begin(c, pen, pen.B, pen.A, "turned on after the change");
                End(c, pen, pen.B, w, "turned on after the change", 1, PartnerSource.Recorded, 1, "server settings.", $"-> chance {Pct(chance)}%;");
                c.Check(LastBirth.Settings == SettingsSource.Server && Close(LastBirth.Decision.Chance, chance),
                    $"turned on after the change: the birth used {LastBirth.Settings} settings, chance {Pct(LastBirth.Decision.Chance)}%; expected Server, {Pct(chance)}%");
                RollOverride = null;
            }
            var reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepReset, "", reply);
            c.Check(reply.Answered && reply.Ok, $"server back to defaults: {reply}");
            c.Report("the server changed ChanceAtFarming0 to 25 while this game's copy was off: turned on again the copy is active, uses "
                     + $"the server's current numbers, and a birth has chance {Pct(chance)}% with 'server settings' (the log line of this "
                     + $"case is checked by {MpBugOffPushLineName})");
        }
        finally
        {
            RollOverride = null;
            if (enabled != null && !enabled.Value)
            {
                enabled.Value = true;
            }
            Cleanup(pen);
        }
    }

    // ---------- breeding.mp.bug.off-push-line ----------

    // KNOWN BUG of the mod (log only), alone here so it block nothing else: client handler stay on the connection while
    // the copy is off, so the server's push is logged as "Using the server's breeding settings" by a copy that is off;
    // turned on again the answer has the same text and the line that should come is left out.
    private static IEnumerator RunMpBugOffPushLine()
    {
        var c = new Checks(MpBugOffPushLineName);
        var enabled = MyEnabled();
        try
        {
            var run = new OffPushRun();
            yield return OffPushSequence(c, enabled, run);
            if (!run.Done)
            {
                c.Report("");
                yield break;
            }
            c.Check(run.WentOff, "turned off: this game's copy of the mod was still active");
            c.Check(run.LinesWhileOff == 0,
                "while the mod is turned off on this game its log must not say 'Using the server's breeding settings' (births here are "
                + $"vanilla then); found {run.LinesWhileOff} such line(s)");
            c.Check(run.LinesAfterOn.Count == 1 && run.LinesAfterOn[0] == SettingsLine(OffPushNumbers, 60f),
                $"turned on again: exactly one line '{SettingsLine(OffPushNumbers, 60f)}' expected, found {run.LinesAfterOn.Count}");
            var reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StepReset, "", reply);
            c.Check(reply.Answered && reply.Ok, $"server back to defaults: {reply}");
            c.Report("server setting changed while this game's copy was off: no settings line while off, exactly one with the current "
                     + "numbers when turned on again");
        }
        finally
        {
            if (enabled != null && !enabled.Value)
            {
                enabled.Value = true;
            }
        }
    }

    // ---------- breeding.mp.vanilla-server ----------

    private static IEnumerator RunMpVanillaServer()
    {
        var c = new Checks(MpVanillaServerName);
        var pen = new Pen(MpVanillaServerName, egg: false);
        try
        {
            Override = null;
            RollOverride = null;
            if (!Connected())
            {
                SelfTest.Fail(MpVanillaServerName, "this game must be a client connected to a server, with its character in the world");
                yield break;
            }
            var player = Player.m_localPlayer;
            c.Check(MyState() == nameof(ModState.ServerMissing) && MyStatus() == ServerMissingStatus,
                $"on a server without the mod this game's copy is {MyState()} '{MyStatus()}', expected '{ServerMissingStatus}'");
            var patched = OwnPatchedMethods();
            c.Check(patched.Count == 0, $"the mod still patches [{string.Join(", ", patched.ToArray())}]");
            c.Check(FarmerSkill.ReadPublished(player) == FarmerSkill.NotTakingPart,
                $"nothing may be published on the player object on a server without the mod (found {Inv(FarmerSkill.ReadPublished(player))})");
            c.Check(Plugin.CurrentSettings().Source == SettingsSource.Own && CountLines(0, ServerSettingsLineStart) == 0,
                "no server settings can exist on a server without the mod");
            if (PenBlocked(MpVanillaServerName) || !SetupPen(pen, "Boar", 1, 3))
            {
                yield break;
            }
            yield return null;
            Override = Never; // would give level 1 if the mod acted
            foreach (var mother in new[] { pen.B, pen.B, pen.A })
            {
                var label = $"server without the mod, own {mother.GetLevel()}";
                var w = Begin(c, pen, mother, Mate(pen, mother), label, noted: false);
                EndVanilla(c, pen, mother, w, label, mother.GetLevel());
            }
            Override = null;
            c.Check(BirthRecord.StoredStamp(pen.A.m_nview.GetZDO()) == -1L && BirthRecord.StoredStamp(pen.B.m_nview.GetZDO()) == -1L,
                "a partner note was written on a server without the mod");
            yield return WaitRealtime(1f);
            c.Check(Connected(), "the connection was lost");
            c.Report($"{pen.Births} births on a server without the mod: this game's copy is inactive ('{ServerMissingStatus}'), patches "
                     + "nothing, publishes nothing, and births follow the game's own rule (levels 3, 3, 1) with no note");
        }
        finally
        {
            Override = null;
            Cleanup(pen);
        }
    }
}
#endif
