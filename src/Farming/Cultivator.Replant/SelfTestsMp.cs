#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using MC.Shared;
using UnityEngine;

namespace MC.Farming.CultivatorReplantMod;

// Debug build only. Multiplayer self tests (tools/Test-Multiplayer.ps1, scenario modded: one client joined to a real
// dedicated server, both with every MC mod). Client tests, with their server halves (steps):
//   replant.mp.server    M09: server loaded the content without graphics and without a warning; the client digs,
//                        plants, grows and upgrades on it; the server holds the planted and the dropped transplant as
//                        saved objects
//   replant.mp.rules     M01 (and the settings to rules to players path of T14 / T26 / T35): server settings changed
//                        in its config reach the client: log line, Upgrade tab cost, grow time, sap cost in the hover
//   replant.mp.pending   M08: no server rules = no transplants in the menu, vanilla levels, Replant refused, nothing
//                        grows; rules asked and received again = all back
//   replant.mp.owngrow   M11: the client's own grow time setting is never used on a server
//   replant.mp.allowed   M10: server lets players in who cannot play by its rules; the client turns the mod off, stays
//                        connected, still knows transplants, and a Yggdrasil transplant grows on it with no root
// Config files of a multiplayer run are throwaway (the script gives server and client fresh ones): the server step
// writes real settings, so the config -> rules -> push path is the real one. Each client test first asks the server
// for its default rules again, so a test cut short never spoils the next.
internal static partial class SelfTests
{
    private const string MpServerName = "replant.mp.server";
    private const string MpRulesName = "replant.mp.rules";
    private const string MpPendingName = "replant.mp.pending";
    private const string MpOwnGrowName = "replant.mp.owngrow";
    private const string MpAllowedName = "replant.mp.allowed";

    private const string RulesStep = "replant.mp.s.rules";
    private const string StateStep = "replant.mp.s.state";
    private const string ZdoStep = "replant.mp.s.zdo";
    private const string AllowStep = "replant.mp.s.allow";
    private const string ClockStep = "replant.mp.s.clock";

    private const string CustomRulesText = "black metal level Wood:1, eitr level Eitr:15, flametal level FlametalNew:5, bloodgold level Gold:5, "
                                           + "grow time x0.5, root sap cost 10, root range 6 m";

    private static void RegisterMultiplayer()
    {
        SelfTest.RegisterMultiplayer(MpServerName, SelfTest.Modded, RunMpServer);
        SelfTest.RegisterMultiplayer(MpRulesName, SelfTest.Modded, RunMpRules);
        SelfTest.RegisterMultiplayer(MpPendingName, SelfTest.Modded, RunMpPending);
        SelfTest.RegisterMultiplayer(MpOwnGrowName, SelfTest.Modded, RunMpOwnGrow);
        SelfTest.RegisterMultiplayer(MpAllowedName, SelfTest.Modded, RunMpAllowed);
        SelfTest.RegisterServerStep(RulesStep, ServerRulesStep);
        SelfTest.RegisterServerStep(StateStep, ServerStateStep);
        SelfTest.RegisterServerStep(ZdoStep, ServerZdoStep);
        SelfTest.RegisterServerStep(AllowStep, ServerAllowStep);
        SelfTest.RegisterServerStep(ClockStep, ServerClockStep);
    }

    private static void UnregisterMultiplayer()
    {
        SelfTest.UnregisterMultiplayer(MpServerName);
        SelfTest.UnregisterMultiplayer(MpRulesName);
        SelfTest.UnregisterMultiplayer(MpPendingName);
        SelfTest.UnregisterMultiplayer(MpOwnGrowName);
        SelfTest.UnregisterMultiplayer(MpAllowedName);
        SelfTest.UnregisterServerStep(RulesStep);
        SelfTest.UnregisterServerStep(StateStep);
        SelfTest.UnregisterServerStep(ZdoStep);
        SelfTest.UnregisterServerStep(AllowStep);
        SelfTest.UnregisterServerStep(ClockStep);
    }

    // ---------- server halves ----------

    private static void Reset<T>(ConfigEntry<T> entry)
    {
        if (entry != null)
        {
            entry.Value = (T)entry.DefaultValue;
        }
    }

