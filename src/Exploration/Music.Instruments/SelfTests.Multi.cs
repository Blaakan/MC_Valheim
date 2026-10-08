#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.MusicInstrumentsMod;

// Debug build only. Self tests of a multiplayer run (tools/Test-Multiplayer.ps1, scenario "modded": this game is a
// client joined to a real dedicated server, both with every MC mod and default settings). Client tests below; their
// server halves ("music.mp.srv.*") run on the server and answer with "key=value" lines.
// There is one client and no second player: what "a friend" would hear or see is stood in for by what the server
// takes, stamps and holds (its counters, its copy of the player's data), never by another screen.
//   music.mp.rules          M01 M08  the server's rules reach the client: pending before, log line, recipe, comfort
//   music.mp.playersongs    M15 (T27)  AllowPlayerSongs = false as a real server setting: hint, own files refused
//   music.mp.relay          M02 M03 M04 M05 M11 M18  what the server takes from a performance (who, where, which notes)
//   music.mp.sharesetting   (T26 T28)  ShareSongs and ServerSongsFolder as real server settings: shared or not
//   music.mp.songs          M12 M14  server songs: list, no download on moving, download on a pick, big file
//   music.mp.songs.changes  M13 M15  cache, changed file, deleted file, sharing off mid-download, own songs off
//   music.mp.settings       T15  personal settings changed in the real config (this run's config is a throwaway)
//   music.mp.oldconfig      T29  a config file with the old SuccessAccuracy line: RequiredAccuracy stays 0.5
//   music.mp.server         M09  the dedicated server: no error, no sound, knows the items, takes notes, keeps a drop
// Server settings are changed through the server's real config entries and put back by "reset" / "clean" (each test
// asks for that first too, in case the one before stopped half way).
internal static partial class SelfTests
{
    private const string MpRulesName = "music.mp.rules";
    private const string MpRelayName = "music.mp.relay";
    private const string MpSongsName = "music.mp.songs";
    private const string MpChangesName = "music.mp.songs.changes";
    private const string MpSettingsName = "music.mp.settings";
    private const string MpServerName = "music.mp.server";
    private const string MpPlayerSongsName = "music.mp.playersongs";
    private const string MpShareSettingName = "music.mp.sharesetting";
    private const string MpOldConfigName = "music.mp.oldconfig";

    private static readonly string[] MpNames =
    {
        MpRulesName, MpPlayerSongsName, MpRelayName, MpShareSettingName, MpSongsName, MpChangesName, MpSettingsName, MpOldConfigName, MpServerName,
    };

    private const string SrvState = "music.mp.srv.state";
    private const string SrvSet = "music.mp.srv.set";
    private const string SrvSongs = "music.mp.srv.songs";
    private const string SrvDrops = "music.mp.srv.drops";

    private static void RegisterMulti()
    {
        SelfTest.RegisterMultiplayer(MpRulesName, SelfTest.Modded, RunMpRules);
        SelfTest.RegisterMultiplayer(MpPlayerSongsName, SelfTest.Modded, RunMpPlayerSongs);
        SelfTest.RegisterMultiplayer(MpRelayName, SelfTest.Modded, RunMpRelay);
        SelfTest.RegisterMultiplayer(MpShareSettingName, SelfTest.Modded, RunMpShareSetting);
        SelfTest.RegisterMultiplayer(MpSongsName, SelfTest.Modded, RunMpSongs);
        SelfTest.RegisterMultiplayer(MpChangesName, SelfTest.Modded, RunMpChanges);
        SelfTest.RegisterMultiplayer(MpSettingsName, SelfTest.Modded, RunMpSettings);
        SelfTest.RegisterMultiplayer(MpOldConfigName, SelfTest.Modded, RunMpOldConfig);
        // Last: it also asks the server for every warning it logged during the run.
        SelfTest.RegisterMultiplayer(MpServerName, SelfTest.Modded, RunMpServer);
        SelfTest.RegisterServerStep(SrvState, ServerState);
        SelfTest.RegisterServerStep(SrvSet, ServerSet);
        SelfTest.RegisterServerStep(SrvSongs, ServerSongs);
        SelfTest.RegisterServerStep(SrvDrops, ServerDrops);
    }

    private static void UnregisterMulti()
    {
        foreach (var name in MpNames)
        {
            SelfTest.UnregisterMultiplayer(name);
        }
        foreach (var name in new[] { SrvState, SrvSet, SrvSongs, SrvDrops })
        {
            SelfTest.UnregisterServerStep(name);
        }
    }

    // ---------- server halves ----------

    private static ZNetPeer FirstPeer()
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

