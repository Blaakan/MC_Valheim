#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using MC.Shared;
using UnityEngine;

namespace MC.Crafting.ForgeIdolUpgradesMod;

// Debug build only. Me = multiplayer self tests (tools/Test-Multiplayer.ps1): one game client joined to a real
// dedicated server. Client = "the friend" of TESTING.md; server = "the host". Server halves = steps the client call.
// Scenario modded (server and client run the mod):
//   forge.mp.dedicated  mod on the dedicated server: loaded, active, no error line, this player judged compatible
//   forge.mp.rules      server settings changed for real (its config, throwaway in this run): client's Forge follow
//                       while it play, its own settings stay untouched, IdolChoice stay its own
//   forge.mp.tiers      server idol tier settings: client's Forge ask for the idol the server's rule say
//   forge.mp.items      what the server hold of a dropped starred idol and of a chest with idols of each level
// Scenario vanilla-server (server without any MC mod: this mod inactive on the client, so me register these two once
// at plugin load and they stay registered while mod off):
//   forge.mp.vanilla-server  status text, vanilla Forge, no IDOLS tab, no stars
//   forge.mp.handoff         game where mod not active = stand-in for a player without it: starred idol without
//                            stars, "[3]" on the ground, Forge refuse it alone, level kept in the item data
// Server steps: forge.mp.settings ("reset" or "Section/Key=value|..."), forge.mp.health, forge.mp.zdo.
internal static partial class SelfTests
{
    private const string MpDedicatedName = "forge.mp.dedicated";
    private const string MpRulesName = "forge.mp.rules";
    private const string MpTiersName = "forge.mp.tiers";
    private const string MpItemsName = "forge.mp.items";
    private const string MpVanillaServerName = "forge.mp.vanilla-server";
    private const string MpHandoffName = "forge.mp.handoff";

    private const string StepSettings = "forge.mp.settings";
    private const string StepHealth = "forge.mp.health";
    private const string StepZdo = "forge.mp.zdo";

    // ---------- server side ----------

    private static bool _serverDirty;
    private static bool _serverGuard;
    private static float _serverResetAt;

    private static ConfigFile OwnConfig =>
        Chainloader.PluginInfos.TryGetValue(ModInfo.Guid, out var info) && info.Instance != null ? info.Instance.Config : null;

    private static ConfigEntryBase Entry(ConfigFile config, string section, string key)
    {
        foreach (var definition in config.Keys)
        {
            if (definition.Section == section && definition.Key == key)
            {
                return config[definition];
            }
        }
        return null;
    }

    // Every rule setting back to its default (General stay: Enabled, AllowPlayersWithoutMod).
    private static void ResetServerSettings(ConfigFile config)
    {
        foreach (var definition in config.Keys.ToList())
        {
            var entry = config[definition];
            if (definition.Section != "General" && !Equals(entry.BoxedValue, entry.DefaultValue))
            {
                entry.BoxedValue = entry.DefaultValue;
            }
        }
        _serverDirty = false;
    }

    // Client test that die half-way cannot call "reset": server put its settings back by itself.
    private static IEnumerator ServerGuard()
    {
        while (_serverDirty && Time.realtimeSinceStartup < _serverResetAt)
        {
            yield return new WaitForSecondsRealtime(1f);
        }
        var config = OwnConfig;
        if (_serverDirty && config != null)
        {
            ResetServerSettings(config);
            SelfTest.Note(StepSettings, "no reset asked in time: server settings put back to the defaults");
        }
        _serverGuard = false;
    }

