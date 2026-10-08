#if DEBUG
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx.Logging;
using MC.Combat.SneakAmbushMod.Patches;
using MC.Shared;
using UnityEngine;

namespace MC.Combat.SneakAmbushMod;

// Debug build only (whole file). Multiplayer self tests (tools/Test-Multiplayer.ps1): ONE client joined to a real
// dedicated server, both with every MC mod. Client tests here, their server halves below ("sneak.mp.s.*"). No second
// player exist in the run: where an item of TESTING.md need a friend, the SERVER stand in (what it stores, decides and
// relays), and the item stay "partial" in the coverage list.
//   sneak.mp.rules          M01 server change StillBonus live: client use it (Info line, icon -50%), own config same
//   sneak.mp.join           M02 player with the mod: server find him compatible and say "allowed" (Debug); M13 (b)
//                           Smoke Screen registered on the dedicated server
//   sneak.mp.toggle         M15 (b)(c) mod off and on within the grace: "Told the server" lines, player stay
//   sneak.mp.allow-switch   M17 AllowPlayersWithoutMod on: player with mod off stay, server warn (M12); switched back
//                           off: every player checked again, compatible one stay
//   sneak.mp.cloud          M04 / M07 / M10 / M13 (c): thrown cloud as the server store it (same start, size, time,
//                           kept when owner leave), server hand it to the player near it when the thrower's game is gone,
//                           cloud unchanged, hide to its end, then gone on the server too
//   sneak.mp.xp             M03: XP call arriving over the network from another game pay the attacker; creature
//                           cooldown reach the server
//   sneak.mp.stealth        M05: stealth of the still player as the server relay it to other games
//   sneak.mp.smoke          M13 (a): hidden in smoke and burst blind with creatures on a dedicated server
//   sneak.mp.server-off     M14 (live part): server turn the mod off: recipe hidden, throw refused, items kept, dropped
//                           Smoke Screen stay known to the server; on again: throw work, cloud hide
//   sneak.mp.no-server-mod  M08, scenario vanilla-server: Inactive text, recipe hidden, throw refused, items kept
// Server halves use the server's real settings (ConfigEntry: config files of a multiplayer run are throwaway).
internal static partial class SelfTests
{
    private const string MpRulesName = "sneak.mp.rules";
    private const string MpJoinName = "sneak.mp.join";
    private const string MpToggleName = "sneak.mp.toggle";
    private const string MpAllowName = "sneak.mp.allow-switch";
    private const string MpCloudName = "sneak.mp.cloud";
    private const string MpXpName = "sneak.mp.xp";
    private const string MpStealthName = "sneak.mp.stealth";
    private const string MpSmokeName = "sneak.mp.smoke";
    private const string MpServerOffName = "sneak.mp.server-off";
    private const string MpNoServerModName = "sneak.mp.no-server-mod";

    // Scenario of tools/Test-Multiplayer.ps1 where the server runs no MC mod.
    private const string VanillaServerScenario = "vanilla-server";

    // Server probe's own step: "<guid>=on|off".
    private const string SetEnabledStep = "probe.set-enabled";

    private const string StepRules = "sneak.mp.s.rules";
    private const string StepSet = "sneak.mp.s.set";
    private const string StepContent = "sneak.mp.s.content";
    private const string StepPeer = "sneak.mp.s.peer";
    private const string StepRecheck = "sneak.mp.s.recheck";
    private const string StepLog = "sneak.mp.s.log";
    private const string StepCloud = "sneak.mp.s.cloud";
    private const string StepHandover = "sneak.mp.s.handover";
    private const string StepStealth = "sneak.mp.s.stealth";
    private const string StepXp = "sneak.mp.s.xp";
    private const string StepLastXp = "sneak.mp.s.lastxp";
    private const string StepWorld = "sneak.mp.s.world";

    // "Player" that owned a cloud and whose game is gone (no such peer on the server).
    private const long DepartedPlayer = 987654321012L;

    private static void RegisterMultiplayer()
    {
        SelfTest.RegisterMultiplayer(MpRulesName, SelfTest.Modded, RunMpRules);
        SelfTest.RegisterMultiplayer(MpJoinName, SelfTest.Modded, RunMpJoin);
        SelfTest.RegisterMultiplayer(MpToggleName, SelfTest.Modded, RunMpToggle);
        SelfTest.RegisterMultiplayer(MpAllowName, SelfTest.Modded, RunMpAllow);
        SelfTest.RegisterMultiplayer(MpCloudName, SelfTest.Modded, RunMpCloud);
        SelfTest.RegisterMultiplayer(MpXpName, SelfTest.Modded, RunMpXp);
        SelfTest.RegisterMultiplayer(MpStealthName, SelfTest.Modded, RunMpStealth);
        SelfTest.RegisterMultiplayer(MpSmokeName, SelfTest.Modded, RunMpSmoke);
        SelfTest.RegisterMultiplayer(MpServerOffName, SelfTest.Modded, RunMpServerOff);
        SelfTest.RegisterMultiplayer(MpNoServerModName, VanillaServerScenario, RunMpNoServerMod);
        SelfTest.RegisterServerStep(StepRules, ServerRulesStep);
        SelfTest.RegisterServerStep(StepSet, ServerSetStep);
        SelfTest.RegisterServerStep(StepContent, ServerContentStep);
        SelfTest.RegisterServerStep(StepPeer, ServerPeerStep);
        SelfTest.RegisterServerStep(StepRecheck, ServerRecheckStep);
        SelfTest.RegisterServerStep(StepLog, ServerLogStep);
        SelfTest.RegisterServerStep(StepCloud, ServerCloudStep);
        SelfTest.RegisterServerStep(StepHandover, ServerHandoverStep);
        SelfTest.RegisterServerStep(StepStealth, ServerStealthStep);
        SelfTest.RegisterServerStep(StepXp, ServerXpStep);
        SelfTest.RegisterServerStep(StepLastXp, ServerLastXpStep);
        SelfTest.RegisterServerStep(StepWorld, ServerWorldStep);
    }

    // Feature off. Two things stay on purpose: the vanilla-server test (this copy is inactive on such a server, and
    // the test is about exactly that) and the server halves (sneak.mp.server-off ask the server while its copy is
    // turned off). Both do nothing unless a multiplayer test run call them.
    private static void UnregisterMultiplayer()
    {
        SelfTest.UnregisterMultiplayer(MpRulesName);
        SelfTest.UnregisterMultiplayer(MpJoinName);
        SelfTest.UnregisterMultiplayer(MpToggleName);
        SelfTest.UnregisterMultiplayer(MpAllowName);
        SelfTest.UnregisterMultiplayer(MpCloudName);
        SelfTest.UnregisterMultiplayer(MpXpName);
        SelfTest.UnregisterMultiplayer(MpStealthName);
        SelfTest.UnregisterMultiplayer(MpSmokeName);
        SelfTest.UnregisterMultiplayer(MpServerOffName);
    }

    // ---------- helpers ----------

    // "a=1;b=two" answer of a server half.
    private static string Field(string detail, string key)
    {
        foreach (var part in (detail ?? "").Split(';'))
        {
            var eq = part.IndexOf('=');
            if (eq > 0 && part.Substring(0, eq) == key)
            {
                return part.Substring(eq + 1);
            }
        }
        return "";
    }

