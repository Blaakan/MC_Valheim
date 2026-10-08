#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using MC.Shared;
using UnityEngine;
using ItemType = ItemDrop.ItemData.ItemType;

namespace MC.Combat.ShieldsTowerWallMod;

// Debug build only. Multiplayer self tests (tools/Test-Multiplayer.ps1): one client joined to a real dedicated server,
// both with the mod. Client tests ask the server half (one step, "tower.mp.server", with a command word) to change the
// SERVER's settings (its own throwaway config: the real settings path), to read what the server hold of this player
// (ZDO: what every other game is given) and to say what it decided about this player. No second player exists: the
// server stand in for "the friend".
//   tower.mp.rules     server rules reach the client's held tower (tooltip, bash, speed) in about a second, client log
//                      line, client settings and file untouched; server's own items follow its own setting; BashAnimation
//                      file values on the server (kick, Kicks, ShieldUp); a client's own Towers list is ignored, also
//                      while it wait for the server's rules
//   tower.mp.view      what the server hold of the bearer: tower in the left hand, on the back, blocking, the slowed
//                      swing's speed; a dropped tower carry nothing of the mod
//   tower.mp.creature  a Draugr controlled by the server: the client's bash stagger it there, a second bash inside
//                      the lock do not. A dedicated server make no objects by itself (its build pin the reference
//                      place far away every physics step): the server half hold that place on the player meanwhile.
//                      Run last: no other test of this mod meet a server that just built and dropped a whole area
//   tower.mp.join      the server's verdicts and log lines for this player: mod turned off (refuse, dry run), off and on
//                      inside the grace (stays), AllowPlayersWithoutMod (allowed, stay, normal towers); with the
//                      player's hello hidden or changed on the server: no mod, other network version
//   tower.mp.toggle    the server turn the mod off and on while the client braces; the client's own Enabled off and on
//   tower.mp.no-server (scenario vanilla-server) the mod inactive on the client, towers normal
internal static partial class SelfTests
{
    private const string MpRulesName = "tower.mp.rules";
    private const string MpViewName = "tower.mp.view";
    private const string MpCreatureName = "tower.mp.creature";
    private const string MpJoinName = "tower.mp.join";
    private const string MpToggleName = "tower.mp.toggle";
    private const string MpNoServerName = "tower.mp.no-server";
    private const string ServerStepName = "tower.mp.server";
    private const string ProbeSetEnabled = "probe.set-enabled"; // the server probe's own step
    private const string VanillaServerScenario = "vanilla-server";

    private static void RegisterMultiplayer()
    {
        SelfTest.RegisterMultiplayer(MpRulesName, SelfTest.Modded, RunMpRules);
        SelfTest.RegisterMultiplayer(MpViewName, SelfTest.Modded, RunMpView);
        SelfTest.RegisterMultiplayer(MpJoinName, SelfTest.Modded, RunMpJoin);
        SelfTest.RegisterMultiplayer(MpToggleName, SelfTest.Modded, RunMpToggle); // after the others: it turns the server's mod off and on
        // Last: the server build ground and every object around the player for it (a dedicated server never do that
        // in play), then drop them. Join and toggle time the server's answers, so they run before.
        SelfTest.RegisterMultiplayer(MpCreatureName, SelfTest.Modded, RunMpCreature);
        SelfTest.RegisterServerStep(ServerStepName, ServerStep);
    }

    private static void UnregisterMultiplayer()
    {
        SelfTest.UnregisterMultiplayer(MpRulesName);
        SelfTest.UnregisterMultiplayer(MpViewName);
        SelfTest.UnregisterMultiplayer(MpCreatureName);
        SelfTest.UnregisterMultiplayer(MpJoinName);
        SelfTest.UnregisterMultiplayer(MpToggleName);
        SelfTest.UnregisterServerStep(ServerStepName);
    }

    // Plugin.BindConfig: this one must run while the mod is NOT active, so it is never unregistered.
    private static void RegisterInactive()
    {
        SelfTest.RegisterMultiplayer(MpNoServerName, VanillaServerScenario, RunMpNoServer);
    }

    // ---------- words between the two halves ----------

    private static string N(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static float ParseFloat(string text) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : float.NaN;

    private static int ParseInt(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : -1;

    private static string IdText(ZDOID id) => id.UserID.ToString(CultureInfo.InvariantCulture) + ":" + id.ID.ToString(CultureInfo.InvariantCulture);

    private static bool ParseId(string text, out ZDOID id)
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

    // Answer of the server half: "key=value" lines.
    private sealed class Answer
    {
        internal bool Ok;
        internal string Text = "";

        internal string this[string key]
        {
            get
            {
                foreach (var line in Text.Split('\n'))
                {
                    if (line.StartsWith(key + "=", StringComparison.Ordinal))
                    {
                        return line.Substring(key.Length + 1);
                    }
                }
                return "";
            }
        }
    }

    private static IEnumerator Ask(string command, Answer answer, string step = ServerStepName)
    {
        var reply = new SelfTest.ServerReply();
        yield return SelfTest.CallServer(step, command, reply);
        answer.Ok = reply.Answered && reply.Ok;
        answer.Text = reply.Answered ? reply.Detail : "no answer: " + reply.Detail;
    }

    private static bool Connected => ZNet.instance != null && !ZNet.instance.IsServer() && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected
                                     && Player.m_localPlayer != null;

    // ---------- server half ----------

    // How long the server may take to build the ground and the objects around the player (zones one per 0.1 s, then
    // up to 100 objects per frame) before the creature and the player exist there. Server step limit is about 80 s.
    private const float OwnWait = 55f;
    // The server give the creature back by itself this long after it took the place (test limit is 120 s).
    private const float OwnLimit = 110f;

    private static int _ownToken;
    private static bool _refMoved;
    private static Vector3 _refBefore;
    private static Vector3 _refHeld;
    private static ZDOID _owned = ZDOID.None;

    private static ZNetPeer FirstPeer()
    {
        var net = ZNet.instance;
        if (net == null)
        {
            return null;
        }
        foreach (var peer in net.GetPeers())
        {
            if (peer != null && peer.IsReady() && peer.m_rpc != null)
            {
                return peer;
            }
        }
        return null;
    }

    private static ZDO PlayerZdo()
    {
        var peer = FirstPeer();
        return peer != null && !peer.m_characterID.IsNone() ? ZDOMan.instance.GetZDO(peer.m_characterID) : null;
    }

    private static string ZdoText(ZDO zdo) =>
        $"left={zdo.GetInt(ZDOVars.s_leftItem)}\nleftBack={zdo.GetInt(ZDOVars.s_leftBackItem)}\nright={zdo.GetInt(ZDOVars.s_rightItem)}\n"
        + $"blocking={(zdo.GetBool(ZDOVars.s_isBlockingHash) ? 1 : 0)}\nspeed={N(zdo.GetFloat(ZDOVars.s_animationSpeed, 1f))}";

    private static string ServerState()
    {
        var iron = ObjectDB.instance != null ? SharedOf(IronTower) : null;
        return $"own={ServerRules.Own.Key}\napplied={TowerSync.AppliedKey ?? ""}\niron={(iron != null ? N(iron.m_blockPower) : "")}\n"
               + $"ironType={(iron != null ? iron.m_itemType.ToString() : "")}\nanimation={Plugin.BashAnimation.Value}\n"
               + $"allow={(Plugin.AllowPlayersWithoutMod.Value ? 1 : 0)}\ndry={(PlayerCheck.DebugDryRun ? 1 : 0)}\n"
               + $"verdicts={PlayerCheck.DebugVerdicts.Count}\nwarnings={Tap.WarningMark()}\ndedicated={(ZNet.instance != null && ZNet.instance.IsDedicated() ? 1 : 0)}";
    }

    // "Name=value;Name=value": the server's own settings, set like its owner would (its config is a throwaway one).
    private static string ServerSet(string list)
    {
        foreach (var part in list.Split(';'))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                return $"bad setting '{part}'";
            }
            var name = part.Substring(0, eq).Trim();
            var value = part.Substring(eq + 1);
            switch (name)
            {
                case "BlockArmorMultiplier":
                    Plugin.BlockArmorMultiplier.Value = ParseFloat(value);
                    break;
                case "BashStagger":
                    Plugin.BashStagger.Value = ParseFloat(value);
                    break;
                case "BashAnimationSpeed":
                    Plugin.BashAnimationSpeed.Value = ParseFloat(value);
                    break;
                case "BashCooldown":
                    Plugin.BashCooldown.Value = ParseFloat(value);
                    break;
                case "CarrySlowPercent":
                    Plugin.CarrySlowPercent.Value = ParseInt(value);
                    break;
                case "BashAnimation":
                    Plugin.BashAnimation.Value = value;
                    break;
                case "AllowPlayersWithoutMod":
                    Plugin.AllowPlayersWithoutMod.Value = value == "true";
                    break;
                default:
                    return $"unknown setting '{name}'";
            }
        }
        return null;
    }