    // "reset", or "Section/Key=value|Section/Key=value": server owner change settings while players are in.
    // Answer: line 1 = rules now in force on the server, line 2 = an "Unknown item name(s)" warning it logged.
    private static IEnumerator ServerSettingsStep(string arg, object[] reply)
    {
        // Me write real settings here: only in a multiplayer test run (its config files are throwaway).
        if (!SelfTest.IsMultiplayerRun)
        {
            SelfTest.Answer(reply, false, "not a multiplayer test run: settings left alone");
            yield break;
        }
        var config = OwnConfig;
        if (config == null)
        {
            SelfTest.Answer(reply, false, "plugin not found on the server");
            yield break;
        }
        var problems = new List<string>();
        var mark = LogWatch.Mark();
        if (string.IsNullOrEmpty(arg) || arg == "reset")
        {
            ResetServerSettings(config);
        }
        else
        {
            foreach (var part in arg.Split('|'))
            {
                var slash = part.IndexOf('/');
                var equals = part.IndexOf('=');
                var entry = slash > 0 && equals > slash ? Entry(config, part.Substring(0, slash), part.Substring(slash + 1, equals - slash - 1)) : null;
                if (entry == null)
                {
                    problems.Add($"no setting '{part}'");
                    continue;
                }
                entry.SetSerializedValue(part.Substring(equals + 1));
            }
            _serverDirty = true;
            _serverResetAt = Time.realtimeSinceStartup + 110f;
            if (!_serverGuard)
            {
                _serverGuard = true;
                Plugin.TestRun(ServerGuard());
            }
        }
        // New snapshot and idol table now (typo in a list get logged here); push to players go out next frame.
        var rules = ServerRules.Current;
        IdolCatalog.IdolAt(0, true);
        yield return null;
        yield return null;
        var unknown = Logged(mark, LogLevel.Warning, "Unknown item name(s)") ?? "";
        SelfTest.Answer(reply, problems.Count == 0, problems.Count == 0 ? rules.Describe() + "\n" + unknown : string.Join("; ", problems.ToArray()));
    }

    // How the mod do on the server, as "key=value" lines.
    private static IEnumerator ServerHealthStep(string arg, object[] reply)
    {
        yield return null;
        var net = ZNet.instance;
        var lines = LogWatch.Since(0);
        var errors = lines.Where(l => (l.Level & (LogLevel.Error | LogLevel.Fatal)) != 0 && !l.Text.StartsWith(SelfTest.Prefix, StringComparison.Ordinal)).ToList();
        var peers = net != null ? net.GetPeers().Where(p => p != null && p.IsReady()).ToList() : new List<ZNetPeer>();
        var answer = new List<string>
        {
            "server=" + (net != null && net.IsServer()),
            "dedicated=" + (net != null && net.IsDedicated()),
            "active=" + Plugin.TestActive,
            "status=" + Plugin.TestStatus,
            "ready=" + lines.Any(l => l.Text.Contains(Log.ReadyMarker) && l.Text.Contains(ModInfo.Guid)),
            "errors=" + errors.Count,
            "firstError=" + (errors.Count > 0 ? errors[0].Text.Split('\n')[0] : ""),
            "peers=" + peers.Count,
            "compatible=" + (peers.Count > 0 && peers.All(NetworkGate.PeerCompatible)),
            "problem=" + string.Join(", ", peers.Select(p => NetworkGate.PeerProblem(p) ?? "none").ToArray()),
            "allowed=" + lines.Count(l => l.Text.EndsWith($"has {ModInfo.Name}: allowed.", StringComparison.Ordinal)),
            "refused=" + lines.Count(l => l.Text.StartsWith("Refused ", StringComparison.Ordinal)),
            "rules=" + ServerRules.Current.Describe(),
        };
        SelfTest.Answer(reply, true, string.Join("\n", answer.ToArray()));
    }

