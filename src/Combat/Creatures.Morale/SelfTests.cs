using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;
#endif

namespace MC.Combat.CreaturesMoraleMod;

// Debug build only (calls vanish in Release). In-world self tests, run by world probe (tools/Test-InWorld.ps1),
// design 7.6. Player in god mode at the spawn stones of throwaway world MCProbe:
//   morale.rules    pure parts: Decide table, Outclasses + worked table (two bosses ahead, elites), home and spawn
//                   biome ranks, biome lookups at real world points, kill steps, Standing.Compute + listing filter +
//                   cap, standing bytes, provokers array, cornered count and fear release, rules wire (round trip,
//                   clamp, layout 2), packs, default names in ZNetScene, boss ladder, join verdicts, plate alert icons;
//                   NOTE lines settle the design's unverified values (names, leaders' death animation, spawn tables,
//                   raid, bombs, targets, senses and flee numbers, biome points)
//   morale.publish  own standing on the player ZDO = Compute(profile); withdraw = 255; publish again; test override;
//                   boss rank message waits past vanilla's boss death message (E5)
//   morale.calm     Greydwarf: rank 0 hunts, rank 4 afraid (drops the player and runs: fear frames, not sensed,
//                   sense vs see), running = vanilla alerted state (ZDO alert, alert icon on its plate, alert raised
//                   once), beyond FearRange it calms (no alert, no icon), a silent player behind it is not noticed until
//                   it turns, 2 stars hunt again,
//                   burn = alert without target then calm down, night hunter stands down and is afraid when close,
//                   NightHuntersCanBeAfraid off = hunts (HuntPlayer true at once); spawn biome: a Skeleton that spawned
//                   in the Plains or Mountains, a Greydwarf in the Deep North or the Meadows
//   morale.provoke  (fear off) hit, fire-only hit, near miss 2 m / 6 m (DoProjectileHitNoise), sneak attack seen /
//                   unseen, real knife stab (EquipItem + StartAttack); (fear on) no sneak attack on a running creature,
//                   cornered: it cannot get away from a player 1.5 m away / a player 5 m away never corners / a silent
//                   player right behind it is never run from; afraid again after the provocation
//   morale.stealth  sleeping Draugr 15 m: InStealthRange true at rank 0, false at rank 5, still asleep; then as a
//                   follower asleep at a rout: not routed, also not once woken while the rout lasts
//   morale.rout     Troll + pack (scene where all stand free and followers have a navmesh path away, else vanilla
//                   Flee stand still): player kill routs followers only (not Boar, not tame), RoutFrom, anger wiped, they
//                   run alerted (ZDO alert, raised once; base update run, vanilla skipped), late one picks it up, rout
//                   over = unalerted, shaken (afraid of a rank 0 player: they run from him once the fear is on) then
//                   hostile again; kill with no attacker = no rout; broadcast path through the Rout message
// More tests in SelfTestsWorld.cs, SelfTestsWorld2.cs (single player) and SelfTestsMp.cs (client joined to a dedicated
// server + server halves); shared tools in SelfTestsTools.cs. Every test also fail on a warning or error line of the
// mod in the log while it run (Checks + LogWatch), except lines a step ask for.
// Me never write config: rules through ServerRules.TestRules, rank through Standing.TestOverride, messages through
// Standing.TestMessages, broadcast through Rout.ForceBroadcast; all put back in finally (recent routs forgotten too).
// Me pause the world spawner, clear
// other creatures near, destroy all me spawn, take back the knife, put the player back where and as healthy as he was.
internal static partial class SelfTests
{
    private const string RulesName = "morale.rules";
    private const string PublishName = "morale.publish";
    private const string CalmName = "morale.calm";
    private const string ProvokeName = "morale.provoke";
    private const string StealthName = "morale.stealth";
    private const string RoutName = "morale.rout";

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        SelfTest.Register(RulesName, RunRules);
        SelfTest.Register(PublishName, RunPublish);
        SelfTest.Register(CalmName, RunCalm);
        SelfTest.Register(ProvokeName, RunProvoke);
        SelfTest.Register(StealthName, RunStealth);
        SelfTest.Register(RoutName, RunRout);
        RegisterMore();
        RegisterMultiplayer();
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        SelfTest.Unregister(RulesName);
        SelfTest.Unregister(PublishName);
        SelfTest.Unregister(CalmName);
        SelfTest.Unregister(ProvokeName);
        SelfTest.Unregister(StealthName);
        SelfTest.Unregister(RoutName);
        UnregisterMore();
        UnregisterMultiplayer();
        ResetHooks();
#endif
    }

