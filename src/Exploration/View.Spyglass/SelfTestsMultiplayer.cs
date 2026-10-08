#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.ViewSpyglassMod;

// Debug build only. Me = multiplayer self tests (tools/Test-Multiplayer.ps1, scenario "modded"): this game is client
// of a real dedicated server, both with me. Client tests below; their server halves (steps) run on the server and
// answer what only server know or do. Config files of the run are throwaway on both sides, so server steps and
// spyglass.mp.settings / spyglass.mp.toggle set real settings; every test first put server back to its defaults
// (MpStart), so a test cut short never spoil the next one.
//   spyglass.mp.rules     server settings MaxMagnification 3 + RecipeResources Bronze:1 reach this game: log line,
//                         recipe 1 Bronze (crafted at a Forge with one Bronze), wheel zoom stops at x3 [M01]
//   spyglass.mp.pending   the server keeps its rules back: click = message once per 3 s, recipe hidden; rules sent:
//                         it works [M05]
//   spyglass.mp.pose      the server sees the spyglass in this player's hand and the raised flag go up and down (what
//                         other games pose the arm from) [M02]
//   spyglass.mp.server    the dedicated server loaded the item clean; craft, use, drop: the server holds the dropped
//                         spyglass as a saved object with a known prefab [M06]
//   spyglass.mp.recall    the server recalls this player (the host's "recall") while looking: view back at once [M07]
//   spyglass.mp.settings  the real personal settings change the view live; own MaxMagnification never beats the
//                         server's [T16 T08]
//   spyglass.mp.toggle    the real Enabled switch while looking: everything back at once, Status says off, clicks do
//                         nothing, on again works [T18 T19 T15]
internal static partial class SelfTests
{
    private const string MpRulesTest = "spyglass.mp.rules";
    private const string MpPendingTest = "spyglass.mp.pending";
    private const string MpPoseTest = "spyglass.mp.pose";
    private const string MpServerTest = "spyglass.mp.server";
    private const string MpRecallTest = "spyglass.mp.recall";
    private const string MpSettingsTest = "spyglass.mp.settings";
    private const string MpToggleTest = "spyglass.mp.toggle";

    private const string StepReset = "spyglass.mp.s.reset";
    private const string StepRules = "spyglass.mp.s.rules";
    private const string StepHold = "spyglass.mp.s.hold";
    private const string StepAllow = "spyglass.mp.s.allow";
    private const string StepPlayer = "spyglass.mp.s.player";
    private const string StepState = "spyglass.mp.s.state";
    private const string StepDrop = "spyglass.mp.s.drop";
    private const string StepRecall = "spyglass.mp.s.recall";

    // Run order: toggle last (it make the server log a warning about this player).
    private static readonly KeyValuePair<string, Func<IEnumerator>>[] MpTests =
    {
        new KeyValuePair<string, Func<IEnumerator>>(MpRulesTest, RunMpRules),
        new KeyValuePair<string, Func<IEnumerator>>(MpPendingTest, RunMpPending),
        new KeyValuePair<string, Func<IEnumerator>>(MpPoseTest, RunMpPose),
        new KeyValuePair<string, Func<IEnumerator>>(MpServerTest, RunMpServer),
        new KeyValuePair<string, Func<IEnumerator>>(MpRecallTest, RunMpRecall),
        new KeyValuePair<string, Func<IEnumerator>>(MpSettingsTest, RunMpSettings),
        new KeyValuePair<string, Func<IEnumerator>>(MpToggleTest, RunMpToggle),
    };

    private static readonly KeyValuePair<string, Func<string, object[], IEnumerator>>[] ServerSteps =
    {
        new KeyValuePair<string, Func<string, object[], IEnumerator>>(StepReset, ServerReset),
        new KeyValuePair<string, Func<string, object[], IEnumerator>>(StepRules, ServerSetRules),
        new KeyValuePair<string, Func<string, object[], IEnumerator>>(StepHold, ServerHold),
        new KeyValuePair<string, Func<string, object[], IEnumerator>>(StepAllow, ServerAllow),
        new KeyValuePair<string, Func<string, object[], IEnumerator>>(StepPlayer, ServerPlayer),
        new KeyValuePair<string, Func<string, object[], IEnumerator>>(StepState, ServerState),
        new KeyValuePair<string, Func<string, object[], IEnumerator>>(StepDrop, ServerDrop),
        new KeyValuePair<string, Func<string, object[], IEnumerator>>(StepRecall, ServerRecall),
    };

    private static void RegisterMultiplayer()
    {
        foreach (var test in MpTests)
        {
            SelfTest.RegisterMultiplayer(test.Key, SelfTest.Modded, test.Value);
        }
        foreach (var step in ServerSteps)
        {
            SelfTest.RegisterServerStep(step.Key, step.Value);
        }
    }

    private static void UnregisterMultiplayer()
    {
        foreach (var test in MpTests)
        {
            SelfTest.UnregisterMultiplayer(test.Key);
        }
        foreach (var step in ServerSteps)
        {
            SelfTest.UnregisterServerStep(step.Key);
        }
    }