    // "drop:<user>:<id>" = item lying on the ground, "chest:<user>:<id>" = a chest: what the server's copy hold.
    private static IEnumerator ServerZdoStep(string arg, object[] reply)
    {
        var parts = (arg ?? "").Split(':');
        if (parts.Length != 3 || !long.TryParse(parts[1], out var user) || !uint.TryParse(parts[2], out var id))
        {
            SelfTest.Answer(reply, false, $"bad object id '{arg}'");
            yield break;
        }
        var uid = new ZDOID(user, id);
        ZDO zdo = null;
        var until = Time.realtimeSinceStartup + 10f;
        while ((zdo = ZDOMan.instance.GetZDO(uid)) == null && Time.realtimeSinceStartup < until)
        {
            yield return null;
        }
        if (zdo == null)
        {
            SelfTest.Answer(reply, false, "the server does not know this object");
            yield break;
        }
        if (parts[0] == "drop")
        {
            var data = new ItemDrop.ItemData();
            ItemDrop.LoadFromZDO(data, zdo);
            SelfTest.Answer(reply, true, $"quality={data.m_quality};stack={data.m_stack}");
            yield break;
        }
        var bytes = zdo.GetByteArray(ZDOVars.s_items);
        var scratch = new Inventory("selftest", null, 8, 8);
        if (bytes != null)
        {
            scratch.Load(new ZPackage(bytes));
        }
        SelfTest.Answer(reply, true, "items=" + Listing(scratch));
    }

    // "prefab:quality:stack" of every stack, sorted.
    private static string Listing(Inventory inventory) =>
        string.Join(",", inventory.GetAllItems()
            .Select(i => $"{(i.m_dropPrefab != null ? i.m_dropPrefab.name : i.m_shared.m_name)}:{i.m_quality}:{i.m_stack}")
            .OrderBy(s => s, StringComparer.Ordinal).ToArray());

    // ---------- client side ----------

    private static IEnumerator WaitFor(Func<bool> done, float seconds)
    {
        var until = Time.realtimeSinceStartup + seconds;
        while (!done() && Time.realtimeSinceStartup < until)
        {
            yield return null;
        }
    }

    private static bool Answered(Checks c, SelfTest.ServerReply reply, string what)
    {
        var ok = reply.Answered && reply.Ok;
        c.Check(ok, $"server step '{what}': {reply}");
        return ok;
    }

    private static Dictionary<string, string> KeyValues(string text)
    {
        var map = new Dictionary<string, string>();
        foreach (var line in (text ?? "").Split('\n'))
        {
            var at = line.IndexOf('=');
            if (at > 0)
            {
                map[line.Substring(0, at)] = line.Substring(at + 1);
            }
        }
        return map;
    }

    private static string Get(Dictionary<string, string> map, string key) => map.TryGetValue(key, out var value) ? value : "";

    private static bool Connected => ZNet.instance != null && !ZNet.instance.IsServer() && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected;

    // Server change its settings, then client wait until the rules it use are the server's new ones.
    private static IEnumerator ServerSet(Checks c, string arg, SelfTest.ServerReply reply)
    {
        yield return SelfTest.CallServer(StepSettings, arg, reply);
        if (!Answered(c, reply, StepSettings + " " + arg))
        {
            yield break;
        }
        var want = reply.Detail.Split('\n')[0];
        yield return WaitFor(() => ServerRules.UsingServer && ServerRules.Current.Describe() == want, 10f);
        c.Check(ServerRules.UsingServer && ServerRules.Current.Describe() == want,
            $"after '{arg}' the client's rules are the server's ({ServerRules.Current.Describe()} vs server {want})");
        yield return null;
        yield return null;
    }

    // ---------- forge.mp.dedicated (M06, M04) ----------