    // "custom": real settings written on the server (its config file is the run's own); "default": all back.
    // Answer = the server's own rules as one line.
    private static IEnumerator ServerRulesStep(string arg, object[] reply)
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer())
        {
            SelfTest.Answer(reply, false, "not a server");
            yield break;
        }
        Reset(Plugin.BlackMetalLevel);
        Reset(Plugin.EitrLevel);
        Reset(Plugin.FlametalLevel);
        Reset(Plugin.BloodgoldLevel);
        Reset(Plugin.GrowTimeMultiplier);
        Reset(Plugin.RootSapCost);
        Reset(Plugin.RootRange);
        if (arg == "custom")
        {
            Plugin.BlackMetalLevel.Value = "Wood:1";
            Plugin.GrowTimeMultiplier.Value = 0.5f;
            Plugin.RootSapCost.Value = 10;
        }
        yield return null;
        SelfTest.Answer(reply, true, CultivatorRules.Own().Describe());
    }

    // What the dedicated server made of the mod.
    private static IEnumerator ServerStateStep(string arg, object[] reply)
    {
        yield return null;
        var items = PlantCatalog.All.Count(k => TransplantContent.ItemPrefab(k) != null && ObjectDB.instance != null && ObjectDB.instance.GetItemPrefab(k.ItemName) != null);
        var saplings = PlantCatalog.All.Count(k => TransplantContent.SaplingPrefab(k) != null && ZNetScene.instance != null && ZNetScene.instance.GetPrefab(k.SaplingHash) != null);
        var painted = 0;
        foreach (var kind in PlantCatalog.All)
        {
            var item = TransplantContent.ItemPrefab(kind);
            var icons = item != null ? item.GetComponent<ItemDrop>().m_itemData.m_shared.m_icons : null;
            if (icons != null && icons.Length > 0 && IconPainter.IsPainted(icons[0]))
            {
                painted++;
            }
        }
        var problems = SelfTestLog.Problems().Where(p => !p.Text.Contains("AllowPlayersWithoutMod") && !p.Text.StartsWith("Refused ", StringComparison.Ordinal)
                                                         && !AskedWarnings.Any(a => p.Text.Contains(a))).ToList();
        var shared = CultivatorTiers.CultivatorDrop != null ? CultivatorTiers.CultivatorDrop.m_itemData.m_shared : null;
        var text = $"dedicated={ZNet.instance != null && ZNet.instance.IsDedicated()};active={Plugin.FeatureActive};items={items};saplings={saplings};"
                   + $"graphics={IconPainter.HasGraphics};painted={painted};max={(shared != null ? shared.m_maxQuality : 0)};tiers={CultivatorTiers.InForce};"
                   + $"logwatch={SelfTestLog.Installed};problems={problems.Count}";
        if (problems.Count > 0)
        {
            text += ";first=" + problems[0].Text.Replace(';', ',').Substring(0, Math.Min(200, problems[0].Text.Length));
        }
        SelfTest.Answer(reply, true, text);
    }

    // arg "hash;x;y;z;radius": objects of that prefab the server holds near the point, and how many are saved ones.
    private static IEnumerator ServerZdoStep(string arg, object[] reply)
    {
        yield return null;
        var parts = (arg ?? "").Split(';');
        if (parts.Length != 5 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hash)
            || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            || !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)
            || !float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var radius) || ZDOMan.instance == null)
        {
            SelfTest.Answer(reply, false, "bad argument: " + arg);
            yield break;
        }
        var at = new Vector3(x, y, z);
        var count = 0;
        var saved = 0;
        foreach (var zdo in ZDOMan.instance.m_objectsByID.Values)
        {
            if (zdo == null || zdo.GetPrefab() != hash)
            {
                continue;
            }
            var d = zdo.GetPosition() - at;
            d.y = 0f;
            if (d.sqrMagnitude > radius * radius)
            {
                continue;
            }
            count++;
            if (zdo.Persistent)
            {
                saved++;
            }
        }
        SelfTest.Answer(reply, true, $"count={count};saved={saved};known={(ZNetScene.instance != null && ZNetScene.instance.GetPrefab(hash) != null)}");
    }

    // "on": players who cannot play by the rules may stay for 80 s (in memory, ends alone). "off": back to refusing,
    // everyone checked again (what switching the setting back does). "peer": how the server sees its first player.
    private static IEnumerator ServerAllowStep(string arg, object[] reply)
    {
        yield return null;
        var net = ZNet.instance;
        if (net == null || !net.IsServer())
        {
            SelfTest.Answer(reply, false, "not a server");
            yield break;
        }
        if (arg == "on")
        {
            Plugin.TestAllowUntil = Time.realtimeSinceStartup + 80f;
        }
        else if (arg == "off")
        {
            Plugin.TestAllowUntil = 0f;
            PlayerCheck.ScheduleAllConnected();
        }
        var peer = net.GetPeers().FirstOrDefault(p => p != null && p.IsReady());
        var allowedOff = SelfTestLog.CountSince(0, "their game does not play by this server's rules");
        var allowedNone = SelfTestLog.CountSince(0, "they lose any transplant items they receive");
        var refused = SelfTestLog.Since(0).Count(l => l.Text.StartsWith("Refused ", StringComparison.Ordinal));
        SelfTest.Answer(reply, true,
            $"allow={Plugin.AllowsPlayersWithoutMod};peer={(peer != null)};compatible={(peer != null && NetworkGate.PeerCompatible(peer))};"
            + $"hasMod={(peer != null && NetworkGate.PeerHasMod(peer))};kicked={(peer != null && ZNet.PeersToDisconnectAfterKick.ContainsKey(peer))};"
            + $"allowedOff={allowedOff};allowedNone={allowedNone};refused={refused}");
    }

    // World clock of the server at least ClockFloor (whole days added, like "skiptime": hour of day stays). Answer =
    // clock before and after.
    private static IEnumerator ServerClockStep(string arg, object[] reply)
    {
        yield return null;
        var net = ZNet.instance;
        if (net == null || !net.IsServer())
        {
            SelfTest.Answer(reply, false, "not a server");
            yield break;
        }
        var before = net.GetTimeSeconds();
        var ok = MakeClockOld();
        SelfTest.Answer(reply, ok, $"clock {before.ToString("0", CultureInfo.InvariantCulture)} s -> {net.GetTimeSeconds().ToString("0", CultureInfo.InvariantCulture)} s");
    }

    // ---------- client helpers ----------

    private static Dictionary<string, string> Fields(string text)
    {
        var map = new Dictionary<string, string>();
        foreach (var part in (text ?? "").Split(';'))
        {
            var eq = part.IndexOf('=');
            if (eq > 0)
            {
                map[part.Substring(0, eq)] = part.Substring(eq + 1);
            }
        }
        return map;
    }

    private static string Field(SelfTest.ServerReply reply, string key)
    {
        return Fields(reply.Detail).TryGetValue(key, out var value) ? value : "";
    }

    private static bool MpReady(string name)
    {
        if (!WorldReady(name))
        {
            return false;
        }
        if (ZNet.instance == null || ZNet.instance.IsServer() || ZNet.instance.GetServerPeer() == null)
        {
            SelfTest.Fail(name, "not a client connected to a server");
            return false;
        }
        return true;
    }

    private static IEnumerator WaitFor(Func<bool> done, float seconds)
    {
        var until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until && !done())
        {
            yield return null;
        }
    }

    // Server back on its default rules and this client using them. False through ok[0] = no server answer.
    private static IEnumerator ServerDefaults(Checks c, bool[] ok)
    {
        ok[0] = false;
        var reply = new SelfTest.ServerReply();
        yield return SelfTest.CallServer(RulesStep, "default", reply);
        var want = CultivatorRules.Default.Describe();
        if (!c.Check(reply.Answered && reply.Ok && reply.Detail == want, $"server on its default rules ({reply})"))
        {
            yield break;
        }
        yield return WaitFor(() => ServerRules.UsingServer && ServerRules.Current.Describe() == want && !TransplantContent.RebuildPending, 8f);
        ok[0] = c.Check(ServerRules.TestRules == null && !ServerRules.TestPending && ServerRules.UsingServer && ServerRules.Current.Describe() == want,
            $"client uses the server's default rules ({ServerRules.Current.Describe()})");
        // World clock old enough for the time jumps of the tests (new server world: 2040 s). Only the server can
        // move it; it tells its players the time every 2 s.
        if (!ClockOldEnough)
        {
            var clock = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(ClockStep, "", clock);
            c.Check(clock.Answered && clock.Ok, $"server made its world clock old enough ({clock})");
            yield return WaitFor(() => ClockOldEnough, 8f);
        }
        ClockOk(c);
    }

    private static string ZdoArg(int hash, Vector3 at, float radius)
    {
        return string.Join(";", new[]
        {
            hash.ToString(CultureInfo.InvariantCulture), at.x.ToString("R", CultureInfo.InvariantCulture), at.y.ToString("R", CultureInfo.InvariantCulture),
            at.z.ToString("R", CultureInfo.InvariantCulture), radius.ToString("R", CultureInfo.InvariantCulture),
        });
    }

    // ---------- replant.mp.server (M09) ----------

    private static IEnumerator RunMpServer()
    {
        if (!MpReady(MpServerName))
        {
            yield break;
        }
        var c = new Checks(MpServerName);
        var player = Player.m_localPlayer;
        var recipe = CultivatorTiers.CultivatorRecipe;
        var rig = new Rig(player, MpServerName);
        var taken = new List<Vector3>();
        try
        {
            PrepareInput(rig, false);
            var ok = new bool[1];
            yield return ServerDefaults(c, ok);
            yield return SlowLoopRuns(rig, c);
            var state = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(StateStep, "", state);
            if (!c.Check(ok[0] && state.Answered && state.Ok, $"server answered ({state})"))
            {
                c.Report();
                yield break;
            }
            c.Note("server: " + state.Detail);
            var n = PlantCatalog.All.Count.ToString(CultureInfo.InvariantCulture);
            c.Check(Field(state, "dedicated") == "True" && Field(state, "active") == "True", "dedicated server with the mod active");
            c.Check(Field(state, "items") == n && Field(state, "saplings") == n, $"server made and registered all {n} transplant items and young plants");
            c.Check(Field(state, "graphics") == "False" && Field(state, "painted") == "0", "no graphics device on the server: plain produce icons there");
            c.Check(Field(state, "max") == "7" && Field(state, "tiers") == "True", "cultivator levels 4 to 7 in force on the server");
            c.Check(Field(state, "logwatch") == "True" && Field(state, "problems") == "0", $"no warning or error of this mod in the server log ({Field(state, "problems")}: {Field(state, "first")})");

            // The player digs, plants, grows and upgrades on it.
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var name = rasp.ItemDisplayName;
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 1);
            var plantSpot = Vector3.zero;
            var wildSpot = Vector3.zero;
            if (!c.Check(tool != null && FindPlantSpot(rig, taken, 1.2f, out wildSpot)
                         && FindPlantSpot(rig, taken, 1.4f, out plantSpot, p => Buildable(p) && Untilled(p)), "cultivator and two free spots"))
            {
                c.Report();
                yield break;
            }
            yield return Hold(rig, tool);
            yield return DigCheck(rig, c, tool, rasp, "RaspberryBush", wildSpot, name, -1);
            rig.Give(rasp.ItemName, 1);
            var placing = new Placing();
            yield return AimGhost(rig, SaplingPiece(rasp), plantSpot, placing);
            c.Check(placing.Status == Player.PlacementStatus.Valid, $"ghost can be placed on the server's world (status {placing.Status})");
            var before = CountByName(rig.Inv, name);
            yield return PressPlace(rig, rasp.SaplingHash, placing);
            if (c.Check(placing.Placed && CountByName(rig.Inv, name) == before - 1, $"transplant planted (status {placing.Status})"))
            {
                var at = placing.Go.transform.position;
                yield return new WaitForSeconds(1.5f);
                var seen = new SelfTest.ServerReply();
                yield return SelfTest.CallServer(ZdoStep, ZdoArg(rasp.SaplingHash, at, 2f), seen);
                c.Check(seen.Ok && Field(seen, "count") == "1" && Field(seen, "saved") == "1" && Field(seen, "known") == "True",
                    $"the server holds the young transplant as a saved object of a prefab it knows ({seen})");
                Age(placing.View, 20000.0);
                var grown = new Grown();
                yield return WaitGrown(rig, rasp, placing.View, at, 20f, grown);
                c.Check(grown.Ok && Utils.GetPrefabName(grown.Go) == "RaspberryBush", $"it grows on the server's world ({F(grown.Waited)} s)");
            }
            // Dropped transplant.
            var carried = rig.Inv.GetAllItems().FirstOrDefault(i => i.m_shared.m_name == name);
            if (c.Check(carried != null, "a transplant to drop"))
            {
                var known = new HashSet<ItemDrop>(ItemDrop.s_instances);
                player.DropItem(rig.Inv, carried, 1);
                yield return new WaitForSeconds(2f);
                var onGround = ItemDrop.s_instances.FirstOrDefault(d => d != null && !known.Contains(d) && d.m_itemData != null && d.m_itemData.m_shared.m_name == name);
                if (c.Check(onGround != null, "transplant dropped on the ground"))
                {
                    var seen = new SelfTest.ServerReply();
                    yield return SelfTest.CallServer(ZdoStep, ZdoArg(rasp.ItemHash, onGround.transform.position, 3f), seen);
                    c.Check(seen.Ok && Field(seen, "count") == "1" && Field(seen, "saved") == "1" && Field(seen, "known") == "True",
                        $"the server holds the dropped transplant as a saved object ({seen})");
                }
            }
            // Upgrade 3 -> 4 at a Forge on the server's world.
            var forge = new ForgeRig();
            if (c.Check(recipe != null && SpawnForge(rig, taken, forge), "Forge spawned"))
            {
                yield return ForgeLevel(rig, forge, 4, c);
                KnowRecipe(rig, recipe);
                var level3 = rig.Give(PlantCatalog.CultivatorPrefab, 1, 3);
                rig.Give("BlackMetal", 5);
                rig.Give("LinenThread", 10);
                yield return OpenStation(rig, forge.Station, true);
                var made = new Crafted();
                yield return CraftStep(rig, c, recipe, level3, 4, WantRows("BlackMetal", 5, "LinenThread", 10), made);
                c.Check(made.Item != null && made.Item.m_quality == 4, "cultivator upgraded to level 4 on the server");
                yield return CloseStation(rig);
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- replant.mp.rules (M01) ----------

    private static IEnumerator RunMpRules()
    {
        if (!MpReady(MpRulesName))
        {
            yield break;
        }
        var c = new Checks(MpRulesName);
        var player = Player.m_localPlayer;
        var gui = InventoryGui.instance;
        var recipe = CultivatorTiers.CultivatorRecipe;
        var rig = new Rig(player, MpRulesName);
        var taken = new List<Vector3>();
        var changed = false;
        try
        {
            PrepareInput(rig, false);
            var ok = new bool[1];
            yield return ServerDefaults(c, ok);
            yield return SlowLoopRuns(rig, c);
            if (!ok[0] || !c.Check(recipe != null && SelfTestLog.Installed, "cultivator recipe captured, log watch on"))
            {
                c.Report();
                yield break;
            }
            // The server's own settings change (its config entries): it sends them half a second later.
            var mark = SelfTestLog.Mark;
            var reply = new SelfTest.ServerReply();
            changed = true;
            yield return SelfTest.CallServer(RulesStep, "custom", reply);
            c.Check(reply.Answered && reply.Ok && reply.Detail == CustomRulesText, $"server settings BlackMetalLevel Wood:1, GrowTimeMultiplier 0.5, RootSapCost 10 make its rules '{reply.Detail}'");
            yield return WaitFor(() => ServerRules.Current.Describe() == CustomRulesText, 8f);
            c.Check(ServerRules.UsingServer && ServerRules.Current.Describe() == CustomRulesText, $"the client uses them: '{ServerRules.Current.Describe()}'");
            var line = "Using the server's rules: " + CustomRulesText + ".";
            c.Check(SelfTestLog.CountSince(mark, line) == 1, $"client log says '{line}' ({SelfTestLog.CountSince(mark, line)} time(s))");
            c.Check(Near(CultivatorRules.Own().GrowTimeMultiplier, Plugin.GrowTimeMultiplier.Value) && Plugin.BlackMetalLevel.Value == CultivatorRules.DefaultBlackMetalLevel,
                "the client's own settings are untouched");
            yield return WaitFor(() => !TransplantContent.RebuildPending, 3f);
            yield return Frames(2);

            // Upgrade tab asks 1 Wood for level 4.
            var wood = Row(recipe, "Wood");
            c.Check(wood != null && wood.GetAmount(4) == 1 && Row(recipe, "BlackMetal") == null, "cultivator recipe: 1 Wood for level 4, no black metal");
            var forge = new ForgeRig();
            if (c.Check(SpawnForge(rig, taken, forge), "Forge spawned"))
            {
                yield return ForgeLevel(rig, forge, 4, c);
                KnowRecipe(rig, recipe);
                var level3 = rig.Give(PlantCatalog.CultivatorPrefab, 1, 3);
                yield return OpenStation(rig, forge.Station, true);
                var row = RecipeRow(recipe, level3);
                if (c.Check(row >= 0, "level 3 cultivator listed for upgrade"))
                {
                    gui.SetRecipe(row, false);
                    yield return Frames(3);
                    c.Check(SameRows(RequirementRows(), WantRows("Wood", 1)), $"Upgrade tab asks '{RequirementRows()}' for level 4 (want 1 Wood)");
                }
                yield return CloseStation(rig);
            }

            // Grow time x0.5 on a young transplant, sap cost 10 in the Yggdrasil hover.
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var ygg = PlantCatalog.ByKey("YggaShoot");
            if (c.Check(FindPlantSpot(rig, taken, 1.4f, out var spot), "free spot"))
            {
                var sapling = rig.Spawn(rasp.SaplingName, spot, Quaternion.identity);
                yield return Settle();
                TransplantContent.GrowTimes(rasp, 0.5f, out var lo, out _);
                c.Check(sapling != null && Near(sapling.GetComponent<Plant>().m_growTime, lo, 0.5f), "a young transplant here has the server's grow time x0.5");
            }
            if (SpawnRoot(rig, c, taken, out _, out var root, out var rootSpot))
            {
                yield return Settle();
                var spots = new List<Vector3>();
                if (c.Check(SpotsNearRoot(rootSpot, rig.Origin, root, 6f, ygg.GrowRadius + 0.3f, 1, spots), "spot in reach of the root"))
                {
                    var shoot = rig.Spawn(ygg.SaplingName, spots[0], Quaternion.identity);
                    yield return Settle();
                    var hover = shoot != null ? shoot.GetComponent<Plant>().GetHoverText() : "";
                    c.Check(hover.Contains("Draws 10 sap from the Ancient Root when grown"), $"Yggdrasil transplant hover: '{OneLine(hover)}'");
                }
            }

            // Server back to defaults: the client follows.
            var back = new bool[1];
            yield return ServerDefaults(c, back);
            changed = !back[0];
            c.Report();
        }
        finally
        {
            if (changed)
            {
                SelfTest.Note(MpRulesName, "server may still be on the test rules; the next test asks for its defaults again");
            }
            rig.Restore();
        }
    }

    // ---------- replant.mp.pending (M08) ----------

    private static IEnumerator RunMpPending()
    {
        if (!MpReady(MpPendingName))
        {
            yield break;
        }
        var c = new Checks(MpPendingName);
        var player = Player.m_localPlayer;
        var recipe = CultivatorTiers.CultivatorRecipe;
        var rig = new Rig(player, MpPendingName);
        var taken = new List<Vector3>();
        var asked = true;
        try
        {
            PrepareInput(rig, false);
            var ok = new bool[1];
            yield return ServerDefaults(c, ok);
            yield return SlowLoopRuns(rig, c);
            var shared = CultivatorTiers.CultivatorDrop != null ? CultivatorTiers.CultivatorDrop.m_itemData.m_shared : null;
            if (!ok[0] || !c.Check(recipe != null && shared != null && ServerRules.UsingServer, "joined: this client asked for and got the server's rules"))
            {
                c.Report();
                yield break;
            }
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var tool = rig.Give(PlantCatalog.CultivatorPrefab, 1, 1);
            rig.Give(rasp.ItemName, 1);
            var lateSpot = Vector3.zero;
            var sapSpot = Vector3.zero;
            var bushSpot = Vector3.zero;
            if (!c.Check(tool != null && FindPlantSpot(rig, taken, 1.2f, out bushSpot) && FindPlantSpot(rig, taken, 1.4f, out sapSpot)
                         && FindPlantSpot(rig, taken, 1.4f, out lateSpot), "cultivator and three free spots"))
            {
                c.Report();
                yield break;
            }
            yield return Hold(rig, tool);
            var bush = rig.Spawn("RaspberryBush", bushSpot, Quaternion.identity);
            var sapling = rig.Spawn(rasp.SaplingName, sapSpot, Quaternion.identity);
            yield return Settle();
            var bushView = bush.GetComponent<ZNetView>();
            var sapView = sapling.GetComponent<ZNetView>();
            var spawnedAt = Time.time;
            var withRules = SaplingPiecesOffered(player);
            var rowsWithRules = recipe.m_resources.Length;
            c.Check(withRules >= 1 && CultivatorTiers.InForce && shared.m_maxQuality == 7, "with the server's rules: transplants in the menu, 7 levels");

            // The state right after connecting: no rules of this server yet.
            asked = false;
            ServerRules.Forget();
            yield return Frames(2);
            c.Check(ServerRules.IsPending && !ServerRules.UsingServer, "no server rules: waiting");
            c.Check(SaplingPiecesOffered(player) == 0 && PlantCatalog.All.All(k => !SaplingPiece(k).m_enabled), "waiting: no transplant in the build menu");
            c.Check(!CultivatorTiers.InForce && shared.m_maxQuality == 3 && recipe.m_resources.Length < rowsWithRules, "waiting: the cultivator stays at vanilla levels and costs");
            c.Check(!Replant.TryReplant(player, tool, bushView, rasp, false, out var why) && why == "waiting for the server's rules" && Alive(bushView),
                $"waiting: Replant does nothing ({why})");
            var late = rig.Spawn(rasp.SaplingName, lateSpot, Quaternion.identity);
            yield return Settle();
            Age(sapView, 20000.0);
            while (Time.time - spawnedAt < 12.5f)
            {
                yield return new WaitForSeconds(0.25f);
            }
            c.Check(Alive(sapView) && ServerRules.IsPending, "waiting: a young transplant long past its time does not grow");

            // Rules asked again like the join does: everything works at once.
            ServerRules.Request(ZNet.instance.GetServerPeer().m_rpc);
            asked = true;
            yield return WaitFor(() => ServerRules.UsingServer, 8f);
            c.Check(ServerRules.UsingServer && !ServerRules.IsPending, "rules asked for: the server answered");
            c.Check(!TransplantContent.RebuildPending && SaplingPiecesOffered(player) == withRules && CultivatorTiers.InForce && shared.m_maxQuality == 7
                    && recipe.m_resources.Length == rowsWithRules, "at once: transplants in the menu, 7 levels and their costs");
            TransplantContent.GrowTimes(rasp, ServerRules.Current.GrowTimeMultiplier, out var lo, out _);
            c.Check(late != null && Near(late.GetComponent<Plant>().m_growTime, lo, 0.5f), "a young transplant loaded while waiting has the server's grow time");
            var grown = new Grown();
            yield return WaitGrown(rig, rasp, sapView, sapSpot, 8f, grown);
            c.Check(grown.Ok, $"the young transplant grows now ({F(grown.Waited)} s)");
            c.Check(Replant.TryReplant(player, tool, bushView, rasp, false, out why) && !Alive(bushView), $"Replant works again ({why})");
            c.Report();
        }
        finally
        {
            if (!asked && ZNet.instance != null && ZNet.instance.GetServerPeer() != null && ZNet.instance.GetServerPeer().m_rpc != null)
            {
                // Cut short while waiting: ask for the rules again so the next test has them.
                ServerRules.Request(ZNet.instance.GetServerPeer().m_rpc);
            }
            rig.Restore();
        }
    }

    // ---------- replant.mp.owngrow (M11) ----------

    private static IEnumerator RunMpOwnGrow()
    {
        if (!MpReady(MpOwnGrowName))
        {
            yield break;
        }
        var c = new Checks(MpOwnGrowName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, MpOwnGrowName);
        var taken = new List<Vector3>();
        var asked = true;
        try
        {
            PrepareInput(rig, false);
            var ok = new bool[1];
            yield return ServerDefaults(c, ok);
            yield return SlowLoopRuns(rig, c);
            var joinSpot = Vector3.zero;
            if (!ok[0] || !c.Check(FindPlantSpot(rig, taken, 1.4f, out var spot) && FindPlantSpot(rig, taken, 1.4f, out joinSpot), "two free spots"))
            {
                c.Report();
                yield break;
            }
            // This player's own setting (this run's own config file): 10 times faster.
            Plugin.GrowTimeMultiplier.Value = 0.1f;
            yield return new WaitForSecondsRealtime(1f);
            c.Check(Near(CultivatorRules.Own().GrowTimeMultiplier, 0.1f), "own setting GrowTimeMultiplier = 0.1");
            c.Check(ServerRules.UsingServer && Near(ServerRules.Current.GrowTimeMultiplier, 1f) && ServerRules.Current.Describe().Contains("grow time x1,"),
                $"rules in force are still the server's: '{ServerRules.Current.Describe()}'");
            var lines = SelfTestLog.Since(0).Where(l => l.Text.StartsWith("Using the server's rules: ", StringComparison.Ordinal)).ToList();
            c.Check(lines.Count >= 1 && lines[lines.Count - 1].Text.Contains("grow time x1,"), $"log says so ({(lines.Count > 0 ? lines[lines.Count - 1].Text : "no such line")})");
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            TransplantContent.GrowTimes(rasp, 1f, out var lo, out _);
            TransplantContent.GrowTimes(rasp, 0.1f, out var lo01, out _);

            // The join itself. Before the server's rules arrive the young-plant prefab carries this player's own grow
            // time (written in the main menu), so a transplant that loads in that moment has it. Same state made
            // here: own rules written on the prefab, then the server's rules forgotten (waiting, as right after
            // connecting). 1 h old = due on the own x0.1 (30 min), not on the server's x1 (5 h).
            ServerRules.TestRules = CultivatorRules.Own();
            TransplantContent.Rebuild();
            asked = false;
            ServerRules.Forget();
            ServerRules.TestRules = null;
            yield return Frames(2);
            var joined = rig.Spawn(rasp.SaplingName, joinSpot, Quaternion.identity);
            yield return Settle();
            var joinedView = joined.GetComponent<ZNetView>();
            var joinedPlant = joined.GetComponent<Plant>();
            var joinedAt = Time.time;
            c.Check(ServerRules.IsPending && Near(joinedPlant.m_growTime, lo01, 0.5f),
                $"as at a join: waiting for the server's rules, a transplant loading now carries the own grow time ({F(joinedPlant.m_growTime)} s, x0.1 = {F(lo01)} s)");
            Age(joinedView, 3600.0);
            while (Time.time - joinedAt < 12.5f)
            {
                yield return new WaitForSeconds(0.25f);
            }
            c.Check(Alive(joinedView) && ServerRules.IsPending, "1 h old, due on the own grow time: it does not grow while the server's rules are not here");
            ServerRules.Request(ZNet.instance.GetServerPeer().m_rpc);
            asked = true;
            yield return WaitFor(() => ServerRules.UsingServer, 8f);
            yield return Frames(2);
            c.Check(ServerRules.UsingServer && Alive(joinedView) && Near(joinedPlant.m_growTime, lo, 0.5f),
                $"the server's rules arrived: that transplant now has the server's grow time ({(Alive(joinedView) ? F(joinedPlant.m_growTime) : "gone")} s, x1 = {F(lo)} s)");

            // A transplant planted with the server's rules in force.
            var sapling = rig.Spawn(rasp.SaplingName, spot, Quaternion.identity);
            yield return Settle();
            var view = sapling.GetComponent<ZNetView>();
            var spawnedAt = Time.time;
            c.Check(Near(sapling.GetComponent<Plant>().m_growTime, lo, 0.5f), $"a young transplant has the server's grow time ({F(sapling.GetComponent<Plant>().m_growTime)} s)");
            Age(view, 3600.0);
            while (Time.time - spawnedAt < 16f)
            {
                yield return new WaitForSeconds(0.25f);
            }
            c.Check(Alive(view) && Alive(joinedView), "1 h after planting both stay young (own x0.1 would have grown them)");
            Age(view, 20000.0);
            Age(joinedView, 20000.0);
            var grown = new Grown();
            yield return WaitGrown(rig, rasp, view, spot, 8f, grown);
            c.Check(grown.Ok, "20000 s after planting it grows");
            var grownJoined = new Grown();
            yield return WaitGrown(rig, rasp, joinedView, joinSpot, 4f, grownJoined);
            c.Check(grownJoined.Ok, "and so does the one that loaded while waiting");
            c.Report();
        }
        finally
        {
            if (Plugin.GrowTimeMultiplier != null)
            {
                Plugin.GrowTimeMultiplier.Value = (float)Plugin.GrowTimeMultiplier.DefaultValue;
            }
            if (!asked && ZNet.instance != null && ZNet.instance.GetServerPeer() != null && ZNet.instance.GetServerPeer().m_rpc != null)
            {
                // Cut short while waiting: ask for the rules again so the next test has them.
                ServerRules.Request(ZNet.instance.GetServerPeer().m_rpc);
            }
            rig.Restore();
        }
    }

    // ---------- replant.mp.allowed (M10) ----------

    private static IEnumerator RunMpAllowed()
    {
        if (!MpReady(MpAllowedName))
        {
            yield break;
        }
        var c = new Checks(MpAllowedName);
        var player = Player.m_localPlayer;
        var rig = new Rig(player, MpAllowedName);
        var taken = new List<Vector3>();
        var enabled = Plugin.TestEnabledEntry;
        try
        {
            PrepareInput(rig, false);
            var ok = new bool[1];
            yield return ServerDefaults(c, ok);
            yield return SlowLoopRuns(rig, c);
            var rasp = PlantCatalog.ByKey("RaspberryBush");
            var ygg = PlantCatalog.ByKey("YggaShoot");
            var name = rasp.ItemDisplayName;
            var dropSpot = Vector3.zero;
            var yggSpot = Vector3.zero;
            if (!ok[0] || !c.Check(enabled != null && enabled.Value && FindPlantSpot(rig, taken, 2.3f, out yggSpot) && FindPlantSpot(rig, taken, 1f, out dropSpot),
                    "mod on, two free spots"))
            {
                c.Report();
                yield break;
            }
            var before = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(AllowStep, "on", before);
            if (!c.Check(before.Answered && before.Ok && Field(before, "allow") == "True" && Field(before, "compatible") == "True",
                    $"server lets players in who cannot play by its rules; this player is fine so far ({before})"))
            {
                c.Report();
                yield break;
            }
            // World first: a Yggdrasil transplant with no Ancient Root anywhere near, a transplant on the ground.
            var shoot = rig.Spawn(ygg.SaplingName, yggSpot, Quaternion.identity);
            var dropGo = rig.Spawn(rasp.ItemName, dropSpot + Vector3.up * 0.5f, Quaternion.identity);
            yield return Settle();
            var shootView = shoot.GetComponent<ZNetView>();
            var spawnedAt = Time.time;
            c.Check(RootGate.FindRoot(yggSpot, 30f) == null && dropGo != null && dropGo.GetComponent<ItemDrop>() != null, "Yggdrasil transplant with no root near, transplant item on the ground");

            // Mod off on this game while connected.
            enabled.Value = false;
            yield return WaitFor(() => !Plugin.FeatureActive, 3f);
            c.Check(!Plugin.FeatureActive, "mod turned off on this game");
            // Refusal would come after 1 s grace and cut 4 s later.
            yield return new WaitForSecondsRealtime(7f);
            c.Check(ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected && ZNet.instance != null && ZNet.instance.GetServerPeer() != null && Player.m_localPlayer != null,
                $"still connected 7 s later (status {ZNet.GetConnectionStatus()})");
            var seen = new SelfTest.ServerReply();
            yield return SelfTest.CallServer(AllowStep, "peer", seen);
            c.Check(seen.Ok && Field(seen, "compatible") == "False" && Field(seen, "hasMod") == "True" && Field(seen, "kicked") == "False",
                $"server sees a player with the mod turned off and keeps them ({seen})");
            int Count(SelfTest.ServerReply r, string key) => int.TryParse(Field(r, key), out var v) ? v : -1;
            c.Check(Count(seen, "allowedOff") == Count(before, "allowedOff") + 1 && Count(seen, "allowedNone") == Count(before, "allowedNone") && Count(seen, "refused") == Count(before, "refused"),
                "server log warns once that they play without the server's rules (not that they lose transplants), no refusal");

            // a. They see a transplant on the ground, can pick it up and keep it.
            var drop = dropGo != null ? dropGo.GetComponent<ItemDrop>() : null;
            c.Check(drop != null && drop.m_itemData.m_shared.m_name == name && ObjectDB.instance.GetItemPrefab(rasp.ItemName) != null, "mod off: the transplant on the ground is a known item");
            if (drop != null)
            {
                c.Check(player.Pickup(dropGo, false, false) && CountByName(rig.Inv, name) == 1, "mod off: picked up and kept");
            }
            // b. A Yggdrasil transplant in an area this game runs grows with no root and no sap.
            Age(shootView, 8000.0);
            var grown = new Grown();
            yield return WaitGrown(rig, ygg, shootView, yggSpot, Mathf.Max(4f, 16f - (Time.time - spawnedAt)), grown);
            c.Check(grown.Ok, $"mod off: the Yggdrasil transplant grows with no Ancient Root ({F(grown.Waited)} s)");

            // Back on, then the server refuses such players again: this one is fine.
            enabled.Value = true;
            yield return WaitFor(() => Plugin.FeatureActive && ServerRules.UsingServer, 8f);
            c.Check(Plugin.FeatureActive && ServerRules.UsingServer, "mod on again, server rules back");
            var again = new SelfTest.ServerReply();
            for (var i = 0; i < 8; i++)
            {
                yield return SelfTest.CallServer(AllowStep, "peer", again);
                if (Field(again, "compatible") == "True")
                {
                    break;
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            if (c.Check(Field(again, "compatible") == "True", $"server sees the player as fine again ({again})"))
            {
                var off = new SelfTest.ServerReply();
                yield return SelfTest.CallServer(AllowStep, "off", off);
                c.Check(off.Ok && Field(off, "allow") == "False", $"server refuses such players again ({off})");
                yield return new WaitForSecondsRealtime(3f);
                c.Check(ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected && ZNet.instance.GetServerPeer() != null, "and this player, mod on, stays");
            }
            c.Report();
        }
        finally
        {
            if (enabled != null && !enabled.Value)
            {
                enabled.Value = true;
            }
            rig.Restore();
        }
    }
}
#endif
