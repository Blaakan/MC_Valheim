#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using MC.Exploration.SailingSkillMod.Patches;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.SailingSkillMod;

// Debug build only. Me = multiplayer self tests (tools/Test-Multiplayer.ps1): one client joined to real dedicated server.
// Scenario "modded" (server and client run the mod):
//   sailing.mp.rules   server state (dedicated, no icon, no warning or error since start), server setting XpPerKm
//                      changed live reaches the client (rules, Info line, own config file untouched), XP at the helm
//                      paid at the server's rate
//   sailing.mp.helm    ship the SERVER simulates (a dedicated server keeps no object by itself: the server step holds
//                      the server's reference place on its ship, Debug only, so it stands in for a passenger's game),
//                      client boards and takes the helm the vanilla way: server reads the helmsman's published level
//                      (0 = no bonus, 100 = bonuses, Debug line), client's rudder swings faster and reaches the
//                      server, helmsman earns XP on a ship the server moves, passenger does not, simulation handed to
//                      the client = bonuses on its game
//   sailing.mp.crew    same ship, client on deck (nobody at the helm): best sailor aboard halves a hit on the server,
//                      full again once the client left; own map reveal on a ship another game simulates
//   sailing.mp.join    server join check on the live player: allowed with the mod; with AllowPlayersWithoutMod forced
//                      on, the server names a player without the mod / with it off / with another network version;
//                      mod turned off and on again within the grace: the player stays
// Scenario "vanilla-server" (server without the mod, registered at plugin start because the feature is off there):
//   sailing.mp.vanilla-server  status text, no handling bonus, no XP, normal map reveal and damage, skill still shown
// Server half = one step "sailing.mp.server", first word of its argument picks the job; answers are
// "key=value ## key=value".
internal static partial class SelfTests
{
    private const string MpRulesName = "sailing.mp.rules";
    private const string MpHelmName = "sailing.mp.helm";
    private const string MpCrewName = "sailing.mp.crew";
    private const string MpJoinName = "sailing.mp.join";
    private const string MpVanillaServerName = "sailing.mp.vanilla-server";
    private const string VanillaServerScenario = "vanilla-server";   // tests/Probes/MpProtocol.cs
    private const string ServerStep = "sailing.mp.server";
    private const string Sep = " ## ";

    private static void RegisterAlwaysMultiplayer()
    {
        SelfTest.RegisterMultiplayer(MpVanillaServerName, VanillaServerScenario, RunMpVanillaServer);
    }

    private static void RegisterMultiplayer()
    {
        SelfTest.RegisterMultiplayer(MpRulesName, SelfTest.Modded, RunMpRules);
        SelfTest.RegisterMultiplayer(MpHelmName, SelfTest.Modded, RunMpHelm);
        SelfTest.RegisterMultiplayer(MpCrewName, SelfTest.Modded, RunMpCrew);
        SelfTest.RegisterMultiplayer(MpJoinName, SelfTest.Modded, RunMpJoin);   // last: it turns the mod off and on
        SelfTest.RegisterServerStep(ServerStep, RunServerStep);
    }

    private static void UnregisterMultiplayer()
    {
        SelfTest.UnregisterMultiplayer(MpRulesName);
        SelfTest.UnregisterMultiplayer(MpHelmName);
        SelfTest.UnregisterMultiplayer(MpCrewName);
        SelfTest.UnregisterMultiplayer(MpJoinName);
        SelfTest.UnregisterServerStep(ServerStep);
    }

    // ---------- words on the wire ----------

