using System.Diagnostics;
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BepInEx.Bootstrap;
using MC.Combat.SneakAmbushMod.Patches;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;
#endif

namespace MC.Combat.SneakAmbushMod;

// Debug build only (calls vanish in Release). In-world self tests, run by world probe (tools/Test-InWorld.ps1,
// -Mod Sneak.Ambush). Design 7.5:
//   sneak.content      Smoke Screen item, projectile, cloud, recipe registered once (still once after two more
//                      registrations), vanilla Smoke Bomb untouched, clone values, every name me rely on exist
//                      (prefabs, animator trigger, status effect, layer), NOTE dumps of unverified game values
//   sneak.bug.missing-bomb-error  known bug, alone: error "BombSmoke is missing" logged at the main menu when another
//                      mod (Jotunn) show a half-filled item database there; FAIL while it was logged since start
//   sneak.curve        early-game curve: crouched target = vanilla formula x 0.85 at Sneak 0, x 1 at Sneak 100
//   sneak.still        holding still -70% (icon, target), moving end it same frame (snap up), carried = no bonus,
//                      rule change reach shown icon without re-add
//   sneak.cover        In foliage at Bush01 (sight ray blocked, FoliageBonus 30 = x 0.7), crown ray under Beech1, fog
//                      density = render fog in six forced weathers, fog icon percent, no mist in Meadows
//   sneak.xp           sneak-attack XP through Character.Damage: vanilla cooldown, ZDO cooldown, bad payloads on the
//                      real RPC, ranged half, tame and training dummy nothing
//   sneak.smoke-throw  real throw (StartAttack), creature hit, water hit, TTL: one cloud each, ZDO keys, persistent,
//                      nobody alerted, no bomb dropped back, 3 s cloud inert then gone after fade; screenshot
//   sneak.smoke-sight  CanSeeTarget / CanHearTarget: in/out, between, inside range, reveal by hits (fire only, tiny
//                      too; none without damage), boss and turret-like observer not fooled, In smoke icon
//   sneak.smoke-blind  burst blind creature chasing player near it, it forget, old reveal gone, new hit reveal; far
//                      one keep its fight
//   sneak.healthbars   EnemyHud.TestShow and real plate: OnlyInside, ThroughSmoke, Off, boss
//   sneak.network      rules on the wire, pending pick, join check verdicts (pure)
//   sneak.guard        throw refused while feature off (stack kept, message), throw work when on
//   sneak.pending      rules pending = vanilla stealth, no icon, smoke hide nothing, recipe hidden, throw refused;
//                      pending over = all back
// Me force rules only with ServerRules.TestRules / TestPending, feature off with Plugin.TestInactive, icons and
// message with StealthCues.TestShowCues / SneakXp.TestShowMessage: never config. Rig put back player (crouch,
// place, aim, skill, items, controls), time, weather, and destroy all me spawn.
// More tests for the in-game test list live in SelfTests.Coverage.cs (single player: real swings, arrows, ship,
// recipe, default cloud, death, real off and on) and SelfTests.Multiplayer.cs (dedicated server runs).
internal static partial class SelfTests
{
    private const string ContentName = "sneak.content";
    private const string CurveName = "sneak.curve";
    private const string StillName = "sneak.still";
    private const string CoverName = "sneak.cover";
    private const string XpName = "sneak.xp";
    private const string ThrowName = "sneak.smoke-throw";
    private const string SightName = "sneak.smoke-sight";
    private const string BlindName = "sneak.smoke-blind";
    private const string BarsName = "sneak.healthbars";
    private const string NetworkName = "sneak.network";
    private const string GuardName = "sneak.guard";
    private const string PendingName = "sneak.pending";

    [Conditional("DEBUG")]
    internal static void Register()
    {
#if DEBUG
        SelfTest.Register(ContentName, RunContent);
        SelfTest.Register(BugMissingBombName, RunBugMissingBomb);
        SelfTest.Register(CurveName, RunCurve);
        SelfTest.Register(StillName, RunStill);
        SelfTest.Register(CoverName, RunCover);
        SelfTest.Register(XpName, RunXp);
        SelfTest.Register(ThrowName, RunThrow);
        SelfTest.Register(SightName, RunSight);
        SelfTest.Register(BlindName, RunBlind);
        SelfTest.Register(BarsName, RunBars);
        SelfTest.Register(NetworkName, RunNetwork);
        SelfTest.Register(GuardName, RunGuard);
        SelfTest.Register(PendingName, RunPending);
        RegisterCoverage();
        RegisterMultiplayer();
#endif
    }

    [Conditional("DEBUG")]
    internal static void Unregister()
    {
#if DEBUG
        SelfTest.Unregister(ContentName);
        SelfTest.Unregister(BugMissingBombName);
        SelfTest.Unregister(CurveName);
        SelfTest.Unregister(StillName);
        SelfTest.Unregister(CoverName);
        SelfTest.Unregister(XpName);
        SelfTest.Unregister(ThrowName);
        SelfTest.Unregister(SightName);
        SelfTest.Unregister(BlindName);
        SelfTest.Unregister(BarsName);
        SelfTest.Unregister(NetworkName);
        SelfTest.Unregister(GuardName);
        SelfTest.Unregister(PendingName);
        UnregisterCoverage();
        UnregisterMultiplayer();
        ClearOverrides();
        if (!Tap.Hold)
        {
            Tap.Remove();
        }
#endif
    }

#if DEBUG
    // Debug only (release keep its old constants, nothing new).
    private const string BugMissingBombName = "sneak.bug.missing-bomb-error";

    // Names me (and TESTING.md) rely on: must be network prefabs in this game version.
    private static readonly string[] NeededPrefabs =
    {
        SmokeContent.VanillaBombName, "Resin", "Coal", "LeatherScraps", AmbushRules.DefaultRecipeStation,
        "Fiddleheadfern", "Wisp", "Bush01", "RaspberryBush", "Beech1", "Greydwarf", "Troll", "Skeleton", "Boar",
        "Neck", "Deer", "Draugr_sleeping", "Eikthyr", "piece_TrainingDummy",
    };

    // Forced weathers of sneak.cover after Misty night and Clear noon (asset-read fog densities to settle).
    private static readonly string[] FogEnvs = { "SwampRain", "Darklands_dark", "Mistlands_clear", "Twilight_Clear" };

    // Bush01 stem: model CapsuleCollider 0.4 m wide (sneak.cover collider dump, first run).
    private const float BushStemRadius = 0.2f;

    // Feature back on first: rule reset rebuild recipe (ServerRules.Changed), and rebuild while "off" hide it.
    private static void ClearOverrides()
    {
        Plugin.TestInactive = false;
        ServerRules.TestPending = false;
        ServerRules.TestRules = null;
        StealthCues.TestShowCues = null;
        SneakXp.TestShowMessage = null;
        Compat.TestOtherModPays = null;
        Tap.ForceMist = false;
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
        internal string Detail = "";
    }

    // One test cloud that moves by being destroyed and made again.
    private sealed class CloudSlot
    {
        internal SmokeCloud Cloud;
    }

    // Me = what one test change on player and world. Restore (finally, no yield) put everything back.
    private sealed class Rig
    {
        internal readonly string Test;
        internal readonly Player Player;
        internal readonly List<GameObject> Spawned = new List<GameObject>();
        private readonly HashSet<SmokeCloud> _cloudsBefore;
        private readonly Vector3 _position;
        private readonly float _pitch;
        private readonly Quaternion _yaw;
        private readonly ItemDrop.ItemData _right;
        private readonly ItemDrop.ItemData _left;
        private readonly int _smokeBefore;
        private bool _skillSaved;
        private float _level;
        private float _accumulator;
        private bool _envSaved;
        private string _env;
        private bool _todOn;
        private float _tod;
        private PlayerController _controller;
        private bool _controllerEnabled;
        private bool _moved;
        private bool _aimed;
        // Items a test gave (shared name -> how many the player had before), every skill, god mode and health.
        private readonly Dictionary<string, int> _givenBefore = new Dictionary<string, int>();
        private Dictionary<Skills.SkillType, KeyValuePair<float, float>> _allSkills;
        private bool _godOff;
        private float _health;
        private readonly float _startHealth;

        private Rig(string test, Player player)
        {
            Test = test;
            Player = player;
            _startHealth = player.GetHealth();
            _cloudsBefore = new HashSet<SmokeCloud>(SmokeRegistry.All);
            _position = player.transform.position;
            _pitch = player.m_lookPitch;
            _yaw = player.m_lookYaw;
            _right = player.m_rightItem;
            _left = player.m_leftItem;
            _smokeBefore = player.GetInventory().CountItems(SmokeContent.DisplayName, -1, false);
        }

        internal static Rig Create(string test)
        {
            var player = Player.m_localPlayer;
            if (player == null || ZNetScene.instance == null || ObjectDB.instance == null || ZoneSystem.instance == null
                || EnvMan.instance == null || ZNet.instance == null)
            {
                SelfTest.Fail(test, "no local player or no world");
                return null;
            }
            return new Rig(test, player);
        }

        internal int SmokeCount() => Player.GetInventory().CountItems(SmokeContent.DisplayName, -1, false);

        // Where the player stood when the test began (Restore put him back there after a move).
        internal Vector3 Home => _position;

        // Any item into the bag (spawn name). Restore take away what the test added (also what a craft made of it).
        internal ItemDrop.ItemData Give(string prefabName, int count = 1)
        {
            var prefab = ObjectDB.instance.GetItemPrefab(prefabName);
            var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null)
            {
                return null;
            }
            Track(drop.m_itemData.m_shared.m_name);
            return Player.GetInventory().AddItem(prefabName, count, 1, 0, 0L, "", false);
        }

        // Count of that item (shared name) now = what Restore put back.
        internal void Track(string sharedName)
        {
            if (!_givenBefore.ContainsKey(sharedName))
            {
                _givenBefore[sharedName] = Player.GetInventory().CountItems(sharedName, -1, false);
            }
        }

        // Item in hand (weapon, shield, bow). False when it could not be equipped.
        internal bool Equip(ItemDrop.ItemData item) =>
            item != null && (Player.IsItemEquiped(item) || Player.EquipItem(item, false));

        // Every skill kept (real swings and shots raise weapon skills too).
        internal void SaveAllSkills()
        {
            if (_allSkills != null)
            {
                return;
            }
            _allSkills = new Dictionary<Skills.SkillType, KeyValuePair<float, float>>();
            foreach (var pair in Player.GetSkills().m_skillData)
            {
                _allSkills[pair.Key] = new KeyValuePair<float, float>(pair.Value.m_level, pair.Value.m_accumulator);
            }
        }

        // Player can be hurt (a hit that must land). Restore give god mode and health back.
        internal void GodOff()
        {
            if (!_godOff)
            {
                _godOff = true;
                _health = Player.GetHealth();
            }
            Player.SetGodMode(false);
        }

        // Sneak level for the test (progress 0); first call keep the real one.
        internal void SetSneak(float level)
        {
            var skill = Player.GetSkills().GetSkill(Skills.SkillType.Sneak);
            if (!_skillSaved)
            {
                _skillSaved = true;
                _level = skill.m_level;
                _accumulator = skill.m_accumulator;
            }
            skill.m_level = level;
            skill.m_accumulator = 0f;
        }

        // Fixed time of day, like console "tod" (0 midnight, 0.5 noon).
        internal void SetTime(float dayFraction)
        {
            SaveEnv();
            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = dayFraction;
        }

        // Forced weather, like console "env", switched at once: vanilla blend weather over EnvMan.m_transitionDuration
        // (asset value; first run: six forced weathers not in place after 6 s each). Instant switch = what vanilla do
        // after spawn and teleport (EnvMan.ForceInstantEnvironmentSwitch, 1 s window).
        internal void SetEnv(string env)
        {
            SaveEnv();
            EnvMan.instance.m_debugEnv = env;
            EnvMan.instance.ForceInstantEnvironmentSwitch();
        }

        private void SaveEnv()
        {
            if (_envSaved)
            {
                return;
            }
            _envSaved = true;
            var env = EnvMan.instance;
            _env = env.m_debugEnv;
            _todOn = env.m_debugTimeOfDay;
            _tod = env.m_debugTime;
        }

        // Me drive the player (no keyboard in between).
        internal void TakeControls()
        {
            if (_controller == null)
            {
                _controller = Player.GetComponent<PlayerController>();
                if (_controller != null)
                {
                    _controllerEnabled = _controller.enabled;
                    _controller.enabled = false;
                }
            }
            Drive(Vector3.zero);
        }

        internal void Drive(Vector3 move) =>
            Player.SetControls(move, false, false, false, false, false, false, false, false, false, false);

        internal void MarkMoved() => _moved = true;

        // Look (and throw) that way, pitch in degrees down.
        internal void Aim(Vector3 dir, float pitchDown)
        {
            _aimed = true;
            Player.SetLookDir(Flat(dir));
            Player.m_lookPitch = pitchDown;
            Player.UpdateEyeRotation();
        }

