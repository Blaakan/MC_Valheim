#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx.Logging;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.TrinketsOnDemandMod;

// Debug build only. Multiplayer self tests (tools/Test-Multiplayer.ps1; same class as SelfTests.cs): one client joined
// to real dedicated server, both with every MC mod. Client tests (scenario "modded") and their server halves:
//   trinkets.mp.rules         M01, M05, T14: server's rules reach client; IncomePerSecond = 3 set on server
//                             while playing arrives live (Info line, 3 per second in fight, own config untouched);
//                             server's own key and message settings change nothing for player; player's own key
//                             setting changed while playing (U, F13, none)
//   trinkets.mp.fight         M09 (T02, T17 with server's rules), M06: credit, dodge, hunt and hit messages that
//                             come from another game (sent by server)
//   trinkets.mp.full-bar      M09 (T08 with server's rules)
//   trinkets.mp.hand-off      M03: trinket used with mod, then dropped: server sees plain item
//   trinkets.mp.toggle        L01, L02, M02, M07: with AllowPlayersWithoutMod on server, mod really turned off
//                             and on in its settings while connected: status text, game's bar while off, back on
//   trinkets.mp.server-log    M02, M09: server allowed this player, logged no error and no unexpected warning
//   trinkets.mp.quick-toggle  M07 (c): off and on again within grace: player stays
// Scenario "vanilla-server" (registered at start: me inactive there):
//   trinkets.mp.no-server-mod M04: status text, game's bar, no key, no tooltip line
// Multiplayer runs use throwaway configs: these tests may write settings, on client and (server steps) on server.
internal static partial class SelfTests
{
    private const string MpRulesName = "trinkets.mp.rules";
    private const string MpFightName = "trinkets.mp.fight";
    private const string MpFullBarName = "trinkets.mp.full-bar";
    private const string MpHandOffName = "trinkets.mp.hand-off";
    private const string MpToggleName = "trinkets.mp.toggle";
    private const string MpServerLogName = "trinkets.mp.server-log";
    private const string MpQuickToggleName = "trinkets.mp.quick-toggle";
    private const string MpNoServerName = "trinkets.mp.no-server-mod";
    private const string VanillaServerScenario = "vanilla-server"; // tools/Test-Multiplayer.ps1 scenario name

    private const string SrvRulesStep = "trinkets.mp.srv-rules";
    private const string SrvSetStep = "trinkets.mp.srv-set";
    private const string SrvLogStep = "trinkets.mp.srv-log";
    private const string SrvSendStep = "trinkets.mp.srv-send";
    private const string SrvItemStep = "trinkets.mp.srv-item";

    private const string ServerRulesLine = "Using the server's rules: ";
    private const string AllowedLine = "with the same network version: allowed.";
    private const string LetInLine = "AllowPlayersWithoutMod is on";
    private const string TurnedOffReason = "has the mod turned off";

    // Every test of other SelfTests*.cs files and of this one, in running order. clean-log last.
    private static void RegisterMore()
    {
        EnsureLog();
        SelfTest.Register(ParityName, RunVanillaParity);
        SelfTest.Register(BlockFightName, RunBlockFight);
        SelfTest.Register(NoFightName, RunNoFight);
        SelfTest.Register(TargetingName, RunTargeting);
        SelfTest.Register(DrainName, RunDrain);
        SelfTest.Register(FullBarName, RunFullBar);
        SelfTest.Register(AnswersName, RunAnswers);
        SelfTest.Register(GatesName, RunGates);
        SelfTest.Register(KeysName, RunKeys);
        SelfTest.Register(FeedbackName, RunFeedback);
        SelfTest.Register(SwapName, RunSwap);
        SelfTest.Register(BowHitsName, RunBowHits);
        SelfTest.Register(CrossbowShotName, RunCrossbowShot);
        SelfTest.Register(OtherRangedName, RunOtherRanged);
        SelfTest.Register(ToggleName, RunToggle);
        SelfTest.Register(DualWieldName, RunDualWield);
        SelfTest.Register(DodgeFightName, RunDodgeFight);
        SelfTest.Register(PvpDodgeBugName, RunPvpDodgeBug);
        SelfTest.Register(CleanLogName, RunCleanLog);

        SelfTest.RegisterMultiplayer(MpRulesName, SelfTest.Modded, RunMpRules);
        SelfTest.RegisterMultiplayer(MpFightName, SelfTest.Modded, RunMpFight);
        SelfTest.RegisterMultiplayer(MpFullBarName, SelfTest.Modded, RunMpFullBar);
        SelfTest.RegisterMultiplayer(MpHandOffName, SelfTest.Modded, RunMpHandOff);
        SelfTest.RegisterMultiplayer(MpToggleName, SelfTest.Modded, RunMpToggle);
        SelfTest.RegisterMultiplayer(MpServerLogName, SelfTest.Modded, RunMpServerLog);
        SelfTest.RegisterMultiplayer(MpQuickToggleName, SelfTest.Modded, RunMpQuickToggle);
        SelfTest.RegisterServerStep(SrvRulesStep, ServerRulesStep);
        SelfTest.RegisterServerStep(SrvSetStep, ServerSetStep);
        SelfTest.RegisterServerStep(SrvLogStep, ServerLogStep);
        SelfTest.RegisterServerStep(SrvSendStep, ServerSendStep);
        SelfTest.RegisterServerStep(SrvItemStep, ServerItemStep);
    }