    private static string N(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    // ---------- server halves ----------

    private static bool OnServer(object[] reply)
    {
        var net = ZNet.instance;
        if (net != null && net.IsServer() && Plugin.FeatureActive)
        {
            return true;
        }
        SelfTest.Answer(reply, false, "not a server with the spyglass active");
        return false;
    }

    // Server settings back to defaults of the mod, nothing held back, players without the mod refused. Answer = rules
    // the server send now.
    private static IEnumerator ServerReset(string arg, object[] reply)
    {
        if (!OnServer(reply))
        {
            yield break;
        }
        var d = SpyglassRules.Default;
        Plugin.RecipeResources.Value = d.RecipeResources;
        Plugin.RecipeStation.Value = d.RecipeStation;
        Plugin.RecipeStationLevel.Value = d.RecipeStationLevel;
        Plugin.MaxMagnification.Value = d.MaxMagnification;
        Plugin.FogClearing.Value = d.FogClearing;
        Plugin.AllowPlayersWithoutMod.Value = false;
        ServerRules.TestHoldAnswers = false;
        yield return null;
        SelfTest.Answer(reply, true, ServerRules.TestOwnDescription);
    }

    // "Key=value;Key=value" with the rule settings of the server's config, like its owner typing them. Null = fine.
    private static string ApplyRules(string arg)
    {
        foreach (var raw in (arg ?? "").Split(';'))
        {
            var eq = raw.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }
            var key = raw.Substring(0, eq).Trim();
            var value = raw.Substring(eq + 1).Trim();
            switch (key)
            {
                case "RecipeResources":
                    Plugin.RecipeResources.Value = value;
                    break;
                case "RecipeStation":
                    Plugin.RecipeStation.Value = value;
                    break;
                case "RecipeStationLevel":
                    Plugin.RecipeStationLevel.Value = int.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "MaxMagnification":
                    Plugin.MaxMagnification.Value = float.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "FogClearing":
                    Plugin.FogClearing.Value = float.Parse(value, CultureInfo.InvariantCulture);
                    break;
                default:
                    return $"unknown setting '{key}'";
            }
        }
        return null;
    }

    private static IEnumerator ServerSetRules(string arg, object[] reply)
    {
        if (!OnServer(reply))
        {
            yield break;
        }
        string problem;
        try
        {
            problem = ApplyRules(arg);
        }
        catch (Exception e)
        {
            problem = e.Message;
        }
        yield return null;
        SelfTest.Answer(reply, problem == null, problem ?? ServerRules.TestOwnDescription);
    }

    private static IEnumerator ServerHold(string arg, object[] reply)
    {
        if (!OnServer(reply))
        {
            yield break;
        }
        ServerRules.TestHoldAnswers = arg == "1";
        yield return null;
        SelfTest.Answer(reply, true, ServerRules.TestHoldAnswers ? "the server keeps its rules to itself" : "the server sends its rules: " + ServerRules.TestOwnDescription);
    }

    private static IEnumerator ServerAllow(string arg, object[] reply)
    {
        if (!OnServer(reply))
        {
            yield break;
        }
        Plugin.AllowPlayersWithoutMod.Value = arg == "1";
        yield return null;
        SelfTest.Answer(reply, true, "AllowPlayersWithoutMod = " + Plugin.AllowPlayersWithoutMod.Value);
    }