    private static string N(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static float Num(Dictionary<string, string> kv, string key) =>
        kv.TryGetValue(key, out var text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : float.NaN;

    private static string Txt(Dictionary<string, string> kv, string key) => kv.TryGetValue(key, out var text) ? text : "";

    private static bool Yes(Dictionary<string, string> kv, string key) => Txt(kv, key) == "True";

    private static Dictionary<string, string> Parse(string detail)
    {
        var result = new Dictionary<string, string>();
        foreach (var part in (detail ?? "").Split(new[] { Sep }, StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq > 0)
            {
                result[part.Substring(0, eq).Trim()] = part.Substring(eq + 1);
            }
        }
        return result;
    }

    private sealed class Call
    {
        internal bool Ok;
        internal string Raw = "";
        internal Dictionary<string, string> Kv = new Dictionary<string, string>();
    }

    private static IEnumerator Server(string arg, Call call)
    {
        var reply = new SelfTest.ServerReply();
        yield return SelfTest.CallServer(ServerStep, arg, reply);
        call.Ok = reply.Answered && reply.Ok;
        call.Raw = reply.ToString();
        call.Kv = Parse(reply.Detail);
    }

    private static bool Connected => ZNet.instance != null && !ZNet.instance.IsServer()
                                     && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected && Player.m_localPlayer != null;

    // ---------- server half ----------

    private static GameObject _serverShip;
    private static int _serverMark;

    private static Player ServerPlayer(long uid)
    {
        foreach (var p in Player.GetAllPlayers())
        {
            if (p != null && p.GetOwner() == uid)
            {
                return p;
            }
        }
        return null;
    }

    private static Ship ServerShip => _serverShip != null ? _serverShip.GetComponent<Ship>() : null;

    private static IEnumerator RunServerStep(string arg, object[] reply)
    {
        var parts = (arg ?? "").Split('|');
        var uid = 0L;
        if (parts.Length > 1)
        {
            long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uid);
        }
        switch (parts[0])
        {
            case "info":
                ServerInfo(reply);
                break;
            case "xp":
                yield return ServerXp(parts, reply);
                break;
            case "spawn":
                yield return ServerSpawn(parts, reply);
                break;
            case "destroy":
                ServerDestroy();
                SelfTest.Answer(reply, true, "gone=True");
                break;
            case "crew":
                yield return ServerCrew(uid, reply);
                break;
            case "helm":
                yield return ServerHelm(uid, parts, reply);
                break;
            case "hit":
                yield return ServerHit(uid, parts, reply);
                break;
            case "rudder":
                ServerRudder(reply);
                break;
            case "move":
                yield return ServerMove(parts, reply);
                break;
            case "owner":
                ServerOwner(uid, reply);
                break;
            case "mark":
                _serverMark = LogMark();
                SelfTest.Answer(reply, true, "mark=" + _serverMark);
                break;
            case "check":
                yield return ServerCheck(uid, parts, reply);
                break;
            case "verdict":
                ServerVerdict(uid, reply);
                break;
            default:
                SelfTest.Answer(reply, false, $"unknown job '{parts[0]}'");
                break;
        }
    }

    private static void ServerInfo(object[] reply)
    {
        var net = ZNet.instance;
        var view = FeatureRegistry.Find(ModInfo.Guid);
        var skills = 0;
        // No player on a dedicated server: the definition itself is all there is to look at.
        SelfTest.Answer(reply, true, string.Join(Sep, new[]
        {
            "dedicated=" + (net != null && net.IsDedicated()),
            "server=" + (net != null && net.IsServer()),
            "graphics=" + SystemInfo.graphicsDeviceType,
            "icon=" + (SailingSkill.Def.m_icon != null),
            "active=" + (view != null && view.Value.IsActive),
            "status=" + (view != null ? view.Value.Status : "not registered"),
            "problems=" + (_watch != null ? _watch.MyProblemCount : -1),
            "first=" + (_watch != null ? Join(_watch.MyProblems()) : "no watcher"),
            "xp=" + N(Plugin.XpPerKm.Value),
            "rules=" + ServerRules.Current.Describe(),
            "pending=" + ServerRules.IsPending,
            "players=" + Player.GetAllPlayers().Count,
            "skills=" + skills,
        }));
    }

    // Server owner changes XpPerKm in the server's (throwaway) config while players are in: real setting, real
    // SettingChanged, real push after the debounce.
    private static IEnumerator ServerXp(string[] parts, object[] reply)
    {
        if (parts.Length < 2 || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            SelfTest.Answer(reply, false, "no value");
            yield break;
        }
        Plugin.XpPerKm.Value = value;
        var until = Time.realtimeSinceStartup + 4f;
        yield return null;
        while (ServerRules.PushPending && Time.realtimeSinceStartup < until)
        {
            yield return null;
        }
        SelfTest.Answer(reply, !ServerRules.PushPending && Near(ServerRules.Current.XpPerKm, value),
            string.Join(Sep, new[] { "xp=" + N(Plugin.XpPerKm.Value), "pushed=" + !ServerRules.PushPending, "rules=" + ServerRules.Current.Describe() }));
    }

    private static void ServerDestroy()
    {
        if (_serverShip != null && ZNetScene.instance != null)
        {
            ZNetScene.instance.Destroy(_serverShip);
        }
        _serverShip = null;
        ReleaseReference();
    }

    // Server's reference place (centre of the area this game makes and keeps objects in) held on the server's ship
    // while that ship is there. First multiplayer run (2026-10-07): the ship me made 30 m above the world centre was
    // gone two frames later, with nothing in the log. Vanilla ZNetScene.RemoveObjects destroys every object outside
    // the area around the reference place, so the likely cause: the server's area was not where the test thought
    // (the answer of that run did not say where it was; this one does: refWas, wasNear). A dedicated server has no
    // player to move that place; a host or a passenger's game has it on its player. Put back when the ship goes
    // (ServerDestroy, the watch below) or after HoldSeconds.
    private const float HoldSeconds = 150f;
    private static bool _refHeld;
    private static Vector3 _refWas;
    private static Vector3 _refAt;

    private static bool HoldReference(Vector3 at)
    {
        var net = ZNet.instance;
        if (net == null || !net.IsServer() || Player.m_localPlayer != null)
        {
            return false;
        }
        if (!_refHeld)
        {
            _refWas = net.GetReferencePosition();
            _refHeld = true;
        }
        _refAt = at;
        net.SetReferencePosition(at);
        return true;
    }

    private static void ReleaseReference()
    {
        if (!_refHeld)
        {
            return;
        }
        _refHeld = false;
        if (ZNet.instance != null)
        {
            ZNet.instance.SetReferencePosition(_refWas);
        }
    }

    // Runs on the plugin, not on the step: keeps the place held every physics step (after the game's own
    // Game.FixedUpdate, which may set it, and before ZNetScene.Update reads it) until this ship is gone.
    private static IEnumerator HoldWhileShip(GameObject ship)
    {
        var until = Time.realtimeSinceStartup + HoldSeconds;
        var step = new WaitForFixedUpdate();
        while (_refHeld && ship != null && ReferenceEquals(_serverShip, ship) && Time.realtimeSinceStartup < until)
        {
            if (ZNet.instance == null)
            {
                break;
            }
            ZNet.instance.SetReferencePosition(_refAt);
            yield return step;
        }
        // Ship gone or time over: let go, unless a newer ship holds the place by now (it has its own watch).
        if (_serverShip == null || ReferenceEquals(_serverShip, ship))
        {
            ReleaseReference();
        }
    }

    private static string P(Vector3 v) => N(v.x) + "," + N(v.y) + "," + N(v.z);

    // Server make own ship, held in the air (kinematic on the server, the game that simulate it). Answer only after
    // the ship stayed half a second and 15 frames; every answer says where the server's area was and is.
    private static IEnumerator ServerSpawn(string[] parts, object[] reply)
    {
        ServerDestroy();
        var prefab = ShipPrefab();
        if (parts.Length < 4 || prefab == null
            || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            || !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
        {
            SelfTest.Answer(reply, false, "no ship prefab or no position");
            yield break;
        }
        var position = new Vector3(x, y, z);
        var net = ZNet.instance;
        var refWas = net != null ? net.GetReferencePosition() : Vector3.zero;
        var wasNear = net != null && ZNetScene.InActiveArea(position, refWas);
        var held = HoldReference(position);
        var host = Chainloader.PluginInfos.TryGetValue(ModInfo.Guid, out var info) ? info.Instance : null;
        _serverShip = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
        var made = _serverShip;
        var ship = made.GetComponent<Ship>();
        if (ship != null && ship.m_body != null)
        {
            ship.m_body.isKinematic = true;
        }
        var view = made.GetComponent<ZNetView>();
        var zdo = view != null ? view.GetZDO() : null;
        if (held && host != null)
        {
            host.StartCoroutine(HoldWhileShip(made));
        }
        _serverMark = LogMark();
        var frames = 0;
        var t0 = Time.realtimeSinceStartup;
        while (made != null && view != null && view.GetZDO() != null && (frames < 15 || Time.realtimeSinceStartup - t0 < 0.5f))
        {
            yield return null;
            frames++;
        }
        var alive = made != null && ship != null && view != null && view.GetZDO() != null && zdo != null;
        var area = string.Join(Sep, new[]
        {
            "frames=" + frames.ToString(CultureInfo.InvariantCulture),
            "object=" + (made != null),
            "zdo=" + (zdo != null && zdo.IsValid()),
            "refWas=" + P(refWas),
            "wasNear=" + wasNear,
            "held=" + held,
            "watch=" + (host != null),
            "ref=" + (net != null ? P(net.GetReferencePosition()) : "none"),
            "near=" + (net != null && ZNetScene.InActiveArea(position, net.GetReferencePosition())),
            "loaded=" + (ZoneSystem.instance != null && ZoneSystem.instance.IsActiveAreaLoaded()),
            "players=" + Player.GetAllPlayers().Count.ToString(CultureInfo.InvariantCulture),
        });
        if (!alive)
        {
            // Object gone: its saved record must not stay in the world as a ship nobody simulates.
            if (zdo != null && zdo.IsValid() && ZDOMan.instance != null)
            {
                zdo.SetOwner(ZDOMan.GetSessionID());
                ZDOMan.instance.DestroyZDO(zdo);
            }
            _serverShip = null;
            ReleaseReference();
            SelfTest.Answer(reply, false, "the ship did not spawn on the server" + Sep + area);
            yield break;
        }
        SelfTest.Answer(reply, true, string.Join(Sep, new[]
        {
            "user=" + zdo.m_uid.UserID.ToString(CultureInfo.InvariantCulture),
            "id=" + zdo.m_uid.ID.ToString(CultureInfo.InvariantCulture),
            "owner=" + zdo.IsOwner(),
            "name=" + Utils.GetPrefabName(_serverShip),
            "health=" + N(_serverShip.GetComponent<WearNTear>() != null ? _serverShip.GetComponent<WearNTear>().m_health : -1f),
            area,
        }));
    }

    private static IEnumerator ServerCrew(long uid, object[] reply)
    {
        var ship = ServerShip;
        // Server makes its copy of the player only once the zones around its reference place are there (a few per
        // second when the place was just moved).
        var until = Time.realtimeSinceStartup + 25f;
        Player player = null;
        while (ship != null && Time.realtimeSinceStartup < until)
        {
            player = ServerPlayer(uid);
            if (player != null && ship.IsPlayerInBoat(player))
            {
                break;
            }
            yield return null;
        }
        var aboard = ship != null && player != null && ship.IsPlayerInBoat(player);
        var net = ZNet.instance;
        SelfTest.Answer(reply, aboard, string.Join(Sep, new[]
        {
            "aboard=" + aboard, "players=" + (ship != null ? ship.m_players.Count : -1), "found=" + (player != null),
            "name=" + (player != null ? player.GetPlayerName() : ""),
            "ship=" + (ship != null), "copies=" + Player.GetAllPlayers().Count.ToString(CultureInfo.InvariantCulture),
            "ref=" + (net != null ? P(net.GetReferencePosition()) : "none"), "held=" + _refHeld,
            "loaded=" + (ZoneSystem.instance != null && ZoneSystem.instance.IsActiveAreaLoaded()),
        }));
    }

    // Who server see at helm of its ship, which level it read, and what its physics step make of it.
    private static IEnumerator ServerHelm(long uid, string[] parts, object[] reply)
    {
        var ship = ServerShip;
        var want = parts.Length > 2 && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : -1f;
        var until = Time.realtimeSinceStartup + 10f;
        Player player = null;
        var ready = false;
        while (ship != null && Time.realtimeSinceStartup < until && !ready)
        {
            player = ServerPlayer(uid);
            ready = player != null && ReferenceEquals(HelmSkill.Helmsman(ship), player) && Near(HelmSkill.Published(player), want, 0.01f)
                    && Near(HelmSkill.Physics01(ship), Mathf.Clamp01(want / 100f), 0.001f);
            if (!ready)
            {
                yield return null;
            }
        }
        if (ship == null)
        {
            SelfTest.Answer(reply, false, "no ship on the server");
            yield break;
        }
        ShipFixedUpdatePatches.LastShip = null;
        yield return FixedSteps(4);
        var helmsman = HelmSkill.Helmsman(ship);
        var name = Utils.GetPrefabName(ship.gameObject);
        var line = helmsman != null ? $"{name}: {helmsman.GetPlayerName()} at the helm, Sailing {want:0} (this game simulates the ship)." : "";
        SelfTest.Answer(reply, ready, string.Join(Sep, new[]
        {
            "helmsman=" + (helmsman != null ? helmsman.GetPlayerName() : ""),
            "is=" + (player != null && ReferenceEquals(helmsman, player)),
            "published=" + N(player != null ? HelmSkill.Published(player) : -2f),
            "s=" + N(HelmSkill.Physics01(ship)),
            "stepped=" + ReferenceEquals(ShipFixedUpdatePatches.LastShip, ship),
            "accel=" + N(ShipFixedUpdatePatches.LastAccel),
            "turn=" + N(ShipFixedUpdatePatches.LastTurn),
            "owner=" + ship.IsOwner(),
            "line=" + (line.Length > 0 && LoggedExact(_serverMark, LogLevel.Debug, line)),
            "lines=" + Join(MyLines(_serverMark, LogLevel.Debug).Where(l => l.Contains("at the helm")).ToList()),
        }));
    }

    // 10 blunt creature hit on the server's ship through the vanilla owner path, once the server sees the player
    // aboard (or gone) and reads the level it published.
    private static IEnumerator ServerHit(long uid, string[] parts, object[] reply)
    {
        var ship = ServerShip;
        var wear = _serverShip != null ? _serverShip.GetComponent<WearNTear>() : null;
        var want = parts.Length > 2 && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : -1f;
        var wantAboard = parts.Length > 3 && parts[3] == "1";
        if (ship == null || wear == null || !ship.IsOwner())
        {
            SelfTest.Answer(reply, false, "no ship of the server's own");
            yield break;
        }
        var until = Time.realtimeSinceStartup + 10f;
        Player player = null;
        var ready = false;
        while (Time.realtimeSinceStartup < until && !ready)
        {
            player = ServerPlayer(uid);
            ready = player != null && ship.IsPlayerInBoat(player) == wantAboard && Near(HelmSkill.Published(player), want, 0.01f);
            if (!ready)
            {
                yield return null;
            }
        }
        var zdo = wear.m_nview.GetZDO();
        zdo.Set(ZDOVars.s_health, wear.m_health);
        var mark = LogMark();
        var best = HelmSkill.Damage01(ship);
        var hit = new HitData
        {
            m_hitType = HitData.HitType.EnemyHit,
            m_point = ship.transform.position,
            m_dir = Vector3.up,
            m_toolTier = 100,
        };
        hit.m_damage.m_blunt = 10f;
        wear.RPC_Damage(0L, hit);
        var drop = wear.m_health - zdo.GetFloat(ZDOVars.s_health, wear.m_health);
        zdo.Set(ZDOVars.s_health, wear.m_health);
        var lines = MyLines(mark, LogLevel.Debug).Where(l => l.Contains("best Sailing aboard")).ToList();
        SelfTest.Answer(reply, ready, string.Join(Sep, new[]
        {
            "drop=" + N(drop), "best=" + N(best), "aboard=" + (player != null && ship.IsPlayerInBoat(player)),
            "published=" + N(player != null ? HelmSkill.Published(player) : -2f), "lines=" + lines.Count,
            "line=" + (lines.Count > 0 ? lines[0] : ""), "name=" + Utils.GetPrefabName(ship.gameObject),
        }));
    }

    private static void ServerRudder(object[] reply)
    {
        var ship = ServerShip;
        if (ship == null)
        {
            SelfTest.Answer(reply, false, "no ship on the server");
            return;
        }
        SelfTest.Answer(reply, true, string.Join(Sep, new[]
        {
            "rudder=" + N(ship.m_rudderValue), "zdo=" + N(ship.m_nview.GetZDO().GetFloat(ZDOVars.s_rudder)), "owner=" + ship.IsOwner(),
        }));
    }

    private static IEnumerator ServerMove(string[] parts, object[] reply)
    {
        var ship = ServerShip;
        if (ship == null || parts.Length < 3
            || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var dx)
            || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var dz))
        {
            SelfTest.Answer(reply, false, "no ship on the server or no offset");
            yield break;
        }
        MoveShip(ship, ship.transform.position + new Vector3(dx, 0f, dz));
        yield return null;
        SelfTest.Answer(reply, ship.IsOwner(), "x=" + N(ship.transform.position.x) + Sep + "z=" + N(ship.transform.position.z) + Sep + "owner=" + ship.IsOwner());
    }