    private static string I(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    // What the server knows now: its copy of the player, what it took from performances, its rules, its songs, itself.
    private static IEnumerator ServerState(string arg, object[] reply)
    {
        yield return null;
        var net = ZNet.instance;
        if (net == null || !net.IsServer())
        {
            SelfTest.Answer(reply, false, "not a server");
            yield break;
        }
        var sb = new StringBuilder();
        void Put(string key, object value) => sb.Append(key).Append('=').Append(value).Append('\n');
        var peer = FirstPeer();
        var playing = -1;
        var owned = false;
        var where = "";
        if (peer != null && !peer.m_characterID.IsNone() && ZDOMan.instance != null)
        {
            var zdo = ZDOMan.instance.GetZDO(peer.m_characterID);
            if (zdo != null)
            {
                playing = zdo.GetInt(InstrumentPose.PlayingKey);
                owned = zdo.GetOwner() == peer.m_uid;
                where = I(zdo.GetPosition().x) + "," + I(zdo.GetPosition().z);
            }
        }
        Put("peers", net.GetPeers().Count);
        Put("char", peer != null ? peer.m_characterID.ToString() : "");
        Put("name", peer != null ? peer.m_playerName : "");
        Put("compatible", peer != null && NetworkGate.PeerCompatible(peer));
        Put("playing", playing);
        Put("owned", owned);
        Put("where", where);
        Put("batches", NoteRelay.SrvBatches);
        Put("notes", NoteRelay.SrvNotes);
        Put("offs", NoteRelay.SrvNoteOffs);
        Put("live", NoteRelay.SrvLive);
        Put("encores", NoteRelay.SrvEncores);
        Put("ends", NoteRelay.SrvEnds);
        Put("empty", NoteRelay.SrvEmpty);
        Put("performer", NoteRelay.SrvPerformer.ToString());
        Put("at", I(NoteRelay.SrvPosition.x) + "," + I(NoteRelay.SrvPosition.z));
        Put("instrument", NoteRelay.SrvInstrument);
        Put("relayed", NoteRelay.RelayedCount);
        Put("rules", ServerRules.Current.Describe());
        Put("sharing", SongShare.Sharing);
        Put("shared", SongShare.DescribeShared());
        Put("transfers", SongShare.TransferCount);
        Put("sends", SongShare.SendsStarted);
        Put("pieces", SongShare.PiecesSent);
        Put("queueAtSend", SongShare.MaxQueueAtSend);
        Put("queueSeen", SongShare.MaxQueueSeen);
        Put("dedicated", net.IsDedicated());
        Put("headless", SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null);
        Put("audio", AudioKit.Available);
        Put("emitters", EmitterCount());
        Put("windows", SongWindow.Built || MiniGameHud.Built);
        var prefabs = ZNetScene.instance != null && ObjectDB.instance != null;
        var recipes = 0;
        foreach (var kind in InstrumentContent.Kinds)
        {
            var name = InstrumentContent.ItemName(kind);
            prefabs = prefabs && ZNetScene.instance.GetPrefab(name) != null && ObjectDB.instance.GetItemPrefab(name) != null;
            var recipe = InstrumentContent.RecipeOf(kind);
            if (recipe != null && recipe.m_enabled)
            {
                recipes++;
            }
        }
        Put("prefabs", prefabs);
        Put("recipes", recipes);
        Put("effect", ObjectDB.instance != null && ObjectDB.instance.GetStatusEffect(InstrumentContent.EffectHash) != null);
        var bad = new List<string>();
        foreach (var line in LogTap.ProblemsSince(0))
        {
            if (!LogTap.Expected(line))
            {
                bad.Add(line);
            }
        }
        Put("problems", bad.Count);
        Put("problem1", bad.Count > 0 ? bad[0].Replace('\n', ' ') : "");
        SelfTest.Answer(reply, true, sb.ToString());
    }

    // Server owner changes settings: "Key=value;Key=value" on the server's own config entries, or "reset" (defaults).
    private static IEnumerator ServerSet(string arg, object[] reply)
    {
        yield return null;
        var inv = CultureInfo.InvariantCulture;
        try
        {
            foreach (var raw in (arg ?? "").Split(';'))
            {
                var part = raw.Trim();
                if (part.Length == 0)
                {
                    continue;
                }
                var eq = part.IndexOf('=');
                var key = eq < 0 ? part : part.Substring(0, eq);
                var value = eq < 0 ? "" : part.Substring(eq + 1);
                switch (key)
                {
                    case "reset":
                        Plugin.ComfortBonus.Value = (int)Plugin.ComfortBonus.DefaultValue;
                        Plugin.SuccessSeconds.Value = (float)Plugin.SuccessSeconds.DefaultValue;
                        Plugin.SuccessAccuracy.Value = (float)Plugin.SuccessAccuracy.DefaultValue;
                        Plugin.BonusMinutes.Value = (float)Plugin.BonusMinutes.DefaultValue;
                        Plugin.BonusRange.Value = (float)Plugin.BonusRange.DefaultValue;
                        Plugin.HearingRange.Value = (float)Plugin.HearingRange.DefaultValue;
                        Plugin.FluteResources.Value = (string)Plugin.FluteResources.DefaultValue;
                        Plugin.AllowPlayerSongs.Value = (bool)Plugin.AllowPlayerSongs.DefaultValue;
                        break;
                    case "ComfortBonus":
                        Plugin.ComfortBonus.Value = int.Parse(value, inv);
                        break;
                    case "SuccessSeconds":
                        Plugin.SuccessSeconds.Value = float.Parse(value, inv);
                        break;
                    case "BonusMinutes":
                        Plugin.BonusMinutes.Value = float.Parse(value, inv);
                        break;
                    case "HearingRange":
                        Plugin.HearingRange.Value = float.Parse(value, inv);
                        break;
                    case "FluteRecipe":
                        Plugin.FluteResources.Value = value;
                        break;
                    case "AllowPlayerSongs":
                        Plugin.AllowPlayerSongs.Value = bool.Parse(value);
                        break;
                    default:
                        SelfTest.Answer(reply, false, "unknown setting " + key);
                        yield break;
                }
            }
        }
        catch (Exception e)
        {
            SelfTest.Answer(reply, false, "could not set '" + arg + "': " + e.Message);
            yield break;
        }
        SelfTest.Answer(reply, true, MusicRules.Own().Describe());
    }

    private const string MpShareFolder = "MC_MusicMpShare";
    internal const string MpSmall = "selftest small";
    internal const string MpMid = "selftest mid";
    internal const string MpOther = "selftest other";
    internal const string MpExtra = "selftest extra";
    internal const string MpBig = "selftest big";
    internal const string MpHuge = "selftest huge";

    // Server owner and the server songs folder: "make" (a temp folder with six files, ShareSongs on), "replace" (the
    // small file gets other notes, same name), "delete:<name>", "off", "on", "clean" (off, folder setting back, gone).
    private static IEnumerator ServerSongs(string arg, object[] reply)
    {
        yield return null;
        var folder = Path.Combine(Path.GetTempPath(), MpShareFolder);
        try
        {
            if (arg == "make")
            {
                TempFolder(MpShareFolder);
                File.WriteAllBytes(Path.Combine(folder, MpSmall + ".mid"), MidiOf(Preset("greensleeves")));
                File.WriteAllBytes(Path.Combine(folder, MpMid + ".mid"), PadMidi(MidiOf(Preset("ravens-jig")), 200 * 1024));
                File.WriteAllBytes(Path.Combine(folder, MpOther + ".mid"), MidiOf(Preset("drunken-sailor")));
                File.WriteAllBytes(Path.Combine(folder, MpExtra + ".mid"), MidiOf(Preset("hearthfire-lullaby")));
                File.WriteAllBytes(Path.Combine(folder, MpBig + ".mid"), PadMidi(MidiOf(Preset("vem-kan-segla")), 1050 * 1024));
                File.WriteAllBytes(Path.Combine(folder, MpHuge + ".mid"), PadMidi(MidiOf(Preset("row-the-longship")), 1900 * 1024));
                Plugin.ServerSongsFolder.Value = folder;
                Plugin.ShareSongs.Value = true;
                SongShare.TestScan();
            }
            else if (arg == "replace")
            {
                File.WriteAllBytes(Path.Combine(folder, MpSmall + ".mid"), MidiOf(Preset("mead-hall-reel")));
                SongShare.TestScan();
            }
            else if (arg != null && arg.StartsWith("delete:", StringComparison.Ordinal))
            {
                File.Delete(Path.Combine(folder, arg.Substring(7) + ".mid"));
            }
            else if (arg == "off")
            {
                Plugin.ShareSongs.Value = false;
            }
            else if (arg == "on")
            {
                Plugin.ShareSongs.Value = true;
                SongShare.TestScan();
            }
            else if (arg == "clean")
            {
                Plugin.ShareSongs.Value = false;
                Plugin.ServerSongsFolder.Value = (string)Plugin.ServerSongsFolder.DefaultValue;
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
            else
            {
                SelfTest.Answer(reply, false, "unknown command " + arg);
                yield break;
            }
        }
        catch (Exception e)
        {
            SelfTest.Answer(reply, false, "songs '" + arg + "' failed: " + e.Message);
            yield break;
        }
        SelfTest.Answer(reply, true, SongShare.DescribeShared());
    }

    // Instruments lying in the world as the server holds them (its data objects, kept in the world save).
    private static IEnumerator ServerDrops(string arg, object[] reply)
    {
        yield return null;
        if (ZDOMan.instance == null || ZNetScene.instance == null)
        {
            SelfTest.Answer(reply, false, "no world data");
            yield break;
        }
        var hashes = new HashSet<int>();
        foreach (var kind in InstrumentContent.Kinds)
        {
            hashes.Add(InstrumentContent.ItemHash(kind));
        }
        var found = 0;
        var kept = 0;
        var known = 0;
        foreach (var pair in ZDOMan.instance.m_objectsByID)
        {
            var zdo = pair.Value;
            if (zdo == null || !hashes.Contains(zdo.GetPrefab()))
            {
                continue;
            }
            found++;
            if (zdo.Persistent)
            {
                kept++;
            }
            if (ZNetScene.instance.HasPrefab(zdo.GetPrefab()))
            {
                known++;
            }
        }
        SelfTest.Answer(reply, true, $"found={found}\nkept={kept}\nknown={known}\n");
    }

    // ---------- client helpers ----------

    private static Dictionary<string, string> Lines(string detail)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in (detail ?? "").Split('\n'))
        {
            var eq = line.IndexOf('=');
            if (eq > 0)
            {
                values[line.Substring(0, eq)] = line.Substring(eq + 1);
            }
        }
        return values;
    }

    private static string Val(Dictionary<string, string> values, string key) => values.TryGetValue(key, out var v) ? v : "";