    private static string I(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static bool Connected =>
        ZNet.instance != null && !ZNet.instance.IsServer() && Player.m_localPlayer != null
        && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected;

    // Client of a server that has the mod, mod active here, the server's rules in use. False = said and test over.
    private static IEnumerator ClientReady(Checks c, Box ready)
    {
        ready.Ok = false;
        if (!c.Check(Connected, "not connected to a server as a client") || !c.Check(ModActive(ModInfo.Guid), $"{ModInfo.Name} is not active on this client"))
        {
            yield break;
        }
        yield return WaitFor(() => ServerRules.UsingServer, 6f);
        ready.Ok = c.Check(ServerRules.UsingServer && !ServerRules.IsPending, "the client does not use the server's rules (still waiting for them)");
        if (ready.Ok && Player.m_localPlayer != null)
        {
            yield return FreeHands(Player.m_localPlayer);
        }
    }

    // Hands free: no swing, no dodge. The test before (any mod) may end with a swing still playing, and the game
    // refuse to equip during one (Humanoid.EquipItem: InAttack or InDodge = false). First multiplayer run: Tower Shield
    // Wall's test began a punch and ended in the same frame, the next test (sneak.mp.no-server-mod) could not hold a
    // Smoke Screen. A swing begun this very frame is not in the animator yet (InAttack still false): the combat timer
    // (0 at Humanoid.StartAttack) cover that gap. Bounded: 6 s.
    private static IEnumerator FreeHands(Player player)
    {
        var until = Time.time + 6f;
        while (Time.time < until && player != null && (player.InAttack() || player.InDodge() || player.m_lastCombatTimer < 0.4f))
        {
            yield return null;
        }
    }

    private static string HandsText(Player player) =>
        $"in an attack {player.InAttack()}, dodging {player.InDodge()}, swimming {player.IsSwimming()}, on the ground {player.IsOnGround()}, "
        + $"{F(player.m_lastCombatTimer)} s since the last attack began";

    // Server half called; false (and a failed check) when it did not answer ok.
    private static IEnumerator Ask(Checks c, string step, string arg, SelfTest.ServerReply reply, bool mustBeOk = true)
    {
        reply.Answered = false;
        reply.Ok = false;
        reply.Detail = "";
        yield return SelfTest.CallServer(step, arg, reply);
        c.Check(reply.Answered && (reply.Ok || !mustBeOk), $"server step {step}('{arg}'): {reply}");
    }

    // The one player of the run, as the server sees him.
    private static ZNetPeer OnlyPlayer()
    {
        var net = ZNet.instance;
        if (net == null || !net.IsServer())
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

    private static List<ZDO> ServerZdos(string prefabName)
    {
        var hash = prefabName.GetStableHashCode();
        var list = new List<ZDO>();
        if (ZDOMan.instance == null)
        {
            return list;
        }
        foreach (var zdo in ZDOMan.instance.m_objectsByID.Values)
        {
            if (zdo != null && zdo.IsValid() && zdo.GetPrefab() == hash)
            {
                list.Add(zdo);
            }
        }
        return list;
    }

    // ---------- server halves ----------

    private static IEnumerator ServerRulesStep(string arg, object[] reply)
    {
        yield return null;
        var net = ZNet.instance;
        SelfTest.Answer(reply, net != null && net.IsServer() && Plugin.FeatureActive, AmbushRules.Own().Describe());
    }

    // "StillBonus=50", "CloudDuration=12", "Allow=true": the server owner changing that setting while players are in.
    private static IEnumerator ServerSetStep(string arg, object[] reply)
    {
        var eq = (arg ?? "").IndexOf('=');
        var key = eq > 0 ? arg.Substring(0, eq) : "";
        var value = eq > 0 ? arg.Substring(eq + 1) : "";
        var known = true;
        switch (key)
        {
            case "StillBonus":
                Plugin.StillBonus.Value = int.Parse(value, CultureInfo.InvariantCulture);
                break;
            case "CloudDuration":
                Plugin.CloudDuration.Value = float.Parse(value, CultureInfo.InvariantCulture);
                break;
            case "Allow":
                Plugin.AllowPlayersWithoutMod.Value = value == "true";
                break;
            default:
                known = false;
                break;
        }
        // Same frame as the change: has the join check been queued for the players already in?
        var pending = PlayerCheck.HasWork;
        yield return null;
        yield return null;
        SelfTest.Answer(reply, known, $"set={key};value={value};pending={pending};allow={Plugin.AllowPlayersWithoutMod.Value};"
                                     + $"still={Plugin.StillBonus.Value};duration={I(Plugin.CloudDuration.Value)}");
    }

    // M13 (b): what the Debug line "Made the Smoke Screen from BombSmoke ..." report, as state.
    private static IEnumerator ServerContentStep(string arg, object[] reply)
    {
        yield return null;
        var net = ZNet.instance;
        var db = ObjectDB.instance;
        var scene = ZNetScene.instance;
        var inDb = db != null && SmokeContent.ItemPrefab != null && db.GetItemPrefab(SmokeContent.ItemName) == SmokeContent.ItemPrefab;
        var inScene = scene != null && SmokeContent.Built
                      && scene.GetPrefab(SmokeContent.ItemName) == SmokeContent.ItemPrefab
                      && scene.GetPrefab(SmokeContent.ProjectileName) == SmokeContent.ProjectilePrefab
                      && scene.GetPrefab(SmokeContent.CloudName) == SmokeContent.CloudPrefab;
        var recipes = db != null ? OurRecipes(db) : 0;
        SelfTest.Answer(reply, net != null && net.IsServer() && SmokeContent.Built && inDb && inScene && recipes == 1 && !SmokeContent.MissingReported,
            $"dedicated={net != null && net.IsDedicated()};built={SmokeContent.Built};itemDatabase={inDb};networkPrefabs={inScene};recipes={recipes};"
            + $"recipe={RecipeText(SmokeContent.CraftRecipe)};missingReported={SmokeContent.MissingReported};headless={SmokeVisual.Headless};"
            + $"active={Plugin.FeatureActive}");
    }

    private static IEnumerator ServerPeerStep(string arg, object[] reply)
    {
        yield return null;
        var peer = OnlyPlayer();
        if (peer == null)
        {
            SelfTest.Answer(reply, false, "no player on the server");
            yield break;
        }
        var compatible = NetworkGate.PeerCompatible(peer);
        var kicked = ZNet.PeersToDisconnectAfterKick.ContainsKey(peer);
        SelfTest.Answer(reply, true, $"name={peer.m_playerName};hasMod={NetworkGate.PeerHasMod(peer)};network={NetworkGate.PeerNetworkVersion(peer)};"
                                     + $"ready={NetworkGate.PeerReady(peer)};compatible={compatible};problem={NetworkGate.PeerProblem(peer) ?? ""};"
                                     + $"kicked={kicked};allow={Plugin.AllowPlayersWithoutMod.Value};pending={PlayerCheck.HasWork};uid={peer.m_uid}");
    }

    // Join check again for everyone in (what the server does when its mod turns on, or AllowPlayersWithoutMod goes off).
    private static IEnumerator ServerRecheckStep(string arg, object[] reply)
    {
        PlayerCheck.ScheduleAllConnected();
        var queued = PlayerCheck.HasWork;
        yield return null;
        SelfTest.Answer(reply, true, $"queued={queued}");
    }

    // "begin": listen to this mod's log lines on the server. "count|<text>": lines holding the text since. "end".
    private static IEnumerator ServerLogStep(string arg, object[] reply)
    {
        yield return null;
        var text = arg ?? "";
        if (text == "begin")
        {
            Tap.Install(false);
            Tap.Hold = true;
            SelfTest.Answer(reply, true, "listening");
        }
        else if (text == "end")
        {
            Tap.Release();
            SelfTest.Answer(reply, true, "stopped");
        }
        else if (text.StartsWith("count|", System.StringComparison.Ordinal))
        {
            var part = text.Substring(6);
            SelfTest.Answer(reply, true, $"count={Tap.Logged(part)};first={Tap.FirstLogged(part).Replace(';', ',')}");
        }
        else
        {
            SelfTest.Answer(reply, false, "unknown log request");
        }
    }

    // "" = newest cloud the server stores. "gone|<start ms>" = ok when no cloud with that start is left.
    private static IEnumerator ServerCloudStep(string arg, object[] reply)
    {
        yield return null;
        var clouds = ServerZdos(SmokeContent.CloudName);
        if ((arg ?? "").StartsWith("gone|", System.StringComparison.Ordinal))
        {
            long.TryParse(arg.Substring(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out var start);
            var left = clouds.Count(z => z.GetLong(SmokeCloud.StartKey, 0L) == start);
            SelfTest.Answer(reply, left == 0, $"count={clouds.Count};withThatStart={left}");
            yield break;
        }
        var newest = clouds.OrderByDescending(z => z.GetLong(SmokeCloud.StartKey, 0L)).FirstOrDefault();
        if (newest == null)
        {
            SelfTest.Answer(reply, false, "count=0");
            yield break;
        }
        var peer = OnlyPlayer();
        var p = newest.GetPosition();
        SelfTest.Answer(reply, true, $"count={clouds.Count};owner={newest.GetOwner()};player={(peer != null ? peer.m_uid : 0L)};persistent={newest.Persistent};"
                                     + $"start={newest.GetLong(SmokeCloud.StartKey, 0L)};duration={I(newest.GetFloat(SmokeCloud.DurationKey, -1f))};"
                                     + $"radius={I(newest.GetFloat(SmokeCloud.RadiusKey, -1f))};height={I(newest.GetFloat(SmokeCloud.HeightKey, -1f))};"
                                     + $"x={I(p.x)};y={I(p.y)};z={I(p.z)};prefabKnown={ZNetScene.instance != null && ZNetScene.instance.GetPrefab(newest.GetPrefab()) != null}");
    }

    // The thrower's game is gone: the cloud's owner is no player the server knows any more. Vanilla then hands a
    // persistent object to a player near it (ZDOMan.ReleaseNearbyZDOS, every 2 s). Answer when the player has it.
    private static IEnumerator ServerHandoverStep(string arg, object[] reply)
    {
        var peer = OnlyPlayer();
        var cloud = ServerZdos(SmokeContent.CloudName).OrderByDescending(z => z.GetLong(SmokeCloud.StartKey, 0L)).FirstOrDefault();
        if (peer == null || cloud == null)
        {
            SelfTest.Answer(reply, false, "no player or no cloud on the server");
            yield break;
        }
        var before = cloud.GetOwner();
        cloud.SetOwner(DepartedPlayer);
        var orphan = cloud.GetOwner();
        var start = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - start < 8f && cloud.IsValid() && cloud.GetOwner() != peer.m_uid)
        {
            yield return null;
        }
        var seconds = Time.realtimeSinceStartup - start;
        SelfTest.Answer(reply, cloud.IsValid() && cloud.GetOwner() == peer.m_uid,
            $"ownerBefore={before};ownerGone={orphan};ownerAfter={(cloud.IsValid() ? cloud.GetOwner() : -1L)};player={peer.m_uid};seconds={I(seconds)};"
            + $"persistent={cloud.IsValid() && cloud.Persistent}");
    }

    // Stealth factor of the player as the server stores and relays it (what creatures on other games read).
    private static IEnumerator ServerStealthStep(string arg, object[] reply)
    {
        yield return null;
        var peer = OnlyPlayer();
        var zdo = peer != null && ZDOMan.instance != null ? ZDOMan.instance.GetZDO(peer.m_characterID) : null;
        if (zdo == null)
        {
            SelfTest.Answer(reply, false, "no player object on the server");
            yield break;
        }
        SelfTest.Answer(reply, true, $"stealth={I(zdo.GetFloat(ZDOVars.s_stealth, -1f))}");
    }

    // "<health>|<0 or 1>": the sneak-attack XP call sent to the player's own object from another game (here: the
    // server), as the game that controls the creature sends it.
    private static IEnumerator ServerXpStep(string arg, object[] reply)
    {
        yield return null;
        var peer = OnlyPlayer();
        var parts = (arg ?? "").Split('|');
        if (peer == null || peer.m_characterID == ZDOID.None || parts.Length != 2 || ZRoutedRpc.instance == null
            || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var health))
        {
            SelfTest.Answer(reply, false, "no player object on the server, or bad argument");
            yield break;
        }
        ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, peer.m_characterID, SneakXp.Rpc, health, parts[1] == "1");
        SelfTest.Answer(reply, true, $"sent={I(health)};ranged={parts[1] == "1"};to={peer.m_playerName}");
    }

