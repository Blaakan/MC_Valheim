#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Farming.HarpoonHooksTamesMod;

// Debug build only. Stage and tools of the self tests: where to stand, what to spawn, the harpoon and its hits,
// what a harmless hook look like, pull, flight, real throw. See SelfTests.cs for the list of tests.
internal static partial class SelfTests
{
    // ---------- rig: one test's piece of world ----------

    // Me = what one test change on the player and the world. Done (finally, no yield) put everything back.
    // Stage find a flat clear lane near the start (cached for next tests): player stand at Origin, look along Dir,
    // creatures go ahead (At), player walk back along -Dir for pulls (Back metres are clear).
    private sealed class Rig
    {
        private static bool _laneKnown;
        private static Vector3 _laneOrigin;
        private static Vector3 _laneDir;
        private static float _laneFront;
        private static float _laneBack;
        private static int _solidMask;

        internal readonly string Test;
        internal readonly Player P;
        internal readonly Inventory Inv;
        internal Vector3 Origin;
        internal Vector3 Dir = Vector3.forward;
        internal float Back;

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<ItemDrop.ItemData> _given = new List<ItemDrop.ItemData>();
        private readonly HashSet<ItemDrop> _dropsBefore = new HashSet<ItemDrop>();
        private readonly Dictionary<Skills.SkillType, float[]> _skills = new Dictionary<Skills.SkillType, float[]>();
        private readonly Vector3 _startPos;
        private readonly Quaternion _startRot;
        private readonly Quaternion _yaw;
        private readonly float _pitch;
        private readonly float _stamina;
        private readonly bool _pvp;
        private readonly float _adrenaline;
        private readonly float _degenTimer;
        private readonly ItemDrop.ItemData _right;
        private readonly ItemDrop.ItemData _left;
        private readonly ItemDrop.ItemData _trinket;
        private readonly ItemDrop.ItemData _ammo;
        private PlayerController _controller;
        private bool _controllerEnabled;
        private bool _noSpawnTaken;
        private bool _noSpawn;
        private bool _rateTaken;
        private bool _hadRate;
        private float _rate;
        private bool _done;

        private Rig(string test, Player player)
        {
            Test = test;
            P = player;
            Inv = player.GetInventory();
            _startPos = player.transform.position;
            _startRot = player.transform.rotation;
            _yaw = player.m_lookYaw;
            _pitch = player.m_lookPitch;
            _stamina = player.m_stamina;
            _pvp = player.IsPVPEnabled();
            _adrenaline = player.m_adrenaline;
            _degenTimer = player.m_adrenalineDegenTimer;
            _right = player.m_rightItem;
            _left = player.m_leftItem;
            _trinket = player.m_trinketItem;
            _ammo = player.m_ammoItem;
            Origin = _startPos;
            foreach (var drop in ItemDrop.s_instances)
            {
                _dropsBefore.Add(drop);
            }
        }

        internal Vector3 Right => Vector3.Cross(Vector3.up, Dir).normalized;

        internal static Rig Create(string test, Checks c)
        {
            var player = Player.m_localPlayer;
            if (player == null || ZNet.instance == null || ZNetScene.instance == null || ZoneSystem.instance == null
                || ObjectDB.instance == null || Game.instance == null || player.m_nview == null || !player.m_nview.IsValid())
            {
                c.Check(false, "no world or no local player");
                return null;
            }
            TestSwitches.ThrowerSideOff = false;
            TestSwitches.OwnerSideOff = false;
            return new Rig(test, player);
        }

        // ----- where -----

        // Find the lane, clear wild creatures around it, stop new spawns, put the player at its start.
        internal IEnumerator Stage(float front, float back, Checks c)
        {
            Back = back;
            if (_laneKnown && _laneFront >= front && _laneBack >= back && LaneClear(_laneOrigin, _laneDir, front, back, false))
            {
                Origin = _laneOrigin;
                Dir = _laneDir;
            }
            else if (FindLane(_startPos, front, back, out var origin, out var dir))
            {
                Origin = origin;
                Dir = dir;
                _laneKnown = true;
                _laneOrigin = origin;
                _laneDir = dir;
                _laneFront = front;
                _laneBack = back;
                c.Note($"lane: start {F(origin)} ({F(HDist(origin, _startPos))} m from the test start), {F(front)} m ahead and {F(back)} m behind clear and flat");
            }
            else
            {
                Origin = Ground(_startPos);
                var forward = P.transform.forward;
                forward.y = 0f;
                Dir = forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.forward;
                c.Note($"no clear flat lane ({F(front)} m ahead, {F(back)} m behind) within 48 m of the start; using the start spot (results may suffer)");
            }
            _noSpawnTaken = true;
            _noSpawn = SpawnSystem.m_nospawn;
            SpawnSystem.m_nospawn = true;
            // Player first, then a moment: creatures of the zones around the new spot must be there to be cleared.
            PlacePlayerAlong(0f);
            var until = Time.time + 0.8f;
            while (Time.time < until)
            {
                yield return Fixed;
            }
            var removed = ClearEnemies(Origin, 45f);
            if (removed > 0)
            {
                c.Note($"removed {removed} wild creature(s) within 45 m so they cannot change a result");
            }
            for (var i = 0; i < 10; i++)
            {
                yield return Fixed;
            }
        }