    private static int Num(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : int.MinValue;

    // Ask a server half; false in ok[0] (and a failed check) when it did not answer or said no.
    private static IEnumerator Ask(Checks c, string step, string arg, Dictionary<string, string> into, bool[] ok = null)
    {
        var reply = new SelfTest.ServerReply();
        yield return SelfTest.CallServer(step, arg, reply);
        var good = c.Check(reply.Answered && reply.Ok, $"server step {step}({arg}): {(reply.Answered && reply.Ok ? "ok" : reply.ToString())}");
        if (ok != null)
        {
            ok[0] = good;
        }
        if (into != null)
        {
            into.Clear();
            if (good)
            {
                foreach (var pair in Lines(reply.Detail))
                {
                    into[pair.Key] = pair.Value;
                }
                into["detail"] = reply.Detail;
            }
        }
    }

    // Ask the server's state again and again until it says what is wanted (its copy of our data lags a moment).
    private static IEnumerator Until(Checks c, Dictionary<string, string> state, Func<Dictionary<string, string>, bool> done, float timeout)
    {
        var end = Time.realtimeSinceStartup + timeout;
        var ok = new bool[1];
        while (true)
        {
            var reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(SrvState, "", reply);
            ok[0] = reply.Answered && reply.Ok;
            if (!ok[0])
            {
                c.Check(false, "server state: " + reply);
                state.Clear();
                yield break;
            }
            state.Clear();
            foreach (var pair in Lines(reply.Detail))
            {
                state[pair.Key] = pair.Value;
            }
            if (done(state) || Time.realtimeSinceStartup >= end)
            {
                yield break;
            }
            yield return Real(0.3f);
        }
    }

    private static bool IsClient => ZNet.instance != null && !ZNet.instance.IsServer() && ZNet.instance.GetServerPeer() != null;

    // "name=hash:size;" list of the server: id ("server:<hash>") and size of one song.
    private static bool Shared(string described, string name, out string id, out int size)
    {
        id = null;
        size = 0;
        foreach (var part in (described ?? "").Split(';'))
        {
            var eq = part.LastIndexOf('=');
            if (eq <= 0 || part.Substring(0, eq) != name)
            {
                continue;
            }
            var colon = part.IndexOf(':', eq);
            if (colon < 0)
            {
                continue;
            }
            id = "server:" + part.Substring(eq + 1, colon - eq - 1);
            return int.TryParse(part.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out size);
        }
        return false;
    }

    private static string SizeText(int bytes) => bytes < 1024 ? bytes + " bytes" : (bytes / 1024).ToString(CultureInfo.InvariantCulture) + " KB";

    // Percent in "Downloading from the server (42 %)." (-1 = none).
    private static int Percent(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return -1;
        }
        var end = text.IndexOf(" %", StringComparison.Ordinal);
        var start = end > 0 ? text.LastIndexOf('(', end) : -1;
        return start >= 0 && int.TryParse(text.Substring(start + 1, end - start - 1), out var p) ? p : -1;
    }

    // Song window opened, with the server's list in it (asked for at most every three seconds: me wait that out).
    private static IEnumerator OpenWithServerList(Checks c, bool wantSection)
    {
        yield return Real(3.2f);
        yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
        Performance.TestRequestOpen();
        yield return Frames(3);
        if (!c.Check(SongWindow.IsOpen, "song window open"))
        {
            yield break;
        }
        yield return WaitReal(() => SongWindow.TestItems().Contains("header|Server songs") == wantSection && SongShare.ListKnown, 6f);
    }

    // ---------- music.mp.rules (M01, M08) ----------