    private static void UnregisterMore()
    {
        foreach (var name in new[]
                 {
                     ParityName, BlockFightName, NoFightName, TargetingName, DrainName, FullBarName, AnswersName, GatesName,
                     KeysName, FeedbackName, SwapName, BowHitsName, CrossbowShotName, OtherRangedName, ToggleName,
                     DualWieldName, DodgeFightName, PvpDodgeBugName, CleanLogName,
                 })
        {
            SelfTest.Unregister(name);
        }
        foreach (var name in new[]
                 {
                     MpRulesName, MpFightName, MpFullBarName, MpHandOffName, MpToggleName, MpServerLogName, MpQuickToggleName,
                 })
        {
            SelfTest.UnregisterMultiplayer(name);
        }
        foreach (var step in new[] { SrvRulesStep, SrvSetStep, SrvLogStep, SrvSendStep, SrvItemStep })
        {
            SelfTest.UnregisterServerStep(step);
        }
    }

    // ---------- server halves ----------

    // One player of run (test client), once its character exists.
    private static ZNetPeer OnlyPlayer()
    {
        var net = ZNet.instance;
        if (net == null)
        {
            return null;
        }
        foreach (var peer in net.GetPeers())
        {
            if (peer != null && peer.IsReady() && !peer.m_characterID.IsNone())
            {
                return peer;
            }
        }
        return null;
    }

    private static string ServerState()
    {
        return ServerRules.Current.Describe()
               + "|key=" + (Plugin.TriggerKey != null ? Plugin.TriggerKey.Value.ToString() : "?")
               + "|message=" + (Plugin.ShowFullMessage != null ? Plugin.ShowFullMessage.Value.ToString() : "?")
               + "|allow=" + (Plugin.AllowPlayersWithoutMod != null ? Plugin.AllowPlayersWithoutMod.Value.ToString() : "?");
    }

    // Server's own rules, as text.
    private static IEnumerator ServerRulesStep(string arg, object[] reply)
    {
        yield return null;
        SelfTest.Answer(reply, true, ServerState());
    }

