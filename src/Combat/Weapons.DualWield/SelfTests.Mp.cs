#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.WeaponsDualWieldMod;

// Debug build only. Multiplayer self tests (tools/Test-Multiplayer.ps1): this game is ONE client joined to a real
// dedicated server, both run every MC mod with default configs. "The other player" is never there: the server stands in
// (what it decides, what it holds in its copy of the world, what it sends).
//   dual.mp.rules       server settings reach the client (off-hand damage on real hits, knife pair block, a weapon
//                       excluded while paired, moves changed while paired), the client's own settings and config file
//                       stay as they are, a pair loads as one weapon under an exclusion; the dedicated server runs the
//                       mod without an error
//   dual.mp.version     a player whose copy says another network version: the server's reason, no rules sent to it;
//                       switching AllowPlayersWithoutMod back to refuse checks every connected player again
//   dual.mp.seen        what the server holds of the pair for other players' games: both hand items, the stance, the
//                       hands after a swap, the back items when sheathed, a dropped former off-hand weapon
//   dual.mp.toggle      the player turns the mod off and on while connected (the real Enabled setting; the test config
//                       is thrown away): the off-hand weapon goes back, status text, vanilla equip and attack, the
//                       server's view and its warning with AllowPlayersWithoutMod on, rules logged again when back
//                       on, off while sheathed; off and on at once with refusal on: the player stays
//   dual.mp.server-off  the server turns the mod off and on: off-hand weapon put away, status text, vanilla equip,
//                       then pairs and the server's rules again
//   dual.mp.no-server   scenario vanilla-server (server without the mod): status text, a saved pair loads as one
//                       weapon, equipping is vanilla. Registered from Plugin.BindConfig: the feature is off there.
// Server halves (RegisterServerStep): dual.mp.server-set (settings), dual.mp.server-peer (the join check's view of the
// player), dual.mp.server-zdo (the player's and a dropped item's data), dual.mp.server-info (state of the mod there).
internal static partial class SelfTests
{
    private const string MpRulesName = "dual.mp.rules";
    private const string MpVersionName = "dual.mp.version";
    private const string MpSeenName = "dual.mp.seen";
    private const string MpToggleName = "dual.mp.toggle";
    private const string MpServerOffName = "dual.mp.server-off";
    private const string MpNoServerName = "dual.mp.no-server";

    private const string ServerSetStep = "dual.mp.server-set";
    private const string ServerPeerStep = "dual.mp.server-peer";
    private const string ServerZdoStep = "dual.mp.server-zdo";
    private const string ServerInfoStep = "dual.mp.server-info";

    // The server probe's own step (docs/modding/framework.md): "<GUID>=on|off".
    private const string ProbeSetEnabledStep = "probe.set-enabled";

    // Scenario of tools/Test-Multiplayer.ps1 where the server runs no MC mod.
    private const string VanillaServerScenario = "vanilla-server";

    private static void RegisterMultiplayerTests()
    {
        // The two that turn the mod off run last.
        SelfTest.RegisterMultiplayer(MpRulesName, SelfTest.Modded, RunMpRules);
        SelfTest.RegisterMultiplayer(MpVersionName, SelfTest.Modded, RunMpVersion);
        SelfTest.RegisterMultiplayer(MpSeenName, SelfTest.Modded, RunMpSeen);
        SelfTest.RegisterMultiplayer(MpToggleName, SelfTest.Modded, RunMpToggle);
        SelfTest.RegisterMultiplayer(MpServerOffName, SelfTest.Modded, RunMpServerOff);
        SelfTest.RegisterServerStep(ServerSetStep, ServerSet);
        SelfTest.RegisterServerStep(ServerPeerStep, ServerPeer);
        SelfTest.RegisterServerStep(ServerZdoStep, ServerZdo);
        SelfTest.RegisterServerStep(ServerInfoStep, ServerInfo);
    }

    private static void UnregisterMultiplayerTests()
    {
        SelfTest.UnregisterMultiplayer(MpRulesName);
        SelfTest.UnregisterMultiplayer(MpVersionName);
        SelfTest.UnregisterMultiplayer(MpSeenName);
        SelfTest.UnregisterMultiplayer(MpToggleName);
        SelfTest.UnregisterMultiplayer(MpServerOffName);
        SelfTest.UnregisterServerStep(ServerSetStep);
        SelfTest.UnregisterServerStep(ServerPeerStep);
        SelfTest.UnregisterServerStep(ServerZdoStep);
        SelfTest.UnregisterServerStep(ServerInfoStep);
    }

    // Plugin.BindConfig (every start, whatever the feature's state): the test of the scenario where the feature is off.
    internal static void RegisterAlways()
    {
        SelfTest.RegisterMultiplayer(MpNoServerName, VanillaServerScenario, RunMpNoServer);
    }

    // ---------- server halves ----------