    private static IEnumerator RunMpDedicated()
    {
        var c = new Checks(MpDedicatedName);
        var reply = new SelfTest.ServerReply();
        yield return SelfTest.CallServer(StepHealth, "", reply);
        if (!Answered(c, reply, StepHealth))
        {
            c.Report();
            yield break;
        }
        var health = KeyValues(reply.Detail);
        SelfTest.Note(MpDedicatedName, "server: " + reply.Detail.Replace('\n', ';'));
        c.Check(Get(health, "server") == "True" && Get(health, "dedicated") == "True", "the mod runs on a dedicated server");
        c.Check(Get(health, "ready") == "True", "the server log shows the mod loaded");
        c.Check(Get(health, "active") == "True", $"the mod is active on the server ({Get(health, "status")})");
        c.Check(Get(health, "errors") == "0", $"no error line from the mod in the server log ({Get(health, "errors")}, first: {Get(health, "firstError")})");
        c.Check(Get(health, "peers") != "0" && Get(health, "compatible") == "True", $"the server's join check finds this player compatible (problem: {Get(health, "problem")})");
        c.Check(Get(health, "allowed") != "0" && Get(health, "allowed") != "" && Get(health, "refused") == "0",
            $"it logged that a player with the mod is allowed, and refused nobody ({Get(health, "allowed")} allowed, {Get(health, "refused")} refused)");
        c.Check(Connected && Plugin.TestActive && ServerRules.UsingServer, $"this client: connected, mod active, using the server's rules ({Plugin.TestStatus})");
        c.Check(ServerRules.Current.Describe() == Get(health, "rules"), $"client and server use the same rules ({ServerRules.Current.Describe()})");
        var own = LogWatch.Since(0).Where(l => (l.Level & (LogLevel.Error | LogLevel.Fatal)) != 0 && !l.Text.StartsWith(SelfTest.Prefix, StringComparison.Ordinal)).ToList();
        c.Check(own.Count == 0, $"no error line from the mod in this client's log either ({own.Count}, first: {(own.Count > 0 ? own[0].Text.Split('\n')[0] : "")})");
        c.Report();
    }

    // ---------- forge.mp.rules (M05; real setting changes behind T13 and T19) ----------