    private static void ServerReset()
    {
        foreach (var entry in new ConfigEntryBase[]
                 {
                     Plugin.Towers, Plugin.BlockArmorMultiplier, Plugin.BlockForcePercent, Plugin.CarrySlowPercent, Plugin.BraceSlowPercent,
                     Plugin.BraceStaggerResistPercent, Plugin.BraceKnockbackResistPercent, Plugin.BlockUnblockableAttacks, Plugin.BashAnimation,
                     Plugin.BashCustomTrigger, Plugin.BashAnimationSpeed, Plugin.BashStamina, Plugin.BashCooldown, Plugin.BashStagger,
                     Plugin.BashStaggerLock, Plugin.BashKnockback, Plugin.BashRange, Plugin.BashAngle, Plugin.AllowPlayersWithoutMod,
                 })
        {
            if (!Equals(entry.BoxedValue, entry.DefaultValue))
            {
                entry.BoxedValue = entry.DefaultValue;
            }
        }
        PlayerCheck.DebugDryRun = false;
        ReleaseOwned(giveBack: false);
    }

    // The server's reference place back where it was; the creature back to the player's game (the client test then
    // remove it), or removed here when nobody asked for it back (a creature left behind would wake up later).
    private static void ReleaseOwned(bool giveBack)
    {
        _ownToken++;
        try
        {
            if (!_owned.IsNone() && ZDOMan.instance != null)
            {
                var zdo = ZDOMan.instance.GetZDO(_owned);
                var peer = FirstPeer();
                if (zdo != null && zdo.IsOwner())
                {
                    if (giveBack && peer != null)
                    {
                        zdo.SetOwner(peer.m_uid);
                    }
                    else
                    {
                        var view = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(zdo) : null;
                        if (view != null)
                        {
                            ZNetScene.instance.Destroy(view.gameObject);
                        }
                        else
                        {
                            ZDOMan.instance.DestroyZDO(zdo);
                        }
                    }
                }
            }
        }
        finally
        {
            _owned = ZDOID.None;
            if (_refMoved && ZNet.instance != null)
            {
                ZNet.instance.SetReferencePosition(_refBefore);
            }
            _refMoved = false;
        }
    }

    private static IEnumerator ReleaseLater(int token)
    {
        yield return new WaitForSecondsRealtime(OwnLimit);
        if (token == _ownToken)
        {
            SelfTest.Note(ServerStepName, "the client never asked its creature back: removed");
            ReleaseOwned(giveBack: false);
        }
    }

    // The dedicated server build put its reference place at (1000000, 0, 1000000) in EVERY Game.FixedUpdate (read in
    // the server's own assembly_valheim.dll; the client build's Game.FixedUpdate has no such line), so a place set
    // once is gone one physics step later and the server never build ground or objects (first multiplayer run: "the
    // creature never appeared on the server" after 25 s). Me put the place back after each physics step: Unity resume
    // WaitForFixedUpdate after every FixedUpdate of the step and before the frame's Update, where ZoneSystem,
    // ZNetScene and ZDOMan read it. Ends when the token change (ReleaseOwned), which also put the old place back.
    private static IEnumerator HoldReference(int token)
    {
        var step = new WaitForFixedUpdate();
        while (token == _ownToken && _refMoved && ZNet.instance != null)
        {
            ZNet.instance.SetReferencePosition(_refHeld);
            yield return step;
        }
    }

    private static bool ReferenceHeld => ZNet.instance != null && Utils.DistanceXZ(ZNet.instance.GetReferencePosition(), _refHeld) < 1f;

    // Place free again = at its next object pass the server drop every object it made, in one long frame. Wait for
    // that (its copy of the player gone), so whatever run next meets a quiet server.
    private static IEnumerator ServerLetGo()
    {
        var peer = FirstPeer();
        var end = Time.realtimeSinceStartup + 6f;
        while (Time.realtimeSinceStartup < end && peer != null && ZNetScene.instance != null && ZNetScene.instance.FindInstance(peer.m_characterID) != null)
        {
            yield return null;
        }
    }

    private static Character ServerCreature(ZDOID id, out ZDO zdo)
    {
        zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(id) : null;
        var view = zdo != null && ZNetScene.instance != null ? ZNetScene.instance.FindInstance(zdo) : null;
        return view != null ? view.GetComponent<Character>() : null;
    }

    // One step, first word = what to do. Answer = "key=value" lines.
    private static IEnumerator ServerStep(string arg, object[] reply)
    {
        var bar = (arg ?? "").IndexOf('|');
        var word = bar >= 0 ? arg.Substring(0, bar) : arg ?? "";
        var rest = bar >= 0 ? arg.Substring(bar + 1) : "";
        var net = ZNet.instance;
        if (net == null || !net.IsServer())
        {
            SelfTest.Answer(reply, false, "not a server");
            yield break;
        }
        switch (word)
        {
            case "state":
                SelfTest.Answer(reply, true, ServerState());
                yield break;
            case "set":
            {
                var problem = ServerSet(rest);
                SelfTest.Answer(reply, problem == null, problem ?? ServerState());
                yield break;
            }
            case "reset":
                ServerReset();
                SelfTest.Answer(reply, true, ServerState());
                yield break;
            case "dry":
                PlayerCheck.DebugDryRun = rest == "on";
                SelfTest.Answer(reply, true, ServerState());
                yield break;
            case "verdicts":
            {
                var from = Mathf.Clamp(ParseInt(rest), 0, PlayerCheck.DebugVerdicts.Count);
                SelfTest.Answer(reply, true, $"count={PlayerCheck.DebugVerdicts.Count}\nlines={string.Join(";", PlayerCheck.DebugVerdicts.Skip(from).ToArray())}");
                yield break;
            }
            case "log":
                SelfTest.Answer(reply, true, $"mark={Tap.WarningMark()}\nlines={string.Join(" || ", Tap.WarningsSince(ParseInt(rest)).ToArray())}");
                yield break;
            case "zdo":
                yield return ServerZdo(rest, reply);
                yield break;
            case "item":
                yield return ServerItem(rest, reply);
                yield break;
            case "own":
                yield return ServerOwn(rest, reply);
                yield break;
            case "creature":
            case "zero":
            case "place":
                yield return ServerCreatureStep(word, rest, reply);
                yield break;
            case "release":
                ReleaseOwned(giveBack: true);
                yield return ServerLetGo();
                SelfTest.Answer(reply, true, "released");
                yield break;
            case "hello":
                yield return ServerHello(rest, reply);
                yield break;
            default:
                SelfTest.Answer(reply, false, $"unknown word '{word}'");
                yield break;
        }
    }