    // Settings as the server's owner would change them: "reset" (every server setting back to its default), or
    // "Name=Value;Name=Value". The server's test config is thrown away after the run. Answer: the rules now in force.
    private static IEnumerator ServerSet(string arg, object[] reply)
    {
        var unknown = new List<string>();
        try
        {
            if (arg == "reset")
            {
                Plugin.OffHandDamage.Value = (int)Plugin.OffHandDamage.DefaultValue;
                Plugin.HitPattern.Value = (HitPatternMode)Plugin.HitPattern.DefaultValue;
                Plugin.BothHandsDamage.Value = (int)Plugin.BothHandsDamage.DefaultValue;
                Plugin.SwingStamina.Value = (int)Plugin.SwingStamina.DefaultValue;
                Plugin.SecondaryMoves.Value = (SecondaryMovesMode)Plugin.SecondaryMoves.DefaultValue;
                Plugin.KnifePairBlock.Value = (int)Plugin.KnifePairBlock.DefaultValue;
                Plugin.ExcludedWeapons.Value = (string)Plugin.ExcludedWeapons.DefaultValue;
                Plugin.PairMoves.Value = (string)Plugin.PairMoves.DefaultValue;
                Plugin.KnifePairMoves.Value = (string)Plugin.KnifePairMoves.DefaultValue;
                Plugin.AllowPlayersWithoutMod.Value = (bool)Plugin.AllowPlayersWithoutMod.DefaultValue;
            }
            else
            {
                foreach (var part in (arg ?? "").Split(';'))
                {
                    var eq = part.IndexOf('=');
                    if (eq <= 0)
                    {
                        continue;
                    }
                    var name = part.Substring(0, eq).Trim();
                    var value = part.Substring(eq + 1).Trim();
                    switch (name)
                    {
                        case "OffHandDamage":
                            Plugin.OffHandDamage.Value = int.Parse(value, CultureInfo.InvariantCulture);
                            break;
                        case "KnifePairBlock":
                            Plugin.KnifePairBlock.Value = int.Parse(value, CultureInfo.InvariantCulture);
                            break;
                        case "ExcludedWeapons":
                            Plugin.ExcludedWeapons.Value = value;
                            break;
                        case "PairMoves":
                            Plugin.PairMoves.Value = value;
                            break;
                        case "AllowPlayersWithoutMod":
                            Plugin.AllowPlayersWithoutMod.Value = bool.Parse(value);
                            break;
                        default:
                            unknown.Add(name);
                            break;
                    }
                }
            }
        }
        catch (Exception e)
        {
            SelfTest.Answer(reply, false, $"could not set '{arg}': {e.Message}");
            yield break;
        }
        // The push to the players goes out in the server's next network update.
        yield return null;
        yield return null;
        yield return null;
        SelfTest.Answer(reply, unknown.Count == 0, unknown.Count > 0
            ? $"unknown setting(s): {string.Join(", ", unknown.ToArray())}"
            : $"allow={Plugin.AllowPlayersWithoutMod.Value};rules={ServerRules.Current.Describe()}");
    }

    // The one player of the test run, as the server holds it.
    private static ZNetPeer OnlyPeer()
    {
        var net = ZNet.instance;
        return net != null ? net.GetPeers().FirstOrDefault(p => p != null && p.IsReady()) : null;
    }

    // What the join check knows about the player. Arg "check" = check every connected player again first (as when
    // the mod turns on, or AllowPlayersWithoutMod goes back to refuse) and wait for its verdict.
    private static IEnumerator ServerPeer(string arg, object[] reply)
    {
        if (OnlyPeer() == null)
        {
            SelfTest.Answer(reply, false, "the server has no connected player");
            yield break;
        }
        if (arg == "check")
        {
            var before = PlayerCheck.VerdictCount;
            PlayerCheck.ScheduleAllConnected();
            var until = Time.unscaledTime + PlayerCheck.GraceSeconds + 4f;
            while (Time.unscaledTime < until && PlayerCheck.VerdictCount == before)
            {
                yield return null;
            }
        }
        else
        {
            yield return null;
        }
        var peer = OnlyPeer();
        if (peer == null)
        {
            SelfTest.Answer(reply, false, "the player left the server");
            yield break;
        }
        var ready = NetworkGate.PeerReady(peer);
        SelfTest.Answer(reply, true, string.Join(";", new[]
        {
            $"hasMod={NetworkGate.PeerHasMod(peer)}",
            $"net={NetworkGate.PeerNetworkVersion(peer)}",
            $"ready={(ready.HasValue ? ready.Value.ToString() : "unknown")}",
            $"compatible={NetworkGate.PeerCompatible(peer)}",
            $"problem={NetworkGate.PeerProblem(peer) ?? "none"}",
            $"kicked={ZNet.PeersToDisconnectAfterKick.ContainsKey(peer)}",
            $"verdicts={PlayerCheck.VerdictCount}",
            $"saidOff={LogWatch.Saw($" turned {ModInfo.Name} off on their game.")}",
            $"last={PlayerCheck.LastVerdict}",
            $"text={PlayerCheck.LastText}",
        }));
    }