    // Server owner changes settings while game runs: "income=3;key=U;message=false;allow=true".
    private static IEnumerator ServerSetStep(string arg, object[] reply)
    {
        yield return null;
        try
        {
            foreach (var part in (arg ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = part.IndexOf('=');
                var key = eq > 0 ? part.Substring(0, eq).Trim() : "";
                var value = eq > 0 ? part.Substring(eq + 1).Trim() : "";
                switch (key)
                {
                    case "income":
                        Plugin.IncomePerSecond.Value = float.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "key":
                        Plugin.TriggerKey.Value = (KeyCode)Enum.Parse(typeof(KeyCode), value);
                        break;
                    case "message":
                        Plugin.ShowFullMessage.Value = bool.Parse(value);
                        break;
                    case "allow":
                        Plugin.AllowPlayersWithoutMod.Value = bool.Parse(value);
                        break;
                    default:
                        SelfTest.Answer(reply, false, $"unknown setting '{part}'");
                        yield break;
                }
            }
        }
        catch (Exception e)
        {
            SelfTest.Answer(reply, false, $"could not set '{arg}': {e.Message}");
            yield break;
        }
        SelfTest.Answer(reply, true, ServerState());
    }

    // What me logged on server since it started.
    private static IEnumerator ServerLogStep(string arg, object[] reply)
    {
        yield return null;
        EnsureLog();
        var errors = 0;
        var warnings = 0;
        var allowed = 0;
        var letIn = 0;
        var letInOff = 0;
        var refused = 0;
        var sent = 0;
        string first = null;
        foreach (var line in _seen.Since(0))
        {
            if (!line.Mine)
            {
                // Other source's error that names my code (exception out of mod on server): error too.
                // Flash warning of HUD cannot come on server: left out.
                if ((line.Level & (LogLevel.Error | LogLevel.Fatal)) != 0)
                {
                    errors++;
                    first = first ?? FirstLine(line.Text);
                }
                continue;
            }
            var text = line.Text;
            if (text.Contains(AllowedLine))
            {
                allowed++;
            }
            if (text.StartsWith("Sent the " + ModInfo.Name + " rules to ", StringComparison.Ordinal))
            {
                sent++;
            }
            if ((line.Level & (LogLevel.Error | LogLevel.Fatal)) != 0)
            {
                errors++;
                first = first ?? FirstLine(text);
            }
            else if ((line.Level & LogLevel.Warning) != 0)
            {
                if (text.Contains(LetInLine))
                {
                    letIn++;
                    if (text.Contains(TurnedOffReason))
                    {
                        letInOff++;
                    }
                }
                else if (text.StartsWith("Refused ", StringComparison.Ordinal))
                {
                    refused++;
                }
                else
                {
                    warnings++;
                    first = first ?? FirstLine(text);
                }
            }
        }
        SelfTest.Answer(reply, true,
            $"errors={errors};warnings={warnings};allowed={allowed};letin={letIn};letinoff={letInOff};refused={refused};sent={sent};first={first ?? ""}");
    }

    // Server sends player what another player's game would send about creature it controls:
    //   credit=<n>           stagger credit (Character.RPC_AddAdrenaline)
    //   dodge                player dodged that creature's attack (Player.RPC_HitWhileDodging)
    //   targeted             that creature, alerted, targets player (Player OnTargeted)
    //   hit=<user>:<id>      that creature (its ZDO id) hit player (Character.RPC_Damage, not blockable)
    private static IEnumerator ServerSendStep(string arg, object[] reply)
    {
        yield return null;
        var peer = OnlyPlayer();
        var rpc = ZRoutedRpc.instance;
        if (peer == null || rpc == null)
        {
            SelfTest.Answer(reply, false, "no player with a character on the server");
            yield break;
        }
        try
        {
            var eq = (arg ?? "").IndexOf('=');
            var what = eq > 0 ? arg.Substring(0, eq) : arg ?? "";
            var value = eq > 0 ? arg.Substring(eq + 1) : "";
            switch (what)
            {
                case "credit":
                    rpc.InvokeRoutedRPC(peer.m_uid, peer.m_characterID, "RPC_AddAdrenaline", float.Parse(value, CultureInfo.InvariantCulture));
                    break;
                case "dodge":
                    rpc.InvokeRoutedRPC(peer.m_uid, peer.m_characterID, "RPC_HitWhileDodging");
                    break;
                case "targeted":
                    rpc.InvokeRoutedRPC(peer.m_uid, peer.m_characterID, "OnTargeted", true, true);
                    break;
                case "hit":
                {
                    var parts = value.Split(':');
                    var hit = new HitData();
                    hit.m_damage.m_blunt = 0.1f;
                    hit.m_point = peer.m_refPos;
                    hit.m_dir = Vector3.forward;
                    hit.m_attacker = new ZDOID(long.Parse(parts[0], CultureInfo.InvariantCulture), uint.Parse(parts[1], CultureInfo.InvariantCulture));
                    rpc.InvokeRoutedRPC(peer.m_uid, peer.m_characterID, "RPC_Damage", hit);
                    break;
                }
                default:
                    SelfTest.Answer(reply, false, $"unknown message '{arg}'");
                    yield break;
            }
        }
        catch (Exception e)
        {
            SelfTest.Answer(reply, false, $"could not send '{arg}': {e.Message}");
            yield break;
        }
        SelfTest.Answer(reply, true, $"sent {arg} to {peer.m_playerName}");
    }

    // What server holds of dropped item ("<user>:<id>" of its object): data any other player's game gets.
    private static IEnumerator ServerItemStep(string arg, object[] reply)
    {
        yield return null;
        try
        {
            var parts = (arg ?? "").Split(':');
            var id = new ZDOID(long.Parse(parts[0], CultureInfo.InvariantCulture), uint.Parse(parts[1], CultureInfo.InvariantCulture));
            var zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(id) : null;
            if (zdo == null)
            {
                SelfTest.Answer(reply, false, "the server does not have this object (yet)");
                yield break;
            }
            var data = new ItemDrop.ItemData();
            ItemDrop.LoadFromZDO(data, zdo);
            SelfTest.Answer(reply, true,
                $"prefab={zdo.GetPrefab()};stack={data.m_stack};quality={data.m_quality};custom={data.m_customData.Count};keys="
                + string.Join(",", data.m_customData.Keys.ToArray()));
        }
        catch (Exception e)
        {
            SelfTest.Answer(reply, false, $"could not read the item '{arg}': {e.Message}");
        }
    }

    // ---------- client helpers ----------

    private static string Field(string detail, string key)
    {
        foreach (var part in (detail ?? "").Split(';', '|'))
        {
            if (part.StartsWith(key + "=", StringComparison.Ordinal))
            {
                return part.Substring(key.Length + 1);
            }
        }
        return "";
    }

    private static int Number(string detail, string key) =>
        int.TryParse(Field(detail, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : -1;

    private static bool Joined()
    {
        var net = ZNet.instance;
        return net != null && !net.IsServer() && Player.m_localPlayer != null
               && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected;
    }

    // Client of run's dedicated server, using its rules (no test rules left over).
    private static bool OnServerRules(Checks c)
    {
        ClearOverrides();
        return c.Check(SelfTest.IsMultiplayerRun && Joined() && ServerRules.UsingServer && !ServerRules.Current.IsPending,
            $"not a client that uses its server's rules (joined {Joined()}, server rules in use {ServerRules.UsingServer})");
    }

    private static string OwnConfigText()
    {
        var entry = Plugin.IncomePerSecond;
        return entry != null && entry.ConfigFile != null && File.Exists(entry.ConfigFile.ConfigFilePath)
            ? File.ReadAllText(entry.ConfigFile.ConfigFilePath)
            : null;
    }

    // ---------- trinkets.mp.rules (M01, M05, T14) ----------

    private static IEnumerator RunMpRules()
    {
        var rig = Rig.Create(MpRulesName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(MpRulesName);
        var mark = LogMark();
        var keyBefore = Plugin.TriggerKey.Value;
        var buttonBefore = Plugin.GamepadButton.Value;
        var warned = Controls.TestWarned;
        try
        {
            var p = rig.P;
            if (!OnServerRules(c))
            {
                c.Report();
                yield break;
            }
            var reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(SrvRulesStep, "", reply);
            if (!c.Check(reply.Answered && reply.Ok, $"server rules step: {reply}"))
            {
                c.Report();
                yield break;
            }
            var plain = reply.Detail.Split('|')[0];
            c.Check(ServerRules.Current.Describe() == plain, $"the client uses '{ServerRules.Current.Describe()}', the server has '{plain}'");
            var baseIncome = ServerRules.Current.IncomePerSecond;
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            var foe = rig.Tough(FoeName, Flat(p.transform.forward) * 4f);
            if (!c.Check(item != null && foe != null, $"could not equip a trinket or spawn {FoeName}"))
            {
                c.Report();
                yield break;
            }
            var hash = item.m_shared.m_fullAdrenalineSE.NameHash();
            rig.Effect(hash);
            var max = p.GetMaxAdrenaline();

            // M01: IncomePerSecond = 3 in server's config while player plays.
            var configBefore = OwnConfigText();
            var ruleMark = LogMark();
            yield return SelfTest.CallServer(SrvSetStep, "income=3", reply);
            c.Check(reply.Answered && reply.Ok, $"server set income=3: {reply}");
            var three = reply.Detail.Split('|')[0];
            yield return Until(() => Near(ServerRules.Current.IncomePerSecond, 3f), 6f);
            c.Check(ServerRules.UsingServer && Near(ServerRules.Current.IncomePerSecond, 3f) && ServerRules.Current.Describe() == three,
                $"after the server's change the client uses '{ServerRules.Current.Describe()}', the server has '{three}'");
            var lines = LogLines(ruleMark, ServerRulesLine);
            c.Check(lines.Count == 1 && lines[0].StartsWith(ServerRulesLine + "income 3 adrenaline per second in a fight", StringComparison.Ordinal),
                $"{lines.Count} Info line(s) '{ServerRulesLine}...': {(lines.Count > 0 ? lines[0] : "none")}");
            c.Check(Near(Plugin.IncomePerSecond.Value, TrinketRules.Default.IncomePerSecond) && configBefore != null
                    && OwnConfigText() == configBefore && configBefore.Contains("IncomePerSecond = 1"),
                $"the player's own setting is {F(Plugin.IncomePerSecond.Value)} and its config file changed: {OwnConfigText() != configBefore}");

            // About 3 per second in fight.
            CombatState.Reset();
            yield return new WaitForSeconds(1.2f);
            p.m_adrenaline = 0f;
            HitFromPlayer(p, foe);
            var ticks = new List<float>();
            var prev = 0f;
            var t0 = Time.time;
            while (Time.time - t0 < 3.6f)
            {
                var now = p.m_adrenaline;
                if (now > prev + 0.0001f)
                {
                    ticks.Add(now - prev);
                }
                prev = now;
                yield return null;
            }
            var wantTick = GainAt(p, 3f, 0f);
            c.Check(ticks.Count >= 3 && ticks.Count <= 4 && ticks.All(t => Near(t, wantTick, 0.001f)),
                $"in a fight with the server's 3 per second: {ticks.Count} income tick(s) in 3.6 s of {string.Join(", ", ticks.Select(F).ToArray())} (expected {F(wantTick)} each)");

            // Back to default.
            yield return SelfTest.CallServer(SrvSetStep, "income=" + baseIncome.ToString(CultureInfo.InvariantCulture), reply);
            yield return Until(() => Near(ServerRules.Current.IncomePerSecond, baseIncome), 6f);
            c.Check(reply.Answered && reply.Ok && Near(ServerRules.Current.IncomePerSecond, baseIncome) && ServerRules.Current.Describe() == plain,
                $"after the server put it back the client uses '{ServerRules.Current.Describe()}'");

            // M05: server's own TriggerKey and ShowFullMessage personal, not rules.
            var personalMark = LogMark();
            yield return SelfTest.CallServer(SrvSetStep, "key=U;message=false", reply);
            c.Check(reply.Answered && reply.Ok && reply.Detail.Contains("key=U") && reply.Detail.Contains("message=False"),
                $"server set key=U;message=false: {reply}");
            yield return new WaitForSecondsRealtime(1.5f);
            c.Check(LogCount(personalMark, ServerRulesLine) == 0 && ServerRules.Current.Describe() == plain,
                "the server's key and message settings changed the client's rules");
            c.Check(Controls.Key == KeyCode.Y && Plugin.TriggerKey.Value == KeyCode.Y && Plugin.ShowFullMessage.Value,
                $"the player's own key is {Controls.Key}, message {Plugin.ShowFullMessage.Value} (expected Y and true)");
            var label = Controls.Label();
            var messages = Feedback.FullMessageCount;
            CombatState.Reset();
            yield return FullEdge(p, max, true);
            c.Check(Feedback.FullMessageCount == messages + 1 && label != null && Feedback.LastFullMessage == FullMessage(label)
                    && TopLeftHas(FullMessage(label)) && (ZInput.IsGamepadActive() || label == "[Y]"),
                $"full bar: {Feedback.FullMessageCount - messages} message(s), text '{Feedback.LastFullMessage}'");
            yield return Press();
            c.Check(FullBar.LastResult == TriggerResult.Fired && rig.Has(hash), $"the player's own trigger: answer {FullBar.LastResult}");
            yield return SelfTest.CallServer(SrvSetStep, "key=Y;message=true", reply);
            c.Check(reply.Answered && reply.Ok, $"server set key=Y;message=true: {reply}");

            // T14 with real setting, changed while playing.
            rig.Effect(hash);
            Plugin.TriggerKey.Value = KeyCode.U;
            c.Check(Controls.Key == KeyCode.U && Controls.KeyLabel(KeyCode.U) == "[U]", $"TriggerKey set to U while playing: key in use {Controls.Key}");
            if (!ZInput.IsGamepadActive())
            {
                c.Check(TipOf(item).Contains(TriggerLine("[U]")), "TriggerKey U: the tooltip line does not say [U]");
                yield return FullEdge(p, max, true);
                c.Check(Feedback.LastFullMessage == FullMessage("[U]") && TopLeftHas(FullMessage("[U]")), $"TriggerKey U: full-bar message '{Feedback.LastFullMessage}'");
            }
            Controls.TestWarned = KeyCode.None;
            var keyMark = LogMark();
            Plugin.TriggerKey.Value = KeyCode.F13;
            var warnings = LogLines(keyMark, F13Warning);
            c.Check(Controls.Key == KeyCode.None && warnings.Count == 1
                    && warnings[0].StartsWith("Controls.TriggerKey = F13: the game cannot read this key, so the keyboard trigger is off.", StringComparison.Ordinal),
                $"TriggerKey set to F13: key in use {Controls.Key}, {warnings.Count} warning(s)");
            var pad = Controls.PadLabel();
            yield return FullEdge(p, max, true);
            c.Check(pad != null && Controls.Label() == pad && Feedback.LastFullMessage == FullMessage(pad),
                $"TriggerKey F13: full-bar message '{Feedback.LastFullMessage}', expected the gamepad buttons '{pad}'");
            Plugin.TriggerKey.Value = KeyCode.None;
            Plugin.GamepadButton.Value = GamepadButton.None;
            yield return FullEdge(p, max, true);
            c.Check(Controls.Label() == null
                    && Feedback.LastFullMessage == "Adrenaline full: set a trigger key for " + ModInfo.Name + " to trigger your trinket"
                    && TipOf(item).Contains("\nTrigger: no key set (" + ModInfo.Name + " settings)"),
                $"no key and no gamepad button: message '{Feedback.LastFullMessage}'");
            Plugin.TriggerKey.Value = keyBefore;
            Plugin.GamepadButton.Value = buttonBefore;
            c.Check(Controls.Key == keyBefore, $"settings put back: key in use {Controls.Key}");
            CheckCleanLog(c, mark, F13Warning);
        }
        finally
        {
            if (Plugin.TriggerKey.Value != keyBefore)
            {
                Plugin.TriggerKey.Value = keyBefore;
            }
            if (Plugin.GamepadButton.Value != buttonBefore)
            {
                Plugin.GamepadButton.Value = buttonBefore;
            }
            Controls.TestWarned = warned;
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.mp.fight (M09: T02 and T17 on dedicated server; M06) ----------

    private static IEnumerator RunMpFight()
    {
        var rig = Rig.Create(MpFightName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(MpFightName);
        var mark = LogMark();
        try
        {
            var p = rig.P;
            if (!OnServerRules(c))
            {
                c.Report();
                yield break;
            }
            var rules = ServerRules.Current;
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            var fwd = Flat(p.transform.forward);
            var foe = rig.Tough(FoeName, fwd * 2f);
            var side = rig.Tough(FoeName, Flat(p.transform.right) * 5f);
            var shield = rig.Give(ShieldName);
            var sword = rig.Give(SwordName);
            var bow = rig.Give(BowName);
            var arrows = rig.Give(ArrowName, 20);
            if (!c.Check(item != null && foe != null && side != null && shield != null && sword != null && bow != null && arrows != null,
                    "could not set up the trinket, the weapons or the creatures"))
            {
                c.Report();
                yield break;
            }
            var hash = item.m_shared.m_fullAdrenalineSE.NameHash();
            rig.Effect(hash);
            var max = p.GetMaxAdrenaline();
            rig.EmptyHands();
            c.Check(p.EquipItem(sword, false) && p.EquipItem(shield, false) && ReferenceEquals(p.m_leftItem, shield), $"could not hold {SwordName} and {ShieldName}");
            rig.TakeControls();
            Face(p, fwd);
            yield return FixedTicks(3);

            // T02 with server's rules.
            yield return BlockFightCore(c, rig, rules, foe, shield, 4);

            // T08 here too: block on full bar pays nothing and fires nothing (shield still up, long past parry).
            if (c.Check(p.IsBlocking() && ReferenceEquals(p.GetCurrentBlocker(), shield), "the shield is no longer raised after the blocks"))
            {
                p.m_adrenaline = max;
                var blockCalls = FullBar.Calls;
                HitPlayerFront(p, foe, true);
                c.Check(FullBar.Calls == blockCalls + 1 && Near(FullBar.LastAmount, shield.m_shared.m_blockAdrenaline)
                        && Near(p.m_adrenaline, max) && !rig.Has(hash),
                    $"full bar + block: bar {F(p.m_adrenaline)} of {F(max)}, effect {rig.Has(hash)}");
            }
            rig.Block(false);

            // M06: messages that come from another player's game about creature it controls (server sends them).
            var reply = new SelfTest.ServerReply();
            CombatState.Reset();
            yield return new WaitForSeconds(1.2f);
            p.m_adrenaline = 10f;
            var want = Gain(p, 3f);
            yield return SelfTest.CallServer(SrvSendStep, "credit=3", reply);
            yield return Until(() => p.m_adrenaline > 10.0001f, 3f);
            c.Check(reply.Answered && reply.Ok && Near(p.m_adrenaline, 10f + want), $"stagger credit of 3 from another game on a bar of 10: bar {F(p.m_adrenaline)} ({reply})");
            p.m_adrenaline = max;
            var calls = FullBar.Calls;
            yield return SelfTest.CallServer(SrvSendStep, "credit=3", reply);
            yield return Until(() => FullBar.Calls > calls, 3f);
            c.Check(reply.Ok && FullBar.Calls > calls && Near(p.m_adrenaline, max) && !rig.Has(hash),
                $"stagger credit from another game on a full bar: arrived {FullBar.Calls > calls}, bar {F(p.m_adrenaline)} of {F(max)}, effect {rig.Has(hash)}");
            CombatState.Reset();
            calls = FullBar.Calls;
            yield return SelfTest.CallServer(SrvSendStep, "dodge", reply);
            yield return Until(() => FullBar.Calls > calls, 3f);
            c.Check(reply.Ok && FullBar.Calls > calls && Near(p.m_adrenaline, max) && !rig.Has(hash) && !float.IsNegativeInfinity(CombatState.LastExchange),
                $"perfect dodge reported by another game on a full bar: arrived {FullBar.Calls > calls}, bar {F(p.m_adrenaline)}, fight {!float.IsNegativeInfinity(CombatState.LastExchange)}");
            CombatState.Reset();
            yield return SelfTest.CallServer(SrvSendStep, "targeted", reply);
            yield return Until(() => !float.IsNegativeInfinity(CombatState.LastTargeted), 3f);
            c.Check(reply.Ok && !float.IsNegativeInfinity(CombatState.LastTargeted), $"'an alerted creature targets you' from another game did not reach the mod ({reply})");
            CombatState.Reset();
            var id = foe.GetZDOID();
            yield return SelfTest.CallServer(SrvSendStep, "hit=" + id.UserID.ToString(CultureInfo.InvariantCulture) + ":" + id.ID.ToString(CultureInfo.InvariantCulture), reply);
            yield return Until(() => !float.IsNegativeInfinity(CombatState.LastExchange), 3f);
            c.Check(reply.Ok && !float.IsNegativeInfinity(CombatState.LastExchange) && Near(p.m_adrenaline, max) && !rig.Has(hash),
                $"hit by a creature, sent by another game: fight {!float.IsNegativeInfinity(CombatState.LastExchange)}, bar {F(p.m_adrenaline)} of {F(max)} ({reply})");

            // T17 with server's rules.
            yield return Until(() => Calm(p), 3f);
            rig.EmptyHands();
            c.Check(p.EquipItem(bow, false), $"could not equip {BowName}");
            AimUp(rig);
            yield return new WaitForSeconds(0.5f);
            yield return BowHitsCore(c, rig, rules, bow, arrows, side);
            c.Check(ServerRules.UsingServer && ServerRules.Current.Describe() == rules.Describe(), "the rules in use changed during the test");
            CheckCleanLog(c, mark);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.mp.full-bar (M09: T08 on dedicated server) ----------

    private static IEnumerator RunMpFullBar()
    {
        var rig = Rig.Create(MpFullBarName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(MpFullBarName);
        var mark = LogMark();
        try
        {
            var p = rig.P;
            if (!OnServerRules(c))
            {
                c.Report();
                yield break;
            }
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            var fwd = Flat(p.transform.forward);
            var foe = rig.Tough(FoeName, -fwd * 5f);
            var sword = rig.Give(SwordName);
            if (!c.Check(item != null && foe != null && sword != null, $"could not equip a trinket, give {SwordName} or spawn {FoeName}"))
            {
                c.Report();
                yield break;
            }
            rig.Effect(item.m_shared.m_fullAdrenalineSE.NameHash());
            rig.EmptyHands();
            p.EquipItem(sword, false);
            rig.TakeControls();
            Face(p, fwd);
            yield return FixedTicks(3);
            yield return FullBarCore(c, rig, item, sword, foe, 60f);
            c.Check(ServerRules.UsingServer, "the server's rules were no longer in use at the end");
            CheckCleanLog(c, mark);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.mp.hand-off (M03) ----------

    private static IEnumerator RunMpHandOff()
    {
        var rig = Rig.Create(MpHandOffName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(MpHandOffName);
        var mark = LogMark();
        try
        {
            var p = rig.P;
            if (!OnServerRules(c))
            {
                c.Report();
                yield break;
            }
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            if (!c.Check(item != null && item.m_dropPrefab != null, "could not equip a trinket"))
            {
                c.Report();
                yield break;
            }
            var hash = item.m_shared.m_fullAdrenalineSE.NameHash();
            rig.Effect(hash);
            var prefabName = item.m_dropPrefab.name;
            var shared = PrefabShared(prefabName);
            var cost = item.m_shared.m_maxAdrenaline;
            var effect = item.m_shared.m_fullAdrenalineSE;

            // Used with mod: held full, fired with key.
            p.m_adrenaline = 0f;
            ConsoleAdrenaline(p, 999f);
            yield return Press();
            c.Check(FullBar.LastResult == TriggerResult.Fired, $"the key did not fire the trinket before the hand-off (answer {FullBar.LastResult})");
            c.Check(!item.m_customData.Keys.Any(k => k.StartsWith(ModInfo.Guid, StringComparison.Ordinal)) && shared != null
                    && ReferenceEquals(shared.m_fullAdrenalineSE, effect) && Near(shared.m_maxAdrenaline, cost),
                "the used trinket carries data of this mod, or its prefab changed");

            // Dropped (4 m away: out of reach of auto pick-up). What server holds is what any other player gets.
            rig.Effect(hash);
            p.UnequipItem(item, false);
            rig.Inv.RemoveItem(item);
            var drop = ItemDrop.DropItem(item, 1, p.transform.position + Flat(p.transform.forward) * 4f + Vector3.up, Quaternion.identity);
            rig.Track(drop.gameObject);
            var view = drop.GetComponent<ZNetView>();
            var id = view != null && view.GetZDO() != null ? view.GetZDO().m_uid : ZDOID.None;
            var reply = new SelfTest.ServerReply();
            for (var attempt = 0; attempt < 6 && !(reply.Answered && reply.Ok); attempt++)
            {
                yield return new WaitForSecondsRealtime(0.7f);
                yield return SelfTest.CallServer(SrvItemStep,
                    id.UserID.ToString(CultureInfo.InvariantCulture) + ":" + id.ID.ToString(CultureInfo.InvariantCulture), reply);
            }
            c.Check(reply.Answered && reply.Ok, $"the server could not read the dropped trinket: {reply}");
            c.Check(Field(reply.Detail, "prefab") == prefabName.GetStableHashCode().ToString(CultureInfo.InvariantCulture)
                    && Number(reply.Detail, "custom") == 0 && Number(reply.Detail, "stack") == 1,
                $"the server holds the dropped trinket as '{reply.Detail}' (expected the plain {prefabName}, no extra data)");

            // Same player picks fresh one up again: key still works.
            var again = rig.EquipTrinket(prefabName);
            p.m_adrenaline = 0f;
            ConsoleAdrenaline(p, 999f);
            yield return Press();
            c.Check(again != null && FullBar.LastResult == TriggerResult.Fired, $"with the mod, a trinket still fires with the key (answer {FullBar.LastResult})");
            CheckCleanLog(c, mark);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.mp.toggle (L01, L02, M02, M07 with real setting) ----------

    private static IEnumerator RunMpToggle()
    {
        var rig = Rig.Create(MpToggleName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(MpToggleName);
        var mark = LogMark();
        var view = FeatureRegistry.Find(ModInfo.Guid);
        try
        {
            var p = rig.P;
            if (!OnServerRules(c) || !c.Check(view != null && view.Value.Enabled != null && view.Value.IsActive, "the mod is not active"))
            {
                c.Report();
                yield break;
            }
            var feature = view.Value;
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            if (!c.Check(item != null, "could not equip a trinket"))
            {
                c.Report();
                yield break;
            }
            var hash = item.m_shared.m_fullAdrenalineSE.NameHash();
            rig.Effect(hash);
            var max = p.GetMaxAdrenaline();

            // Server lets players without mod in (else it refuses this player one second after switch).
            var reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(SrvSetStep, "allow=true", reply);
            if (!c.Check(reply.Answered && reply.Ok && reply.Detail.Contains("allow=True"), $"server set allow=true: {reply}"))
            {
                c.Report();
                yield break;
            }

            // Bar 20 held for long (drain timer at my floor), then mod unticked.
            p.m_adrenaline = 0f;
            ConsoleAdrenaline(p, 20f);
            var start = p.m_adrenaline;
            p.m_adrenalineDegenTimer = 0f;
            yield return FixedTicks(5);
            c.Check(Near(p.m_adrenaline, start) && p.m_adrenalineDegenTimer >= Income.DegenFloor - 0.1f, $"mod on: bar {F(p.m_adrenaline)}, drain timer {F(p.m_adrenalineDegenTimer)}");
            var offMark = LogMark();
            feature.Enabled.Value = false;
            var t0 = Time.time;
            while (Time.time - t0 < 3f && p.m_adrenaline >= start - 0.0001f)
            {
                yield return null;
            }
            var took = Time.time - t0;
            c.Check(feature.State == nameof(ModState.Disabled) && feature.Status == "Off (disabled in settings).",
                $"mod unticked: state {feature.State}, status '{feature.Status}'");
            c.Check(p.m_adrenaline < start && took >= 0.5f && took <= 1.6f, $"mod unticked: the bar started draining after {F(took)} s (expected about 1 s)");
            c.Check(LogCount(offMark, "Told the server that " + ModInfo.Name + " is now off on this game.") == 1, "the 'Told the server ... is now off' line was not logged once");

            // Off: no key, no tooltip line, game's full-bar rules.
            var bar = p.m_adrenaline;
            rig.MarkCenter();
            yield return Press();
            c.Check(FullBar.LastResult == TriggerResult.None && !rig.Has(hash) && p.m_adrenaline <= bar && Rig.Center() == Rig.NoMessage,
                $"mod off: the key did something (answer {FullBar.LastResult}, message '{Rig.Center()}')");
            c.Check(!TipOf(item).Contains(TooltipMark), "mod off: the tooltip still has the trigger line");
            p.m_adrenaline = max; // bar mod left full
            p.m_adrenalineDegenTimer = 2f;
            var fired = false;
            t0 = Time.time;
            while (Time.time - t0 < 5f && p.m_adrenaline >= max - 1f)
            {
                fired |= rig.Has(hash);
                yield return null;
            }
            took = Time.time - t0;
            c.Check(p.m_adrenaline < max && p.m_adrenaline > 0.5f * max && !fired && !rig.Has(hash) && took >= 1.5f && took <= 3.5f,
                $"mod off, full bar left alone: after {F(took)} s the bar is {F(p.m_adrenaline)} of {F(max)}, fired {fired || rig.Has(hash)} (it should drain without firing)");
            p.m_adrenaline = max;
            p.AddAdrenaline(1f); // pay of hit
            c.Check(Near(p.m_adrenaline, 0f) && rig.Has(hash), $"mod off, full bar + a hit: bar {F(p.m_adrenaline)}, effect {rig.Has(hash)} (the game fires at once)");
            rig.Effect(hash);
            CombatState.MarkExchange();
            p.m_adrenaline = 5f;
            p.m_adrenalineDegenTimer = 100f;
            yield return new WaitForSeconds(2.3f);
            c.Check(p.m_adrenaline <= 5f + 0.0001f, $"mod off: income in a fight (bar 5 -> {F(p.m_adrenaline)})");

            // Ticked again.
            var onMark = LogMark();
            feature.Enabled.Value = true;
            yield return Until(() => feature.IsActive && ServerRules.UsingServer, 6f);
            c.Check(feature.IsActive && feature.Status == "Active." && ServerRules.UsingServer, $"mod ticked again: state {feature.State}, status '{feature.Status}', server rules {ServerRules.UsingServer}");
            c.Check(LogCount(onMark, "Told the server that " + ModInfo.Name + " is now on on this game.") == 1, "the 'Told the server ... is now on' line was not logged once");
            c.Check(TipOf(item).Contains(TooltipMark), "mod on again: the tooltip line is not back");
            p.m_adrenaline = 20f;
            p.m_adrenalineDegenTimer = 0f;
            yield return new WaitForSeconds(2f);
            c.Check(Near(p.m_adrenaline, 20f), $"mod on again: the bar went from 20 to {F(p.m_adrenaline)} in 2 s");
            p.m_adrenaline = max;
            p.AddAdrenaline(1f);
            c.Check(Near(p.m_adrenaline, max) && !rig.Has(hash), "mod on again: the full bar does not wait");
            yield return Press();
            c.Check(FullBar.LastResult == TriggerResult.Fired && rig.Has(hash), $"mod on again: the key answered {FullBar.LastResult}");

            // Server saw player with mod turned off and let it in; then it requires mod again.
            yield return new WaitForSecondsRealtime(1.5f);
            yield return SelfTest.CallServer(SrvLogStep, "", reply);
            c.Check(reply.Answered && reply.Ok && Number(reply.Detail, "letinoff") >= 1 && Number(reply.Detail, "refused") == 0,
                $"server log: '{reply.Detail}' (expected its Warning '<player> has the mod turned off; AllowPlayersWithoutMod is on...')");
            yield return SelfTest.CallServer(SrvSetStep, "allow=false", reply);
            c.Check(reply.Answered && reply.Ok && reply.Detail.Contains("allow=False"), $"server set allow=false: {reply}");
            yield return new WaitForSecondsRealtime(6.5f);
            c.Check(Joined() && feature.IsActive, "the player was refused after the server required the mod again (it has the mod on)");
            CheckCleanLog(c, mark);
        }
        finally
        {
            if (view != null && view.Value.Enabled != null && !view.Value.Enabled.Value)
            {
                view.Value.Enabled.Value = true;
            }
            rig.Restore();
        }
        c.Report();
    }

    // ---------- trinkets.mp.server-log (M02, M09) ----------

    private static IEnumerator RunMpServerLog()
    {
        var c = new Checks(MpServerLogName);
        var reply = new SelfTest.ServerReply();
        yield return SelfTest.CallServer(SrvLogStep, "", reply);
        if (c.Check(reply.Answered && reply.Ok, $"server log step: {reply}"))
        {
            c.Note("server log of " + ModInfo.Name + ": " + reply.Detail);
            c.Check(Number(reply.Detail, "errors") == 0 && Number(reply.Detail, "warnings") == 0,
                $"the server logged errors or unexpected warnings from the mod: {reply.Detail}");
            c.Check(Number(reply.Detail, "allowed") >= 1,
                "the server did not log '<player> has " + ModInfo.Name + " on, with the same network version: allowed.'");
            c.Check(Number(reply.Detail, "refused") == 0, "the server refused a player that has the mod");
        }
        c.Report();
    }

    // ---------- trinkets.mp.quick-toggle (M07 c) ----------

    private static IEnumerator RunMpQuickToggle()
    {
        var c = new Checks(MpQuickToggleName);
        var mark = LogMark();
        var view = FeatureRegistry.Find(ModInfo.Guid);
        try
        {
            if (!OnServerRules(c) || !c.Check(view != null && view.Value.Enabled != null && view.Value.IsActive, "the mod is not active"))
            {
                c.Report();
                yield break;
            }
            var feature = view.Value;
            var reply = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(SrvSetStep, "allow=false", reply);
            if (!c.Check(reply.Answered && reply.Ok && reply.Detail.Contains("allow=False"), $"server set allow=false: {reply}"))
            {
                c.Report();
                yield break;
            }

            // Unticked and ticked again within one second: server's check finds mod on.
            feature.Enabled.Value = false;
            var off = feature.State;
            yield return new WaitForSecondsRealtime(0.25f);
            feature.Enabled.Value = true;
            c.Check(off == nameof(ModState.Disabled), $"unticked: state {off}");
            yield return new WaitForSecondsRealtime(7f);
            c.Check(Joined() && feature.IsActive, $"off and on within a second: the player did not stay (connection {ZNet.GetConnectionStatus()}, state {feature.State})");
            yield return Until(() => ServerRules.UsingServer, 6f);
            c.Check(ServerRules.UsingServer && !ServerRules.Current.IsPending, "after the quick switch the server's rules did not come back");
            c.Check(LogCount(mark, "is now off on this game.") == 1 && LogCount(mark, "is now on on this game.") == 1,
                "the client did not tell the server once 'off' and once 'on'");
            if (Joined())
            {
                yield return SelfTest.CallServer(SrvLogStep, "", reply);
                c.Check(reply.Answered && reply.Ok && Number(reply.Detail, "refused") == 0, $"server log after the quick switch: {reply.Detail}");
            }
        }
        finally
        {
            if (view != null && view.Value.Enabled != null && !view.Value.Enabled.Value)
            {
                view.Value.Enabled.Value = true;
            }
        }
        c.Report();
    }

    // ---------- trinkets.mp.no-server-mod (M04, scenario vanilla-server) ----------

    private static IEnumerator RunMpNoServerMod()
    {
        var rig = Rig.Create(MpNoServerName);
        if (rig == null)
        {
            yield break;
        }
        var c = new Checks(MpNoServerName);
        var mark = LogMark();
        try
        {
            var p = rig.P;
            var view = FeatureRegistry.Find(ModInfo.Guid);
            var plugin = Self();
            c.Check(Joined() && view != null && view.Value.State == nameof(ModState.ServerMissing) && plugin != null && !plugin.IsActive
                    && view.Value.Status.StartsWith("Inactive: the server does not have this mod.", StringComparison.Ordinal),
                $"on a server without the mod: state {(view != null ? view.Value.State : "?")}, status '{(view != null ? view.Value.Status : "?")}'");
            var item = EquipNamedTrinket(c, rig, BronzeTrinket);
            if (!c.Check(item != null, "could not equip a trinket"))
            {
                c.Report();
                yield break;
            }
            var hash = item.m_shared.m_fullAdrenalineSE.NameHash();
            rig.Effect(hash);
            var max = p.GetMaxAdrenaline();

            // Game's bar: fires at once, drains, no income, no key, no tooltip line.
            p.m_adrenaline = 0f;
            ConsoleAdrenaline(p, 999f);
            c.Check(Near(p.m_adrenaline, 0f) && rig.Has(hash), $"a full bar: bar {F(p.m_adrenaline)}, effect {rig.Has(hash)} (the game fires at once)");
            rig.Effect(hash);
            p.m_adrenaline = 20f;
            p.m_adrenalineDegenTimer = 0f;
            yield return new WaitForSeconds(1.5f);
            c.Check(p.m_adrenaline < 20f - 0.3f && max > 20f, $"bar 20 with the drain due: {F(p.m_adrenaline)} after 1.5 s (the game drains it)");
            CombatState.MarkExchange();
            p.m_adrenaline = 5f;
            p.m_adrenalineDegenTimer = 100f;
            yield return new WaitForSeconds(2.3f);
            c.Check(p.m_adrenaline <= 5f + 0.0001f, $"income in a fight: bar 5 -> {F(p.m_adrenaline)}");
            var bar = p.m_adrenaline;
            rig.MarkCenter();
            yield return Press();
            c.Check(FullBar.LastResult == TriggerResult.None && Near(p.m_adrenaline, bar) && !rig.Has(hash) && Rig.Center() == Rig.NoMessage,
                $"the key did something (answer {FullBar.LastResult}, message '{Rig.Center()}')");
            c.Check(!TipOf(item).Contains(TooltipMark), "the tooltip has the trigger line");
            CheckCleanLog(c, mark);
        }
        finally
        {
            rig.Restore();
        }
        c.Report();
    }
}
#endif
