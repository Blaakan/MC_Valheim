using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MC.Exploration.SailingSkillMod.Patches;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;
#endif

namespace MC.Exploration.SailingSkillMod;

// Debug build only (calls vanish in Release). In-world self tests, run by world probe (tools/Test-InWorld.ps1,
// -Mod Sailing). Design 7.5:
//   sailing.network  rules on the wire, pending pick, push debounce, join check verdicts (pure)
//   sailing.math     wind factor (vanilla at level 0, grow with level, no-go cone), same top speed with faster
//                    approach, sail boost along the bow only, brake, catch-up, damage, scaled field put back only
//                    while it hold my value (both patch orders), XP per sample, cheat names, compat markers (pure)
//   sailing.skill    id formula, registered (IsSkillValid, def, words, icon), reading never add the skill, save/load
//                    round trip, console cheats (sailing, all, clamp, reset), death penalty, Tab list, NOTE dumps
//                    (vanilla skill steps, death factor, skill cap, Rested bonus)
//   sailing.helm     ship spawned above the spawn (kinematic, no water needed), player at its helm: owner read the
//                    helmsman's level (real skill and override), level published on player ZDO, owner step factors
//                    and fields put back (no compounding), sail force along the bow x1.5 (sideways and stored force
//                    vanilla), wind factor, rudder speed, map reveal radius, damage cut (player hit, capsized not
//                    cut), XP for 30 m at the helm, none when not steering
//   sailing.pending  same rig: rules pending = vanilla wind, no owner step factors, vanilla rudder, reveal and damage,
//                    no XP; rules back = all back
//   sailing.ship     NOTE dump of every ship prefab and minimap values; then open sea found with WorldGenerator,
//                    player teleported there, Karve spawned and steered: paddle acceleration and brake measured at
//                    level 0 and 100 (paddle compare mean speed from 0.5 to 1 s, need 15% more; speeds are the level
//                    part along the bow: Ship.GetSpeed carries the bobbing on waves), sail speed checked when wind
//                    enough (else NOTE); player back at the spawn. No sea in reach = NOTE, no measure.
// More tests live in the other SelfTests.*.cs files (same class, Debug only):
//   SelfTests.Tools.cs        log watcher, HUD message spy, Skills panel reader, console runner, wind setter
//   SelfTests.Skill.cs        sailing.panel (console, Skills panel, save data, death penalty, language)
//   SelfTests.Air.cs          sailing.xp, sailing.hud, sailing.sail, sailing.map, sailing.damage, sailing.ram, sailing.toggle,
//                             sailing.bug.wind-icon-foreign, sailing.bug.sail-overshoot, sailing.cleanlog (ship in
//                             the air above the spawn)
//   SelfTests.Sea.cs          sailing.reach, sailing.upwind, sailing.paddle, sailing.heel, sailing.crew (real water,
//                             fixed wind)
//   SelfTests.Multiplayer.cs  sailing.mp.* client tests and their server half (tools/Test-Multiplayer.ps1)
// Me force rules only with ServerRules.TestRules / TestPending and the level with HelmSkill.TestLocalLevel: never
// config. Character skills changed in a test are put back from a saved copy (Skills.Save / Load). Every ship me spawn is
// destroyed, the player put back where he was.
internal static partial class SelfTests
{
    private const string NetworkName = "sailing.network";
    private const string MathName = "sailing.math";
    private const string SkillName = "sailing.skill";
    private const string HelmName = "sailing.helm";
    private const string PendingName = "sailing.pending";
    private const string ShipName = "sailing.ship";

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        SelfTest.Register(NetworkName, RunNetwork);
        SelfTest.Register(MathName, RunMath);
        SelfTest.Register(SkillName, RunSkill);
        SelfTest.Register(HelmName, RunHelm);
        SelfTest.Register(PendingName, RunPending);
        SelfTest.Register(ShipName, RunShip);
        RegisterMore();
#endif
    }

    // Plugin BindConfig, once per process, feature on or off: log watcher (counts what this mod logs) and the tests
    // that run while the feature is off (server without the mod).
    [Conditional("DEBUG")]
    internal static void RegisterAlways()
    {
#if DEBUG
        StartWatch();
        RegisterAlwaysMultiplayer();
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        SelfTest.Unregister(NetworkName);
        SelfTest.Unregister(MathName);
        SelfTest.Unregister(SkillName);
        SelfTest.Unregister(HelmName);
        SelfTest.Unregister(PendingName);
        SelfTest.Unregister(ShipName);
        UnregisterMore();
        ClearOverrides();
#endif
    }