        internal GameObject Spawn(string prefabName, Vector3 position, Quaternion rotation)
        {
            var prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null)
            {
                return null;
            }
            var go = Object.Instantiate(prefab, position, rotation);
            Spawned.Add(go);
            return go;
        }

        // Creature facing that way; ai false = its AI component off (stands still, alert only from what the test does).
        internal Character Creature(string prefabName, Vector3 position, Vector3 facing, bool ai)
        {
            var go = Spawn(prefabName, position, Quaternion.LookRotation(Flat(facing)));
            if (go == null)
            {
                return null;
            }
            var character = go.GetComponent<Character>();
            var baseAi = go.GetComponent<BaseAI>();
            if (!ai && baseAi != null)
            {
                baseAi.enabled = false;
            }
            if (character != null)
            {
                character.SetLookDir(Flat(facing));
            }
            return character;
        }

        internal SmokeCloud Cloud(Vector3 position)
        {
            var cloud = SmokeCloud.Spawn(position);
            if (cloud != null)
            {
                Spawned.Add(cloud.gameObject);
            }
            return cloud;
        }

        // Move the test cloud: old one gone (end of frame), new one made next frame.
        internal IEnumerator PutCloud(CloudSlot slot, Vector3 position)
        {
            if (slot.Cloud != null)
            {
                Remove(slot.Cloud.gameObject);
                slot.Cloud = null;
                yield return null;
            }
            slot.Cloud = Cloud(position);
            yield return null;
        }

        internal void Remove(GameObject go)
        {
            if (go == null)
            {
                return;
            }
            Spawned.Remove(go);
            DestroyObject(go);
        }

        // Our projectile, as a Smoke Screen throw of the player would launch it.
        internal Projectile Launch(Vector3 from, Vector3 velocity, float ttl = -1f)
        {
            var prefab = SmokeContent.ProjectilePrefab;
            if (prefab == null)
            {
                return null;
            }
            var go = Object.Instantiate(prefab, from, Quaternion.LookRotation(velocity));
            Spawned.Add(go);
            var projectile = go.GetComponent<Projectile>();
            if (ttl > 0f)
            {
                projectile.m_ttl = ttl;
            }
            var hit = new HitData { m_backstabBonus = 1f, m_dodgeable = true, m_blockable = true };
            hit.SetAttacker(Player);
            projectile.Setup(Player, velocity, -1f, hit, null, null);
            return projectile;
        }

        // Smoke Screens in the bag, one of them in hand. Null when the item or the equip failed.
        internal ItemDrop.ItemData GiveAndEquip(int count)
        {
            var inventory = Player.GetInventory();
            inventory.AddItem(SmokeContent.ItemName, count, 1, 0, 0L, "", false);
            ItemDrop.ItemData item = null;
            foreach (var it in inventory.GetAllItems())
            {
                if (SmokeContent.IsSmokeScreen(it))
                {
                    item = it;
                    break;
                }
            }
            if (item == null)
            {
                return null;
            }
            if (!Player.IsItemEquiped(item) && !Player.EquipItem(item, true))
            {
                return null;
            }
            return item;
        }

        internal void Restore()
        {
            // Feature really turned off by a test: on again first (activation rebuild recipe, register tests again).
            Safe("feature", () =>
            {
                if (Plugin.TestBlocked)
                {
                    Plugin.TestBlocked = false;
                    FeatureRegistry.RefreshAll();
                }
            });
            Safe("overrides", ClearOverrides);
            Safe("test hooks", Tap.Release);
            Safe("clouds", DestroyNewClouds);
            Safe("spawned objects", () =>
            {
                foreach (var go in Spawned)
                {
                    DestroyObject(go);
                }
                Spawned.Clear();
            });
            Safe("controls", () =>
            {
                Drive(Vector3.zero);
                if (_controller != null)
                {
                    _controller.enabled = _controllerEnabled;
                }
            });
            Safe("items", RestoreItems);
            Safe("skill", () =>
            {
                if (_skillSaved)
                {
                    var skill = Player.GetSkills().GetSkill(Skills.SkillType.Sneak);
                    skill.m_level = _level;
                    skill.m_accumulator = _accumulator;
                }
                if (_allSkills != null)
                {
                    var data = Player.GetSkills().m_skillData;
                    foreach (var type in data.Keys.ToList())
                    {
                        if (_allSkills.TryGetValue(type, out var saved))
                        {
                            data[type].m_level = saved.Key;
                            data[type].m_accumulator = saved.Value;
                        }
                        else
                        {
                            // Skill first used in the test: gone again.
                            data.Remove(type);
                        }
                    }
                }
            });
            Safe("god mode and health", () =>
            {
                if (Player == null)
                {
                    return;
                }
                if (_godOff)
                {
                    Player.SetGodMode(true);
                }
                // Creature hits take health from a god-mode player too (down to 1): given back.
                var wanted = _godOff ? Mathf.Max(_health, _startHealth) : _startHealth;
                if (Player.GetHealth() < wanted)
                {
                    Player.SetHealth(Mathf.Min(wanted, Player.GetMaxHealth()));
                }
            });
            Safe("time and weather", () =>
            {
                if (_envSaved && EnvMan.instance != null)
                {
                    var changed = EnvMan.instance.m_debugEnv != _env;
                    EnvMan.instance.m_debugEnv = _env;
                    EnvMan.instance.m_debugTimeOfDay = _todOn;
                    EnvMan.instance.m_debugTime = _tod;
                    if (changed)
                    {
                        // Real weather back at once too (later tests and screenshots not in test fog).
                        EnvMan.instance.ForceInstantEnvironmentSwitch();
                    }
                }
            });
            Safe("player", () =>
            {
                if (Player == null)
                {
                    return;
                }
                if (_moved)
                {
                    MovePlayer(Player, _position + Vector3.up * 0.05f);
                }
                if (_aimed)
                {
                    Player.m_lookYaw = _yaw;
                    Player.m_lookPitch = _pitch;
                    Player.UpdateEyeRotation();
                }
                Player.SetCrouch(false);
                StealthCues.RemoveAll(Player);
                Player.m_stealthFactorUpdateTimer = 0.51f;
            });
        }

        private void DestroyNewClouds()
        {
            foreach (var cloud in SmokeRegistry.All.ToList())
            {
                if (cloud != null && !_cloudsBefore.Contains(cloud))
                {
                    DestroyObject(cloud.gameObject);
                }
            }
        }

        private void RestoreItems()
        {
            if (Player == null)
            {
                return;
            }
            var inventory = Player.GetInventory();
            foreach (var it in inventory.GetAllItems().ToList())
            {
                if (SmokeContent.IsSmokeScreen(it) && Player.IsItemEquiped(it))
                {
                    Player.UnequipItem(it, false);
                }
            }
            var extra = inventory.CountItems(SmokeContent.DisplayName, -1, false) - _smokeBefore;
            if (extra > 0)
            {
                inventory.RemoveItem(SmokeContent.DisplayName, extra, -1, false);
            }
            // Items a test gave: out of the hands first, then only the extra ones go.
            foreach (var given in _givenBefore)
            {
                var more = inventory.CountItems(given.Key, -1, false) - given.Value;
                if (more <= 0)
                {
                    continue;
                }
                foreach (var it in inventory.GetAllItems().ToList())
                {
                    if (it.m_shared.m_name == given.Key && Player.IsItemEquiped(it))
                    {
                        Player.UnequipItem(it, false);
                    }
                }
                inventory.RemoveItem(given.Key, more, -1, false);
            }
            if (_right != null && inventory.ContainsItem(_right) && !Player.IsItemEquiped(_right))
            {
                Player.EquipItem(_right, false);
            }
            if (_left != null && inventory.ContainsItem(_left) && !Player.IsItemEquiped(_left))
            {
                Player.EquipItem(_left, false);
            }
        }

        private void Safe(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                SelfTest.Fail(Test, $"clean-up of {what} failed: {e.Message}");
            }
        }
    }

    private static void DestroyObject(GameObject go)
    {
        if (go == null)
        {
            return;
        }
        var view = go.GetComponent<ZNetView>();
        if (view != null && view.IsValid() && ZNetScene.instance != null)
        {
            ZNetScene.instance.Destroy(go);
        }
        else
        {
            Object.Destroy(go);
        }
    }

    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string V(Vector3 v) => $"({F(v.x)}, {F(v.y)}, {F(v.z)})";

    private static bool Near(float a, float b, float tolerance = 0.001f) => Mathf.Abs(a - b) <= tolerance;

    // Loaded mods known to change RenderSettings.fogDensity after the game wrote it (name, or null when none).
    private static readonly string[] RenderedFogMods =
    {
        "MC.Exploration.View.DistantHorizons", "vapok.mods.nofogbruh", "shudnal.Seasons",
        "com.Skarif.ValheimPerformanceOverhaul_WATER", "marc.donegalhorizonlift",
    };

    private static string RenderedFogMod()
    {
        foreach (var guid in RenderedFogMods)
        {
            if (Chainloader.PluginInfos.TryGetValue(guid, out var info) && info != null && info.Instance != null)
            {
                return info.Metadata.Name;
            }
        }
        return null;
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
    }

    // Terrain under a point, a little above it (creatures and trees drop on it).
    private static Vector3 Ground(Vector3 p)
    {
        var zones = ZoneSystem.instance;
        if (zones != null && zones.GetGroundHeight(p, out var height))
        {
            p.y = height + 0.05f;
        }
        return p;
    }

    private static int ViewMask =>
        BaseAI.m_viewBlockMask != 0
            ? BaseAI.m_viewBlockMask
            : LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "viewblock", "vehicle");

    private static int ViewblockLayerMask
    {
        get
        {
            var layer = LayerMask.NameToLayer("viewblock");
            return layer >= 0 ? 1 << layer : 0;
        }
    }

    // Horizontal direction from the player with open, dry, fairly flat ground for that distance (camera side first,
    // so screenshots see it). avoid: skip directions closer than 90 degrees to it.
    private static Vector3 ClearDirection(Player player, float distance, out bool found, Vector3? avoid = null)
    {
        var cam = Utils.GetMainCamera();
        var start = Flat(cam != null ? cam.transform.forward : player.transform.forward);
        var feet = player.transform.position;
        var center = feet + Vector3.up * 0.9f;
        var mask = ViewMask;
        var water = ZoneSystem.instance.m_waterLevel;
        for (var i = 0; i < 16; i++)
        {
            var dir = Quaternion.Euler(0f, i * 22.5f, 0f) * start;
            if (avoid.HasValue && Vector3.Angle(dir, avoid.Value) < 90f)
            {
                continue;
            }
            if (Physics.Raycast(center, dir, distance, mask) || Physics.Raycast(center + Vector3.up * 0.8f, dir, distance, mask))
            {
                continue;
            }
            var ok = true;
            for (var k = 1; k <= 4 && ok; k++)
            {
                var ground = ZoneSystem.instance.GetGroundHeight(feet + dir * (distance * k / 4f));
                ok = Mathf.Abs(ground - feet.y) < 1.5f && ground > water + 0.3f;
            }
            if (ok)
            {
                found = true;
                return dir;
            }
        }
        found = false;
        return start;
    }

    private static void Place(Character c, Vector3 position, Vector3 facing)
    {
        var rotation = Quaternion.LookRotation(Flat(facing));
        c.transform.SetPositionAndRotation(position, rotation);
        var body = c.m_body;
        if (body != null)
        {
            body.position = position;
            body.rotation = rotation;
            body.linearVelocity = Vector3.zero;
        }
        c.SetLookDir(Flat(facing));
    }

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

    private static HitData.DamageTypes Blunt(float v) => new HitData.DamageTypes { m_blunt = v };

    private static HitData.DamageTypes Slash(float v) => new HitData.DamageTypes { m_slash = v };

    private static HitData.DamageTypes Pierce(float v) => new HitData.DamageTypes { m_pierce = v };

    private static HitData.DamageTypes Fire(float v) => new HitData.DamageTypes { m_fire = v };

    // A player hit through the vanilla entry point (Character.Damage -> RPC_Damage on the owner, here at once).
    private static void Hit(Character victim, Player attacker, HitData.DamageTypes damage, float backstab = 1f,
        bool ranged = false)
    {
        var hit = new HitData
        {
            m_damage = damage,
            m_backstabBonus = backstab,
            m_ranged = ranged,
            m_point = victim.GetCenterPoint(),
            m_dir = Flat(victim.transform.position - attacker.transform.position),
            m_hitType = HitData.HitType.PlayerHit,
            m_dodgeable = false,
            m_blockable = false,
        };
        hit.SetAttacker(attacker);
        victim.Damage(hit);
    }

    // What an unalerted, targetless creature look like again (after a give-up, a reload).
    private static void Unalert(BaseAI ai)
    {
        ai.SetAlerted(false);
        if (ai is MonsterAI monster)
        {
            monster.m_targetCreature = null;
        }
    }

    private static float LightFactor(Player player) =>
        StealthSystem.instance != null ? StealthSystem.instance.GetLightFactor(player.GetCenterPoint()) : float.NaN;

    // Vanilla crouched target (Player.UpdateStealth before status effects), live light and skill.
    private static float VanillaTarget(Player player)
    {
        var s = player.GetSkillFactor(Skills.SkillType.Sneak);
        var lf = LightFactor(player);
        return Mathf.Clamp01(Mathf.Lerp(0.5f + lf * 0.5f, 0.2f + lf * 0.4f, s));
    }

    // Expected target with the still bonus on top of "without still" (floor as the mod does).
    private static float WithStill(float withoutStill, AmbushRules rules) =>
        Mathf.Max(withoutStill * (1f - rules.StillBonus / 100f), Mathf.Min(rules.VisibilityFloor, withoutStill));

    private static IEnumerator Frames(int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return null;
        }
    }

    private static IEnumerator Crouch(Player player, Box result)
    {
        player.SetCrouch(true);
        var until = Time.time + 3f;
        while (!player.IsCrouching() && Time.time < until)
        {
            yield return null;
        }
        result.Ok = player.IsCrouching();
    }

    // Standing, stealth factor back at 1 (creatures see the player at full range).
    private static IEnumerator Stand(Player player)
    {
        player.SetCrouch(false);
        var until = Time.time + 5f;
        while (Time.time < until && (player.IsCrouching() || player.m_stealthFactor < 0.99f))
        {
            yield return null;
        }
    }

    // Stealth refresh now (vanilla 0.5 s timer due), then after that physics step.
    private static IEnumerator ForceRefresh(Player player)
    {
        player.m_stealthFactorUpdateTimer = 0.51f;
        yield return new WaitForFixedUpdate();
    }

    private static List<SmokeCloud> NewClouds(HashSet<SmokeCloud> before)
    {
        var list = new List<SmokeCloud>();
        foreach (var cloud in SmokeRegistry.All)
        {
            if (cloud != null && !before.Contains(cloud))
            {
                list.Add(cloud);
            }
        }
        return list;
    }

    private static IEnumerator WaitForCloud(HashSet<SmokeCloud> before, float seconds, CloudSlot found)
    {
        var until = Time.time + seconds;
        found.Cloud = null;
        while (Time.time < until)
        {
            var fresh = NewClouds(before);
            if (fresh.Count > 0)
            {
                found.Cloud = fresh[0];
                yield break;
            }
            yield return null;
        }
    }

    private static int SmokeDrops()
    {
        var n = 0;
        foreach (var drop in ItemDrop.s_instances)
        {
            if (drop != null && drop.m_itemData != null && SmokeContent.IsSmokeScreen(drop.m_itemData))
            {
                n++;
            }
        }
        return n;
    }

    private static bool AnyCue(Player player)
    {
        foreach (CueKind kind in Enum.GetValues(typeof(CueKind)))
        {
            if (StealthCues.Has(player, kind))
            {
                return true;
            }
        }
        return false;
    }

    // How many top-left messages waiting in HUD queue hold this text. Player.Message pass log false (vanilla default),
    // so top-left message never reach message log (MessageHud.GetLog): only this queue. MessageHud.Update take one out
    // per second: me read it in same frame as call that sent message.
    private static int QueuedCount(string text)
    {
        var hud = MessageHud.instance;
        if (hud == null || hud.m_msgQeue == null)
        {
            return 0;
        }
        var n = 0;
        foreach (var msg in hud.m_msgQeue)
        {
            if (msg != null && msg.m_text != null && msg.m_text.Contains(text))
            {
                n++;
            }
        }
        return n;
    }

    // Hud hidden by user = MessageHud.ShowMessage drop message.
    private static bool MessageHudWorks => MessageHud.instance != null && !Hud.IsUserHidden();

    private static long NowMs() => (long)(SmokeRegistry.Now * 1000d);

    private static string LayerNames(int mask)
    {
        var names = new List<string>();
        for (var i = 0; i < 32; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                var name = LayerMask.LayerToName(i);
                names.Add(string.IsNullOrEmpty(name) ? i.ToString(CultureInfo.InvariantCulture) : $"{name}({i})");
            }
        }
        return string.Join(", ", names.ToArray());
    }

    private static string Components(GameObject go, bool children)
    {
        if (go == null)
        {
            return "none";
        }
        var parts = children ? go.GetComponentsInChildren<Component>(true) : go.GetComponents<Component>();
        return string.Join(", ", parts.Where(p => p != null).Select(p => p.GetType().Name).Distinct().ToArray());
    }

    private static string Colliders(GameObject go)
    {
        var sb = new StringBuilder();
        foreach (var col in go.GetComponentsInChildren<Collider>(true))
        {
            if (sb.Length > 0)
            {
                sb.Append("; ");
            }
            sb.Append(col.name).Append(' ').Append(col.GetType().Name).Append(" layer ")
                .Append(LayerMask.LayerToName(col.gameObject.layer)).Append(col.isTrigger ? " trigger" : " solid")
                .Append(col.enabled ? "" : " disabled").Append(" centre ")
                .Append(V(col.bounds.center - go.transform.position)).Append(" size ").Append(V(col.bounds.size));
        }
        return sb.Length > 0 ? sb.ToString() : "no collider";
    }

    private static string Effects(EffectList list)
    {
        if (list == null || list.m_effectPrefabs == null || list.m_effectPrefabs.Length == 0)
        {
            return "none";
        }
        return string.Join(", ", list.m_effectPrefabs.Where(e => e != null && e.m_prefab != null)
            .Select(e => $"{e.m_prefab.name} [{Components(e.m_prefab, true)}]").ToArray());
    }

    // Kept hit effects that would network, hurt or choke (must be none).
    private static string HarmfulEffects(Projectile p)
    {
        var bad = new List<string>();
        foreach (var list in new[] { p.m_hitEffects, p.m_hitWaterEffects, p.m_spawnOnHitEffects })
        {
            if (list == null || list.m_effectPrefabs == null)
            {
                continue;
            }
            foreach (var effect in list.m_effectPrefabs)
            {
                var go = effect != null ? effect.m_prefab : null;
                if (go == null)
                {
                    continue;
                }
                if (go.GetComponentInChildren<ZNetView>(true) != null || go.GetComponentInChildren<Aoe>(true) != null
                    || go.GetComponentInChildren<SmokeSpawner>(true) != null || go.GetComponentInChildren<Smoke>(true) != null
                    || go.GetComponentInChildren<SpawnAbility>(true) != null)
                {
                    bad.Add(go.name);
                }
            }
        }
        return string.Join(", ", bad.ToArray());
    }

    private static string ProjectileValues(Projectile p)
    {
        if (p == null)
        {
            return "no Projectile";
        }
        return $"stayAfterHitStatic {p.m_stayAfterHitStatic}, stayAfterHitDynamic {p.m_stayAfterHitDynamic}, "
               + $"attachToRigidBody {p.m_attachToRigidBody}, attachToClosestBone {p.m_attachToClosestBone}, "
               + $"onlyStopOnTerrain {p.m_onlyStopOnTerrain}, respawnItemOnHit {p.m_respawnItemOnHit}, "
               + $"bounce {p.m_bounce}, bounceOnWater {p.m_bounceOnWater}, canHitWater {p.m_canHitWater}, "
               + $"spawnOnTtl {p.m_spawnOnTtl}, ttl {F(p.m_ttl)}, stayTTL {F(p.m_stayTTL)}, gravity {F(p.m_gravity)}, "
               + $"drag {F(p.m_drag)}, rayRadius {F(p.m_rayRadius)}, aoe {F(p.m_aoe)}, hitNoise {F(p.m_hitNoise)}, "
               + $"spawnOnHit {(p.m_spawnOnHit != null ? p.m_spawnOnHit.name : "none")}, "
               + $"spawnItem {(p.m_spawnItem != null && p.m_spawnItem.m_shared != null ? p.m_spawnItem.m_shared.m_name : "none")}, "
               + $"randomSpawnOnHit {(p.m_randomSpawnOnHit != null ? p.m_randomSpawnOnHit.Count : 0)}, "
               + $"spawnOnHitChance {F(p.m_spawnOnHitChance)}, spawnCount {p.m_spawnCount}, "
               + $"spawnOnTerrain {p.m_spawnOnTerrain}, spawnOnCharacters {p.m_spawnOnCharacters}, "
               + $"spawnOnWearNTear {p.m_spawnOnWearNTear}, groundHitOnly {p.m_groundHitOnly}, "
               + $"staticHitOnly {p.m_staticHitOnly}, damage {F(p.m_damage.GetTotalDamage())}, "
               + $"statusEffect '{p.m_statusEffect}'";
    }

    private static string RecipeText(Recipe recipe)
    {
        if (recipe == null)
        {
            return "none";
        }
        var parts = new List<string>();
        if (recipe.m_resources != null)
        {
            foreach (var r in recipe.m_resources)
            {
                if (r != null && r.m_resItem != null)
                {
                    parts.Add($"{r.m_resItem.gameObject.name}:{r.m_amount}");
                }
            }
        }
        var station = recipe.m_craftingStation != null ? recipe.m_craftingStation.name : "none";
        return $"{string.Join(",", parts.ToArray())} -> {recipe.m_amount} at {station} level {recipe.m_minStationLevel}"
               + (recipe.m_enabled ? "" : " (disabled)");
    }

    private static bool HasParameter(Animator animator, string name, AnimatorControllerParameterType type)
    {
        if (animator == null || string.IsNullOrEmpty(name))
        {
            return false;
        }
        foreach (var p in animator.parameters)
        {
            if (p.name == name && p.type == type)
            {
                return true;
            }
        }
        return false;
    }

    // Water surface in the active area (a spot deep enough for a burst on water), for the water throw.
    private static bool FindWater(Player player, out Vector3 surface, out float floor)
    {
        var zones = ZoneSystem.instance;
        var scene = ZNetScene.instance;
        var origin = player.transform.position;
        var level = zones.m_waterLevel;
        for (var r = 8f; r <= 200f; r += 8f)
        {
            for (var a = 0; a < 360; a += 15)
            {
                var p = origin + Quaternion.Euler(0f, a, 0f) * Vector3.forward * r;
                if (scene.OutsideActiveArea(p))
                {
                    continue;
                }
                var ground = zones.GetGroundHeight(p);
                if (ground > level - 1.5f)
                {
                    continue;
                }
                var liquid = Mathf.Max(Floating.GetLiquidLevel(new Vector3(p.x, level - 0.3f, p.z)),
                    Floating.GetLiquidLevel(new Vector3(p.x, ground + 0.3f, p.z)));
                if (liquid > ground + 1f)
                {
                    surface = new Vector3(p.x, liquid, p.z);
                    floor = ground;
                    return true;
                }
            }
        }
        surface = Vector3.zero;
        floor = 0f;
        return false;
    }

    // ---------- sneak.content ----------

    private static IEnumerator RunContent()
    {
        var c = new Checks(ContentName);
        var db = ObjectDB.instance;
        var scene = ZNetScene.instance;
        var player = Player.m_localPlayer;
        if (db == null || scene == null || player == null)
        {
            SelfTest.Fail(ContentName, "no item database, network scene or local player");
            yield break;
        }
        try
        {
            // Recipe from the design's defaults, not this game's config.
            ServerRules.TestRules = new AmbushRules();
            CheckRegistered(c, db, scene);
            CheckClones(c, db);
            CheckNames(c, db, scene, player);
            NoteGameValues(c, db, player);
            c.Report();
        }
        finally
        {
            ServerRules.TestRules = null;
        }
    }

    // ---------- sneak.bug.missing-bomb-error (L03) ----------

    // Known bug of the mod, alone here so sneak.content judge the rest. With some other mods installed (Jotunn) the
    // main menu show an item database that is not empty and hold no vanilla item yet: SmokeContent.RegisterInObjectDB
    // then log the error "The vanilla Smoke Bomb (BombSmoke) is missing from the item database ..." although the
    // Smoke Screen is made a moment later. FAIL while that error was logged since the game started.
    private static IEnumerator RunBugMissingBomb()
    {
        var c = new Checks(BugMissingBombName);
        c.Check(!SmokeContent.MissingReported,
            "the error \"The vanilla Smoke Bomb (BombSmoke) is missing ...\" was logged since the game started "
            + $"(the Smoke Screen was made all the same: {SmokeContent.Built})");
        c.Report();
        yield break;
    }

    private static int OurRecipes(ObjectDB db) => db.m_recipes.Count(r => r != null && r.name == SmokeContent.RecipeName);

    private static string Counts(ObjectDB db, ZNetScene scene) =>
        $"items {db.m_items.Count}, by hash {db.m_itemByHash.Count}, by data {db.m_itemByData.Count}, recipes "
        + $"{db.m_recipes.Count}, network prefabs {scene.m_prefabs.Count}, named {scene.m_namedPrefabs.Count}";

    private static void CheckRegistered(Checks c, ObjectDB db, ZNetScene scene)
    {
        c.Check(SmokeContent.Built, "the Smoke Screen was not made from BombSmoke");
        var item = SmokeContent.ItemPrefab;
        var projectile = SmokeContent.ProjectilePrefab;
        var cloud = SmokeContent.CloudPrefab;
        if (!c.Check(item != null && projectile != null && cloud != null, "item, projectile or cloud prefab missing"))
        {
            return;
        }
        c.Check(db.GetItemPrefab(SmokeContent.ItemName) == item, "MC_SmokeScreen not in the item database");
        c.Check(db.m_itemByHash.TryGetValue(SmokeContent.ItemName.GetStableHashCode(), out var byHash) && byHash == item,
            "MC_SmokeScreen not in the item database's hash table");
        c.Check(scene.GetPrefab(SmokeContent.ItemName) == item, "MC_SmokeScreen not a network prefab");
        c.Check(scene.GetPrefab(SmokeContent.ProjectileName) == projectile, "MC_SmokeScreen_projectile not a network prefab");
        c.Check(scene.GetPrefab(SmokeContent.CloudName) == cloud, "MC_SmokeScreen_cloud not a network prefab");
        c.Check(OurRecipes(db) == 1, $"exactly one {SmokeContent.RecipeName} expected, found {OurRecipes(db)}");
        var recipe = SmokeContent.CraftRecipe;
        if (c.Check(recipe != null, "no Smoke Screen recipe object"))
        {
            c.Check(recipe.m_item == item.GetComponent<ItemDrop>(), "the recipe makes another item");
            var text = RecipeText(recipe);
            c.Check(text == "Resin:2,Coal:1,LeatherScraps:1 -> 2 at piece_workbench level 1",
                $"default recipe expected \"Resin:2,Coal:1,LeatherScraps:1 -> 2 at piece_workbench level 1\", is \"{text}\"");
            c.Check(recipe.m_enabled, "recipe hidden while the feature is on and the rules are here");
        }
        // Two more registrations of each kind: nothing added twice.
        var before = Counts(db, scene);
        for (var i = 0; i < 2; i++)
        {
            SmokeContent.RegisterInObjectDB(db);
            SmokeContent.RegisterInZNetScene(scene);
        }
        var after = Counts(db, scene);
        c.Check(before == after, $"registering again changed the lists: {before} -> {after}");
        c.Check(OurRecipes(db) == 1, "registering again added a second recipe");
    }

    private static void CheckClones(Checks c, ObjectDB db)
    {
        var bombGo = db.GetItemPrefab(SmokeContent.VanillaBombName);
        var bomb = bombGo != null ? bombGo.GetComponent<ItemDrop>() : null;
        var itemGo = SmokeContent.ItemPrefab;
        var ours = itemGo != null ? itemGo.GetComponent<ItemDrop>() : null;
        if (!c.Check(bomb != null && ours != null, "BombSmoke or Smoke Screen without ItemDrop"))
        {
            return;
        }
        var vs = bomb.m_itemData.m_shared;
        var os = ours.m_itemData.m_shared;
        c.Check(!ReferenceEquals(vs, os), "the Smoke Screen shares the vanilla Smoke Bomb's data");

        // Vanilla Smoke Bomb untouched.
        c.Check(vs.m_name == "$item_smokebomb", $"vanilla bomb name changed: {vs.m_name}");
        c.Check(Near(vs.m_damages.m_blunt, 5f), $"vanilla bomb blunt changed: {F(vs.m_damages.m_blunt)}");
        var vanillaProjectile = vs.m_attack != null ? vs.m_attack.m_attackProjectile : null;
        c.Check(vanillaProjectile != null && vanillaProjectile != SmokeContent.ProjectilePrefab,
            "the vanilla bomb must keep its own projectile");
        var vp = vanillaProjectile != null ? vanillaProjectile.GetComponent<Projectile>() : null;
        c.Check(vp != null && vp.m_spawnOnHit != SmokeContent.CloudPrefab, "the vanilla projectile must keep its own explosion");

        // Ours.
        c.Check(os.m_name == SmokeContent.DisplayName, $"item name {os.m_name}");
        c.Check(os.m_description == SmokeContent.Description, "item description");
        c.Check(os.m_damages.GetTotalDamage() == 0f && os.m_damagesPerLevel.GetTotalDamage() == 0f, "Smoke Screen damage not 0");
        c.Check(Near(os.m_backstabBonus, 1f), $"Smoke Screen backstab {F(os.m_backstabBonus)}, expected 1");
        c.Check(os.m_attackForce == 0f, "Smoke Screen attack force not 0");
        c.Check(os.m_attackStatusEffect == null, "Smoke Screen attack status effect not empty");
        c.Check(os.m_icons != null && os.m_icons.Length > 0 && vs.m_icons != null && vs.m_icons.Length > 0
                && os.m_icons[0] == vs.m_icons[0], "Smoke Screen icon must be the vanilla Smoke Bomb's");
        var a = os.m_attack;
        if (c.Check(a != null, "Smoke Screen without primary attack"))
        {
            c.Check(a.m_attackHitNoise == 0f && a.m_attackStartNoise == 0f,
                $"throw noise: start {F(a.m_attackStartNoise)}, hit {F(a.m_attackHitNoise)}");
            c.Check(a.m_attackProjectile == SmokeContent.ProjectilePrefab, "the throw launches another projectile");
            c.Check(a.m_spawnOnHit == null, "the throw has an attack-level spawn (would replace the cloud)");
            c.Check(a.m_consumeItem, "a throw must use up the Smoke Screen");
            c.Check(vs.m_attack != null && a.m_attackAnimation == vs.m_attack.m_attackAnimation,
                $"throw animation '{a.m_attackAnimation}' differs from the vanilla bomb's");
        }
        c.Check(ours.m_itemData.m_dropPrefab == itemGo, "Smoke Screen drop prefab");
        c.Check(SmokeContent.IsSmokeScreen(ours.m_itemData), "IsSmokeScreen false for the Smoke Screen");
        c.Check(!SmokeContent.IsSmokeScreen(bomb.m_itemData), "IsSmokeScreen true for the vanilla Smoke Bomb");

        // Projectile: one projectile, one cloud.
        var pGo = SmokeContent.ProjectilePrefab;
        var p = pGo != null ? pGo.GetComponent<Projectile>() : null;
        if (c.Check(p != null, "our projectile has no Projectile"))
        {
            c.Check(p.m_spawnOnHit == SmokeContent.CloudPrefab, "our projectile does not spawn our cloud");
            c.Check(!p.m_stayAfterHitStatic && !p.m_stayAfterHitDynamic && !p.m_attachToRigidBody && !p.m_attachToClosestBone
                    && !p.m_onlyStopOnTerrain && !p.m_respawnItemOnHit && !p.m_bounceOnWater,
                $"our projectile can stay, attach, pass through, bounce on water or give the item back: {ProjectileValues(p)}");
            c.Check(p.m_canHitWater && p.m_spawnOnTtl && p.m_spawnOnTerrain && p.m_spawnOnCharacters && p.m_spawnOnWearNTear,
                $"our projectile must spawn on water, TTL, terrain, creatures and pieces: {ProjectileValues(p)}");
            c.Check(p.m_spawnItem == null && (p.m_randomSpawnOnHit == null || p.m_randomSpawnOnHit.Count == 0)
                    && p.m_spawnCount == 1 && p.m_spawnOnHitChance >= 1f && !p.m_groundHitOnly && !p.m_staticHitOnly,
                $"our projectile spawn rules: {ProjectileValues(p)}");
            c.Check(p.m_damage.GetTotalDamage() == 0f && p.m_aoe == 0f && p.m_hitNoise == 0f && string.IsNullOrEmpty(p.m_statusEffect),
                $"our projectile must be harmless and silent: {ProjectileValues(p)}");
            var harmful = HarmfulEffects(p);
            c.Check(harmful.Length == 0, $"networked or harmful hit effects kept: {harmful}");
        }

        // Cloud prefab: persistent network object, no collider.
        var cloud = SmokeContent.CloudPrefab;
        var view = cloud != null ? cloud.GetComponent<ZNetView>() : null;
        c.Check(view != null && view.m_persistent, "cloud ZNetView must be persistent");
        c.Check(cloud != null && cloud.GetComponent<SmokeCloud>() != null, "cloud without SmokeCloud");
        c.Check(cloud != null && cloud.GetComponentsInChildren<Collider>(true).Length == 0, "cloud must have no collider");
        c.Check(cloud != null && cloud.layer != LayerMask.NameToLayer("smoke"), "cloud must not be on the smoke layer");
    }

    private static void CheckNames(Checks c, ObjectDB db, ZNetScene scene, Player player)
    {
        foreach (var name in NeededPrefabs)
        {
            c.Check(scene.GetPrefab(name) != null, $"prefab {name} not found");
        }
        var eikthyr = scene.GetPrefab("Eikthyr");
        var boss = eikthyr != null ? eikthyr.GetComponent<Character>() : null;
        c.Check(boss != null && boss.IsBoss(), "Eikthyr must be a boss (Character.m_boss)");
        var greydwarf = scene.GetPrefab("Greydwarf");
        var gd = greydwarf != null ? greydwarf.GetComponent<Character>() : null;
        c.Check(gd != null && Near(gd.m_health, 40f), $"Greydwarf base health {(gd != null ? F(gd.m_health) : "?")}, the XP table uses 40");
        c.Check(db.GetStatusEffect("Wet".GetStableHashCode()) != null, "status effect Wet not found (fog icon)");
        c.Check(LayerMask.NameToLayer("viewblock") >= 0, "layer viewblock not found (foliage cue)");
        var animator = player.m_animator;
        c.Check(HasParameter(animator, "throw_bomb", AnimatorControllerParameterType.Trigger),
            "player animator has no throw_bomb trigger");
        c.Check(HasParameter(animator, "crouching", AnimatorControllerParameterType.Bool),
            "player animator has no crouching bool");
        var item = SmokeContent.ItemPrefab;
        var drop = item != null ? item.GetComponent<ItemDrop>() : null;
        var anim = drop != null && drop.m_itemData.m_shared.m_attack != null ? drop.m_itemData.m_shared.m_attack.m_attackAnimation : "";
        c.Check(HasParameter(animator, anim, AnimatorControllerParameterType.Trigger),
            $"the Smoke Screen throw animation '{anim}' is no trigger of the player animator");
        var sneak = player.GetSkills().GetSkill(Skills.SkillType.Sneak);
        c.Check(sneak != null && sneak.m_info != null && sneak.m_info.m_increseStep > 0f, "Sneak skill has no raise step");
    }

    private static void NoteGameValues(Checks c, ObjectDB db, Player player)
    {
        var bombGo = db.GetItemPrefab(SmokeContent.VanillaBombName);
        var bomb = bombGo != null ? bombGo.GetComponent<ItemDrop>() : null;
        var vanillaProjectile = bomb != null && bomb.m_itemData.m_shared.m_attack != null
            ? bomb.m_itemData.m_shared.m_attack.m_attackProjectile
            : null;
        var vp = vanillaProjectile != null ? vanillaProjectile.GetComponent<Projectile>() : null;
        c.Note($"vanilla projectile {(vanillaProjectile != null ? vanillaProjectile.name : "none")}: {ProjectileValues(vp)}; "
               + $"components [{Components(vanillaProjectile, false)}]");
        if (vp != null)
        {
            c.Note($"vanilla projectile effects: hit {Effects(vp.m_hitEffects)}; water {Effects(vp.m_hitWaterEffects)}; "
                   + $"spawn {Effects(vp.m_spawnOnHitEffects)}");
            var explosion = vp.m_spawnOnHit;
            var renderers = explosion != null
                ? string.Join(", ", explosion.GetComponentsInChildren<ParticleSystemRenderer>(true)
                    .Select(r => $"{r.name}:{(r.sharedMaterial != null ? r.sharedMaterial.name : "no material")}").ToArray())
                : "";
            c.Note($"vanilla explosion {(explosion != null ? explosion.name : "none")}: components "
                   + $"[{Components(explosion, true)}], particle renderers [{renderers}]");
        }
        var ourProjectile = SmokeContent.ProjectilePrefab != null ? SmokeContent.ProjectilePrefab.GetComponent<Projectile>() : null;
        if (ourProjectile != null)
        {
            c.Note($"our projectile keeps: hit {Effects(ourProjectile.m_hitEffects)}; water {Effects(ourProjectile.m_hitWaterEffects)}; "
                   + $"spawn {Effects(ourProjectile.m_spawnOnHitEffects)}");
        }
        var material = SmokeContent.SmokeMaterial;
        c.Note($"cloud smoke material: {(material != null ? material.name : "none (clouds show no smoke)")}, "
               + $"shader {(material != null && material.shader != null ? material.shader.name : "-")}");
        var vanillaRecipe = bomb != null ? db.m_recipes.FirstOrDefault(r => r != null && r.m_item == bomb) : null;
        c.Note($"vanilla Smoke Bomb recipe: {RecipeText(vanillaRecipe)}");
        var stealth = StealthSystem.instance;
        c.Note(stealth != null
            ? $"StealthSystem light range {F(stealth.m_minLightLevel)}-{F(stealth.m_maxLightLevel)}, shadow mask "
              + $"[{LayerNames(stealth.m_shadowTestMask.value)}]"
            : "no StealthSystem");
        c.Note($"layer viewblock = {LayerMask.NameToLayer("viewblock")}; creature sight mask [{LayerNames(ViewMask)}]");
        var sneak = player.GetSkills().GetSkill(Skills.SkillType.Sneak);
        c.Note($"Sneak raise step {(sneak != null && sneak.m_info != null ? F(sneak.m_info.m_increseStep) : "?")}, "
               + $"skill gain rate {F(Game.m_skillGainRate)}");
        var icons = new StringBuilder();
        foreach (CueKind kind in Enum.GetValues(typeof(CueKind)))
        {
            var template = StealthCues.Template(player, kind);
            var icon = template != null ? template.m_icon : null;
            icons.Append(kind).Append(' ').Append(icon != null ? icon.name : "none").Append("; ");
            c.Check(icon != null, $"cue {kind} has no icon");
        }
        c.Note($"cue icons: {icons}");
        c.Note($"Karve (ship for T08) {(ZNetScene.instance.GetPrefab("Karve") != null ? "found" : "NOT found")}");
        var guids = Chainloader.PluginInfos.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        c.Note($"{guids.Length} plugins loaded: {string.Join(", ", guids)}");
    }

    // ---------- sneak.network (pure) ----------

    private static IEnumerator RunNetwork()
    {
        var c = new Checks(NetworkName);
        var custom = new AmbushRules
        {
            SneakAttackXpFlat = 4.5f, SneakAttackXp = 12f, ReferenceHealth = 150f, MaxHealthScale = 2f,
            RangedXpFactor = 0.25f, SneakAttackXpCooldown = 120f, PayAlongsideOtherSneakXpMods = true,
            EarlyGameBonus = 20, StillBonus = 60, StillDelay = 2f, StillEndsAtOnce = false, FoliageBonus = 10,
            FoliageReach = 0.8f, FogBonus = 40, FogDensityNoBonus = 0.02f, FogDensityFullBonus = 0.1f,
            VisibilityFloor = 0.2f, RecipeResources = "Resin:3,Coal:2", RecipeAmount = 3, RecipeStation = "forge",
            RecipeStationLevel = 2, CloudRadius = 5f, CloudHeight = 6f, CloudDuration = 20f, ActivationDelay = 1f,
            InsideSightRange = 3f, BlocksLineOfSight = false, BlocksHearing = false, RevealSeconds = 10f,
            BlindMargin = 6f, BlindSeconds = 7f, ForgetSeconds = 4f, HealthBars = HealthBarMode.ThroughSmoke,
        };
        var pkg = new ZPackage();
        custom.Write(pkg);
        pkg.SetPos(0);
        c.Check(AmbushRules.TryRead(pkg, out var back, out var clamped) && !clamped && back.Describe() == custom.Describe()
                && back.HealthBars == HealthBarMode.ThroughSmoke && !back.StillEndsAtOnce && back.PayAlongsideOtherSneakXpMods
                && !back.BlocksHearing && !back.IsPending,
            "rules survive the wire unchanged");

        var wild = new AmbushRules
        {
            StillBonus = 150, EarlyGameBonus = 40, CloudRadius = 0.5f, CloudDuration = 999f,
            FogDensityNoBonus = float.NaN, SneakAttackXp = -3f, RecipeResources = new string('x', 1500),
            HealthBars = (HealthBarMode)7, RecipeAmount = 0, ReferenceHealth = float.PositiveInfinity,
        };
        pkg = new ZPackage();
        wild.Write(pkg);
        pkg.SetPos(0);
        var d = AmbushRules.Default;
        c.Check(AmbushRules.TryRead(pkg, out back, out clamped) && clamped && back.StillBonus == AmbushRules.BonusMax
                && back.EarlyGameBonus == AmbushRules.EarlyGameBonusMax && Near(back.CloudRadius, AmbushRules.CloudSizeMin)
                && Near(back.CloudDuration, AmbushRules.CloudDurationMax) && Near(back.FogDensityNoBonus, d.FogDensityNoBonus)
                && back.SneakAttackXp == 0f && back.RecipeResources.Length == AmbushRules.TextMax
                && back.HealthBars == d.HealthBars && back.RecipeAmount == AmbushRules.RecipeAmountMin
                && Near(back.ReferenceHealth, d.ReferenceHealth),
            "out-of-range, NaN, infinite and unknown values from the wire are pulled into range");

        pkg = new ZPackage();
        pkg.Write(AmbushRules.Layout + 1);
        pkg.Write(1f);
        pkg.SetPos(0);
        c.Check(!AmbushRules.TryRead(pkg, out _, out _), "unknown rules layout refused");
        pkg = new ZPackage();
        pkg.Write(AmbushRules.Layout);
        pkg.Write(3f);
        pkg.SetPos(0);
        c.Check(!AmbushRules.TryRead(pkg, out _, out _), "cut-off rules package refused");
        c.Check(AmbushRules.Pending.IsPending && !AmbushRules.Default.IsPending && !AmbushRules.Own().IsPending,
            "only the built-in Pending rules are pending");

        // Cloud timeline: build-up part of the duration, cut to leave 1 s of hiding; blind never outlive cloud + fade.
        c.Check(Near(SmokeCloud.BuildUp(15f, d), 0.5f)
                && Near(SmokeCloud.BuildUp(3f, new AmbushRules { ActivationDelay = 3f }), 3f - SmokeCloud.MinHideSeconds)
                && Near(SmokeCloud.BuildUp(4f, new AmbushRules { ActivationDelay = 3f }), 3f),
            "build-up: 0.5 s by default, 3 s cut to 2 s in a 3 s cloud, kept in a 4 s cloud");
        c.Check(Near((float)(SmokeCloud.BlindEnd(100d, 15f, d) - 100d), 6f)
                && Near((float)(SmokeCloud.BlindEnd(100d, 3f, new AmbushRules { BlindSeconds = 30f }) - 100d),
                    3f + SmokeCloud.FadeSeconds)
                && Near((float)(SmokeCloud.BlindEnd(100d, 60f, new AmbushRules { BlindSeconds = 30f }) - 100d), 30f),
            "blind end: 6 s by default, a 30 s blind cut to cloud + fade in a 3 s cloud, kept in a 60 s cloud");

        // A server (this single-player world is one) never takes rules from a peer.
        var current = ServerRules.Current;
        pkg = new ZPackage();
        custom.Write(pkg);
        pkg.SetPos(0);
        c.Check(!ServerRules.Receive(pkg) && ReferenceEquals(ServerRules.Current, current) && !ServerRules.UsingServer
                && !ServerRules.IsPending,
            "single player / server took rules from a peer or is pending");

        // Which rules apply.
        var own = new AmbushRules();
        c.Check(ReferenceEquals(ServerRules.Select(false, custom, own), own), "single player, host, server: own rules");
        c.Check(ReferenceEquals(ServerRules.Select(false, null, own), own), "server without peer rules: own rules");
        c.Check(ReferenceEquals(ServerRules.Select(true, custom, own), custom), "client with server rules: the server's");
        c.Check(ServerRules.Select(true, null, own).IsPending, "client without (readable) server rules: pending");

        // Join check.
        c.Check(PlayerCheck.Decide(true, true, true, false, true, false) == JoinVerdict.Compatible, "compatible -> Compatible");
        c.Check(PlayerCheck.Decide(true, true, true, false, false, false) == JoinVerdict.Refuse,
            "not compatible (no mod, turned off, other network version) -> Refuse");
        c.Check(PlayerCheck.Decide(true, true, true, false, false, true) == JoinVerdict.Allowed,
            "not compatible with AllowPlayersWithoutMod -> Allowed");
        c.Check(PlayerCheck.Decide(false, true, true, false, false, false) == JoinVerdict.Skip, "not a server -> Skip");
        c.Check(PlayerCheck.Decide(true, false, true, false, false, false) == JoinVerdict.Skip, "not connected -> Skip");
        c.Check(PlayerCheck.Decide(true, true, false, false, false, false) == JoinVerdict.Skip, "not ready -> Skip");
        c.Check(PlayerCheck.Decide(true, true, true, true, false, false) == JoinVerdict.Skip, "already being kicked -> Skip");
        c.Report();
        yield break;
    }

    // ---------- sneak.curve ----------

    private static void CheckStealthMath(Checks c)
    {
        var d = new AmbushRules();
        c.Check(Near(StealthState.Early(d, 0f), 0.85f) && Near(StealthState.Early(d, 0.5f), 0.925f) && Near(StealthState.Early(d, 1f), 1f),
            "early(s) = 1 - 15% x (1 - s)");
        // Highest setting: full-light curve still falls with skill (design 2.2).
        var max = new AmbushRules { EarlyGameBonus = AmbushRules.EarlyGameBonusMax };
        var last = 2f;
        var falling = true;
        for (var i = 0; i <= 20; i++)
        {
            var s = i / 20f;
            var v = Mathf.Lerp(1f, 0.6f, s) * StealthState.Early(max, s);
            falling &= v <= last + 1e-5f;
            last = v;
        }
        c.Check(falling, "with EarlyGameBonus 25 the full-light factor must keep falling with skill");
        c.Check(Near(StealthState.Apply(0.5f, 0.3f, 0.1f, out var capped), 0.15f) && !capped, "0.5 x 0.3 = 0.15, not capped");
        c.Check(Near(StealthState.Apply(0.2f, 0.3f, 0.1f, out capped), 0.1f) && capped, "0.2 x 0.3 floored to 0.1");
        c.Check(Near(StealthState.Apply(0.05f, 0.5f, 0.1f, out capped), 0.05f) && capped, "floor never above before");
        c.Check(Near(StealthState.Apply(0.6f, 1f, 0.1f, out capped), 0.6f) && !capped, "no bonus = unchanged");
        c.Check(Near(StealthState.Apply(0.425f, 0.3f * 0.7f, 0.1f, out capped), 0.1f) && capped,
            "Sneak 0, night, still, thick fog = floor 0.1 (design 2.5 table)");
        c.Check(Near(StealthState.FogShareFor(0.01f, d), 0f) && Near(StealthState.FogShareFor(0.08f, d), 1f)
                && Near(StealthState.FogShareFor(0.045f, d), 0.5f) && Near(StealthState.FogShareFor(0.3f, d), 1f)
                && Near(StealthState.FogShareFor(0.005f, d), 0f),
            "fog share 0 at 0.01, half at 0.045, full from 0.08");
        var odd = new AmbushRules { FogDensityNoBonus = 0.05f, FogDensityFullBonus = 0.03f };
        c.Check(Near(StealthState.FogShareFor(0.06f, odd), 1f) && Near(StealthState.FogShareFor(0.05f, odd), 0f),
            "full density not above no-bonus density: any thicker fog gives the full bonus");
    }

    private static IEnumerator RunCurve()
    {
        var c = new Checks(CurveName);
        CheckStealthMath(c);
        var rig = Rig.Create(CurveName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            ServerRules.TestRules = new AmbushRules { StillBonus = 0, FogBonus = 0, FoliageBonus = 0 };
            rig.SetTime(0.5f);
            rig.SetSneak(0f);
            var crouched = new Box();
            yield return Crouch(player, crouched);
            if (c.Check(crouched.Ok, "the player did not crouch"))
            {
                yield return new WaitForSeconds(0.3f);
                yield return ForceRefresh(player);
                var light = LightFactor(player);
                var t0 = player.m_stealthFactorTarget;
                var v0 = VanillaTarget(player);
                c.Check(Near(t0, v0 * 0.85f, 0.01f), $"Sneak 0: target {F(t0)}, vanilla {F(v0)} x 0.85 = {F(v0 * 0.85f)}");
                rig.SetSneak(100f);
                yield return ForceRefresh(player);
                var t1 = player.m_stealthFactorTarget;
                var v1 = VanillaTarget(player);
                c.Check(Near(t1, v1, 0.01f), $"Sneak 100: target {F(t1)}, vanilla {F(v1)}");
                c.Note($"noon, crouched: light factor {F(light)}; Sneak 0 target {F(t0)} (vanilla {F(v0)}); "
                       + $"Sneak 100 target {F(t1)} (vanilla {F(v1)})");
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.still ----------

    private static IEnumerator RunStill()
    {
        var c = new Checks(StillName);
        var rig = Rig.Create(StillName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            var rules = new AmbushRules { FogBonus = 0 };
            ServerRules.TestRules = rules;
            StealthCues.TestShowCues = true;
            rig.SetTime(0.5f);
            rig.SetSneak(0f);
            var dir = ClearDirection(player, 6f, out var clear);
            if (!clear)
            {
                c.Note("no open ground found around the player; the test walks toward the camera anyway");
            }
            rig.TakeControls();
            rig.Aim(dir, 0f);
            var crouched = new Box();
            yield return Crouch(player, crouched);
            if (!c.Check(crouched.Ok, "the player did not crouch"))
            {
                c.Report();
                yield break;
            }

            // 1. Hold still: icon, bonus in the target.
            yield return new WaitForSeconds(3f);
            var w = StealthState.LastWithoutStill;
            var target = player.m_stealthFactorTarget;
            c.Check(StealthState.StillActive, "holding still 3 s: no still bonus");
            c.Check(StealthCues.Has(player, CueKind.Still), "holding still 3 s: no Holding still icon");
            var text = StealthCues.IconText(CueKind.Still);
            c.Check(text == "-70%", $"Holding still icon text '{text}', expected '-70%'");
            c.Check(Near(target, WithStill(w, rules), 0.01f), $"still target {F(target)}, expected {F(WithStill(w, rules))} (0.3 x {F(w)})");

            // 2. Walk: stillness ends this very step, factor snaps up to the value without it, icon gone.
            var factorBefore = player.m_stealthFactor;
            c.Check(factorBefore < w - 0.05f, $"the bar did not sink while still ({F(factorBefore)} vs {F(w)} without the bonus)");
            var snapped = false;
            var factorAfter = 0f;
            var targetAfter = 0f;
            var cueGone = false;
            var sneaking = false;
            var speed = 0f;
            var start = player.transform.position;
            var until = Time.time + 0.5f;
            rig.MarkMoved();
            while (Time.time < until)
            {
                rig.Drive(Vector3.forward);
                yield return new WaitForFixedUpdate();
                sneaking |= player.IsSneaking();
                speed = Mathf.Max(speed, player.m_currentVel.magnitude);
                if (!snapped && !StealthState.StillActive)
                {
                    snapped = true;
                    factorAfter = player.m_stealthFactor;
                    targetAfter = player.m_stealthFactorTarget;
                    cueGone = !StealthCues.Has(player, CueKind.Still);
                }
            }
            rig.Drive(Vector3.zero);
            var walked = Vector3.Distance(start, player.transform.position);
            c.Note($"Player.SetControls drove the crouched player {F(walked)} m in 0.5 s (top move speed {F(speed)}, "
                   + $"sneaking seen {sneaking})");
            if (c.Check(snapped, $"walking 0.5 s did not end the still bonus (moved {F(walked)} m)"))
            {
                var expect = Mathf.Min(w, targetAfter);
                c.Check(factorAfter >= expect - 0.02f,
                    $"stillness end: factor {F(factorBefore)} -> {F(factorAfter)} the same step, expected at least {F(expect)}");
                c.Check(cueGone, "stillness end: Holding still icon still there the same step");
            }

            // 3. Carried (moved by something else, no walking): never still.
            yield return new WaitForSeconds(0.3f);
            var carry = ClearDirection(player, 5f, out _, null);
            var anyStill = false;
            var calm = 0;
            var samples = 0;
            var stepAt = Time.time;
            var carryUntil = Time.time + 2.5f;
            while (Time.time < carryUntil)
            {
                if (Time.time >= stepAt)
                {
                    stepAt += 0.25f;
                    var next = player.transform.position + carry * 0.4f;
                    var ground = ZoneSystem.instance.GetGroundHeight(next);
                    next.y = Mathf.Max(next.y, ground + 0.02f);
                    MovePlayer(player, next);
                }
                yield return new WaitForFixedUpdate();
                samples++;
                if (player.IsCrouching() && player.IsOnGround() && !player.IsSneaking())
                {
                    calm++;
                }
                anyStill |= StealthState.StillActive;
            }
            c.Check(!anyStill, "carried 0.4 m every 0.25 s: the still bonus came on");
            c.Check(!StealthCues.Has(player, CueKind.Still), "carried: Holding still icon shown");
            c.Note($"carried: {calm} of {samples} physics steps crouched, on the ground and not walking (only the "
                   + "displacement rule could stop the bonus there)");

            // 4. Still again, then the server's number changes: same icon, new text.
            yield return new WaitForSeconds(2.5f);
            var seman = player.GetSEMan();
            var hash = StealthCues.NameHash(CueKind.Still);
            var shown = seman.GetStatusEffect(hash);
            if (c.Check(shown != null, "still again 2.5 s: no Holding still icon"))
            {
                c.Check(shown.GetIconText() == "-70%", $"icon text '{shown.GetIconText()}' before the rule change");
                var rules50 = new AmbushRules { FogBonus = 0, StillBonus = 50 };
                ServerRules.TestRules = rules50;
                yield return null;
                c.Check(shown.GetIconText() == "-50%", $"icon text '{shown.GetIconText()}' after StillBonus 50, expected '-50%'");
                yield return new WaitForSeconds(0.7f);
                c.Check(ReferenceEquals(seman.GetStatusEffect(hash), shown), "the icon was removed or added again for the rule change");
                var w50 = StealthState.LastWithoutStill;
                c.Check(Near(player.m_stealthFactorTarget, WithStill(w50, rules50), 0.01f),
                    $"StillBonus 50 target {F(player.m_stealthFactorTarget)}, expected {F(WithStill(w50, rules50))}");
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.cover ----------

    private static IEnumerator RunCover()
    {
        var c = new Checks(CoverName);
        var rig = Rig.Create(CoverName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            var rules = new AmbushRules { StillBonus = 0 };
            ServerRules.TestRules = rules;
            StealthCues.TestShowCues = true;
            rig.SetTime(0.5f);
            // Sneak 0: the factor stays well above the floor, so icon numbers are never "max".
            rig.SetSneak(0f);
            var dir = ClearDirection(player, 14f, out _);
            var crouched = new Box();
            yield return Crouch(player, crouched);
            if (!c.Check(crouched.Ok, "the player did not crouch"))
            {
                c.Report();
                yield break;
            }

            // Bush beside player: stem next to player capsule (not in it), on what player stand on, like player crouch
            // against bush. Bush spawned on player feet lift player onto its stem (first run: centre 1.74 m over root,
            // above bush viewblock sphere, nothing to find, nothing to block).
            var feet = player.transform.position;
            var bushAt = feet + dir * (player.GetRadius() + BushStemRadius + 0.1f);
            bushAt.y = ZoneSystem.instance.GetSolidHeight(bushAt, out var solid, 2) ? solid : Ground(bushAt).y;
            var bush = rig.Spawn("Bush01", bushAt, Quaternion.identity);
            if (c.Check(bush != null, "could not spawn Bush01"))
            {
                yield return new WaitForSeconds(1.2f);
                var center = player.GetCenterPoint();
                var moved = player.transform.position - feet;
                c.Note($"Bush01 colliders: {Colliders(bush)}; bush root {V(bush.transform.position - feet)} from the "
                       + $"player's feet (player radius {F(player.GetRadius())}, centre {F(center.y - player.transform.position.y)} m "
                       + $"above the feet), player centre {F(Vector3.Distance(center, bush.transform.position))} m from the bush "
                       + $"root, player moved {V(moved)}, touching {StealthState.FoliageTouching}, crown {StealthState.FoliageCrown}");
                c.Check(StealthState.FoliageTouching || StealthState.FoliageCrown, "at Bush01: no foliage found");
                c.Check(StealthCues.Has(player, CueKind.Foliage), "at Bush01: no In foliage icon");
                c.Check(StealthCues.IconText(CueKind.Foliage) == "", $"In foliage icon shows a number with FoliageBonus 0: "
                                                                     + $"'{StealthCues.IconText(CueKind.Foliage)}'");
                c.Check(Near(StealthState.FoliageFactor(rules), 1f), "FoliageBonus 0 must add nothing to the factor");

                // Creature sight ray from 12 m through bush to crouched centre, from bush side (along open direction when
                // bush stand right under centre).
                var offset = bush.transform.position - center;
                offset.y = 0f;
                var through = offset.magnitude > 0.2f ? offset.normalized : dir;
                var origin = center + through * 12f;
                var ray = center - origin;
                var hits = Physics.RaycastAll(origin, ray.normalized, ray.magnitude, ViewMask);
                var byBush = hits.Any(h => h.collider != null && h.collider.transform.IsChildOf(bush.transform));
                c.Note("ray from 12 m to the crouched centre hits: " + (hits.Length == 0 ? "nothing" : string.Join(", ", hits
                    .OrderBy(h => h.distance)
                    .Select(h => $"{h.collider.name} ({LayerMask.LayerToName(h.collider.gameObject.layer)}) at {F(h.distance)} m")
                    .ToArray())));
                c.Check(byBush, "a creature's sight ray through Bush01 to the crouched player is not blocked by the bush");

                // FoliageBonus 30: x 0.7 while touching.
                yield return ForceRefresh(player);
                var t0 = player.m_stealthFactorTarget;
                var touching = StealthState.FoliageTouching;
                var rules30 = new AmbushRules { StillBonus = 0, FoliageBonus = 30 };
                ServerRules.TestRules = rules30;
                yield return ForceRefresh(player);
                var t1 = player.m_stealthFactorTarget;
                if (touching)
                {
                    c.Check(Near(t1, t0 * 0.7f, 0.02f), $"FoliageBonus 30: target {F(t0)} -> {F(t1)}, expected x 0.7");
                    c.Check(StealthCues.IconText(CueKind.Foliage) == "-30%",
                        $"In foliage icon with FoliageBonus 30: '{StealthCues.IconText(CueKind.Foliage)}'");
                }
                else
                {
                    c.Note("the crouched centre is not within FoliageReach of Bush01 (crown only): FoliageBonus not applied");
                    c.Check(Near(t1, t0, 0.02f), $"FoliageBonus under a crown only changed the target {F(t0)} -> {F(t1)}");
                }
                ServerRules.TestRules = rules;
                rig.Remove(bush);
                yield return Frames(2);
            }

            // Under a tree crown.
            var tree = rig.Spawn("Beech1", Ground(player.transform.position + dir * 2f), Quaternion.identity);
            if (c.Check(tree != null, "could not spawn Beech1"))
            {
                yield return new WaitForSeconds(1.2f);
                var center = player.GetCenterPoint();
                var crown = Physics.Raycast(center, Vector3.up, out var hit, 20f, ViewblockLayerMask, QueryTriggerInteraction.UseGlobal);
                c.Note($"Beech1 colliders: {Colliders(tree)}; crown ray from the crouched centre: "
                       + (crown ? $"hit {hit.collider.name} at {F(hit.distance)} m" : "missed"));
                if (crown)
                {
                    c.Check(StealthState.FoliageTouching || StealthState.FoliageCrown, "under Beech1: crown ray hits but no foliage found");
                    c.Check(StealthCues.Has(player, CueKind.Foliage), "under Beech1: no In foliage icon");
                }
                else
                {
                    c.Note("under Beech1 the crown ray missed (crown shape unverified): no In foliage icon expected");
                }
                rig.Remove(tree);
                yield return Frames(2);
            }

            // Fog: our density from environment data = the rendered density; icon percent.
            c.Check(!ParticleMist.IsInMist(player.GetCenterPoint()), "In the Meadows the player is in mist");
            var envs = new List<KeyValuePair<string, float>>
            {
                new KeyValuePair<string, float>("Misty", 0f),
                new KeyValuePair<string, float>("Clear", 0.5f),
            };
            envs.AddRange(FogEnvs.Select(e => new KeyValuePair<string, float>(e, 0.5f)));
            var lines = new List<string>();
            foreach (var pair in envs)
            {
                if (EnvMan.instance.GetEnv(pair.Key) == null)
                {
                    c.Note($"environment {pair.Key} does not exist in this game version: skipped");
                    continue;
                }
                rig.SetEnv(pair.Key);
                rig.SetTime(pair.Value);
                var settled = new Box();
                yield return SettleEnv(pair.Key, settled);
                if (!c.Check(settled.Ok, $"environment {pair.Key} did not settle ({settled.Detail})"))
                {
                    continue;
                }
                yield return new WaitForSeconds(0.7f);
                var ours = StealthState.FogDensity;
                var render = RenderSettings.fogDensity;
                c.Check(StealthState.FogDensityKnown, $"{pair.Key}: fog density from data not known");
                // Another loaded mod may change the RENDERED fog (Distant Horizons thin clear weather): bonus never
                // follow it (game data only, design 2.4), so then me only note the two values.
                var fogMod = RenderedFogMod();
                if (fogMod == null)
                {
                    c.Check(Near(ours, render, 0.001f), $"{pair.Key}: fog density from data {F(ours)}, rendered {F(render)}");
                }
                else
                {
                    c.Note($"{pair.Key}: fog density from data {F(ours)}, rendered {F(render)} ({fogMod} changes the rendered fog)");
                }
                var pct = Mathf.RoundToInt(rules.FogBonus * StealthState.FogShareFor(ours, rules));
                var shown = StealthCues.Has(player, CueKind.Fog);
                if (pct >= 2)
                {
                    c.Check(shown, $"{pair.Key}: fog {pct}% but no Fog icon");
                    c.Check(StealthCues.IconText(CueKind.Fog) == $"-{pct}%",
                        $"{pair.Key}: Fog icon text '{StealthCues.IconText(CueKind.Fog)}', expected '-{pct}%'");
                }
                else if (pct < 1)
                {
                    c.Check(!shown, $"{pair.Key}: no fog bonus but a Fog icon");
                }
                if (pair.Key == "Clear")
                {
                    c.Check(!shown && pct == 0, $"Clear at noon: fog {pct}%, icon {shown}");
                }
                lines.Add($"{pair.Key} at {F(pair.Value)}: density {F(ours)} (rendered {F(render)}), fog bonus {pct}%, icon {shown}");
            }
            c.Note($"fog (weather blend {F(EnvMan.instance.m_transitionDuration)} s, switched at once): "
                   + string.Join("; ", lines.ToArray()));
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // Forced weather in place (transition over), then two frames of SetEnv with it. Blend still running = me ask
    // instant switch again (Rig.SetEnv ask first). Not in place = Detail say what EnvMan hold.
    private static IEnumerator SettleEnv(string name, Box result)
    {
        var env = EnvMan.instance;
        var until = Time.time + 6f;
        while (Time.time < until)
        {
            var current = env.GetCurrentEnvironment();
            if (env.m_nextEnv == null && current != null && current.m_name == name)
            {
                result.Ok = true;
                break;
            }
            if (env.m_nextEnv != null)
            {
                env.ForceInstantEnvironmentSwitch();
            }
            yield return null;
        }
        if (!result.Ok)
        {
            var current = env.GetCurrentEnvironment();
            result.Detail = $"current {(current != null ? current.m_name : "none")}, blending to "
                            + $"{(env.m_nextEnv != null ? env.m_nextEnv.m_name : "nothing")}, forced '{env.m_forceEnv}', "
                            + $"debug '{env.m_debugEnv}', transition {F(env.m_transitionTimer)} of "
                            + $"{F(env.m_transitionDuration)} s";
        }
        yield return null;
        yield return null;
    }

    // ---------- sneak.xp ----------

    private static void CheckXpMath(Checks c, AmbushRules r)
    {
        c.Check(Near(SneakXp.Amount(r, 40f, false), 7f) && Near(SneakXp.Amount(r, 10f, false), 4f)
                && Near(SneakXp.Amount(r, 5f, false), 3.5f) && Near(SneakXp.Amount(r, 20f, false), 5f)
                && Near(SneakXp.Amount(r, 100f, false), 13f) && Near(SneakXp.Amount(r, 600f, false), 33f),
            "XP table of design 2.1 (Neck 3.5, Boar 4, Greyling 5, Greydwarf 7, Draugr 13, Troll 33)");
        c.Check(Near(SneakXp.Amount(r, 5000f, false), 33f), "health part capped at x3");
        c.Check(Near(SneakXp.Amount(r, 40f, true), 3.5f), "ranged pays half");
        c.Check(Near(SneakXp.Amount(r, 0f, false), 3f), "flat part for 0 health");
        c.Check(SneakXp.Amount(r, -5f, false) == 0f && SneakXp.Amount(r, float.NaN, false) == 0f
                && SneakXp.Amount(r, float.PositiveInfinity, false) == 0f && SneakXp.Amount(r, float.NegativeInfinity, false) == 0f,
            "bad victim health pays nothing");
        c.Check(SneakXp.Amount(AmbushRules.Pending, 40f, false) == 0f, "pending rules pay nothing");
        c.Check(SneakXp.MessageText(false, 0.072f, false) == "Sneak attack! +7% Sneak"
                && SneakXp.MessageText(true, 0.5f, false) == "Sneak attack!"
                && SneakXp.MessageText(false, 0.004f, false) == "Sneak attack! +<1% Sneak"
                && SneakXp.MessageText(false, 0.1f, true) == "Sneak attack!",
            "message texts");
        c.Check(Compat.StandDownXp(new AmbushRules()) == Compat.OtherModPaysSneakXp
                && !Compat.StandDownXp(new AmbushRules { PayAlongsideOtherSneakXpMods = true }),
            "stand-down only for another sneak-XP mod, never with PayAlongsideOtherSneakXpMods");
    }

    private static IEnumerator RunXp()
    {
        var c = new Checks(XpName);
        var rules = new AmbushRules();
        CheckXpMath(c, rules);
        var rig = Rig.Create(XpName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            if (Compat.OtherModPaysSneakXp)
            {
                rules = new AmbushRules { PayAlongsideOtherSneakXpMods = true };
                c.Note("another sneak-XP mod is installed: this test pays alongside it (PayAlongsideOtherSneakXpMods)");
            }
            ServerRules.TestRules = rules;
            SneakXp.TestShowMessage = true;
            rig.SetSneak(20f);
            var skill = player.GetSkills().GetSkill(Skills.SkillType.Sneak);
            var step = skill.m_info.m_increseStep;
            var multiplier = 1f;
            player.GetSEMan().ModifyRaiseSkill(Skills.SkillType.Sneak, ref multiplier);
            float Gain(float xp) => step * xp * Game.m_skillGainRate * multiplier;

            yield return Stand(player);
            var dir = ClearDirection(player, 6f, out _);
            var side = Vector3.Cross(Vector3.up, dir).normalized;
            var origin = player.transform.position;
            // Behind the creature (it faces away): Creature Morale's calm creatures only lose the backstab when they see.
            var a = rig.Creature("Greydwarf", Ground(origin + dir * 2.5f), dir, false);
            if (!c.Check(a != null, "could not spawn a Greydwarf"))
            {
                c.Report();
                yield break;
            }
            yield return Frames(3);
            var aiA = a.GetBaseAI();
            var zdoA = a.m_nview.GetZDO();
            c.Check(!aiA.IsAlerted(), "the fresh Greydwarf is alerted");
            var victimHealth = a.m_health * Mathf.Max(1, a.GetLevel());
            var xp = SneakXp.Amount(rules, victimHealth, false);
            c.Check(Near(xp, 7f), $"a level-1 Greydwarf ({F(victimHealth)} health) should pay 7 XP, pays {F(xp)}");

            // 1. Sneak attack: XP, LastXp, message.
            var acc = skill.m_accumulator;
            var bt = a.m_backstabTime;
            var queuedBefore = QueuedCount("Sneak attack!");
            Hit(a, player, Slash(1f), 3f);
            // Same frame: routed RPCs run at once on this game (owner of both), message queued, not shown yet.
            var queuedAfter = QueuedCount("Sneak attack!");
            var gained = skill.m_accumulator - acc;
            c.Check(a.m_backstabTime != bt, "the first hit on the unaware Greydwarf was no vanilla backstab");
            c.Check(Near(gained, Gain(xp), 0.001f), $"first sneak attack: progress +{F(gained)}, expected +{F(Gain(xp))}");
            c.Check(zdoA.GetLong(SneakXp.LastXpKey, 0L) != 0L, "LastXp not written on the creature");
            if (MessageHudWorks)
            {
                var expected = SneakXp.MessageText(false, gained / skill.GetNextLevelRequirement(), false);
                c.Check(QueuedCount(expected) > 0 && queuedAfter == queuedBefore + 1,
                    $"message '{expected}' not shown (top-left queue: {queuedBefore} -> {queuedAfter} 'Sneak attack!')");
            }
            else
            {
                c.Note("HUD hidden: message not checked");
            }

            // 2. Unalerted again within vanilla's 300 s: no backstab, no XP.
            Unalert(aiA);
            Place(a, a.transform.position, dir);
            acc = skill.m_accumulator;
            bt = a.m_backstabTime;
            Hit(a, player, Slash(1f), 3f);
            c.Check(a.m_backstabTime == bt, "second hit within vanilla's 300 s was a backstab");
            c.Check(Near(skill.m_accumulator, acc), "second hit paid XP");

            // 3. Vanilla cooldown over (reload, new owner), ZDO cooldown not: backstab, no XP.
            Unalert(aiA);
            Place(a, a.transform.position, dir);
            a.m_backstabTime = Time.time - 301f;
            bt = a.m_backstabTime;
            acc = skill.m_accumulator;
            Hit(a, player, Slash(1f), 3f);
            c.Check(a.m_backstabTime != bt, "no backstab once vanilla's cooldown was over");
            c.Check(Near(skill.m_accumulator, acc), "the creature's XP cooldown (ZDO) did not hold");

            // 4. Both over: XP again.
            Unalert(aiA);
            Place(a, a.transform.position, dir);
            a.m_backstabTime = Time.time - 301f;
            bt = a.m_backstabTime;
            zdoA.Set(SneakXp.LastXpKey, NowMs() - 301000L);
            acc = skill.m_accumulator;
            Hit(a, player, Slash(1f), 3f);
            c.Check(a.m_backstabTime != bt, "fourth hit: no backstab");
            c.Check(Near(skill.m_accumulator - acc, Gain(xp), 0.001f),
                $"both cooldowns over: progress +{F(skill.m_accumulator - acc)}, expected +{F(Gain(xp))}");

            // The handler through the real routed RPC on the local player: bad payloads ignored, good ones paid.
            foreach (var bad in new[] { -5f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                acc = skill.m_accumulator;
                player.m_nview.InvokeRPC(SneakXp.Rpc, bad, false);
                c.Check(Near(skill.m_accumulator, acc), $"payload {F(bad)} paid XP");
            }
            acc = skill.m_accumulator;
            player.m_nview.InvokeRPC(SneakXp.Rpc, 40f, false);
            c.Check(Near(skill.m_accumulator - acc, Gain(7f), 0.001f), "payload 40 health (melee) did not pay 7 XP");
            acc = skill.m_accumulator;
            player.m_nview.InvokeRPC(SneakXp.Rpc, 40f, true);
            c.Check(Near(skill.m_accumulator - acc, Gain(3.5f), 0.001f), "payload 40 health (ranged) did not pay 3.5 XP");

            // 5. Ranged sneak attack on a fresh Greydwarf: half.
            var b = rig.Creature("Greydwarf", Ground(origin + side * 2.5f), side, false);
            if (c.Check(b != null, "could not spawn a second Greydwarf"))
            {
                yield return Frames(3);
                acc = skill.m_accumulator;
                bt = b.m_backstabTime;
                Hit(b, player, Pierce(1f), 3f, ranged: true);
                c.Check(b.m_backstabTime != bt, "ranged hit on the unaware Greydwarf was no backstab");
                c.Check(Near(skill.m_accumulator - acc, Gain(3.5f), 0.001f),
                    $"ranged sneak attack: progress +{F(skill.m_accumulator - acc)}, expected +{F(Gain(3.5f))}");
            }

            // 6. Tamed Boar and training dummy: nothing.
            var boar = rig.Creature("Boar", Ground(origin - side * 2.5f), -side, false);
            var dummy = rig.Creature("piece_TrainingDummy", Ground(origin - dir * 2.5f), -dir, false);
            yield return Frames(3);
            if (c.Check(boar != null, "could not spawn a Boar"))
            {
                if (boar.GetBaseAI() is MonsterAI boarAi)
                {
                    boarAi.MakeTame();
                }
                c.Check(boar.IsTamed(), "the Boar is not tamed");
                acc = skill.m_accumulator;
                bt = boar.m_backstabTime;
                Hit(boar, player, Slash(1f), 3f);
                c.Check(Near(skill.m_accumulator, acc), "a tamed Boar paid sneak-attack XP");
                c.Check(boar.m_nview.GetZDO().GetLong(SneakXp.LastXpKey, 0L) == 0L, "LastXp written on a tamed Boar");
                c.Note($"tamed Boar: vanilla backstab {(boar.m_backstabTime != bt ? "happened" : "did not happen")}");
            }
            if (c.Check(dummy != null, "could not spawn piece_TrainingDummy"))
            {
                acc = skill.m_accumulator;
                bt = dummy.m_backstabTime;
                Hit(dummy, player, Slash(1f), 3f);
                c.Check(Near(skill.m_accumulator, acc), "a training dummy paid sneak-attack XP");
                c.Check(dummy.m_nview.GetZDO().GetLong(SneakXp.LastXpKey, 0L) == 0L, "LastXp written on a training dummy");
                c.Note($"training dummy: faction {dummy.GetFaction()}, creature AI {(dummy.GetBaseAI() != null)}, vanilla "
                       + $"backstab {(dummy.m_backstabTime != bt ? "happened" : "did not happen")}");
            }
            c.Note($"Sneak raise step {F(step)}, skill gain rate {F(Game.m_skillGainRate)}, raise-skill multiplier "
                   + $"{F(multiplier)}: 7 XP = +{F(Gain(7f))} progress at Sneak 20 (needs {F(skill.GetNextLevelRequirement())})");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.smoke-throw ----------

    private static void CheckCloud(Checks c, SmokeCloud cloud, AmbushRules rules, string label)
    {
        var view = cloud != null ? cloud.View : null;
        var zdo = view != null ? view.GetZDO() : null;
        if (!c.Check(zdo != null, $"{label}: cloud without ZDO"))
        {
            return;
        }
        c.Check(cloud.Ready, $"{label}: cloud not set up");
        c.Check(zdo.GetLong(SmokeCloud.StartKey, 0L) != 0L, $"{label}: no CloudStart key");
        c.Check(Near(zdo.GetFloat(SmokeCloud.DurationKey, -1f), rules.CloudDuration), $"{label}: CloudDuration key");
        c.Check(Near(zdo.GetFloat(SmokeCloud.RadiusKey, -1f), rules.CloudRadius), $"{label}: CloudRadius key");
        c.Check(Near(zdo.GetFloat(SmokeCloud.HeightKey, -1f), rules.CloudHeight), $"{label}: CloudHeight key");
        c.Check(zdo.Persistent, $"{label}: cloud ZDO not persistent");
        c.Check(view.IsOwner(), $"{label}: the thrower's game does not own its cloud");
    }

    private static IEnumerator RunThrow()
    {
        var c = new Checks(ThrowName);
        var rig = Rig.Create(ThrowName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            var rules = new AmbushRules { CloudDuration = AmbushRules.CloudDurationMin };
            ServerRules.TestRules = rules;
            yield return Stand(player);
            var dir = ClearDirection(player, 14f, out _);
            var side = Vector3.Cross(Vector3.up, dir).normalized;
            var origin = player.transform.position;
            // Unaware Greydwarf 12 m away, facing away, AI off: only the smoke itself could alert it.
            var far = rig.Creature("Greydwarf", Ground(origin + side * 12f), side, false);
            var item = rig.GiveAndEquip(3);
            if (!c.Check(far != null && item != null, "could not spawn a Greydwarf or equip a Smoke Screen"))
            {
                c.Report();
                yield break;
            }
            rig.Aim(dir, 35f);
            yield return new WaitForSeconds(0.6f);

            // 1. The real throw.
            var before = new HashSet<SmokeCloud>(SmokeRegistry.All);
            var stack = rig.SmokeCount();
            var drops = SmokeDrops();
            c.Check(player.StartAttack(null, false), "StartAttack with a Smoke Screen refused while the feature is on");
            var slot = new CloudSlot();
            yield return WaitForCloud(before, 5f, slot);
            var first = slot.Cloud;
            c.Check(rig.SmokeCount() == stack - 1, $"stack {stack} -> {rig.SmokeCount()} after one throw");
            if (c.Check(first != null, "no cloud within 5 s of the throw"))
            {
                yield return new WaitForSeconds(1f);
                c.Check(NewClouds(before).Count == 1, $"{NewClouds(before).Count} clouds from one throw");
                CheckCloud(c, first, rules, "throw");
                c.Check(SmokeDrops() == drops, "a Smoke Screen was dropped back after the throw");
                c.Check(!far.GetBaseAI().IsAlerted(), "the unaware Greydwarf 12 m away was alerted by the throw");
                // Base on what it landed on: terrain, or a stone / piece there.
                var ground = ZoneSystem.instance.GetGroundHeight(first.Base);
                var solid = ZoneSystem.instance.GetSolidHeight(first.Base, out var solidY) ? solidY : ground;
                c.Check(Mathf.Abs(first.Base.y - ground) < 0.6f || Mathf.Abs(first.Base.y - solid) < 0.6f,
                    $"thrown cloud base {F(first.Base.y)} not on the ground (terrain {F(ground)}, solid {F(solid)})");
                c.Note($"throw: cloud {F(Vector3.Distance(origin, first.Base))} m away, base {V(first.Base)}, radius "
                       + $"{F(first.Radius)}, height {F(first.Height)}, look: see screenshot");
                SelfTest.Screenshot(ThrowName, "cloud");
                yield return null;
                yield return null;

                // Inert while it fades, then gone (its owner destroys it).
                var start = first.StartTime;
                var end = first.EndTime;
                while (first != null && SmokeRegistry.Now < end + 0.2d)
                {
                    yield return null;
                }
                if (c.Check(first != null, $"cloud gone before its end ({F(SmokeRegistry.Now - start)} s after impact)"))
                {
                    var now = SmokeRegistry.Now;
                    c.Check(!first.IsActive(now, rules) && !SmokeRegistry.InActiveCloud(first.Base + Vector3.up, rules, now),
                        "cloud still hides after its duration");
                    while (first != null && SmokeRegistry.Now < end + SmokeCloud.FadeSeconds + 2d)
                    {
                        yield return null;
                    }
                    var goneAfter = SmokeRegistry.Now - start;
                    c.Check(first == null, $"3 s cloud still there {F(goneAfter)} s after impact");
                    c.Check(goneAfter >= rules.CloudDuration + SmokeCloud.FadeSeconds - 0.2d,
                        $"cloud destroyed after {F(goneAfter)} s, before its end + fade");
                }
            }

            // 2. Our projectile on a creature: zero-damage hit, no alert, one cloud, projectile gone.
            var g = rig.Creature("Greydwarf", Ground(origin + dir * 3f), dir, false);
            if (c.Check(g != null, "could not spawn the target Greydwarf"))
            {
                yield return Frames(3);
                before = new HashSet<SmokeCloud>(SmokeRegistry.All);
                drops = SmokeDrops();
                var health = g.GetHealth();
                var from = player.GetCenterPoint() + dir * 0.8f;
                var projectile = rig.Launch(from, (g.GetCenterPoint() - from).normalized * 20f);
                yield return WaitForCloud(before, 3f, slot);
                if (c.Check(slot.Cloud != null, "no cloud from the projectile hitting a Greydwarf"))
                {
                    yield return new WaitForSeconds(0.8f);
                    c.Check(NewClouds(before).Count == 1, $"{NewClouds(before).Count} clouds from one projectile on a creature");
                    c.Check(projectile == null, "the projectile is still there after its hit");
                    var attackers = g.m_nview.GetZDO().GetBool(ZDOVars.s_attackers + player.GetPlayerName());
                    c.Check(attackers, "the projectile did not hit the Greydwarf (no hit recorded on it)");
                    c.Check(!g.GetBaseAI().IsAlerted(), "the Greydwarf hit by a Smoke Screen was alerted");
                    c.Check(Near(g.GetHealth(), health), $"the Smoke Screen hurt the Greydwarf ({F(health)} -> {F(g.GetHealth())})");
                    c.Check(SmokeDrops() == drops, "a Smoke Screen was dropped back after the creature hit");
                    CheckCloud(c, slot.Cloud, rules, "creature hit");
                }
                rig.Remove(g.gameObject);
            }

            // 3. On water: bursts on the surface.
            if (FindWater(player, out var surface, out var floor))
            {
                before = new HashSet<SmokeCloud>(SmokeRegistry.All);
                var projectile = rig.Launch(surface + Vector3.up * 3f, new Vector3(0.3f, -12f, 0f));
                yield return WaitForCloud(before, 3f, slot);
                if (c.Check(slot.Cloud != null, $"no cloud from the projectile landing in water at {V(surface)}"))
                {
                    yield return new WaitForSeconds(0.8f);
                    c.Check(NewClouds(before).Count == 1, $"{NewClouds(before).Count} clouds from one projectile in water");
                    c.Check(projectile == null, "the projectile is still there after hitting water");
                    var baseY = slot.Cloud.Base.y;
                    c.Check(Mathf.Abs(baseY - surface.y) < 0.5f && baseY > floor + 0.5f,
                        $"water cloud base {F(baseY)}, water surface {F(surface.y)}, bottom {F(floor)}");
                    c.Note($"water burst {F(Vector3.Distance(origin, surface))} m away: surface {F(surface.y)}, bottom "
                           + $"{F(floor)}, cloud base {F(baseY)}");
                }
            }
            else
            {
                c.Note("no water deep enough within the active area: water burst not tested");
            }

            // 4. TTL expiry in the air: one cloud, projectile gone.
            before = new HashSet<SmokeCloud>(SmokeRegistry.All);
            var up = rig.Launch(player.GetCenterPoint() + dir * 0.8f + Vector3.up * 0.5f, Vector3.up * 9f + dir * 1.5f, 0.5f);
            yield return WaitForCloud(before, 2.5f, slot);
            if (c.Check(slot.Cloud != null, "no cloud when the projectile's TTL ran out"))
            {
                yield return new WaitForSeconds(0.8f);
                c.Check(NewClouds(before).Count == 1, $"{NewClouds(before).Count} clouds from one TTL expiry");
                c.Check(up == null, "the projectile is still there after its TTL");
            }

            // 5. TTL expiry 30 m up (thrown off a cliff): cloud on what is below, not floating where time ran out.
            before = new HashSet<SmokeCloud>(SmokeRegistry.All);
            var high = rig.Launch(origin + dir * 4f + Vector3.up * 30f, Vector3.up * 2f + dir, 0.3f);
            yield return WaitForCloud(before, 2.5f, slot);
            if (c.Check(slot.Cloud != null, "no cloud when the projectile's TTL ran out 30 m up"))
            {
                yield return new WaitForSeconds(0.8f);
                c.Check(NewClouds(before).Count == 1, $"{NewClouds(before).Count} clouds from one TTL expiry 30 m up");
                c.Check(high == null, "the projectile is still there after its TTL 30 m up");
                var cloudBase = slot.Cloud.Base;
                var groundY = ZoneSystem.instance.GetGroundHeight(cloudBase);
                var solidY = ZoneSystem.instance.GetSolidHeight(cloudBase, out var solidHit) ? solidHit : groundY;
                var waterY = Floating.GetLiquidLevel(cloudBase);
                c.Check(Mathf.Abs(cloudBase.y - groundY) < 0.6f || Mathf.Abs(cloudBase.y - solidY) < 0.6f
                        || Mathf.Abs(cloudBase.y - waterY) < 0.6f,
                    $"cloud from a TTL expiry 30 m up floats: base {F(cloudBase.y)}, terrain {F(groundY)}, solid "
                    + $"{F(solidY)}, water {F(waterY)}");
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.smoke-sight ----------

    private static IEnumerator RunSight()
    {
        var c = new Checks(SightName);
        var rig = Rig.Create(SightName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            var rules = new AmbushRules { ActivationDelay = 0f };
            ServerRules.TestRules = rules;
            StealthCues.TestShowCues = true;
            yield return Stand(player);
            var dir = ClearDirection(player, 14f, out var clear);
            if (!clear)
            {
                c.Note("no open ground found: sight checks may be blocked by the scenery");
            }
            var origin = player.transform.position;
            var g = rig.Creature("Greydwarf", Ground(origin + dir * 8f), -dir, false);
            if (!c.Check(g != null, "could not spawn a Greydwarf"))
            {
                c.Report();
                yield break;
            }
            yield return Frames(3);
            var ai = g.GetBaseAI();

            bool Sees() => ai.CanSeeTarget(player);

            bool Hears()
            {
                player.m_noiseRange = 30f;
                return ai.CanHearTarget(player);
            }

            void At(float distance) => Place(g, Ground(origin + dir * distance), -dir);

            c.Check(Sees() && Hears(), "no cloud: the Greydwarf 8 m away does not see or hear the player (scenery?)");
            var slot = new CloudSlot();

            // Player inside, creature outside.
            yield return rig.PutCloud(slot, origin);
            c.Check(!Sees(), "player inside, creature outside: seen");
            c.Check(!Hears(), "player inside, creature outside: heard");
            yield return new WaitForSeconds(0.7f);
            c.Check(StealthCues.Has(player, CueKind.Smoke), "player inside an active cloud: no In smoke icon");

            // Creature inside, player outside.
            yield return rig.PutCloud(slot, g.transform.position);
            c.Check(!Sees(), "creature inside, player outside: seen");
            c.Check(!Hears(), "creature inside, player outside: heard");

            // Neither inside, cloud between (E1); with BlocksLineOfSight off it sees.
            At(12f);
            yield return rig.PutCloud(slot, origin + dir * 6f);
            yield return Frames(2);
            c.Check(!Sees(), "cloud between: seen");
            ServerRules.TestRules = new AmbushRules { ActivationDelay = 0f, BlocksLineOfSight = false };
            c.Check(Sees(), "cloud between, BlocksLineOfSight off: not seen");
            ServerRules.TestRules = rules;

            // Both inside: within InsideSightRange only.
            yield return rig.PutCloud(slot, origin);
            At(2f);
            yield return Frames(2);
            c.Check(Sees(), "both inside 2 m apart: not seen");
            At(3.5f);
            yield return Frames(2);
            c.Check(!Sees(), "both inside 3.5 m apart: seen");

            // A damaging player hit reveals.
            Hit(g, player, Blunt(1f));
            c.Check(Sees(), "both inside 3.5 m apart, after a damaging hit: not seen");
            rig.Remove(g.gameObject);
            yield return Frames(2);

            // Fresh creatures: fire only and a tiny hit (below vanilla's OnDamaged) reveal; no damage does not.
            var cases = new[]
            {
                new KeyValuePair<string, HitData.DamageTypes>("fire 5 only", Fire(5f)),
                new KeyValuePair<string, HitData.DamageTypes>("blunt 0.05", Blunt(0.05f)),
                new KeyValuePair<string, HitData.DamageTypes>("no damage", default),
            };
            foreach (var hitCase in cases)
            {
                var fresh = rig.Creature("Greydwarf", Ground(origin + dir * 3.5f), -dir, false);
                if (!c.Check(fresh != null, "could not spawn a fresh Greydwarf"))
                {
                    continue;
                }
                yield return Frames(3);
                var freshAi = fresh.GetBaseAI();
                c.Check(!freshAi.CanSeeTarget(player), $"{hitCase.Key}: seen before the hit (3.5 m in the cloud)");
                Hit(fresh, player, hitCase.Value);
                var alerted = freshAi.IsAlerted();
                var sees = freshAi.CanSeeTarget(player);
                if (hitCase.Value.GetTotalDamage() > 0f)
                {
                    c.Check(!alerted, $"{hitCase.Key}: vanilla alerted the creature (the hit is not below OnDamaged's threshold)");
                    c.Check(sees, $"{hitCase.Key}: the hit did not reveal the player");
                }
                else
                {
                    c.Check(!sees, $"{hitCase.Key}: a hit without damage revealed the player");
                }
                rig.Remove(fresh.gameObject);
                yield return Frames(2);
            }

            // Bosses are not fooled.
            var boss = rig.Creature("Greydwarf", Ground(origin + dir * 3.5f), -dir, false);
            if (c.Check(boss != null, "could not spawn the boss stand-in"))
            {
                yield return Frames(3);
                var bossAi = boss.GetBaseAI();
                c.Check(!bossAi.CanSeeTarget(player), "boss stand-in seen before m_boss");
                boss.m_boss = true;
                c.Check(bossAi.CanSeeTarget(player), "m_boss = true: the smoke still hides the player from it");
                boss.m_boss = false;
                rig.Remove(boss.gameObject);
            }

            // A turret-like observer (no creature AI) is not fooled: player inside, observer outside.
            var observer = new GameObject(ModInfo.Guid + ".TestObserver");
            rig.Spawned.Add(observer);
            var eye = origin + dir * 8f + Vector3.up * 1.5f;
            observer.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(Flat(-dir)));
            c.Check(BaseAI.CanSeeTarget(observer.transform, eye, 30f, 90f, true, false, player),
                "an observer without creature AI (turret) is fooled by the smoke");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.smoke-blind ----------

    private static IEnumerator RunBlind()
    {
        var c = new Checks(BlindName);
        var rig = Rig.Create(BlindName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            // Blind long enough to look at it after the give-up (default 6 s).
            var rules = new AmbushRules { BlindSeconds = 12f };
            ServerRules.TestRules = rules;
            yield return Stand(player);
            var dir = ClearDirection(player, 16f, out _);
            var origin = player.transform.position;

            // 1. Chasing the player at 3 m, hit once, cloud at the player.
            var a = rig.Creature("Greydwarf", Ground(origin + dir * 3f), -dir, true);
            if (!c.Check(a != null, "could not spawn a Greydwarf"))
            {
                c.Report();
                yield break;
            }
            yield return Frames(3);
            var aiA = (MonsterAI)a.GetBaseAI();
            aiA.SetAlerted(true);
            aiA.m_targetCreature = player;
            var slot = new CloudSlot();
            yield return rig.PutCloud(slot, origin);
            Hit(a, player, Blunt(1f));
            c.Check(AiMemory.IsRevealed(a.transform, player, SmokeRegistry.Now), "the hit before the burst did not reveal");
            var until = Time.time + 2f;
            while (Time.time < until && !AiMemory.IsBlinded(a.transform, SmokeRegistry.Now))
            {
                yield return null;
            }
            if (c.Check(AiMemory.IsBlinded(a.transform, SmokeRegistry.Now), "the burst did not blind the Greydwarf chasing the player"))
            {
                var blindAt = Time.time;
                c.Check(!AiMemory.IsRevealed(a.transform, player, SmokeRegistry.Now), "the burst did not clear the earlier reveal");
                until = blindAt + rules.ForgetSeconds + 2f;
                while (Time.time < until && (aiA.m_targetCreature != null || aiA.IsAlerted()))
                {
                    yield return null;
                }
                var forgotAfter = Time.time - blindAt;
                c.Check(aiA.m_targetCreature == null && !aiA.IsAlerted(),
                    $"still chasing {F(forgotAfter)} s after the burst (target {aiA.m_targetCreature != null}, alerted {aiA.IsAlerted()})");
                c.Check(forgotAfter <= rules.ForgetSeconds + 0.5f, $"gave up only {F(forgotAfter)} s after the burst");
                c.Note($"blinded creature gave up {F(forgotAfter)} s after the burst (ForgetSeconds {F(rules.ForgetSeconds)})");

                // Still blind: at 2 m in the cloud it does not see the player, a plain eye there would. Checked as an
                // alerted look from its own transform (its idle walk may turn it away; the smoke rule is what counts).
                var near = player.transform.position + dir * 2f;
                Place(a, Ground(near), -dir);
                yield return Frames(2);
                var observer = new GameObject(ModInfo.Guid + ".TestObserver");
                rig.Spawned.Add(observer);
                var eye = a.m_eye.position;
                observer.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(Flat(-dir)));
                c.Check(BaseAI.CanSeeTarget(observer.transform, eye, aiA.m_viewRange, aiA.m_viewAngle, true, false, player),
                    "a plain eye 2 m from the player does not see him (scenery?)");
                c.Check(AiMemory.IsBlinded(a.transform, SmokeRegistry.Now), "blind over too early");
                c.Check(!BaseAI.CanSeeTarget(a.transform, a.m_eye.position, aiA.m_viewRange, aiA.m_viewAngle, true, false, player),
                    "blinded creature 2 m from the player (both in the cloud) sees him");
                Hit(a, player, Blunt(1f));
                c.Check(BaseAI.CanSeeTarget(a.transform, a.m_eye.position, aiA.m_viewRange, aiA.m_viewAngle, true, false, player),
                    "blinded creature hit by the player does not see him");
            }
            rig.Remove(a.gameObject);
            if (slot.Cloud != null)
            {
                rig.Remove(slot.Cloud.gameObject);
                slot.Cloud = null;
            }
            yield return Frames(3);

            // 2. Chasing the player, burst 15 m from the player: not blinded, keeps its fight.
            origin = player.transform.position;
            var b = rig.Creature("Greydwarf", Ground(origin + dir * 3f), -dir, true);
            if (c.Check(b != null, "could not spawn the second Greydwarf"))
            {
                yield return Frames(3);
                var aiB = (MonsterAI)b.GetBaseAI();
                aiB.SetAlerted(true);
                aiB.m_targetCreature = player;
                Hit(b, player, Blunt(1f));
                yield return rig.PutCloud(slot, origin + dir * 15f);
                yield return new WaitForSeconds(rules.ActivationDelay + 0.4f);
                c.Check(!AiMemory.IsBlinded(b.transform, SmokeRegistry.Now), "a creature chasing a player 15 m from the burst was blinded");
                yield return new WaitForSeconds(rules.ForgetSeconds + 1.5f);
                c.Check(ReferenceEquals(aiB.m_targetCreature, player) && aiB.IsAlerted(),
                    $"the creature chasing a player 15 m from the burst lost him (target {aiB.m_targetCreature != null}, "
                    + $"alerted {aiB.IsAlerted()})");
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.healthbars ----------

    private static IEnumerator RunBars()
    {
        var c = new Checks(BarsName);
        var rig = Rig.Create(BarsName);
        var hud = EnemyHud.instance;
        if (rig == null)
        {
            yield break;
        }
        if (hud == null)
        {
            SelfTest.Fail(BarsName, "no EnemyHud");
            yield break;
        }
        try
        {
            var player = rig.Player;
            var rules = new AmbushRules { ActivationDelay = 0f };
            ServerRules.TestRules = rules;
            yield return Stand(player);
            var dir = ClearDirection(player, 12f, out _);
            var origin = player.transform.position;
            // Camera on the creature: the game switch plates outside the picture off.
            rig.Aim(dir, 0f);
            var spot = Ground(origin + dir * 8f);
            var g = rig.Creature("Greydwarf", spot, -dir, false);
            if (!c.Check(g != null, "could not spawn a Greydwarf"))
            {
                c.Report();
                yield break;
            }
            EnemyHud.HudData data = null;
            for (var i = 0; i < 90 && data == null; i++)
            {
                yield return null;
                hud.m_huds.TryGetValue(g, out data);
            }
            if (!c.Check(data != null, "no plate made for a Greydwarf 8 m away"))
            {
                c.Report();
                yield break;
            }
            // What the crosshair on it does in vanilla.
            data.m_hoverTimer = 0f;
            yield return Frames(2);
            c.Check(data.m_gui != null && data.m_gui.activeSelf, "the plate did not show after its hover timer was reset");
            c.Check(hud.TestShow(g, true), "no cloud: TestShow false");

            // OnlyInside: creature in the cloud, player outside -> no plate, and it stays away.
            var slot = new CloudSlot();
            yield return rig.PutCloud(slot, g.transform.position);
            c.Check(!hud.TestShow(g, true), "creature inside, player outside: TestShow true");
            var removed = false;
            for (var i = 0; i < 20 && !removed; i++)
            {
                yield return null;
                removed = !hud.m_huds.ContainsKey(g);
            }
            c.Check(removed, "the creature's plate did not leave within 20 frames");
            var back = false;
            for (var i = 0; i < 30; i++)
            {
                yield return null;
                back |= hud.m_huds.ContainsKey(g);
            }
            c.Check(!back, "the creature's plate came back while it is in the cloud");

            // Cloud on the player only.
            yield return rig.PutCloud(slot, origin);
            c.Check(hud.TestShow(g, true), "player inside, creature outside, OnlyInside: TestShow false");
            // T22, the real plate. OnlyInside: the game make it again, it show once aimed at. ThroughSmoke: it go.
            var plate = new Box();
            yield return Plate(hud, g, plate, 60);
            c.Check(plate.Ok && InSmoke(player, rules) && AimAt(hud, g),
                $"player inside the cloud ({InSmoke(player, rules)}), creature outside, OnlyInside: the game made no plate for the creature");
            yield return Frames(2);
            c.Check(PlateShown(hud, g), "player inside, creature outside, OnlyInside: its bar does not show after aiming at it");
            ServerRules.TestRules = new AmbushRules { ActivationDelay = 0f, HealthBars = HealthBarMode.ThroughSmoke };
            c.Check(!hud.TestShow(g, true), "player inside, creature outside, ThroughSmoke: TestShow true");
            var taken = new Box();
            yield return WaitFor(() => !hud.m_huds.ContainsKey(g), 1f, taken);
            c.Check(taken.Ok, "player inside, creature outside, ThroughSmoke: its bar did not go within 1 s");
            ServerRules.TestRules = new AmbushRules { ActivationDelay = 0f, HealthBars = HealthBarMode.Off };
            yield return rig.PutCloud(slot, g.transform.position);
            c.Check(hud.TestShow(g, true), "HealthBars Off: TestShow false");
            ServerRules.TestRules = rules;

            // Bosses keep their bar.
            c.Check(!hud.TestShow(g, true), "creature inside again, OnlyInside: TestShow true");
            g.m_boss = true;
            g.m_dontHideBossHud = true;
            c.Check(hud.TestShow(g, true), "boss in the cloud: bar hidden");
            g.m_boss = false;
            g.m_dontHideBossHud = false;

            // Tames keep their bar: the smoke hides players only (D15). Owner read the field (IsTamed).
            g.m_tamed = true;
            c.Check(g.IsTamed() && hud.TestShow(g, true), "tame in the cloud: bar hidden");
            g.m_tamed = false;
            c.Check(!hud.TestShow(g, true), "creature inside again after the tame check: TestShow true");
            c.Note($"plates shown within {F(hud.m_maxShowDistance)} m (bosses {F(hud.m_maxShowDistanceBoss)} m), "
                   + $"for {F(hud.m_hoverShowDuration)} s after the crosshair");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.guard ----------

    private static IEnumerator RunGuard()
    {
        var c = new Checks(GuardName);
        var rig = Rig.Create(GuardName);
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            yield return Stand(player);
            var dir = ClearDirection(player, 8f, out _);
            var item = rig.GiveAndEquip(3);
            if (!c.Check(item != null, "could not equip a Smoke Screen"))
            {
                c.Report();
                yield break;
            }
            rig.Aim(dir, 40f);
            yield return new WaitForSeconds(0.6f);

            // Feature "off": refused, nothing used, message.
            Plugin.TestInactive = true;
            var stack = rig.SmokeCount();
            var messages = QueuedCount(HumanoidPatches.InactiveMessage);
            HumanoidPatches.TestResetMessageTimer();
            c.Check(!player.StartAttack(null, false), "StartAttack with a Smoke Screen allowed while the feature is off");
            // Same frame (HUD queue only shrink in its Update).
            var queued = QueuedCount(HumanoidPatches.InactiveMessage) - messages;
            yield return new WaitForSeconds(1.2f);
            c.Check(!player.InAttack(), "the refused throw started an attack");
            c.Check(rig.SmokeCount() == stack, $"stack {stack} -> {rig.SmokeCount()} after a refused throw");
            if (MessageHudWorks)
            {
                c.Check(queued == 1, $"no \"{ModInfo.Name} is turned off\" message ({queued} queued)");
            }
            else
            {
                c.Note("HUD hidden: message not checked");
            }

            // Feature on: it throws.
            Plugin.TestInactive = false;
            var before = new HashSet<SmokeCloud>(SmokeRegistry.All);
            c.Check(player.StartAttack(null, false), "StartAttack with a Smoke Screen refused while the feature is on");
            var slot = new CloudSlot();
            yield return WaitForCloud(before, 5f, slot);
            c.Check(rig.SmokeCount() == stack - 1, $"stack {stack} -> {rig.SmokeCount()} after a throw");
            c.Check(slot.Cloud != null, "no cloud after the allowed throw");
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }

    // ---------- sneak.pending ----------

    private static IEnumerator RunPending()
    {
        var c = new Checks(PendingName);
        var rig = Rig.Create(PendingName);
        var hud = EnemyHud.instance;
        if (rig == null)
        {
            yield break;
        }
        try
        {
            var player = rig.Player;
            var rules = new AmbushRules { FogBonus = 0 };
            ServerRules.TestRules = rules;
            StealthCues.TestShowCues = true;
            rig.SetTime(0.5f);
            rig.SetSneak(0f);
            yield return Stand(player);
            var dir = ClearDirection(player, 12f, out _);
            var origin = player.transform.position;
            var item = rig.GiveAndEquip(3);
            var g = rig.Creature("Greydwarf", Ground(origin + dir * 8f), -dir, false);
            if (!c.Check(item != null && g != null, "could not equip a Smoke Screen or spawn a Greydwarf"))
            {
                c.Report();
                yield break;
            }
            rig.Aim(dir, 40f);
            yield return new WaitForSeconds(0.5f);

            ServerRules.TestPending = true;
            c.Check(ServerRules.IsPending, "TestPending did not make the rules pending");
            var recipe = SmokeContent.CraftRecipe;
            c.Check(recipe != null && !recipe.m_enabled, "recipe shown while the rules are pending");
            rig.Cloud(origin);
            rig.Cloud(g.transform.position);

            // Crouched and still: vanilla target, no icon.
            var crouched = new Box();
            yield return Crouch(player, crouched);
            if (!c.Check(crouched.Ok, "the player did not crouch"))
            {
                c.Report();
                yield break;
            }
            yield return new WaitForSeconds(2.5f);
            yield return ForceRefresh(player);
            var t = player.m_stealthFactorTarget;
            var v = VanillaTarget(player);
            c.Check(Near(t, v, 0.01f), $"pending: target {F(t)}, vanilla formula {F(v)} (no early curve, no still bonus)");
            c.Check(!StealthState.StillActive, "pending: still bonus on");
            yield return new WaitForSeconds(0.6f);
            c.Check(!AnyCue(player), "pending: a stealth icon is shown");

            // Smoke hides nothing, bars stay.
            var eye = g.m_eye.position;
            c.Check(!SmokeRegistry.Hides(g.transform, eye, player, false, out _), "pending: the smoke hides the player in it");
            if (hud != null)
            {
                c.Check(hud.TestShow(g, true), "pending: the bar of a creature in smoke is hidden");
            }

            // Throw refused with the waiting message, stack kept.
            var stack = rig.SmokeCount();
            var messages = QueuedCount(HumanoidPatches.PendingMessage);
            HumanoidPatches.TestResetMessageTimer();
            c.Check(!player.StartAttack(null, false), "pending: StartAttack with a Smoke Screen allowed");
            // Same frame (HUD queue only shrink in its Update).
            var queued = QueuedCount(HumanoidPatches.PendingMessage) - messages;
            yield return null;
            c.Check(rig.SmokeCount() == stack, "pending: the refused throw used a Smoke Screen");
            if (MessageHudWorks)
            {
                c.Check(queued == 1, $"pending: no waiting message ({queued} queued)");
            }
            else
            {
                c.Note("HUD hidden: message not checked");
            }

            // Rules here: recipe back, smoke hides, still bonus at the next refresh.
            ServerRules.TestPending = false;
            c.Check(recipe != null && recipe.m_enabled, "rules here: recipe still hidden");
            c.Check(SmokeRegistry.Hides(g.transform, eye, player, false, out _), "rules here: the smoke does not hide the player in it");
            if (hud != null)
            {
                c.Check(!hud.TestShow(g, true), "rules here: the bar of a creature in smoke still shown");
            }
            var until = Time.time + 1.2f;
            while (Time.time < until && !StealthState.StillActive)
            {
                yield return new WaitForFixedUpdate();
            }
            if (c.Check(StealthState.StillActive, "rules here: no still bonus at the next refresh"))
            {
                var w = StealthState.LastWithoutStill;
                c.Check(Near(player.m_stealthFactorTarget, WithStill(w, rules), 0.01f),
                    $"rules here: target {F(player.m_stealthFactorTarget)}, expected {F(WithStill(w, rules))}");
                c.Check(Near(w, VanillaTarget(player) * StealthState.Early(rules, StealthState.SkillFactor), 0.02f),
                    $"rules here: value without the still bonus {F(w)}, expected the early curve on vanilla");
            }
            c.Report();
        }
        finally
        {
            rig.Restore();
        }
    }
#endif
}