    private static IEnumerator RunMpRules()
    {
        var c = new Checks(MpRulesName);
        var player = Player.m_localPlayer;
        if (player == null || !IsClient)
        {
            SelfTest.Fail(MpRulesName, "not a client of a server");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            PadInput.Test = false;
            var state = new Dictionary<string, string>();
            yield return Ask(c, SrvSet, "reset", null);
            yield return Ask(c, SrvSongs, "clean", null);
            yield return Until(c, state, s => Val(s, "rules") == ServerRules.Current.Describe(), 6f);
            c.Check(ServerRules.UsingServer && !ServerRules.IsPending, "the server's rules arrived when joining");
            c.Check(Val(state, "rules") == ServerRules.Current.Describe(), "the rules in use here are the server's: " + Val(state, "rules"));
            c.Check(Val(state, "dedicated") == "True" && Val(state, "compatible") == "True", "dedicated server, this player compatible");
            var seman = player.GetSEMan();
            seman.RemoveStatusEffect(InstrumentContent.EffectHash, true);
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);

            // Before the rules arrive (the state right after joining): nothing to craft, no playing, the message.
            // First let the copy the server sends after its "reset" above pass (PushDelay after the change), so it
            // cannot bring the rules back in the middle of the waiting part.
            yield return Real(ServerRules.PushDelay + 0.4f);
            ServerRules.Forget();
            c.Check(ServerRules.IsPending && !ServerRules.UsingServer, "no rules from the server yet: waiting");
            yield return new WaitForSeconds(InstrumentContent.RebuildDelaySeconds + 0.4f);
            var hidden = true;
            foreach (var kind in InstrumentContent.Kinds)
            {
                hidden &= !InstrumentContent.RecipeOf(kind).m_enabled;
            }
            c.Check(hidden, "no instrument recipe while waiting");
            Seen.Clear();
            yield return Click(rig);
            c.Check(!SongWindow.IsOpen && !Performance.WindowOpen, "left click opens no window while waiting");
            c.Check(Seen.Count(MessageHud.MessageType.TopLeft, "The server has not sent the instrument settings yet.") == 1, "message: " + Seen.Tail());
            c.Check(!MusicBonus.Grant(player, false) && !seman.HaveStatusEffect(InstrumentContent.EffectHash), "no Music effect while waiting");

            // The server has its own settings; the client asks for them (what it does right after the handshake).
            var info = LogTap.InfoMark;
            yield return Ask(c, SrvSet, "ComfortBonus=5;HearingRange=20;FluteRecipe=Wood:1;SuccessSeconds=6", state);
            ServerRules.Request(ZNet.instance.GetServerPeer().m_rpc);
            yield return WaitReal(() => ServerRules.UsingServer && ServerRules.Current.ComfortBonus == 5, 10f);
            var rules = ServerRules.Current;
            c.Check(ServerRules.UsingServer && rules.ComfortBonus == 5 && Mathf.Approximately(rules.HearingRange, 20f) && rules.FluteResources == "Wood:1"
                    && Mathf.Approximately(rules.SuccessSeconds, 6f), "the server's rules are in use: " + rules.Describe());
            c.Check(LogTap.InfoSince(info, "Using the server's rules: flute Wood:1 at piece_workbench level 1;"), "log line \"Using the server's rules: flute Wood:1...\"");
            // The recipes are made again once the rules stayed still for RebuildDelay s. The server sends its rules a
            // second time half a second after its last setting changed (ServerRules.PushDelay), which starts that wait
            // again: me wait for the rebuild itself, not for a fixed time (first run: checked 0.9 s after the first
            // copy, before the rebuild).
            var flute = InstrumentContent.RecipeOf(InstrumentKind.Flute);
            yield return WaitReal(() => !InstrumentContent.RebuildPending && flute.m_enabled, 8f);
            var cost = flute.m_resources != null && flute.m_resources.Length > 0 && flute.m_resources[0] != null && flute.m_resources[0].m_resItem != null
                ? flute.m_resources[0].m_resItem.name + ":" + flute.m_resources[0].m_amount + (flute.m_resources.Length > 1 ? ",..." : "")
                : "nothing";
            c.Check(flute.m_enabled && flute.m_resources.Length == 1 && flute.m_resources[0].m_resItem.name == "Wood" && flute.m_resources[0].m_amount == 1
                    && flute.m_craftingStation != null && flute.m_craftingStation.name == MusicRules.Workbench && flute.m_minStationLevel == 1,
                $"the workbench flute costs 1 Wood (recipe on {flute.m_enabled}, costs {cost}, level {flute.m_minStationLevel}, rebuild waiting {InstrumentContent.RebuildPending})");
            // The rules are here: the click opens the window.
            yield return OpenWindow(rig);
            c.Check(SongWindow.IsOpen, "with the rules here the click opens the window");
            Performance.CloseWindow();
            yield return Frames(3);

            // Their Encore gives +5.
            yield return WaitFor(() => player.m_comfortLevel > 0, 4f);
            var baseComfort = player.m_comfortLevel;
            Seen.Clear();
            c.Check(Performance.StartMiniGame(Preset("kjerringa-med-staven"), -1, out var error), "performs: " + error);
            yield return Frames(2);
            var game = Performance.Game;
            var pressed = new HashSet<long>();
            var banner = "";
            var began = Time.realtimeSinceStartup;
            while (game != null && Time.realtimeSinceStartup - began < 30f && Performance.Mode == PerformanceMode.MiniGame && banner.Length == 0)
            {
                PressDue(game, pressed, 0);
                if (game.Encores > 0)
                {
                    yield return null;
                    banner = MiniGameHud.EncoreShown;
                    break;
                }
                yield return null;
            }
            var effect = seman.GetStatusEffect(InstrumentContent.EffectHash);
            c.Check(game != null && game.Encores == 1 && banner == "Encore! +5 comfort", $"Encore: '{banner}'");
            c.Check(effect != null && effect.GetIconText().StartsWith("+5  ", StringComparison.Ordinal) && player.GetComfortLevel() == baseComfort + 5,
                $"it gives +5 comfort ({baseComfort} -> {player.GetComfortLevel()})");
            c.Check(Seen.Count(MessageHud.MessageType.TopLeft, "+5 comfort.") == 1, "message: " + Seen.Tail());
            Performance.TestRequestStop();
            yield return Frames(3);
            seman.RemoveStatusEffect(InstrumentContent.EffectHash, true);

            // Server settings back.
            yield return Ask(c, SrvSet, "reset", state);
            yield return WaitReal(() => ServerRules.Current.Describe() == MusicRules.Default.Describe(), 8f);
            c.Check(ServerRules.Current.Describe() == MusicRules.Default.Describe(), "server settings put back, the player follows: " + ServerRules.Current.Describe());
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.mp.playersongs (rule side of M15; setting side of T27) ----------

    // AllowPlayerSongs = false in the server's own config: the rule reaches the player while connected; own MIDI files
    // are refused and hidden, built-in songs play.
    private static IEnumerator RunMpPlayerSongs()
    {
        var c = new Checks(MpPlayerSongsName);
        var player = Player.m_localPlayer;
        if (player == null || !IsClient)
        {
            SelfTest.Fail(MpPlayerSongsName, "not a client of a server");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            PadInput.Test = false;
            yield return Ask(c, SrvSet, "reset", null);
            yield return Ask(c, SrvSongs, "clean", null);
            yield return WaitReal(() => ServerRules.UsingServer && ServerRules.Current.AllowPlayerSongs, 8f);
            c.Check(ServerRules.UsingServer && ServerRules.Current.AllowPlayerSongs, "server default: own MIDI songs allowed");
            var own = x.SongsFolder("MC_MusicMpOwn");
            File.WriteAllBytes(Path.Combine(own, "selftest own.mid"), MidiOf(Preset("greensleeves")));
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            yield return Ask(c, SrvSet, "AllowPlayerSongs=false", null);
            yield return WaitReal(() => !ServerRules.Current.AllowPlayerSongs, 8f);
            c.Check(!ServerRules.Current.AllowPlayerSongs && ServerRules.UsingServer, "the server's AllowPlayerSongs = false reaches the player while connected");
            var files = SongLibrary.ScanMidiFolder(out _);
            var refused = "";
            c.Check(files.Count == 1 && !Performance.StartAuto(files[0], -1, out refused) && refused == Performance.PlayerSongsOff, "own MIDI file refused: " + refused);
            yield return OpenWindow(rig);
            var items = SongWindow.TestItems();
            var midi = items.IndexOf("header|Your MIDI songs");
            c.Check(SongWindow.IsOpen && midi >= 0 && midi + 1 < items.Count && items[midi + 1] == "hint|" + Performance.PlayerSongsOff
                    && !items.Exists(i => i.StartsWith("song|midi:", StringComparison.Ordinal)), "the window shows the hint instead of own files");
            c.Check(SongWindow.TestSelect("preset:greensleeves") && SongWindow.TestPress("Play"), "a built-in song, Play");
            yield return Frames(3);
            c.Check(Performance.Mode == PerformanceMode.Auto, "built-in songs play");
            Performance.TestRequestStop();
            yield return Frames(3);
            yield return Ask(c, SrvSet, "reset", null);
            yield return WaitReal(() => ServerRules.Current.AllowPlayerSongs, 8f);
            c.Check(ServerRules.Current.AllowPlayerSongs, "put back on the server: allowed again here");
            yield return OpenWindow(rig);
            c.Check(SongWindow.TestItems().Contains("song|midi:selftest own.mid|selftest own"), "own files are listed again");
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.mp.sharesetting (setting side of T26 and T28) ----------

    // ShareSongs and ServerSongsFolder as real settings of the server: on = its folder's songs are offered to the
    // player's window, off = not.
    private static IEnumerator RunMpShareSetting()
    {
        var c = new Checks(MpShareSettingName);
        var player = Player.m_localPlayer;
        if (player == null || !IsClient)
        {
            SelfTest.Fail(MpShareSettingName, "not a client of a server");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            PadInput.Test = false;
            var state = new Dictionary<string, string>();
            yield return Ask(c, SrvSet, "reset", null);
            yield return Ask(c, SrvSongs, "clean", null);
            yield return Until(c, state, s => Val(s, "sharing") == "False", 3f);
            c.Check(Val(state, "sharing") == "False" && Val(state, "shared").Length == 0, "server default: nothing shared");
            yield return Ask(c, SrvSongs, "make", null);
            yield return Until(c, state, s => Val(s, "sharing") == "True", 3f);
            var shared = Val(state, "shared");
            var six = true;
            foreach (var name in new[] { MpSmall, MpMid, MpOther, MpExtra, MpBig, MpHuge })
            {
                six &= Shared(shared, name, out _, out _);
            }
            c.Check(Val(state, "sharing") == "True" && six, "ShareSongs = true and ServerSongsFolder set on the server: it shares that folder's six files");
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            yield return OpenWithServerList(c, true);
            c.Check(SongWindow.TestItems().Contains("header|Server songs") && SongShare.ListShared, "the player's window gets the \"Server songs\" section");
            Performance.CloseWindow();
            yield return Frames(3);
            yield return Ask(c, SrvSongs, "off", null);
            yield return Until(c, state, s => Val(s, "sharing") == "False", 3f);
            c.Check(Val(state, "sharing") == "False", "ShareSongs = false on the server: it shares nothing");
            yield return OpenWithServerList(c, false);
            c.Check(!SongWindow.TestItems().Contains("header|Server songs"), "and the section is gone from the window");
            Performance.CloseWindow();
            yield return Frames(3);
            yield return Ask(c, SrvSongs, "clean", null);
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.mp.oldconfig (config side of T29) ----------

    // A config file from an older version (it has a SuccessAccuracy line): the new setting RequiredAccuracy keeps its
    // default 0.5 and the old line is ignored. Written to this run's throwaway config file.
    private static IEnumerator RunMpOldConfig()
    {
        var c = new Checks(MpOldConfigName);
        if (!SelfTest.IsMultiplayerRun || Plugin.SuccessAccuracy == null)
        {
            SelfTest.Fail(MpOldConfigName, "not a multiplayer run (this test writes the config file)");
            yield break;
        }
        var entry = Plugin.SuccessAccuracy;
        var file = entry.ConfigFile;
        var path = file.ConfigFilePath;
        c.Check(entry.Definition.Key == "RequiredAccuracy" && entry.Definition.Section == Plugin.ComfortSection, "the setting is called RequiredAccuracy");
        c.Check(Mathf.Approximately((float)entry.DefaultValue, 0.5f), "its default is 0.5");
        var text = File.ReadAllText(path);
        var section = text.IndexOf("[" + Plugin.ComfortSection + "]", StringComparison.Ordinal);
        if (c.Check(section >= 0 && text.IndexOf("SuccessAccuracy", StringComparison.Ordinal) < 0, "config file has the Comfort section and no SuccessAccuracy line"))
        {
            var lineEnd = text.IndexOf('\n', section);
            text = text.Substring(0, lineEnd + 1) + "\nSuccessAccuracy = 0.7\n" + text.Substring(lineEnd + 1);
            File.WriteAllText(path, text);
            file.Reload();
            yield return Frames(3);
            c.Check(Mathf.Approximately(entry.Value, 0.5f) && Mathf.Approximately(MusicRules.Own().SuccessAccuracy, 0.5f),
                $"with an old SuccessAccuracy = 0.7 line in the file, RequiredAccuracy is 0.5 ({F(entry.Value)}): the old line is ignored");
            c.Check(File.ReadAllText(path).Contains("RequiredAccuracy = 0.5"), "RequiredAccuracy = 0.5 is in the config file");
        }
        c.Report();
    }

    // ---------- music.mp.relay (server side of M02 M03 M04 M05 M11 M18) ----------

    private static IEnumerator RunMpRelay()
    {
        var c = new Checks(MpRelayName);
        var player = Player.m_localPlayer;
        if (player == null || !IsClient)
        {
            SelfTest.Fail(MpRelayName, "not a client of a server");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            PadInput.Test = false;
            FreePlayKeys.ClearTest();
            Performance.Repeat = false;
            var state = new Dictionary<string, string>();
            yield return Ask(c, SrvSongs, "clean", null);
            yield return Ask(c, SrvSet, "reset;SuccessSeconds=6", null);
            yield return WaitReal(() => ServerRules.UsingServer && Mathf.Approximately(ServerRules.Current.SuccessSeconds, 6f), 8f);
            c.Check(ServerRules.UsingServer && Mathf.Approximately(ServerRules.Current.SuccessSeconds, 6f), "rules from the server (Encore after 6 s for this test)");
            player.GetSEMan().RemoveStatusEffect(InstrumentContent.EffectHash, true);
            var me = player.GetZDOID().ToString();
            var flute = rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            yield return Until(c, state, s => Num(s, "playing") == 0, 5f);
            var batches = Num(state, "batches");
            var notes = Num(state, "notes");
            var live = Num(state, "live");
            var ends = Num(state, "ends");
            var relayed = Num(state, "relayed");
            c.Check(Val(state, "char") == me && Val(state, "owned") == "True" && Num(state, "peers") == 1, $"the server knows this player's character ({Val(state, "char")})");

            // A song playing by itself: the server takes the notes, says who plays (from the connection) and where;
            // its copy of the player carries the playing flag (what poses the arms on other games, also for late joiners).
            var played = Performance.NotesPlayed;
            c.Check(Performance.StartAuto(Preset("greensleeves"), -1, out var error), "plays: " + error);
            yield return new WaitForSeconds(2.5f);
            yield return Until(c, state, s => Num(s, "playing") == (int)InstrumentKind.Flute && Num(s, "notes") > notes, 6f);
            c.Check(Num(state, "batches") > batches && Num(state, "notes") > notes, $"the server gets the notes ({Num(state, "notes") - notes} so far)");
            c.Check(Val(state, "performer") == me && Val(state, "instrument") == InstrumentKind.Flute.ToString(), "stamped with this player and the flute");
            c.Check(Num(state, "live") == live, "as a song playing by itself (not live)");
            c.Check(Num(state, "playing") == (int)InstrumentKind.Flute, "the server's copy of the player says: playing the flute");
            var at = Val(state, "at").Split(',');
            c.Check(at.Length == 2 && float.TryParse(at[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var ax)
                    && float.TryParse(at[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var az)
                    && Mathf.Abs(ax - player.transform.position.x) < 5f && Mathf.Abs(az - player.transform.position.z) < 5f,
                "from where the player stands: " + Val(state, "at"));
            c.Check(Num(state, "relayed") == relayed, "nobody else is near: passed on to nobody");
            Performance.TestRequestStop();
            yield return Frames(3);
            var sent = Performance.NotesPlayed - played;
            yield return Until(c, state, s => Num(s, "playing") == 0 && Num(s, "ends") > ends, 6f);
            c.Check(Num(state, "playing") == 0 && Num(state, "ends") == ends + 1, "stopped: the flag is cleared on the server, the end is told");
            c.Check(Num(state, "notes") - notes == sent, $"every note sent arrived ({Num(state, "notes") - notes} of {sent})");

            // Rhythm game: only the notes hit arrive, live; the Encore passes once.
            notes = Num(state, "notes");
            live = Num(state, "live");
            var encores = Num(state, "encores");
            var chart = Performance.ChartNotesPlayed;
            c.Check(Performance.StartMiniGame(Preset("kjerringa-med-staven"), -1, out error), "performs: " + error);
            yield return Frames(2);
            var game = Performance.Game;
            var pressed = new HashSet<long>();
            var began = Time.realtimeSinceStartup;
            while (game != null && Time.realtimeSinceStartup - began < 28f && Performance.Mode == PerformanceMode.MiniGame && game.Encores == 0)
            {
                PressDue(game, pressed, 2);
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);
            var hits = game != null ? game.Judge.Hits : 0;
            var misses = game != null ? game.Judge.Misses : 0;
            var mine = game != null ? game.Encores : 0;
            Performance.TestRequestStop();
            yield return Frames(3);
            var hitNotes = Performance.ChartNotesPlayed - chart;
            yield return Until(c, state, s => Num(s, "notes") - notes >= hitNotes && Num(s, "playing") == 0, 6f);
            c.Check(hits >= 6 && misses >= 4 && mine == 1, $"every other note hit, Encore ({hits} hits, {misses} misses)");
            c.Check(Num(state, "notes") - notes == hitNotes && hitNotes > 0, $"the server got the notes of the hits only ({Num(state, "notes") - notes} of {hitNotes} hit notes)");
            c.Check(Num(state, "live") > live, "flagged live");
            c.Check(Num(state, "encores") == encores + 1, "the Encore passed the server once (players within range get Music from it)");

            // Free play: a held note's key up arrives as a note off; while it is held, empty batches keep it alive.
            notes = Num(state, "notes");
            var offs = Num(state, "offs");
            var empty = Num(state, "empty");
            c.Check(Performance.StartFreePlay(out error), "free play: " + error);
            yield return Frames(3);
            FreePlayKeys.TestDown[12] = true;
            yield return new WaitForSeconds(3.6f);
            yield return Until(c, state, s => Num(s, "empty") >= empty + 2, 3f);
            c.Check(Num(state, "notes") == notes + 1 && Num(state, "offs") == offs && Num(state, "empty") >= empty + 2,
                $"a held note: one note on, then a batch every second while it is held ({Num(state, "empty") - empty} empty ones)");
            c.Check(Num(state, "playing") == (int)InstrumentKind.Flute, "playing flag set for free play");
            FreePlayKeys.TestUp[12] = true;
            yield return new WaitForSeconds(0.5f);
            yield return Until(c, state, s => Num(s, "offs") == offs + 1, 4f);
            c.Check(Num(state, "offs") == offs + 1, "key up: the note off reaches the server (the note ends for listeners too)");
            Performance.TestRequestStop();
            yield return Frames(3);
            FreePlayKeys.ClearTest();

            // Own MIDI file (the server has no such file): its notes arrive, also across a long note and a rest.
            var folder = x.SongsFolder("MC_MusicMpRelay");
            var song = new List<Note>();
            AddRun(song, 0.3f, 2, 0.5f);
            song.Add(new Note(1.5f, 4f, 79, 100));
            AddRun(song, 8.5f, 2, 0.4f);
            File.WriteAllBytes(Path.Combine(folder, "selftest long.mid"), MidiFrom(song));
            var list = SongLibrary.ScanMidiFolder(out _);
            yield return Until(c, state, s => Num(s, "playing") == 0, 4f);
            notes = Num(state, "notes");
            empty = Num(state, "empty");
            ends = Num(state, "ends");
            c.Check(list.Count == 1 && Performance.StartAuto(list[0], -1, out error), "own MIDI file plays: " + error);
            yield return WaitReal(() => Performance.Mode == PerformanceMode.None, 14f);
            yield return Until(c, state, s => Num(s, "ends") > ends, 5f);
            c.Check(Num(state, "notes") - notes == song.Count, $"all its notes reached the server ({Num(state, "notes") - notes} of {song.Count})");
            c.Check(Num(state, "empty") - empty >= 4, $"through the long note and the rest a batch still came every second ({Num(state, "empty") - empty} empty ones)");

            // Putting the instrument away mid-song: the flag goes on the server too (arms down for everyone).
            c.Check(Performance.StartAuto(Preset("greensleeves"), -1, out error), "plays once more: " + error);
            yield return Until(c, state, s => Num(s, "playing") == (int)InstrumentKind.Flute, 6f);
            c.Check(Num(state, "playing") == (int)InstrumentKind.Flute, "flag set");
            player.UnequipItem(flute, false);
            yield return Frames(3);
            yield return Until(c, state, s => Num(s, "playing") == 0, 6f);
            c.Check(Performance.Mode == PerformanceMode.None && Num(state, "playing") == 0, "instrument put away: flag cleared on the server");
            yield return Ask(c, SrvSet, "reset", null);
            c.Report();
        }
        finally
        {
            FreePlayKeys.ClearTest();
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.mp.songs (M12, M14) ----------

    private static IEnumerator RunMpSongs()
    {
        var c = new Checks(MpSongsName);
        var player = Player.m_localPlayer;
        if (player == null || !IsClient)
        {
            SelfTest.Fail(MpSongsName, "not a client of a server");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            PadInput.Test = false;
            Performance.Repeat = false;
            var state = new Dictionary<string, string>();
            var ok = new bool[1];
            yield return Ask(c, SrvSet, "reset", null);
            yield return Ask(c, SrvSongs, "make", state, ok);
            var described = Val(state, "detail");
            if (!ok[0] || !Shared(described, MpSmall, out var smallId, out var smallSize) || !Shared(described, MpMid, out var midId, out var midSize)
                || !Shared(described, MpOther, out var otherId, out _) || !Shared(described, MpBig, out var bigId, out var bigSize)
                || !Shared(described, MpHuge, out var hugeId, out _) || !Shared(described, MpExtra, out var extraId, out _))
            {
                c.Check(false, "the server shares the six test songs: " + described);
                c.Report();
                yield break;
            }
            c.Check(midSize > 16 * 1024 && bigSize > 1024 * 1024 && bigSize < 2 * 1024 * 1024, $"one song over 16 KB ({midSize} bytes), one between 1 and 2 MB ({bigSize} bytes)");
            // Own folder: a file of the player, selected when the window opens (the server's rows come above it).
            var own = x.SongsFolder("MC_MusicMpSongsOwn");
            File.WriteAllBytes(Path.Combine(own, "selftest own.mid"), MidiOf(Preset("greensleeves")));
            const string ownId = "midi:selftest own.mid";
            Performance.LastSongId = ownId;
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            yield return Real(3.2f);
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            Performance.TestRequestOpen();
            yield return Frames(2);
            c.Check(SongWindow.IsOpen && SongWindow.SelectedId == ownId, "window open, own file selected");
            yield return WaitReal(() => SongWindow.TestItems().Contains("header|Server songs"), 6f);
            var items = SongWindow.TestItems();
            c.Note("list: " + string.Join(" / ", items.ToArray()));
            var header = items.IndexOf("header|Server songs");
            var midi = items.IndexOf("header|Your MIDI songs");
            var lastPreset = items.IndexOf("song|" + SongLibrary.Presets[SongLibrary.Presets.Count - 1].Id + "|" + SongLibrary.Presets[SongLibrary.Presets.Count - 1].Title);
            c.Check(header == lastPreset + 1 && midi == header + 7, "\"Server songs\" with the six files, between the built-in songs and \"Your MIDI songs\"");
            var all = true;
            foreach (var pair in new[] { new[] { smallId, MpSmall }, new[] { midId, MpMid }, new[] { otherId, MpOther }, new[] { extraId, MpExtra }, new[] { bigId, MpBig }, new[] { hugeId, MpHuge } })
            {
                var at = items.IndexOf("song|" + pair[0] + "|" + pair[1]);
                all &= at > header && at < midi;
            }
            c.Check(all, "each file listed with its name");
            c.Check(SongWindow.SelectedId == ownId && SongWindow.SelectedInView, "the selected song stays selected and in view when the list arrives");
            c.Check(SongWindow.TestRowInfo(smallId) == "Server MIDI, " + SizeText(smallSize) && SongWindow.TestRowInfo(midId) == "Server MIDI, " + SizeText(midSize),
                "the line under a song shows its size: " + SongWindow.TestRowInfo(midId));

            // Moving over them with the arrow keys downloads nothing.
            var requests = SongShare.RequestsMade;
            for (var i = 0; i < 6; i++)
            {
                yield return KeyStep(KeyCode.UpArrow);
                yield return Real(0.35f);
            }
            var untouched = SongShare.RequestsMade == requests;
            foreach (var id in new[] { smallId, midId, otherId, extraId, bigId, hugeId })
            {
                untouched &= (SongWindow.TestRowInfo(id) ?? "").StartsWith("Server MIDI, ", StringComparison.Ordinal);
            }
            c.Check(SongWindow.SelectedId != ownId && SongWindow.SelectedId.StartsWith("server:", StringComparison.Ordinal), "the arrow keys moved onto the server songs: " + SongWindow.SelectedId);
            c.Check(untouched, "moving over them downloads nothing");

            // A click downloads: "Downloading...", a percentage, then parts and length.
            c.Check(SongWindow.TestClickRow(midId), "the bigger song clicked");
            var details = SongWindow.DetailsText ?? "";
            c.Check(details.Contains("Downloading from the server"), "details line: " + details);
            var percent = false;
            var began = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - began < 20f)
            {
                details = SongWindow.DetailsText ?? "";
                percent |= Percent(details) >= 0;
                if (details.Contains(" parts, ") || details.Contains(" part, "))
                {
                    break;
                }
                yield return null;
            }
            c.Check(percent, "a percentage while it comes");
            c.Check(details.Contains("Server MIDI, ") && details.Contains(":") && (details.Contains(" parts, ") || details.Contains(" part, ")),
                $"then its parts and length ({F(Time.realtimeSinceStartup - began)} s): {details}");
            c.Check(SongShare.RequestsMade == requests + 1, "one download asked for");
            SongEntry midEntry = null;
            foreach (var e in SongShare.ClientList)
            {
                if (e.Id == midId)
                {
                    midEntry = e;
                }
            }
            var arranged = midEntry != null;
            foreach (var kind in InstrumentContent.Kinds)
            {
                arranged = arranged && SongLibrary.Arrange(midEntry, kind, -1, out var part, out _, out _) && part.Length > 8;
            }
            c.Check(arranged, "the downloaded song has a part for each instrument (flute, lyre, tambourine)");
            c.Check(SongWindow.TestPress("Play"), "Play");
            yield return Frames(3);
            c.Check(Performance.Mode == PerformanceMode.Auto && Performance.LastSongId == midId, "Play works");
            Performance.TestRequestStop();
            yield return Frames(3);
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            Performance.TestRequestOpen();
            yield return Frames(3);
            c.Check(SongWindow.IsOpen && SongWindow.SelectedId == midId && SongWindow.TestPress("Perform"), "window again on that song, Perform");
            yield return Frames(3);
            c.Check(Performance.Mode == PerformanceMode.MiniGame, "Perform works");
            Performance.TestRequestStop();
            yield return Frames(3);

            // Play on a song not downloaded yet (selected with the keys): it starts by itself when it is here.
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            Performance.TestRequestOpen();
            yield return Frames(3);
            var ids = SongIds();
            var want = ids.IndexOf(otherId);
            for (var i = 0; i < 12 && SongWindow.SelectedId != otherId; i++)
            {
                yield return KeyStep(ids.IndexOf(SongWindow.SelectedId) < want ? KeyCode.DownArrow : KeyCode.UpArrow);
            }
            requests = SongShare.RequestsMade;
            c.Check(SongWindow.SelectedId == otherId && (SongWindow.TestRowInfo(otherId) ?? "").StartsWith("Server MIDI, ", StringComparison.Ordinal), "another server song selected with the keys, not downloaded");
            c.Check(SongWindow.TestPress("Play"), "Play pressed on it");
            yield return WaitReal(() => Performance.Mode == PerformanceMode.Auto, 12f);
            c.Check(Performance.Mode == PerformanceMode.Auto && Performance.LastSongId == otherId && !SongWindow.IsOpen && SongShare.RequestsMade == requests + 1,
                "it downloads and starts by itself");
            Performance.TestRequestStop();
            yield return Frames(3);

            // The big file: progress up to the end, then it plays; the server sent each piece only into a short queue.
            yield return Until(c, state, s => true, 1f);
            var pieces = Num(state, "pieces");
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            Performance.TestRequestOpen();
            yield return Frames(3);
            c.Check(SongWindow.IsOpen && SongWindow.TestClickRow(bigId), "the 1 MB song clicked");
            var last = -1;
            var steady = true;
            var steps = 0;
            var done = false;
            began = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - began < 60f)
            {
                details = SongWindow.DetailsText ?? "";
                if (details.Contains(" parts, ") || details.Contains(" part, "))
                {
                    done = true;
                    break;
                }
                var p = Percent(details);
                if (p >= 0)
                {
                    steady &= p >= last;
                    if (p != last)
                    {
                        steps++;
                    }
                    last = p;
                }
                yield return null;
            }
            var took = Time.realtimeSinceStartup - began;
            c.Check(done && steady && steps >= 10 && last >= 90, $"the progress goes up to the end ({steps} steps, last {last} %, {F(took)} s)");
            c.Check(SongWindow.TestPress("Play"), "Play");
            yield return Frames(3);
            c.Check(Performance.Mode == PerformanceMode.Auto && Performance.LastSongId == bigId, "and the song plays");
            Performance.TestRequestStop();
            yield return Frames(3);
            yield return Until(c, state, s => Num(s, "transfers") == 0, 3f);
            var sentPieces = Num(state, "pieces") - pieces;
            c.Note($"server: {sentPieces} pieces, longest queue a piece went into {Num(state, "queueAtSend")} bytes, longest seen {Num(state, "queueSeen")} bytes");
            c.Check(sentPieces == (bigSize + SongShare.ChunkBytes - 1) / SongShare.ChunkBytes, $"sent in pieces of {SongShare.ChunkBytes} bytes ({sentPieces})");
            c.Check(Num(state, "queueAtSend") >= 0 && Num(state, "queueAtSend") <= SongShare.QueueLimit,
                $"each piece went out only while that connection's send queue was short (at most {Num(state, "queueAtSend")} of {SongShare.QueueLimit} bytes): world data keeps room");
            c.Check(ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected, "still connected");

            // ShareSongs off on the server: no section.
            yield return Ask(c, SrvSongs, "off", null);
            yield return OpenWithServerList(c, false);
            items = SongWindow.TestItems();
            c.Check(SongWindow.IsOpen && !items.Contains("header|Server songs") && !items.Exists(i => i.StartsWith("song|server:", StringComparison.Ordinal))
                    && SongShare.ListKnown && !SongShare.ListShared, "ShareSongs off on the server: no \"Server songs\" section");
            Performance.CloseWindow();
            yield return Frames(3);
            yield return Ask(c, SrvSongs, "clean", null);
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.mp.songs.changes (M13, window side of M15) ----------

    private static IEnumerator WaitDownloaded(string id, float timeout)
    {
        var end = Time.realtimeSinceStartup + timeout;
        while (Time.realtimeSinceStartup < end)
        {
            var info = SongWindow.TestRowInfo(id) ?? "";
            if (info.Contains(" parts, ") || info.Contains(" part, "))
            {
                yield break;
            }
            yield return null;
        }
    }

    private static bool Downloaded(string id)
    {
        var info = SongWindow.TestRowInfo(id) ?? "";
        return info.Contains(" parts, ") || info.Contains(" part, ");
    }

    private static IEnumerator RunMpChanges()
    {
        var c = new Checks(MpChangesName);
        var player = Player.m_localPlayer;
        if (player == null || !IsClient)
        {
            SelfTest.Fail(MpChangesName, "not a client of a server");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            PadInput.Test = false;
            Performance.Repeat = false;
            Performance.LastSongId = null;
            var state = new Dictionary<string, string>();
            var ok = new bool[1];
            yield return Ask(c, SrvSet, "reset", null);
            yield return Ask(c, SrvSongs, "make", state, ok);
            var described = Val(state, "detail");
            if (!ok[0] || !Shared(described, MpSmall, out var smallId, out _) || !Shared(described, MpExtra, out var extraId, out _)
                || !Shared(described, MpHuge, out var hugeId, out _))
            {
                c.Check(false, "the server shares the test songs: " + described);
                c.Report();
                yield break;
            }
            var own = x.SongsFolder("MC_MusicMpChangesOwn");
            File.WriteAllBytes(Path.Combine(own, "selftest own.mid"), MidiOf(Preset("greensleeves")));
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            yield return OpenWithServerList(c, true);
            c.Check(SongWindow.TestItems().Contains("song|" + smallId + "|" + MpSmall), "the server's songs are listed");

            // Picked, downloaded; picked again: at once, no download.
            var requests = SongShare.RequestsMade;
            c.Check(SongWindow.TestClickRow(smallId), "a song clicked");
            yield return WaitDownloaded(smallId, 10f);
            c.Check(Downloaded(smallId) && SongShare.RequestsMade == requests + 1, "downloaded");
            Performance.CloseWindow();
            yield return Frames(3);
            yield return OpenWithServerList(c, true);
            requests = SongShare.RequestsMade;
            c.Check(SongWindow.TestClickRow(smallId) && Downloaded(smallId) && SongShare.RequestsMade == requests, "the same song again: at once, no download");
            Performance.CloseWindow();
            yield return Frames(3);

            // The server's file replaced by another version (same name): listed as a new song, downloads again.
            yield return Ask(c, SrvSongs, "replace", state, ok);
            var newId = "";
            c.Check(ok[0] && Shared(Val(state, "detail"), MpSmall, out newId, out _) && newId != smallId, "the server has another version under the same name");
            yield return OpenWithServerList(c, true);
            yield return WaitReal(() => SongWindow.TestItems().Contains("song|" + newId + "|" + MpSmall), 6f);
            var items = SongWindow.TestItems();
            c.Check(items.Contains("song|" + newId + "|" + MpSmall) && !items.Contains("song|" + smallId + "|" + MpSmall), "the window lists the new version, not the old one");
            requests = SongShare.RequestsMade;
            c.Check(SongWindow.TestClickRow(newId), "the new version clicked");
            c.Check((SongWindow.DetailsText ?? "").Contains("Downloading from the server"), "it downloads: " + SongWindow.DetailsText);
            yield return WaitDownloaded(newId, 10f);
            c.Check(Downloaded(newId) && SongShare.RequestsMade == requests + 1, "the new version is here");

            // A song deleted on the server while the window lists it: picking it says so; a downloaded one still plays.
            yield return Ask(c, SrvSongs, "delete:" + MpExtra, null);
            c.Check(SongWindow.TestItems().Contains("song|" + extraId + "|" + MpExtra) && SongWindow.TestClickRow(extraId), "the deleted song is still listed here; clicked");
            yield return WaitReal(() => (SongWindow.TestRowInfo(extraId) ?? "").Contains("no longer shares"), 6f);
            c.Check((SongWindow.TestRowInfo(extraId) ?? "").StartsWith("The server no longer shares this song", StringComparison.Ordinal)
                    && (SongWindow.DetailsText ?? "").Contains("The server no longer shares this song"), "it says: " + SongWindow.TestRowInfo(extraId));
            c.Check(SongWindow.TestClickRow(newId) && SongWindow.TestPress("Play"), "the downloaded one, Play");
            yield return Frames(3);
            c.Check(Performance.Mode == PerformanceMode.Auto && Performance.LastSongId == newId, "a song already downloaded keeps playing");
            Performance.TestRequestStop();
            yield return Frames(3);

            // Sharing turned off while a big file downloads: told at once.
            yield return OpenWithServerList(c, true);
            c.Check(SongWindow.TestClickRow(hugeId), "the 1.9 MB song clicked");
            yield return WaitReal(() => Percent(SongWindow.TestRowInfo(hugeId)) >= 1 || Downloaded(hugeId), 10f);
            var mid = Percent(SongWindow.TestRowInfo(hugeId));
            c.Check(mid >= 1 && mid < 80 && !Downloaded(hugeId), $"download under way ({mid} %)");
            yield return Ask(c, SrvSongs, "off", null);
            var asked = Time.realtimeSinceStartup;
            yield return WaitReal(() => (SongWindow.TestRowInfo(hugeId) ?? "").Contains("no longer shares") || Downloaded(hugeId), 4f);
            c.Check((SongWindow.TestRowInfo(hugeId) ?? "").Contains("no longer shares") && !Downloaded(hugeId) && Time.realtimeSinceStartup - asked < 2f,
                $"ShareSongs off mid-download: \"no longer shares\" at once ({F(Time.realtimeSinceStartup - asked)} s): {SongWindow.TestRowInfo(hugeId)}");
            Performance.CloseWindow();
            yield return Frames(3);

            // Own MIDI songs not allowed by the server, its songs shared: hint for own files, server songs play.
            yield return Ask(c, SrvSongs, "on", null);
            yield return Ask(c, SrvSet, "AllowPlayerSongs=false", null);
            yield return WaitReal(() => !ServerRules.Current.AllowPlayerSongs, 8f);
            yield return OpenWithServerList(c, true);
            items = SongWindow.TestItems();
            var midi = items.IndexOf("header|Your MIDI songs");
            c.Check(!ServerRules.Current.AllowPlayerSongs && midi >= 0 && midi + 1 < items.Count && items[midi + 1] == "hint|" + Performance.PlayerSongsOff
                    && !items.Exists(i => i.StartsWith("song|midi:", StringComparison.Ordinal)), "the window shows the hint instead of own MIDI files");
            c.Check(items.Contains("song|" + newId + "|" + MpSmall) && SongWindow.TestClickRow(newId) && SongWindow.TestPress("Play"), "a server song, Play");
            yield return Frames(3);
            c.Check(Performance.Mode == PerformanceMode.Auto && Performance.LastSongId == newId, "server songs play");
            Performance.TestRequestStop();
            yield return Frames(3);
            yield return WaitReal(() => !GameScreens.AnyOpen(), 2f);
            Performance.TestRequestOpen();
            yield return Frames(3);
            c.Check(SongWindow.TestSelect("preset:ravens-jig") && SongWindow.TestPress("Play"), "a built-in song, Play");
            yield return Frames(3);
            c.Check(Performance.Mode == PerformanceMode.Auto, "built-in songs play");
            Performance.TestRequestStop();
            yield return Frames(3);
            yield return Ask(c, SrvSongs, "clean", null);
            yield return Ask(c, SrvSet, "reset", null);
            yield return WaitReal(() => ServerRules.Current.AllowPlayerSongs, 8f);
            c.Check(ServerRules.Current.AllowPlayerSongs, "server settings put back");
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.mp.settings (T15 with the real config entries) ----------

    // A multiplayer run has its own throwaway config files: here the settings are changed for real (the entries the
    // settings window writes), so the entry-to-game wiring is checked too.
    private static IEnumerator RunMpSettings()
    {
        var c = new Checks(MpSettingsName);
        var player = Player.m_localPlayer;
        if (player == null || !SelfTest.IsMultiplayerRun)
        {
            SelfTest.Fail(MpSettingsName, "not a multiplayer run (this test writes the config file)");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        var volume = Plugin.Volume.Value;
        var gameMusic = Plugin.GameMusicVolume.Value;
        var speed = Plugin.NoteSpeed.Value;
        var lane = Plugin.Lane1Key.Value;
        try
        {
            rig.TakeControls();
            PadInput.Test = false;
            rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            c.Check(Performance.StartAuto(Preset("greensleeves"), -1, out var error), "plays: " + error);
            yield return new WaitForSeconds(0.6f);
            var emitter = Performance.LocalEmitter;
            Plugin.Volume.Value = 0.2f;
            c.Check(emitter != null && emitter.Source != null && Mathf.Approximately(emitter.Source.volume, 0.2f), "Volume = 0.2 applies at once");
            Plugin.GameMusicVolume.Value = 1f;
            yield return WaitFor(() => Listeners.Duck > 0.995f, 2f);
            c.Check(Listeners.Duck > 0.995f, "GameMusicVolume = 1: the game's music is not turned down");
            Plugin.GameMusicVolume.Value = 0.3f;
            yield return WaitFor(() => Listeners.Duck < 0.32f, 2f);
            c.Check(Listeners.Duck < 0.32f, "GameMusicVolume = 0.3: it goes down");
            Performance.TestRequestStop();
            yield return Frames(3);
            c.Check(Performance.StartMiniGame(Preset("kjerringa-med-staven"), -1, out error), "performs: " + error);
            yield return Frames(3);
            var game = Performance.Game;
            if (!c.Check(game != null, "rhythm game running"))
            {
                c.Report();
                yield break;
            }
            Plugin.NoteSpeed.Value = 2f;
            c.Check(Mathf.Approximately(game.LookAhead, MiniGame.LookAheadBase / 2f), "NoteSpeed = 2 applies at once");
            Plugin.Lane1Key.Value = KeyCode.A;
            c.Check(LaneKeys.Key(0) == KeyCode.A, "Lane1Key = A: lane 1 on A at once");
            yield return Frames(2);
            c.Check(MiniGameHud.LaneCaption(0) == LaneKeys.Label(0) && LaneKeys.Label(0) != "-" && LaneKeys.Label(0).Length > 0,
                "the key under lane 1 changes at once: " + MiniGameHud.LaneCaption(0));
            var mark = LogTap.ProblemMark;
            Plugin.Lane1Key.Value = KeyCode.F15;
            c.Check(LaneKeys.Key(0) == KeyCode.None && LogTap.CountProblems(mark, "MiniGame.Lane1Key = F15") == 1, "Lane1Key = F15: lane off, one warning");
            Plugin.Lane1Key.Value = KeyCode.Escape;
            c.Check(LaneKeys.Key(0) == KeyCode.None && LogTap.CountProblems(mark, "MiniGame.Lane1Key = Escape") == 1, "Lane1Key = Escape: lane off, one warning");
            Plugin.Lane1Key.Value = KeyCode.Mouse1;
            c.Check(LaneKeys.Key(0) == KeyCode.None && LogTap.CountProblems(mark, "MiniGame.Lane1Key = Mouse1") == 1, "Lane1Key = Mouse1: lane off, one warning");
            Plugin.Lane1Key.Value = KeyCode.F5;
            c.Check(LaneKeys.Key(0) == KeyCode.F5 && LogTap.ProblemsSince(mark).Count == 3, "Lane1Key = F5 is taken (no warning)");
            Performance.TestRequestStop();
            yield return Frames(3);
            Plugin.Lane1Key.Value = lane;
            Plugin.NoteSpeed.Value = speed;
            c.Report();
        }
        finally
        {
            Plugin.Volume.Value = volume;
            Plugin.GameMusicVolume.Value = gameMusic;
            Plugin.NoteSpeed.Value = speed;
            Plugin.Lane1Key.Value = lane;
            x.Restore();
            rig.Restore();
        }
    }

    // ---------- music.mp.server (M09) ----------

    private static IEnumerator RunMpServer()
    {
        var c = new Checks(MpServerName);
        var player = Player.m_localPlayer;
        if (player == null || !IsClient)
        {
            SelfTest.Fail(MpServerName, "not a client of a server");
            yield break;
        }
        var rig = new Rig(player);
        var x = new Extra(player);
        try
        {
            rig.TakeControls();
            var state = new Dictionary<string, string>();
            yield return Until(c, state, s => true, 1f);
            c.Check(Val(state, "dedicated") == "True" && Val(state, "headless") == "True", "a dedicated server without a graphics device");
            c.Check(Num(state, "problems") == 0, $"it logged no warning or error from Music Instruments ({Num(state, "problems")}): {Val(state, "problem1")}");
            c.Check(Val(state, "audio") == "False" && Num(state, "emitters") == 0 && Val(state, "windows") == "False", "no sound and no window on the server");
            c.Check(Val(state, "prefabs") == "True" && Val(state, "effect") == "True" && Num(state, "recipes") == 3, "it knows the three instruments, their recipes and the Music effect");
            var batches = Num(state, "batches");
            var flute = rig.Hold(InstrumentKind.Flute);
            yield return new WaitForSeconds(0.4f);
            c.Check(Performance.StartAuto(Preset("greensleeves"), -1, out var error), "plays: " + error);
            yield return new WaitForSeconds(1.5f);
            yield return Until(c, state, s => Num(s, "batches") > batches, 5f);
            c.Check(Num(state, "batches") > batches && Num(state, "emitters") == 0, "it takes a player's notes to pass them on (and plays nothing itself)");
            Performance.TestRequestStop();
            yield return Frames(3);
            // An instrument dropped on the ground: the server holds it as world data that is saved.
            var drops = new Dictionary<string, string>();
            yield return Ask(c, SrvDrops, "", drops);
            var before = Num(drops, "found");
            c.Check(player.DropItem(player.GetInventory(), flute, 1), "flute dropped");
            yield return new WaitForSeconds(0.5f);
            foreach (var d in ItemDrop.s_instances)
            {
                if (d != null && InstrumentContent.KindOf(d.m_itemData) == InstrumentKind.Flute && (d.transform.position - player.transform.position).magnitude < 12f)
                {
                    rig.Spawned(d.gameObject);
                }
            }
            var end = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < end)
            {
                yield return Ask(c, SrvDrops, "", drops);
                if (Num(drops, "found") > before)
                {
                    break;
                }
                yield return Real(0.5f);
            }
            c.Check(Num(drops, "found") == before + 1, $"the server holds the dropped flute ({Num(drops, "found")} instruments on the ground)");
            c.Check(Num(drops, "kept") == Num(drops, "found") && Num(drops, "known") == Num(drops, "found"), "as an object it knows and keeps in the world save");
            c.Report();
        }
        finally
        {
            x.Restore();
            rig.Restore();
        }
    }
}
#endif