#if DEBUG
    // Tests of the other SelfTests.*.cs files, after the six above. sailing.cleanlog last: it looks back at everything
    // the mod logged so far.
    private static void RegisterMore()
    {
        SelfTest.Register(PanelName, RunPanel);
        SelfTest.Register(XpName, RunXp);
        SelfTest.Register(HudName, RunHud);
        SelfTest.Register(SailName, RunSail);
        SelfTest.Register(MapName, RunMap);
        SelfTest.Register(DamageName, RunDamage);
        SelfTest.Register(RamName, RunRam);
        SelfTest.Register(ToggleName, RunToggle);
        SelfTest.Register(ReachName, RunReach);
        SelfTest.Register(UpwindName, RunUpwind);
        SelfTest.Register(PaddleName, RunPaddle);
        SelfTest.Register(HeelName, RunHeel);
        SelfTest.Register(CrewName, RunCrew);
        SelfTest.Register(HudForeignName, RunHudForeign);
        SelfTest.Register(BugSailName, RunBugSailOvershoot);
        SelfTest.Register(CleanLogName, RunCleanLog);
        RegisterMultiplayer();
    }

    private static void UnregisterMore()
    {
        SelfTest.Unregister(PanelName);
        SelfTest.Unregister(XpName);
        SelfTest.Unregister(HudName);
        SelfTest.Unregister(SailName);
        SelfTest.Unregister(MapName);
        SelfTest.Unregister(DamageName);
        SelfTest.Unregister(RamName);
        SelfTest.Unregister(ToggleName);
        SelfTest.Unregister(ReachName);
        SelfTest.Unregister(UpwindName);
        SelfTest.Unregister(PaddleName);
        SelfTest.Unregister(HeelName);
        SelfTest.Unregister(CrewName);
        SelfTest.Unregister(HudForeignName);
        SelfTest.Unregister(BugSailName);
        SelfTest.Unregister(CleanLogName);
        UnregisterMultiplayer();
    }

    private static void ClearOverrides()
    {
        ServerRules.TestPending = false;
        ServerRules.TestRules = null;
        HelmSkill.TestLocalLevel = null;
        PlayerCheck.TestAllowWithoutMod = null;
    }

    // ---------- helpers ----------

    private sealed class Checks
    {
        private readonly string _name;
        private readonly List<string> _failures = new List<string>();
        private int _count;

        internal Checks(string name) => _name = name;

        internal bool Check(bool ok, string what)
        {
            _count++;
            if (!ok)
            {
                _failures.Add(what);
            }
            return ok;
        }

        internal void Note(string detail) => SelfTest.Note(_name, detail);

        internal void Report(string extra = "")
        {
            if (_failures.Count == 0)
            {
                SelfTest.Pass(_name, $"{_count} checks OK{extra}");
            }
            else
            {
                SelfTest.Fail(_name, $"{_failures.Count} of {_count} checks failed: {string.Join("; ", _failures.ToArray())}");
            }
        }
    }

    // Result of a nested coroutine.
    private sealed class Box
    {
        internal bool Ok;
        internal Vector3 Point;
    }

    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static bool Near(float a, float b, float tolerance = 1e-4f) => Mathf.Abs(a - b) <= tolerance;

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    private static void MovePlayer(Player player, Vector3 position)
    {
        player.transform.position = position;
        var body = player.m_body;
        if (body != null)
        {
            body.position = position;
            body.linearVelocity = Vector3.zero;
        }
    }

    private static IEnumerator FixedSteps(int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return new WaitForFixedUpdate();
        }
    }

    // Karve (not in .ref; sailing.ship NOTE found it in 1.0.16 with Raft, Trailership, VikingShip, VikingShip_Ashlands), else
    // the first network prefab with a Ship.
    private static GameObject ShipPrefab()
    {
        var scene = ZNetScene.instance;
        if (scene == null)
        {
            return null;
        }
        var karve = scene.GetPrefab("Karve");
        if (karve != null && karve.GetComponent<Ship>() != null)
        {
            return karve;
        }
        foreach (var prefab in scene.m_prefabs)
        {
            if (prefab != null && prefab.GetComponent<Ship>() != null)
            {
                return prefab;
            }
        }
        return null;
    }

    private static void SetRotation(Ship ship, Quaternion rotation)
    {
        ship.transform.rotation = rotation;
        var body = ship.m_body;
        if (body != null)
        {
            body.rotation = rotation;
        }
    }

    // One spawned ship with the local player at its helm, and everything to put back.
    private sealed class ShipRig
    {
        internal readonly Player Player;
        internal readonly Vector3 Origin;
        internal readonly Quaternion OriginRotation;
        internal GameObject Go;
        internal Ship Boat;
        internal Ship PrefabShip;
        internal WearNTear Wear;
        internal ZDO Zdo;
        private ZPackage _skills;

        private ShipRig(Player player)
        {
            Player = player;
            Origin = player.transform.position;
            OriginRotation = player.transform.rotation;
        }

        internal static ShipRig Create(string test)
        {
            var player = Player.m_localPlayer;
            if (player == null || ZNetScene.instance == null || ZDOMan.instance == null)
            {
                SelfTest.Fail(test, "no local player or no world");
                return null;
            }
            return new ShipRig(player);
        }

        internal bool Spawn(Vector3 position, Quaternion rotation, bool kinematic)
        {
            var prefab = ShipPrefab();
            if (prefab == null)
            {
                return false;
            }
            PrefabShip = prefab.GetComponent<Ship>();
            Go = Object.Instantiate(prefab, position, rotation);
            Boat = Go.GetComponent<Ship>();
            Wear = Go.GetComponent<WearNTear>();
            var nview = Go.GetComponent<ZNetView>();
            Zdo = nview != null ? nview.GetZDO() : null;
            if (Boat != null && kinematic && Boat.m_body != null)
            {
                Boat.m_body.isKinematic = true;
            }
            return Boat != null && Zdo != null;
        }

        // Owner side of vanilla RequestControl (helm user in ZDO) + helmsman side of RequestRespons (doodad control,
        // attached to the helm with onShip).
        internal bool TakeHelm()
        {
            var controls = Boat.m_shipControlls;
            if (controls == null || controls.m_attachPoint == null)
            {
                return false;
            }
            Zdo.Set(ZDOVars.s_user, Player.GetPlayerID());
            Player.StartDoodadControl(controls);
            Player.AttachStart(controls.m_attachPoint, null, false, false, true, controls.m_attachAnimation,
                controls.m_detachOffset);
            return true;
        }

        // Player in the ship's list (trigger volume) and local ship set, within a few seconds.
        internal IEnumerator WaitAboard(Box result, float seconds = 3f)
        {
            var until = Time.time + seconds;
            while (Time.time < until && !(Boat.IsPlayerInBoat(Player) && Ship.GetLocalShip() == Boat))
            {
                yield return new WaitForFixedUpdate();
            }
            result.Ok = Boat.IsPlayerInBoat(Player) && Ship.GetLocalShip() == Boat;
        }

        // Normal end: off the helm, player moved off the ship, volume left (vanilla trigger exit), ship destroyed.
        internal IEnumerator Leave(Vector3 playerTo)
        {
            ReleaseHelm();
            MovePlayer(Player, playerTo);
            for (var i = 0; i < 6 && Boat != null && Boat.IsPlayerInBoat(Player); i++)
            {
                yield return new WaitForFixedUpdate();
            }
            DestroyShip();
        }

        internal void ReleaseHelm()
        {
            if (Player.GetDoodadController() != null)
            {
                Player.StopDoodadControl();
            }
            if (Player.IsAttached())
            {
                Player.AttachStop();
            }
        }

        // Also the abnormal path (finally): player still in the volume = take him out by hand (a destroyed ship never
        // send the trigger exit), then the ship and its ZDO go.
        internal void DestroyShip()
        {
            if (Boat != null)
            {
                if (Boat.IsPlayerInBoat(Player))
                {
                    Boat.m_players.Remove(Player);
                    Player.InNumShipVolumes = Mathf.Max(0, Player.InNumShipVolumes - 1);
                }
                Ship.s_currentShips.Remove(Boat);
            }
            var scene = ZNetScene.instance;
            if (Go != null && scene != null)
            {
                scene.Destroy(Go);
            }
            else if (Zdo != null && Zdo.IsValid() && ZDOMan.instance != null)
            {
                // GameObject gone (zone unloaded): the saved ship would stay in the world.
                Zdo.SetOwner(ZDOMan.GetSessionID());
                ZDOMan.instance.DestroyZDO(Zdo);
            }
            Go = null;
            Boat = null;
            Zdo = null;
        }

        internal void BackupSkills()
        {
            if (_skills != null)
            {
                return;
            }
            _skills = new ZPackage();
            Player.GetSkills().Save(_skills);
        }

        internal void RestoreSkills()
        {
            if (_skills == null)
            {
                return;
            }
            _skills.SetPos(0);
            Player.GetSkills().Load(_skills);
            _skills = null;
            HelmSkill.Invalidate();
        }

        // finally: never throw, never yield.
        internal void Restore()
        {
            Safe(ClearOverrides);
            Safe(RestoreSkills);
            Safe(ReleaseHelm);
            Safe(DestroyShip);
            Safe(() =>
            {
                if (Flat(Player.transform.position - Origin).magnitude < 60f)
                {
                    if (!Player.IsTeleporting())
                    {
                        MovePlayer(Player, Origin);
                    }
                    return;
                }
                // Far away (sea test cut short): vanilla distant teleport hold the player until the spawn area is there.
                if (!Player.IsTeleporting())
                {
                    Player.m_teleportCooldown = 10f;
                    Player.TeleportTo(Origin, OriginRotation, true);
                }
            });
        }

        private static void Safe(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Log.Error($"Sailing self test clean-up failed: {e}");
            }
        }
    }

    // Wind factor with the bow 36.87 degrees off the wind (d = 0.8: vanilla gives 0 there, Sailing 100 gives 0.92)
    // and dead downwind (d = -1: vanilla 0.7, Sailing 100 0.9). Expected: formula at the given s (0 = vanilla).
    private static void CheckWind(Checks c, Ship ship, SailingRules rules, float s, string phase)
    {
        var env = EnvMan.instance;
        var wind = env != null ? Flat(env.GetWindDir()) : Vector3.zero;
        if (wind.sqrMagnitude < 1e-4f)
        {
            c.Note($"{phase}: no wind direction: wind factor not checked");
            return;
        }
        wind.Normalize();
        var keep = ship.transform.rotation;
        try
        {
            var close = -(Quaternion.Euler(0f, Mathf.Acos(0.8f) * Mathf.Rad2Deg, 0f) * wind);
            foreach (var forward in new[] { close, wind })
            {
                SetRotation(ship, Quaternion.LookRotation(forward, Vector3.up));
                var d = Vector3.Dot(env.GetWindDir(), -ship.transform.forward);
                var got = ship.GetWindAngleFactor();
                var expected = SailMath.WindFactor(d, s, rules.WindFloorAtMax, rules.NoGoShiftAtMax);
                c.Check(Near(got, expected, 1e-3f), $"{phase}: wind factor {F(got)} at d {F(d)}, expected {F(expected)} "
                                                     + $"(vanilla {F(SailMath.VanillaWindFactor(d))})");
            }
        }
        finally
        {
            SetRotation(ship, keep);
        }
    }

    // Sail force at Sailing 100 (local player at the helm, TestLocalLevel 100): one GetSailForce as the owner step make
    // it (ShipTick set) against the same call outside a step (no bonus), same frame, same stored force and wind. Sail
    // response off (rules copy) so only the acceleration part show: along the bow x accel, sideways and up parts the
    // same, stored m_sailForce not boosted. Ship force and SmoothDamp velocity put back after.
    private static void CheckSailBoost(Checks c, Ship ship, SailingRules rules)
    {
        if (EnvMan.instance == null)
        {
            c.Note("no EnvMan: sail force not checked");
            return;
        }
        var keepForce = ship.m_sailForce;
        var keepVelocity = ship.m_windChangeVelocity;
        var forward = ship.transform.forward;
        var right = ship.transform.right;
        var up = ship.transform.up;
        var start = forward * 0.02f + right * 0.03f;
        var accel = SailMath.Scale(rules.AccelerationBonusAtMax, 1f);
        var noResponse = new SailingRules
        {
            WindFloorAtMax = rules.WindFloorAtMax,
            NoGoShiftAtMax = rules.NoGoShiftAtMax,
            SailResponseAtMax = 0f,
        };
        try
        {
            ship.m_sailForce = start;
            ship.m_windChangeVelocity = Vector3.zero;
            var plain = ship.GetSailForce(1f, Time.fixedDeltaTime);
            var plainStored = ship.m_sailForce;
            ship.m_sailForce = start;
            ship.m_windChangeVelocity = Vector3.zero;
            Vector3 boosted;
            ShipTick.Begin(ship, 1f, noResponse, accel);
            try
            {
                boosted = ship.GetSailForce(1f, Time.fixedDeltaTime);
            }
            finally
            {
                ShipTick.End();
            }
            var stored = ship.m_sailForce;
            float Along(Vector3 v, Vector3 axis) => Vector3.Dot(v, axis);
            c.Check(Near(Along(plain, forward), Along(plainStored, forward), 1e-6f) && (stored - plainStored).magnitude < 1e-6f,
                $"sail force: outside a step the force is not the stored one, or the step stored a boosted force "
                + $"(stored {F(Along(stored, forward))} along the bow, expected {F(Along(plainStored, forward))})");
            c.Check(Near(Along(boosted, forward), Along(plain, forward) * accel, 1e-5f)
                    && Near(Along(boosted, right), Along(plain, right), 1e-6f) && Near(Along(boosted, up), Along(plain, up), 1e-6f),
                $"sail force at Sailing 100: along the bow {F(Along(boosted, forward))} (expected {F(Along(plain, forward) * accel)}),"
                + $" sideways {F(Along(boosted, right))} (expected {F(Along(plain, right))})");
        }
        finally
        {
            ship.m_sailForce = keepForce;
            ship.m_windChangeVelocity = keepVelocity;
        }
    }

    // Rudder value after one ApplyControlls with full right, from centre (vanilla: 0.5 * rudderSpeed * fixed dt).
    private static float RudderStep(Ship ship)
    {
        ship.m_rudderValue = 0f;
        ship.ApplyControlls(new Vector3(1f, 0f, 0f));
        var step = ship.m_rudderValue;
        ship.m_rudderValue = 0f;
        return step;
    }

    // Radius the next Explore uses (vanilla timer forced), and the radius left on the minimap after the call.
    private static bool RevealRadius(Player player, out float used, out float after)
    {
        used = -1f;
        after = -1f;
        var map = Minimap.instance;
        if (map == null)
        {
            return false;
        }
        MinimapPatches.LastRadius = -1f;
        map.m_exploreTimer = map.m_exploreInterval + 1f;
        map.UpdateExplore(0f, player);
        used = MinimapPatches.LastRadius;
        after = map.m_exploreRadius;
        return used > 0f;
    }

    // Health a 10 blunt hit takes off the ship through the vanilla owner path (RPC_Damage), health put back.
    private static float DamageTaken(ShipRig rig, HitData.HitType type, Character attacker)
    {
        var wear = rig.Wear;
        var zdo = wear.m_nview.GetZDO();
        zdo.Set(ZDOVars.s_health, wear.m_health);
        var hit = new HitData
        {
            m_hitType = type,
            m_point = rig.Boat.transform.position,
            m_dir = Vector3.up,
            m_toolTier = 100,
        };
        hit.m_damage.m_blunt = 10f;
        if (attacker != null)
        {
            hit.SetAttacker(attacker);
        }
        wear.RPC_Damage(0L, hit);
        var drop = wear.m_health - zdo.GetFloat(ZDOVars.s_health, wear.m_health);
        zdo.Set(ZDOVars.s_health, wear.m_health);
        return drop;
    }

    // Owner-step fields equal the prefab's between steps (scaled only inside a step).
    private static bool FieldsLikePrefab(Ship ship, Ship prefab, out string text)
    {
        text = $"sail {F(ship.m_sailForceFactor)}/{F(prefab.m_sailForceFactor)}, paddle {F(ship.m_backwardForce)}/"
               + $"{F(prefab.m_backwardForce)}, drag {F(ship.m_dampingForward)}/{F(prefab.m_dampingForward)}, steer "
               + $"{F(ship.m_stearVelForceFactor)}/{F(prefab.m_stearVelForceFactor)}, paddle steer {F(ship.m_stearForce)}/"
               + $"{F(prefab.m_stearForce)}, rudder {F(ship.m_rudderSpeed)}/{F(prefab.m_rudderSpeed)}";
        return ship.m_sailForceFactor == prefab.m_sailForceFactor && ship.m_backwardForce == prefab.m_backwardForce
               && ship.m_dampingForward == prefab.m_dampingForward
               && ship.m_stearVelForceFactor == prefab.m_stearVelForceFactor && ship.m_stearForce == prefab.m_stearForce
               && ship.m_rudderSpeed == prefab.m_rudderSpeed;
    }

    // Common start of sailing.helm and sailing.pending: kinematic ship 30 m above the player, player at its helm.
    private static IEnumerator SetUpAirShip(ShipRig rig, Checks c, Box ok)
    {
        ok.Ok = false;
        if (!c.Check(rig.Spawn(rig.Origin + Vector3.up * 30f, Quaternion.identity, kinematic: true),
                "no ship prefab (Karve) or it did not spawn"))
        {
            yield break;
        }
        // Ship.Start (vanilla RPCs) and ShipControlls.Awake run on the next frames.
        yield return null;
        yield return null;
        if (!c.Check(rig.TakeHelm(), "the ship has no helm attach point"))
        {
            yield break;
        }
        var aboard = new Box();
        yield return rig.WaitAboard(aboard);
        if (!c.Check(aboard.Ok, "the player at the helm never entered the ship's volume"))
        {
            yield break;
        }
        c.Check(rig.Boat.IsOwner(), "this game does not own the spawned ship");
        c.Check(ReferenceEquals(rig.Player.GetControlledShip(), rig.Boat), "the player does not control the ship");
        c.Check(ReferenceEquals(HelmSkill.Helmsman(rig.Boat), rig.Player), "the local player is not seen as the helmsman");
        c.Check(MinimapPatches.Aboard(rig.Player), "the player at the helm does not count as aboard for the map");
        ok.Ok = true;
    }

    // ---------- sailing.network (pure) ----------

    private static IEnumerator RunNetwork()
    {
        var c = new Checks(NetworkName);
        var custom = new SailingRules
        {
            XpPerKm = 75f, WindFloorAtMax = 0.85f, NoGoShiftAtMax = 0.05f, AccelerationBonusAtMax = 0.75f,
            SailResponseAtMax = 2f, TurnBonusAtMax = 0.25f, RudderBonusAtMax = 1f, BrakeAtMax = 1.2f,
            RevealBonusAtMax = 2f, DamageReductionAtMax = 0.3f,
        };
        var pkg = new ZPackage();
        custom.Write(pkg);
        pkg.SetPos(0);
        c.Check(SailingRules.TryRead(pkg, out var back, out var clamped) && !clamped && back.Describe() == custom.Describe()
                && back.XpPerKm == 75f && back.DamageReductionAtMax == 0.3f && !back.IsPending,
            "rules survive the wire unchanged");

        var wild = new SailingRules
        {
            XpPerKm = -5f, WindFloorAtMax = 0.2f, NoGoShiftAtMax = 0.5f, AccelerationBonusAtMax = float.NaN,
            SailResponseAtMax = 99f, TurnBonusAtMax = -1f, RudderBonusAtMax = float.PositiveInfinity, BrakeAtMax = 7f,
            RevealBonusAtMax = 10f, DamageReductionAtMax = 1.5f,
        };
        pkg = new ZPackage();
        wild.Write(pkg);
        pkg.SetPos(0);
        var d = SailingRules.Default;
        c.Check(SailingRules.TryRead(pkg, out back, out clamped) && clamped && back.XpPerKm == 0f
                && back.WindFloorAtMax == SailingRules.WindFloorMin && back.NoGoShiftAtMax == SailingRules.NoGoShiftMax
                && back.AccelerationBonusAtMax == d.AccelerationBonusAtMax
                && back.SailResponseAtMax == SailingRules.SailResponseMax && back.TurnBonusAtMax == 0f
                && back.RudderBonusAtMax == d.RudderBonusAtMax && back.BrakeAtMax == SailingRules.BrakeMax
                && back.RevealBonusAtMax == SailingRules.RevealBonusMax
                && back.DamageReductionAtMax == SailingRules.DamageReductionMax,
            "out-of-range, NaN and infinite values from the wire are pulled into range");

        pkg = new ZPackage();
        pkg.Write(SailingRules.Layout + 1);
        pkg.Write(1f);
        pkg.SetPos(0);
        c.Check(!SailingRules.TryRead(pkg, out _, out _), "unknown rules layout refused");
        pkg = new ZPackage();
        pkg.Write(SailingRules.Layout);
        pkg.Write(3f);
        pkg.SetPos(0);
        c.Check(!SailingRules.TryRead(pkg, out _, out _), "cut-off rules package refused");
        var p = SailingRules.Pending;
        c.Check(p.IsPending && !SailingRules.Default.IsPending && !SailingRules.Own().IsPending,
            "only the built-in Pending rules are pending");
        c.Check(p.XpPerKm == 0f && p.WindFloorAtMax == SailMath.VanillaFloor && p.NoGoShiftAtMax == 0f
                && p.AccelerationBonusAtMax == 0f && p.SailResponseAtMax == 0f && p.TurnBonusAtMax == 0f
                && p.RudderBonusAtMax == 0f && p.BrakeAtMax == 0f && p.RevealBonusAtMax == 0f
                && p.DamageReductionAtMax == 0f,
            "Pending rules are not neutral");

        // A server (this single-player world is one) never takes rules from a peer.
        var current = ServerRules.Current;
        pkg = new ZPackage();
        custom.Write(pkg);
        pkg.SetPos(0);
        c.Check(!ServerRules.Receive(pkg) && ReferenceEquals(ServerRules.Current, current) && !ServerRules.UsingServer
                && !ServerRules.IsPending,
            "single player / server took rules from a peer or is pending");

        var own = new SailingRules();
        c.Check(ReferenceEquals(ServerRules.Select(false, custom, own), own), "single player, host, server: own rules");
        c.Check(ReferenceEquals(ServerRules.Select(false, null, own), own), "server without peer rules: own rules");
        c.Check(ReferenceEquals(ServerRules.Select(true, custom, own), custom), "client with server rules: the server's");
        c.Check(ServerRules.Select(true, null, own).IsPending, "client without (readable) server rules: pending");
        c.Check(!ServerRules.Settled(10f, 10f - ServerRules.PushDelay + 0.1f) && ServerRules.Settled(10f, 10f - ServerRules.PushDelay)
                && ServerRules.Settled(10f, float.NegativeInfinity),
            "push debounce: not settled within PushDelay of a change, settled after it and at start");

        c.Check(PlayerCheck.Decide(true, true, true, false, true, false) == JoinVerdict.Compatible, "compatible -> Compatible");
        c.Check(PlayerCheck.Decide(true, true, true, false, false, false) == JoinVerdict.Refuse,
            "not compatible (no mod, turned off, other network version) -> Refuse");
        c.Check(PlayerCheck.Decide(true, true, true, false, false, true) == JoinVerdict.Allowed,
            "not compatible with AllowPlayersWithoutMod -> Allowed");
        c.Check(PlayerCheck.Decide(false, true, true, false, false, false) == JoinVerdict.Skip, "not a server -> Skip");
        c.Check(PlayerCheck.Decide(true, false, true, false, false, false) == JoinVerdict.Skip, "not connected -> Skip");
        c.Check(PlayerCheck.Decide(true, true, false, false, false, false) == JoinVerdict.Skip, "not ready -> Skip");
        c.Check(PlayerCheck.Decide(true, true, true, true, false, false) == JoinVerdict.Skip, "already being kicked -> Skip");
        c.Note("rules in force: " + ServerRules.Current.Describe());
        c.Report();
        yield break;
    }

    // ---------- sailing.math (pure) ----------

    private static IEnumerator RunMath()
    {
        var c = new Checks(MathName);
        var r = new SailingRules();

        // Vanilla wind factor (research table: dead run 0.7, beam reach 1, 120 deg 0.85, 142 deg 0.18, no-go 0).
        c.Check(Near(SailMath.VanillaWindFactor(-1f), 0.7f) && Near(SailMath.VanillaWindFactor(0f), 1f)
                && Near(SailMath.VanillaWindFactor(0.5f), 0.85f)
                && Near(SailMath.VanillaWindFactor(-Mathf.Cos(142f * Mathf.Deg2Rad)), 0.183f, 0.005f)
                && SailMath.VanillaWindFactor(0.8f) == 0f && SailMath.VanillaWindFactor(1f) == 0f,
            "vanilla wind factor does not match Ship.GetWindAngleFactor's known values");
        var equal = true;
        var monotonic = true;
        var bounded = true;
        for (var d = -1f; d <= 1.0001f; d += 0.05f)
        {
            equal &= SailMath.WindFactor(d, 0f, r.WindFloorAtMax, r.NoGoShiftAtMax) == SailMath.VanillaWindFactor(d);
            var last = -1f;
            for (var s = 0f; s <= 1.0001f; s += 0.1f)
            {
                var f = SailMath.WindFactor(d, s, r.WindFloorAtMax, r.NoGoShiftAtMax);
                monotonic &= f >= last - 1e-6f;
                bounded &= f <= 1f + 1e-6f && f >= 0f;
                last = f;
            }
        }
        c.Check(equal, "wind factor at Sailing 0 is not exactly vanilla");
        c.Check(monotonic, "wind factor drops somewhere as Sailing rises");
        c.Check(bounded, "wind factor leaves 0..1");
        c.Check(Near(SailMath.WindFactor(-1f, 1f, r.WindFloorAtMax, r.NoGoShiftAtMax), 0.9f)
                && Near(SailMath.WindFactor(0.8f, 1f, r.WindFloorAtMax, r.NoGoShiftAtMax), 0.92f)
                && SailMath.WindFactor(0.9f, 1f, r.WindFloorAtMax, r.NoGoShiftAtMax) < 1e-4f
                && Near(SailMath.WindFactor(0.8f, 0.5f, r.WindFloorAtMax, r.NoGoShiftAtMax), 0.84f),
            "defaults: at Sailing 100 dead run 0.9, 36.9 deg off the bow 0.92, 25.8 deg still nothing; at Sailing 50 36.9 deg 0.84");
        c.Check(Near(SailMath.NoGoHalfAngle(0f, r.NoGoShiftAtMax), 36.87f, 0.05f)
                && Near(SailMath.NoGoHalfAngle(1f, r.NoGoShiftAtMax), 25.84f, 0.05f)
                && SailMath.NoGoHalfAngle(1f, SailingRules.NoGoShiftMax) > 18f,
            "no-go cone: 36.9 deg in vanilla, 25.8 at Sailing 100, never under 18 at the largest setting");

        // Same top speed, reached sooner: push along the bow and forward drag x the same factor (vanilla owner step,
        // numbers of a paddle; sail push along the bow the same through BowBoost, checked below).
        const float thrust = 0.01f;
        const float drag = 0.0025f;
        Approach(thrust, drag, 1f, out var top1, out var steps1);
        var acc = SailMath.Scale(r.AccelerationBonusAtMax, 1f);
        Approach(thrust * acc, drag * acc, 1f, out var top2, out var steps2);
        c.Check(Near(top2, top1, top1 * 0.01f), $"top speed changed: {F(top1)} -> {F(top2)}");
        c.Check(Near(steps2 / (float)steps1, 1f / acc, 0.05f),
            $"time to 90% of top speed: {steps1} -> {steps2} steps, expected about 1/{F(acc)}");
        c.Note($"forward model: top {F(top1)} m/s, 90% after {steps1} steps at Sailing 0, {steps2} at Sailing 100");

        // Sail boost: only the part along the bow grow; sideways and up parts (leeway, heel) stay.
        var bow = new Vector3(0f, 0f, 1f);
        var sail = new Vector3(0.7f, 0.1f, 0.7f);
        var boosted = SailMath.BowBoost(sail, bow, acc);
        c.Check(Near(boosted.z, 0.7f * acc) && boosted.x == sail.x && boosted.y == sail.y
                && SailMath.BowBoost(sail, bow, 1f) == sail,
            $"sail boost: ({F(boosted.x)}, {F(boosted.y)}, {F(boosted.z)}), expected only the bow part x{F(acc)}");

        // Brake: forward speed half-life with water drag only vs drag + brake at Sailing 100.
        var dt = 0.02f;
        var halfDrag = HalfLife(5f, drag, 0f, dt);
        var halfBrake = HalfLife(5f, drag, SailMath.BrakeFraction(r.BrakeAtMax, 1f, dt), dt);
        c.Check(halfBrake < halfDrag * 0.75f && halfBrake <= 1f,
            $"brake half-life {F(halfBrake)} s, drag only {F(halfDrag)} s (expected under 1 s with the brake)");
        c.Check(SailMath.BrakeFraction(r.BrakeAtMax, 0f, dt) == 0f && Near(SailMath.BrakeFraction(0.8f, 1f, 0.02f), 0.016f)
                && SailMath.BrakeFraction(100f, 1f, 0.02f) == 1f,
            "brake fraction: 0 at Sailing 0, 0.016 per 0.02 s step at 0.8/s, never over 1");
        // Largest setting at the physics step stay well under 1. An absurd rate * dt (100 per s over 1 s) round to
        // exactly 1 in float (1 - 3.8e-44): allowed, it only means "reach the target", never past it.
        c.Check(SailMath.CatchUp(r.SailResponseAtMax, 0f, dt) == 0f && Near(SailMath.CatchUp(1.5f, 1f, 0.02f), 0.0296f, 1e-3f)
                && SailMath.CatchUp(SailingRules.SailResponseMax, 1f, 0.02f) < 0.1f && SailMath.CatchUp(100f, 1f, 1f) <= 1f,
            "sail catch-up: 0 at Sailing 0, about 3% per step at 1.5/s, under 10% per step at the largest setting, "
            + "never past the target");

        // Scaled field put back only while it still hold my value: a partner mod that scale in its prefix and put back
        // in its postfix, running before or after me, never make the value grow.
        var field = 10f;
        var partner = field;
        field *= 2f;                                        // partner prefix first
        var mine = ScaledField.Scale(ref field, 1.5f);
        field = partner;                                    // partner postfix put its value back
        mine.PutBack(ref field);
        var partnerFirst = field;
        field = 10f;
        mine = ScaledField.Scale(ref field, 1.5f);          // me first
        partner = field;
        field *= 2f;
        field = partner;
        mine.PutBack(ref field);
        var meFirst = field;
        field = 10f;
        mine = ScaledField.Scale(ref field, 1.5f);
        field = 7f;                                         // another mod set it during the call
        mine.PutBack(ref field);
        var setDuring = field;
        field = 10f;
        default(ScaledField).PutBack(ref field);
        c.Check(partnerFirst == 10f && meFirst == 10f && setDuring == 7f && field == 10f,
            $"scaled field put back: {F(partnerFirst)} (partner first), {F(meFirst)} (me first), {F(setDuring)} (set "
            + $"during the call), {F(field)} (not scaled); expected 10, 10, 7, 10");
        c.Check(SailMath.Scale(0.5f, 0f) == 1f && SailMath.Scale(0.5f, 1f) == 1.5f && SailMath.Scale(1f, 0.5f) == 1.5f,
            "bonus scale is not 1 + bonus * s");
        c.Check(SailMath.DamageFactor(0.5f, 0f) == 1f && SailMath.DamageFactor(0.5f, 1f) == 0.5f
                && SailMath.DamageFactor(SailingRules.DamageReductionMax, 1f) > 0f,
            "damage factor: 1 at Sailing 0, 0.5 at 100, never 0");

        // XP per 1 s sample.
        c.Check(SailingXp.XpFor(10f, r) == 0.5f && SailingXp.XpFor(0.5f, r) == 0f && SailingXp.XpFor(61f, r) == 0f
                && SailingXp.XpFor(60f, r) == 3f && SailingXp.XpFor(float.NaN, r) == 0f
                && SailingXp.XpFor(10f, SailingRules.Pending) == 0f && SailingXp.XpFor(10f, null) == 0f,
            "XP per sample: 10 m = 0.5, under 1 m or over 60 m nothing, pending nothing");

        c.Check(SailingSkill.IsCheatName("sailing") && SailingSkill.IsCheatName(" Sailing ") && SailingSkill.IsCheatName("697262889")
                && !SailingSkill.IsCheatName("sail") && !SailingSkill.IsCheatName(null) && SailingSkill.IsAll("ALL")
                && !SailingSkill.IsAll("sailing"),
            "console names: sailing (any case) and the number, all");
        c.Check(Compat.Matches("GrindstoneSkills", new[] { "grindstoneskills" }) && !Compat.Matches("Swim Dive", new[] { "Sailing" })
                && !Compat.Matches(null, new[] { "Sailing" }),
            "compat name markers");

        // Sail response: my catch-up move the stored sail force after vanilla SmoothDamp and leave its velocity alone.
        // Real engine SmoothDamp here: force never go past the target and never turn around when the sail empties,
        // also when the target switch mid-way (15 steps); from a settled sail (100 steps) it only ever come closer.
        foreach (var rate in new[] { 0.5f, r.SailResponseAtMax, SailingRules.SailResponseMax })
        {
            foreach (var segment in new[] { 100, 15 })
            {
                var closer = segment == 100 && rate >= r.SailResponseAtMax;
                c.Check(CatchUpStaysBeforeTarget(rate, segment, closer, out var detail),
                    $"sail catch-up at {F(rate)}/s, target switched every {segment} steps: {detail}");
            }
        }
        c.Report();
        yield break;
    }

    // Vanilla Ship.GetSailForce step (SmoothDamp, 1 s, max speed 99, velocity kept between steps) then my catch-up
    // (Lerp toward the same target), with the target going full, empty, full the other way, full, empty.
    private static bool CatchUpStaysBeforeTarget(float rate, int segment, bool onlyCloser, out string detail)
    {
        const float dt = 0.02f;
        var full = new Vector3(0.02f, 0f, 0.03f);
        var force = Vector3.zero;
        var velocity = Vector3.zero;
        var share = SailMath.CatchUp(rate, 1f, dt);
        detail = "";
        foreach (var target in new[] { full, Vector3.zero, -full, full, Vector3.zero })
        {
            var approach = target - force;
            var last = approach.magnitude;
            for (var i = 0; i < segment; i++)
            {
                force = Vector3.SmoothDamp(force, target, ref velocity, 1f, 99f, dt);
                force = Vector3.Lerp(force, target, share);
                var left = target - force;
                if (Vector3.Dot(left, approach) < -1e-9f)
                {
                    detail = $"the force went past its target at step {i} ({left.magnitude.ToString("0.#######", CultureInfo.InvariantCulture)} beyond)";
                    return false;
                }
                var distance = left.magnitude;
                if (onlyCloser && distance > last + 1e-7f)
                {
                    detail = $"the force moved away from its target at step {i}";
                    return false;
                }
                last = distance;
            }
        }
        return true;
    }

    private static void Approach(float thrust, float drag, float submersion, out float top, out int stepsTo90)
    {
        var v = 0f;
        for (var i = 0; i < 20000; i++)
        {
            v = SailMath.ForwardStep(v, thrust, drag, submersion);
        }
        top = v;
        v = 0f;
        stepsTo90 = 0;
        while (v < top * 0.9f && stepsTo90 < 20000)
        {
            v = SailMath.ForwardStep(v, thrust, drag, submersion);
            stepsTo90++;
        }
    }

    private static float HalfLife(float v0, float drag, float brakeFraction, float dt)
    {
        var v = v0;
        var t = 0f;
        while (v > v0 * 0.5f && t < 120f)
        {
            v = SailMath.ForwardStep(v, 0f, drag, 1f);
            v -= v * brakeFraction;
            t += dt;
        }
        return t;
    }

    // ---------- sailing.skill ----------

    private static IEnumerator RunSkill()
    {
        var c = new Checks(SkillName);
        var player = Player.m_localPlayer;
        if (player == null || player.GetSkills() == null)
        {
            SelfTest.Fail(SkillName, "no local player");
            yield break;
        }
        var skills = player.GetSkills();
        var type = SailingSkill.Type;
        ZPackage backup = null;
        try
        {
            c.Check(Math.Abs(ModInfo.Guid.GetStableHashCode()) == SailingSkill.Id,
                $"id {SailingSkill.Id} is not Math.Abs(hash of {ModInfo.Guid}) = {Math.Abs(ModInfo.Guid.GetStableHashCode())}");
            c.Check((int)type == SailingSkill.Id && SailingSkill.Id > 999 && !Enum.IsDefined(typeof(Skills.SkillType), type),
                "the skill id is not a positive non-enum number above 999");
            c.Check(Skills.IsSkillValid(type) && Skills.IsSkillValid(Skills.SkillType.Swim)
                    && !Skills.IsSkillValid((Skills.SkillType)123457),
                "IsSkillValid: Sailing not valid, or vanilla answers changed");
            c.Check(ReferenceEquals(skills.GetSkillDef(type), SailingSkill.Def), "GetSkillDef does not return the Sailing def");
            c.Check(skills.m_skills.Count(def => def != null && def.m_skill == type) == 1,
                "the Sailing def is not exactly once in the player's m_skills");

            var loc = Localization.instance;
            var name = loc.Localize("$" + SailingSkill.NameKey);
            c.Check(name == SailingSkill.DisplayName, $"skill name localizes to '{name}'");
            var description = loc.Localize(SailingSkill.Def.m_description);
            c.Check(description == SailingSkill.Description, $"skill description localizes to '{description}'");
            var levelUp = loc.Localize("$msg_skillup $skill_" + type.ToString().ToLower() + ": 5");
            c.Check(levelUp.Contains(SailingSkill.DisplayName), $"level-up message '{levelUp}' has no skill name");
            c.Note($"level-up message: {levelUp}");
            var icon = SailingSkill.Def.m_icon;
            c.Check(icon != null, "the skill has no icon");
            if (icon != null)
            {
                c.Note($"icon {icon.name}, {F(icon.rect.width)}x{F(icon.rect.height)}");
            }

            backup = new ZPackage();
            skills.Save(backup);

            // Reading our level never adds the skill.
            skills.ResetSkill(type);
            HelmSkill.Invalidate();
            var read = HelmSkill.OwnLevel(player);
            c.Check(read == 0f && !skills.m_skillData.ContainsKey(type), "reading the level added a Sailing entry");

            // Save -> Load keeps level and progress.
            var skill = skills.GetSkill(type);
            skill.m_level = 37f;
            skill.m_accumulator = 1.25f;
            var pkg = new ZPackage();
            skills.Save(pkg);
            pkg.SetPos(0);
            skills.Load(pkg);
            c.Check(skills.m_skillData.TryGetValue(type, out var loaded) && loaded.m_level == 37f
                    && loaded.m_accumulator == 1.25f && ReferenceEquals(loaded.m_info, SailingSkill.Def),
                "Save -> Load lost the Sailing level, its progress or its definition");
            c.Check(player.GetSkillLevel(type) >= 37f, $"GetSkillLevel reads {F(player.GetSkillLevel(type))} after load");

            // Console.
            skills.CheatRaiseSkill("sailing", 5f, false);
            c.Check(Near(skills.GetSkill(type).m_level, 42f), "raiseskill sailing 5 did not add 5 levels");
            skills.CheatRaiseSkill("Sailing", 3f, false);
            c.Check(Near(skills.GetSkill(type).m_level, 45f), "raiseskill Sailing 3 did not add 3 levels");
            skills.CheatRaiseSkill("sailing", 500f, false);
            c.Check(Near(skills.GetSkill(type).m_level, 100f), "raiseskill sailing 500 did not stop at 100");
            skills.CheatResetSkill("sailing");
            c.Check(!skills.m_skillData.ContainsKey(type), "resetskill sailing did not remove the skill");
            if (global::Console.instance != null)
            {
                skills.GetSkill(type).m_level = 10f;
                var swim = skills.GetSkill(Skills.SkillType.Swim).m_level;
                skills.CheatRaiseSkill("all", 1f, false);
                c.Check(Near(skills.GetSkill(type).m_level, 11f) && Near(skills.GetSkill(Skills.SkillType.Swim).m_level, Mathf.Min(100f, swim + 1f)),
                    "raiseskill all 1 did not raise Sailing (and Swim) by 1");
                skills.CheatResetSkill("all");
                c.Check(!skills.m_skillData.ContainsKey(type), "resetskill all did not remove Sailing");
            }
            else
            {
                c.Note("no console: raiseskill all / resetskill all not checked");
            }

            // Vanilla death penalty includes Sailing.
            skills.GetSkill(type).m_level = 40f;
            skills.LowerAllSkills(0.25f);
            c.Check(Near(skills.GetSkill(type).m_level, 30f), "the death penalty did not lower Sailing");

            // Tab completion.
            foreach (var key in new[] { "raiseskill", "resetskill" })
            {
                var options = Terminal.commands.TryGetValue(key, out var command) && command != null
                    ? command.GetTabOptions()
                    : null;
                c.Check(options != null && options.Contains(SailingSkill.CheatName) && options.Contains("Swim"),
                    $"{key}: Tab list has no Sailing (or lost the vanilla names)");
            }

            // Values not in .ref (design section 9: settled by these NOTE lines in 1.0.16; keep them for game updates).
            var sb = new StringBuilder();
            foreach (var def in skills.m_skills)
            {
                if (def != null)
                {
                    sb.Append(def.m_skill).Append(' ').Append(F(def.m_increseStep)).Append("; ");
                }
            }
            c.Note($"skill definitions (type, raise step): {sb}");
            c.Note($"death lower factor {F(skills.m_DeathLowerFactor)}, skill cap {(skills.m_useSkillCap ? "on" : "off")} "
                   + $"({F(skills.m_totalSkillCap)}), world skill gain x{F(Game.m_skillGainRate)}, skill loss x"
                   + $"{F(Game.m_skillReductionRate)}");
            var db = ObjectDB.instance;
            var rested = db != null ? db.GetStatusEffect(SEMan.s_statusEffectRested) as SE_Stats : null;
            c.Note(rested != null
                ? $"Rested: raise skill {rested.m_raiseSkill} +{F(rested.m_raiseSkillModifier)}"
                : "Rested status effect not found");
        }
        finally
        {
            if (backup != null)
            {
                backup.SetPos(0);
                skills.Load(backup);
                HelmSkill.Invalidate();
            }
        }
        c.Report();
        yield break;
    }

    // ---------- sailing.helm ----------

    private static IEnumerator RunHelm()
    {
        var c = new Checks(HelmName);
        var rig = ShipRig.Create(HelmName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var rules = new SailingRules();
            ServerRules.TestRules = rules;
            var ok = new Box();
            yield return SetUpAirShip(rig, c, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            var player = rig.Player;
            var ship = rig.Boat;
            var skills = player.GetSkills();
            var type = SailingSkill.Type;

            // Owner read the helmsman's (local) level: real skill.
            rig.BackupSkills();
            skills.GetSkill(type).m_level = 40f;
            HelmSkill.Invalidate();
            c.Check(Near(HelmSkill.Physics01(ship), 0.4f), $"owner reads helmsman s {F(HelmSkill.Physics01(ship))}, expected 0.4 (Sailing 40)");
            c.Check(Near(HelmSkill.Damage01(ship), 0.4f), $"best sailor aboard s {F(HelmSkill.Damage01(ship))}, expected 0.4");
            skills.ResetSkill(type);
            HelmSkill.Invalidate();
            c.Check(HelmSkill.Physics01(ship) == 0f && !skills.m_skillData.ContainsKey(type),
                "no Sailing: s is not 0, or reading added the skill");
            rig.RestoreSkills();

            // Published on the player ZDO.
            HelmSkill.TestLocalLevel = 30f;
            LevelPublisher.PublishNow();
            c.Check(Near(HelmSkill.Published(player), 30f), $"published level {F(HelmSkill.Published(player))}, expected 30");
            HelmSkill.TestLocalLevel = 100f;
            LevelPublisher.PublishNow();
            c.Check(Near(HelmSkill.Published(player), 100f), $"published level {F(HelmSkill.Published(player))}, expected 100");

            // Owner step: factors at Sailing 100, fields back between steps, even after many steps.
            ShipFixedUpdatePatches.LastShip = null;
            yield return FixedSteps(3);
            c.Check(ReferenceEquals(ShipFixedUpdatePatches.LastShip, ship)
                    && Near(ShipFixedUpdatePatches.LastAccel, SailMath.Scale(rules.AccelerationBonusAtMax, 1f))
                    && Near(ShipFixedUpdatePatches.LastTurn, SailMath.Scale(rules.TurnBonusAtMax, 1f)),
                $"owner step at Sailing 100: acceleration x{F(ShipFixedUpdatePatches.LastAccel)}, turning x"
                + $"{F(ShipFixedUpdatePatches.LastTurn)}, expected x1.5 both");
            yield return FixedSteps(50);
            c.Check(FieldsLikePrefab(ship, rig.PrefabShip, out var fields), "ship fields not put back after 50 steps: " + fields);
            CheckSailBoost(c, ship, rules);
            HelmSkill.TestLocalLevel = 0f;
            yield return FixedSteps(2);
            c.Check(ShipFixedUpdatePatches.LastAccel == 1f && ShipFixedUpdatePatches.LastTurn == 1f,
                "owner step at Sailing 0 still scales the ship");

            // Wind.
            CheckWind(c, ship, rules, 0f, "Sailing 0");
            HelmSkill.TestLocalLevel = 100f;
            CheckWind(c, ship, rules, 1f, "Sailing 100");
            HelmSkill.TestLocalLevel = 50f;
            CheckWind(c, ship, rules, 0.5f, "Sailing 50");

            // Rudder.
            var rudderSpeed = ship.m_rudderSpeed;
            var vanillaStep = 0.5f * rudderSpeed * Time.fixedDeltaTime;
            HelmSkill.TestLocalLevel = 0f;
            var step0 = RudderStep(ship);
            HelmSkill.TestLocalLevel = 100f;
            var step100 = RudderStep(ship);
            c.Check(Near(step0, vanillaStep, 1e-5f) && Near(step100, vanillaStep * SailMath.Scale(rules.RudderBonusAtMax, 1f), 1e-5f),
                $"rudder step {F(step0)} at Sailing 0 and {F(step100)} at 100, expected {F(vanillaStep)} and x1.5");
            c.Check(ship.m_rudderSpeed == rudderSpeed, "rudder speed not put back");

            // Map reveal.
            var map = Minimap.instance;
            if (map != null)
            {
                var radius = map.m_exploreRadius;
                HelmSkill.TestLocalLevel = 100f;
                if (c.Check(RevealRadius(player, out var used, out var after), "Explore did not run"))
                {
                    c.Check(Near(used, radius * SailMath.Scale(rules.RevealBonusAtMax, 1f), 0.01f) && after == radius,
                        $"reveal radius at Sailing 100 {F(used)} (left {F(after)}), expected {F(radius * 2f)} (left {F(radius)})");
                }
                HelmSkill.TestLocalLevel = 0f;
                RevealRadius(player, out used, out _);
                c.Check(Near(used, radius, 0.01f), $"reveal radius at Sailing 0 {F(used)}, expected {F(radius)}");
                c.Note($"minimap: explore radius {F(radius)} m every {F(map.m_exploreInterval)} s, pixel {F(map.m_pixelSize)} m, "
                       + $"texture {map.m_textureSize}");
            }
            else
            {
                c.Note("no minimap: map reveal not checked");
            }

            // Damage.
            if (c.Check(rig.Wear != null, "the ship has no WearNTear"))
            {
                HelmSkill.TestLocalLevel = 0f;
                var full = DamageTaken(rig, HitData.HitType.EnemyHit, null);
                HelmSkill.TestLocalLevel = 100f;
                var cut = DamageTaken(rig, HitData.HitType.EnemyHit, null);
                var byPlayerType = DamageTaken(rig, HitData.HitType.PlayerHit, null);
                var byPlayer = DamageTaken(rig, HitData.HitType.EnemyHit, player);
                var keep = ship.transform.rotation;
                SetRotation(ship, keep * Quaternion.Euler(0f, 0f, 180f));
                float capsized;
                try
                {
                    capsized = DamageTaken(rig, HitData.HitType.EnemyHit, null);
                }
                finally
                {
                    SetRotation(ship, keep);
                }
                c.Check(full > 0f, "a 10 blunt hit did no damage to the ship");
                c.Check(Near(cut, full * SailMath.DamageFactor(rules.DamageReductionAtMax, 1f), 0.01f),
                    $"creature hit at Sailing 100 took {F(cut)}, expected half of {F(full)}");
                c.Check(Near(byPlayerType, full, 0.01f) && Near(byPlayer, full, 0.01f),
                    $"player hits cut: {F(byPlayerType)} (PlayerHit), {F(byPlayer)} (player attacker), expected {F(full)}");
                c.Check(Near(capsized, full, 0.01f), $"capsized ship's hit cut: {F(capsized)}, expected {F(full)}");
                c.Note($"ship health {F(rig.Wear.m_health)}, blunt {rig.Wear.m_damages.m_blunt}");
            }

            // XP: real skill at 50 (no level-up from 1.5 XP), ship moved 30 m while steering.
            HelmSkill.TestLocalLevel = null;
            rig.BackupSkills();
            var sailing = skills.GetSkill(type);
            sailing.m_level = 50f;
            sailing.m_accumulator = 0f;
            SailingXp.Reset();
            yield return new WaitForSeconds(1.2f);
            var before = sailing.m_accumulator;
            MoveShip(ship, ship.transform.position + Vector3.right * 30f);
            yield return new WaitForSeconds(1.3f);
            var multiplier = 1f;
            player.GetSEMan().ModifyRaiseSkill(type, ref multiplier);
            var expected = 30f / 1000f * rules.XpPerKm * multiplier * sailing.m_info.m_increseStep * Game.m_skillGainRate;
            var gained = sailing.m_accumulator - before;
            c.Check(Near(gained, expected, expected * 0.03f), $"30 m at the helm gave {F(gained)} XP, expected {F(expected)}");
            // Not steering: nothing.
            player.m_doodadController = null;
            yield return new WaitForSeconds(1.1f);
            before = sailing.m_accumulator;
            MoveShip(ship, ship.transform.position + Vector3.right * 30f);
            yield return new WaitForSeconds(1.3f);
            c.Check(sailing.m_accumulator == before, "the player earned Sailing XP without steering");
            rig.RestoreSkills();

            yield return rig.Leave(rig.Origin);
            if (map != null)
            {
                var radius = map.m_exploreRadius;
                HelmSkill.TestLocalLevel = 100f;
                RevealRadius(player, out var ashore, out _);
                c.Check(Near(ashore, radius, 0.01f), $"reveal radius off the ship at Sailing 100 {F(ashore)}, expected {F(radius)}");
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    private static void MoveShip(Ship ship, Vector3 position)
    {
        ship.transform.position = position;
        var body = ship.m_body;
        if (body != null)
        {
            body.position = position;
        }
    }

    // ---------- sailing.pending ----------

    private static IEnumerator RunPending()
    {
        var c = new Checks(PendingName);
        var rig = ShipRig.Create(PendingName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var rules = new SailingRules();
            ServerRules.TestRules = rules;
            var ok = new Box();
            yield return SetUpAirShip(rig, c, ok);
            if (!ok.Ok)
            {
                c.Report();
                yield break;
            }
            var player = rig.Player;
            var ship = rig.Boat;
            var map = Minimap.instance;
            var radius = map != null ? map.m_exploreRadius : -1f;
            HelmSkill.TestLocalLevel = 0f;
            var full = rig.Wear != null ? DamageTaken(rig, HitData.HitType.EnemyHit, null) : -1f;
            var vanillaStep = 0.5f * ship.m_rudderSpeed * Time.fixedDeltaTime;
            HelmSkill.TestLocalLevel = 100f;

            ServerRules.TestPending = true;
            c.Check(ServerRules.IsPending, "TestPending did not make the rules pending");
            CheckWind(c, ship, ServerRules.Current, 0f, "pending");
            ShipFixedUpdatePatches.LastShip = null;
            yield return FixedSteps(3);
            c.Check(ShipFixedUpdatePatches.LastShip == null, "pending: the owner step still used the skill");
            c.Check(FieldsLikePrefab(ship, rig.PrefabShip, out var fields), "pending: ship fields changed: " + fields);
            c.Check(Near(RudderStep(ship), vanillaStep, 1e-5f), "pending: rudder faster than vanilla");
            if (map != null && RevealRadius(player, out var used, out _))
            {
                c.Check(Near(used, radius, 0.01f), $"pending: reveal radius {F(used)}, expected {F(radius)}");
            }
            if (full >= 0f)
            {
                var got = DamageTaken(rig, HitData.HitType.EnemyHit, null);
                c.Check(Near(got, full, 0.01f), $"pending: ship took {F(got)}, expected the full {F(full)}");
            }
            c.Check(SailingXp.XpFor(30f, ServerRules.Current) == 0f, "pending: a sample still pays XP");

            ServerRules.TestPending = false;
            c.Check(!ServerRules.IsPending, "rules still pending");
            CheckWind(c, ship, rules, 1f, "rules here");
            ShipFixedUpdatePatches.LastShip = null;
            yield return FixedSteps(3);
            c.Check(ReferenceEquals(ShipFixedUpdatePatches.LastShip, ship) && ShipFixedUpdatePatches.LastAccel > 1f,
                "rules here: the owner step does not use the skill");
            c.Check(RudderStep(ship) > vanillaStep * 1.4f, "rules here: rudder not faster");
            if (map != null && RevealRadius(player, out var back, out _))
            {
                c.Check(back > radius * 1.9f, $"rules here: reveal radius {F(back)}, expected about {F(radius * 2f)}");
            }
            if (full >= 0f)
            {
                var got = DamageTaken(rig, HitData.HitType.EnemyHit, null);
                c.Check(Near(got, full * 0.5f, 0.01f), $"rules here: ship took {F(got)}, expected half of {F(full)}");
            }
            c.Check(SailingXp.XpFor(30f, ServerRules.Current) > 0f, "rules here: a sample pays no XP");

            yield return rig.Leave(rig.Origin);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sailing.ship ----------

    private sealed class Run
    {
        internal float Level;
        internal readonly float[] Paddle = new float[4];   // speed after 0.25, 0.5, 1, 3 s at Slow
        internal float PaddleEarly;                        // mean speed from 0.5 to 1 s at Slow, every physics step
        internal float StopFrom;
        internal float StopAfter;                          // 2 s after Stop
        internal float Sail2;                              // speed after 2 s at Full
        internal float Sail4;
        internal float Wind;
        internal float WindD;

        internal string Text() =>
            $"Sailing {F(Level)}: paddle {F(Paddle[0])}/{F(Paddle[1])}/{F(Paddle[2])}/{F(Paddle[3])} m/s after "
            + $"0.25/0.5/1/3 s (mean {F(PaddleEarly)} from 0.5 to 1 s), stop {F(StopFrom)} -> {F(StopAfter)} m/s in 2 s, "
            + $"sail {F(Sail2)}/{F(Sail4)} m/s after 2/4 s (wind {F(Wind)}, d {F(WindD)}); speeds level along the bow";
    }

    private static IEnumerator RunShip()
    {
        var c = new Checks(ShipName);
        var rig = ShipRig.Create(ShipName);
        if (rig == null)
        {
            yield break;
        }
        var started = Time.realtimeSinceStartup;
        try
        {
            DumpShips(c);
            var rules = new SailingRules();
            ServerRules.TestRules = rules;
            var player = rig.Player;
            var water = ZoneSystem.instance.m_waterLevel;

            var found = new Box();
            yield return FindOcean(rig.Origin, found);
            if (!found.Ok)
            {
                c.Note("no open sea within 5 km of the player: the voyage was not measured");
                c.Report();
                yield break;
            }
            var sea = found.Point;
            c.Note($"open sea at ({F(sea.x)}, {F(sea.z)}), {F(Flat(sea - rig.Origin).magnitude)} m from the player, "
                   + $"found in {F(Time.realtimeSinceStartup - started)} s");
            var arrived = new Box();
            yield return TeleportAndWait(player, new Vector3(sea.x, water + 1.5f, sea.z), 35f, arrived);
            if (!arrived.Ok)
            {
                c.Note($"the teleport to the sea did not finish: the voyage was not measured");
                c.Report();
                yield break;
            }
            var surface = -10000f;
            var until = Time.time + 10f;
            while (Time.time < until)
            {
                surface = Floating.GetLiquidLevel(new Vector3(sea.x, water - 0.5f, sea.z));
                if (surface > water - 3f)
                {
                    break;
                }
                yield return new WaitForSeconds(0.25f);
            }
            if (!(surface > water - 3f))
            {
                c.Note("no water volume at the sea point after 10 s: the voyage was not measured");
                c.Report();
                yield break;
            }
            // 8 m from the swimming player (no overlap push), bow across the wind.
            var across = BeamReach();
            var start = new Vector3(sea.x + 8f, surface + 0.3f, sea.z);
            if (!c.Check(rig.Spawn(start, Quaternion.LookRotation(across, Vector3.up), kinematic: false), "no ship prefab (Karve) or it did not spawn"))
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(2f);
            var ship = rig.Boat;
            if (!c.Check(rig.TakeHelm(), "the ship has no helm attach point"))
            {
                c.Report();
                yield break;
            }
            var aboard = new Box();
            yield return rig.WaitAboard(aboard);
            if (!c.Check(aboard.Ok, "the player at the helm never entered the ship's volume"))
            {
                c.Report();
                yield break;
            }
            var floatY = ship.transform.position.y;
            c.Note($"{Utils.GetPrefabName(ship.gameObject)} settled at height {F(floatY)} (water {F(surface)}), up {F(ship.transform.up.y)}");
            if (!c.Check(ship.transform.up.y > 0.8f, "the spawned ship is not upright"))
            {
                c.Report();
                yield break;
            }
            start = new Vector3(start.x, floatY, start.z);

            var runs = new List<Run>();
            foreach (var level in new[] { 0f, 100f })
            {
                if (Time.realtimeSinceStartup - started > 70f)
                {
                    c.Note($"time budget used: Sailing {F(level)} not measured");
                    break;
                }
                var run = new Run { Level = level };
                yield return Measure(ship, start, run);
                runs.Add(run);
                c.Note(run.Text());
            }
            if (runs.Count == 2)
            {
                Compare(c, runs[0], runs[1]);
            }
            c.Check(FieldsLikePrefab(ship, rig.PrefabShip, out var fields), "ship fields not put back after the voyage: " + fields);

            HelmSkill.TestLocalLevel = null;
            yield return rig.Leave(ship.transform.position + ship.transform.right * 20f + Vector3.up);
            var home = new Box();
            yield return TeleportAndWait(player, rig.Origin, 35f, home);
            c.Check(home.Ok, "the player did not get back to the start after the voyage");
            c.Note($"voyage test took {F(Time.realtimeSinceStartup - started)} s");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // Bow across the wind (beam reach: vanilla's best pull).
    private static Vector3 BeamReach()
    {
        var env = EnvMan.instance;
        var windFlat = env != null ? Flat(env.GetWindDir()) : Vector3.forward;
        if (windFlat.sqrMagnitude < 1e-4f)
        {
            windFlat = Vector3.forward;
        }
        windFlat.Normalize();
        return Vector3.Cross(Vector3.up, windFlat);
    }

    private static void ResetBoat(Ship ship, Vector3 position, Vector3 forward)
    {
        var rotation = Quaternion.LookRotation(forward, Vector3.up);
        ship.transform.SetPositionAndRotation(position, rotation);
        var body = ship.m_body;
        body.position = position;
        body.rotation = rotation;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        ship.m_sailForce = Vector3.zero;
        ship.m_windChangeVelocity = Vector3.zero;
        ship.m_rudderValue = 0f;
        ship.m_speed = Ship.Speed.Stop;
    }

    // Speed along the bow, level part only (no up/down). Vanilla Ship.GetSpeed take the whole velocity along the pitched
    // bow: on a rough sea the ship's bobbing (1 m/s and more, bow up or down 10 degrees) leak into it, +-0.1 m/s, as
    // big as the paddle speed of the first second (run 2026-10-07, 2 m waves: 0.092 + 0.3 m/s at Sailing 0 against
    // 0.05 + 0.202 at 100, while the 3 s speeds were 0.666 against 0.855). Waves only push the ship up and down
    // (Ship.CustomFixedUpdate: every water force is Vector3.up), paddle and sail push along the bow: level speed
    // along the level bow show them without the bobbing.
    private static float BowSpeedFlat(Ship ship)
    {
        var bow = Flat(ship.transform.forward);
        if (ship.m_body == null || bow.sqrMagnitude < 1e-6f)
        {
            return 0f;
        }
        return Vector3.Dot(Flat(ship.m_body.linearVelocity), bow.normalized);
    }

    private static IEnumerator Measure(Ship ship, Vector3 start, Run run)
    {
        HelmSkill.TestLocalLevel = run.Level;
        ResetBoat(ship, start, BeamReach());
        yield return FixedSteps(2);
        ship.m_speed = Ship.Speed.Slow;
        var t0 = Time.time;
        var marks = new[] { 0.25f, 0.5f, 1f, 3f };
        var earlySum = 0f;
        var earlyCount = 0;
        for (var i = 0; i < marks.Length; i++)
        {
            while (Time.time - t0 < marks[i])
            {
                yield return new WaitForFixedUpdate();
                var t = Time.time - t0;
                if (t >= 0.5f && t <= 1f)
                {
                    earlySum += BowSpeedFlat(ship);
                    earlyCount++;
                }
            }
            run.Paddle[i] = BowSpeedFlat(ship);
        }
        run.PaddleEarly = earlyCount > 0 ? earlySum / earlyCount : 0f;
        ship.m_speed = Ship.Speed.Stop;
        run.StopFrom = BowSpeedFlat(ship);
        yield return new WaitForSeconds(2f);
        run.StopAfter = BowSpeedFlat(ship);

        ResetBoat(ship, start, BeamReach());
        yield return FixedSteps(2);
        var env = EnvMan.instance;
        run.Wind = env != null ? env.GetWindIntensity() : 0f;
        run.WindD = env != null ? Vector3.Dot(env.GetWindDir(), -ship.transform.forward) : 0f;
        ship.m_speed = Ship.Speed.Full;
        yield return new WaitForSeconds(2f);
        run.Sail2 = BowSpeedFlat(ship);
        yield return new WaitForSeconds(2f);
        run.Sail4 = BowSpeedFlat(ship);
        ship.m_speed = Ship.Speed.Stop;
    }

    private static void Compare(Checks c, Run low, Run high)
    {
        // Paddle: mean level speed along the bow from 0.5 to 1 s (every physics step), only while Sailing 0 still
        // clearly speed up then (under 70% of its 3 s speed). Vanilla paddle = 0.2 m/s per second on a Karve, x1.5 at
        // Sailing 100: about 0.15 against 0.225 m/s. Single samples of Ship.GetSpeed were wave noise (see
        // BowSpeedFlat); the 0.25 s sample is too small to compare at all.
        var lowEarly = low.PaddleEarly;
        var highEarly = high.PaddleEarly;
        if (lowEarly > 0.05f && lowEarly < low.Paddle[3] * 0.7f)
        {
            c.Check(highEarly > lowEarly * 1.15f,
                $"paddle: Sailing 100 not faster to pick up speed (mean from 0.5 to 1 s: {F(highEarly)} against "
                + $"{F(lowEarly)} m/s, level speed along the bow)");
        }
        else
        {
            c.Note("paddle: Sailing 0 not clearly accelerating at 1 s (70% of its 3 s speed or more, or no speed): "
                   + "acceleration not compared");
        }
        // Level speed after 3 s of paddling is about 0.57 m/s at Sailing 0 (0.2 m/s per second, little drag yet).
        if (low.StopFrom > 0.4f && high.StopFrom > 0.4f)
        {
            var lowKeep = low.StopAfter / low.StopFrom;
            var highKeep = high.StopAfter / high.StopFrom;
            c.Check(highKeep < lowKeep * 0.7f,
                $"brake: Sailing 100 kept {F(highKeep * 100f)}% of its speed after 2 s at Stop, Sailing 0 {F(lowKeep * 100f)}%");
        }
        else
        {
            c.Note("brake: speed too low when Stop was set: not compared");
        }
        if (low.Sail2 > 0.3f && low.Wind > 0.1f && high.Wind > 0.1f)
        {
            c.Check(high.Sail2 > low.Sail2 * 1.1f,
                $"sail: Sailing 100 not faster after 2 s ({F(high.Sail2)} against {F(low.Sail2)} m/s)");
        }
        else
        {
            c.Note("sail: too little wind or speed: not compared");
        }
    }

    // Open sea point: Ocean biome, 12 m deep, 4 m deep all around at 150 m (room to sail). Rings from 300 m to 5 km
    // around the player (WorldGenerator, x/z only); one frame per ring. Fixed probe seed = same point every run.
    private static IEnumerator FindOcean(Vector3 origin, Box result)
    {
        result.Ok = false;
        var generator = WorldGenerator.instance;
        var zones = ZoneSystem.instance;
        if (generator == null || zones == null)
        {
            yield break;
        }
        var water = zones.m_waterLevel;
        for (var r = 300f; r <= 5000f; r += 150f)
        {
            for (var a = 0; a < 360; a += 10)
            {
                var p = origin + Quaternion.Euler(0f, a, 0f) * Vector3.forward * r;
                if (generator.GetBiome(p) != Heightmap.Biome.Ocean || generator.GetHeight(p.x, p.z) > water - 12f)
                {
                    continue;
                }
                var open = true;
                for (var b = 0; b < 360 && open; b += 30)
                {
                    var q = p + Quaternion.Euler(0f, b, 0f) * Vector3.forward * 150f;
                    open = generator.GetHeight(q.x, q.z) < water - 4f;
                }
                if (open)
                {
                    result.Ok = true;
                    result.Point = new Vector3(p.x, water, p.z);
                    yield break;
                }
            }
            yield return null;
        }
    }

    // Vanilla distant teleport (loading screen, hold until the area is there). Vanilla refuse a new one within 2 s of
    // the last: retry.
    // fast: skip vanilla's 8 s of teleport animation (timer put past it); the wait for the area stay.
    private static IEnumerator TeleportAndWait(Player player, Vector3 target, float seconds, Box result, bool fast = false)
    {
        result.Ok = false;
        var until = Time.time + seconds;
        while (!player.TeleportTo(target, player.transform.rotation, true))
        {
            if (Time.time > until)
            {
                yield break;
            }
            yield return new WaitForSeconds(0.25f);
        }
        if (fast)
        {
            player.m_teleportTimer = 8.1f;
        }
        while (player.IsTeleporting() && Time.time < until)
        {
            yield return new WaitForSeconds(0.25f);
        }
        result.Ok = !player.IsTeleporting() && Flat(player.transform.position - target).magnitude < 20f;
    }

    // Every ship prefab's tuning (not in .ref) and the physics step.
    private static void DumpShips(Checks c)
    {
        var scene = ZNetScene.instance;
        var names = new List<string>();
        foreach (var prefab in scene.m_prefabs)
        {
            var ship = prefab != null ? prefab.GetComponent<Ship>() : null;
            if (ship == null)
            {
                continue;
            }
            names.Add(prefab.name);
            var sb = new StringBuilder(prefab.name).Append(": ");
            sb.Append("sail ").Append(F(ship.m_sailForceFactor)).Append(" (offset ").Append(F(ship.m_sailForceOffset))
                .Append("), rudder speed ").Append(F(ship.m_rudderSpeed)).Append(", steer ").Append(F(ship.m_stearForce))
                .Append(" / vel ").Append(F(ship.m_stearVelForceFactor)).Append(" (offset ").Append(F(ship.m_stearForceOffset))
                .Append("), paddle ").Append(F(ship.m_backwardForce)).Append(", damping ").Append(F(ship.m_damping))
                .Append(" fwd ").Append(F(ship.m_dampingForward)).Append(" side ").Append(F(ship.m_dampingSideway))
                .Append(" ang ").Append(F(ship.m_angularDamping)).Append(", force ").Append(F(ship.m_force)).Append(" dist ")
                .Append(F(ship.m_forceDistance)).Append(", water offset ").Append(F(ship.m_waterLevelOffset))
                .Append(", disable level ").Append(F(ship.m_disableLevel)).Append(", rudder max ")
                .Append(F(ship.m_rudderRotationMax)).Append(", impact ").Append(F(ship.m_minWaterImpactForce)).Append('/')
                .Append(F(ship.m_minWaterImpactInterval)).Append(" s/").Append(F(ship.m_waterImpactDamage))
                .Append(" dmg, upside down ").Append(F(ship.m_upsideDownDmg)).Append(" per ")
                .Append(F(ship.m_upsideDownDmgInterval)).Append(" s, ashlands ready ").Append(ship.m_ashlandsReady)
                .Append(", has sail ").Append(ship.m_hasSail);
            var body = prefab.GetComponent<Rigidbody>();
            if (body != null)
            {
                sb.Append("; body mass ").Append(F(body.mass)).Append(", drag ").Append(F(body.linearDamping)).Append('/')
                    .Append(F(body.angularDamping));
            }
            var wear = prefab.GetComponent<WearNTear>();
            if (wear != null)
            {
                var m = wear.m_damages;
                sb.Append("; health ").Append(F(wear.m_health)).Append(", resist blunt ").Append(m.m_blunt).Append(" slash ")
                    .Append(m.m_slash).Append(" pierce ").Append(m.m_pierce).Append(" chop ").Append(m.m_chop)
                    .Append(" pickaxe ").Append(m.m_pickaxe).Append(" fire ").Append(m.m_fire).Append(", burnable ")
                    .Append(wear.m_burnable).Append(", rain wear ").Append(wear.m_noRoofWear).Append(", support wear ")
                    .Append(wear.m_noSupportWear).Append(", ash immune ").Append(wear.m_ashDamageImmune).Append(" resist ")
                    .Append(wear.m_ashDamageResist).Append(", tool tier ").Append(wear.m_minToolTier)
                    .Append(", private area ").Append(wear.m_triggerPrivateArea);
            }
            var impact = prefab.GetComponentInChildren<ImpactEffect>(true);
            if (impact != null)
            {
                sb.Append("; impact ").Append(impact.m_hitType).Append(" to self ").Append(impact.m_damageToSelf)
                    .Append(", speed ").Append(F(impact.m_minVelocity)).Append('-').Append(F(impact.m_maxVelocity))
                    .Append(", blunt ").Append(F(impact.m_damages.m_blunt)).Append(", every ").Append(F(impact.m_interval))
                    .Append(" s, tier ").Append(impact.m_toolTier);
            }
            else
            {
                sb.Append("; no ImpactEffect");
            }
            if (ship.m_shipControlls != null)
            {
                sb.Append("; helm range ").Append(F(ship.m_shipControlls.m_maxUseRange));
            }
            c.Note(sb.ToString());
        }
        c.Check(names.Count > 0, "no prefab with a Ship component");
        c.Note($"{names.Count} ship prefabs: {string.Join(", ", names.ToArray())}; physics step {F(Time.fixedDeltaTime)} s; "
               + $"water level {F(ZoneSystem.instance.m_waterLevel)}");
    }
#endif
}