    private static IEnumerator RunMpRules()
    {
        var rig = new Rig(MpRulesName);
        var c = rig.C;
        var reply = new SelfTest.ServerReply();
        var pick = Plugin.Pick.Value;
        try
        {
            ServerRules.TestRules = null;
            IdolChoice.TestPick = null;
            // Me set this player's own IdolChoice below (real setting): only in a multiplayer run (throwaway config).
            c.Check(SelfTest.IsMultiplayerRun, "this test runs in a multiplayer test run only");
            if (!SelfTest.IsMultiplayerRun)
            {
                c.Report();
                yield break;
            }
            yield return ServerSet(c, "reset", reply);
            if (!reply.Answered || !reply.Ok || !StoneAxeReady(rig))
            {
                c.Report();
                yield break;
            }
            c.Check(Connected && ServerRules.UsingServer, "client of a server with the mod: it uses the rules the server sent");
            c.Check(Plugin.Chance[0].Value == (int)Plugin.Chance[0].DefaultValue && Plugin.Failure.Value == FailureMode.LoseLevels,
                $"the player's own settings are the defaults ({Plugin.Chance[0].Value}%, {Plugin.Failure.Value})");
            var button = L("$inventory_upgraderbutton");
            var axe = rig.Give("AxeStone", 1, 3);
            rig.Give(WoodIdol, 2, 1);
            rig.Give(WoodIdol, 1, 3);
            yield return rig.OpenForge();
            yield return rig.Select(axe);
            if (axe == null || rig.Row < 0)
            {
                c.Check(false, "the Stone axe is not listed in the UPGRADE tab");
                c.Report();
                yield break;
            }

            // Host set ChanceLevel0 = 50 and OnFailure = Destroy.
            var mark = LogWatch.Mark();
            yield return ServerSet(c, "Refinement/ChanceLevel0=50|Refinement/OnFailure=Destroy", reply);
            c.Check(ServerRules.Current.Chance[0] == 50 && ServerRules.Current.Failure == FailureMode.Destroy, "the client got 50 % and Destroy");
            c.Check(Logged(mark, LogLevel.Info, "Using the server's Forge rules: chances 50/55/75/95%, failure destroys the item") != null,
                $"their log says 'Using the server's Forge rules' with the new values: {Tail(mark)}");
            c.Check(Plugin.Chance[0].Value == (int)Plugin.Chance[0].DefaultValue && Plugin.Failure.Value == FailureMode.LoseLevels, "their own settings did not change");

            // Their own IdolChoice (Lowest): plain idol, at the server's 50 %.
            Plugin.Pick.Value = IdolPick.Lowest;
            yield return Frames(3);
            c.Check(rig.CraftText == "50% chance with a plain idol. A failure destroys the item." && rig.ButtonLabel == button + " (50%)",
                $"their panel shows 50% and 'A failure destroys the item.', is '{rig.CraftText}' / '{rig.ButtonLabel}'");

            // Host change the setting while they play: it follow, window open.
            yield return ServerSet(c, "Refinement/ChanceLevel0=42", reply);
            c.Check(InventoryGui.IsVisible() && rig.ButtonLabel == button + " (42%)", $"changed on the host while they play: the open Forge follows, button '{rig.ButtonLabel}'");
            Plugin.Pick.Value = IdolPick.Highest;
            yield return Frames(3);
            c.Check(rig.ButtonLabel == button + " (75%)" && ServerRules.Current.Chance[0] == 42,
                $"their own IdolChoice still applies (Highest: the 2-star idol, 75%), button '{rig.ButtonLabel}'");

            // More settings changed for real on the server (what T13 and T19 change by hand).
            yield return ServerSet(c, "Refinement/ChanceLevel2=60|Upgrade costs/Level1Trophies=2|Refinement/OnFailure=LoseLevels|Refinement/LevelsLost=2", reply);
            c.Check(rig.ButtonLabel == button + " (60%)" && rig.CraftText == "60% chance with a 2-star idol. A failure costs 2 levels.",
                $"ChanceLevel2 = 60 and LevelsLost = 2 on the server: '{rig.ButtonLabel}' / '{rig.CraftText}'");
            c.Check(ServerRules.Current.Trophies[1] == 2, "Level1Trophies = 2 on the server reached the client");
            yield return ServerSet(c, "Tier 3 - Silver idols/CommonTrophies=TrophyWolf, TrophyWolff", reply);
            var warning = reply.Detail.Split('\n').Skip(1).FirstOrDefault() ?? "";
            c.Check(warning.Contains("Unknown item name(s)") && warning.Contains("TrophyWolff"), $"a typo in a trophy list on the server: its log names the ignored name ('{warning}')");

            yield return ServerSet(c, "reset", reply);
            c.Check(ServerRules.Current.Chance[0] == (int)Plugin.Chance[0].DefaultValue && ServerRules.Current.Failure == FailureMode.LoseLevels && Connected,
                "server settings back to the defaults: the client follows, still connected");
            c.Report();
        }
        finally
        {
            if (SelfTest.IsMultiplayerRun && Plugin.Pick.Value != pick)
            {
                Plugin.Pick.Value = pick;
            }
            rig.Dispose();
        }
    }

    // ---------- forge.mp.tiers (M07; real setting changes behind T21) ----------

