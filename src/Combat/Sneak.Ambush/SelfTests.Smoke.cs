#if DEBUG
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MC.Combat.SneakAmbushMod.Patches;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Combat.SneakAmbushMod;

// Debug build only (whole file). In-world self tests of the Smoke Screen with the default settings and real
// creatures, the real off and on, and a real death:
//   sneak.smoke-default  T14 real throw with default settings: 8 m wide, hide from 0.5 s to 15 s, gone after fade, smoke
//                        look made of vanilla smoke material and thinning before the end, no Smoked, campfire burn on;
//                        T15 walking and running inside, creature outside never notice, In smoke icon in and out
//   sneak.smoke-bars     T16 bar of creature in thrown cloud go, creature not notice, bar back after cloud only once
//                        aimed; T26 really tamed Boar keep its bar
//   sneak.smoke-geometry T17 both inside: seen within 2.5 m only; T18 cloud between at 15 m, sidestep = seen
//   sneak.smoke-burst    T19 three pursuers blinded (Debug line), give up, walk back, find player again after blind;
//                        T31 one that only noticed (not alerted) is not blinded
//   sneak.smoke-reveal   T20 arrow from the smoke on two-star Greydwarf: survive, come and fight; second one not find;
//                        level-9 Deer flee
//   sneak.smoke-boss     T21 real Eikthyr: not blinded, see, keep bar, attack
//   sneak.smoke-sleeper  T25 sleeping Draugr wake when player near, not come for player in smoke
//   sneak.smoke-hunt     T29 hunting creature: blinded but never give up, find player after blind
//   sneak.smoke-late     T28 cloud that arrive after its end: inert, no look, no blind, gone at once
//   sneak.smoke-ceiling  T27 burst against underside of a floor piece: cloud belong on ground below (KNOWN WRONG now:
//                        test of the wanted behaviour, fail until fixed)
//   sneak.toggle         L01 feature really off and on (Plugin.TestBlocked through framework refresh: patches removed
//                        and applied again); T24 refused throw and its 3 s message gap; L02 items kept and movable while
//                        off; L04 shot from smoke after late on; M12 XP handler pay nothing while off
//   sneak.death          T23 real death and respawn: icons back on the new player (bag emptied first: no tombstone;
//                        bag, skills, food, place put back after)
internal static partial class SelfTests
{
    private const string SmokeDefaultName = "sneak.smoke-default";
    private const string SmokeBarsName = "sneak.smoke-bars";
    private const string SmokeGeometryName = "sneak.smoke-geometry";
    private const string SmokeBurstName = "sneak.smoke-burst";
    private const string SmokeRevealName = "sneak.smoke-reveal";
    private const string SmokeBossName = "sneak.smoke-boss";
    private const string SmokeSleeperName = "sneak.smoke-sleeper";
    private const string SmokeHuntName = "sneak.smoke-hunt";
    private const string SmokeLateName = "sneak.smoke-late";
    private const string SmokeCeilingName = "sneak.smoke-ceiling";
    private const string ToggleName = "sneak.toggle";

    // Refused-throw message as TESTING.md spell it (T24, M08): the shown text is compared with it, not only with the
    // mod's own constant (a test that compare the mod's text with itself never notice a changed text).
    private const string RefusedThrowText =
        "Smoke Screen does nothing here: Sneak Ambush is turned off (or the server does not have it).";
    private const string DeathName = "sneak.death";

    private static void RegisterSmoke()
    {
        SelfTest.Register(SmokeDefaultName, RunSmokeDefault);
        SelfTest.Register(SmokeBarsName, RunSmokeBars);
        SelfTest.Register(SmokeGeometryName, RunSmokeGeometry);
        SelfTest.Register(SmokeBurstName, RunSmokeBurst);
        SelfTest.Register(SmokeRevealName, RunSmokeReveal);
        SelfTest.Register(SmokeBossName, RunSmokeBoss);
        SelfTest.Register(SmokeSleeperName, RunSmokeSleeper);
        SelfTest.Register(SmokeHuntName, RunSmokeHunt);
        SelfTest.Register(SmokeLateName, RunSmokeLate);
        SelfTest.Register(SmokeCeilingName, RunSmokeCeiling);
        SelfTest.Register(ToggleName, RunToggle);
        // Last of this mod: it kills the player (everything put back, but later tests start on a new player object).
        SelfTest.Register(DeathName, RunDeath);
    }

    private static void UnregisterSmoke()
    {
        SelfTest.Unregister(SmokeDefaultName);
        SelfTest.Unregister(SmokeBarsName);
        SelfTest.Unregister(SmokeGeometryName);
        SelfTest.Unregister(SmokeBurstName);
        SelfTest.Unregister(SmokeRevealName);
        SelfTest.Unregister(SmokeBossName);
        SelfTest.Unregister(SmokeSleeperName);
        SelfTest.Unregister(SmokeHuntName);
        SelfTest.Unregister(SmokeLateName);
        SelfTest.Unregister(SmokeCeilingName);
        SelfTest.Unregister(ToggleName);
        SelfTest.Unregister(DeathName);
    }

    // ---------- helpers ----------

    // What the creature would see and hear with no smoke rule at all (rules pending for these two calls, same frame):
    // show that only the smoke hide the player.
    private static void WithoutSmoke(BaseAI ai, Player player, out bool sees, out bool hears)
    {
        var pending = ServerRules.TestPending;
        ServerRules.TestPending = true;
        try
        {
            sees = ai.CanSeeTarget(player);
            hears = ai.CanHearTarget(player);
        }
        finally
        {
            ServerRules.TestPending = pending;
        }
    }

    private static bool InSmoke(Player player, AmbushRules rules) =>
        SmokeRegistry.InActiveCloud(SmokeRegistry.TracedPoint(player), rules, SmokeRegistry.Now);

    // Smoke look of the cloud at that base (its particle system), or null.
    private static ParticleSystem CloudVisual(Vector3 basePoint)
    {
        ParticleSystem best = null;
        var bestDistance = 3f;
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
        {
            if (ps.gameObject.name != SmokeContent.CloudName + "_visual")
            {
                continue;
            }
            var d = Utils.DistanceXZ(ps.transform.position, basePoint);
            if (d < bestDistance)
            {
                best = ps;
                bestDistance = d;
            }
        }
        return best;
    }

    // Health plate of that creature made by the game (it make one for every creature in range). Ok = there.
    private static IEnumerator Plate(EnemyHud hud, Character creature, Box found, int frames = 90)
    {
        found.Ok = hud.m_huds.ContainsKey(creature);
        for (var i = 0; i < frames && !found.Ok; i++)
        {
            yield return null;
            found.Ok = creature != null && hud.m_huds.ContainsKey(creature);
        }
    }

    // Crosshair on the creature (what vanilla do then): its plate show for the next 60 s.
    private static bool AimAt(EnemyHud hud, Character creature)
    {
        if (!hud.m_huds.TryGetValue(creature, out var data))
        {
            return false;
        }
        data.m_hoverTimer = 0f;
        return true;
    }

    // Bar really drawn: plate there, inside its 60 s after the crosshair, and switched on by the game (it switches
    // plates of creatures outside the picture off: tests keep the creature in front of the camera).
    private static bool PlateShown(EnemyHud hud, Character creature) =>
        hud.m_huds.TryGetValue(creature, out var data) && data.m_gui != null && data.m_gui.activeSelf
        && data.m_hoverTimer < hud.m_hoverShowDuration;

    // ---------- sneak.smoke-default (T14, T15) ----------