    // What vanilla Ship.UpdateOwner does on a passenger's game when that passenger leaves: the ship goes to a player
    // still aboard.
    private static void ServerOwner(long uid, object[] reply)
    {
        var ship = ServerShip;
        var zdo = ship != null && ship.m_nview != null ? ship.m_nview.GetZDO() : null;
        if (zdo == null || uid == 0L)
        {
            SelfTest.Answer(reply, false, "no ship on the server or no player id");
            return;
        }
        zdo.SetOwner(uid);
        SelfTest.Answer(reply, zdo.GetOwner() == uid, "owner=" + zdo.GetOwner().ToString(CultureInfo.InvariantCulture));
    }

    private static string PeerName(ZNetPeer peer)
    {
        var name = string.IsNullOrEmpty(peer.m_playerName) ? "A player" : peer.m_playerName;
        var host = peer.m_socket != null ? peer.m_socket.GetHostName() : "";
        return string.IsNullOrEmpty(host) ? name : $"{name} ({host})";
    }

    // The server's join check run again on the live player.
    //   recheck: as it is (with the mod, on): Debug "... allowed.".
    //   nomod / off / version: what the server knows about the player's copy is bent for one check (framework hello
    //   record of this mod), with AllowPlayersWithoutMod forced on so nobody is thrown out: the Warning names the
    //   reason. Then all put back and checked once more: allowed again.
    private static IEnumerator ServerCheck(long uid, string[] parts, object[] reply)
    {
        var net = ZNet.instance;
        var peer = net != null ? net.GetPeer(uid) : null;
        var mode = parts.Length > 2 ? parts[2] : "";
        if (peer == null || peer.m_rpc == null)
        {
            SelfTest.Answer(reply, false, "the player is not connected to this server");
            yield break;
        }
        var who = PeerName(peer);
        var allowedLine = $"{who} has {ModInfo.Name} on, with the same network version: allowed.";
        var warning = "";
        if (mode != "recheck")
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
            var hellos = typeof(NetworkGate).GetField("ClientHellos", flags)?.GetValue(null) as Dictionary<ZRpc, string>;
            var readies = typeof(NetworkGate).GetField("ClientReady", flags)?.GetValue(null) as Dictionary<ZRpc, bool>;
            if (hellos == null || readies == null || !hellos.TryGetValue(peer.m_rpc, out var hello))
            {
                SelfTest.Answer(reply, false, "the framework's record of the player's copy was not found");
                yield break;
            }
            var hadReady = readies.TryGetValue(peer.m_rpc, out var wasReady);
            var mark = LogMark();
            try
            {
                PlayerCheck.TestAllowWithoutMod = true;
                switch (mode)
                {
                    case "nomod":
                        hellos.Remove(peer.m_rpc);
                        break;
                    case "off":
                        readies[peer.m_rpc] = false;
                        break;
                    case "version":
                        hellos[peer.m_rpc] = $"1|{ModInfo.NetworkVersion + 1}|9.9.9|on";
                        break;
                    default:
                        SelfTest.Answer(reply, false, $"unknown check '{mode}'");
                        yield break;
                }
                PlayerCheck.Schedule(peer);
                yield return new WaitForSecondsRealtime(PlayerCheck.GraceSeconds + 0.8f);
            }
            finally
            {
                // The true record first, the forced setting after: the player must never look refusable.
                hellos[peer.m_rpc] = hello;
                if (hadReady)
                {
                    readies[peer.m_rpc] = wasReady;
                }
                else
                {
                    readies.Remove(peer.m_rpc);
                }
                PlayerCheck.TestAllowWithoutMod = null;
            }
            warning = MyLines(mark, LogLevel.Warning).FirstOrDefault(l => l.StartsWith(who, StringComparison.Ordinal)) ?? "";
        }
        var again = LogMark();
        PlayerCheck.Schedule(peer);
        yield return new WaitForSecondsRealtime(PlayerCheck.GraceSeconds + 0.8f);
        var allowed = LoggedExact(again, LogLevel.Debug, allowedLine);
        var refused = MyLines(again, LogLevel.Warning).Count(l => l.StartsWith("Refused ", StringComparison.Ordinal));
        var kicked = ZNet.PeersToDisconnectAfterKick.ContainsKey(peer);
        SelfTest.Answer(reply, allowed && refused == 0 && !kicked, string.Join(Sep, new[]
        {
            "who=" + who, "warning=" + warning, "allowed=" + allowed, "refused=" + refused, "kicked=" + kicked,
            "compatible=" + NetworkGate.PeerCompatible(peer),
        }));
    }

    // What server made of the player since "mark".
    private static void ServerVerdict(long uid, object[] reply)
    {
        var net = ZNet.instance;
        var peer = net != null ? net.GetPeer(uid) : null;
        if (peer == null)
        {
            SelfTest.Answer(reply, false, "the player is no longer connected to this server");
            return;
        }
        var who = PeerName(peer);
        var info = MyLines(_serverMark, LogLevel.Info);
        var warnings = MyLines(_serverMark, LogLevel.Warning);
        SelfTest.Answer(reply, true, string.Join(Sep, new[]
        {
            "off=" + info.Count(l => l.Contains($"turned {ModInfo.Name} off on their game")),
            "on=" + info.Count(l => l.Contains($"turned {ModInfo.Name} on on their game")),
            "allowed=" + MyLines(_serverMark, LogLevel.Debug).Count(l => l == $"{who} has {ModInfo.Name} on, with the same network version: allowed."),
            "refused=" + warnings.Count(l => l.StartsWith("Refused ", StringComparison.Ordinal)),
            "kicked=" + ZNet.PeersToDisconnectAfterKick.ContainsKey(peer),
            "compatible=" + NetworkGate.PeerCompatible(peer),
            "warnings=" + Join(warnings),
        }));
    }

    // ---------- sailing.mp.rules ----------

    private static string ConfigLine(string path, string key)
    {
        try
        {
            foreach (var line in File.ReadAllLines(path))
            {
                if (line.StartsWith(key + " =", StringComparison.Ordinal))
                {
                    return line;
                }
            }
        }
        catch (Exception)
        {
            // Same answer as "no such line".
        }
        return "";
    }

    // Whole file as text. Null = could not read (then the caller's check fail: no proof).
    private static string FileText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IEnumerator RunMpRules()
    {
        var c = new Checks(MpRulesName);
        var watch = LogMark();
        var player = Player.m_localPlayer;
        var rig = ShipRig.Create(MpRulesName);
        if (rig == null)
        {
            yield break;
        }
        var changed = false;
        var call = new Call();
        try
        {
            yield return Server("info", call);
            if (!c.Check(call.Ok, "the server did not answer: " + call.Raw))
            {
                c.Report();
                yield break;
            }
            var info = call.Kv;
            // The dedicated server itself.
            c.Check(Yes(info, "dedicated") && Yes(info, "active") && Txt(info, "status") == "Active.",
                $"the server is not a dedicated server with the mod active (dedicated {Txt(info, "dedicated")}, status '{Txt(info, "status")}')");
            c.Check(Txt(info, "graphics") == "Null" && !Yes(info, "icon"),
                $"the server drew the skill icon, or is not headless (graphics {Txt(info, "graphics")}, icon {Txt(info, "icon")})");
            c.Check(Txt(info, "problems") == "0",
                $"the server logged {Txt(info, "problems")} warning(s) or error(s) from {ModInfo.Name} since it started: {Txt(info, "first")}");
            c.Note("server rules: " + Txt(info, "rules"));

            // The server's rules are in force here.
            var serverXp = Num(info, "xp");
            var ownXp = Plugin.XpPerKm.Value;
            var path = Plugin.XpPerKm.ConfigFile.ConfigFilePath;
            var ownLine = ConfigLine(path, "XpPerKm");
            var ownFile = FileText(path);
            c.Check(ServerRules.UsingServer && !ServerRules.IsPending && ServerRules.Current.Describe() == Txt(info, "rules"),
                $"the client does not use the server's rules (using server {ServerRules.UsingServer}: {ServerRules.Current.Describe()})");

            // Server owner sets XpPerKm while the player is in.
            var target = Near(serverXp, 100f) ? 80f : 100f;
            var mark = LogMark();
            changed = true;
            yield return Server("xp|" + N(target), call);
            c.Check(call.Ok, "the server did not take the new XpPerKm: " + call.Raw);
            var arrived = new Box();
            yield return WaitFor(() => ServerRules.UsingServer && Near(ServerRules.Current.XpPerKm, target), 6f, arrived);
            c.Check(arrived.Ok, $"the server's XpPerKm = {F(target)} did not reach the client (rules here: {ServerRules.Current.Describe()})");
            var line = $"Using the server's rules: {ServerRules.Current.Describe()}. Your own settings apply again in single player and when you host.";
            c.Check(LoggedExact(mark, LogLevel.Info, line)
                    && line.StartsWith($"Using the server's rules: {F(target)} XP per km at the helm; ", StringComparison.Ordinal),
                $"no Info line '{line}' (Info lines: {Join(MyLines(mark, LogLevel.Info))})");
            c.Check(Plugin.XpPerKm.Value == ownXp && ConfigLine(path, "XpPerKm") == ownLine && ownLine.Length > 0,
                $"the player's own setting changed: XpPerKm {F(Plugin.XpPerKm.Value)} (was {F(ownXp)}), config line '{ConfigLine(path, "XpPerKm")}' (was '{ownLine}')");
            // Test list M01: "their own config files are unchanged" = the whole file, not only that line.
            c.Check(ownFile != null && FileText(path) == ownFile, "the player's own config file changed (or could not be read) while the server's rules came in");

            // Sailing by the server's number: 20 m at the helm of an own ship pays the server's rate.
            var ok = new Box();
            yield return SetUpAirShip(rig, c, ok);
            if (ok.Ok)
            {
                HelmSkill.TestLocalLevel = null;
                rig.BackupSkills();
                var sailing = SetSailing(player, 50f, 0f);
                var multiplier = 1f;
                player.GetSEMan().ModifyRaiseSkill(SailingSkill.Type, ref multiplier);
                var gain = new Gain();
                yield return SampleXp(rig.Boat, sailing, 20f, gain);
                var expected = 20f / 1000f * target * multiplier * Game.m_skillGainRate;
                c.Check(gain.Sampled && Near(gain.Xp, expected, expected * 0.03f),
                    $"20 m at the helm gave {F(gain.Xp)} XP, expected {F(expected)} (the server's {F(target)} per km, own setting {F(ownXp)})");
                rig.RestoreSkills();
                yield return rig.Leave(rig.Origin);
            }

            // And back.
            mark = LogMark();
            yield return Server("xp|" + N(serverXp), call);
            changed = !call.Ok;
            var back = new Box();
            yield return WaitFor(() => ServerRules.UsingServer && Near(ServerRules.Current.XpPerKm, serverXp), 6f, back);
            c.Check(call.Ok && back.Ok && CountLogged(mark, LogLevel.Info, $"Using the server's rules: {F(serverXp)} XP per km at the helm; ") == 1,
                $"the server's XpPerKm put back to {F(serverXp)} did not reach the client once (rules here: {ServerRules.Current.Describe()})");
            CheckQuiet(c, watch);
            c.Report();
        }
        finally
        {
            if (changed)
            {
                Log.Info("sailing.mp.rules ended early: the test server keeps the XpPerKm the test set.");
            }
            rig.Restore();
        }
    }

    // ---------- ship the server simulates ----------

    private sealed class MpShip
    {
        internal bool Ok;
        internal bool Away;
        internal bool Asked;      // server was asked for a ship: it holds its reference place until the ship goes
        internal GameObject Go;
        internal Ship Boat;
        internal ZDO Zdo;
        internal string Name = "";
        internal Vector3 Home;
        internal Quaternion HomeRotation;
        internal Vector3 Ground;   // where the player stood under the ship before boarding
    }

    // Dedicated server by itself keeps NO object: its reference place is far outside the world (runs of 2026-10-08:
    // "refWas=1000000,0,1000000"). Only because the server step holds that place on its ship (HoldReference, Debug
    // only) does the server build the area, keep the ship as its own and get a copy of the player ("crew" answer:
    // copies=1, aboard=True): that makes it a stand-in for a passenger's game that simulates the ship. Server spawns
    // a ship 30 m above the player, ship shows up here, player put on its deck, server sees him aboard.
    private static IEnumerator MpBoard(Checks c, Player player, MpShip s)
    {
        s.Ok = false;
        s.Home = player.transform.position;
        s.HomeRotation = player.transform.rotation;
        // Old belief (server's own area = around the world centre) was wrong, see above; the move to the centre is
        // not needed any more but both ways ran fine (2026-10-08), so it stays as it ran.
        if (Flat(s.Home).magnitude > 60f)
        {
            var height = Mathf.Max(WorldGenerator.instance.GetHeight(0f, 0f), ZoneSystem.instance.m_waterLevel) + 2f;
            s.Away = true;
            var there = new Box();
            yield return TeleportAndWait(player, new Vector3(0f, height, 0f), 40f, there, fast: true);
            if (!c.Check(there.Ok, "could not bring the player to the world centre (where the dedicated server simulates)"))
            {
                yield break;
            }
        }
        s.Ground = player.transform.position;
        var at = s.Ground + Vector3.up * 30f;
        var call = new Call();
        s.Asked = true;
        yield return Server($"spawn|{N(at.x)}|{N(at.y)}|{N(at.z)}", call);
        if (!c.Check(call.Ok && Yes(call.Kv, "owner") && Yes(call.Kv, "near"),
                "the server did not spawn a ship of its own inside the area it simulates: " + call.Raw))
        {
            yield break;
        }
        s.Name = Txt(call.Kv, "name");
        if (!long.TryParse(Txt(call.Kv, "user"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var user)
            || !uint.TryParse(Txt(call.Kv, "id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            c.Check(false, "the server's answer has no ship id: " + call.Raw);
            yield break;
        }
        var zdoid = new ZDOID(user, id);
        var here = new Box();
        yield return WaitFor(() => ZNetScene.instance.FindInstance(zdoid) != null, 15f, here);
        if (!c.Check(here.Ok, "the server's ship never showed up on the client"))
        {
            yield break;
        }
        s.Go = ZNetScene.instance.FindInstance(zdoid);
        s.Boat = s.Go.GetComponent<Ship>();
        var nview = s.Go.GetComponent<ZNetView>();
        s.Zdo = nview != null ? nview.GetZDO() : null;
        if (!c.Check(s.Boat != null && s.Zdo != null && s.Boat.m_shipControlls != null && s.Boat.m_shipControlls.m_attachPoint != null,
                "the server's ship is not a ship with a helm here"))
        {
            yield break;
        }
        yield return null;
        yield return null;
        c.Check(!s.Boat.IsOwner() && s.Zdo.GetOwner() != ZDOMan.GetSessionID(), "this game, not the server, simulates the ship");
        MovePlayer(player, s.Boat.m_shipControlls.m_attachPoint.position + Vector3.up * 0.6f);
        var deck = new Box();
        yield return WaitFor(() => player.GetStandingOnShip() == s.Boat && s.Boat.IsPlayerInBoat(player), 6f, deck);
        if (!c.Check(deck.Ok, "the player put on the deck of the server's ship does not stand on it"))
        {
            yield break;
        }
        yield return Server("crew|" + ZDOMan.GetSessionID().ToString(CultureInfo.InvariantCulture), call);
        if (!c.Check(call.Ok, "the server does not see the player aboard its ship: " + call.Raw))
        {
            yield break;
        }
        s.Ok = true;
    }

    // Helm the vanilla way: ShipControlls.Interact asks the game that simulates the ship (the server). Server writes
    // the helm user on the ship's ZDO and answers with an RPC; the RPC is here at once, the ZDO value comes with the
    // next ZDO packet, some frames later. Runs of 2026-10-08: helm given ("Doodad controlls set" in the client log),
    // but in that same frame the ship's copy here still said "no helm user", and the old check (helmsman known here,
    // read right after the answer) failed every time. So me wait for both: this game controls the ship, AND its copy
    // of the ship names the player as helmsman (what HelmSkill reads here). No answer = ask again every 2 s (owner
    // drops a request without a word when it does not see the player aboard in that step; a player presses again).
    private static IEnumerator MpTakeHelm(Player player, MpShip s, Box result)
    {
        result.Ok = false;
        var standing = new Box();
        yield return WaitFor(() => s.Boat != null && player.GetStandingOnShip() == s.Boat, 4f, standing);
        if (!standing.Ok)
        {
            yield break;
        }
        var until = Time.realtimeSinceStartup + 8f;
        var nextAsk = 0f;
        while (s.Boat != null && !ReferenceEquals(player.GetControlledShip(), s.Boat) && Time.realtimeSinceStartup < until)
        {
            if (Time.realtimeSinceStartup >= nextAsk)
            {
                nextAsk = Time.realtimeSinceStartup + 2f;
                s.Boat.m_shipControlls.Interact(player, false, false);
            }
            yield return null;
        }
        if (s.Boat == null || !ReferenceEquals(player.GetControlledShip(), s.Boat))
        {
            yield break;
        }
        yield return WaitFor(() => s.Boat != null && ReferenceEquals(HelmSkill.Helmsman(s.Boat), player), 5f, result);
    }

    // What this game knows about the helm of the server's ship (for failure lines).
    private static string HelmState(Player player, MpShip s)
    {
        var boat = s.Boat;
        if (boat == null)
        {
            return "the ship is gone here";
        }
        var controls = boat.m_shipControlls;
        var user = controls != null ? controls.GetUser() : 0L;
        return $"this game controls the ship: {ReferenceEquals(player.GetControlledShip(), boat)}; helm user on the ship's copy here: "
               + $"{(user == 0L ? "none" : user == player.GetPlayerID() ? "the player" : "somebody else")}; players aboard here: {boat.m_players.Count}; "
               + $"player in that list: {boat.IsPlayerInBoat(player)}; standing on the ship: {player.GetStandingOnShip() == boat}; "
               + $"attached: {player.IsAttached()}; this game simulates the ship: {boat.IsOwner()}";
    }

    // Player off, me take ship (any game may claim an object) and destroy it, for every game. Never yield: finally use it.
    private static void MpRemoveShip(Player player, MpShip s)
    {
        try
        {
            if (player.GetDoodadController() != null)
            {
                player.StopDoodadControl();
            }
            if (player.IsAttached())
            {
                player.AttachStop();
            }
            if (s.Boat != null)
            {
                if (s.Boat.IsPlayerInBoat(player))
                {
                    s.Boat.m_players.Remove(player);
                    player.InNumShipVolumes = Mathf.Max(0, player.InNumShipVolumes - 1);
                }
                Ship.s_currentShips.Remove(s.Boat);
            }
            if (s.Zdo != null && s.Zdo.IsValid())
            {
                s.Zdo.SetOwner(ZDOMan.GetSessionID());
                if (s.Go != null && ZNetScene.instance != null)
                {
                    ZNetScene.instance.Destroy(s.Go);
                }
                else if (ZDOMan.instance != null)
                {
                    ZDOMan.instance.DestroyZDO(s.Zdo);
                }
            }
        }
        catch (Exception e)
        {
            Log.Error($"Sailing self test clean-up failed: {e}");
        }
        s.Go = null;
        s.Boat = null;
        s.Zdo = null;
    }

    private static IEnumerator MpLeave(Checks c, Player player, MpShip s)
    {
        MpRemoveShip(player, s);
        if (s.Asked)
        {
            // Server lets go of its ship (if it still has one) and puts its reference place back now, not when its
            // own watch notices.
            s.Asked = false;
            yield return Server("destroy", new Call());
        }
        if (s.Away)
        {
            var home = new Box();
            yield return TeleportAndWait(player, s.Home, 40f, home, fast: true);
            c.Check(home.Ok, "the player did not get back to where the test started");
            s.Away = !home.Ok;
        }
        else
        {
            MovePlayer(player, s.Ground);
        }
    }

    // finally of the server-ship tests.
    private static void MpRestore(Player player, MpShip s, ZPackage skills)
    {
        ClearOverrides();
        MpRemoveShip(player, s);
        try
        {
            if (skills != null)
            {
                skills.SetPos(0);
                player.GetSkills().Load(skills);
                HelmSkill.Invalidate();
            }
            if (s.Away && !player.IsTeleporting())
            {
                player.m_teleportCooldown = 10f;
                player.TeleportTo(s.Home, s.HomeRotation, true);
            }
            else if (!s.Away && (player.transform.position - s.Home).magnitude > 0.5f && !player.IsTeleporting())
            {
                MovePlayer(player, s.Home);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Sailing self test clean-up failed: {e}");
        }
    }

    // ---------- sailing.mp.helm ----------

    private static IEnumerator RunMpHelm()
    {
        var c = new Checks(MpHelmName);
        var watch = LogMark();
        var player = Player.m_localPlayer;
        if (player == null || !Connected)
        {
            SelfTest.Fail(MpHelmName, "not in a world as a client");
            yield break;
        }
        var s = new MpShip();
        var call = new Call();
        var uid = ZDOMan.GetSessionID().ToString(CultureInfo.InvariantCulture);
        ZPackage backup = null;
        try
        {
            if (!c.Check(ServerRules.UsingServer && !ServerRules.IsPending, "the client has no server rules"))
            {
                c.Report();
                yield break;
            }
            var rules = ServerRules.Current;
            var accel = SailMath.Scale(rules.AccelerationBonusAtMax, 1f);
            var turn = SailMath.Scale(rules.TurnBonusAtMax, 1f);
            yield return MpBoard(c, player, s);
            if (!s.Ok)
            {
                yield return MpLeave(c, player, s);
                c.Report();
                yield break;
            }
            var ship = s.Boat;

            // ----- helmsman at Sailing 0 on a ship another game simulates: normal handling -----
            HelmSkill.TestLocalLevel = 0f;
            LevelPublisher.PublishNow();
            var helm = new Box();
            yield return MpTakeHelm(player, s, helm);
            if (!c.Check(helm.Ok && ReferenceEquals(player.GetControlledShip(), ship) && ReferenceEquals(HelmSkill.Helmsman(ship), player),
                    "the player asked for the helm of the server's ship the vanilla way and is not its helmsman here: " + HelmState(player, s)))
            {
                yield return MpLeave(c, player, s);
                c.Report();
                yield break;
            }
            yield return Server($"helm|{uid}|0", call);
            c.Check(call.Ok && Yes(call.Kv, "is") && Yes(call.Kv, "owner") && Yes(call.Kv, "stepped") && Num(call.Kv, "s") == 0f
                    && Num(call.Kv, "accel") == 1f && Num(call.Kv, "turn") == 1f,
                "helmsman at Sailing 0: the server's physics step is not the normal game's: " + call.Raw);

            // ----- helmsman at Sailing 100: the game that simulates the ship reads the published level -----
            HelmSkill.TestLocalLevel = 100f;
            LevelPublisher.PublishNow();
            yield return Server($"helm|{uid}|100", call);
            c.Check(call.Ok && Yes(call.Kv, "is") && Yes(call.Kv, "stepped") && Near(Num(call.Kv, "published"), 100f) && Near(Num(call.Kv, "s"), 1f)
                    && Near(Num(call.Kv, "accel"), accel) && Near(Num(call.Kv, "turn"), turn),
                $"helmsman at Sailing 100: the server did not use the level (expected acceleration x{F(accel)}, turning x{F(turn)}): " + call.Raw);
            c.Check(Yes(call.Kv, "line"),
                $"the server did not log '{s.Name}: {player.GetPlayerName()} at the helm, Sailing 100 (this game simulates the ship).' "
                + $"(its lines: {Txt(call.Kv, "lines")})");
            // "Not this ship": another ship this game simulates (left by somebody near the spawn) may step meanwhile.
            ShipFixedUpdatePatches.LastShip = null;
            yield return FixedSteps(4);
            c.Check(!ReferenceEquals(ShipFixedUpdatePatches.LastShip, ship) && !ship.IsOwner(),
                "the helmsman's game applied the handling bonuses to a ship it does not simulate");

            // ----- rudder: turned on the helmsman's own game, faster at 100, and it reaches the server -----
            var steps = new int[2];
            var physicsStep = new WaitForFixedUpdate();
            for (var li = 0; li < 2; li++)
            {
                HelmSkill.TestLocalLevel = li * 100f;
                // Rudder at centre, no key, half a second (same per-step call, so the server hears "centre" too and
                // this game's own value stays the one in force).
                var centre = Time.time;
                while (Time.time - centre < 0.5f)
                {
                    ship.m_rudderValue = 0f;
                    ship.ApplyControlls(Vector3.zero);
                    yield return physicsStep;
                }
                ship.m_rudderValue = 0f;
                var n = 0;
                while (ship.m_rudderValue < 1f && n < 2000)
                {
                    ship.ApplyControlls(new Vector3(1f, 0f, 0f));
                    n++;
                }
                steps[li] = n;
                // Rudder key held at full lock for a second: one call per physics step, as the helmsman's game makes
                // (vanilla sends the value to the game that simulates the ship from inside that call, every 0.2 s; the
                // loop above ran inside one frame, so at most its first small value went out). Value read BEFORE each
                // held call = what the game's own step left: must stay at full lock (no snap back to the server's
                // older value).
                var lowest = 1f;
                var t0 = Time.time;
                while (Time.time - t0 < 1f)
                {
                    yield return physicsStep;
                    lowest = Mathf.Min(lowest, ship.m_rudderValue);
                    ship.ApplyControlls(new Vector3(1f, 0f, 0f));
                }
                yield return Server("rudder", call);
                c.Check(lowest >= 0.999f, $"Sailing {li * 100}: the rudder held at full lock jumped back to {F(lowest)} on the helmsman's game");
                c.Check(call.Ok && Num(call.Kv, "rudder") >= 0.95f,
                    $"Sailing {li * 100}: the rudder at full lock reads {Txt(call.Kv, "rudder")} on the server, expected 1");
            }
            ship.m_rudderValue = 0f;
            ship.m_rudder = 0f;
            var ratio = steps[0] > 0 ? steps[1] / (float)steps[0] : 0f;
            var wanted = 1f / SailMath.Scale(rules.RudderBonusAtMax, 1f);
            c.Check(steps[0] > 20 && Near(ratio, wanted, 0.04f),
                $"rudder to full lock on a ship the server simulates: {steps[0]} steps at Sailing 0, {steps[1]} at 100 (x{F(ratio)}), expected x{F(wanted)}");

            // ----- only the helmsman earns: the server moves its ship 20 m -----
            HelmSkill.TestLocalLevel = null;
            backup = new ZPackage();
            player.GetSkills().Save(backup);
            var sailing = SetSailing(player, 50f, 0f);
            var multiplier = 1f;
            player.GetSEMan().ModifyRaiseSkill(SailingSkill.Type, ref multiplier);
            SailingXp.Reset();
            yield return new WaitForSeconds(1.3f);
            c.Check(sailing.m_accumulator == 0f, "Sailing XP at the helm of a ship that does not move");
            yield return Server("move|20|0", call);
            yield return new WaitForSeconds(3f);
            var expected = 20f / 1000f * rules.XpPerKm * multiplier * Game.m_skillGainRate;
            c.Check(call.Ok && Near(sailing.m_accumulator, expected, expected * 0.1f),
                $"helmsman of a ship the server moved 20 m: {F(sailing.m_accumulator)} Sailing XP, expected {F(expected)}");
            // Passenger: helm let go (vanilla), sits attached so the ship can carry him.
            player.StopDoodadControl();
            var freed = new Box();
            yield return WaitFor(() => ship.m_shipControlls.GetUser() == 0L, 5f, freed);
            c.Check(freed.Ok && player.GetControlledShip() == null, "letting go of the helm did not free it on the server's ship");
            var controls = ship.m_shipControlls;
            player.AttachStart(controls.m_attachPoint, null, false, false, true, controls.m_attachAnimation, controls.m_detachOffset);
            yield return new WaitForSeconds(1.1f);
            var held = sailing.m_accumulator;
            yield return Server("move|-20|0", call);
            yield return new WaitForSeconds(3f);
            c.Check(call.Ok && sailing.m_accumulator == held,
                $"passenger of a ship the server moved 20 m earned {F(sailing.m_accumulator - held)} Sailing XP");
            player.AttachStop();
            backup.SetPos(0);
            player.GetSkills().Load(backup);
            backup = null;
            HelmSkill.Invalidate();

            // ----- the ship's simulation moves to the helmsman's game: the bonuses stay -----
            HelmSkill.TestLocalLevel = 100f;
            LevelPublisher.PublishNow();
            yield return MpTakeHelm(player, s, helm);
            if (c.Check(helm.Ok, "could not take the helm again: " + HelmState(player, s)))
            {
                if (ship.m_body != null)
                {
                    ship.m_body.isKinematic = true;   // this game is about to hold the ship in the air itself
                }
                yield return Server("owner|" + uid, call);
                var mine = new Box();
                yield return WaitFor(() => ship != null && ship.IsOwner(), 8f, mine);
                if (c.Check(call.Ok && mine.Ok, "the ship's simulation did not move to the helmsman's game: " + call.Raw))
                {
                    // Mod keeps a ship's helmsman level for half a second (HelmSkill slots), so the first steps as
                    // the simulating game may still use what the HUD asked a moment ago. Me wait for a step of THIS
                    // ship with the factors (2 s at most), not a fixed five steps.
                    ShipFixedUpdatePatches.LastShip = null;
                    var boosted = new Box();
                    yield return WaitFor(() => ReferenceEquals(ShipFixedUpdatePatches.LastShip, ship) && Near(ShipFixedUpdatePatches.LastAccel, accel)
                                               && Near(ShipFixedUpdatePatches.LastTurn, turn), 2f, boosted);
                    c.Check(boosted.Ok,
                        $"simulation moved to the helmsman's game: within 2 s its own step of the ship used acceleration "
                        + $"x{F(ShipFixedUpdatePatches.LastAccel)}, turning x{F(ShipFixedUpdatePatches.LastTurn)} (stepped: "
                        + $"{ReferenceEquals(ShipFixedUpdatePatches.LastShip, ship)}), expected x{F(accel)} and x{F(turn)}; " + HelmState(player, s));
                }
            }

            yield return MpLeave(c, player, s);
            c.Check(Connected, "the client lost the connection during the test");
            CheckQuiet(c, watch);
            c.Report();
        }
        finally
        {
            MpRestore(player, s, backup);
        }
    }

    // ---------- sailing.mp.crew ----------

    private static IEnumerator RunMpCrew()
    {
        var c = new Checks(MpCrewName);
        var watch = LogMark();
        var player = Player.m_localPlayer;
        if (player == null || !Connected)
        {
            SelfTest.Fail(MpCrewName, "not in a world as a client");
            yield break;
        }
        var s = new MpShip();
        var call = new Call();
        var uid = ZDOMan.GetSessionID().ToString(CultureInfo.InvariantCulture);
        try
        {
            if (!c.Check(ServerRules.UsingServer && !ServerRules.IsPending, "the client has no server rules"))
            {
                c.Report();
                yield break;
            }
            var rules = ServerRules.Current;
            var cut = SailMath.DamageFactor(rules.DamageReductionAtMax, 1f);
            yield return MpBoard(c, player, s);
            if (!s.Ok)
            {
                yield return MpLeave(c, player, s);
                c.Report();
                yield break;
            }
            var ship = s.Boat;
            c.Check(ship.m_shipControlls.GetUser() == 0L && player.GetControlledShip() == null, "somebody holds the helm (the crew checks want it free)");

            // ----- the best sailor aboard protects the ship, on the game that simulates it -----
            HelmSkill.TestLocalLevel = 0f;
            LevelPublisher.PublishNow();
            yield return Server($"hit|{uid}|0|1", call);
            var full = Num(call.Kv, "drop");
            c.Check(call.Ok && full > 0f && Num(call.Kv, "best") == 0f && Txt(call.Kv, "lines") == "0",
                "Sailing 0 on deck: the hit on the server's ship was not a plain full hit: " + call.Raw);
            HelmSkill.TestLocalLevel = 100f;
            LevelPublisher.PublishNow();
            yield return Server($"hit|{uid}|100|1", call);
            var line = DamageLine(s.Name, HitData.HitType.EnemyHit, 10f, 10f * cut, 100f);
            c.Check(call.Ok && Near(Num(call.Kv, "drop"), full * cut, 0.01f) && Near(Num(call.Kv, "best"), 1f),
                $"Sailing 100 on deck, nobody at the helm: the server's ship took {Txt(call.Kv, "drop")}, expected {F(full * cut)} (half of {F(full)}): " + call.Raw);
            c.Check(Txt(call.Kv, "line") == line, $"the server logged '{Txt(call.Kv, "line")}', expected '{line}'");

            // ----- own map on a ship another game simulates -----
            var map = Minimap.instance;
            if (map != null && c.Check(player.GetStandingOnShip() == ship, "the player no longer stands on the deck (map reveal not checked)"))
            {
                var radius = map.m_exploreRadius;
                RevealRadius(player, out var wideUsed, out var left);
                HelmSkill.TestLocalLevel = 0f;
                RevealRadius(player, out var normalUsed, out _);
                HelmSkill.TestLocalLevel = 100f;
                c.Check(Near(wideUsed, radius * SailMath.Scale(rules.RevealBonusAtMax, 1f), 0.01f) && Near(normalUsed, radius, 0.01f) && left == radius,
                    $"on the deck of the server's ship: reveal radius {F(wideUsed)} at Sailing 100 and {F(normalUsed)} at 0, expected "
                    + $"{F(radius * SailMath.Scale(rules.RevealBonusAtMax, 1f))} and {F(radius)}");
            }

            // ----- the sailor leaves the ship: full damage again -----
            LevelPublisher.PublishNow();
            MovePlayer(player, s.Ground);
            yield return Server($"hit|{uid}|100|0", call);
            c.Check(call.Ok && Near(Num(call.Kv, "drop"), full, 0.01f) && Num(call.Kv, "best") == 0f && !Yes(call.Kv, "aboard"),
                $"Sailing-100 player left the ship: the server's ship took {Txt(call.Kv, "drop")}, expected the full {F(full)}: " + call.Raw);

            yield return MpLeave(c, player, s);
            c.Check(Connected, "the client lost the connection during the test");
            CheckQuiet(c, watch);
            c.Report();
        }
        finally
        {
            MpRestore(player, s, null);
        }
    }

    // ---------- sailing.mp.join ----------

    private static IEnumerator RunMpJoin()
    {
        var c = new Checks(MpJoinName);
        var watch = LogMark();
        if (Player.m_localPlayer == null || !Connected)
        {
            SelfTest.Fail(MpJoinName, "not in a world as a client");
            yield break;
        }
        var call = new Call();
        var uid = ZDOMan.GetSessionID().ToString(CultureInfo.InvariantCulture);
        try
        {
            // With the mod, on, same network version: let in.
            yield return Server($"check|{uid}|recheck", call);
            c.Check(call.Ok && Yes(call.Kv, "allowed") && Yes(call.Kv, "compatible"),
                "the server's join check does not let in a player with the mod on: " + call.Raw);

            // What the server says of players who cannot play by its rules (AllowPlayersWithoutMod forced on for the
            // check, so this player stays; the refusal itself is the each-off and vanilla-client scenarios).
            var tail = "; AllowPlayersWithoutMod is on, so they may play, but a ship their game simulates sails and takes damage like in the normal game";
            var reasons = new[]
            {
                new KeyValuePair<string, string>("nomod", "does not have the mod"),
                new KeyValuePair<string, string>("off", "has the mod turned off"),
                new KeyValuePair<string, string>("version",
                    $"has another version of the mod (network version {ModInfo.NetworkVersion + 1}, the server has {ModInfo.NetworkVersion})"),
            };
            foreach (var reason in reasons)
            {
                yield return Server($"check|{uid}|{reason.Key}", call);
                var want = $"{Txt(call.Kv, "who")} {reason.Value}{tail}";
                c.Check(Txt(call.Kv, "warning").StartsWith(want, StringComparison.Ordinal),
                    $"server check '{reason.Key}': Warning '{Txt(call.Kv, "warning")}', expected it to start with '{want}'");
                c.Check(call.Ok && Yes(call.Kv, "allowed") && !Yes(call.Kv, "kicked"),
                    $"server check '{reason.Key}': the player was not let in again afterwards: " + call.Raw);
            }
            c.Check(Connected, "the client lost the connection during the server checks");

            // Mod turned off and on again within a second: the server's grace covers it, the player stays.
            var view = FeatureRegistry.Find(ModInfo.Guid);
            if (c.Check(view != null && view.Value.IsActive && Connected, "the mod is not active on the client"))
            {
                var feature = view.Value;
                yield return Server("mark", call);
                var told = LogMark();
                SetBlocked(true);
                var wasOff = !feature.IsActive;
                yield return new WaitForSecondsRealtime(0.3f);
                SetBlocked(false);
                c.Check(wasOff && feature.IsActive, $"the mod did not go off and on again on the client (now {feature.State})");
                c.Check(CountLogged(told, LogLevel.Info, $"Told the server that {ModInfo.Name} is now off on this game.") == 1
                        && CountLogged(told, LogLevel.Info, $"Told the server that {ModInfo.Name} is now on on this game.") == 1,
                    "the client did not tell the server 'off' then 'on' once each");
                // A refusal would come after the 1 s grace and cut the connection 4 s later.
                yield return new WaitForSecondsRealtime(PlayerCheck.GraceSeconds + PlayerCheck.DisconnectDelay + 2f);
                c.Check(Connected && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected,
                    $"off and on again within a second: the player was thrown out (connection {ZNet.GetConnectionStatus()})");
                if (Connected)
                {
                    yield return Server("verdict|" + uid, call);
                    c.Check(call.Ok && Num(call.Kv, "off") >= 1f && Num(call.Kv, "on") >= 1f && Num(call.Kv, "refused") == 0f
                            && Num(call.Kv, "allowed") >= 1f && !Yes(call.Kv, "kicked") && Yes(call.Kv, "compatible"),
                        "off and on again within a second: the server did not see both and let the player stay: " + call.Raw);
                    var rules = new Box();
                    yield return WaitFor(() => ServerRules.UsingServer && !ServerRules.IsPending, 6f, rules);
                    c.Check(rules.Ok, "after turning on again the client did not get the server's rules back");
                }
            }
            CheckQuiet(c, watch);
            c.Report();
        }
        finally
        {
            if (Plugin.TestBlocked)
            {
                Plugin.TestBlocked = false;
                try
                {
                    FeatureRegistry.RefreshAll();
                }
                catch (Exception e)
                {
                    Log.Error($"Sailing self test clean-up failed: {e}");
                }
            }
        }
    }

    // ---------- sailing.mp.vanilla-server ----------

    // Server without the mod. The feature is off here (the framework said so when the server did not answer), so
    // this test is registered at plugin start, not at activation.
    private static IEnumerator RunMpVanillaServer()
    {
        var c = new Checks(MpVanillaServerName);
        var watch = LogMark();
        var rig = ShipRig.Create(MpVanillaServerName);
        if (rig == null)
        {
            yield break;
        }
        Skills fresh = null;
        try
        {
            var player = rig.Player;
            var skills = player.GetSkills();
            var type = SailingSkill.Type;
            var view = FeatureRegistry.Find(ModInfo.Guid);
            c.Check(Connected, "not in a world as a client");
            c.Check(view != null && !view.Value.IsActive && view.Value.State == nameof(ModState.ServerMissing)
                    && view.Value.Status.StartsWith("Inactive: the server does not have this mod.", StringComparison.Ordinal),
                $"on a server without the mod the status is {(view != null ? view.Value.State + " '" + view.Value.Status + "'" : "not registered")}");
            c.Check(OwnShipPrefixes() == 0, "the ship patches are applied on a server without the mod");
            c.Check(HelmSkill.Published(player) == HelmSkill.NotPublished, $"the player still publishes a Sailing level ({F(HelmSkill.Published(player))})");

            var ok = new Box();
            yield return SetUpAirShip(rig, c, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            var ship = rig.Boat;
            rig.BackupSkills();
            var sailing = SetSailing(player, 100f, 0f);

            // Normal handling.
            ShipFixedUpdatePatches.LastShip = null;
            yield return FixedSteps(4);
            var like = FieldsLikePrefab(ship, rig.PrefabShip, out var fields);
            c.Check(ShipFixedUpdatePatches.LastShip == null && like, "Sailing 100 at the helm: the ship's physics step used the skill: " + fields);
            var vanillaStep = 0.5f * ship.m_rudderSpeed * Time.fixedDeltaTime;
            c.Check(Near(RudderStep(ship), vanillaStep, 1e-5f), "Sailing 100 at the helm: the rudder swings faster than the normal game's");
            CheckWind(c, ship, new SailingRules(), 0f, "server without the mod");
            var map = Minimap.instance;
            if (map != null)
            {
                var r = RevealReach(player);
                c.Check(r.Ok && r.Reach == ReachOf(map, map.m_exploreRadius) && r.Used < 0f,
                    $"Sailing 100 at the helm: map revealed {F(r.Reach * map.m_pixelSize)} m, expected the normal {F(ReachOf(map, map.m_exploreRadius) * map.m_pixelSize)} m");
            }
            if (rig.Wear != null)
            {
                // Same hit with the level in the character and without: both the game's plain damage.
                var with = DamageTaken(rig, HitData.HitType.EnemyHit, null);
                skills.ResetSkill(type);
                var without = DamageTaken(rig, HitData.HitType.EnemyHit, null);
                sailing = SetSailing(player, 100f, 0f);
                c.Check(with > 0f && Near(with, without, 0.01f), $"Sailing 100 aboard: the ship took {F(with)}, against {F(without)} without the skill");
            }

            // No XP.
            sailing = SetSailing(player, 50f, 0f);
            var home = ship.transform.position;
            MoveShip(ship, home + Vector3.right * 30f);
            yield return new WaitForSeconds(1.4f);
            MoveShip(ship, home);
            yield return new WaitForSeconds(1.4f);
            c.Check(sailing.m_accumulator == 0f && sailing.m_level == 50f, $"60 m at the helm gave {F(sailing.m_accumulator)} Sailing XP on a server without the mod");

            // The skill is still the character's and still shown.
            sailing = SetSailing(player, 100f, 0f);
            c.Check(skills.m_skillData.ContainsKey(type) && Skills.IsSkillValid(type) && ReferenceEquals(skills.GetSkillDef(type), SailingSkill.Def),
                "the Sailing skill is not known to the game on a server without the mod");
            yield return ShowInventory(c);
            var rows = OpenPanel();
            var row = FindRow(rows, SailingSkill.DisplayName);
            c.Check(row != null && row.Level == "100" && row.Icon != null && row.Icon == SailingSkill.Def.m_icon,
                $"the Skills panel shows Sailing '{(row != null ? row.Level : "no row")}', expected 100 with its icon");
            ClosePanel();
            // What the character takes home: its save data holds the level, and a character loaded from it keeps it.
            sailing = SetSailing(player, 77f, 1.5f);
            var saved = new ZPackage();
            player.Save(saved);
            var record = new ZPackage();
            record.Write((int)type);
            record.Write(77f);
            record.Write(1.5f);
            fresh = FreshSkills(player);
            var pkg = new ZPackage();
            skills.Save(pkg);
            pkg.SetPos(0);
            fresh.Load(pkg);
            c.Check(CountBytes(saved.GetArray(), record.GetArray()) == 1 && fresh.m_skillData.TryGetValue(type, out var loaded)
                    && loaded.m_level == 77f && loaded.m_accumulator == 1.5f,
                "the character's save data lost the Sailing level on a server without the mod");

            rig.RestoreSkills();
            yield return rig.Leave(rig.Origin);
            c.Check(Connected, "the client lost the connection during the test");
            CheckQuiet(c, watch);
            c.Report();
        }
        finally
        {
            if (fresh != null)
            {
                DestroyNow(fresh.gameObject);
            }
            if (InventoryGui.instance != null && InventoryGui.IsVisible())
            {
                ClosePanel();
            }
            rig.Restore();
        }
    }
}
#endif