        private static bool FindLane(Vector3 start, float front, float back, out Vector3 origin, out Vector3 dir)
        {
            // Rings around the start first (away from the spawn stones and their no-monster area), start spot last.
            // Second pass: same, no-monster area allowed. First clear lane win.
            float[] rings = { 20f, 32f, 44f, 0f };
            origin = start;
            dir = Vector3.forward;
            for (var pass = 0; pass < 2; pass++)
            {
                foreach (var ring in rings)
                {
                    var spots = ring > 0f ? 12 : 1;
                    for (var s = 0; s < spots; s++)
                    {
                        var o = start + Quaternion.Euler(0f, s * 30f, 0f) * Vector3.forward * ring;
                        for (var i = 0; i < 12; i++)
                        {
                            var d = Quaternion.Euler(0f, i * 30f, 0f) * Vector3.forward;
                            if (!LaneClear(o, d, front, back, pass == 0))
                            {
                                continue;
                            }
                            origin = Ground(o);
                            dir = d;
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private static int SolidMask()
        {
            if (_solidMask == 0)
            {
                // What stop a projectile or a dragged creature, minus characters.
                _solidMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "vehicle");
            }
            return _solidMask;
        }

        // Lane from `back` m behind origin to `front` m ahead: dry, nearly flat (ground within 0.45 m of a straight
        // line), nothing solid in a 0.9 m wide tube 1 m above it.
        private static bool LaneClear(Vector3 origin, Vector3 dir, float front, float back, bool avoidNoMonsterArea)
        {
            var zones = ZoneSystem.instance;
            var a = origin - dir * back;
            var b = origin + dir * front;
            if (!zones.GetGroundHeight(a, out var ha) || !zones.GetGroundHeight(b, out var hb))
            {
                return false;
            }
            var length = front + back;
            if (Mathf.Abs(hb - ha) > length * 0.1f)
            {
                return false;
            }
            var steps = Mathf.CeilToInt(length);
            for (var i = 0; i <= steps; i++)
            {
                var t = i / (float)steps;
                var p = Vector3.Lerp(a, b, t);
                if (!zones.GetGroundHeight(p, out var h) || h < zones.m_waterLevel + 0.5f
                    || Mathf.Abs(h - Mathf.Lerp(ha, hb, t)) > 0.45f)
                {
                    return false;
                }
            }
            a.y = ha + 1f;
            b.y = hb + 1f;
            if (avoidNoMonsterArea && (EffectArea.IsPointInsideNoMonsterArea(a) != null || EffectArea.IsPointInsideNoMonsterArea(b) != null))
            {
                return false;
            }
            var mask = SolidMask();
            if (Physics.CheckSphere(a, 0.45f, mask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }
            return !Physics.SphereCast(a, 0.45f, (b - a).normalized, out _, (b - a).magnitude, mask, QueryTriggerInteraction.Ignore);
        }

        // Wild creatures (enemy of the player, not tamed) within radius. This game own them in single player; on a
        // server me take them first.
        private int ClearEnemies(Vector3 center, float radius)
        {
            var doomed = new List<GameObject>();
            foreach (var ai in BaseAI.BaseAIInstances)
            {
                if (ai == null)
                {
                    continue;
                }
                var ch = ai.m_character;
                if (ch == null || ch.IsPlayer() || ch.IsTamed() || !BaseAI.IsEnemy(P, ch)
                    || (ai.transform.position - center).sqrMagnitude > radius * radius)
                {
                    continue;
                }
                doomed.Add(ai.gameObject);
            }
            foreach (var go in doomed)
            {
                DestroyObject(go);
            }
            return doomed.Count;
        }

        // Ground point `forward` m ahead of the lane start, `side` m to the right.
        internal Vector3 At(float forward, float side = 0f) => Ground(Origin + Dir * forward + Right * side);

        // Lane coordinate of a point (m ahead of the lane start).
        internal float Along(Vector3 pos) => Vector3.Dot(pos - Origin, Dir);

        internal void PlacePlayerAlong(float s)
        {
            Place(P, Ground(Origin + Dir * s), Dir);
            P.m_lookYaw = Quaternion.LookRotation(Dir);
            P.m_lookPitch = 0f;
        }

        // ----- what -----

        internal Character Spawn(string prefabName, Vector3 pos, bool tame)
        {
            var prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null)
            {
                SelfTest.Note(Test, $"prefab {prefabName} not found");
                return null;
            }
            var go = Object.Instantiate(prefab, pos + Vector3.up * 0.15f, Quaternion.LookRotation(-Dir));
            _spawned.Add(go);
            var ch = go.GetComponent<Character>();
            if (ch == null)
            {
                SelfTest.Note(Test, $"{prefabName} is not a creature");
                return null;
            }
            NoDrops(ch);
            if (tame)
            {
                var ai = go.GetComponent<MonsterAI>();
                if (ai != null)
                {
                    ai.MakeTame(); // not Tameable.Tame: that one count a tame (statistic, Creature Kill and Tame Counts)
                }
                else
                {
                    ch.SetTamed(true);
                }
            }
            return ch;
        }

        internal void Track(GameObject go)
        {
            if (go != null && !_spawned.Contains(go))
            {
                _spawned.Add(go);
            }
        }

        internal void Destroy(GameObject go)
        {
            _spawned.Remove(go);
            DestroyObject(go);
        }

        internal void Destroy(Component c)
        {
            if (c != null)
            {
                Destroy(c.gameObject);
            }
        }

        internal ItemDrop.ItemData Give(string prefab, int stack = 1)
        {
            var item = Inv.AddItem(prefab, stack, 1, 0, 0L, "", false);
            if (item != null && !_given.Contains(item))
            {
                _given.Add(item);
            }
            return item;
        }

        internal void EmptyHands()
        {
            if (P.m_rightItem != null)
            {
                P.UnequipItem(P.m_rightItem, false);
            }
            if (P.m_leftItem != null)
            {
                P.UnequipItem(P.m_leftItem, false);
            }
        }

        // Me drive the player (no keyboard or mouse in between).
        internal void TakeControls()
        {
            if (_controller == null)
            {
                _controller = P.GetComponent<PlayerController>();
                if (_controller != null)
                {
                    _controllerEnabled = _controller.enabled;
                    _controller.enabled = false;
                }
            }
            Drive(false);
        }

        // Block held or not, nothing else pressed. Toggle-block setting: one press flip it.
        internal void Drive(bool block)
        {
            P.SetControls(Vector3.zero, false, false, false, false, false, block, false, false, false, false);
            if (P.m_blocking != block)
            {
                P.SetControls(Vector3.zero, false, false, false, false, true, block, false, false, false, false);
            }
        }

        // Skill me may raise: level and progress go back at the end.
        internal void KeepSkill(Skills.SkillType type)
        {
            if (_skills.ContainsKey(type))
            {
                return;
            }
            var data = P.GetSkills().m_skillData;
            _skills[type] = data.TryGetValue(type, out var skill) ? new[] { 1f, skill.m_level, skill.m_accumulator } : new[] { 0f, 0f, 0f };
        }

        // World modifier StaminaRate 0 (set by the probe) off: stamina is used again. Done set it back.
        internal IEnumerator StaminaOn(Checks c)
        {
            var zone = ZoneSystem.instance;
            if (!_rateTaken)
            {
                _hadRate = zone.GetGlobalKey(GlobalKeys.StaminaRate, out _rate);
                _rateTaken = true;
            }
            zone.RemoveGlobalKey(GlobalKeys.StaminaRate);
            var until = Time.time + 5f;
            while (Time.time < until && Game.m_staminaRate <= 0f)
            {
                yield return null;
            }
            c.Check(Game.m_staminaRate > 0f, $"stamina is used again for this part (rate {F(Game.m_staminaRate)})");
        }

        internal IEnumerator StaminaOff()
        {
            if (!_rateTaken || !_hadRate)
            {
                yield break;
            }
            ZoneSystem.instance.SetGlobalKey(GlobalKeys.StaminaRate, _rate);
            var until = Time.time + 5f;
            while (Time.time < until && !Near(Game.m_staminaRate, _rate, 0.001f))
            {
                yield return null;
            }
        }

        // ----- end -----

        internal void Done()
        {
            if (_done)
            {
                return;
            }
            _done = true;
            Safe("switches", () =>
            {
                TestSwitches.ThrowerSideOff = false;
                TestSwitches.OwnerSideOff = false;
            });
            Safe("controls", () =>
            {
                if (_controller != null)
                {
                    Drive(false);
                    _controller.enabled = _controllerEnabled;
                }
            });
            Safe("ride", () =>
            {
                if (P.GetDoodadController() != null)
                {
                    P.StopDoodadControl();
                }
                if (P.IsAttached())
                {
                    P.AttachStop();
                }
            });
            Safe("pvp", () =>
            {
                if (P.IsPVPEnabled() != _pvp)
                {
                    P.SetPVP(_pvp);
                }
            });
            Safe("stamina", () =>
            {
                if (_rateTaken && _hadRate && ZoneSystem.instance != null)
                {
                    ZoneSystem.instance.SetGlobalKey(GlobalKeys.StaminaRate, _rate);
                }
                P.m_stamina = Mathf.Min(_stamina, P.GetMaxStamina());
            });
            Safe("items", () =>
            {
                foreach (var item in _given)
                {
                    if (item == null)
                    {
                        continue;
                    }
                    if (P.IsItemEquiped(item) || ReferenceEquals(P.m_ammoItem, item))
                    {
                        P.UnequipItem(item, false);
                    }
                    if (Inv.ContainsItem(item))
                    {
                        Inv.RemoveItem(item);
                    }
                }
                _given.Clear();
            });
            Safe("equipment", () =>
            {
                foreach (var item in new[] { _right, _left, _trinket, _ammo })
                {
                    if (item != null && Inv.ContainsItem(item) && !P.IsItemEquiped(item))
                    {
                        P.EquipItem(item, false);
                    }
                }
                P.UpdateModifiers();
            });
            Safe("spawned", () =>
            {
                foreach (var go in _spawned)
                {
                    DestroyObject(go);
                }
                _spawned.Clear();
            });
            Safe("drops", () =>
            {
                // Item that fell during the test near the lane (thrown weapon, saddle): gone.
                var doomed = new List<GameObject>();
                foreach (var drop in ItemDrop.s_instances)
                {
                    if (drop != null && !_dropsBefore.Contains(drop) && HDist(drop.transform.position, Origin) < 70f)
                    {
                        doomed.Add(drop.gameObject);
                    }
                }
                foreach (var go in doomed)
                {
                    DestroyObject(go);
                }
                if (doomed.Count > 0)
                {
                    SelfTest.Note(Test, $"removed {doomed.Count} item(s) dropped during the test");
                }
            });
            Safe("skills", () =>
            {
                var data = P.GetSkills().m_skillData;
                foreach (var pair in _skills)
                {
                    if (pair.Value[0] < 0.5f)
                    {
                        data.Remove(pair.Key);
                    }
                    else if (data.TryGetValue(pair.Key, out var skill))
                    {
                        skill.m_level = pair.Value[1];
                        skill.m_accumulator = pair.Value[2];
                    }
                }
                P.m_adrenaline = _adrenaline;
                P.m_adrenalineDegenTimer = _degenTimer;
            });
            Safe("spawns", () =>
            {
                if (_noSpawnTaken)
                {
                    SpawnSystem.m_nospawn = _noSpawn;
                }
            });
            Safe("player", () =>
            {
                if (!P.IsDead())
                {
                    Place(P, _startPos, Vector3.zero);
                    P.transform.rotation = _startRot;
                    if (P.m_body != null)
                    {
                        P.m_body.rotation = _startRot;
                    }
                    P.m_lookYaw = _yaw;
                    P.m_lookPitch = _pitch;
                }
            });
        }

        private void Safe(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Log.Warning($"Self test clean-up ({what}) of {Test} failed: {e}");
            }
        }
    }

    // Network object gone for everybody. On a server another game may own it: me take it first.
    private static void DestroyObject(GameObject go)
    {
        if (go == null)
        {
            return;
        }
        var scene = ZNetScene.instance;
        if (scene == null)
        {
            Object.Destroy(go);
            return;
        }
        var nview = go.GetComponent<ZNetView>();
        if (nview != null && nview.IsValid() && !nview.IsOwner())
        {
            nview.ClaimOwnership();
        }
        scene.Destroy(go);
    }

    // No loot, no ragdoll (ragdoll drop items later): a kill leave nothing behind. Only this instance.
    private static void NoDrops(Character c)
    {
        var drop = c.GetComponent<CharacterDrop>();
        if (drop != null)
        {
            drop.SetDropsEnabled(false);
        }
        c.m_deathEffects = new EffectList();
    }

    // Fixture that must live through a vanilla hit (boar has 10 health).
    private static void Tough(Character c)
    {
        c.SetMaxHealth(2000f);
        c.SetHealth(2000f);
    }

    // ---------- the harpoon ----------

    // Me = Abyssal Harpoon as the game data have it: item, the attack that throw it, its projectile, its hook effect.
    private sealed class Harpoon
    {
        internal const string Prefab = "SpearChitin";

        internal ItemDrop.ItemData Item;
        internal Attack Throw;
        internal bool Secondary;
        internal GameObject ProjectilePrefab;
        internal Projectile ProjectileData;
        internal SE_Harpooned Effect;
        internal int Hash;

        internal float Velocity => Throw.m_projectileVel;

        internal float Gravity => ProjectileData.m_gravity;

        // XP and adrenaline of the hit are only taken away from a single-hit projectile (ProjectilePatches).
        internal bool SingleHit => ProjectileData.m_aoe <= 0f && !ProjectileData.m_onlyStopOnTerrain;

        internal static Harpoon Resolve(Checks c)
        {
            var prefab = ObjectDB.instance.GetItemPrefab(Prefab);
            var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (!c.Check(drop != null, $"item {Prefab} (Abyssal Harpoon) not found"))
            {
                return null;
            }
            var item = drop.m_itemData.Clone();
            item.m_dropPrefab = prefab;
            var shared = item.m_shared;
            Attack attack = null;
            var secondary = false;
            if (shared.m_attack != null && shared.m_attack.m_attackProjectile != null)
            {
                attack = shared.m_attack;
            }
            else if (shared.m_secondaryAttack != null && shared.m_secondaryAttack.m_attackProjectile != null)
            {
                attack = shared.m_secondaryAttack;
                secondary = true;
            }
            var projectile = attack != null ? attack.m_attackProjectile.GetComponent<Projectile>() : null;
            var effect = shared.m_attackStatusEffect as SE_Harpooned;
            if (!c.Check(attack != null && projectile != null, $"{Prefab} has no attack that throws a projectile")
                || !c.Check(effect != null, $"{Prefab}'s hit effect is not SE_Harpooned ({(shared.m_attackStatusEffect != null ? shared.m_attackStatusEffect.name : "none")})"))
            {
                return null;
            }
            c.Check(HarpoonEffect.IsHarpoon(effect.NameHash()), "the mod recognises the harpoon's hook effect");
            c.Check(shared.m_attackStatusEffectChance >= 1f, $"the hook effect is applied on every hit (chance {F(shared.m_attackStatusEffectChance)})");
            return new Harpoon
            {
                Item = item,
                Throw = attack,
                Secondary = secondary,
                ProjectilePrefab = attack.m_attackProjectile,
                ProjectileData = projectile,
                Effect = effect,
                Hash = effect.NameHash(),
            };
        }

        // Game data the docs call unverified: said once per run in harpoon.hook.
        internal void NoteFacts(Checks c)
        {
            var s = Item.m_shared;
            var p = ProjectileData;
            c.Note($"{Prefab}: throw is the {(Secondary ? "secondary" : "primary")} attack ('{Throw.m_attackAnimation}'), secondary attack "
                   + $"'{(s.m_secondaryAttack != null ? s.m_secondaryAttack.m_attackAnimation : "")}', damage {Item.GetDamage().GetTotalDamage():0.#}, push {F(s.m_attackForce)}, "
                   + $"backstab x{F(s.m_backstabBonus)}, blockable {s.m_blockable}, dodgeable {s.m_dodgeable}, throw gives adrenaline {F(Throw.m_attackUseAdrenaline)}, "
                   + $"hit noise {F(Throw.m_attackHitNoise)} m, speed {F(Throw.m_projectileVel)}, accuracy {F(Throw.m_projectileAccuracy)}..{F(Throw.m_projectileAccuracyMin)} (skill accuracy {Throw.m_skillAccuracy}), consumed {Throw.m_consumeItem}");
            c.Note($"projectile {ProjectilePrefab.name}: aoe {F(p.m_aoe)}, only stop on terrain {p.m_onlyStopOnTerrain}, hit friendly {p.m_hitFriendly}, no damage friendly {p.m_noDamageFriendly}, "
                   + $"adrenaline {F(p.m_adrenaline)}, gravity {F(p.m_gravity)}, ray radius {F(p.m_rayRadius)}, owner ray test {p.m_doOwnerRaytest}, attach {p.m_attachToRigidBody || p.m_attachToClosestBone}, "
                   + $"stay after dynamic hit {p.m_stayAfterHitDynamic}, respawn item {p.m_respawnItemOnHit}");
            c.Note($"effect {Effect.name}: max distance {F(Effect.m_maxDistance)}, break distance {F(Effect.m_breakDistance)}, stamina drain {F(Effect.m_staminaDrain)} every {F(Effect.m_staminaDrainInterval)} s, "
                   + $"pull force {F(Effect.m_pullForce)}, pull speed {F(Effect.m_pullSpeed)}, force power {F(Effect.m_forcePower)}, smooth distance {F(Effect.m_smoothDistance)}, ttl {F(Effect.m_ttl)}");
        }
    }

    // Hit data like Attack.FireProjectileBurst build it for a thrown or shot projectile of this weapon.
    private static HitData BuildHit(Player p, ItemDrop.ItemData weapon, Attack attack, ItemDrop.ItemData ammo = null)
    {
        var shared = weapon.m_shared;
        var factor = p.GetRandomSkillFactor(shared.m_skillType);
        var hit = new HitData();
        hit.m_toolTier = (short)shared.m_toolTier;
        hit.m_pushForce = shared.m_attackForce * attack.m_forceMultiplier;
        hit.m_backstabBonus = shared.m_backstabBonus;
        hit.m_staggerMultiplier = attack.m_staggerMultiplier;
        hit.m_damage.Add(weapon.GetDamage());
        hit.m_statusEffectHash = shared.m_attackStatusEffect != null ? shared.m_attackStatusEffect.NameHash() : 0;
        hit.m_skillLevel = p.GetSkillLevel(shared.m_skillType);
        hit.m_itemLevel = (short)weapon.m_quality;
        hit.m_itemWorldLevel = (byte)weapon.m_worldLevel;
        hit.m_blockable = shared.m_blockable;
        hit.m_dodgeable = shared.m_dodgeable;
        hit.m_skill = shared.m_skillType;
        hit.m_skillRaiseAmount = attack.m_raiseSkillAmount;
        hit.SetAttacker(p);
        hit.m_hitType = HitData.HitType.PlayerHit;
        hit.m_healthReturn = attack.m_attackHealthReturnHit;
        hit.m_eitrAdd = attack.m_attackEitrAdd;
        hit.m_variant = shared.m_hitVariant;
        if (ammo != null)
        {
            hit.m_damage.Add(ammo.GetDamage());
            hit.m_pushForce += ammo.m_shared.m_attackForce;
        }
        hit.m_pushForce *= factor;
        if (attack.m_damageMultiplier != 1f)
        {
            hit.m_damage.Modify(attack.m_damageMultiplier);
        }
        hit.m_damage.Modify(factor);
        p.GetSEMan().ModifyAttack(shared.m_skillType, ref hit);
        return hit;
    }

    // What one projectile hit did, read from the projectile.
    private sealed class Shot
    {
        internal bool Valid;       // Projectile.IsValidTarget (vanilla + this mod) said "hit it"
        internal bool Stopped;     // the projectile stopped on the target (false = it would fly on)
        internal float RaiseSkill; // what the projectile pay its owner after the hit
        internal float Adrenaline;
        internal float Push;       // push force the hit was built with
    }

    // The harpoon's own projectile hit the creature, without the flight: projectile made and set up like the throw
    // does, then Projectile.OnHit on the creature's collider (what its FixedUpdate ray would call). In single player
    // the whole chain run before OnHit return: IsValidTarget, Character.Damage, the owner's RPC_Damage (on a copy of
    // the hit, like over the network), the hook effect.
    private static Shot DirectHit(Rig rig, Harpoon h, Character target, bool probe = true)
    {
        var hit = BuildHit(rig.P, h.Item, h.Throw);
        return ProjectileHit(rig, h.ProjectilePrefab, rig.P, target, hit, h.Item, null, h.Velocity, h.Throw.m_attackHitNoise, probe);
    }

    private static Shot ProjectileHit(Rig rig, GameObject prefab, Character owner, Character target, HitData hit,
        ItemDrop.ItemData weapon, ItemDrop.ItemData ammo, float velocity, float noise, bool probe)
    {
        var shot = new Shot { Push = hit != null ? hit.m_pushForce : 0f };
        var center = target.GetCenterPoint();
        var dir = center - owner.GetCenterPoint();
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : rig.Dir;
        var go = Object.Instantiate(prefab, center - dir, Quaternion.LookRotation(dir));
        rig.Track(go);
        var projectile = go.GetComponent<Projectile>();
        projectile.enabled = false; // no flight: me call the hit
        projectile.Setup(owner, dir * velocity, noise, hit, weapon, ammo);
        if (probe)
        {
            shot.Valid = projectile.IsValidTarget(target);
        }
        Collider collider = target.m_collider;
        if (collider == null)
        {
            collider = target.GetComponentInChildren<Collider>();
        }
        projectile.OnHit(collider, center, false, -dir);
        shot.Stopped = projectile.m_didHit;
        shot.RaiseSkill = projectile.m_raiseSkillAmount;
        shot.Adrenaline = projectile.m_adrenaline;
        rig.Destroy(go);
        return shot;
    }

    // A harpoon projectile of `owner` that never fly: for asking Projectile.IsValidTarget only.
    private static Projectile Probe(Rig rig, Harpoon h, Character owner)
    {
        var go = Object.Instantiate(h.ProjectilePrefab, owner.GetCenterPoint() + Vector3.up * 2f, Quaternion.LookRotation(rig.Dir));
        rig.Track(go);
        var projectile = go.GetComponent<Projectile>();
        projectile.enabled = false;
        var hit = BuildHit(rig.P, h.Item, h.Throw);
        hit.SetAttacker(owner);
        projectile.Setup(owner, rig.Dir * h.Velocity, 0f, hit, h.Item, null);
        return projectile;
    }

    private static SE_Harpooned HookOn(Character c, Harpoon h)
    {
        return c != null && c.GetSEMan() != null ? c.GetSEMan().GetStatusEffect(h.Hash) as SE_Harpooned : null;
    }

    private static void Release(Character c, Harpoon h)
    {
        if (c != null && c.GetSEMan() != null)
        {
            c.GetSEMan().RemoveStatusEffect(h.Hash, true);
        }
    }

    private static Character TargetOf(Character c)
    {
        var ai = c != null ? c.GetBaseAI() : null;
        return ai != null ? ai.GetTargetCreature() : null;
    }

    // Creature state a hit would change, taken right before the hit.
    private sealed class Snap
    {
        internal float Health;
        internal HitData LastHit;
        internal float Push;
        internal float Stagger;
        internal bool Alerted;
        internal float SinceHurt;
        internal Vector3 Position;

        internal static Snap Take(Character c)
        {
            var ai = c.GetBaseAI();
            return new Snap
            {
                Health = c.GetHealth(),
                LastHit = c.m_lastHit,
                Push = c.m_pushForce.magnitude,
                Stagger = c.m_staggerDamage,
                Alerted = ai != null && ai.IsAlerted(),
                SinceHurt = ai != null ? ai.m_timeSinceHurt : 0f,
                Position = c.transform.position,
            };
        }
    }

    // Nothing of a hit reached the creature: health, last damaging hit, push, stagger, AI as before.
    private static bool Untouched(Character c, Snap before)
    {
        var ai = c.GetBaseAI();
        return Near(c.GetHealth(), before.Health) && ReferenceEquals(c.m_lastHit, before.LastHit)
               && c.m_pushForce.magnitude <= before.Push + 0.001f && Near(c.m_staggerDamage, before.Stagger)
               && (ai == null || (ai.IsAlerted() == before.Alerted && ai.m_timeSinceHurt >= before.SinceHurt));
    }

    private static string Describe(Character c, Snap before)
    {
        var ai = c.GetBaseAI();
        return $"health {F(before.Health)} -> {F(c.GetHealth())} of {F(c.GetMaxHealth())}, damaging hit {(ReferenceEquals(c.m_lastHit, before.LastHit) ? "none" : "taken")}, "
               + $"push {F(before.Push)} -> {F(c.m_pushForce.magnitude)}, stagger {F(before.Stagger)} -> {F(c.m_staggerDamage)}"
               + (ai != null ? $", alerted {before.Alerted} -> {ai.IsAlerted()}" : "");
    }

    // The hook as the mod promise it (right after the hit, same frame): hooked by the player with the line, the
    // "harpooned" message, and no harm: health, no damaging hit (so no damage number), no push, no stagger, not
    // alerted or hurt for its AI, the player not its target, no XP or adrenaline paid by the projectile.
    private static void CheckHarmlessHook(Checks c, string tag, Rig rig, Harpoon h, Character tame, Snap before, Shot shot,
        bool full = true, bool message = true)
    {
        var p = rig.P;
        c.Check(shot.Valid, $"{tag}: the harpoon is allowed to hit the tame (Projectile.IsValidTarget)");
        c.Check(shot.Stopped, $"{tag}: the harpoon stopped on the tame");
        var se = HookOn(tame, h);
        if (c.Check(se != null, $"{tag}: the tame carries the hook effect"))
        {
            c.Check(se.m_attacker == p && !se.m_broken, $"{tag}: the hook is held by the player and not broken");
            c.Check(se.m_line != null, $"{tag}: the line is linked to the player");
        }
        if (message)
        {
            var want = HarpoonedText(tame);
            c.Check(Center() == want, $"{tag}: message '{want}' on screen (got '{Center()}')");
        }
        c.Check(Near(tame.GetHealth(), before.Health) && (!full || Near(tame.GetHealth(), tame.GetMaxHealth())),
            $"{tag}: health unchanged{(full ? " and full" : "")} ({Describe(tame, before)})");
        c.Check(ReferenceEquals(tame.m_lastHit, before.LastHit), $"{tag}: no damage applied, so no damage number");
        c.Check(tame.m_pushForce.magnitude <= before.Push + 0.001f, $"{tag}: not pushed ({F(before.Push)} -> {F(tame.m_pushForce.magnitude)}, the hit was built with push {F(shot.Push)})");
        c.Check(Near(tame.m_staggerDamage, before.Stagger), $"{tag}: no stagger build-up");
        var ai = tame.GetBaseAI();
        if (ai != null && ai.enabled)
        {
            c.Check(ai.IsAlerted() == before.Alerted && ai.m_timeSinceHurt >= before.SinceHurt, $"{tag}: its AI is not alerted and does not count as hurt");
            c.Check(ai.GetTargetCreature() != p, $"{tag}: the player is not its target");
        }
        if (h.SingleHit)
        {
            c.Check(Near(shot.RaiseSkill, 0f) && Near(shot.Adrenaline, 0f),
                $"{tag}: the projectile pays no Spears skill ({F(shot.RaiseSkill)}) and no adrenaline ({F(shot.Adrenaline)})");
        }
    }

    // Some seconds later: still no harm, still not after the player.
    private static IEnumerator CheckCalm(Checks c, string tag, Rig rig, Character tame, Snap before, float seconds, Action each = null)
    {
        yield return Wait(seconds, each);
        if (!c.Check(tame != null, $"{tag}: the tame is gone {F(seconds)} s later"))
        {
            yield break;
        }
        var ai = tame.GetBaseAI();
        c.Check(Near(tame.GetHealth(), before.Health) && ReferenceEquals(tame.m_lastHit, before.LastHit),
            $"{tag}: {F(seconds)} s later still unharmed ({Describe(tame, before)})");
        c.Check(ai == null || (ai.GetTargetCreature() != rig.P && ai.IsAlerted() == before.Alerted),
            $"{tag}: {F(seconds)} s later it does not flee or turn on the player (alerted {(ai != null && ai.IsAlerted())}, target {(TargetOf(tame) != null ? TargetOf(tame).m_name : "none")})");
        c.Check(!tame.IsStaggering(), $"{tag}: not staggering");
    }

    // Vanilla harpoon hit on a creature that is not a tame: hooked, hurt, the hit counts (damage number), pushed
    // when the weapon push, its AI alerted or on the player, XP left on the projectile.
    private static void CheckVanillaHit(Checks c, string tag, Rig rig, Harpoon h, Character wild, Snap before, Shot shot)
    {
        c.Check(shot.Valid && shot.Stopped, $"{tag}: the harpoon hits it");
        c.Check(HookOn(wild, h) != null, $"{tag}: hooked (vanilla)");
        c.Check(wild.GetHealth() < before.Health - 0.01f, $"{tag}: damaged ({Describe(wild, before)})");
        c.Check(wild.m_lastHit != null && !ReferenceEquals(wild.m_lastHit, before.LastHit), $"{tag}: the damage was applied (damage number shown)");
        if (shot.Push > 0f)
        {
            c.Check(wild.m_pushForce.magnitude > before.Push + 0.001f, $"{tag}: pushed ({F(before.Push)} -> {F(wild.m_pushForce.magnitude)})");
        }
        else
        {
            c.Note($"{tag}: the harpoon hit carries no push force, push not checked");
        }
        var ai = wild.GetBaseAI();
        c.Check(ai == null || !ai.enabled || ai.IsAlerted() || ai.GetTargetCreature() == rig.P || ai.m_timeSinceHurt < before.SinceHurt,
            $"{tag}: its AI reacts to the hit (alerted, hurt or targeting the player)");
        c.Check(shot.RaiseSkill > 0f, $"{tag}: the projectile keeps its Spears skill gain ({F(shot.RaiseSkill)})");
    }

    // ---------- pull ----------

    private sealed class PullResult
    {
        internal float Moved;   // how far the creature came along the lane toward the player
        internal float Drop;    // stamina used
        internal float Seconds;
        internal bool Ended;    // hook gone before the time was over
        internal float Walked;  // how far the player went (Walk)
        internal float Stretch; // most the line was stretched beyond its length
        internal float Gap;     // at the end: how far the creature is ahead of the player along the lane

        internal void Reset()
        {
            Moved = 0f;
            Drop = 0f;
            Seconds = 0f;
            Ended = false;
            Walked = 0f;
            Stretch = 0f;
            Gap = 0f;
        }
    }

    // Speed of the walk away in the drag tests (player jog is a bit faster, walk a bit slower).
    private const float WalkSpeed = 4f;

    // Player walk away along the lane at `speed` m/s: set down one step further back each physics step (a steady
    // walk, no key in between), for `seconds`, until the lane end or until the hook is gone. Vanilla SE_Harpooned do
    // the pull and the drain from the two positions. This is the drag a player see. Standing far beyond the line
    // length is NOT: game data pull hard (pull speed 1000, code default 5), a boar held 2.5 m beyond the line fly
    // 20 m in 0.1 s past the player and the line hang loose before the first stamina tick (seen in the first run).
    private static IEnumerator Walk(Rig rig, Harpoon h, Character creature, float speed, float seconds, PullResult r)
    {
        var p = rig.P;
        var se = HookOn(creature, h);
        r.Reset();
        r.Ended = se == null;
        if (se == null)
        {
            yield break;
        }
        var start = rig.Along(creature.transform.position);
        var from = rig.Along(p.transform.position);
        var s = from;
        var stamina = p.GetStamina();
        var low = stamina;
        var t0 = Time.time;
        while (Time.time - t0 < seconds)
        {
            if (creature == null || !ReferenceEquals(HookOn(creature, h), se))
            {
                r.Ended = true;
                break;
            }
            var next = s - speed * Time.fixedDeltaTime;
            if (next < -rig.Back)
            {
                break; // lane end
            }
            s = next;
            rig.PlacePlayerAlong(s);
            yield return Fixed;
            low = Mathf.Min(low, p.GetStamina());
            if (creature != null)
            {
                r.Stretch = Mathf.Max(r.Stretch, Vector3.Distance(p.transform.position, creature.transform.position) - se.m_baseDistance);
            }
        }
        if (!r.Ended && (creature == null || !ReferenceEquals(HookOn(creature, h), se)))
        {
            r.Ended = true; // hook went in the last step
        }
        r.Seconds = Time.time - t0;
        r.Walked = from - s;
        r.Moved = creature != null ? start - rig.Along(creature.transform.position) : 0f;
        r.Gap = creature != null ? rig.Along(creature.transform.position) - s : 0f;
        r.Drop = stamina - low;
    }

    // Player kept `extra` m beyond the line length (flat distance; creature beside the lane axis counted), set down
    // again each physics step as the creature come, for `seconds`, until the lane end or until the hook is gone.
    // Same stretch = same pull strength: for comparing two creatures. Keep `extra` small (see Walk).
    private static IEnumerator Hold(Rig rig, Harpoon h, Character creature, float extra, float seconds, PullResult r)
    {
        var p = rig.P;
        var se = HookOn(creature, h);
        r.Reset();
        r.Ended = se == null;
        if (se == null)
        {
            yield break;
        }
        var start = rig.Along(creature.transform.position);
        var stamina = p.GetStamina();
        var low = stamina;
        var t0 = Time.time;
        while (Time.time - t0 < seconds)
        {
            if (creature == null || !ReferenceEquals(HookOn(creature, h), se))
            {
                r.Ended = true;
                break;
            }
            var rel = creature.transform.position - rig.Origin;
            var along = Vector3.Dot(rel, rig.Dir);
            var side = Vector3.Dot(rel, rig.Right);
            var want = se.m_baseDistance + extra;
            var at = along - Mathf.Sqrt(Mathf.Max(1f, want * want - side * side));
            if (at < -rig.Back)
            {
                at = -rig.Back;
                if (along - at <= se.m_baseDistance + 0.1f)
                {
                    break; // lane end and line slack: nothing more to pull
                }
            }
            rig.PlacePlayerAlong(at);
            yield return Fixed;
            low = Mathf.Min(low, p.GetStamina());
            if (creature != null)
            {
                r.Stretch = Mathf.Max(r.Stretch, Vector3.Distance(p.transform.position, creature.transform.position) - se.m_baseDistance);
            }
        }
        if (!r.Ended && (creature == null || !ReferenceEquals(HookOn(creature, h), se)))
        {
            r.Ended = true;
        }
        r.Seconds = Time.time - t0;
        r.Moved = creature != null ? start - rig.Along(creature.transform.position) : 0f;
        r.Drop = stamina - low;
    }

    // Hold block until the hook is gone (vanilla: only after the first 2 s of the hook).
    private static IEnumerator BlockRelease(Rig rig, Harpoon h, Character creature, float timeout, Box box)
    {
        rig.TakeControls();
        var t0 = Time.time;
        box.Ok = false;
        var blocked = false;
        while (Time.time - t0 < timeout)
        {
            rig.Drive(true);
            blocked |= rig.P.IsBlocking();
            yield return null;
            if (HookOn(creature, h) == null)
            {
                box.Ok = blocked;
                break;
            }
        }
        box.Seconds = Time.time - t0;
        rig.Drive(false);
    }

    // ---------- flight and real throw ----------

    private sealed class Flight
    {
        internal bool Stopped;
        internal float Seconds;
        internal float RaiseSkill;
        internal float Adrenaline;
        internal float Push;

        // As a Shot for CheckHarmlessHook: a flying projectile that stopped on a creature was allowed to hit it.
        internal Shot AsShot(bool hooked) => new Shot { Valid = hooked, Stopped = Stopped, RaiseSkill = RaiseSkill, Adrenaline = Adrenaline, Push = Push };
    }

    // A harpoon projectile set up like the throw does, launched from `launch` at `aim` (drop from gravity added to
    // the aim). Vanilla Projectile.FixedUpdate fly it and ray-test what is in the way. `pin` run every physics step.
    private static IEnumerator Fly(Rig rig, Harpoon h, Vector3 launch, Vector3 aim, Action pin, Flight f)
    {
        var p = rig.P;
        var to = aim - launch;
        var time = h.Velocity > 0.1f ? to.magnitude / h.Velocity : 0f;
        to.y += 0.5f * h.Gravity * time * time;
        var dir = to.normalized;
        var hit = BuildHit(p, h.Item, h.Throw);
        f.Push = hit.m_pushForce;
        var go = Object.Instantiate(h.ProjectilePrefab, launch, Quaternion.LookRotation(dir));
        rig.Track(go);
        var projectile = go.GetComponent<Projectile>();
        projectile.Setup(p, dir * h.Velocity, h.Throw.m_attackHitNoise, hit, h.Item, null);
        var t0 = Time.time;
        while (Time.time - t0 < 3f)
        {
            pin?.Invoke();
            yield return Fixed;
            if (go == null || projectile.m_didHit)
            {
                break;
            }
        }
        f.Seconds = Time.time - t0;
        f.Stopped = projectile.m_didHit;
        f.RaiseSkill = projectile.m_raiseSkillAmount;
        f.Adrenaline = projectile.m_adrenaline;
        pin?.Invoke();
        rig.Destroy(go);
    }

    // Height above the player's feet where the throw let the projectile go (as Attack.GetProjectileSpawnPoint: the
    // attack's origin joint, or the feet, plus the attack height). Joint height is read in the idle pose: a guess
    // until a first throw showed the real one.
    private static float LaunchHeight(Player p, Attack attack)
    {
        var origin = p.transform;
        if (!string.IsNullOrEmpty(attack.m_attackOriginJoint) && p.GetVisual() != null)
        {
            var joint = Utils.FindChild(p.GetVisual().transform, attack.m_attackOriginJoint);
            if (joint != null)
            {
                origin = joint;
            }
        }
        return origin.position.y + attack.m_attackHeight - p.transform.position.y;
    }

    // Look so that a projectile leaving the weapon's launch point (`height` above the feet) reach `target` (gravity
    // drop added).
    private static void Aim(Player p, Harpoon h, Vector3 target, float height)
    {
        var flat = target - p.transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.01f)
        {
            return;
        }
        flat.Normalize();
        var launch = p.transform.position + Vector3.up * height + flat * h.Throw.m_attackRange;
        var to = target - launch;
        var time = h.Velocity > 0.1f ? to.magnitude / h.Velocity : 0f;
        to.y += 0.5f * h.Gravity * time * time;
        var dir = to.normalized;
        p.m_lookYaw = Quaternion.LookRotation(flat);
        p.m_lookPitch = -Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg; // positive pitch = down in vanilla
        p.transform.rotation = p.m_lookYaw;
        if (p.m_body != null)
        {
            p.m_body.rotation = p.m_lookYaw;
        }
        if (p.m_eye != null)
        {
            p.m_eye.rotation = p.m_lookYaw * Quaternion.Euler(p.m_lookPitch, 0f, 0f);
        }
    }

    private sealed class ThrowResult
    {
        internal int Attempts;
        internal bool Started;
        internal bool Launched;
        internal bool Hooked;
        internal float RaiseSkill = -1f;
        internal float Adrenaline = -1f;
        internal float Push;
        internal string Detail = "";
    }

    private const int MaxThrows = 5;

    // The real thing: equipped harpoon, Humanoid.StartAttack (what the attack key call), the throw animation's
    // trigger, Attack.FireProjectileBurst, the flight. Up to 5 throws: the throw has a random spread, and the first
    // one tell the real launch height for the aim of the next. The target is held on its spot so a wandering tame is
    // not a miss.
    private static IEnumerator RealThrow(Rig rig, Harpoon h, ItemDrop.ItemData weapon, Character target, Vector3 spot, ThrowResult r)
    {
        var p = rig.P;
        var height = LaunchHeight(p, h.Throw);
        var notes = "";
        Action hold = () =>
        {
            if (target != null)
            {
                Place(target, spot, -rig.Dir);
                Aim(p, h, target.GetCenterPoint(), height);
            }
        };
        for (var attempt = 1; attempt <= MaxThrows && !r.Hooked; attempt++)
        {
            r.Attempts = attempt;
            var t0 = Time.time;
            while (p.InAttack() && Time.time - t0 < 3f)
            {
                hold();
                yield return null;
            }
            yield return Wait(0.3f, hold);
            weapon.m_lastProjectile = null;
            r.Started = p.StartAttack(null, h.Secondary);
            if (!r.Started)
            {
                notes += $" throw {attempt}: the attack did not start;";
                yield return Wait(0.7f, hold);
                continue;
            }
            t0 = Time.time;
            while (weapon.m_lastProjectile == null && Time.time - t0 < 4f)
            {
                hold();
                yield return null;
            }
            var go = weapon.m_lastProjectile;
            if (go == null)
            {
                notes += $" throw {attempt}: no projectile within 4 s of the attack start;";
                continue;
            }
            r.Launched = true;
            rig.Track(go);
            var projectile = go.GetComponent<Projectile>();
            var aimed = height;
            if (projectile != null)
            {
                r.Push = projectile.m_attackForce;
                height = projectile.m_startPoint.y - p.transform.position.y; // where the game really let it go
            }
            t0 = Time.time;
            while (Time.time - t0 < 4f)
            {
                hold();
                yield return Fixed;
                if (go == null || (projectile != null && projectile.m_didHit) || HookOn(target, h) != null)
                {
                    break;
                }
            }
            r.Hooked = HookOn(target, h) != null;
            if (projectile != null)
            {
                r.RaiseSkill = projectile.m_raiseSkillAmount;
                r.Adrenaline = projectile.m_adrenaline;
            }
            notes += $" throw {attempt}: aimed for a launch {F(aimed)} m up, launched {F(height)} m up, {(r.Hooked ? "hooked" : "missed")} after {F(Time.time - t0)} s;";
            if (!r.Hooked)
            {
                rig.Destroy(go);
            }
        }
        r.Detail = notes.Trim();
    }
}
#endif