    private static IEnumerator RunSmokeDefault()
    {
        var c = new Checks(SmokeDefaultName);
        var rig = Rig.Create(SmokeDefaultName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            var rules = new AmbushRules();
            ServerRules.TestRules = rules;
            StealthCues.TestShowCues = true;
            Tap.Install();
            rig.SaveAllSkills();
            // Dry weather: rain alone puts an open campfire out.
            rig.SetEnv("Clear");
            yield return Stand(player);
            var dir = ClearDirection(player, 14f, out _);
            var side = Vector3.Cross(Vector3.up, dir).normalized;
            var origin = player.transform.position;
            rig.TakeControls();
            var seman = player.GetSEMan();
            var dry = new Box();
            yield return SettleEnv("Clear", dry);

            // Lit campfire 10 m behind the player (the creature of this test stay far from it: Greydwarfs fear fire).
            var fireGo = rig.Spawn("fire_pit", Ground(origin - dir * 10f), Quaternion.identity);
            var fire = fireGo != null ? fireGo.GetComponent<Fireplace>() : null;
            var item = rig.GiveAndEquip(2);
            if (!c.Check(fire != null && item != null, "could not spawn a campfire or equip a Smoke Screen"))
            {
                c.Report();
                yield break;
            }
            yield return Frames(3);
            var fireZdo = fireGo.GetComponent<ZNetView>().GetZDO();
            if (fireZdo.GetFloat(ZDOVars.s_fuel) < 1f)
            {
                fireZdo.Set(ZDOVars.s_fuel, 2f);
            }
            yield return new WaitForSeconds(0.3f);
            c.Check(fire.IsBurning(), "the spawned campfire does not burn before the test: nothing to check");

            // The real throw, default settings.
            rig.Aim(dir, 35f);
            yield return new WaitForSeconds(0.6f);
            var before = new HashSet<SmokeCloud>(SmokeRegistry.All);
            var stack = rig.SmokeCount();
            c.Check(player.StartAttack(null, false), "StartAttack with a Smoke Screen refused");
            var slot = new CloudSlot();
            yield return WaitForCloud(before, 5f, slot);
            var cloud = slot.Cloud;
            if (!c.Check(cloud != null, "no cloud within 5 s of the throw"))
            {
                c.Report();
                yield break;
            }
            c.Check(rig.SmokeCount() == stack - 1, $"stack {stack} -> {rig.SmokeCount()} after one throw");
            CheckCloud(c, cloud, rules, "default throw");
            c.Check(Near(cloud.Radius, 4f) && Near(cloud.Height, 4f) && Near(cloud.Duration, 15f),
                $"default cloud: radius {F(cloud.Radius)} m, height {F(cloud.Height)} m, {F(cloud.Duration)} s (expected 8 m wide, 15 s)");
            var start = cloud.StartTime;
            var cloudBase = cloud.Base;
            double Age() => SmokeRegistry.Now - start;
            if (Age() < 0.45d)
            {
                c.Check(!cloud.IsActive(SmokeRegistry.Now, rules), $"the cloud hides {F(Age())} s after impact, before the smoke built up");
            }
            else
            {
                c.Note($"cloud first seen {F(Age())} s after impact: 'not hiding yet' not checked");
            }

            // Second Smoke Screen straight onto the campfire.
            var fuel = fireZdo.GetFloat(ZDOVars.s_fuel);
            var beforeFire = new HashSet<SmokeCloud>(SmokeRegistry.All);
            rig.Launch(fireGo.transform.position + Vector3.up * 4f + dir * 0.05f, Vector3.down * 10f);
            var fireSlot = new CloudSlot();
            yield return WaitForCloud(beforeFire, 3f, fireSlot);
            var fireAt = Time.time;
            c.Check(fireSlot.Cloud != null && Utils.DistanceXZ(fireSlot.Cloud.Base, fireGo.transform.position) < 1.5f,
                "no cloud on the campfire from the Smoke Screen dropped onto it");

            // Hiding from 0.5 s on. Player into the cloud: In smoke icon standing and crouched, never Smoked.
            yield return WaitFor(() => Age() >= 0.7d, 2f);
            c.Check(cloud != null && cloud.IsActive(SmokeRegistry.Now, rules), $"the cloud does not hide {F(Age())} s after impact");
            MovePlayer(player, cloudBase + Vector3.up * 0.1f);
            rig.MarkMoved();
            yield return new WaitForSeconds(0.8f);
            c.Check(InSmoke(player, rules), "the player at the cloud's middle is not inside it");
            c.Check(StealthCues.Has(player, CueKind.Smoke), "standing in the cloud: no In smoke icon");
            var visual = CloudVisual(cloudBase);
            var renderer = visual != null ? visual.GetComponent<ParticleSystemRenderer>() : null;
            c.Check(visual != null && visual.isEmitting && renderer != null && renderer.sharedMaterial == SmokeContent.SmokeMaterial
                    && SmokeContent.SmokeMaterial != null,
                $"smoke look: particle system {visual != null}, emitting {visual != null && visual.isEmitting}, vanilla smoke material "
                + $"{renderer != null && renderer.sharedMaterial == SmokeContent.SmokeMaterial}");
            var crouched = new Box();
            yield return Crouch(player, crouched);
            yield return new WaitForSeconds(0.7f);
            c.Check(crouched.Ok && StealthCues.Has(player, CueKind.Smoke), "crouched in the cloud: no In smoke icon");
            player.SetCrouch(false);
            yield return WaitFor(() => !player.IsCrouching(), 2f);

            // T15. Unaware Greydwarf 8 m from the cloud's middle (outside it), looking at the player, AI on (held in
            // place each frame); player walks then runs around inside the cloud for 5 s.
            var spot = Ground(cloudBase + side * 8f);
            var g = rig.Creature("Greydwarf", spot, -side, true);
            if (c.Check(g != null, "could not spawn a Greydwarf"))
            {
                var ai = (MonsterAI)g.GetBaseAI();
                var heading = dir;
                var noticed = false;
                var left = false;
                var smoked = false;
                var wouldSee = false;
                var wouldHear = false;
                var topSpeed = 0f;
                var topNoise = 0f;
                var move = Time.time;
                while (Time.time - move < 5f)
                {
                    var off = player.transform.position - cloudBase;
                    off.y = 0f;
                    if (off.magnitude > 1.8f)
                    {
                        heading = -off.normalized;
                    }
                    rig.Aim(heading, 0f);
                    var run = Time.time - move > 2.5f;
                    player.SetControls(Vector3.forward, false, false, false, false, false, false, false, false, run, false);
                    Place(g, spot, player.transform.position - spot);
                    yield return null;
                    left |= !InSmoke(player, rules);
                    noticed |= ReferenceEquals(ai.m_targetCreature, player) || ai.CanSeeTarget(player) || ai.CanHearTarget(player);
                    smoked |= seman.HaveStatusEffect(SEMan.s_statusEffectSmoked);
                    topSpeed = Mathf.Max(topSpeed, player.m_currentVel.magnitude);
                    topNoise = Mathf.Max(topNoise, player.GetNoiseRange());
                    if (!wouldSee || !wouldHear)
                    {
                        WithoutSmoke(ai, player, out var s, out var h);
                        wouldSee |= s;
                        wouldHear |= h;
                    }
                }
                rig.Drive(Vector3.zero);
                c.Check(!left, "the walking and running player left the cloud: nothing proven");
                c.Check(topSpeed > 4.5f, $"the player did not run inside the cloud (top speed {F(topSpeed)})");
                c.Check(wouldSee && wouldHear, $"without smoke the Greydwarf 8 m away would see {wouldSee} and hear {wouldHear} the running "
                                               + $"player (noise up to {F(topNoise)} m): the test proves nothing");
                c.Check(!noticed, "the Greydwarf outside the cloud noticed the player walking and running inside it");
                c.Check(!smoked, "the player in the cloud got the Smoked effect");
                rig.Remove(g.gameObject);
            }

            // Leaving: icon about 1 s more.
            MovePlayer(player, Ground(cloudBase - side * 8f) + Vector3.up * 0.05f);
            var leftAt = Time.time;
            yield return new WaitForSeconds(0.3f);
            c.Check(StealthCues.Has(player, CueKind.Smoke), "out of the cloud: the In smoke icon went at once (expected about 1 s later)");
            var gone = new Box();
            yield return WaitFor(() => !StealthCues.Has(player, CueKind.Smoke), 2.2f, gone);
            var after = Time.time - leftAt;
            c.Check(gone.Ok && after >= 0.4f && after <= 2.2f, $"out of the cloud: In smoke icon {(gone.Ok ? "went" : "still there")} after {F(after)} s, expected about 1 s");

            // Rest of the life: smoke thins before the end, hides until 15 s, gone after the fade.
            yield return WaitFor(() => cloud == null || Age() >= 13.2d, 16f);
            c.Check(cloud != null && cloud.IsActive(SmokeRegistry.Now, rules), $"{F(Age())} s after impact: the cloud no longer hides");
            c.Check(visual != null && !visual.isEmitting, $"{F(Age())} s after impact: the smoke still emits at full (it should thin before the end)");
            yield return WaitFor(() => cloud == null || Age() >= 14.6d, 3f);
            c.Check(cloud != null && cloud.IsActive(SmokeRegistry.Now, rules), $"{F(Age())} s after impact: the cloud no longer hides");
            yield return WaitFor(() => cloud == null || Age() >= 15.15d, 3f);
            c.Check(cloud != null && !cloud.IsActive(SmokeRegistry.Now, rules) && !SmokeRegistry.InActiveCloud(cloudBase + Vector3.up, rules, SmokeRegistry.Now),
                $"{F(Age())} s after impact: the cloud still hides (or is already gone: {cloud == null})");
            yield return WaitFor(() => cloud == null, 6f);
            var goneAfter = Age();
            c.Check(cloud == null && goneAfter >= 17.8d && goneAfter <= 19.8d, $"cloud object gone {F(goneAfter)} s after impact, expected about 18 s");

            // Campfire: its cloud has been on it for more than two of its own checks.
            yield return WaitFor(() => Time.time - fireAt >= 9.5f, 10f);
            var fuelNow = fireZdo.GetFloat(ZDOVars.s_fuel);
            c.Check(fire != null && fire.IsBurning() && !fire.m_blocked, "the campfire went out under the Smoke Screen cloud");
            c.Check(fuelNow > fuel - 0.3f, $"campfire fuel {F(fuel)} -> {F(fuelNow)} under the cloud");
            c.Note($"default cloud: {F(Vector3.Distance(origin, cloudBase))} m from the thrower, gone {F(goneAfter)} s after impact; "
                   + $"campfire fuel {F(fuel)} -> {F(fuelNow)}");
            NoProblems(c);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.smoke-bars (T16, T26) ----------

    private static IEnumerator RunSmokeBars()
    {
        var c = new Checks(SmokeBarsName);
        var rig = Rig.Create(SmokeBarsName);
        var hud = EnemyHud.instance;
        if (rig == null)
        {
            yield break;
        }
        if (hud == null)
        {
            SelfTest.Fail(SmokeBarsName, "no EnemyHud");
            yield break;
        }
        try
        {
            var player = rig.Player;
            var rules = new AmbushRules { CloudDuration = AmbushRules.CloudDurationMin };
            ServerRules.TestRules = rules;
            yield return Stand(player);
            var dir = ClearDirection(player, 12f, out _);
            var side = Vector3.Cross(Vector3.up, dir).normalized;
            var origin = player.transform.position;
            rig.Aim(dir, 0f);

            // T16. Unaware Greydwarf 8 m in front, aimed at: bar shown. Smoke Screen thrown onto it.
            var spot = Ground(origin + dir * 8f);
            var g = rig.Creature("Greydwarf", spot, -dir, false);
            if (!c.Check(g != null, "could not spawn a Greydwarf"))
            {
                c.Report();
                yield break;
            }
            var plate = new Box();
            yield return Plate(hud, g, plate);
            c.Check(plate.Ok && AimAt(hud, g), "no health plate for a Greydwarf 8 m away");
            yield return Frames(2);
            c.Check(PlateShown(hud, g) && hud.TestShow(g, true), "the aimed-at Greydwarf's bar does not show before the throw");
            var before = new HashSet<SmokeCloud>(SmokeRegistry.All);
            var from = player.GetCenterPoint() + dir * 0.8f + Vector3.up * 0.3f;
            rig.Launch(from, (g.GetCenterPoint() - from).normalized * 20f);
            var slot = new CloudSlot();
            yield return WaitForCloud(before, 3f, slot);
            var cloud = slot.Cloud;
            if (c.Check(cloud != null, "no cloud from the Smoke Screen thrown onto the Greydwarf"))
            {
                var ai = (MonsterAI)g.GetBaseAI();
                c.Check(!ai.IsAlerted(), "the Greydwarf hit by the Smoke Screen is alerted");
                yield return WaitFor(() => cloud == null || cloud.IsActive(SmokeRegistry.Now, rules), 2f);
                var activeAt = Time.time;
                var removed = new Box();
                yield return WaitFor(() => !hud.m_huds.ContainsKey(g), 1f, removed);
                c.Check(removed.Ok && !hud.TestShow(g, true), $"its bar did not disappear within 1 s of the smoke building up (plate gone {removed.Ok})");
                c.Note($"bar gone {F(Time.time - activeAt)} s after the cloud became active");
                // It faces the standing player 8 m away, AI on now (held in place): it must not notice him.
                ai.enabled = true;
                var noticed = false;
                var wouldSee = false;
                var back = false;
                while (cloud != null && SmokeRegistry.Now < cloud.EndTime - 0.15d)
                {
                    Place(g, spot, -dir);
                    yield return null;
                    noticed |= ReferenceEquals(ai.m_targetCreature, player) || ai.CanSeeTarget(player);
                    back |= hud.m_huds.ContainsKey(g);
                    if (!wouldSee)
                    {
                        WithoutSmoke(ai, player, out wouldSee, out _);
                    }
                }
                ai.enabled = false;
                c.Check(wouldSee, "without smoke the Greydwarf facing the standing player 8 m away would not see him: nothing proven");
                c.Check(!noticed, "the Greydwarf inside the cloud noticed the player outside it");
                c.Check(!back, "its bar came back while it was in the cloud");
                // After the cloud: bar again, but only once aimed at.
                yield return WaitFor(() => cloud == null || SmokeRegistry.Now >= cloud.EndTime + 0.2d, 3f);
                c.Check(hud.TestShow(g, true), "cloud over: its bar is still hidden");
                yield return Plate(hud, g, plate, 60);
                yield return Frames(2);
                c.Check(plate.Ok && !PlateShown(hud, g), "cloud over: the bar shows again without aiming at the creature");
                c.Check(AimAt(hud, g), "cloud over: no plate to aim at");
                yield return Frames(2);
                c.Check(PlateShown(hud, g), "cloud over: the bar does not show again after aiming at the creature");
            }
            rig.Remove(g.gameObject);
            yield return Frames(3);

            // T26. Tamed Boar (what console "tame" calls), aimed at, cloud on it, player outside: bar stays.
            var boarSpot = Ground(origin + dir * 6f + side * 3f);
            var boar = rig.Creature("Boar", boarSpot, -dir, false);
            if (c.Check(boar != null, "could not spawn a Boar"))
            {
                yield return Frames(3);
                var tameable = boar.GetComponent<Tameable>();
                if (tameable != null)
                {
                    tameable.Tame();
                }
                c.Check(boar.IsTamed(), "the Boar is not tamed");
                yield return Plate(hud, boar, plate);
                c.Check(plate.Ok && AimAt(hud, boar), "no health plate for the tamed Boar");
                yield return Frames(2);
                c.Check(PlateShown(hud, boar), "the tamed Boar's bar does not show before the smoke");
                var boarSlot = new CloudSlot();
                yield return rig.PutCloud(boarSlot, boar.transform.position);
                yield return WaitFor(() => boarSlot.Cloud == null || boarSlot.Cloud.IsActive(SmokeRegistry.Now, rules), 2f);
                c.Check(boarSlot.Cloud != null && boarSlot.Cloud.Contains(boar.GetCenterPoint()) && !InSmoke(player, rules),
                    "the Boar is not inside the active cloud with the player outside it");
                var kept = true;
                for (var i = 0; i < 45; i++)
                {
                    yield return null;
                    kept &= hud.TestShow(boar, true) && PlateShown(hud, boar);
                }
                c.Check(kept, "the tamed Boar's bar went while it stood in the smoke");
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.smoke-geometry (T17, T18) ----------

    private static IEnumerator RunSmokeGeometry()
    {
        var c = new Checks(SmokeGeometryName);
        var rig = Rig.Create(SmokeGeometryName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            var rules = new AmbushRules { ActivationDelay = 0f };
            ServerRules.TestRules = rules;
            yield return Stand(player);
            // Open ground with room for the side step of T18 (first run: at the start spot of the test world the
            // scenery blocked both sides): 16 m lane, and from where the Greydwarf will stand (15 m ahead) a free
            // line to a spot 12 m to one side of the player.
            var water = ZoneSystem.instance.m_waterLevel;
            bool SideStep(Vector3 feet, Vector3 d)
            {
                var s = Vector3.Cross(Vector3.up, d).normalized;
                var eye = Ground(feet + d * 15f) + Vector3.up * 1.4f;
                foreach (var sign in new[] { 1f, -1f })
                {
                    var step = Ground(feet + s * (sign * 12f));
                    if (Mathf.Abs(step.y - feet.y) < 2f && step.y > water + 0.3f
                        && !Physics.Linecast(eye, step + Vector3.up * 1.6f, ViewMask)
                        && !Physics.Linecast(eye, step + Vector3.up * 0.9f, ViewMask)
                        && !Physics.Linecast(eye + Vector3.up * 0.4f, step + Vector3.up * 1.6f, ViewMask))
                    {
                        return true;
                    }
                }
                return false;
            }

            var place = new Spot();
            yield return FindOpenSpot(rig, 16f, false, SideStep, place);
            if (!place.Found)
            {
                c.Note("no open ground with room for the side step found within 48 m: sight checks may be blocked by the scenery");
            }
            var dir = place.Dir;
            var side = Vector3.Cross(Vector3.up, dir).normalized;
            var origin = player.transform.position;
            var g = rig.Creature("Greydwarf", Ground(origin + dir * 8f), -dir, false);
            if (!c.Check(g != null, "could not spawn a Greydwarf"))
            {
                c.Report();
                yield break;
            }
            yield return Frames(3);
            var ai = g.GetBaseAI();
            var slot = new CloudSlot();

            // T17. Both inside one cloud: noticed only within 2.5 m (eye to eye, as the rule measures).
            yield return rig.PutCloud(slot, origin);
            foreach (var want in new[] { 2.2f, 2.8f })
            {
                var reach = want;
                var measured = 0f;
                for (var i = 0; i < 3; i++)
                {
                    Place(g, Ground(origin + dir * reach), -dir);
                    yield return Frames(2);
                    measured = Vector3.Distance(g.m_eye.position, SmokeRegistry.TracedPoint(player));
                    reach += want - measured;
                }
                var inside = slot.Cloud != null && slot.Cloud.Contains(g.m_eye.position) && InSmoke(player, rules);
                WithoutSmoke(ai, player, out var wouldSee, out _);
                c.Check(inside && wouldSee, $"{F(measured)} m apart: both are not inside the cloud, or it would not see him anyway ({inside}, {wouldSee})");
                if (want < rules.InsideSightRange)
                {
                    c.Check(ai.CanSeeTarget(player), $"both inside, {F(measured)} m apart: the Greydwarf does not notice the player");
                }
                else
                {
                    c.Check(!ai.CanSeeTarget(player), $"both inside, {F(measured)} m apart: the Greydwarf notices the player");
                }
            }

            // T18. Cloud between, neither inside, standing player about 15 m away.
            var far = Mathf.Min(15f, ai.m_viewRange - 2f);
            var spot = Ground(origin + dir * far);
            Place(g, spot, -dir);
            yield return rig.PutCloud(slot, origin + dir * (far * 0.5f));
            yield return Frames(2);
            if (c.Check(slot.Cloud != null, "no cloud between"))
            {
                var cloud = slot.Cloud;
                WithoutSmoke(ai, player, out var wouldSee, out _);
                c.Check(!cloud.Contains(g.m_eye.position) && !InSmoke(player, rules) && wouldSee,
                    $"cloud between at {F(far)} m: one of them is inside it, or the Greydwarf would not see the player anyway ({wouldSee})");
                c.Check(!ai.CanSeeTarget(player), "cloud between: the Greydwarf sees the standing player through it");
                // Step sideways out of the cloud's line.
                var seen = false;
                var tried = false;
                foreach (var sign in new[] { 1f, -1f })
                {
                    var step = Ground(origin + side * (sign * 12f));
                    if (Physics.Linecast(g.m_eye.position, step + Vector3.up * 1.6f, ViewMask))
                    {
                        continue;
                    }
                    tried = true;
                    MovePlayer(player, step + Vector3.up * 0.05f);
                    rig.MarkMoved();
                    Place(g, spot, step - spot);
                    yield return Frames(3);
                    c.Check(!cloud.SegmentCrosses(g.m_eye.position, SmokeRegistry.TracedPoint(player)),
                        "12 m to the side the line of sight still crosses the cloud");
                    seen = ai.CanSeeTarget(player);
                    break;
                }
                c.Check(tried, "no clear spot 12 m to either side of the player (scenery): sidestep not checked");
                c.Check(!tried || seen, "out of the cloud's line: the Greydwarf does not see the standing player");
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.smoke-burst (T19, T31) ----------

    private static IEnumerator RunSmokeBurst()
    {
        var c = new Checks(SmokeBurstName);
        var rig = Rig.Create(SmokeBurstName);
        var hud = EnemyHud.instance;
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            var rules = new AmbushRules();
            ServerRules.TestRules = rules;
            Tap.Install();
            yield return Stand(player);
            var dir = ClearDirection(player, 14f, out _);
            var origin = player.transform.position;

            // Three pursuers 3-5 m around the front, AI on, alerted, after the player. They "came" from 25 m ahead.
            var home = Ground(origin + dir * 25f);
            var chasers = new List<Character>();
            foreach (var place in new[] { new Vector2(-35f, 3.2f), new Vector2(0f, 4.2f), new Vector2(35f, 5f) })
            {
                var p = Ground(origin + Quaternion.Euler(0f, place.x, 0f) * dir * place.y);
                var chaser = rig.Creature("Greydwarf", p, origin - p, true);
                if (chaser != null)
                {
                    chasers.Add(chaser);
                }
            }
            // T31: one more that only noticed the player (he is its target, it is not alerted), 10 m behind, looking away.
            // Its spot: 10 m behind the player if nothing stands between (later it is turned to face him), else beside.
            var watchSpot = Ground(origin - dir * 10f);
            foreach (var turn in new[] { 180f, 135f, 225f, 110f, 250f })
            {
                var p = Ground(origin + Quaternion.Euler(0f, turn, 0f) * dir * 10f);
                if (Mathf.Abs(p.y - origin.y) < 2f && !Physics.Linecast(p + Vector3.up * 1.4f, player.GetCenterPoint(), ViewMask)
                    && !Physics.Linecast(p + Vector3.up * 1.4f, player.m_eye.position, ViewMask))
                {
                    watchSpot = p;
                    break;
                }
            }
            var lookAway = Flat(watchSpot - origin);
            var watcher = rig.Creature("Greydwarf", watchSpot, lookAway, true);
            if (!c.Check(chasers.Count == 3 && watcher != null, "could not spawn four Greydwarfs"))
            {
                c.Report();
                yield break;
            }
            yield return Frames(3);
            var ais = chasers.Select(x => (MonsterAI)x.GetBaseAI()).ToList();
            foreach (var ai in ais)
            {
                ai.m_spawnPoint = home;
                ai.SetAlerted(true);
                ai.m_targetCreature = player;
            }
            var watcherAi = (MonsterAI)watcher.GetBaseAI();
            watcherAi.m_targetCreature = player;
            for (var i = 0; i < 5; i++)
            {
                // Held looking away from the start: it must not get to see the player before the burst.
                Place(watcher, watchSpot, lookAway);
                yield return null;
            }
            if (hud != null)
            {
                foreach (var chaser in chasers)
                {
                    AimAt(hud, chaser);
                }
            }
            Tap.Clear();
            var slot = new CloudSlot();
            yield return rig.PutCloud(slot, origin);
            var cloud = slot.Cloud;
            if (!c.Check(cloud != null, "no cloud at the player's feet"))
            {
                c.Report();
                yield break;
            }
            var start = cloud.StartTime;
            var watcherReady = true;
            var wait = Time.time;
            while (Time.time - wait < 2.5f && AiMemory.BlindCount < 3)
            {
                // Held looking away until the burst: it must not get to see the player meanwhile.
                Place(watcher, watchSpot, lookAway);
                if (!cloud.IsActive(SmokeRegistry.Now, rules))
                {
                    watcherReady &= ReferenceEquals(watcherAi.m_targetCreature, player) && !watcherAi.IsAlerted();
                    foreach (var ai in ais)
                    {
                        // Still pursuers at the burst, whatever the half second before it did.
                        ai.m_targetCreature = player;
                    }
                }
                yield return null;
            }
            var now = SmokeRegistry.Now;
            var blindAt = Time.time;
            c.Check(chasers.All(x => AiMemory.IsBlinded(x.transform, now)), "not every pursuer was blinded by the burst at the player's feet");
            c.Check(AiMemory.BlindCount == 3, $"{AiMemory.BlindCount} creatures blinded, expected the 3 pursuers");
            c.Check(watcherReady, "the fourth Greydwarf did not stay 'noticed, not alerted' until the burst: T31 not checked");
            c.Check(!AiMemory.IsBlinded(watcher.transform, now), "a creature that only noticed the player (not alerted) was blinded");
            c.Check(Tap.Logged("Smoke Screen burst: 3 creature(s) chasing a player nearby lose their sight for") == 1,
                $"Debug burst line for 3 creatures not logged once ({Tap.FirstLogged("Smoke Screen burst")})");
            // T31, second half: not blinded, but the cloud hides the player from it like from any creature. Turned to
            // face him from its spot 10 m away (outside the cloud).
            Place(watcher, watchSpot, -lookAway);
            yield return Frames(2);
            WithoutSmoke(watcherAi, player, out var watcherWouldSee, out _);
            c.Check(!cloud.Contains(watcher.m_eye.position) && InSmoke(player, rules) && watcherWouldSee,
                $"the fourth Greydwarf is inside the cloud, the player is not, or it would not see him without smoke either ({watcherWouldSee}): nothing proven");
            c.Check(!watcherAi.CanSeeTarget(player), "the creature that only noticed the player sees him in the cloud");
            rig.Remove(watcher.gameObject);

            // They stop attacking and give up within 3-4 s.
            var lateAttack = false;
            bool GaveUp() => ais.All(ai => ai.m_targetCreature == null && !ai.IsAlerted());
            while (Time.time - blindAt < rules.ForgetSeconds + 2f && !GaveUp())
            {
                if (Time.time - blindAt > 1.2f)
                {
                    lateAttack |= chasers.Any(x => x.InAttack());
                }
                yield return null;
            }
            var gaveUpAfter = Time.time - blindAt;
            c.Check(GaveUp(), $"not all three gave up within {F(gaveUpAfter)} s of the burst");
            c.Check(gaveUpAfter <= rules.ForgetSeconds + 0.8f, $"they gave up only {F(gaveUpAfter)} s after the burst (expected 3-4 s after the throw)");
            c.Check(!lateAttack, "a blinded pursuer still attacked more than 1.2 s after the burst");
            yield return Frames(3);
            if (hud != null)
            {
                var icons = 0;
                foreach (var chaser in chasers)
                {
                    if (hud.m_huds.TryGetValue(chaser, out var data) && data.m_gui != null && data.m_alerted != null)
                    {
                        icons++;
                        c.Check(!data.m_alerted.gameObject.activeSelf, "the alert icon of a pursuer that gave up is still on");
                    }
                }
                c.Note($"{icons} of 3 pursuers had a health plate to read the alert icon from");
                c.Check(icons > 0, "no pursuer had a health plate: the alert icons were not checked");
            }

            // They walk back the way they came (the idle pause before the next step is skipped).
            var away = new List<float>();
            foreach (var ai in ais)
            {
                ai.m_randomMoveUpdateTimer = 0f;
                away.Add(Utils.DistanceXZ(ai.transform.position, home));
            }
            yield return new WaitForSeconds(4f);
            for (var i = 0; i < chasers.Count; i++)
            {
                var d = Utils.DistanceXZ(chasers[i].transform.position, home);
                c.Check(d < away[i] - 1.5f, $"pursuer {i + 1} did not walk back toward where it came from ({F(away[i])} -> {F(d)} m away from it)");
            }

            // After the blind (6 s from the impact), out of the smoke and in front of one: it can find the player again.
            yield return WaitFor(() => SmokeRegistry.Now >= SmokeCloud.BlindEnd(start, cloud != null ? cloud.Duration : rules.CloudDuration, rules) + 0.2d, 8f);
            var first = chasers[0];
            var outside = Ground(first.transform.position + Flat(first.transform.forward) * 5f);
            MovePlayer(player, outside + Vector3.up * 0.05f);
            rig.MarkMoved();
            yield return Frames(3);
            c.Check(!InSmoke(player, rules) && !AiMemory.IsBlinded(first.transform, SmokeRegistry.Now), "the player is still in smoke, or the blind is not over");
            var found = new Box();
            yield return WaitFor(() => ais.Any(ai => ReferenceEquals(ai.m_targetCreature, player)), 8f, found);
            c.Check(found.Ok, "8 s after the blind none of the three found the player standing in front of them outside the smoke");
            c.Note($"three pursuers gave up {F(gaveUpAfter)} s after the burst");
            NoProblems(c);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.smoke-reveal (T20) ----------

    private static IEnumerator RunSmokeReveal()
    {
        var c = new Checks(SmokeRevealName);
        var rig = Rig.Create(SmokeRevealName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            // Default rules, cloud long enough for both halves of the test.
            var rules = XpRules(c);
            rules.CloudDuration = 30f;
            ServerRules.TestRules = rules;
            Tap.Install();
            rig.SaveAllSkills();
            rig.SetSneak(20f);
            yield return Stand(player);
            // Open ground ahead (Greydwarfs) and behind (the Deer, 11 m back, and room for it to run).
            var place = new Spot();
            yield return FindOpenSpot(rig, 14f, false, (feet, d) => Lane(feet, -d, 14f), place);
            if (!place.Found)
            {
                c.Note("no open ground with 14 m free ahead and behind found within 48 m: the scenery may be in the way");
            }
            var dir = place.Dir;
            var side = Vector3.Cross(Vector3.up, dir).normalized;
            var origin = player.transform.position;
            var slot = new CloudSlot();
            yield return rig.PutCloud(slot, origin);
            var cloud = slot.Cloud;
            yield return WaitFor(() => cloud == null || cloud.IsActive(SmokeRegistry.Now, rules), 2f);
            if (!c.Check(cloud != null && InSmoke(player, rules), "the player is not inside an active cloud"))
            {
                c.Report();
                yield break;
            }
            var bow = Shared("Bow");
            var arrow = Shared("ArrowWood");
            var noise = (bow != null && bow.m_attack != null ? bow.m_attack.m_attackHitNoise : 0f)
                        + (arrow != null && arrow.m_attack != null ? arrow.m_attack.m_attackHitNoise : 0f);

            // Two unaware two-star Greydwarfs outside the cloud, 15 m from each other, AI on.
            var a = rig.Creature("Greydwarf", Ground(origin + dir * 10f), -dir, true);
            var b = rig.Creature("Greydwarf", Ground(origin + dir * 10f + side * 15f), -dir, true);
            if (!c.Check(a != null && b != null, "could not spawn two Greydwarfs"))
            {
                c.Report();
                yield break;
            }
            a.SetLevel(3);
            b.SetLevel(3);
            yield return new WaitForSeconds(1.2f);
            var aiA = (MonsterAI)a.GetBaseAI();
            var aiB = (MonsterAI)b.GetBaseAI();
            c.Check(a.GetLevel() == 3 && Near(a.GetMaxHealth(), 120f, 0.5f), $"two-star Greydwarf: level {a.GetLevel()}, {F(a.GetMaxHealth())} health, expected 120");
            c.Check(!aiA.IsAlerted() && !ReferenceEquals(aiA.m_targetCreature, player) && !aiB.IsAlerted() && !ReferenceEquals(aiB.m_targetCreature, player),
                "a Greydwarf outside noticed the player in the smoke before the shot");
            var health = a.GetHealth();
            var backstabTime = a.m_backstabTime;
            var shot = new Box();
            yield return Shoot(rig, "Bow", "ArrowWood", a, 20f, noise, shot);
            if (c.Check(shot.Ok && a != null && !a.IsDead(), $"the arrow did not hit, or killed it ({shot.Detail})"))
            {
                var dealt = health - a.GetHealth();
                c.Check(a.m_backstabTime != backstabTime && dealt >= 33f && dealt <= 73f, $"the arrow did {F(dealt)} damage (backstab {a.m_backstabTime != backstabTime}), expected a backstab of 33-73");
                c.Check(AiMemory.IsRevealed(a.transform, player, SmokeRegistry.Now) && aiA.CanSeeTarget(player),
                    "the Greydwarf that was shot does not see the player in the smoke");
                var from = Vector3.Distance(a.transform.position, player.transform.position);
                var came = false;
                var attacked = false;
                var otherFound = false;
                var watch = Time.time;
                while (Time.time - watch < 8f && !(came && attacked) && a != null && b != null)
                {
                    if (!InSmoke(player, rules))
                    {
                        // Pushed out by a hit: back into the smoke.
                        MovePlayer(player, origin + Vector3.up * 0.05f);
                    }
                    yield return null;
                    if (a == null || b == null)
                    {
                        break;
                    }
                    came |= Vector3.Distance(a.transform.position, player.transform.position) < from - 3f;
                    attacked |= a.InAttack();
                    otherFound |= InSmoke(player, rules) && ReferenceEquals(aiB.m_targetCreature, player)
                                  && Vector3.Distance(b.m_eye.position, SmokeRegistry.TracedPoint(player)) > rules.InsideSightRange + 0.3f;
                }
                c.Check(ReferenceEquals(aiA.m_targetCreature, player) && aiA.IsAlerted(), "the shot Greydwarf does not have the player as its target");
                c.Check(came, "the shot Greydwarf did not come into the smoke within 8 s");
                c.Check(attacked, "the shot Greydwarf did not attack the player within 8 s");
                c.Check(!otherFound, "the second Greydwarf found the player in the smoke from more than 2.5 m");
            }
            if (a != null)
            {
                rig.Remove(a.gameObject);
            }
            if (b != null)
            {
                rig.Remove(b.gameObject);
            }
            MovePlayer(player, origin + Vector3.up * 0.05f);
            yield return Frames(3);

            // Level-9 Deer (90 health) outside the cloud, shot from inside: survives and flees from the player.
            var deer = rig.Creature("Deer", Ground(origin - dir * 11f), dir, true);
            if (c.Check(deer != null && cloud != null && cloud.IsActive(SmokeRegistry.Now, rules), "could not spawn a Deer while the cloud is still up"))
            {
                deer.SetLevel(9);
                yield return new WaitForSeconds(1f);
                var deerAi = deer.GetBaseAI() as AnimalAI;
                c.Check(deer.GetLevel() == 9 && Near(deer.GetMaxHealth(), 90f, 0.5f), $"level-9 Deer: level {deer.GetLevel()}, {F(deer.GetMaxHealth())} health, expected 90");
                c.Check(deerAi != null && !deerAi.IsAlerted(), "the Deer noticed the player in the smoke before the shot");
                health = deer.GetHealth();
                yield return Shoot(rig, "Bow", "ArrowWood", deer, 20f, noise, shot);
                if (c.Check(shot.Ok && deer != null && !deer.IsDead() && deerAi != null, $"the arrow did not hit the Deer, or killed it ({shot.Detail})"))
                {
                    var dealt = health - deer.GetHealth();
                    c.Check(dealt >= 33f && dealt <= 73f, $"the arrow did {F(dealt)} damage to the Deer, expected 33-73");
                    var targeted = new Box();
                    yield return WaitFor(() => deer == null || ReferenceEquals(deerAi.m_target, player), 3.5f, targeted);
                    c.Check(deer != null && ReferenceEquals(deerAi.m_target, player), "the shot Deer does not know where the player is (it should flee from him)");
                    if (deer != null)
                    {
                        // It was looking at the player: it turn, pick a way and run (first run: only 2 m further
                        // after 2.5 s). Waited for, bounded: 4 m further away within 8 s.
                        var from = Vector3.Distance(deer.transform.position, player.transform.position);
                        var to = from;
                        var fleeing = Time.time;
                        while (Time.time - fleeing < 8f && deer != null && to <= from + 4f)
                        {
                            yield return null;
                            to = deer != null ? Vector3.Distance(deer.transform.position, player.transform.position) : from + 100f;
                        }
                        c.Check(to > from + 4f, $"the shot Deer did not flee from the player within 8 s ({F(from)} -> {F(to)} m)");
                        c.Note($"shot Deer: {F(from)} -> {F(to)} m from the player {F(Time.time - fleeing)} s after it knew where he is");
                    }
                }
            }
            NoProblems(c);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.smoke-boss (T21) ----------

    private static IEnumerator RunSmokeBoss()
    {
        var c = new Checks(SmokeBossName);
        var rig = Rig.Create(SmokeBossName);
        var hud = EnemyHud.instance;
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            var rules = new AmbushRules();
            ServerRules.TestRules = rules;
            Tap.Install();
            yield return Stand(player);
            var dir = ClearDirection(player, 14f, out _);
            var origin = player.transform.position;
            var boss = rig.Creature("Eikthyr", Ground(origin + dir * 9f), -dir, true);
            if (!c.Check(boss != null && boss.IsBoss() && hud != null, "could not spawn Eikthyr (a boss), or no EnemyHud"))
            {
                c.Report();
                yield break;
            }
            var ai = (MonsterAI)boss.GetBaseAI();
            var hunting = new Box();
            yield return WaitFor(() => ReferenceEquals(ai.m_targetCreature, player) && ai.IsAlerted(), 8f, hunting);
            c.Check(hunting.Ok, "Eikthyr does not hunt the player standing 9 m in front of it");
            // Cloud where the player stands now (a hit may have pushed him).
            var slot = new CloudSlot();
            yield return rig.PutCloud(slot, player.transform.position);
            var cloud = slot.Cloud;
            if (!c.Check(cloud != null, "no cloud"))
            {
                c.Report();
                yield break;
            }
            var spot = cloud.Base + Vector3.up * 0.1f;
            yield return WaitFor(() => cloud == null || cloud.IsActive(SmokeRegistry.Now, rules), 2f);
            MovePlayer(player, spot);
            rig.MarkMoved();
            yield return Frames(3);
            c.Check(InSmoke(player, rules), "the player is not inside the active cloud");
            c.Check(AiMemory.BlindCount == 0 && !AiMemory.IsBlinded(boss.transform, SmokeRegistry.Now), "Eikthyr was blinded by the burst");
            c.Check(Tap.Logged("Smoke Screen burst") == 0, $"a burst blinded something: {Tap.FirstLogged("Smoke Screen burst")}");
            c.Check(ai.CanSeeTarget(player), "Eikthyr does not see the player in the smoke");
            // Same creature without its boss flag would be fooled (when the cloud's shape hides the player from it now).
            if (SmokeRegistry.GeometryHides(boss.m_eye.position, SmokeRegistry.TracedPoint(player), rules, SmokeRegistry.Now))
            {
                boss.m_boss = false;
                var fooled = !ai.CanSeeTarget(player);
                boss.m_boss = true;
                c.Check(fooled, "control: without its boss flag the same creature is not fooled either (the smoke hides nothing here)");
            }
            c.Check(hud.TestShow(boss, true), "Eikthyr's health bar is hidden by the smoke");
            var attacked = false;
            var kept = true;
            var barKept = true;
            var watch = Time.time;
            while (Time.time - watch < 10f && !attacked && boss != null)
            {
                if (!InSmoke(player, rules))
                {
                    MovePlayer(player, spot);
                }
                yield return null;
                if (boss == null)
                {
                    break;
                }
                attacked |= boss.InAttack();
                kept &= ReferenceEquals(ai.m_targetCreature, player);
                barKept &= hud.TestShow(boss, true);
            }
            c.Check(attacked, "Eikthyr did not attack the player in the smoke within 10 s");
            c.Check(kept, "Eikthyr lost the player in the smoke");
            c.Check(barKept && hud.m_huds.ContainsKey(boss), "Eikthyr's health bar did not stay");
            NoProblems(c);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.smoke-sleeper (T25) ----------

    private static IEnumerator RunSmokeSleeper()
    {
        var c = new Checks(SmokeSleeperName);
        var rig = Rig.Create(SmokeSleeperName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            var rules = new AmbushRules { ActivationDelay = 0f };
            ServerRules.TestRules = rules;
            yield return Stand(player);
            var dir = ClearDirection(player, 20f, out _);
            var origin = player.transform.position;
            var lair = Ground(origin + dir * 19f);
            var draugr = rig.Creature("Draugr_sleeping", lair, -dir, true);
            var ai = draugr != null ? draugr.GetBaseAI() as MonsterAI : null;
            if (!c.Check(ai != null, "could not spawn Draugr_sleeping"))
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(1.5f);
            var wake = ai.m_wakeupRange;
            c.Note($"Draugr_sleeping wakes within {F(wake)} m (noise wake-up {ai.m_noiseWakeup}, delay {F(ai.m_wakeUpDelayMin)}-{F(ai.m_wakeUpDelayMax)} s)");
            if (!c.Check(ai.IsSleeping() && wake > 3.6f && wake < 18f,
                    $"the Draugr 19 m away is {(ai.IsSleeping() ? "asleep" : "awake")}, wake range {F(wake)} m: the test needs it asleep with a range of 3.6-18 m"))
            {
                c.Report();
                yield break;
            }
            // Cloud inside its wake range, more than 2.5 m from it; player walks into the cloud.
            var apart = Mathf.Min(wake - 1f, 7.5f);
            var spot = Ground(draugr.transform.position - dir * apart);
            var slot = new CloudSlot();
            yield return rig.PutCloud(slot, spot);
            yield return Frames(2);
            MovePlayer(player, spot + Vector3.up * 0.05f);
            rig.MarkMoved();
            var woke = new Box();
            yield return WaitFor(() => !ai.IsSleeping(), 10f, woke);
            if (c.Check(woke.Ok, $"the Draugr did not wake within 10 s with the player {F(apart)} m away"))
            {
                var came = false;
                var wouldSee = false;
                var nearest = 100f;
                var watch = Time.time;
                while (Time.time - watch < 6f && draugr != null)
                {
                    yield return null;
                    if (draugr == null)
                    {
                        break;
                    }
                    var gap = Vector3.Distance(draugr.m_eye.position, SmokeRegistry.TracedPoint(player));
                    nearest = Mathf.Min(nearest, gap);
                    if (!InSmoke(player, rules) || gap <= rules.InsideSightRange + 0.2f)
                    {
                        continue; // not the case of the item (out of the smoke, or closer than 2.5 m)
                    }
                    came |= ReferenceEquals(ai.m_targetCreature, player) || ai.CanSeeTarget(player) || ai.CanHearTarget(player);
                    if (!wouldSee)
                    {
                        WithoutSmoke(ai, player, out wouldSee, out _);
                    }
                }
                c.Check(wouldSee, "without smoke the woken Draugr would not see the player either: nothing proven");
                c.Check(!came, "the woken Draugr came for the player in the smoke (more than 2.5 m away)");
                c.Note($"woken Draugr: nearest {F(nearest)} m from the player in 6 s");
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.smoke-hunt (T29) ----------

    private static IEnumerator RunSmokeHunt()
    {
        var c = new Checks(SmokeHuntName);
        var rig = Rig.Create(SmokeHuntName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            var rules = new AmbushRules();
            ServerRules.TestRules = rules;
            Tap.Install();
            yield return Stand(player);
            var dir = ClearDirection(player, 12f, out _);
            var origin = player.transform.position;
            var g = rig.Creature("Greydwarf", Ground(origin + dir * 6f), -dir, true);
            if (!c.Check(g != null, "could not spawn a Greydwarf"))
            {
                c.Report();
                yield break;
            }
            yield return Frames(3);
            var ai = (MonsterAI)g.GetBaseAI();
            // As raid and hunt spawns are.
            ai.SetHuntPlayer(true);
            ai.SetAlerted(true);
            ai.m_targetCreature = player;
            yield return new WaitForSeconds(0.3f);
            var slot = new CloudSlot();
            yield return rig.PutCloud(slot, origin);
            var cloud = slot.Cloud;
            if (!c.Check(cloud != null, "no cloud"))
            {
                c.Report();
                yield break;
            }
            var start = cloud.StartTime;
            var blindEnd = SmokeCloud.BlindEnd(start, cloud.Duration, rules);
            var blinded = new Box();
            yield return WaitFor(() => AiMemory.IsBlinded(g.transform, SmokeRegistry.Now), 2f, blinded);
            c.Check(blinded.Ok, "the hunting Greydwarf chasing the player was not blinded by the burst");
            var fromFar = Vector3.Distance(g.transform.position, player.transform.position);
            var lost = false;
            var saw = false;
            var struck = false;
            // "Cannot attack" = it begins no attack while blind. A swing begun before the burst still plays to its end
            // (first run: this test counted that animation, 0.7 s after the blind, as an attack). A new attack is seen
            // two ways: vanilla puts the AI's attack timer back to 0 (MonsterAI.DoAttack), and the animation goes from
            // not attacking to attacking.
            var wasAttacking = g.InAttack();
            var sinceAttack = ai.m_timeSinceAttacking;
            var oldSwing = wasAttacking;
            var oldSwingEnd = 0d;
            // Well past ForgetSeconds (a normal pursuer gave up by now), still inside the blind.
            while (SmokeRegistry.Now < blindEnd - 1.2d && g != null)
            {
                if (!InSmoke(player, rules))
                {
                    MovePlayer(player, origin + Vector3.up * 0.05f);
                }
                yield return null;
                if (g == null)
                {
                    break;
                }
                lost |= !ReferenceEquals(ai.m_targetCreature, player) || !ai.IsAlerted();
                var attacking = g.InAttack();
                if (AiMemory.IsBlinded(g.transform, SmokeRegistry.Now))
                {
                    saw |= ai.CanSeeTarget(player);
                    struck |= (attacking && !wasAttacking) || ai.m_timeSinceAttacking < sinceAttack - 0.001f;
                }
                if (oldSwing && !attacking)
                {
                    oldSwing = false;
                    oldSwingEnd = SmokeRegistry.Now - start;
                }
                wasAttacking = attacking;
                sinceAttack = ai.m_timeSinceAttacking;
            }
            var near = g != null ? Vector3.Distance(g.transform.position, player.transform.position) : fromFar;
            c.Check(!lost, $"the hunting Greydwarf gave up or lost its target within {F(SmokeRegistry.Now - start)} s of the burst (hunters never give up)");
            c.Check(!saw && !struck, $"while blinded the hunter saw ({saw}) the player or began an attack ({struck})");
            c.Check(!oldSwing, "a swing the hunter began before the burst was still playing shortly before the end of the blind");
            if (oldSwingEnd > 0d)
            {
                c.Note($"a swing begun before the burst ended {F(oldSwingEnd)} s after the impact (not counted as an attack while blind)");
            }
            c.Check(near < fromFar - 2f || near < 3f, $"the blinded hunter did not keep coming for the player ({F(fromFar)} -> {F(near)} m)");
            // Blind over: inside the cloud it sees the player within 2.5 m and attacks.
            yield return WaitFor(() => SmokeRegistry.Now >= blindEnd + 0.1d, 3f);
            var seen = false;
            var attacked = false;
            var watch = Time.time;
            while (Time.time - watch < 7f && !(seen && attacked) && g != null)
            {
                if (!InSmoke(player, rules))
                {
                    MovePlayer(player, origin + Vector3.up * 0.05f);
                }
                yield return null;
                if (g == null)
                {
                    break;
                }
                if (Vector3.Distance(g.m_eye.position, SmokeRegistry.TracedPoint(player)) <= rules.InsideSightRange)
                {
                    seen |= ai.CanSeeTarget(player);
                }
                attacked |= g.InAttack();
            }
            c.Check(seen, "after the blind the hunter within 2.5 m of the player in the cloud does not see him");
            c.Check(attacked, "after the blind the hunter did not attack the player within 7 s");
            c.Check(ReferenceEquals(ai.m_targetCreature, player) && ai.IsAlerted(), "the hunter no longer has the player as its target");
            NoProblems(c);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.smoke-late (T28) ----------

    private static IEnumerator RunSmokeLate()
    {
        var c = new Checks(SmokeLateName);
        var rig = Rig.Create(SmokeLateName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            var rules = new AmbushRules();
            ServerRules.TestRules = rules;
            Tap.Install();
            yield return Stand(player);
            var dir = ClearDirection(player, 10f, out _);
            var origin = player.transform.position;
            // A pursuer a live burst would blind, and an onlooker a live cloud would hide the player from.
            var chaser = rig.Creature("Greydwarf", Ground(origin + dir * 3.5f), -dir, true);
            var onlooker = rig.Creature("Greydwarf", Ground(origin - dir * 8f), dir, false);
            if (!c.Check(chaser != null && onlooker != null && SmokeContent.CloudPrefab != null, "could not spawn two Greydwarfs, or no cloud prefab"))
            {
                c.Report();
                yield break;
            }
            yield return Frames(3);
            var chaserAi = (MonsterAI)chaser.GetBaseAI();
            chaserAi.SetAlerted(true);
            chaserAi.m_targetCreature = player;
            var onlookerAi = onlooker.GetBaseAI();
            c.Check(onlookerAi.CanSeeTarget(player), "the onlooker 8 m away does not see the player before the test (scenery?)");
            var visualName = new HashSet<string> { SmokeContent.CloudName + "_visual" };
            var visuals = CountNamed(visualName);
            var blindBefore = AiMemory.BlindCount;
            Tap.Clear();

            // Cloud as it arrives from a save or from another game long after its end: start 60 s ago, keys set
            // before its first frame.
            var go = Object.Instantiate(SmokeContent.CloudPrefab, origin, Quaternion.identity);
            rig.Spawned.Add(go);
            var late = go.GetComponent<SmokeCloud>();
            var zdo = go.GetComponent<ZNetView>().GetZDO();
            zdo.Set(SmokeCloud.StartKey, System.Math.Max(1L, NowMs() - 60000L));
            zdo.Set(SmokeCloud.DurationKey, rules.CloudDuration);
            zdo.Set(SmokeCloud.RadiusKey, rules.CloudRadius);
            zdo.Set(SmokeCloud.HeightKey, rules.CloudHeight);
            var hid = InSmoke(player, rules) || !onlookerAi.CanSeeTarget(player);
            var blinded = false;
            var frames = 0;
            for (var i = 0; i < 4; i++)
            {
                yield return null;
                if (late != null)
                {
                    frames++;
                }
                hid |= InSmoke(player, rules) || !onlookerAi.CanSeeTarget(player);
                blinded |= AiMemory.IsBlinded(chaser.transform, SmokeRegistry.Now);
            }
            c.Check(late == null, "a cloud 60 s past its start is still there after 4 frames");
            c.Check(!hid, "a cloud past its end hid the player");
            c.Check(!blinded && AiMemory.BlindCount == blindBefore && Tap.Logged("Smoke Screen burst") == 0, "a cloud past its end blinded a pursuer");
            c.Check(CountNamed(visualName) == visuals, "a cloud past its end made a smoke look");
            c.Note($"late cloud lived {frames} frame(s)");
            yield return new WaitForSeconds(0.5f);
            c.Check(ReferenceEquals(chaserAi.m_targetCreature, player), "the pursuer lost the player although nothing hid him");
            NoProblems(c);
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.smoke-ceiling (T27) ----------

    // Wanted behaviour (design: base under the impact). From the 1.0.16 code the base should land ON a thin floor piece
    // hit from below (probe start 1 m above the burst point, above the slab), but the first run in the real game
    // (1.0.17) put it on the ground. So the test also check that the Smoke Screen really flew up to the underside
    // (a pass must not come from a burst somewhere else) and note what the ground probe meets above the piece.
    private static IEnumerator RunSmokeCeiling()
    {
        var c = new Checks(SmokeCeilingName);
        var rig = Rig.Create(SmokeCeilingName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            // No still or fog bonus, Sneak 0: the crouched player's bar stays high enough for the Greydwarf of the last
            // step to see him 7 m away without smoke, also at night.
            var rules = new AmbushRules { ActivationDelay = 0f, StillBonus = 0, FogBonus = 0 };
            ServerRules.TestRules = rules;
            StealthCues.TestShowCues = true;
            rig.SetSneak(0f);
            yield return Stand(player);
            var dir = ClearDirection(player, 8f, out _);
            var feet = player.transform.position;
            GameObject slab = null;
            var pieceName = "";
            foreach (var name in new[] { "wood_floor", "wood_floor_1x1" })
            {
                if (ZNetScene.instance.GetPrefab(name) != null)
                {
                    pieceName = name;
                    slab = rig.Spawn(name, feet + Vector3.up * 2.6f + dir * 0.4f, Quaternion.identity);
                    break;
                }
            }
            if (!c.Check(slab != null, "no wood floor piece found (wood_floor, wood_floor_1x1): names unverified"))
            {
                c.Report();
                yield break;
            }
            // Floating piece: no support wear, or the game breaks it.
            var wear = slab.GetComponent<WearNTear>();
            if (wear != null)
            {
                wear.m_noSupportWear = false;
            }
            yield return Frames(3);
            var top = float.MinValue;
            var bottom = float.MaxValue;
            foreach (var col in slab.GetComponentsInChildren<Collider>())
            {
                if (!col.isTrigger)
                {
                    top = Mathf.Max(top, col.bounds.max.y);
                    bottom = Mathf.Min(bottom, col.bounds.min.y);
                }
            }
            if (!c.Check(top > bottom && bottom > feet.y + 1.9f, $"{pieceName}: no solid collider above the player's head (top {F(top)}, underside {F(bottom)}, feet {F(feet.y)})"))
            {
                c.Report();
                yield break;
            }
            // Thrown against the underside three ways: speed, distance and angle change where the game puts the
            // cloud around the hit point (it spawns it at the Smoke Screen's place of that physics step, moved a
            // quarter metre back along the hit normal), so one lucky throw proves little.
            var throws = new[]
            {
                new KeyValuePair<string, Vector3[]>("straight up from 0.9 m below at 12 m/s",
                    new[] { new Vector3(feet.x, bottom - 0.9f, feet.z) + dir * 0.7f, Vector3.up * 12f }),
                new KeyValuePair<string, Vector3[]>("straight up from 1.6 m below at 20 m/s",
                    new[] { new Vector3(feet.x, bottom - 1.6f, feet.z) + dir * 0.9f, Vector3.up * 20f }),
                new KeyValuePair<string, Vector3[]>("slanted from 1.2 m below at 18 m/s",
                    new[] { new Vector3(feet.x, bottom - 1.2f, feet.z) + dir * 0.3f, (Vector3.up * 0.94f + dir * 0.34f).normalized * 18f }),
            };
            var groundMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
            SmokeCloud cloud = null;
            for (var n = 0; n < throws.Length; n++)
            {
                var how = throws[n].Key;
                var from = throws[n].Value[0];
                if (cloud != null)
                {
                    rig.Remove(cloud.gameObject);
                    cloud = null;
                    yield return Frames(2);
                }
                var before = new HashSet<SmokeCloud>(SmokeRegistry.All);
                var thrown = rig.Launch(from, throws[n].Value[1]);
                // Highest point of the Smoke Screen: read in the frame of its hit (the game puts it at the hit point,
                // then removes it at the end of that frame).
                var reached = from.y;
                var flying = Time.time;
                while (Time.time - flying < 3f && cloud == null)
                {
                    if (thrown != null)
                    {
                        reached = Mathf.Max(reached, thrown.transform.position.y);
                    }
                    var fresh = NewClouds(before);
                    if (fresh.Count > 0)
                    {
                        cloud = fresh[0];
                        break;
                    }
                    yield return null;
                }
                if (!c.Check(cloud != null, $"{how}: no cloud from a Smoke Screen thrown against the underside of the floor piece"))
                {
                    continue;
                }
                yield return Frames(2);
                // What a ground probe like the cloud's own meets from above the piece at the burst spot.
                var probe = Physics.Raycast(new Vector3(cloud.Base.x, top + 0.6f, cloud.Base.z), Vector3.down, out var probeHit, 10f, groundMask, QueryTriggerInteraction.Ignore)
                    ? $"{probeHit.collider.name} (layer {LayerMask.LayerToName(probeHit.collider.gameObject.layer)}) {F(probeHit.point.y - feet.y)} m above the ground"
                    : "nothing";
                c.Note($"{how}: {pieceName} {F(top - bottom)} m thick, underside {F(bottom - feet.y)} m and top {F(top - feet.y)} m above the ground; "
                       + $"thrown from {F(from.y - feet.y)} m, highest point {F(reached - feet.y)} m; cloud base {F(cloud.Base.y - feet.y)} m above the "
                       + $"ground, {F(Utils.DistanceXZ(cloud.Base, from))} m beside the throw; a ray down from 0.6 m above the piece meets {probe}");
                c.Check(reached > bottom - 0.3f && Utils.DistanceXZ(cloud.Base, from) < 1.2f,
                    $"{how}: the Smoke Screen did not burst against the underside of the floor piece (highest point {F(bottom - reached)} m below it, "
                    + $"cloud {F(Utils.DistanceXZ(cloud.Base, from))} m beside the throw): nothing checked");
                c.Check(Mathf.Abs(cloud.Base.y - feet.y) < 0.6f,
                    $"{how}: the cloud sits {F(cloud.Base.y - feet.y)} m above the ground under the impact (on top of the floor piece, {F(top - feet.y)} m up) instead of on the ground");
            }
            if (cloud != null)
            {
                // The cloud of the last throw: crouched under the piece the player is inside it, the In smoke icon
                // shows, and a Greydwarf outside the cloud does not see him.
                var crouched = new Box();
                yield return Crouch(player, crouched);
                yield return Frames(3);
                c.Check(crouched.Ok && InSmoke(player, rules), "a crouched player under the floor piece is not inside the cloud that burst above his head");
                var icon = new Box();
                yield return WaitFor(() => StealthCues.Has(player, CueKind.Smoke), 2f, icon);
                c.Check(icon.Ok, "crouched under the floor piece in the cloud: no In smoke icon");
                var g = rig.Creature("Greydwarf", Ground(feet + dir * 7f), -dir, false);
                if (c.Check(g != null, "could not spawn a Greydwarf"))
                {
                    yield return Frames(3);
                    var ai = g.GetBaseAI();
                    WithoutSmoke(ai, player, out var wouldSee, out _);
                    c.Check(!cloud.Contains(g.m_eye.position) && wouldSee,
                        $"the Greydwarf 7 m away is inside the cloud, or would not see the crouched player without smoke either ({wouldSee}, bar "
                        + $"{F(player.m_stealthFactor)}): nothing proven");
                    c.Check(!ai.CanSeeTarget(player), "the Greydwarf outside the cloud sees the player crouched under the floor piece");
                }
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.toggle (T24, L01, L02, L04, M12) ----------

    private static IEnumerator RunToggle()
    {
        var c = new Checks(ToggleName);
        var rig = Rig.Create(ToggleName);
        if (rig == null)
        {
            yield break;
        }
        var knewRecipe = true;
        var addedRecipe = false;
        try
        {
            var player = rig.Player;
            // One long cloud for the whole test, hiding at once.
            var rules = new AmbushRules { ActivationDelay = 0f, CloudDuration = 40f };

            // Feature going off clear every override: put back after each switch. Ears held through the switches.
            void Overrides()
            {
                ServerRules.TestRules = rules;
                StealthCues.TestShowCues = true;
                SneakXp.TestShowMessage = true;
                Compat.TestOtherModPays = false;
            }

            Overrides();
            Tap.Install();
            Tap.Hold = true;
            rig.SaveAllSkills();
            rig.SetSneak(0f);
            rig.SetEnv("Misty");
            rig.SetTime(0f);
            yield return Stand(player);
            // Ground chosen by checks (start spot of the test world lie between standing stones): free lane ahead
            // (Greydwarf 8 m) and to the side (fresh Greydwarf 9 m, shot with an arrow at the end), both creature
            // spots bare terrain. Round 2: only "ahead" was checked, and the arrow at the fresh one never reached it.
            var place = new Spot();
            yield return FindOpenSpot(rig, 12f, false, (feet, d) =>
            {
                var s = Vector3.Cross(Vector3.up, d).normalized;
                return Lane(feet, s, 11f) && BareGround(Ground(feet + s * 9f), false) && BareGround(Ground(feet + d * 8f), false);
            }, place);
            if (!place.Found)
            {
                c.Note("no open ground with 12 m free ahead and 11 m free to the side found within 48 m: the scenery may be in the way");
            }
            var dir = place.Dir;
            var side = Vector3.Cross(Vector3.up, dir).normalized;
            var origin = player.transform.position;
            rig.TakeControls();
            rig.Aim(dir, 35f);
            var settled = new Box();
            yield return SettleEnv("Misty", settled);
            var item = rig.GiveAndEquip(4);
            var chestGo = rig.Spawn("piece_chest_wood", Ground(origin - dir * 2.5f), Quaternion.identity);
            var chest = chestGo != null ? chestGo.GetComponent<Container>() : null;
            var g = rig.Creature("Greydwarf", Ground(origin + dir * 8f), -dir, false);
            var benchGo = rig.Spawn(AmbushRules.DefaultRecipeStation, Ground(origin - dir * 3f - side * 3f), Quaternion.identity);
            var station = benchGo != null ? benchGo.GetComponent<CraftingStation>() : null;
            var found = FeatureRegistry.Find(ModInfo.Guid);
            var recipe = SmokeContent.CraftRecipe;
            if (!c.Check(item != null && chest != null && g != null && station != null && found != null && recipe != null,
                    "could not equip a Smoke Screen, spawn a chest, a workbench or a Greydwarf, or find this mod in the registry"))
            {
                c.Report();
                yield break;
            }
            var view = found.Value;
            yield return Frames(3);
            chest.GetInventory().AddItem(SmokeContent.ItemName, 1, 1, 0, 0L, "", false);
            var ai = g.GetBaseAI();
            var skill = player.GetSkills().GetSkill(Skills.SkillType.Sneak);
            var slot = new CloudSlot();
            yield return rig.PutCloud(slot, origin);
            knewRecipe = player.m_knownRecipes.Contains(SmokeContent.DisplayName);
            player.m_knownRecipes.Add(SmokeContent.DisplayName);
            addedRecipe = true;
            bool Offered() => OfferedAt(player, station, recipe);

            // On, standing in the cloud first (bar full): only the smoke hides him from the Greydwarf 8 m away.
            yield return Frames(2);
            WithoutSmoke(ai, player, out var wouldSeeBefore, out _);
            c.Check(InSmoke(player, rules) && wouldSeeBefore && !ai.CanSeeTarget(player),
                $"before: standing in the cloud ({InSmoke(player, rules)}) the Greydwarf 8 m away sees the player, or would not see him "
                + $"without smoke either ({wouldSeeBefore})");

            // On: fog, still, in a cloud, Greydwarf outside 8 m away looking at the player. Bar let sink to its low
            // value (first run: switched off while the bar was still on its way down from standing, so "it climbs
            // back" was met at once and proved nothing).
            var still = new Box();
            yield return HoldStill(rig, still);
            yield return new WaitForSeconds(0.7f);
            yield return Settle(player, settled, 8f);
            var low = player.m_stealthFactor;
            c.Check(still.Ok && StealthCues.Has(player, CueKind.Fog) && StealthCues.Has(player, CueKind.Smoke),
                $"before: icons Holding still {StealthCues.Has(player, CueKind.Still)}, Fog {StealthCues.Has(player, CueKind.Fog)}, In smoke {StealthCues.Has(player, CueKind.Smoke)}");
            c.Check(player.m_stealthFactorTarget < VanillaTarget(player) - 0.05f, $"before: bar target {F(player.m_stealthFactorTarget)} not below the normal game's {F(VanillaTarget(player))}");
            c.Check(settled.Ok && low < VanillaTarget(player) - 0.15f, $"before: the bar did not sink well below the normal game's value (bar {F(low)}, normal {F(VanillaTarget(player))})");
            c.Check(!ai.CanSeeTarget(player), "before: the Greydwarf sees the player in the cloud");
            c.Check(view.IsActive && recipe.m_enabled && Offered(), "before: the mod is not active or the workbench does not offer the recipe");

            // OFF, for real: framework refresh removes the patches and runs OnDeactivated.
            Plugin.TestBlocked = true;
            FeatureRegistry.RefreshAll();
            Overrides();
            c.Check(!view.IsActive && !Plugin.FeatureActive && view.Status == Plugin.TestBlockedText, $"off: state {view.State}, status '{view.Status}'");
            c.Check(!AnyCue(player), "off: a stealth icon stays");
            c.Check(!recipe.m_enabled && !Offered(), "off: the Smoke Screen recipe is still offered");
            c.Check(slot.Cloud != null && SmokeRegistry.Count > 0, "off: the cloud went away (its smoke should stay until its end)");
            c.Check(slot.Cloud != null && CloudVisual(slot.Cloud.Base) != null, "off: the smoke of the cloud no longer shows (it should until the cloud ends)");
            // The game's own stealth refresh (every 0.5 s) now sets the normal target; the bar climbs to it.
            yield return ForceRefresh(player);
            var climbed = new Box();
            yield return WaitFor(() => player.m_stealthFactor >= VanillaTarget(player) - 0.02f, 6f, climbed);
            c.Check(climbed.Ok && player.m_stealthFactor > low + 0.15f && Near(player.m_stealthFactorTarget, VanillaTarget(player), 0.01f),
                $"off: bar {F(low)} -> {F(player.m_stealthFactor)}, target {F(player.m_stealthFactorTarget)}, the normal game's value {F(VanillaTarget(player))}");
            c.Check(!AnyCue(player), "off: a stealth icon came back");
            c.Check(ai.CanSeeTarget(player), $"off: the Greydwarf 8 m away does not see the player in the smoke (bar {F(player.m_stealthFactor)}, view {F(ai.m_viewRange)} m)");

            // T24: throws refused, message at most every 3 s, nothing used.
            var stack = rig.SmokeCount();
            HumanoidPatches.TestResetMessageTimer();
            var told = Tap.Messages(HumanoidPatches.InactiveMessage, MessageHud.MessageType.TopLeft);
            c.Check(!player.StartAttack(null, false), "off: StartAttack with a Smoke Screen allowed");
            c.Check(Tap.Messages(HumanoidPatches.InactiveMessage, MessageHud.MessageType.TopLeft) == told + 1, "off: the refused throw showed no message");
            c.Check(Tap.Messages(RefusedThrowText, MessageHud.MessageType.TopLeft) == told + 1,
                $"off: the refused throw's message is not the text TESTING.md names: '{Tap.MessageTexts("Smoke Screen")}'");
            yield return new WaitForSeconds(1f);
            c.Check(!player.StartAttack(null, false) && Tap.Messages(HumanoidPatches.InactiveMessage) == told + 1, "off: a second try 1 s later was allowed or showed the message again");
            yield return new WaitForSeconds(2.2f);
            c.Check(!player.StartAttack(null, false) && Tap.Messages(HumanoidPatches.InactiveMessage) == told + 2, "off: a try 3.2 s after the first message was allowed or showed no new message");
            yield return new WaitForSeconds(0.5f);
            c.Check(rig.SmokeCount() == stack && !player.InAttack(), $"off: stack {stack} -> {rig.SmokeCount()}, attacking {player.InAttack()}");

            // L02 (the part that needs no restart): Smoke Screens stay real items, in the bag and in the chest.
            c.Check(ObjectDB.instance.GetItemPrefab(SmokeContent.ItemName) == SmokeContent.ItemPrefab
                    && ZNetScene.instance.GetPrefab(SmokeContent.ItemName) == SmokeContent.ItemPrefab,
                "off: the Smoke Screen is no longer registered");
            var bag = player.GetInventory();
            var chestBag = chest.GetInventory();
            var drops = SmokeDrops();
            var held = bag.GetAllItems().FirstOrDefault(SmokeContent.IsSmokeScreen);
            if (c.Check(held != null && player.DropItem(bag, held, 1), "off: could not drop a Smoke Screen"))
            {
                yield return Frames(3);
                var dropped = ItemDrop.s_instances.FirstOrDefault(d => d != null && d.m_itemData != null && SmokeContent.IsSmokeScreen(d.m_itemData));
                c.Check(dropped != null && SmokeDrops() == drops + 1 && rig.SmokeCount() == stack - 1, "off: the dropped Smoke Screen is not on the ground");
                if (dropped != null)
                {
                    c.Check(player.Pickup(dropped.gameObject, false, false), "off: could not pick the Smoke Screen up again");
                    yield return Frames(3);
                    c.Check(rig.SmokeCount() == stack && SmokeDrops() == drops, $"off: after the pick-up the bag holds {rig.SmokeCount()} (expected {stack})");
                }
            }
            var inChest = chestBag.GetAllItems().FirstOrDefault(SmokeContent.IsSmokeScreen);
            if (c.Check(inChest != null, "off: the chest lost its Smoke Screen"))
            {
                bag.MoveItemToThis(chestBag, inChest);
                c.Check(chestBag.CountItems(SmokeContent.DisplayName, -1, false) == 0 && rig.SmokeCount() == stack + 1, "off: could not take the Smoke Screen out of the chest");
                var one = bag.GetAllItems().FirstOrDefault(SmokeContent.IsSmokeScreen);
                c.Check(one != null && chestBag.MoveItemToThis(bag, one, 1, 0, 0)
                        && chestBag.CountItems(SmokeContent.DisplayName, -1, false) == 1 && rig.SmokeCount() == stack,
                    "off: could not put a Smoke Screen back into the chest");
            }

            // M12 (this game's side): the XP handler exists while off and pays nothing; a hit starts no cooldown here.
            rig.SetSneak(20f);
            player.m_nview.InvokeRPC(SneakXp.Rpc, 40f, false);
            c.Check(Near(skill.m_accumulator, 0f), "off: the sneak-attack XP handler paid");
            Hit(g, player, Slash(1f), 3f);
            c.Check(Near(skill.m_accumulator, 0f) && g.m_nview.GetZDO().GetLong(SneakXp.LastXpKey, 0L) == 0L, "off: a hit paid XP or started the creature's XP cooldown");
            c.Check(Tap.Messages("Sneak attack") == 0, "off: a Sneak attack message was shown");
            var offProblems = Tap.Problems();
            c.Check(offProblems.Length == 0, $"off: {ModInfo.Name} logged a warning or an error: {offProblems}");

            // ON again.
            Plugin.TestBlocked = false;
            FeatureRegistry.RefreshAll();
            Overrides();
            c.Check(view.IsActive && Plugin.FeatureActive, $"on: state {view.State}, status '{view.Status}'");
            c.Check(recipe.m_enabled && Offered(), "on: the Smoke Screen recipe is not back");
            if (slot.Cloud == null || !slot.Cloud.IsActive(SmokeRegistry.Now, rules))
            {
                yield return rig.PutCloud(slot, origin);
            }
            yield return HoldStill(rig, still, 5f);
            yield return new WaitForSeconds(0.7f);
            c.Check(still.Ok && StealthCues.Has(player, CueKind.Fog) && StealthCues.Has(player, CueKind.Smoke),
                $"on: icons Holding still {StealthCues.Has(player, CueKind.Still)}, Fog {StealthCues.Has(player, CueKind.Fog)}, In smoke {StealthCues.Has(player, CueKind.Smoke)}");
            c.Check(player.m_stealthFactorTarget < VanillaTarget(player) - 0.05f, $"on: bar target {F(player.m_stealthFactorTarget)} not below the normal game's {F(VanillaTarget(player))}");
            c.Check(InSmoke(player, rules) && !ai.CanSeeTarget(player), "on: the cloud does not hide the player again");
            // Standing up in the cloud (bar back to full, as in T20): now only the smoke hides him. First run: the
            // shot below was made crouched and still, and the Greydwarf 9 m away could not see that far anyway.
            yield return Stand(player);
            yield return Frames(2);
            WithoutSmoke(ai, player, out var wouldSeeOn, out _);
            c.Check(InSmoke(player, rules) && wouldSeeOn && !ai.CanSeeTarget(player),
                $"on: the cloud does not hide the standing player again (inside it {InSmoke(player, rules)}, seen without smoke {wouldSeeOn}, "
                + $"bar {F(player.m_stealthFactor)})");

            // L04 (turned on mid-game): shot from the smoke at a fresh unaware two-star Greydwarf. Its place 9 m away
            // chosen by checks, side first: free lane, bare terrain where it stand (not inside a stone: from there it
            // see out but no arrow come in) and nothing on the arrow's path as the game's projectile ray see it.
            var freshDir = side;
            var freshPlace = false;
            foreach (var angle in new[] { 90f, -90f, 60f, -60f, 120f, -120f, 150f, -150f })
            {
                var candidate = Quaternion.Euler(0f, angle, 0f) * dir;
                var at = Ground(origin + candidate * 9f);
                if (Lane(origin, candidate, 11f) && BareGround(at, false) && ShotBlocker(player, at + Vector3.up * 0.9f, null) == null)
                {
                    freshDir = candidate;
                    freshPlace = true;
                    break;
                }
            }
            if (!freshPlace)
            {
                c.Note("no place 9 m from the player with a free path for the arrow found: the shot may meet the scenery");
            }
            var fresh = rig.Creature("Greydwarf", Ground(origin + freshDir * 9f), -freshDir, false);
            if (c.Check(fresh != null, "could not spawn a second Greydwarf"))
            {
                fresh.SetLevel(3);
                // On the ground, collider where it stand, before the shot (AI off: it does nothing meanwhile).
                yield return new WaitForSeconds(0.6f);
                yield return new WaitForFixedUpdate();
                var freshAi = (MonsterAI)fresh.GetBaseAI();
                WithoutSmoke(freshAi, player, out var wouldSeeFresh, out _);
                c.Check(wouldSeeFresh, "on: the fresh Greydwarf 9 m away would not see the standing player without smoke either (scenery?): nothing proven");
                c.Check(!freshAi.CanSeeTarget(player), "on: a fresh Greydwarf outside sees the player in the smoke before the shot");
                var shot = new Box();
                yield return Shoot(rig, "Bow", "ArrowWood", fresh, 20f, 0f, shot);
                c.Check(shot.Ok && fresh != null && !fresh.IsDead(), $"on: the arrow did not hit, or killed it ({shot.Detail})");
                if (shot.Ok && fresh != null && !fresh.IsDead())
                {
                    c.Check(AiMemory.IsRevealed(fresh.transform, player, SmokeRegistry.Now) && freshAi.CanSeeTarget(player),
                        "on: the Greydwarf shot from the smoke does not see the player");
                    c.Check(freshAi.IsAlerted() && ReferenceEquals(freshAi.m_targetCreature, player), "on: the Greydwarf shot from the smoke does not fight the player");
                }
            }

            // Throws work again.
            player.SetCrouch(false);
            yield return WaitFor(() => !player.IsCrouching() && !player.InAttack(), 2f);
            c.Check(rig.GiveAndEquip(0) != null, "on: could not put a Smoke Screen back in hand");
            rig.Aim(dir, 35f);
            yield return new WaitForSeconds(0.4f);
            var before = new HashSet<SmokeCloud>(SmokeRegistry.All);
            stack = rig.SmokeCount();
            c.Check(player.StartAttack(null, false), "on: StartAttack with a Smoke Screen refused");
            var thrown = new CloudSlot();
            yield return WaitForCloud(before, 5f, thrown);
            c.Check(thrown.Cloud != null && rig.SmokeCount() == stack - 1, $"on: no cloud from the throw, or stack {stack} -> {rig.SmokeCount()}");
            NoProblems(c);
            c.Report();
        }
        finally
        {
            if (addedRecipe && !knewRecipe && rig.Player != null)
            {
                rig.Player.m_knownRecipes.Remove(SmokeContent.DisplayName);
            }
            rig.Restore();
        }
    }

    // Does that workbench offer the recipe to this player (who knows it)? Vanilla list of the crafting window, asked
    // as if the player stood at the station with its window open (station set for this one call).
    private static bool OfferedAt(Player player, CraftingStation station, Recipe recipe)
    {
        var keep = player.m_currentStation;
        player.m_currentStation = station;
        try
        {
            var list = new List<Recipe>();
            player.GetAvailableRecipes(ref list);
            return list.Contains(recipe);
        }
        finally
        {
            player.m_currentStation = keep;
        }
    }

    // ---------- sneak.death (T23) ----------

    private static IEnumerator RunDeath()
    {
        var c = new Checks(DeathName);
        var old = Player.m_localPlayer;
        var env = EnvMan.instance;
        if (old == null || env == null || Game.instance == null || ZNet.instance == null || ZNetScene.instance == null)
        {
            SelfTest.Fail(DeathName, "no local player or no world");
            yield break;
        }
        // Everything a death takes, kept to give back to the new player object.
        var envName = env.m_debugEnv;
        var todOn = env.m_debugTimeOfDay;
        var tod = env.m_debugTime;
        var position = old.transform.position;
        var skills = old.GetSkills().m_skillData.ToDictionary(p => p.Key, p => new KeyValuePair<float, float>(p.Value.m_level, p.Value.m_accumulator));
        var foods = old.m_foods.ToList();
        var health = old.GetHealth();
        var sinceDeath = old.m_timeSinceDeath;
        var effects = old.GetSEMan().GetStatusEffects().Select(se => se.name).ToList();
        var profile = Game.instance.GetPlayerProfile();
        var world = profile.GetWorldData(ZNet.instance.GetWorldUID());
        var hadDeathPoint = world.m_haveDeathPoint;
        var deathPoint = world.m_deathPoint;
        var bag = new ZPackage();
        old.GetInventory().Save(bag);
        var emptied = false;
        var given = false;
        var sawTutorial = true;

        void GiveBack(Player player)
        {
            given = true;
            if (!sawTutorial)
            {
                player.m_shownTutorials.Remove("death");
            }
            bag.SetPos(0);
            player.GetInventory().Load(bag);
            player.EquipInventoryItems();
            var data = player.GetSkills().m_skillData;
            foreach (var type in data.Keys.ToList())
            {
                if (!skills.ContainsKey(type))
                {
                    data.Remove(type);
                }
            }
            foreach (var saved in skills)
            {
                var skill = player.GetSkills().GetSkill(saved.Key);
                skill.m_level = saved.Value.Key;
                skill.m_accumulator = saved.Value.Value;
            }
            if (!ReferenceEquals(player, old))
            {
                player.m_foods.Clear();
                player.m_foods.AddRange(foods);
                player.m_timeSinceDeath = sinceDeath;
                if (sinceDeath > player.m_hardDeathCooldown)
                {
                    player.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectSoftDeath, true);
                }
                MovePlayer(player, position + Vector3.up * 0.1f);
                player.m_maxAirAltitude = player.transform.position.y;
                if (Minimap.instance != null)
                {
                    Minimap.instance.RemovePin(position, 3f);
                }
                world.m_haveDeathPoint = hadDeathPoint;
                world.m_deathPoint = deathPoint;
            }
            player.SetHealth(Mathf.Min(health, player.GetMaxHealth()));
        }

        try
        {
            ServerRules.TestRules = new AmbushRules();
            StealthCues.TestShowCues = true;
            Tap.Install();
            env.m_debugEnv = "Misty";
            env.ForceInstantEnvironmentSwitch();
            env.m_debugTimeOfDay = true;
            env.m_debugTime = 0f;
            var settled = new Box();
            yield return SettleEnv("Misty", settled);
            c.Check(settled.Ok, $"weather Misty did not settle ({settled.Detail})");
            old.SetControls(Vector3.zero, false, false, false, false, false, false, false, false, false, false);
            old.SetCrouch(true);
            var icons = new Box();
            yield return WaitFor(() => StealthCues.Has(old, CueKind.Still) && StealthCues.Has(old, CueKind.Fog), 6f, icons);
            c.Check(icons.Ok, $"before the death: Holding still {StealthCues.Has(old, CueKind.Still)}, Fog {StealthCues.Has(old, CueKind.Fog)}");

            // Die like console "die" with god mode off. Bag emptied first (given back after): no tombstone is made.
            // Death tutorial marked as seen for the death (no raven comes), unmarked after.
            sawTutorial = old.m_shownTutorials.Contains("death");
            old.m_shownTutorials.Add("death");
            old.UnequipAllItems();
            old.GetInventory().RemoveAll();
            emptied = true;
            old.SetGodMode(false);
            var hit = new HitData { m_hitType = HitData.HitType.Self };
            hit.m_damage.m_damage = 99999f;
            old.Damage(hit);
            var died = Time.time;
            var respawn = new Box();
            yield return WaitFor(() =>
            {
                var p = Player.m_localPlayer;
                return p != null && !ReferenceEquals(p, old) && !p.IsDead();
            }, 70f, respawn);
            if (!c.Check(respawn.Ok, "no new player 70 s after the death"))
            {
                c.Report();
                yield break;
            }
            var player = Player.m_localPlayer;
            c.Note($"respawned {F(Time.time - died)} s after the death");
            yield return WaitFor(() => !player.IsTeleporting() && ZNetScene.instance.IsAreaReady(player.transform.position), 20f);
            yield return new WaitForSeconds(1f);
            GiveBack(player);
            player.SetGodMode(true);
            yield return new WaitForSeconds(1f);

            // Crouch again: icons back on the new player. (Log listened to since before the death: no clearing.)
            player.SetControls(Vector3.zero, false, false, false, false, false, false, false, false, false, false);
            player.SetCrouch(true);
            yield return WaitFor(() => StealthCues.Has(player, CueKind.Still) && StealthCues.Has(player, CueKind.Fog), 8f, icons);
            c.Check(ReferenceEquals(StealthState.For, player), "the stealth state still belongs to the dead player object");
            c.Check(icons.Ok, $"after the respawn: Holding still {StealthCues.Has(player, CueKind.Still)}, Fog {StealthCues.Has(player, CueKind.Fog)}");
            c.Note($"after the respawn the icons read '{StealthCues.IconText(CueKind.Still)}' and '{StealthCues.IconText(CueKind.Fog)}'");
            var lost = effects.Where(n => !player.GetSEMan().GetStatusEffects().Any(se => se.name == n) && !n.StartsWith(ModInfo.Guid, System.StringComparison.Ordinal)).ToArray();
            if (lost.Length > 0)
            {
                c.Note("status effects the death took (not given back): " + string.Join(", ", lost));
            }
            NoProblems(c);
            c.Report();
        }
        finally
        {
            ClearOverrides();
            Tap.Remove();
            env.m_debugEnv = envName;
            env.m_debugTimeOfDay = todOn;
            env.m_debugTime = tod;
            env.ForceInstantEnvironmentSwitch();
            var now = Player.m_localPlayer;
            if (now != null)
            {
                if (emptied && !given)
                {
                    GiveBack(now);
                }
                if (!sawTutorial)
                {
                    now.m_shownTutorials.Remove("death");
                }
                now.SetGodMode(true);
                now.SetCrouch(false);
                StealthCues.RemoveAll(now);
                now.m_stealthFactorUpdateTimer = 0.51f;
            }
        }
    }
}
#endif