    // The player's data in the server's copy of the world (what every other player's game draws that player from).
    // Arg "item:<user>:<id>" = a dropped item's data instead.
    private static IEnumerator ServerZdo(string arg, object[] reply)
    {
        yield return null;
        var man = ZDOMan.instance;
        if (man == null)
        {
            SelfTest.Answer(reply, false, "no ZDOMan on the server");
            yield break;
        }
        if (arg != null && arg.StartsWith("item:", StringComparison.Ordinal))
        {
            var parts = arg.Split(':');
            ZDO itemZdo = null;
            if (parts.Length == 3 && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var user)
                && uint.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                itemZdo = man.GetZDO(new ZDOID(user, id));
            }
            if (itemZdo == null)
            {
                SelfTest.Answer(reply, false, $"the server has no object '{arg}'");
                yield break;
            }
            var data = new ItemDrop.ItemData();
            ItemDrop.LoadFromZDO(data, itemZdo);
            SelfTest.Answer(reply, true, $"prefab={itemZdo.GetPrefab()};marker={Hands.IsMarked(data)};custom={data.m_customData.Count}");
            yield break;
        }
        var peer = OnlyPeer();
        var zdo = peer != null ? man.GetZDO(peer.m_characterID) : null;
        if (zdo == null)
        {
            SelfTest.Answer(reply, false, "the server has no character of the player");
            yield break;
        }
        // ZSyncAnimation.SetInt stores the value under 438569 + hash (vanilla), read there by every other game.
        SelfTest.Answer(reply, true, $"left={zdo.GetInt(ZDOVars.s_leftItem)};right={zdo.GetInt(ZDOVars.s_rightItem)};"
                                     + $"statei={zdo.GetInt(438569 + ZSyncAnimation.GetHash("statei"))};"
                                     + $"leftBack={zdo.GetInt(ZDOVars.s_leftBackItem)};rightBack={zdo.GetInt(ZDOVars.s_rightBackItem)}");
    }

    // Warnings the server logs when a test makes a player look like one without the mod.
    private static readonly string[] ProvokedServerWarnings = { " plays without ", "Refused ", " connected without " };

    // State of the mod on the server: dedicated, feature state, the visual helpers on a game without graphics, patch
    // failures reported so far (PatchGuard logs each site once), warnings and errors logged.
    private static IEnumerator ServerInfo(string arg, object[] reply)
    {
        yield return null;
        var feature = FeatureRegistry.Find(ModInfo.Guid);
        var visuals = "ok";
        try
        {
            BackCross.RebuildAll();
            LeftTrails.Clear();
        }
        catch (Exception e)
        {
            visuals = e.GetType().Name;
        }
        var reported = typeof(PatchGuard).GetField("Reported", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as HashSet<string>;
        var warnings = LogWatch.WarningLines();
        var odd = warnings.Count(w => !ProvokedWarnings.Any(p => w.StartsWith(p, StringComparison.Ordinal))
                                      && !ProvokedServerWarnings.Any(p => w.Contains(p)));
        var errors = LogWatch.ErrorLines().Count(e => !e.Contains(SelfTest.Prefix + " FAIL"));
        var net = ZNet.instance;
        SelfTest.Answer(reply, true, string.Join(";", new[]
        {
            $"dedicated={net != null && net.IsDedicated()}",
            $"state={(feature.HasValue ? feature.Value.State : "missing")}",
            $"nographics={LeftTrails.NoGraphics}",
            $"visuals={visuals}",
            $"patchErrors={(reported != null ? reported.Count.ToString(CultureInfo.InvariantCulture) : "unknown")}",
            $"oddWarnings={odd}",
            $"errors={errors}",
            $"rules={ServerRules.Current.Describe()}",
        }));
    }

    // ---------- client helpers ----------

    // Answer of the last CallStep.
    private static SelfTest.ServerReply _reply = new SelfTest.ServerReply();

    private static IEnumerator CallStep(string step, string arg)
    {
        _reply = new SelfTest.ServerReply();
        yield return SelfTest.CallServer(step, arg, _reply);
    }

    private static bool ReplyOk => _reply.Answered && _reply.Ok;

    // "name=value" out of the last answer ("a=1;b=2;text=..."). toEnd: the value runs to the end (it may hold ';').
    private static string Field(string name, bool toEnd = false)
    {
        var detail = _reply.Detail ?? "";
        var key = name + "=";
        int start;
        if (detail.StartsWith(key, StringComparison.Ordinal))
        {
            start = key.Length;
        }
        else
        {
            var at = detail.IndexOf(";" + key, StringComparison.Ordinal);
            if (at < 0)
            {
                return null;
            }
            start = at + 1 + key.Length;
        }
        if (toEnd)
        {
            return detail.Substring(start);
        }
        var end = detail.IndexOf(';', start);
        return end < 0 ? detail.Substring(start) : detail.Substring(start, end - start);
    }

    private static IEnumerator WaitFor(Func<bool> condition, float seconds)
    {
        var until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until && !condition())
        {
            yield return null;
        }
    }

    private static bool Connected =>
        ZNet.instance != null && !ZNet.instance.IsServer() && Player.m_localPlayer != null
        && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected;

    // Client of a server, in the world. Else the test fails at once.
    private static Player MpPlayer(string test)
    {
        if (!Connected)
        {
            SelfTest.Fail(test, $"not in a world as a client of a server (connection status {ZNet.GetConnectionStatus()})");
            return null;
        }
        return Player.m_localPlayer;
    }

    // Server settings back to their defaults, and the client using them.
    private static IEnumerator ResetServer(Checks c)
    {
        yield return CallStep(ServerSetStep, "reset");
        c.Check(ReplyOk, $"server settings back to their defaults ({_reply})");
        yield return WaitFor(() => ServerRules.TestRules == null && ServerRules.UsingServer
                                   && ServerRules.Current.SameAs(DualRules.Defaults), 8f);
    }

    // ---------- dual.mp.rules ----------

    private static IEnumerator RunMpRules()
    {
        var c = new Checks(MpRulesName);
        var player = MpPlayer(MpRulesName);
        if (player == null)
        {
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        SkillSave skills = null;
        try
        {
            bench = new Bench(player);
            // The rules of the test are the server's: no forced rules on this side.
            ServerRules.TestRules = null;
            skills = new SkillSave(player, Skills.SkillType.Swords, Skills.SkillType.Blocking);
            var inventory = bench.Inventory;
            yield return ResetServer(c);
            c.Check(ServerRules.UsingServer && ServerRules.Current.SameAs(DualRules.Defaults),
                $"the client uses the server's rules: {ServerRules.Current.Describe()}");

            // M15: the dedicated server runs the mod, nothing failed there.
            yield return CallStep(ServerInfoStep, "");
            c.Check(ReplyOk && Field("dedicated") == "True" && Field("state") == nameof(ModState.Active),
                $"dedicated server with the mod active ({_reply})");
            c.Check(ReplyOk && Field("visuals") == "ok" && Field("patchErrors") == "0" && Field("errors") == "0"
                    && Field("oddWarnings") == "0",
                $"no error, failed patch or unexpected warning from the mod on the server ({_reply})");

            // M06: server OffHandDamage 50, the client's own setting untouched.
            var plugin = Chainloader.PluginInfos.TryGetValue(ModInfo.Guid, out var info) ? info.Instance as Plugin : null;
            var configPath = plugin != null ? plugin.Config.ConfigFilePath : null;
            var configBefore = configPath != null && File.Exists(configPath) ? File.ReadAllText(configPath) : null;
            var ownDamage = Plugin.OffHandDamage.Value;
            var ownBlock = Plugin.KnifePairBlock.Value;
            yield return CallStep(ServerSetStep, "OffHandDamage=50");
            yield return WaitFor(() => ServerRules.Current.OffHandDamage == 50, 8f);
            c.Check(ReplyOk && ServerRules.UsingServer && ServerRules.Current.OffHandDamage == 50,
                $"server OffHandDamage 50 reached the client ({ServerRules.Current.Describe()}; server: {_reply})");
            c.Check(ServerRules.LastLogged != null && ServerRules.LastLogged == ServerRules.Current.Describe()
                    && ServerRules.LastLogged.StartsWith("off-hand damage 50%", StringComparison.Ordinal)
                    && LogWatch.Saw("Using the server's dual wielding rules: off-hand damage 50%"),
                $"the client logged \"Using the server's dual wielding rules: off-hand damage 50%, ...\" (logged: '{ServerRules.LastLogged}'; "
                + $"line seen in the log {LogWatch.Saw("Using the server's dual wielding rules: off-hand damage 50%")})");
            var first = bench.Give(Sword);
            var second = bench.Give(Sword);
            var dummy = bench.Spawn(TrollName, DummyGap);
            c.Check(first != null && second != null && dummy != null && dummy.Character != null,
                "two SwordIron and a Troll for the damage check");
            if (first != null && second != null && dummy != null && dummy.Character != null)
            {
                NoteDummy(MpRulesName, dummy);
                SkillSave.SetLevel(player, Skills.SkillType.Swords, 100f);
                bench.Pair(first, second);
                yield return new WaitForSeconds(0.5f);
                c.Check(Holds(player, first, second), $"two SwordIron paired ({HandsText(player)})");
                DualSwing.ResetRecords();
                DualSwing.Recording = true;
                var from = DualSwing.Swings.Count;
                yield return Swing(player, dummy, 4, false);
                foreach (var trigger in new[] { "dualaxes2", "dualaxes3" })
                {
                    var swing = SwingIndex(from, trigger);
                    var mainDamage = DamageOf(swing, 0, Hand.Main);
                    var offDamage = DamageOf(swing, 1, Hand.Off);
                    var ratio = BaseRatio(offDamage, mainDamage);
                    SelfTest.Note(MpRulesName, $"{trigger}: main-hand hit {DamageText(mainDamage)}, off-hand hit {DamageText(offDamage)}");
                    c.Check(Rolled(mainDamage) && Rolled(offDamage) && Mathf.Abs(ratio - 0.5f) <= RatioTolerance,
                        $"{trigger}: with the server's OffHandDamage 50 the off-hand hit is half the main-hand hit (ratio {F2(ratio)})");
                }
                DualSwing.Recording = false;
                DualSwing.ResetRecords();
            }
            c.Check(Plugin.OffHandDamage.Value == ownDamage,
                $"the client's own OffHandDamage setting is unchanged ({Plugin.OffHandDamage.Value})");

            // M06: server KnifePairBlock 0: two knives block like one.
            yield return CallStep(ServerSetStep, "OffHandDamage=100;KnifePairBlock=0");
            yield return WaitFor(() => ServerRules.Current.KnifePairBlock == 0 && ServerRules.Current.OffHandDamage == 100, 8f);
            c.Check(ReplyOk && ServerRules.Current.KnifePairBlock == 0 && ServerRules.LastLogged != null
                    && ServerRules.LastLogged.Contains("knife pair block 0%")
                    && LogWatch.Saw("Using the server's dual wielding rules: " + ServerRules.LastLogged),
                $"server KnifePairBlock 0 reached the client and was logged ('{ServerRules.LastLogged}')");
            var black = bench.Give(KnifeBlack);
            var black2 = bench.Give(KnifeBlack);
            if (black != null && black2 != null)
            {
                bench.Pair(black, black2);
                yield return null;
                c.Check(Holds(player, black, black2) && Approx(black2.GetBaseBlockPower(), 2f),
                    $"two Black Metal knives paired, one knife blocks 2 ({HandsText(player)})");
                CheckBlock(c, player, black2.GetBaseBlockPower(), black2.m_shared.m_timedBlockBonus, false,
                    "server KnifePairBlock 0: two knives block like one knife");
            }
            c.Check(Plugin.KnifePairBlock.Value == ownBlock
                    && (configBefore == null || File.ReadAllText(configPath) == configBefore),
                $"the client's own KnifePairBlock setting ({Plugin.KnifePairBlock.Value}) and its config file are unchanged");

            // M08: a weapon excluded by the server while the client holds the pair: put away at once.
            yield return ResetServer(c);
            var sword = first;
            var axe = bench.Give(Axe);
            if (sword != null && axe != null)
            {
                bench.Pair(sword, axe);
                yield return null;
                c.Check(Holds(player, sword, axe), $"pair before the server's change ({HandsText(player)})");
                yield return CallStep(ServerSetStep, "ExcludedWeapons=AxeIron");
                yield return WaitFor(() => ServerRules.Current.ExcludedWeapons == Axe, 8f);
                var arrived = Time.frameCount;
                yield return WaitFor(() => player.m_leftItem == null, 2f);
                c.Check(ReplyOk && ServerRules.Current.ExcludedWeapons == Axe && Holds(player, sword, null) && !axe.m_equipped
                        && Time.frameCount - arrived <= 3,
                    $"server ExcludedWeapons = AxeIron: the client's axe is put away at once ({Time.frameCount - arrived} frame(s) "
                    + $"after the rules came; {HandsText(player)})");
                Invariant(c, player, "server excluded the off-hand weapon");

                // M07: a pair saved before, loaded while the server excludes the axe (what a rejoin does at spawn): one
                // weapon in the main hand, the other in the inventory, whichever loads first.
                foreach (var offFirst in new[] { true, false })
                {
                    LoadPair(player, inventory, sword, axe, offFirst);
                    yield return null;
                    yield return null;
                    var one = player.m_leftItem == null
                              && (ReferenceEquals(player.m_rightItem, sword) || ReferenceEquals(player.m_rightItem, axe))
                              && sword.m_equipped != axe.m_equipped;
                    c.Check(one, $"saved pair loaded under ExcludedWeapons = AxeIron ({(offFirst ? "off-hand" : "main")} weapon "
                                 + $"first): one weapon in the main hand, the other not equipped ({HandsText(player)})");
                    Invariant(c, player, "pair loaded under an exclusion");
                }

                // Cleared: pairing works again. Then PairMoves changed by the server: knife moves and stance at once.
                yield return CallStep(ServerSetStep, "ExcludedWeapons=");
                yield return WaitFor(() => ServerRules.Current.ExcludedWeapons.Length == 0, 8f);
                bench.Pair(sword, axe);
                yield return null;
                c.Check(ReplyOk && Holds(player, sword, axe) && StateI(player) == 15,
                    $"exclusion cleared by the server: the pair can be made again ({HandsText(player)})");
                yield return CallStep(ServerSetStep, "PairMoves=" + DualRules.DefaultKnifePairMoves);
                yield return WaitFor(() => ServerRules.Current.PairMoves == DualRules.DefaultKnifePairMoves, 8f);
                yield return null;
                yield return null;
                yield return null;
                var moves = MoveTemplates.For(sword, axe, ServerRules.Current, player);
                c.Check(ReplyOk && StateI(player) == 11 && moves != null && moves.PrefabName == DualRules.DefaultKnifePairMoves,
                    $"server PairMoves = KnifeSkollAndHati: the client's pair switches to the knife moves and stance (statei {StateI(player)})");
            }
            yield return ResetServer(c);
            c.Report();
        }
        finally
        {
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

    // ---------- dual.mp.version ----------

    private static IEnumerator RunMpVersion()
    {
        var c = new Checks(MpVersionName);
        var player = MpPlayer(MpVersionName);
        if (player == null)
        {
            yield break;
        }
        var serverPeer = ZNet.instance.GetServerPeer();
        var rpc = serverPeer != null ? serverPeer.m_rpc : null;
        if (rpc == null)
        {
            SelfTest.Fail(MpVersionName, "no connection to the server");
            yield break;
        }
        // The framework's hello of this mod (NetworkGate): "1|<network version>|<version>|on".
        var helloRpc = ModInfo.Guid + ".Hello";
        var realHello = $"1|{ModInfo.NetworkVersion}|{ModInfo.Version}|on";
        var other = ModInfo.NetworkVersion + 1;
        var otherSaid = false;
        try
        {
            ServerRules.TestRules = null;
            yield return ResetServer(c);
            yield return CallStep(ServerPeerStep, "check");
            c.Check(ReplyOk && Field("compatible") == "True" && Field("last") == nameof(JoinVerdict.Compatible),
                $"before: the server's join check finds the player compatible ({_reply})");

            // AllowPlayersWithoutMod on first: nothing here may get the test's player refused.
            yield return CallStep(ServerSetStep, "AllowPlayersWithoutMod=true");
            var allowed = ReplyOk && Field("allow") == "True";
            c.Check(allowed, $"server AllowPlayersWithoutMod = true ({_reply})");
            if (allowed)
            {
                // M03 / M04: the player's copy now says another network version (what a build with
                // ModNetworkVersion + 1 says when it joins).
                rpc.Invoke(helloRpc, $"1|{other}|{ModInfo.Version}|on");
                otherSaid = true;
                yield return new WaitForSecondsRealtime(0.5f);
                yield return CallStep(ServerPeerStep, "check");
                var reason = $"has another version of the mod (network version {other}, the server has {ModInfo.NetworkVersion})";
                c.Check(ReplyOk && Field("compatible") == "False" && Field("problem") == reason,
                    $"a copy with network version {other}: the server's reason is '{reason}' ({_reply})");
                c.Check(ReplyOk && Field("last") == nameof(JoinVerdict.Allowed) && Field("kicked") == "False"
                        && (Field("text", true) ?? "").Contains($"plays without {ModInfo.Name}: their game {reason}. "
                                                                 + "AllowPlayersWithoutMod is on, so they may play"),
                    $"with AllowPlayersWithoutMod on the server lets that player play and warns ({_reply})");
                // The server sends its rules only to copies with its own network version.
                var rules = ServerRules.Current;
                var logged = ServerRules.LastLogged;
                yield return CallStep(ServerSetStep, "OffHandDamage=75");
                yield return new WaitForSecondsRealtime(2f);
                c.Check(ReplyOk && ReferenceEquals(ServerRules.Current, rules) && ServerRules.Current.OffHandDamage != 75
                        && ServerRules.LastLogged == logged,
                    $"the server's new rules are not sent to the other network version (client still on: {ServerRules.Current.Describe()})");
                // Back to the real version.
                rpc.Invoke(helloRpc, realHello);
                otherSaid = false;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            yield return CallStep(ServerPeerStep, "check");
            var compatible = ReplyOk && Field("compatible") == "True";
            c.Check(compatible && Field("last") == nameof(JoinVerdict.Compatible) && Field("kicked") == "False",
                $"the real copy again: compatible ({_reply})");
            if (compatible)
            {
                // M04: switched back to refuse: every connected player is checked again after the grace (this one is
                // compatible and stays).
                int.TryParse(Field("verdicts"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var verdicts);
                yield return CallStep(ServerSetStep, "AllowPlayersWithoutMod=false");
                c.Check(ReplyOk && Field("allow") == "False", $"server AllowPlayersWithoutMod = false ({_reply})");
                yield return new WaitForSecondsRealtime(PlayerCheck.GraceSeconds + 1.5f);
                yield return CallStep(ServerPeerStep, "");
                int.TryParse(Field("verdicts"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var after);
                c.Check(ReplyOk && after > verdicts && Field("last") == nameof(JoinVerdict.Compatible) && Field("kicked") == "False"
                        && Connected,
                    $"AllowPlayersWithoutMod back to false: the server checked the connected player again ({after - verdicts} "
                    + $"check(s)), the compatible player stays ({_reply})");
                // Rules reach the player again.
                yield return CallStep(ServerSetStep, "OffHandDamage=60");
                yield return WaitFor(() => ServerRules.Current.OffHandDamage == 60, 8f);
                c.Check(ReplyOk && ServerRules.Current.OffHandDamage == 60,
                    $"back on the server's network version the rules come again ({ServerRules.Current.Describe()})");
            }
            yield return ResetServer(c);
            c.Report();
        }
        finally
        {
            if (otherSaid && rpc.IsConnected())
            {
                rpc.Invoke(helloRpc, realHello);
            }
            Bench.ClearOverrides();
        }
    }

    // ---------- dual.mp.seen ----------

    // Me ask the server for the player's data until 'ok' says it is what the test wants (the data travels in the next
    // sync, a moment later) or 'tries' answers went by.
    private static IEnumerator ServerSees(Func<bool> ok, int tries = 8)
    {
        for (var i = 0; i < tries; i++)
        {
            yield return CallStep(ServerZdoStep, "");
            if (ReplyOk && ok())
            {
                yield break;
            }
            yield return new WaitForSecondsRealtime(0.5f);
        }
    }

    private static IEnumerator RunMpSeen()
    {
        var c = new Checks(MpSeenName);
        var player = MpPlayer(MpSeenName);
        if (player == null)
        {
            yield break;
        }
        yield return WaitIdle(player);
        Bench bench = null;
        try
        {
            bench = new Bench(player);
            ServerRules.TestRules = null;
            yield return ResetServer(c);
            var inventory = bench.Inventory;
            var sword = bench.Give(Sword);
            var axe = bench.Give(Axe);
            if (sword == null || axe == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            var swordHash = sword.m_dropPrefab.name.GetStableHashCode().ToString(CultureInfo.InvariantCulture);
            var axeHash = axe.m_dropPrefab.name.GetStableHashCode().ToString(CultureInfo.InvariantCulture);

            // M09: the pair as the server (and so every other player's game) holds it.
            bench.Pair(sword, axe);
            yield return null;
            c.Check(Holds(player, sword, axe), $"pair on the client ({HandsText(player)})");
            yield return ServerSees(() => Field("right") == swordHash && Field("left") == axeHash && Field("statei") == "15");
            c.Check(ReplyOk && Field("right") == swordHash && Field("left") == axeHash && Field("statei") == "15",
                $"the server holds the pair: sword in the right hand, axe in the left, dual axe stance 15 ({_reply})");

            // M16: after a swap the server holds the hands the other way round.
            yield return SwapHands(player);
            c.Check(_swapQueued && _swapDone && Holds(player, axe, sword), $"swap on the client ({HandsText(player)})");
            yield return ServerSees(() => Field("right") == axeHash && Field("left") == swordHash);
            c.Check(ReplyOk && Field("right") == axeHash && Field("left") == swordHash && Field("statei") == "15",
                $"after the swap the server holds the axe in the right hand and the sword in the left ({_reply})");
            yield return WaitIdle(player);

            // M17 (data): sheathed, both weapons are back items for every game.
            player.HideHandItems();
            yield return null;
            yield return ServerSees(() => Field("rightBack") == axeHash && Field("leftBack") == swordHash);
            c.Check(ReplyOk && Field("right") == "0" && Field("left") == "0" && Field("rightBack") == axeHash
                    && Field("leftBack") == swordHash,
                $"sheathed: the server holds both weapons as back items, nothing in the hands ({_reply})");
            player.ShowHandItems();
            yield return null;
            c.Check(Holds(player, axe, sword), $"drawn again: same hands ({HandsText(player)})");

            // M11: the former off-hand weapon dropped for another player: a plain item of its kind on the server
            // (the marker travels as custom data, which the game ignores); picked up again it pairs normally.
            bench.Pair(sword, axe);
            yield return null;
            var dropsBefore = new HashSet<ItemDrop>(ItemDrop.s_instances);
            var dropped = player.DropItem(inventory, axe, 1);
            yield return null;
            var onGround = ItemDrop.s_instances.FirstOrDefault(d => d != null && !dropsBefore.Contains(d) && d.m_itemData != null
                                                                    && ReferenceEquals(d.m_itemData.m_shared, axe.m_shared));
            var view = onGround != null ? onGround.GetComponent<ZNetView>() : null;
            c.Check(dropped && view != null && view.IsValid() && Holds(player, sword, null),
                $"off-hand axe dropped, the sword stays ({HandsText(player)})");
            if (view != null && view.IsValid())
            {
                bench.Track(onGround.gameObject);
                var uid = view.GetZDO().m_uid;
                var itemArg = $"item:{uid.UserID.ToString(CultureInfo.InvariantCulture)}:{uid.ID.ToString(CultureInfo.InvariantCulture)}";
                for (var i = 0; i < 8; i++)
                {
                    yield return CallStep(ServerZdoStep, itemArg);
                    if (ReplyOk)
                    {
                        break;
                    }
                    yield return new WaitForSecondsRealtime(0.5f);
                }
                c.Check(ReplyOk && Field("prefab") == axeHash,
                    $"the dropped axe is an AxeIron item on the server ({_reply})");
                SelfTest.Note(MpSeenName, $"the dropped former off-hand axe carries the off-hand marker on the server = {Field("marker")} "
                                          + $"({Field("custom")} custom data entr(y/ies); a game without the mod ignores them)");
                var back = onGround.m_itemData;
                var picked = player.Pickup(onGround.gameObject, false, false);
                yield return null;
                var paired = picked && inventory.ContainsItem(back) && player.EquipItem(back);
                yield return null;
                c.Check(paired && Holds(player, sword, back) && Hands.IsMarked(back),
                    $"picked up again, the axe pairs normally with the sword ({HandsText(player)})");
                Invariant(c, player, "picked up again");
            }
            c.Report();
        }
        finally
        {
            if (bench != null)
            {
                bench.TakeBack();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
        }
    }

    // ---------- dual.mp.toggle ----------

    private static IEnumerator RunMpToggle()
    {
        var c = new Checks(MpToggleName);
        var player = MpPlayer(MpToggleName);
        var found = FeatureRegistry.Find(ModInfo.Guid);
        if (player == null)
        {
            yield break;
        }
        if (found == null || found.Value.Enabled == null)
        {
            SelfTest.Fail(MpToggleName, "the mod is not in the MC Mods list");
            yield break;
        }
        var feature = found.Value;
        var enabled = feature.Enabled;
        yield return WaitIdle(player);
        Bench bench = null;
        try
        {
            bench = new Bench(player);
            ServerRules.TestRules = null;
            yield return ResetServer(c);
            var inventory = bench.Inventory;
            var vis = player.m_visEquipment;
            var sword = bench.Give(Sword);
            var sword2 = bench.Give(Sword);
            var axe = bench.Give(Axe);
            if (sword == null || sword2 == null || axe == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            // The mod is only ever turned off here while the server lets such players play: the test's player must
            // never be refused (every later test needs the connection).
            yield return CallStep(ServerSetStep, "AllowPlayersWithoutMod=true");
            var allowed = ReplyOk && Field("allow") == "True";
            c.Check(allowed, $"server AllowPlayersWithoutMod = true ({_reply})");
            if (!allowed)
            {
                c.Report();
                yield break;
            }
            bench.Pair(sword, axe);
            yield return null;
            c.Check(Holds(player, sword, axe) && feature.IsActive, $"pair, mod active ({HandsText(player)})");

            // L01 / M05: the player unticks the mod (what the MC Mods panel's tick box sets).
            enabled.Value = false;
            c.Check(!feature.IsActive && feature.State == nameof(ModState.Disabled) && feature.Status == "Off (disabled in settings).",
                $"turned off: status '{feature.Status}' ({feature.State})");
            c.Check(Holds(player, sword, null) && !axe.m_equipped && !Hands.IsMarked(axe) && inventory.ContainsItem(axe),
                $"turned off: the axe is back in the inventory, the sword stays ({HandsText(player)})");
            yield return null;
            yield return null;
            c.Check(StateI(player) == (int)sword.m_shared.m_animationState,
                $"turned off: the sword's own stance (statei {StateI(player)})");
            player.EquipItem(sword2);
            yield return null;
            c.Check(Holds(player, sword2, null) && !sword.m_equipped,
                $"turned off: a second one-handed weapon replaces the first, no pair ({HandsText(player)})");
            yield return WaitIdle(player);
            yield return WaitMinor(player);
            // The game's own gates open first: a refused start then names its cause.
            yield return WaitCanAttack(player);
            var gate = _attackGate;
            var started = player.StartAttack(null, false);
            var attack = player.m_currentAttack;
            c.Check(started && attack != null && ReferenceEquals(attack.m_weapon, sword2)
                    && attack.m_attackAnimation == sword2.m_shared.m_attack.m_attackAnimation,
                $"turned off: the attack is the weapon's own (started {started}, {(attack != null ? attack.m_attackAnimation : "no attack")}; "
                + $"before the press: {gate ?? "nothing in the game's way"})");
            yield return WaitIdle(player);
            c.Check(LogWatch.Saw($"Told the server that {ModInfo.Name} is now off on this game."),
                $"the client logged \"Told the server that {ModInfo.Name} is now off on this game.\"");
            // Past the server's grace: with AllowPlayersWithoutMod on the player stays, the server warns.
            yield return new WaitForSecondsRealtime(PlayerCheck.GraceSeconds + 1.5f);
            c.Check(Connected, $"turned off with AllowPlayersWithoutMod on: the player stays (status {ZNet.GetConnectionStatus()})");
            yield return CallStep(ServerPeerStep, "");
            c.Check(ReplyOk && Field("hasMod") == "True" && Field("ready") == "False" && Field("compatible") == "False"
                    && Field("problem") == "has the mod turned off" && Field("kicked") == "False",
                $"the server knows the player turned the mod off: 'has the mod turned off' ({_reply})");
            c.Check(ReplyOk && Field("saidOff") == "True",
                $"the server logged \"<player> turned {ModInfo.Name} off on their game.\" ({_reply})");
            c.Check(ReplyOk && Field("last") == nameof(JoinVerdict.Allowed)
                    && (Field("text", true) ?? "").Contains($"plays without {ModInfo.Name}: their game has the mod turned off. "
                                                             + "AllowPlayersWithoutMod is on, so they may play"),
                $"the server warned that the player plays without the mod ({_reply})");

            // On again: active, the server's rules asked and logged again, pairs work, compatible for the server.
            enabled.Value = true;
            ServerRules.TestRules = null;
            c.Check(feature.IsActive && feature.Status == "Active.", $"turned on: status '{feature.Status}' ({feature.State})");
            yield return WaitFor(() => ServerRules.UsingServer && ServerRules.LastLogged != null, 8f);
            c.Check(ServerRules.UsingServer && ServerRules.LastLogged != null,
                $"turned on: the server's rules are in use and logged again ('{ServerRules.LastLogged}')");
            bench.Pair(sword, axe);
            yield return null;
            c.Check(Holds(player, sword, axe) && StateI(player) == 15, $"turned on: pairs work again ({HandsText(player)})");
            Invariant(c, player, "on again");
            yield return CallStep(ServerPeerStep, "check");
            var compatible = ReplyOk && Field("compatible") == "True";
            c.Check(compatible && Field("last") == nameof(JoinVerdict.Compatible), $"turned on: compatible for the server again ({_reply})");

            // L01: off while the pair is sheathed.
            player.HideHandItems();
            yield return null;
            yield return null;
            enabled.Value = false;
            c.Check(!feature.IsActive && feature.Status == "Off (disabled in settings).",
                $"turned off while sheathed: status '{feature.Status}' ({feature.State})");
            c.Check(ReferenceEquals(player.m_hiddenRightItem, sword) && player.m_hiddenLeftItem == null && !Hands.IsMarked(axe),
                $"turned off while sheathed: the off-hand weapon is forgotten, the sword stays sheathed ({HandsText(player)})");
            yield return null;
            yield return null;
            yield return null;
            if (vis != null)
            {
                c.Check(vis.m_leftBackItemInstance == null, "turned off while sheathed: no second weapon drawn on the back");
                CheckVanillaPose(c, vis, vis.m_rightBackItemInstance, vis.m_currentRightBackItemHash, vis.m_backMelee,
                    "turned off while sheathed: the sword");
            }
            player.ShowHandItems();
            yield return null;
            c.Check(Holds(player, sword, null) && !axe.m_equipped, $"turned off while sheathed: R draws only the sword ({HandsText(player)})");
            enabled.Value = true;
            ServerRules.TestRules = null;
            yield return WaitFor(() => ServerRules.UsingServer, 8f);
            c.Check(feature.IsActive && feature.Status == "Active.",
                $"turned on after the sheathed case: status '{feature.Status}' ({feature.State})");

            // M05: with refusal on, off and on again at once: the server checks after its grace and the player stays.
            yield return CallStep(ServerPeerStep, "check");
            compatible = ReplyOk && Field("compatible") == "True";
            c.Check(compatible, $"compatible before the quick off and on ({_reply})");
            if (compatible)
            {
                yield return CallStep(ServerSetStep, "AllowPlayersWithoutMod=false");
                var refusing = ReplyOk && Field("allow") == "False";
                c.Check(refusing, $"server AllowPlayersWithoutMod = false ({_reply})");
                yield return new WaitForSecondsRealtime(PlayerCheck.GraceSeconds + 1.5f);
                yield return CallStep(ServerPeerStep, "");
                int.TryParse(Field("verdicts"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var verdicts);
                if (refusing && ReplyOk && Field("kicked") == "False")
                {
                    bench.Pair(sword, axe);
                    yield return null;
                    enabled.Value = false;
                    enabled.Value = true;
                    ServerRules.TestRules = null;
                    // Grace, then the time a refused player's connection is cut after: still here = not refused.
                    yield return new WaitForSecondsRealtime(PlayerCheck.GraceSeconds + PlayerCheck.DisconnectDelay + 1f);
                    c.Check(Connected && feature.IsActive,
                        $"off and on again at once: the player stays connected (status {ZNet.GetConnectionStatus()})");
                    yield return CallStep(ServerPeerStep, "");
                    int.TryParse(Field("verdicts"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var after);
                    c.Check(ReplyOk && after > verdicts && Field("last") == nameof(JoinVerdict.Compatible)
                            && Field("compatible") == "True" && Field("kicked") == "False",
                        $"the server checked the player again after its grace and found it compatible ({after - verdicts} check(s); {_reply})");
                }
            }
            yield return ResetServer(c);
            c.Report();
        }
        finally
        {
            if (!enabled.Value)
            {
                enabled.Value = true;
            }
            if (bench != null)
            {
                bench.TakeBack();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
        }
    }

    // ---------- dual.mp.server-off ----------

    private static IEnumerator RunMpServerOff()
    {
        var c = new Checks(MpServerOffName);
        var player = MpPlayer(MpServerOffName);
        var found = FeatureRegistry.Find(ModInfo.Guid);
        if (player == null)
        {
            yield break;
        }
        if (found == null)
        {
            SelfTest.Fail(MpServerOffName, "the mod is not in the MC Mods list");
            yield break;
        }
        var feature = found.Value;
        yield return WaitIdle(player);
        Bench bench = null;
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

            // M13: the server's owner turns the mod off.
            yield return CallStep(ProbeSetEnabledStep, ModInfo.Guid + "=off");
            c.Check(ReplyOk, $"the server turned the mod off ({_reply})");
            yield return WaitFor(() => feature.State == nameof(ModState.ServerMissing), 10f);
            c.Check(feature.State == nameof(ModState.ServerMissing)
                    && feature.Status == "Inactive: the server has this mod turned off (or it is not working there).",
                $"server off: the client's status is '{feature.Status}' ({feature.State})");
            c.Check(Holds(player, sword, null) && !axe.m_equipped && !Hands.IsMarked(axe),
                $"server off: the client's off-hand weapon is put away ({HandsText(player)})");
            player.EquipItem(axe);
            yield return null;
            c.Check(Holds(player, axe, null) && !sword.m_equipped,
                $"server off: equipping is vanilla, a second weapon replaces the first ({HandsText(player)})");
            c.Check(ServerRules.LastLogged == null && Connected, "server off: the player stays connected, no server rules in use");

            // On again: active, the server's rules logged again, pairs work.
            yield return CallStep(ProbeSetEnabledStep, ModInfo.Guid + "=on");
            c.Check(ReplyOk, $"the server turned the mod on again ({_reply})");
            yield return WaitFor(() => feature.IsActive, 10f);
            ServerRules.TestRules = null;
            yield return WaitFor(() => ServerRules.UsingServer && ServerRules.LastLogged != null, 8f);
            c.Check(feature.IsActive && ServerRules.UsingServer && ServerRules.LastLogged != null,
                $"server on again: active ('{feature.Status}'), the server's rules logged again ('{ServerRules.LastLogged}')");
            bench.Pair(sword, axe);
            yield return null;
            c.Check(Holds(player, sword, axe) && StateI(player) == 15 && Connected,
                $"server on again: pairs work ({HandsText(player)})");
            Invariant(c, player, "server on again");
            c.Report();
        }
        finally
        {
            if (bench != null)
            {
                bench.TakeBack();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
        }
    }

    // ---------- dual.mp.no-server (scenario vanilla-server) ----------

    private static IEnumerator RunMpNoServer()
    {
        var c = new Checks(MpNoServerName);
        var player = MpPlayer(MpNoServerName);
        var found = FeatureRegistry.Find(ModInfo.Guid);
        if (player == null)
        {
            yield break;
        }
        if (found == null)
        {
            SelfTest.Fail(MpNoServerName, "the mod is not in the MC Mods list");
            yield break;
        }
        var feature = found.Value;
        yield return WaitIdle(player);
        Bench bench = null;
        try
        {
            // M14: the server has no Dual Wielding.
            c.Check(feature.State == nameof(ModState.ServerMissing)
                    && feature.Status == "Inactive: the server does not have this mod. It must be installed on the server too.",
                $"server without the mod: status '{feature.Status}' ({feature.State})");
            bench = new Bench(player);
            var inventory = bench.Inventory;
            var sword = bench.Give(Sword);
            var axe = bench.Give(Axe);
            if (sword == null || axe == null)
            {
                c.Check(false, "could not give the test items");
                c.Report();
                yield break;
            }
            // A pair from the save (both flagged equipped, the axe marked) loads as one weapon, whichever comes first.
            foreach (var offFirst in new[] { true, false })
            {
                LoadPair(player, inventory, sword, axe, offFirst);
                yield return null;
                yield return null;
                var one = player.m_leftItem == null
                          && (ReferenceEquals(player.m_rightItem, sword) || ReferenceEquals(player.m_rightItem, axe))
                          && sword.m_equipped != axe.m_equipped
                          && sword.m_equipped == ReferenceEquals(player.m_rightItem, sword);
                c.Check(one, $"server without the mod: a saved pair ({(offFirst ? "off-hand" : "main")} weapon first) loads as "
                             + $"one weapon in the main hand ({HandsText(player)}; flags sword {sword.m_equipped}, axe {axe.m_equipped})");
            }
            // Equipping is vanilla.
            bench.Empty();
            player.EquipItem(sword);
            player.EquipItem(axe);
            yield return null;
            c.Check(Holds(player, axe, null) && !sword.m_equipped,
                $"server without the mod: a second one-handed weapon replaces the first ({HandsText(player)})");
            var patchErrors = typeof(PatchGuard).GetField("Reported", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as HashSet<string>;
            c.Check(patchErrors != null && patchErrors.Count == 0,
                $"no failed patch reported by the mod ({(patchErrors != null ? string.Join(", ", patchErrors.ToArray()) : "the list of failed patches could not be read")})");
            c.Report();
        }
        finally
        {
            if (bench != null)
            {
                bench.TakeBack();
            }
            Bench.ClearOverrides();
            Hands.ResetState();
        }
    }
}
#endif