#if DEBUG
    // ================================================================ helpers

    private sealed class Checks
    {
        private readonly string _name;
        private readonly List<string> _failures = new List<string>();
        private readonly List<string> _expected = new List<string>();
        private int _count;

        // Me also watch the mod's own log while the test run (T24, L04): warning or error line = failed check.
        internal Checks(string name)
        {
            _name = name;
            LogWatch.Instance.Begin();
        }

        // Warning or error line a step ask for on purpose (it holds this text): not a problem.
        internal void Expect(string part) => _expected.Add(part);

        internal bool Check(bool ok, string what)
        {
            _count++;
            if (!ok)
            {
                _failures.Add(what);
            }
            return ok;
        }

        internal void Report(string extra = "")
        {
            var problems = LogWatch.Instance.Problems(_expected);
            LogWatch.Instance.End();
            Check(problems.Count == 0, $"clean log: {problems.Count} warning or error line(s) from {ModInfo.Name} during the test"
                                       + (problems.Count > 0 ? $", first: {problems[0]}" : ""));
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

    // Until tell here: met or not, and how long it wait.
    private sealed class Waiter
    {
        internal bool Met;
        internal float Took;
    }

    // Lambda put found plate here (out into field).
    private sealed class PlateRef
    {
        internal EnemyHud.HudData Data;
    }

    // Me = one test's piece of world: spawner paused, other creatures near gone, all me spawn destroyed at End, player
    // put back (place, health, hands).
    private sealed class Stage
    {
        private static int _viewMask;
        private static int _solidMask;

        internal readonly string Test;
        internal readonly Player Player;
        internal readonly Vector3 Forward;
        internal readonly Vector3 Right;
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<ItemDrop.ItemData> _given = new List<ItemDrop.ItemData>();
        private readonly Vector3 _startPos;
        private readonly float _startHealth;
        private readonly bool _noSpawn;
        private bool _equipTouched;
        private ItemDrop.ItemData _oldRight;
        private ItemDrop.ItemData _oldLeft;
        private bool _ended;

        private Stage(string test, Player player)
        {
            Test = test;
            Player = player;
            _startPos = player.transform.position;
            _startHealth = player.GetHealth();
            _noSpawn = SpawnSystem.m_nospawn;
            SpawnSystem.m_nospawn = true;
            var cam = Utils.GetMainCamera();
            var forward = cam != null ? cam.transform.forward : player.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f)
            {
                forward = Vector3.forward;
            }
            Forward = forward.normalized;
            Right = Vector3.Cross(Vector3.up, Forward).normalized;
        }

        internal static Stage Begin(string test, Checks c)
        {
            var player = Player.m_localPlayer;
            if (player == null || ZNet.instance == null || ZNetScene.instance == null || ZoneSystem.instance == null
                || player.m_nview == null || !player.m_nview.IsValid())
            {
                c.Check(false, "no world or no local player");
                return null;
            }
            var s = new Stage(test, player);
            c.Check(!Attitudes.PassiveMobs(), "the test world has the Passive Mobs modifier (creatures never pick players there)");
            c.Check(!ServerRules.Pending, "rules pending in single player");
            var removed = s.ClearEnemies(player.transform.position, 50f, null);
            if (removed > 0)
            {
                SelfTest.Note(test, $"removed {removed} other creature(s) within 50 m so they cannot change a result");
            }
            var pos = player.transform.position;
            if (EffectArea.IsPointInsideNoMonsterArea(pos) != null || EffectArea.IsPointCloseToNoMonsterArea(pos) != null)
            {
                SelfTest.Note(test, "the player stands in or near a no-monster area; creature spots avoid it");
            }
            return s;
        }

        private static int ViewMask()
        {
            if (_viewMask == 0)
            {
                // Same layers BaseAI sight raycast use.
                _viewMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "viewblock", "vehicle");
            }
            return _viewMask;
        }

        // Ground spot `distance` away from `from` toward `dir`. Spot under water, in no-monster area or (needSight) not
        // seen from `from` = me turn 30 degrees and try again.
        internal Vector3 Spot(Vector3 from, Vector3 dir, float distance, bool needSight)
        {
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Forward;
            for (var i = 0; i < 12; i++)
            {
                var angle = (i % 2 == 0 ? 1f : -1f) * ((i + 1) / 2) * 30f;
                var p = Ground(from + Quaternion.Euler(0f, angle, 0f) * dir * distance);
                if (!Usable(p))
                {
                    continue;
                }
                if (needSight && Physics.Linecast(p + Vector3.up * 1.4f, from + Vector3.up * 1.4f, ViewMask()))
                {
                    continue;
                }
                return p;
            }
            SelfTest.Note(Test, $"no clear spot {F(distance)} m away; using the one straight ahead");
            return Ground(from + dir * distance);
        }

        private static bool Usable(Vector3 p)
        {
            var zones = ZoneSystem.instance;
            if (zones != null && p.y < zones.m_waterLevel + 0.3f)
            {
                return false;
            }
            return EffectArea.IsPointInsideNoMonsterArea(p) == null && EffectArea.IsPointCloseToNoMonsterArea(p) == null;
        }

        private static int SolidMask()
        {
            if (_solidMask == 0)
            {
                // Rocks, trees, pieces: no terrain (a slope under the capsule is fine).
                _solidMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "vehicle");
            }
            return _solidMask;
        }

        // Ground spot where a creature stand free: usable (dry, no ward) and no rock, tree or piece in a creature-size
        // capsule there (Ground read the terrain only: a spot can be inside a boulder).
        internal static bool Open(Vector3 p)
        {
            return Usable(p) && !Physics.CheckCapsule(p + Vector3.up * 0.6f, p + Vector3.up * 1.6f, 0.5f, SolidMask(),
                QueryTriggerInteraction.Ignore);
        }

        internal MonsterAI Spawn(string prefabName, Vector3 pos, Vector3 facing)
        {
            var prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null)
            {
                SelfTest.Note(Test, $"prefab {prefabName} not found");
                return null;
            }
            var go = Object.Instantiate(prefab, pos + Vector3.up * 0.1f, Look(facing));
            _spawned.Add(go);
            var ai = go.GetComponent<MonsterAI>();
            if (ai == null)
            {
                SelfTest.Note(Test, $"{prefabName} has no MonsterAI");
            }
            return ai;
        }

        // Any prefab (piece, animal, creature without MonsterAI), destroyed at End like creatures.
        internal GameObject SpawnAny(string prefabName, Vector3 pos, Vector3 facing)
        {
            var prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null)
            {
                SelfTest.Note(Test, $"prefab {prefabName} not found");
                return null;
            }
            var go = Object.Instantiate(prefab, pos + Vector3.up * 0.1f, Look(facing));
            _spawned.Add(go);
            return go;
        }

        // Object made by something else (projectile, dropped item): destroyed at End too.
        internal void Track(GameObject go)
        {
            if (go != null && !_spawned.Contains(go))
            {
                _spawned.Add(go);
            }
        }

        // Item on the ground (like a drop: a new network object this game own), destroyed at End like creatures.
        internal ItemDrop SpawnItem(GameObject prefab, Vector3 pos)
        {
            var go = Object.Instantiate(prefab, pos, Quaternion.identity);
            _spawned.Add(go);
            return go.GetComponent<ItemDrop>();
        }

        internal void Destroy(Component c)
        {
            if (c == null)
            {
                return;
            }
            var go = c.gameObject;
            _spawned.Remove(go);
            if (ZNetScene.instance != null)
            {
                ZNetScene.instance.Destroy(go);
            }
            else
            {
                Object.Destroy(go);
            }
        }

        // Enemy creatures of player (world spawns, leftovers) within radius, but not keep. This game own them in single
        // player.
        internal int ClearEnemies(Vector3 center, float radius, MonsterAI keep)
        {
            var doomed = new List<GameObject>();
            foreach (var ai in BaseAI.BaseAIInstances)
            {
                if (ai == null || ai == keep)
                {
                    continue;
                }
                var c = ai.m_character;
                if (c == null || c.IsPlayer() || c.IsTamed() || !BaseAI.IsEnemy(Player, c)
                    || (ai.transform.position - center).sqrMagnitude > radius * radius)
                {
                    continue;
                }
                doomed.Add(ai.gameObject);
            }
            foreach (var go in doomed)
            {
                _spawned.Remove(go);
                ZNetScene.instance.Destroy(go);
            }
            return doomed.Count;
        }

        // Noisy player: every creature within 40 m hear him (vanilla hearing), whatever way it look.
        internal void Noise()
        {
            if (Player != null)
            {
                Player.AddNoise(40f);
            }
        }

        // Silent player (a player standing still or sneaking): no creature hear him, only sight count.
        internal void Silence()
        {
            if (Player != null)
            {
                Player.m_noiseRange = 0f;
            }
        }

        internal void MovePlayer(Vector3 pos) => Place(Player, Ground(pos), Vector3.zero);

        // New weapon in the inventory, equipped (Humanoid.EquipItem). End take it back and equip the old hands again.
        internal ItemDrop.ItemData EquipWeapon(string prefab)
        {
            if (!_equipTouched)
            {
                _equipTouched = true;
                _oldRight = Player.m_rightItem;
                _oldLeft = Player.m_leftItem;
            }
            var item = Player.GetInventory().AddItem(prefab, 1, 1, 0, 0L, "", false);
            if (item == null)
            {
                return null;
            }
            _given.Add(item);
            Player.EquipItem(item, false);
            return item;
        }

        private void GiveBack()
        {
            if (Player == null)
            {
                return;
            }
            var inventory = Player.GetInventory();
            foreach (var item in _given)
            {
                if (Player.IsItemEquiped(item))
                {
                    Player.UnequipItem(item, false);
                }
                inventory.RemoveItem(item);
            }
            _given.Clear();
            if (!_equipTouched)
            {
                return;
            }
            foreach (var old in new[] { _oldRight, _oldLeft })
            {
                if (old != null && inventory.ContainsItem(old) && !Player.IsItemEquiped(old))
                {
                    Player.EquipItem(old, false);
                }
            }
        }

        internal void End()
        {
            if (_ended)
            {
                return;
            }
            _ended = true;
            GiveBack();
            var scene = ZNetScene.instance;
            foreach (var go in _spawned)
            {
                if (go == null)
                {
                    continue;
                }
                if (scene != null)
                {
                    scene.Destroy(go);
                }
                else
                {
                    Object.Destroy(go);
                }
            }
            _spawned.Clear();
            SpawnSystem.m_nospawn = _noSpawn;
            if (Player != null && !Player.IsDead())
            {
                Place(Player, _startPos, Vector3.zero);
                if (Player.GetHealth() < _startHealth)
                {
                    Player.SetHealth(_startHealth);
                }
            }
        }

        internal static Quaternion Look(Vector3 facing)
        {
            facing.y = 0f;
            return facing.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(facing.normalized) : Quaternion.identity;
        }

        // Me set transform and body (else physics put old place back). Zero facing = rotation stay.
        internal static void Place(Character c, Vector3 pos, Vector3 facing)
        {
            if (c == null)
            {
                return;
            }
            var t = c.transform;
            t.position = pos;
            facing.y = 0f;
            if (facing.sqrMagnitude > 0.0001f)
            {
                t.rotation = Quaternion.LookRotation(facing.normalized);
            }
            var body = c.m_body;
            if (body != null)
            {
                body.position = pos;
                body.rotation = t.rotation;
                body.linearVelocity = Vector3.zero;
            }
        }

        internal static Vector3 Ground(Vector3 p)
        {
            var zones = ZoneSystem.instance;
            if (zones != null && zones.GetGroundHeight(p, out var height))
            {
                p.y = height;
            }
            return p;
        }
    }

    // Me put every test hook back (each setter publish standing again; nothing happen while mod off).
    private static void ResetHooks()
    {
        Rout.ForceBroadcast = false;
        Rout.Clear();
        Standing.TestMessages = null;
        if (ServerRules.TestRules != null)
        {
            ServerRules.TestRules = null;
        }
        if (Standing.TestOverride != null)
        {
            Standing.TestOverride = null;
        }
    }

    private static void Finish(Stage s)
    {
        if (s != null)
        {
            s.End();
        }
        ResetHooks();
    }

    private static IEnumerator Until(Func<bool> condition, float timeout, Action eachFrame, Waiter w)
    {
        var start = Time.time;
        w.Met = false;
        w.Took = 0f;
        while (true)
        {
            eachFrame?.Invoke();
            if (condition())
            {
                w.Met = true;
                w.Took = Time.time - start;
                yield break;
            }
            if (Time.time - start >= timeout)
            {
                w.Took = Time.time - start;
                yield break;
            }
            yield return null;
        }
    }

    private static IEnumerator Wait(float seconds, Action eachFrame)
    {
        var end = Time.time + seconds;
        while (Time.time < end)
        {
            eachFrame?.Invoke();
            yield return null;
        }
    }

    private static IEnumerator WaitUntilTime(float time, Action eachFrame)
    {
        while (Time.time < time)
        {
            eachFrame?.Invoke();
            yield return null;
        }
    }

    private static MoraleRules NewRules(Action<MoraleRules> tweak)
    {
        var r = new MoraleRules();
        if (tweak != null)
        {
            tweak(r);
        }
        r.Build();
        return r;
    }

    private static void Hit(Character victim, Player attacker, float slash, float fire, float backstab)
    {
        var hit = new HitData();
        hit.m_damage.m_slash = slash;
        hit.m_damage.m_fire = fire;
        hit.m_point = victim.GetCenterPoint();
        var dir = victim.transform.position - attacker.transform.position;
        hit.m_dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
        hit.m_backstabBonus = backstab;
        hit.SetAttacker(attacker);
        victim.Damage(hit);
    }

    private static void Kill(Character victim, Player attacker)
    {
        var hit = new HitData();
        hit.m_damage.m_damage = 1e7f;
        hit.m_point = victim.GetCenterPoint();
        hit.SetAttacker(attacker);
        victim.Damage(hit);
    }

    // No loot, no ragdoll (ragdoll drop items later): kill leave nothing behind. Only this instance.
    private static void NoDrops(MonsterAI ai)
    {
        if (ai == null)
        {
            return;
        }
        var drop = ai.GetComponent<CharacterDrop>();
        if (drop != null)
        {
            drop.SetDropsEnabled(false);
        }
        ai.m_character.m_deathEffects = new EffectList();
    }

    private static bool Provoked(MonsterAI ai, Player player)
    {
        if (ai == null || player == null || ai.m_nview == null || !ai.m_nview.IsValid())
        {
            return false;
        }
        return CreatureKeys.IsProvokedBy(ai.m_nview.GetZDO(), player.GetPlayerID(), Attitudes.Now());
    }

    // Player the creature runs from right now (owner memory, design 2.5 piece 2), or null.
    private static Player FearOf(MonsterAI ai)
    {
        if (ai == null || !CreatureState.TryGet(ai, out var state))
        {
            return null;
        }
        return state.FearPlayer;
    }

    // Alert as other games see it: owner's SetAlerted write ZDO "alert", non-owners read it every AI tick
    // (BaseAI.UpdateAI), their plate show the icon from it.
    private static bool ZdoAlert(MonsterAI ai) =>
        ai != null && ai.m_nview != null && ai.m_nview.IsValid() && ai.m_nview.GetZDO().GetBool(ZDOVars.s_alert);

    // Vanilla plate of that creature, shown (EnemyHud.UpdateHuds): Alerted icon on = alerted, Aware icon off (Aware =
    // target and not alerted; a running or calm afraid creature has no target).
    private static bool IconsShow(EnemyHud.HudData data, bool alerted)
    {
        return data != null && data.m_gui != null && data.m_gui.activeSelf && data.m_alerted != null && data.m_aware != null
               && data.m_alerted.gameObject.activeSelf == alerted && !data.m_aware.gameObject.activeSelf;
    }

    // Both icons off (vanilla unalerted look), whether the plate is on screen or not.
    private static bool IconsOff(EnemyHud.HudData data)
    {
        return data != null && data.m_gui != null && data.m_alerted != null && data.m_aware != null
               && !data.m_alerted.gameObject.activeSelf && !data.m_aware.gameObject.activeSelf;
    }

    private static string DescribeIcons(EnemyHud.HudData data)
    {
        if (data == null || data.m_gui == null)
        {
            return "no plate";
        }
        return $"plate shown {data.m_gui.activeSelf}, alert icon {(data.m_alerted != null ? data.m_alerted.gameObject.activeSelf.ToString() : "missing")}, "
               + $"aware icon {(data.m_aware != null ? data.m_aware.gameObject.activeSelf.ToString() : "missing")}";
    }

    private static float Dist(Component a, Component b) =>
        a != null && b != null ? Vector3.Distance(a.transform.position, b.transform.position) : -1f;

    // A world point of that biome (WorldGenerator, x and z only), searched in rings around the world centre. Zero =
    // none found (the note says so).
    private static Vector3 BiomePoint(Heightmap.Biome biome)
    {
        var generator = WorldGenerator.instance;
        if (generator == null)
        {
            return Vector3.zero;
        }
        for (var r = 500f; r <= 10000f; r += 250f)
        {
            for (var a = 0; a < 360; a += 10)
            {
                var p = new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * r, 0f, Mathf.Cos(a * Mathf.Deg2Rad) * r);
                if (generator.GetBiome(p) == biome)
                {
                    return p;
                }
            }
        }
        return Vector3.zero;
    }

    private static long RoutUntilOf(MonsterAI ai) =>
        ai != null && ai.m_nview != null && ai.m_nview.IsValid() ? CreatureKeys.GetRoutUntil(ai.m_nview.GetZDO()) : 0L;

    private static Vector3 RoutFromOf(MonsterAI ai) =>
        ai != null && ai.m_nview != null && ai.m_nview.IsValid()
            ? CreatureKeys.GetRoutFrom(ai.m_nview.GetZDO(), Vector3.one * 99999f)
            : Vector3.one * 99999f;

    private static string Who(Character c)
    {
        if (c == null)
        {
            return "none";
        }
        return c.IsPlayer() ? "the player" : c.m_name;
    }

    private static string TokenOf(string prefab)
    {
        var scene = ZNetScene.instance;
        var go = scene != null ? scene.GetPrefab(prefab) : null;
        var character = go != null ? go.GetComponent<Character>() : null;
        return character != null ? character.m_name : null;
    }

    private static int Hash(string name) => name.GetStableHashCode();

    private static string F(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static Dictionary<string, float> Kills(params object[] pairs)
    {
        var d = new Dictionary<string, float>();
        for (var i = 0; i + 1 < pairs.Length; i += 2)
        {
            d[(string)pairs[i]] = Convert.ToSingle(pairs[i + 1], CultureInfo.InvariantCulture);
        }
        return d;
    }

    private static bool TryEntry(Standing.Computed computed, string token, out Standing.Entry entry)
    {
        foreach (var e in computed.Bonuses)
        {
            if (e.Token == token)
            {
                entry = e;
                return true;
            }
        }
        entry = default;
        return false;
    }

    // ================================================================ morale.rules

    private static IEnumerator RunRules()
    {
        var c = new Checks(RulesName);
        var def = NewRules(null);
        CheckDecide(c);
        CheckOutclasses(c, def);
        CheckBiomes(c, def);
        CheckKillSteps(c, def);
        CheckStanding(c, def);
        CheckStandingBytes(c);
        CheckProvokers(c);
        CheckCornered(c);
        CheckWire(c);
        CheckPacks(c, def);
        CheckNames(c, def);
        CheckLadder(c);
        CheckJoin(c);
        CheckPlateParts(c);
        NoteUnverified();
        c.Report($"; default rules: {def.Describe()}");
        yield break;
    }

    private static AttitudeFacts BaseFacts()
    {
        return new AttitudeFacts
        {
            Active = true,
            Pending = false,
            Exempt = false,
            Now = 1000000L * TimeSpan.TicksPerSecond,
            RoutUntil = 0L,
            ShakenTicks = 60L * TimeSpan.TicksPerSecond,
            HasStanding = true,
            Provoked = false,
            PassiveMobs = false,
            HasRank = true,
            Rank = 4,
            Level = 1,
            BossRank = 4,
            KillBonus = 0,
            StarRank = 1,
            MaxBossesSkipped = 1,
        };
    }

    // Every row of design 2.4, and which row win over which.
    private static void CheckDecide(Checks c)
    {
        var sec = TimeSpan.TicksPerSecond;

        void Expect(string what, AttitudeFacts f, Attitude want)
        {
            var got = Attitudes.Decide(in f);
            c.Check(got == want, $"Decide, {what}: got {got}, expected {want}");
        }

        var b = BaseFacts();
        Expect("rank 4 player, rank 4 creature", b, Attitude.Afraid);
        var f = b;
        f.BossRank = 3;
        Expect("rank 3 player, rank 4 creature", f, Attitude.Hostile);
        f = b;
        f.Active = false;
        Expect("mod off here", f, Attitude.Hostile);
        f = b;
        f.Pending = true;
        Expect("rules pending", f, Attitude.Hostile);
        f = b;
        f.Exempt = true;
        f.RoutUntil = b.Now + 10 * sec;
        Expect("exempt, even while routed", f, Attitude.Hostile);
        f = b;
        f.RoutUntil = b.Now + 10 * sec;
        f.HasStanding = false;
        Expect("routed, player without standing", f, Attitude.Routed);
        f = b;
        f.RoutUntil = b.Now + 10 * sec;
        f.Provoked = true;
        Expect("routed beats provoked", f, Attitude.Routed);
        f = b;
        f.HasStanding = false;
        Expect("player without standing", f, Attitude.Hostile);
        f = b;
        f.Provoked = true;
        Expect("provoked by an outclassing player", f, Attitude.Provoked);
        f = b;
        f.BossRank = 0;
        f.RoutUntil = b.Now - 10 * sec;
        Expect("shaken, rank 0 player", f, Attitude.Afraid);
        f.HasStanding = false;
        Expect("shaken, player without standing", f, Attitude.Hostile);
        f = b;
        f.RoutUntil = b.Now - 10 * sec;
        f.Provoked = true;
        Expect("shaken, provoked", f, Attitude.Provoked);
        f = b;
        f.BossRank = 0;
        f.RoutUntil = b.Now - 61 * sec;
        Expect("shaken over (61 s after a 60 s shaken period)", f, Attitude.Hostile);
        f = b;
        f.BossRank = 0;
        f.RoutUntil = b.Now;
        Expect("rout ends right now: shaken", f, Attitude.Afraid);
        f = b;
        f.BossRank = 0;
        f.RoutUntil = b.Now - 1 * sec;
        f.ShakenTicks = 0L;
        Expect("ShakenSeconds 0", f, Attitude.Hostile);
        f = b;
        f.BossRank = 0;
        f.Now = 5 * sec;
        Expect("never routed is never shaken (early clock)", f, Attitude.Hostile);
        f = b;
        f.PassiveMobs = true;
        Expect("Passive Mobs world", f, Attitude.Hostile);
        f.BossRank = 0;
        f.RoutUntil = b.Now - 10 * sec;
        Expect("shaken beats Passive Mobs", f, Attitude.Afraid);
        f = b;
        f.HasRank = false;
        f.BossRank = 8;
        Expect("creature in no home biome list", f, Attitude.Hostile);
        f = b;
        f.Level = 3;
        Expect("2 stars at rank 4 (nerve 6)", f, Attitude.Hostile);
        f.BossRank = 6;
        Expect("2 stars at rank 6", f, Attitude.Afraid);
    }

    // Design 2.3 and tables of 2.5, with default home biomes (spawned at home), bosses ahead, elites and kill steps.
    private static void CheckOutclasses(Checks c, MoraleRules def)
    {
        int RankOf(string prefab) => def.TryGetRank(Hash(prefab), 0, out var r) ? r : -1;

        var star = def.StarRank;
        var skip = def.MaxBossesSkippedByKills;
        c.Check(star == 1 && skip == 1 && def.BossesAhead == 2 && def.EliteExtraBosses == 1,
            $"defaults: StarRank 1, MaxBossesSkippedByKills 1, BossesAhead 2, EliteExtraBosses 1; are {star}, {skip}, "
            + $"{def.BossesAhead}, {def.EliteExtraBosses}");
        var expected = new[]
        {
            ("Boar", 3), ("Neck", 3), ("Greyling", 3), ("Greydwarf", 4), ("Skeleton", 4), ("Greydwarf_Shaman", 5),
            ("Greydwarf_Elite", 5), ("Troll", 5), ("Draugr", 5), ("Skeleton_Swamps", 5), ("Serpent", 5),
            ("Draugr_Elite", 6), ("Wolf", 6), ("Skeleton_Mountains", 6), ("Fenring_Cultist", 7), ("Lox", 7), ("Goblin", 7),
            ("GoblinBrute", 8), ("Seeker", 8), ("SeekerBrute", 9), ("Charred_Melee", 9), ("Charred_Mage", 10),
            ("Greydwarf_Frozen", 10), ("Greydwarf_Shaman_Frozen", 11),
        };
        var wrongRanks = new List<string>();
        foreach (var (prefab, rank) in expected)
        {
            var got = RankOf(prefab);
            if (got != rank)
            {
                wrongRanks.Add($"{prefab} {got} (expected {rank})");
            }
        }
        c.Check(wrongRanks.Count == 0, "default ranks (home biome level + 2, elites + 1): " + string.Join(", ", wrongRanks.ToArray()));
        c.Check(RankOf("TrollFrost") < 0 && RankOf("JotunWarrior") < 0 && RankOf("FallenWarrior") < 0 && RankOf("Eikthyr") < 0,
            "never afraid: TrollFrost, JotunWarrior, FallenWarrior, Eikthyr in no home biome list");

        bool Out(int bossRank, int kills, string prefab, int level, int maxSkip) =>
            Attitudes.Outclasses(bossRank, def.KillBonus(kills), RankOf(prefab), level, star, maxSkip);

        c.Check(Out(5, 0, "Boar", 3, skip), "2-star Boar at rank 5: afraid");
        c.Check(!Out(4, 0, "Boar", 3, skip), "2-star Boar at rank 4: hostile");
        c.Check(Out(4, 0, "Greydwarf", 1, skip) && !Out(3, 0, "Greydwarf", 1, skip),
            "the user's example: a Black Forest Greydwarf is afraid once Moder is dead (rank 4), not at Bonemass (3)");
        c.Check(Out(3, 120, "Greydwarf", 1, skip), "rank 3 + 120 Greydwarf kills: Greydwarf afraid (one boss early)");
        c.Check(!Out(3, 120, "Greydwarf", 2, skip), "rank 3 + 120 Greydwarf kills: 1-star Greydwarf hostile");
        c.Check(!Out(2, 450, "Greydwarf", 1, skip), "rank 2 + 450 Greydwarf kills: Greydwarf hostile (kills bring one boss at most)");
        c.Check(!Out(8, 500, "Greydwarf_Frozen", 1, skip), "rank 8 + 500 Greydwarf kills: Greydwarf_Frozen (rank 10) hostile");
        c.Check(Out(4, 450, "Skeleton_Swamps", 1, skip), "rank 4 + 450 skeleton kills: Skeleton_Swamps afraid");
        c.Check(!Out(4, 450, "Skeleton_Mountains", 1, skip), "rank 4 + 450 skeleton kills: Skeleton_Mountains hostile");
        c.Check(Out(5, 450, "Draugr", 3, skip), "rank 5 + 450 Draugr kills: 2-star Draugr afraid (kills make up for stars)");
        c.Check(Out(6, 120, "Lox", 1, skip), "rank 6 + 120 Lox kills: Lox afraid (one boss early)");
        c.Check(Out(8, 150, "Charred_Melee", 1, skip) && !Out(8, 0, "Charred_Melee", 1, skip),
            "rank 8: a Charred Warrior (rank 9) is afraid only with 100 kills of it");
        c.Check(!Out(3, 120, "Greydwarf", 1, 0), "MaxBossesSkippedByKills 0: rank 3 + 120 kills leaves the Greydwarf hostile");
        c.Check(Out(4, 450, "Greydwarf", 3, 0), "MaxBossesSkippedByKills 0: rank 4 + 450 kills makes up for 2 stars");
        c.Check(Attitudes.Outclasses(4, 0, 4, 3, 0, 1), "StarRank 0: stars do not matter");

        // Worked outcomes (2.5): column = prefab, level, nerve the table say.
        var columns = new[]
        {
            ("Boar", 1, 3), ("Neck", 1, 3), ("Greydwarf", 1, 4), ("Boar", 3, 5), ("Troll", 1, 5), ("Draugr", 1, 5),
            ("Wolf", 1, 6), ("Goblin", 1, 7), ("GoblinBrute", 1, 8), ("Seeker", 1, 8), ("Charred_Melee", 1, 9),
            ("Greydwarf_Frozen", 1, 10),
        };
        var wrong = new List<string>();
        foreach (var (prefab, level, nerve) in columns)
        {
            var rank = RankOf(prefab);
            if (rank + (level - 1) * star != nerve)
            {
                wrong.Add($"{prefab} {level - 1} stars has nerve {rank + (level - 1) * star}, table says {nerve}");
                continue;
            }
            for (var bossRank = 0; bossRank <= MoraleRules.BossCount; bossRank++)
            {
                var afraid = Attitudes.Outclasses(bossRank, 0, rank, level, star, skip);
                if (afraid != bossRank >= nerve)
                {
                    wrong.Add($"{prefab} {level - 1} stars at boss rank {bossRank}: {(afraid ? "afraid" : "attacks")}");
                }
            }
        }
        c.Check(wrong.Count == 0, "worked outcomes table (2.5): " + string.Join(", ", wrong.ToArray()));
    }

    // Design 2.3, 1.11: levels, spawn biome vs home biome, elites, the rank settings, and real world points.
    private static void CheckBiomes(Checks c, MoraleRules def)
    {
        c.Check(Biomes.LevelOf(Heightmap.Biome.Meadows) == 1 && Biomes.LevelOf(Heightmap.Biome.BlackForest) == 2
                && Biomes.LevelOf(Heightmap.Biome.Swamp) == 3 && Biomes.LevelOf(Heightmap.Biome.Mountain) == 4
                && Biomes.LevelOf(Heightmap.Biome.Plains) == 5 && Biomes.LevelOf(Heightmap.Biome.Mistlands) == 6
                && Biomes.LevelOf(Heightmap.Biome.AshLands) == 7 && Biomes.LevelOf(Heightmap.Biome.DeepNorth) == 8,
            "biome levels = the order of each biome's boss");
        c.Check(Biomes.LevelOf(Heightmap.Biome.Ocean) == 0 && Biomes.LevelOf(Heightmap.Biome.None) == 0
                && Biomes.LevelOf(Heightmap.Biome.Meadows | Heightmap.Biome.Swamp) == 0,
            "the Ocean, None or several flags give no spawn level (the home biome decides)");
        c.Check(Biomes.HomeLevels.Length == MoraleRules.HomeCount && MoraleRules.HomeKeys.Length == MoraleRules.HomeCount
                && Biomes.HomeLevels[MoraleRules.HomeCount - 1] == 3 && MoraleRules.HomeKeys[MoraleRules.HomeCount - 1] == "Ocean",
            "nine home lists, the Ocean counts as level 3 (Bonemass)");

        int Rank(MoraleRules r, string prefab, int spawn) => r.TryGetRank(Hash(prefab), spawn, out var rank) ? rank : -1;

        var cases = new[]
        {
            ("Skeleton", 2, 4), ("Skeleton", 4, 6), ("Skeleton", 5, 7), ("Skeleton", 1, 4), ("Skeleton", 0, 4),
            ("Greydwarf", 1, 4), ("Greydwarf", 8, 10), ("Draugr", 5, 7), ("Goblin", 1, 7), ("Surtling", 7, 9),
            ("Serpent", 0, 5), ("Troll", 1, 5), ("Troll", 4, 7), ("Seeker", 2, 8),
        };
        var wrong = new List<string>();
        foreach (var (prefab, spawn, want) in cases)
        {
            var got = Rank(def, prefab, spawn);
            if (got != want)
            {
                wrong.Add($"{prefab} spawned at level {spawn}: {got} (expected {want})");
            }
        }
        c.Check(wrong.Count == 0, "rank = the harder of home and spawn biome + 2 (+1 elite): " + string.Join(", ", wrong.ToArray()));

        var custom = NewRules(r =>
        {
            r.BossesAhead = 0;
            r.EliteExtraBosses = 3;
            r.HomeLists[0] = "Boar, Troll";
            r.Elites = "Troll, NoSuchCreature";
        });
        c.Check(Rank(custom, "Boar", 0) == 1 && Rank(custom, "Greydwarf", 3) == 3,
            "BossesAhead 0: rank = the biome level used (Boar 1; a Greydwarf that spawned in the Swamp 3)");
        c.Check(custom.IsElite(Hash("Troll")) && custom.TryGetHomeLevel(Hash("Troll"), out var trollHome) && trollHome == 1
                && Rank(custom, "Troll", 0) == 4,
            "a name in two home lists takes the easiest (Troll in the Meadows and the Black Forest: level 1), and "
            + "EliteExtraBosses 3 adds 3 (rank 4)");
        c.Check(custom.NamesInSeveralHomeLists.Contains("Troll") && custom.ElitesWithoutHome.Contains("NoSuchCreature")
                && !custom.ElitesWithoutHome.Contains("Troll"),
            "the name check lists names in several home lists and elites in no home list");

        // Real world points (WorldGenerator, x and z only): a spawn point there gives that biome's level.
        var generator = WorldGenerator.instance;
        if (!c.Check(generator != null, "biomes: no WorldGenerator in the world"))
        {
            return;
        }
        var notes = new StringBuilder();
        foreach (var biome in new[] { Heightmap.Biome.Meadows, Heightmap.Biome.Mountain, Heightmap.Biome.Plains, Heightmap.Biome.DeepNorth, Heightmap.Biome.Ocean })
        {
            var p = BiomePoint(biome);
            if (p == Vector3.zero)
            {
                notes.Append(notes.Length > 0 ? "; " : "").Append(biome).Append(": none found");
                continue;
            }
            var level = Biomes.LevelAt(p);
            c.Check(level == Biomes.LevelOf(biome), $"a {biome} point ({F(p.x)}, {F(p.z)}) gives spawn level {level}, expected {Biomes.LevelOf(biome)}");
            notes.Append(notes.Length > 0 ? "; " : "").Append(biome).Append(" at (").Append(F(p.x)).Append(", ").Append(F(p.z)).Append(")");
        }
        var plainsPoint = BiomePoint(Heightmap.Biome.Plains);
        if (plainsPoint != Vector3.zero)
        {
            // Far from the player: no location of that zone is loaded, so the dungeon rule falls back to the same x, z.
            var sky = plainsPoint + Vector3.up * 5000f;
            c.Check(Character.InInterior(sky) && Location.GetZoneLocation(sky) == null && Biomes.LevelAt(sky) == 5,
                "a spawn point up in the sky (a dungeon) with no loaded location in its zone uses its own x, z (Plains = 5)");
        }
        var player = Player.m_localPlayer;
        if (player != null)
        {
            notes.Append("; the test player stands in ").Append(Biomes.Name(Biomes.LevelAt(player.transform.position)));
        }
        SelfTest.Note(RulesName, "biome points found (spawn level checks): " + notes);
    }

    private static void CheckKillSteps(Checks c, MoraleRules def)
    {
        var steps = def.KillStepValues;
        c.Check(steps.Length == 2 && steps[0] == 100 && steps[1] == 400, "default kill steps 100, 400");
        c.Check(def.KillBonus(0) == 0 && def.KillBonus(99) == 0 && def.KillBonus(100) == 1 && def.KillBonus(399) == 1
                && def.KillBonus(400) == 2 && def.KillBonus(100000) == 2,
            "kill bonus: 99 -> 0, 100 -> 1, 399 -> 1, 400 -> 2");
        var problems = new StringBuilder();
        var parsed = MoraleRules.ParseKillSteps("400, 100, x, 0, 200000, 100", problems);
        c.Check(parsed.Length == 4 && parsed[0] == 1 && parsed[1] == 100 && parsed[2] == 400 && parsed[3] == 100000
                && problems.Length > 0,
            $"kill steps \"400, 100, x, 0, 200000, 100\" -> 1, 100, 400, 100000 with problems; got {string.Join(",", Array.ConvertAll(parsed, v => v.ToString(CultureInfo.InvariantCulture)))}");
        problems.Length = 0;
        c.Check(MoraleRules.ParseKillSteps("", problems).Length == 0 && problems.Length == 0, "empty kill steps: none, no problem");
        c.Check(MoraleRules.ParseKillSteps("1,2,3,4,5,6", problems).Length == MoraleRules.MaxKillSteps && problems.Length > 0,
            "six kill steps: the first five kept, a problem logged");
        var off = NewRules(r => r.KillSteps = "");
        c.Check(off.KillBonus(100000) == 0, "KillSteps empty: kills never give a bonus");
    }

    private static void CheckStanding(Checks c, MoraleRules def)
    {
        var orders = BossLadder.Get();
        var ranked = PrefabTokens.Get(def);
        if (!c.Check(orders != null && ranked != null, "standing: boss ladder and prefab tokens need ZNetScene"))
        {
            return;
        }
        var gd = TokenOf("Greydwarf");
        var boar = TokenOf("Boar");
        var moose = TokenOf("Moose");
        var deer = TokenOf("Deer");
        c.Check(gd == "$enemy_greydwarf" && TokenOf("Greydwarf_Frozen") == gd, "Greydwarf and Greydwarf_Frozen share $enemy_greydwarf (1.7)");
        c.Check(TokenOf("Skeleton_Swamps") == "$enemy_skeleton" && TokenOf("Skeleton_Mountains") == "$enemy_skeleton",
            "skeleton variants share $enemy_skeleton (1.7)");
        if (!c.Check(gd != null && boar != null && moose != null && deer != null, "tokens of Greydwarf, Boar, Moose, Deer"))
        {
            return;
        }

        Standing.Computed Run(Dictionary<string, float> kills, int forced = -1) =>
            Standing.Compute(kills, orders, def, ranked, forced);

        c.Check(Run(Kills("$enemy_eikthyr", 1, "$enemy_gdking", 1)).BossRank == 2, "Eikthyr + The Elder killed -> boss rank 2");
        c.Check(Run(Kills("$enemy_gdking", 1)).BossRank == 2, "only The Elder killed -> boss rank 2 (highest, not count)");
        c.Check(Run(Kills("$enemy_frozenking", 3)).BossRank == 0, "only Kall's first phases ($enemy_frozenking, order 0) -> 0");
        c.Check(Run(Kills("$enemy_frozenking_p3", 1)).BossRank == 8, "$enemy_frozenking_p3 -> 8");
        c.Check(Run(Kills("$enemy_eikthyr", 0)).BossRank == 0, "a boss with 0 kills does not count");
        c.Check(Run(null).BossRank == 0 && Run(null).Published.Count == 0, "no kill table -> rank 0, nothing listed");
        var forced = Run(Kills("$enemy_eikthyr", 1), 5);
        c.Check(forced.BossRank == 5 && forced.Forced, "forced rank 5 wins over the table");

        // Greydwarf token home ranks 4 (Greydwarf) .. 10 (Greydwarf_Frozen): listed from rank 3 (one boss early).
        var r1 = Run(Kills("$enemy_bonemass", 1, gd, 120));
        c.Check(r1.BossRank == 3 && TryEntry(r1, gd, out var e1) && e1.Bonus == 1 && e1.Listed
                && r1.Published.BonusFor(Hash(gd)) == 1,
            "rank 3 + 120 Greydwarf kills: +1 listed");
        var r2 = Run(Kills("$enemy_bonemass", 1, gd, 400));
        c.Check(r2.Published.BonusFor(Hash(gd)) == 2, "rank 3 + 400 Greydwarf kills: +2");
        var r0 = Run(Kills("$enemy_eikthyr", 1, gd, 120));
        c.Check(TryEntry(r0, gd, out var e0) && e0.Bonus == 1 && !e0.Listed && r0.Published.Count == 0,
            "rank 1 + 120 Greydwarf kills: bonus known but not listed (it cannot change an outcome yet: two bosses short)");

        // Listing filter at rank 8: Boar (3) afraid by boss rank alone; Moose (10) out of reach even with kills;
        // Greydwarf (4..10) and Charred Warrior (9) still have stars or one boss to beat.
        var charred = TokenOf("Charred_Melee");
        var r8 = Run(Kills("$enemy_frozenking_p3", 1, boar, 500, moose, 500, gd, 500, deer, 500, charred ?? "$none", 150));
        c.Check(r8.BossRank == 8 && r8.Published.BonusFor(Hash(boar)) == 0, "rank 8: the Boar bonus is not listed");
        c.Check(r8.Published.BonusFor(Hash(moose)) == 0, "rank 8: the Moose bonus (Deep North, rank 10) is not listed");
        c.Check(r8.Published.BonusFor(Hash(gd)) == 2, "rank 8: the Greydwarf bonus is listed (its token reaches rank 10)");
        c.Check(charred != null && r8.Published.BonusFor(Hash(charred)) == 1,
            "rank 8: the Charred Warrior bonus (rank 9, one boss early) is listed");
        c.Check(!TryEntry(r8, deer, out _), "a kind in no home biome list (Deer) never gets a bonus");

        // World spawn table: Draugr (Swamp home, rank 5) also spawn at night in the Plains (rank 7). Home rank alone
        // would stop listing it at rank 7; the world spawn keep it (a 1-star Plains Draugr need 7 + 1).
        var draugr = TokenOf("Draugr");
        var r7 = Run(Kills("$enemy_fader", 1, draugr ?? "$none", 150));
        c.Check(draugr != null && r7.BossRank == 7 && r7.Published.BonusFor(Hash(draugr)) == 1,
            "rank 7: the Draugr bonus is listed (Draugr also spawn in the Plains, rank 7)");

        // Cap 16, biggest bonus first.
        var fake = new List<PrefabTokens.RankedToken>();
        var fakeKills = Kills("$enemy_bonemass", 1);
        for (var i = 0; i < 20; i++)
        {
            var t = "$mc_morale_test_" + i.ToString(CultureInfo.InvariantCulture);
            var rt = new PrefabTokens.RankedToken { Token = t, Hash = Hash(t) };
            rt.Add(3);
            fake.Add(rt);
            fakeKills[t] = i < 10 ? 150f : 500f;
        }
        var capped = Standing.Compute(fakeKills, orders, def, fake, -1);
        var sorted = true;
        var twos = 0;
        for (var i = 0; i < capped.Published.Count; i++)
        {
            if (i > 0 && capped.Published.Bonuses[i] > capped.Published.Bonuses[i - 1])
            {
                sorted = false;
            }
            if (capped.Published.Bonuses[i] == 2)
            {
                twos++;
            }
        }
        c.Check(capped.BossRank == 3 && capped.Capped && capped.Published.Count == Standing.MaxListed,
            $"20 listed kinds: capped at {Standing.MaxListed}, got {capped.Published.Count}");
        c.Check(sorted && twos == 10, $"cap keeps the highest bonuses first (all ten +2 kept, got {twos})");
        var again = Standing.Compute(fakeKills, orders, def, fake, -1);
        c.Check(CreatureKeys.SameBytes(Standing.Encode(again.Published), Standing.Encode(capped.Published)),
            "same table twice = same bytes (no needless rewrite)");
    }

    private static void CheckStandingBytes(Checks c)
    {
        var data = new StandingData(3, new[] { Hash("$enemy_greydwarf"), Hash("$enemy_boar") }, new byte[] { 2, 1 });
        var bytes = Standing.Encode(data);
        c.Check(bytes.Length == 3 + 5 * 2 && bytes[0] == Standing.Layout && bytes[1] == 3 && bytes[2] == 2,
            "standing bytes: layout 1, rank, count, 5 bytes per entry");
        c.Check(Standing.TryDecode(bytes, out var back) && back != null && back.BossRank == 3 && back.Count == 2
                && back.BonusFor(Hash("$enemy_greydwarf")) == 2 && back.BonusFor(Hash("$enemy_boar")) == 1
                && back.BonusFor(Hash("$enemy_troll")) == 0,
            "standing bytes round trip");
        var empty = Standing.Encode(new StandingData(8, null, null));
        c.Check(empty.Length == 3 && Standing.TryDecode(empty, out var e8) && e8 != null && e8.BossRank == 8 && e8.Count == 0,
            "rank 8 with nothing listed = 3 bytes");
        var withdrawn = Standing.WithdrawnBytes();
        c.Check(withdrawn.Length == 3 && withdrawn[1] == Standing.NoRank && Standing.TryDecode(withdrawn, out var none) && none == null,
            "withdrawn [1,255,0] reads as no standing");
        c.Check(!ReferenceEquals(Standing.WithdrawnBytes(), withdrawn), "withdrawn bytes are a fresh array each time (ZDO compares by reference)");
        var bad = new List<byte[]>
        {
            null, new byte[0], new byte[] { 1, 9, 0 }, new byte[] { 2, 1, 0 }, new byte[] { 1, 1, 1, 0, 0, 0, 0 },
            new byte[] { 1, 1, 1, 1, 2, 3, 4, 0 }, new byte[] { 1, 1, 1, 1, 2, 3, 4, 6 }, new byte[] { 1, 255, 1, 1, 2, 3, 4, 1 },
            new byte[] { 1, 1, 17 },
        };
        var accepted = new List<int>();
        for (var i = 0; i < bad.Count; i++)
        {
            if (Standing.TryDecode(bad[i], out _))
            {
                accepted.Add(i);
            }
        }
        c.Check(accepted.Count == 0, "corrupt standing arrays must read as no standing; accepted #" + string.Join(",", accepted.ConvertAll(i => i.ToString(CultureInfo.InvariantCulture)).ToArray()));
    }

    private static void CheckProvokers(Checks c)
    {
        var sec = TimeSpan.TicksPerSecond;
        var now = 5000000L * sec;
        const long a = 7001, b = 7002, cc = 7003, d = 7004, e = 7005;
        var d1 = CreatureKeys.WithProvocation(null, a, now + 30 * sec, now);
        c.Check(CreatureKeys.ProvokerCount(d1) == 1 && d1.Length == 18, "provokers: add = one entry, 18 bytes");
        c.Check(CreatureKeys.TryGetUntil(d1, a, out var until) && until == now + 30 * sec, "provokers: entry holds the deadline");
        c.Check(CreatureKeys.HasLive(d1, a, now) && CreatureKeys.HasLive(d1, a, now + 29 * sec)
                && !CreatureKeys.HasLive(d1, a, now + 30 * sec) && !CreatureKeys.HasLive(d1, b, now),
            "provokers: live before the deadline, not at it, and only for that player");
        var d2 = CreatureKeys.WithProvocation(d1, a, now + 40 * sec, now + 10 * sec);
        c.Check(CreatureKeys.ProvokerCount(d2) == 1 && CreatureKeys.TryGetUntil(d2, a, out until) && until == now + 40 * sec,
            "provokers: refresh moves the same entry");
        var d3 = CreatureKeys.WithProvocation(d2, b, now + 15 * sec, now + 10 * sec);
        var d4 = CreatureKeys.WithProvocation(d3, cc, now + 50 * sec, now + 20 * sec);
        c.Check(CreatureKeys.ProvokerCount(d3) == 2 && CreatureKeys.ProvokerCount(d4) == 2 && !CreatureKeys.TryGetUntil(d4, b, out _)
                && CreatureKeys.TryGetUntil(d4, a, out _) && CreatureKeys.TryGetUntil(d4, cc, out _),
            "provokers: an expired entry is pruned on the next write");
        byte[] full = null;
        full = CreatureKeys.WithProvocation(full, a, now + 10 * sec, now);
        full = CreatureKeys.WithProvocation(full, b, now + 20 * sec, now);
        full = CreatureKeys.WithProvocation(full, cc, now + 30 * sec, now);
        full = CreatureKeys.WithProvocation(full, d, now + 40 * sec, now);
        var fifth = CreatureKeys.WithProvocation(full, e, now + 50 * sec, now);
        c.Check(CreatureKeys.ProvokerCount(full) == 4 && CreatureKeys.ProvokerCount(fifth) == CreatureKeys.MaxProvokers
                && !CreatureKeys.TryGetUntil(fifth, a, out _) && CreatureKeys.TryGetUntil(fifth, b, out _)
                && CreatureKeys.TryGetUntil(fifth, d, out _) && CreatureKeys.TryGetUntil(fifth, e, out _) && fifth.Length <= 66,
            "provokers: a fifth player drops the entry ending first, at most 66 bytes");
        c.Check(!CreatureKeys.ShouldWrite(d1, a, now + 30 * sec + sec / 2) && CreatureKeys.ShouldWrite(d1, a, now + 31 * sec)
                && CreatureKeys.ShouldWrite(d1, b, now + 1),
            "provokers: at most one write per player per second, a new player always written");
        var cleared = CreatureKeys.EmptyProvokers();
        c.Check(cleared.Length == 2 && cleared[0] == CreatureKeys.ProvokersLayout && cleared[1] == 0
                && CreatureKeys.ProvokerCount(cleared) == 0 && !CreatureKeys.HasLive(cleared, a, now)
                && !ReferenceEquals(cleared, CreatureKeys.EmptyProvokers()),
            "provokers: rout clear = [1,0], fresh array");
        c.Check(CreatureKeys.ProvokerCount(new byte[] { 2, 0 }) == 0 && CreatureKeys.ProvokerCount(new byte[] { 1, 1, 0 }) == 0
                && CreatureKeys.ProvokerCount(new byte[] { 1, 5 }) == 0 && !CreatureKeys.HasLive(d1, 0L, now),
            "provokers: unknown layout, wrong length, too many entries or player id 0 = nobody provoked");
        var buf = new byte[8];
        CreatureKeys.WriteLong(buf, 0, -123456789012345L);
        var ib = new byte[4];
        CreatureKeys.WriteInt(ib, 0, Hash("$enemy_greydwarf"));
        c.Check(CreatureKeys.ReadLong(buf, 0) == -123456789012345L && CreatureKeys.ReadInt(ib, 0) == Hash("$enemy_greydwarf"),
            "little-endian long and int round trip (negative values too)");
    }

    // Cornered count (2.6) and the fear's start and release (2.5 piece 2), pure.
    private static void CheckCornered(Checks c)
    {
        var t = 0f;
        var inside = 2f * 2f;
        var outside = 3.5f * 3.5f;
        var struck = Cornered.Advance(ref t, inside, 0.5f, 3f, 2f);
        c.Check(!struck && Mathf.Approximately(t, 0.5f), $"cornered: 0.5 s within 3 m counts 0.5 s (time {F(t)})");
        Cornered.Advance(ref t, inside, 1f, 3f, 2f);
        struck = Cornered.Advance(ref t, inside, 0.6f, 3f, 2f);
        c.Check(struck, $"cornered: 2.1 s in a row within 3 m strikes at CorneredSeconds 2 (time {F(t)})");
        t = 1.9f;
        struck = Cornered.Advance(ref t, outside, 0.2f, 3f, 2f);
        c.Check(!struck && t == 0f, "cornered: one frame beyond the range starts the count again");
        t = 5f;
        c.Check(!Cornered.Advance(ref t, 0f, 1f, 0f, 2f) && t == 0f, "cornered: CorneredRange 0 = never");

        var m = Fear.Margin;
        var mem = Fear.Memory;
        c.Check(Fear.Qualifies(11f, 12f, false, true, float.MaxValue), "fear: a sensed player 11 m away starts it (FearRange 12)");
        c.Check(!Fear.Qualifies(11f, 12f, false, false, float.MaxValue), "fear: a player it does not sense never starts it (sneaking)");
        c.Check(!Fear.Qualifies(13f, 12f, false, true, float.MaxValue), "fear: a sensed player 13 m away does not start it");
        c.Check(Fear.Qualifies(12f + m - 0.1f, 12f, true, true, 0f), $"fear: the player it runs from keeps it up to FearRange + {F(m)} m");
        c.Check(!Fear.Qualifies(12f + m + 0.1f, 12f, true, true, 0f), $"fear: beyond FearRange + {F(m)} m it calms");
        c.Check(Fear.Qualifies(8f, 12f, true, false, mem - 0.5f) && !Fear.Qualifies(8f, 12f, true, false, mem + 0.5f),
            $"fear: unsensed, it keeps running {F(mem)} s, then calms");
        c.Check(!Fear.Qualifies(1f, 0f, true, true, 0f) && !Fear.Qualifies(1f, 0f, false, true, 0f), "fear: FearRange 0 (test rules only) = no fear");
    }

    private static bool SameRules(MoraleRules a, MoraleRules b)
    {
        for (var i = 0; i < MoraleRules.HomeCount; i++)
        {
            if (a.HomeLists[i] != b.HomeLists[i])
            {
                return false;
            }
        }
        return a.BossesAhead == b.BossesAhead && a.Elites == b.Elites && a.EliteExtraBosses == b.EliteExtraBosses
               && a.StarRank == b.StarRank && a.KillSteps == b.KillSteps && a.MaxBossesSkippedByKills == b.MaxBossesSkippedByKills
               && a.NightHuntersCanBeAfraid == b.NightHuntersCanBeAfraid && a.FearRange == b.FearRange
               && a.ProvokedSeconds == b.ProvokedSeconds && a.NearMissRange == b.NearMissRange
               && a.CorneredRange == b.CorneredRange && a.CorneredSeconds == b.CorneredSeconds && a.Packs == b.Packs
               && a.RoutRadius == b.RoutRadius && a.RoutSeconds == b.RoutSeconds && a.ShakenSeconds == b.ShakenSeconds
               && a.Describe() == b.Describe();
    }

    private static MoraleRules ReadBack(MoraleRules rules, out bool ok, out bool clamped)
    {
        var pkg = new ZPackage();
        rules.Write(pkg);
        pkg.SetPos(0);
        ok = MoraleRules.TryRead(pkg, out var back, out clamped);
        return back;
    }

    private static void CheckWire(Checks c)
    {
        var custom = NewRules(r =>
        {
            r.HomeLists[0] = "Boar, Neck";
            r.BossesAhead = 1;
            r.Elites = "Neck";
            r.EliteExtraBosses = 2;
            r.StarRank = 2;
            r.KillSteps = "10, 20";
            r.MaxBossesSkippedByKills = 2;
            r.NightHuntersCanBeAfraid = false;
            r.FearRange = 20f;
            r.ProvokedSeconds = 45f;
            r.NearMissRange = 2.5f;
            r.CorneredRange = 1.75f;
            r.CorneredSeconds = 4f;
            r.Packs = "Troll > Greydwarf";
            r.RoutRadius = 30f;
            r.RoutSeconds = 20f;
            r.ShakenSeconds = 90f;
        });
        var back = ReadBack(custom, out var ok, out var clamped);
        c.Check(ok && !clamped && back != null && SameRules(back, custom), "rules wire round trip (every field)");
        if (back != null)
        {
            c.Check(back.TryGetRank(Hash("Neck"), 0, out var neck) && neck == 1 + 1 + 2 && !back.TryGetRank(Hash("Hen"), 0, out _)
                    && back.KillStepValues.Length == 2 && back.IsFollower(Hash("Troll"), Hash("Greydwarf"))
                    && !back.IsLeader(Hash("Greydwarf_Elite")),
                "rules read from the wire rebuild their tables (Neck: Meadows 1 + ahead 1 + elite 2 = 4)");
        }
        var def = ReadBack(NewRules(null), out ok, out clamped);
        c.Check(ok && !clamped && def != null && SameRules(def, NewRules(null)), "default rules survive the wire unchanged");

        var wild = NewRules(r =>
        {
            r.BossesAhead = 99;
            r.EliteExtraBosses = -1;
            r.StarRank = 99;
            r.MaxBossesSkippedByKills = -3;
            r.FearRange = 0f;
            r.ProvokedSeconds = 1f;
            r.NearMissRange = float.NaN;
            r.CorneredRange = 100f;
            r.CorneredSeconds = float.PositiveInfinity;
            r.RoutRadius = 1f;
            r.RoutSeconds = 1000f;
            r.ShakenSeconds = -5f;
        });
        var w = ReadBack(wild, out ok, out clamped);
        c.Check(ok && clamped && w != null && w.BossesAhead == MoraleRules.MaxBossesAhead && w.EliteExtraBosses == 0
                && w.StarRank == MoraleRules.MaxStarRank && w.MaxBossesSkippedByKills == 0
                && w.FearRange == MoraleRules.MinFearRange
                && w.ProvokedSeconds == MoraleRules.MinProvokedSeconds && w.NearMissRange == MoraleRules.MinNearMissRange
                && w.CorneredRange == MoraleRules.MaxCorneredRange && w.CorneredSeconds == MoraleRules.MaxCorneredSeconds
                && w.RoutRadius == MoraleRules.MinRoutRadius && w.RoutSeconds == MoraleRules.MaxRoutSeconds
                && w.ShakenSeconds == MoraleRules.MinShakenSeconds,
            "out-of-range rules from the wire are brought into the config ranges (NaN = min, infinity = max; a FearRange of 0 becomes 3 m: no creature ignores a player)");
        var edges = NewRules(r =>
        {
            r.BossesAhead = MoraleRules.MaxBossesAhead;
            r.EliteExtraBosses = MoraleRules.MaxEliteExtra;
            r.StarRank = MoraleRules.MaxStarRank;
            r.MaxBossesSkippedByKills = MoraleRules.MaxBossesSkipped;
            r.FearRange = MoraleRules.MaxFearRange;
            r.ProvokedSeconds = MoraleRules.MaxProvokedSeconds;
            r.NearMissRange = MoraleRules.MaxNearMissRange;
            r.CorneredRange = MoraleRules.MinCorneredRange;
            r.CorneredSeconds = MoraleRules.MinCorneredSeconds;
            r.RoutRadius = MoraleRules.MaxRoutRadius;
            r.RoutSeconds = MoraleRules.MinRoutSeconds;
            r.ShakenSeconds = MoraleRules.MaxShakenSeconds;
        });
        ReadBack(edges, out ok, out clamped);
        c.Check(ok && !clamped, "values at the range edges are not clamped");

        var pkg = new ZPackage();
        pkg.Write(MoraleRules.Layout + 1);
        pkg.SetPos(0);
        c.Check(!MoraleRules.TryRead(pkg, out _, out _), "unknown rules layout refused");
        pkg = new ZPackage();
        pkg.Write(1);
        for (var i = 0; i < 8; i++)
        {
            pkg.Write("Boar");
        }
        pkg.SetPos(0);
        c.Check(MoraleRules.Layout == 2 && !MoraleRules.TryRead(pkg, out _, out _), "rules of the first version (layout 1) refused");
        pkg = new ZPackage();
        custom.Write(pkg);
        pkg.Write(7);
        pkg.SetPos(0);
        c.Check(!MoraleRules.TryRead(pkg, out _, out _), "rules with bytes after the last field refused");
        pkg = new ZPackage();
        pkg.Write(MoraleRules.Layout);
        pkg.Write("Boar");
        pkg.SetPos(0);
        c.Check(!MoraleRules.TryRead(pkg, out _, out _), "cut-off rules refused");
        c.Check(!ServerRules.Receive(new ZPackage()), "single player never takes rules from a peer");

        var raw = NewRules(r =>
        {
            r.ProvokedSeconds = 1f;
            r.RoutRadius = 1f;
            r.FearRange = 0f;
        });
        try
        {
            ServerRules.TestRules = raw;
            c.Check(ReferenceEquals(ServerRules.Current, raw) && ServerRules.Current.ProvokedSeconds == 1f
                    && ServerRules.Current.RoutRadius == 1f && ServerRules.Current.FearRange == 0f && !ServerRules.Pending,
                "TestRules are used as given (not clamped; FearRange 0 turns the fear off for a test step)");
        }
        finally
        {
            ServerRules.TestRules = null;
        }
        c.Check(!ReferenceEquals(ServerRules.Current, raw), "TestRules cleared: own rules again");
    }

    private static void CheckPacks(Checks c, MoraleRules def)
    {
        var p = NewRules(r => r.Packs = "  LeaderA , LeaderB >FollowerC;  LeaderB> FollowerD ,FollowerE ; ; X>Y>Z; >F; G>");
        c.Check(p.LeaderCount == 2 && p.IsLeader(Hash("LeaderA")) && p.IsLeader(Hash("LeaderB")),
            $"packs: two leaders parsed (spaces trimmed), got {p.LeaderCount}");
        c.Check(p.IsFollower(Hash("LeaderA"), Hash("FollowerC")) && p.IsFollower(Hash("LeaderB"), Hash("FollowerC"))
                && p.IsFollower(Hash("LeaderB"), Hash("FollowerD")) && p.IsFollower(Hash("LeaderB"), Hash("FollowerE"))
                && !p.IsFollower(Hash("LeaderA"), Hash("FollowerD")),
            "packs: several leaders share followers, a leader in two packs gets both follower lists merged");
        var problems = p.ParseProblems ?? "";
        c.Check(!p.IsLeader(Hash("X")) && !p.IsLeader(Hash("G")) && problems.Contains("X>Y>Z") && problems.Contains(">F")
                && problems.Contains("G>"),
            $"packs: broken packs ignored and reported ({problems})");
        c.Check(def.ParseProblems == null, "default settings parse without problems");
        c.Check(def.IsFollower(Hash("Troll"), Hash("Greydwarf")) && def.IsFollower(Hash("Greydwarf_Elite"), Hash("Greyling"))
                && def.IsFollower(Hash("Greydwarf_Shaman"), Hash("Greydwarf")) && def.IsFollower(Hash("Troll_sleeping"), Hash("Greyling"))
                && def.IsFollower(Hash("Charred_Mage"), Hash("Charred_Twitcher"))
                && def.IsFollower(Hash("Greydwarf_Shaman_Frozen"), Hash("Greydwarf_Frozen")),
            "default packs: the user's example (Troll, Shaman, Brute lead Greydwarfs and Greylings) and the extra ones");
        c.Check(!def.IsFollower(Hash("Troll"), Hash("Boar")) && !def.IsLeader(Hash("TrollFrost")) && !def.IsLeader(Hash("Greydwarf")),
            "default packs: Boar follows nobody, TrollFrost and Greydwarf lead nobody");
        c.Check(def.RankedPrefabCount == 79 && def.EliteCount == 24 && def.LeaderCount == 12,
            $"default lists: 79 creatures in the home biome lists, 24 elites and 12 leaders, got {def.RankedPrefabCount}, {def.EliteCount} and {def.LeaderCount}");
    }

    // Every default name = creature me can make afraid (MonsterAI, not boss or other exempt kind), in one home list only;
    // every elite has a home; every default pack leader is an elite (design 2.3).
    private static void CheckNames(Checks c, MoraleRules def)
    {
        var scene = ZNetScene.instance;
        if (!c.Check(scene != null, "names: no ZNetScene"))
        {
            return;
        }
        var missing = new List<string>();
        var notAi = new List<string>();
        var exempt = new List<string>();
        foreach (var name in def.Names)
        {
            var go = scene.GetPrefab(name);
            if (go == null)
            {
                missing.Add(name);
                continue;
            }
            var character = go.GetComponent<Character>();
            var ai = go.GetComponent<MonsterAI>();
            if (character == null || ai == null)
            {
                notAi.Add(name);
                continue;
            }
            var faction = character.m_faction;
            if (character.m_boss || ai.m_enableHuntPlayer || faction == Character.Faction.Boss
                || faction == Character.Faction.TrainingDummy || faction == Character.Faction.Dverger)
            {
                exempt.Add(name);
            }
        }
        c.Check(missing.Count == 0, "default names not in the game: " + string.Join(", ", missing.ToArray()));
        c.Check(notAi.Count == 0, "default names without MonsterAI: " + string.Join(", ", notAi.ToArray()));
        c.Check(exempt.Count == 0, "default names that are always exempt (boss, hunter, dummy, Dvergr): " + string.Join(", ", exempt.ToArray()));
        c.Check(def.NamesInSeveralHomeLists.Count == 0, "default names in several home biome lists: " + string.Join(", ", def.NamesInSeveralHomeLists.ToArray()));
        c.Check(def.ElitesWithoutHome.Count == 0, "default elites in no home biome list: " + string.Join(", ", def.ElitesWithoutHome.ToArray()));
        var leadersNotElite = new List<string>();
        foreach (var pack in MoraleRules.DefaultPacks.Split(';'))
        {
            foreach (var leader in MoraleRules.SplitNames(pack.Split('>')[0]))
            {
                if (!def.IsElite(Hash(leader)) && !leadersNotElite.Contains(leader))
                {
                    leadersNotElite.Add(leader);
                }
            }
        }
        c.Check(leadersNotElite.Count == 0, "default pack leaders that are not elites: " + string.Join(", ", leadersNotElite.ToArray()));
        PrefabTokens.Get(def);
        c.Check(PrefabTokens.UnknownNames.Count == 0 && PrefabTokens.NotCreatures.Count == 0,
            "the name check finds nothing wrong in the defaults");
        foreach (var name in new[] { "Greydwarf", "Troll", "Boar", "Draugr_sleeping", "Greyling" })
        {
            var go = scene.GetPrefab(name);
            c.Check(go != null && go.GetComponent<MonsterAI>() != null, $"test creature {name} exists with MonsterAI");
        }
        var draugr = scene.GetPrefab("Draugr_sleeping");
        var draugrAi = draugr != null ? draugr.GetComponent<MonsterAI>() : null;
        c.Check(draugrAi != null && draugrAi.m_sleeping, "Draugr_sleeping starts asleep (prefab)");
        c.Check(scene.GetPrefab("Deer") != null && scene.GetPrefab("Deer").GetComponent<AnimalAI>() != null,
            "Deer is an AnimalAI (never in a home biome list)");
    }

    private static void CheckLadder(Checks c)
    {
        var expected = new Dictionary<string, int>
        {
            { "$enemy_eikthyr", 1 }, { "$enemy_gdking", 2 }, { "$enemy_bonemass", 3 }, { "$enemy_dragon", 4 },
            { "$enemy_goblinking", 5 }, { "$enemy_seekerqueen", 6 }, { "$enemy_fader", 7 }, { "$enemy_frozenking_p3", 8 },
        };
        var orders = BossLadder.Get();
        if (!c.Check(orders != null, "boss ladder: no ZNetScene"))
        {
            return;
        }
        var wrong = new List<string>();
        foreach (var pair in expected)
        {
            if (!orders.TryGetValue(pair.Key, out var order) || order != pair.Value)
            {
                wrong.Add($"{pair.Key} {(orders.ContainsKey(pair.Key) ? order.ToString(CultureInfo.InvariantCulture) : "missing")}");
            }
        }
        c.Check(orders.Count == 8 && wrong.Count == 0,
            $"boss ladder from the live prefabs = orders 1..8 of 1.7 ({orders.Count} entries; wrong: {string.Join(", ", wrong.ToArray())})");
        c.Check(ReferenceEquals(BossLadder.Get(), orders), "boss ladder cached per ZNetScene");
    }

    private static void CheckJoin(Checks c)
    {
        c.Check(PlayerCheck.Decide(false, true, true, false, false, false) == JoinVerdict.Skip
                && PlayerCheck.Decide(true, false, true, false, false, false) == JoinVerdict.Skip
                && PlayerCheck.Decide(true, true, false, false, false, false) == JoinVerdict.Skip
                && PlayerCheck.Decide(true, true, true, true, false, false) == JoinVerdict.Skip,
            "join check: not server, gone, not ready or already being kicked = skip");
        c.Check(PlayerCheck.Decide(true, true, true, false, true, false) == JoinVerdict.Compatible
                && PlayerCheck.Decide(true, true, true, false, true, true) == JoinVerdict.Compatible,
            "join check: compatible player = fine (whatever AllowPlayersWithoutMod)");
        c.Check(PlayerCheck.Decide(true, true, true, false, false, false) == JoinVerdict.Refuse
                && PlayerCheck.Decide(true, true, true, false, false, true) == JoinVerdict.Allowed,
            "join check: not compatible = refused, or allowed with AllowPlayersWithoutMod");
        c.Check(!PlayerCheck.HasWork, "join check: nothing queued in single player");
    }

    // Running creature show vanilla's alert icon (EnemyHud.ShowHud find "Alerted" and "Aware" on the plate): both there.
    private static void CheckPlateParts(Checks c)
    {
        var hud = EnemyHud.instance;
        if (!c.Check(hud != null && hud.m_baseHud != null, "EnemyHud and its creature plate template exist"))
        {
            return;
        }
        var root = hud.m_baseHud.transform;
        c.Check(root.Find("Alerted") is RectTransform, "creature plate has the vanilla Alerted icon (shown while a creature runs)");
        c.Check(root.Find("Aware") is RectTransform, "creature plate has the vanilla Aware icon");
        SelfTest.Note(RulesName, $"plate: shown within {F(hud.m_maxShowDistance)} m for {F(hud.m_hoverShowDuration)} s after the crosshair");
    }

    // ---------- NOTE lines: me settle the design's unverified values (9) ----------

    private static void NoteUnverified()
    {
        TryNote("English names", NoteNames);
        TryNote("leaders' death animation", NoteLeaders);
        TryNote("world spawn tables", NoteSpawns);
        TryNote("raid army_eikthyr", NoteRaid);
        TryNote("bomb damage", NoteBombs);
        TryNote("priority building targets", NoteTargets);
        TryNote("senses and flee", NoteSenses);
    }

    private static void TryNote(string what, Action note)
    {
        try
        {
            note();
        }
        catch (Exception e)
        {
            SelfTest.Note(RulesName, $"{what}: could not read ({e.GetType().Name}: {e.Message})");
        }
    }

    // Fear (2.5 piece 2): the senses and flee numbers the fear relies on, from the live prefabs.
    private static void NoteSenses()
    {
        var scene = ZNetScene.instance;
        var sb = new StringBuilder();
        foreach (var name in new[] { "Greydwarf", "Boar", "Neck", "Skeleton", "Troll", "Draugr", "Wolf", "Lox", "Leech", "Serpent" })
        {
            var go = scene.GetPrefab(name);
            var ai = go != null ? go.GetComponent<MonsterAI>() : null;
            var ch = go != null ? go.GetComponent<Character>() : null;
            if (ai == null || ch == null)
            {
                continue;
            }
            sb.Append(sb.Length > 0 ? "; " : "").Append(name).Append(": view ").Append(F(ai.m_viewRange)).Append(" m / ")
                .Append(F(ai.m_viewAngle)).Append(" deg, hear ").Append(F(ai.m_hearRange)).Append(" m, flee ")
                .Append(F(ai.m_fleeRange)).Append(" m every ").Append(F(ai.m_fleeInterval)).Append(" s, run ")
                .Append(F(ch.m_runSpeed));
        }
        SelfTest.Note(RulesName, $"senses and flee (default FearRange {F(new MoraleRules().FearRange)} m + {F(Fear.Margin)} m to calm): " + sb);
    }

    private static void NoteNames()
    {
        var loc = Localization.instance;
        var sb = new StringBuilder();
        foreach (var prefab in new[]
                 {
                     "Charred_Melee", "Charred_Archer", "Charred_Twitcher", "Charred_Mage", "Goblin", "GoblinArcher",
                     "GoblinBrute", "GoblinShaman", "Greydwarf_Elite", "Greydwarf_Shaman", "Fenring_Cultist", "SeekerBrute",
                     "Greydwarf_Frozen", "Greydwarf_Shaman_Frozen", "Hatchling", "Writhan",
                 })
        {
            var token = TokenOf(prefab);
            sb.Append(sb.Length > 0 ? "; " : "").Append(prefab).Append(" = \"")
                .Append(token != null && loc != null ? loc.Localize(token) : "?").Append('"');
        }
        SelfTest.Note(RulesName, "English names: " + sb);
    }

    private static void NoteLeaders()
    {
        var scene = ZNetScene.instance;
        var with = new List<string>();
        var checkedCount = 0;
        foreach (var pack in MoraleRules.DefaultPacks.Split(';'))
        {
            var parts = pack.Split('>');
            if (parts.Length != 2)
            {
                continue;
            }
            foreach (var leader in MoraleRules.SplitNames(parts[0]))
            {
                var go = scene.GetPrefab(leader);
                var character = go != null ? go.GetComponent<Character>() : null;
                checkedCount++;
                if (character != null && character.m_deathAnimation)
                {
                    with.Add(leader);
                }
            }
        }
        SelfTest.Note(RulesName, $"default pack leaders with a death animation (OnDeath may run twice; the handled-death set guards): "
                                 + $"{(with.Count == 0 ? "none" : string.Join(", ", with.ToArray()))} of {checkedCount}");
    }

    private static void NoteSpawns()
    {
        SpawnSystem system = null;
        if (SpawnSystem.m_instances != null && SpawnSystem.m_instances.Count > 0)
        {
            system = SpawnSystem.m_instances[0];
        }
        if (system == null && ZoneSystem.instance != null && ZoneSystem.instance.m_zoneCtrlPrefab != null)
        {
            system = ZoneSystem.instance.m_zoneCtrlPrefab.GetComponent<SpawnSystem>();
        }
        if (system == null)
        {
            SelfTest.Note(RulesName, "world spawn tables: no spawn system to read");
            return;
        }
        var where = new Dictionary<string, List<string>>();
        var hunters = new List<string>();
        foreach (var list in system.m_spawnLists)
        {
            if (list == null)
            {
                continue;
            }
            foreach (var d in list.m_spawners)
            {
                if (d == null || d.m_prefab == null || !d.m_enabled)
                {
                    continue;
                }
                var text = d.m_biome + (string.IsNullOrEmpty(d.m_requiredGlobalKey) ? "" : " after " + d.m_requiredGlobalKey)
                                     + (d.m_spawnAtDay ? "" : ", night only");
                if (!where.TryGetValue(d.m_prefab.name, out var places))
                {
                    places = new List<string>();
                    where[d.m_prefab.name] = places;
                }
                places.Add(text);
                if (d.m_huntPlayer)
                {
                    hunters.Add($"{d.m_prefab.name} ({text})");
                }
            }
        }
        var sb = new StringBuilder();
        foreach (var name in new[] { "Hen", "Skeleton_Meadows", "Skeleton_Mountains", "Bat_Swamp", "BlobFrost", "Leech_cave", "Ghost" })
        {
            sb.Append(sb.Length > 0 ? "; " : "").Append(name).Append(": ")
                .Append(where.TryGetValue(name, out var places) ? string.Join(" / ", places.ToArray()) : "not in the world spawn table");
        }
        SelfTest.Note(RulesName, "world spawns of the variants placed by name only: " + sb);
        SelfTest.Note(RulesName, "world spawns that hunt players (night hunters): " + string.Join("; ", hunters.ToArray()));
    }

    private static void NoteRaid()
    {
        var events = RandEventSystem.instance;
        if (events == null)
        {
            SelfTest.Note(RulesName, "raids: no RandEventSystem");
            return;
        }
        foreach (var ev in events.m_events)
        {
            if (ev == null || ev.m_name != "army_eikthyr")
            {
                continue;
            }
            var spawns = new List<string>();
            foreach (var d in ev.m_spawn)
            {
                if (d != null && d.m_prefab != null)
                {
                    spawns.Add(d.m_prefab.name);
                }
            }
            SelfTest.Note(RulesName, $"raid army_eikthyr: enabled {ev.m_enabled}, sends {string.Join(", ", spawns.ToArray())}; "
                                     + $"needs world keys [{string.Join(", ", ev.m_requiredGlobalKeys.ToArray())}], "
                                     + $"not after [{string.Join(", ", ev.m_notRequiredGlobalKeys.ToArray())}], "
                                     + $"player keys any [{string.Join(", ", ev.m_altRequiredPlayerKeysAny.ToArray())}], "
                                     + $"not with player keys [{string.Join(", ", ev.m_altNotRequiredPlayerKeys.ToArray())}]");
            return;
        }
        SelfTest.Note(RulesName, "raid army_eikthyr: not in this game");
    }

    private static string Damage(HitData.DamageTypes d)
    {
        var sb = new StringBuilder();

        void Add(string type, float value)
        {
            if (value > 0f)
            {
                sb.Append(sb.Length > 0 ? " " : "").Append(type).Append(' ').Append(F(value));
            }
        }

        Add("damage", d.m_damage);
        Add("blunt", d.m_blunt);
        Add("slash", d.m_slash);
        Add("pierce", d.m_pierce);
        Add("chop", d.m_chop);
        Add("pickaxe", d.m_pickaxe);
        Add("fire", d.m_fire);
        Add("frost", d.m_frost);
        Add("lightning", d.m_lightning);
        Add("poison", d.m_poison);
        Add("spirit", d.m_spirit);
        return sb.Length > 0 ? sb.ToString() : "none";
    }

    private static void NoteBombs()
    {
        var db = ObjectDB.instance;
        var sb = new StringBuilder();
        foreach (var name in new[] { "BombOoze", "BombBile", "BombLava", "BombSmoke" })
        {
            var prefab = db != null ? db.GetItemPrefab(name) : null;
            var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            sb.Append(sb.Length > 0 ? "; " : "").Append(name).Append(": ");
            if (drop == null)
            {
                sb.Append("not in the game");
                continue;
            }
            var attack = drop.m_itemData.m_shared.m_attack;
            var projectilePrefab = attack != null ? attack.m_attackProjectile : null;
            var projectile = projectilePrefab != null ? projectilePrefab.GetComponent<Projectile>() : null;
            var aoe = projectile != null && projectile.m_spawnOnHit != null ? projectile.m_spawnOnHit.GetComponent<Aoe>() : null;
            sb.Append("hit ").Append(projectile != null ? Damage(projectile.m_damage) : "no projectile")
                .Append(", area ").Append(aoe != null ? Damage(aoe.m_damage) : "none");
        }
        SelfTest.Note(RulesName, "player bombs (for T12, a fire- or poison-only hit): " + sb);
    }

    private static void NoteTargets()
    {
        var scene = ZNetScene.instance;
        var names = new List<string>();
        foreach (var prefab in scene.m_prefabs)
        {
            if (prefab == null)
            {
                continue;
            }
            var target = prefab.GetComponent<StaticTarget>();
            if (target != null && target.m_primaryTarget && prefab.GetComponent<Piece>() != null)
            {
                names.Add(prefab.name);
            }
        }
        names.Sort(StringComparer.Ordinal);
        var shown = names.Count > 40 ? names.GetRange(0, 40) : names;
        SelfTest.Note(RulesName, $"pieces that are priority building targets (decision 11): {names.Count}: "
                                 + string.Join(", ", shown.ToArray()) + (names.Count > 40 ? ", ..." : ""));
    }

    // ================================================================ morale.publish

    private static Standing.Computed ExpectedStanding()
    {
        var game = Game.instance;
        var profile = game != null ? game.GetPlayerProfile() : null;
        var rules = ServerRules.Current;
        var forced = Plugin.ForceBossRank != null && Plugin.ForceBossRank.Value >= 0 ? Plugin.ForceBossRank.Value : -1;
        return Standing.Compute(profile != null ? Standing.LifetimeKills(profile) : null, BossLadder.Get(), rules,
            PrefabTokens.Get(rules), forced);
    }

    private static IEnumerator RunPublish()
    {
        var c = new Checks(PublishName);
        var player = Player.m_localPlayer;
        if (player == null || player.m_nview == null || !player.m_nview.IsValid() || Game.instance == null)
        {
            SelfTest.Fail(PublishName, "no world or no local player");
            yield break;
        }
        var extra = "";
        try
        {
            ResetHooks();
            var zdo = player.m_nview.GetZDO();
            Standing.PublishNow();
            var expected = ExpectedStanding();
            var bytes = zdo.GetByteArray(CreatureKeys.Standing);
            c.Check(bytes != null && CreatureKeys.SameBytes(bytes, Standing.Encode(expected.Published)),
                "after PublishNow the player ZDO holds Standing.Compute(profile)");
            c.Check(Standing.TryDecode(bytes, out var data) && data != null && data.BossRank == expected.BossRank,
                "the published standing decodes to the computed boss rank");
            var cached = StandingCache.Get(player);
            c.Check(cached != null && cached.BossRank == expected.BossRank, "StandingCache reads the own standing at once");
            extra = $"; this character: boss rank {expected.BossRank}{(expected.Forced ? " (forced)" : "")}, {expected.Published.Count} kill bonus(es) listed";
            yield return null;

            Standing.Withdraw();
            var gone = zdo.GetByteArray(CreatureKeys.Standing);
            c.Check(gone != null && gone.Length == 3 && gone[1] == Standing.NoRank && Standing.TryDecode(gone, out var none) && none == null,
                "Withdraw writes [1,255,0] = no standing");
            c.Check(StandingCache.Get(player) == null, "after Withdraw the cache says no standing at once");
            yield return null;

            Standing.PublishNow();
            c.Check(CreatureKeys.SameBytes(zdo.GetByteArray(CreatureKeys.Standing), Standing.Encode(ExpectedStanding().Published)),
                "publishing again restores the standing");
            c.Check(StandingCache.Get(player) != null, "the cache sees the standing again");

            var o = new Standing.Override(3);
            o.Kills["$enemy_greydwarf"] = 150f;
            Standing.TestOverride = o;
            c.Check(Standing.TryDecode(zdo.GetByteArray(CreatureKeys.Standing), out var forced) && forced != null
                    && forced.BossRank == 3 && forced.BonusFor(Hash("$enemy_greydwarf")) == 1,
                "the test override (rank 3, 150 Greydwarf kills: listed one boss early) goes through the normal publish path");
            Standing.TestOverride = null;
            c.Check(CreatureKeys.SameBytes(zdo.GetByteArray(CreatureKeys.Standing), Standing.Encode(ExpectedStanding().Published)),
                "clearing the override publishes the real standing again");

            yield return RankMessageSteps(c);
            c.Report(extra);
        }
        finally
        {
            ResetHooks();
        }
    }

    // E5: a credited kill that raise the boss rank. Vanilla's boss death message (BaseAI.OnDeath -> MessageAll Center,
    // same Character.OnDeath call, right after the kill credit) replace the center text at once, no queue: ours must
    // wait until it faded (4 s), then show. Me do what vanilla do right after the credit, with the real boss line.
    private static IEnumerator RankMessageSteps(Checks c)
    {
        var hud = MessageHud.instance;
        if (!c.Check(hud != null && hud.m_messageCenterText != null, "E5: MessageHud and its center text exist"))
        {
            yield break;
        }
        if (Hud.IsUserHidden())
        {
            SelfTest.Note(PublishName, "E5: the HUD is hidden (messages are not shown), rank-up message steps skipped");
            yield break;
        }
        var w = new Waiter();
        ServerRules.TestRules = NewRules(null); // default ranks: the lowest home rank is 3 (the Meadows)
        Standing.TestMessages = true;
        Standing.TestOverride = new Standing.Override(0);
        Standing.TestKill(new Standing.Override(1));
        c.Check(!Standing.MessagePending,
            "E5: a kill that raises the boss rank to 1 (Eikthyr) shows no rank-up message: nothing is afraid of a rank 1 player with the defaults (two bosses ahead)");
        var before = hud.m_messageCenterText.text;
        Standing.TestKill(new Standing.Override(3));
        var due = Standing.RankMessageAt - Time.time;
        c.Check(Standing.MessagePending && hud.m_messageCenterText.text == before,
            "E5: a kill that raises the boss rank to 3 (the Meadows creatures now afraid) does not show its center message at once (vanilla's boss death message would replace it)");
        c.Check(due >= 4f && due <= Standing.RankMessageDelay + 0.01f,
            $"E5: the rank-up message is due after the 4 s fade of vanilla's boss death message (due in {F(due)} s)");
        var scene = ZNetScene.instance;
        var boss = scene != null ? scene.GetPrefab("Eikthyr") : null;
        var bossAi = boss != null ? boss.GetComponent<BaseAI>() : null;
        var line = bossAi != null && !string.IsNullOrEmpty(bossAi.m_deathMessage) ? bossAi.m_deathMessage : "$enemy_eikthyr_deathmessage";
        hud.ShowMessage(MessageHud.MessageType.Center, line);
        yield return Until(() => !Standing.MessagePending, Standing.RankMessageDelay + 1f, null, w);
        var now = hud.m_messageCenterText.text;
        c.Check(w.Met && now == Standing.RankUpText,
            $"E5: after vanilla's boss death line ({line}) the center text becomes the rank-up message (now \"{now}\", waited {F(w.Took)} s)");
        Standing.TestMessages = null;
    }

    // ================================================================ morale.calm

    // Longest me wait for an afraid creature to get 1 m farther from the player (a few 2 s flee point picks).
    private const float RunWait = 10f;

    private static IEnumerator RunCalm()
    {
        var c = new Checks(CalmName);
        Stage s = null;
        var w = new Waiter();
        try
        {
            s = Stage.Begin(CalmName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var rules = NewRules(null);
            ServerRules.TestRules = rules;
            Standing.TestOverride = new Standing.Override(0);

            // ----- rank 0: normal game, it go for player -----
            var home = s.Spot(player.transform.position, s.Forward, 4f, true);
            var gd = s.Spawn("Greydwarf", home, player.transform.position - home);
            if (gd == null)
            {
                c.Check(false, "could not spawn a Greydwarf");
                c.Report();
                yield break;
            }
            var ch = gd.m_character;
            yield return Until(() => gd.m_targetCreature == player && gd.HaveTarget() && player.IsSensed(), 4f, s.Noise, w);
            c.Check(w.Met, $"rank 0: the Greydwarf must target and sense the (noisy) player within 4 s; target {Who(gd.m_targetCreature)}, "
                           + $"have target {gd.HaveTarget()}, player sensed {player.IsSensed()}");
            c.Check(Attitudes.Judge(gd, player) == Attitude.Hostile, $"rank 0: Judge must say Hostile, says {Attitudes.Judge(gd, player)}");

            // ----- rank 4 (Moder): afraid, drop the player and run -----
            Standing.TestOverride = new Standing.Override(4);
            yield return Until(() => FearOf(gd) == player && gd.m_targetCreature == null && gd.IsAlerted(), 3f, s.Noise, w);
            c.Check(w.Met, $"rank 4: the now afraid Greydwarf {F(Dist(gd, player))} m away must drop the player and run from them within 3 s "
                           + $"(runs from {Who(FearOf(gd))}, target {Who(gd.m_targetCreature)}, alerted {gd.IsAlerted()})");
            if (w.Met)
            {
                SelfTest.Note(CalmName, $"rank 4: running from the player {F(w.Took)} s after the standing changed");
            }
            c.Check(Attitudes.Judge(gd, player) == Attitude.Afraid, $"rank 4: Judge must say Afraid, says {Attitudes.Judge(gd, player)}");
            var startDist = Dist(gd, player);
            var startHurt = gd.m_timeSinceHurt;
            var startTimer = gd.m_updateTargetTimer;
            var picked = false;
            var dropped = false;
            var farthest = startDist;
            var hadPath = false;
            var inAttack = false;
            // Me watch the run every frame: farthest it got, a navmesh path found by the flee itself (not the old chase
            // path result: only after the first flee frame, which reset the path timers), an attack swing still going.
            Action watchRun = () =>
            {
                s.Noise();
                farthest = Mathf.Max(farthest, Dist(gd, player));
                hadPath |= CreatureState.TryGet(gd, out var runState) && !runState.FirstFleeFrame && gd.FoundPath();
                inAttack |= ch.InAttack();
            };
            yield return Wait(2.5f, () =>
            {
                watchRun();
                picked |= gd.m_targetCreature != null;
                dropped |= !gd.IsAlerted();
            });
            var ranDist = Dist(gd, player);
            c.Check(!picked && !gd.HaveTarget() && gd.IsAlerted() && FearOf(gd) == player,
                $"rank 4: while running it has no target and stays alerted (picked a target {picked}, runs from {Who(FearOf(gd))})");
            c.Check(!dropped, "rank 4: the alert stays on every frame of the run (raised once: one alert sound, no flicker of the icon)");
            c.Check(ZdoAlert(gd), "rank 4: running = the ZDO alert every game reads is on (every player sees the vanilla alert icon)");
            c.Check(!player.IsSensed(), "rank 4: a running creature does not make the player sensed (no combat music, bed and rest work)");
            c.Check(gd.m_timeSinceHurt - startHurt >= 2f && Mathf.Approximately(gd.m_updateTargetTimer, startTimer),
                $"rank 4: fear frames run the base update (hurt timer grew {F(gd.m_timeSinceHurt - startHurt)} s) and skip vanilla "
                + $"(target timer {F(startTimer)} -> {F(gd.m_updateTargetTimer)})");

            // The distance: me wait on it (at most RunWait s in all), not on a clock. Vanilla Flee pick a new point only
            // every 2 s and MoveTo stand still while that point has no path: first creature of its kind in a fresh
            // world = its navmesh tiles are not built yet (built on demand), and the test spot is the start temple
            // (boss stones right behind it). Both runs so far: under 1.3 m in the first 2.5 s, path found by then.
            var runTook = 2.5f;
            if (farthest <= startDist + 1f)
            {
                yield return Until(() => farthest > startDist + 1f, RunWait - 2.5f, watchRun, w);
                runTook += w.Took;
            }
            var fleePoint = Vector3.Distance(gd.m_fleeTarget, player.transform.position);
            if (farthest <= startDist + 1f && !hadPath)
            {
                // Vanilla Flee stands still when it finds no navmesh path (fresh world, rocks): terrain, not the mod.
                SelfTest.Note(CalmName, $"rank 4: no navmesh path to run on here in {F(runTook)} s ({F(startDist)} m -> {F(farthest)} m at most, "
                                        + $"flee point {F(fleePoint)} m from the player); the running distance is not checked");
            }
            else
            {
                c.Check(farthest > startDist + 1f, $"rank 4: it runs away from the player ({F(startDist)} m -> {F(farthest)} m at most within "
                                                   + $"{F(runTook)} s; {F(ranDist)} m after 2.5 s; flee point {F(fleePoint)} m from the player, "
                                                   + $"a path found {hadPath}, in an attack swing meanwhile {inAttack})");
            }
            SelfTest.Note(CalmName, $"rank 4: {F(ranDist - startDist)} m farther after 2.5 s, {F(farthest - startDist)} m farther after {F(runTook)} s "
                                    + $"(path found {hadPath}, in an attack swing meanwhile {inAttack})");

            // Pinned 4 m in front, facing the player: it sees them and still does not sense them (never a target).
            Action holdHome = () =>
            {
                s.Noise();
                Stage.Place(ch, home, player.transform.position - home);
            };
            holdHome();
            var canSense = gd.CanSenseTarget(player);
            var canSee = gd.CanSeeTarget(player);
            var canHear = gd.CanHearTarget(player);
            c.Check(!canSense, "rank 4: CanSenseTarget(player) must be false for an afraid creature");
            c.Check(canSee, $"rank 4: the afraid Greydwarf facing the player 4 m away must still see them (CanSeeTarget; hears them: {canHear})");
            yield return Wait(1.5f, holdHome);
            c.Check(!player.IsSensed() && gd.m_targetCreature == null && FearOf(gd) == player,
                $"rank 4: held 4 m from the noisy player it keeps running from them, never targets them (target {Who(gd.m_targetCreature)})");

            // ----- vanilla alert icon on its plate while it runs (no label of the mod's own) -----
            yield return AlertIconSteps(s, c, gd, home);

            // ----- beyond FearRange + margin: it calms (vanilla unalerted look), and stays calm with the noisy player 20 m away -----
            var farDistance = rules.FearRange + Fear.Margin + 4f;
            var far = s.Spot(player.transform.position, home - player.transform.position, farDistance, false);
            Action holdFar = () =>
            {
                s.Noise();
                Stage.Place(ch, far, player.transform.position - far);
            };
            yield return Until(() => FearOf(gd) == null && !gd.IsAlerted() && gd.m_targetCreature == null, 3f, holdFar, w);
            c.Check(w.Met, $"{F(farDistance)} m away (FearRange {F(rules.FearRange)} + {F(Fear.Margin)} m): it stops running and calms within 3 s "
                           + $"(runs from {Who(FearOf(gd))}, alerted {gd.IsAlerted()}, {F(Dist(gd, player))} m)");
            c.Check(!ZdoAlert(gd), "calmed: the ZDO alert every game reads is off again (no alert icon for anyone)");
            yield return CalmIconSteps(s, c, gd, holdFar);
            yield return Wait(2f, holdFar);
            c.Check(FearOf(gd) == null && !gd.IsAlerted() && gd.m_targetCreature == null,
                "the noisy player 20 m away: it neither runs nor comes for them");

            // ----- a silent player behind it: not noticed; turned toward him: it runs -----
            var behind = Stage.Ground(player.transform.position + (home - player.transform.position).normalized * 5f);
            Action holdAway = () =>
            {
                s.Silence();
                Stage.Place(ch, behind, behind - player.transform.position);
            };
            yield return Wait(2.5f, holdAway);
            c.Check(FearOf(gd) == null && !gd.IsAlerted() && !gd.CanSeeTarget(player) && !gd.CanHearTarget(player),
                $"a silent player 5 m behind an afraid Greydwarf facing away: it does not notice and does not run (runs from {Who(FearOf(gd))}, "
                + $"sees {gd.CanSeeTarget(player)}, hears {gd.CanHearTarget(player)})");
            Action holdFacing = () =>
            {
                s.Silence();
                Stage.Place(ch, behind, player.transform.position - behind);
            };
            yield return Until(() => FearOf(gd) == player, 2.5f, holdFacing, w);
            c.Check(w.Met, $"turned toward the silent player (it sees them): it runs within 2.5 s (sees {gd.CanSeeTarget(player)})");

            // ----- 2 stars: nerve 6 > rank 4 -----
            ch.SetLevel(3);
            yield return Until(() => gd.m_targetCreature == player, 5f, holdHome, w);
            c.Check(w.Met, $"2-star Greydwarf at rank 4 (nerve 6) must stop running and target the player again within 5 s "
                           + $"(runs from {Who(FearOf(gd))}, target {Who(gd.m_targetCreature)})");
            c.Check(Attitudes.Judge(gd, player) == Attitude.Hostile, "2-star Greydwarf at rank 4: Judge must say Hostile");
            s.Destroy(gd);

            // ----- burning: startled with no target, then calm down -----
            yield return BurnSteps(s, c, rules);

            // ----- night hunter -----
            yield return HunterSteps(s, c, rules);

            // ----- biome by spawn place -----
            yield return SpawnBiomeSteps(s, c, home);

            c.Report();
        }
        finally
        {
            Finish(s);
        }
    }

    // Running afraid Greydwarf held 4 m in front: the game's own plate shows its alert icon (EnemyHud.UpdateHuds from
    // IsAlerted, which non-owners read from the ZDO) and the aware icon off. Screenshot: nothing else on the plate.
    private static IEnumerator AlertIconSteps(Stage s, Checks c, MonsterAI gd, Vector3 home)
    {
        var player = s.Player;
        var ch = gd.m_character;
        var hud = EnemyHud.instance;
        if (!c.Check(hud != null, "alert icon: no EnemyHud"))
        {
            yield break;
        }
        var w = new Waiter();
        var plate = new PlateRef();
        Action hold = () =>
        {
            s.Noise();
            Stage.Place(ch, home, player.transform.position - home);
        };
        yield return Until(() => hud.m_huds.TryGetValue(ch, out plate.Data) && plate.Data != null && plate.Data.m_gui != null, 2f, hold, w);
        if (!c.Check(w.Met, "alert icon: the game must make a plate for the Greydwarf 4 m away"))
        {
            yield break;
        }
        // Me do what crosshair on it do (EnemyHud.UpdateHuds: hover timer 0 = plate shown 60 s).
        yield return Until(() => IconsShow(plate.Data, true), 1.5f, () =>
        {
            hold();
            plate.Data.m_hoverTimer = 0f;
        }, w);
        c.Check(w.Met && FearOf(gd) == player,
            $"alert icon: the running Greydwarf's shown plate has the vanilla alert icon on and the aware icon off ({DescribeIcons(plate.Data)}, runs from {Who(FearOf(gd))})");
        if (w.Met)
        {
            SelfTest.Screenshot(CalmName, "alert-icon");
            yield return null;
            yield return null;
        }
    }

    // Calmed Greydwarf (held 20 m away): its plate icons back to vanilla's unalerted look. Plate still there (within
    // m_maxShowDistance, 30 m): icons are set while the crosshair time runs, on screen or not.
    private static IEnumerator CalmIconSteps(Stage s, Checks c, MonsterAI gd, Action holdFar)
    {
        var hud = EnemyHud.instance;
        var ch = gd.m_character;
        var plate = new PlateRef();
        if (hud == null || !hud.m_huds.TryGetValue(ch, out plate.Data) || plate.Data == null || plate.Data.m_gui == null)
        {
            SelfTest.Note(CalmName, $"calm icon: no plate for the Greydwarf {F(Dist(gd, s.Player))} m away; only the ZDO alert is checked");
            yield break;
        }
        var w = new Waiter();
        yield return Until(() => IconsOff(plate.Data), 1f, () =>
        {
            holdFar();
            plate.Data.m_hoverTimer = 0f;
        }, w);
        c.Check(w.Met, $"calmed: its plate shows no alert icon and no aware icon ({DescribeIcons(plate.Data)})");
        if (w.Met && plate.Data.m_gui != null && plate.Data.m_gui.activeSelf)
        {
            SelfTest.Screenshot(CalmName, "calm-no-icon");
            yield return null;
            yield return null;
        }
    }

    // Burn at rank 4, the Greydwarf held beyond FearRange but inside its view range (calming down needs a player near).
    private static IEnumerator BurnSteps(Stage s, Checks c, MoraleRules rules)
    {
        var player = s.Player;
        var w = new Waiter();
        var distance = rules.FearRange + Fear.Margin;
        var spot = s.Spot(player.transform.position, s.Forward, distance, false);
        var gb = s.Spawn("Greydwarf", spot, player.transform.position - spot);
        if (!c.Check(gb != null, "burn: could not spawn a Greydwarf"))
        {
            yield break;
        }
        var ch = gb.m_character;
        Action hold = () =>
        {
            s.Noise();
            Stage.Place(ch, spot, player.transform.position - spot);
        };
        yield return Wait(1.2f, hold);
        c.Check(!gb.IsAlerted() && gb.m_targetCreature == null && FearOf(gb) == null,
            $"burn setup: a fresh Greydwarf at rank 4, {F(Dist(gb, player))} m away (beyond FearRange), must be afraid, calm and unalerted "
            + $"(its view range {F(gb.m_viewRange)} m)");
        var db = ObjectDB.instance;
        var burning = db != null ? db.GetStatusEffect(SEMan.s_statusEffectBurning) : null;
        var ttl = burning != null ? burning.m_ttl : 5f;
        SelfTest.Note(CalmName, $"burning lasts {F(ttl)} s{(burning == null ? " (not found, 5 s assumed)" : "")}; "
                                + $"Greydwarf fire modifier {ch.m_damageModifiers.m_fire}");
        ch.AddFireDamage(5f, 0);
        var seman = ch.GetSEMan();
        c.Check(seman.HaveStatusEffect(SEMan.s_statusEffectBurning), "burn setup: the Greydwarf must be burning");
        yield return Until(() => gb.IsAlerted(), 2f, hold, w);
        c.Check(w.Met && gb.m_targetCreature == null,
            $"burning at rank 4: must be alerted with no target (alerted {gb.IsAlerted()}, target {Who(gb.m_targetCreature)})");
        var picked = false;
        yield return Until(() => !seman.HaveStatusEffect(SEMan.s_statusEffectBurning), ttl + 3f, () =>
        {
            hold();
            if (gb.m_targetCreature != null)
            {
                picked = true;
            }
        }, w);
        c.Check(w.Met, "burn: the burning must end");
        c.Check(!picked, "burn: the burning afraid Greydwarf picked a target (the noisy player?)");
        yield return Until(() => !gb.IsAlerted(), 7f, hold, w);
        c.Check(w.Met, $"burn: the startled afraid Greydwarf must calm down within 7 s after the burn ends (last hurt {F(gb.m_timeSinceHurt)} s ago, "
                       + $"{F(Dist(gb, player))} m from the player)");
        if (w.Met)
        {
            SelfTest.Note(CalmName, $"burn: calmed down {F(w.Took)} s after the burn ended");
        }
        c.Check(gb.m_targetCreature == null, "burn: no target after calming down");
        s.Destroy(gb);
    }

    private static IEnumerator HunterSteps(Stage s, Checks c, MoraleRules rules)
    {
        var player = s.Player;
        var w = new Waiter();
        Standing.TestOverride = new Standing.Override(8);
        var farDistance = rules.FearRange + Fear.Margin;
        var far = s.Spot(player.transform.position, s.Forward, farDistance, false);
        var gh = s.Spawn("Greydwarf", far, player.transform.position - far);
        if (!c.Check(gh != null, "hunter: could not spawn a Greydwarf"))
        {
            yield break;
        }
        var ch = gh.m_character;
        var at = far;
        Action hold = () =>
        {
            s.Noise();
            Stage.Place(ch, at, player.transform.position - at);
        };
        gh.SetHuntPlayer(true);
        c.Check(gh.m_nview.GetZDO().GetBool(ZDOVars.s_huntPlayer), "hunter setup: the hunt flag must be in the ZDO");
        yield return Wait(3f, hold);
        c.Check(gh.m_targetCreature == null && !gh.HaveTarget(),
            $"hunter at rank 8: the night hunter must leave the outclassing player alone (target {Who(gh.m_targetCreature)})");
        CreatureState.TryGet(gh, out var state);
        c.Check(state != null && state.NightHunter && state.HuntStandDown,
            $"hunter at rank 8: must count as a night hunter standing down (night hunter {state != null && state.NightHunter}, "
            + $"stand-down {state != null && state.HuntStandDown})");
        c.Check(!gh.HuntPlayer(), "hunter at rank 8: HuntPlayer must say false while it stands down");
        yield return Wait(2f, hold);
        c.Check(!gh.IsAlerted() && gh.m_targetCreature == null && FearOf(gh) == null,
            $"hunter at rank 8, {F(Dist(gh, player))} m away: not alerted 2 s later (no forced hunt alert, too far to run)");

        // Close: afraid like the others, it runs.
        at = s.Spot(player.transform.position, s.Forward, 6f, true);
        yield return Until(() => FearOf(gh) == player && gh.m_targetCreature == null, 2.5f, hold, w);
        c.Check(w.Met, $"hunter at rank 8, moved 6 m from the player: the standing-down night hunter runs from them within 2.5 s "
                       + $"(runs from {Who(FearOf(gh))}, target {Who(gh.m_targetCreature)})");

        ServerRules.TestRules = NewRules(r => r.NightHuntersCanBeAfraid = false);
        c.Check(gh.HuntPlayer(), "NightHuntersCanBeAfraid off: HuntPlayer is true at once (the rule change ends the stand-down, not the next check)");
        yield return Until(() => gh.m_targetCreature == player, 8f, hold, w);
        c.Check(w.Met, "NightHuntersCanBeAfraid off: the night hunter must stop running and hunt the player again within 8 s");
        c.Check(gh.HuntPlayer(), "NightHuntersCanBeAfraid off: HuntPlayer is true again");
        ServerRules.TestRules = rules;
        s.Destroy(gh);
    }

    // Design 2.3, G2: the rank follows the biome the creature spawned in (its spawn point, as vanilla BaseAI.Awake read
    // it from the ZDO), never easier than its home biome. Spawn points moved on purpose to real world points.
    private static IEnumerator SpawnBiomeSteps(Stage s, Checks c, Vector3 home)
    {
        var player = s.Player;
        var plains = BiomePoint(Heightmap.Biome.Plains);
        var mountain = BiomePoint(Heightmap.Biome.Mountain);
        var deepNorth = BiomePoint(Heightmap.Biome.DeepNorth);
        var spot = s.Spot(player.transform.position, s.Forward, 25f, false);
        var sk = s.Spawn("Skeleton", spot, player.transform.position - spot);
        var gd = s.Spawn("Greydwarf", Stage.Ground(spot + s.Right * 3f), player.transform.position - spot);
        if (!c.Check(sk != null && gd != null, "spawn biome: could not spawn a Skeleton and a Greydwarf"))
        {
            yield break;
        }
        yield return null;
        var here = Biomes.LevelAt(spot);
        var skState = CreatureState.Get(sk);
        var gdState = CreatureState.Get(gd);

        Attitude At(MonsterAI ai, int rank)
        {
            Standing.TestOverride = new Standing.Override(rank);
            return Attitudes.Judge(ai, player);
        }

        void Move(MonsterAI ai, CreatureState st, Vector3 point)
        {
            ai.m_spawnPoint = point;
            ai.m_nview.GetZDO().Set(ZDOVars.s_spawnPoint, point);
            st.ForgetSpawnLevel();
        }

        skState.ForgetFacts();
        skState.EnsureFacts(ServerRules.Current);
        var hereRank = Math.Max(2, here) + 2;
        c.Check(skState.SpawnLevel == here && skState.HomeLevel == 2 && skState.Rank == hereRank,
            $"a Skeleton spawned here ({Biomes.Name(here)}): spawn level {skState.SpawnLevel}, home level {skState.HomeLevel}, rank {skState.Rank} "
            + $"(expected {hereRank}: the harder of home and spawn biome + 2)");
        c.Check(At(sk, hereRank - 1) == Attitude.Hostile && At(sk, hereRank) == Attitude.Afraid,
            $"a Black Forest Skeleton spawned here: hostile at rank {hereRank - 1}, afraid at rank {hereRank}");
        SelfTest.Note(CalmName, $"spawn biome: the test spot is in the {Biomes.Name(here)}; Plains point {plains}, Mountains point {mountain}, Deep North point {deepNorth}");

        if (plains != Vector3.zero)
        {
            Move(sk, skState, plains);
            c.Check(At(sk, 6) == Attitude.Hostile && At(sk, 7) == Attitude.Afraid && skState.SpawnLevel == 5 && skState.Rank == 7,
                $"a Skeleton whose spawn point is in the Plains counts as a Plains creature: hostile at rank 6, afraid at rank 7 "
                + $"(spawn level {skState.SpawnLevel}, rank {skState.Rank})");
        }
        if (mountain != Vector3.zero)
        {
            Move(sk, skState, mountain);
            c.Check(At(sk, 5) == Attitude.Hostile && At(sk, 6) == Attitude.Afraid && skState.SpawnLevel == 4,
                $"a Skeleton whose spawn point is in the Mountains: hostile at rank 5, afraid at rank 6 (spawn level {skState.SpawnLevel})");
        }
        if (deepNorth != Vector3.zero)
        {
            Move(gd, gdState, deepNorth);
            c.Check(At(gd, 8) == Attitude.Hostile && gdState.SpawnLevel == 8 && gdState.Rank == 10,
                $"a Greydwarf whose spawn point is in the Deep North still attacks a Kall-killer (rank 10; spawn level {gdState.SpawnLevel}, rank {gdState.Rank})");
        }
        var meadows = BiomePoint(Heightmap.Biome.Meadows);
        if (meadows != Vector3.zero)
        {
            Move(gd, gdState, meadows);
            c.Check(At(gd, 3) == Attitude.Hostile && At(gd, 4) == Attitude.Afraid && gdState.SpawnLevel == 1 && gdState.Rank == 4,
                "a Greydwarf that spawned in the Meadows stays a Black Forest Greydwarf (its home is harder): afraid at rank 4 only");
        }
        var sky = (plains != Vector3.zero ? plains : home) + Vector3.up * 5000f;
        Move(sk, skState, sky);
        skState.EnsureFacts(ServerRules.Current);
        c.Check(skState.SpawnLevel == Biomes.LevelAt(sky) && skState.SpawnLevel != Biomes.Unknown,
            $"a spawn point inside a dungeon (up in the sky) uses the biome of its zone's entrance (spawn level {skState.SpawnLevel})");
        s.Destroy(sk);
        s.Destroy(gd);
    }

    // ================================================================ morale.provoke

    private static IEnumerator RunProvoke()
    {
        var c = new Checks(ProvokeName);
        Stage s = null;
        var w = new Waiter();
        try
        {
            s = Stage.Begin(ProvokeName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            ServerRules.TestRules = NewRules(r =>
            {
                r.ProvokedSeconds = 5f;
                r.NearMissRange = 4f;
                r.CorneredRange = 3f;
                r.CorneredSeconds = 1f;
                r.FearRange = 0f; // fear off: these steps test what a hit or an impact does, not the running
            });
            Standing.TestOverride = new Standing.Override(4);
            var pid = player.GetPlayerID();
            var spot = s.Spot(player.transform.position, s.Forward, 4f, true);

            // ----- a hit (1 slash) -----
            var a = s.Spawn("Greydwarf", spot, player.transform.position - spot);
            if (!c.Check(a != null, "could not spawn a Greydwarf"))
            {
                c.Report();
                yield break;
            }
            yield return Wait(1f, null);
            c.Check(Attitudes.Judge(a, player) == Attitude.Afraid && !a.IsAlerted() && a.m_targetCreature == null,
                "hit setup: a fresh Greydwarf at rank 4 must be afraid");
            Hit(a.m_character, player, 1f, 0f, 1f);
            yield return Until(() => Provoked(a, player) && a.m_targetCreature == player && a.IsAlerted(), 1f, null, w);
            c.Check(w.Met, $"hit: provoked, target and alerted within 1 s (provoked {Provoked(a, player)}, target {Who(a.m_targetCreature)}, alerted {a.IsAlerted()})");
            var ok = CreatureKeys.TryGetUntil(a.m_nview.GetZDO().GetByteArray(CreatureKeys.Provokers), pid, out var until);
            var left = (until - Attitudes.Now()) / (double)TimeSpan.TicksPerSecond;
            c.Check(ok && left > 3.5 && left <= 5.01, $"hit: the provokers array holds the player's entry ending ProvokedSeconds (5 s) later; ends in {F(left)} s");
            c.Check(Attitudes.Judge(a, player) == Attitude.Provoked, "hit: Judge says Provoked");
            yield return Wait(7f, null);
            c.Check(a.m_targetCreature == null && !a.IsAlerted() && Attitudes.Judge(a, player) == Attitude.Afraid,
                $"hit: 7 s later (provocation of 5 s over) afraid again, no target, not alerted (fear off in these steps) (target {Who(a.m_targetCreature)}, alerted {a.IsAlerted()})");
            s.Destroy(a);

            // ----- fire only (vanilla strip fire before OnDamaged) -----
            var b = s.Spawn("Greydwarf", spot, player.transform.position - spot);
            yield return Wait(1f, null);
            Hit(b.m_character, player, 0f, 1f, 1f);
            yield return Until(() => Provoked(b, player) && b.m_targetCreature == player, 1f, null, w);
            c.Check(w.Met, $"fire-only hit: provoked and targets the player within 1 s (provoked {Provoked(b, player)}, target {Who(b.m_targetCreature)})");
            s.Destroy(b);

            // ----- projectile land 2 m away (every projectile impact call this; 30 m = thrown spear noise) -----
            var d = s.Spawn("Greydwarf", spot, player.transform.position - spot);
            yield return Wait(1f, null);
            BaseAI.DoProjectileHitNoise(d.transform.position + s.Right * 2f, 30f, player);
            yield return Until(() => Provoked(d, player), 1f, null, w);
            c.Check(w.Met && d.IsAlerted(), $"near miss 2 m away: provoked and alerted (provoked {Provoked(d, player)}, alerted {d.IsAlerted()})");
            s.Destroy(d);

            // ----- projectile land 6 m away (vanilla alert everything within 30 m hit noise) -----
            var e = s.Spawn("Greydwarf", spot, player.transform.position - spot);
            yield return Wait(1f, null);
            BaseAI.DoProjectileHitNoise(e.transform.position + s.Right * 6f, 30f, player);
            yield return Wait(1f, null);
            c.Check(!Provoked(e, player) && !e.IsAlerted() && e.m_targetCreature == null,
                $"near miss 6 m away: the afraid Greydwarf ignores it (provoked {Provoked(e, player)}, alerted {e.IsAlerted()}, target {Who(e.m_targetCreature)})");
            s.Destroy(e);

            // ----- sneak attack while it watch -----
            var f = s.Spawn("Greydwarf", spot, player.transform.position - spot);
            yield return Wait(1f, null);
            Stage.Place(f.m_character, f.transform.position, player.transform.position - f.transform.position);
            c.Check(f.CanSeeTarget(player) && !f.IsAlerted() && Attitudes.Judge(f, player) == Attitude.Afraid,
                $"sneak setup: an afraid unalerted Greydwarf facing the player sees them (sees {f.CanSeeTarget(player)}, alerted {f.IsAlerted()})");
            var before = f.m_character.m_backstabTime;
            Hit(f.m_character, player, 1f, 0f, 3f);
            c.Check(f.m_character.m_backstabTime == before, "sneak attack on an afraid Greydwarf that sees the player: no sneak-attack bonus");
            c.Check(Provoked(f, player), "the watched hit still provokes");
            s.Destroy(f);

            // ----- sneak attack from behind -----
            var g = s.Spawn("Greydwarf", spot, player.transform.position - spot);
            yield return Wait(1f, null);
            Stage.Place(g.m_character, g.transform.position, g.transform.position - player.transform.position);
            c.Check(!g.CanSeeTarget(player) && !g.IsAlerted() && Attitudes.Judge(g, player) == Attitude.Afraid,
                $"sneak setup: an afraid Greydwarf turned away cannot see the player (sees {g.CanSeeTarget(player)}, alerted {g.IsAlerted()})");
            before = g.m_character.m_backstabTime;
            Hit(g.m_character, player, 1f, 0f, 3f);
            c.Check(g.m_character.m_backstabTime != before, "sneak attack on an afraid Greydwarf turned away: the sneak-attack bonus applies");
            s.Destroy(g);

            yield return KnifeSteps(s, c);
            yield return CorneredSteps(s, c);
            c.Report();
        }
        finally
        {
            Finish(s);
        }
    }

    // Real player attack (Humanoid.StartAttack -> vanilla Attack -> Character.Damage): flint knife stab (sneak-attack
    // bonus x6) on afraid Greydwarf that watch player (fear off): no sneak attack, provoked, it target player.
    private static IEnumerator KnifeSteps(Stage s, Checks c)
    {
        var player = s.Player;
        var w = new Waiter();
        var zanim = player.GetZAnim();
        c.Check(zanim != null && zanim.HasParameter("knife_stab", AnimatorControllerParameterType.Trigger),
            "knife: the player animator has the knife_stab trigger (the stab this step uses)");
        var knife = s.EquipWeapon("KnifeFlint");
        if (!c.Check(knife != null && player.GetCurrentWeapon() == knife, "knife: a flint knife added and equipped"))
        {
            yield break;
        }
        c.Check(knife.m_shared.m_backstabBonus > 1f, $"knife: it has a sneak-attack bonus ({F(knife.m_shared.m_backstabBonus)})");
        yield return Wait(0.6f, null);
        var pos = Stage.Ground(player.transform.position + s.Forward * 1.3f);
        var g = s.Spawn("Greydwarf", pos, player.transform.position - pos);
        if (!c.Check(g != null, "knife: could not spawn a Greydwarf"))
        {
            yield break;
        }
        Action hold = () => Stage.Place(g.m_character, pos, player.transform.position - pos);
        yield return Wait(1f, hold);
        hold();
        c.Check(Attitudes.Judge(g, player) == Attitude.Afraid && !g.IsAlerted() && g.CanSeeTarget(player),
            $"knife setup: an afraid unalerted Greydwarf 1.3 m in front sees the player (sees {g.CanSeeTarget(player)}, alerted {g.IsAlerted()})");
        var before = g.m_character.m_backstabTime;
        var health = g.m_character.GetHealth();
        var swings = 0;
        yield return Until(() => g.m_character.GetHealth() < health, 4f, () =>
        {
            hold();
            if (!player.InAttack() && player.StartAttack(null, false))
            {
                swings++;
            }
        }, w);
        c.Check(w.Met, $"knife: a real stab must land within 4 s ({swings} swing(s) started)");
        if (w.Met)
        {
            c.Check(g.m_character.m_backstabTime == before, "knife: a real stab on an afraid Greydwarf that watches the player is no sneak attack");
            c.Check(Provoked(g, player) && g.m_targetCreature == player,
                $"knife: the stab provokes it and it targets the player (provoked {Provoked(g, player)}, target {Who(g.m_targetCreature)})");
        }
        s.Destroy(g);
    }

    // Fear on (design 2.5 piece 2, 2.6): no sneak attack on a running creature; cornered = the player stays within
    // CorneredRange while it runs (here it cannot get away: held at its spot); a player a few metres away never
    // corners it; a silent player right behind it is never run from (sneaking); afraid again after the provocation.
    private static IEnumerator CorneredSteps(Stage s, Checks c)
    {
        var player = s.Player;
        var w = new Waiter();
        var rules = NewRules(r =>
        {
            r.ProvokedSeconds = 5f;
            r.CorneredRange = 3f;
            r.CorneredSeconds = 1f;
        });
        ServerRules.TestRules = rules;

        // ----- a running (alerted) creature that has seen the player: no sneak attack -----
        var from = player.transform.position;
        var rpos = s.Spot(from, s.Forward, 5f, true);
        var run = s.Spawn("Greydwarf", rpos, from - rpos);
        if (!c.Check(run != null, "fear: could not spawn a Greydwarf"))
        {
            yield break;
        }
        yield return Until(() => FearOf(run) == player && run.IsAlerted(), 3f, s.Noise, w);
        c.Check(w.Met, $"fear: a fresh Greydwarf 5 m from the noisy rank 4 player runs from them within 3 s (runs from {Who(FearOf(run))})");
        var before = run.m_character.m_backstabTime;
        Hit(run.m_character, player, 1f, 0f, 3f);
        c.Check(run.m_character.m_backstabTime == before,
            "a sneak-attack hit on a creature running from the player (alerted) is no sneak attack");
        yield return Until(() => FearOf(run) == null && run.m_targetCreature == player, 1f, null, w);
        c.Check(w.Met && Provoked(run, player),
            $"the hit ends its fear at once: it stops running and targets the player (runs from {Who(FearOf(run))}, target {Who(run.m_targetCreature)})");
        s.Destroy(run);

        // ----- cannot get away: held at its spot, the player 1.5 m in front -----
        var cpos = s.Spot(from, s.Forward, 6f, true);
        var h = s.Spawn("Greydwarf", cpos, from - cpos);
        if (!c.Check(h != null, "cornered: could not spawn a Greydwarf"))
        {
            yield break;
        }
        Action holdH = () =>
        {
            s.Noise();
            Stage.Place(h.m_character, cpos, player.transform.position - cpos);
        };
        yield return Wait(1.6f, holdH);
        c.Check(Attitudes.Judge(h, player) == Attitude.Afraid && !Provoked(h, player), "cornered setup: afraid, not provoked");
        s.MovePlayer(cpos + (from - cpos).normalized * 1.5f);
        var ran = false;
        yield return Until(() => Provoked(h, player), 3.5f, () =>
        {
            holdH();
            ran |= FearOf(h) == player;
        }, w);
        var hs = CreatureState.Get(h);
        c.Check(w.Met && ran, $"cornered: held in place with the player 1.5 m away, the afraid Greydwarf runs and is provoked within 3.5 s "
                              + $"(it ran {ran}, runs from {Who(FearOf(h))}, cornered time {F(hs.CorneredTime)} s)");
        if (w.Met)
        {
            SelfTest.Note(ProvokeName, $"cornered after {F(w.Took)} s (CorneredSeconds 1; the fear starts at the once-per-second check, then counts every frame)");
            c.Check(h.m_targetCreature == player && h.IsAlerted() && FearOf(h) == null,
                $"cornered: it stops running, targets the player and is alerted (target {Who(h.m_targetCreature)}, runs from {Who(FearOf(h))})");
        }
        // Provocation over (5 s): afraid again, it runs from the close player again.
        yield return Until(() => !Provoked(h, player) && FearOf(h) == player, rules.ProvokedSeconds + 3f, () =>
        {
            s.Noise();
            if (h != null && h.m_character != null)
            {
                Stage.Place(h.m_character, cpos, player.transform.position - cpos);
            }
            s.MovePlayer(cpos + (from - cpos).normalized * 5f);
        }, w);
        c.Check(w.Met, $"{F(rules.ProvokedSeconds)} s after the provocation it is afraid again and runs from the close player "
                       + $"(provoked {Provoked(h, player)}, runs from {Who(FearOf(h))}, target {Who(h.m_targetCreature)})");
        s.Destroy(h);
        s.MovePlayer(from);

        // ----- the player keeps 5 m away: it runs, never cornered -----
        var kpos = s.Spot(from, s.Forward, 6f, true);
        var k = s.Spawn("Greydwarf", kpos, from - kpos);
        if (!c.Check(k != null, "cornered: could not spawn a second Greydwarf"))
        {
            yield break;
        }
        s.MovePlayer(kpos + (from - kpos).normalized * 5f);
        Action holdK = () =>
        {
            s.Noise();
            Stage.Place(k.m_character, kpos, player.transform.position - kpos);
        };
        var kran = false;
        yield return Wait(4f, () =>
        {
            holdK();
            kran |= FearOf(k) == player;
        });
        c.Check(kran && !Provoked(k, player) && k.m_targetCreature == null,
            $"cornered: the player 5 m away (beyond CorneredRange 3 m): it runs but is not provoked after 4 s (it ran {kran}, provoked {Provoked(k, player)})");
        s.Destroy(k);
        s.MovePlayer(from);

        // ----- a silent player right behind it: not sensed, never run from, never cornering -----
        var jpos = s.Spot(from, s.Forward, 6f, true);
        var j = s.Spawn("Greydwarf", jpos, jpos - from);
        if (!c.Check(j != null, "sneaking: could not spawn a third Greydwarf"))
        {
            yield break;
        }
        yield return Wait(1f, () =>
        {
            s.Silence();
            Stage.Place(j.m_character, jpos, jpos - player.transform.position);
        });
        s.MovePlayer(jpos + (from - jpos).normalized * 1.5f);
        Action holdJ = () =>
        {
            s.Silence();
            Stage.Place(j.m_character, jpos, jpos - player.transform.position);
        };
        yield return Wait(3f, holdJ);
        c.Check(FearOf(j) == null && !Provoked(j, player) && !j.IsAlerted() && !j.CanSeeTarget(player),
            $"sneaking: a silent player 1.5 m behind an afraid Greydwarf is not noticed: it neither runs nor fights after 3 s "
            + $"(runs from {Who(FearOf(j))}, provoked {Provoked(j, player)}, alerted {j.IsAlerted()}, sees {j.CanSeeTarget(player)})");
        s.Destroy(j);
        s.MovePlayer(from);
    }

    // ================================================================ morale.stealth

    private static string StealthRange(Player player)
    {
        var sb = new StringBuilder();
        var pos = player.transform.position;
        foreach (var ai in BaseAI.BaseAIInstances)
        {
            if (ai == null || !BaseAI.IsEnemy(player, ai.m_character))
            {
                continue;
            }
            var distance = Vector3.Distance(pos, ai.transform.position);
            if (distance >= ai.m_viewRange && distance >= 10f)
            {
                continue;
            }
            var monster = ai as MonsterAI;
            sb.Append(sb.Length > 0 ? "; " : "").Append(ai.m_character.m_name).Append(' ').Append(F(distance)).Append(" m, alerted ")
                .Append(ai.IsAlerted()).Append(", ").Append(monster != null ? Attitudes.Judge(monster, player).ToString() : "not MonsterAI");
        }
        return sb.Length > 0 ? sb.ToString() : "no enemy in range";
    }

    private static IEnumerator RunStealth()
    {
        var c = new Checks(StealthName);
        Stage s = null;
        try
        {
            s = Stage.Begin(StealthName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            ServerRules.TestRules = NewRules(null);
            Standing.TestOverride = new Standing.Override(0);
            var pos = s.Spot(player.transform.position, s.Forward, 15f, false);
            var d = s.Spawn("Draugr_sleeping", pos, player.transform.position - pos);
            if (!c.Check(d != null, "could not spawn Draugr_sleeping"))
            {
                c.Report();
                yield break;
            }
            yield return Wait(1.5f, null);
            var distance = Vector3.Distance(d.transform.position, player.transform.position);
            c.Check(d.IsSleeping() && !d.IsAlerted(), "setup: Draugr_sleeping is asleep and unalerted");
            c.Check(distance > d.m_wakeupRange && distance < d.m_viewRange,
                $"setup: {F(distance)} m is outside its wake range ({F(d.m_wakeupRange)} m) and inside its view range ({F(d.m_viewRange)} m)");
            s.ClearEnemies(player.transform.position, 60f, d);
            c.Check(BaseAI.InStealthRange(player), $"rank 0: InStealthRange must be true next to an unaware hostile Draugr ({StealthRange(player)})");
            Standing.TestOverride = new Standing.Override(5);
            c.Check(Attitudes.Judge(d, player) == Attitude.Afraid, "rank 5 (Bonemass + 2): the Swamp Draugr is afraid");
            c.Check(!BaseAI.InStealthRange(player), $"rank 5: InStealthRange must be false with only an afraid Draugr near ({StealthRange(player)})");
            Standing.TestOverride = new Standing.Override(0);
            c.Check(BaseAI.InStealthRange(player), "rank 0 again: InStealthRange true again");
            yield return Wait(1f, null);
            c.Check(d.IsSleeping(), "the Draugr woke up during the test");

            // Rout (2.7): a follower asleep when its leader died did not see it: not routed, also not once it wakes
            // while the rout lasts (the recent-rout check would else apply it).
            ServerRules.TestRules = NewRules(r =>
            {
                r.Packs = "Troll > Draugr_sleeping";
                r.RoutSeconds = 10f;
            });
            var dzdo = d.m_nview.GetZDO();
            var applied = Rout.Apply(d.transform.position + s.Right * 3f, Hash("Troll"));
            c.Check(applied == 0 && CreatureKeys.GetRoutUntil(dzdo) == 0L,
                $"rout: the sleeping follower 3 m from the fallen leader is not routed ({applied} routed)");
            d.Wakeup();
            yield return Wait(2.5f, null);
            c.Check(!d.IsSleeping(), "rout setup: the follower woke up");
            c.Check(CreatureKeys.GetRoutUntil(dzdo) == 0L,
                "rout: woken 2.5 s into the rout, the follower that slept through it is still not routed");
            Rout.Clear();
            c.Report();
        }
        finally
        {
            Finish(s);
        }
    }

    // ================================================================ morale.rout

    private static bool AllCalmedDown(List<MonsterAI> pack)
    {
        foreach (var g in pack)
        {
            if (g == null || g.IsAlerted() || g.m_targetCreature != null)
            {
                return false;
            }
        }
        return true;
    }

    private static bool AllTarget(List<MonsterAI> pack, Player player)
    {
        foreach (var g in pack)
        {
            if (g == null || g.m_targetCreature != player)
            {
                return false;
            }
        }
        return true;
    }

    private static string DescribePack(List<MonsterAI> pack, Player player)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < pack.Count; i++)
        {
            var g = pack[i];
            sb.Append(i > 0 ? "; " : "").Append('#').Append(i + 1);
            if (g == null)
            {
                sb.Append(" gone");
                continue;
            }
            sb.Append(" alerted ").Append(g.IsAlerted()).Append(", target ").Append(Who(g.m_targetCreature)).Append(", ")
                .Append(F(Vector3.Distance(g.transform.position, player.transform.position))).Append(" m from the player, ")
                .Append(Attitudes.Judge(g, player));
        }
        return sb.ToString();
    }

    // Rout scene: Troll 15 m from the player, followers 4 m right, left and beyond it, Boar and tame on the player's
    // side. Routed follower run with vanilla BaseAI.Flee: 10 random points FleeRange away within 45 degrees of "away
    // from the death point", first one with a full navmesh path (HavePath) on dry ground; none = a random point with
    // no path check, and MoveTo stand still when it has no path. So a follower in a boulder or tree, or with water or
    // a cliff behind it, stay put: nothing the mod decide. Me pick a scene where every creature stand free and every
    // follower have a path away.
    private sealed class RoutScene
    {
        internal float Angle;          // degrees from the view direction
        internal Vector3 Troll;
        internal Vector3 Forward;
        internal Vector3 Right;
        internal Vector3[] Followers;  // right, left, beyond the Troll
        internal Vector3 Boar;
        internal Vector3 Tame;
        internal int Paths;            // flee paths found, of Followers.Length x FleeAngles.Length

        internal static RoutScene At(Vector3 start, Vector3 view, float angle)
        {
            var dir = Quaternion.Euler(0f, angle, 0f) * view;
            dir.y = 0f;
            dir.Normalize();
            var right = Vector3.Cross(Vector3.up, dir).normalized;
            var troll = Stage.Ground(start + dir * 15f);
            return new RoutScene
            {
                Angle = angle,
                Troll = troll,
                Forward = dir,
                Right = right,
                Followers = new[]
                {
                    Stage.Ground(troll + right * 4f),
                    Stage.Ground(troll - right * 4f),
                    Stage.Ground(troll + dir * 4f),
                },
                Boar = Stage.Ground(troll - dir * 4f + right * 2f),
                Tame = Stage.Ground(troll - dir * 4f - right * 2f),
            };
        }

        internal bool Open()
        {
            if (!Stage.Open(Troll) || !Stage.Open(Boar) || !Stage.Open(Tame))
            {
                return false;
            }
            foreach (var f in Followers)
            {
                if (!Stage.Open(f))
                {
                    return false;
                }
            }
            return true;
        }
    }

    // Straight away and 30 degrees either side: inside vanilla's 45 degree flee cone.
    private static readonly float[] FleeAngles = { 0f, -30f, 30f };
    private const float NavmeshWait = 15f;

    private sealed class ScenePick
    {
        internal RoutScene Scene;
    }

    private static int FleePaths(RoutScene scene, float range, Pathfinding.AgentType agent)
    {
        var pathfinding = Pathfinding.instance;
        var zones = ZoneSystem.instance;
        if (pathfinding == null || zones == null)
        {
            return 0;
        }
        var found = 0;
        foreach (var f in scene.Followers)
        {
            var away = f - scene.Troll;
            away.y = 0f;
            away.Normalize();
            foreach (var angle in FleeAngles)
            {
                var target = f + Quaternion.Euler(0f, angle, 0f) * away * range;
                // Flee's own test for a creature that avoid water (Greydwarf): solid height not under the water level.
                if (zones.GetSolidHeight(target) >= zones.m_waterLevel && pathfinding.HavePath(f, target, agent))
                {
                    found++;
                }
            }
        }
        return found;
    }

    // Open scenes in Spot's order (straight ahead, then 30 degree turns). A fresh world has no navmesh around the
    // player yet: Pathfinding build 32 m tiles on demand, one at a time, and HavePath say false until they are there
    // (each call poke them). So me ask again every 0.25 s for up to NavmeshWait (15 s), first 4 open scenes only (fewer tiles).
    private static IEnumerator PickRoutScene(Stage s, Vector3 start, ScenePick pick)
    {
        var prefab = ZNetScene.instance.GetPrefab("Greydwarf");
        var ai = prefab != null ? prefab.GetComponent<MonsterAI>() : null;
        var range = ai != null ? ai.m_fleeRange : 25f;
        var agent = ai != null ? ai.m_pathAgentType : Pathfinding.AgentType.Humanoid;
        var open = new List<RoutScene>();
        for (var i = 0; i < 12 && open.Count < 4; i++)
        {
            var scene = RoutScene.At(start, s.Forward, (i % 2 == 0 ? 1f : -1f) * ((i + 1) / 2) * 30f);
            if (scene.Open())
            {
                open.Add(scene);
            }
        }
        if (open.Count == 0)
        {
            pick.Scene = RoutScene.At(start, s.Forward, 0f);
            SelfTest.Note(RoutName, "no rout scene around the player where every creature stands free (rocks, trees, water, wards); "
                                    + "using the one straight ahead");
            yield break;
        }
        var need = open[0].Followers.Length * FleeAngles.Length;
        var begin = Time.time;
        var best = open[0];
        while (true)
        {
            foreach (var scene in open)
            {
                scene.Paths = FleePaths(scene, range, agent);
                if (scene.Paths > best.Paths)
                {
                    best = scene;
                }
                if (scene.Paths == need)
                {
                    pick.Scene = scene;
                    SelfTest.Note(RoutName, $"rout scene {F(scene.Angle)} degrees from the view: every creature stands free, every follower "
                                            + $"has a path {F(range)} m away (navmesh ready after {F(Time.time - begin)} s)");
                    yield break;
                }
            }
            if (Time.time - begin >= NavmeshWait)
            {
                break;
            }
            yield return Wait(0.25f, null);
        }
        pick.Scene = best;
        SelfTest.Note(RoutName, $"no rout scene with a flee path for every follower within {F(NavmeshWait)} s; using {F(best.Angle)} degrees from the view "
                                + $"with {best.Paths} of {need} paths: a follower with no path stands still (vanilla Flee)");
    }

    private static IEnumerator RunRout()
    {
        var c = new Checks(RoutName);
        Stage s = null;
        var w = new Waiter();
        try
        {
            s = Stage.Begin(RoutName, c);
            if (s == null)
            {
                c.Report();
                yield break;
            }
            var player = s.Player;
            var rules = NewRules(r =>
            {
                // 6 s, not 4: room for three of vanilla Flee's 2 s point picks (a follower in an attack swing at the
                // kill, or with no path to its first point, start late; run 2 had one 0.2 m closer after 3 s). Not
                // longer: at 6 m/s they stay inside the loaded zones around the player.
                r.RoutSeconds = 6f;
                r.ShakenSeconds = 12f;
                r.FearRange = 0f; // fear off first: the shaken calm-down is checked, then the fear is turned on for one step
            });
            ServerRules.TestRules = rules;
            Standing.TestOverride = new Standing.Override(0);
            var start = player.transform.position;
            var pick = new ScenePick();
            yield return PickRoutScene(s, start, pick);
            var strays = s.ClearEnemies(start, 50f, null); // walked in while me waited for the navmesh
            if (strays > 0)
            {
                SelfTest.Note(RoutName, $"removed {strays} creature(s) that came within 50 m while the scene was picked");
            }
            var scene = pick.Scene;
            var fwd = scene.Forward;
            var right = scene.Right;
            var trollPos = scene.Troll;
            var face = start - trollPos;
            var troll = s.Spawn("Troll", trollPos, face);
            var pack = new List<MonsterAI>();
            foreach (var spot in scene.Followers)
            {
                pack.Add(s.Spawn("Greydwarf", spot, face));
            }
            var boar = s.Spawn("Boar", scene.Boar, face);
            var spawned = troll != null && boar != null;
            foreach (var g in pack)
            {
                spawned &= g != null;
            }
            if (!c.Check(spawned, "could not spawn the rout scene (Troll, 3 Greydwarfs, Boar)"))
            {
                c.Report();
                yield break;
            }
            NoDrops(troll);
            yield return Wait(1f, null);
            Hit(pack[0].m_character, player, 1f, 0f, 1f);
            c.Check(Provoked(pack[0], player), "setup: the player's hit provokes the first Greydwarf (the rout must wipe it)");
            yield return Wait(1.2f, null);
            // Me make tame just before kill: with time, it fight wild pack (hurt timers, even a death).
            var tame = s.Spawn("Greydwarf", Stage.Ground(troll.transform.position - fwd * 4f - right * 2f), face);
            if (tame != null)
            {
                tame.MakeTame();
            }
            c.Check(tame != null && tame.m_character.IsTamed(), "setup: a tame Greydwarf within 6 m of the Troll");
            var alive = true;
            foreach (var g in pack)
            {
                alive &= g != null && !g.m_character.IsDead();
            }
            if (!c.Check(alive, "setup: the 3 wild Greydwarfs are alive before the kill"))
            {
                c.Report();
                yield break;
            }

            // ----- player kill Troll -----
            var trollCh = troll.m_character;
            var deathPos = troll.transform.position;
            Kill(trollCh, player);
            yield return Until(() => trollCh == null || trollCh.IsDead(), 2f, () =>
            {
                if (trollCh != null && !trollCh.IsDead())
                {
                    deathPos = trollCh.transform.position;
                }
            }, w);
            if (!c.Check(w.Met, "the Troll must die from the player's hit"))
            {
                c.Report();
                yield break;
            }
            var killTime = Time.time;
            var now = Attitudes.Now();
            long packUntil = 0L;
            var startDist = new float[3];
            var startHurt = new float[3];
            var startTimer = new float[3];
            var killPos = new Vector3[3];
            var routFrom = new Vector3[3];
            var watched = new bool[3];
            var farthest = new float[3];
            var hadPath = new bool[3];
            for (var i = 0; i < 3; i++)
            {
                var g = pack[i];
                if (!c.Check(g != null && g.m_nview.IsValid(), $"Greydwarf {i + 1}: still there at the kill"))
                {
                    continue;
                }
                var zdo = g.m_nview.GetZDO();
                var until = CreatureKeys.GetRoutUntil(zdo);
                var from = RoutFromOf(g);
                c.Check(until > now, $"Greydwarf {i + 1}: RoutUntil must be set by the Troll's death");
                c.Check(Vector3.Distance(from, deathPos) <= 1f, $"Greydwarf {i + 1}: RoutFrom {from} must be within 1 m of the death point {deathPos}");
                c.Check(CreatureKeys.ProvokerCount(zdo.GetByteArray(CreatureKeys.Provokers)) == 0, $"Greydwarf {i + 1}: no provocation left after the rout");
                packUntil = Math.Max(packUntil, until);
                startDist[i] = Vector3.Distance(g.transform.position, from);
                killPos[i] = g.transform.position;
                startHurt[i] = g.m_timeSinceHurt;
                startTimer[i] = g.m_updateTargetTimer;
                routFrom[i] = from;
                farthest[i] = startDist[i];
                watched[i] = true;
            }
            // Every frame from the kill on: the farthest each follower got from the death point, and whether its flee
            // found a navmesh path (the rout reset the path timer to -999: a later time = a path search of the rout,
            // not the old chase one).
            Action watchPack = () =>
            {
                for (var i = 0; i < 3; i++)
                {
                    var g = pack[i];
                    if (!watched[i] || g == null)
                    {
                        continue;
                    }
                    farthest[i] = Mathf.Max(farthest[i], Vector3.Distance(g.transform.position, routFrom[i]));
                    hadPath[i] |= g.m_lastFindPathTime > -900f && g.FoundPath();
                }
            };
            var left = (packUntil - now) / (double)TimeSpan.TicksPerSecond;
            c.Check(left > rules.RoutSeconds - 0.5 && left <= rules.RoutSeconds + 0.01,
                $"the rout lasts RoutSeconds ({F(rules.RoutSeconds)} s): {F(left)} s left");
            c.Check(RoutUntilOf(boar) == 0L, "the Boar (not a follower) does not rout");
            c.Check(RoutUntilOf(tame) == 0L, "the tame Greydwarf (exempt) does not rout");
            c.Check(Rout.LastAppliedHere == 3, $"the rout reached the 3 wild Greydwarfs here (single player, applied directly), reached {Rout.LastAppliedHere}");
            s.Destroy(tame); // it would fight the routed pack and reset their hurt timers

            // ----- Greydwarf that come 1 s later pick rout up (recent routs) -----
            yield return WaitUntilTime(killTime + 1f, watchPack);
            var late = s.Spawn("Greydwarf", Stage.Ground(deathPos + right * 5f), right);
            yield return Until(() => RoutUntilOf(late) > Attitudes.Now(), 1f, watchPack, w);
            c.Check(w.Met && RoutUntilOf(late) == packUntil,
                "a Greydwarf spawned 5 m from the death point 1 s later picks up the same rout at its first check");
            if (late != null)
            {
                pack.Add(late);
            }

            // ----- running -----
            var dropped = new bool[3];
            yield return WaitUntilTime(killTime + 3f, () =>
            {
                watchPack();
                for (var i = 0; i < 3; i++)
                {
                    dropped[i] |= pack[i] != null && !pack[i].IsAlerted();
                }
            });
            var after3 = new float[3];
            for (var i = 0; i < 3; i++)
            {
                var g = pack[i];
                if (!c.Check(g != null, $"Greydwarf {i + 1}: still there 3 s after the kill"))
                {
                    continue;
                }
                var grew = g.m_timeSinceHurt - startHurt[i];
                after3[i] = Vector3.Distance(g.transform.position, routFrom[i]);
                c.Check(g.IsAlerted() && g.m_targetCreature == null && !g.HaveTarget(),
                    $"Greydwarf {i + 1}: routed = alerted, no target (alerted {g.IsAlerted()}, target {Who(g.m_targetCreature)})");
                c.Check(grew >= 2f, $"Greydwarf {i + 1}: the base update runs in rout frames (m_timeSinceHurt grew {F(grew)} s in 3 s)");
                c.Check(Mathf.Approximately(g.m_updateTargetTimer, startTimer[i]),
                    $"Greydwarf {i + 1}: vanilla MonsterAI.UpdateAI is skipped in rout frames (target timer {F(startTimer[i])} -> {F(g.m_updateTargetTimer)})");
                c.Check(Attitudes.Judge(g, player) == Attitude.Routed && ZdoAlert(g),
                    $"Greydwarf {i + 1}: routed, and the ZDO alert every game reads is on (every player sees the vanilla alert icon; Judge {Attitudes.Judge(g, player)})");
                c.Check(!dropped[i], $"Greydwarf {i + 1}: the alert stays on every frame of the rout from 1 s after the kill (raised once, no flicker)");
            }

            // Away from the death point: me wait on the distance (until 0.4 s before the rout's end), not on a clock.
            // Vanilla Flee: a new point every 2 s, within 45 degrees of "away" only when such a point has a full path
            // and lies no more than 1 m above the ground there, else any direction; no path = it stands until the
            // next pick; a swing begun before the kill slows it too. This scene (start temple, boss stones, trees)
            // had a path for 3 of 9 checked points in both runs, and one follower was 0.2 m closer after 3 s.
            var runEnd = killTime + rules.RoutSeconds - 0.4f;
            yield return Until(() =>
            {
                for (var i = 0; i < 3; i++)
                {
                    if (watched[i] && pack[i] != null && farthest[i] <= startDist[i] + 0.5f)
                    {
                        return false;
                    }
                }
                return true;
            }, Mathf.Max(runEnd - Time.time, 0f), watchPack, w);
            var ranBy = Time.time - killTime;
            var ranAway = 0;
            for (var i = 0; i < 3; i++)
            {
                var g = pack[i];
                if (!watched[i] || g == null)
                {
                    continue;
                }
                var ran = farthest[i] > startDist[i] + 0.5f;
                ranAway += ran ? 1 : 0;
                if (!ran && !hadPath[i])
                {
                    // Like morale.calm and T01: no path at all = vanilla Flee stand still, nothing the mod decide.
                    SelfTest.Note(RoutName, $"Greydwarf {i + 1}: its flee found no navmesh path in the {F(ranBy)} s after the kill "
                                            + $"({F(startDist[i])} m -> {F(farthest[i])} m at most from the death point); its running distance is not checked");
                    continue;
                }
                c.Check(ran, $"Greydwarf {i + 1}: runs away from the death point ({F(startDist[i])} m -> {F(farthest[i])} m at most within {F(ranBy)} s "
                             + $"of the kill; {F(after3[i])} m after 3 s; moved {F(Vector3.Distance(g.transform.position, killPos[i]))} m since the kill and "
                             + $"{F(Vector3.Distance(killPos[i], scene.Followers[i]))} m from its spawn spot before it; "
                             + $"flee point {F(Vector3.Distance(g.m_fleeTarget, routFrom[i]))} m from the death point, a path found {hadPath[i]}, "
                             + $"in an attack swing now {g.m_character.InAttack()})");
            }
            // No follower with a path at all would leave "they run away" unchecked: that is a failed run, not a pass.
            c.Check(ranAway > 0, $"at least one of the 3 followers gets farther from the death point during the rout ({ranAway} did within {F(ranBy)} s)");
            SelfTest.Note(RoutName, $"{ranAway} of 3 followers got 0.5 m farther from the death point within {F(ranBy)} s of the kill "
                                    + $"(after 3 s: {F(after3[0] - startDist[0])} m, {F(after3[1] - startDist[1])} m, {F(after3[2] - startDist[2])} m)");

            // Shaken fear step below need followers within 10 m of the player: me join the scattered pack. (The rout's
            // own alert goes at its end with or without a player near: FleeFrames.Calm, checked in BroadcastSteps too.)
            yield return WaitUntilTime(runEnd, null);
            pack.RemoveAll(g => g == null);
            if (!c.Check(pack.Count > 0, "no follower left to watch"))
            {
                c.Report();
                yield break;
            }
            var centre = Vector3.zero;
            foreach (var g in pack)
            {
                centre += g.transform.position;
            }
            s.MovePlayer(centre / pack.Count);
            var pulled = 0;
            foreach (var g in pack)
            {
                var off = g.transform.position - player.transform.position;
                off.y = 0f;
                if (off.magnitude > 20f)
                {
                    Stage.Place(g.m_character, Stage.Ground(player.transform.position + off.normalized * 15f), off);
                    pulled++;
                }
            }
            if (pulled > 0)
            {
                SelfTest.Note(RoutName, $"{pulled} follower(s) ran more than 20 m from the pack's centre; moved to 15 m of the player");
            }

            // ----- shaken: afraid of everyone, even rank 0 player (fear off: they calm down) -----
            var routEnd = killTime + rules.RoutSeconds;
            yield return WaitUntilTime(routEnd, null);
            yield return Until(() => AllCalmedDown(pack), 2f, s.Noise, w);
            c.Check(w.Met, $"shaken: within 2 s after the rout ends every follower is unalerted with no target although the player is rank 0 ({DescribePack(pack, player)})");
            if (w.Met)
            {
                SelfTest.Note(RoutName, $"shaken: calmed down {F(w.Took)} s after the rout ended");
            }
            foreach (var g in pack)
            {
                c.Check(Attitudes.Judge(g, player) == Attitude.Afraid && !ZdoAlert(g),
                    $"shaken: {Attitudes.Judge(g, player)} toward the player, expected Afraid, with the ZDO alert off (no alert icon while it does not run)");
            }

            // ----- shaken with the fear on: the followers close to the rank 0 player run from him -----
            var spots = new List<Vector3>();
            var close = new List<MonsterAI>();
            foreach (var g in pack)
            {
                spots.Add(g.transform.position);
                if (Dist(g, player) <= 10f)
                {
                    close.Add(g);
                }
            }
            var fearOn = rules.Clone();
            fearOn.FearRange = 12f;
            fearOn.Build();
            ServerRules.TestRules = fearOn;
            if (close.Count == 0)
            {
                // All ran far (first run: step skipped, nothing checked). Me put the first one 8 m from the player,
                // facing him: the step always check something.
                var off = pack[0].transform.position - player.transform.position;
                off.y = 0f;
                var toward = off.sqrMagnitude > 0.01f ? off.normalized : s.Forward;
                var nearSpot = Stage.Ground(player.transform.position + toward * 8f);
                Stage.Place(pack[0].m_character, nearSpot, -toward);
                spots[0] = nearSpot;
                close.Add(pack[0]);
                SelfTest.Note(RoutName, "shaken fear: no follower within 10 m of the player; the first one moved to 8 m from him");
            }
            yield return Until(() => close.TrueForAll(g => g != null && FearOf(g) == player && g.m_targetCreature == null), 2.5f, s.Noise, w);
            c.Check(w.Met, $"shaken with FearRange 12: the {close.Count} follower(s) within 10 m of the rank 0 player run from him within 2.5 s, "
                           + $"with no target ({DescribePack(close, player)})");
            ServerRules.TestRules = rules;
            yield return Until(() => AllCalmedDown(pack), 2.5f, () =>
            {
                s.Noise();
                for (var i = 0; i < pack.Count && i < spots.Count; i++)
                {
                    if (pack[i] != null)
                    {
                        Stage.Place(pack[i].m_character, spots[i], player.transform.position - spots[i]);
                    }
                }
            }, w);
            c.Check(w.Met && pack.TrueForAll(g => g != null && FearOf(g) == null),
                $"shaken with the fear off again: they stop running and calm down within 2.5 s ({DescribePack(pack, player)})");

            // ----- shaken over: rank 0 again -----
            yield return WaitUntilTime(routEnd + rules.ShakenSeconds, s.Noise);
            yield return Until(() => AllTarget(pack, player), 4.5f, s.Noise, w);
            c.Check(w.Met, $"after ShakenSeconds the followers target the (noisy) player again within 4 s ({DescribePack(pack, player)})");
            foreach (var g in pack)
            {
                s.Destroy(g);
            }

            yield return NoRoutSteps(s, c);
            yield return BroadcastSteps(s, c);
            c.Report();
        }
        finally
        {
            Finish(s);
        }
    }

    // Leader killed with no player or tame in the fight (killall, fall...) rout nobody (decision 10).
    private static IEnumerator NoRoutSteps(Stage s, Checks c)
    {
        var player = s.Player;
        var w = new Waiter();
        var here = player.transform.position;
        var pos = s.Spot(here, s.Forward, 10f, false);
        var troll = s.Spawn("Troll", pos, here - pos);
        var follower = s.Spawn("Greydwarf", Stage.Ground(pos + s.Right * 4f), here - pos);
        if (!c.Check(troll != null && follower != null, "no-rout: could not spawn a Troll and a Greydwarf"))
        {
            yield break;
        }
        NoDrops(troll);
        yield return Wait(1f, null);
        var trollCh = troll.m_character;
        c.Check(troll.m_nview.GetZDO().GetInt(ZDOVars.s_attackers) == 0, "no-rout setup: no player ever hit the second Troll");
        trollCh.Damage(new HitData(1e10f));
        yield return Until(() => trollCh == null || trollCh.IsDead(), 2f, null, w);
        c.Check(w.Met, "no-rout: the second Troll dies from the attacker-less hit");
        yield return Wait(1.5f, null);
        c.Check(RoutUntilOf(follower) == 0L, "no-rout: a Troll killed with no player or tame taking part does not rout its pack");
        s.Destroy(follower);
    }

    // Same rout through the Rout message: no peer in the test world, ZRoutedRpc handle it here at once.
    private static IEnumerator BroadcastSteps(Stage s, Checks c)
    {
        var player = s.Player;
        var w = new Waiter();
        Rout.ForceBroadcast = true;
        var here = player.transform.position;
        var pos = s.Spot(here, s.Forward, 10f, false);
        var troll = s.Spawn("Troll", pos, here - pos);
        var b1 = s.Spawn("Greydwarf", Stage.Ground(pos + s.Right * 4f), here - pos);
        var b2 = s.Spawn("Greydwarf", Stage.Ground(pos - s.Right * 4f), here - pos);
        if (!c.Check(troll != null && b1 != null && b2 != null, "broadcast: could not spawn a Troll and two Greydwarfs"))
        {
            yield break;
        }
        NoDrops(troll);
        yield return Wait(1f, null);
        var trollCh = troll.m_character;
        var deathPos = troll.transform.position;
        Kill(trollCh, player);
        yield return Until(() => trollCh == null || trollCh.IsDead(), 2f, () =>
        {
            if (trollCh != null && !trollCh.IsDead())
            {
                deathPos = trollCh.transform.position;
            }
        }, w);
        c.Check(w.Met, "broadcast: the Troll dies from the player's hit");
        var now = Attitudes.Now();
        c.Check(RoutUntilOf(b1) > now && RoutUntilOf(b2) > now, "broadcast: both followers routed through the Rout message");
        c.Check(Rout.LastAppliedHere == 2, $"broadcast: the handler applied the rout to 2 creatures here, {Rout.LastAppliedHere}");
        c.Check(Vector3.Distance(RoutFromOf(b1), deathPos) <= 1f, "broadcast: the message carries the death point");
        Rout.ForceBroadcast = false;

        // Rout over: the alert of the run goes within a check, whether a player is near or not (FleeFrames.Calm; fear
        // off in these rules, rank 0 player: shaken, not run from).
        var left = (float)((Math.Max(RoutUntilOf(b1), RoutUntilOf(b2)) - Attitudes.Now()) / (double)TimeSpan.TicksPerSecond);
        yield return Wait(Mathf.Max(left, 0f), null);
        yield return Until(() => b1 != null && b2 != null && !b1.IsAlerted() && !b2.IsAlerted() && !ZdoAlert(b1) && !ZdoAlert(b2), 2f, null, w);
        c.Check(w.Met, $"broadcast: within 2 s after the rout ends both followers are unalerted, ZDO alert off (alerted {b1 != null && b1.IsAlerted()} / "
                       + $"{b2 != null && b2.IsAlerted()}, target {Who(b1 != null ? b1.m_targetCreature : null)} / {Who(b2 != null ? b2.m_targetCreature : null)}; "
                       + $"{F(Dist(b1, player))} m and {F(Dist(b2, player))} m from the player)");
        if (w.Met)
        {
            SelfTest.Note(RoutName, $"rout over: unalerted {F(w.Took)} s after the rout ended, {F(Dist(b1, player))} m and {F(Dist(b2, player))} m from the player");
        }
        s.Destroy(b1);
        s.Destroy(b2);
    }
#endif
}