    private static IEnumerator RunMpTiers()
    {
        var rig = new Rig(MpTiersName);
        var c = rig.C;
        var reply = new SelfTest.ServerReply();
        try
        {
            ServerRules.TestRules = null;
            yield return ServerSet(c, "reset", reply);
            if (!reply.Answered || !reply.Ok || !StoneAxeReady(rig))
            {
                c.Report();
                yield break;
            }
            var session = ZNet.instance;
            var wood = L(NameOf(WoodIdol));
            var axe = rig.Give("AxeStone", 1, 4);
            rig.Give(WoodIdol, 1, 1);
            yield return rig.OpenForge();
            yield return rig.Select(axe);
            if (axe == null || rig.Row < 0)
            {
                c.Check(false, "the Stone axe is not listed in the UPGRADE tab");
                c.Report();
                yield break;
            }
            c.Check(rig.SlotName(0) == wood && Plugin.BaseLevels.Value == (int)Plugin.BaseLevels.DefaultValue,
                $"defaults: a level 4 Stone axe asks for the Wooden idol, is '{rig.SlotName(0)}'");

            var mark = LogWatch.Mark();
            yield return ServerSet(c, "Refinement/LevelsOnOwnIdol=3", reply);
            c.Check(rig.SlotName(0) == L(NameOf(BronzeIdol)) && Plugin.BaseLevels.Value == (int)Plugin.BaseLevels.DefaultValue,
                $"LevelsOnOwnIdol = 3 on the host: their Forge asks for the Bronze idol (own setting still {Plugin.BaseLevels.Value}), is '{rig.SlotName(0)}'");
            c.Check(Logged(mark, LogLevel.Info, "Using the server's Forge rules", "idol one tier higher from level 4, then every 4 level(s)") != null,
                $"their log line says 'idol one tier higher from level 4, then every 4 level(s)': {Tail(mark)}");

            yield return ServerSet(c, "Refinement/LevelsPerIdolTier=1", reply);
            axe.m_quality = 5;
            yield return Frames(3);
            var five = rig.SlotName(0);
            axe.m_quality = 6;
            yield return Frames(3);
            c.Check(five == L(NameOf("Upgrader2Weapon")) && rig.SlotName(0) == L(NameOf(SilverIdol)),
                $"LevelsPerIdolTier = 1 on the host: level 5 asks for Iron, level 6 for Silver ('{five}', '{rig.SlotName(0)}')");
            yield return ServerSet(c, "Refinement/HigherIdolAtHighLevels=false", reply);
            c.Check(rig.SlotName(0) == wood, $"HigherIdolAtHighLevels = false on the host: level 6 asks for the Wooden idol, is '{rig.SlotName(0)}'");

            yield return ServerSet(c, "reset", reply);
            axe.m_quality = 4;
            yield return Frames(3);
            c.Check(rig.SlotName(0) == wood && ServerRules.Current.BaseLevels == 5, $"back to 5 on the host: a level 4 axe asks for the Wooden idol again, is '{rig.SlotName(0)}'");
            c.Check(ReferenceEquals(session, ZNet.instance) && Connected && InventoryGui.IsVisible(), "all of it without rejoining (same connection, Forge window open)");
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.mp.items (M02, M03) ----------

    private static IEnumerator RunMpItems()
    {
        var rig = new Rig(MpItemsName);
        var c = rig.C;
        var reply = new SelfTest.ServerReply();
        try
        {
            var name = NameOf(SilverIdol);
            var plainIcon = PlainIconOf(SilverIdol);

            // 2-star idol on the ground: the copy every other player get come from the server.
            var dropped = rig.Drop(SilverIdol, 3);
            if (dropped == null || dropped.m_nview == null || !dropped.m_nview.IsValid())
            {
                c.Check(false, "could not drop a 2-star idol");
                c.Report();
                yield break;
            }
            var uid = dropped.m_nview.GetZDO().m_uid;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                yield return new WaitForSecondsRealtime(1.5f);
                yield return SelfTest.CallServer(StepZdo, $"drop:{uid.UserID}:{uid.ID}", reply);
                if (reply.Answered && reply.Ok && reply.Detail == "quality=3;stack=1")
                {
                    break;
                }
            }
            c.Check(reply.Answered && reply.Ok && reply.Detail == "quality=3;stack=1", $"the server holds the dropped idol at quality 3 (2 stars): {reply}");
            c.Check(rig.P.Pickup(dropped.gameObject, false, false), "picked up again by a player with the mod");
            var back = rig.Find(SilverIdol, 3);
            c.Check(back != null && IdolLevels.Of(back) == 2 && back.GetIcon() == StarIcons.Get(plainIcon, 2), "it still has 2 stars");

            // Chest with every level: what the server hold is what a relog, or another player, load.
            var chest = rig.SpawnChest();
            yield return new WaitForSeconds(0.5f);
            if (chest == null || chest.GetInventory() == null || chest.m_nview == null || !chest.m_nview.IsValid())
            {
                c.Check(false, "could not place a chest");
                c.Report();
                yield break;
            }
            var box = chest.GetInventory();
            Put(box, SilverIdol, 1, 2, 0, 0);
            Put(box, SilverIdol, 2, 1, 1, 0);
            Put(box, SilverIdol, 3, 1, 2, 0);
            Put(box, SilverIdol, 4, 1, 3, 0);
            var want = "items=" + Listing(box);
            c.Check(want == $"items={SilverIdol}:1:2,{SilverIdol}:2:1,{SilverIdol}:3:1,{SilverIdol}:4:1", $"chest filled with one stack per level ({want})");
            uid = chest.m_nview.GetZDO().m_uid;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                yield return new WaitForSecondsRealtime(1.5f);
                yield return SelfTest.CallServer(StepZdo, $"chest:{uid.UserID}:{uid.ID}", reply);
                if (reply.Answered && reply.Ok && reply.Detail == want)
                {
                    break;
                }
            }
            c.Check(reply.Answered && reply.Ok && reply.Detail == want, $"the server's copy of the chest keeps every idol at its level, in its own stack: {reply}");
            rig.Inv.MoveAll(box);
            c.Check(rig.Has(SilverIdol, 1) == 2 && rig.Has(SilverIdol, 2) == 1 && rig.Has(SilverIdol, 3) == 2 && rig.Has(SilverIdol, 4) == 1 && Stacks(rig.Inv, name, 1) == 1,
                $"taken out: levels kept, the 2-star idols joined their own level only ({rig.Has(SilverIdol, 1)}/{rig.Has(SilverIdol, 2)}/{rig.Has(SilverIdol, 3)}/{rig.Has(SilverIdol, 4)})");
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.mp.vanilla-server (M01) ----------

    private static IEnumerator RunMpVanillaServer()
    {
        var rig = new Rig(MpVanillaServerName);
        var c = rig.C;
        try
        {
            var view = FeatureRegistry.Find(ModInfo.Guid);
            var state = view != null ? view.Value.State : "not registered";
            var status = view != null ? view.Value.Status : "";
            c.Check(Connected, "client of a dedicated server");
            c.Check(state == nameof(ModState.ServerMissing) && status.StartsWith("Inactive: the server does not have this mod", StringComparison.Ordinal),
                $"the mod's status (what the MC Mods panel shows) is 'Inactive: the server does not have this mod...', is {state}: '{status}'");
            c.Check(!OursPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting)) && !OursPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateCraftingPanel))
                    && !OursPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetIcon)), "none of the mod's patches is on");
            if (!StoneAxeReady(rig))
            {
                c.Report();
                yield break;
            }
            var plainIcon = PlainIconOf(WoodIdol);
            var axe = rig.Give("AxeStone", 1, 3);
            rig.Give(WoodIdol, 1, 1);
            var star = rig.Give(WoodIdol, 1, 3);
            yield return rig.OpenForge();
            yield return rig.Select(axe);
            var gui = rig.Gui;
            c.Check(gui.m_tabCraft.transform.parent.Find("MC_IdolsTab") == null, "no IDOLS tab at the Forge of Potential");
            c.Check(gui.m_tabUpgrade.gameObject.activeSelf && !gui.m_tabUpgrade.interactable && !gui.m_tabCraft.gameObject.activeSelf, "the vanilla tabs: UPGRADE only, selected");
            c.Check(rig.Row >= 0 && rig.CraftText == L("$inventory_upgraderwarning") && rig.ButtonLabel == L("$inventory_upgraderbutton"),
                $"the Forge is vanilla: text '{rig.CraftText}', button '{rig.ButtonLabel}'");
            c.Check(rig.SlotIcon(0) == plainIcon && star != null && star.GetIcon() == plainIcon && !L(star.GetTooltip()).Contains("Refinement chance"),
                "no stars and no idol level text");
            var shared = Prefab(WoodIdol).m_itemData.m_shared;
            c.Check(Mathf.Abs(shared.m_upgradeChance - 0.65f) < 0.001f && shared.m_breakChance >= 1f,
                $"vanilla odds in the idol data: {shared.m_upgradeChance * 100f:0}% success, break chance {shared.m_breakChance:0.##}");
            c.Report();
        }
        finally
        {
            rig.Dispose();
        }
    }

    // ---------- forge.mp.handoff (M02) ----------

    // Game where mod not active stand in for the friend without the mod. Me put one difference right for the length
    // of the test: mod raised the idols' quality levels while it was on at the main menu (game without the mod has 1
    // level there).
    private static IEnumerator RunMpHandoff()
    {
        var rig = new Rig(MpHandoffName);
        var c = rig.C;
        var prefab = Prefab(SilverIdol);
        var levels = prefab != null ? prefab.m_itemData.m_shared.m_maxQuality : 0;
        try
        {
            c.Check(Connected && !Plugin.TestActive && prefab != null, $"the mod is not active on this game ({Plugin.TestStatus})");
            var recipe = ObjectDB.instance.m_recipes.FirstOrDefault(r => r != null && r.m_item != null && r.m_enabled && r.m_resources != null
                && r.m_resources.Any(q => q != null && q.m_upgraderResource && q.m_resItem != null && q.m_resItem.gameObject.name == SilverIdol));
            c.Check(recipe != null, "an item whose own idol is the Silver Battle Idol exists in the game data");
            if (prefab == null || recipe == null)
            {
                c.Report();
                yield break;
            }
            prefab.m_itemData.m_shared.m_maxQuality = 1;
            var plainIcon = PlainIconOf(SilverIdol);
            var gui = rig.Gui;
            var star = rig.Give(SilverIdol, 1, 3);
            rig.P.m_knownRecipes.Add(recipe.m_item.m_itemData.m_shared.m_name);
            var item = rig.Give(recipe.m_item.gameObject.name, 1, 3);
            if (star == null || item == null)
            {
                c.Report();
                yield break;
            }
            SelfTest.Note(MpHandoffName, $"item that needs the silver idol: {recipe.m_item.gameObject.name}");

            // What they see.
            yield return rig.OpenInventory();
            var slot = rig.Element(gui.m_playerGrid, star);
            c.Check(star.m_quality == 3 && star.GetIcon() == plainIcon && slot != null && slot.m_icon.sprite == plainIcon && !slot.m_quality.enabled,
                $"a 2-star idol in a game without the mod: no stars, no quality number ({Describe(slot != null ? slot.m_icon.sprite : null)})");
            yield return rig.CloseWindow();
            var dropped = rig.Drop(SilverIdol, 3);
            yield return null;
            var hover = dropped != null ? dropped.GetHoverText() : "";
            c.Check(hover.Contains("[3]") && !hover.Contains("star"), $"dropped, its ground hover reads '[3]': '{hover}'");

            // Their Forge refuse it on its own.
            yield return rig.OpenForge();
            yield return rig.Select(item);
            c.Check(rig.Row >= 0 && !rig.RowCanCraft && !rig.ButtonOn && rig.ButtonTip == L("$msg_missingrequirement"),
                $"their Forge refuses the starred idol on its own: button off, '{rig.ButtonTip}'");
            c.Check(!rig.P.HaveRequirementItems(recipe, false, item.m_quality + 1), "the vanilla requirement check does not count a starred idol");

            // Given back: level travel in the item's own data.
            var carried = new ItemDrop.ItemData();
            if (dropped != null && dropped.m_nview != null && dropped.m_nview.IsValid())
            {
                ItemDrop.LoadFromZDO(carried, dropped.m_nview.GetZDO());
            }
            c.Check(carried.m_quality == 3 && star.m_quality == 3, $"the idol keeps quality 3 (2 stars) in their inventory and on the ground (ground copy: {carried.m_quality})");
            c.Report();
        }
        finally
        {
            if (prefab != null && levels > 0)
            {
                prefab.m_itemData.m_shared.m_maxQuality = levels;
            }
            rig.Dispose();
        }
    }
}
#endif