    // "wait|key=value|seconds": until the player's ZDO say so. "speed|seconds": every animation speed seen meanwhile.
    private static IEnumerator ServerZdo(string rest, object[] reply)
    {
        var parts = rest.Split('|');
        var zdo = PlayerZdo();
        if (zdo == null)
        {
            SelfTest.Answer(reply, false, "the server has no ZDO of the player");
            yield break;
        }
        if (parts[0] == "speed")
        {
            var seen = new List<string>();
            var until = Time.realtimeSinceStartup + Mathf.Clamp(parts.Length > 1 ? ParseFloat(parts[1]) : 2f, 0.2f, 10f);
            while (Time.realtimeSinceStartup < until)
            {
                var speed = zdo.GetFloat(ZDOVars.s_animationSpeed, 1f).ToString("0.00", CultureInfo.InvariantCulture);
                if (!seen.Contains(speed))
                {
                    seen.Add(speed);
                }
                yield return null;
            }
            SelfTest.Answer(reply, true, "seen=" + string.Join(";", seen.ToArray()));
            yield break;
        }
        var want = parts.Length > 1 ? parts[1] : "";
        var end = Time.realtimeSinceStartup + Mathf.Clamp(parts.Length > 2 ? ParseFloat(parts[2]) : 6f, 0.2f, 20f);
        var ok = false;
        while (true)
        {
            ok = ("\n" + ZdoText(zdo) + "\n").Contains("\n" + want + "\n");
            if (ok || Time.realtimeSinceStartup > end)
            {
                break;
            }
            yield return null;
        }
        SelfTest.Answer(reply, ok, ZdoText(zdo));
    }

    // "user:id": the item on the ground as the server hold it (all another game get to build it from).
    private static IEnumerator ServerItem(string rest, object[] reply)
    {
        if (!ParseId(rest, out var id))
        {
            SelfTest.Answer(reply, false, $"bad id '{rest}'");
            yield break;
        }
        ZDO zdo = null;
        var end = Time.realtimeSinceStartup + 6f;
        while ((zdo = ZDOMan.instance.GetZDO(id)) == null && Time.realtimeSinceStartup < end)
        {
            yield return null;
        }
        if (zdo == null)
        {
            SelfTest.Answer(reply, false, "the server never got that item");
            yield break;
        }
        SelfTest.Answer(reply, true, $"prefab={zdo.GetPrefab()}\ndata={zdo.GetInt(ZDOVars.s_dataCount)}\nquality={zdo.GetInt(ZDOVars.s_quality, 1)}\n"
                                     + $"crafter={zdo.GetString(ZDOVars.s_crafterName)}");
    }

    // "user:id": the server take that creature over (as another player's game near it would): its reference place
    // is held on the player (HoldReference: same active area as the player's game, so nothing else change hands), it
    // wait for the creature and the player to exist here, take ownership and stops its AI. Given back by "release",
    // "reset" or OwnLimit later. While the place is held the server's 2 s ownership pass leave the creature with the
    // server (ZDOMan.ReleaseNearbyZDOS: in its owner's active area); without the hold it would hand it straight back.
    private static IEnumerator ServerOwn(string rest, object[] reply)
    {
        if (!ParseId(rest, out var id))
        {
            SelfTest.Answer(reply, false, $"bad id '{rest}'");
            yield break;
        }
        ReleaseOwned(giveBack: false);
        ZDO zdo = null;
        var end = Time.realtimeSinceStartup + 6f;
        while ((zdo = ZDOMan.instance.GetZDO(id)) == null && Time.realtimeSinceStartup < end)
        {
            yield return null;
        }
        var peer = FirstPeer();
        if (zdo == null || peer == null)
        {
            SelfTest.Answer(reply, false, zdo == null ? "the server never got that creature" : "no player");
            yield break;
        }
        _refBefore = ZNet.instance.GetReferencePosition();
        // The player's own reference place (what the server already use for that player's area); the creature's place
        // when the server hold none yet for the player.
        _refHeld = Utils.DistanceXZ(peer.GetRefPos(), zdo.GetPosition()) < 32f ? peer.GetRefPos() : zdo.GetPosition();
        _refMoved = true;
        _owned = id;
        var token = ++_ownToken;
        ZNet.instance.StartCoroutine(HoldReference(token));
        ZNet.instance.StartCoroutine(ReleaseLater(token));
        Character creature = null;
        var t0 = Time.realtimeSinceStartup;
        var bothSince = -1f;
        var madeCreature = false;
        var madePlayer = false;
        var held = true;
        end = t0 + OwnWait;
        while (Time.realtimeSinceStartup < end)
        {
            yield return null;
            held &= ReferenceHeld;
            creature = ServerCreature(id, out zdo);
            madeCreature = creature != null;
            madePlayer = ZNetScene.instance.FindInstance(peer.m_characterID) != null;
            if (!madeCreature || !madePlayer)
            {
                bothSince = -1f;
                creature = null;
                continue;
            }
            if (bothSince < 0f)
            {
                bothSince = Time.realtimeSinceStartup;
            }
            // The ground must exist here too before the server move the creature's body (else it falls). The game
            // make no object before every ground zone of the active area is there (ZNetScene.CreateObjectsSorted), so
            // the creature existing already say so; "area ready" (all objects of the 9 zones around) is the tidy
            // sign, 3 s with both here the fallback when some object never get made on a server.
            if (ZNetScene.instance.IsAreaReady(creature.transform.position) || Time.realtimeSinceStartup - bothSince > 3f)
            {
                break;
            }
            creature = null;
        }
        var waited = Time.realtimeSinceStartup - t0;
        if (creature == null || zdo == null)
        {
            // Every part named: a next failure say which step never came.
            var zones = ZoneSystem.instance != null && ZoneSystem.instance.IsActiveAreaLoaded();
            var made = ZNetScene.instance != null ? ZNetScene.instance.m_instances.Count : -1;
            var detail = $"the creature never appeared on the server within {N(OwnWait)} s: reference place held on the player {(held && ReferenceHeld ? "yes" : "NO")}, "
                         + $"ground zones around it loaded {(zones ? "yes" : "no")}, objects made here {made}, creature made {(madeCreature ? "yes" : "no")}, "
                         + $"player made {(madePlayer ? "yes" : "no")}";
            ReleaseOwned(giveBack: true);
            yield return ServerLetGo();
            SelfTest.Answer(reply, false, detail);
            yield break;
        }
        zdo.SetOwner(ZDOMan.GetSessionID());
        var ai = creature.GetComponent<BaseAI>();
        if (ai != null)
        {
            ai.enabled = false;
        }
        creature.m_staggerDamage = 0f;
        yield return null;
        SelfTest.Answer(reply, zdo.IsOwner(), $"owner={(zdo.IsOwner() ? 1 : 0)}\nthreshold={N(creature.GetStaggerTreshold())}\n"
                                              + $"attacker={(ZNetScene.instance.FindInstance(peer.m_characterID) != null ? 1 : 0)}\nrules={(TowerSync.Applied != null ? 1 : 0)}\n"
                                              + $"held={(held && ReferenceHeld ? 1 : 0)}\nwaited={N(waited)}\nobjects={ZNetScene.instance.m_instances.Count}");
    }