    // "<peer id>|<raised 0/1>|<spyglass in right hand 0/1>": what server hold for that player (state every other game
    // get), me wait for it up to 6 s.
    private static IEnumerator ServerPlayer(string arg, object[] reply)
    {
        if (!OnServer(reply))
        {
            yield break;
        }
        var parts = (arg ?? "").Split('|');
        if (parts.Length < 3 || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var uid))
        {
            SelfTest.Answer(reply, false, "bad argument '" + arg + "'");
            yield break;
        }
        var wantRaised = parts[1] == "1";
        var wantHeld = parts[2] == "1";
        var end = Time.realtimeSinceStartup + 6f;
        var ok = false;
        var detail = "";
        while (true)
        {
            var peer = ZNet.instance.GetPeer(uid);
            var zdo = peer != null && !peer.m_characterID.IsNone() && ZDOMan.instance != null ? ZDOMan.instance.GetZDO(peer.m_characterID) : null;
            if (zdo == null)
            {
                detail = peer == null ? "no such player on the server" : "the server has no object for that player";
            }
            else
            {
                var raised = zdo.GetBool(Scope.RaisedKey);
                var held = zdo.GetInt(ZDOVars.s_rightItem) == SpyglassContent.ItemHash;
                ok = raised == wantRaised && held == wantHeld;
                detail = $"{peer.m_playerName}: spyglass in the right hand {held}, raised flag {raised}";
            }
            if (ok || Time.realtimeSinceStartup > end)
            {
                break;
            }
            yield return null;
        }
        SelfTest.Answer(reply, ok, detail);
    }

    // How dedicated server stand: item and recipe registered, nothing complained about.
    private static IEnumerator ServerState(string arg, object[] reply)
    {
        if (!OnServer(reply))
        {
            yield break;
        }
        yield return null;
        var ok = true;
        var facts = new List<string>();
        void Fact(bool good, string what)
        {
            ok &= good;
            facts.Add((good ? "" : "NOT: ") + what);
        }
        var net = ZNet.instance;
        var db = ObjectDB.instance;
        var scene = ZNetScene.instance;
        var prefab = SpyglassContent.ItemPrefab;
        var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
        var recipe = SpyglassContent.CraftRecipe;
        Fact(net.IsDedicated(), "dedicated server");
        Fact(SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null, "no graphics device");
        Fact(SpyglassContent.Built && !SpyglassContent.MissingReported && drop != null, "spyglass item made");
        Fact(db != null && prefab != null && db.GetItemPrefab(SpyglassContent.ItemName) == prefab, "in the item database");
        Fact(scene != null && prefab != null && scene.GetPrefab(SpyglassContent.ItemHash) == prefab, "in the network prefab list");
        Fact(recipe != null && db != null && db.m_recipes.Contains(recipe) && recipe.m_enabled, "recipe registered and on");
        if (drop != null)
        {
            var s = drop.m_itemData.m_shared;
            Fact(s.m_itemType == ItemDrop.ItemData.ItemType.Tool && !s.m_useDurability && string.IsNullOrEmpty(s.m_attack.m_attackAnimation),
                "a tool without attack or durability");
            Fact(prefab.GetComponentInChildren<Collider>(true) != null, "ground copy has its collider");
        }
        var problems = _tap != null ? _tap.Problems() : null;
        if (problems != null)
        {
            // Toggle test make the server say this on purpose.
            problems.RemoveAll(p => p.Contains("AllowPlayersWithoutMod is on"));
        }
        Fact(problems != null && problems.Count == 0, problems == null ? "server log listened to"
            : problems.Count == 0 ? "no warning or error from the spyglass in the server log"
            : $"no warning or error in the server log ({problems.Count}, first: {First(problems, 1)})");
        SelfTest.Answer(reply, ok, string.Join("; ", facts.ToArray()));
    }

    // "<user>:<id>" of object the client dropped: server has it (me wait up to 8 s), as a spyglass, saved with the
    // world, with a prefab the server know.
    private static IEnumerator ServerDrop(string arg, object[] reply)
    {
        if (!OnServer(reply))
        {
            yield break;
        }
        var parts = (arg ?? "").Split(':');
        if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var user)
            || !uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            SelfTest.Answer(reply, false, "bad argument '" + arg + "'");
            yield break;
        }
        var id = new ZDOID(user, number);
        var end = Time.realtimeSinceStartup + 8f;
        ZDO zdo = null;
        while (zdo == null && Time.realtimeSinceStartup < end)
        {
            zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(id) : null;
            if (zdo == null)
            {
                yield return null;
            }
        }
        if (zdo == null)
        {
            SelfTest.Answer(reply, false, "the dropped object never reached the server");
            yield break;
        }
        var isSpyglass = zdo.GetPrefab() == SpyglassContent.ItemHash;
        var known = ZNetScene.instance != null && ZNetScene.instance.GetPrefab(zdo.GetPrefab()) != null;
        var p = zdo.GetPosition();
        SelfTest.Answer(reply, isSpyglass && known && zdo.Persistent,
            $"object at ({F(p.x)}, {F(p.y)}, {F(p.z)}): spyglass {isSpyglass}, prefab known to the server {known}, saved with the world {zdo.Persistent}");
    }

    // "<peer id>|x|y|z": what host's "recall" console command send to a player (Chat.TeleportPlayer).
    private static IEnumerator ServerRecall(string arg, object[] reply)
    {
        if (!OnServer(reply))
        {
            yield break;
        }
        var parts = (arg ?? "").Split('|');
        if (parts.Length != 4 || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var uid)
            || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            || !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)
            || ZRoutedRpc.instance == null || ZNet.instance.GetPeer(uid) == null)
        {
            SelfTest.Answer(reply, false, "bad argument or no such player: '" + arg + "'");
            yield break;
        }
        ZRoutedRpc.instance.InvokeRoutedRPC(uid, "RPC_TeleportPlayer", new Vector3(x, y, z), Quaternion.identity, true);
        yield return null;
        SelfTest.Answer(reply, true, "recall sent");
    }

    // ---------- client helpers ----------

    private static IEnumerator Server(Checks c, string step, string arg, SelfTest.ServerReply reply, string what)
    {
        yield return SelfTest.CallServer(step, arg, reply);
        c.Check(reply.Answered && reply.Ok, $"{what}: {reply}");
    }

    private static string CostText(Recipe recipe)
    {
        var parts = new List<string>();
        if (recipe != null && recipe.m_resources != null)
        {
            foreach (var r in recipe.m_resources)
            {
                parts.Add(r.m_resItem.name + ":" + r.m_amount);
            }
        }
        return string.Join(",", parts.ToArray());
    }

    // Start of every multiplayer test: server back to its default spyglass settings, this game using them (rules
    // received, recipe rebuilt). ok[0] false = nothing to test on.
    private static IEnumerator MpStart(Checks c, bool[] ok)
    {
        ok[0] = false;
        var net = ZNet.instance;
        if (!c.Check(SelfTest.IsMultiplayerRun && net != null && !net.IsServer() && _tap != null, "this game is a client of the test server"))
        {
            yield break;
        }
        var reply = new SelfTest.ServerReply();
        yield return SelfTest.CallServer(StepReset, "", reply);
        if (!c.Check(reply.Answered && reply.Ok, $"server put back to its default spyglass settings: {reply}"))
        {
            yield break;
        }
        yield return WaitReal(() => ServerRules.UsingServer && ServerRules.Current.Describe() == reply.Detail, 10f);
        if (!c.Check(ServerRules.UsingServer && ServerRules.Current.Describe() == reply.Detail,
                $"this game uses the server's rules ({ServerRules.Current.Describe()})"))
        {
            yield break;
        }
        var recipe = SpyglassContent.CraftRecipe;
        yield return WaitReal(() => recipe != null && !SpyglassContent.RebuildPending && recipe.m_enabled && CostText(recipe) == "Bronze:2,Crystal:2", 4f);
        ok[0] = c.Check(recipe != null && recipe.m_enabled && CostText(recipe) == "Bronze:2,Crystal:2",
            $"recipe follows the server's default rules ({CostText(recipe)})");
    }

    // ---------- spyglass.mp.rules ----------

    private static IEnumerator RunMpRules()
    {
        var c = new Checks(MpRulesTest);
        if (!Ready(MpRulesTest, out var player, out var cam))
        {
            yield break;
        }
        var rig = new SpyRig(player, MpRulesTest);
        try
        {
            var ok = new bool[1];
            yield return MpStart(c, ok);
            if (!ok[0])
            {
                c.Report();
                yield break;
            }
            rig.TakeControls();
            rig.Noon();
            var recipe = SpyglassContent.CraftRecipe;
            var inv = player.GetInventory();
            var bronze = SharedName("Bronze");

            // Server owner set MaxMagnification 3 and RecipeResources Bronze:1.
            var mark = _tap.InfoMark;
            var reply = new SelfTest.ServerReply();
            yield return Server(c, StepRules, "MaxMagnification=3;RecipeResources=Bronze:1", reply, "server settings changed");
            yield return WaitReal(() => ServerRules.Current.Describe() == reply.Detail, 10f);
            var now = ServerRules.Current;
            c.Check(ServerRules.UsingServer && now.Describe() == reply.Detail && Near(now.MaxMagnification, 3f) && now.RecipeResources == "Bronze:1",
                $"the server's new rules arrived here ({now.Describe()})");
            const string line = "Using the server's rules: recipe Bronze:1 at forge level 1, zoom up to x3";
            var logged = false;
            foreach (var text in _tap.InfoSince(mark))
            {
                logged |= text.StartsWith(line, StringComparison.Ordinal);
            }
            c.Check(logged, $"log says \"{line}...\"");
            yield return WaitReal(() => !SpyglassContent.RebuildPending && CostText(recipe) == "Bronze:1", 4f);
            c.Check(recipe.m_enabled && CostText(recipe) == "Bronze:1" && recipe.m_amount == 1, $"the recipe costs 1 Bronze now ({CostText(recipe)})");

            // At a Forge: crafted with a single Bronze.
            ItemDrop.ItemData crafted = null;
            if (c.Check(bronze != null, "Bronze exists"))
            {
                KeepKnown(rig);
                KeepCount(rig, bronze);
                var had = new HashSet<ItemDrop.ItemData>();
                foreach (var item in inv.GetAllItems())
                {
                    if (SpyglassContent.IsSpyglass(item))
                    {
                        had.Add(item);
                    }
                }
                var station = SpawnStation(c, rig);
                if (station != null)
                {
                    var bronzeBefore = inv.CountItems(bronze);
                    inv.AddItem("Bronze", 1, 1, 0, 0L, "", false);
                    yield return KnowStation(c, player, station);
                    player.UpdateKnownRecipesList();
                    c.Check(player.IsRecipeKnown(recipe.m_item.m_itemData.m_shared.m_name), "recipe known with Bronze alone");
                    var row = new int[1];
                    yield return OpenStation(player, station, row);
                    if (c.Check(row[0] >= 0, "listed at the Forge"))
                    {
                        InventoryGui.instance.SetRecipe(row[0], false);
                        yield return null;
                        yield return null;
                        var shown = ShownCost();
                        if (shown.Length > 0)
                        {
                            c.Check(shown == $"{Text(bronze)} 1", $"the Forge shows the cost: {shown}");
                        }
                        var count = CountSpyglasses(inv);
                        yield return CraftRow(row[0]);
                        TrackNewSpyglasses(rig, had);
                        c.Check(CountSpyglasses(inv) == count + 1 && inv.CountItems(bronze) == bronzeBefore,
                            $"crafted with one Bronze ({CountSpyglasses(inv) - count} made, {inv.CountItems(bronze) - bronzeBefore} Bronze left of the one given)");
                        foreach (var item in inv.GetAllItems())
                        {
                            if (SpyglassContent.IsSpyglass(item) && !had.Contains(item))
                            {
                                crafted = item;
                            }
                        }
                    }
                    yield return CloseStation(player);
                }
            }

            // Wheel stop at x3.
            var spy = crafted ?? rig.Give(SpyglassContent.ItemName);
            if (c.Check(spy != null, "a spyglass to look through"))
            {
                player.EquipItem(spy, false);
                rig.Look(OpenYaw(player, out _), -1f);
                yield return new WaitForSeconds(0.5f);
                var vanillaFov = cam.m_camera.fieldOfView;
                Scope.TestSetMagnification(2f);
                yield return RaiseUp(c, player, "zoom");
                var steps = new List<string>();
                for (var i = 0; i < 5; i++)
                {
                    yield return Notch(1f);
                    steps.Add(F(Scope.Magnification));
                }
                yield return Frames(8);
                yield return new WaitForEndOfFrame();
                c.Check(Near(Scope.Magnification, 3f, 0.001f) && Near(ScopeCamera.CurrentMagnification, 3f, 0.01f)
                        && Near(cam.m_camera.fieldOfView, ScopeCamera.ZoomedFov(vanillaFov, 3f), 0.05f),
                    $"wheel zoom stops at x3 (steps {string.Join(" ", steps.ToArray())}, view x{F(ScopeCamera.CurrentMagnification)})");
                yield return LowerDown();
            }

            // Server back to its defaults: this game follow again.
            yield return MpStart(c, ok);
            c.Check(ok[0] && Near(ServerRules.Current.MaxMagnification, SpyglassRules.Default.MaxMagnification), "server defaults back: followed again");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- spyglass.mp.pending ----------

    private static IEnumerator RunMpPending()
    {
        var c = new Checks(MpPendingTest);
        if (!Ready(MpPendingTest, out var player, out _))
        {
            yield break;
        }
        var rig = new SpyRig(player, MpPendingTest);
        try
        {
            var ok = new bool[1];
            yield return MpStart(c, ok);
            var spy = ok[0] ? Hold(rig, c) : null;
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            var recipe = SpyglassContent.CraftRecipe;
            // Me wait out the 3 s pause of an earlier message.
            yield return Seconds(3.2f);
            var reply = new SelfTest.ServerReply();
            yield return Server(c, StepHold, "1", reply, "the server keeps its rules back");
            var asked = false;
            yield return PendingClicks(c, player, recipe, () => asked = ServerRules.TestForgetAndAsk());
            c.Check(asked && ServerRules.IsPending && !ServerRules.UsingServer, "asked the server like a fresh connection, no answer yet");
            yield return Server(c, StepHold, "0", reply, "the server sends its rules");
            yield return WaitReal(() => !ServerRules.IsPending, 10f);
            c.Check(ServerRules.UsingServer && !ServerRules.IsPending, "the rules arrived");
            yield return WaitReal(() => recipe.m_enabled, 4f);
            c.Check(recipe.m_enabled, "recipe shown again");
            yield return WaitReal(() => Scope.CanRaise(player), 4f);
            yield return Click(player);
            yield return WaitReal(() => Scope.State == ScopeState.Raised, 3f);
            c.Check(Scope.State == ScopeState.Raised, $"afterwards the click raises it ({Scope.State})");
            yield return LowerDown();
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- spyglass.mp.pose ----------

    private static IEnumerator RunMpPose()
    {
        var c = new Checks(MpPoseTest);
        if (!Ready(MpPoseTest, out var player, out _))
        {
            yield break;
        }
        var rig = new SpyRig(player, MpPoseTest);
        try
        {
            var ok = new bool[1];
            yield return MpStart(c, ok);
            var spy = ok[0] ? Hold(rig, c) : null;
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            var uid = ZNet.GetUID().ToString(CultureInfo.InvariantCulture);
            var reply = new SelfTest.ServerReply();
            yield return new WaitForSeconds(0.5f);
            yield return Server(c, StepPlayer, uid + "|0|1", reply, "spyglass in hand, down: the server");
            yield return WaitReal(() => Scope.CanRaise(player), 4f);
            yield return Click(player);
            yield return WaitReal(() => Scope.State == ScopeState.Raised, 3f);
            c.Check(Scope.State == ScopeState.Raised && Flag(player), "raised by a click, flag set on this game");
            yield return Server(c, StepPlayer, uid + "|1|1", reply, "raised: the server (and so every other game)");
            yield return Click(player);
            yield return WaitReal(() => Scope.State == ScopeState.Idle, 3f);
            c.Check(Scope.State == ScopeState.Idle && !Flag(player), "lowered by a click, flag cleared on this game");
            yield return Server(c, StepPlayer, uid + "|0|1", reply, "lowered: the server");
            // Put away while up: flag down at once.
            yield return RaiseUp(c, player, "put away");
            yield return Server(c, StepPlayer, uid + "|1|1", reply, "raised again: the server");
            player.UnequipItem(spy, false);
            yield return Server(c, StepPlayer, uid + "|0|0", reply, "put away while up: the server");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- spyglass.mp.server ----------

    private static IEnumerator RunMpServer()
    {
        var c = new Checks(MpServerTest);
        if (!Ready(MpServerTest, out var player, out _))
        {
            yield break;
        }
        var rig = new SpyRig(player, MpServerTest);
        var autoPickup = Player.m_enableAutoPickup;
        GameObject dropped = null;
        try
        {
            var ok = new bool[1];
            yield return MpStart(c, ok);
            if (!ok[0])
            {
                c.Report();
                yield break;
            }
            rig.TakeControls();
            rig.Noon();
            var reply = new SelfTest.ServerReply();
            yield return Server(c, StepState, "", reply, "dedicated server");

            // Craft at a Forge with the server's default recipe.
            var recipe = SpyglassContent.CraftRecipe;
            var inv = player.GetInventory();
            var bronze = SharedName("Bronze");
            var crystal = SharedName("Crystal");
            ItemDrop.ItemData crafted = null;
            var had = new HashSet<ItemDrop.ItemData>();
            foreach (var item in inv.GetAllItems())
            {
                if (SpyglassContent.IsSpyglass(item))
                {
                    had.Add(item);
                }
            }
            if (c.Check(bronze != null && crystal != null, "Bronze and Crystal exist"))
            {
                KeepKnown(rig);
                KeepCount(rig, bronze);
                KeepCount(rig, crystal);
                var station = SpawnStation(c, rig);
                if (station != null)
                {
                    var bronzeBefore = inv.CountItems(bronze);
                    var crystalBefore = inv.CountItems(crystal);
                    inv.AddItem("Bronze", 2, 1, 0, 0L, "", false);
                    inv.AddItem("Crystal", 2, 1, 0, 0L, "", false);
                    yield return KnowStation(c, player, station);
                    player.UpdateKnownRecipesList();
                    var row = new int[1];
                    yield return OpenStation(player, station, row);
                    if (c.Check(row[0] >= 0, "the spyglass is in the Forge's craft list on the server's world"))
                    {
                        var count = CountSpyglasses(inv);
                        yield return CraftRow(row[0]);
                        TrackNewSpyglasses(rig, had);
                        c.Check(CountSpyglasses(inv) == count + 1 && inv.CountItems(bronze) == bronzeBefore && inv.CountItems(crystal) == crystalBefore,
                            "crafted: one spyglass for 2 Bronze and 2 Crystal");
                        foreach (var item in inv.GetAllItems())
                        {
                            if (SpyglassContent.IsSpyglass(item) && !had.Contains(item))
                            {
                                crafted = item;
                            }
                        }
                    }
                    yield return CloseStation(player);
                }
            }
            if (!c.Check(crafted != null, "a crafted spyglass to use"))
            {
                c.Report();
                yield break;
            }

            // Use it.
            player.EquipItem(crafted, false);
            c.Check(player.GetRightItem() == crafted, "crafted spyglass in the right hand");
            yield return WaitReal(() => Scope.CanRaise(player), 4f);
            yield return Click(player);
            yield return WaitReal(() => Scope.State == ScopeState.Raised, 3f);
            yield return Frames(5);
            c.Check(Scope.State == ScopeState.Raised && ScopeOverlay.Shown, $"used: a click raises it ({Scope.State})");
            yield return LowerDown();

            // Drop it: server hold it as object it will save and can load.
            Player.m_enableAutoPickup = false;
            player.UnequipItem(crafted, false);
            inv.RemoveItem(crafted);
            var at = player.transform.position + player.transform.forward * 3f + Vector3.up * 0.5f;
            var drop = ItemDrop.DropItem(crafted, 1, at, Quaternion.identity);
            dropped = drop != null ? drop.gameObject : null;
            var view = drop != null ? drop.m_nview : null;
            if (c.Check(dropped != null && view != null && view.GetZDO() != null, "dropped on the ground"))
            {
                var id = view.GetZDO().m_uid;
                yield return Server(c, StepDrop, id.UserID.ToString(CultureInfo.InvariantCulture) + ":" + id.ID.ToString(CultureInfo.InvariantCulture),
                    reply, "dropped spyglass on the server");
                yield return new WaitForSeconds(2f);
                c.Check(dropped != null, "still on the ground here");
                if (dropped != null)
                {
                    var count = CountSpyglasses(inv);
                    var picked = player.Pickup(dropped, false, false);
                    yield return Frames(3);
                    TrackNewSpyglasses(rig, had);
                    c.Check(picked && CountSpyglasses(inv) == count + 1, "picked up again");
                    if (picked)
                    {
                        dropped = null;
                    }
                }
            }
            c.Report();
        }
        finally
        {
            Player.m_enableAutoPickup = autoPickup;
            if (dropped != null && ZNetScene.instance != null)
            {
                ZNetScene.instance.Destroy(dropped);
            }
            rig.Restore();
        }
    }

    // ---------- spyglass.mp.recall ----------

    private static IEnumerator RunMpRecall()
    {
        var c = new Checks(MpRecallTest);
        if (!Ready(MpRecallTest, out var player, out var cam))
        {
            yield break;
        }
        var rig = new SpyRig(player, MpRecallTest);
        try
        {
            var ok = new bool[1];
            yield return MpStart(c, ok);
            var spy = ok[0] ? Hold(rig, c) : null;
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            rig.KeepPlace();
            Scope.TestSetMagnification(4f);
            yield return RaiseUp(c, player, "recall");
            var p = player.transform.position;
            var reply = new SelfTest.ServerReply();
            yield return Server(c, StepRecall, $"{ZNet.GetUID().ToString(CultureInfo.InvariantCulture)}|{N(p.x)}|{N(p.y)}|{N(p.z)}", reply, "recall from the server");
            yield return WaitReal(() => player.IsTeleporting(), 5f);
            if (c.Check(player.IsTeleporting(), "the recall teleports this player"))
            {
                yield return null;
                yield return null;
                c.Check(NotBack(player).Length == 0, "recalled while looking: view back at once" + (NotBack(player).Length == 0 ? "" : ": " + NotBack(player)));
                Scope.TestRequestRaise();
                yield return Frames(3);
                c.Check(Scope.State == ScopeState.Idle, "no raise during the teleport");
                var during = new string[1];
                yield return WatchTeleport(player, 45f, during);
                c.Check(during[0].Length == 0, "nothing of the spyglass shows on any frame of the teleport" + (during[0].Length == 0 ? "" : ": " + during[0]));
                yield return Frames(5);
                c.Check(!player.IsTeleporting() && Scope.State == ScopeState.Idle && !ScopeOverlay.Shown && Hud.instance.m_crosshair.enabled
                        && Near(cam.m_camera.fieldOfView, cam.m_fov, 0.01f), "still down after arriving, normal view");
                yield return RaiseUp(c, player, "after the recall");
                yield return LowerDown();
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- spyglass.mp.settings ----------

    private static IEnumerator RunMpSettings()
    {
        var c = new Checks(MpSettingsTest);
        if (!Ready(MpSettingsTest, out var player, out var cam))
        {
            yield break;
        }
        var rig = new SpyRig(player, MpSettingsTest);
        try
        {
            var ok = new bool[1];
            yield return MpStart(c, ok);
            var spy = ok[0] ? Hold(rig, c) : null;
            if (spy == null)
            {
                c.Report();
                yield break;
            }
            // Config file of this run is throwaway: me set real settings, like player change them.
            var clear = Plugin.ClearViewSize.Value;
            var blurOn = Plugin.EdgeBlur.Value;
            var darkness = Plugin.EdgeDarkness.Value;
            var aim = Plugin.AimSensitivity.Value;
            var hold = Plugin.HoldToLook.Value;
            var start = Plugin.StartMagnification.Value;
            var cap = Plugin.MaxMagnification.Value;
            rig.OnRestore("settings", () =>
            {
                Plugin.ClearViewSize.Value = clear;
                Plugin.EdgeBlur.Value = blurOn;
                Plugin.EdgeDarkness.Value = darkness;
                Plugin.AimSensitivity.Value = aim;
                Plugin.HoldToLook.Value = hold;
                Plugin.StartMagnification.Value = start;
                Plugin.MaxMagnification.Value = cap;
            });
            Plugin.ClearViewSize.Value = ScopeOverlay.DefaultClearSize;
            Plugin.EdgeBlur.Value = true;
            Plugin.EdgeDarkness.Value = ScopeOverlay.DefaultEdgeDarkness;
            Plugin.AimSensitivity.Value = 1f;
            Plugin.HoldToLook.Value = false;

            // StartMagnification: where zoom start in a session.
            Plugin.StartMagnification.Value = 2.5f;
            Scope.TestRawMagnification = -1f;
            c.Check(Near(Scope.Magnification, 2.5f, 0.001f), $"StartMagnification 2.5: the first zoom of a session (x{F(Scope.Magnification)})");
            Scope.TestSetMagnification(4f);
            rig.Look(OpenYaw(player, out _), -1f);
            yield return RaiseUp(c, player, "settings");
            yield return Frames(12);
            c.Check(Near(ScopeOverlay.TestDark.x, 0.7f) && ScopeOverlay.TestBlurOn, "default view first");
            Plugin.ClearViewSize.Value = 0.4f;
            yield return Frames(2);
            c.Check(Near(ScopeOverlay.TestDark.x, 0.4f), $"ClearViewSize 0.4 shows at once ({F(ScopeOverlay.TestDark.x)})");
            Plugin.ClearViewSize.Value = 1f;
            yield return Frames(2);
            c.Check(Near(ScopeOverlay.TestDark.x, 1f), $"ClearViewSize 1 ({F(ScopeOverlay.TestDark.x)})");
            Plugin.EdgeBlur.Value = false;
            yield return Frames(2);
            c.Check(!ScopeOverlay.TestBlurOn, "EdgeBlur off: no blur");
            Plugin.EdgeBlur.Value = true;
            Plugin.EdgeDarkness.Value = 0f;
            yield return Frames(3);
            c.Check(Near(ScopeOverlay.TestDark.z, 0f) && ScopeOverlay.TestBlurOn, $"EdgeDarkness 0 ({F(ScopeOverlay.TestDark.z)}), blur on again");
            Plugin.EdgeDarkness.Value = 1f;
            yield return Frames(2);
            c.Check(Near(ScopeOverlay.TestDark.z, 1f), $"EdgeDarkness 1 ({F(ScopeOverlay.TestDark.z)})");
            Plugin.AimSensitivity.Value = 0.5f;
            var yaw0 = player.m_lookYaw.eulerAngles.y;
            player.SetMouseLook(new Vector2(80f, 0f));
            var turned = Mathf.DeltaAngle(yaw0, player.m_lookYaw.eulerAngles.y);
            player.SetMouseLook(new Vector2(-80f, 0f));
            c.Check(Near(turned, 80f * 0.5f / 4f, 0.3f), $"AimSensitivity 0.5: 80 in turns {F(turned)} deg at x4");

            // Own MaxMagnification never beat the server's.
            Plugin.MaxMagnification.Value = 2f;
            Scope.TestSetMagnification(8f);
            yield return Frames(12);
            c.Check(Near(ServerRules.Current.MaxMagnification, SpyglassRules.Default.MaxMagnification) && Near(ScopeCamera.CurrentMagnification, 8f, 0.01f),
                $"own MaxMagnification 2 on a server allowing x8: zoom x{F(ScopeCamera.CurrentMagnification)}");
            yield return LowerDown();

            // HoldToLook, the real setting.
            Plugin.HoldToLook.Value = true;
            yield return WaitReal(() => Scope.CanRaise(player), 4f);
            yield return new WaitForFixedUpdate();
            yield return HoldAttack(player, 2f, true, () => Scope.State == ScopeState.Raised);
            var up = Scope.State == ScopeState.Raised;
            Controls(player);
            yield return null;
            yield return null;
            c.Check(up && !Scope.Engaged, $"HoldToLook on: held = up ({up}), released = down ({Scope.State})");
            yield return WaitReal(() => Scope.State == ScopeState.Idle, 3f);
            Plugin.HoldToLook.Value = false;
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- spyglass.mp.toggle ----------

    private static string StatusInConfig()
    {
        if (BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(ModInfo.Guid, out var info) && info.Instance != null
            && info.Instance.Config.TryGetEntry<string>("General", "Status", out ConfigEntry<string> entry))
        {
            return entry.Value;
        }
        return null;
    }

    private static IEnumerator RunMpToggle()
    {
        var c = new Checks(MpToggleTest);
        if (!Ready(MpToggleTest, out var player, out var cam))
        {
            yield break;
        }
        var rig = new SpyRig(player, MpToggleTest);
        try
        {
            var ok = new bool[1];
            yield return MpStart(c, ok);
            var spy = ok[0] ? Hold(rig, c) : null;
            var found = FeatureRegistry.Find(ModInfo.Guid);
            var recipe = SpyglassContent.CraftRecipe;
            if (spy == null || !c.Check(found.HasValue && found.Value.Enabled != null && found.Value.IsActive, "the feature is on and has its Enabled switch"))
            {
                c.Report();
                yield break;
            }
            var view = found.Value;
            var enabled = view.Enabled;
            // Server refuse a player who turn the mod off: for this test it let them stay.
            var reply = new SelfTest.ServerReply();
            yield return Server(c, StepAllow, "1", reply, "server lets players without the mod stay");
            if (!reply.Answered || !reply.Ok)
            {
                c.Report();
                yield break;
            }
            rig.Noon();
            rig.Look(OpenYaw(player, out _), -1f);
            Scope.TestSetMagnification(4f);
            yield return new WaitForSeconds(0.5f);
            var vanillaFov = cam.m_camera.fieldOfView;
            yield return RaiseUp(c, player, "toggle");
            yield return Frames(10);

            // The real switch, like MC Mods panel flip it.
            rig.OnRestore("Enabled", () =>
            {
                if (!enabled.Value)
                {
                    enabled.Value = true;
                }
            });
            enabled.Value = false;
            var left = OffNow(player);
            c.Check(!Plugin.FeatureActive && view.State == nameof(ModState.Disabled), $"Enabled = false: feature off ({view.State})");
            c.Check(left.Length == 0, "off: everything back in the same frame" + (left.Length == 0 ? "" : ": " + left));
            c.Check(view.Status.StartsWith("Off", StringComparison.Ordinal) && StatusInConfig() == view.Status, $"Status says off: \"{view.Status}\" (config: \"{StatusInConfig()}\")");
            c.Check(player.GetRightItem() == spy, "off: the spyglass stays in the hand");
            c.Check(!recipe.m_enabled, "off: recipe hidden");
            yield return Frames(3);
            yield return new WaitForEndOfFrame();
            var gap = (cam.transform.position - player.m_eye.position).magnitude;
            c.Check(Near(cam.m_camera.fieldOfView, vanillaFov, 0.01f) && gap > 0.5f, $"off: camera and zoom normal (fov {F(cam.m_camera.fieldOfView)}, {F(gap)} m from the eye)");
            yield return DeadClicks(c, player);
            c.Check(SurvivesLoad(player.GetInventory()) && ObjectDB.instance.GetItemPrefab(SpyglassContent.ItemName) != null,
                "off: the spyglass is still a known item, a saved inventory loads it back");
            yield return Seconds(3f);
            c.Check(ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected && Player.m_localPlayer == player, "still on the server");

            // On again.
            enabled.Value = true;
            c.Check(view.IsActive && Plugin.FeatureActive, $"Enabled = true: feature on again ({view.State}: {view.Status})");
            yield return WaitReal(() => ServerRules.UsingServer, 10f);
            c.Check(ServerRules.UsingServer, "the server's rules asked for and received again");
            yield return WaitReal(() => recipe.m_enabled, 4f);
            c.Check(recipe.m_enabled, "on: recipe back");
            yield return WaitReal(() => Scope.CanRaise(player), 4f);
            yield return Click(player);
            yield return WaitReal(() => Scope.State == ScopeState.Raised, 3f);
            yield return Frames(5);
            yield return new WaitForEndOfFrame();
            c.Check(Scope.State == ScopeState.Raised && ScopeOverlay.Shown && !Hud.instance.m_crosshair.enabled
                    && Near(cam.m_camera.fieldOfView, ScopeCamera.ZoomedFov(vanillaFov, 4f), 0.05f),
                $"on: a click raises it again, round view and zoom back ({Scope.State})");
            yield return LowerDown();

            // Server refuse players without the mod again: this game, on again, stay.
            yield return Server(c, StepAllow, "0", reply, "server refuses players without the mod again");
            yield return Seconds(3f);
            c.Check(ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected && Player.m_localPlayer == player && view.IsActive,
                "still on the server with the feature active");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }
}
#endif