    // "<user id>|<object id>": XP cooldown stamp of that creature as the server stores it.
    private static IEnumerator ServerLastXpStep(string arg, object[] reply)
    {
        yield return null;
        var parts = (arg ?? "").Split('|');
        if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var user)
            || !uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || ZDOMan.instance == null)
        {
            SelfTest.Answer(reply, false, "bad argument");
            yield break;
        }
        var zdo = ZDOMan.instance.GetZDO(new ZDOID(user, id));
        SelfTest.Answer(reply, zdo != null, zdo != null ? $"lastXp={zdo.GetLong(SneakXp.LastXpKey, 0L)};nowMs={NowMs()}" : "the server does not know that creature");
    }

    // Smoke Screens in the world as the server stores them: on the ground, in chests, clouds; and whether the server
    // knows their prefabs (what keeps them through a save and load, also while the mod is turned off).
    private static IEnumerator ServerWorldStep(string arg, object[] reply)
    {
        yield return null;
        var scene = ZNetScene.instance;
        var known = scene != null && scene.GetPrefab(SmokeContent.ItemName.GetStableHashCode()) != null
                    && scene.GetPrefab(SmokeContent.ProjectileName.GetStableHashCode()) != null
                    && scene.GetPrefab(SmokeContent.CloudName.GetStableHashCode()) != null;
        var drops = ServerZdos(SmokeContent.ItemName).Count;
        var chested = 0;
        foreach (var chest in ServerZdos("piece_chest_wood"))
        {
            var bytes = chest.GetByteArray(ZDOVars.s_items);
            if (bytes == null || bytes.Length == 0)
            {
                continue;
            }
            try
            {
                var bag = new Inventory("selftest", null, 8, 8);
                bag.Load(new ZPackage(bytes));
                chested += bag.GetAllItems().Where(SmokeContent.IsSmokeScreen).Sum(i => i.m_stack);
            }
            catch (System.Exception)
            {
                // Chest the server cannot read: not ours to judge.
            }
        }
        var view = FeatureRegistry.Find(ModInfo.Guid);
        SelfTest.Answer(reply, known, $"prefabsKnown={known};drops={drops};chested={chested};clouds={ServerZdos(SmokeContent.CloudName).Count};"
                                      + $"active={Plugin.FeatureActive};state={(view != null ? view.Value.State : "?")}");
    }

    // ---------- sneak.mp.rules (M01) ----------

    private static IEnumerator RunMpRules()
    {
        var c = new Checks(MpRulesName);
        var rig = Rig.Create(MpRulesName);
        if (rig == null)
        {
            yield break;
        }
        var changed = false;
        var reply = new SelfTest.ServerReply();
        try
        {
            var player = rig.Player;
            var ready = new Box();
            yield return ClientReady(c, ready);
            if (!ready.Ok)
            {
                c.Report();
                yield break;
            }
            StealthCues.TestShowCues = true;
            Tap.Install();
            yield return Ask(c, StepRules, "", reply);
            c.Check(ServerRules.Current.Describe() == reply.Detail, $"client rules differ from the server's: client \"{ServerRules.Current.Describe()}\", server \"{reply.Detail}\"");
            var path = Path.Combine(BepInEx.Paths.ConfigPath, ModInfo.Guid + ".cfg");
            var fileBefore = File.Exists(path) ? File.ReadAllText(path) : "";
            var ownStill = Plugin.StillBonus.Value;
            var serverStill = ServerRules.Current.StillBonus;
            rig.SetTime(0.5f);
            rig.SetSneak(0f);
            yield return Stand(player);
            rig.TakeControls();
            var still = new Box();
            yield return HoldStill(rig, still);
            c.Check(still.Ok && StealthCues.IconText(CueKind.Still) == $"-{serverStill}%", $"before: Holding still icon '{StealthCues.IconText(CueKind.Still)}', the server's rule is {serverStill}%");

            // The server owner sets StillBonus = 50 while the player is in.
            Tap.Clear();
            yield return Ask(c, StepLog, "begin", reply);
            changed = true;
            yield return Ask(c, StepSet, "StillBonus=50", reply);
            var pushed = new Box();
            yield return WaitFor(() => ServerRules.Current.StillBonus == 50, 8f, pushed);
            // The server sends to every player in who has the mod: here the one player of the run.
            yield return Ask(c, StepLog, $"count|Sent the {ModInfo.Name} rules to 1 player(s)", reply);
            c.Check(Field(reply.Detail, "count") == "1" && Field(reply.Detail, "first").Contains("holding still 50%"),
                $"server Debug \"Sent the {ModInfo.Name} rules to 1 player(s): ... holding still 50% ...\" not logged once: {reply.Detail}");
            yield return Ask(c, StepLog, "end", reply);
            if (c.Check(pushed.Ok, $"the server's StillBonus = 50 did not reach the client within 8 s (rule {ServerRules.Current.StillBonus})"))
            {
                var line = Tap.FirstLogged("Using the server's rules: ");
                c.Check(Tap.Logged(LogLevel.Info, "Using the server's rules: ") == 1 && line.Contains("holding still 50%"),
                    $"Info \"Using the server's rules: ... holding still 50% ...\" not logged once: {line}");
                yield return new WaitForSeconds(0.8f);
                c.Check(StealthState.StillActive && StealthCues.IconText(CueKind.Still) == "-50%", $"Holding still icon reads '{StealthCues.IconText(CueKind.Still)}', expected '-50%'");
                c.Check(Near(player.m_stealthFactorTarget, WithStill(StealthState.LastWithoutStill, ServerRules.Current), 0.01f),
                    $"bar target {F(player.m_stealthFactorTarget)}, expected {F(WithStill(StealthState.LastWithoutStill, ServerRules.Current))} with the server's 50%");
                c.Check(Plugin.StillBonus.Value == ownStill, $"the client's own StillBonus setting changed ({ownStill} -> {Plugin.StillBonus.Value})");
                c.Check((File.Exists(path) ? File.ReadAllText(path) : "") == fileBefore, "the client's config file changed");
            }
            yield return Ask(c, StepSet, "StillBonus=" + serverStill.ToString(CultureInfo.InvariantCulture), reply);
            changed = false;
            yield return WaitFor(() => ServerRules.Current.StillBonus == serverStill, 8f, pushed);
            c.Check(pushed.Ok, $"the server's StillBonus put back to {serverStill} did not reach the client");
            NoProblems(c);
            c.Report();
        }
        finally
        {
            if (changed)
            {
                SelfTest.Note(MpRulesName, "the server's StillBonus may still be 50 (the test ended before putting it back)");
            }
            rig.Restore();
        }
    }

    // ---------- sneak.mp.join (M02, M13 b) ----------

    private static IEnumerator RunMpJoin()
    {
        var c = new Checks(MpJoinName);
        var reply = new SelfTest.ServerReply();
        var ready = new Box();
        yield return ClientReady(c, ready);
        if (!ready.Ok)
        {
            c.Report();
            yield break;
        }
        // M13 (b): the Smoke Screen is registered on the dedicated server.
        yield return Ask(c, StepContent, "", reply);
        c.Check(Field(reply.Detail, "dedicated") == "True", $"the server is not a dedicated server: {reply.Detail}");
        c.Note("server content: " + reply.Detail);

        // M02, last sentence: a player with the mod on is found compatible; the join check says "allowed" (Debug).
        yield return Ask(c, StepPeer, "", reply);
        c.Check(Field(reply.Detail, "hasMod") == "True" && Field(reply.Detail, "compatible") == "True" && Field(reply.Detail, "kicked") == "False"
                && Field(reply.Detail, "network") == ModInfo.NetworkVersion.ToString(CultureInfo.InvariantCulture),
            $"the server does not see this player as compatible: {reply.Detail}");
        var name = Field(reply.Detail, "name");
        yield return Ask(c, StepLog, "begin", reply);
        yield return Ask(c, StepRecheck, "", reply);
        c.Check(Field(reply.Detail, "queued") == "True", "the server queued no join check");
        yield return new WaitForSecondsRealtime(PlayerCheck.GraceSeconds + 1f);
        yield return Ask(c, StepLog, "count|with the same network version: allowed.", reply);
        var first = Field(reply.Detail, "first");
        c.Check(Field(reply.Detail, "count") == "1" && first.Contains(name) && first.Contains("has " + ModInfo.Name + " on"),
            $"server Debug \"{name} has {ModInfo.Name} on, with the same network version: allowed.\" not logged once: {reply.Detail}");
        yield return Ask(c, StepLog, "count|Refused ", reply);
        c.Check(Field(reply.Detail, "count") == "0", $"the server refused someone: {reply.Detail}");
        yield return Ask(c, StepLog, "end", reply);
        yield return new WaitForSecondsRealtime(PlayerCheck.DisconnectDelay + 1f);
        c.Check(Connected, "the player with the mod on did not stay connected");
        c.Report();
    }

    // ---------- sneak.mp.toggle (M15 b, c) ----------

    private static IEnumerator RunMpToggle()
    {
        var c = new Checks(MpToggleName);
        var reply = new SelfTest.ServerReply();
        var ready = new Box();
        yield return ClientReady(c, ready);
        var found = FeatureRegistry.Find(ModInfo.Guid);
        if (!ready.Ok || !c.Check(found != null && found.Value.Enabled != null, "this mod is not in the registry"))
        {
            c.Report();
            yield break;
        }
        var view = found.Value;
        try
        {
            Tap.Install();
            Tap.Hold = true;
            yield return Ask(c, StepLog, "begin", reply);
            // Unticked and ticked again within the grace (1 s): what the MC Mods panel does to the Enabled setting.
            view.Enabled.Value = false;
            yield return new WaitForSecondsRealtime(0.3f);
            c.Check(!view.IsActive && view.Status.StartsWith("Off (disabled in settings)", System.StringComparison.Ordinal), $"unticked: state {view.State}, '{view.Status}'");
            view.Enabled.Value = true;
            var back = new Box();
            yield return WaitFor(() => view.IsActive && ServerRules.UsingServer, 8f, back);
            c.Check(back.Ok, $"ticked again: state {view.State}, '{view.Status}', server rules in use {ServerRules.UsingServer}");
            c.Check(Tap.Logged(LogLevel.Info, $"Told the server that {ModInfo.Name} is now off on this game.") == 1,
                "client log: \"Told the server that ... is now off on this game.\" not logged once");
            c.Check(Tap.Logged(LogLevel.Info, $"Told the server that {ModInfo.Name} is now on on this game.") == 1,
                "client log: \"Told the server that ... is now on on this game.\" not logged once");
            // Past the grace and the 4 s before a refused player is cut: still in.
            yield return new WaitForSecondsRealtime(PlayerCheck.GraceSeconds + PlayerCheck.DisconnectDelay + 2f);
            c.Check(Connected && view.IsActive, $"off and on within a second: the player did not stay (status {ZNet.GetConnectionStatus()})");
            yield return Ask(c, StepPeer, "", reply);
            c.Check(Field(reply.Detail, "compatible") == "True" && Field(reply.Detail, "kicked") == "False", $"server after off and on: {reply.Detail}");
            yield return Ask(c, StepLog, $"count|turned {ModInfo.Name} off on their game", reply);
            c.Check(Field(reply.Detail, "count") == "1", $"the server did not hear the mod go off once: {reply.Detail}");
            yield return Ask(c, StepLog, "count|with the same network version: allowed.", reply);
            c.Check(Field(reply.Detail, "count") == "1", $"the server's check after the grace did not find the player compatible once: {reply.Detail}");
            yield return Ask(c, StepLog, "count|Refused ", reply);
            c.Check(Field(reply.Detail, "count") == "0", $"the server refused the player: {reply.Detail}");
            yield return Ask(c, StepLog, "end", reply);
            NoProblems(c);
            c.Report();
        }
        finally
        {
            if (!view.Enabled.Value)
            {
                view.Enabled.Value = true;
            }
            ClearOverrides();
            Tap.Release();
        }
    }

    // ---------- sneak.mp.allow-switch (M17, M12) ----------

    private static IEnumerator RunMpAllow()
    {
        var c = new Checks(MpAllowName);
        var reply = new SelfTest.ServerReply();
        var ready = new Box();
        yield return ClientReady(c, ready);
        var found = FeatureRegistry.Find(ModInfo.Guid);
        if (!ready.Ok || !c.Check(found != null && found.Value.Enabled != null, "this mod is not in the registry"))
        {
            c.Report();
            yield break;
        }
        var view = found.Value;
        var allowOn = false;
        try
        {
            yield return Ask(c, StepLog, "begin", reply);
            // Off -> on: nobody is checked again.
            yield return Ask(c, StepSet, "Allow=true", reply);
            allowOn = reply.Ok;
            c.Check(Field(reply.Detail, "allow") == "True" && Field(reply.Detail, "pending") == "False",
                $"switching AllowPlayersWithoutMod on: {reply.Detail} (expected nobody checked again)");
            // M12: this player turns the mod off: stays, the server warns.
            view.Enabled.Value = false;
            yield return new WaitForSecondsRealtime(PlayerCheck.GraceSeconds + PlayerCheck.DisconnectDelay + 2f);
            c.Check(Connected && !view.IsActive, $"mod off with AllowPlayersWithoutMod on: the player did not stay (status {ZNet.GetConnectionStatus()})");
            yield return Ask(c, StepLog, "count|has the mod turned off; AllowPlayersWithoutMod is on, so they may play", reply);
            c.Check(Field(reply.Detail, "count") == "1", $"server Warning \"... has the mod turned off; AllowPlayersWithoutMod is on ...\" not logged once: {reply.Detail}");
            yield return Ask(c, StepLog, "count|Refused ", reply);
            c.Check(Field(reply.Detail, "count") == "0", $"the server refused the player although AllowPlayersWithoutMod is on: {reply.Detail}");
            // Mod on again, then the server goes back to refusing: everyone in is checked again after the grace.
            view.Enabled.Value = true;
            var back = new Box();
            yield return WaitFor(() => view.IsActive && ServerRules.UsingServer, 8f, back);
            c.Check(back.Ok, $"mod on again: state {view.State}, '{view.Status}'");
            yield return new WaitForSecondsRealtime(PlayerCheck.GraceSeconds + 1f);
            yield return Ask(c, StepLog, "begin", reply);
            yield return Ask(c, StepSet, "Allow=false", reply);
            allowOn = !reply.Ok;
            c.Check(Field(reply.Detail, "allow") == "False" && Field(reply.Detail, "pending") == "True",
                $"switching AllowPlayersWithoutMod back off: {reply.Detail} (expected the players already in to be checked again)");
            yield return new WaitForSecondsRealtime(PlayerCheck.GraceSeconds + 1f);
            yield return Ask(c, StepLog, "count|with the same network version: allowed.", reply);
            c.Check(Field(reply.Detail, "count") == "1", $"after the switch the compatible player was not checked once: {reply.Detail}");
            yield return Ask(c, StepLog, "count|Refused ", reply);
            c.Check(Field(reply.Detail, "count") == "0", $"after the switch the compatible player was refused: {reply.Detail}");
            yield return Ask(c, StepLog, "end", reply);
            yield return new WaitForSecondsRealtime(PlayerCheck.DisconnectDelay + 1f);
            c.Check(Connected && view.IsActive, "the compatible player did not stay after AllowPlayersWithoutMod went back off");
            c.Report();
        }
        finally
        {
            if (!view.Enabled.Value)
            {
                view.Enabled.Value = true;
            }
            if (allowOn)
            {
                SelfTest.Note(MpAllowName, "the server's AllowPlayersWithoutMod may still be on (the test ended before putting it back)");
            }
        }
    }

    // ---------- sneak.mp.cloud (M04, M07, M10, M13 c) ----------

    private static IEnumerator RunMpCloud()
    {
        var c = new Checks(MpCloudName);
        var rig = Rig.Create(MpCloudName);
        if (rig == null)
        {
            yield break;
        }
        var reply = new SelfTest.ServerReply();
        var shortened = false;
        try
        {
            var player = rig.Player;
            var ready = new Box();
            yield return ClientReady(c, ready);
            if (!ready.Ok)
            {
                c.Report();
                yield break;
            }
            Tap.Install();
            // Short cloud for the test: the server's own setting, as everyone gets it.
            var normal = ServerRules.Current.CloudDuration;
            shortened = true;
            yield return Ask(c, StepSet, "CloudDuration=12", reply);
            var pushed = new Box();
            yield return WaitFor(() => Near(ServerRules.Current.CloudDuration, 12f), 8f, pushed);
            if (!c.Check(pushed.Ok, "the server's CloudDuration = 12 did not reach the client"))
            {
                c.Report();
                yield break;
            }
            var rules = ServerRules.Current;
            yield return Stand(player);
            var dir = ClearDirection(player, 12f, out _);
            var origin = player.transform.position;
            var g = rig.Creature("Greydwarf", Ground(origin + dir * 10f), -dir, false);
            var item = rig.GiveAndEquip(2);
            if (!c.Check(g != null && item != null, "could not spawn a Greydwarf or equip a Smoke Screen"))
            {
                c.Report();
                yield break;
            }
            // Real throw at the ground just ahead.
            rig.Aim(dir, 55f);
            yield return new WaitForSeconds(0.6f);
            var before = new HashSet<SmokeCloud>(SmokeRegistry.All);
            c.Check(player.StartAttack(null, false), "StartAttack with a Smoke Screen refused on the server");
            var slot = new CloudSlot();
            yield return WaitForCloud(before, 5f, slot);
            var cloud = slot.Cloud;
            if (!c.Check(cloud != null, "no cloud within 5 s of the throw"))
            {
                c.Report();
                yield break;
            }
            CheckCloud(c, cloud, rules, "throw on the server");
            var start = cloud.StartTime;
            var startMs = cloud.View.GetZDO().GetLong(SmokeCloud.StartKey, 0L);
            var cloudBase = cloud.Base;
            MovePlayer(player, cloudBase + Vector3.up * 0.1f);
            rig.MarkMoved();
            var ai = g.GetBaseAI();
            Place(g, g.transform.position, player.transform.position - g.transform.position);

            // M04: the server stores the same cloud (what every other game near it loads).
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Ask(c, StepCloud, "", reply);
            c.Check(Field(reply.Detail, "start") == startMs.ToString(CultureInfo.InvariantCulture)
                    && Field(reply.Detail, "duration") == I(cloud.Duration) && Field(reply.Detail, "radius") == I(cloud.Radius)
                    && Field(reply.Detail, "height") == I(cloud.Height) && Field(reply.Detail, "persistent") == "True"
                    && Field(reply.Detail, "owner") == Field(reply.Detail, "player") && Field(reply.Detail, "prefabKnown") == "True",
                $"the server's copy of the cloud differs (client start {startMs}, {I(cloud.Duration)} s, radius {I(cloud.Radius)}): {reply.Detail}");
            c.Check(float.TryParse(Field(reply.Detail, "y"), NumberStyles.Float, CultureInfo.InvariantCulture, out var serverY) && Near(serverY, cloudBase.y, 0.01f),
                $"the server's cloud base differs from the client's ({Field(reply.Detail, "y")} vs {F(cloudBase.y)})");

            // M07 / M10: the thrower's game is gone; the server hands the cloud to the player near it.
            yield return Ask(c, StepHandover, "", reply);
            c.Note("hand-over on the server: " + reply.Detail);
            var owner = new Box();
            yield return WaitFor(() => cloud == null || cloud.View.IsOwner(), 6f, owner);
            if (c.Check(cloud != null && cloud.View.IsOwner(), "after the hand-over this game does not own the cloud"))
            {
                c.Check(System.Math.Abs(cloud.StartTime - start) < 0.001d && Near(cloud.Duration, 12f) && Vector3.Distance(cloud.Base, cloudBase) < 0.01f,
                    "the cloud changed (start, time or place) with its owner");
                if (SmokeRegistry.Now < cloud.EndTime - 0.3d)
                {
                    WithoutSmoke(ai, player, out var wouldSee, out _);
                    c.Check(cloud.IsActive(SmokeRegistry.Now, rules) && InSmoke(player, rules) && wouldSee && !ai.CanSeeTarget(player),
                        $"after the hand-over the cloud does not hide the player from the Greydwarf outside (would see without smoke {wouldSee})");
                }
                else
                {
                    c.Check(false, "the hand-over took so long that the cloud was over: hiding after it not checked");
                }
            }
            // To its normal end, then gone here and on the server.
            yield return WaitFor(() => cloud == null, 22f);
            var goneAfter = SmokeRegistry.Now - start;
            c.Check(cloud == null && goneAfter >= 12f + SmokeCloud.FadeSeconds - 0.3d, $"cloud gone {F(goneAfter)} s after impact, expected about {F(12f + SmokeCloud.FadeSeconds)} s");
            yield return new WaitForSecondsRealtime(2f);
            yield return Ask(c, StepCloud, "gone|" + startMs.ToString(CultureInfo.InvariantCulture), reply);
            yield return Ask(c, StepSet, "CloudDuration=" + I(normal), reply);
            shortened = false;
            NoProblems(c);
            c.Report();
        }
        finally
        {
            if (shortened)
            {
                SelfTest.Note(MpCloudName, "the server's CloudDuration may still be 12 (the test ended before putting it back)");
            }
            rig.Restore();
        }
    }

    // ---------- sneak.mp.xp (M03) ----------

    private static IEnumerator RunMpXp()
    {
        var c = new Checks(MpXpName);
        var rig = Rig.Create(MpXpName);
        if (rig == null)
        {
            yield break;
        }
        var reply = new SelfTest.ServerReply();
        try
        {
            var player = rig.Player;
            var ready = new Box();
            yield return ClientReady(c, ready);
            if (!ready.Ok)
            {
                c.Report();
                yield break;
            }
            var rules = ServerRules.Current;
            Compat.TestOtherModPays = false;
            SneakXp.TestShowMessage = true;
            Tap.Install();
            rig.SaveAllSkills();
            rig.SetSneak(20f);
            yield return Stand(player);
            var skill = player.GetSkills().GetSkill(Skills.SkillType.Sneak);
            var step = skill.m_info.m_increseStep;
            var multiplier = 1f;
            player.GetSEMan().ModifyRaiseSkill(Skills.SkillType.Sneak, ref multiplier);
            float Gain(float xp) => step * xp * Game.m_skillGainRate * multiplier;
            var melee = SneakXp.Amount(rules, 40f, false);
            var ranged = SneakXp.Amount(rules, 40f, true);

            // The XP call arrives over the network from another game (the server stands in for the creature's owner).
            yield return Ask(c, StepXp, "40|0", reply);
            var paid = new Box();
            yield return WaitFor(() => skill.m_accumulator > 0f, 5f, paid);
            c.Check(paid.Ok && Near(skill.m_accumulator, Gain(melee), 0.001f), $"40 health sent by another game: progress +{F(skill.m_accumulator)}, expected +{F(Gain(melee))} ({F(melee)} XP)");
            c.Check(Tap.Messages("Sneak attack!", MessageHud.MessageType.TopLeft) == 1, $"no \"Sneak attack!\" message for XP sent by another game ({Tap.MessageTexts("Sneak attack")})");
            c.Check(Tap.Logged($"Sneak attack: {F(melee)} Sneak XP.") == 1, $"Debug \"Sneak attack: {F(melee)} Sneak XP.\" not logged once ({Tap.FirstLogged("Sneak attack: ")})");
            rig.SetSneak(20f);
            Tap.Clear();
            yield return Ask(c, StepXp, "40|1", reply);
            yield return WaitFor(() => skill.m_accumulator > 0f, 5f, paid);
            c.Check(paid.Ok && Near(skill.m_accumulator, Gain(ranged), 0.001f), $"40 health (ranged) sent by another game: progress +{F(skill.m_accumulator)}, expected +{F(Gain(ranged))}");
            c.Check(Tap.Logged($"Sneak attack: {F(ranged)} Sneak XP (ranged).") == 1, "Debug line for the ranged XP not logged once");
            rig.SetSneak(20f);
            yield return Ask(c, StepXp, "-5|0", reply);
            yield return new WaitForSecondsRealtime(1.5f);
            c.Check(Near(skill.m_accumulator, 0f), "a bad payload (-5 health) sent by another game paid XP");

            // Sender half on this game: its creature, sneak-attacked; the cooldown stamp reaches the server (it then
            // holds for whichever game controls the creature next).
            var dir = ClearDirection(player, 6f, out _);
            var g = rig.Creature("Greydwarf", Ground(player.transform.position + dir * 3f), dir, false);
            if (c.Check(g != null, "could not spawn a Greydwarf"))
            {
                yield return Frames(3);
                rig.SetSneak(20f);
                Tap.Clear();
                Hit(g, player, Slash(1f), 3f);
                var zdo = g.m_nview.GetZDO();
                var stamp = zdo.GetLong(SneakXp.LastXpKey, 0L);
                c.Check(stamp != 0L && Near(skill.m_accumulator, Gain(melee), 0.001f) && Tap.Logged("sent 40 health to their game for Sneak XP.") == 1,
                    $"sneak attack on this game's Greydwarf: stamp {stamp}, progress +{F(skill.m_accumulator)}, sent line {Tap.Logged("to their game")}");
                yield return new WaitForSecondsRealtime(2f);
                yield return Ask(c, StepLastXp, $"{zdo.m_uid.UserID.ToString(CultureInfo.InvariantCulture)}|{zdo.m_uid.ID.ToString(CultureInfo.InvariantCulture)}", reply);
                c.Check(Field(reply.Detail, "lastXp") == stamp.ToString(CultureInfo.InvariantCulture), $"the creature's XP cooldown stamp on the server: {reply.Detail}, here {stamp}");
            }
            NoProblems(c);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.mp.stealth (M05) ----------

    private static IEnumerator RunMpStealth()
    {
        var c = new Checks(MpStealthName);
        var rig = Rig.Create(MpStealthName);
        if (rig == null)
        {
            yield break;
        }
        var reply = new SelfTest.ServerReply();
        try
        {
            var player = rig.Player;
            var ready = new Box();
            yield return ClientReady(c, ready);
            if (!ready.Ok)
            {
                c.Report();
                yield break;
            }
            var rules = ServerRules.Current;
            StealthCues.TestShowCues = true;
            rig.SetEnv("Clear");
            rig.SetTime(0.5f);
            rig.SetSneak(0f);
            yield return Stand(player);
            var dir = ClearDirection(player, 12f, out _);
            var origin = player.transform.position;
            rig.TakeControls();
            rig.Aim(dir, 0f);
            var settled = new Box();
            yield return SettleEnv("Clear", settled);
            // Standing: fully visible, on the server too.
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Ask(c, StepStealth, "", reply);
            float.TryParse(Field(reply.Detail, "stealth"), NumberStyles.Float, CultureInfo.InvariantCulture, out var standing);
            c.Check(Near(standing, player.m_stealthFactor, 0.03f) && standing > 0.95f, $"standing: the server relays stealth {F(standing)}, this game has {F(player.m_stealthFactor)}");
            // Crouched and still: the low value reaches the server.
            var still = new Box();
            yield return HoldStill(rig, still);
            yield return Settle(player, settled);
            yield return new WaitForSecondsRealtime(2f);
            yield return Ask(c, StepStealth, "", reply);
            float.TryParse(Field(reply.Detail, "stealth"), NumberStyles.Float, CultureInfo.InvariantCulture, out var hidden);
            var expected = WithStill(StealthState.LastWithoutStill, rules);
            c.Check(still.Ok && settled.Ok && Near(player.m_stealthFactor, expected, 0.02f), $"holding still: bar {F(player.m_stealthFactor)}, expected {F(expected)} with the server's rules");
            c.Check(Near(hidden, player.m_stealthFactor, 0.02f), $"holding still: the server relays stealth {F(hidden)}, this game has {F(player.m_stealthFactor)}");
            // What a creature does with that value (any game computes the same from the relayed number).
            var g = rig.Creature("Greydwarf", Ground(origin + dir * 9f), -dir, false);
            if (c.Check(g != null, "could not spawn a Greydwarf"))
            {
                yield return Frames(3);
                var ai = g.GetBaseAI();
                var reach = ai.m_viewRange * hidden;
                c.Check(ai.CanSeeTarget(player) == (9f <= reach) && reach < 9f,
                    $"a Greydwarf 9 m in front (view {F(ai.m_viewRange)} m x relayed stealth {F(hidden)} = {F(reach)} m) sees the still player: {ai.CanSeeTarget(player)}");
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.mp.smoke (M13 a) ----------

    private static IEnumerator RunMpSmoke()
    {
        var c = new Checks(MpSmokeName);
        var rig = Rig.Create(MpSmokeName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            var ready = new Box();
            yield return ClientReady(c, ready);
            if (!ready.Ok)
            {
                c.Report();
                yield break;
            }
            var rules = ServerRules.Current;
            StealthCues.TestShowCues = true;
            Tap.Install();
            yield return Stand(player);
            var dir = ClearDirection(player, 14f, out _);
            var origin = player.transform.position;

            // T15 on the server: hidden inside from a Greydwarf outside.
            var g = rig.Creature("Greydwarf", Ground(origin + dir * 8f), -dir, false);
            if (!c.Check(g != null, "could not spawn a Greydwarf"))
            {
                c.Report();
                yield break;
            }
            yield return Frames(3);
            c.Check(g.m_nview.IsOwner(), "this game does not control the Greydwarf it spawned");
            var ai = g.GetBaseAI();
            player.m_noiseRange = 30f;
            c.Check(ai.CanSeeTarget(player) && ai.CanHearTarget(player), "no cloud: the Greydwarf 8 m away does not see and hear the player (scenery?)");
            var slot = new CloudSlot();
            yield return rig.PutCloud(slot, origin);
            yield return WaitFor(() => slot.Cloud == null || slot.Cloud.IsActive(SmokeRegistry.Now, rules), 4f);
            player.m_noiseRange = 30f;
            c.Check(slot.Cloud != null && InSmoke(player, rules) && !ai.CanSeeTarget(player) && !ai.CanHearTarget(player),
                "player inside the cloud, Greydwarf outside: seen or heard");
            yield return new WaitForSeconds(0.8f);
            c.Check(StealthCues.Has(player, CueKind.Smoke), "no In smoke icon inside the cloud");
            rig.Remove(g.gameObject);
            if (slot.Cloud != null)
            {
                rig.Remove(slot.Cloud.gameObject);
                slot.Cloud = null;
            }
            yield return Frames(3);

            // T19 on the server: two pursuers blinded by the burst, they give up.
            var chasers = new List<MonsterAI>();
            foreach (var angle in new[] { -30f, 30f })
            {
                var p = Ground(origin + Quaternion.Euler(0f, angle, 0f) * dir * 4f);
                var chaser = rig.Creature("Greydwarf", p, origin - p, true);
                if (chaser != null)
                {
                    chasers.Add((MonsterAI)chaser.GetBaseAI());
                }
            }
            if (c.Check(chasers.Count == 2, "could not spawn two pursuers"))
            {
                yield return Frames(3);
                Tap.Clear();
                yield return rig.PutCloud(slot, origin);
                var wait = Time.time;
                while (Time.time - wait < 4f && AiMemory.BlindCount < 2)
                {
                    if (slot.Cloud != null && !slot.Cloud.IsActive(SmokeRegistry.Now, rules))
                    {
                        foreach (var chaser in chasers)
                        {
                            chaser.SetAlerted(true);
                            chaser.m_targetCreature = player;
                        }
                    }
                    yield return null;
                }
                var blindAt = Time.time;
                c.Check(chasers.All(x => AiMemory.IsBlinded(x.transform, SmokeRegistry.Now)), "the burst did not blind both pursuers");
                c.Check(Tap.Logged("Smoke Screen burst: 2 creature(s)") == 1, $"Debug burst line for 2 creatures not logged once ({Tap.FirstLogged("Smoke Screen burst")})");
                var gaveUp = new Box();
                yield return WaitFor(() => chasers.All(x => x.m_targetCreature == null && !x.IsAlerted()), rules.ForgetSeconds + 2f, gaveUp);
                c.Check(gaveUp.Ok, $"the pursuers did not give up within {F(Time.time - blindAt)} s of the burst");
            }
            NoProblems(c);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.mp.server-off (M14) ----------

    private static IEnumerator RunMpServerOff()
    {
        var c = new Checks(MpServerOffName);
        var rig = Rig.Create(MpServerOffName);
        if (rig == null)
        {
            yield break;
        }
        var reply = new SelfTest.ServerReply();
        var knew = true;
        var added = false;
        var serverOff = false;
        try
        {
            var player = rig.Player;
            var ready = new Box();
            yield return ClientReady(c, ready);
            var found = FeatureRegistry.Find(ModInfo.Guid);
            var recipe = SmokeContent.CraftRecipe;
            if (!ready.Ok || !c.Check(found != null && recipe != null, "this mod is not in the registry, or it has no recipe"))
            {
                c.Report();
                yield break;
            }
            var view = found.Value;
            Tap.Install();
            Tap.Hold = true;
            yield return Stand(player);
            var dir = ClearDirection(player, 12f, out _);
            var origin = player.transform.position;
            var item = rig.GiveAndEquip(4);
            var benchGo = rig.Spawn(AmbushRules.DefaultRecipeStation, Ground(origin - dir * 3f), Quaternion.identity);
            var station = benchGo != null ? benchGo.GetComponent<CraftingStation>() : null;
            var g = rig.Creature("Greydwarf", Ground(origin + dir * 10f), -dir, false);
            if (!c.Check(item != null && station != null && g != null, "could not equip a Smoke Screen, or spawn a workbench or a Greydwarf"))
            {
                c.Report();
                yield break;
            }
            knew = player.m_knownRecipes.Contains(SmokeContent.DisplayName);
            player.m_knownRecipes.Add(SmokeContent.DisplayName);
            added = true;
            c.Check(recipe.m_enabled && OfferedAt(player, station, recipe), "before: the workbench does not offer the Smoke Screen");

            // One Smoke Screen on the ground. Count taken two frames after the Smoke Screens were given: the game's
            // Inventory.AddItem(name, ...) make a short-lived copy of the item object (ItemDrop, gone at end of frame)
            // that the drop count see. First run: counted in the same frame, so "one more on the ground" failed (1
            // before = that copy, 1 after = the real one) although the server had the dropped one.
            yield return Frames(2);
            var bag = player.GetInventory();
            var drops = SmokeDrops();
            var one = bag.GetAllItems().FirstOrDefault(SmokeContent.IsSmokeScreen);
            c.Check(one != null && player.DropItem(bag, one, 1), "could not drop a Smoke Screen");
            yield return new WaitForSecondsRealtime(2f);
            var dropped = ItemDrop.s_instances.FirstOrDefault(d => d != null && d.m_itemData != null && SmokeContent.IsSmokeScreen(d.m_itemData));
            c.Check(dropped != null && SmokeDrops() == drops + 1,
                $"the dropped Smoke Screen is not on the ground (Smoke Screens on the ground {drops} -> {SmokeDrops()}, expected one more)");
            yield return Ask(c, StepWorld, "", reply);
            int.TryParse(Field(reply.Detail, "drops"), out var serverDrops);
            c.Check(serverDrops >= 1 && Field(reply.Detail, "active") == "True", $"before: the server does not store the dropped Smoke Screen: {reply.Detail}");
            var stack = rig.SmokeCount();

            // The server owner turns the mod off while the player is in.
            serverOff = true;
            yield return Ask(c, SetEnabledStep, ModInfo.Guid + "=off", reply);
            var follows = new Box();
            yield return WaitFor(() => !view.IsActive, 10f, follows);
            c.Check(follows.Ok && view.State == "ServerMissing" && view.Status.Contains("turned off"), $"server off: client state {view.State}, '{view.Status}'");
            c.Check(!recipe.m_enabled && !OfferedAt(player, station, recipe), "server off: the workbench still offers the Smoke Screen");
            c.Check(rig.GiveAndEquip(0) != null, "server off: could not hold a Smoke Screen");
            rig.Aim(dir, 45f);
            HumanoidPatches.TestResetMessageTimer();
            var told = Tap.Messages(HumanoidPatches.InactiveMessage, MessageHud.MessageType.TopLeft);
            var clouds = SmokeRegistry.Count;
            c.Check(!player.StartAttack(null, false) && Tap.Messages(HumanoidPatches.InactiveMessage, MessageHud.MessageType.TopLeft) == told + 1,
                "server off: the throw was not refused with the message");
            yield return new WaitForSecondsRealtime(1.5f);
            c.Check(rig.SmokeCount() == stack && SmokeRegistry.Count == clouds && !player.InAttack(), $"server off: stack {stack} -> {rig.SmokeCount()}, clouds {clouds} -> {SmokeRegistry.Count}");
            c.Check(dropped != null && SmokeDrops() == drops + 1,
                $"server off: the dropped Smoke Screen went away (Smoke Screens on the ground {SmokeDrops()}, expected {drops + 1})");
            yield return Ask(c, StepWorld, "", reply);
            int.TryParse(Field(reply.Detail, "drops"), out var dropsWhileOff);
            c.Check(dropsWhileOff >= serverDrops && Field(reply.Detail, "prefabsKnown") == "True" && Field(reply.Detail, "active") == "False",
                $"server off: the server lost the dropped Smoke Screen or its prefabs: {reply.Detail}");

            // On again, no restart: throws work and the cloud hides.
            yield return Ask(c, SetEnabledStep, ModInfo.Guid + "=on", reply);
            serverOff = !reply.Ok;
            yield return WaitFor(() => view.IsActive && ServerRules.UsingServer, 12f, follows);
            c.Check(follows.Ok, $"server on again: client state {view.State}, '{view.Status}', server rules in use {ServerRules.UsingServer}");
            var rules = ServerRules.Current;
            c.Check(recipe.m_enabled && OfferedAt(player, station, recipe), "server on again: the workbench does not offer the Smoke Screen");
            c.Check(rig.GiveAndEquip(0) != null, "server on again: could not hold a Smoke Screen");
            rig.Aim(dir, 55f);
            yield return new WaitForSeconds(0.5f);
            var before = new HashSet<SmokeCloud>(SmokeRegistry.All);
            c.Check(player.StartAttack(null, false), "server on again: the throw is refused");
            var slot = new CloudSlot();
            yield return WaitForCloud(before, 5f, slot);
            if (c.Check(slot.Cloud != null && rig.SmokeCount() == stack - 1, $"server on again: no cloud, or stack {stack} -> {rig.SmokeCount()}"))
            {
                var cloud = slot.Cloud;
                yield return WaitFor(() => cloud == null || cloud.IsActive(SmokeRegistry.Now, rules), 4f);
                if (cloud != null)
                {
                    MovePlayer(player, cloud.Base + Vector3.up * 0.1f);
                    rig.MarkMoved();
                    Place(g, g.transform.position, player.transform.position - g.transform.position);
                    yield return Frames(3);
                    var ai = g.GetBaseAI();
                    WithoutSmoke(ai, player, out var wouldSee, out _);
                    c.Check(InSmoke(player, rules) && wouldSee && !ai.CanSeeTarget(player), $"server on again: the cloud does not hide the player (would see without smoke {wouldSee})");
                }
            }
            if (dropped != null)
            {
                player.Pickup(dropped.gameObject, false, false);
            }
            NoProblems(c);
            c.Report();
        }
        finally
        {
            if (serverOff)
            {
                SelfTest.Note(MpServerOffName, "the server's copy of the mod may still be off (the test ended before turning it on again)");
            }
            if (added && !knew && rig.Player != null)
            {
                rig.Player.m_knownRecipes.Remove(SmokeContent.DisplayName);
            }
            foreach (var drop in ItemDrop.s_instances.Where(d => d != null && d.m_itemData != null && SmokeContent.IsSmokeScreen(d.m_itemData)).ToList())
            {
                DestroyObject(drop.gameObject);
            }
            rig.Restore();
        }
    }

    // ---------- sneak.mp.no-server-mod (M08, scenario vanilla-server) ----------

    private static IEnumerator RunMpNoServerMod()
    {
        var c = new Checks(MpNoServerModName);
        var found = FeatureRegistry.Find(ModInfo.Guid);
        if (!c.Check(Connected, "not connected to a server as a client") || !c.Check(found != null, "this mod is not in the registry"))
        {
            c.Report();
            yield break;
        }
        var view = found.Value;
        // What the MC Mods panel shows for this mod.
        c.Check(view.State == "ServerMissing" && view.Status.StartsWith("Inactive: the server does not have this mod.", System.StringComparison.Ordinal),
            $"on a server without the mod: state {view.State}, status '{view.Status}'");
        c.Check(!Plugin.FeatureActive, "the feature counts as active on a server without the mod");
        var rig = Rig.Create(MpNoServerModName);
        if (rig == null)
        {
            yield break;
        }
        var knew = true;
        var added = false;
        try
        {
            var player = rig.Player;
            Tap.Install();
            StealthCues.TestShowCues = true;
            yield return Stand(player);
            var dir = ClearDirection(player, 10f, out _);
            var origin = player.transform.position;
            var recipe = SmokeContent.CraftRecipe;
            var benchGo = rig.Spawn(AmbushRules.DefaultRecipeStation, Ground(origin - dir * 3f), Quaternion.identity);
            var station = benchGo != null ? benchGo.GetComponent<CraftingStation>() : null;
            if (c.Check(recipe != null && station != null, "no Smoke Screen recipe object, or could not spawn a workbench"))
            {
                knew = player.m_knownRecipes.Contains(SmokeContent.DisplayName);
                player.m_knownRecipes.Add(SmokeContent.DisplayName);
                added = true;
                c.Check(!recipe.m_enabled && !OfferedAt(player, station, recipe), "the workbench offers the Smoke Screen on a server without the mod");
            }
            // Smoke Screens stay in the bag; a throw is refused with the message and uses nothing. Hands free first:
            // first run, the test before (another mod's) had begun a punch in its last frame, and the game does not
            // equip during a swing.
            yield return FreeHands(player);
            var item = rig.GiveAndEquip(3);
            if (c.Check(item != null, $"could not hold Smoke Screens on a server without the mod (in the bag {rig.SmokeCount()}; {HandsText(player)})"))
            {
                var stack = rig.SmokeCount();
                var clouds = SmokeRegistry.Count;
                // What a join does with the bag (Player.Load -> Inventory.Load, item objects made from their names):
                // every Smoke Screen come through, although the mod is inactive here.
                var saved = new ZPackage();
                player.GetInventory().Save(saved);
                saved.SetPos(0);
                var loaded = new Inventory("selftest", null, player.GetInventory().GetWidth(), player.GetInventory().GetHeight());
                loaded.Load(saved);
                c.Check(loaded.CountItems(SmokeContent.DisplayName, -1, false) == stack && stack >= 3,
                    $"Smoke Screens after the bag is saved and loaded as a join does: {loaded.CountItems(SmokeContent.DisplayName, -1, false)} of {stack}");
                rig.Aim(dir, 45f);
                yield return new WaitForSeconds(0.5f);
                HumanoidPatches.TestResetMessageTimer();
                Tap.Clear();
                c.Check(!player.StartAttack(null, false), "the throw was allowed on a server without the mod");
                c.Check(Tap.Messages(HumanoidPatches.InactiveMessage, MessageHud.MessageType.TopLeft) == 1, "the refused throw showed no message");
                c.Check(Tap.Messages(RefusedThrowText, MessageHud.MessageType.TopLeft) == 1,
                    $"the refused throw's message is not the text TESTING.md names: '{Tap.MessageTexts("Smoke Screen")}'");
                yield return new WaitForSecondsRealtime(2f);
                c.Check(rig.SmokeCount() == stack && stack >= 3 && !player.InAttack(), $"Smoke Screens in the bag: {stack} -> {rig.SmokeCount()} after the refused throw");
                c.Check(SmokeRegistry.Count == clouds && ZNetScene.instance.GetPrefab(SmokeContent.ItemName) == SmokeContent.ItemPrefab,
                    "a cloud appeared, or the Smoke Screen is no longer a known item here");
            }
            // The rest of the mod is off too: no icon, normal bar.
            rig.SetSneak(0f);
            rig.TakeControls();
            var crouched = new Box();
            yield return Crouch(player, crouched);
            yield return new WaitForSeconds(2.5f);
            yield return ForceRefresh(player);
            c.Check(crouched.Ok && !AnyCue(player) && !StealthState.StillActive, "crouched and still on a server without the mod: a stealth icon or the still bonus is on");
            c.Check(Near(player.m_stealthFactorTarget, VanillaTarget(player), 0.01f), $"bar target {F(player.m_stealthFactorTarget)}, the normal game's {F(VanillaTarget(player))}");
            NoProblems(c);
            c.Report();
        }
        finally
        {
            if (added && !knew && rig.Player != null)
            {
                rig.Player.m_knownRecipes.Remove(SmokeContent.DisplayName);
            }
            rig.Restore();
        }
    }
}
#endif