    // "creature|id": its state here. "zero|id": bar empty, full health. "place|id|x|y|z|fx|fz": put it there, facing.
    private static IEnumerator ServerCreatureStep(string word, string rest, object[] reply)
    {
        var parts = rest.Split('|');
        if (!ParseId(parts[0], out var id))
        {
            SelfTest.Answer(reply, false, $"bad id '{parts[0]}'");
            yield break;
        }
        var creature = ServerCreature(id, out var zdo);
        if (creature == null || zdo == null)
        {
            SelfTest.Answer(reply, false, "the server has no such creature");
            yield break;
        }
        if (word == "zero")
        {
            creature.m_staggerDamage = 0f;
            creature.SetHealth(creature.GetMaxHealth());
        }
        else if (word == "place" && parts.Length >= 6)
        {
            var pos = new Vector3(ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3]));
            var facing = new Vector3(ParseFloat(parts[4]), 0f, ParseFloat(parts[5]));
            var rot = facing.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(facing) : creature.transform.rotation;
            creature.transform.SetPositionAndRotation(pos, rot);
            var body = creature.m_body;
            if (body != null)
            {
                body.position = pos;
                body.rotation = rot;
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                }
            }
            creature.m_pushForce = Vector3.zero;
        }
        else
        {
            yield return new WaitForSeconds(0.3f);
        }
        var locked = BashStagger.DebugLockTime(creature, out var lockTime);
        SelfTest.Answer(reply, true, $"owner={(zdo.IsOwner() ? 1 : 0)}\nbar={N(creature.m_staggerDamage)}\nthreshold={N(creature.GetStaggerTreshold())}\n"
                                     + $"health={N(creature.GetHealth())}\nmax={N(creature.GetMaxHealth())}\nstaggering={(creature.IsStaggering() ? 1 : 0)}\n"
                                     + $"lock={(locked ? 1 : 0)}\nlockAge={(locked ? N(Time.time - lockTime) : "")}");
    }

    // "none" or a hello text: what the server would decide for this player if its game had said that when joining (no
    // mod, another network version). The framework's record of the player's hello is changed for one check, dry run
    // (decided and logged, nobody kicked), then put back.
    private static IEnumerator ServerHello(string rest, object[] reply)
    {
        var peer = FirstPeer();
        var field = typeof(NetworkGate).GetField("ClientHellos", BindingFlags.NonPublic | BindingFlags.Static);
        var hellos = field != null ? field.GetValue(null) as Dictionary<ZRpc, string> : null;
        if (peer == null || hellos == null || !hellos.TryGetValue(peer.m_rpc, out var real))
        {
            SelfTest.Answer(reply, false, hellos == null ? "the framework's hello table was not found (renamed?)" : "the player said no hello");
            yield break;
        }
        var count = PlayerCheck.DebugVerdicts.Count;
        var mark = Tap.WarningMark();
        var dry = PlayerCheck.DebugDryRun;
        PlayerCheck.DebugDryRun = true;
        try
        {
            if (rest == "none")
            {
                hellos.Remove(peer.m_rpc);
            }
            else
            {
                hellos[peer.m_rpc] = rest;
            }
            PlayerCheck.ScheduleAllConnected();
            var end = Time.unscaledTime + PlayerCheck.GraceSeconds + 1.5f;
            while (Time.unscaledTime < end && PlayerCheck.DebugVerdicts.Count == count)
            {
                yield return null;
            }
        }
        finally
        {
            hellos[peer.m_rpc] = real;
            PlayerCheck.DebugDryRun = dry;
        }
        var verdict = PlayerCheck.DebugVerdicts.Count > count ? PlayerCheck.DebugVerdicts[PlayerCheck.DebugVerdicts.Count - 1] : "";
        SelfTest.Answer(reply, verdict.Length > 0, $"verdict={verdict}\nlines={string.Join(" || ", Tap.WarningsSince(mark).ToArray())}");
    }

    // ---------- client: tower.mp.rules ----------

    private static IEnumerator ServerDefaults(Answer a)
    {
        yield return Ask("reset", a);
        if (a.Ok)
        {
            var key = a["own"];
            yield return Until(() => TowerSync.AppliedKey == key, 8f);
        }
    }

    private static IEnumerator RunMpRules()
    {
        var c = new Checks(MpRulesName);
        var rig = new Rig(MpRulesName);
        var a = new Answer();
        try
        {
            var p = rig.P;
            var tap = Tap;
            yield return ServerDefaults(a);
            if (!a.Ok)
            {
                c.Check(false, $"the server half did not answer: {a.Text}");
                c.Report();
                yield break;
            }
            var defaultsKey = TowerRules.Defaults().Key;
            c.Check(a["dedicated"] == "1" && a["own"] == defaultsKey, "a dedicated server with the default rules");
            c.Check(Connected && ServerRules.UsingServer && !ServerRules.Waiting && TowerSync.AppliedKey == a["own"],
                "joined: the client uses the rules the server sent");
            var path = Plugin.DebugConfigPath;
            var fileBefore = path != null && File.Exists(path) ? File.ReadAllText(path) : null;
            var ownBefore = TowerRules.Own().Key;
            var tower = rig.Give(IronTower);
            var draugr = rig.Creature("Draugr");
            var snap = SnapshotOf(IronTower);
            if (tower == null || draugr == null || snap == null)
            {
                c.Check(false, "could not add a ShieldIronTower (or no snapshot) or spawn a Draugr");
                c.Report();
                yield break;
            }
            p.UpdateModifiers();
            var jog0 = p.GetJogSpeedFactor();
            yield return FreshBash(p, tower);

            // TESTING M03: the server's owner sets BlockArmorMultiplier 4 and BashStagger 20.
            var infoMark = tap.InfoMark();
            yield return Ask("set|BlockArmorMultiplier=4;BashStagger=20", a);
            var t0 = Time.unscaledTime;
            var key = a["own"];
            yield return Until(() => TowerSync.AppliedKey == key, 8f);
            var took = Time.unscaledTime - t0;
            c.Check(a.Ok && key != defaultsKey && TowerSync.AppliedKey == key && took <= 2.5f,
                $"the server's new rules are on the client's items {F(took)} s after the server answered (about a second)");
            var tip = tower.GetTooltip();
            var bash = BashAttack.Current;
            c.Check(ReferenceEquals(p.m_leftItem, tower) && tower.m_equipped && tip.Contains($"$item_blockarmor: <color=orange>{snap.BlockPower * 4f}</color>")
                    && tip.Contains("Bash stagger: <color=orange>×20</color>") && bash != null && Near(bash.m_staggerMultiplier, 20f),
                $"the held tower was not re-equipped and shows the server's rules: block armor {F(snap.BlockPower * 4f)}, bash stagger ×20 ('{OneLine(tip)}')");
            var lines = tap.InfosSince(infoMark);
            c.Check(lines.Any(l => l.StartsWith("Using the server's tower shield rules: ", StringComparison.Ordinal) && l.Contains("block armor x4") && l.Contains("stagger x20")),
                $"the client's log says 'Using the server's tower shield rules: ...' ({lines.Count} info lines)");
            // The measure: Blocking 100 (the bash then add 204 to 240 at x20, and 255 to 300 at the default x25: the two
            // never overlap, as they do at Blocking 0) and the Draugr's threshold raised x8 for this one bash (its bar
            // never go above the threshold of 50: the single-player run read +50 for a x20 bash). The Draugr is the
            // client's own creature: the stagger is worked out here, with the server's rules.
            SetSkill(p, Skills.SkillType.Blocking, 100f);
            var staggerFactor = draugr.m_staggerDamageFactor;
            draugr.m_staggerDamageFactor = staggerFactor * 8f;
            var s = new Swing();
            try
            {
                yield return Press(p, draugr, s, MpRulesName, null);
            }
            finally
            {
                draugr.m_staggerDamageFactor = staggerFactor;
            }
            SetSkill(p, Skills.SkillType.Blocking, 0f);
            SelfTest.Note(MpRulesName, Describe("bash with the server's BashStagger 20 (Blocking 100, threshold raised for the measure)", s));
            c.Check(s.Landed && draugr.m_nview.IsOwner(), "the client's bash lands on its own Draugr");
            if (s.Landed)
            {
                CheckHit(c, "the client's bash uses the server's BashStagger 20", s, 12f, 20f, 1f, -1f);
            }
            var fileAfter = path != null && File.Exists(path) ? File.ReadAllText(path) : null;
            c.Check(Near(Plugin.BlockArmorMultiplier.Value, TowerRules.DefaultBlockArmorMultiplier) && Near(Plugin.BashStagger.Value, TowerRules.DefaultBashStagger)
                    && TowerRules.Own().Key == ownBefore && fileBefore != null && fileBefore == fileAfter,
                "the client's own settings and its settings file are unchanged");

            // TESTING M10: BashStagger and CarrySlowPercent changed while the client hold the tower.
            yield return Ask("set|BashStagger=30;CarrySlowPercent=20", a);
            t0 = Time.unscaledTime;
            key = a["own"];
            yield return Until(() => TowerSync.AppliedKey == key, 8f);
            took = Time.unscaledTime - t0;
            tip = tower.GetTooltip();
            p.UpdateModifiers();
            c.Check(a.Ok && TowerSync.AppliedKey == key && took <= 2.5f && ReferenceEquals(p.m_leftItem, tower) && tower.m_equipped,
                $"a live change on the server reaches the held tower {F(took)} s after the server answered, without re-equipping");
            c.Check(tip.Contains("Bash stagger: <color=orange>×30</color>") && tip.Contains(Player.s_equipmentModifierTooltips[0] + ": <color=orange>-20%</color>")
                    && Near(p.GetJogSpeedFactor(), jog0 - 0.20f, 0.001f),
                $"tooltip and speed follow: bash stagger ×30, movement -20%, jog factor {F(p.GetJogSpeedFactor())} ({F(jog0)} without the tower)");

            // TESTING T15 on a real settings file: the server's BashAnimation read from odd values; its choice applies.
            yield return Ask("set|BashAnimation=kick", a);
            key = a["own"];
            var read = a["animation"];
            yield return Until(() => TowerSync.AppliedKey == key, 8f);
            c.Check(read == "Kick" && TowerSync.Applied != null && TowerSync.Applied.BashAnimation == BashAnimationKind.Kick && BashAttack.Mode == BashAnimationKind.Kick,
                $"server BashAnimation = kick: the setting reads '{read}' and the client's bash is the Kick (the host's choice applies)");
            yield return Ask("state", a);
            var warnMark = a["warnings"];
            yield return Ask("set|BashAnimation=Kicks", a);
            read = a["animation"];
            yield return Ask("log|" + warnMark, a);
            c.Check(read == "ShieldPunch" && a["lines"].Contains("BashAnimation 'Kicks' is not one of ShieldPunch, OtherPunch, Kick, Custom; ShieldPunch is used."),
                $"server BashAnimation = Kicks: read as '{read}', with its Warning in the server's log ('{a["lines"]}')");
            warnMark = a["mark"];
            yield return Ask("set|BashAnimation=Kick", a);
            yield return Ask("set|BashAnimation=ShieldUp", a);
            read = a["animation"];
            yield return Ask("log|" + warnMark, a);
            c.Check(read == "ShieldPunch" && !a["lines"].Contains("BashAnimation"), $"server BashAnimation = ShieldUp: read as '{read}', no warning ('{a["lines"]}')");

            // The server's own items follow its own setting (the settings handler on the game that owns the rules).
            yield return Ask("set|BlockArmorMultiplier=3", a);
            key = a["own"];
            yield return Until(() => TowerSync.AppliedKey == key, 8f);
            yield return Seconds(0.8f);
            yield return Ask("state", a);
            c.Check(a["applied"] == a["own"] && Near(ParseFloat(a["iron"]), snap.BlockPower * 3f) && a["ironType"] == ItemType.TwoHandedWeaponLeft.ToString(),
                $"BlockArmorMultiplier 3 on the server: its own ShieldIronTower has block armor {a["iron"]} ({F(snap.BlockPower * 3f)} expected)");

            // TESTING M11: the client's own Towers list (with ShieldBanded) is not used on a server, also while the
            // client wait for the server's rules (as right after joining).
            var sword = rig.Give("SwordIron");
            var banded = rig.Give("ShieldBanded");
            if (sword != null && banded != null)
            {
                yield return WaitIdle(p);
                rig.EmptyHands();
                p.EquipItem(sword);
                p.EquipItem(banded);
                TowerRules.DebugOwn = TowerRules.Defaults().With(r => r.Towers = TowerRules.DefaultTowers + ", ShieldBanded:7");
                ServerRules.OwnChanged();
                yield return Seconds(1.2f);
                c.Check(TowerSync.AppliedKey == key && ReferenceEquals(p.m_rightItem, sword) && ReferenceEquals(p.m_leftItem, banded) && banded.m_shared.m_itemType == ItemType.Shield,
                    $"own Towers list with ShieldBanded while on the server: ignored, sword and ShieldBanded stay equipped ({Hands(p)})");
                ServerRules.Forget(); // as on a new connection: no rules of the server yet
                yield return Until(() => TowerSync.AppliedKey == null && !TowerSync.HasWork, 4f);
                var prefab = SharedOf(IronTower);
                c.Check(ServerRules.Waiting && TowerSync.AppliedKey == null && ReferenceEquals(p.m_rightItem, sword) && ReferenceEquals(p.m_leftItem, banded)
                        && banded.m_shared.m_itemType == ItemType.Shield && prefab != null && prefab.m_itemType == ItemType.Shield,
                    $"waiting for the server's rules: tower shields are normal, the own Towers list is not used, both stay equipped ({Hands(p)})");
                var server = ZNet.instance.GetServerPeer();
                if (server != null && server.m_rpc != null)
                {
                    ServerRules.Request(server.m_rpc);
                }
                yield return Until(() => TowerSync.AppliedKey == key, 8f);
                c.Check(ServerRules.UsingServer && TowerSync.AppliedKey == key && ReferenceEquals(p.m_rightItem, sword) && ReferenceEquals(p.m_leftItem, banded)
                        && banded.m_shared.m_itemType == ItemType.Shield && !banded.IsTwoHanded(),
                    $"the server's rules arrive: both still equipped, ShieldBanded stays a one-handed shield ({Hands(p)})");
                ClearOwn();
            }
            else
            {
                c.Check(false, "could not add SwordIron and ShieldBanded");
            }
            yield return ServerDefaults(a);
            c.Check(a.Ok && TowerSync.AppliedKey == defaultsKey, "server settings back to the defaults, the client follows");
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- client: tower.mp.view ----------

    private static IEnumerator RunMpView()
    {
        var c = new Checks(MpViewName);
        var rig = new Rig(MpViewName);
        var a = new Answer();
        try
        {
            var p = rig.P;
            yield return ServerDefaults(a);
            if (!a.Ok)
            {
                c.Check(false, $"the server half did not answer: {a.Text}");
                c.Report();
                yield break;
            }
            var tower = rig.Give(IronTower);
            var draugr = rig.Creature("Draugr");
            if (tower == null || draugr == null)
            {
                c.Check(false, "could not add a ShieldIronTower or spawn a Draugr");
                c.Report();
                yield break;
            }
            var hash = Hash(IronTower);
            yield return FreshBash(p, tower);

            // TESTING M04 (what the server give every other game): in the left hand, blocking, on the back.
            yield return Ask($"zdo|wait|left={hash}|6", a);
            c.Check(a.Ok && a["leftBack"] == "0", $"the server holds the tower shield in the bearer's left hand ({a.Text.Replace("\n", ", ")})");
            yield return Guard(p, true);
            yield return Ask("zdo|wait|blocking=1|6", a);
            c.Check(a.Ok, "braced: the server holds the bearer as blocking (the block pose other players see)");
            yield return Guard(p, false);
            yield return Ask("zdo|wait|blocking=0|6", a);
            c.Check(a.Ok, "block released: no longer blocking on the server");
            p.HideHandItems();
            yield return Ask($"zdo|wait|leftBack={hash}|6", a);
            c.Check(a.Ok && a["left"] == "0", $"put away: the server holds the tower on the bearer's back, the left hand empty ({a.Text.Replace("\n", ", ")})");
            var vis = p.m_visEquipment;
            yield return Frames(4);
            c.Check(vis != null && vis.m_leftBackItemInstance != null && vis.m_backShield != null && vis.m_leftBackItemInstance.transform.IsChildOf(vis.m_backShield),
                "it hangs in the shield slot of the back (same code on every game: the prefab says shield)");
            p.ShowHandItems(false, false);
            yield return Ask($"zdo|wait|left={hash}|6", a);

            // TESTING M17: the slowed swing as the server hold it (other games set their copy of player to it).
            SetSkill(p, Skills.SkillType.Blocking, 0f);
            foreach (var speed in new[] { TowerRules.DefaultBashAnimationSpeed, 1f })
            {
                if (!Mathf.Approximately(speed, TowerRules.DefaultBashAnimationSpeed))
                {
                    yield return Ask("set|BashAnimationSpeed=" + N(speed), a);
                    var key = a["own"];
                    yield return Until(() => TowerSync.AppliedKey == key, 8f);
                }
                yield return WaitIdle(p);
                yield return WaitCooldown(p);
                FaceClearLane(p, Reach(p, draugr), LaneFan, MpViewName, draugr);
                PlaceInFront(p, draugr);
                yield return Fixed;
                var started = p.StartAttack(null, false);
                yield return Ask("zdo|speed|2.4", a);
                var seen = a["seen"].Split(';').Select(ParseFloat).ToList();
                var scaled = 2f * speed; // the punch clip's own speed is 2
                c.Check(started && a.Ok && seen.Any(v => Near(v, scaled, 0.03f)) && (Mathf.Approximately(speed, 1f) || !seen.Any(v => Near(v, 2f, 0.03f))),
                    $"bash at BashAnimationSpeed {F(speed)}: the server holds the swing at speed {F(scaled)} (speeds it saw: {a["seen"]})");
                yield return WaitIdle(p);
            }
            yield return ServerDefaults(a);

            // TESTING M06 (the item as it travels): a dropped tower carry the prefab and nothing of the mod; picked up
            // by a game with the mod it is two-handed again.
            yield return WaitIdle(p);
            rig.EmptyHands();
            rig.Inv.RemoveItem(tower);
            var drop = ItemDrop.DropItem(tower, 1, p.transform.position + Flat(p.transform.right) * 5f + Vector3.up, Quaternion.identity);
            rig.Track(drop.gameObject);
            var data = drop.m_itemData;
            rig.Track(data);
            yield return Frames(3);
            var zdo = drop.m_nview != null ? drop.m_nview.GetZDO() : null;
            if (zdo == null)
            {
                c.Check(false, "the dropped tower has no ZDO");
            }
            else
            {
                yield return Ask("item|" + IdText(zdo.m_uid), a);
                c.Check(a.Ok && a["prefab"] == hash.ToString(CultureInfo.InvariantCulture) && a["data"] == "0",
                    $"a dropped tower shield on the server: the ShieldIronTower prefab and no custom data ({a.Text.Replace("\n", ", ")}): a game without the mod builds a normal shield from it");
                if (drop != null)
                {
                    p.Pickup(drop.gameObject, true, false);
                }
                c.Check(rig.Inv.ContainsItem(data) && !data.m_equipped && p.m_leftItem == null, $"picked up again: in the inventory, not in the hands ({Hands(p)})");
                c.Check(p.EquipItem(data) && Only(p, data, left: true) && data.m_shared.m_itemType == ItemType.TwoHandedWeaponLeft,
                    $"for the game with the mod it is two-handed again ({Hands(p)})");
            }
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }

    // ---------- client: tower.mp.creature ----------

    private static IEnumerator RunMpCreature()
    {
        var c = new Checks(MpCreatureName);
        var rig = new Rig(MpCreatureName);
        var a = new Answer();
        var taken = false;
        try
        {
            var p = rig.P;
            yield return ServerDefaults(a);
            if (!a.Ok)
            {
                c.Check(false, $"the server half did not answer: {a.Text}");
                c.Report();
                yield break;
            }
            var tower = rig.Give(IronTower);
            var draugr = rig.Creature("Draugr");
            if (tower == null || draugr == null)
            {
                c.Check(false, "could not add a ShieldIronTower or spawn a Draugr");
                c.Report();
                yield break;
            }
            yield return FreshBash(p, tower);
            SetSkill(p, Skills.SkillType.Blocking, 0f);
            yield return WaitIdle(p);
            FaceClearLane(p, Reach(p, draugr), LaneFan, MpCreatureName, draugr);
            PlaceInFront(p, draugr);
            yield return Seconds(0.8f); // its place reaches the server
            var id = IdText(draugr.m_nview.GetZDO().m_uid);

            // TESTING M05: the Draugr is controlled by another game (here the server, which builds the player's area for
            // it: up to OwnWait seconds, the answer says how long it took).
            yield return Ask("own|" + id, a);
            taken = a.Ok;
            c.Check(a.Ok && a["attacker"] == "1" && a["rules"] == "1", $"the server took the Draugr over ({a.Text.Replace("\n", ", ")})");
            if (!a.Ok)
            {
                c.Report();
                yield break;
            }
            SelfTest.Note(MpCreatureName, $"the dedicated server built the player's area in {a["waited"]} s ({a["objects"]} objects) and controls the Draugr");
            yield return Until(() => !draugr.m_nview.IsOwner(), 6f);
            c.Check(!draugr.m_nview.IsOwner(), "on the client the Draugr is now another game's creature");
            yield return WaitIdle(p);
            yield return WaitCooldown(p);
            Face(p, Flat(draugr.transform.position - p.transform.position));
            yield return Fixed;
            var events = BashWatch.DebugHitEvents;
            var started = p.StartAttack(null, false);
            yield return Until(() => BashWatch.DebugHitEvents > events, 4f);
            var hit = BashWatch.DebugHitEvents > events;
            // Hit goes to the server, its stagger trigger comes back: a server busy with a whole area answers late.
            yield return Until(() => draugr.IsStaggering(), 3f);
            var staggers = draugr.IsStaggering();
            yield return Ask("creature|" + id, a);
            c.Check(started && hit && a.Ok && a["owner"] == "1" && a["lock"] == "1",
                $"the client's bash reaches the server's Draugr and staggers it there: the lock started on the server ({a.Text.Replace("\n", ", ")})");
            c.Check(staggers, "the client sees it stagger");
            var firstLock = ParseFloat(a["lockAge"]);

            // TESTING M13 (the mechanism): the lock live with the creature on its owner, so a second bash inside it,
            // whoever make it, add no heavy stagger.
            yield return WaitIdle(p);
            yield return WaitCooldown(p);
            var fwd = Flat(p.transform.forward);
            var spot = p.transform.position + fwd * (Radius(p) + Radius(draugr) + 0.3f);
            yield return Ask($"place|{id}|{N(spot.x)}|{N(spot.y)}|{N(spot.z)}|{N(-fwd.x)}|{N(-fwd.z)}", a);
            yield return Ask("zero|" + id, a);
            yield return Seconds(0.6f); // its new place reaches the client
            Face(p, Flat(draugr.transform.position - p.transform.position));
            yield return Fixed;
            events = BashWatch.DebugHitEvents;
            started = p.StartAttack(null, false);
            yield return Until(() => BashWatch.DebugHitEvents > events, 4f);
            hit = BashWatch.DebugHitEvents > events;
            yield return Ask("creature|" + id, a);
            var bar = ParseFloat(a["bar"]);
            var lost = ParseFloat(a["max"]) - ParseFloat(a["health"]);
            var age = ParseFloat(a["lockAge"]);
            c.Check(started && hit && a.Ok && lost > 0.001f && bar <= lost + 1.5f && age > firstLock && age < TowerRules.DefaultBashStaggerLock,
                $"a second bash {F(age)} s after the stagger: it lands on the server ({F(lost)} health) but adds no heavy stagger there (bar {F(bar)}); the first lock still runs");
            yield return Ask("release|" + id, a);
            taken = false;
            yield return Until(() => draugr == null || draugr.m_nview.IsOwner(), 8f);
            c.Check(draugr != null && draugr.m_nview.IsOwner(), "the Draugr is the client's again");
            c.Report();
        }
        finally
        {
            if (taken)
            {
                SelfTest.Note(MpCreatureName, $"test ended while the server controlled the Draugr: the server lets it and the player's area go by itself within {F(OwnLimit)} s of taking them");
            }
            rig.Done();
        }
    }

    // ---------- client: tower.mp.join ----------

    private static IEnumerator OwnOn(ConfigEntry<bool> enabled, string key)
    {
        if (!enabled.Value)
        {
            enabled.Value = true;
        }
        yield return Until(() => TowerSync.Active && TowerSync.AppliedKey == key, 8f);
    }

    private static IEnumerator RunMpJoin()
    {
        var c = new Checks(MpJoinName);
        var rig = new Rig(MpJoinName);
        var a = new Answer();
        ConfigEntry<bool> enabled = null;
        try
        {
            var p = rig.P;
            var view = FeatureRegistry.Find(ModInfo.Guid);
            enabled = view != null ? view.Value.Enabled : null;
            yield return ServerDefaults(a);
            if (!a.Ok || enabled == null)
            {
                c.Check(false, $"the server half did not answer or the mod is not registered: {a.Text}");
                c.Report();
                yield break;
            }
            var key = a["own"];
            var tower = rig.Give(IronTower);
            var sword = rig.Give("SwordIron");
            if (tower == null || sword == null)
            {
                c.Check(false, "could not add ShieldIronTower and SwordIron");
                c.Report();
                yield break;
            }
            p.EquipItem(tower);
            yield return Fixed;
            var name = p.GetPlayerName();
            yield return Ask("verdicts|0", a);
            c.Check(a.Ok && a["lines"].Split(';').Any(l => l == name + "|Compatible|"),
                $"the server checked this player when it joined and found it compatible (verdicts so far: {a["lines"]})");

            // TESTING M12: player turn the mod off while connected. Dry run on the server: it decide and log the
            // refusal but do not kick (the kick itself is the probe's each-off scenario).
            yield return Ask("dry|on", a);
            var verdicts = a["verdicts"];
            var warnings = a["warnings"];
            if (!a.Ok || a["dry"] != "1")
            {
                c.Check(false, $"the server did not go into its dry run ({a.Text.Replace('\n', ',')}): the client is not turned off (it would be refused for real)");
                c.Report();
                yield break;
            }
            enabled.Value = false;
            c.Check(view.Value.State == nameof(ModState.Disabled) && view.Value.Status == "Off (disabled in settings)." && tower.m_shared.m_itemType == ItemType.Shield,
                $"mod unticked on the client: '{view.Value.Status}', the held tower is a normal shield at once");
            yield return Seconds(PlayerCheck.GraceSeconds + 1.6f);
            yield return Ask("verdicts|" + verdicts, a);
            var lines = a["lines"];
            verdicts = a["count"];
            yield return Ask("log|" + warnings, a);
            c.Check(lines.Split(';').Any(l => l == name + "|Refuse|has the mod turned off"),
                $"about a second later the server decides to refuse this player: 'has the mod turned off' (verdicts: {lines})");
            c.Check(a["lines"].Contains("Refused " + name) && a["lines"].Contains(": their game has the mod turned off. This server requires " + ModInfo.Name + " on every player"),
                $"the server's log says so ('{a["lines"]}')");
            warnings = a["mark"];
            yield return OwnOn(enabled, key);
            yield return Seconds(PlayerCheck.GraceSeconds + 1.2f);
            yield return Ask("verdicts|" + verdicts, a);
            c.Check(TowerSync.AppliedKey == key && a["lines"].Split(';').LastOrDefault() == name + "|Compatible|", $"ticked again: the server finds the player compatible again ({a["lines"]})");
            verdicts = a["count"];

            // Off and on again inside the grace second: never refused.
            enabled.Value = false;
            yield return Frames(3);
            enabled.Value = true;
            yield return OwnOn(enabled, key);
            yield return Seconds(PlayerCheck.GraceSeconds + 1.2f);
            yield return Ask("verdicts|" + verdicts, a);
            lines = a["lines"];
            c.Check(lines.Length > 0 && !lines.Contains("|Refuse|") && lines.Split(';').All(l => l == name + "|Compatible|"),
                $"unticked and ticked again within the second: the server only finds the player compatible ({lines})");
            verdicts = a["count"];
            yield return Ask("dry|off", a);

            // AllowPlayersWithoutMod on the server: player with the mod off may stay, with normal tower shields.
            yield return Ask("set|AllowPlayersWithoutMod=true", a);
            warnings = a["warnings"];
            if (!a.Ok || a["allow"] != "1")
            {
                c.Check(false, $"the server did not turn AllowPlayersWithoutMod on ({a.Text.Replace('\n', ',')}): the client is not turned off (it would be refused for real)");
                c.Report();
                yield break;
            }
            yield return WaitIdle(p);
            rig.EmptyHands();
            p.EquipItem(tower);
            enabled.Value = false;
            yield return Seconds(PlayerCheck.GraceSeconds + PlayerCheck.DisconnectDelay + 1.5f);
            var both = p.EquipItem(sword);
            c.Check(Connected && tower.m_shared.m_itemType == ItemType.Shield && both && ReferenceEquals(p.m_rightItem, sword) && ReferenceEquals(p.m_leftItem, tower),
                $"AllowPlayersWithoutMod on, mod off on the client: still connected {F(PlayerCheck.GraceSeconds + PlayerCheck.DisconnectDelay + 1.5f)} s later, tower shields are normal shields (sword next to it: {Hands(p)})");
            yield return Ask("verdicts|" + verdicts, a);
            lines = a["lines"];
            verdicts = a["count"];
            yield return Ask("log|" + warnings, a);
            c.Check(lines.Split(';').Any(l => l == name + "|Allowed|has the mod turned off")
                    && a["lines"].Contains(name) && a["lines"].Contains("joined, but their game has the mod turned off. AllowPlayersWithoutMod is on"),
                $"the server lets the player in with a Warning ('{a["lines"]}')");
            rig.EmptyHands();
            yield return OwnOn(enabled, key);
            yield return Ask("set|AllowPlayersWithoutMod=false", a);
            yield return Seconds(PlayerCheck.GraceSeconds + 1.2f);
            yield return Ask("verdicts|" + verdicts, a);
            c.Check(a["lines"].Split(';').LastOrDefault() == name + "|Compatible|", $"AllowPlayersWithoutMod off again: every player is checked again, this one is compatible ({a["lines"]})");

            // TESTING M02 and M14 (the server's side): what it decide and log when the player's game said no hello
            // (no mod) or another network version. The hello it hold is changed for one dry-run check.
            yield return Ask("hello|none", a);
            c.Check(a.Ok && a["verdict"] == name + "|Refuse|does not have the mod" && a["lines"].Contains("Refused " + name)
                    && a["lines"].Contains(": their game does not have the mod. This server requires"),
                $"a player whose game does not have the mod: refused, logged ('{a["verdict"]}'; '{a["lines"]}')");
            yield return Ask("set|AllowPlayersWithoutMod=true", a);
            yield return Ask("hello|none", a);
            c.Check(a.Ok && a["verdict"] == name + "|Allowed|does not have the mod" && a["lines"].Contains("joined, but their game does not have the mod. AllowPlayersWithoutMod is on"),
                $"the same with AllowPlayersWithoutMod on: allowed, with the Warning ('{a["verdict"]}'; '{a["lines"]}')");
            yield return Ask("set|AllowPlayersWithoutMod=false", a);
            var other = ModInfo.NetworkVersion + 1;
            yield return Ask($"hello|1|{other}|{ModInfo.Version}|on", a);
            var reason = $"has another version of the mod (network version {other}, the server has {ModInfo.NetworkVersion})";
            c.Check(a.Ok && a["verdict"] == name + "|Refuse|" + reason && a["lines"].Contains("their game " + reason),
                $"a player whose mod has another network version: refused with that reason ('{a["verdict"]}')");
            yield return ServerDefaults(a);
            yield return Seconds(PlayerCheck.GraceSeconds + PlayerCheck.DisconnectDelay + 1f);
            c.Check(Connected && TowerSync.AppliedKey == key, "at the end the player is still connected and uses the server's rules");
            c.Report();
        }
        finally
        {
            if (enabled != null && !enabled.Value)
            {
                enabled.Value = true;
            }
            rig.Done();
        }
    }

    // ---------- client: tower.mp.toggle ----------

    private static IEnumerator RunMpToggle()
    {
        var c = new Checks(MpToggleName);
        var rig = new Rig(MpToggleName);
        var a = new Answer();
        ConfigEntry<bool> enabled = null;
        var serverOff = false;
        try
        {
            var p = rig.P;
            var view = FeatureRegistry.Find(ModInfo.Guid);
            enabled = view != null ? view.Value.Enabled : null;
            yield return ServerDefaults(a);
            if (!a.Ok || enabled == null)
            {
                c.Check(false, $"the server half did not answer or the mod is not registered: {a.Text}");
                c.Report();
                yield break;
            }
            var key = a["own"];
            var tower = rig.Give(IronTower);
            var sword = rig.Give("SwordIron");
            var snap = SnapshotOf(IronTower);
            if (tower == null || sword == null || snap == null)
            {
                c.Check(false, "could not add ShieldIronTower and SwordIron (or no snapshot)");
                c.Report();
                yield break;
            }
            p.UpdateModifiers();
            var jog0 = p.GetJogSpeedFactor();
            p.EquipItem(tower);
            yield return Guard(p, true);
            c.Check(Brace.Clone != null && !Brace.Clone.m_hidden && tower.m_shared.m_itemType == ItemType.TwoHandedWeaponLeft, "braced with the tower before the server turns the mod off");

            // TESTING M09: the server's owner unticks the mod.
            yield return Ask(ModInfo.Guid + "=off", a, ProbeSetEnabled);
            serverOff = a.Ok;
            var t0 = Time.unscaledTime;
            yield return Until(() => view.Value.State == nameof(ModState.ServerMissing), 10f);
            var took = Time.unscaledTime - t0;
            p.UpdateModifiers();
            var tip = tower.GetTooltip();
            c.Check(a.Ok && view.Value.State == nameof(ModState.ServerMissing) && view.Value.Status.Contains("turned off") && took <= 5f,
                $"server mod off: the client's copy goes inactive {F(took)} s after the server answered, at once for a player ('{view.Value.Status}')");
            c.Check(ReferenceEquals(p.m_leftItem, tower) && IsVanilla(tower.m_shared, snap) && tip.Contains("\n$item_onehanded") && !tip.Contains("Cannot parry"),
                "the client's tower shield is a one-handed shield in place");
            c.Check(!p.GetSEMan().HaveStatusEffect(Brace.Hash) && Near(p.GetJogSpeedFactor(), jog0 + snap.MovementModifier, 0.001f),
                $"the Braced effect and the slowdown are gone (jog factor {F(p.GetJogSpeedFactor())})");
            yield return WaitIdle(p);
            var added = p.EquipItem(sword);
            c.Check(added && ReferenceEquals(p.m_rightItem, sword) && ReferenceEquals(p.m_leftItem, tower), $"a sword goes next to it ({Hands(p)})");

            // The server's owner ticks it again while the client hold both.
            yield return Ask(ModInfo.Guid + "=on", a, ProbeSetEnabled);
            serverOff = !a.Ok;
            t0 = Time.unscaledTime;
            yield return Until(() => view.Value.IsActive && TowerSync.AppliedKey == key, 12f);
            took = Time.unscaledTime - t0;
            yield return Fixed;
            c.Check(a.Ok && view.Value.IsActive && ServerRules.UsingServer && TowerSync.AppliedKey == key,
                $"server mod on again: the client's copy is active with the server's rules {F(took)} s later, without rejoining");
            c.Check(p.m_rightItem == null && !sword.m_equipped && ReferenceEquals(p.m_leftItem, tower) && IsTower(tower.m_shared, snap, TowerRules.Defaults()),
                $"the tower shield is two-handed again, the sword put away ({Hands(p)})");
            yield return Seconds(PlayerCheck.GraceSeconds + PlayerCheck.DisconnectDelay + 1.5f);
            c.Check(Connected && view.Value.IsActive, "the client is not refused after the server turned the mod on again (its own copy stayed on)");

            // TESTING T18 through the real setting (what the MC Mods tick writes): the client's own Enabled off, then on
            // again inside the server's grace second (TESTING M12: such a player stay).
            yield return Guard(p, true);
            c.Check(Brace.Clone != null && !Brace.Clone.m_hidden, "braced again");
            enabled.Value = false;
            p.UpdateModifiers();
            tip = tower.GetTooltip();
            c.Check(view.Value.State == nameof(ModState.Disabled) && view.Value.Status == "Off (disabled in settings).",
                $"own Enabled off: the mod says '{view.Value.Status}'");
            c.Check(ReferenceEquals(p.m_leftItem, tower) && IsVanilla(tower.m_shared, snap) && tip.Contains("\n$item_onehanded") && !tip.Contains("$item_twohanded")
                    && tip.Contains($"$item_blockarmor: <color=orange>{snap.BlockPower}</color>") && !p.GetSEMan().HaveStatusEffect(Brace.Hash),
                "own Enabled off: at once a one-handed shield in place with normal block armor, the Braced effect gone");
            var punched = p.StartAttack(null, false);
            var punch = punched ? p.m_currentAttack : null;
            c.Check(punched && punch != null && p.m_unarmedWeapon != null && ReferenceEquals(punch.GetWeapon(), p.m_unarmedWeapon.m_itemData), "own Enabled off: attack punches");
            enabled.Value = true;
            yield return Until(() => view.Value.IsActive && TowerSync.AppliedKey == key, 8f);
            yield return Fixed;
            c.Check(view.Value.IsActive && IsTower(tower.m_shared, snap, TowerRules.Defaults()), "own Enabled on again: the tower is two-handed again with the server's rules");
            yield return Seconds(PlayerCheck.GraceSeconds + PlayerCheck.DisconnectDelay + 1.5f);
            c.Check(Connected && view.Value.IsActive, "off and on again within the grace second: the player stays connected");
            c.Report();
        }
        finally
        {
            if (enabled != null && !enabled.Value)
            {
                enabled.Value = true;
            }
            if (serverOff)
            {
                SelfTest.Note(MpToggleName, "test ended while the server's mod was off: the probe's own server-toggle test turns it on again");
            }
            rig.Done();
        }
    }

    // ---------- client: tower.mp.no-server (scenario vanilla-server) ----------

    private static IEnumerator RunMpNoServer()
    {
        var c = new Checks(MpNoServerName);
        var rig = new Rig(MpNoServerName);
        try
        {
            var p = rig.P;
            yield return null;
            var view = FeatureRegistry.Find(ModInfo.Guid);
            // TESTING M01.
            c.Check(view != null && view.Value.State == nameof(ModState.ServerMissing) && view.Value.Status.StartsWith("Inactive: the server does not have this mod", StringComparison.Ordinal),
                $"on a server without the mod the panel line is '{(view != null ? view.Value.Status : "not registered")}'");
            c.Check(!TowerSync.Active && TowerSync.Applied == null && TowerCatalog.Count == 0, "no tower rules are applied");
            var prefab = SharedOf(IronTower);
            c.Check(prefab != null && prefab.m_itemType == ItemType.Shield && Near(prefab.m_blockPower, 52f),
                $"the ShieldIronTower prefab is the normal game's (type {(prefab != null ? prefab.m_itemType.ToString() : "missing")}, block armor {(prefab != null ? F(prefab.m_blockPower) : "?")})");
            var tower = rig.Give(IronTower);
            var sword = rig.Give("SwordIron");
            if (tower == null || sword == null)
            {
                c.Check(false, "could not add ShieldIronTower and SwordIron");
                c.Report();
                yield break;
            }
            var tip = tower.GetTooltip();
            c.Check(tip.Contains("\n$item_onehanded") && !tip.Contains("Cannot parry") && !tip.Contains("Bash stagger"), $"its tooltip is the normal one ('{OneLine(tip)}')");
            p.EquipItem(sword);
            p.EquipItem(tower);
            c.Check(ReferenceEquals(p.m_rightItem, sword) && ReferenceEquals(p.m_leftItem, tower) && !tower.IsTwoHanded(), $"sword and tower shield are held together ({Hands(p)})");
            p.UnequipItem(sword, false);
            yield return Fixed;
            c.Check(!p.GetSEMan().HaveStatusEffect(Brace.Hash), "no Braced effect");
            var punched = p.StartAttack(null, false);
            var punch = punched ? p.m_currentAttack : null;
            c.Check(punched && punch != null && p.m_unarmedWeapon != null && ReferenceEquals(punch.GetWeapon(), p.m_unarmedWeapon.m_itemData),
                "attack with only the tower shield punches: no bash");
            var until = Time.time + 3f;
            while (Time.time < until && p.InAttack())
            {
                yield return null;
            }
            c.Report();
        }
        finally
        {
            rig.Done();
        }
    }
}
#endif
